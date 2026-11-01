using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// The engine's cloth frame by frame, for the capture's cloth watch part (cloth-watch.txt, <see cref="ClothWatchSeries.File"/> beside it):
/// the physics objects near the player found in memory as ClothLive finds them, then every particle's targets and every body's place and
/// motion read as often as they change for seconds while the player stands, takes a few steps and stands again - with the contacts each step
/// made, the capsules round him and the physics system's clock - so the tool's own cloth can be stepped offline on the game's own targets
/// from the game's own state and held against the game's next state, step by step (ClothWatchSeries.Report).
/// </summary>
/// <remarks>
/// WHAT IS READ, from the engine's code (v0.1.421, v0.1.424; schema ClothObject, ClothParticle, ClothContainer, ClothBody, PhysicsSystem,
/// PhysicsWorldColliders): an object's particles (+0x00, 0x58 each) hold their targets at +0x20 and +0x2C and their body's handle at +0x08,
/// its container (+0x10) the bodies (+0x88, 0x38 each) by the slot its +0x40 gives each handle (module+0x13A1100 reads a place so), its
/// last step's seconds (+0x08), the contacts the step made (+0x10), the distance (+0x108) and bend (+0x188) constraints. The world the
/// colliders are read from is the objects' manager (+0x150) - the physics system's +0x18, whose vtable's +0x08 is the world's update
/// (module+0x139E940), the world step's context's first word (module+0x139F150 reads its spheres at +0x158, their slots at +0x110, the
/// capsules at +0x1D8, their slots at +0x190 and the capsules it tests at +0x1A8). The system's static is found as the place in the
/// program's writable data whose +0x18 is the world; its clock is at +0xF0.
///
/// A READ IS KEPT WHOLE OR NOT AT ALL: each object's particles and bodies are read, then both again, and dropped where either changed
/// between - a step of the world's jobs between the two halves. Each change is kept with when it was read; a read whose targets and bodies
/// both changed keeps the targets first, so the series tells a frame whose phases fell between two reads from one that did not.
/// </remarks>
public sealed class ClothWatch
{
    /// <summary>How long it watches - CHOSEN: a few steps and the cloth settling after them.</summary>
    public const double Seconds = 10;

    /// <summary>How far across the ground an object's particles may stand from the player for it to be watched - CHOSEN.</summary>
    public const float Reach = 300f;

    /// <summary>How far across the ground a capsule's spheres may stand from the player for it to be kept - CHOSEN: his own and his pieces'.</summary>
    public const float ShapeReach = 500f;

    /// <summary>Most objects watched, the nearest first - CHOSEN.</summary>
    public const int MostObjects = 16;

    /// <summary>Most capsules kept - CHOSEN; the player's body carries 21.</summary>
    public const int MostCapsules = 64;

    /// <summary>Most contacts of one step kept - CHOSEN.</summary>
    public const int MostContacts = 512;

    /// <summary>Most bytes of changes kept - CHOSEN: ten seconds of the player's eight cloths are some twenty megabytes.</summary>
    public const long MostBytes = 64L << 20;

    /// <summary>Most objects the search keeps before the near ones are taken - CHOSEN: a town's players and their cloths are many.</summary>
    private const int MostFound = 1024;

    /// <summary>Most particles of one object read - a guard.</summary>
    private const int MostParticles = 4096;

    /// <summary>Most records of a vector read at the setup - a guard.</summary>
    private const int MostRecords = 1 << 16;

    /// <summary>How much of a container's head is read: past its collision flags and its bodies' box (+0x1AC to +0x1C7).</summary>
    private const int ContainerHeadBytes = 0x1C8;

    /// <summary>How much of the world's head is read: past the capsules' vector at +0x1D8.</summary>
    private const int WorldHeadBytes = 0x1F0;

    /// <summary>How much of the program's writable data is read at once while the system's static is looked for.</summary>
    private const int ScanChunk = 1 << 20;

    /// <summary>Where it stands, for the banner the capture's window shows the person at the game.</summary>
    public enum Stage
    {
        /// <summary>Not begun.</summary>
        Starting,

        /// <summary>Reading the player and searching memory: the person is to stand still.</summary>
        Searching,

        /// <summary>Watching: the person is to take a few steps, then stand completely still.</summary>
        Watching,

        /// <summary>Finished, or stopped.</summary>
        Done,
    }

    private readonly IMemoryReader _reader;
    private readonly OffsetSchema _schema;
    private readonly double _seconds;
    private readonly FloatHuntProgress _hunting = new();
    private volatile bool _stop;
    private volatile Stage _stage;
    private long _began;

