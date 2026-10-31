using System.Globalization;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// The capture's tile-layout.txt: every tile definition (<c>.tdt</c>) the area's lists name or its terrain laid, walked whole by zao's
/// layout (<see cref="TileLayout"/>), and every edge type (<c>.et</c>) its tiles and rooms name, read and whole - each file beside the
/// part (<see cref="Folder"/>), read again offline (<see cref="Replay"/>).
/// </summary>
/// <remarks>
/// BUILT BEFORE THE TILE READER IS CHANGED (v0.1.382, asked for from the live client with zao's poe-cpp in hand). The tool's
/// TileDefinition reads to the corners and FINDS the ground grid by its shape; zao's reader walks on past them - side offsets, flags,
/// sub-tiles, ground grid, heights, the tmd - and asks for the file's end. But his repository's last commit (2022-07-02) is older than
/// PoE2, so his layout is a lead, not a rule. The measure is the files' own: how many of this install's files his layout takes to their
/// last byte, by version and flags, and where the others stop - then whether his grid is the one the tool's search finds, what the side
/// offsets hold beside the sides' types (his own code reads them two ways), what the heights hold, and which sub-tile types occur, version
/// 4's own templates among them. The edge types go with it because a virtual edge names the edges it stands for - the files' own word on
/// which types a slot may take as alike, which SlotWants takes on trust today.
/// </remarks>
public static class CaptureTileLayout
{
    /// <summary>The folder beside the part's file the definitions and edge types are written to, at their install paths.</summary>
    public const string Folder = "tile-layout";

    /// <summary>
    /// The most bytes written beside the part (CHOSEN, as the stage sweep's samples): the rest are walked and reported all the same,
    /// and listed as not written.
    /// </summary>
    public const long MostBeside = 64L * 1024 * 1024;

    /// <summary>
    /// The part's text, every definition and edge type written under <see cref="Folder"/> beside it where <paramref name="folder"/> is given.
    /// </summary>
    /// <param name="read">The install's files.</param>
    /// <param name="loaded">The files the area loaded - its masters and room sets among them.</param>
    /// <param name="masters">The install's index of masters, or null.</param>
    /// <param name="laid">The tiles the area laid, or null where the terrain was not read - then only the listed files.</param>
    /// <param name="folder">The part's folder, or null to write nothing beside it.</param>
    public static string Write(Func<string, byte[]?> read, IReadOnlyList<string> loaded, IReadOnlyList<string>? masters, TerrainTiles? laid, string? folder)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(loaded);

        // EVERY TILE FILE AND EVERY FILE IT INHERITS, each once.
        SortedDictionary<string, string> named = CaptureReport.TileFileSet(read, loaded, masters, laid);
        var tiles = new SortedDictionary<string, (byte[] Bytes, string From)>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach ((string path, string from) in named)
        {
            string at = path;
            string by = from;
            for (var hop = 0; hop <= TileModels.MostHops; hop++)
            {
                if (tiles.ContainsKey(at))
                {
                    break;
                }

                if (read(at) is not { Length: > 0 } bytes)
                {
                    missing.Add(at);
                    break;
                }

                tiles[at] = (bytes, by);
                string inherits = TileDefinition.Read(bytes).Inherits.Replace('\\', '/').Trim();
                if (inherits.Length == 0)
                {
                    break;
                }

                by = "inherited by " + at;
                at = inherits;
            }
        }

