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

    /// <summary>How far from the player an emitter's translation may stand to be followed and its bones looked for - CHOSEN.</summary>
    public const float NearReach = 300f;

    /// <summary>The most emitters near the player followed - CHOSEN.</summary>
    public const int MostNear = 24;

    /// <summary>The most changes of one followed emitter written - CHOSEN; all are counted.</summary>
    public const int MostChanges = 48;

    /// <summary>The most places near one emitter kept as its bones' candidates - CHOSEN.</summary>
    public const int MostBonePlaces = 32768;

    /// <summary>How long one emitter's bone record is: BoneData, pos and prev_pos, a float4 each.</summary>
    public const int BoneBytes = 32;

    /// <summary>The most bytes of the region holding the arrays written whole - CHOSEN.</summary>
    public const int MostRegionBytes = 64 * 1024 * 1024;

    /// <summary>Where the region holding the arrays is written whole beside the report: its address and length (eight bytes each), then its bytes.</summary>
    public const string RegionFile = "particle-emitters.bin";

    /// <summary>How many records of an array one read of the follow takes - CHOSEN, past the most measured (537).</summary>
    public const int FollowRecords = 1024;

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
        private readonly FloatHuntProgress _bones = new();
        private readonly FloatHuntProgress _globals = new();
        private int _phase;

        /// <summary>The heap's sweep.</summary>
        public FloatHuntProgress Heap => _heap;

        /// <summary>The driver's heaps' sweep.</summary>
        public FloatHuntProgress Slow => _slow;

        /// <summary>The bones' sweep.</summary>
        public FloatHuntProgress BoneSweep => _bones;

        /// <summary>The globals' sweep.</summary>
        public FloatHuntProgress GlobalSweep => _globals;

        /// <summary>Asks it to stop, keeping what it found.</summary>
        public void Stop()
        {
            _heap.Stop();
            _slow.Stop();
            _bones.Stop();
            _globals.Stop();
        }

        /// <summary>Whether <see cref="Stop"/> was asked for.</summary>
        public bool Stopping => _heap.Stopping;

        /// <summary>The window's line.</summary>
        public string Line => Volatile.Read(ref _phase) switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"looking for the particle emitters' records in the game's heap: {_heap.Bytes / 1e9:0.0} GB, {_heap.Places:N0} found"),
            1 => string.Create(CultureInfo.InvariantCulture, $"none in the heap, so in the driver's upload heaps (at most {SlowLimit.TotalSeconds:0} s): {_slow.Bytes / 1e9:0.00} GB, {_slow.Places:N0} found"),
            2 => string.Create(CultureInfo.InvariantCulture, $"looking for the near emitters' bones: {_bones.Bytes / 1e9:0.0} GB, {_bones.Places:N0} places"),
            3 => "following the emitters near the player frame by frame",
            5 => string.Create(CultureInfo.InvariantCulture, $"looking for the dispatch's globals by the emitters' numbers: {_globals.Bytes / 1e9:0.0} GB, {_globals.Places:N0} places"),
            _ => "writing what was found",
        };

        /// <summary>How far it is.</summary>
        public float Fraction => Volatile.Read(ref _phase) switch
        {
            0 => _heap.Budget > 0 ? 0.4f * Math.Clamp((float)_heap.Bytes / _heap.Budget, 0f, 1f) : 0.05f,
            1 => 0.45f,
            2 => _bones.Budget > 0 ? 0.45f + (0.35f * Math.Clamp((float)_bones.Bytes / _bones.Budget, 0f, 1f)) : 0.5f,
            3 => 0.85f,
            5 => 0.8f,
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

    /// <summary>The report: the sweeps, every run of records by where it lies, the nearest records decoded, the pieces, the bones, and the nearest emitters followed.</summary>
    /// <param name="reader">The game's memory, past the tap.</param>
    /// <param name="schema">The offsets, for the player's place and his pieces.</param>
    /// <param name="gameStatesStatic">The game states' static, for the player.</param>
    /// <param name="folder">Where the region holding the arrays is written whole (<see cref="RegionFile"/>), or null.</param>
    /// <param name="running">The progress and the stop.</param>
    /// <param name="anchors">The numbers the dispatch's globals may hold (the panes' emitters' bursts and durations, CaptureParticles.Anchors), or null for no globals search.</param>
    public static List<string> Run(IMemoryReader reader, OffsetSchema schema, ulong gameStatesStatic, string? folder, Running running, IReadOnlyList<FloatNeedle>? anchors = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(running);
        if (reader is not IMemoryRegions regions)
        {
            return ["particle emitters: the reader cannot list the game's memory"];
        }

        var lines = new List<string>();
        Vector3? player = PlayerPlace(reader, schema, gameStatesStatic, out string who, out ulong render);
        lines.Add(player is { } at
            ? string.Create(CultureInfo.InvariantCulture, $"particle emitters: the player {who} at {Say(at)}; records are ranked by their translation's distance to him")
            : "particle emitters: no player read - the records are listed by where they lie");

        // THE GAME'S HEAP; THE DRIVER'S HEAPS ONLY WHERE IT HOLDS NONE: the first capture (v0.1.432, 19:18) found both arrays in the heap
        // and nothing in 3.2 GB of the driver's heaps, at 215 of the threads' seconds.
        FloatNeedle[] kinds = [new("ParticleEmitterData (a scale beside its inverse twice, two unit quaternions)", [], 0f, Most: MostRecords)];
        var writable = new WritableRegions(regions);
        FloatHuntResult heap = FloatHunt.Scan(reader, writable, kinds, Sweep, RecordBytes, running.Heap, budget: 2 * HeapScan.ByteBudget);
        lines.Add(Swept("the game's heap", heap, $"leaving out {writable.Unwritable / 1e9:0.0} GB the game cannot write and {writable.Slow / 1e9:0.0} GB of the driver's heaps"));
        FloatHuntResult? slow = null;
        if (!running.Stopping && heap.Sightings.Count == 0)
        {
            running.Phase(1);
            using var limit = new Timer(_ => running.Slow.Stop(), null, SlowLimit, Timeout.InfiniteTimeSpan);
            slow = FloatHunt.Scan(reader, new DriverHeaps(regions), kinds, Sweep, RecordBytes, running.Slow, budget: long.MaxValue / 4);
            lines.Add(Swept($"none in the heap, so the driver's write-combined and uncached heaps (at most {SlowLimit.TotalSeconds:0} s)", slow, string.Empty));
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
        var runs = new List<(ulong At, int Length)>();
        int start = 0;
        for (var one = 1; one <= found.Count; one++)
        {
            if (one < found.Count && found[one].At - found[one - 1].At == RecordBytes)
            {
                continue;
            }

            int length = one - start;
            ulong runAt = found[start].At;
            runs.Add((runAt, length));
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

        // THE EMITTERS NEAR HIM, one record each: an emitter stands in both arrays (a frame apart), told by its first particle - the one of
        // the latest time kept.
        List<Record> near = [.. ranked
            .Where(one => one.Distance <= NearReach)
            .Select(one => Decode(one.Bytes))
            .GroupBy(one => one.ParticlesStart)
            .Select(group => group.MaxBy(one => one.Time))
            .Take(MostNear)];
        // EVERY RECORD NEAR HIM VOTES for where the bones array starts - both frames' arrays, each by its own first bone.
        List<Record> voters = [.. ranked.Where(one => one.Distance <= NearReach).Select(one => Decode(one.Bytes)).Take(32)];
        Pieces(reader, schema, render, near, lines);
        List<(ulong At, byte[] Bytes)> bones = [];
        if (!running.Stopping && voters.Count > 0)
        {
            running.Phase(2);
            bones = Bones(reader, regions, writable, voters, running, lines);
        }

        if (!running.Stopping && anchors is { Count: > 0 })
        {
            running.Phase(5);
            Globals(reader, regions, writable, all, runs, near, anchors, running, lines);
        }
        else if (!running.Stopping)
        {
            // SAID, NOT LEFT OUT (v0.1.437): v0.1.436's first press took no panes and wrote no globals section, and nothing said why.
            lines.Add(string.Empty);
            lines.Add(anchors is null
                ? "=== the dispatch's globals: not looked for - the install is not open, so no emitter's numbers could be read"
                : "=== the dispatch's globals: not looked for - no model pane at the press shows a monster naming a particle effect with numbers to look for (take the player with the effect's weapon in the Monster Book)");
        }

        if (runs.Count > 0 && runs.MaxBy(one => one.Length) is var longest && Array.Find(all, region => longest.At >= region.Address && longest.At < region.Address + region.Size) is { Size: > 0 } holding)
        {
            Region(reader, holding, folder, bones, lines);
        }

        // THE EMITTERS NEAR HIM, FOLLOWED by their first particle through every array, frame after frame.
        running.Phase(3);
        if (!running.Stopping && near.Count > 0)
        {
            Follow(reader, all, runs, near, lines);
        }

        running.Phase(4);
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
    /// The replay's reading of a report: every 'record' line decoded again with today's decoder, and the counts of what they hold; and where
    /// the region holding the arrays was written beside it (<see cref="RegionFile"/>), the near emitters' bones voted for again inside it.
    /// </summary>
    /// <param name="report">The report's lines.</param>
    /// <param name="region">The region file's bytes, or null.</param>
    public static List<string> Replay(IEnumerable<string> report, byte[]? region = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string> { "particle emitters, read again offline: every record line of the report decoded with this build's decoder" };
        int records = 0, fitting = 0;
        var groups = new SortedDictionary<uint, int>();
        var decoded = new List<Record>();
        Vector3? player = null;
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
            decoded.Add(record);
            groups[record.GroupSize] = groups.GetValueOrDefault(record.GroupSize) + 1;
            lines.Add(string.Empty);
            lines.Add($"--- {parts[1]}{(fits ? string.Empty : " (does not read as a record by today's test)")}");
            lines.AddRange(Said(record).Select(one => "  " + one));
        }

        lines.Insert(1, string.Create(CultureInfo.InvariantCulture,
            $"{records} records, {fitting} read as one by today's test; by group size: {string.Join(", ", groups.Select(one => $"{one.Key}: {one.Value}"))}"));
        Windowed(report, lines);
        if (region is { Length: > 16 } && decoded.Count > 0)
        {
            // THE NEAR ONES as the capture took them: the records are written nearest first, so the first record's translation stands for
            // the player's place within the reach.
            player ??= decoded[0].Translation;
            List<Record> near = [.. decoded
                .Where(one => Vector3.Distance(one.Translation, player.Value) <= NearReach)
                .GroupBy(one => one.ParticlesStart)
                .Select(group => group.MaxBy(one => one.Time))
                .Take(MostNear)];
            // EVERY BLOCK searched for runs of bones as the capture searched memory, voted for by the near records, the best read back.
            List<Record> voters = [.. decoded.Where(one => Vector3.Distance(one.Translation, player.Value) <= NearReach).Take(32)];
            List<(ulong At, byte[] Bytes)> blocks = Blocks(region);
            var runs = new List<FloatSighting>();
            foreach ((ulong at, byte[] bytes) in blocks)
            {
                ChainSweep(bytes, bytes.Length, at, runs);
            }

            List<(ulong Base, uint Agreed)> bases = Votes(runs.Select(one => one.At), voters);
            lines.Add(string.Empty);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"=== {RegionFile}: {blocks.Count} blocks ({string.Join(", ", blocks.Select(one => $"0x{one.At:X} {one.Bytes.Length:N0} bytes"))}); {runs.Count} runs of bones in them, voted for by {voters.Count} near records ({near.Count} emitters)"));
            foreach ((ulong at, uint agreed) in bases.Take(2))
            {
                (ulong blockAt, byte[] block) = blocks.Find(one => at >= one.At && at < one.At + (ulong)one.Bytes.Length);
                if (block is null)
                {
                    continue;
                }

                lines.Add(string.Create(CultureInfo.InvariantCulture, $"--- base 0x{at:X}: {BitOperations.PopCount(agreed)} of {voters.Count} records agree"));
                lines.AddRange(SaidBones(block.AsSpan((int)(at - blockAt)), voters).Select(one => "  " + one));
            }
        }

        return lines;
    }

    /// <summary>
    /// The globals section's windows read back from the report's rows (each row its offset from the needle and four words as hex), and every
    /// emitter's object uniforms decoded in them by this build's <see cref="Uniforms"/>, with the duration needles the report names.
    /// </summary>
    private static void Windowed(IEnumerable<string> report, List<string> lines)
    {
        var durations = new List<Vector2>();
        var windows = new List<(ulong From, byte[] Bytes)>();
        (ulong At, SortedDictionary<long, byte[]> Rows)? current = null;
        void Close()
        {
            if (current is { Rows.Count: > 0 } done)
            {
                long first = done.Rows.Keys.First();
                windows.Add(((ulong)((long)done.At + first), [.. done.Rows.Values.SelectMany(row => row)]));
            }
        }

        foreach (string line in report.Select(one => one.TrimEnd('\r')))
        {
            if (line.StartsWith("  needle ", StringComparison.Ordinal) && line.IndexOf("particle_duration_min, _max (", StringComparison.Ordinal) is var open and >= 0
                && line.IndexOf(')', open) is var close and > 0)
            {
                string[] values = line[(open + "particle_duration_min, _max (".Length)..close].Split(", ");
                if (values.Length == 2 && float.TryParse(values[0], CultureInfo.InvariantCulture, out float min) && float.TryParse(values[1], CultureInfo.InvariantCulture, out float max))
                {
                    durations.Add(new Vector2(min, max));
                }
            }
            else if (line.StartsWith("--- needle ", StringComparison.Ordinal) && line.IndexOf(" at 0x", StringComparison.Ordinal) is var at and > 0)
            {
                Close();
                string hex = line[(at + " at 0x".Length)..].Split(' ')[0];
                current = ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong place) ? (place, new SortedDictionary<long, byte[]>()) : null;
            }
            else if (line.StartsWith("===", StringComparison.Ordinal))
            {
                Close();
                current = null;
            }
            else if (current is { } open2 && line.Split('\t') is { Length: >= 2 } parts && long.TryParse(parts[0].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long offset))
            {
                string[] words = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 4)
                {
                    open2.Rows[offset] = Convert.FromHexString(string.Concat(words.Select(word => string.Concat(Enumerable.Range(0, 4).Select(b => word.Substring(6 - (2 * b), 2))))));
                }
            }
        }

        Close();
        if (windows.Count == 0)
        {
            return;
        }

        List<ObjectUniforms> blocks = [.. windows.SelectMany(one => Uniforms(one.Bytes, one.From, durations)).DistinctBy(one => one.At)];
        lines.Add(string.Empty);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"=== the globals section's {windows.Count} windows read back: {blocks.Count} blocks of an emitter's object uniforms (schema GpuParticleObjectUniforms) by the {durations.Count} duration needles"));
        lines.AddRange(blocks.Select(one => "  " + Said(one)));
    }

    /// <summary>
    /// The player's pieces' places in the world (PiecePlacement.World) beside the near emitters' translations - whether an emitter stands
    /// where a piece does, and with its turn.
    /// </summary>
    private static void Pieces(IMemoryReader reader, OffsetSchema schema, ulong render, IReadOnlyList<Record> near, List<string> lines)
    {
        lines.Add(string.Empty);
        lines.Add("=== the player's pieces in the world (PieceObject.Placement -> PiecePlacement.World, by rows): each one's translation, scale and turn as a quaternion, and the near emitters standing within a unit of it - the emitter's translation and rotation set beside the piece's");
        ulong composition = PlayerPieces.CompositionOf(reader, schema, render);
        if (composition == 0 || !schema.Structs.TryGetValue("PieceObject", out StructDef? objectFields) || objectFields.Field("Placement") is not { } placement
            || !schema.Structs.TryGetValue("PiecePlacement", out StructDef? placementFields) || placementFields.Field("World") is not { } world)
        {
            lines.Add("  not read: no composition, or the schema names no PieceObject.Placement or PiecePlacement.World");
            return;
        }

        byte[] bytes = new byte[64];
        foreach (PlayerPieces.Piece piece in PlayerPieces.Slots(PlayerPieces.Read(reader, schema, composition)))
        {
            ulong at = reader.ReadPointer(piece.Object + (ulong)placement.Offset);
            if (at == 0 || !reader.TryRead(at + (ulong)world.Offset, bytes))
            {
                lines.Add($"  {Path.GetFileName(piece.Path)}: its placement did not read");
                continue;
            }

            Matrix4x4 m = PoseWorld.Read(bytes, 0);
            var row = new Vector3(m.M11, m.M12, m.M13);
            float scale = row.Length();
            Vector3 t = m.Translation;
            Quaternion turn = scale > 1e-6f ? Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Matrix4x4.Multiply(m with { M41 = 0, M42 = 0, M43 = 0 }, 1f / scale) with { M44 = 1 })) : Quaternion.Identity;
            string beside = string.Join("; ", near
                .Where(one => Vector3.Distance(one.Translation, t) <= 1f)
                .Select(one => string.Create(CultureInfo.InvariantCulture, $"emitter of particles {one.ParticlesStart} {Vector3.Distance(one.Translation, t):0.###} off, its rotation {Say(one.Rotation)} ({AngleBetween(one.Rotation, turn):0.##} degrees from the piece's)")));
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {Path.GetFileName(piece.Path)}\tat {Say(t)}\tscale {scale:0.####}\tturn {Say(turn)}\t{(beside.Length > 0 ? beside : "no near emitter within a unit")}"));
        }
    }

    /// <summary>The angle between two turns in degrees, either sign of a quaternion the same turn.</summary>
    private static float AngleBetween(Quaternion a, Quaternion b)
    {
        float dot = MathF.Min(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))));
        return 2f * MathF.Acos(dot) * 180f / MathF.PI;
    }

    /// <summary>
    /// The near emitters' bones (emitter_bones_data). A bone position is in the EMITTER'S OWN FRAME - the update places it through the
    /// emitter's transform, <c>translation + RotateVec(bone * scale, rotation)</c> (gpuparticles.ffx's emitter transform with bones) - and its
    /// w is the distance along the bones (GetParticleEmitterBoneGroupLength reads the last bone's, a binary search runs over them), so an
    /// emitter's run of bones begins with w nought, pos and prev_pos alike, and the next bone's w is its distance from the first. Every such
    /// pair in the game's heap and then in the driver's heaps (<see cref="SlowLimit"/>) votes for the array's start by each near record's
    /// own first bone (base = place - 32 x start), records of both frames' arrays alike; the bases most records agree on are read back, each
    /// record's bones decoded there and placed in the world through its emitter.
    /// </summary>
    /// <remarks>
    /// MEASURED BEFORE (v0.1.433, 19:44): the bones stand neither in the region holding the arrays nor anywhere in the heap in the world's
    /// frame - every base voted for then lay inside the emitter arrays themselves, whose translation and time read as a float4 pair.
    /// </remarks>
    private static List<(ulong At, byte[] Bytes)> Bones(
        IMemoryReader reader, IMemoryRegions regions, IMemoryRegions writable, IReadOnlyList<Record> voters, Running running, List<string> lines)
    {
        FloatNeedle[] kinds = [new("a run of an emitter's bones: w nought, then the next bone's w its distance", [], 0f, Most: MostBonePlaces)];
        FloatHuntResult heap = FloatHunt.Scan(reader, writable, kinds, ChainSweep, 2 * BoneBytes, running.BoneSweep, budget: 2 * HeapScan.ByteBudget);
        var places = new List<(ulong At, string Where)>(heap.Sightings.Select(one => (one.At, "heap")));
        lines.Add(string.Empty);
        lines.Add("=== the near emitters' bones (emitter_bones_data: BoneData pos and prev_pos, a float4 each, in the emitter's own frame, w the distance along the bones) - found as runs whose first w is nought and whose second w is the distance between them, voted for by every near record's first bone");
        lines.Add("  " + Swept("the game's heap", heap, string.Empty));
        if (!running.Stopping)
        {
            using var limit = new Timer(_ => running.Slow.Stop(), null, SlowLimit, Timeout.InfiniteTimeSpan);
            FloatHuntResult slow = FloatHunt.Scan(reader, new DriverHeaps(regions), kinds, ChainSweep, 2 * BoneBytes, running.Slow, budget: long.MaxValue / 4);
            places.AddRange(slow.Sightings.Select(one => (one.At, "driver")));
            lines.Add("  " + Swept($"the driver's write-combined and uncached heaps (at most {SlowLimit.TotalSeconds:0} s)", slow, string.Empty));
        }

        List<(ulong Base, uint Agreed)> bases = Votes(places.Select(one => one.At), voters);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  {places.Count:N0} runs found, {bases.Count:N0} bases voted for by {voters.Count} near records (each record's own first bone); the {Math.Min(4, bases.Count)} most agree on, read back"));
        var blocks = new List<(ulong At, byte[] Bytes)>();
        foreach ((ulong at, uint agreed) in bases.Take(4))
        {
            string where = places.Exists(one => one.At >= at && one.At < at + (64UL * BoneBytes) && one.Where == "driver") ? "the driver's heaps" : "the heap";
            uint most = voters.Count == 0 ? 0 : voters.Max(one => one.Bones.Start + one.Bones.Count);
            byte[] bytes = new byte[(int)Math.Max(1u, most) * BoneBytes];
            if (!reader.TryRead(at, bytes))
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  --- base 0x{at:X}: did not read"));
                continue;
            }

            blocks.Add((at, bytes));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  --- base 0x{at:X} in {where}: {BitOperations.PopCount(agreed)} of {voters.Count} records agree"));
            lines.AddRange(SaidBones(bytes, voters).Select(line => "    " + line));
        }

        return blocks;
    }

    /// <summary>The chain's test: a 16-byte place where a run of bones begins - w nought now and last frame, the next bone's w its distance.</summary>
    public static void ChainSweep(ReadOnlySpan<byte> chunk, int starts, ulong at, List<FloatSighting> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        ReadOnlySpan<float> f = MemoryMarshal.Cast<byte, float>(chunk);
        int first = (int)((16 - (at & 15)) & 15) / 4;
        int last = Math.Min((starts / 4) - 1, f.Length - 16);
        for (int i = first; i <= last; i += 4)
        {
            if (f[i + 3] != 0f || f[i + 7] != 0f || f[i + 11] == 0f)
            {
                continue;
            }

            if (Chains(f.Slice(i, 16)))
            {
                into.Add(new FloatSighting(at + (ulong)(i * 4), 0));
            }
        }
    }

    /// <summary>Whether sixteen floats begin a run of bones: w nought for both of the first, and the second's w its distance from the first, now and last frame.</summary>
    public static bool Chains(ReadOnlySpan<float> f)
    {
        if (f.Length < 16 || f[3] != 0f || f[7] != 0f)
        {
            return false;
        }

        float d = Distance(f, 8, 0), dp = Distance(f, 12, 4);
        return float.IsFinite(d) && float.IsFinite(dp) && d > 0.01f && d < 1e5f
            && MathF.Abs(f[11] - d) <= 1e-3f * MathF.Max(1f, d) && MathF.Abs(f[15] - dp) <= 1e-3f * MathF.Max(1f, dp);
    }

    private static float Distance(ReadOnlySpan<float> f, int a, int b)
    {
        float x = f[a] - f[b], y = f[a + 1] - f[b + 1], z = f[a + 2] - f[b + 2];
        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }

    /// <summary>
    /// Where the bones array may start, by every run found: base = run - 32 x a record's first bone, for every record with two bones or more;
    /// the bases the most records vote for first. Shared by the capture and the offline reader.
    /// </summary>
    /// <param name="runs">Where each run of bones was found.</param>
    /// <param name="voters">The near records (at most 32), each voting by its own first bone.</param>
    public static List<(ulong Base, uint Agreed)> Votes(IEnumerable<ulong> runs, IReadOnlyList<Record> voters)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(voters);
        var votes = new Dictionary<ulong, uint>();
        foreach (ulong at in runs)
        {
            for (var r = 0; r < voters.Count && r < 32; r++)
            {
                ulong offset = (ulong)voters[r].Bones.Start * BoneBytes;
                if (voters[r].Bones.Count >= 2 && at >= offset)
                {
                    votes[at - offset] = votes.GetValueOrDefault(at - offset) | (1u << r);
                }
            }
        }

        return [.. votes
            .Select(one => (Base: one.Key, Agreed: one.Value))
            .OrderByDescending(one => BitOperations.PopCount(one.Agreed))
            .ThenBy(one => one.Base)];
    }

    /// <summary>Every record's bones as a bones array's bytes hold them from its start: each bone in the emitter's frame, its w, and where it stands in the world through the emitter.</summary>
    public static List<string> SaidBones(ReadOnlySpan<byte> bones, IReadOnlyList<Record> voters)
    {
        ArgumentNullException.ThrowIfNull(voters);
        var lines = new List<string>();
        for (var r = 0; r < voters.Count; r++)
        {
            Record emitter = voters[r];
            var said = new List<string>();
            bool chain = true;
            for (var b = 0; b < emitter.Bones.Count; b++)
            {
                int at = (int)(emitter.Bones.Start + (uint)b) * BoneBytes;
                if (at + BoneBytes > bones.Length)
                {
                    said.Add("past the bytes read");
                    chain = false;
                    break;
                }

                ReadOnlySpan<float> f = MemoryMarshal.Cast<byte, float>(bones.Slice(at, BoneBytes));
                var pos = new Vector3(f[0], f[1], f[2]);
                Vector3 world = emitter.Translation + Vector3.Transform(pos * emitter.Scale, emitter.Rotation);
                said.Add(string.Create(CultureInfo.InvariantCulture,
                    $"[{b}] {Say(pos)} w {f[3]:G6} prev {Say(new Vector3(f[4], f[5], f[6]))} w {f[7]:G6} -> world {Say(world)} ({Vector3.Distance(world, emitter.Translation):0.#} from the emitter)"));
                chain &= b > 0 || (f[3] == 0f && f[7] == 0f);
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"particles {emitter.ParticlesStart} at {emitter.Time:G8} (bones {emitter.Bones.Start}+{emitter.Bones.Count}){(chain ? string.Empty : " - NO RUN STARTS THERE")}: {string.Join("; ", said)}"));
        }

        return lines;
    }

    /// <summary>
    /// The region holding the longest array and the bones read back written beside the report (<see cref="RegionFile"/>: blocks of an address
    /// and a length, eight bytes each, then the bytes) - the frame's other uploads stand beside them.
    /// </summary>
    private static void Region(IMemoryReader reader, MemoryRegion region, string? folder, IReadOnlyList<(ulong At, byte[] Bytes)> bones, List<string> lines)
    {
        int length = (int)Math.Min(region.Size, (ulong)MostRegionBytes);
        byte[] bytes = new byte[length];
        lines.Add(string.Empty);
        bool read = reader.TryRead(region.Address, bytes);
        var blocks = new List<(ulong At, byte[] Bytes)>();
        if (read)
        {
            blocks.Add((region.Address, bytes));
        }

        blocks.AddRange(bones);
        string written = "not written (no folder)";
        if (folder is { Length: > 0 } && blocks.Count > 0)
        {
            try
            {
                using FileStream file = File.Create(Path.Combine(folder, RegionFile));
                Span<byte> head = stackalloc byte[16];
                foreach ((ulong at, byte[] block) in blocks)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(head, at);
                    BinaryPrimitives.WriteUInt64LittleEndian(head[8..], (ulong)block.Length);
                    file.Write(head);
                    file.Write(block);
                }

                written = $"written as {RegionFile} with the {bones.Count} bones arrays read back";
            }
            catch (IOException failed)
            {
                written = $"not written: {failed.Message}";
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"=== the region holding the longest array, 0x{region.Address:X} ({region.Size:N0} bytes, {region.Kind}): {(read ? $"{length:N0} bytes read" : "did not read")}, {written}"));
    }

    /// <summary>The blocks of a region file: each block's address and bytes.</summary>
    public static List<(ulong At, byte[] Bytes)> Blocks(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var blocks = new List<(ulong At, byte[] Bytes)>();
        for (var at = 0; at + 16 <= file.Length;)
        {
            ulong address = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at));
            int length = (int)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at + 8)), (ulong)(file.Length - at - 16));
            blocks.Add((address, file.AsSpan(at + 16, length).ToArray()));
            at += 16 + length;
        }

        return blocks;
    }

    /// <summary>
    /// The dispatch's own globals - gpu_particle_emitter (the record's index), gpu_particle_seed, _count, _burst, gpu_particles_duration,
    /// frame_delta_time, instance_count, batch_size - which neither a file nor a record holds: the heap and then the driver's heaps (within
    /// <see cref="SlowLimit"/>) swept for the numbers they should hold (<paramref name="anchors"/>: the panes' emitters' bursts and
    /// durations), each place's window read again AT ONE MOMENT with the frame's arrays, and every word of it marked where it equals a near
    /// emitter's index in an array read that moment, its first particle, its time or its frame's step, or an anchor's number - the windows
    /// holding an index or a step first. The block the card's constant buffer is copied from is to be read off them.
    /// </summary>
    /// <summary>
    /// One emitter's dispatch values as the processor keeps them beside its World (schema <c>GpuParticleObjectUniforms</c>): the update's
    /// object uniforms by their names - found where an emitter file's particle_duration_min and _max stand.
    /// </summary>
    /// <param name="At">Where the duration stands.</param>
    /// <param name="Duration">gpu_particles_duration: particle_duration_min and _max.</param>
    /// <param name="Burst">gpu_particle_burst: the file's emit_burst over a hundred.</param>
    /// <param name="Seed">gpu_particle_seed, a fraction.</param>
    /// <param name="Emitter">gpu_particle_emitter: the record's index in the frame's ParticleEmitterData array.</param>
    /// <param name="Count">gpu_particle_count: the pool, times the group size.</param>
    /// <param name="Culling">gpu_particle_culling_priority.</param>
    /// <param name="Passes">gpu_particle_pass_count: the render passes.</param>
    /// <param name="Start">The float after them, the same for every emitter of one effect (the effect's start on the game's clock, a reading).</param>
    /// <param name="Id">gpu_particle_emitter_id: a running number, an effect's emitters one after another.</param>
    /// <param name="Translation">The World's translation standing before them, or null where none does.</param>
    public readonly record struct ObjectUniforms(
        ulong At, Vector2 Duration, float Burst, float Seed, uint Emitter, uint Count, uint Culling, uint Passes, float Start, uint Id, Vector3? Translation);

    /// <summary>How far the World stands before the duration in a <see cref="ObjectUniforms"/> block, and how long the block is from the duration on.</summary>
    private const int UniformsWorld = 0x50, UniformsLength = 0x38;

    /// <summary>
    /// Every <see cref="ObjectUniforms"/> block in <paramref name="bytes"/> (read from <paramref name="from"/>) whose duration is one of
    /// <paramref name="durations"/> - the layout v0.1.436's capture read off the lamp's three emitters, each block once by its content.
    /// </summary>
    /// <remarks>
    /// THE FINGERPRINT (MEASURED, 2026-11-01 20:42, Canal Hideout, the staff in hand): the duration pair, a burst and a seed between nought
    /// and one, the emitter's index, the count, the culling priority, the pass count, the start, the id, two ones, a nought, and THE COUNT
    /// AGAIN - which a duration pair standing anywhere else does not carry.
    /// </remarks>
    public static List<ObjectUniforms> Uniforms(ReadOnlySpan<byte> bytes, ulong from, IReadOnlyList<Vector2> durations)
    {
        ArgumentNullException.ThrowIfNull(durations);
        var found = new List<ObjectUniforms>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var at = 0; at + UniformsLength <= bytes.Length; at += 4)
        {
            float min = BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]), max = BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 4)..]);
            if (!durations.Any(one => MathF.Abs(one.X - min) <= 1e-5f && MathF.Abs(one.Y - max) <= 1e-5f))
            {
                continue;
            }

            float burst = BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 0x08)..]), seed = BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 0x0C)..]);
            uint emitter = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 0x10)..]), count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 0x14)..]);
            uint culling = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 0x18)..]), passes = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 0x1C)..]);
            if (!(burst is >= 0f and <= 1f) || !(seed is >= 0f and < 1f) || count is 0 or > 1u << 20 || passes is 0 or > 16
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 0x34)..]) != count
                || !seen.Add(Convert.ToHexString(bytes.Slice(at, UniformsLength))))
            {
                continue;
            }

            Vector3? translation = null;
            if (at >= UniformsWorld && BinaryPrimitives.ReadSingleLittleEndian(bytes[(at - 0x14)..]) == 1f)
            {
                translation = new Vector3(
                    BinaryPrimitives.ReadSingleLittleEndian(bytes[(at - 0x20)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bytes[(at - 0x1C)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bytes[(at - 0x18)..]));
            }

            found.Add(new ObjectUniforms(
                from + (ulong)at, new Vector2(min, max), burst, seed, emitter, count, culling, passes,
                BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 0x20)..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 0x24)..]), translation));
        }

        return found;
    }

    /// <summary>One <see cref="ObjectUniforms"/> block as a line.</summary>
    public static string Said(ObjectUniforms one)
        => string.Create(CultureInfo.InvariantCulture,
            $"0x{one.At:X}: gpu_particles_duration ({one.Duration.X:G6}, {one.Duration.Y:G6}), gpu_particle_burst {one.Burst:G6}, gpu_particle_seed {one.Seed:G6}, gpu_particle_emitter {one.Emitter}, gpu_particle_count {one.Count}, gpu_particle_culling_priority {one.Culling}, gpu_particle_pass_count {one.Passes}, start {one.Start:G8}, gpu_particle_emitter_id 0x{one.Id:X}{(one.Translation is { } t ? $"; its World at ({t.X:G7}, {t.Y:G7}, {t.Z:G7})" : "; no World before it")}");

    private static void Globals(
        IMemoryReader reader, IMemoryRegions regions, IMemoryRegions writable, MemoryRegion[] all, IReadOnlyList<(ulong At, int Length)> runs,
        IReadOnlyList<Record> near, IReadOnlyList<FloatNeedle> anchors, Running running, List<string> lines)
    {
        FloatHuntResult heap = FloatHunt.Run(reader, writable, anchors, 0, running.GlobalSweep, budget: 2 * HeapScan.ByteBudget);
        FloatHuntResult? slow = null;
        if (!running.Stopping)
        {
            using var limit = new Timer(_ => running.Slow.Stop(), null, SlowLimit, Timeout.InfiniteTimeSpan);
            slow = FloatHunt.Run(reader, new DriverHeaps(regions), anchors, 0, running.Slow, budget: long.MaxValue / 4);
        }

        lines.Add(string.Empty);
        lines.Add("=== the dispatch's globals, looked for by the numbers the emitters' files give them (gpu_particle_burst, gpu_particles_duration by their names, a reading): every place of each, its window read again at one moment with the frame's arrays, each word marked - idx(P,a) a near emitter's index in array a read that moment (P its first particle), first(P) its first particle, time(P) its clock, step(P) its frame's step, and an anchor's number by its needle");
        lines.Add("  " + Swept("the game's heap", heap, string.Empty));
        if (slow is not null)
        {
            lines.Add("  " + Swept($"the driver's write-combined and uncached heaps (at most {SlowLimit.TotalSeconds:0} s)", slow, string.Empty));
        }

        for (var n = 0; n < anchors.Count; n++)
        {
            int inHeap = heap.Sightings.Count(one => one.Needle == n), inSlow = slow?.Sightings.Count(one => one.Needle == n) ?? 0;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  needle {n}: {anchors[n].Name} ({string.Join(", ", anchors[n].Values.Select(value => value.ToString("G7", CultureInfo.InvariantCulture)))}): {inHeap} places in the heap{(heap.Capped.Contains(n) ? " (capped)" : string.Empty)}, {inSlow} in the driver's heaps"));
        }

        // EVERY WINDOW AND THE ARRAYS AT ONE MOMENT: the windows first, then the arrays, as fast as they read.
        List<(FloatDump Dump, string Where)> dumps = [.. (heap.Dumps ?? []).Select(one => (one, "heap")).Concat((slow?.Dumps ?? []).Select(one => (one, "driver")))];
        var windows = new List<(FloatDump Dump, string Where, byte[] Now)>(dumps.Count);
        foreach ((FloatDump dump, string where) in dumps)
        {
            byte[] now = new byte[dump.Bytes.Length];
            windows.Add((dump, where, reader.TryRead(dump.From, now) ? now : dump.Bytes));
        }

        var index = new Dictionary<uint, List<(uint Index, int Array, float Time, float Step)>>();
        HashSet<uint> firsts = [.. near.Select(one => one.ParticlesStart)];
        List<(ulong At, int Length)> arrays = [.. runs.Where(one => one.Length >= 8).OrderByDescending(one => one.Length).Take(4)];
        for (var a = 0; a < arrays.Count; a++)
        {
            byte[] bytes = new byte[Math.Min(arrays[a].Length + 64, FollowRecords) * RecordBytes];
            if (!reader.TryRead(arrays[a].At, bytes))
            {
                continue;
            }

            for (var r = 0; r < bytes.Length / RecordBytes; r++)
            {
                ReadOnlySpan<byte> record = bytes.AsSpan(r * RecordBytes, RecordBytes);
                uint first = BinaryPrimitives.ReadUInt32LittleEndian(record[0x8C..]);
                if (!firsts.Contains(first) || !Fits(MemoryMarshal.Cast<byte, float>(record)))
                {
                    continue;
                }

                float time = BinaryPrimitives.ReadSingleLittleEndian(record[0x6C..]), prev = BinaryPrimitives.ReadSingleLittleEndian(record[0x7C..]);
                if (!index.TryGetValue(first, out List<(uint Index, int Array, float Time, float Step)>? list))
                {
                    index[first] = list = [];
                }

                list.Add(((uint)r, a, time, time - prev));
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  the near emitters in the arrays read with the windows: {string.Join("; ", index.Select(one => $"first {one.Key}: {string.Join(", ", one.Value.Select(v => string.Create(CultureInfo.InvariantCulture, $"index {v.Index} in array {v.Array} at {v.Time:G8} (step {v.Step:G5})")))}"))}"));

        // THE MARKS, then the windows holding an index or a step first.
        string Marks(byte[] bytes, int at)
        {
            uint u = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
            float f = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at));
            var marks = new List<string>();
            foreach ((uint first, List<(uint Index, int Array, float Time, float Step)> seen) in index)
            {
                marks.AddRange(seen.Where(one => one.Index == u && u != 0).Select(one => string.Create(CultureInfo.InvariantCulture, $"idx({first},{one.Array})")));
                if (u == first)
                {
                    marks.Add($"first({first})");
                }

                if (float.IsFinite(f) && f != 0f)
                {
                    marks.AddRange(seen.Where(one => MathF.Abs(one.Time - f) <= 1e-3f).Select(one => $"time({first})"));
                    marks.AddRange(seen.Where(one => one.Step > 0 && MathF.Abs(one.Step - f) <= 2e-4f).Select(one => $"step({first})"));
                }
            }

            for (var n = 0; n < anchors.Count; n++)
            {
                if (f != 0f && Array.Exists(anchors[n].Values, value => MathF.Abs(value - f) <= 1e-5f))
                {
                    marks.Add($"n{n}");
                }
            }

            return string.Join(",", marks.Distinct());
        }

        // THE EMITTERS' OBJECT UNIFORMS as v0.1.436's capture read them off the windows, decoded wherever a duration needle's place carries them.
        List<Vector2> durations = [.. anchors.Where(one => one.Values.Length == 2).Select(one => new Vector2(one.Values[0], one.Values[1]))];
        List<ObjectUniforms> blocks = [.. windows.SelectMany(one => Uniforms(one.Now, one.Dump.From, durations)).DistinctBy(one => one.At)];
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  {blocks.Count} blocks of an emitter's object uniforms in the windows (schema GpuParticleObjectUniforms: the duration, the burst, the seed, the emitter's index, the count, the culling priority, the pass count, the start, the id, then the count again), the near emitters' indices beside them"));
        foreach (ObjectUniforms block in blocks)
        {
            string marks = string.Join(", ", index.Where(one => one.Value.Exists(seen => seen.Index == block.Emitter)).Select(one => $"the index of the emitter of particles {one.Key}"));
            lines.Add("  uniforms " + Said(block) + (marks.Length > 0 ? " - " + marks : string.Empty));
        }

        var said = windows
            .Select(one => (one.Dump, one.Where, one.Now, Telling: Enumerable.Range(0, one.Now.Length / 4).Count(w => Marks(one.Now, w * 4) is { Length: > 0 } m && (m.Contains("idx(", StringComparison.Ordinal) || m.Contains("step(", StringComparison.Ordinal)))))
            .OrderByDescending(one => one.Telling)
            .ThenBy(one => one.Dump.At)
            .ToList();
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {said.Count} windows of {2 * CaptureWindow} bytes and the needle's own; the {said.Count(one => one.Telling > 0)} holding a near emitter's index or step first; each row 16 bytes - its offset from the needle, four words as hex, as floats, and their marks"));
        foreach ((FloatDump dump, string where, byte[] now, int telling) in said)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"--- needle {dump.Needle} at 0x{dump.At:X} ({where}, {KindAt(all, dump.At)}): {telling} words an index or a step; changed since the sweep: {(now.AsSpan().SequenceEqual(dump.Bytes) ? "no" : "yes")}"));
            for (var row = 0; row + 16 <= now.Length; row += 16)
            {
                long offset = (long)(dump.From + (ulong)row) - (long)dump.At;
                var floats = new StringBuilder();
                var hex = new StringBuilder();
                var marks = new List<string>();
                for (var w = 0; w < 4; w++)
                {
                    hex.Append(CultureInfo.InvariantCulture, $"{BinaryPrimitives.ReadUInt32LittleEndian(now.AsSpan(row + (w * 4))):X8} ");
                    floats.Append(CultureInfo.InvariantCulture, $"{BinaryPrimitives.ReadSingleLittleEndian(now.AsSpan(row + (w * 4))):G6} ");
                    if (Marks(now, row + (w * 4)) is { Length: > 0 } m)
                    {
                        marks.Add(string.Create(CultureInfo.InvariantCulture, $"[{w}] {m}"));
                    }
                }

                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {offset,6:+0;-0}\t{hex}\t{floats}\t{string.Join(" ", marks)}"));
            }
        }
    }

    /// <summary>The bytes either side of an anchor's place the globals' windows hold - CaptureParticles.GlobalsAround's.</summary>
    private const int CaptureWindow = 256;

    /// <summary>
    /// The near emitters followed frame after frame by their first particle: every array read whole as often as it changes - the runs found
    /// and their kin a stride apart in the same region (the frames' buffers, measured two of them 0x3FFFC0 apart) - and each emitter's record
    /// of the latest time kept where it differs from the last.
    /// </summary>
    private static void Follow(IMemoryReader reader, MemoryRegion[] all, IReadOnlyList<(ulong At, int Length)> runs, IReadOnlyList<Record> near, List<string> lines)
    {
        // THE WINDOWS: each long run, and in its region the places a stride apart that the two longest set.
        List<(ulong At, int Length)> longRuns = [.. runs.Where(one => one.Length >= 8).OrderByDescending(one => one.Length).Take(4)];
        var windows = new SortedSet<ulong>(longRuns.Select(one => one.At));
        if (longRuns.Count >= 2)
        {
            ulong stride = longRuns[0].At > longRuns[1].At ? longRuns[0].At - longRuns[1].At : longRuns[1].At - longRuns[0].At;
            MemoryRegion region = Array.Find(all, one => longRuns[0].At >= one.Address && longRuns[0].At < one.Address + one.Size);
            for (int k = -3; k <= 3 && stride > 0; k++)
            {
                long at = (long)longRuns[0].At + (k * (long)stride);
                if (at >= (long)region.Address && (ulong)at + RecordBytes <= region.Address + region.Size)
                {
                    windows.Add((ulong)at);
                }
            }
        }

        var series = new Dictionary<uint, List<(double Seconds, byte[] Bytes, int Window)>>();
        foreach (Record one in near)
        {
            series[one.ParticlesStart] = [];
        }

        ulong[] starts = [.. windows];
        var counts = new List<int>[starts.Length];
        for (var w = 0; w < starts.Length; w++)
        {
            counts[w] = [];
        }

        byte[] buffer = new byte[FollowRecords * RecordBytes];
        var clock = Stopwatch.StartNew();
        int reads = 0;
        while (clock.Elapsed < WatchFor)
        {
            reads++;
            var latest = new Dictionary<uint, (byte[] Bytes, float Time, int Window)>();
            for (var w = 0; w < starts.Length; w++)
            {
                MemoryRegion region = Array.Find(all, one => starts[w] >= one.Address && starts[w] < one.Address + one.Size);
                int length = (int)Math.Min((ulong)buffer.Length, region.Address + region.Size - starts[w]) / RecordBytes * RecordBytes;
                if (length <= 0 || !reader.TryRead(starts[w], buffer.AsSpan(0, length)))
                {
                    continue;
                }

                int fitting = 0;
                for (var r = 0; r < length; r += RecordBytes)
                {
                    ReadOnlySpan<byte> record = buffer.AsSpan(r, RecordBytes);
                    if (!Fits(MemoryMarshal.Cast<byte, float>(record)))
                    {
                        continue;
                    }

                    fitting++;
                    uint first = BinaryPrimitives.ReadUInt32LittleEndian(record[0x8C..]);
                    float time = BinaryPrimitives.ReadSingleLittleEndian(record[0x6C..]);
                    if (series.ContainsKey(first) && (!latest.TryGetValue(first, out var was) || time > was.Time))
                    {
                        latest[first] = (record.ToArray(), time, w);
                    }
                }

                if (counts[w].Count == 0 || counts[w][^1] != fitting)
                {
                    counts[w].Add(fitting);
                }
            }

            foreach ((uint first, (byte[] bytes, _, int window)) in latest)
            {
                List<(double Seconds, byte[] Bytes, int Window)> kept = series[first];
                if (kept.Count == 0 || !kept[^1].Bytes.AsSpan().SequenceEqual(bytes))
                {
                    kept.Add((clock.Elapsed.TotalSeconds, bytes, window));
                }
            }

            Thread.Sleep(WatchEvery);
        }

        lines.Add(string.Empty);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"=== the {near.Count} emitters near the player followed by their first particle through {starts.Length} arrays ({string.Join(", ", starts.Select(one => $"0x{one:X}"))}), read {reads} times over {clock.Elapsed.TotalSeconds:0.0} s; each array's records counted as they changed: {string.Join("; ", counts.Select((one, w) => $"{w}: {string.Join(" ", one.Take(12))}{(one.Count > 12 ? " ..." : string.Empty)}"))}"));
        foreach (Record one in near)
        {
            List<(double Seconds, byte[] Bytes, int Window)> kept = series[one.ParticlesStart];
            if (kept.Count < 2)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"--- particles {one.ParticlesStart}: {(kept.Count == 0 ? "not seen again" : "seen, never changed")}"));
                continue;
            }

            Record first = Decode(kept[0].Bytes), end = Decode(kept[^1].Bytes);
            double span = kept[^1].Seconds - kept[0].Seconds;
            List<float> steps = [.. kept.Skip(1).Select(entry => Decode(entry.Bytes)).Select(r => r.Time - r.PrevTime).Order()];
            float median = steps[steps.Count / 2];
            float turned = kept.Skip(1).Max(entry => AngleBetween(Decode(entry.Bytes).Rotation, first.Rotation));
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"--- particles {one.ParticlesStart} (group size {one.GroupSize}, locks 0x{one.Locks:X}): {kept.Count - 1} changes over {span:0.###} s; time {first.Time:G8} -> {end.Time:G8} ({(span > 0 ? (end.Time - first.Time) / span : 0):G5} a second), its step at the median {median:G5} s (a frame of {(median > 0 ? 1 / median : 0):0.#} a second); translation moved {Vector3.Distance(first.Translation, end.Translation):0.##}, turned at most {turned:0.##} degrees; bones starts {string.Join(" ", kept.Select(entry => Decode(entry.Bytes).Bones.Start).Distinct())}; flags {string.Join(" ", kept.Select(entry => $"0x{Decode(entry.Bytes).Flags:X}").Distinct())}"));
            for (var change = 1; change < kept.Count && change <= MostChanges; change++)
            {
                Record r = Decode(kept[change].Bytes);
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  {kept[change].Seconds:0.0000}\tarray {kept[change].Window}\ttime {r.Time:G8}\tprev {r.PrevTime:G8}\tstep {r.Time - r.PrevTime:G5}\tat {Say(r.Translation)}\tturn {Say(r.Rotation)}\tbones {r.Bones.Start}+{r.Bones.Count}\tflags 0x{r.Flags:X}\tchanged {Changed(kept[change - 1].Bytes, kept[change].Bytes)}"));
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

    private static Vector3? PlayerPlace(IMemoryReader reader, OffsetSchema schema, ulong gameStatesStatic, out string who, out ulong render)
    {
        who = "unread";
        render = 0;
        GameChainAddresses chain = GameChain.Resolve(reader, schema, gameStatesStatic);
        var entities = new EntityReader(reader, schema);
        if (chain.PlayerEntity == 0 || entities.ReadIdentity(chain.PlayerEntity) is not { } player)
        {
            return null;
        }

        who = player.Path;
        render = entities.ReadComponents(player.Address, player.Details).GetValueOrDefault("Render");
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
