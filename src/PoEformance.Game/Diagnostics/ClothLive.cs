using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Game.Files;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// The engine's cloth as it runs (cloth-live.txt): every physics object of a ClientAnimationController found in memory, its parameters,
/// particles, triangles, edges and constraints, and the physics world's containers its particles live in - where the world keeps each
/// particle's place is looked for round them, so the next build can read the cloth tick by tick and step it the world's way.
/// </summary>
/// <remarks>
/// WHAT IS KNOWN, READ FROM THE ENGINE'S CODE (v0.1.420; the build of 2026-10-27, see ClothSettings): a controller with a simulation makes
/// one physics object of 0x168 bytes (constructor module+0x13A7A60) - a std::vector of particles of 0x58 bytes at +0x00, of constraints of
/// 0x28 at +0x18, of triangles of 0x1C at +0x48 (three vertex numbers, the three edges' rest lengths, the area), of edges of 0x10 at +0x60,
/// the pinned particles' numbers (eight bytes each) at +0xA8, then the parameters as eleven floats from +0xD8 (ClothSettings.ObjectBlock),
/// and the world's manager and the object's handle in it at +0x150 and +0x158. A particle holds at +0x08 its body's handle in the world, at
/// +0x10 the world's container of it, at +0x20 its animated place this step and at +0x2C the last (the targets the step pulls towards), at
/// +0x38 its depth from the pinned ones and at +0x3C its parent (both read by the enclosure, module+0x13A8440). The bodies are the world's,
/// handled through virtual calls on the container (place +0x68, velocity +0x70, set place +0x88, add force +0x90, set velocity +0xA0, is
/// kinematic +0xB0, make a body +0xD8, a distance constraint +0xE0) - a world with no RTTI and no known library, whose integrator and
/// constraint solver are what is still to be read.
///
/// SO THE OBJECTS ARE FOUND BY THEIR TAIL: animation_max_velocity, the scale, gravity at +0xF8..+0x100 - 1000, anything, 980 where an .ao
/// keeps the defaults, as nearly every set does (gravity is set in 377 of 8,147 simulated files, the max velocity in 204) - and kept where
/// the vectors before it hold together. Each is read whole, its particles twice a moment apart (a moving cloth's targets move), and its
/// containers' memory searched for its particles' places: a float triple within <see cref="Near"/> of a particle's animated place, which
/// is where the world keeps the body (the targets pull the bodies towards them, the enclosure holds them within a few units).
/// </remarks>
public static class ClothLive
{
    /// <summary>Where the tail looked for stands in an object: animation_max_velocity, then the scale, then gravity.</summary>
    public const int TailAt = 0xF8;

    /// <summary>Where the parameters start in an object.</summary>
    public const int BlockAt = 0xD8;

    /// <summary>How long an object is.</summary>
    public const int ObjectBytes = 0x168;

    /// <summary>How long a particle is.</summary>
    public const int ParticleBytes = 0x58;

    /// <summary>The most objects read whole - CHOSEN.</summary>
    public const int MostObjects = 64;

    /// <summary>The most particles of one object written - CHOSEN; all are counted.</summary>
    public const int MostParticles = 128;

    /// <summary>How much of a container is read for its particles' places - CHOSEN.</summary>
    public const int ContainerBytes = 512 * 1024;

    /// <summary>How near a particle's animated place a float triple must lie to be taken for its body's place, in world units - CHOSEN.</summary>
    public const float Near = 40f;

    /// <summary>The particles whose places are looked for in a container - CHOSEN.</summary>
    public const int SoughtParticles = 12;

    /// <summary>How long between the two reads of an object's particles.</summary>
    public static readonly TimeSpan Between = TimeSpan.FromMilliseconds(150);

    /// <summary>A capture's progress and its stop.</summary>
    public sealed class Running
    {
        private readonly FloatHuntProgress _hunt = new();
        private int _phase;

        /// <summary>The sweep's progress.</summary>
        public FloatHuntProgress Hunt => _hunt;

        /// <summary>Asks it to stop, keeping what it found.</summary>
        public void Stop() => _hunt.Stop();

