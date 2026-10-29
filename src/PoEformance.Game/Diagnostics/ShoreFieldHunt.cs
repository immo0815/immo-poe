using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// Looks through the whole of the game's memory for the numbers the engine's shore field is drawn by - the box it covers, its
/// texture's size and the factor that turns its colours into distances - beside every place the scene's planar size is kept.
/// </summary>
/// <remarks>
/// WHAT THE ENGINE READS (v0.1.318's render-debug.txt and water-shore.txt, Port): its two ocean markup views
/// (<c>debug_ocean_markup_deep</c> and <c>_land</c>) and its water read <c>ShorelineFieldData</c>, whose GetShoreData
/// (shoreline_fields) samples <c>shoreline_coord_tex_sampler</c> at <c>(tilemap_pos - shoreline_coord_planar_aabb.xy) /
/// (aabb.zw - aabb.xy)</c>, steps its slopes by <c>scene_planar_size.x / shoreline_coord_tex_size.x</c>, and makes world
/// distances of its colours over <c>dist_to_color</c>: the shore's <c>(1 - r) / dist_to_color</c>, the open sea's
/// <c>(1 - b) / dist_to_color</c>. All four are pass uniforms (common.ffx) the processor sets - a box, a texture's size, a factor
/// and the scene's size - while the texture is made at run time by <c>OceanMarkupField(IDevice*, const int*, vector2_int, vector4,
/// float, bool, float)</c>, on the card. The tool's own field (ShoreField) CHOSE the factor: its <see cref="ShoreField.Reach"/> of
/// 2,000 units a colour unit is a dist_to_color of 0.0005.
///
/// WHAT IS LOOKED FOR: the flood waves read their texture at <c>planar_pos / scene_planar_size.xy</c>, so the scene's planar size is
/// the area's in world units - 250 a tile - and so is predictable from the terrain alone. It is looked for both ways round, and the
/// walkable grid's size in cells beside it; round every place found the hunt reads <see cref="FloatHunt.MostAround"/> bytes either
/// way and lists what stands there: a texture's size (two whole numbers and their inverses), boxes of about the scene's size, pairs
/// of ints in the scene's proportions, and small positive floats - a dist_to_color among them, if the processor keeps one there.
///
/// THE RIVER'S FLOW MAP TOO (v0.1.351): water.ffx's GetFlowSample reads <c>flowmap_tex_sampler</c> at <c>(tilemap_pos -
/// river_flowmap_planar_aabb.xy) / (aabb.zw - aabb.xy)</c>, a texture <c>RiverMarkupField</c> makes at run time from point lines or a grid
/// of bools and its <c>MarkupFlowConstraints</c> - inputs no file names, so the tool draws its rivers at no flow (ShadeProgram.River). A
/// river's box need not be the scene's, so beside the boxes of about the scene's size the hunt lists every box lying inside the scene, a
/// tile or more each way, its corners whole numbers ("inner"), the river's candidates - in an area with a river.
///
/// A DIAGNOSTIC: nothing is concluded here. Every window is written whole as bytes, so the replay (<see cref="Replay"/>) lists the
/// same things from the file alone, and a better rule for what stands beside a place is held against the capture without the game.
/// It reads past the tap, and stops itself at <see cref="StopSeconds"/> - the capture waits two minutes for a part run at the press.
/// </remarks>
public static class ShoreFieldHunt
{
    /// <summary>How long the hunt looks before it stops itself and writes what it found.</summary>
    public const int StopSeconds = 90;

    /// <summary>How many places of each needle have their surroundings read and written whole.</summary>
    public const int MostWindows = 32;

    /// <summary>How many small floats a window lists, the nearest the place first.</summary>
    public const int MostSmall = 24;

    /// <summary>How many of each kind the tallies over every window list.</summary>
    public const int MostTallied = 12;

    /// <summary>The bytes a line of a window's dump holds.</summary>
    private const int BytesALine = 32;

    /// <summary>The smallest and largest whole number taken for a texture's side.</summary>
    private const float LeastSide = 8f, MostSide = 16384f;

