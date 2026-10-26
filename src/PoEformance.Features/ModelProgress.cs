using System.Diagnostics;
using System.Globalization;

namespace PoEformance.Features;

/// <summary>
/// How far a model being built has got: which step it is on and how much of that step is done - read by the pane while the build runs on the thread pool.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. A laid room takes seconds - hundreds of tile pieces, dozens of doodads, every
/// material's graphs - and all the pane said while it ran was one line that did not change. Asked for
/// from the live client: whether it is still working or has quietly failed should not be a guess.
///
/// ONLY THE OUTERMOST STEP COUNTS. A room lays tile pieces, and each piece paints its own shapes; were
/// the inner walk to report as well, the bar would run back and forth inside every piece. So a step
/// begun while another is open is inert, and the bar follows the steps a person can name: the pieces,
/// the doodads, the graphs. A build that is handed none reports nothing and pays one null check a step.
///
/// THE PANE READS WHILE THE BUILD WRITES, from another thread, and three numbers read one after another
/// may be from two moments - a bar a step behind its label for one frame. That is a picture of
/// progress, not a ledger, and no lock is worth it.
///
/// AND IT KEEPS THE TIME (asked for from the live client: a stage took thirty seconds where a monster takes
/// thirty milliseconds, and which part of it was the question). Each outermost step's wall time, and in it
/// how long went on each kind of work (<see cref="BuildWork"/>) - timed EXCLUSIVELY, a kind's time stopping
/// while a kind nested in it runs, so a texture's read is reading and not decoding, and the kinds and the
/// rest add up to the step. What no kind names is the walk itself: parsing the meshes and the .ao files,
/// placing, joining. On a build spread over several threads the kinds are summed over them, and say so.
/// </remarks>
public sealed class ModelProgress
{
    private readonly long _started = Environment.TickCount64;
    private readonly long _startedTicks = Stopwatch.GetTimestamp();
    private readonly long[] _spent = new long[Kinds];
    private readonly List<StepTime> _steps = [];
    private readonly Lock _gate = new();
    private long _stepStarted;
    private long[] _stepSpent = new long[Kinds];
    private int _depth;
    private int _done;
    private int _total;
    private string _stage = string.Empty;

    /// <summary>How many kinds of work are timed - see <see cref="BuildWork"/>.</summary>
    private const int Kinds = (int)BuildWork.Count;

    /// <summary>The innermost open timing on this thread and those under it - see <see cref="Time"/>.</summary>
    [ThreadStatic]
    private static Open[]? t_open;

    [ThreadStatic]
    private static int t_depth;

    /// <summary>The step under way, in words - empty before the first.</summary>
    public string Stage => Volatile.Read(ref _stage);

    /// <summary>How much of the step is done.</summary>
    public int Done => Volatile.Read(ref _done);

    /// <summary>How much the step has to do - nought where it did not say.</summary>
    public int Total => Volatile.Read(ref _total);

    /// <summary>How much of the step is done, nought to one.</summary>
    public float Fraction
    {
        get
        {
            int total = Total;
            return total > 0 ? Math.Clamp((float)Done / total, 0f, 1f) : 0f;
        }
    }

    /// <summary>How long the build has run, in whole seconds.</summary>
    public long Seconds => (Environment.TickCount64 - _started) / 1000;

    /// <summary>The line the pane shows: what is being built, the step and how far it is, and for how long.</summary>
    public string Said()
    {
        string stage = Stage;
        int total = Total;
        string step = stage.Length == 0
            ? string.Empty
            : total > 0 ? string.Create(CultureInfo.InvariantCulture, $" · {stage} {Math.Min(Done, total)} of {total}") : " · " + stage;
        return string.Create(CultureInfo.InvariantCulture, $"building the model{step} · {Seconds} s");
    }