        /// <summary>The window's line.</summary>
        public string Line => Volatile.Read(ref _phase) switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"looking for the cloth's physics objects: {_hunt.Bytes / 1e9:0.0} GB"),
            1 => "reading the objects and their particles",
            _ => "looking for the particles' places in the world's containers",
        };

        /// <summary>How far it is.</summary>
        public float Fraction => Volatile.Read(ref _phase) switch
        {
            0 => _hunt.Budget > 0 ? 0.7f * Math.Clamp((float)_hunt.Bytes / _hunt.Budget, 0f, 1f) : 0.1f,
            1 => 0.75f,
            _ => 0.9f,
        };

        internal void Phase(int phase) => Volatile.Write(ref _phase, phase);
    }

    /// <summary>The report: what was looked for, every object found and its particles, constraints and containers.</summary>
    /// <param name="reader">The game's memory, past the tap.</param>
    /// <param name="regions">Its regions.</param>
    /// <param name="extra">More tails to look for: each a name and its settings, whose whole block of parameters is the needle - the pieces the Monster Book shows, say.</param>
    /// <param name="running">The progress and the stop.</param>
    public static List<string> Run(IMemoryReader reader, IMemoryRegions regions, IReadOnlyList<(string Name, ClothSettings Settings)> extra, Running running)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(extra);
        ArgumentNullException.ThrowIfNull(running);
        var lines = new List<string>();
        var needles = new List<FloatNeedle>
        {
            new("the defaults' tail: animation_max_velocity 1000, the scale, gravity 980", [1000f, float.NaN, 980f], 0f, Most: 4096),
        };
        foreach ((string name, ClothSettings settings) in extra)
        {
            float[] block = settings.ObjectBlock();
            block[9] = float.NaN;
            needles.Add(new FloatNeedle($"{name}'s parameters", block, 1e-4f, Most: 256));
        }

        var writable = new WritableRegions(regions);
        FloatHuntResult swept = FloatHunt.Run(reader, writable, needles, 0, running.Hunt, budget: 2 * HeapScan.ByteBudget);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"cloth live: {needles.Count} needles, {swept.BytesScanned / 1e9:0.0} GB of {swept.RegionsWalked:N0} regions on {swept.Workers} threads in {swept.Took.TotalSeconds:0.0} s{(swept.Stopped ? " (stopped)" : swept.Truncated ? " (its budget ended it)" : string.Empty)}, leaving out {writable.Unwritable / 1e9:0.0} GB the game cannot write and {writable.Slow / 1e9:0.0} GB of the driver's heaps; {swept.Sightings.Count} places found"));
        for (var one = 0; one < needles.Count; one++)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  needle {one}: {needles[one].Name}: {swept.Sightings.Count(sight => sight.Needle == one)} places{(swept.Capped.Contains(one) ? " (capped)" : string.Empty)}"));
        }

        running.Phase(1);
        var objects = new SortedDictionary<ulong, byte[]>();
        int refused = 0;
        foreach (FloatSighting sight in swept.Sightings)
        {
            ulong at = sight.At - (ulong)(sight.Needle == 0 ? TailAt : BlockAt);
            if (objects.ContainsKey(at) || objects.Count >= MostObjects)
            {
                continue;
            }

            byte[] bytes = new byte[ObjectBytes];
            if (reader.TryRead(at, bytes) && Holds(bytes))
            {
                objects[at] = bytes;
            }
            else
            {
                refused++;
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"{objects.Count} physics objects whose vectors hold together (particles of 0x58, constraints of 0x28, triangles of 0x1C, edges of 0x10), {refused} places refused"));

        Thread.Sleep(Between);
        var containers = new SortedDictionary<ulong, List<(ulong Object, int Particle, Vector3 Target)>>();
        var managers = new SortedSet<ulong>();
        foreach ((ulong at, byte[] bytes) in objects)
        {
            Object(reader, at, bytes, lines, containers);
            managers.Add(U64(bytes, 0x150));
        }

        running.Phase(2);
        lines.Add(string.Empty);
        lines.Add("=== the world's containers the particles live in: each one's first words and vtable (its entries as module offsets, for the code); and where its particles' animated places have a float triple near them in its first 512 KB - where the world keeps a body's place");
        foreach ((ulong container, List<(ulong Object, int Particle, Vector3 Target)> particles) in containers)
        {
            Container(reader, container, particles, lines);
        }

        lines.Add(string.Empty);
        lines.Add("=== the world's managers the objects name (+0x150): each one's first words and vtable");
        foreach (ulong manager in managers.Where(one => one != 0))
        {
            Words(reader, manager, 0x200, $"manager 0x{manager:X}", lines);
        }

        return lines;
    }

    /// <summary>Whether an object's vectors hold together: begin, end and capacity in order, whole records, and a sane number of them.</summary>
    /// <param name="bytes">The object's first <see cref="ObjectBytes"/> bytes.</param>
    public static bool Holds(byte[] bytes)
    {
        static bool Vector(byte[] bytes, int at, int record, int most)
        {
            ulong begin = U64(bytes, at), end = U64(bytes, at + 8), capacity = U64(bytes, at + 16);
            if (begin == 0 && end == 0 && capacity == 0)
            {
                return true;
            }

            return begin != 0 && begin <= end && end <= capacity && (end - begin) % (ulong)record == 0 && (end - begin) / (ulong)record <= (ulong)most;
        }

        ulong particles = U64(bytes, 0), past = U64(bytes, 8);
        return particles != 0 && past > particles
            && Vector(bytes, 0x00, ParticleBytes, 16384)
            && Vector(bytes, 0x18, 0x28, 65536)
            && Vector(bytes, 0x48, 0x1C, 65536)
            && Vector(bytes, 0x60, 0x10, 65536)
            && Vector(bytes, 0xA8, 8, 16384);
    }

    private static void Object(
        IMemoryReader reader, ulong at, byte[] bytes, List<string> lines, SortedDictionary<ulong, List<(ulong Object, int Particle, Vector3 Target)>> containers)
    {
        int Count(int offset, int record) => (int)((U64(bytes, offset + 8) - U64(bytes, offset)) / (ulong)record);
        int particles = Count(0x00, ParticleBytes), constraints = Count(0x18, 0x28), triangles = Count(0x48, 0x1C), edges = Count(0x60, 0x10), pinned = Count(0xA8, 8);
        float F(int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));
        lines.Add(string.Empty);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"--- object 0x{at:X}: {particles} particles, {pinned} pinned, {triangles} triangles, {edges} edges, {constraints} constraints; normal viscosity {F(0xD8):G6}, tangent {F(0xDC):G6}, enclosure radius {F(0xE0):G6} angle {F(0xE4):G6} additive {F(0xE8):G6}, animation force {F(0xEC):G6} velocity {F(0xF0):G6} multiplier {F(0xF4):G6} max {F(0xF8):G6}, scale {F(0xFC):G6}, gravity {F(0x100):G6}; manager 0x{U64(bytes, 0x150):X} handle {(long)U64(bytes, 0x158)}"));
        lines.Add("  words: " + Hex(bytes, 0, ObjectBytes));

        int read = Math.Min(particles, MostParticles);
        byte[] first = new byte[read * ParticleBytes], second = new byte[read * ParticleBytes];
        bool once = reader.TryRead(U64(bytes, 0), first);
        Thread.Sleep(Between);
        bool twice = reader.TryRead(U64(bytes, 0), second);
        if (!once)
        {
            lines.Add("  its particles did not read");
            return;
        }

        int moved = 0;
        lines.Add("  particle\tholder vtable\thandle\tcontainer\tflag\tanimated place\tlast\tdepth\tparent\tmoved since (a moment on)\t+0x40..+0x57");
        for (var one = 0; one < read; one++)
        {
            int p = one * ParticleBytes;
            Vector3 target = V3(first, p + 0x20), last = V3(first, p + 0x2C);
            float shift = twice ? Vector3.Distance(target, V3(second, p + 0x20)) : float.NaN;
            if (shift > 1e-3f)
            {
                moved++;
            }

            ulong container = U64(first, p + 0x10);
            if (container != 0 && one < SoughtParticles * 4)
            {
                if (!containers.TryGetValue(container, out List<(ulong Object, int Particle, Vector3 Target)>? list))
                {
                    containers[container] = list = [];
                }

                if (list.Count < SoughtParticles)
                {
                    list.Add((at, one, target));
                }
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {one}\t{U64(first, p):X}\t{(long)U64(first, p + 0x08)}\t{container:X}\t{first[p + 0x18]}\t{Say(target)}\t{Say(last)}\t{BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(p + 0x38))}\t{BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(p + 0x3C))}\t{shift:0.###}\t{Hex(first, p + 0x40, p + 0x58)}"));
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {moved} of {read} particles' animated places moved in {Between.TotalMilliseconds:0} ms"));
        Records(reader, U64(bytes, 0x48), triangles, 0x1C, 32, "triangles (three vertex numbers, three rest lengths, the area)", lines, Triangle);
        Records(reader, U64(bytes, 0x60), edges, 0x10, 32, "edges (16 bytes)", lines, null);
        Records(reader, U64(bytes, 0x18), constraints, 0x28, 16, "constraints (0x28 bytes: the world's handles)", lines, null);
        Records(reader, U64(bytes, 0xA8), pinned, 8, 64, "pinned particles", lines, null);
    }

    private static string Triangle(byte[] record) => string.Create(CultureInfo.InvariantCulture,
        $"{BinaryPrimitives.ReadInt32LittleEndian(record)} {BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(4))} {BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(8))}  lengths {BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(12)):0.###} {BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(16)):0.###} {BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(20)):0.###}  area {BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(24)):0.###}");

    private static void Records(IMemoryReader reader, ulong at, int count, int size, int most, string title, List<string> lines, Func<byte[], string>? say)
    {
        int read = Math.Min(count, most);
        if (read <= 0 || at == 0)
        {
            return;
        }

        byte[] all = new byte[read * size];
        if (!reader.TryRead(at, all))
        {
            lines.Add($"  {title}: did not read");
            return;
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {title}, {read} of {count}:"));
        for (var one = 0; one < read; one++)
        {
            byte[] record = all.AsSpan(one * size, size).ToArray();
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"    {one}\t{(say is null ? Hex(record, 0, size) : say(record) + "\t" + Hex(record, 0, size))}"));
        }
    }

    private static void Container(IMemoryReader reader, ulong container, List<(ulong Object, int Particle, Vector3 Target)> particles, List<string> lines)
    {
        lines.Add(string.Empty);
        byte[] head = new byte[0x200];
        if (!reader.TryRead(container, head))
        {
            lines.Add($"--- container 0x{container:X}: did not read");
            return;
        }

        ulong vtable = U64(head, 0);
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"--- container 0x{container:X}: vtable {Module(reader, vtable)}, {particles.Count} particles sought"));
        lines.Add("  words: " + Hex(head, 0, head.Length));
        VTable(reader, vtable, lines);

        // ITS MEMORY, page by page until one does not read.
        var memory = new List<byte>();
        byte[] page = new byte[4096];
        for (var at = 0; at < ContainerBytes; at += page.Length)
        {
            if (!reader.TryRead(container + (ulong)at, page))
            {
                break;
            }

            memory.AddRange(page);
        }

        byte[] all = [.. memory];
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {all.Length / 1024} KB read from it"));
        foreach ((ulong owner, int particle, Vector3 target) in particles)
        {
            var near = new List<string>();
            for (var at = 0; at + 12 <= all.Length && near.Count < 8; at += 4)
            {
                Vector3 place = V3(all, at);
                if (float.IsFinite(place.X) && Vector3.Distance(place, target) < Near && place.LengthSquared() > 1f)
                {
                    near.Add(string.Create(CultureInfo.InvariantCulture, $"+0x{at:X} {Say(place)} ({Vector3.Distance(place, target):0.##} off)"));
                }
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  object 0x{owner:X} particle {particle} at {Say(target)}: {(near.Count == 0 ? "nothing near it" : string.Join("; ", near))}"));
        }
    }

    /// <summary>A vtable's first entries as module offsets.</summary>
    private static void VTable(IMemoryReader reader, ulong vtable, List<string> lines)
    {
        if (vtable < reader.ModuleBase || vtable >= reader.ModuleBase + reader.ModuleSize)
        {
            return;
        }

        byte[] entries = new byte[64 * 8];
        if (!reader.TryRead(vtable, entries))
        {
            return;
        }

        var said = new StringBuilder("  vtable entries:");
        for (var one = 0; one < 64; one++)
        {
            said.Append(CultureInfo.InvariantCulture, $" [0x{one * 8:X}] {Module(reader, U64(entries, one * 8))}");
        }

        lines.Add(said.ToString());
    }

    private static void Words(IMemoryReader reader, ulong at, int length, string title, List<string> lines)
    {
        byte[] bytes = new byte[length];
        if (!reader.TryRead(at, bytes))
        {
            lines.Add($"--- {title}: did not read");
            return;
        }

        lines.Add($"--- {title}: vtable {Module(reader, U64(bytes, 0))}");
        lines.Add("  words: " + Hex(bytes, 0, length));
        VTable(reader, U64(bytes, 0), lines);
    }

    private static string Module(IMemoryReader reader, ulong address)
        => address >= reader.ModuleBase && address < reader.ModuleBase + reader.ModuleSize
            ? string.Create(CultureInfo.InvariantCulture, $"module+0x{address - reader.ModuleBase:X}")
            : string.Create(CultureInfo.InvariantCulture, $"0x{address:X}");

    private static ulong U64(byte[] bytes, int at) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at));

    private static Vector3 V3(byte[] bytes, int at) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at)),
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 4)),
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 8)));

    private static string Say(Vector3 point) => string.Create(CultureInfo.InvariantCulture, $"({point.X:0.##}, {point.Y:0.##}, {point.Z:0.##})");

    private static string Hex(byte[] bytes, int from, int to)
    {
        var text = new StringBuilder();
        for (int at = from; at + 8 <= to; at += 8)
        {
            text.Append(CultureInfo.InvariantCulture, $"{U64(bytes, at):X16} ");
        }

        return text.ToString().TrimEnd();
    }
}
