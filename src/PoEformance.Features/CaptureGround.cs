using System.Globalization;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>A ground mask the laid templates name, decoded once - see <see cref="CaptureGround.Write(Func{string, byte[]}, TerrainTiles, IReadOnlyList{string}, IReadOnlyList{string}, IReadOnlyList{string}, out IReadOnlyList{GroundMask})"/>.</summary>
/// <param name="Number">Its number in ground.txt, from one - the PNGs beside it are named by it.</param>
/// <param name="Path">The texture as the templates name it.</param>
/// <param name="Picture">The decoded picture, or null where it would not decode.</param>
/// <param name="Templates">The templates naming it.</param>
public sealed record GroundMask(int Number, string Path, GamePicture? Picture, IReadOnlyList<string> Templates)
{
    /// <summary>The PNG one channel (r, g, b or a) of it is written to, beside ground.txt.</summary>
    public string FileOf(char channel)
        => string.Create(CultureInfo.InvariantCulture, $"ground-mask-{Number}-{System.IO.Path.GetFileNameWithoutExtension(Path)}-{channel}.png");
}

/// <summary>
/// What the ground under the area's laid tiles is made of, as far as the files say: what its blocks carry, what the
/// area's MaterialsList draws each corner's ground with, and the shader sources that name where its coordinates come from.
/// </summary>
/// <remarks>
/// THE QUESTION IT WAS WRITTEN FOR. The rooms' pictures draw every tile's ground block plain - white - and Port's pier, the
/// floor a staged Malgor fights on, is most of his picture. Three things stand between that and a painted ground, and each
/// is a file of the install, not something a picture of the game can settle:
///
///     what a ground block's vertex carries - the block facts below say, the fields the reader steps over as halves too
///     which material a corner's ground is drawn with - the master's MaterialsList keys its groups by the ground type's
///         name, and a group with several materials weighs them; a tile's four corners may name four grounds
///     how the engine makes the coordinates the ground materials read - the shader sources naming those words, whole
///
/// WHAT THE FIRST PORT CAPTURE (v0.1.195) SETTLED: 891 of its 893 ground blocks carry coordinates (format 0x238, a pair
/// of halves on bit 3 - small, 0.18..0.21 in one sub-tile, more like a place in the template's ground mask than a
/// material's tiling), while the engine's own CreateProjectedCoords makes the materials' from the world position:
/// uv = (world.xy + ground_scalemove_uv.zw) * ground_scalemove_uv.xy, a pass uniform no file holds. And the probe said
/// Port's white pier IS the harbour tile's ground block - whose corners name IslandOcean and IslandTownUnwalkableFill,
/// black and cobblestone, and the game draws it cobblestone. How a tile of two grounds is split between them is the
/// engine's blend - up to four materials by engine and user masks - so the sources of that blending are searched for,
/// the template is written whole (its MaterialSlots and the triples' first numbers are read past by TileTemplate), and
/// each template's ground shapes and coordinates are counted.
///
/// The Tile Book's tile dump answers the same for ONE tile from a window; this answers it for every tile the area laid,
/// and stays in the capture with the pictures and the recording.
///
/// READ ONCE A TEMPLATE. Tile files share templates, and a template is read with every sub-tile mesh it names - Port's boss
/// harbour is 315 - so each is read once whichever files lay it, up to <see cref="MostSubTiles"/> in all.
/// </remarks>
public static class CaptureGround
{
    /// <summary>Most sub-tile meshes read over the whole area - past it the templates are named, not opened.</summary>
    public const int MostSubTiles = 4096;

    /// <summary>Most ground materials written with their graphs.</summary>
    public const int MostMaterials = 48;

    /// <summary>Most shader sources written whole; more are named.</summary>
    public const int MostShaders = 32;

