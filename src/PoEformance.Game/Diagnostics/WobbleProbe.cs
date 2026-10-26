using System.Diagnostics;
using System.Globalization;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Components;
using PoEformance.Game.Entities;
using PoEformance.Game.World;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// Watches the game sway a ship: every wobble point's WobblePoint component, and the doodads standing by it, read over seconds - for the capture's wobble part.
/// </summary>
/// <remarks>
/// WHY IT EXISTS. Port's ship (<c>boss_boat.ao</c>) sways in the game - measured off a video, its gangway about four pixels up and
/// down every 2.05 s with an amplitude that comes and goes, its hull 0.62 of that in step - yet its .ao is a FixedMesh with no rig
/// and no animation (v0.1.289's capture). What the room lays beside it is a two-triangle doodad,
/// <c>BoatnautWobbleCore_small.ao</c>, whose entity type <c>Metadata/Terrain/Gallows/Act4/4_6/Objects/BoatWobbleCore</c> carries a
/// component no reference names, WobblePoint; and the room's other doodads are of the type <c>Doodad</c> or
/// <c>DoodadNoWobble</c>. One read of the component (that capture's recording) held a rotation, three rows of four at +0x68 with a
/// tilt of 0.078 degrees, then 0.171875 and 0.423828 and 18.15 after it. One read says nothing of a motion: this reads it for
/// seconds, as fast as it reads, and says what changes and how.
///
/// AND WHERE THE SWAY LANDS. The ship's own Render holds its place and a turn of 3 pi / 2 and nothing more on that one read, so the
/// transform the engine draws it by lives elsewhere. Rather than guess where, every component of the doodads by the wobble point
/// is read, and one hop beyond every pointer in them, at a twentieth of a second - and every four bytes that change are named with
/// their range and period. A <c>DoodadNoWobble</c> doodad nearby is read the same way, as the control: what changes in both is the
/// clock, not the sway.
///
/// PRINTS BOTH HALVES: the first read of every block whole, then the changes - so a field whose meaning is argued about later is
/// in the file as raw bytes, not only as a statistic.
/// </remarks>
public sealed class WobbleProbe
{
    /// <summary>How long it watches - five of the measured periods and more, so an amplitude that comes and goes shows its cycle.</summary>
    public const double Seconds = 20;

    /// <summary>How much of a WobblePoint is read at full rate - its fields end at +0xA8 on the one read seen.</summary>
    public const int PointBytes = 0xC0;

    /// <summary>How much of each component and each block one hop away is read.</summary>
    public const int BlockBytes = 0x200;

    /// <summary>How often the doodads' blocks are read - a twentieth of a second, forty reads to the period.</summary>
    public const double BlockEverySeconds = 0.05;

    /// <summary>How far from a wobble point a doodad is watched.</summary>
    public const float Reach = 6000;

    /// <summary>Most wobbling doodads watched a point, nearest first.</summary>
    public const int MostWobblers = 4;

    /// <summary>Most control doodads watched a point, nearest first.</summary>
    public const int MostControls = 2;

    /// <summary>Most blocks one hop beyond one entity's components.</summary>
    public const int MostHops = 96;

    /// <summary>Most changed fields written an entity.</summary>
    public const int MostFields = 160;

    /// <summary>The component this is about.</summary>
    public const string Component = "WobblePoint";

    /// <summary>The entity type of a doodad the engine may sway.</summary>
    public const string Wobbling = "Metadata/MiscellaneousObjects/Doodad";

    /// <summary>The entity type of a doodad it may not - the control.</summary>
    public const string NotWobbling = "Metadata/MiscellaneousObjects/DoodadNoWobble";

    private readonly IMemoryReader _reader;
    private readonly OffsetSchema _schema;
    private readonly int _worldPosition;
    private volatile bool _stop;
    private long _began;

    private readonly double _seconds;

    /// <param name="reader">The game's memory.</param>
    /// <param name="schema">The offsets.</param>
    /// <param name="seconds">How long it watches - <see cref="Seconds"/> but in a test.</param>
    public WobbleProbe(IMemoryReader reader, OffsetSchema schema, double seconds = Seconds)
    {
        _seconds = seconds;
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        _reader = reader;
        _schema = schema;
        _worldPosition = schema.Structs["Render"].OffsetOf("CurrentWorldPosition");
    }

    /// <summary>For the capture's window: what it is doing.</summary>
    public string Line { get; private set; } = "not started";

