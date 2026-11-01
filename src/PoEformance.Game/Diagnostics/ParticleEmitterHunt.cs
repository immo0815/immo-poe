using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Entities;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// The engine's GPU particle emitters as the processor hands them to the card (particle-emitters.txt): every <c>ParticleEmitterData</c>
/// record found in memory, decoded field by field, the ones nearest the player watched for a few seconds - what a dispatch of the linked
/// update shaders (ParticleLinker) must be handed, read rather than chosen.
/// </summary>
/// <remarks>
/// THE RECORD IS THE ENGINE'S OWN STRUCT (schema <c>ParticleEmitterData</c>): <c>Shaders/CommonDeclarations.inc</c> declares it under
/// GPU_PARTICLE_DRAW_CALL, "has to be in-sync with CPP code", 160 bytes - the scale, the emitter's duration, the last scale, the dead time
/// and fade-out time as two halves, the inverse scale, the lock flags, the last inverse scale, the simulation's and the particles' speed as
/// two halves, the rotation and the last rotation as quaternions, the translation and the time, the last translation and the last time, the
/// culling aggression as a half, the bones (start &lt;&lt; 8 | count, GPU_PARTICLE_BONE_COUNT_BITS), the culling tag (a dynamic culling tag of
/// 5 bits, the group size less one in 6, the lock condition in 3, the emitter flags above), the first particle's index in the pool, and two
/// custom words of four halves. The shaders say what every bit means (gpuparticles.ffx's PARTICLE_EMITTER_FLAG_* and PARTICLE_LOCK_FLAG_*);
/// what values the processor puts there is in no file and no shader.
///
/// IT IS FOUND BY HOW ITS FIELDS AGREE, not by values: a scale beside its exact inverse twice (now and last frame) and two unit quaternions
/// say what it is wherever it lies. The engine's GPU particle code (GpuParticles::Impl, schema <c>GpuParticlesImpl</c>) carries no type name
/// on the records, so where it writes them was not found statically (v0.1.432); the card reads them from a buffer the processor fills each
/// frame, which may be a copy in the game's own heap, the driver's write-combined upload heap, or both - so the game's writable memory is
/// searched first and the driver's heaps after, within <see cref="SlowLimit"/>.
/// </remarks>
public static class ParticleEmitterHunt
{
    /// <summary>How long a record is.</summary>
    public const int RecordBytes = 0xA0;

    /// <summary>The most records the sweeps keep - CHOSEN; all are counted.</summary>
    public const int MostRecords = 16384;

    /// <summary>The most records written decoded, the nearest the player first - CHOSEN.</summary>
    public const int MostDecoded = 96;

    /// <summary>The most records watched, the nearest the player first - CHOSEN.</summary>
    public const int MostWatched = 32;

    /// <summary>The most changes of one watched record written - CHOSEN; all are counted.</summary>
    public const int MostChanges = 48;

    /// <summary>How long the driver's write-combined and uncached heaps are searched at most - CHOSEN: they read some hundred times slower than the heap.</summary>
    public static readonly TimeSpan SlowLimit = TimeSpan.FromSeconds(60);

    /// <summary>How long the nearest records are watched - CHOSEN.</summary>
    public static readonly TimeSpan WatchFor = TimeSpan.FromSeconds(3);

    /// <summary>How long between two reads of the watched records - CHOSEN: about a frame at 99 a second.</summary>
    public static readonly TimeSpan WatchEvery = TimeSpan.FromMilliseconds(5);

    /// <summary>The engine's emitter flags (gpuparticles.ffx, PARTICLE_EMITTER_FLAG_*), bit by bit from bit 0.</summary>
    public static readonly string[] EmitterFlags =
    [
        "IS_ALIVE", "IS_INFINITE", "REVERSE_BONES", "IS_NEW", "TELEPORTED", "IS_VISIBLE", "ORPHANED", "PAUSED", "EMIT_WHEN_ORPHANED",
        "NO_SUBFRAME", "INDIVIDUAL_GROUP_CULL", "FADE_OUT",
    ];