        // EVERY EDGE TYPE the tiles' sides and the rooms' tables name, and every one a virtual edge stands for.
        var edgeNames = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Name(string edge, string by)
        {
            string at = edge.Replace('\\', '/').Trim();
            if (at.EndsWith(EdgeTypeFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                (edgeNames.TryGetValue(at, out SortedSet<string>? set) ? set : edgeNames[at] = new SortedSet<string>(StringComparer.Ordinal)).Add(by);
            }
        }

        foreach ((_, (byte[] bytes, _)) in tiles)
        {
            foreach (TileRef side in TileLayout.Read(bytes).SideEdges)
            {
                Name(side.Text ?? string.Empty, "tiles");
            }
        }

        List<string> rooms = AreaRoomSet.Files(loaded, read, masters);
        foreach (string room in rooms)
        {
            foreach (string text in RoomLayout.Read(read(room)).Strings)
            {
                Name(text, "rooms");
            }
        }

        var edges = new SortedDictionary<string, (byte[] Bytes, string From)>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(edgeNames.Keys);
        while (queue.TryDequeue(out string? edge))
        {
            if (edges.ContainsKey(edge))
            {
                continue;
            }

            if (read(edge) is not { Length: > 0 } bytes)
            {
                missing.Add(edge);
                continue;
            }

            EdgeTypeFile file = EdgeTypeFile.Read(bytes);
            foreach ((string stands, _) in file.Virtual)
            {
                Name(stands, "virtual of " + edge);
                if (!edges.ContainsKey(stands.Replace('\\', '/').Trim()))
                {
                    queue.Enqueue(stands.Replace('\\', '/').Trim());
                }
            }

            edges[edge] = (bytes, string.Empty);
        }

        foreach (string edge in edges.Keys.ToList())
        {
            edges[edge] = (edges[edge].Bytes, string.Join(", ", edgeNames.GetValueOrDefault(edge) ?? []));
        }

        // EACH FILE BESIDE THE PART, to the cap.
        var unwritten = new List<string>();
        long written = 0;
        foreach ((string path, (byte[] bytes, _)) in tiles.Concat(edges))
        {
            if (folder is null)
            {
                break;
            }

            if (written + bytes.Length > MostBeside)
            {
                unwritten.Add(path);
                continue;
            }

            if (Beside(folder, path, bytes))
            {
                written += bytes.Length;
            }
        }

        var head = new StringBuilder();
        head.Append(CultureInfo.InvariantCulture, $"from {named.Count} tile files the area's tile lists or fill lists name or its terrain laid ({(laid is null ? "the terrain not read" : "laid read")}), ")
            .Append(CultureInfo.InvariantCulture, $"their inherited files and {rooms.Count} rooms; {missing.Count} named files not in the install");
        if (missing.Count > 0)
        {
            head.Append(": ").Append(string.Join(", ", missing.Distinct(StringComparer.OrdinalIgnoreCase).Take(40)));
        }

        head.AppendLine();
        head.Append(CultureInfo.InvariantCulture, $"written beside the part under {Folder}/: {written / 1024.0 / 1024.0:0.#} MB");
        head.Append(unwritten.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $", {unwritten.Count} files past the cap of {MostBeside / 1024 / 1024} MB left out: {string.Join(", ", unwritten.Take(40))}") : string.Empty);
        head.AppendLine();
        return Report(
            [.. tiles.Select(one => (one.Key, one.Value.Bytes, one.Value.From))],
            [.. edges.Select(one => (one.Key, one.Value.Bytes, one.Value.From))],
            head.ToString());
    }

    /// <summary>
    /// The definitions and edge types written beside a capture's tile-layout.txt walked again with today's readers - tile-layout-replay.txt.
    /// </summary>
    /// <param name="folder">The folder the part's file is in.</param>
    public static string Replay(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string root = Path.Combine(folder, Folder);
        if (!Directory.Exists(root))
        {
            return "tile layout replay: no " + Folder + " folder beside the part - a capture before v0.1.382, or the part was off\n";
        }

        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        var tiles = new List<(string, byte[], string)>();
        var edges = new List<(string, byte[], string)>();
        foreach (string file in files)
        {
            string path = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (path.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase))
            {
                tiles.Add((path, File.ReadAllBytes(file), "replayed"));
            }
            else if (path.EndsWith(EdgeTypeFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                edges.Add((path, File.ReadAllBytes(file), "replayed"));
            }
        }

        return Report(tiles, edges, "replayed from " + Folder + "/ with today's readers\n").Replace("tile layout: ", "tile layout replay: ", StringComparison.Ordinal);
    }

    /// <summary>The whole report over the definitions and edge types given - the capture's and the replay's alike.</summary>
    /// <param name="tiles">Each definition: its install path, its bytes and what named it.</param>
    /// <param name="edges">Each edge type: its install path, its bytes and what named it.</param>
    /// <param name="from">Lines on where the files came from, written under the first line.</param>
    public static string Report(IReadOnlyList<(string Path, byte[] Bytes, string From)> tiles, IReadOnlyList<(string Path, byte[] Bytes, string From)> edges, string from)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(from);

        // EVERY FILE WALKED ONCE, on every core: the walks do not share anything.
        var walked = new (TileLayout Layout, TileDefinition Tool)[tiles.Count];
        Parallel.For(0, tiles.Count, one => walked[one] = (TileLayout.Read(tiles[one].Bytes), TileDefinition.Read(tiles[one].Bytes)));
        EdgeTypeFile[] read = [.. edges.Select(one => EdgeTypeFile.Read(one.Bytes))];

        var said = new StringBuilder();
        int whole = walked.Count(one => one.Layout.Whole);
        said.Append(CultureInfo.InvariantCulture,
            $"tile layout: {tiles.Count} tile definitions walked by zao's layout (poe-cpp, PoE1, 2022) - {whole} to their last byte exactly, {tiles.Count - whole} not; ")
            .Append(CultureInfo.InvariantCulture, $"{edges.Count} edge types, {read.Count(one => one.Why.Length == 0)} read whole by poe_data_tools' grammar, {read.Count(one => one.Virtual.Count > 0)} virtual")
            .AppendLine();
        said.Append(from).AppendLine();

        Holds(said, walked);
        Leads(said, walked);
        Grids(said, walked);
        Offsets(said, walked);
        Heights(said, walked);
        SubTiles(said, walked);
        Refusals(said, walked);
        Edges(said, edges, read);

        // EVERY FILE, field by field.
        said.AppendLine().AppendLine("=== every tile definition: what named it, then each field walked - byte it began at, bytes it took, name, value - and where the walk stopped");
        for (var one = 0; one < tiles.Count; one++)
        {
            (TileLayout layout, TileDefinition tool) = walked[one];
            said.Append("##### ").Append(tiles[one].Path).Append("  (").Append(tiles[one].From).AppendLine(")");
            said.Append(CultureInfo.InvariantCulture,
                $"  version {layout.Version}, {layout.Length} bytes, walked to {layout.End}{(layout.Whole ? " - the file's end" : " - " + layout.Stop)}; the tool's reader read to {tool.End}{(tool.Ready ? string.Empty : " (" + tool.Why + ")")}").AppendLine();
            foreach (TileLayoutField field in layout.Fields)
            {
                said.Append(CultureInfo.InvariantCulture, $"  @{field.At} +{field.Bytes} {field.Name}: {field.Said}").AppendLine();
            }

            for (var index = 0; index < layout.SubTiles.Count; index++)
            {
                TileLayoutSubTile sub = layout.SubTiles[index];
                said.Append(CultureInfo.InvariantCulture, $"    sub-tile {index} @{sub.At}: type 0x{sub.Type:X2} value {sub.Value}");
                if (sub.Fixed is { } block)
                {
                    said.Append(", fixed ").Append(Values(block));
                }

                if (sub.Varying is { } varying)
                {
                    said.Append(CultureInfo.InvariantCulture, $", block of {varying.Length}: {Hex(varying, 48)}");
                }

                if (sub.Template is { } template)
                {
                    said.Append(", template ").Append(TileLayout.Said(template));
                }

                said.AppendLine();
            }
        }

        return said.ToString();
    }

    /// <summary>How far zao's layout holds, by version and flags, and the commonest places it stops.</summary>
    private static void Holds(StringBuilder said, (TileLayout Layout, TileDefinition Tool)[] walked)
    {
        said.AppendLine("=== how far zao's layout holds - by version and flags byte: files, those walked to their last byte exactly, the rest");
        foreach (var group in walked.GroupBy(one => (one.Layout.Version, one.Layout.Flags)).OrderBy(one => one.Key.Version).ThenBy(one => one.Key.Flags))
        {
            int count = group.Count();
            int whole = group.Count(one => one.Layout.Whole);
            said.Append(CultureInfo.InvariantCulture, $"  version {group.Key.Version} flags 0x{group.Key.Flags:X2} ({Bits(group.Key.Flags)}): {count} files, {whole} whole, {count - whole} not").AppendLine();
        }

        said.AppendLine("  where the others stop, by kind (numbers left out):");
        foreach (var stop in walked.Where(one => !one.Layout.Whole).GroupBy(one => Kind(one.Layout.Stop)).OrderByDescending(one => one.Count()))
        {
            said.Append(CultureInfo.InvariantCulture, $"    {stop.Count()}: {stop.Key}").AppendLine();
        }

        said.AppendLine("  each flags bit, files carrying it (whole of them):");
        for (var bit = 0; bit < 8; bit++)
        {
            int mask = 1 << bit;
            int with = walked.Count(one => (one.Layout.Flags & mask) != 0);
            if (with > 0)
            {
                said.Append(CultureInfo.InvariantCulture, $"    bit {bit} (0x{mask:X2}{BitName(mask)}): {with} ({walked.Count(one => (one.Layout.Flags & mask) != 0 && one.Layout.Whole)})").AppendLine();
            }
        }
    }

    /// <summary>Version 7's leading reference: where it is filled, how many bytes follow it - annalithic's reading has none, zao's the rest.</summary>
    private static void Leads(StringBuilder said, (TileLayout Layout, TileDefinition Tool)[] walked)
    {
        said.AppendLine().AppendLine("=== version 7's leading reference filled (the tool's \"inherits\") - annalithic's Tdt.cs takes such a file to carry nothing else, zao reads on");
        var filled = walked.Where(one => one.Layout.Lead is { Text.Length: > 0 }).ToList();
        said.Append(CultureInfo.InvariantCulture, $"  {filled.Count} files, {filled.Count(one => one.Layout.Whole)} of them whole by zao's layout").AppendLine();
        foreach (var left in filled.GroupBy(one => one.Layout.Length - one.Layout.LeadEnd).OrderBy(one => one.Key))
        {
            said.Append(CultureInfo.InvariantCulture, $"    {left.Key} bytes after the reference: {left.Count()} files (whole by zao's: {left.Count(one => one.Layout.Whole)})").AppendLine();
        }
    }

    /// <summary>zao's ground grid against the one the tool's reader finds by its shape.</summary>
    private static void Grids(StringBuilder said, (TileLayout Layout, TileDefinition Tool)[] walked)
    {
        said.AppendLine().AppendLine("=== the ground grid: zao's, stepped to by the flags, against the one the tool's reader finds by its shape (TileDefinition.GroundGrid)");
        int same = 0, onlyZao = 0, onlyTool = 0, differ = 0, neither = 0;
        for (var one = 0; one < walked.Length; one++)
        {
            (TileLayout layout, TileDefinition tool) = walked[one];
            bool zao = layout.Whole && layout.GroundGrid.Count > 0;
            bool ours = tool.GroundGrid.Count > 0;
            if (zao && ours)
            {
                if (layout.GroundGrid.Select(cell => cell.Text ?? string.Empty).SequenceEqual(tool.GroundGrid, StringComparer.Ordinal))
                {
                    same++;
                }
                else
                {
                    differ++;
                }
            }
            else if (zao)
            {
                onlyZao++;
            }
            else if (ours)
            {
                onlyTool++;
            }
            else
            {
                neither++;
            }
        }

        said.Append(CultureInfo.InvariantCulture,
            $"  both, the same: {same}; both, different: {differ}; zao's alone (the tool's search found none or several): {onlyZao}; the tool's alone (zao's walk not whole or no grid flag): {onlyTool}; neither: {neither}").AppendLine();
    }

    /// <summary>The eight side offsets beside the sides' types, both of zao's readings.</summary>
    private static void Offsets(StringBuilder said, (TileLayout Layout, TileDefinition Tool)[] walked)
    {
        said.AppendLine().AppendLine("=== the eight side offsets, over the whole walks - value: files; then beside the side's edge type (named or not) and corner's ground type");
        said.AppendLine("  zao reads them two ways: game_data.cpp a pair a side ([i] real, [4+i] virtual, none = 3 x the side's length); level_gen.cpp [i] an edge offset, [4+i] a ground offset");
        var good = walked.Where(one => one.Layout.Whole && one.Layout.SideOffsets.Count == 8).Select(one => one.Layout).ToList();
        for (var index = 0; index < 8; index++)
        {
            int side = index % 4;
            said.Append(CultureInfo.InvariantCulture, $"  [{index}]: {Histogram(good.Select(one => one.SideOffsets[index]))}").AppendLine();
            said.Append(CultureInfo.InvariantCulture, $"      edge {side} named: {Histogram(good.Where(one => Named(one.SideEdges, side)).Select(one => one.SideOffsets[index]))}").AppendLine();
            said.Append(CultureInfo.InvariantCulture, $"      edge {side} none:  {Histogram(good.Where(one => !Named(one.SideEdges, side)).Select(one => one.SideOffsets[index]))}").AppendLine();
            if (index >= 4)
            {
                said.Append(CultureInfo.InvariantCulture, $"      ground {side} named: {Histogram(good.Where(one => Named(one.SideGrounds, side)).Select(one => one.SideOffsets[index]))}").AppendLine();
                said.Append(CultureInfo.InvariantCulture, $"      ground {side} none:  {Histogram(good.Where(one => !Named(one.SideGrounds, side)).Select(one => one.SideOffsets[index]))}").AppendLine();
            }

            // zao's sentinel reading: a side's length three times over - sides 0 and 2 run along the width, 1 and 3 along the height
            // in his TileKey (his sides as written: Bottom, Right, Top, Left).
            int at3 = good.Count(one => one.SideOffsets[index] == 3 * (side % 2 == 0 ? one.Width : one.Height));
            said.Append(CultureInfo.InvariantCulture, $"      equal to 3 x the side's length: {at3} of {good.Count}").AppendLine();
        }

        said.Append(CultureInfo.InvariantCulture, $"  zero (zao's zero1): {Histogram(good.Select(one => (int)one.Zero))}").AppendLine();
        said.Append(CultureInfo.InvariantCulture, $"  second size (zao's dim2) against the size: {Histogram(good.Select(one => one.Size2 == (one.Width, one.Height) ? 1 : one.Size2 == (0, 0) ? 0 : 2))} (1 the same, 0 nought, 2 other)").AppendLine();
    }

    /// <summary>What the heights at the end hold.</summary>
    private static void Heights(StringBuilder said, (TileLayout Layout, TileDefinition Tool)[] walked)
    {
        said.AppendLine().AppendLine("=== the heights at the end (zao's tailBytes, flags bit 0) - over the whole walks");
        var with = walked.Where(one => one.Layout.Whole && one.Layout.Heights.Count > 0).Select(one => one.Layout).ToList();
        said.Append(CultureInfo.InvariantCulture, $"  {with.Count} files; their values: {Histogram(with.SelectMany(one => one.Heights.Select(height => (int)height)))}").AppendLine();
        said.Append(CultureInfo.InvariantCulture, $"  files whose heights are not all one value: {with.Count(one => one.Heights.Distinct().Count() > 1)}").AppendLine();
        said.Append(CultureInfo.InvariantCulture, $"  zao's four corners (down-left, down-right, up-right, up-left) not all one: {with.Count(one => one.CornerHeights is { } four && !(four.DownLeft == four.DownRight && four.DownRight == four.UpRight && four.UpRight == four.UpLeft))}").AppendLine();
    }

    /// <summary>The sub-tiles: types, counts against the size, version 4's own templates, the fixed blocks' values.</summary>
    private static void SubTiles(StringBuilder said, (TileLayout Layout, TileDefinition Tool)[] walked)
    {
        said.AppendLine().AppendLine("=== the sub-tiles - over the whole walks");
        var good = walked.Where(one => one.Layout.Whole && one.Layout.Version > 3).Select(one => one.Layout).ToList();
        said.Append(CultureInfo.InvariantCulture, $"  types: {Histogram(good.SelectMany(one => one.SubTiles.Select(sub => (int)sub.Type)), hex: true)}").AppendLine();
        said.Append(CultureInfo.InvariantCulture, $"  values: {Histogram(good.SelectMany(one => one.SubTiles.Select(sub => (int)sub.Value)))}").AppendLine();
        said.Append(CultureInfo.InvariantCulture, $"  count against width x height: {Histogram(good.Select(one => one.SubTiles.Count == one.Width * one.Height ? 1 : one.SubTiles.Count == 0 ? 0 : 2))} (1 one a sub-tile, 0 none, 2 other)").AppendLine();
        var four = good.Where(one => one.Version == 4).ToList();
        said.Append(CultureInfo.InvariantCulture, $"  version 4 files, whose sub-tiles each name a template: {four.Count}; of them with two templates or more: {four.Count(one => one.SubTiles.Select(sub => sub.Template?.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)}").AppendLine();
        said.Append(CultureInfo.InvariantCulture, $"  fixed blocks' values (a byte a cell, {TileLayout.Cells} x {TileLayout.Cells}): {Histogram(good.SelectMany(one => one.SubTiles).Where(sub => sub.Fixed is not null).SelectMany(sub => sub.Fixed!.Select(cell => (int)cell)))}").AppendLine();
        said.Append(CultureInfo.InvariantCulture, $"  counted blocks' lengths: {Histogram(good.SelectMany(one => one.SubTiles).Where(sub => sub.Varying is not null).Select(sub => sub.Varying!.Length))}").AppendLine();
    }

    /// <summary>The fields zao's own reader would refuse though the walk went on.</summary>
    private static void Refusals(StringBuilder said, (TileLayout Layout, TileDefinition Tool)[] walked)
    {
        said.AppendLine().AppendLine("=== where zao's own reader would refuse a file the walk took on - a reference his reader requires naming no string (numbers left out)");
        var refused = walked.SelectMany(one => one.Layout.Refused).GroupBy(Kind).OrderByDescending(one => one.Count()).ToList();
        if (refused.Count == 0)
        {
            said.AppendLine("  (none)");
        }

        foreach (var kind in refused)
        {
            said.Append(CultureInfo.InvariantCulture, $"  {kind.Count()}: {kind.Key}").AppendLine();
        }
    }

    /// <summary>Every edge type, read then whole.</summary>
    private static void Edges(StringBuilder said, IReadOnlyList<(string Path, byte[] Bytes, string From)> edges, EdgeTypeFile[] read)
    {
        said.AppendLine().AppendLine("=== the edge types (.et) - poe_data_tools' grammar (EdgeTypeFile); where a file holds more than the grammar takes, its reading says where it stopped");
        said.Append(CultureInfo.InvariantCulture,
            $"  number lines' bools: {Histogram(read.Where(one => one.Numbers is not null).Select(one => Bools(one.Numbers!).Count))} (bools: files), no number line: {read.Count(one => one.Numbers is null)}; another ground: {read.Count(one => one.OtherGround is not null)}; a wildcard side: {read.Count(one => one.Grounds.Contains(EdgeTypeFile.Wildcard))}")
            .AppendLine();
        said.AppendLine("  where the others stop, by kind (numbers left out):");
        foreach (var stop in read.Where(one => one.Why.Length > 0).GroupBy(one => Kind(one.Why)).OrderByDescending(one => one.Count()))
        {
            said.Append(CultureInfo.InvariantCulture, $"    {stop.Count()}: {stop.Key}").AppendLine();
        }

        said.AppendLine("  every virtual edge and what it stands for:");
        for (var one = 0; one < edges.Count; one++)
        {
            if (read[one].Virtual.Count > 0)
            {
                said.Append(CultureInfo.InvariantCulture,
                    $"    {read[one].Name} ({edges[one].Path}): {string.Join(", ", read[one].Virtual.Select(stands => stands.Edge + " " + (stands.Flag ? 1 : 0)))}; numbers {Rotations(read[one])}").AppendLine();
            }
        }

        for (var one = 0; one < edges.Count; one++)
        {
            EdgeTypeFile file = read[one];
            string numbers = file.Numbers is { } line
                ? string.Create(CultureInfo.InvariantCulture, $"{line.Uint1} {line.Uint2} bools {string.Join(' ', Bools(line).Select(flag => flag ? 1 : 0))}")
                : "-";
            said.Append("##### ").Append(edges[one].Path).Append("  (named by ").Append(edges[one].From.Length > 0 ? edges[one].From : "-").AppendLine(")");
            said.Append(CultureInfo.InvariantCulture,
                $"  read: name {file.Name}{(file.Hex is { } hex ? " #" + hex : string.Empty)}, grounds {string.Join(" | ", file.Grounds)}, numbers {numbers}, other ground {file.OtherGround ?? "-"}, virtual {(file.Virtual.Count > 0 ? string.Join(", ", file.Virtual.Select(stands => stands.Edge)) + " numbers " + Rotations(file) : "no")}{(file.Why.Length > 0 ? " - " + file.Why : string.Empty)}")
                .AppendLine();
            foreach (string text in StatDescriptionFiles.Decode(edges[one].Bytes).ReplaceLineEndings("\n").Split('\n'))
            {
                said.Append("  | ").AppendLine(text.TrimEnd());
            }
        }
    }

    /// <summary>A number line's flags, the first and those written after it.</summary>
    private static List<bool> Bools(EdgeTypeNumbers line)
    {
        List<bool> bools = [line.Bool1];
        foreach (bool? more in (bool?[])[line.Bool2, line.Bool3, line.Bool4])
        {
            if (more is { } flag)
            {
                bools.Add(flag);
            }
        }

        return bools;
    }

    /// <summary>A virtual section's two numbers, or a dash.</summary>
    private static string Rotations(EdgeTypeFile file)
        => file.Rotations is { } two ? string.Create(CultureInfo.InvariantCulture, $"{two.First} {two.Second}") : "-";

    /// <summary>A file written under <see cref="Folder"/> at its install path - false where its path is not one to write.</summary>
    private static bool Beside(string folder, string path, byte[] bytes)
    {
        string[] parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(one => one is "." or ".." || one.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return false;
        }

        string at = Path.Combine([folder, Folder, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(at)!);
        File.WriteAllBytes(at, bytes);
        return true;
    }

    private static bool Named(IReadOnlyList<TileRef> four, int at) => at < four.Count && four[at].Text is { Length: > 0 };

    /// <summary>A sentence with its numbers taken out, so stops and refusals of one kind count together.</summary>
    private static string Kind(string text)
    {
        var kind = new StringBuilder(text.Length);
        var inNumber = false;
        foreach (char letter in text)
        {
            bool digit = char.IsAsciiDigit(letter) || (inNumber && char.IsAsciiHexDigitUpper(letter));
            if (digit)
            {
                if (!inNumber)
                {
                    kind.Append('#');
                }
            }
            else
            {
                kind.Append(letter);
            }

            inNumber = digit;
        }

        return kind.ToString();
    }

    /// <summary>Values and how often each occurs, the commonest first, up to twenty-four.</summary>
    private static string Histogram(IEnumerable<int> values, bool hex = false)
    {
        var counts = new Dictionary<int, int>();
        foreach (int value in values)
        {
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        if (counts.Count == 0)
        {
            return "none";
        }

        var top = counts.OrderByDescending(one => one.Value).ThenBy(one => one.Key).ToList();
        string shown = string.Join(", ", top.Take(24).Select(one => (hex ? "0x" + one.Key.ToString("X2", CultureInfo.InvariantCulture) : one.Key.ToString(CultureInfo.InvariantCulture)) + ":" + one.Value.ToString(CultureInfo.InvariantCulture)));
        return top.Count > 24 ? string.Create(CultureInfo.InvariantCulture, $"{shown} and {top.Count - 24} values more") : shown;
    }

    /// <summary>A block's values, the commonest first.</summary>
    private static string Values(byte[] block) => Histogram(block.Select(one => (int)one));

    /// <summary>The first bytes of a block in hex.</summary>
    private static string Hex(byte[] block, int most)
        => Convert.ToHexString(block, 0, Math.Min(block.Length, most)) + (block.Length > most ? "..." : string.Empty);

    /// <summary>A flags byte's set bits by zao's names.</summary>
    private static string Bits(byte flags)
    {
        var names = new List<string>();
        for (var bit = 0; bit < 8; bit++)
        {
            int mask = 1 << bit;
            if ((flags & mask) != 0)
            {
                names.Add(BitName(mask) is { Length: > 0 } name ? name.TrimStart(',', ' ') : "bit " + bit.ToString(CultureInfo.InvariantCulture));
            }
        }

        return names.Count > 0 ? string.Join(", ", names) : "none";
    }

    private static string BitName(int mask) => mask switch
    {
        TileLayout.FlagHeights => ", heights",
        TileLayout.FlagGrid => ", ground grid",
        TileLayout.FlagUnknown2 => ", zao's unknown bit 2",
        TileLayout.FlagExtra => ", extra block",
        _ => string.Empty,
    };
}