    /// <summary>For the capture's window: how far it is, nought to one.</summary>
    public float Fraction => _began == 0 ? 0f : (float)Math.Clamp(Stopwatch.GetElapsedTime(_began).TotalSeconds / _seconds, 0, 1);

    /// <summary>Stops the watching early - what it read until then is written.</summary>
    public void Stop() => _stop = true;

    /// <summary>One entity watched: what it is, where, and the blocks read off it.</summary>
    private sealed record Watched(string Role, uint Id, string Path, string Model, ulong Address, float X, float Y, float Z, List<Block> Blocks);

    /// <summary>One block of memory read again and again: its name, address, and every read of it.</summary>
    private sealed record Block(string Name, ulong Address, int Size, List<(double At, byte[] Bytes)> Reads);

    /// <summary>Watches, and says what it saw - the part's lines, the first a summary.</summary>
    /// <param name="gameStatesStatic">The game-states static, the chain's root.</param>
    public IReadOnlyList<string> Run(ulong gameStatesStatic)
    {
        var lines = new List<string>();
        GameChainAddresses chain = GameChain.Resolve(_reader, _schema, gameStatesStatic);
        if (chain.AreaInstance == 0)
        {
            return ["wobble: not in an area"];
        }

        Line = "walking both entity maps for the wobble points";
        var entities = new EntityReader(_reader, _schema);
        var maps = new EntityMapReader(_reader, _schema);
        var render = new RenderReader(_reader, _schema);
        StructDef area = _schema.Structs["AreaInstance"];
        var all = new List<(uint Id, EntityIdentity Identity, bool Asleep)>();
        foreach ((string map, bool asleep) in (ReadOnlySpan<(string, bool)>)[("SleepingEntities", true), ("AwakeEntities", false)])
        {
            foreach ((uint id, ulong address) in maps.ReadEntityPointers(
                chain.AreaInstance + (ulong)area.OffsetOf(map), SleepingDoodads.MostNodes, includeVisuals: true, mostVisits: SleepingDoodads.MostNodes))
            {
                if (entities.ReadIdentity(address) is { Path.Length: > 0 } identity)
                {
                    all.Add((id, identity, asleep));
                }
            }
        }

        // THE WOBBLE POINTS BY THEIR COMPONENT, among the entities whose type names a wobble - reading every entity's components
        // would be thousands of reads for the one or two that carry it.
        var points = new List<Watched>();
        var placed = new List<(uint Id, EntityIdentity Identity, float X, float Y, float Z)>();
        foreach ((uint id, EntityIdentity identity, _) in all)
        {
            bool candidate = identity.Path.Contains("wobble", StringComparison.OrdinalIgnoreCase) && !identity.Path.Equals(NotWobbling, StringComparison.OrdinalIgnoreCase);
            bool doodad = identity.Path.Equals(Wobbling, StringComparison.OrdinalIgnoreCase) || identity.Path.Equals(NotWobbling, StringComparison.OrdinalIgnoreCase);
            if (!candidate && !doodad)
            {
                continue;
            }

            IReadOnlyDictionary<string, ulong> components = entities.ReadComponents(identity.Address, identity.Details);
            if (render.Read(components.GetValueOrDefault("Render")) is not { } at)
            {
                continue;
            }

            if (candidate && components.GetValueOrDefault(Component) != 0)
            {
                points.Add(new Watched("wobble point", id, identity.Path, ModelOf(components), identity.Address, at.X, at.Y, at.Z, Blocks(components, identity.Address)));
            }
            else if (doodad)
            {
                placed.Add((id, identity, at.X, at.Y, at.Z));
            }
        }

        string walked = string.Create(CultureInfo.InvariantCulture, $"{all.Count} named entities walked, {all.Count(one => one.Asleep)} asleep");
        if (points.Count == 0)
        {
            return [$"wobble: no entity carries a {Component} - {walked}"];
        }

        // THE DOODADS BY EACH POINT, the swayable nearest first and the control beside them.
        var watched = new List<Watched>(points);
        foreach (Watched point in points)
        {
            foreach ((string type, string role, int most) in (ReadOnlySpan<(string, string, int)>)[(Wobbling, "doodad", MostWobblers), (NotWobbling, "control", MostControls)])
            {
                foreach ((uint id, EntityIdentity identity, float x, float y, float z) in placed
                    .Where(one => one.Identity.Path.Equals(type, StringComparison.OrdinalIgnoreCase))
                    .Select(one => (one, distance: MathF.Sqrt(((one.X - point.X) * (one.X - point.X)) + ((one.Y - point.Y) * (one.Y - point.Y)))))
                    .Where(one => one.distance <= Reach)
                    .OrderBy(one => one.distance)
                    .Take(most)
                    .Select(one => one.one))
                {
                    if (watched.Any(one => one.Id == id))
                    {
                        continue;
                    }

                    IReadOnlyDictionary<string, ulong> components = entities.ReadComponents(identity.Address, identity.Details);
                    watched.Add(new Watched(role, id, identity.Path, ModelOf(components), identity.Address, x, y, z, Blocks(components, identity.Address)));
                }
            }
        }

        // ONE HOP BEYOND every pointer of every component, chosen on the first read.
        foreach (Watched one in watched)
        {
            Hops(one);
        }

        // THE WATCH: the points' components at full rate, every block at a twentieth of a second.
        _began = Stopwatch.GetTimestamp();
        double nextBlocks = 0;
        int sweeps = 0;
        byte[] buffer = new byte[BlockBytes];
        while (!_stop)
        {
            double now = Stopwatch.GetElapsedTime(_began).TotalSeconds;
            if (now >= _seconds)
            {
                break;
            }

            foreach (Watched point in points)
            {
                Read(point.Blocks[0], now, buffer, onlyChanged: true);
            }

            if (now >= nextBlocks)
            {
                foreach (Watched one in watched)
                {
                    foreach (Block block in one.Blocks)
                    {
                        if (!ReferenceEquals(block, one.Blocks[0]) || one.Role != "wobble point")
                        {
                            Read(block, now, buffer, onlyChanged: false);
                        }
                    }
                }

                sweeps++;
                nextBlocks += BlockEverySeconds;
                Line = string.Create(CultureInfo.InvariantCulture, $"{now:0.0} of {_seconds:0} s - {points.Count} wobble points read {points.Sum(one => one.Blocks[0].Reads.Count)} times changed, {watched.Sum(one => one.Blocks.Count)} blocks read {sweeps} times");
            }

            Thread.Sleep(1);
        }

        double watchedFor = Stopwatch.GetElapsedTime(_began).TotalSeconds;
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"wobble: {points.Count} wobble points and {watched.Count - points.Count} doodads by them watched for {watchedFor:0.0} s{(_stop ? " (stopped)" : string.Empty)} - {walked}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"each point's {Component} read as fast as it reads ({PointBytes} bytes), kept where it changed; every component and one hop beyond its pointers read every {BlockEverySeconds} s ({BlockBytes} bytes each), {sweeps} times"));
        lines.Add("a field is four bytes; a float field one whose every read is a finite float under a million; its period the autocorrelation's highest peak between 0.3 and 8 s, over the reads at an even step");
        foreach (Watched one in watched)
        {
            Describe(one, lines);
        }

        foreach (Watched point in points)
        {
            PointSeries(point, lines);
        }

        return lines;
    }