    /// <summary>How near two proportions must be to be called the same - texels square, as the engine's one step for both axes asks.</summary>
    private const double SameProportions = 0.02;

    private static readonly Regex Heading = new(
        @"^=== window (\d+) of \d+: (.+) at 0x([0-9A-F]+) \(read from 0x([0-9A-F]+), (\d+) bytes\)$", RegexOptions.CultureInvariant);

    private static readonly Regex Scene = new(
        @"^shore field hunt: scene (\S+) (\S+), cells (\d+) (\d+), tiles (\d+) (\d+)(?:, stored (\d+))?", RegexOptions.CultureInvariant);

    /// <summary>One thing found beside a place: where, of which kind, what it is for the tallies, and how it is said.</summary>
    /// <param name="Offset">Bytes from the place found.</param>
    /// <param name="Kind">texture, whole, box, ints or small.</param>
    /// <param name="Key">What it is, the same for the same numbers in another window.</param>
    /// <param name="Said">Its line.</param>
    public sealed record Sight(int Offset, string Kind, string Key, string Said);

    /// <summary>The area the needles are worked out from: its world size and its cells.</summary>
    /// <param name="TilesX">Tiles across.</param>
    /// <param name="TilesY">Tiles down.</param>
    /// <param name="StoredCellsX">The walkable grid's width as kept, two cells a byte of its row - a cell more than the tiles' where they
    /// make an odd number (Port's 153 tiles, 3519 cells, kept 3520 wide) - or nought where not read.</param>
    public readonly record struct Area(int TilesX, int TilesY, int StoredCellsX = 0)
    {
        /// <summary>The scene's planar width, world units.</summary>
        public float Width => TilesX * ModelFloor.Tile;

        /// <summary>Its height.</summary>
        public float Height => TilesY * ModelFloor.Tile;

        /// <summary>The walkable grid's cells across.</summary>
        public int CellsX => TilesX * TerrainGrid.CellsPerTile;

        /// <summary>Its cells down.</summary>
        public int CellsY => TilesY * TerrainGrid.CellsPerTile;
    }

    /// <summary>One window read back from a report: which needle, where, and its bytes.</summary>
    /// <param name="Number">Its number in the report.</param>
    /// <param name="Needle">The needle's name as written.</param>
    /// <param name="At">Where the needle was found.</param>
    /// <param name="From">The window's first byte.</param>
    /// <param name="Bytes">The window.</param>
    public sealed record Window(int Number, string Needle, ulong At, ulong From, byte[] Bytes);

    /// <summary>A hunt as it runs - the capture window's line and share, and its stop.</summary>
    public sealed class Running
    {
        private long _began;

        /// <summary>The float hunt's own progress, and its stop.</summary>
        public FloatHuntProgress Progress { get; } = new();

        /// <summary>The window's line: where it stands.</summary>
        public string Line
        {
            get
            {
                if (Volatile.Read(ref _began) == 0)
                {
                    return "reading the terrain, then the whole of memory";
                }

                const double Gb = 1024.0 * 1024 * 1024;
                double seconds = Stopwatch.GetElapsedTime(Volatile.Read(ref _began)).TotalSeconds;
                return string.Create(CultureInfo.InvariantCulture,
                    $"the shore field's numbers: {Progress.Bytes / Gb:0.00} GB of the game's memory looked through in {seconds:0} s (it stops itself at {StopSeconds} s) · {Progress.Places} places found");
            }
        }

        /// <summary>How far it is, by bytes or by time, whichever is further.</summary>
        public float Fraction
        {
            get
            {
                long began = Volatile.Read(ref _began);
                if (began == 0)
                {
                    return 0f;
                }

                double byTime = Stopwatch.GetElapsedTime(began).TotalSeconds / StopSeconds;
                double byBytes = Progress.Budget > 0 ? (double)Progress.Bytes / Progress.Budget : 0;
                return (float)Math.Clamp(Math.Max(byTime, byBytes), 0, 1);
            }
        }

        internal void Begin() => Volatile.Write(ref _began, Stopwatch.GetTimestamp());
    }

