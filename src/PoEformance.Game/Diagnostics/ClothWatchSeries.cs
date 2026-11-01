using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// What the cloth watch read (ClothWatch): the game's own cloth objects near the player as they were set up, and every change of their
/// targets and their bodies - with the contacts each step made, the capsules round the player and the physics system's clock - each with
/// when it was read. Written to <see cref="File"/> beside the part's text and read back offline: its reading (<see cref="Report"/>) steps the
/// tool's own cloth (ClothWorld) from the game's own state on the game's own targets, one step at a time, under each order the frame's
/// phases may run in, and holds it against the game's next state; and holds each object's setup against the tool's own cloth of the piece.
/// </summary>
/// <remarks>
/// WHY (2026-11-01, v0.1.425): the tool's cloth is a port of the engine's code (v0.1.421), run under a CPU emulator function by function -
/// but how the functions are put together in a frame was CHOSEN where the code did not say (ClothWorld's remarks: the order of the
/// controller's phases against the system's step, how a kinematic particle reaches its target, a capsule surface's motion). The game's own
/// state step by step settles each: a step the port takes from the game's state onto the game's targets must land on the game's next state.
/// </remarks>
public sealed class ClothWatchSeries
{
    /// <summary>The file the reads are kept in beside the part's text.</summary>
    public const string File = "cloth-watch.bin";

    /// <summary>A particle of the engine's object (schema ClothParticle).</summary>
    public const int ParticleBytes = 0x58;

    /// <summary>A body of the world's container (schema ClothBody).</summary>
    public const int BodyBytes = 0x38;

    /// <summary>A contact the world's step made (schema ClothContainer.Contacts).</summary>
    public const int ContactBytes = 0x30;

    /// <summary>A capsule of the physics world (schema PhysicsWorldColliders.Capsules).</summary>
    public const int CapsuleBytes = 0x70;

    /// <summary>A sphere of the physics world (schema PhysicsWorldColliders.Spheres).</summary>
    public const int SphereBytes = 0x64;

    /// <summary>A particle's targets as a Targets event keeps them: its target now and its last, three floats each (ClothParticle +0x20).</summary>
    public const int TargetBytes = 24;

    /// <summary>A capsule as a Capsules event keeps it: its record whole, then each of its spheres' centre and radius.</summary>
    public const int ShapeBytes = CapsuleBytes + 32;

    /// <summary>Most bodies events stepped in the free run - CHOSEN: some ten seconds of frames.</summary>
    private const int MostFreeSteps = 1200;

    /// <summary>A particle within this of a place stands on it - CHOSEN, a float's rounding at a world place.</summary>
    public const float OnTarget = 0.01f;

    private static readonly byte[] Magic = "POEFCLOTH1"u8.ToArray();

    /// <summary>
    /// The orders of a frame's phases the replay tries: the controller's targets (SetTargets), the system's step, the controller's phase
    /// after it (shock propagation and the enclosure) - the tool's is the first.
    /// </summary>
    private static readonly string[] Orders =
    [
        "targets, step, after (the tool's)",
        "step, targets, after",
        "targets, after, step",
    ];

    /// <summary>What a change read is of.</summary>
    public enum Kind : byte
    {
        /// <summary>The physics system's clock, 16 bytes: the time held, the last step, the time since it, the smoothed frame time (schema PhysicsSystem +0xF0).</summary>
        Clock = 1,

        /// <summary>An object's targets: <see cref="TargetBytes"/> a particle in its order.</summary>
        Targets = 2,

        /// <summary>
        /// An object's bodies after a step: the container's last step's seconds (float), the body count (int), the bodies (<see cref="BodyBytes"/>
        /// each, in the particles' order), the contacts' count (int) and the contacts (<see cref="ContactBytes"/> each).
        /// </summary>
        Bodies = 3,

        /// <summary>The capsules round the player: their count (int), then <see cref="ShapeBytes"/> each.</summary>
        Capsules = 4,
    }

    /// <summary>A distance constraint of the world's container: its two particles (-1 where a handle is no particle's), rest, stiffness, contraction.</summary>
    public readonly record struct Distance(int A, int B, float Rest, float Stiffness, float Contraction);

    /// <summary>A bend constraint of the world's container: the edge's two particles, its two wings, stiffness, rest fold, allowed angle.</summary>
    public readonly record struct Bend(int A, int B, int C, int D, float Stiffness, float RestFold, float AllowedAngle);

    /// <summary>
    /// How one order of a frame's phases takes the game's steps again: how many steps, how many landed within <see cref="OnTarget"/> of the
    /// game's next state throughout, and each step's worst particle off it - its median and its most.
    /// </summary>
    public sealed record OrderFit(string Order, int Steps, int Exact, float MedianWorst, float MostWorst);

    /// <summary>A change read: what, of which object (-1 for the clock and the capsules), when (seconds from the watch's start), and where its bytes are.</summary>
    public readonly record struct Event(Kind Kind, int Object, double Time, int At, int Length);

    /// <summary>
    /// A physics object as the game set it up, read once: its own bytes and its container's head, each particle's body handle, depth and
    /// parent, its triangles, edges, pinned particles, shock links and bone records, and its container's distance and bend constraints.
    /// </summary>
    public sealed record Setup(
        ulong Address,
        byte[] Head,
        byte[] ContainerHead,
        long[] Handles,
        int[] Depth,
        int[] Parent,
        ClothTriangle[] Triangles,
        int[] Edges,
        int[] Pinned,
        (long Parent, long Child, float Length)[] Shocks,
        Distance[] Distances,
        Bend[] Bends,
        byte[] Bones,
        float Nearest)
    {
        /// <summary>How many particles.</summary>
        public int Count => Handles.Length;

        /// <summary>A float of its own bytes.</summary>
        public float F(int offset) => offset + 4 <= Head.Length ? BinaryPrimitives.ReadSingleLittleEndian(Head.AsSpan(offset)) : float.NaN;

        /// <summary>
        /// Its simulation as the engine holds it: its block from +0xD8 (the viscosities, the enclosure, the animation's factors, the max
        /// velocity, the scale folded into the two factors, gravity), its constraints' stiffness, contraction and allowed angle, and its
        /// container's collisions flag; the shock propagation <paramref name="shocks"/> (the object's flag for it is not read).
        /// </summary>
        public ClothSettings Settings(bool shocks)
        {
            float scale = F(0xFC) is var s && float.IsFinite(s) && s != 0f ? s : 1f;
            Distance first = Distances.Length > 0 ? Distances[0] : new Distance(-1, -1, 0f, -1f, 0.5f);
            Bend bend = Bends.Length > 0 ? Bends[0] : new Bend(-1, -1, -1, -1, 10f, 0f, 1f);
            bool collides = ContainerHead.Length > 0x1A9 && ContainerHead[0x1A9] != 0;
            bool heightmap = ContainerHead.Length > 0x1AA && ContainerHead[0x1AA] != 0;
            return new ClothSettings(
                first.Stiffness, first.Contraction, bend.Stiffness, bend.AllowedAngle, F(0xD8), F(0xDC), F(0xE0), F(0xE4), F(0xE8),
                F(0xF0) * scale, F(0xEC) * scale, F(0xF4), F(0xF8), F(0x100), shocks, collides, heightmap, true);
        }
    }

