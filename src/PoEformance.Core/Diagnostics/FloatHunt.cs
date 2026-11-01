using System.Buffers;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using PoEformance.Core.Memory;

namespace PoEformance.Core.Diagnostics;

/// <summary>A run of floats to look for in the target, side by side.</summary>
/// <param name="Name">What it is, for the report.</param>
/// <param name="Values">The floats in order; a NaN is a place left unchecked - padding in a matrix row, say.</param>
/// <param name="Tolerance">How far each float may be from its value; nought asks for the exact value.</param>
/// <param name="Around">How many bytes either side of its first few places to read back with the result - nought for none. See <see cref="FloatDump"/>.</param>
/// <param name="Dumps">How many of its places to read back - nought for <see cref="FloatHunt.MostDumps"/>.</param>
/// <param name="Most">
/// How many of its places are kept, nearest the anchor first - nought for <see cref="FloatHunt.MostPerNeedle"/>. A needle standing for a
/// whole box of places (the bone watch's world needles, v0.1.407) asks for thousands.
/// </param>
public sealed record FloatNeedle(string Name, float[] Values, float Tolerance, int Around = 0, int Dumps = 0, int Most = 0);

/// <summary>The bytes read round a place a needle was found, for a person to see what the game keeps beside it.</summary>
/// <param name="At">Where the needle was found.</param>
/// <param name="Needle">Which needle.</param>
/// <param name="From">The address of the first byte read.</param>
/// <param name="Bytes">What was there.</param>
public sealed record FloatDump(ulong At, int Needle, ulong From, byte[] Bytes);

/// <summary>Where a needle was found.</summary>
/// <param name="At">The address of its first float.</param>
/// <param name="Needle">Which needle, by its index in the list asked for.</param>
public readonly record struct FloatSighting(ulong At, int Needle);

/// <summary>
/// Looks through one chunk of the target's memory for what no run of floats can say - a record known by how its fields agree with one
/// another - and adds every place it finds, tagged by which of the caller's kinds it is. See <see cref="FloatHunt.Scan"/>.
/// </summary>
/// <param name="chunk">The chunk's bytes and a few read past it, so a record starting in it is seen whole.</param>
/// <param name="starts">How many of its bytes are this chunk's own: a record starting past them is the next chunk's.</param>
/// <param name="at">The address of its first byte.</param>
/// <param name="into">Where the places go, each with its kind as the needle.</param>
public delegate void FloatSweeper(ReadOnlySpan<byte> chunk, int starts, ulong at, List<FloatSighting> into);

/// <summary>How far a running hunt has got - read from another thread while it runs - and how to stop it.</summary>
public sealed class FloatHuntProgress
{
    private long _bytes;
    private long _regions;
    private long _places;
    private long _budget;
    private int _stop;

    /// <summary>How many bytes have been looked through so far.</summary>
    public long Bytes => Volatile.Read(ref _bytes);

    /// <summary>How many regions have been begun.</summary>
    public long Regions => Volatile.Read(ref _regions);

    /// <summary>How many places the needles were found at so far, before each needle's cap is applied.</summary>
    public long Places => Volatile.Read(ref _places);

    /// <summary>The most bytes the hunt looks through, set as it starts - nought before, and for a hunt that does not say.</summary>
    public long Budget => Volatile.Read(ref _budget);

    /// <summary>Whether <see cref="Stop"/> was asked for.</summary>
    public bool Stopping => Volatile.Read(ref _stop) != 0;

    /// <summary>Asks the hunt to end at its next chunk and hand back what it found so far - its result then says it was stopped.</summary>
    public void Stop() => Volatile.Write(ref _stop, 1);

    internal void Add(long bytes) => Interlocked.Add(ref _bytes, bytes);

    internal void Began() => Interlocked.Increment(ref _regions);

    internal void Found(int places) => Interlocked.Add(ref _places, places);

    internal void Budgeted(long budget) => Volatile.Write(ref _budget, budget);
}

