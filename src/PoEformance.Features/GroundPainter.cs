using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// Paints a laid tile's ground block from the area's MaterialsList: one ground all round in its material, two grounds split
/// by the template's mask - or says why a piece stays plain.
/// </summary>
/// <remarks>
/// WHAT IS MEASURED AND WHAT IS NOT, from Port's captures (v0.1.195 to v0.1.200):
///
///     the materials       the master's MaterialsList keys a group by the ground type's name (the .gt's first line); a
///                         tile's corners name the grounds. MEASURED: Port's harbour is cobblestone where its corners'
///                         IslandTownUnwalkableFill names G_IHLT_CobblestoneDirt01
///     two grounds         the engine weighs them by its mask's green read at the ground block's own coordinates
///                         (ground.ffx, GetUserMask; GroundGenerateWeights_1a1b: lerp(first, second, green)). MEASURED:
///                         read so at its vertices, green follows height on the harbour (r 0.78, the sea floor all 1),
///                         the ocean-edge walls (r 1.00) and the coasts - the block's coordinates were made to split it
///     which is first      DECIDED FROM ONE AREA: in all three of Port's two-ground families the ground at green nought
///                         is the one the MaterialsList lists first (IslandTownUnwalkableFill, then IslandOcean, then
///                         TropicalFill). Neither the names' order nor the terrain's own list fits all three
///     the coordinates     the engine's CreateProjectedCoords: world.xy times ground_scalemove_uv.xy, a pass uniform no
///                         file holds; every environment of every capture carries "ground_scale": 0.004 in the block
///                         beside dust_color, the uniform's neighbour - taken as it, likely and not measured
///     which choice        a group of several materials is drawn by weight per tile in the game; the heaviest is taken
///
/// MIXED PER PIXEL, as the engine mixes them (v0.1.244): one shape wearing both grounds, the mask's green read at the block's
/// own coordinates and the two materials run and mixed by it (ShadeProgram.Grounds, MonsterModels.Paints.Grounds) - the
/// mesh keeps the block's coordinates and the program works the materials' out of the place. Before, a triangle wore the
/// second ground where its three vertices' green averaged a half or more, and Port's harbour showed the ocean's black in
/// patches with straight, toothed edges on its beach (said from the live client, v0.1.242's probe on one: blackc.mat under
/// a thin sea and a thin beach film). That split stays where the two cannot be run as one program, and the line under the
/// picture says why. NOT DRAWN, DECIDED from the live client (2026-10-12): the engine's height blending
/// (GROUND_HEIGHT_BLENDING, which Port's ground graph PBRGroundBN.fxgraph turns on - the weights sharpened by the materials'
/// height maps, blackc.mat's height nought). Three or four grounds (the engine's red atlas, placed by transforms no file
/// holds) stay plain.
/// </remarks>
internal sealed class GroundPainter
{
    /// <summary>The ground materials' tiling: world units to texture coordinates - see the remarks.</summary>
    public const float GroundScale = 0.004f;

    /// <summary>Green at or over this, averaged over a triangle's vertices, is the second ground's.</summary>
    private const float Half = 0.5f;

    private readonly Func<string, byte[]?> _read;
    private readonly GroundMaterials _list;
    private readonly MonsterModels.Paints _paints;
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GamePicture?> _masks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Plan> _plans = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _plain = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _split = new(StringComparer.Ordinal);
    private int _one;
    private int _two;
    private int _mixed;

    private GroundPainter(Func<string, byte[]?> read, GroundMaterials list, string from, MonsterModels.Paints paints)
    {
        _read = read;
        _list = list;
        _paints = paints;
        From = from;
    }

    /// <summary>The MaterialsList painted from.</summary>
    public string From { get; }

    /// <summary>
    /// The painter for a master's MaterialsList, or null and why there is none.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="master">The area's master (<c>.tsi</c>).</param>
    /// <param name="paints">The room's paint cache, so the ground's textures are read once with everything else's.</param>
    public static (GroundPainter? Painter, string Why) For(Func<string, byte[]?> read, string master, MonsterModels.Paints paints)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(paints);
        if (master.Length == 0)
        {
            return (null, "no master to name a MaterialsList");
        }