    /// <summary>The tool's own cloth of the same piece (MonsterModel's Swaying): its name, simulation, rest in the model's space, triangles and pinned particles.</summary>
    public sealed record Tool(string Name, ClothSettings Settings, Vector3[] Rest, int[] Triangles, int[] Pinned)
    {
        /// <summary>The tool's cloth again, unlinked to any bone.</summary>
        public ClothRig Rig() => new(Rest, [.. Rest.Select(_ => new Int4(-1, -1, -1, -1))], new Vector4[Rest.Length], Triangles, Pinned, [], Settings, Name);

        /// <summary>The tool's cloth of a model, as the watch keeps it.</summary>
        public static Tool Of(string name, ClothRig rig)
        {
            ArgumentNullException.ThrowIfNull(rig);
            return new Tool(
                name ?? string.Empty,
                rig.Settings,
                [.. rig.Rest],
                [.. rig.Triangles.SelectMany(t => new[] { t.A, t.B, t.C })],
                [.. Enumerable.Range(0, rig.Count).Where(one => rig.Pinned[one])]);
        }
    }

    private readonly byte[] _bytes;

    private ClothWatchSeries(
        byte[] bytes, Vector3 player, ulong world, long system, double seconds, long cycles, long torn, string note, List<Setup> setups,
        List<Tool?> tools, List<(ulong Capsule, ulong A, ulong B)> capsules, List<Event> events)
    {
        _bytes = bytes;
        Player = player;
        World = world;
        System = system;
        Seconds = seconds;
        Cycles = cycles;
        Torn = torn;
        Note = note;
        Setups = setups;
        Tools = tools;
        Capsules = capsules;
        Events = events;
    }

    /// <summary>Where the player stood when the watch began.</summary>
    public Vector3 Player { get; }

    /// <summary>The physics world (the objects' manager) the capsules were read from.</summary>
    public ulong World { get; }

    /// <summary>The physics system's static as an offset into the program, -1 where it was not found.</summary>
    public long System { get; }

    /// <summary>How long it watched.</summary>
    public double Seconds { get; }

    /// <summary>How many times it read everything.</summary>
    public long Cycles { get; }

    /// <summary>How many of an object's reads were torn - a step between its two reads - and dropped.</summary>
    public long Torn { get; }

    /// <summary>What ended the watch, where something other than its time did.</summary>
    public string Note { get; }

    /// <summary>The objects as they were set up.</summary>
    public IReadOnlyList<Setup> Setups { get; }

    /// <summary>The tool's own cloth matched to each object, null where none.</summary>
    public IReadOnlyList<Tool?> Tools { get; }

    /// <summary>The capsules round the player: each one's handle and its two spheres' handles.</summary>
    public IReadOnlyList<(ulong Capsule, ulong A, ulong B)> Capsules { get; }

    /// <summary>Every change read, in the order read.</summary>
    public IReadOnlyList<Event> Events { get; }

    /// <summary>An event's bytes.</summary>
    public ReadOnlySpan<byte> Bytes(Event one) => _bytes.AsSpan(one.At, one.Length);

    /// <summary>The series being written while the watch reads - events appended as they come.</summary>
    public sealed class Writer : IDisposable
    {
        private readonly MemoryStream _events = new();
        private readonly BinaryWriter _write;
        private int _count;

        /// <summary>A writer with nothing in it.</summary>
        public Writer() => _write = new BinaryWriter(_events, Encoding.UTF8, leaveOpen: true);

        /// <summary>The events' bytes so far.</summary>
        public long Length => _events.Length;

        /// <summary>How many events so far.</summary>
        public int Count => _count;

        /// <summary>A change read.</summary>
        public void Add(Kind kind, int owner, double time, ReadOnlySpan<byte> bytes)
        {
            _write.Write((byte)kind);
            _write.Write((short)owner);
            _write.Write(time);
            _write.Write(bytes.Length);
            _write.Write(bytes);
            _count++;
        }

        /// <summary>The whole file: its head, the setups, the tools, the capsules and the events.</summary>
        public byte[] Finish(
            Vector3 player, ulong world, long system, double seconds, long cycles, long torn, string note, IReadOnlyList<Setup> setups,
            IReadOnlyList<Tool?> tools, IReadOnlyList<(ulong Capsule, ulong A, ulong B)> capsules)
        {
            ArgumentNullException.ThrowIfNull(setups);
            ArgumentNullException.ThrowIfNull(tools);
            ArgumentNullException.ThrowIfNull(capsules);
            _write.Flush();
            using var stream = new MemoryStream();
            using (var write = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                write.Write(Magic);
                write.Write(player.X);
                write.Write(player.Y);
                write.Write(player.Z);
                write.Write(world);
                write.Write(system);
                write.Write(seconds);
                write.Write(cycles);
                write.Write(torn);
                write.Write(note ?? string.Empty);
                write.Write(setups.Count);
                for (var one = 0; one < setups.Count; one++)
                {
                    WriteSetup(write, setups[one]);
                    Tool? tool = one < tools.Count ? tools[one] : null;
                    write.Write(tool is not null);
                    if (tool is not null)
                    {
                        WriteTool(write, tool);
                    }
                }

                write.Write(capsules.Count);
                foreach ((ulong capsule, ulong a, ulong b) in capsules)
                {
                    write.Write(capsule);
                    write.Write(a);
                    write.Write(b);
                }

                write.Write(_count);
                write.Write(_events.GetBuffer(), 0, (int)_events.Length);
            }

            return stream.ToArray();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _write.Dispose();
            _events.Dispose();
        }
    }

