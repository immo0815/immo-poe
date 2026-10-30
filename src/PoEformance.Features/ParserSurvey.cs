using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's parser survey: every file of the formats read after poe_data_tools' <c>file_parsers</c> - the ones the tool
/// had no reader for, and the mesh readers' gaps - read from the install on every core, and counted per format and version as
/// read whole or stopped, with the first stops' reasons and files.
/// </summary>
/// <remarks>
/// WHY: the readers were written from the reference's grammar, and only five of the formats had a file of the game's to test
/// against (the master's tables and ground types the captures carry). This holds every reader against every file the install
/// has - poe_data_tools' own <c>validate</c>, file by file - so a format a reader gets wrong is a count, a reason and the very
/// file, and the mend is held against those files offline (<see cref="Replay"/>, <c>parser-survey-replay.txt</c>).
///
/// WHAT "WHOLE" MEANS is the reference's: a text format's every line taken by its grammar and nothing left; a binary one walked
/// to the file's end (a <c>.smd</c> through its tail, a <c>.fmt</c> to its pool's end, a <c>.tgm</c> exactly to the file's end);
/// a <c>.tgt</c> of version 1 read whole even though it is not drawn.
///
/// INTERLEAVED, SO A STOP STILL COVERS EVERY FORMAT: the paths go in runs of <see cref="RunLength"/> neighbours (a bundle's last
/// unpacked chunks serve the files after), the runs dealt round the formats in turn. It stops itself at <see cref="BudgetSeconds"/>
/// from the press, within the capture's wait for work started at the press - CHOSEN, and the file says how far it got. A format
/// of more than <see cref="Kept.MostPerFormat"/> files is surveyed by an even stride over its sorted paths, also CHOSEN.
/// </remarks>
public static class ParserSurvey
{
    /// <summary>The formats surveyed, by extension.</summary>
    public static IReadOnlyList<string> Extensions { get; } =
    [
        AnimationData.Extension, ChestTable.Extension, CritterTable.Extension, DecalTable.Extension, DoodadTable.Extension,
        EffectPackage.Extension, EdgeTypeFile.Extension, GroundCombinations.Extension, GroundTypeFile.Extension,
        ParticleEmitters.Extension, PassiveGraph.Extension, TrailEffects.Extension,
        ".smd", ".fmt", TileMesh.Extension, TileTemplate.Extension,
    ];

    /// <summary>How long the survey runs from the press before it stops itself and writes what it read - CHOSEN under the capture's 120 s wait.</summary>
    public const int BudgetSeconds = 100;

    /// <summary>Where the stopped files are written beside the part's own file.</summary>
    public const string SampleFolder = "parser-survey";

    /// <summary>How many neighbouring paths go to a worker at once.</summary>
    private const int RunLength = 64;

    /// <summary>How many stopped files of a format are written beside the part, the first by path - CHOSEN.</summary>
    private const int SamplesPerFormat = 3;

    /// <summary>The largest file written as a sample, and all samples together - CHOSEN.</summary>
    private const long SampleBytes = 4L << 20, SamplesBytes = 40L << 20;

    /// <summary>How many stops of a format the file lists.</summary>
    private const int StopsListed = 8;

    /// <summary>What one file came to: its version as written, whether it read whole, why not, and what it carries worth counting.</summary>
    public readonly record struct Verdict(string Version, bool Whole, string Why, IReadOnlyList<string> Facts);

    /// <summary>How far a survey is, for the capture's window - and its stop.</summary>
    public sealed class Running
    {
        private long _done;
        private long _of;
        private volatile bool _stopped;

        /// <summary>Files read.</summary>
        public long Done => Interlocked.Read(ref _done);

        /// <summary>Files to read; nought until they are dealt.</summary>
        public long Of => Interlocked.Read(ref _of);

        /// <summary>Whether it was told to stop.</summary>
        public bool Stopped => _stopped;

        /// <summary>Its line in the capture's window.</summary>
        public string Line => Of > 0 ? $"{Done:N0} of {Of:N0} files read" : "dealing the formats' paths";

        /// <summary>How far it is.</summary>
        public float Fraction => Of > 0 ? (float)((double)Done / Of) : 0f;

