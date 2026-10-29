using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// Looks through the whole of the game's memory for the grid the engine's river flow map is made from, or the map itself: lists whose
/// length is the river box's texels, its corners or its cells times a texel's bytes, and the box's size as ints beside a pointer.
/// </summary>
/// <remarks>
/// WHAT IS KNOWN (v0.1.351's The Red Vale capture, the schema's WaterPassBlock): the processor keeps the water pass's numbers in a block
/// found by the scene's planar size, and in a river area one of its four boxes - river_flowmap_planar_aabb by common.ffx's order - holds the
/// river's: (-16.3, -16.3) to (14983.7, 15962.0) there, 276 x 294 texels of 54.35 units (1250 / 23, five cells), while Crimson Shores, which
/// has no river, holds (0, 0) to (1, 1). What the map holds - GetFlowSample's velocity in xy and a distance in w - is made at run time by
/// <c>RiverMarkupField(IDevice*, const PointLine*, const MarkupFlowConstraints*, vector2_int, vector4, float, bool, float)</c> or its twin over
/// a grid of bools; no shader of the install's 197 builds it, so the processor likely does, and a grid of the box's size may stand in its
/// memory - the input or the made map before its upload.
///
/// WHAT IS LOOKED FOR: the block again (the scene's size, nearest the area instance first), each of its boxes but the shoreline's as a grid
/// of 54.35-unit texels, then every 8-byte pair in memory whose difference is such a grid's length - a std::vector's begin and end - for
/// texels of 1, 2, 4, 8, 12 or 16 bytes, at the texels, their corners or the cells under them; and every pair of ints that is a grid's size.
/// Each list found is read and set out as every way its texels could be read (bytes, halves, floats, one to four channels), each way's
/// numbers and how alike neighbouring texels are, the likeliest drawn as a map; its bytes are written beside the report
/// (<c>river-flow-N.bin</c>) so the replay reads them again (<see cref="Replay"/>).
///
/// FOUND (v0.1.352's The Red Vale capture): list 1, four floats a texel, is flowmap_tex_sampler's contents (the schema's RiverFlowField) -
/// so a list laid out as it is named a flow field (<see cref="FlowMap"/>; the guiding fog's is laid out the same) and its way home sought
/// (<see cref="Home"/>), what a live read needs.
///
/// A DIAGNOSTIC: nothing is concluded here. It reads past the tap and stops itself at <see cref="BlockSeconds"/>, then
/// <see cref="SweepSeconds"/>, then <see cref="LevelSeconds"/> a level of the way home; the capture's window says how far it is and stops
/// it on a click, keeping what it found.
/// </remarks>
public static partial class RiverFlowHunt
{
    /// <summary>How long the block is looked for before the sweep starts with what was found.</summary>
    public const int BlockSeconds = 20;

    /// <summary>How long the sweep for the grid looks before it stops itself and writes what it found.</summary>
    public const int SweepSeconds = 75;

    /// <summary>A flow map's texel in world units - five cells, MEASURED in both areas' blocks (the schema's WaterPassBlock).</summary>
    public const float Texel = 1250f / 23f;

    /// <summary>Cells a texel spans.</summary>
    public const int CellsPerTexel = 5;

    /// <summary>The most places of each kind kept.</summary>
    public const int MostHits = 64;

    /// <summary>The most lists read and set out.</summary>
    public const int MostRead = 12;

    /// <summary>The most bytes read of one list.</summary>
    public const int MostBytes = 16 * 1024 * 1024;

    /// <summary>The widest map written, in characters.</summary>
    public const int MapColumns = 96;

    /// <summary>The bytes a texel may take: a bool or byte, a half or two bytes, a float, two floats or four halves, three floats, four.</summary>
    private static readonly int[] Elements = [1, 2, 4, 8, 12, 16];

    /// <summary>The block's four boxes, by common.ffx's order - see the schema's WaterPassBlock.</summary>
    private static readonly (string Name, int At)[] Slots =
        [("fog flow map", 0x84), ("guiding fog", 0x94), ("river flow map", 0xA4), ("shoreline", ShorelineSlot)];

    /// <summary>The block's shoreline box, the fingerprint a place is the block by - see <see cref="Grids"/>.</summary>
    private const int ShorelineSlot = 0xB4;

    private static readonly Regex Candidate = new(
        @"^=== list (\d+): .* the (.+) grid's (texels|corners|cells) (\d+) x (\d+), (\d+) bytes a texel - written to (river-flow-\d+\.bin) \((\d+) bytes\)",
        RegexOptions.CultureInvariant);

    /// <summary>A box of the block made a grid: its slot, its corners and its texels across and down.</summary>
    /// <param name="Slot">Which of the block's boxes.</param>
    /// <param name="X0">Its least x.</param>
    /// <param name="Y0">Its least y.</param>
    /// <param name="X1">Its greatest x.</param>
    /// <param name="Y1">Its greatest y.</param>
    public sealed record Grid(string Slot, float X0, float Y0, float X1, float Y1)
    {
        /// <summary>Texels across.</summary>
        public int Width => (int)MathF.Round((X1 - X0) / Texel);