    /// <summary>What to look for in an area: the scene's planar size both ways round, and the walkable grid's size in cells.</summary>
    public static IReadOnlyList<FloatNeedle> Needles(Area area)
    {
        var needles = new List<FloatNeedle>
        {
            new($"the scene's planar size ({Say(area.Width)}, {Say(area.Height)})", [area.Width, area.Height], 0.5f, FloatHunt.MostAround, MostWindows),
        };

        if (area.TilesX != area.TilesY)
        {
            needles.Add(new($"the scene's planar size turned ({Say(area.Height)}, {Say(area.Width)})", [area.Height, area.Width], 0.5f, FloatHunt.MostAround, MostWindows));
        }

        needles.Add(new($"the walkable grid's size in cells ({area.CellsX}, {area.CellsY})", [area.CellsX, area.CellsY], 0.5f, FloatHunt.MostAround, MostWindows));
        if (area.StoredCellsX > 0 && area.StoredCellsX != area.CellsX)
        {
            needles.Add(new($"the walkable grid's size in cells as kept ({area.StoredCellsX}, {area.CellsY})", [area.StoredCellsX, area.CellsY], 0.5f, FloatHunt.MostAround, MostWindows));
        }

        return needles;
    }

    /// <summary>
    /// The hunt, run once at the press: the area read from the terrain, the whole of memory looked through, its report's lines - or a line
    /// saying why it could not run: no regions, no area, a schema lacking a field, tile counts out of reason.
    /// </summary>
    /// <param name="reader">Past the tap.</param>
    /// <param name="regions">The game's memory regions, or null where this session cannot list them.</param>
    /// <param name="schema">The offsets.</param>
    /// <param name="areaInstance">The area instance the terrain sits in - and where the search starts, nearest first.</param>
    /// <param name="running">Where it says how far it is, and is stopped.</param>
    public static IReadOnlyList<string> Run(IMemoryReader reader, IMemoryRegions? regions, OffsetSchema schema, ulong areaInstance, Running running)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(running);
        var lines = new List<string> { "=== the engine's shore field, its numbers sought in the whole of memory (ShoreFieldHunt)" };
        if (regions is null)
        {
            lines.Add("shore field hunt: nothing in this session can list the game's memory");
            return lines;
        }

        if (!MemoryReaderExtensions.IsPlausiblePointer(areaInstance))
        {
            lines.Add("shore field hunt: no area instance");
            return lines;
        }

        long tilesX, tilesY;
        int bytesPerRow;
        try
        {
            ulong terrainBase = areaInstance + (ulong)schema.Structs["AreaInstance"].OffsetOf("TerrainMetadata");
            StructDef terrain = schema.Structs["TerrainMetadata"];
            tilesX = reader.Read<long>(terrainBase + (ulong)terrain.OffsetOf("TotalTilesX"));
            tilesY = reader.Read<long>(terrainBase + (ulong)terrain.OffsetOf("TotalTilesY"));
            bytesPerRow = reader.Read<int>(terrainBase + (ulong)terrain.OffsetOf("BytesPerRow"));
        }
        catch (KeyNotFoundException missing)
        {
            lines.Add("shore field hunt: the schema lacks " + missing.Message);
            return lines;
        }

        if (tilesX is < 1 or > 4096 || tilesY is < 1 or > 4096)
        {
            lines.Add($"shore field hunt: the terrain's tile counts read {tilesX} by {tilesY}");
            return lines;
        }

        var area = new Area((int)tilesX, (int)tilesY, bytesPerRow is > 0 and < 65536 ? bytesPerRow * 2 : 0);
        IReadOnlyList<FloatNeedle> needles = Needles(area);
        running.Begin();
        FloatHuntResult result;
        using (new Timer(_ => running.Progress.Stop(), null, TimeSpan.FromSeconds(StopSeconds), Timeout.InfiniteTimeSpan))
        {
            result = FloatHunt.Run(reader, regions, needles, areaInstance, running.Progress, budget: 2 * HeapScan.ByteBudget);
        }