        string named = MasterFile.Read(read(Clean(master))).Values.GetValueOrDefault("MaterialsList", string.Empty);
        foreach (string candidate in MasterFile.Candidates(Clean(master), named))
        {
            GroundMaterials list = GroundMaterials.Read(read(candidate));
            if (list.Groups.Count > 0)
            {
                return (new GroundPainter(read, list, candidate, paints), string.Empty);
            }
        }

        return (null, named.Length == 0 ? $"the master names no MaterialsList: {master}" : $"the MaterialsList did not read: {named}");
    }

    /// <summary>
    /// The master beside a room - <c>master.tsi</c> in the folder above its <c>Rooms/</c> - or empty where there is none.
    /// </summary>
    /// <remarks>
    /// WHERE 456 OF RePoE's 458 MAP GRAPHS KEEP THEIRS (see AreaRoomSet), looked for by name and read to be sure; a room
    /// whose master is elsewhere or named otherwise is drawn with its ground plain, and the line under it says so.
    /// </remarks>
    public static string MasterBeside(Func<string, byte[]?> read, string room)
    {
        ArgumentNullException.ThrowIfNull(read);
        string path = Clean(room);
        int rooms = path.IndexOf("/Rooms/", StringComparison.OrdinalIgnoreCase);
        if (rooms < 0)
        {
            return string.Empty;
        }

        string master = path[..(rooms + 1)] + "master.tsi";
        return read(master) is { Length: > 0 } ? master : string.Empty;
    }

    /// <summary>
    /// The tile's ground painted for one piece, or null where it stays plain - counted either way for <see cref="Said"/>.
    /// </summary>
    /// <param name="tile">The tile's <c>.tdt</c>.</param>
    /// <param name="ground">Its ground block, as the tile's walk joined it - its coordinates the mask's.</param>
    /// <param name="place">Where the piece is laid, which the materials' coordinates are worked out from.</param>
    /// <param name="source">What the piece is called, for the probe.</param>
    public MonsterModel? Paint(string tile, SkinnedMesh ground, Matrix4x4 place, string source)
    {
        ArgumentNullException.ThrowIfNull(ground);
        if (!_plans.TryGetValue(tile, out Plan? plan))
        {
            plan = Planned(tile, ground);
            _plans[tile] = plan;
        }

        if (plan.Why.Length > 0)
        {
            _plain[plan.Why] = _plain.GetValueOrDefault(plan.Why) + 1;
            return null;
        }

        // THE ENGINE'S COORDINATES, from where the piece lies - so a pattern runs on across the pieces as the game's does. Two
        // grounds mixed keep the block's own, for the mask, and their program works these out of the place (ShadeProgram.Grounds).
        Vector2[] coordinates;
        if (plan.Mixed)
        {
            coordinates = ground.Coordinates;
        }
        else
        {
            coordinates = new Vector2[ground.Positions.Length];
            for (var vertex = 0; vertex < coordinates.Length; vertex++)
            {
                Vector3 laid = Vector3.Transform(ground.Positions[vertex], place);
                coordinates[vertex] = new Vector2(laid.X, laid.Y) * GroundScale;
            }
        }

        SkinnedMesh painted = SkinnedMesh.Of(
            ground.Positions, ground.Normals, plan.Indices, ground.Least, ground.Most, coordinates, plan.Shapes, ground.Facts);
        _one += plan.Shapes.Count == 1 && !plan.Mixed ? 1 : 0;
        _mixed += plan.Mixed ? 1 : 0;
        if (plan.Shapes.Count == 2)
        {
            _two++;
            _split[plan.Split] = _split.GetValueOrDefault(plan.Split) + 1;
        }

        return MonsterModels.Worn(_read, painted, plan.Named, paintTheRest: false, source, "a tile's ground is static", _paints);
    }

    /// <summary>What was painted and what stayed plain, and on what it rests - for the line under the picture.</summary>
    public string Said()
    {
        string plain = _plain.Count == 0 ? "none"
            : string.Join(", ", _plain.OrderByDescending(one => one.Value).Select(one => string.Create(CultureInfo.InvariantCulture, $"{one.Value} {one.Key}")));
        string split = _two == 0 ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $", {_two} of two split by triangle where the green averages a half - mixing them could not be: ")
                + string.Join(", ", _split.OrderByDescending(one => one.Value).Select(one => string.Create(CultureInfo.InvariantCulture, $"{one.Value} {one.Key}")));
        return string.Create(CultureInfo.InvariantCulture,
            $"ground: painted from {From} - {_one} pieces of one ground, {_mixed} of two mixed per pixel by the template mask's green at the block's own coordinates as the engine mixes them (GroundGenerateWeights_1a1b; the MaterialsList's first ground at green nought, as Port's three families agree - one area; its height blending not drawn, DECIDED){split}, plain: {plain}; the materials' coordinates the world times {GroundScale} (the environments' ground_scale, likely and not measured), a group's heaviest choice where the game draws by weight");
    }

    /// <summary>How a tile's ground is painted, worked out once whatever number of pieces lay it.</summary>
    private sealed record Plan(int[] Indices, IReadOnlyList<MeshShape> Shapes, IReadOnlyList<(string Shape, string Material)> Named, string Why)
    {
        public static Plan Plain(string why) => new([], [], [], why);

        /// <summary>Whether the two grounds are mixed per pixel, the block keeping its own coordinates - see the class's remarks.</summary>
        public bool Mixed { get; init; }

        /// <summary>Why two grounds are split by triangle instead, where they are.</summary>
        public string Split { get; init; } = string.Empty;
    }

    private Plan Planned(string tile, SkinnedMesh ground)
    {
        (TileDefinition definition, string why) = TileModels.Defined(_read, tile);
        if (!definition.Ready)
        {
            return Plan.Plain("whose definition did not read: " + why);
        }

        // THE GROUNDS BY NAME, in the MaterialsList's order - the order the engine's weights follow (see the remarks).
        var grounds = new List<(int Order, GroundGroup Group)>();
        foreach (string corner in definition.Grounds)
        {
            if (corner.Length == 0)
            {
                continue;
            }

            string name = NameOf(corner);
            int order = -1;
            for (var at = 0; at < _list.Groups.Count; at++)
            {
                if (string.Equals(_list.Groups[at].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    order = at;
                    break;
                }
            }

            if (order < 0)
            {
                return Plan.Plain("with a ground the MaterialsList does not list");
            }

            if (!grounds.Exists(one => one.Order == order))
            {
                grounds.Add((order, _list.Groups[order]));
            }
        }

        grounds.Sort((one, other) => one.Order.CompareTo(other.Order));
        if (grounds.Count == 0)
        {
            return Plan.Plain("whose corners name no ground");
        }

        if (grounds.Count > 2)
        {
            return Plan.Plain("of three or four grounds (the engine's atlas, placed by transforms no file holds)");
        }

        string first = Heaviest(grounds[0].Group);
        if (grounds.Count == 1)
        {
            return new Plan(ground.Indices, [new MeshShape("0", 0, ground.Indices.Length)], [("0", first)], string.Empty);
        }

        string second = Heaviest(grounds[1].Group);
        string mask = definition.Templates.Count > 0 ? TileTemplate.Read(_read(Clean(definition.Templates[0]))).GroundMask : string.Empty;
        if (mask.Length == 0 || MaskOf(mask) is not { } picture)
        {
            return Plan.Plain("of two grounds whose template names no mask that reads");
        }

        if (!ground.Coordinated)
        {
            return Plan.Plain("of two grounds whose block carries no coordinates into its mask");
        }

        // MIXED PER PIXEL as the engine mixes them, where the two grounds' programs run as one - see the remarks.
        if (_paints.Grounds(_read, first, second, Clean(mask), GroundScale, out string unmixed) is { } mixed)
        {
            return new Plan(ground.Indices, [new MeshShape("0", 0, ground.Indices.Length)], [("0", mixed)], string.Empty) { Mixed = true };
        }

        // ELSE EACH TRIANGLE TO THE GROUND ITS VERTICES' GREEN AVERAGES TO, the first's then the second's.
        var green = new float[ground.Positions.Length];
        for (var vertex = 0; vertex < green.Length; vertex++)
        {
            Vector2 uv = ground.Coordinates[vertex];
            green[vertex] = Sample(picture, 1, uv.X, uv.Y);
        }

        int[] indices = ground.Indices;
        var firsts = new List<int>(indices.Length);
        var seconds = new List<int>(indices.Length / 2);
        for (var at = 0; at + 2 < indices.Length; at += 3)
        {
            float mean = (green[indices[at]] + green[indices[at + 1]] + green[indices[at + 2]]) / 3f;
            (mean >= Half ? seconds : firsts).AddRange([indices[at], indices[at + 1], indices[at + 2]]);
        }

        int[] ordered = [.. firsts, .. seconds];
        return new Plan(
            ordered,
            [new MeshShape("0", 0, firsts.Count), new MeshShape("1", firsts.Count, seconds.Count)],
            [("0", first), ("1", second)],
            string.Empty) { Split = unmixed };
    }

    /// <summary>A group's heaviest choice - its only one where it has one.</summary>
    private static string Heaviest(GroundGroup group)
    {
        int best = 0;
        for (var at = 1; at < group.Choices.Count && at < group.Weights.Count; at++)
        {
            best = group.Weights[at] > group.Weights[best] ? at : best;
        }

        return group.Choices.Count > 0 ? group.Choices[best].Material : string.Empty;
    }

    private string NameOf(string ground)
    {
        if (!_names.TryGetValue(ground, out string? name))
        {
            name = GroundType.Name(_read(Clean(ground)));
            _names[ground] = name;
        }

        return name;
    }

    private GamePicture? MaskOf(string path)
    {
        if (!_masks.TryGetValue(path, out GamePicture? picture))
        {
            picture = GameArt.Decode(GameArt.ReadRaw(_read, Clean(path))) is { Ready: true } decoded ? decoded : null;
            _masks[path] = picture;
        }

        return picture;
    }

    /// <summary>One channel at a texture coordinate, nought to one, as a linear sampler that wraps reads it.</summary>
    internal static float Sample(GamePicture picture, int channel, float u, float v)
    {
        float x = (u * picture.Width) - 0.5f, y = (v * picture.Height) - 0.5f;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        float At(int column, int row)
        {
            int wrappedX = ((column % picture.Width) + picture.Width) % picture.Width;
            int wrappedY = ((row % picture.Height) + picture.Height) % picture.Height;
            return picture.Rgba[(((wrappedY * picture.Width) + wrappedX) * 4) + channel] / 255f;
        }

        float top = (At(x0, y0) * (1 - fx)) + (At(x0 + 1, y0) * fx);
        float bottom = (At(x0, y0 + 1) * (1 - fx)) + (At(x0 + 1, y0 + 1) * fx);
        return (top * (1 - fy)) + (bottom * fy);
    }

    private static string Clean(string path) => path.Replace('\\', '/').Trim();
}