    /// <summary>A series back from its file's bytes - null where they are not one.</summary>
    public static ClothWatchSeries? Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < Magic.Length || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, Magic.Length, bytes.Length - Magic.Length);
            using var read = new BinaryReader(stream, Encoding.UTF8);
            var player = new Vector3(read.ReadSingle(), read.ReadSingle(), read.ReadSingle());
            ulong world = read.ReadUInt64();
            long system = read.ReadInt64();
            double seconds = read.ReadDouble();
            long cycles = read.ReadInt64();
            long torn = read.ReadInt64();
            string note = read.ReadString();
            int count = read.ReadInt32();
            var setups = new List<Setup>(count);
            var tools = new List<Tool?>(count);
            for (var one = 0; one < count; one++)
            {
                setups.Add(ReadSetup(read));
                tools.Add(read.ReadBoolean() ? ReadTool(read) : null);
            }

            int shapes = read.ReadInt32();
            var capsules = new List<(ulong, ulong, ulong)>(shapes);
            for (var one = 0; one < shapes; one++)
            {
                capsules.Add((read.ReadUInt64(), read.ReadUInt64(), read.ReadUInt64()));
            }

            int events = read.ReadInt32();
            var list = new List<Event>(events);
            for (var one = 0; one < events; one++)
            {
                var kind = (Kind)read.ReadByte();
                int owner = read.ReadInt16();
                double time = read.ReadDouble();
                int length = read.ReadInt32();
                int at = Magic.Length + (int)stream.Position;
                if (length < 0 || at + length > bytes.Length)
                {
                    break;
                }

                list.Add(new Event(kind, owner, time, at, length));
                stream.Position += length;
            }

            return new ClothWatchSeries(bytes, player, world, system, seconds, cycles, torn, note, setups, tools, capsules, list);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    private static void WriteSetup(BinaryWriter write, Setup setup)
    {
        write.Write(setup.Address);
        Block(write, setup.Head);
        Block(write, setup.ContainerHead);
        write.Write(setup.Handles.Length);
        for (var one = 0; one < setup.Handles.Length; one++)
        {
            write.Write(setup.Handles[one]);
            write.Write(setup.Depth[one]);
            write.Write(setup.Parent[one]);
        }

        write.Write(setup.Triangles.Length);
        foreach (ClothTriangle t in setup.Triangles)
        {
            write.Write(t.A);
            write.Write(t.B);
            write.Write(t.C);
            write.Write(t.AB);
            write.Write(t.BC);
            write.Write(t.CA);
            write.Write(t.Area);
        }

        Ints(write, setup.Edges);
        Ints(write, setup.Pinned);
        write.Write(setup.Shocks.Length);
        foreach ((long parent, long child, float length) in setup.Shocks)
        {
            write.Write(parent);
            write.Write(child);
            write.Write(length);
        }

        write.Write(setup.Distances.Length);
        foreach (Distance d in setup.Distances)
        {
            write.Write(d.A);
            write.Write(d.B);
            write.Write(d.Rest);
            write.Write(d.Stiffness);
            write.Write(d.Contraction);
        }

        write.Write(setup.Bends.Length);
        foreach (Bend b in setup.Bends)
        {
            write.Write(b.A);
            write.Write(b.B);
            write.Write(b.C);
            write.Write(b.D);
            write.Write(b.Stiffness);
            write.Write(b.RestFold);
            write.Write(b.AllowedAngle);
        }

        Block(write, setup.Bones);
        write.Write(setup.Nearest);
    }

    private static Setup ReadSetup(BinaryReader read)
    {
        ulong address = read.ReadUInt64();
        byte[] head = Block(read);
        byte[] container = Block(read);
        int n = read.ReadInt32();
        var handles = new long[n];
        var depth = new int[n];
        var parent = new int[n];
        for (var one = 0; one < n; one++)
        {
            handles[one] = read.ReadInt64();
            depth[one] = read.ReadInt32();
            parent[one] = read.ReadInt32();
        }

        int triangles = read.ReadInt32();
        var t = new ClothTriangle[triangles];
        for (var one = 0; one < triangles; one++)
        {
            t[one] = new ClothTriangle(read.ReadInt32(), read.ReadInt32(), read.ReadInt32(), read.ReadSingle(), read.ReadSingle(), read.ReadSingle(), read.ReadSingle());
        }

        int[] edges = Ints(read);
        int[] pinned = Ints(read);
        int shocks = read.ReadInt32();
        var s = new (long, long, float)[shocks];
        for (var one = 0; one < shocks; one++)
        {
            s[one] = (read.ReadInt64(), read.ReadInt64(), read.ReadSingle());
        }

        int distances = read.ReadInt32();
        var d = new Distance[distances];
        for (var one = 0; one < distances; one++)
        {
            d[one] = new Distance(read.ReadInt32(), read.ReadInt32(), read.ReadSingle(), read.ReadSingle(), read.ReadSingle());
        }

        int bends = read.ReadInt32();
        var b = new Bend[bends];
        for (var one = 0; one < bends; one++)
        {
            b[one] = new Bend(read.ReadInt32(), read.ReadInt32(), read.ReadInt32(), read.ReadInt32(), read.ReadSingle(), read.ReadSingle(), read.ReadSingle());
        }

        byte[] bones = Block(read);
        float nearest = read.ReadSingle();
        return new Setup(address, head, container, handles, depth, parent, t, edges, pinned, s, d, b, bones, nearest);
    }

    private static void WriteTool(BinaryWriter write, Tool tool)
    {
        write.Write(tool.Name);
        ClothSettings s = tool.Settings;
        foreach (float value in (ReadOnlySpan<float>)[
            s.LinearStiffness, s.LinearAllowedContraction, s.BendStiffness, s.BendAllowedAngle, s.NormalViscosity, s.TangentViscosity,
            s.EnclosureRadius, s.EnclosureAngle, s.EnclosureRadiusAdditive, s.AnimationVelocityFactor, s.AnimationForceFactor,
            s.AnimationVelocityMultiplier, s.AnimationMaxVelocity, s.Gravity])
        {
            write.Write(value);
        }

        write.Write(s.ShockPropagation);
        write.Write(s.Collisions);
        write.Write(s.HeightmapCollisions);
        write.Write(s.RecreateOnScale);
        write.Write(tool.Rest.Length);
        foreach (Vector3 rest in tool.Rest)
        {
            write.Write(rest.X);
            write.Write(rest.Y);
            write.Write(rest.Z);
        }

        Ints(write, tool.Triangles);
        Ints(write, tool.Pinned);
    }

    private static Tool ReadTool(BinaryReader read)
    {
        string name = read.ReadString();
        var f = new float[14];
        for (var one = 0; one < f.Length; one++)
        {
            f[one] = read.ReadSingle();
        }

        var settings = new ClothSettings(
            f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13],
            read.ReadBoolean(), read.ReadBoolean(), read.ReadBoolean(), read.ReadBoolean());
        int n = read.ReadInt32();
        var rest = new Vector3[n];
        for (var one = 0; one < n; one++)
        {
            rest[one] = new Vector3(read.ReadSingle(), read.ReadSingle(), read.ReadSingle());
        }

        return new Tool(name, settings, rest, Ints(read), Ints(read));
    }

    private static void Block(BinaryWriter write, byte[] bytes)
    {
        write.Write(bytes.Length);
        write.Write(bytes);
    }

    private static byte[] Block(BinaryReader read) => read.ReadBytes(read.ReadInt32());

    private static void Ints(BinaryWriter write, int[] values)
    {
        write.Write(values.Length);
        foreach (int value in values)
        {
            write.Write(value);
        }
    }

    private static int[] Ints(BinaryReader read)
    {
        var values = new int[read.ReadInt32()];
        for (var one = 0; one < values.Length; one++)
        {
            values[one] = read.ReadInt32();
        }

        return values;
    }

    /// <summary>A Targets event's targets and lasts, by particle.</summary>
    public void Targets(Event one, Span<Vector3> target, Span<Vector3> last)
    {
        ReadOnlySpan<byte> bytes = Bytes(one);
        int n = Math.Min(Math.Min(target.Length, last.Length), bytes.Length / TargetBytes);
        for (var p = 0; p < n; p++)
        {
            target[p] = V3(bytes, p * TargetBytes);
            last[p] = V3(bytes, (p * TargetBytes) + 12);
        }
    }

    /// <summary>A Bodies event's places, moves, kinematic flags, moving flags and the places a kinematic body is moved onto, by particle; the step's seconds and the contacts' count.</summary>
    public float Bodies(Event one, Span<Vector3> place, Span<Vector3> move, Span<bool> kinematic, Span<bool> moving, Span<Vector3> moveTo, out int contacts)
    {
        ReadOnlySpan<byte> bytes = Bytes(one);
        contacts = 0;
        if (bytes.Length < 8)
        {
            return float.NaN;
        }

        float dt = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        int n = Math.Min(Math.Min(place.Length, move.Length), count);
        for (var p = 0; p < n; p++)
        {
            int at = 8 + (p * BodyBytes);
            if (at + BodyBytes > bytes.Length)
            {
                break;
            }

            place[p] = V3(bytes, at);
            move[p] = V3(bytes, at + 0x0C);
            if (p < moveTo.Length)
            {
                moveTo[p] = V3(bytes, at + 0x24);
            }

            if (p < kinematic.Length)
            {
                kinematic[p] = bytes[at + 0x34] != 0;
            }

            if (p < moving.Length)
            {
                moving[p] = bytes[at + 0x35] != 0;
            }
        }

        int tail = 8 + (count * BodyBytes);
        if (tail + 4 <= bytes.Length)
        {
            contacts = BinaryPrimitives.ReadInt32LittleEndian(bytes[tail..]);
        }

        return dt;
    }

    /// <summary>A Capsules event's spheres - each capsule's two, its first and its second - and their radii; how many capsules it holds.</summary>
    public int Shapes(Event one, Span<Vector3> centres, Span<float> radii)
    {
        ReadOnlySpan<byte> bytes = Bytes(one);
        if (bytes.Length < 4)
        {
            return 0;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        int n = Math.Min(count, Math.Min(centres.Length, radii.Length) / 2);
        for (var c = 0; c < n; c++)
        {
            int at = 4 + (c * ShapeBytes) + CapsuleBytes;
            if (at + 32 > bytes.Length)
            {
                return c;
            }

            centres[2 * c] = V3(bytes, at);
            radii[2 * c] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 12)..]);
            centres[(2 * c) + 1] = V3(bytes, at + 16);
            radii[(2 * c) + 1] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 28)..]);
        }

        return n;
    }

    private static Vector3 V3(ReadOnlySpan<byte> bytes, int at) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 4)..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 8)..]));

    /// <summary>
    /// Each object's particles numbered as its own: a shock link's ends, as the object keeps them - particle numbers where they are less
    /// than its count, else body handles - and -1 where neither.
    /// </summary>
    private static int Particle(Setup setup, long value, Dictionary<long, int> byHandle)
        => value >= 0 && value < setup.Count ? (int)value : byHandle.GetValueOrDefault(value, -1);

    /// <summary>The object's own cloth, as the game set it up: its constraints in the world's order, stepped on the targets it is handed.</summary>
    public static ClothRig Rig(Setup setup, ClothSettings settings, out string why)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(settings);
        var byHandle = new Dictionary<long, int>();
        for (var one = 0; one < setup.Count; one++)
        {
            byHandle.TryAdd(setup.Handles[one], one);
        }

        var pinned = new bool[setup.Count];
        foreach (int one in setup.Pinned)
        {
            if ((uint)one < (uint)setup.Count)
            {
                pinned[one] = true;
            }
        }

        var edges = setup.Distances.Where(d => d.A >= 0 && d.B >= 0).Select(d => new ClothEdge(d.A, d.B, -1, -1)).ToList();
        var bends = setup.Bends.Where(b => b.A >= 0 && b.B >= 0 && b.C >= 0 && b.D >= 0).ToList();
        var shocks = setup.Shocks
            .Select(s => (Particle(setup, s.Parent, byHandle), Particle(setup, s.Child, byHandle)))
            .Where(s => s.Item1 >= 0 && s.Item2 >= 0)
            .ToList();
        why = string.Create(CultureInfo.InvariantCulture,
            $"{edges.Count} of {setup.Distances.Length} distances, {bends.Count} of {setup.Bends.Length} bends and {shocks.Count} of {setup.Shocks.Length} shock links name particles of it");
        return ClothRig.Recorded(
            setup.Count,
            [.. setup.Triangles.Where(t => (uint)t.A < (uint)setup.Count && (uint)t.B < (uint)setup.Count && (uint)t.C < (uint)setup.Count)],
            edges,
            [.. bends.Select(b => new ClothEdge(b.A, b.B, b.C, b.D))],
            [.. bends.Select(b => b.RestFold)],
            pinned,
            [.. setup.Depth],
            [.. setup.Parent.Select(one => (uint)one < (uint)setup.Count ? one : -1)],
            shocks,
            settings,
            string.Create(CultureInfo.InvariantCulture, $"object 0x{setup.Address:X}"));
    }

    /// <summary>
    /// The reading: what was read and how often, each object's setup against the tool's own cloth, the pinned particles against the targets,
    /// every step taken again from the game's state under each order of a frame's phases, and the cloth run free on the game's targets.
    /// </summary>
    public IReadOnlyList<string> Report()
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"=== the reading (ClothWatchSeries): {Setups.Count} objects, {Events.Count} changes read in {Seconds:0.0} s over {Cycles:N0} reads of everything ({(Seconds > 0 ? Cycles / Seconds : 0):0} a second), {Torn:N0} of an object's reads torn by a step between its two halves and dropped{(Note.Length > 0 ? "; " + Note : string.Empty)}"),
        };
        Clock(lines);
        for (var o = 0; o < Setups.Count; o++)
        {
            lines.Add(string.Empty);
            try
            {
                Object(lines, o);
            }
            catch (ArgumentException e)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"--- object 0x{Setups[o].Address:X}: not stepped - {e.Message}"));
            }
            catch (IndexOutOfRangeException e)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"--- object 0x{Setups[o].Address:X}: not stepped - {e.Message}"));
            }
        }

        return lines;
    }

    private void Clock(List<string> lines)
    {
        var smoothed = new List<float>();
        var steps = new List<float>();
        foreach (Event one in Events)
        {
            if (one.Kind != Kind.Clock || one.Length < 16)
            {
                continue;
            }

            ReadOnlySpan<byte> bytes = Bytes(one);
            steps.Add(BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]));
            smoothed.Add(BinaryPrimitives.ReadSingleLittleEndian(bytes[12..]));
        }

        lines.Add(steps.Count == 0
            ? $"the system's clock: not read{(System < 0 ? " - its static was not found" : string.Empty)}"
            : string.Create(CultureInfo.InvariantCulture,
                $"the system's clock (module+0x{System:X}): {steps.Count} changes, its last step {Quantiles(steps)} s, its smoothed frame time {Quantiles(smoothed)} s"));
        int shapes = Events.Count(one => one.Kind == Kind.Capsules);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"the capsules round the player: {Capsules.Count} of the world 0x{World:X}, {shapes} changes read"));
    }

    /// <summary>Each order's fit for object <paramref name="o"/>: every step taken again from the game's state onto its targets (see <see cref="Report"/>).</summary>
    public IReadOnlyList<OrderFit> Fits(int o) => Object([], o);

    private List<OrderFit> Object(List<string> lines, int o)
    {
        Setup setup = Setups[o];
        Tool? tool = Tools[o];
        ClothSettings settings = setup.Settings(tool?.Settings.ShockPropagation ?? ClothSettings.Defaults.ShockPropagation);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"--- object 0x{setup.Address:X}: {setup.Count} particles, {setup.Pinned.Length} pinned, {setup.Triangles.Length} triangles, {setup.Distances.Length} distances, {setup.Bends.Length} bends, {setup.Shocks.Length} shock links, {setup.Bones.Length / 0xA0} bone records; {setup.Nearest:0} from the player across the ground; the tool's: {tool?.Name ?? "none matched"}"));
        lines.Add("  as the game holds it: " + settings.Say());
        ClothRig rig = Rig(setup, settings, out string why);
        lines.Add("  " + why);
        if (tool is not null)
        {
            Against(lines, setup, rig, tool);
        }

        var targets = Events.Select((e, index) => (e, index)).Where(x => x.e.Kind == Kind.Targets && x.e.Object == o).ToList();
        var bodies = Events.Select((e, index) => (e, index)).Where(x => x.e.Kind == Kind.Bodies && x.e.Object == o).ToList();
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  read: {targets.Count} changes of its targets, {bodies.Count} of its bodies ({(Seconds > 0 ? bodies.Count / Seconds : 0):0.#} a second)"));
        if (targets.Count == 0 || bodies.Count < 2)
        {
            lines.Add("  too few changes to step");
            return [];
        }

        Pattern(lines, o);
        return Steps(lines, rig, targets, bodies);
    }

    /// <summary>The tool's own cloth of the piece against the game's setup: the pinned, the triangles, the constraints and their order, depth, parent, the shock links, the rest folds.</summary>
    private static void Against(List<string> lines, Setup setup, ClothRig game, Tool tool)
    {
        ClothRig own = tool.Rig();
        var said = new List<string>();
        said.Add(own.Count == setup.Count ? "particles alike" : $"particles {own.Count} against the game's {setup.Count}");
        var pinned = new HashSet<int>(setup.Pinned);
        int pinnedOff = Enumerable.Range(0, own.Count).Count(one => own.Pinned[one] != pinned.Contains(one));
        said.Add(pinnedOff == 0 ? "pinned alike" : $"{pinnedOff} pinned otherwise");
        int triangles = Math.Min(own.Triangles.Length, setup.Triangles.Length);
        int cornersOff = Enumerable.Range(0, triangles).Count(one => own.Triangles[one].A != setup.Triangles[one].A || own.Triangles[one].B != setup.Triangles[one].B || own.Triangles[one].C != setup.Triangles[one].C);
        var scales = new List<float>();
        for (var one = 0; one < triangles; one++)
        {
            foreach ((float ours, float theirs) in (ReadOnlySpan<(float, float)>)[
                (own.Triangles[one].AB, setup.Triangles[one].AB), (own.Triangles[one].BC, setup.Triangles[one].BC), (own.Triangles[one].CA, setup.Triangles[one].CA)])
            {
                if (ours > 1e-4f)
                {
                    scales.Add(theirs / ours);
                }
            }
        }

        said.Add(cornersOff == 0 && own.Triangles.Length == setup.Triangles.Length
            ? string.Create(CultureInfo.InvariantCulture, $"triangles alike, the game's rest lengths {Quantiles(scales)} of the tool's")
            : $"{cornersOff} triangles' corners otherwise of {triangles}, counts {own.Triangles.Length} and {setup.Triangles.Length}");
        int edges = Math.Min(own.Edges.Length, game.Edges.Length);
        int edgeOff = Enumerable.Range(0, edges).Count(one => own.Edges[one].A != game.Edges[one].A || own.Edges[one].B != game.Edges[one].B);
        int firstEdge = Enumerable.Range(0, edges).FirstOrDefault(one => own.Edges[one].A != game.Edges[one].A || own.Edges[one].B != game.Edges[one].B, -1);
        said.Add(edgeOff == 0 && own.Edges.Length == game.Edges.Length
            ? "distances in the same order"
            : $"distances: {edgeOff} of {edges} otherwise (first at {firstEdge}), counts {own.Edges.Length} and {game.Edges.Length}");
        int bends = Math.Min(own.Bends.Length, game.Bends.Length);
        int bendOff = Enumerable.Range(0, bends).Count(one => own.Bends[one] != game.Bends[one]);
        var folds = new List<float>();
        for (var one = 0; one < bends; one++)
        {
            if (own.Bends[one] == game.Bends[one])
            {
                folds.Add(MathF.Abs(own.RestFolds[one] - game.RestFolds[one]));
            }
        }

        said.Add(string.Create(CultureInfo.InvariantCulture,
            $"bends: {bendOff} of {bends} otherwise, counts {own.Bends.Length} and {game.Bends.Length}; the rest folds of those alike off by {Quantiles(folds)}"));
        int depthOff = Enumerable.Range(0, Math.Min(own.Count, setup.Count)).Count(one => own.Depth[one] != setup.Depth[one]);
        int parentOff = Enumerable.Range(0, Math.Min(own.Count, setup.Count)).Count(one => own.Parent[one] != setup.Parent[one]);
        said.Add($"depth otherwise at {depthOff}, parent at {parentOff}");
        int shocks = Math.Min(own.Shocks.Length, game.Shocks.Length);
        int shockOff = Enumerable.Range(0, shocks).Count(one => own.Shocks[one] != game.Shocks[one]);
        said.Add($"shock links: {shockOff} of {shocks} otherwise, counts {own.Shocks.Length} and {game.Shocks.Length}");
        ClothSettings g = setup.Settings(tool.Settings.ShockPropagation);
        ClothSettings t = tool.Settings;
        var keys = new List<string>();
        void Key(string name, float ours, float theirs)
        {
            if (MathF.Abs(ours - theirs) > 1e-4f * MathF.Max(1f, MathF.Abs(ours)))
            {
                keys.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {ours:G6} against the game's {theirs:G6}"));
            }
        }

        Key("linear_stiffness", t.LinearStiffness, g.LinearStiffness);
        Key("linear_allowed_contraction", t.LinearAllowedContraction, g.LinearAllowedContraction);
        Key("bend_stiffness", t.BendStiffness, g.BendStiffness);
        Key("bend_allowed_angle", t.BendAllowedAngle, g.BendAllowedAngle);
        Key("normal_viscosity", t.NormalViscosity, g.NormalViscosity);
        Key("tangent_viscosity", t.TangentViscosity, g.TangentViscosity);
        Key("enclosure_radius", t.EnclosureRadius, g.EnclosureRadius);
        Key("enclosure_angle", t.EnclosureAngle, g.EnclosureAngle);
        Key("enclosure_radius_additive", t.EnclosureRadiusAdditive, g.EnclosureRadiusAdditive);
        Key("animation_velocity_factor", t.AnimationVelocityFactor, g.AnimationVelocityFactor);
        Key("animation_force_factor", t.AnimationForceFactor, g.AnimationForceFactor);
        Key("animation_velocity_multiplier", t.AnimationVelocityMultiplier, g.AnimationVelocityMultiplier);
        Key("animation_max_velocity", t.AnimationMaxVelocity, g.AnimationMaxVelocity);
        Key("gravity", t.Gravity, g.Gravity);
        if (t.Collisions != g.Collisions)
        {
            keys.Add($"enable_collisions {t.Collisions} against the game's {g.Collisions}");
        }

        said.Add(keys.Count == 0 ? "its simulation the game's" : "its simulation otherwise: " + string.Join(", ", keys));
        lines.Add("  the tool's own cloth against it: " + string.Join("; ", said));
    }

    /// <summary>How its changes came: alone or with the other in one read, and how many of each between two of the other.</summary>
    private void Pattern(List<string> lines, int o)
    {
        int together = 0, targetsAlone = 0, bodiesAlone = 0;
        var between = new SortedDictionary<int, int>();
        double lastTime = double.NaN;
        Kind lastKind = 0;
        int targetsSince = 0;
        bool seenBodies = false;
        foreach (Event one in Events)
        {
            if (one.Object != o || (one.Kind != Kind.Targets && one.Kind != Kind.Bodies))
            {
                continue;
            }

            if (one.Kind == Kind.Bodies)
            {
                if (lastKind == Kind.Targets && one.Time.Equals(lastTime))
                {
                    together++;
                    targetsAlone--;
                }
                else
                {
                    bodiesAlone++;
                }

                if (seenBodies)
                {
                    between[targetsSince] = between.GetValueOrDefault(targetsSince) + 1;
                }

                seenBodies = true;
                targetsSince = 0;
            }
            else
            {
                targetsAlone++;
                targetsSince++;
            }

            lastTime = one.Time;
            lastKind = one.Kind;
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  how the changes came: {together} reads with new targets and new bodies both, {targetsAlone} with new targets alone, {bodiesAlone} with new bodies alone; targets changes between two bodies changes: {string.Join(", ", between.Select(kv => $"{kv.Key} x{kv.Value}"))}"));
    }

    private List<OrderFit> Steps(List<string> lines, ClothRig rig, List<(Event e, int index)> targets, List<(Event e, int index)> bodies)
    {
        int n = rig.Count;
        int spheres = 2 * Capsules.Count;
        var capsules = Enumerable.Range(0, Capsules.Count).Select(c => (2 * c, (2 * c) + 1)).ToList();
        var world = ClothWorld.Recorded([rig], spheres, capsules);
        var shapeEvents = Events.Select((e, index) => (e, index)).Where(x => x.e.Kind == Kind.Capsules).ToList();
        var centresBefore = new Vector3[spheres];
        var centresNow = new Vector3[spheres];
        var radii = new float[spheres];
        var radiiBefore = new float[spheres];

        var oldTarget = new Vector3[n];
        var oldLast = new Vector3[n];
        var newTarget = new Vector3[n];
        var newLast = new Vector3[n];
        var placeBefore = new Vector3[n];
        var moveBefore = new Vector3[n];
        var place = new Vector3[n];
        var move = new Vector3[n];
        var kinematic = new bool[n];
        var moving = new bool[n];
        var moveTo = new Vector3[n];
        var scratch = new bool[n];
        var scratchTo = new Vector3[n];

        var errors = Orders.Select(_ => new List<float>()).ToArray();
        var moveErrors = Orders.Select(_ => new List<float>()).ToArray();
        var worst = Orders.Select(_ => new List<float>()).ToArray();
        var best = new int[Orders.Length];
        var still = new List<float>();
        var inertia = new List<float>();
        var pinnedNew = new List<float>();
        var pinnedOld = new List<float>();
        var pinnedMoveTo = new List<float>();
        int movingSet = 0, pinnedSeen = 0;
        int taken = 0, skipped = 0, mismatched = 0;
        var contacts = new List<int>();
        int lastSkipReason = 0;

        for (var j = 1; j < bodies.Count; j++)
        {
            (Event prev, int prevIndex) = bodies[j - 1];
            (Event cur, int curIndex) = bodies[j];

            // ONE CHANGE OF THE TARGETS BETWEEN THE TWO STATES: the frame's own; the targets before it are those in force at the first.
            int between = targets.Count(t => t.index > prevIndex && t.index < curIndex);
            int fresh = targets.FindLastIndex(t => t.index < curIndex);
            if (between != 1 || fresh < 1)
            {
                skipped++;
                lastSkipReason = between;
                continue;
            }

            Targets(targets[fresh].e, newTarget, newLast);
            Targets(targets[fresh - 1].e, oldTarget, oldLast);
            float dtBefore = Bodies(prev, placeBefore, moveBefore, scratch, scratch, scratchTo, out _);
            float dt = Bodies(cur, place, move, kinematic, moving, moveTo, out int touching);
            contacts.Add(touching);
            float joined = 0f;
            for (var p = 0; p < n; p++)
            {
                joined = MathF.Max(joined, Vector3.Distance(newLast[p], oldTarget[p]));
            }

            if (joined > OnTarget)
            {
                mismatched++;
            }

            // THE PINNED PARTICLES: where the game's step put them, against the targets new and old and the place they are moved onto.
            for (var p = 0; p < n; p++)
            {
                if (!rig.Pinned[p])
                {
                    continue;
                }

                pinnedSeen++;
                pinnedNew.Add(Vector3.Distance(place[p], newTarget[p]));
                pinnedOld.Add(Vector3.Distance(place[p], oldTarget[p]));
                pinnedMoveTo.Add(Vector3.Distance(moveTo[p], newTarget[p]));
                if (moving[p])
                {
                    movingSet++;
                }
            }

            // THE CAPSULES where they stood at each of the two states.
            Place(shapeEvents, prevIndex, centresBefore, radiiBefore);
            Place(shapeEvents, curIndex, centresNow, radii);

            float[] stepWorst = new float[Orders.Length];
            for (var order = 0; order < Orders.Length; order++)
            {
                world.Seed(0, placeBefore, moveBefore, oldTarget, oldLast, dtBefore);
                world.Spheres(centresBefore, centresNow, radii);
                switch (order)
                {
                    case 0:
                        world.Retarget(0, newTarget);
                        world.StepOnce(dt);
                        world.Settle();
                        break;
                    case 1:
                        world.StepOnce(dt);
                        world.Retarget(0, newTarget);
                        world.Settle();
                        break;
                    default:
                        world.Retarget(0, newTarget);
                        world.Settle();
                        world.StepOnce(dt);
                        break;
                }

                ReadOnlySpan<Vector3> ours = world.Particles(0);
                ReadOnlySpan<Vector3> ourMoves = world.Moves(0);
                for (var p = 0; p < n; p++)
                {
                    if (rig.Pinned[p])
                    {
                        continue;
                    }

                    float off = Vector3.Distance(ours[p], place[p]);
                    errors[order].Add(off);
                    moveErrors[order].Add(Vector3.Distance(ourMoves[p], move[p]));
                    stepWorst[order] = MathF.Max(stepWorst[order], off);
                }

                worst[order].Add(stepWorst[order]);
            }

            int chosen = 0;
            for (var order = 1; order < Orders.Length; order++)
            {
                if (stepWorst[order] < stepWorst[chosen])
                {
                    chosen = order;
                }
            }

            best[chosen]++;
            for (var p = 0; p < n; p++)
            {
                if (!rig.Pinned[p])
                {
                    still.Add(Vector3.Distance(placeBefore[p], place[p]));
                    inertia.Add(Vector3.Distance(placeBefore[p] + (moveBefore[p] * (dtBefore > 0f ? dt / dtBefore : 1f)), place[p]));
                }
            }

            taken++;
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  one step at a time: {taken} steps taken again from the game's state, {skipped} left out (not one change of the targets between two of the bodies; the last such had {lastSkipReason}), {mismatched} whose new targets' last was not the old targets (a frame read past)"));
        if (taken == 0)
        {
            return [];
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"    its free particles' moves in a step: {Quantiles(still)}; a step on by their own motion alone lands {Quantiles(inertia)} off the game's"));
        var fits = new List<OrderFit>(Orders.Length);
        for (var order = 0; order < Orders.Length; order++)
        {
            int exact = worst[order].Count(one => one < OnTarget);
            float[] sorted = [.. worst[order].Order()];
            fits.Add(new OrderFit(Orders[order], taken, exact, sorted[sorted.Length / 2], sorted[^1]));
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"    {Orders[order]}: places off by {Quantiles(errors[order])}, moves off by {Quantiles(moveErrors[order])}; a step's worst {Quantiles(worst[order])}; {exact} of {taken} steps within {OnTarget} throughout; the best of the three in {best[order]}"));
        }

        lines.Add(pinnedSeen == 0
            ? "    no particle pinned"
            : string.Create(CultureInfo.InvariantCulture,
                $"    the pinned particles after the step: off the new targets by {Quantiles(pinnedNew)}, off the old by {Quantiles(pinnedOld)}; the place each is moved onto off the new targets by {Quantiles(pinnedMoveTo)}; {movingSet} of {pinnedSeen} still flagged to be moved"));
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"    contacts the game's steps made: {Quantiles(contacts.Select(one => (float)one).ToList())} a step, {contacts.Count(one => one > 0)} of {contacts.Count} steps with any"));
        Free(lines, rig, world, targets, bodies, shapeEvents, centresBefore, centresNow, radii, radiiBefore);
        return fits;
    }

    /// <summary>The capsules' spheres as the last Capsules event read before <paramref name="index"/> holds them; nought where none was read.</summary>
    private void Place(List<(Event e, int index)> shapes, int index, Vector3[] centres, float[] radii)
    {
        int at = shapes.FindLastIndex(x => x.index < index);
        if (at < 0)
        {
            Array.Clear(centres);
            Array.Clear(radii);
            return;
        }

        Shapes(shapes[at].e, centres, radii);
    }

    /// <summary>
    /// The cloth run free from the game's first state on the game's own targets and steps, in the tool's order: how far it drifts from the
    /// game's over time.
    /// </summary>
    private void Free(
        List<string> lines, ClothRig rig, ClothWorld world, List<(Event e, int index)> targets, List<(Event e, int index)> bodies,
        List<(Event e, int index)> shapes, Vector3[] centresBefore, Vector3[] centresNow, float[] radii, float[] radiiBefore)
    {
        int n = rig.Count;
        var target = new Vector3[n];
        var last = new Vector3[n];
        var place = new Vector3[n];
        var move = new Vector3[n];
        var scratch = new bool[n];
        var scratchTo = new Vector3[n];
        int first = bodies.FindIndex(b => targets.Any(t => t.index < b.index));
        if (first < 0)
        {
            return;
        }

        int seed = targets.FindLastIndex(t => t.index < bodies[first].index);
        Targets(targets[seed].e, target, last);
        float dt0 = Bodies(bodies[first].e, place, move, scratch, scratch, scratchTo, out _);
        world.Seed(0, place, move, target, last, dt0);
        Place(shapes, bodies[first].index, centresNow, radii);
        double start = bodies[first].e.Time;
        var marks = new SortedDictionary<double, float>();
        double[] at = [0.1, 0.5, 1, 2, 4, 8];
        var all = new List<float>();
        int steps = 0;
        for (int j = first + 1; j < bodies.Count && steps < MostFreeSteps; j++)
        {
            (Event cur, int curIndex) = bodies[j];
            (Event _, int prevIndex) = bodies[j - 1];
            Array.Copy(centresNow, centresBefore, centresNow.Length);
            Array.Copy(radii, radiiBefore, radii.Length);
            Place(shapes, curIndex, centresNow, radii);
            foreach ((Event t, int index) in targets.Where(t => t.index > prevIndex && t.index < curIndex))
            {
                Targets(t, target, last);
                world.Retarget(0, target);
            }

            float dt = Bodies(cur, place, move, scratch, scratch, scratchTo, out _);
            world.Spheres(centresBefore, centresNow, radii);
            world.StepOnce(dt);
            world.Settle();
            steps++;
            ReadOnlySpan<Vector3> ours = world.Particles(0);
            float sum = 0f;
            int free = 0;
            for (var p = 0; p < n; p++)
            {
                if (!rig.Pinned[p])
                {
                    sum += Vector3.Distance(ours[p], place[p]);
                    free++;
                }
            }

            float mean = free > 0 ? sum / free : 0f;
            all.Add(mean);
            double since = cur.Time - start;
            foreach (double mark in at)
            {
                if (since >= mark && !marks.ContainsKey(mark))
                {
                    marks[mark] = mean;
                }
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  run free from the game's first state on its targets and steps, the tool's order ({steps} steps): its free particles off the game's by {string.Join(", ", marks.Select(kv => $"{kv.Value:0.###} at {kv.Key:0.#} s"))} on the mean; over the run {Quantiles(all)}"));
    }

    /// <summary>Median, ninetieth percentile and most of some values, or "none".</summary>
    private static string Quantiles(List<float> values)
    {
        if (values.Count == 0)
        {
            return "none";
        }

        float[] sorted = [.. values.Where(float.IsFinite).Order()];
        if (sorted.Length == 0)
        {
            return "none finite";
        }

        float At(double share) => sorted[Math.Clamp((int)Math.Round(share * (sorted.Length - 1)), 0, sorted.Length - 1)];
        return string.Create(CultureInfo.InvariantCulture, $"{At(0.5):G4} at the median, {At(0.9):G4} at 90%, {sorted[^1]:G4} at most");
    }
}