        lines.AddRange(Report(area, needles, result));
        return lines;
    }

    /// <summary>A finished hunt said: what was looked for, where it was found, the tallies over every window, and each window whole.</summary>
    public static List<string> Report(Area area, IReadOnlyList<FloatNeedle> needles, FloatHuntResult result)
    {
        ArgumentNullException.ThrowIfNull(needles);
        ArgumentNullException.ThrowIfNull(result);
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"shore field hunt: scene {Say(area.Width)} {Say(area.Height)}, cells {area.CellsX} {area.CellsY}, tiles {area.TilesX} {area.TilesY}, stored {area.StoredCellsX} - the area at {ModelFloor.Tile:0} world units a tile, the walkable grid kept {area.StoredCellsX} wide"),
            "  why: the engine's water and its ocean markup views read the shore field (shoreline_coord_tex_sampler, made at run time by",
            "  OceanMarkupField) through pass uniforms the processor sets - shoreline_coord_planar_aabb (a box), shoreline_coord_tex_size, dist_to_color",
            "  (colours to world distances) and scene_planar_size, the area's size in world units. Beside every place the scene's size or the",
            "  walkable grid's is kept, what stands there is listed; the tool's own field (ShoreField) CHOSE a dist_to_color of 0.0005.",
            "  a river's flow map (flowmap_tex_sampler, made at run time by RiverMarkupField) is read over river_flowmap_planar_aabb, which need",
            "  not be the scene's box: every box inside the scene with whole corners is listed too ('inner') - read them in an area with a river.",
            string.Create(CultureInfo.InvariantCulture,
                $"looked through {result.BytesScanned / (1024.0 * 1024 * 1024):0.00} GB in {result.RegionsWalked} regions in {result.Took.TotalSeconds:0.0} s on {result.Workers} threads, nearest the area instance first{(result.Truncated ? " - stopped at the budget, so what is not found may lie past it" : result.Stopped ? $" - STOPPED (at {StopSeconds} s or by a click), so what is not found may lie past it" : string.Empty)}"),
        };

        var places = new List<ulong>[needles.Count];
        for (var at = 0; at < places.Length; at++)
        {
            places[at] = [];
        }

        foreach (FloatSighting one in result.Sightings)
        {
            if (one.Needle >= 0 && one.Needle < places.Length)
            {
                places[one.Needle].Add(one.At);
            }
        }

        var capped = new HashSet<int>(result.Capped);
        for (var at = 0; at < needles.Count; at++)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {needles[at].Name}: {places[at].Count}{(capped.Contains(at) ? " places, more not kept" : " places")}{(places[at].Count > 0 ? " - " + string.Join(", ", places[at].Take(8).Select(place => $"0x{place:X}")) : string.Empty)}"));
        }

        IReadOnlyList<FloatDump> dumps = result.Dumps ?? [];
        var windows = new List<(Window Window, List<Sight> Sights)>(dumps.Count);
        for (var at = 0; at < dumps.Count; at++)
        {
            FloatDump dump = dumps[at];
            var window = new Window(at + 1, dump.Needle >= 0 && dump.Needle < needles.Count ? needles[dump.Needle].Name : "?", dump.At, dump.From, dump.Bytes);
            windows.Add((window, Beside(window.Bytes, (int)(dump.At - dump.From), area)));
        }

        lines.AddRange(Tallies(windows));
        foreach ((Window window, List<Sight> sights) in windows)
        {
            lines.Add(string.Empty);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"=== window {window.Number} of {windows.Count}: {window.Needle} at 0x{window.At:X} (read from 0x{window.From:X}, {window.Bytes.Length} bytes)"));
            lines.Add($"  beside it, bytes from the place found - {sights.Count} things:");
            lines.AddRange(sights.Select(sight => "    " + sight.Said));
            lines.Add($"  the window's bytes, {BytesALine} a line from its first:");
            for (var offset = 0; offset < window.Bytes.Length; offset += BytesALine)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"    {offset:X4}  {Convert.ToHexStringLower(window.Bytes.AsSpan(offset, Math.Min(BytesALine, window.Bytes.Length - offset)))}"));
            }
        }

        return lines;
    }

    /// <summary>
    /// What stands beside one place: texture sizes, whole floats and ints in the scene's proportions, boxes of about its size, and the small positive floats nearest it.
    /// </summary>
    /// <param name="window">The bytes read round the place.</param>
    /// <param name="place">Where in them the place is.</param>
    /// <param name="area">The area, for the scene's size and proportions.</param>
    public static List<Sight> Beside(ReadOnlySpan<byte> window, int place, Area area)
    {
        var sights = new List<Sight>();
        var small = new List<Sight>();
        float width = area.Width, height = area.Height;
        double aspect = (double)width / height;
        for (var at = 0; at + 4 <= window.Length; at += 4)
        {
            int offset = at - place;
            float a = Float(window, at);
            if (a is > 1e-6f and < 1e-2f)
            {
                small.Add(new Sight(offset, "small", Say(a), string.Create(CultureInfo.InvariantCulture, $"{Offset(offset)}  {a:G6}  (1 / {1f / a:G6})")));
            }

            if (at + 8 > window.Length)
            {
                continue;
            }

            float b = Float(window, at + 4);
            bool four = at + 16 <= window.Length;
            float c = four ? Float(window, at + 8) : float.NaN, d = four ? Float(window, at + 12) : float.NaN;

            // A TEXTURE'S SIZE, as common.ffx's *_tex_size uniforms are read: the two sides, then their inverses.
            if (Side(a) && Side(b) && four && MathF.Abs((c * a) - 1f) < 1e-3f && MathF.Abs((d * b) - 1f) < 1e-3f)
            {
                sights.Add(new Sight(offset, "texture", $"{a:0} x {b:0}", string.Create(CultureInfo.InvariantCulture,
                    $"{Offset(offset)}  a texture's size {a:0} x {b:0} with its inverses{Proportions(a, b, aspect, width)}")));
            }
            else if (Side(a) && Side(b) && Same(a / b, aspect))
            {
                sights.Add(new Sight(offset, "whole", $"{a:0} x {b:0}", string.Create(CultureInfo.InvariantCulture,
                    $"{Offset(offset)}  whole floats {a:0} x {b:0}{Proportions(a, b, aspect, width)}{Known((int)a, (int)b, area)}")));
            }

            // A BOX of about the scene's size, either way round - shoreline_coord_planar_aabb's shape.
            if (four && Box(a, b, c, d, width, height))
            {
                sights.Add(new Sight(offset, "box", $"({Say(a)}, {Say(b)}) to ({Say(c)}, {Say(d)})", string.Create(CultureInfo.InvariantCulture,
                    $"{Offset(offset)}  a box ({Say(a)}, {Say(b)}) to ({Say(c)}, {Say(d)}), {Say(c - a)} x {Say(d - b)}")));
            }
            else if (four && Inner(a, b, c, d, width, height))
            {
                // A BOX INSIDE THE SCENE - river_flowmap_planar_aabb's candidates, a river's own box.
                sights.Add(new Sight(offset, "inner", $"({Say(a)}, {Say(b)}) to ({Say(c)}, {Say(d)})", string.Create(CultureInfo.InvariantCulture,
                    $"{Offset(offset)}  a box inside the scene ({Say(a)}, {Say(b)}) to ({Say(c)}, {Say(d)}), {Say(c - a)} x {Say(d - b)} - {(c - a) / ModelFloor.Tile:0.#} x {(d - b) / ModelFloor.Tile:0.#} tiles")));
            }

            // INTS IN THE SCENE'S PROPORTIONS: a texture's size as the processor keeps it, or the markup grid's (OceanMarkupField's vector2_int).
            int ia = BinaryPrimitives.ReadInt32LittleEndian(window[at..]), ib = BinaryPrimitives.ReadInt32LittleEndian(window[(at + 4)..]);
            if (ia >= LeastSide && ib >= LeastSide && ia <= MostSide && ib <= MostSide && Same((double)ia / ib, aspect))
            {
                sights.Add(new Sight(offset, "ints", $"{ia} x {ib}", string.Create(CultureInfo.InvariantCulture,
                    $"{Offset(offset)}  ints {ia} x {ib}, a texel {width / ia:0.###} units{Known(ia, ib, area)}")));
            }
        }

        sights.AddRange(small.OrderBy(one => Math.Abs(one.Offset)).ThenBy(one => one.Offset).Take(MostSmall));
        if (small.Count > MostSmall)
        {
            sights.Add(new Sight(0, "more", string.Empty, $"... {small.Count - MostSmall} more small floats further off"));
        }

        return sights;
    }

    /// <summary>The tallies over every window: each texture size, box, pair and small float with how many windows hold it, the most first.</summary>
    public static List<string> Tallies(IReadOnlyList<(Window Window, List<Sight> Sights)> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var lines = new List<string> { $"tallies over the {windows.Count} windows read - what stands beside several places is the likelier:" };
        foreach ((string kind, string said) in (ReadOnlySpan<(string, string)>)
        [
            ("texture", "texture sizes (two whole numbers and their inverses)"),
            ("box", "boxes of about the scene's size"),
            ("inner", "boxes inside the scene, whole corners (a river's flow map's?)"),
            ("ints", "ints in the scene's proportions"),
            ("whole", "whole floats in the scene's proportions"),
            ("small", "small positive floats (a dist_to_color among them?)"),
        ])
        {
            List<(string Key, int Windows)> counted = [.. windows
                .SelectMany(window => window.Sights.Where(sight => sight.Kind == kind).Select(sight => sight.Key).Distinct())
                .GroupBy(key => key, StringComparer.Ordinal)
                .Select(same => (same.Key, same.Count()))
                .OrderByDescending(one => one.Item2)
                .ThenBy(one => one.Key, StringComparer.Ordinal)];
            lines.Add(counted.Count == 0
                ? $"  {said}: none"
                : $"  {said}: " + string.Join("; ", counted.Take(MostTallied).Select(one => $"{one.Key} in {one.Windows}")) + (counted.Count > MostTallied ? $"; {counted.Count - MostTallied} more" : string.Empty));
        }

        return lines;
    }

    /// <summary>The windows of a report read back, with the area its first line names - or none where it names none.</summary>
    public static (Area? Area, IReadOnlyList<Window> Windows) Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Area? area = null;
        var windows = new List<Window>();
        Window? open = null;
        var bytes = new List<byte>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (Scene.Match(line) is { Success: true } scene)
            {
                area = new Area(
                    int.Parse(scene.Groups[5].Value, CultureInfo.InvariantCulture),
                    int.Parse(scene.Groups[6].Value, CultureInfo.InvariantCulture),
                    scene.Groups[7].Success ? int.Parse(scene.Groups[7].Value, CultureInfo.InvariantCulture) : 0);
                continue;
            }

            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                Close();
                if (Heading.Match(line) is { Success: true } heading)
                {
                    open = new Window(
                        int.Parse(heading.Groups[1].Value, CultureInfo.InvariantCulture),
                        heading.Groups[2].Value,
                        ulong.Parse(heading.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        ulong.Parse(heading.Groups[4].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        new byte[int.Parse(heading.Groups[5].Value, CultureInfo.InvariantCulture)]);
                }

                continue;
            }

            // A DUMP LINE: four spaces, the offset in hex, two spaces, the bytes in hex.
            if (open is not null && line.Length > 10 && line.StartsWith("    ", StringComparison.Ordinal) && line[8..10] == "  "
                && int.TryParse(line.AsSpan(4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int offset) && offset == bytes.Count)
            {
                try
                {
                    bytes.AddRange(Convert.FromHexString(line.AsSpan(10)));
                }
                catch (FormatException)
                {
                    // Not a dump line after all - a sight's line that happened to start alike.
                }
            }
        }

        Close();
        return (area, windows);

        void Close()
        {
            if (open is not null && bytes.Count == open.Bytes.Length)
            {
                bytes.CopyTo(open.Bytes);
                windows.Add(open);
            }

            open = null;
            bytes.Clear();
        }
    }

    /// <summary>A report's windows read back and listed again by today's rules - shore-field-hunt-replay's part of ocean-markup-replay.txt.</summary>
    public static string Replay(string text)
    {
        (Area? area, IReadOnlyList<Window> windows) = Read(text);
        var said = new StringBuilder();
        if (area is not { } known)
        {
            said.AppendLine("shore field hunt replay: the report names no area - no hunt in it, or one that did not run");
            return said.ToString();
        }

        var seen = windows.Select(window => (window, Beside(window.Bytes, (int)(window.At - window.From), known))).ToList();
        said.Append(CultureInfo.InvariantCulture,
            $"shore field hunt replay: {windows.Count} windows read back, the scene {Say(known.Width)} x {Say(known.Height)}").AppendLine();
        foreach (string line in Tallies(seen))
        {
            said.AppendLine(line);
        }

        foreach ((Window window, List<Sight> sights) in seen)
        {
            said.Append(CultureInfo.InvariantCulture, $"  window {window.Number}: {window.Needle} at 0x{window.At:X}, {sights.Count} things").AppendLine();
            foreach (Sight sight in sights.Where(one => one.Kind is "texture" or "box" or "inner" or "ints"))
            {
                said.Append("    ").AppendLine(sight.Said);
            }
        }

        return said.ToString();
    }

    private static float Float(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]);

    private static bool Side(float value) => value is >= LeastSide and <= MostSide && value == MathF.Round(value);

    private static bool Same(double proportion, double aspect) => Math.Abs((proportion / aspect) - 1.0) < SameProportions;

    /// <summary>Whether four floats are a box of about the scene's size, either way round, not far from it.</summary>
    private static bool Box(float a, float b, float c, float d, float width, float height)
    {
        if (!float.IsFinite(a) || !float.IsFinite(b) || !float.IsFinite(c) || !float.IsFinite(d) || c <= a || d <= b)
        {
            return false;
        }

        float far = 2f * MathF.Max(width, height), across = c - a, down = d - b;
        bool sized = (Within(across, width) && Within(down, height)) || (Within(across, height) && Within(down, width));
        return sized && MathF.Abs(a) <= far && MathF.Abs(b) <= far;

        static bool Within(float value, float of) => value >= 0.25f * of && value <= 2f * of;
    }

    /// <summary>
    /// Whether four floats are a box inside the scene, a tile or more each way, its corners whole numbers and within a tile of the scene
    /// - a river's flow map's box (river_flowmap_planar_aabb), which need not be the scene's.
    /// </summary>
    private static bool Inner(float a, float b, float c, float d, float width, float height)
    {
        if (!float.IsFinite(a) || !float.IsFinite(b) || !float.IsFinite(c) || !float.IsFinite(d) || c - a < ModelFloor.Tile || d - b < ModelFloor.Tile)
        {
            return false;
        }

        float margin = ModelFloor.Tile;
        bool inside = a >= -margin && b >= -margin && c <= width + margin && d <= height + margin;
        return inside && a == MathF.Round(a) && b == MathF.Round(b) && c == MathF.Round(c) && d == MathF.Round(d);
    }

    private static string Proportions(float a, float b, double aspect, float width) => Same(a / b, aspect)
        ? string.Create(CultureInfo.InvariantCulture, $", the scene's proportions - a texel {width / a:0.###} units")
        : string.Create(CultureInfo.InvariantCulture, $", not the scene's proportions ({a / b:0.###} against {aspect:0.###})");

    /// <summary>Which of the area's own grids a pair of numbers is, where it is one.</summary>
    private static string Known(int a, int b, Area area)
    {
        if ((a == area.CellsX || a == area.StoredCellsX) && b == area.CellsY)
        {
            return a == area.CellsX ? " = the walkable grid's cells" : " = the walkable grid's cells as kept";
        }

        return a % area.TilesX == 0 && b == area.TilesY * (a / area.TilesX)
            ? string.Create(CultureInfo.InvariantCulture, $" = the tiles x{a / area.TilesX}")
            : string.Empty;
    }

    private static string Offset(int offset) => offset >= 0
        ? string.Create(CultureInfo.InvariantCulture, $"+0x{offset:X}")
        : string.Create(CultureInfo.InvariantCulture, $"-0x{-offset:X}");

    private static string Say(float value) => value.ToString("G7", CultureInfo.InvariantCulture);
}