        /// <summary>Stops it, keeping what it read.</summary>
        public void Stop() => _stopped = true;

        internal void Begin(long of) => Interlocked.Exchange(ref _of, of);

        internal void Read(int files) => Interlocked.Add(ref _done, files);
    }

    /// <summary>
    /// One file judged by its extension's reader. Never throws: a reader that throws despite its promise is a stop, by its exception.
    /// </summary>
    public static Verdict Judge(string path, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            return Judged(Path.GetExtension(path).ToLowerInvariant(), content);
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException or OverflowException
            or InvalidOperationException or FormatException or DecoderFallbackException)
        {
            return new Verdict("-", false, $"the reader threw {exception.GetType().Name}: {exception.Message}", []);
        }
    }

    private static Verdict Judged(string extension, byte[] content)
    {
        switch (extension)
        {
            case AnimationData.Extension:
            {
                AnimationData read = AnimationData.Read(content);
                return Text(read.Version, read.Why, read.BoneGroups is null ? [] : ["bone groups"]);
            }

            case ChestTable.Extension:
            {
                ChestTable read = ChestTable.Read(content);
                return Text(read.Version, read.Why, read.Groups.Count > 1 ? ["named groups"] : []);
            }

            case CritterTable.Extension:
            {
                CritterTable read = CritterTable.Read(content);
                return Text(read.Version, read.Why, read.Groups.Any(group => group.Lines.Count > 0) ? ["critter lines"] : []);
            }

            case DecalTable.Extension:
            {
                DecalTable read = DecalTable.Read(content);
                return Text(read.Version, read.Why, read.Groups.Any(group => group.Entries.Count > 0) ? ["decal lines"] : []);
            }

            case DoodadTable.Extension:
            {
                DoodadTable read = DoodadTable.Read(content);
                return Text(read.Version, read.Why, read.Groups.SelectMany(group => group.Lines).Any(line => line.All) ? ["All weights"] : []);
            }

            case EffectPackage.Extension:
            {
                EffectPackage read = EffectPackage.Read(content);
                return new Verdict("-", read.Why.Length == 0, read.Why, [.. read.Effects.OfType<PackageOther>().Select(other => "other keyword " + other.Kind).Distinct(StringComparer.Ordinal)]);
            }

            case EdgeTypeFile.Extension:
            {
                EdgeTypeFile read = EdgeTypeFile.Read(content);
                return new Verdict("-", read.Why.Length == 0, read.Why, [.. Facts(
                    (read.Numbers is not null, "a line of numbers"), (read.OtherGround is not null, "another ground"), (read.Virtual.Count > 0, "a virtual section"))]);
            }

            case GroundCombinations.Extension:
            {
                GroundCombinations read = GroundCombinations.Read(content);
                return Text(read.Version, read.Why, []);
            }

            case GroundTypeFile.Extension:
            {
                GroundTypeFile read = GroundTypeFile.Read(content);
                return new Verdict("-", read.Why.Length == 0, read.Why, [$"{read.Flags.Count} flags", .. read.Extra is null ? Array.Empty<string>() : ["a string"]]);
            }

            case ParticleEmitters.Extension:
            {
                ParticleEmitters read = ParticleEmitters.Read(content);
                return new Verdict(Said(read.Version), read.Why.Length == 0, read.Why, read.Payload is null ? [] : ["a JSON payload"]);
            }

            case PassiveGraph.Extension:
            {
                PassiveGraph read = PassiveGraph.Read(content);
                return new Verdict(read.Version.ToString(CultureInfo.InvariantCulture), read.Why.Length == 0, read.Why, [$"graph type {read.GraphType}"]);
            }

            case TrailEffects.Extension:
            {
                TrailEffects read = TrailEffects.Read(content);
                return new Verdict(Said(read.Version), read.Why.Length == 0, read.Why, read.Payload is null ? [] : ["a JSON payload"]);
            }

            case ".smd":
            {
                SkinnedMeshWhole read = SkinnedMesh.ReadWhole(content);
                return new Verdict(Said(read.Version), read.Why.Length == 0, read.Why,
                    [$"tail version {read.Tail.Version}", $"{read.Levels.Levels.Count} levels", .. Facts((read.Unread > 0, "bytes past the tail"))]);
            }

            case ".fmt":
            {
                FixedMesh read = FixedMesh.Read(content);
                string why = !read.Ready ? read.Why : read.Unread != 0 ? $"{read.Unread} bytes past the string pool" : string.Empty;
                return new Verdict(Said(content[0]), why.Length == 0, why, [.. Facts((read.Pieces.Count > 0, "pieces"), (read.Records.Count > 0, "records"))]);
            }

            case TileMesh.Extension:
            {
                TileMesh read = TileMesh.Read(content);
                string why = !read.Ready ? read.Why : !read.Exact ? "the walk does not end at the file's end" : string.Empty;
                return new Verdict(Said(content[0]), why.Length == 0, why, [.. Facts(
                    (read.Version < 9 && read.Ready, "older layout drawn"), (read.OlderTail.Count > 0 || read.TailData.Length > 0, "tail records"))]);
            }

            case TileTemplate.Extension:
            {
                TileTemplate read = TileTemplate.Read(content);
                bool whole = read.Unread.Length == 0 && (read.Ready || (read.Version < 3 && read.SingleMesh.Length > 0));
                string why = whole ? string.Empty : read.Unread.Length > 0 ? $"left \"{read.Unread}\" unread" : read.Why;
                return new Verdict(Said(read.Version), whole, why, [.. Facts(
                    (read.Version < 3, "one mesh, read not drawn"), (read.Slots.Count > 0, "material slots"), (read.Leads.Any(lead => lead.Count > 0), "triples"))]);
            }

            default:
                return new Verdict("-", false, $"no reader for {extension}", []);
        }
    }

    private static Verdict Text(int version, string why, IReadOnlyList<string> facts)
        => new(Said(version), why.Length == 0, why, facts);

    private static string Said(int? version) => version?.ToString(CultureInfo.InvariantCulture) ?? "none";

    private static IEnumerable<string> Facts(params (bool Holds, string Fact)[] facts)
        => facts.Where(one => one.Holds).Select(one => one.Fact);

    /// <summary>
    /// The survey: every kept path read and judged on every core, and the part's lines - the first saying what it holds. The
    /// first stopped files of each format are written under <see cref="SampleFolder"/> in <paramref name="folder"/>.
    /// </summary>
    /// <param name="kept">The formats' paths, as the startup walk kept them.</param>
    /// <param name="read">How to get a file out of the install, by path; safe on several threads at once.</param>
    /// <param name="folder">The folder the part's file is written in, or null to write no samples.</param>
    /// <param name="running">How far it is, and its stop.</param>
    /// <param name="budgetSeconds">When it stops itself.</param>
    public static IReadOnlyList<string> Run(Kept kept, Func<string, byte[]?> read, string? folder, Running running, int budgetSeconds = BudgetSeconds)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(running);

        if (!kept.Walked)
        {
            return ["parser survey: not run - the install's walk at startup has not finished; press again once the Tile Book lists its tiles"];
        }

        var clock = Stopwatch.StartNew();
        var byFormat = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string extension in Extensions)
        {
            byFormat[extension] = [];
        }

        foreach (string path in kept.Paths())
        {
            if (byFormat.TryGetValue(Path.GetExtension(path).ToLowerInvariant(), out List<string>? list))
            {
                list.Add(path);
            }
        }

        List<(int From, int To, List<string> Paths)> runs = Dealt(byFormat);
        running.Begin(runs.Sum(run => (long)(run.To - run.From)));

        var tallies = new ConcurrentDictionary<(string Format, string Version), Tally>();
        var facts = new ConcurrentDictionary<(string Format, string Fact), int>();
        var stops = new ConcurrentBag<(string Format, string Path, string Why)>();
        var unread = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        long bytes = 0;
        bool cut = false;
        TimeSpan budget = TimeSpan.FromSeconds(budgetSeconds);

        Parallel.ForEach(runs, run =>
        {
            int done = 0;
            long mine = 0;
            for (int at = run.From; at < run.To; at++, done++)
            {
                if (running.Stopped || clock.Elapsed > budget)
                {
                    cut |= !running.Stopped;
                    break;
                }

                string path = run.Paths[at];
                string format = Path.GetExtension(path).ToLowerInvariant();
                if (read(path) is not { Length: > 0 } content)
                {
                    unread.AddOrUpdate(format, 1, (_, count) => count + 1);
                    continue;
                }

                mine += content.Length;
                Verdict verdict = Judge(path, content);
                tallies.GetOrAdd((format, verdict.Version), _ => new Tally()).Add(verdict.Whole);
                foreach (string fact in verdict.Facts)
                {
                    facts.AddOrUpdate((format, fact), 1, (_, count) => count + 1);
                }

                if (!verdict.Whole)
                {
                    stops.Add((format, path, verdict.Why));
                }
            }

            running.Read(done);
            Interlocked.Add(ref bytes, mine);
        });

        string? samples = folder is null ? null : Path.Combine(folder, SampleFolder);
        List<string> written = samples is null ? [] : Samples(stops, read, samples);
        return Report(kept, byFormat, tallies, facts, stops, unread, written, running, clock.Elapsed.TotalSeconds, bytes, cut, budgetSeconds);
    }

    /// <summary>Every format's paths in runs of neighbours, the runs dealt round the formats in turn.</summary>
    private static List<(int From, int To, List<string> Paths)> Dealt(Dictionary<string, List<string>> byFormat)
    {
        var dealt = new List<(int, int, List<string>)>();
        var next = byFormat.Values.Select(_ => 0).ToArray();
        List<string>[] lists = [.. byFormat.Values];
        bool any = true;
        while (any)
        {
            any = false;
            for (var one = 0; one < lists.Length; one++)
            {
                if (next[one] >= lists[one].Count)
                {
                    continue;
                }

                int to = Math.Min(lists[one].Count, next[one] + RunLength);
                dealt.Add((next[one], to, lists[one]));
                next[one] = to;
                any = true;
            }
        }

        return dealt;
    }

    /// <summary>The first stopped files of each format by path, written under <paramref name="samples"/> at their install paths, within the caps.</summary>
    private static List<string> Samples(IEnumerable<(string Format, string Path, string Why)> stops, Func<string, byte[]?> read, string samples)
    {
        var written = new List<string>();
        long total = 0;
        foreach (IGrouping<string, (string Format, string Path, string Why)> format in stops.GroupBy(stop => stop.Format).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            foreach (string path in format.Select(stop => stop.Path).Order(StringComparer.Ordinal).Take(SamplesPerFormat))
            {
                if (read(path) is not { Length: > 0 } content || content.Length > SampleBytes || total + content.Length > SamplesBytes)
                {
                    continue;
                }

                string target = Path.Combine(samples, path.Replace('\\', '/').TrimStart('/'));
                if (!Path.GetFullPath(target).StartsWith(Path.GetFullPath(samples), StringComparison.Ordinal))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, content);
                total += content.Length;
                written.Add($"{path} ({content.Length:N0} bytes)");
            }
        }

        return written;
    }

    private static List<string> Report(
        Kept kept,
        Dictionary<string, List<string>> byFormat,
        ConcurrentDictionary<(string Format, string Version), Tally> tallies,
        ConcurrentDictionary<(string Format, string Fact), int> facts,
        ConcurrentBag<(string Format, string Path, string Why)> stops,
        ConcurrentDictionary<string, int> unread,
        List<string> written,
        Running running,
        double seconds,
        long bytes,
        bool cut,
        int budgetSeconds)
    {
        long whole = tallies.Values.Sum(tally => (long)tally.Whole);
        long stopped = tallies.Values.Sum(tally => (long)tally.Stopped);
        int failing = byFormat.Keys.Count(format => tallies.Any(entry => entry.Key.Format == format && entry.Value.Stopped > 0));
        string ending = running.Stopped ? ", stopped from the capture's window" : cut ? $", stopped at its budget of {budgetSeconds} s" : string.Empty;
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"parser survey: {whole + stopped:N0} of {running.Of:N0} files of {byFormat.Count} formats read in {seconds:0.#} s on {Environment.ProcessorCount} cores - {whole:N0} whole, {stopped:N0} stopped, {failing} formats with a stop{ending}"),
            string.Create(CultureInfo.InvariantCulture,
                $"{bytes / 1048576.0:N0} MB read; whole is the reference's - every line taken, a binary walked to its end; a format past {Kept.MostPerFormat:N0} files is surveyed by an even stride over its sorted paths"),
            string.Empty,
        };

        foreach ((string format, List<string> paths) in byFormat.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            KeyValuePair<(string Format, string Version), Tally>[] versions = [.. tallies.Where(entry => entry.Key.Format == format).OrderBy(entry => entry.Key.Version, StringComparer.Ordinal)];
            int formatWhole = versions.Sum(entry => entry.Value.Whole);
            int formatStopped = versions.Sum(entry => entry.Value.Stopped);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"=== {format}: {kept.InInstall(format):N0} files in the install, {paths.Count:N0} surveyed, {formatWhole + formatStopped:N0} read - {formatWhole:N0} whole, {formatStopped:N0} stopped{(unread.TryGetValue(format, out int missed) ? $", {missed:N0} not read out of the install" : string.Empty)}"));
            foreach (KeyValuePair<(string Format, string Version), Tally> version in versions)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  version {version.Key.Version}: {version.Value.Whole:N0} whole, {version.Value.Stopped:N0} stopped"));
            }

            string[] said = [.. facts.Where(entry => entry.Key.Format == format).OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key.Fact, StringComparer.Ordinal)
                .Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Key.Fact} {entry.Value:N0}"))];
            if (said.Length > 0)
            {
                lines.Add("  carries: " + string.Join(", ", said));
            }

            (string Format, string Path, string Why)[] own = [.. stops.Where(stop => stop.Format == format).OrderBy(stop => stop.Path, StringComparer.Ordinal)];
            if (own.Length > 0)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  stops by reason (the first {StopsListed} by path below):"));
                foreach (IGrouping<string, (string Format, string Path, string Why)> reason in own.GroupBy(stop => Reason(stop.Why)).OrderByDescending(group => group.Count()).Take(12))
                {
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"    {reason.Count():N0} · {reason.Key}"));
                }

                foreach ((string Format, string Path, string Why) stop in own.Take(StopsListed))
                {
                    lines.Add($"    {stop.Path} · {stop.Why}");
                }
            }

            lines.Add(string.Empty);
        }

        lines.Add(written.Count == 0
            ? "=== samples: none written"
            : $"=== samples: the first {SamplesPerFormat} stopped files of each format, under {SampleFolder}/ at their install paths (parser-survey-replay.txt reads them again)");
        lines.AddRange(written.Select(one => "  " + one));
        return lines;
    }

    /// <summary>A stop's reason with its numbers and quoted text taken out, so stops of one kind are counted together.</summary>
    private static string Reason(string why)
    {
        var kept = new StringBuilder(why.Length);
        bool quoted = false;
        foreach (char one in why)
        {
            if (one == '"')
            {
                quoted = !quoted;
                _ = kept.Append(quoted ? "\"...\"" : string.Empty);
                continue;
            }

            if (!quoted)
            {
                _ = kept.Append(char.IsAsciiDigit(one) ? '#' : one);
            }
        }

        return kept.ToString();
    }

    /// <summary>
    /// The offline reader: every sample a capture's survey wrote, judged again by today's readers, beside what the survey said of
    /// it - <c>parser-survey-replay.txt</c>, so a mend is held against the very files before the next capture.
    /// </summary>
    /// <param name="folder">The folder holding <c>parser-survey.txt</c> and its <see cref="SampleFolder"/>.</param>
    public static string Replay(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        string samples = Path.Combine(folder, SampleFolder);
        var said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string report = Path.Combine(folder, CaptureReport.ParserSurveyFile);
        if (File.Exists(report))
        {
            foreach (string line in File.ReadLines(report))
            {
                int dot = line.IndexOf(" · ", StringComparison.Ordinal);
                if (line.StartsWith("    ", StringComparison.Ordinal) && dot > 4 && !char.IsAsciiDigit(line[4]))
                {
                    said[line[4..dot]] = line[(dot + 3)..];
                }
            }
        }

        string[] files = Directory.Exists(samples) ? [.. Directory.GetFiles(samples, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)] : [];
        var lines = new List<string>();
        int now = 0;
        foreach (string file in files)
        {
            string path = Path.GetRelativePath(samples, file).Replace('\\', '/');
            Verdict verdict = Judge(path, File.ReadAllBytes(file));
            now += verdict.Whole ? 1 : 0;
            lines.Add($"{path} · version {verdict.Version} · was: {said.GetValueOrDefault(path, "(not in the survey's list)")} · now: {(verdict.Whole ? "whole" : verdict.Why)}");
        }

        return string.Join(Environment.NewLine, [$"parser survey replay: {files.Length} sample files read again - {now} whole now, {files.Length - now} still stopped", string.Empty, .. lines]);
    }

    /// <summary>
    /// The formats' paths the startup walk found, kept for the press: every one up to <see cref="MostPerFormat"/> a format and an
    /// even stride over the sorted rest past it, as one UTF-8 blob - a few hundred thousand paths nobody else reads would cost
    /// tens of megabytes as strings for the whole session.
    /// </summary>
    /// <remarks>
    /// THE INDEX DROPS ITS PATH TEXT after the walks a session announced (BundleIndex.ExpectWalks), so the survey's paths are
    /// one more place in the startup walk rather than a walk of their own at the press.
    /// </remarks>
    public sealed class Kept
    {
        /// <summary>The most paths of one format surveyed - CHOSEN: more than the budget reads of the slower meshes.</summary>
        public const int MostPerFormat = 20_000;

        private volatile Store? _store;

        /// <summary>Whether the walk has handed its paths over.</summary>
        public bool Walked => _store is not null;

        /// <summary>How many paths of a format the install holds, before the stride.</summary>
        public int InInstall(string format) => _store?.Totals.GetValueOrDefault(format) ?? 0;

        /// <summary>Takes the walk's paths: grouped by format, each sorted, the stride taken, and packed.</summary>
        public void Set(IReadOnlyList<string> walked)
        {
            ArgumentNullException.ThrowIfNull(walked);

            var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string path in walked)
            {
                string format = Path.GetExtension(path).ToLowerInvariant();
                if (!groups.TryGetValue(format, out List<string>? list))
                {
                    groups[format] = list = [];
                }

                list.Add(path);
            }

            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            var blob = new MemoryStream();
            var starts = new List<int> { 0 };
            foreach ((string format, List<string> list) in groups)
            {
                totals[format] = list.Count;
                list.Sort(StringComparer.Ordinal);
                double step = list.Count <= MostPerFormat ? 1 : (double)list.Count / MostPerFormat;
                for (double at = 0; at < list.Count; at += step)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(list[(int)at]);
                    blob.Write(bytes);
                    starts.Add((int)blob.Length);
                }
            }

            _store = new Store(blob.ToArray(), [.. starts], totals);
        }

        /// <summary>The kept paths as strings, made at the press.</summary>
        internal List<string> Paths()
        {
            Store? store = _store;
            var paths = new List<string>(store is null ? 0 : store.Starts.Length - 1);
            for (var one = 0; store is not null && one + 1 < store.Starts.Length; one++)
            {
                paths.Add(Encoding.UTF8.GetString(store.Blob, store.Starts[one], store.Starts[one + 1] - store.Starts[one]));
            }

            return paths;
        }

        private sealed record Store(byte[] Blob, int[] Starts, Dictionary<string, int> Totals);
    }

    /// <summary>One format's and version's counts, added to from every core.</summary>
    private sealed class Tally
    {
        private int _whole;
        private int _stopped;

        public int Whole => Volatile.Read(ref _whole);

        public int Stopped => Volatile.Read(ref _stopped);

        public void Add(bool whole)
        {
            if (whole)
            {
                Interlocked.Increment(ref _whole);
            }
            else
            {
                Interlocked.Increment(ref _stopped);
            }
        }
    }
}