    /// <summary>The words a shader source is taken for: the ground's coordinates and the ground's basis, as the files spell them.</summary>
    /// <remarks>
    /// The first two are the line the tile dump caught; <c>TileGroundUVs</c> is the graph node MASK_TextureStatic uses, and
    /// <c>GetGroundTBN</c> the node the engine's own Ground_TBN graph writes the ground's basis with - the coordinates' twin.
    /// The rest are what the ground's blending reads and the first capture's sources did not define: the weights
    /// (<c>first_ground</c>), the two masks, and how the engine's mask coordinates are made.
    /// </remarks>
    public static IReadOnlyList<string> Words { get; } =
        ["tile_ground_tiling", "ground_scalemove", "TileGroundUVs", "GetGroundTBN", "first_ground", "GetEngineMask", "GetUserMask", "GenerateBlendUVs", "blend1_uv_transform"];

    /// <summary>Most templates written whole; more are named.</summary>
    public const int MostTemplatesWhole = 96;

    /// <summary>Most ground masks decoded and handed on as pictures; more are named.</summary>
    public const int MostMasks = 8;

    /// <summary>
    /// The whole text of the part. Never throws for a file that will not read - it is named instead.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="tiles">The tiles the area laid, or null where the terrain was not read.</param>
    /// <param name="loaded">The files the area loaded - for its masters.</param>
    /// <param name="masters">Every master the install has, as its index lists them - or none.</param>
    /// <param name="shaders">The install's shader sources, as its walk found them - or none.</param>
    public static string Write(
        Func<string, byte[]?> read,
        TerrainTiles? tiles,
        IReadOnlyList<string> loaded,
        IReadOnlyList<string>? masters,
        IReadOnlyList<string>? shaders)
        => Write(read, tiles, loaded, masters, shaders, out _);

    /// <summary>
    /// The whole text of the part, and the ground masks the laid templates name, decoded - for the caller to write each
    /// channel beside it as a picture (<see cref="GroundMask.FileOf"/>), which no line of text can stand in for.
    /// </summary>
    /// <remarks>
    /// WHY THE MASKS AS PICTURES: the engine blends a tile's grounds by one texture's channels - red sampled through an
    /// atlas of eight transitions, green through the ground block's own coordinates (ground.ffx, user_blend and
    /// blend_uv_generation) - and whether a template's GroundMask is that texture, and what its channels hold, is to be
    /// looked at before any blend is built on it.
    /// </remarks>
    public static string Write(
        Func<string, byte[]?> read,
        TerrainTiles? tiles,
        IReadOnlyList<string> loaded,
        IReadOnlyList<string>? masters,
        IReadOnlyList<string>? shaders,
        out IReadOnlyList<GroundMask> masks)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(loaded);
        var body = new StringBuilder();

        // THE LAID FILES, each with its cells: a ground covering the area is counted by the ground it covers, not by files.
        Dictionary<string, int> cells = Cells(tiles);
        List<string> laid = [.. cells.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        var corners = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var templates = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in laid)
        {
            (TileDefinition definition, _) = TileModels.Defined(read, path);
            if (!definition.Ready)
            {
                continue;
            }

            corners[path] = [.. definition.Grounds.Select(Clean)];
            foreach (string template in definition.Templates)
            {
                if (!templates.TryGetValue(Clean(template), out List<string>? users))
                {
                    users = [];
                    templates[Clean(template)] = users;
                }

                users.Add(path);
            }
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string NameOf(string ground)
        {
            if (!names.TryGetValue(ground, out string? name))
            {
                name = ground.Length == 0 ? string.Empty : GroundType.Name(read(ground));
                names[ground] = name;
            }

            return name;
        }

        var texts = new List<(string Path, string Text)>();
        var named = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int opened = Blocks(read, templates, body, texts, named, out int subTiles);
        masks = Masks(read, named, body);
        string groundsSaid = Corners(read, laid, cells, corners, NameOf, loaded, masters, body);
        string shadersSaid = Shaders(read, shaders, body);
        Whole(texts, body);

        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture,
            $"{laid.Count} tile files laid over {cells.Values.Sum()} cells, {templates.Count} templates, {opened} of their {subTiles} sub-tile meshes read, {named.Count} ground masks; {groundsSaid}; {shadersSaid}")
            .AppendLine();
        if (tiles is null)
        {
            said.AppendLine("(the terrain's tiles were not read - nothing laid to write about)");
        }