        /// <summary>Texels down.</summary>
        public int Height => (int)MathF.Round((Y1 - Y0) / Texel);
    }

    /// <summary>A length a list may have: which grid, laid how (texels, corners, cells), and the bytes a texel takes.</summary>
    /// <param name="Grid">The grid's index.</param>
    /// <param name="Laid">texels, corners or cells.</param>
    /// <param name="Width">Across.</param>
    /// <param name="Height">Down.</param>
    /// <param name="Element">Bytes a texel.</param>
    public readonly record struct Shape(int Grid, string Laid, int Width, int Height, int Element)
    {
        /// <summary>The list's length in bytes.</summary>
        public long Bytes => (long)Width * Height * Element;
    }

    /// <summary>One place found: a list whose length is a shape's, or a grid's size as two ints.</summary>
    /// <param name="Kind">list or size.</param>
    /// <param name="At">Where the begin and end, or the two ints, stand.</param>
    /// <param name="Data">The list's first byte, or nought.</param>
    /// <param name="Shape">The shape it matched.</param>
    public readonly record struct Hit(string Kind, ulong At, ulong Data, Shape Shape);

    /// <summary>A hunt as it runs - the capture window's line and share, and its stop.</summary>
    public sealed class Running
    {
        private long _began;
        private long _swept;
        private long _budget;
        private int _phase;
        private int _stop;
        private int _level;

        /// <summary>The block's float hunt's progress, and its stop.</summary>
        public FloatHuntProgress Block { get; } = new();

        /// <summary>Whether it was asked to stop.</summary>
        public bool Stopping => Volatile.Read(ref _stop) != 0;

        /// <summary>Asks it to stop, keeping what it found.</summary>
        public void Stop()
        {
            Volatile.Write(ref _stop, 1);
            Block.Stop();
        }

        /// <summary>The window's line: where it stands.</summary>
        public string Line
        {
            get
            {
                const double Gb = 1024.0 * 1024 * 1024;
                double seconds = Volatile.Read(ref _began) == 0 ? 0 : Stopwatch.GetElapsedTime(Volatile.Read(ref _began)).TotalSeconds;
                return Volatile.Read(ref _phase) switch
                {
                    0 => "reading the terrain",
                    1 => string.Create(CultureInfo.InvariantCulture, $"finding the water pass's block: {Block.Places} places in {Block.Bytes / Gb:0.00} GB, {seconds:0} s"),
                    2 => string.Create(CultureInfo.InvariantCulture, $"the river's grid in the whole of memory: {Volatile.Read(ref _swept) / Gb:0.00} GB looked through, {seconds:0} s (it stops itself at {SweepSeconds} s)"),
                    3 => "reading the lists found",
                    _ => string.Create(CultureInfo.InvariantCulture,
                        $"the flow fields' way home, level {Volatile.Read(ref _level)} of {MostLevels}: {Volatile.Read(ref _swept) / Gb:0.00} GB looked through, {seconds:0} s (a level stops itself at {LevelSeconds} s)"),
                };
            }
        }

        /// <summary>How far it is, by phase and time.</summary>
        public float Fraction
        {
            get
            {
                long began = Volatile.Read(ref _began);
                double seconds = began == 0 ? 0 : Stopwatch.GetElapsedTime(began).TotalSeconds;
                double share = Math.Clamp(Math.Max(
                    seconds / (Volatile.Read(ref _phase) == 2 ? SweepSeconds : LevelSeconds),
                    Volatile.Read(ref _budget) > 0 ? (double)Volatile.Read(ref _swept) / Volatile.Read(ref _budget) : 0), 0, 1);
                return Volatile.Read(ref _phase) switch
                {
                    0 => 0f,
                    1 => (float)Math.Clamp(seconds / BlockSeconds * 0.15, 0, 0.15),
                    2 => 0.15f + (0.4f * (float)share),
                    3 => 0.55f,
                    _ => 0.6f + (0.4f * (float)((Math.Max(0, Volatile.Read(ref _level) - 1) + share) / MostLevels)),
                };
            }
        }

        /// <summary>A phase begun: its clock and its bytes start again.</summary>
        internal void Phase(int phase)
        {
            Volatile.Write(ref _began, Stopwatch.GetTimestamp());
            Volatile.Write(ref _swept, 0);
            Volatile.Write(ref _phase, phase);
        }

        /// <summary>A level of the way home begun.</summary>
        internal void Level(int level, long budget)
        {
            Volatile.Write(ref _level, level);
            Budgeted(budget);
            Phase(4);
        }

        internal void Budgeted(long budget) => Volatile.Write(ref _budget, budget);

        internal void Swept(long bytes) => Interlocked.Add(ref _swept, bytes);
    }