    /// <param name="reader">The game's memory, past the tap.</param>
    /// <param name="schema">The offsets.</param>
    /// <param name="seconds">How long it watches - <see cref="Seconds"/> but in a test.</param>
    public ClothWatch(IMemoryReader reader, OffsetSchema schema, double seconds = Seconds)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        _reader = reader;
        _schema = schema;
        _seconds = seconds;
    }

    /// <summary>For the capture's window: what it is doing.</summary>
    public string Line { get; private set; } = "not started";

    /// <summary>For the capture's banner: where it stands.</summary>
    public Stage Now => _stage;

    /// <summary>For the capture's banner: the seconds left of the watch while it watches, its whole length before.</summary>
    public double Left => _began == 0 ? _seconds : Math.Max(0, _seconds - Stopwatch.GetElapsedTime(_began).TotalSeconds);

    /// <summary>For the capture's window: how far it is - the search its first half, the watch its second.</summary>
    public float Fraction
    {
        get
        {
            if (_began != 0)
            {
                return 0.5f + (0.5f * (float)Math.Clamp(Stopwatch.GetElapsedTime(_began).TotalSeconds / _seconds, 0, 1));
            }

            return _hunting.Budget > 0 ? 0.5f * (float)Math.Clamp((double)_hunting.Bytes / _hunting.Budget, 0, 1) : 0f;
        }
    }

    /// <summary>Stops the search and the watch early - what was read until then is written.</summary>
    public void Stop()
    {
        _stop = true;
        _hunting.Stop();
    }

    /// <summary>Watches, and says what it read and what the reading makes of it; the reads go to <paramref name="bin"/> where one is given.</summary>
    /// <param name="gameStatesStatic">The game-states static, the chain's root.</param>
    /// <param name="bin">Where the reads are written (<see cref="ClothWatchSeries.File"/> beside the part's text), or null.</param>
    /// <param name="regions">The game's memory regions, for the search.</param>
    /// <param name="tools">The tool's own cloths of the model the Monster Book shows - each object is held against the one of its piece.</param>
    public IReadOnlyList<string> Run(ulong gameStatesStatic, string? bin, IMemoryRegions? regions, IReadOnlyList<(string Name, ClothRig Rig)> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _stage = Stage.Searching;
        try
        {
            return Watch(gameStatesStatic, bin, regions, tools);
        }
        finally
        {
            _stage = Stage.Done;
        }
    }

    private IReadOnlyList<string> Watch(ulong gameStatesStatic, string? bin, IMemoryRegions? regions, IReadOnlyList<(string Name, ClothRig Rig)> tools)
    {
        if (regions is null)
        {
            return ["cloth watch: the reader cannot list the game's memory"];
        }

        Line = "reading the player";
        GameChainAddresses chain = GameChain.Resolve(_reader, _schema, gameStatesStatic);
        var entities = new EntityReader(_reader, _schema);
        if (chain.PlayerEntity == 0 || entities.ReadIdentity(chain.PlayerEntity) is not { } player)
        {
            return ["cloth watch: no player read - not in an area, or the chain did not resolve"];
        }

        IReadOnlyDictionary<string, ulong> components = entities.ReadComponents(player.Address, player.Details);
        ulong render = components.GetValueOrDefault("Render");
        ulong at = render == 0 ? 0 : render + (ulong)_schema.Structs["Render"].OffsetOf("CurrentWorldPosition");
        if (Place(at) is not { } place)
        {
            return ["cloth watch: the player's place did not read"];
        }

        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"cloth watch: the player {player.Path} at {Say(place)}; objects within {Reach:0} of him across the ground are watched for {_seconds:0} s"),
        };

        // THE OBJECTS, found as ClothLive finds them - by the defaults' tail and every tool cloth's own block - then those near him.
        Line = "looking for the cloth's physics objects";
        SortedDictionary<ulong, byte[]> found = ClothLive.Find(
            _reader, regions, [.. tools.Select(one => (one.Name, one.Rig.Settings))], _hunting, MostFound, "the search", lines);

        // WHERE HE STANDS NOW, not where he stood before the search: the 14:41 capture of v0.1.425 took his place at the press, he walked while
        // the search ran, and every object of his - read after it - stood 470 units from it and was left out.
        if (Place(at) is { } moved)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  after the search he stood at {Say(moved)}, {Vector2.Distance(new Vector2(moved.X, moved.Y), new Vector2(place.X, place.Y)):0} from the press"));
            place = moved;
        }

        var near = new List<(ulong Address, byte[] Head, byte[] Particles, float Nearest)>();
        var far = new List<(ulong Address, int Count, float Nearest)>();
        foreach ((ulong address, byte[] head) in found)
        {
            int count = Count(head, 0x00, ClothWatchSeries.ParticleBytes);
            if (count <= 0 || count > MostParticles)
            {
                continue;
            }

            byte[] particles = new byte[count * ClothWatchSeries.ParticleBytes];
            if (!_reader.TryRead(U64(head, 0x00), particles))
            {
                continue;
            }

            float nearest = float.MaxValue;
            for (var p = 0; p < count; p++)
            {
                Vector3 target = V3(particles, (p * ClothWatchSeries.ParticleBytes) + 0x20);
                nearest = MathF.Min(nearest, Vector2.Distance(new Vector2(target.X, target.Y), new Vector2(place.X, place.Y)));
            }

            if (nearest <= Reach)
            {
                near.Add((address, head, particles, nearest));
            }
            else
            {
                far.Add((address, count, nearest));
            }
        }

        near = [.. near.OrderBy(one => one.Nearest).Take(MostObjects)];
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{near.Count} of {found.Count} objects within {Reach:0} of the player across the ground"));
        if (far.Count > 0)
        {
            // THE OTHERS AND HOW FAR, so a watch that keeps none says why.
            lines.Add("  the others: " + string.Join(", ", far.OrderBy(one => one.Nearest).Take(24).Select(one =>
                string.Create(CultureInfo.InvariantCulture, $"0x{one.Address:X} ({one.Count} particles) {one.Nearest:0} away"))));
        }

        if (near.Count == 0 || _stop)
        {
            return lines;
        }

        // EACH OBJECT AS IT WAS SET UP, read once.
        Line = "reading the objects as they were set up";
        var watched = new List<Watched>();
        foreach ((ulong address, byte[] head, byte[] particles, float nearest) in near)
        {
            if (Setup(address, head, particles, nearest, lines) is { } one)
            {
                watched.Add(one);
            }
        }

        if (watched.Count == 0)
        {
            return lines;
        }

        // THE TOOL'S OWN CLOTH OF EACH PIECE: the same particles and the same triangles' corners in the same order.
        var matched = new List<ClothWatchSeries.Tool?>();
        foreach (Watched one in watched)
        {
            (string Name, ClothRig Rig)? same = tools.FirstOrDefault(tool =>
                tool.Rig.Count == one.Setup.Count
                && tool.Rig.Triangles.Length == one.Setup.Triangles.Length
                && tool.Rig.Triangles.Zip(one.Setup.Triangles).All(pair => pair.First.A == pair.Second.A && pair.First.B == pair.Second.B && pair.First.C == pair.Second.C));
            matched.Add(same is { Rig: not null } tool ? ClothWatchSeries.Tool.Of(tool.Name, tool.Rig) : null);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  object 0x{one.Setup.Address:X}: {one.Setup.Count} particles, {one.Setup.Pinned.Length} pinned, {one.Setup.Triangles.Length} triangles, {one.Setup.Distances.Length} distances, {one.Setup.Bends.Length} bends, {one.Setup.Shocks.Length} shock links; {one.Setup.Nearest:0} from him; its container 0x{one.Container:X}, {one.Bodies} bodies; the tool's own: {(same is { Rig: not null } t ? t.Name : "none")}"));
        }

        // THE WORLD'S CAPSULES ROUND HIM and the system's clock.
        ulong world = U64(watched[0].Setup.Head, 0x150);
        Shapes shapes = Capsules(world, place, lines);
        (ulong system, long systemOffset) = SystemOf(world, lines);

        // THE WATCH.
        using var writer = new ClothWatchSeries.Writer();
        long cycles = 0, torn = 0;
        string note = string.Empty;
        byte[] clock = new byte[16], lastClock = new byte[16];
        bool clockRead = false;
        byte[] his = new byte[12], lastHis = new byte[12];
        bool hisRead = false;
        _began = Stopwatch.GetTimestamp();
        _stage = Stage.Watching;
        Line = "watching the cloth";
        while (!_stop)
        {
            double now = Stopwatch.GetElapsedTime(_began).TotalSeconds;
            if (now >= _seconds)
            {
                break;
            }

            if (writer.Length >= MostBytes)
            {
                note = string.Create(CultureInfo.InvariantCulture, $"the changes reached {MostBytes >> 20} MB at {now:0.0} s and the watch ended there");
                break;
            }

            cycles++;
            if (system != 0 && _reader.TryRead(system + 0xF0, clock) && (!clockRead || !clock.AsSpan().SequenceEqual(lastClock)))
            {
                writer.Add(ClothWatchSeries.Kind.Clock, -1, now, clock);
                clock.CopyTo(lastClock, 0);
                clockRead = true;
            }

            if (at != 0 && _reader.TryRead(at, his) && (!hisRead || !his.AsSpan().SequenceEqual(lastHis)))
            {
                writer.Add(ClothWatchSeries.Kind.Player, -1, now, his);
                his.CopyTo(lastHis, 0);
                hisRead = true;
            }

            for (var o = 0; o < watched.Count; o++)
            {
                if (!watched[o].Read(_reader, writer, o, now))
                {
                    torn++;
                }
            }

            shapes.Read(_reader, writer, now);
            if ((cycles & 0xFF) == 0)
            {
                Line = string.Create(CultureInfo.InvariantCulture, $"watching the cloth: {now:0.0} of {_seconds:0} s, {writer.Count:N0} changes ({writer.Length / 1e6:0.0} MB)");
            }
        }

        double seconds = Stopwatch.GetElapsedTime(_began).TotalSeconds;
        if (_stop && note.Length == 0)
        {
            note = string.Create(CultureInfo.InvariantCulture, $"stopped at {seconds:0.0} s");
        }

        byte[] bytes = writer.Finish(
            place, world, systemOffset, Math.Min(seconds, _seconds), cycles, torn, note, [.. watched.Select(one => one.Setup)], matched, shapes.Kept);
        if (bin is not null)
        {
            File.WriteAllBytes(bin, bytes);
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"watched {Math.Min(seconds, _seconds):0.0} s: {cycles:N0} reads of everything, {torn:N0} of an object's torn, {writer.Count:N0} changes kept, {bytes.Length / 1e6:0.0} MB{(bin is null ? string.Empty : " written to " + ClothWatchSeries.File)}"));
        lines.Add(string.Empty);
        if (ClothWatchSeries.Read(bytes) is { } series)
        {
            lines.AddRange(series.Report());
        }

        return lines;
    }

    /// <summary>The player's place (Render.CurrentWorldPosition) at <paramref name="at"/>, or null where it does not read.</summary>
    private Vector3? Place(ulong at)
    {
        byte[] where = new byte[12];
        return at != 0 && _reader.TryRead(at, where) ? new Vector3(F(where, 0), F(where, 4), F(where, 8)) : null;
    }

    /// <summary>One object as it was set up and its reads since: its particles, its container's bodies by each particle's slot.</summary>
    private Watched? Setup(ulong at, byte[] head, byte[] particles, float nearest, List<string> lines)
    {
        int n = particles.Length / ClothWatchSeries.ParticleBytes;
        var handles = new long[n];
        var depth = new int[n];
        var parent = new int[n];
        ulong container = U64(particles, 0x10);
        for (var p = 0; p < n; p++)
        {
            int r = p * ClothWatchSeries.ParticleBytes;
            handles[p] = (long)U64(particles, r + 0x08);
            depth[p] = BinaryPrimitives.ReadInt32LittleEndian(particles.AsSpan(r + 0x38));
            parent[p] = BinaryPrimitives.ReadInt32LittleEndian(particles.AsSpan(r + 0x3C));
            if (U64(particles, r + 0x10) != container)
            {
                lines.Add($"  object 0x{at:X}: its particles live in more than one container - left out");
                return null;
            }
        }

        byte[] containerHead = new byte[ContainerHeadBytes];
        if (container == 0 || !_reader.TryRead(container, containerHead))
        {
            lines.Add($"  object 0x{at:X}: its container 0x{container:X} did not read - left out");
            return null;
        }

        // EACH PARTICLE'S BODY by its handle's slot (container +0x40).
        long most = handles.Length == 0 ? -1 : handles.Max();
        if (most < 0 || most >= MostRecords * 16L || handles.Any(one => one < 0))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  object 0x{at:X}: its handles reach {most} - left out"));
            return null;
        }

        byte[] slotBytes = new byte[(most + 1) * 8];
        if (!_reader.TryRead(U64(containerHead, 0x40), slotBytes))
        {
            lines.Add($"  object 0x{at:X}: its container's slots did not read - left out");
            return null;
        }

        int bodies = Count(containerHead, 0x88, ClothWatchSeries.BodyBytes);
        var slots = new int[n];
        for (var p = 0; p < n; p++)
        {
            ulong slot = U64(slotBytes, (int)handles[p] * 8);
            if (slot >= (ulong)bodies)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  object 0x{at:X}: particle {p}'s handle {handles[p]} has no body of the {bodies} - left out"));
                return null;
            }

            slots[p] = (int)slot;
        }

        var byHandle = new Dictionary<long, int>();
        for (var p = 0; p < n; p++)
        {
            byHandle.TryAdd(handles[p], p);
        }

        int Of(ulong handle) => byHandle.GetValueOrDefault((long)handle, -1);
        byte[] triangleBytes = Records(head, 0x48, 0x1C);
        var triangles = new ClothTriangle[triangleBytes.Length / 0x1C];
        for (var t = 0; t < triangles.Length; t++)
        {
            int r = t * 0x1C;
            triangles[t] = new ClothTriangle(
                BinaryPrimitives.ReadInt32LittleEndian(triangleBytes.AsSpan(r)),
                BinaryPrimitives.ReadInt32LittleEndian(triangleBytes.AsSpan(r + 4)),
                BinaryPrimitives.ReadInt32LittleEndian(triangleBytes.AsSpan(r + 8)),
                F(triangleBytes, r + 12),
                F(triangleBytes, r + 16),
                F(triangleBytes, r + 20),
                F(triangleBytes, r + 24));
        }

        byte[] edgeBytes = Records(head, 0x60, 0x10);
        var edges = new int[edgeBytes.Length / 4];
        for (var e = 0; e < edges.Length; e++)
        {
            edges[e] = BinaryPrimitives.ReadInt32LittleEndian(edgeBytes.AsSpan(e * 4));
        }

        byte[] pinnedBytes = Records(head, 0xA8, 8);
        var pinned = new int[pinnedBytes.Length / 8];
        for (var one = 0; one < pinned.Length; one++)
        {
            pinned[one] = (int)U64(pinnedBytes, one * 8);
        }

        byte[] shockBytes = Records(head, 0x110, 0x18);
        var shocks = new (long, long, float)[shockBytes.Length / 0x18];
        for (var one = 0; one < shocks.Length; one++)
        {
            int r = one * 0x18;
            shocks[one] = ((long)U64(shockBytes, r), (long)U64(shockBytes, r + 8), F(shockBytes, r + 16));
        }

        byte[] distanceBytes = Records(containerHead, 0x108, 0x20);
        var distances = new ClothWatchSeries.Distance[distanceBytes.Length / 0x20];
        for (var one = 0; one < distances.Length; one++)
        {
            int r = one * 0x20;
            distances[one] = new ClothWatchSeries.Distance(
                Of(U64(distanceBytes, r)), Of(U64(distanceBytes, r + 8)), F(distanceBytes, r + 0x10), F(distanceBytes, r + 0x14), F(distanceBytes, r + 0x1C));
        }

        byte[] bendBytes = Records(containerHead, 0x188, 0x30);
        var bends = new ClothWatchSeries.Bend[bendBytes.Length / 0x30];
        for (var one = 0; one < bends.Length; one++)
        {
            int r = one * 0x30;
            bends[one] = new ClothWatchSeries.Bend(
                Of(U64(bendBytes, r)), Of(U64(bendBytes, r + 8)), Of(U64(bendBytes, r + 16)), Of(U64(bendBytes, r + 24)),
                F(bendBytes, r + 0x20), F(bendBytes, r + 0x24), F(bendBytes, r + 0x28));
        }

        byte[] bones = Records(head, 0x78, 0xA0);
        var setup = new ClothWatchSeries.Setup(
            at, head, containerHead, handles, depth, parent, triangles, edges, pinned, shocks, distances, bends, bones, nearest);
        return new Watched(setup, U64(head, 0x00), container, bodies, slots);
    }

    /// <summary>A vector's records whole - empty where it does not hold together or does not read.</summary>
    private byte[] Records(byte[] owner, int at, int size)
    {
        int count = Count(owner, at, size);
        if (count <= 0 || count > MostRecords)
        {
            return [];
        }

        byte[] bytes = new byte[count * size];
        return _reader.TryRead(U64(owner, at), bytes) ? bytes : [];
    }

    /// <summary>How many records of <paramref name="size"/> bytes a std::vector at <paramref name="at"/> holds; -1 where its begin and end do not hold together.</summary>
    private static int Count(byte[] owner, int at, int size)
    {
        if (at + 16 > owner.Length)
        {
            return -1;
        }

        ulong begin = U64(owner, at), end = U64(owner, at + 8);
        if (begin == 0 || end < begin || (end - begin) % (ulong)size != 0)
        {
            return begin == 0 && end == 0 ? 0 : -1;
        }

        ulong count = (end - begin) / (ulong)size;
        return count > int.MaxValue ? -1 : (int)count;
    }

    /// <summary>The capsules round the player in the world: each kept one's handle and its two spheres' - and how each read is kept.</summary>
    private Shapes Capsules(ulong world, Vector3 place, List<string> lines)
    {
        byte[] head = new byte[WorldHeadBytes];
        if (world == 0 || !_reader.TryRead(world, head))
        {
            lines.Add($"the physics world 0x{world:X} did not read - no capsules");
            return new Shapes(world, []);
        }

        int spheres = Count(head, 0x158, ClothWatchSeries.SphereBytes);
        int capsules = Count(head, 0x1D8, ClothWatchSeries.CapsuleBytes);
        int active = Count(head, 0x1A8, 8);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"the physics world 0x{world:X} (the objects' manager, +0x150): {spheres} spheres, {capsules} capsules, {active} of them tested a step"));
        if (spheres <= 0 || capsules <= 0 || active <= 0 || spheres > MostRecords || capsules > MostRecords || active > MostRecords)
        {
            return new Shapes(world, []);
        }

        byte[] sphereBytes = new byte[spheres * ClothWatchSeries.SphereBytes];
        byte[] capsuleBytes = new byte[capsules * ClothWatchSeries.CapsuleBytes];
        byte[] activeBytes = new byte[active * 8];
        if (!_reader.TryRead(U64(head, 0x158), sphereBytes) || !_reader.TryRead(U64(head, 0x1D8), capsuleBytes) || !_reader.TryRead(U64(head, 0x1A8), activeBytes))
        {
            lines.Add("  its spheres, capsules or the capsules tested did not read");
            return new Shapes(world, []);
        }

        var kept = new List<(ulong Capsule, ulong A, ulong B)>();
        var said = new List<string>();
        for (var one = 0; one < active && kept.Count < MostCapsules; one++)
        {
            ulong handle = U64(activeBytes, one * 8);
            if (Slot(head, 0x190, handle) is not { } capsuleSlot || capsuleSlot >= (ulong)capsules)
            {
                continue;
            }

            int c = (int)capsuleSlot * ClothWatchSeries.CapsuleBytes;
            ulong a = U64(capsuleBytes, c), b = U64(capsuleBytes, c + 8);
            if (Slot(head, 0x110, a) is not { } sa || Slot(head, 0x110, b) is not { } sb || sa >= (ulong)spheres || sb >= (ulong)spheres)
            {
                continue;
            }

            Vector3 ca = V3(sphereBytes, ((int)sa * ClothWatchSeries.SphereBytes) + 0x24);
            Vector3 cb = V3(sphereBytes, ((int)sb * ClothWatchSeries.SphereBytes) + 0x24);
            if (Vector2.Distance(new Vector2(ca.X, ca.Y), new Vector2(place.X, place.Y)) > ShapeReach
                || Vector2.Distance(new Vector2(cb.X, cb.Y), new Vector2(place.X, place.Y)) > ShapeReach)
            {
                continue;
            }

            kept.Add((handle, a, b));
            if (said.Count < 2)
            {
                said.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  capsule {handle} (slot {capsuleSlot}): {Hex(capsuleBytes, c, c + ClothWatchSeries.CapsuleBytes)}; its first sphere {a} (slot {sa}): {Hex(sphereBytes, (int)sa * ClothWatchSeries.SphereBytes, ((int)sa + 1) * ClothWatchSeries.SphereBytes)}; its second {b} (slot {sb}): {Hex(sphereBytes, (int)sb * ClothWatchSeries.SphereBytes, ((int)sb + 1) * ClothWatchSeries.SphereBytes)}"));
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {kept.Count} capsules within {ShapeReach:0} of the player kept; the first two whole:"));
        lines.AddRange(said);
        return new Shapes(world, kept);
    }

    /// <summary>A handle's slot in the world's table at <paramref name="table"/> (a pointer to u64 slots by handle), null where it does not read.</summary>
    private ulong? Slot(byte[] head, int table, ulong handle)
    {
        if (handle >= (ulong)MostRecords * 16)
        {
            return null;
        }

        Span<byte> slot = stackalloc byte[8];
        return _reader.TryRead(U64(head, table) + (handle * 8), slot) ? BinaryPrimitives.ReadUInt64LittleEndian(slot) : null;
    }

    /// <summary>The physics system's static: the place in the program's writable data whose +0x18 is the world, its first word a vtable of the program.</summary>
    private (ulong Address, long Offset) SystemOf(ulong world, List<string> lines)
    {
        if (world == 0)
        {
            return (0, -1);
        }

        byte[] header = new byte[0x1000];
        if (!_reader.TryRead(_reader.ModuleBase, header))
        {
            lines.Add("the system's static: the program's header did not read");
            return (0, -1);
        }

        var candidates = new List<ulong>();
        foreach ((int rva, int size) in WritableSections(header))
        {
            byte[] chunk = new byte[ScanChunk];
            for (var from = 0; from < size; from += ScanChunk)
            {
                int length = Math.Min(ScanChunk, size - from);
                Span<byte> part = chunk.AsSpan(0, length & ~7);
                if (!_reader.TryRead(_reader.ModuleBase + (ulong)rva + (ulong)from, part))
                {
                    continue;
                }

                for (var at = 0; at + 8 <= part.Length; at += 8)
                {
                    if (BinaryPrimitives.ReadUInt64LittleEndian(part[at..]) == world && rva + from + at >= 0x18)
                    {
                        candidates.Add(_reader.ModuleBase + (ulong)(rva + from + at - 0x18));
                    }
                }
            }
        }

        byte[] system = new byte[0x100];
        foreach (ulong candidate in candidates)
        {
            if (!_reader.TryRead(candidate, system))
            {
                continue;
            }

            ulong vtable = U64(system, 0);
            float smoothed = F(system, 0xFC);
            bool ticking = smoothed.Equals(-1f) || (smoothed > 1e-4f && smoothed < 1f);
            if (vtable >= _reader.ModuleBase && vtable < _reader.ModuleBase + _reader.ModuleSize && ticking)
            {
                long offset = (long)(candidate - _reader.ModuleBase);
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"the physics system's static: module+0x{offset:X} ({candidates.Count} places in the program's data hold the world at +0x18), its vtable module+0x{vtable - _reader.ModuleBase:X}, its clock held {F(system, 0xF0):G6}, last step {F(system, 0xF4):G6}, since {F(system, 0xF8):G6}, smoothed {smoothed:G6}"));
                return (candidate, offset);
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"the physics system's static: not found ({candidates.Count} places in the program's data hold the world)"));
        return (0, -1);
    }

    /// <summary>The program's writable sections, read off its header in memory.</summary>
    private static List<(int Rva, int Size)> WritableSections(byte[] header)
    {
        var sections = new List<(int, int)>();
        int pe = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0x3C));
        if (pe <= 0 || pe + 24 > header.Length || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(pe)) != 0x00004550)
        {
            return sections;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(pe + 6));
        int optional = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(pe + 20));
        int first = pe + 24 + optional;
        for (var one = 0; one < count && first + ((one + 1) * 40) <= header.Length; one++)
        {
            int at = first + (one * 40);
            int size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(at + 8));
            int rva = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(at + 12));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(at + 36));
            if ((flags & 0x80000000u) != 0 && size > 0)
            {
                sections.Add((rva, size));
            }
        }

        return sections;
    }

    private static ulong U64(byte[] bytes, int at) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at));

    private static float F(byte[] bytes, int at) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at));

    private static Vector3 V3(byte[] bytes, int at) => new(F(bytes, at), F(bytes, at + 4), F(bytes, at + 8));

    private static string Say(Vector3 point) => string.Create(CultureInfo.InvariantCulture, $"({point.X:0.##}, {point.Y:0.##}, {point.Z:0.##})");

    private static string Hex(byte[] bytes, int from, int to)
    {
        var text = new System.Text.StringBuilder();
        for (int at = from; at + 4 <= to; at += 4)
        {
            text.Append(CultureInfo.InvariantCulture, $"{BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)):X8} ");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// One object watched: its particles' vector, its container, and each particle's body slot - read whole twice a time, its targets and
    /// bodies kept where they changed.
    /// </summary>
    private sealed class Watched
    {
        private readonly byte[] _first, _second;
        private readonly byte[] _head = new byte[0x98], _bodiesFirst, _bodiesSecond;
        private readonly byte[] _targets, _lastTargets;
        private readonly byte[] _bodies, _lastBodies;
        private readonly byte[] _contacts = new byte[MostContacts * ClothWatchSeries.ContactBytes];
        private readonly int[] _slots;
        private readonly int _least, _span;
        private byte[] _event = [];
        private bool _readTargets, _readBodies;

        public Watched(ClothWatchSeries.Setup setup, ulong particles, ulong container, int bodies, int[] slots)
        {
            Setup = setup;
            Particles = particles;
            Container = container;
            Bodies = bodies;
            _slots = slots;
            _least = slots.Length == 0 ? 0 : slots.Min();
            _span = slots.Length == 0 ? 0 : slots.Max() - _least + 1;
            int n = setup.Count;
            _first = new byte[n * ClothWatchSeries.ParticleBytes];
            _second = new byte[n * ClothWatchSeries.ParticleBytes];
            _bodiesFirst = new byte[_span * ClothWatchSeries.BodyBytes];
            _bodiesSecond = new byte[_span * ClothWatchSeries.BodyBytes];
            _targets = new byte[n * ClothWatchSeries.TargetBytes];
            _lastTargets = new byte[_targets.Length];
            _bodies = new byte[8 + (n * ClothWatchSeries.BodyBytes)];
            _lastBodies = new byte[_bodies.Length];
        }

        public ClothWatchSeries.Setup Setup { get; }

        public ulong Particles { get; }

        public ulong Container { get; }

        public int Bodies { get; }

        /// <summary>Reads it whole twice; false where the two halves differ (a step between them) and nothing is kept.</summary>
        public bool Read(IMemoryReader reader, ClothWatchSeries.Writer writer, int index, double now)
        {
            if (!reader.TryRead(Particles, _first) || !reader.TryRead(Container, _head))
            {
                return true;
            }

            ulong bodies = U64(_head, 0x88) + ((ulong)_least * ClothWatchSeries.BodyBytes);
            if (!reader.TryRead(bodies, _bodiesFirst))
            {
                return true;
            }

            int contacts = Contacts(reader);
            if (!reader.TryRead(Particles, _second) || !reader.TryRead(bodies, _bodiesSecond))
            {
                return true;
            }

            if (!_first.AsSpan().SequenceEqual(_second) || !_bodiesFirst.AsSpan().SequenceEqual(_bodiesSecond))
            {
                return false;
            }

            int n = Setup.Count;
            for (var p = 0; p < n; p++)
            {
                _first.AsSpan((p * ClothWatchSeries.ParticleBytes) + 0x20, ClothWatchSeries.TargetBytes).CopyTo(_targets.AsSpan(p * ClothWatchSeries.TargetBytes));
            }

            if (!_readTargets || !_targets.AsSpan().SequenceEqual(_lastTargets))
            {
                writer.Add(ClothWatchSeries.Kind.Targets, index, now, _targets);
                _targets.CopyTo(_lastTargets, 0);
                _readTargets = true;
            }

            _head.AsSpan(0x08, 4).CopyTo(_bodies);
            BinaryPrimitives.WriteInt32LittleEndian(_bodies.AsSpan(4), n);
            for (var p = 0; p < n; p++)
            {
                _bodiesFirst.AsSpan((_slots[p] - _least) * ClothWatchSeries.BodyBytes, ClothWatchSeries.BodyBytes)
                    .CopyTo(_bodies.AsSpan(8 + (p * ClothWatchSeries.BodyBytes)));
            }

            if (!_readBodies || !_bodies.AsSpan().SequenceEqual(_lastBodies))
            {
                // ONE BUFFER FOR THE EVENT: the bodies, the contacts' count and the contacts after them.
                int contactBytes = contacts * ClothWatchSeries.ContactBytes;
                if (_event.Length != _bodies.Length + 4 + contactBytes)
                {
                    _event = new byte[_bodies.Length + 4 + contactBytes];
                }

                _bodies.CopyTo(_event, 0);
                BinaryPrimitives.WriteInt32LittleEndian(_event.AsSpan(_bodies.Length), contacts);
                _contacts.AsSpan(0, contactBytes).CopyTo(_event.AsSpan(_bodies.Length + 4));
                writer.Add(ClothWatchSeries.Kind.Bodies, index, now, _event);
                _bodies.CopyTo(_lastBodies, 0);
                _readBodies = true;
            }

            return true;
        }

        /// <summary>The contacts the container's last step made (+0x10), at most <see cref="MostContacts"/>, into its buffer: how many.</summary>
        private int Contacts(IMemoryReader reader)
        {
            int count = Math.Min(Count(_head, 0x10, ClothWatchSeries.ContactBytes), MostContacts);
            return count > 0 && reader.TryRead(U64(_head, 0x10), _contacts.AsSpan(0, count * ClothWatchSeries.ContactBytes)) ? count : 0;
        }
    }

    /// <summary>
    /// The capsules kept round the player, read each time through the world's slots - five reads a time: the world's head, the two slot
    /// tables over the kept handles, and the capsules' and spheres' records over the slots they reach - and kept where they changed.
    /// </summary>
    private sealed class Shapes
    {
        private readonly ulong _world;
        private readonly byte[] _head = new byte[WorldHeadBytes];
        private readonly ulong _leastCapsule, _leastSphere;
        private readonly byte[] _capsuleSlots, _sphereSlots;
        private readonly byte[] _payload, _last;
        private byte[] _capsules = [], _spheres = [];
        private bool _read;

        public Shapes(ulong world, List<(ulong Capsule, ulong A, ulong B)> kept)
        {
            _world = world;
            Kept = kept;
            if (kept.Count == 0)
            {
                _capsuleSlots = _sphereSlots = _payload = _last = [];
                return;
            }

            _leastCapsule = kept.Min(one => one.Capsule);
            _leastSphere = kept.Min(one => Math.Min(one.A, one.B));
            _capsuleSlots = new byte[(int)(kept.Max(one => one.Capsule) - _leastCapsule + 1) * 8];
            _sphereSlots = new byte[(int)(kept.Max(one => Math.Max(one.A, one.B)) - _leastSphere + 1) * 8];
            _payload = new byte[4 + (kept.Count * ClothWatchSeries.ShapeBytes)];
            _last = new byte[_payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(_payload, kept.Count);
        }

        public List<(ulong Capsule, ulong A, ulong B)> Kept { get; }

        public void Read(IMemoryReader reader, ClothWatchSeries.Writer writer, double now)
        {
            if (Kept.Count == 0
                || !reader.TryRead(_world, _head)
                || !reader.TryRead(U64(_head, 0x190) + (_leastCapsule * 8), _capsuleSlots)
                || !reader.TryRead(U64(_head, 0x110) + (_leastSphere * 8), _sphereSlots))
            {
                return;
            }

            ulong CapsuleSlot(ulong handle) => U64(_capsuleSlots, (int)(handle - _leastCapsule) * 8);
            ulong SphereSlot(ulong handle) => U64(_sphereSlots, (int)(handle - _leastSphere) * 8);
            ulong leastC = ulong.MaxValue, mostC = 0, leastS = ulong.MaxValue, mostS = 0;
            foreach ((ulong capsule, ulong a, ulong b) in Kept)
            {
                ulong c = CapsuleSlot(capsule);
                leastC = Math.Min(leastC, c);
                mostC = Math.Max(mostC, c);
                foreach (ulong s in (ReadOnlySpan<ulong>)[SphereSlot(a), SphereSlot(b)])
                {
                    leastS = Math.Min(leastS, s);
                    mostS = Math.Max(mostS, s);
                }
            }

            if (mostC - leastC >= MostRecords || mostS - leastS >= MostRecords)
            {
                return;
            }

            int capsuleBytes = (int)(mostC - leastC + 1) * ClothWatchSeries.CapsuleBytes;
            int sphereBytes = (int)(mostS - leastS + 1) * ClothWatchSeries.SphereBytes;
            if (_capsules.Length != capsuleBytes)
            {
                _capsules = new byte[capsuleBytes];
            }

            if (_spheres.Length != sphereBytes)
            {
                _spheres = new byte[sphereBytes];
            }

            if (!reader.TryRead(U64(_head, 0x1D8) + (leastC * ClothWatchSeries.CapsuleBytes), _capsules)
                || !reader.TryRead(U64(_head, 0x158) + (leastS * ClothWatchSeries.SphereBytes), _spheres))
            {
                return;
            }

            for (var c = 0; c < Kept.Count; c++)
            {
                (ulong capsule, ulong a, ulong b) = Kept[c];
                int at = 4 + (c * ClothWatchSeries.ShapeBytes);
                _capsules.AsSpan((int)(CapsuleSlot(capsule) - leastC) * ClothWatchSeries.CapsuleBytes, ClothWatchSeries.CapsuleBytes).CopyTo(_payload.AsSpan(at));
                int into = at + ClothWatchSeries.CapsuleBytes;
                foreach (ulong handle in (ReadOnlySpan<ulong>)[a, b])
                {
                    int sphere = (int)(SphereSlot(handle) - leastS) * ClothWatchSeries.SphereBytes;
                    _spheres.AsSpan(sphere + 0x24, 12).CopyTo(_payload.AsSpan(into));
                    _spheres.AsSpan(sphere + 0x30, 4).CopyTo(_payload.AsSpan(into + 12));
                    into += 16;
                }
            }

            if (!_read || !_payload.AsSpan().SequenceEqual(_last))
            {
                writer.Add(ClothWatchSeries.Kind.Capsules, -1, now, _payload);
                _payload.CopyTo(_last, 0);
                _read = true;
            }
        }
    }
}