    /// <summary>The engine's lock flags (gpuparticles.ffx, PARTICLE_LOCK_FLAG_*), bit by bit from bit 0.</summary>
    public static readonly string[] LockFlags =
    [
        "TRANSLATION", "ROTATION", "ROTATION_EMIT", "SCALE_X", "SCALE_X_EMIT", "SCALE_Y", "SCALE_Y_EMIT", "SCALE_Z", "SCALE_Z_EMIT",
        "SCALE_X_BONE", "SCALE_X_BONE_EMIT", "SCALE_Y_BONE", "SCALE_Y_BONE_EMIT", "SCALE_Z_BONE", "SCALE_Z_BONE_EMIT", "MOVEMENT",
        "TRANSLATION_BONE", "ROTATION_BONE", "ROTATION_BONE_EMIT", "MOVEMENT_BONE", "ROTATION_BONE_FIXED", "ALONG", "ALONG_BONE", "INVERSE_CND",
    ];

    /// <summary>A capture's progress and its stop.</summary>
    public sealed class Running
    {
        private readonly FloatHuntProgress _heap = new();
        private readonly FloatHuntProgress _slow = new();
        private int _phase;

        /// <summary>The heap's sweep.</summary>
        public FloatHuntProgress Heap => _heap;

        /// <summary>The driver's heaps' sweep.</summary>
        public FloatHuntProgress Slow => _slow;

        /// <summary>Asks it to stop, keeping what it found.</summary>
        public void Stop()
        {
            _heap.Stop();
            _slow.Stop();
        }

        /// <summary>Whether <see cref="Stop"/> was asked for.</summary>
        public bool Stopping => _heap.Stopping;