    /// <summary>
    /// The hunt, run once at the press: the report's lines, its lists' bytes written into <paramref name="folder"/> - or a line saying
    /// why it could not run.
    /// </summary>
    /// <param name="reader">Past the tap.</param>
    /// <param name="regions">The game's memory regions, or null where this session cannot list them.</param>
    /// <param name="schema">The offsets.</param>
    /// <param name="areaInstance">The area instance - the terrain's, and where the search starts, nearest first.</param>
    /// <param name="running">Where it says how far it is, and is stopped.</param>
    /// <param name="folder">Where the lists' bytes are written, or null to write none.</param>
    public static IReadOnlyList<string> Run(
        IMemoryReader reader, IMemoryRegions? regions, OffsetSchema schema, ulong areaInstance, Running running, string? folder)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(running);
        var lines = new List<string> { "=== the river's flow map, its grid sought in the whole of memory (RiverFlowHunt)" };
        if (regions is null)
        {
            lines.Add("river flow hunt: nothing in this session can list the game's memory");
            return lines;
        }

        if (!MemoryReaderExtensions.IsPlausiblePointer(areaInstance))
        {
            lines.Add("river flow hunt: no area instance");
            return lines;
        }

        if (ShoreFieldHunt.AreaOf(reader, schema, areaInstance, out string why) is not { } area)
        {
            lines.Add("river flow hunt: " + why);
            return lines;
        }

        // THE BLOCK, nearest the area instance first, stopped once a few copies are in.
        running.Phase(1);
        var needle = new FloatNeedle($"the scene's planar size ({area.Width}, {area.Height})", [area.Width, area.Height], 0.5f, 0x100, 16);
        var blockClock = Stopwatch.StartNew();
        FloatHuntResult blocks;
        using (new Timer(
            _ =>
            {
                if (running.Block.Places >= 16 || blockClock.Elapsed.TotalSeconds >= BlockSeconds)
                {
                    running.Block.Stop();
                }
            },
            null,
            200,
            200))
        {
            blocks = FloatHunt.Run(reader, regions, [needle], areaInstance, running.Block, budget: 2 * HeapScan.ByteBudget);
        }