/// <summary>What one kind of memory cost a hunt - see <see cref="MemoryRegion.Kind"/>.</summary>
/// <param name="Kind">The regions' type and protection, in words.</param>
/// <param name="Regions">How many of them were begun.</param>
/// <param name="Bytes">How many of their bytes were looked through.</param>
/// <param name="Reading">The time spent reading them out of the game, summed over every worker.</param>
/// <param name="Sweeping">The time spent looking through what was read, summed likewise.</param>
/// <param name="Failed">How many of them gave up a read part way, the rest of the region left.</param>
public sealed record FloatHuntKind(string Kind, long Regions, long Bytes, TimeSpan Reading, TimeSpan Sweeping, long Failed);

/// <summary>What a float hunt looked through and what it found.</summary>
/// <param name="RegionsWalked">How many regions were read.</param>
/// <param name="BytesScanned">How many bytes.</param>
/// <param name="Sightings">Every place a needle was found, at most <see cref="FloatHunt.Most"/> of it a needle.</param>
/// <param name="Capped">The needles found more often than that - the count is then a floor.</param>
/// <param name="Unsearched">The needles that could not be looked for: nothing in them but nought, one and blanks.</param>
/// <param name="Truncated">True when the byte budget stopped it before the last region.</param>
/// <param name="Took">How long it took.</param>
/// <param name="Dumps">The bytes round the first places of the needles that asked for them, or null.</param>
public sealed record FloatHuntResult(
    long RegionsWalked,
    long BytesScanned,
    IReadOnlyList<FloatSighting> Sightings,
    IReadOnlyList<int> Capped,
    IReadOnlyList<int> Unsearched,
    bool Truncated,
    TimeSpan Took,
    IReadOnlyList<FloatDump>? Dumps = null)
{
    /// <summary>True when <see cref="FloatHuntProgress.Stop"/> ended it before the last region - what it holds is what was found until then.</summary>
    public bool Stopped { get; init; }

    /// <summary>How many threads looked.</summary>
    public int Workers { get; init; } = 1;

    /// <summary>What each kind of memory cost, the most bytes first.</summary>
    public IReadOnlyList<FloatHuntKind> Kinds { get; init; } = [];

    /// <summary>How many regions the target listed, those too large to be data among them.</summary>
    public long Listed { get; init; }

    /// <summary>How long listing and ordering them took, before the first byte was read.</summary>
    public TimeSpan Listing { get; init; }
}

/// <summary>
/// Searches the whole of the target's memory for runs of floats near given values - a vector or a matrix the caller can work out, to learn where and in which form the game keeps it.
/// </summary>
/// <remarks>
/// WHAT IT IS FOR: a value the game computes on its processor and writes in no file - the sun's
/// direction from an environment's two angles, say - can be worked out every way it might be, and
/// the way the game holds is the one found in its memory. The search asks the game rather than a
/// screenshot, which is what this project prefers wherever it can.
///
/// EACH NEEDLE IS LOOKED FOR BY ONE FLOAT FIRST - its most telling one, never a nought or a one,
/// which half of memory is - and only where that one matches are the rest compared. One cheap pass
/// comes before any of that: each word's top sixteen bits - a float's sign, exponent and first
/// mantissa bits - looked up in a bitmap of the buckets the anchors fall in. A few dozen buckets of
/// 65536 are marked, so hardly a word in a thousand goes further. About 2.7 GB a second on one
/// core against a test heap a fifth of it floats, before the cost of reading the game.
///
/// NEAREST THE ANCHOR FIRST, within the same budget as <see cref="HeapScan"/>, for the reason its
/// remarks give: in address order the budget goes on driver mappings far below the game's heap.
///
/// NOT RECORDED: it reads in megabyte chunks, above the recorder's cap, like HeapScan.
///
/// ON EVERY CORE, asked for from the live client: a capture's hunt over sixteen gigabytes on one
/// thread was still running after two minutes, and whoever presses the capture key has chosen to
/// spend the machine on it. The chunks are handed out one at a time, nearest the anchor first, so
/// the workers still go outward together; what each found is put back in that order before each
/// needle's cap is applied, so the first places kept are the nearest ones as they were on one thread.
/// </remarks>
public static class FloatHunt
{
    /// <summary>Most places kept for one needle that does not say (<see cref="FloatNeedle.Most"/>); past it the needle is reported as capped.</summary>
    public const int MostPerNeedle = 64;

    /// <summary>The longest needle, in floats - a 4 by 4 matrix.</summary>
    public const int LongestNeedle = 16;

    /// <summary>How many of a needle's places have their surroundings read back, where it asks.</summary>
    public const int MostDumps = 4;