    /// <summary>
    /// Begins a step of <paramref name="total"/> things - inert where <paramref name="progress"/> is null or another step is open. Dispose it when the step is done.
    /// </summary>
    public static Step Begin(ModelProgress? progress, string stage, int total)
    {
        if (progress is null)
        {
            return default;
        }

        if (Interlocked.Increment(ref progress._depth) != 1)
        {
            return new Step(progress, counts: false);
        }

        Volatile.Write(ref progress._done, 0);
        Volatile.Write(ref progress._total, Math.Max(0, total));
        Volatile.Write(ref progress._stage, stage);
        lock (progress._gate)
        {
            progress._stepStarted = Stopwatch.GetTimestamp();
            progress._stepSpent = progress.Spent();
        }

        return new Step(progress, counts: true);
    }

    /// <summary>
    /// Times one piece of work of a kind on this thread until disposed - inert where <paramref name="progress"/> is null. The kind
    /// open round it on the same thread stops while it runs, so every moment is counted once, to the innermost kind.
    /// </summary>
    public static Timed Time(ModelProgress? progress, BuildWork work)
    {
        if (progress is null)
        {
            return default;
        }

        Open[] open = t_open ??= new Open[MostNested];
        long now = Stopwatch.GetTimestamp();
        int depth = t_depth;
        if (depth > 0)
        {
            ref Open outer = ref open[Math.Min(depth, MostNested) - 1];
            outer.Progress.Add(outer.Work, now - outer.Since);
        }

        if (depth < MostNested)
        {
            open[depth] = new Open(progress, work, now);
        }

        t_depth = depth + 1;
        return new Timed(progress);
    }

    /// <summary>Every read through <paramref name="read"/> timed as <see cref="BuildWork.Reading"/> - the install's bundles and their decompression.</summary>
    public Func<string, byte[]?> Timing(Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return path =>
        {
            using Timed timed = Time(this, BuildWork.Reading);
            return read(path);
        };
    }