        said.Append(body);
        return said.ToString();
    }

    /// <summary>The ground blocks of every template, by vertex format - then one line a template. Returns how many sub-tile meshes were read.</summary>
    private static int Blocks(
        Func<string, byte[]?> read,
        SortedDictionary<string, List<string>> templates,
        StringBuilder said,
        List<(string Path, string Text)> texts,
        SortedDictionary<string, List<string>> masks,
        out int subTiles)
    {
        var formats = new SortedDictionary<uint, Format>();
        var lines = new StringBuilder();
        int opened = 0;
        subTiles = 0;
        int unread = 0;
        string firstUnread = string.Empty;
        foreach ((string template, List<string> users) in templates)
        {
            byte[]? raw = read(template);
            TileTemplate layout = TileTemplate.Read(raw);
            if (raw is { Length: > 0 })
            {
                texts.Add((template, StatDescriptionFiles.Decode(raw)));
            }

            if (layout.Ready && layout.GroundMask.Length > 0)
            {
                string mask = Clean(layout.GroundMask);
                if (!masks.TryGetValue(mask, out List<string>? naming))
                {
                    naming = [];
                    masks[mask] = naming;
                }

                naming.Add(template);
            }

            lines.Append("  ").Append(template);
            if (!layout.Ready)
            {
                lines.Append("  (did not read: ").Append(layout.Why).AppendLine(")");
                continue;
            }

            int count = layout.Width * layout.Height;
            subTiles += count;
            lines.Append(CultureInfo.InvariantCulture, $"  {layout.Width}x{layout.Height}");
            if (count > TileModels.MostSubTiles || opened + count > MostSubTiles)
            {
                lines.Append(opened + count > MostSubTiles ? "  (not opened: past the part's limit)" : "  (not opened: too many sub-tiles)");
                lines.Append("  mask ").Append(layout.GroundMask.Length > 0 ? layout.GroundMask : "none");
                lines.Append("  laid as ").AppendLine(string.Join(", ", users.Select(Short)));
                continue;
            }

            var mine = new SortedSet<uint>();
            int grounds = 0, coordinated = 0, fewestShapes = int.MaxValue, mostShapes = 0;
            float uLeast = float.MaxValue, uMost = float.MinValue, vLeast = float.MaxValue, vMost = float.MinValue;
            for (var y = 1; y <= layout.Height; y++)
            {
                for (var x = 1; x <= layout.Width; x++)
                {
                    string mesh = layout.MeshOf(x, y);
                    TileMesh part = TileMesh.Read(read(Clean(mesh)));
                    opened++;
                    if (!part.Ready)
                    {
                        unread++;
                        firstUnread = firstUnread.Length > 0 ? firstUnread : $"{mesh} - {part.Why}";
                        continue;
                    }

                    SkinnedMesh ground = part.Ground;
                    if (!ground.Ready)
                    {
                        continue;
                    }

                    grounds++;
                    fewestShapes = Math.Min(fewestShapes, ground.Shapes.Count);
                    mostShapes = Math.Max(mostShapes, ground.Shapes.Count);
                    bool coordinates = ground.Coordinated;
                    coordinated += coordinates ? 1 : 0;
                    if (coordinates)
                    {
                        foreach (System.Numerics.Vector2 spot in ground.Coordinates)
                        {
                            (uLeast, uMost) = (MathF.Min(uLeast, spot.X), MathF.Max(uMost, spot.X));
                            (vLeast, vMost) = (MathF.Min(vLeast, spot.Y), MathF.Max(vMost, spot.Y));
                        }
                    }
                    uint word = ground.Facts.Format;
                    mine.Add(word);
                    if (!formats.TryGetValue(word, out Format? format))
                    {
                        format = new Format(ground, string.Create(CultureInfo.InvariantCulture, $"{mesh} (c{x}r{y} of {template})"));
                        formats[word] = format;
                    }

                    format.SubTiles++;
                    format.Templates.Add(template);
                    format.Vertices += ground.Positions.Length;
                    format.Coordinated += coordinates ? 1 : 0;
                }
            }

            lines.Append(CultureInfo.InvariantCulture, $"  ground in {grounds} of {count}");
            if (mine.Count > 0)
            {
                lines.Append("  format ").Append(string.Join(", ", mine.Select(Hex)));
            }

            if (grounds > 0)
            {
                lines.Append(CultureInfo.InvariantCulture, $"  ground shapes {fewestShapes}..{mostShapes} a sub-tile");
            }

            lines.Append(CultureInfo.InvariantCulture, $"  coordinates in {coordinated}");
            if (coordinated > 0)
            {
                lines.Append(CultureInfo.InvariantCulture, $" (u {uLeast:G4}..{uMost:G4} v {vLeast:G4}..{vMost:G4})");
            }

            lines.Append("  mask ").Append(layout.GroundMask.Length > 0 ? layout.GroundMask : "none");
            lines.Append("  laid as ").AppendLine(string.Join(", ", users.Select(Short)));
        }

        said.AppendLine().AppendLine("=== the ground blocks, by vertex format");
        if (formats.Count == 0)
        {
            said.AppendLine("(no ground block read)");
        }

        foreach ((uint word, Format format) in formats)
        {
            MeshFacts facts = format.First.Facts;
            said.Append(CultureInfo.InvariantCulture,
                $"format {Hex(word)} · stride {facts.Stride} · in {format.SubTiles} sub-tiles of {format.Templates.Count} templates · {format.Vertices} vertices · texture coordinates in {format.Coordinated}")
                .AppendLine();
            said.Append("  first: ").AppendLine(format.Where);
            said.Append("  ").AppendLine(Spread(format.First));
            said.Append("  unknown vertex fields · ").AppendLine(facts.Extras.Length > 0 ? facts.Extras : "none");
        }

        if (unread > 0)
        {
            said.Append(CultureInfo.InvariantCulture, $"{unread} sub-tile meshes did not read, the first {firstUnread}").AppendLine();
        }

        said.AppendLine().AppendLine("=== the templates: size, sub-tiles with a ground, its formats, how many carry coordinates, the ground mask, the files laying it");
        said.Append(lines);
        return opened;
    }

    /// <summary>What one vertex format came to over the area.</summary>
    private sealed class Format(SkinnedMesh first, string where)
    {
        public SkinnedMesh First { get; } = first;

        public string Where { get; } = where;

        public HashSet<string> Templates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int SubTiles { get; set; }

        public int Vertices { get; set; }

        public int Coordinated { get; set; }
    }

    /// <summary>
    /// The laid files' corners by ground name, the area's MaterialsList groups for each, and the ground materials whole. Returns the line's summary.
    /// </summary>
    private static string Corners(
        Func<string, byte[]?> read,
        List<string> laid,
        Dictionary<string, int> cells,
        Dictionary<string, string[]> corners,
        Func<string, string> nameOf,
        IReadOnlyList<string> loaded,
        IReadOnlyList<string>? masters,
        StringBuilder said)
    {
        // EACH NAME with the files and cells it lies under, and how many of those have it at all four corners.
        var byName = new SortedDictionary<string, (int Files, int Cells, int AllRound, int AllRoundCells)>(StringComparer.OrdinalIgnoreCase);
        int one = 0, oneCells = 0, mixed = 0, mixedCells = 0, none = 0, noneCells = 0;
        foreach (string path in laid)
        {
            int covered = cells.GetValueOrDefault(path);
            string[] four = corners.TryGetValue(path, out string[]? known) ? known : [];
            string[] named = [.. four.Select(nameOf)];
            string[] distinct = [.. named.Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
            bool allRound = named.Length == 4 && distinct.Length == 1 && named.All(name => name.Length > 0);
            if (allRound)
            {
                (one, oneCells) = (one + 1, oneCells + covered);
            }
            else if (distinct.Length == 0)
            {
                (none, noneCells) = (none + 1, noneCells + covered);
            }
            else
            {
                (mixed, mixedCells) = (mixed + 1, mixedCells + covered);
            }

            foreach (string name in distinct)
            {
                (int files, int under, int round, int roundCells) = byName.GetValueOrDefault(name);
                byName[name] = (files + 1, under + covered, round + (allRound ? 1 : 0), roundCells + (allRound ? covered : 0));
            }
        }

        said.AppendLine().AppendLine("=== the laid files' corners, by ground name");
        said.Append(CultureInfo.InvariantCulture,
            $"one ground all round: {one} files over {oneCells} cells · mixed: {mixed} files over {mixedCells} cells · no ground named: {none} files over {noneCells} cells")
            .AppendLine();
        foreach ((string name, (int files, int under, int round, int roundCells)) in byName)
        {
            said.Append(CultureInfo.InvariantCulture,
                $"  \"{name}\": at a corner of {files} files over {under} cells; all round in {round} files over {roundCells} cells").AppendLine();
        }

        // THE MASTERS' MATERIALSLISTS, each read where the master's name resolves first - as RePoE resolves it.
        var materials = new List<string>();
        var offeredBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int lists = 0;
        foreach (string master in AreaRoomSet.Masters(loaded, masters))
        {
            string path = Clean(master);
            string named = MasterFile.Read(read(path)).Values.GetValueOrDefault("MaterialsList", string.Empty);
            foreach (string candidate in MasterFile.Candidates(path, named))
            {
                if (read(candidate) is not { Length: > 0 } bytes)
                {
                    continue;
                }

                lists++;
                GroundMaterials list = GroundMaterials.Read(bytes);
                said.AppendLine().Append("=== MaterialsList ").Append(candidate).Append("  (of ").Append(path).AppendLine(")");
                said.Append(CultureInfo.InvariantCulture, $"version {list.Version} · {list.Groups.Count} groups")
                    .AppendLine(list.Ready ? string.Empty : " · stopped reading at " + list.Why);
                foreach (GroundGroup group in list.Groups)
                {
                    string label = group.Name.Length > 0 ? "\"" + group.Name + "\"" : "(unnamed)";
                    string laidSaid = group.Name.Length == 0 ? "the group with no name"
                        : byName.TryGetValue(group.Name, out (int Files, int Cells, int AllRound, int AllRoundCells) under) ? string.Create(CultureInfo.InvariantCulture, $"at a corner of {under.Files} laid files")
                        : "at no laid corner";
                    said.Append("  ").Append(label).Append("  ").Append(laidSaid).Append(CultureInfo.InvariantCulture, $" · {group.Choices.Count} choices");
                    if (group.Weights.Count > 0)
                    {
                        said.Append(" · weights ").Append(string.Join(' ', group.Weights.Select(Say))).Append(" · then ").Append(Say(group.Trailing));
                    }

                    said.AppendLine();
                    foreach (GroundChoice choice in group.Choices.Concat(group.Extras))
                    {
                        said.Append("      ").Append(choice.Material);
                        foreach (string layer in choice.Layers)
                        {
                            said.Append(" + ").Append(layer);
                        }

                        said.AppendLine();
                        string material = Clean(choice.Material);
                        if (!offeredBy.TryGetValue(material, out List<string>? users))
                        {
                            users = [];
                            offeredBy[material] = users;
                            materials.Add(material);
                        }

                        users.Add(label);
                    }
                }

                foreach (string name in byName.Keys.Where(name => list.Named(name) is null))
                {
                    said.Append("  \"").Append(name).AppendLine("\"  laid, and not in this list");
                }

                break;
            }
        }

        Materials(read, materials, offeredBy, said);
        return string.Create(CultureInfo.InvariantCulture,
            $"{oneCells} cells under one ground all round, {mixedCells} mixed; {lists} MaterialsLists, {materials.Count} ground materials");
    }

    /// <summary>Every ground material once: the file, the Input nodes its graphs name, what it compiles to - and each graph once, whole.</summary>
    private static void Materials(Func<string, byte[]?> read, List<string> materials, Dictionary<string, List<string>> offeredBy, StringBuilder said)
    {
        if (materials.Count == 0)
        {
            return;
        }

        said.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the ground materials ({materials.Count})").AppendLine();
        var paints = new MonsterModels.Paints();
        var graphs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string material in materials.Take(MostMaterials))
        {
            said.AppendLine().Append("##### ").AppendLine(material);
            said.Append("offered as: ").AppendLine(string.Join(", ", offeredBy[material]));
            byte[]? content = read(material);
            if (content is not { Length: > 0 })
            {
                said.AppendLine("(not in the install)");
                continue;
            }

            foreach (string line in StatDescriptionFiles.Decode(content).TrimEnd().Split('\n'))
            {
                said.Append("  | ").AppendLine(line.TrimEnd('\r'));
            }

            (ShadeCompile compiled, IReadOnlyList<string> inputs) = paints.Compiled(read, material);
            said.Append("graphs name: ").AppendLine(inputs.Count > 0 ? string.Join(", ", inputs) : "no Input node");
            said.Append("program: ").AppendLine(compiled.Program switch
            {
                null => "none - " + (compiled.Skipped.Count > 0 ? string.Join("; ", compiled.Skipped) : "no graph compiled"),
                { Plain: >= 0 } plain => "a plain read of " + plain.Textures[plain.Plain].Path,
                { } program => Say(program.Textures.Count) + " textures read: " + string.Join(", ", program.Textures.Select(texture => texture.Path))
                    + (compiled.Skipped.Count > 0 ? " · left out: " + string.Join("; ", compiled.Skipped) : string.Empty),
            });

            foreach (string graph in MaterialFile.Read(content).Parents)
            {
                if (graph.Length > 0 && seen.Add(Clean(graph)))
                {
                    graphs.Add(Clean(graph));
                }
            }
        }

        if (materials.Count > MostMaterials)
        {
            said.Append(CultureInfo.InvariantCulture, $"{materials.Count - MostMaterials} more ground materials not written").AppendLine();
        }

        foreach (string graph in graphs)
        {
            said.AppendLine().Append("##### ").Append(graph).AppendLine("  (a graph the ground materials are instances of)");
            byte[]? text = read(graph);
            said.AppendLine(text is { Length: > 0 } ? StatDescriptionFiles.Decode(text).TrimEnd() : "(not in the install)");
        }
    }

    /// <summary>
    /// The shader sources that name any of <see cref="Words"/>, whole, in path order. Returns the line's summary.
    /// </summary>
    /// <remarks>
    /// WHOLE, because a line without its fragment was what the tile dump's first search gave, and "pos" in it could not be
    /// told from anything. Every source is read to be searched; only those naming a word are written.
    /// </remarks>
    private static string Shaders(Func<string, byte[]?> read, IReadOnlyList<string>? shaders, StringBuilder said)
    {
        said.AppendLine().Append("=== shader sources naming ").AppendLine(string.Join(", ", Words));
        if (shaders is not { Count: > 0 })
        {
            said.AppendLine("(no shader sources: the install walk found none, or it had not run when the key went down)");
            return "no shader sources to search";
        }

        var found = new List<(string Path, string Text, string Words)>();
        foreach (string path in shaders.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (read(Clean(path)) is not { Length: > 0 } bytes)
            {
                continue;
            }

            string text = StatDescriptionFiles.Decode(bytes);
            string[] named = [.. Words.Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))];
            if (named.Length > 0)
            {
                found.Add((Clean(path), text, string.Join(", ", named)));
            }
        }

        said.Append(CultureInfo.InvariantCulture, $"{found.Count} of {shaders.Count} sources name one").AppendLine();
        foreach ((string path, _, string words) in found)
        {
            said.Append("  ").Append(path).Append("  (").Append(words).AppendLine(")");
        }

        foreach ((string path, string text, _) in found.Take(MostShaders))
        {
            said.AppendLine().Append("##### ").AppendLine(path);
            said.AppendLine(text.TrimEnd());
        }

        if (found.Count > MostShaders)
        {
            said.AppendLine().Append(CultureInfo.InvariantCulture, $"{found.Count - MostShaders} more not written whole - named above").AppendLine();
        }

        return string.Create(CultureInfo.InvariantCulture, $"{found.Count} of {shaders.Count} shader sources name the ground's coordinates");
    }

    /// <summary>The ground masks the templates name, each decoded once and described: header, size, every channel's spread.</summary>
    private static List<GroundMask> Masks(Func<string, byte[]?> read, SortedDictionary<string, List<string>> named, StringBuilder said)
    {
        var masks = new List<GroundMask>();
        said.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the ground masks ({named.Count}) - each channel beside this file as a picture, ground-mask-N-name-r/g/b/a.png").AppendLine();
        foreach ((string path, List<string> templates) in named)
        {
            if (masks.Count == MostMasks)
            {
                said.Append("  ").Append(path).AppendLine("  (not decoded: past the part's limit)");
                continue;
            }

            byte[]? raw = GameArt.ReadRaw(read, path);
            GamePicture? picture = GameArt.Decode(raw);
            var mask = new GroundMask(masks.Count + 1, path, picture is { Ready: true } ? picture : null, templates);
            masks.Add(mask);
            said.AppendLine().Append(CultureInfo.InvariantCulture, $"{mask.Number}. {path} - named by {templates.Count} templates").AppendLine();
            said.Append("   header: ").AppendLine(ModelDump.Header(raw));
            if (mask.Picture is { } decoded)
            {
                foreach (string line in ModelDump.Channels(decoded).TrimEnd().Split('\n'))
                {
                    said.Append("   ").AppendLine(line.TrimEnd('\r'));
                }

                said.Append("   pictures: ").AppendLine(string.Join(", ", "rgba".Select(mask.FileOf)));
            }
            else
            {
                said.AppendLine("   (did not decode)");
            }
        }

        return masks;
    }

    /// <summary>
    /// Every laid template whole, in path order - what TileTemplate reads past (MaterialSlots, a triple's first number) included.
    /// </summary>
    private static void Whole(List<(string Path, string Text)> texts, StringBuilder said)
    {
        said.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the templates whole ({texts.Count})").AppendLine();
        foreach ((string path, string text) in texts.Take(MostTemplatesWhole))
        {
            said.AppendLine().Append("##### ").AppendLine(path);
            foreach (string line in text.TrimEnd().Split('\n'))
            {
                said.Append("  | ").AppendLine(line.TrimEnd('\r'));
            }
        }

        if (texts.Count > MostTemplatesWhole)
        {
            said.AppendLine().Append(CultureInfo.InvariantCulture, $"{texts.Count - MostTemplatesWhole} more templates not written - named in the template lines above").AppendLine();
        }
    }

    /// <summary>How many cells each laid tile file covers.</summary>
    private static Dictionary<string, int> Cells(TerrainTiles? tiles)
    {
        var cells = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (tiles is null)
        {
            return cells;
        }

        var counts = new int[tiles.Paths.Count];
        for (var y = 0; y < tiles.Height; y++)
        {
            for (var x = 0; x < tiles.Width; x++)
            {
                if (tiles.IdAt(x, y) is var id and >= 0 && id < counts.Length)
                {
                    counts[id]++;
                }
            }
        }

        for (var id = 0; id < counts.Length; id++)
        {
            string path = Clean(tiles.Paths[id]);
            if (counts[id] > 0 && path.Length > 0)
            {
                cells[path] = cells.GetValueOrDefault(path) + counts[id];
            }
        }

        return cells;
    }

    /// <summary>A mesh in one line: size, box, and its coordinates' range where it has any.</summary>
    private static string Spread(SkinnedMesh mesh)
    {
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture,
            $"{mesh.Positions.Length} vertices · {mesh.Triangles} triangles · x {mesh.Least.X:0.#}..{mesh.Most.X:0.#} y {mesh.Least.Y:0.#}..{mesh.Most.Y:0.#} z {mesh.Least.Z:0.#}..{mesh.Most.Z:0.#}");
        if (!mesh.Coordinated)
        {
            return line.Append(" · no texture coordinates").ToString();
        }

        float uLeast = float.MaxValue, uMost = float.MinValue, vLeast = float.MaxValue, vMost = float.MinValue;
        foreach (System.Numerics.Vector2 spot in mesh.Coordinates)
        {
            uLeast = MathF.Min(uLeast, spot.X);
            uMost = MathF.Max(uMost, spot.X);
            vLeast = MathF.Min(vLeast, spot.Y);
            vMost = MathF.Max(vMost, spot.Y);
        }

        return line.Append(CultureInfo.InvariantCulture, $" · u {uLeast:G4}..{uMost:G4} v {vLeast:G4}..{vMost:G4}").ToString();
    }

    private static string Hex(uint word) => "0x" + word.ToString("X", CultureInfo.InvariantCulture);

    private static string Short(string path) => Path.GetFileNameWithoutExtension(path);

    private static string Say(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static string Clean(string path) => path.Replace('\\', '/').Trim();
}