        IReadOnlyList<Grid> grids = Grids(blocks.Dumps ?? [], area.Width, area.Height, out List<string> boxLines);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"river flow hunt: scene {area.Width:0} {area.Height:0}, tiles {area.TilesX} {area.TilesY} - the block found at {blocks.Sightings.Count} places in {blocks.Took.TotalSeconds:0.0} s, {blocks.Dumps?.Count ?? 0} read"));
        lines.AddRange(boxLines);
        if (grids.Count == 0)
        {
            lines.Add("  no box of the block but the shoreline's holds a grid - no flow map in this area (Crimson Shores' river slot is (0, 0) to (1, 1))");
            return lines;
        }

        // THE SWEEP for the grid's lists and sizes.
        IReadOnlyList<Shape> shapes = Shapes(grids);
        running.Phase(2);
        long budget = 2 * HeapScan.ByteBudget;
        running.Budgeted(budget);
        (List<Hit> hits, Walked walked) = Sweep(reader, regions, areaInstance, shapes, running, budget);
        lines.Add("  " + walked.Said("the area instance"));
        lines.Add("  the lengths looked for: " + string.Join("; ", shapes.Select(shape => Said(shape, grids))));
        List<Hit> lists = [.. hits.Where(hit => hit.Kind == "list")];
        List<Hit> sizes = [.. hits.Where(hit => hit.Kind == "size")];
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {lists.Count} lists of those lengths, {sizes.Count} grid sizes as two ints"));
        foreach (Hit size in sizes.Take(MostHits))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"    ints {size.Shape.Width} x {size.Shape.Height} (the {grids[size.Shape.Grid].Slot} grid's {size.Shape.Laid}) at 0x{size.At:X}{Around(reader, size.At)}"));
        }

        // EACH LIST READ AND SET OUT, its bytes written beside the report.
        running.Phase(3);
        var number = 0;
        var flowMaps = new List<(ulong Holder, string Name)>();
        foreach (Hit list in lists.GroupBy(hit => hit.Data).Select(same => same.First()).Take(MostRead))
        {
            number++;
            int length = (int)Math.Min(list.Shape.Bytes, MostBytes);
            var bytes = new byte[length];
            if (!reader.TryRead(list.Data, bytes))
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"=== list {number}: at 0x{list.Data:X} did not read"));
                continue;
            }

            string file = string.Create(CultureInfo.InvariantCulture, $"river-flow-{number}.bin");
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, file), bytes);
            }

            lines.Add(string.Empty);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"=== list {number}: a vector of {list.Shape.Bytes} bytes at 0x{list.Data:X}, its begin and end at 0x{list.At:X} - the {grids[list.Shape.Grid].Slot} grid's {list.Shape.Laid} {list.Shape.Width} x {list.Shape.Height}, {list.Shape.Element} bytes a texel - written to {file} ({length} bytes)"));
            lines.AddRange(SetOut(bytes, list.Shape.Width, list.Shape.Height, list.Shape.Element));
            if (list.Shape.Laid == "texels" && FlowMap(bytes, list.Shape.Width, list.Shape.Height, list.Shape.Element) is { } flow)
            {
                lines.AddRange(flow.Lines);
                if (flow.Is)
                {
                    flowMaps.Add((list.At, string.Create(CultureInfo.InvariantCulture, $"list {number}'s holder")));
                }
            }
        }

        // THEIR WAY HOME: who points at each flow field's holder, up to a static or the area instance - what a live read needs.
        lines.Add(string.Empty);
        lines.AddRange(Home(reader, regions, areaInstance, flowMaps, running));
        return lines;
    }

    /// <summary>
    /// The grids the blocks' boxes make - every box but the shoreline's, inside reason and not the empty (0, 0) to (1, 1) - and a line a box.
    /// </summary>
    /// <remarks>
    /// A PLACE IS THE BLOCK WHERE ITS SHORELINE BOX IS THE SCENE A QUARTER TILE WIDER ALL ROUND - MEASURED in both areas' blocks (the schema's
    /// WaterPassBlock): the scene's size stands in other places too, whose bytes at the slots are anything.
    /// </remarks>
    public static IReadOnlyList<Grid> Grids(IReadOnlyList<FloatDump> dumps, float sceneWidth, float sceneHeight, out List<string> lines)
    {
        ArgumentNullException.ThrowIfNull(dumps);
        lines = [];
        var grids = new List<Grid>();
        var said = new HashSet<string>(StringComparer.Ordinal);
        var blocks = 0;
        foreach (FloatDump dump in dumps)
        {
            int place = (int)(dump.At - dump.From);
            if (place + ShorelineSlot + 16 > dump.Bytes.Length || !Widened(dump.Bytes.AsSpan(place + ShorelineSlot, 16), sceneWidth, sceneHeight))
            {
                continue;
            }

            blocks++;
            foreach ((string slot, int at) in Slots)
            {
                if (place + at + 16 > dump.Bytes.Length)
                {
                    continue;
                }

                ReadOnlySpan<byte> box = dump.Bytes.AsSpan(place + at, 16);
                float x0 = BinaryPrimitives.ReadSingleLittleEndian(box), y0 = BinaryPrimitives.ReadSingleLittleEndian(box[4..]);
                float x1 = BinaryPrimitives.ReadSingleLittleEndian(box[8..]), y1 = BinaryPrimitives.ReadSingleLittleEndian(box[12..]);
                string line = string.Create(CultureInfo.InvariantCulture, $"  {slot} (+0x{at:X}): ({x0:G7}, {y0:G7}) to ({x1:G7}, {y1:G7})");
                if (!said.Add(line))
                {
                    continue;
                }

                bool real = float.IsFinite(x0) && float.IsFinite(y0) && float.IsFinite(x1) && float.IsFinite(y1)
                    && x1 - x0 >= Texel && y1 - y0 >= Texel && x1 - x0 < 1e6f && y1 - y0 < 1e6f;
                var grid = new Grid(slot, x0, y0, x1, y1);
                int same = grids.FindIndex(one => one with { Slot = slot } == grid);
                if (!real)
                {
                    line += " - empty";
                }
                else if (slot == "shoreline")
                {
                    line += " - the shoreline's, not looked for";
                }
                else if (same >= 0)
                {
                    // ONE GRID, NAMED BY EVERY SLOT HOLDING IT - The Red Vale's guiding fog holds the river's box.
                    line += " - the same box as the " + grids[same].Slot;
                    grids[same] = grids[same] with { Slot = grids[same].Slot + " and " + slot };
                }
                else
                {
                    grids.Add(grid);
                    line += string.Create(CultureInfo.InvariantCulture, $" - {grid.Width} x {grid.Height} texels of {Texel:0.####}, looked for");
                }

                lines.Add(line);
            }
        }

        lines.Insert(0, string.Create(CultureInfo.InvariantCulture,
            $"  {blocks} of the {dumps.Count} places read are the block - their shoreline box the scene a quarter tile wider all round; their boxes:"));
        return grids;
    }

    /// <summary>Whether a box is the scene a quarter tile wider all round, to half a unit.</summary>
    private static bool Widened(ReadOnlySpan<byte> box, float width, float height)
    {
        const float Quarter = 62.5f;
        return MathF.Abs(BinaryPrimitives.ReadSingleLittleEndian(box) + Quarter) < 0.5f
            && MathF.Abs(BinaryPrimitives.ReadSingleLittleEndian(box[4..]) + Quarter) < 0.5f
            && MathF.Abs(BinaryPrimitives.ReadSingleLittleEndian(box[8..]) - (width + Quarter)) < 0.5f
            && MathF.Abs(BinaryPrimitives.ReadSingleLittleEndian(box[12..]) - (height + Quarter)) < 0.5f;
    }

    /// <summary>Every length a grid's list may have: its texels, their corners and the cells under them, at every texel's size.</summary>
    public static IReadOnlyList<Shape> Shapes(IReadOnlyList<Grid> grids)
    {
        ArgumentNullException.ThrowIfNull(grids);
        var shapes = new List<Shape>();
        for (var at = 0; at < grids.Count; at++)
        {
            Grid grid = grids[at];
            foreach (int element in Elements)
            {
                shapes.Add(new Shape(at, "texels", grid.Width, grid.Height, element));
                shapes.Add(new Shape(at, "corners", grid.Width + 1, grid.Height + 1, element));
            }

            foreach (int element in (ReadOnlySpan<int>)[1, 2, 4])
            {
                shapes.Add(new Shape(at, "cells", grid.Width * CellsPerTexel, grid.Height * CellsPerTexel, element));
            }
        }

        return [.. shapes.DistinctBy(shape => (shape.Bytes, shape.Width, shape.Height))];
    }

    /// <summary>
    /// The lists and sizes in one chunk: every 8-byte pair whose difference is a shape's length and whose first is a pointer, and every pair
    /// of ints that is a grid's size, either way round.
    /// </summary>
    public static void Scan(ReadOnlySpan<byte> chunk, ulong address, IReadOnlyDictionary<long, Shape> lengths, long least, long most,
        IReadOnlyDictionary<(int, int), Shape> sizes, List<Hit> found)
    {
        ArgumentNullException.ThrowIfNull(lengths);
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(found);
        for (var at = 0; at + 16 <= chunk.Length; at += 8)
        {
            ulong begin = BinaryPrimitives.ReadUInt64LittleEndian(chunk[at..]);
            ulong end = BinaryPrimitives.ReadUInt64LittleEndian(chunk[(at + 8)..]);
            if (end <= begin)
            {
                continue;
            }

            ulong span = end - begin;
            if (span < (ulong)least || span > (ulong)most || !lengths.TryGetValue((long)span, out Shape shape) || !MemoryReaderExtensions.IsPlausiblePointer(begin))
            {
                continue;
            }

            found.Add(new Hit("list", address + (ulong)at, begin, shape));
        }

        for (var at = 0; at + 8 <= chunk.Length; at += 4)
        {
            int a = BinaryPrimitives.ReadInt32LittleEndian(chunk[at..]);
            if (a is < 8 or > 65536)
            {
                continue;
            }

            int b = BinaryPrimitives.ReadInt32LittleEndian(chunk[(at + 4)..]);
            if (sizes.TryGetValue((a, b), out Shape shape))
            {
                found.Add(new Hit("size", address + (ulong)at, 0, shape));
            }
        }
    }

    /// <summary>The whole of memory looked through for the shapes, on every core, nearest the anchor first.</summary>
    private static (List<Hit> Hits, Walked Walked) Sweep(
        IMemoryReader reader, IMemoryRegions regions, ulong anchor, IReadOnlyList<Shape> shapes, Running running, long budget)
    {
        var lengths = new Dictionary<long, Shape>();
        var sizes = new Dictionary<(int, int), Shape>();
        foreach (Shape shape in shapes)
        {
            lengths.TryAdd(shape.Bytes, shape);
            sizes.TryAdd((shape.Width, shape.Height), shape with { Element = 0 });
            sizes.TryAdd((shape.Height, shape.Width), shape with { Element = 0 });
        }

        long least = lengths.Keys.Min(), most = lengths.Keys.Max();
        (List<Hit> hits, Walked walked) = Walk<Hit>(
            reader, regions, anchor, running, budget, SweepSeconds, (chunk, address, found) => Scan(chunk, address, lengths, least, most, sizes, found));

        // THE NEAREST FIRST and the same place once, each kind to its cap.
        List<Hit> kept = [.. hits.DistinctBy(hit => (hit.Kind, hit.At))
            .OrderBy(hit => hit.At > anchor ? hit.At - anchor : anchor - hit.At)
            .GroupBy(hit => hit.Kind)
            .SelectMany(kind => kind.Take(MostHits))];
        return (kept, walked);
    }

    /// <summary>What a walk through memory looked through: its bytes, regions, time, whether it stopped short, and what it left out.</summary>
    /// <param name="Swept">Bytes read.</param>
    /// <param name="Regions">Regions begun.</param>
    /// <param name="Took">How long.</param>
    /// <param name="Cut">Whether it stopped before the end (the budget, the time or a click).</param>
    /// <param name="Unwritable">Bytes of regions left out as not writable.</param>
    /// <param name="Driver">Bytes of regions left out as write-combined or uncached.</param>
    private readonly record struct Walked(long Swept, int Regions, TimeSpan Took, bool Cut, long Unwritable, long Driver)
    {
        public string Said(string from) => string.Create(CultureInfo.InvariantCulture,
            $"looked through {Swept / (1024.0 * 1024 * 1024):0.00} GB in {Regions} regions in {Took.TotalSeconds:0.0} s, nearest {from} first, leaving out {Unwritable / (1024.0 * 1024 * 1024):0.00} GB not writable and {Driver / (1024.0 * 1024 * 1024):0.00} GB write-combined or uncached{(Cut ? " - STOPPED (by the budget, the time or a click), so what is not found may lie past it" : string.Empty)}");
    }

    /// <summary>How a chunk of memory is searched: its bytes, where they stand, and the list to add to.</summary>
    private delegate void ChunkScan<T>(ReadOnlySpan<byte> chunk, ulong address, List<T> found);

    /// <summary>
    /// Every writable region of the game's memory read in chunks on every core, nearest the anchor first, each chunk handed to
    /// <paramref name="scan"/> with eight bytes past it so a pair across its end is seen; stopped by the budget, the time or a click.
    /// </summary>
    /// <remarks>
    /// WRITABLE MEMORY ALONE, AND NOT THE DRIVER'S: a list's begin and end, a grid's size and a pointer to an object are written by the
    /// game, so they stand in memory it may write; and write-combined or uncached memory is the driver's upload heaps, where light-hunt.txt
    /// (v0.1.208) found most of a sweep's time going at a hundredth of a gigabyte a second. A region nobody asked the protection of (a
    /// recording, a test) is read.
    /// </remarks>
    private static (List<T> Found, Walked Walked) Walk<T>(
        IMemoryReader reader, IMemoryRegions regions, ulong anchor, Running running, long budget, int seconds, ChunkScan<T> scan)
    {
        var clock = Stopwatch.StartNew();
        long unwritable = 0, driver = 0;
        var searched = new List<MemoryRegion>();
        foreach (MemoryRegion region in regions.Regions())
        {
            if (region.Size > HeapScan.LargestRegion)
            {
                continue;
            }

            if (region.Protect != 0 && (region.Protect & 0x600) != 0)
            {
                driver += (long)region.Size;
            }
            else if (region.Protect != 0 && (region.Protect & 0xFF) is not (0x04 or 0x08 or 0x40 or 0x80))
            {
                unwritable += (long)region.Size;
            }
            else
            {
                searched.Add(region);
            }
        }

        MemoryRegion[] ordered = [.. searched
            .OrderBy(region => anchor >= region.Address && anchor < region.Address + region.Size ? 0 : anchor > region.Address ? anchor - region.Address : region.Address - anchor)];
        var chunks = new List<(int Region, ulong Offset)>();
        for (var at = 0; at < ordered.Length; at++)
        {
            for (ulong offset = 0; offset < ordered[at].Size; offset += HeapScan.ChunkBytes)
            {
                chunks.Add((at, offset));
            }
        }

        var next = -1;
        long swept = 0;
        var walked = 0;
        var all = new List<T>();
        var cut = false;
        void Work()
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(HeapScan.ChunkBytes + 16);
            var found = new List<T>();
            try
            {
                while (true)
                {
                    if (running.Stopping || Volatile.Read(ref swept) >= budget || clock.Elapsed.TotalSeconds >= seconds)
                    {
                        Volatile.Write(ref cut, Volatile.Read(ref next) + 1 < chunks.Count);
                        break;
                    }

                    int item = Interlocked.Increment(ref next);
                    if (item >= chunks.Count)
                    {
                        break;
                    }

                    (int index, ulong offset) = chunks[item];
                    MemoryRegion region = ordered[index];
                    if (offset == 0)
                    {
                        Interlocked.Increment(ref walked);
                    }

                    // EIGHT BYTES PAST THE CHUNK, so a begin in its last word still meets its end.
                    int want = (int)Math.Min((ulong)HeapScan.ChunkBytes, region.Size - offset);
                    int extra = (int)Math.Min(8UL, region.Size - offset - (ulong)want);
                    while (want >= 8 && !reader.TryRead(region.Address + offset, buffer.AsSpan(0, want + extra)))
                    {
                        want /= 2;
                        extra = 0;
                    }

                    if (want < 8)
                    {
                        continue;
                    }

                    Interlocked.Add(ref swept, want);
                    running.Swept(want);
                    scan(buffer.AsSpan(0, want + extra), region.Address + offset, found);
                    if (found.Count > 0)
                    {
                        lock (all)
                        {
                            all.AddRange(found);
                        }

                        found.Clear();
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        var threads = new Thread[Math.Max(0, Environment.ProcessorCount - 1)];
        for (var at = 0; at < threads.Length; at++)
        {
            threads[at] = new Thread(Work) { IsBackground = true, Name = "RiverFlowHunt" };
            threads[at].Start();
        }

        Work();
        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        return (all, new Walked(swept, walked, clock.Elapsed, cut, unwritable, driver));
    }

    /// <summary>The 32 bytes either side of a pair of ints, as pointers - what a grid's size may stand beside.</summary>
    private static string Around(IMemoryReader reader, ulong at)
    {
        var bytes = new byte[64];
        if (at < 32 || !reader.TryRead(at - 32, bytes))
        {
            return string.Empty;
        }

        var pointers = new List<string>();
        for (var offset = 0; offset + 8 <= bytes.Length; offset += 8)
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));
            if (MemoryReaderExtensions.IsPlausiblePointer(value))
            {
                pointers.Add(string.Create(CultureInfo.InvariantCulture, $"{offset - 32:+0;-0} -> 0x{value:X}"));
            }
        }

        return pointers.Count == 0 ? " - no pointer within 32 bytes" : " - pointers beside it: " + string.Join(", ", pointers);
    }

    /// <summary>A shape said for the report.</summary>
    private static string Said(Shape shape, IReadOnlyList<Grid> grids) => string.Create(
        CultureInfo.InvariantCulture, $"{grids[shape.Grid].Slot} {shape.Laid} {shape.Width} x {shape.Height} x {shape.Element} = {shape.Bytes}");

    /// <summary>The ways a texel of so many bytes may be read: a name, how many channels, and how one channel of one texel is read.</summary>
    private static IEnumerable<(string Name, int Channels, Func<byte[], int, int, float> Read)> Ways(int element)
    {
        static float Byte(byte[] data, int at) => data[at];
        static float UShort(byte[] data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        static float Half(byte[] data, int at) => (float)BinaryPrimitives.ReadHalfLittleEndian(data.AsSpan(at));
        static float Float(byte[] data, int at) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at));
        return element switch
        {
            1 => [("u8", 1, (data, texel, _) => Byte(data, texel))],
            2 => [
                ("u16", 1, (data, texel, _) => UShort(data, texel * 2)),
                ("u8 x2", 2, (data, texel, channel) => Byte(data, (texel * 2) + channel)),
                ("half", 1, (data, texel, _) => Half(data, texel * 2))],
            4 => [
                ("float", 1, (data, texel, _) => Float(data, texel * 4)),
                ("u8 x4", 4, (data, texel, channel) => Byte(data, (texel * 4) + channel)),
                ("half x2", 2, (data, texel, channel) => Half(data, (texel * 4) + (channel * 2))),
                ("u16 x2", 2, (data, texel, channel) => UShort(data, (texel * 4) + (channel * 2)))],
            8 => [
                ("float x2", 2, (data, texel, channel) => Float(data, (texel * 8) + (channel * 4))),
                ("half x4", 4, (data, texel, channel) => Half(data, (texel * 8) + (channel * 2)))],
            12 => [("float x3", 3, (data, texel, channel) => Float(data, (texel * 12) + (channel * 4)))],
            16 => [("float x4", 4, (data, texel, channel) => Float(data, (texel * 16) + (channel * 4)))],
            _ => [],
        };
    }

    /// <summary>One channel's numbers over the grid.</summary>
    private readonly record struct Channel(double Finite, float Least, float Most, double Mean, double Zero, double Across, double Down);

    /// <summary>
    /// A list's texels set out every way they may be read - each channel's range, share of noughts and how alike neighbours across and
    /// down are - the likeliest way (its neighbours most alike, its numbers finite and not all one) drawn as a map.
    /// </summary>
    public static List<string> SetOut(byte[] data, int width, int height, int element)
    {
        ArgumentNullException.ThrowIfNull(data);
        var lines = new List<string>();
        int texels = Math.Min(width * height, data.Length / Math.Max(1, element));
        int rows = width > 0 ? texels / width : 0;
        (string Name, int Channels, Func<byte[], int, int, float> Read)? best = null;
        double bestScore = double.NegativeInfinity;
        foreach ((string name, int channels, Func<byte[], int, int, float> read) in Ways(element))
        {
            var all = new Channel[channels];
            for (var channel = 0; channel < channels; channel++)
            {
                all[channel] = Measure(data, width, rows, channel, read);
            }

            // A CHANNEL ALL ONE NUMBER IS LEFT OUT OF THE SCORE, not the way: a map may leave a channel unused.
            Channel[] varied = [.. all.Where(one => one.Most > one.Least)];
            double score = all.All(one => one.Finite >= 0.99) && varied.Length > 0 ? varied.Average(one => Math.Min(one.Across, one.Down)) : double.NegativeInfinity;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  as {name}: {(double.IsNegativeInfinity(score) ? "not finite throughout, or every channel one number" : $"neighbours alike {score:0.000}")}"));
            for (var channel = 0; channel < channels; channel++)
            {
                Channel one = all[channel];
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"    channel {channel}: finite {one.Finite:P1}, {one.Least:G5} to {one.Most:G5}, mean {one.Mean:G5}, noughts {one.Zero:P1}, alike across {one.Across:0.000} down {one.Down:0.000}"));
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = (name, channels, read);
            }
        }

        if (best is { } drawn && rows > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  drawn as {drawn.Name}{(drawn.Channels >= 2 ? ": the way channels 0 and 1 point, 0 along +x and on round by eighths towards +y, '.' where they are under a twentieth of their most" : ": channel 0 in tenths of its range, '.' where nought")}, row 0 at the top"));
            lines.AddRange(Map(data, width, rows, drawn.Channels, drawn.Read));
        }

        return lines;
    }

    /// <summary>One channel's range, noughts and how alike neighbours are (correlation over the grid's pairs across and down).</summary>
    private static Channel Measure(byte[] data, int width, int rows, int channel, Func<byte[], int, int, float> read)
    {
        long finite = 0, zero = 0, count = (long)width * rows;
        float least = float.MaxValue, most = float.MinValue;
        double sum = 0;
        for (var texel = 0; texel < count; texel++)
        {
            float value = read(data, texel, channel);
            if (!float.IsFinite(value))
            {
                continue;
            }

            finite++;
            zero += value == 0f ? 1 : 0;
            least = MathF.Min(least, value);
            most = MathF.Max(most, value);
            sum += value;
        }

        double Alike(int dx, int dy)
        {
            double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
            long n = 0;
            for (var y = 0; y + dy < rows; y++)
            {
                for (var x = 0; x + dx < width; x++)
                {
                    float a = read(data, (y * width) + x, channel), b = read(data, ((y + dy) * width) + x + dx, channel);
                    if (!float.IsFinite(a) || !float.IsFinite(b))
                    {
                        continue;
                    }

                    sa += a;
                    sb += b;
                    saa += (double)a * a;
                    sbb += (double)b * b;
                    sab += (double)a * b;
                    n++;
                }
            }

            if (n < 2)
            {
                return 0;
            }

            double va = (saa / n) - ((sa / n) * (sa / n)), vb = (sbb / n) - ((sb / n) * (sb / n));
            return va <= 1e-20 || vb <= 1e-20 ? 0 : ((sab / n) - ((sa / n) * (sb / n))) / Math.Sqrt(va * vb);
        }

        return finite == 0
            ? new Channel(0, 0, 0, 0, 0, 0, 0)
            : new Channel((double)finite / count, least, most, sum / finite, (double)zero / finite, Alike(1, 0), Alike(0, 1));
    }

    /// <summary>The grid drawn in characters, <see cref="MapColumns"/> wide at most, a character twice as tall as it is wide.</summary>
    private static IEnumerable<string> Map(byte[] data, int width, int rows, int channels, Func<byte[], int, int, float> read)
    {
        int columns = Math.Min(MapColumns, width);
        double step = (double)width / columns;
        int lines = Math.Max(1, (int)(rows / step / 2));
        float least = float.MaxValue, most = float.MinValue, longest = 0f;
        for (var texel = 0; texel < width * rows; texel++)
        {
            float a = read(data, texel, 0);
            if (!float.IsFinite(a))
            {
                continue;
            }

            least = MathF.Min(least, a);
            most = MathF.Max(most, a);
            if (channels >= 2 && read(data, texel, 1) is var b && float.IsFinite(b))
            {
                longest = MathF.Max(longest, MathF.Sqrt((a * a) + (b * b)));
            }
        }

        var line = new StringBuilder(columns + 4);
        for (var row = 0; row < lines; row++)
        {
            line.Clear().Append("    ");
            int y = Math.Min(rows - 1, (int)(row * step * 2));
            for (var column = 0; column < columns; column++)
            {
                int texel = (y * width) + Math.Min(width - 1, (int)(column * step));
                float a = read(data, texel, 0);
                if (channels >= 2)
                {
                    float b = read(data, texel, 1);
                    float length = MathF.Sqrt((a * a) + (b * b));
                    line.Append(!float.IsFinite(length) ? '?' : length < longest / 20f ? '.'
                        : (char)('0' + (((int)MathF.Round(MathF.Atan2(b, a) / (MathF.PI / 4f)) + 8) % 8)));
                }
                else
                {
                    line.Append(!float.IsFinite(a) ? '?' : a == 0f ? '.' : most > least
                        ? (char)('0' + Math.Clamp((int)((a - least) / (most - least) * 10f), 0, 9)) : '0');
                }
            }

            yield return line.ToString();
        }
    }

    /// <summary>A report's lists read back from their files beside it and set out again by today's rules - river-flow-replay.txt.</summary>
    public static string Replay(string text, string folder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(folder);
        var said = new StringBuilder();
        var lists = 0;
        foreach (string raw in text.Split('\n'))
        {
            if (Candidate.Match(raw.TrimEnd('\r')) is not { Success: true } match)
            {
                continue;
            }

            lists++;
            string file = Path.Combine(folder, match.Groups[7].Value);
            said.Append(raw.TrimEnd('\r')).AppendLine();
            if (!File.Exists(file))
            {
                said.AppendLine("  its file is not beside the report");
                continue;
            }

            int width = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            int height = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
            int element = int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture);
            byte[] bytes = File.ReadAllBytes(file);
            IEnumerable<string> flow = match.Groups[3].Value == "texels" && FlowMap(bytes, width, height, element) is { } reading ? reading.Lines : [];
            foreach (string line in SetOut(bytes, width, height, element).Concat(flow))
            {
                said.AppendLine(line);
            }
        }

        return lists == 0 ? "river flow hunt replay: the report holds no list read" + Environment.NewLine : said.ToString();
    }
}