    /// <summary>The model an entity loaded, by the Animated component's chain.</summary>
    private string ModelOf(IReadOnlyDictionary<string, ulong> components)
    {
        ulong animated = components.GetValueOrDefault("Animated");
        if (animated == 0)
        {
            return string.Empty;
        }

        ulong info = _reader.ReadPointer(animated + (ulong)_schema.Structs["Animated"].OffsetOf("ModelInfoPtr"));
        ulong record = info == 0 ? 0 : _reader.ReadPointer(info + (ulong)_schema.Structs["AnimatedModelInfo"].OffsetOf("ModelFileRecordPtr"));
        return record == 0 ? string.Empty : SleepingDoodads.ModelPath(_reader.ReadStdWString(record + (ulong)_schema.Structs["FileInfoValue"].OffsetOf("Name")));
    }

    /// <summary>An entity's blocks: its WobblePoint first where it has one, then the entity itself and each component.</summary>
    private static List<Block> Blocks(IReadOnlyDictionary<string, ulong> components, ulong entity)
    {
        var blocks = new List<Block>();
        if (components.GetValueOrDefault(Component) is var point and not 0)
        {
            blocks.Add(new Block(Component, point, PointBytes, []));
        }

        blocks.Add(new Block("entity", entity, BlockBytes, []));
        foreach ((string name, ulong at) in components.OrderBy(one => one.Key, StringComparer.Ordinal))
        {
            if (at != 0 && name != Component)
            {
                blocks.Add(new Block(name, at, BlockBytes, []));
            }
        }

        return blocks;
    }