    /// <summary>The most bytes read either side of a place.</summary>
    public const int MostAround = 4096;

    /// <summary>
    /// Walks every readable region, nearest the anchor first, and reports where each needle lies.
    /// </summary>
    /// <param name="reader">The target's memory.</param>
    /// <param name="regions">Its regions.</param>
    /// <param name="needles">What to look for.</param>
    /// <param name="anchor">An address in the game's own heap to start from; nought keeps the enumeration order.</param>
    /// <param name="progress">Where to say how far it has got, and where it is asked to stop - or null.</param>
    /// <param name="cancel">Stops it between chunks, throwing.</param>
    /// <param name="budget">The most bytes looked through - HeapScan's budget unless the caller wants further.</param>
    /// <param name="workers">How many threads look - nought for one a core.</param>
    public static FloatHuntResult Run(
        IMemoryReader reader,
        IMemoryRegions regions,
        IReadOnlyList<FloatNeedle> needles,
        ulong anchor = 0,
        FloatHuntProgress? progress = null,
        CancellationToken cancel = default,
        long budget = HeapScan.ByteBudget,
        int workers = 0)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(needles);

        var clock = Stopwatch.StartNew();
        var unsearched = new List<int>();
        var anchors = new List<Anchor>(needles.Count);
        int longest = 1;
        for (var at = 0; at < needles.Count; at++)
        {
            FloatNeedle needle = needles[at];
            if (needle.Values.Length is 0 or > LongestNeedle || AnchorOf(needle.Values, needle.Tolerance) is not { } first)
            {
                unsearched.Add(at);
                continue;
            }

            float value = needle.Values[first];
            float tolerance = MathF.Max(0f, needle.Tolerance);
            anchors.Add(new Anchor(at, first, value - tolerance, value + tolerance));
            longest = Math.Max(longest, needle.Values.Length);
        }

        if (anchors.Count == 0)
        {
            return new FloatHuntResult(0, 0, [], [], unsearched, false, clock.Elapsed);
        }

        progress?.Budgeted(budget);
        var hunt = new Hunt(reader, needles, [.. anchors.OrderBy(one => one.Low)], longest * sizeof(float), budget, progress, cancel);
        hunt.Plan(anchor == 0 ? [.. regions.Regions()] : [.. regions.Regions().OrderBy(one => HeapScan.Distance(one, anchor))]);
        TimeSpan listing = clock.Elapsed;
        hunt.Go(workers > 0 ? workers : Environment.ProcessorCount);
        cancel.ThrowIfCancellationRequested();

