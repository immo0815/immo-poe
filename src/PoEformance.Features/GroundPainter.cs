using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// Paints a laid tile's ground block from the area's MaterialsList, sub-tile by sub-tile: one ground in its material, two grounds
/// mixed by the template's mask - or says why a sub-tile stays plain.
/// </summary>
/// <remarks>
/// WHAT IS MEASURED AND WHAT IS NOT, from Port's captures (v0.1.195 to v0.1.200):
///
///     the materials       the master's MaterialsList keys a group by the ground type's name (the .gt's first line); a
///                         tile's corners name the grounds. MEASURED: Port's harbour is cobblestone where its corners'
///                         IslandTownUnwalkableFill names G_IHLT_CobblestoneDirt01
///     whose corners       each SUB-TILE's own, from the definition's ground grid (TileDefinition.GroundGrid, v0.1.248) -
///                         MEASURED: Port's harbour names two grounds at its corners and four in its grid; the tile's four
///                         corners stand for every sub-tile only where a definition carries no grid
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
    private int _gridded;
    private int _unnamed;

    /// <summary>The MaterialsList's group with no name, by its place in the list, or -1 - see SetOf.</summary>
    private readonly int _unnamedGroup;
    private int _cornered;

    private GroundPainter(Func<string, byte[]?> read, GroundMaterials list, string from, MonsterModels.Paints paints)
    {
        _read = read;
        _list = list;
        _paints = paints;
        From = from;
        _unnamedGroup = -1;
        for (var at = 0; at < list.Groups.Count && _unnamedGroup < 0; at++)
        {
            _unnamedGroup = list.Groups[at].Name.Length == 0 ? at : -1;
        }
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
    /// The tile's ground painted for one piece with its four corners standing for every sub-tile - see the other overload.
    /// </summary>
    public MonsterModel? Paint(string tile, SkinnedMesh ground, Matrix4x4 place, string source) => Paint(tile, ground, [], place, source);

    /// <summary>
    /// The tile's ground painted for one piece, or null where all of it stays plain - counted sub-tile by sub-tile for <see cref="Said"/>.
    /// </summary>
    /// <param name="tile">The tile's <c>.tdt</c>.</param>
    /// <param name="ground">Its ground block, as the tile's walk joined it - its coordinates the mask's.</param>
    /// <param name="pieces">Which sub-tile each run of the block's triangles is, in the block's order - or none, and the tile's four corners stand for all.</param>
    /// <param name="place">Where the piece is laid, which the materials' coordinates are worked out from.</param>
    /// <param name="source">What the piece is called, for the probe.</param>
    public MonsterModel? Paint(string tile, SkinnedMesh ground, IReadOnlyList<GroundPiece> pieces, Matrix4x4 place, string source)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(pieces);
        if (!_plans.TryGetValue(tile, out Plan? plan))
        {
            plan = Planned(tile, ground, pieces);
            _plans[tile] = plan;
        }

        _one += plan.One;
        _mixed += plan.Mixed;
        _two += plan.Split;
        _gridded += plan.Gridded ? 1 : 0;
        _unnamed += plan.Unnamed;
        _cornered += plan.Gridded ? 0 : 1;
        foreach ((string why, int count) in plan.Plain)
        {
            _plain[why] = _plain.GetValueOrDefault(why) + count;
        }

        foreach ((string why, int count) in plan.Splits)
        {
            _split[why] = _split.GetValueOrDefault(why) + count;
        }

        if (plan.Named.Count == 0)
        {
            return null;
        }

        // THE ENGINE'S COORDINATES, from where the piece lies - so a pattern runs on across the pieces as the game's does. A sub-tile
        // of two grounds mixed keeps the block's own, for the mask, and its program works these out of the place (ShadeProgram.Grounds).
        // A sub-tile's vertices are its own - the block joins the sub-tiles' meshes whole - so each takes its sub-tile's kind.
        var coordinates = new Vector2[ground.Positions.Length];
        for (var vertex = 0; vertex < coordinates.Length; vertex++)
        {
            if (plan.Own[vertex])
            {
                coordinates[vertex] = ground.Coordinates[vertex];
                continue;
            }

            Vector3 laid = Vector3.Transform(ground.Positions[vertex], place);
            coordinates[vertex] = new Vector2(laid.X, laid.Y) * GroundScale;
        }

        SkinnedMesh painted = SkinnedMesh.Of(
            ground.Positions, ground.Normals, plan.Indices, ground.Least, ground.Most, coordinates, plan.Shapes, ground.Facts);
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
            $"ground: painted from {From}, each sub-tile from its own corners in its definition's ground grid ({_gridded} pieces; {_cornered} from the tile's four corners, their definition carrying no grid; {_unnamed} sub-tiles with a corner the grid leaves unnamed, taken as the MaterialsList's group with no name - the game holds its null slot there, and that it draws this group is DECIDED) - {_one} sub-tiles of one ground, {_mixed} of two mixed per pixel by the template mask's green at the block's own coordinates as the engine mixes them (GroundGenerateWeights_1a1b; the MaterialsList's first ground at green nought, as Port's three families agree - one area; its height blending not drawn, DECIDED){split}, plain: {plain}; the materials' coordinates the world times {GroundScale} (the environments' ground_scale, likely and not measured), a group's heaviest choice where the game draws by weight");
    }

    /// <summary>How a tile's ground is painted, worked out once whatever number of pieces lay it - and what it comes to, sub-tile by sub-tile.</summary>
    private sealed record Plan(int[] Indices, IReadOnlyList<MeshShape> Shapes, IReadOnlyList<(string Shape, string Material)> Named, bool[] Own)
    {
        /// <summary>Sub-tiles of one ground.</summary>
        public int One { get; init; }

        /// <summary>Sub-tiles of two grounds mixed per pixel - see the class's remarks.</summary>
        public int Mixed { get; init; }

        /// <summary>Sub-tiles of two grounds split by triangle, the two being unable to run as one program.</summary>
        public int Split { get; init; }

        /// <summary>Whether the sub-tiles took their own corners from the definition's ground grid.</summary>
        public bool Gridded { get; init; }

        /// <summary>Sub-tiles with a corner the grid leaves unnamed, which takes the MaterialsList's group with no name.</summary>
        public int Unnamed { get; init; }

        /// <summary>Sub-tiles left plain, by why.</summary>
        public IReadOnlyDictionary<string, int> Plain { get; init; } = new Dictionary<string, int>();

        /// <summary>Sub-tiles split by triangle, by why the two could not be mixed.</summary>
        public IReadOnlyDictionary<string, int> Splits { get; init; } = new Dictionary<string, int>();

        public static Plan Plained(string why, int subTiles) => new([], [], [], []) { Plain = new Dictionary<string, int> { [why] = subTiles } };
    }

    /// <summary>The triangles of the block whose sub-tiles name one set of grounds.</summary>
    private sealed class Run(List<(int Order, GroundGroup Group)> grounds, string why)
    {
        public List<(int Order, GroundGroup Group)> Grounds { get; } = grounds;

        public string Why { get; } = why;

        public List<int> Triangles { get; } = [];

        public int SubTiles { get; set; }
    }

    /// <summary>
    /// A tile's ground worked out sub-tile by sub-tile: each sub-tile's grounds from its own corners in the definition's ground grid
    /// (TileDefinition.GroundGrid) - the tile's four corners where it has none - and the sub-tiles naming one set painted alike.
    /// </summary>
    /// <remarks>
    /// THE GRID, NOT THE TILE'S CORNERS (v0.1.248). Port's harbour names two grounds at its corners and four in its grid: its land
    /// stands on PortTownTrim and the tropical fill, and its ground shapes there carry coordinates laid across the mask a third a
    /// sub-tile - read through newmask.dds, an atlas of transitions, they came out as a checkerboard of black and cobblestone
    /// (v0.1.247's capture, ground-shapes-12-Port_Boss_01.png). A sub-tile whose corners name one ground is drawn in it, its mask
    /// unread, as the engine's GroundGenerateWeights_1a draws one; two are mixed as before, in the list's order.
    /// </remarks>
    private Plan Planned(string tile, SkinnedMesh ground, IReadOnlyList<GroundPiece> pieces)
    {
        (TileDefinition definition, string why) = TileModels.Defined(_read, tile);
        if (!definition.Ready)
        {
            return Plan.Plained("whose definition did not read: " + why, Math.Max(1, pieces.Count));
        }

        // EACH SUB-TILE'S OWN CORNERS where the block's pieces add up to it and the definition has its grid; else one run, the tile's corners.
        int triangles = ground.Indices.Length / 3;
        bool gridded = definition.GroundGrid.Count > 0 && pieces.Count > 0 && pieces.Sum(one => one.Triangles) == triangles;
        List<(int Triangles, IReadOnlyList<string> Corners)> subTiles = gridded
            ? [.. pieces.Select(one => (one.Triangles, definition.CornersOf(one.X, one.Y)))]
            : [(triangles, definition.Grounds)];

        var runs = new Dictionary<string, Run>(StringComparer.Ordinal);
        var order = new List<Run>();
        var at = 0;
        var unnamedCorners = 0;
        foreach ((int count, IReadOnlyList<string> corners) in subTiles)
        {
            (string key, List<(int Order, GroundGroup Group)> grounds, string setWhy) = SetOf(corners, gridded);
            unnamedCorners += gridded && corners.Any(corner => corner.Length == 0) ? 1 : 0;
            if (!runs.TryGetValue(key, out Run? run))
            {
                run = new Run(grounds, setWhy);
                runs[key] = run;
                order.Add(run);
            }

            run.SubTiles++;
            for (var one = 0; one < count; one++)
            {
                run.Triangles.Add(at + one);
            }

            at += count;
        }

        string mask = definition.Templates.Count > 0 ? TileTemplate.Read(_read(Clean(definition.Templates[0]))).GroundMask : string.Empty;
        var indices = new List<int>(ground.Indices.Length);
        var shapes = new List<MeshShape>();
        var named = new List<(string Shape, string Material)>();
        var own = new bool[ground.Positions.Length];
        var plain = new Dictionary<string, int>(StringComparer.Ordinal);
        var splits = new Dictionary<string, int>(StringComparer.Ordinal);
        int oneGround = 0, mixedGrounds = 0, splitGrounds = 0;

        void Shape(IEnumerable<int> triangleList, string? material)
        {
            int from = indices.Count;
            foreach (int triangle in triangleList)
            {
                indices.AddRange([ground.Indices[triangle * 3], ground.Indices[(triangle * 3) + 1], ground.Indices[(triangle * 3) + 2]]);
            }

            string name = shapes.Count.ToString(CultureInfo.InvariantCulture);
            shapes.Add(new MeshShape(name, from, indices.Count - from));
            if (material is not null)
            {
                named.Add((name, material));
            }
        }

        void Plain(Run run, string reason)
        {
            plain[reason] = plain.GetValueOrDefault(reason) + run.SubTiles;
            Shape(run.Triangles, null);
        }

        foreach (Run run in order)
        {
            if (run.Why.Length > 0)
            {
                Plain(run, run.Why);
                continue;
            }

            string first = Heaviest(run.Grounds[0].Group);
            if (run.Grounds.Count == 1)
            {
                oneGround += run.SubTiles;
                Shape(run.Triangles, first);
                continue;
            }

            string second = Heaviest(run.Grounds[1].Group);
            if (mask.Length == 0 || MaskOf(mask) is not { } picture)
            {
                Plain(run, "of two grounds whose template names no mask that reads");
                continue;
            }

            if (!ground.Coordinated)
            {
                Plain(run, "of two grounds whose block carries no coordinates into its mask");
                continue;
            }

            // MIXED PER PIXEL as the engine mixes them, where the two grounds' programs run as one - see the remarks.
            if (_paints.Grounds(_read, first, second, Clean(mask), GroundScale, out string unmixed) is { } mixed)
            {
                mixedGrounds += run.SubTiles;
                foreach (int triangle in run.Triangles)
                {
                    own[ground.Indices[triangle * 3]] = own[ground.Indices[(triangle * 3) + 1]] = own[ground.Indices[(triangle * 3) + 2]] = true;
                }

                Shape(run.Triangles, mixed);
                continue;
            }

            // ELSE EACH TRIANGLE TO THE GROUND ITS VERTICES' GREEN AVERAGES TO, the first's then the second's.
            splitGrounds += run.SubTiles;
            splits[unmixed] = splits.GetValueOrDefault(unmixed) + run.SubTiles;
            var firsts = new List<int>(run.Triangles.Count);
            var seconds = new List<int>(run.Triangles.Count / 2);
            foreach (int triangle in run.Triangles)
            {
                float mean = 0f;
                for (var corner = 0; corner < 3; corner++)
                {
                    Vector2 uv = ground.Coordinates[ground.Indices[(triangle * 3) + corner]];
                    mean += Sample(picture, 1, uv.X, uv.Y) / 3f;
                }

                (mean >= Half ? seconds : firsts).Add(triangle);
            }

            Shape(firsts, first);
            Shape(seconds, second);
        }

        return new Plan([.. indices], shapes, named, own)
        {
            One = oneGround,
            Mixed = mixedGrounds,
            Split = splitGrounds,
            Gridded = gridded,
            Unnamed = unnamedCorners,
            Plain = plain,
            Splits = splits,
        };
    }

    /// <summary>
    /// The grounds a sub-tile's corners name, by name in the MaterialsList's order - the order the engine's weights follow (see the
    /// remarks) - with a key the sub-tiles naming the same set share; or why it stays plain.
    /// </summary>
    /// <remarks>
    /// A CORNER THE GRID LEAVES UNNAMED takes the MaterialsList's group with no name, where <paramref name="unnamedIsDefault"/> - a
    /// grid's corners. MEASURED (v0.1.248's capture, ground-grid-replay.txt): where Port's harbour grid names no ground, along the
    /// pier, the game holds the null slot of its ground-type list at the same corners, and draws the pier cobblestone; the list's one
    /// group without a name is all cobblestone. DECIDED, not measured: that the game draws that group for its null slot. A tile's own
    /// four corners, standing in where there is no grid, keep the old reading - an unnamed one names nothing.
    /// </remarks>
    private (string Key, List<(int Order, GroundGroup Group)> Grounds, string Why) SetOf(IReadOnlyList<string> corners, bool unnamedIsDefault)
    {
        var grounds = new List<(int Order, GroundGroup Group)>();
        foreach (string corner in corners)
        {
            if (corner.Length == 0)
            {
                int unnamed = unnamedIsDefault ? _unnamedGroup : -1;
                if (unnamed >= 0 && !grounds.Exists(one => one.Order == unnamed))
                {
                    grounds.Add((unnamed, _list.Groups[unnamed]));
                }

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
                return Plained("with a ground the MaterialsList does not list");
            }

            if (!grounds.Exists(one => one.Order == order))
            {
                grounds.Add((order, _list.Groups[order]));
            }
        }

        grounds.Sort((one, other) => one.Order.CompareTo(other.Order));
        return grounds.Count switch
        {
            0 => Plained("whose corners name no ground"),
            > 2 => Plained("of three or four grounds (the engine's atlas, placed by transforms no file holds)"),
            _ => (string.Join(",", grounds.Select(one => one.Order.ToString(CultureInfo.InvariantCulture))), grounds, string.Empty),
        };

        static (string, List<(int, GroundGroup)>, string) Plained(string why) => ("plain: " + why, [], why);
    }

    /// <summary>
    /// Each sub-tile's corners and the grounds they come to, as <see cref="Paint"/> works them out - for the capture's picture of the
    /// ground on the screen (CaptureGroundScreen) - and the template's mask with the picture it decodes to.
    /// </summary>
    /// <param name="tile">The tile's <c>.tdt</c>.</param>
    /// <param name="pieces">Which sub-tile each run of the block's triangles is, in the block's order.</param>
    /// <param name="triangles">The block's triangles, which the pieces must add up to for the grid to be read - as Paint asks.</param>
    internal (IReadOnlyList<SubTileGrounds> SubTiles, string Mask, GamePicture? Picture) SubTilesOf(string tile, IReadOnlyList<GroundPiece> pieces, int triangles)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        (TileDefinition definition, string why) = TileModels.Defined(_read, tile);
        if (!definition.Ready)
        {
            return ([.. pieces.Select(_ => new SubTileGrounds([], [], "whose definition did not read: " + why))], string.Empty, null);
        }

        bool gridded = definition.GroundGrid.Count > 0 && pieces.Count > 0 && pieces.Sum(one => one.Triangles) == triangles;
        var subTiles = new SubTileGrounds[pieces.Count];
        for (var at = 0; at < pieces.Count; at++)
        {
            IReadOnlyList<string> corners = gridded ? definition.CornersOf(pieces[at].X, pieces[at].Y) : definition.Grounds;
            (_, List<(int Order, GroundGroup Group)> grounds, string setWhy) = SetOf(corners, gridded);
            subTiles[at] = new SubTileGrounds(corners, [.. grounds.Select(one => one.Group.Name)], setWhy);
        }

        string mask = definition.Templates.Count > 0 ? TileTemplate.Read(_read(Clean(definition.Templates[0]))).GroundMask : string.Empty;
        return (subTiles, mask, mask.Length > 0 ? MaskOf(mask) : null);
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

/// <summary>One sub-tile's ground as GroundPainter works it out - see GroundPainter.SubTilesOf.</summary>
/// <param name="Corners">Its four corners' ground types (<c>.gt</c>), empty where unnamed - from the definition's grid, or the tile's own four.</param>
/// <param name="Grounds">The MaterialsList groups they come to, by name in the list's order - empty for the group with none.</param>
/// <param name="Why">Why it stays plain, or empty.</param>
internal sealed record SubTileGrounds(IReadOnlyList<string> Corners, IReadOnlyList<string> Grounds, string Why);

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