    /// <summary>
    /// Where the time went: the whole build, each outermost step with its kinds and the rest, and what fell outside every step - for
    /// the capture (CapturePanes). Asked once the build is done.
    /// </summary>
    public IReadOnlyList<string> Report()
    {
        long ended = Stopwatch.GetTimestamp();
        long[] spent = Spent();
        StepTime[] steps;
        lock (_gate)
        {
            steps = [.. _steps];
        }

        var lines = new List<string>(steps.Length + 3)
        {
            string.Create(CultureInfo.InvariantCulture, $"built in {InSeconds(ended - _startedTicks):0.000} s wall") + Kinded(spent, ended - _startedTicks),
        };

        long inSteps = 0;
        foreach (StepTime step in steps)
        {
            inSteps += step.Wall;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"step \"{step.Stage}\" ({step.Total} things): {InSeconds(step.Wall):0.000} s wall") + Kinded(step.Spent, step.Wall));
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"outside every step: {InSeconds(ended - _startedTicks - inSteps):0.000} s wall"));
        lines.Add("the kinds are timed exclusively, each on the thread it ran on and summed over threads; \"the rest\" is the wall time less them -"
            + " the .ao, mesh and tile walks, placing and joining - and comes out negative where several threads ran at once");
        return lines;
    }

    /// <summary>How long has gone on one kind of work so far, in seconds, summed over threads.</summary>
    public double SecondsOn(BuildWork work) => InSeconds(Interlocked.Read(ref _spent[(int)work]));

    /// <summary>How deep timings may nest on one thread before the deeper ones are charged to the one above them.</summary>
    private const int MostNested = 16;

    private static double InSeconds(long ticks) => (double)ticks / Stopwatch.Frequency;

    /// <summary>Each kind's seconds and the rest of <paramref name="wall"/>.</summary>
    private static string Kinded(long[] spent, long wall)
    {
        var said = new System.Text.StringBuilder(" -");
        long kinds = 0;
        for (var kind = 0; kind < spent.Length; kind++)
        {
            kinds += spent[kind];
            said.Append(CultureInfo.InvariantCulture, $" {Named((BuildWork)kind)} {InSeconds(spent[kind]):0.000},");
        }

        said.Append(CultureInfo.InvariantCulture, $" the rest {InSeconds(wall - kinds):0.000}");
        return said.ToString();
    }

    private static string Named(BuildWork work) => work switch
    {
        BuildWork.Reading => "reading (bundles and Oodle)",
        BuildWork.Unpacking => "texture unpacking (Brotli)",
        BuildWork.Decoding => "texture decoding",
        BuildWork.Mipmapping => "mipmaps",
        BuildWork.GraphReading => "graph and material parsing",
        BuildWork.Compiling => "graph compiling",
        _ => work.ToString(),
    };

    /// <summary>Every kind's time so far.</summary>
    private long[] Spent()
    {
        var spent = new long[Kinds];
        for (var kind = 0; kind < Kinds; kind++)
        {
            spent[kind] = Interlocked.Read(ref _spent[kind]);
        }

        return spent;
    }

    private void Add(BuildWork work, long ticks) => Interlocked.Add(ref _spent[(int)work], ticks);

    /// <summary>The outermost step ended: its wall time and its kinds' since it began.</summary>
    private void Ended()
    {
        long now = Stopwatch.GetTimestamp();
        long[] spent = Spent();
        lock (_gate)
        {
            for (var kind = 0; kind < Kinds; kind++)
            {
                spent[kind] -= _stepSpent[kind];
            }

            _steps.Add(new StepTime(Stage, Total, now - _stepStarted, spent));
        }
    }

    /// <summary>One outermost step's times.</summary>
    private readonly record struct StepTime(string Stage, int Total, long Wall, long[] Spent);

    /// <summary>One open timing on a thread.</summary>
    private struct Open(ModelProgress progress, BuildWork work, long since)
    {
        public readonly ModelProgress Progress = progress;
        public readonly BuildWork Work = work;
        public long Since = since;
    }

    /// <summary>One timing - see <see cref="Time"/>.</summary>
    public readonly struct Timed : IDisposable
    {
        private readonly ModelProgress? _progress;

        internal Timed(ModelProgress progress) => _progress = progress;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_progress is null || t_open is not { } open || t_depth == 0)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            int depth = --t_depth;
            if (depth < MostNested)
            {
                ref Open closing = ref open[depth];
                closing.Progress.Add(closing.Work, now - closing.Since);
            }

            // THE ONE AROUND IT RUNS AGAIN FROM NOW - nested past the limit, the deepest one kept.
            if (depth > 0)
            {
                open[Math.Min(depth, MostNested) - 1].Since = now;
            }
        }
    }

    /// <summary>One step of a build - see <see cref="Begin"/>.</summary>
    public readonly struct Step : IDisposable
    {
        private readonly ModelProgress? _progress;
        private readonly bool _counts;

        internal Step(ModelProgress progress, bool counts)
        {
            _progress = progress;
            _counts = counts;
        }

        /// <summary>One more thing done.</summary>
        public void Advance()
        {
            if (_counts)
            {
                Interlocked.Increment(ref _progress!._done);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_progress is null)
            {
                return;
            }

            if (_counts)
            {
                _progress.Ended();
            }

            Interlocked.Decrement(ref _progress._depth);
        }
    }
}

/// <summary>The kinds of work a build's time is told apart by - see <see cref="ModelProgress.Time"/>.</summary>
public enum BuildWork
{
    /// <summary>A file out of the install: the bundle's bytes and their Oodle decompression.</summary>
    Reading,

    /// <summary>A texture's own wrapping taken off after the read - the Brotli PoE2 packs its .dds in (GameArt.ReadRaw).</summary>
    Unpacking,

    /// <summary>A texture decoded to plain pixels (GameArt.Decode).</summary>
    Decoding,

    /// <summary>A texture's smaller levels worked out (Mipmaps.Of).</summary>
    Mipmapping,

    /// <summary>Material files and shader graphs parsed.</summary>
    GraphReading,

    /// <summary>A material's graphs compiled into a program (ShadeProgram.Compile).</summary>
    Compiling,

    /// <summary>How many kinds there are.</summary>
    Count,
}