/// <summary>
/// The name a tile's ground of two grounds wears as its material when the two are mixed per pixel - see GroundPainter and
/// MonsterModels.Paints.Grounds - and how a picture's lines say it.
/// </summary>
/// <remarks>
/// A NAME, NOT A FILE: every list a shape's material travels through - the pile, the join, the stage, the probe, the capture - carries
/// it as it carries a material's path, and the paint cache that made the program answers for it.
/// </remarks>
internal static class GroundBlend
{
    private const string With = " mixed with ";

    private const string By = " by the green of ";

    /// <summary>The name for two grounds mixed by a mask.</summary>
    public static string Name(string first, string second, string mask) => first + With + second + By + mask;

    /// <summary>The two grounds and the mask a name says, or false where it is a material's own path.</summary>
    public static bool Parts(string material, out string first, out string second, out string mask)
    {
        first = second = mask = string.Empty;
        int with = material.IndexOf(With, StringComparison.Ordinal);
        int by = material.LastIndexOf(By, StringComparison.Ordinal);
        if (with <= 0 || by <= with)
        {
            return false;
        }

        first = material[..with];
        second = material[(with + With.Length)..by];
        mask = material[(by + By.Length)..];
        return true;
    }

    /// <summary>The name as a picture's lines say it - the three files' own names.</summary>
    public static string Short(string material)
        => Parts(material, out string first, out string second, out string mask)
            ? $"{Tail(first)} + {Tail(second)} by {Tail(mask)}'s green"
            : Tail(material);

    private static string Tail(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
}