        (List<FloatSighting> sightings, HashSet<int> capped) = hunt.Gather();
        return new FloatHuntResult(
            hunt.Walked, hunt.Scanned, sightings, [.. capped.Order()], unsearched, hunt.Truncated, clock.Elapsed, Dumps(reader, needles, sightings))
        {
            Stopped = hunt.Stopped,
            Workers = hunt.Workers,
            Kinds = hunt.Kinds(),
            Listed = hunt.Listed,
            Listing = listing,
        };
    }

    /// <summary>
    /// Walks every region as <see cref="Run"/> does - on every core, the chunks handed out in order, within a budget - but hands each chunk
    /// to the caller's own test, for a record known by how its fields agree rather than by values the caller can work out beforehand.
    /// </summary>
    /// <remarks>
    /// FOR THE ENGINE'S PARTICLE EMITTER RECORDS (v0.1.432, ParticleEmitterHunt): each holds a scale beside its exact inverse and two unit
    /// quaternions, which say what it is wherever it lies and whatever it holds - no needle of values could. The kinds the sweeper tags are
    /// capped and gathered as needles are: <paramref name="kinds"/> names them and says how many of each are kept.
    /// </remarks>
    /// <param name="reader">The target's memory.</param>
    /// <param name="regions">Its regions.</param>
    /// <param name="kinds">What the sweeper finds, one a kind: the name for the report and how many places are kept (<see cref="FloatNeedle.Most"/>).</param>
    /// <param name="sweeper">The test, run on every chunk on every worker - it must touch nothing shared.</param>
    /// <param name="overlap">How many bytes past a chunk it needs to see a record starting at its last byte whole.</param>
    /// <param name="progress">Where to say how far it has got, and where it is asked to stop - or null.</param>
    /// <param name="budget">The most bytes looked through.</param>
    /// <param name="workers">How many threads look - nought for one a core.</param>
    public static FloatHuntResult Scan(
        IMemoryReader reader,
        IMemoryRegions regions,
        IReadOnlyList<FloatNeedle> kinds,
        FloatSweeper sweeper,
        int overlap,
        FloatHuntProgress? progress = null,
        long budget = HeapScan.ByteBudget,
        int workers = 0)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(sweeper);
        var clock = Stopwatch.StartNew();
        progress?.Budgeted(budget);
        var hunt = new Hunt(reader, kinds, [], Math.Max(0, overlap), budget, progress, default) { Custom = sweeper };
        hunt.Plan([.. regions.Regions()]);
        TimeSpan listing = clock.Elapsed;
        hunt.Go(workers > 0 ? workers : Environment.ProcessorCount);
        (List<FloatSighting> sightings, HashSet<int> capped) = hunt.Gather();
        return new FloatHuntResult(hunt.Walked, hunt.Scanned, sightings, [.. capped.Order()], [], hunt.Truncated, clock.Elapsed)
        {
            Stopped = hunt.Stopped,
            Workers = hunt.Workers,
            Kinds = hunt.Kinds(),
            Listed = hunt.Listed,
            Listing = listing,
        };
    }

    /// <summary>
    /// The part of a hunt's result that belongs to a run of its needles - as though they alone had been looked for, numbered from nought.
    /// </summary>
    /// <remarks>
    /// FOR TWO READERS OF ONE SWEEP: the light hunt and the player light's hunt look through the game's memory once, their needles side by
    /// side, and each reads back its own. What the sweep cost (bytes, regions, time, kinds) stays the whole sweep's.
    /// </remarks>
    /// <param name="result">The whole sweep's result.</param>
    /// <param name="first">The run's first needle, by its index in the list looked for.</param>
    /// <param name="count">How many needles the run holds.</param>
    public static FloatHuntResult Part(FloatHuntResult result, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(result);
        bool Ours(int needle) => needle >= first && needle < first + count;
        return result with
        {
            Sightings = [.. result.Sightings.Where(one => Ours(one.Needle)).Select(one => one with { Needle = one.Needle - first })],
            Capped = [.. result.Capped.Where(Ours).Select(one => one - first)],
            Unsearched = [.. result.Unsearched.Where(Ours).Select(one => one - first)],
            Dumps = result.Dumps is null ? null : [.. result.Dumps.Where(one => Ours(one.Needle)).Select(one => one with { Needle = one.Needle - first })],
        };
    }

    /// <summary>The bytes round the first places of each needle that asked for them, windows another one already read whole left out.</summary>
    /// <remarks>
    /// THE NARROWEST WINDOWS FIRST, then by address: a wide window asked for to read one value well
    /// past its needle - the light hunt's dust colour, 0x360 bytes after phi - would otherwise swallow
    /// a narrow one asked for to be printed whole, the sun's vector among them.
    ///
    /// LEFT OUT ONLY WHERE A WINDOW READ ALREADY HOLDS ALL OF IT. Left out wherever the place lay in one,
    /// the widest window could never be read: the light hunt's water window round phi (4096 bytes either
    /// side) lies inside the narrower hor_angle window read before it, every time.
    /// </remarks>
    private static List<FloatDump> Dumps(IMemoryReader reader, IReadOnlyList<FloatNeedle> needles, List<FloatSighting> sightings)
    {
        var dumps = new List<FloatDump>();
        var taken = new int[needles.Count];
        foreach (FloatSighting one in sightings.OrderBy(sighting => Math.Clamp(needles[sighting.Needle].Around, 0, MostAround)).ThenBy(sighting => sighting.At))
        {
            int around = Math.Clamp(needles[one.Needle].Around, 0, MostAround);
            int most = needles[one.Needle].Dumps > 0 ? Math.Min(needles[one.Needle].Dumps, MostPerNeedle) : MostDumps;
            if (around == 0 || taken[one.Needle] >= most)
            {
                continue;
            }

            ulong from = one.At > (ulong)around ? one.At - (ulong)around : 0;
            var bytes = new byte[(2 * around) + (needles[one.Needle].Values.Length * sizeof(float))];
            ulong end = from + (ulong)bytes.Length;
            if (dumps.Exists(dump => from >= dump.From && end <= dump.From + (ulong)dump.Bytes.Length))
            {
                continue;
            }

            if (reader.TryRead(from, bytes))
            {
                taken[one.Needle]++;
                dumps.Add(new FloatDump(one.At, one.Needle, from, bytes));
            }
        }

        return dumps;
    }

    /// <summary>
    /// The float a needle is first looked for by: its largest that cannot be taken for nought or one within the tolerance, and is no blank - null where it has none.
    /// </summary>
    /// <remarks>
    /// WITHIN THE TOLERANCE, NOT EXACTLY: Port's hor_angle is pi, so every turn of the cube the light hunt
    /// looks for is made of noughts and ones but for sin(pi), which a float makes -8.7e-8. That was the
    /// largest value that was not exactly nought or one, so it became the anchor - and its range, 1e-4
    /// either side, takes in nought. Every bucket from nought up to 1e-4 was marked on both signs, which
    /// is every nought in memory and every integer below a billion, and each such word was compared with
    /// the 572 arrangements of the turn: 3.15 GB took 4860 s of the threads' time to look through and
    /// 7.7 s to read. A needle of noughts and ones in all but rounding is an identity, found everywhere and
    /// telling nothing, so it is not looked for.
    /// </remarks>
    internal static int? AnchorOf(float[] values, float tolerance)
    {
        float within = MathF.Max(0f, tolerance);
        int? best = null;
        for (var at = 0; at < values.Length; at++)
        {
            float value = values[at];
            if (!float.IsFinite(value) || MathF.Abs(value) <= within || MathF.Abs(MathF.Abs(value) - 1f) <= within)
            {
                continue;
            }

            if (best is not { } was || MathF.Abs(value) > MathF.Abs(values[was]))
            {
                best = at;
            }
        }

        return best;
    }

    /// <summary>
    /// The buckets the anchors fall in, one bit each: a float's bucket is its top sixteen bits, which order the floats of one sign by size.
    /// </summary>
    private static ulong[] Buckets(Anchor[] anchors)
    {
        var marks = new ulong[(1 << 16) / 64];
        foreach (Anchor one in anchors)
        {
            // ACROSS NOUGHT the two signs are two runs of buckets, each from nought outward.
            if (one.Low < 0f && one.High > 0f)
            {
                Mark(marks, one.Low, -0f);
                Mark(marks, 0f, one.High);
            }
            else
            {
                Mark(marks, one.Low, one.High);
            }
        }

        return marks;
    }

    /// <summary>Marks every bucket from one float's to another's, both of one sign.</summary>
    private static void Mark(ulong[] marks, float from, float to)
    {
        uint a = Bucket(from), b = Bucket(to);
        for (uint bucket = Math.Min(a, b); bucket <= Math.Max(a, b); bucket++)
        {
            marks[bucket >> 6] |= 1UL << (int)(bucket & 63);
        }
    }

    private static uint Bucket(float value) => (uint)BitConverter.SingleToInt32Bits(value) >> 16;

    /// <summary>Checks one chunk: each word by its bucket, and the few in a marked one against the anchors.</summary>
    /// <param name="chunk">The chunk and the few floats read past it.</param>
    /// <param name="starts">How many of its bytes are this chunk's own - a needle starting past them is the next chunk's.</param>
    /// <remarks>
    /// ONE SHIFT AND ONE BIT TEST A WORD, and that is all nearly every word costs: a pass over the
    /// floats' size a vector at a time came first once, and it was slower - in memory full of floats
    /// most vectors hold one of the right size, and picking the lanes back out cost more than it saved.
    /// </remarks>
    private static void Sweep(
        ReadOnlySpan<byte> chunk, int starts, ulong at, IReadOnlyList<FloatNeedle> needles, Anchor[] anchors, float widest, ulong[] buckets,
        int[] found, HashSet<int> capped, List<FloatSighting> into)
    {
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(chunk);
        ReadOnlySpan<ulong> marks = buckets;
        for (var index = 0; index < words.Length; index++)
        {
            uint bucket = words[index] >> 16;
            if ((marks[(int)(bucket >> 6)] & (1UL << (int)(bucket & 63))) != 0)
            {
                Check(MemoryMarshal.Cast<byte, float>(chunk), index, starts, at, needles, anchors, widest, found, capped, into);
            }
        }
    }

    /// <summary>
    /// Compares the needles whose first float this one may be, found by a binary search on the anchors' lower bounds - there are hundreds of them when every turn of a matrix is looked for.
    /// </summary>
    private static void Check(
        ReadOnlySpan<float> floats, int index, int starts, ulong at, IReadOnlyList<FloatNeedle> needles, Anchor[] anchors, float widest,
        int[] found, HashSet<int> capped, List<FloatSighting> into)
    {
        float value = floats[index];
        int low = 0, high = anchors.Length;
        float least = value - widest;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (anchors[middle].Low < least)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (int place = low; place < anchors.Length; place++)
        {
            Anchor one = anchors[place];
            if (one.Low > value)
            {
                break;
            }

            if (one.High < value)
            {
                continue;
            }

            FloatNeedle needle = needles[one.Needle];
            int start = index - one.Index;
            if (start < 0 || start * sizeof(float) >= starts || start + needle.Values.Length > floats.Length)
            {
                continue;
            }

            if (!Matches(floats.Slice(start, needle.Values.Length), needle))
            {
                continue;
            }

            if (found[one.Needle] >= Most(needle))
            {
                capped.Add(one.Needle);
                continue;
            }

            found[one.Needle]++;
            into.Add(new FloatSighting(at + (ulong)(start * sizeof(float)), one.Needle));
        }
    }

    /// <summary>How many of a needle's places are kept: its own <see cref="FloatNeedle.Most"/>, else <see cref="MostPerNeedle"/>.</summary>
    public static int Most(FloatNeedle needle)
    {
        ArgumentNullException.ThrowIfNull(needle);
        return needle.Most > 0 ? needle.Most : MostPerNeedle;
    }

    private static bool Matches(ReadOnlySpan<float> floats, FloatNeedle needle)
    {
        float tolerance = MathF.Max(0f, needle.Tolerance);
        for (var at = 0; at < floats.Length; at++)
        {
            float wanted = needle.Values[at];
            if (float.IsNaN(wanted))
            {
                continue;
            }

            if (!(MathF.Abs(floats[at] - wanted) <= tolerance))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A needle's first float and the values it may take.</summary>
    private readonly record struct Anchor(int Needle, int Index, float Low, float High);

    /// <summary>One chunk of one region - the unit the workers take, in the order the regions were put.</summary>
    private readonly record struct Chunk(int Region, ulong Offset);

    /// <summary>One run's shared state: the chunks to take, the bytes looked through, and each worker's own findings.</summary>
    private sealed class Hunt(
        IMemoryReader reader, IReadOnlyList<FloatNeedle> needles, Anchor[] anchors, int overlap, long budget, FloatHuntProgress? progress,
        CancellationToken cancel)
    {
        private readonly float _widest = anchors.Length == 0 ? 0f : anchors.Max(one => one.High - one.Low);
        private readonly ulong[] _buckets = Buckets(anchors);
        private readonly List<string> _kindNames = [];
        private MemoryRegion[] _regions = [];
        private int[] _kindOf = [];
        private int[] _dead = [];
        private Chunk[] _chunks = [];
        private Lane[] _lanes = [];
        private int _next = -1;
        private long _scanned;
        private ExceptionDispatchInfo? _failed;

        public long Scanned => Volatile.Read(ref _scanned);

        /// <summary>The caller's own test in place of the needles' (<see cref="Scan"/>), or null.</summary>
        public FloatSweeper? Custom { get; init; }

        public long Walked => _lanes.Sum(lane => lane.Walked);

        public int Workers => _lanes.Length;

        public long Listed => _regions.Length;

        /// <summary>True when the budget ended it with chunks left.</summary>
        public bool Truncated => Scanned >= budget && Volatile.Read(ref _next) + 1 < _chunks.Length;

        /// <summary>True when it was asked to stop with chunks left.</summary>
        public bool Stopped => progress is { Stopping: true } && !Truncated && Volatile.Read(ref _next) + 1 < _chunks.Length;

        /// <summary>Every chunk of every region small enough to be data, in the order given, and each region's kind.</summary>
        public void Plan(MemoryRegion[] regions)
        {
            _regions = regions;
            _kindOf = new int[regions.Length];
            _dead = new int[regions.Length];
            var kinds = new Dictionary<(uint Protect, uint Type), int>();
            var chunks = new List<Chunk>();
            for (var at = 0; at < regions.Length; at++)
            {
                MemoryRegion region = regions[at];
                if (region.Size > HeapScan.LargestRegion)
                {
                    continue;
                }

                if (!kinds.TryGetValue((region.Protect, region.Type), out int kind))
                {
                    kind = kinds.Count;
                    kinds.Add((region.Protect, region.Type), kind);
                    _kindNames.Add(region.Kind);
                }

                _kindOf[at] = kind;
                for (ulong taken = 0; taken < region.Size; taken += HeapScan.ChunkBytes)
                {
                    chunks.Add(new Chunk(at, taken));
                }
            }

            _chunks = [.. chunks];
        }

        /// <summary>Runs the workers - this thread one of them - until the chunks, the budget or the asking runs out.</summary>
        public void Go(int workers)
        {
            _lanes = new Lane[Math.Clamp(workers, 1, Math.Max(1, _chunks.Length))];
            for (var at = 0; at < _lanes.Length; at++)
            {
                _lanes[at] = new Lane(needles.Count, _kindNames.Count);
            }

            var threads = new Thread[_lanes.Length - 1];
            for (var at = 0; at < threads.Length; at++)
            {
                Lane lane = _lanes[at + 1];
                threads[at] = new Thread(() => Work(lane)) { IsBackground = true, Name = "FloatHunt" };
                threads[at].Start();
            }

            Work(_lanes[0]);
            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            _failed?.Throw();
        }

        private void Work(Lane lane)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(HeapScan.ChunkBytes + overlap);
            try
            {
                while (!cancel.IsCancellationRequested && progress is not { Stopping: true } && Volatile.Read(ref _scanned) < budget)
                {
                    int item = Interlocked.Increment(ref _next);
                    if (item >= _chunks.Length)
                    {
                        break;
                    }

                    Chunk chunk = _chunks[item];
                    if (Volatile.Read(ref _dead[chunk.Region]) != 0)
                    {
                        continue;
                    }

                    MemoryRegion region = _regions[chunk.Region];
                    int kind = _kindOf[chunk.Region];
                    if (chunk.Offset == 0)
                    {
                        lane.Walked++;
                        lane.Regions[kind]++;
                        progress?.Began();
                    }

                    // A FEW FLOATS PAST THE CHUNK, so a needle that starts in it and ends in the next
                    // is still compared whole; each start is tried in the one chunk it begins in.
                    int want = (int)Math.Min((ulong)HeapScan.ChunkBytes, region.Size - chunk.Offset);
                    int extra = (int)Math.Min((ulong)overlap, region.Size - chunk.Offset - (ulong)want);
                    long began = Stopwatch.GetTimestamp();
                    while (want >= sizeof(float) && !reader.TryRead(region.Address + chunk.Offset, buffer.AsSpan(0, want + extra)))
                    {
                        want /= 2;
                        extra = 0;
                    }

                    long read = Stopwatch.GetTimestamp();
                    lane.Reading[kind] += read - began;
                    if (want < sizeof(float))
                    {
                        // THE REST OF THE REGION IS LEFT, as on one thread: a region that will not read
                        // here was freed or changed under the walk, and its other chunks would only
                        // fail the same way - each after halving its read down to a word.
                        Volatile.Write(ref _dead[chunk.Region], 1);
                        lane.Failed[kind]++;
                        continue;
                    }

                    Interlocked.Add(ref _scanned, want);
                    progress?.Add(want);
                    if (Custom is { } custom)
                    {
                        // THE CALLER'S TEST, its places capped per kind as a needle's are.
                        lane.Raw.Clear();
                        custom(buffer.AsSpan(0, (want + extra) & ~3), want, region.Address + chunk.Offset, lane.Raw);
                        foreach (FloatSighting one in lane.Raw)
                        {
                            if ((uint)one.Needle >= (uint)needles.Count)
                            {
                                continue;
                            }

                            if (lane.Found[one.Needle] >= Most(needles[one.Needle]))
                            {
                                lane.Capped.Add(one.Needle);
                                continue;
                            }

                            lane.Found[one.Needle]++;
                            lane.Fresh.Add(one);
                        }
                    }
                    else
                    {
                        Sweep(
                            buffer.AsSpan(0, (want + extra) & ~3), want, region.Address + chunk.Offset, needles, anchors, _widest, _buckets,
                            lane.Found, lane.Capped, lane.Fresh);
                    }
                    if (lane.Fresh.Count > 0)
                    {
                        progress?.Found(lane.Fresh.Count);
                        foreach (FloatSighting one in lane.Fresh)
                        {
                            lane.Sightings.Add((item, one));
                        }

                        lane.Fresh.Clear();
                    }

                    lane.Bytes[kind] += want;
                    lane.Sweeping[kind] += Stopwatch.GetTimestamp() - read;
                }
            }
            catch (Exception failed)
            {
                // ON A THREAD OF ITS OWN an exception would take the process down; it is thrown
                // again on the caller's once every worker is done.
                Interlocked.CompareExchange(ref _failed, ExceptionDispatchInfo.Capture(failed), null);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Every worker's places in the order of the chunks they came from, each needle's cap applied to them in that order.
        /// </summary>
        /// <remarks>
        /// A WORKER'S OWN CAP LOSES NOTHING KEPT HERE: its chunks are in the hunt's order too, so a place
        /// among a needle's first few overall had fewer still before it in its own worker.
        /// </remarks>
        public (List<FloatSighting> Sightings, HashSet<int> Capped) Gather()
        {
            var all = new List<(int Item, FloatSighting Place)>();
            var capped = new HashSet<int>();
            foreach (Lane lane in _lanes)
            {
                all.AddRange(lane.Sightings);
                capped.UnionWith(lane.Capped);
            }

            all.Sort(static (a, b) => a.Item != b.Item ? a.Item.CompareTo(b.Item)
                : a.Place.At != b.Place.At ? a.Place.At.CompareTo(b.Place.At)
                : a.Place.Needle.CompareTo(b.Place.Needle));
            var found = new int[needles.Count];
            var sightings = new List<FloatSighting>(all.Count);
            foreach ((_, FloatSighting place) in all)
            {
                if (found[place.Needle] >= Most(needles[place.Needle]))
                {
                    capped.Add(place.Needle);
                    continue;
                }

                found[place.Needle]++;
                sightings.Add(place);
            }

            return (sightings, capped);
        }

        /// <summary>What each kind of memory cost, summed over the workers, the most bytes first.</summary>
        public FloatHuntKind[] Kinds()
        {
            var kinds = new FloatHuntKind[_kindNames.Count];
            for (var kind = 0; kind < kinds.Length; kind++)
            {
                long regions = 0, bytes = 0, reading = 0, sweeping = 0, failed = 0;
                foreach (Lane lane in _lanes)
                {
                    regions += lane.Regions[kind];
                    bytes += lane.Bytes[kind];
                    reading += lane.Reading[kind];
                    sweeping += lane.Sweeping[kind];
                    failed += lane.Failed[kind];
                }

                kinds[kind] = new FloatHuntKind(
                    _kindNames[kind], regions, bytes, Stopwatch.GetElapsedTime(0, reading), Stopwatch.GetElapsedTime(0, sweeping), failed);
            }

            return [.. kinds.Where(one => one.Regions > 0).OrderByDescending(one => one.Bytes)];
        }
    }

    /// <summary>One worker's own findings and tallies - touched by its thread alone until the workers are done.</summary>
    private sealed class Lane(int needles, int kinds)
    {
        public int[] Found { get; } = new int[needles];

        public HashSet<int> Capped { get; } = [];

        /// <summary>The places the chunk in hand yielded, before they are tagged with it.</summary>
        public List<FloatSighting> Fresh { get; } = [];

        /// <summary>What a caller's own test found in the chunk in hand, before each kind's cap.</summary>
        public List<FloatSighting> Raw { get; } = [];

        public List<(int Item, FloatSighting Place)> Sightings { get; } = [];

        public long Walked { get; set; }

        public long[] Regions { get; } = new long[kinds];

        public long[] Bytes { get; } = new long[kinds];

        public long[] Reading { get; } = new long[kinds];

        public long[] Sweeping { get; } = new long[kinds];

        public long[] Failed { get; } = new long[kinds];
    }
}