    /// <summary>Adds a block one hop beyond every heap pointer in the entity's blocks, read once now to choose them.</summary>
    private void Hops(Watched one)
    {
        var seen = new HashSet<ulong>(one.Blocks.Select(block => block.Address));
        ulong moduleEnd = _reader.ModuleBase + _reader.ModuleSize;
        byte[] bytes = new byte[BlockBytes];
        foreach (Block block in one.Blocks.ToArray())
        {
            if (!_reader.TryRead(block.Address, bytes.AsSpan(0, block.Size)))
            {
                continue;
            }

            for (var at = 0; at + 8 <= block.Size && one.Blocks.Count < MostHops + 8; at += 8)
            {
                ulong pointer = BitConverter.ToUInt64(bytes, at);

                // NOT THE PROGRAM'S OWN: a vtable or a static says nothing that moves.
                if (!MemoryReaderExtensions.IsPlausiblePointer(pointer) || (pointer >= _reader.ModuleBase && pointer < moduleEnd) || !seen.Add(pointer))
                {
                    continue;
                }

                one.Blocks.Add(new Block(string.Create(CultureInfo.InvariantCulture, $"{block.Name}+0x{at:X} ->"), pointer, BlockBytes, []));
            }
        }
    }

    /// <summary>Reads a block once more; where <paramref name="onlyChanged"/>, keeps the read only if it differs from the last.</summary>
    private void Read(Block block, double now, byte[] buffer, bool onlyChanged)
    {
        Span<byte> span = buffer.AsSpan(0, block.Size);
        if (!_reader.TryRead(block.Address, span))
        {
            return;
        }

        if (onlyChanged && block.Reads.Count > 0 && span.SequenceEqual(block.Reads[^1].Bytes))
        {
            return;
        }

        block.Reads.Add((now, span.ToArray()));
    }