        /// <summary>The window's line.</summary>
        public string Line => Volatile.Read(ref _phase) switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"looking for the particle emitters' records in the game's heap: {_heap.Bytes / 1e9:0.0} GB, {_heap.Places:N0} found"),
            1 => string.Create(CultureInfo.InvariantCulture, $"and in the driver's upload heaps (at most {SlowLimit.TotalSeconds:0} s): {_slow.Bytes / 1e9:0.00} GB, {_slow.Places:N0} found"),
            2 => "watching the records nearest the player",
            _ => "writing what was found",
        };

        /// <summary>How far it is.</summary>
        public float Fraction => Volatile.Read(ref _phase) switch
        {
            0 => _heap.Budget > 0 ? 0.4f * Math.Clamp((float)_heap.Bytes / _heap.Budget, 0f, 1f) : 0.05f,
            1 => 0.45f,
            2 => 0.85f,
            _ => 0.95f,
        };

        internal void Phase(int phase) => Volatile.Write(ref _phase, phase);
    }

    /// <summary>One record as the shaders read it (CommonDeclarations.inc's ParticleEmitterData; HalfUtility's UnpackHalf2: the low half first).</summary>
    /// <param name="Scale">The scale.</param>
    /// <param name="Duration">emitter_duration.</param>
    /// <param name="LastScale">last_scale.</param>
    /// <param name="DeadTime">dead_time's two halves: the dead time and the normalised fade-out time.</param>
    /// <param name="InverseScale">inverse_scale.</param>
    /// <param name="Locks">lock_flags.</param>
    /// <param name="LastInverseScale">last_inverse_scale.</param>
    /// <param name="Speed">speed_factor's two halves: the simulation's speed and the particles'.</param>
    /// <param name="Rotation">rotation (x, y, z, w).</param>
    /// <param name="LastRotation">last_rotation.</param>
    /// <param name="Translation">translation.</param>
    /// <param name="Time">time.</param>
    /// <param name="LastTranslation">last_translation.</param>
    /// <param name="PrevTime">prev_time.</param>
    /// <param name="Aggression">culling_aggression's two halves (the shaders read the first).</param>
    /// <param name="Bones">bones: the first bone's index and how many.</param>
    /// <param name="Tag">culling_tag whole.</param>
    /// <param name="ParticlesStart">particles_start.</param>
    /// <param name="CustomA">custom_data_a's four halves.</param>
    /// <param name="CustomB">custom_data_b's four halves.</param>
    public readonly record struct Record(
        Vector3 Scale, float Duration, Vector3 LastScale, Vector2 DeadTime, Vector3 InverseScale, uint Locks, Vector3 LastInverseScale,
        Vector2 Speed, Quaternion Rotation, Quaternion LastRotation, Vector3 Translation, float Time, Vector3 LastTranslation, float PrevTime,
        Vector2 Aggression, (uint Start, uint Count) Bones, uint Tag, uint ParticlesStart, Vector4 CustomA, Vector4 CustomB)
    {
        /// <summary>The dynamic culling tag (PARTICLE_DYN_CULL_TAG_BITS, 5).</summary>
        public uint CullTag => Tag & 0x1F;

        /// <summary>The group size (PARTICLE_GROUP_SIZE_BITS, 6 bits, less one).</summary>
        public uint GroupSize => ((Tag >> 5) & 0x3F) + 1;

        /// <summary>The lock condition (PARTICLE_LOCK_CND_BITS, 3).</summary>
        public uint LockCondition => (Tag >> 11) & 0x7;

        /// <summary>The emitter flags (ParticleEmitterFlags(): the tag past its first 14 bits).</summary>
        public uint Flags => Tag >> 14;
    }

    /// <summary>A record decoded from its 160 bytes.</summary>
    public static Record Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < RecordBytes)
        {
            throw new ArgumentException("a record is 160 bytes", nameof(bytes));
        }

        ReadOnlySpan<float> f = MemoryMarshal.Cast<byte, float>(bytes[..RecordBytes]);
        ReadOnlySpan<uint> u = MemoryMarshal.Cast<byte, uint>(bytes[..RecordBytes]);
        uint bones = u[0x84 / 4];
        Vector2 a0 = Halves(u[0x90 / 4]), a1 = Halves(u[0x94 / 4]), b0 = Halves(u[0x98 / 4]), b1 = Halves(u[0x9C / 4]);
        return new Record(
            new Vector3(f[0], f[1], f[2]), f[3], new Vector3(f[4], f[5], f[6]), Halves(u[7]), new Vector3(f[8], f[9], f[10]), u[11],
            new Vector3(f[12], f[13], f[14]), Halves(u[15]), new Quaternion(f[16], f[17], f[18], f[19]), new Quaternion(f[20], f[21], f[22], f[23]),
            new Vector3(f[24], f[25], f[26]), f[27], new Vector3(f[28], f[29], f[30]), f[31], Halves(u[0x80 / 4]), (bones >> 8, bones & 0xFF),
            u[0x88 / 4], u[0x8C / 4], new Vector4(a0, a1.X, a1.Y), new Vector4(b0, b1.X, b1.Y));
    }

    /// <summary>A word's two halves as HalfUtility's UnpackHalf2 reads them: the low sixteen bits first.</summary>
    public static Vector2 Halves(uint word)
        => new((float)BitConverter.UInt16BitsToHalf((ushort)(word & 0xFFFF)), (float)BitConverter.UInt16BitsToHalf((ushort)(word >> 16)));

    /// <summary>
    /// Whether 160 bytes read as a record: the scale and its inverse multiply to one in every axis, now and last frame; both rotations are
    /// unit quaternions; the translations and times are finite and the translation is not the origin.
    /// </summary>
    /// <remarks>
    /// THE ORIGIN IS LEFT OUT: a block of ones and an identity quaternion passes every other test, and an emitter standing at the world's
    /// origin is none the game draws. Every tolerance CHOSEN: 1e-3 on the products and the quaternions' lengths.
    /// </remarks>
    public static bool Fits(ReadOnlySpan<float> f)
    {
        if (f.Length < RecordBytes / 4)
        {
            return false;
        }

        for (var axis = 0; axis < 3; axis++)
        {
            if (!Inverse(f[axis], f[8 + axis]) || !Inverse(f[4 + axis], f[12 + axis]))
            {
                return false;
            }
        }

        if (!Unit(f.Slice(16, 4)) || !Unit(f.Slice(20, 4)))
        {
            return false;
        }

        float tx = f[24], ty = f[25], tz = f[26], time = f[27], lx = f[28], ly = f[29], lz = f[30], prev = f[31];
        return float.IsFinite(tx) && float.IsFinite(ty) && float.IsFinite(tz) && float.IsFinite(lx) && float.IsFinite(ly) && float.IsFinite(lz)
            && float.IsFinite(time) && float.IsFinite(prev) && float.IsFinite(f[3])
            && MathF.Abs(tx) < 1e7f && MathF.Abs(ty) < 1e7f && MathF.Abs(tz) < 1e7f
            && (MathF.Abs(tx) + MathF.Abs(ty) + MathF.Abs(tz)) > 1f;
    }

    private static bool Inverse(float value, float inverse)
    {
        float size = MathF.Abs(value);
        return size is >= 1e-3f and <= 1e3f && MathF.Abs((value * inverse) - 1f) <= 1e-3f;
    }

    private static bool Unit(ReadOnlySpan<float> q)
    {
        float length = (q[0] * q[0]) + (q[1] * q[1]) + (q[2] * q[2]) + (q[3] * q[3]);
        return MathF.Abs(length - 1f) <= 2e-3f;
    }

    /// <summary>
    /// The sweep's test on one chunk: every four-byte place starting in the chunk's own bytes whose first float may be a scale is tried
    /// whole (<see cref="Fits"/>). One multiply rejects nearly every word.
    /// </summary>
    public static void Sweep(ReadOnlySpan<byte> chunk, int starts, ulong at, List<FloatSighting> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(chunk);
        int last = Math.Min((starts / 4) - 1, floats.Length - (RecordBytes / 4));
        for (var index = 0; index <= last; index++)
        {
            float scale = floats[index];
            float size = MathF.Abs(scale);
            if (!(size is >= 1e-3f and <= 1e3f) || MathF.Abs((scale * floats[index + 8]) - 1f) > 1e-3f)
            {
                continue;
            }

            if (Fits(floats.Slice(index, RecordBytes / 4)))
            {
                into.Add(new FloatSighting(at + (ulong)(index * 4), 0));
            }
        }
    }

    /// <summary>The report: the sweeps, every run of records by where it lies, the nearest records decoded, and the watched ones' changes.</summary>
    /// <param name="reader">The game's memory, past the tap.</param>
    /// <param name="schema">The offsets, for the player's place.</param>
    /// <param name="gameStatesStatic">The game states' static, for the player.</param>
    /// <param name="running">The progress and the stop.</param>
    public static List<string> Run(IMemoryReader reader, OffsetSchema schema, ulong gameStatesStatic, Running running)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(running);
        if (reader is not IMemoryRegions regions)
        {
            return ["particle emitters: the reader cannot list the game's memory"];
        }

        var lines = new List<string>();
        Vector3? player = PlayerPlace(reader, schema, gameStatesStatic, out string who);
        lines.Add(player is { } at
            ? string.Create(CultureInfo.InvariantCulture, $"particle emitters: the player {who} at {Say(at)}; records are ranked by their translation's distance to him")
            : "particle emitters: no player read - the records are listed by where they lie");

        // THE GAME'S HEAP, then THE DRIVER'S HEAPS within a time limit: a record the card reads may be a copy in either.
        FloatNeedle[] kinds = [new("ParticleEmitterData (a scale beside its inverse twice, two unit quaternions)", [], 0f, Most: MostRecords)];
        var writable = new WritableRegions(regions);
        FloatHuntResult heap = FloatHunt.Scan(reader, writable, kinds, Sweep, RecordBytes, running.Heap, budget: 2 * HeapScan.ByteBudget);
        lines.Add(Swept("the game's heap", heap, $"leaving out {writable.Unwritable / 1e9:0.0} GB the game cannot write and {writable.Slow / 1e9:0.0} GB of the driver's heaps, searched next"));
        FloatHuntResult? slow = null;
        if (!running.Stopping)
        {
            running.Phase(1);
            using var limit = new Timer(_ => running.Slow.Stop(), null, SlowLimit, Timeout.InfiniteTimeSpan);
            slow = FloatHunt.Scan(reader, new DriverHeaps(regions), kinds, Sweep, RecordBytes, running.Slow, budget: long.MaxValue / 4);
            lines.Add(Swept($"the driver's write-combined and uncached heaps (at most {SlowLimit.TotalSeconds:0} s)", slow, string.Empty));
        }

        // EVERY RECORD READ ONCE, ITS REGION'S KIND BESIDE IT; the runs a stride of 160 apart are the arrays.
        MemoryRegion[] all = [.. regions.Regions()];
        var found = new List<(ulong At, byte[] Bytes, string Where)>();
        foreach ((FloatHuntResult? result, string where) in new[] { (heap, "heap"), (slow, "driver") })
        {
            if (result is null)
            {
                continue;
            }

            foreach (FloatSighting sight in result.Sightings)
            {
                byte[] bytes = new byte[RecordBytes];
                if (reader.TryRead(sight.At, bytes) && Fits(MemoryMarshal.Cast<byte, float>(bytes.AsSpan())))
                {
                    found.Add((sight.At, bytes, $"{where}, {KindAt(all, sight.At)}"));
                }
            }
        }

        found.Sort((a, b) => a.At.CompareTo(b.At));
        lines.Add(string.Empty);
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"=== {found.Count} records still read as one after the sweeps; the runs of them {RecordBytes} bytes apart (an array the card reads as a StructuredBuffer, or its copy) - each run's first address, its length, where it lies, the nearest record's distance to the player and index in it, and how many are alive (IS_ALIVE) and visible (IS_VISIBLE)"));
        var index = new Dictionary<ulong, (ulong Run, int Index)>();
        int start = 0;
        for (var one = 1; one <= found.Count; one++)
        {
            if (one < found.Count && found[one].At - found[one - 1].At == RecordBytes)
            {
                continue;
            }

            int length = one - start;
            ulong runAt = found[start].At;
            float nearest = float.PositiveInfinity;
            int nearestIndex = -1, alive = 0, visible = 0;
            for (int r = start; r < one; r++)
            {
                index[found[r].At] = (runAt, r - start);
                Record record = Decode(found[r].Bytes);
                alive += (record.Flags & 1) != 0 ? 1 : 0;
                visible += (record.Flags & 0x20) != 0 ? 1 : 0;
                float distance = player is { } p ? Vector3.Distance(record.Translation, p) : float.NaN;
                if (distance < nearest)
                {
                    nearest = distance;
                    nearestIndex = r - start;
                }
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  run\t0x{runAt:X}\t{length} records\t{found[start].Where}\tnearest {nearest:0.#} at index {nearestIndex}\talive {alive}\tvisible {visible}"));
            start = one;
        }

        // THE NEAREST, DECODED, their bytes whole for the replay.
        List<(ulong At, byte[] Bytes, string Where, float Distance)> ranked = [.. found
            .Select(one => (one.At, one.Bytes, one.Where, Distance: player is { } p ? Vector3.Distance(Decode(one.Bytes).Translation, p) : 0f))
            .OrderBy(one => one.Distance)
            .ThenBy(one => one.At)];
        lines.Add(string.Empty);
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"=== the {Math.Min(MostDecoded, ranked.Count)} records nearest the player decoded (gpuparticles.ffx's names) - its run and index (the index the card's gpu_particle_emitter names), its distance, then every field; and its bytes whole as a line 'record\\taddress\\thex' for the replay"));
        foreach ((ulong place, byte[] bytes, string where, float distance) in ranked.Take(MostDecoded))
        {
            (ulong run, int position) = index.GetValueOrDefault(place, (place, 0));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"--- 0x{place:X}: run 0x{run:X} index {position}, {where}, {distance:0.#} from the player"));
            lines.AddRange(Said(Decode(bytes)).Select(line => "  " + line));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"record\t0x{place:X}\t{Convert.ToHexString(bytes)}"));
        }

        // THE NEAREST, WATCHED: every read that differs from the last kept, with when.
        running.Phase(2);
        if (!running.Stopping && ranked.Count > 0)
        {
            Watch(reader, [.. ranked.Take(MostWatched).Select(one => one.At)], lines);
        }

        running.Phase(3);
        return lines;
    }

    /// <summary>A record's fields as lines, every flag by its name.</summary>
    public static IEnumerable<string> Said(Record r)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"scale {Say(r.Scale)} last {Say(r.LastScale)}; inverse {Say(r.InverseScale)} last {Say(r.LastInverseScale)}");
        yield return string.Create(CultureInfo.InvariantCulture, $"rotation {Say(r.Rotation)} last {Say(r.LastRotation)}");
        yield return string.Create(CultureInfo.InvariantCulture, $"translation {Say(r.Translation)} last {Say(r.LastTranslation)} (moved {Vector3.Distance(r.Translation, r.LastTranslation):0.###})");
        yield return string.Create(CultureInfo.InvariantCulture, $"time {r.Time:G7} prev_time {r.PrevTime:G7} (step {r.Time - r.PrevTime:G6}); emitter_duration {r.Duration:G7}; dead_time {r.DeadTime.X:G5} fade_out {r.DeadTime.Y:G5}; speed sim {r.Speed.X:G5} particles {r.Speed.Y:G5}");
        yield return string.Create(CultureInfo.InvariantCulture, $"culling_tag 0x{r.Tag:X8}: dyn tag {r.CullTag}, group size {r.GroupSize}, lock condition {r.LockCondition}, flags 0x{r.Flags:X} {Names(r.Flags, EmitterFlags)}; aggression {r.Aggression.X:G5} ({r.Aggression.Y:G5})");
        yield return string.Create(CultureInfo.InvariantCulture, $"lock_flags 0x{r.Locks:X8} {Names(r.Locks, LockFlags)}");
        yield return string.Create(CultureInfo.InvariantCulture, $"bones start {r.Bones.Start} count {r.Bones.Count}; particles_start {r.ParticlesStart}; custom_a {Say(r.CustomA)} custom_b {Say(r.CustomB)}");
    }

    /// <summary>The names of a word's set bits, by a list of names from bit 0, the rest as numbers.</summary>
    public static string Names(uint word, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var said = new StringBuilder();
        for (var bit = 0; bit < 32; bit++)
        {
            if ((word & (1u << bit)) == 0)
            {
                continue;
            }

            said.Append(said.Length > 0 ? " " : string.Empty).Append(bit < names.Count ? names[bit] : string.Create(CultureInfo.InvariantCulture, $"bit{bit}"));
        }

        return said.Length > 0 ? said.ToString() : "none";
    }

    /// <summary>
    /// The replay's reading of a report: every 'record' line decoded again with today's decoder, and the counts of what they hold.
    /// </summary>
    public static List<string> Replay(IEnumerable<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string> { "particle emitters, read again offline: every record line of the report decoded with this build's decoder" };
        int records = 0, fitting = 0;
        var groups = new SortedDictionary<uint, int>();
        foreach (string line in report)
        {
            if (!line.StartsWith("record\t", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length < 3 || parts[2].Length != RecordBytes * 2)
            {
                continue;
            }

            byte[] bytes = Convert.FromHexString(parts[2]);
            records++;
            bool fits = Fits(MemoryMarshal.Cast<byte, float>(bytes.AsSpan()));
            fitting += fits ? 1 : 0;
            Record record = Decode(bytes);
            groups[record.GroupSize] = groups.GetValueOrDefault(record.GroupSize) + 1;
            lines.Add(string.Empty);
            lines.Add($"--- {parts[1]}{(fits ? string.Empty : " (does not read as a record by today's test)")}");
            lines.AddRange(Said(record).Select(one => "  " + one));
        }

        lines.Insert(1, string.Create(CultureInfo.InvariantCulture,
            $"{records} records, {fitting} read as one by today's test; by group size: {string.Join(", ", groups.Select(one => $"{one.Key}: {one.Value}"))}"));
        return lines;
    }

    private static void Watch(IMemoryReader reader, IReadOnlyList<ulong> watched, List<string> lines)
    {
        var last = new byte[watched.Count][];
        var changes = new List<(double Seconds, byte[] Bytes)>[watched.Count];
        for (var one = 0; one < watched.Count; one++)
        {
            changes[one] = [];
        }

        var clock = Stopwatch.StartNew();
        int reads = 0;
        while (clock.Elapsed < WatchFor)
        {
            reads++;
            for (var one = 0; one < watched.Count; one++)
            {
                byte[] bytes = new byte[RecordBytes];
                if (!reader.TryRead(watched[one], bytes) || (last[one] is { } was && was.AsSpan().SequenceEqual(bytes)))
                {
                    continue;
                }

                last[one] = bytes;
                changes[one].Add((clock.Elapsed.TotalSeconds, bytes));
            }

            Thread.Sleep(WatchEvery);
        }

        lines.Add(string.Empty);
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"=== the {watched.Count} records nearest the player read {reads} times over {clock.Elapsed.TotalSeconds:0.0} s: each read that differs from the last, its seconds since the first read, time and prev_time, the translation, the flags and what else changed; a record that never changed is a copy no longer written"));
        for (var one = 0; one < watched.Count; one++)
        {
            List<(double Seconds, byte[] Bytes)> seen = changes[one];
            if (seen.Count == 0)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"--- 0x{watched[one]:X}: did not read"));
                continue;
            }

            Record first = Decode(seen[0].Bytes), end = Decode(seen[^1].Bytes);
            double span = seen[^1].Seconds - seen[0].Seconds;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"--- 0x{watched[one]:X}: {seen.Count - 1} changes; time {first.Time:G7} -> {end.Time:G7} over {span:0.###} s of reading ({(span > 0 ? (end.Time - first.Time) / span : 0):G4} a second); translation moved {Vector3.Distance(first.Translation, end.Translation):0.##}"));
            for (var change = 1; change < seen.Count && change <= MostChanges; change++)
            {
                Record r = Decode(seen[change].Bytes);
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  {seen[change].Seconds:0.0000}\ttime {r.Time:G7}\tprev {r.PrevTime:G7}\tstep {r.Time - r.PrevTime:G5}\tat {Say(r.Translation)}\tflags 0x{r.Flags:X}\tchanged {Changed(seen[change - 1].Bytes, seen[change].Bytes)}"));
            }
        }
    }

    /// <summary>Which fields of a record two reads differ in, by name.</summary>
    private static string Changed(byte[] before, byte[] after)
    {
        (int At, int Length, string Name)[] fields =
        [
            (0x00, 12, "scale"), (0x0C, 4, "duration"), (0x10, 12, "last_scale"), (0x1C, 4, "dead_time"), (0x20, 12, "inverse"), (0x2C, 4, "lock_flags"),
            (0x30, 12, "last_inverse"), (0x3C, 4, "speed"), (0x40, 16, "rotation"), (0x50, 16, "last_rotation"), (0x60, 12, "translation"),
            (0x6C, 4, "time"), (0x70, 12, "last_translation"), (0x7C, 4, "prev_time"), (0x80, 4, "aggression"), (0x84, 4, "bones"),
            (0x88, 4, "culling_tag"), (0x8C, 4, "particles_start"), (0x90, 8, "custom_a"), (0x98, 8, "custom_b"),
        ];
        return string.Join(" ", fields.Where(one => !before.AsSpan(one.At, one.Length).SequenceEqual(after.AsSpan(one.At, one.Length))).Select(one => one.Name));
    }

    private static string Swept(string what, FloatHuntResult result, string beside) => string.Create(CultureInfo.InvariantCulture,
        $"{what}: {result.BytesScanned / 1e9:0.00} GB of {result.RegionsWalked:N0} regions on {result.Workers} threads in {result.Took.TotalSeconds:0.0} s{(result.Stopped ? " (stopped)" : result.Truncated ? " (its budget ended it)" : string.Empty)}{(beside.Length > 0 ? ", " + beside : string.Empty)}; {result.Sightings.Count:N0} places{(result.Capped.Count > 0 ? " (capped)" : string.Empty)}{string.Concat(result.Kinds.Take(4).Select(kind => string.Create(CultureInfo.InvariantCulture, $"; {kind.Kind} {kind.Bytes / 1e9:0.00} GB read in {kind.Reading.TotalSeconds:0.0} s")))}");

    private static string KindAt(MemoryRegion[] all, ulong at)
    {
        int low = 0, high = all.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            MemoryRegion region = all[middle];
            if (at < region.Address)
            {
                high = middle - 1;
            }
            else if (at >= region.Address + region.Size)
            {
                low = middle + 1;
            }
            else
            {
                return string.Create(CultureInfo.InvariantCulture, $"{region.Kind} region 0x{region.Address:X} of {region.Size:N0} bytes");
            }
        }

        return "no region";
    }

    private static Vector3? PlayerPlace(IMemoryReader reader, OffsetSchema schema, ulong gameStatesStatic, out string who)
    {
        who = "unread";
        GameChainAddresses chain = GameChain.Resolve(reader, schema, gameStatesStatic);
        var entities = new EntityReader(reader, schema);
        if (chain.PlayerEntity == 0 || entities.ReadIdentity(chain.PlayerEntity) is not { } player)
        {
            return null;
        }

        who = player.Path;
        ulong render = entities.ReadComponents(player.Address, player.Details).GetValueOrDefault("Render");
        byte[] where = new byte[12];
        return render != 0 && reader.TryRead(render + (ulong)schema.Structs["Render"].OffsetOf("CurrentWorldPosition"), where)
            ? new Vector3(BinaryPrimitives.ReadSingleLittleEndian(where), BinaryPrimitives.ReadSingleLittleEndian(where.AsSpan(4)), BinaryPrimitives.ReadSingleLittleEndian(where.AsSpan(8)))
            : null;
    }

    private static string Say(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:G6}, {v.Y:G6}, {v.Z:G6})");

    private static string Say(Vector4 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:G4}, {v.Y:G4}, {v.Z:G4}, {v.W:G4})");

    private static string Say(Quaternion q) => string.Create(CultureInfo.InvariantCulture, $"({q.X:G5}, {q.Y:G5}, {q.Z:G5}, {q.W:G5})");

    /// <summary>The driver's write-combined and uncached heaps alone - what <see cref="WritableRegions"/> leaves out as slow.</summary>
    private sealed class DriverHeaps(IMemoryRegions all) : IMemoryRegions
    {
        public IEnumerable<MemoryRegion> Regions() => all.Regions().Where(region => region.Slow);
    }
}