    /// <summary>One entity's lines: what it is, each block's first read whole, then every field that changed.</summary>
    private static void Describe(Watched one, List<string> lines)
    {
        lines.Add(string.Empty);
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"=== {one.Role} {one.Id} {one.Path} at {one.X:0.###} {one.Y:0.###} {one.Z:0.###}, model {(one.Model.Length > 0 ? one.Model : "(none read)")}, entity 0x{one.Address:X} - {one.Blocks.Count} blocks"));
        var changed = new List<(string Line, double Spread)>();
        int unreadable = 0, pointersChanged = 0;
        foreach (Block block in one.Blocks)
        {
            if (block.Reads.Count == 0)
            {
                unreadable++;
                continue;
            }

            for (var at = 0; at + 4 <= block.Size; at += 4)
            {
                if (!Changes(block, at))
                {
                    continue;
                }

                float[] values = [.. block.Reads.Select(read => BitConverter.ToSingle(read.Bytes, at))];
                if (values.Any(value => !float.IsFinite(value) || MathF.Abs(value) >= 1e6f))
                {
                    pointersChanged++;
                    continue;
                }

                float least = values.Min(), most = values.Max();
                double period = Period(block.Reads.Select(read => read.At).ToArray(), values);
                changed.Add((
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {block.Name} 0x{block.Address:X} +0x{at:X}: {least:G6} .. {most:G6}, mean {values.Average(value => (double)value):G6}, first {values[0]:G6}, {block.Reads.Count} reads, period {(period > 0 ? period.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "none")}"),
                    most - least));
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"changed: {changed.Count} float fields, {pointersChanged} other fields, {unreadable} blocks never read"));
        lines.AddRange(changed.Take(MostFields).Select(one => one.Line));
        if (changed.Count > MostFields)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {changed.Count - MostFields} more not written"));
        }

        lines.Add("first read of every block:");
        foreach (Block block in one.Blocks.Where(block => block.Reads.Count > 0))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  -- {block.Name} 0x{block.Address:X}"));
            Hex(block.Reads[0].Bytes, lines);
        }
    }

    /// <summary>Whether four bytes at <paramref name="at"/> differ between any two reads.</summary>
    private static bool Changes(Block block, int at)
    {
        uint first = BitConverter.ToUInt32(block.Reads[0].Bytes, at);
        return block.Reads.Any(read => BitConverter.ToUInt32(read.Bytes, at) != first);
    }

    /// <summary>A point's WobblePoint, every changed float field a column, over its reads at a fiftieth of a second.</summary>
    private static void PointSeries(Watched point, List<string> lines)
    {
        Block block = point.Blocks[0];
        int[] fields = [.. Enumerable.Range(0, block.Size / 4).Select(index => index * 4).Where(at => block.Reads.Count > 1 && Changes(block, at))];
        lines.Add(string.Empty);
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"=== {Component} of {point.Id} over time: {block.Reads.Count} reads that changed it, every changed field a column, a row every 0.02 s"));
        lines.Add(string.Join('\t', fields.Select(at => string.Create(CultureInfo.InvariantCulture, $"+0x{at:X}")).Prepend("t")));
        double next = 0;
        foreach ((double at, byte[] bytes) in block.Reads)
        {
            if (at < next)
            {
                continue;
            }

            next = at + 0.02;
            lines.Add(string.Join(
                '\t',
                fields.Select(field => BitConverter.ToSingle(bytes, field).ToString("G7", CultureInfo.InvariantCulture)).Prepend(at.ToString("0.000", CultureInfo.InvariantCulture))));
        }
    }

    /// <summary>The bytes as rows of sixteen, each row as two quadwords and four floats.</summary>
    private static void Hex(byte[] bytes, List<string> lines)
    {
        for (var row = 0; row < bytes.Length; row += 16)
        {
            int length = Math.Min(16, bytes.Length - row);
            if (bytes.AsSpan(row, length).IndexOfAnyExcept((byte)0) < 0)
            {
                continue;
            }

            string quads = string.Join(' ', Enumerable.Range(0, length / 8).Select(index => BitConverter.ToUInt64(bytes, row + (index * 8)).ToString("X16", CultureInfo.InvariantCulture)));
            string floats = string.Join(' ', Enumerable.Range(0, length / 4).Select(index =>
            {
                float value = BitConverter.ToSingle(bytes, row + (index * 4));
                return float.IsFinite(value) && value != 0 && MathF.Abs(value) is > 1e-7f and < 1e7f ? value.ToString("G6", CultureInfo.InvariantCulture) : ".";
            }));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"    +{row:X3}  {quads}  {floats}"));
        }
    }

    /// <summary>
    /// The period of a series read at uneven times: resampled at a hundredth of a second, its autocorrelation's highest peak between 0.3 and 8 s - nought where none stands out.
    /// </summary>
    public static double Period(double[] times, float[] values)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(values);
        const double Step = 0.01;
        if (times.Length < 8 || times[^1] - times[0] < 1)
        {
            return 0;
        }

        // EVEN STEPS, each the last read at or before it - a field read only where it changed holds its value between.
        int count = (int)((times[^1] - times[0]) / Step) + 1;
        double[] even = new double[count];
        int source = 0;
        for (var index = 0; index < count; index++)
        {
            double at = times[0] + (index * Step);
            while (source + 1 < times.Length && times[source + 1] <= at)
            {
                source++;
            }

            even[index] = values[source];
        }

        double mean = even.Average();
        double power = 0;
        for (var index = 0; index < count; index++)
        {
            even[index] -= mean;
            power += even[index] * even[index];
        }

        if (power <= 0)
        {
            return 0;
        }

        // PAST THE FIRST FALL BELOW NOUGHT: a slow drift correlates highest at the shortest lags, which is no period. And THE
        // FIRST PEAK near the highest, not the highest: a swing whose amplitude comes and goes may correlate best at three periods.
        int least = (int)(0.3 / Step), most = Math.Min((int)(8 / Step), count / 2);
        double[] correlations = new double[most + 2];
        int fall = 0;
        for (var lag = 1; lag <= most + 1 && lag < count; lag++)
        {
            double sum = 0;
            for (var index = 0; index + lag < count; index++)
            {
                sum += even[index] * even[index + lag];
            }

            correlations[lag] = sum / power * count / (count - lag);
            if (fall == 0 && correlations[lag] < 0)
            {
                fall = lag;
            }
        }

        if (fall == 0)
        {
            return 0;
        }

        double best = 0;
        for (int lag = Math.Max(least, fall); lag <= most; lag++)
        {
            best = Math.Max(best, correlations[lag]);
        }

        for (int lag = Math.Max(least, fall); best >= 0.3 && lag <= most; lag++)
        {
            if (correlations[lag] >= 0.8 * best && correlations[lag] >= correlations[lag - 1] && correlations[lag] >= correlations[lag + 1])
            {
                return lag * Step;
            }
        }

        return 0;
    }
}
