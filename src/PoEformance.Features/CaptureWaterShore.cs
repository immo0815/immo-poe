using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's water shore part: what the install says of what the engine's shore field is made from - every file naming the
/// water's blocking or flow, the ocean's markup or deep water, the area's object types that reach one, the area's ground types whole,
/// the data tables naming one, the shader sources naming the field and the program's strings naming a shore.
/// </summary>
/// <remarks>
/// WRITTEN FOR PORT'S SHIP (asked for from the live client, v0.1.293, with an in-game picture): the game's swell runs between the ship
/// and the quay, while the tool's runs in under one side of the hull and out of the other, its shore field made from the ground and
/// the spraying rocks alone (<see cref="ShoreField"/>). The engine makes its field (<c>shoreline_coord_tex_sampler</c>) at run time
/// from something no file read so far names - but the game's program holds two script commands, <c>SetWaterBlocking</c> and
/// <c>SetWaterFlow</c>, beside <c>RiverMarkupField</c> and <c>MarkupFlowConstraints</c> (v0.1.294).
/// AND THE OCEAN'S MARKUP (v0.1.295, from v0.1.292's capture and four in-game videos): the program names <c>OceanMarkupField</c>, made
/// either from a grid of ints or from three point lines, beside <c>ShoreDeep</c>, <c>ShoreShallow</c> and <c>IgnoreDeepWaterMarkup</c>;
/// Port loads a ground type of deep water, <c>island_ocean_deepwater.gt</c>, which none of its tile corners holds, among water ground
/// types of other areas. So the part searches every kind of terrain file too - tiles, rooms, masters, ground and edge types - and the
/// data tables, on the bytes: ASCII and UTF-16 alike, binary files included, any case.
/// </remarks>
public static class CaptureWaterShore
{
    /// <summary>
    /// The words the sea's spray is found by (v0.1.300, asked for from the live client: the spray thrown up Port's quay): water.ffx's
    /// fragments CrashingSprayTrigger, BreakingSprayTrigger and FloodSprayTrigger tell a particle effect when to fire, and no file read
    /// so far names which effect - nor the environments' <c>water_splash_ao</c> what it draws. Declared before <see cref="Words"/>,
    /// which a static initializer reads in order.
    /// </summary>
    public static IReadOnlyList<string> SprayWords { get; } = ["SprayTrigger", "CrashingSpray", "OceanSpray", "water_splash"];

    /// <summary>The words a file is found by, any case, ASCII or UTF-16.</summary>
    public static IReadOnlyList<string> Words { get; } =
    [
        "SetWaterBlocking", "SetWaterFlow", "WaterBlocking", "water_block", "water_flow",
        "ShoreDeep", "ShoreShallow", "IgnoreDeepWaterMarkup", "OceanMarkup", "ocean_markup", "deepwater",
        .. SprayWords,
    ];

    /// <summary>The kinds of file a spray's effect is made of, searched beside the rest: graphs, particles, effect packages and trails.</summary>
    public static IReadOnlyList<string> EffectExtensions { get; } = [".fxgraph", ".pet", ".epk", ".trl"];

    /// <summary>The folder the install's walk keeps the files from.</summary>
    public const string Folder = "Metadata/";

    /// <summary>
    /// The kinds of file the walk keeps under <see cref="Folder"/> - object scripts, ground and edge types, environments, the
    /// masters' lists and the effects' files. Tiles, rooms and masters (<c>.tdt</c>, <c>.arm</c>, <c>.tsi</c>) come from the Tile Book's own walk.
    /// </summary>
    public static IReadOnlyList<string> Extensions { get; } =
        [".ot", ".otc", ".gt", ".et", ".env", ".gft", ".rs", ".mtd", ".tst", ".ecf", ".tmo", ".ddt", ".dgr", ".tgt", ".toy", .. EffectExtensions];

    /// <summary>The folder the data tables are walked from.</summary>
    public const string TableFolder = "Data/";

    /// <summary>The data tables' extension.</summary>
    public const string TableExtension = ".datc64";

    /// <summary>The words a shader source is found by - the field and whatever might make it.</summary>
    public static IReadOnlyList<string> ShaderWords { get; } =
        ["shoreline", "WaterBlocking", "water_blocking", "RiverMarkup", "MarkupFlow", "OceanMarkup", "ocean_markup", "JumpFlood", "jump_flood"];

    /// <summary>The words the program's strings are found by.</summary>
    public static IReadOnlyList<string> ProgramWords { get; } = ["shore", "waterblock", "water_block", "setwater", "markup", "deepwater", "deep_water", "spray", "splash"];

    /// <summary>Most found text files written whole.</summary>
    private const int MostWhole = 60;

    /// <summary>Most effect files whose names the second pass looks for.</summary>
    private const int MostEffects = 64;

    /// <summary>Largest a found text file is written whole.</summary>
    private const int LargestWhole = 96 * 1024;

    /// <summary>Most lines kept a file.</summary>
    private const int MostLines = 40;

    /// <summary>Most characters either side of a hit a binary file's string is kept to.</summary>
    private const int Reach = 240;

    /// <summary>Most sources written whole.</summary>
    private const int MostShaders = 6;

    /// <summary>Most strings written a program.</summary>
    private const int MostStrings = 800;

    /// <summary>Paths a worker reads in a row - side by side in their bundle, so its last chunks serve the next file.</summary>
    private const int Run = 256;

    /// <summary>The heading the found files' lines are listed under, which <see cref="Calls"/> reads back.</summary>
    public const string CallsHeading = "=== the lines naming them, file by file";

    private static readonly byte[][] ProgramBytes = [.. ProgramWords.Select(word => Encoding.ASCII.GetBytes(word.ToLowerInvariant()))];

    /// <summary>Each word lower case, as ASCII bytes and as UTF-16 bytes.</summary>
    private static readonly (byte[] Ascii, byte[] Wide)[] Needles =
        [.. Words.Select(word => (Encoding.ASCII.GetBytes(word.ToLowerInvariant()), Encoding.Unicode.GetBytes(word.ToLowerInvariant())))];

    /// <summary>One line of a file naming one of the words.</summary>
    /// <param name="Path">The file.</param>
    /// <param name="Line">The line, trimmed - or, in a binary file, the string round the word.</param>
    public readonly record struct Call(string Path, string Line);

    /// <summary>How far a search is, for the capture's window - and its stop.</summary>
    public sealed class Search
    {
        private long _done;
        private long _of;
        private int _found;
        private volatile bool _stopped;

        /// <summary>Files searched.</summary>
        public long Done => Interlocked.Read(ref _done);

        /// <summary>Files to search.</summary>
        public long Of => Interlocked.Read(ref _of);

        /// <summary>Files naming a word.</summary>
        public int Found => Volatile.Read(ref _found);

        /// <summary>Whether it was told to stop.</summary>
        public bool Stopped => _stopped;

        /// <summary>Stops it, keeping what it found.</summary>
        public void Stop() => _stopped = true;

        internal void Begin(long of) => Interlocked.Exchange(ref _of, of);

        internal void Searched(int files) => Interlocked.Add(ref _done, files);

        internal void Hit() => Interlocked.Increment(ref _found);
    }

    /// <summary>What one file said: its lines naming a word, and its text where it is text.</summary>
    private sealed record Hit(List<string> Lines, string? Text);

    /// <summary>The part's text: a line saying what it holds, then the files, the area's types and grounds, the tables, the sources and the strings. Never throws on a missing file.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="files">Every file to search, as the install's walks found them.</param>
    /// <param name="tables">Every data table, as the install's walk found them.</param>
    /// <param name="loaded">The files the area loaded - its object and ground types among them.</param>
    /// <param name="shaders">Every shader source in the install, as its walk found them.</param>
    /// <param name="programs">The game's program files, read from disk.</param>
    /// <param name="search">How far it is, and its stop - for the capture's window.</param>
    public static string Write(
        Func<string, byte[]?> read, IReadOnlyList<string>? files, IReadOnlyList<string>? tables, IReadOnlyList<string>? loaded,
        IReadOnlyList<string>? shaders, IReadOnlyList<string>? programs, Search? search = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        search ??= new Search();
        var body = new StringBuilder();

        string[] paths = [.. (files ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        string[] datas = [.. (tables ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        search.Begin(paths.Length + datas.Length);
        var clock = Stopwatch.StartNew();
        (ConcurrentDictionary<string, Hit> found, long bytes, int unread) = Searched(read, paths, search);
        TimeSpan filesTook = clock.Elapsed;
        (ConcurrentDictionary<string, Hit> inTables, long tableBytes, int tablesUnread) = Searched(read, datas, search);
        clock.Stop();

        string[] named = [.. found.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        string stopped = search.Stopped ? $" - STOPPED after {search.Done} of {search.Of}" : string.Empty;
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the install's files ({string.Join(", ", Extensions)} under {Folder}, and the tiles, rooms and masters) naming {string.Join(", ", Words)}, any case, ASCII or UTF-16: {named.Length} of {paths.Length} searched ({bytes:N0} bytes, {unread} not read) in {filesTook.TotalSeconds:0.0} s{stopped}").AppendLine();
        if (paths.Length == 0)
        {
            body.AppendLine("(no files: the install walk found none, or it had not run when the key went down)");
        }

        foreach (IGrouping<string, string> kind in named.GroupBy(path => Path.GetExtension(path).ToLowerInvariant()).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            body.Append(CultureInfo.InvariantCulture, $"  {kind.Key}: {kind.Count()}").AppendLine();
        }

        body.AppendLine().AppendLine(CallsHeading);
        foreach (string path in named)
        {
            foreach (string line in found[path].Lines)
            {
                body.Append("  ").Append(path).Append("  |  ").AppendLine(line);
            }
        }

        // THE AREA'S OWN: every loaded object type followed through what it extends, and the found files it reaches.
        string[] types = [.. (loaded ?? []).Select(TypeOf).Where(type => type.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        var reaching = new List<(string Type, string[] Reached)>();
        foreach (string type in types)
        {
            var reached = new List<string>();
            foreach (string kind in (string[])[".ot", ".otc"])
            {
                Reaches(read, type + kind, kind, found, new HashSet<string>(StringComparer.OrdinalIgnoreCase), reached, depth: 0);
            }

            if (reached.Count > 0)
            {
                reaching.Add((type, [.. reached]));
            }
        }

        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the area's loaded object types reaching a found file through what they extend: {reaching.Count} of {types.Length}").AppendLine();
        foreach ((string type, string[] reached) in reaching)
        {
            body.Append("  ").Append(type).Append("  ->  ").AppendLine(string.Join(", ", reached));
        }

        int grounds = Grounds(read, loaded, body);

        string[] tabled = [.. inTables.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the data tables ({TableExtension} under {TableFolder}) naming one, its strings round the word: {tabled.Length} of {datas.Length} searched ({tableBytes:N0} bytes, {tablesUnread} not read)").AppendLine();
        foreach (string path in tabled)
        {
            foreach (string line in inTables[path].Lines)
            {
                body.Append("  ").Append(path).Append("  |  ").AppendLine(line);
            }
        }

        // THE SPRAY'S EFFECTS, ONE STEP ON: the effect files naming a spray word, whole, and every searched file naming one of them by
        // its file's name - what fires them, and where.
        string[] effects = [.. named.Where(path => EffectExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            && found[path].Lines.Any(line => SprayWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase))))];
        string[] effectNames = [.. effects.Select(Path.GetFileName).OfType<string>().Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MostEffects)];
        ConcurrentDictionary<string, Hit> loading = new(StringComparer.OrdinalIgnoreCase);
        if (effectNames.Length > 0 && !search.Stopped)
        {
            search.Begin(search.Of + paths.Length);
            (byte[] Ascii, byte[] Wide)[] effectNeedles =
                [.. effectNames.Select(name => (Encoding.ASCII.GetBytes(name.ToLowerInvariant()), Encoding.Unicode.GetBytes(name.ToLowerInvariant())))];
            loading = Searched(read, paths, search, effectNeedles, effectNames).Found;
        }

        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the spray's effect files ({string.Join(", ", EffectExtensions)}) naming {string.Join(", ", SprayWords)}, whole: {effects.Length}").AppendLine();
        foreach (string path in effects.Take(MostWhole))
        {
            if (found[path].Text is { Length: <= LargestWhole } text)
            {
                Whole(path, text, body);
            }
            else
            {
                body.AppendLine().Append("##### ").Append(path).AppendLine("  (binary or too long - its lines above)");
            }
        }

        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the searched files naming one of those by its file's name ({string.Join(", ", effectNames)}): {loading.Count}").AppendLine();
        foreach (string path in loading.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string line in loading[path].Lines)
            {
                body.Append("  ").Append(path).Append("  ->  ").AppendLine(line);
            }
        }

        string[] whole = [.. named.Where(path => found[path].Text is { Length: <= LargestWhole }).Take(MostWhole)];
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the found text files whole, the first {MostWhole} of at most {LargestWhole / 1024} KB: {whole.Length}").AppendLine();
        foreach (string path in whole)
        {
            Whole(path, found[path].Text!, body);
        }

        int sources = CaptureSources.Naming(read, shaders, ShaderWords, MostShaders, body);
        string strings = Programs(programs, body);
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"water shore: {named.Length} of {paths.Length} files and {tabled.Length} of {datas.Length} data tables name the water's blocking, flow, markup, deep water or spray ({clock.Elapsed.TotalSeconds:0.0} s{stopped}), {effects.Length} effect files the spray and {loading.Count} files name those, {reaching.Count} of the area's {types.Length} loaded object types reach one, {grounds} loaded ground types; {(sources < 0 ? "no shader sources" : $"{sources} shader sources name the shore field's words")}; {strings}");
        return head + Environment.NewLine + body;
    }

    /// <summary>The lines a part's text lists under <see cref="CallsHeading"/> - the offline reader's way in.</summary>
    public static IReadOnlyList<Call> Calls(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var calls = new List<Call>();
        bool inside = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("===", StringComparison.Ordinal))
            {
                inside = line == CallsHeading || line == "=== the lines naming them, script by script";
                continue;
            }

            int bar = line.IndexOf("  |  ", StringComparison.Ordinal);
            if (inside && bar > 2)
            {
                calls.Add(new Call(line[2..bar], line[(bar + 5)..]));
            }
        }

        return calls;
    }

    /// <summary>What the calls say, worked out: each word with the lines naming it and the files they are in.</summary>
    public static string Report(IReadOnlyList<Call> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture, $"water shore replay: {calls.Count} lines in {calls.Select(call => call.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()} files").AppendLine();
        foreach (string word in Words)
        {
            Call[] naming = [.. calls.Where(call => call.Line.Contains(word, StringComparison.OrdinalIgnoreCase))];
            said.AppendLine().Append(CultureInfo.InvariantCulture, $"=== {word}: {naming.Length} lines").AppendLine();
            foreach (IGrouping<string, Call> line in naming.GroupBy(call => call.Line, StringComparer.Ordinal).OrderByDescending(group => group.Count()))
            {
                said.Append(CultureInfo.InvariantCulture, $"  {line.Count(),4}x  {line.Key}  ({string.Join(", ", line.Select(call => call.Path).Take(4))}{(line.Count() > 4 ? ", ..." : string.Empty)})").AppendLine();
            }
        }

        return said.ToString();
    }

    /// <summary>
    /// Every path searched on every core - runs of neighbours to one worker, so a bundle's last unpacked chunks serve the files after -
    /// on the bytes lowered: each word as ASCII and as UTF-16, whatever the file.
    /// </summary>
    private static (ConcurrentDictionary<string, Hit> Found, long Bytes, int Unread) Searched(Func<string, byte[]?> read, string[] paths, Search search)
        => Searched(read, paths, search, Needles, Words);

    /// <summary>The same, for other words.</summary>
    private static (ConcurrentDictionary<string, Hit> Found, long Bytes, int Unread) Searched(
        Func<string, byte[]?> read, string[] paths, Search search, (byte[] Ascii, byte[] Wide)[] needles, IReadOnlyList<string> words)
    {
        var found = new ConcurrentDictionary<string, Hit>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        int unread = 0;
        Parallel.ForEach(Partitioner.Create(0, Math.Max(1, paths.Length), Run), range =>
        {
            long mine = 0;
            int missed = 0, done = 0;
            for (int at = range.Item1; at < Math.Min(range.Item2, paths.Length) && !search.Stopped; at++, done++)
            {
                if (read(paths[at]) is not { Length: > 0 } content)
                {
                    missed++;
                    continue;
                }

                mine += content.Length;
                if (Lines(content, needles, words) is { } hit)
                {
                    found[paths[at]] = hit;
                    search.Hit();
                }
            }

            search.Searched(done);
            Interlocked.Add(ref bytes, mine);
            Interlocked.Add(ref unread, missed);
        });

        return (found, bytes, unread);
    }

    /// <summary>A file's lines naming a word, or null where it names none - text files by their lines, binary ones by the string round the word.</summary>
    private static Hit? Lines(byte[] content, (byte[] Ascii, byte[] Wide)[] needles, IReadOnlyList<string> words)
    {
        byte[] lowered = ArrayPool<byte>.Shared.Rent(content.Length);
        try
        {
            Span<byte> low = lowered.AsSpan(0, content.Length);
            for (var at = 0; at < content.Length; at++)
            {
                byte one = content[at];
                low[at] = one is >= (byte)'A' and <= (byte)'Z' ? (byte)(one + 32) : one;
            }

            bool any = false;
            foreach ((byte[] ascii, byte[] wide) in needles)
            {
                if (low.IndexOf(ascii) >= 0 || low.IndexOf(wide) >= 0)
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return null;
            }

            // TEXT where it says so - a mark, or no nought in its first bytes - and its lines; else the strings round each word.
            bool text = (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
                || (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
                || content.AsSpan(0, Math.Min(content.Length, 4096)).IndexOf((byte)0) < 0;
            if (text)
            {
                string decoded = StatDescriptionFiles.Decode(content);
                List<string> lines = [.. decoded.Split('\n').Select(line => line.Trim()).Where(line => words.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase))).Take(MostLines)];
                return new Hit(lines, decoded);
            }

            var strings = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach ((byte[] ascii, byte[] wide) in needles)
            {
                Gather(content, low, ascii, wide: false, strings, seen);
                Gather(content, low, wide, wide: true, strings, seen);
            }

            return new Hit(strings, null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lowered);
        }
    }

    /// <summary>The printable run round every place a word stands in a binary file - a string of the file's own.</summary>
    private static void Gather(byte[] content, ReadOnlySpan<byte> low, byte[] needle, bool wide, List<string> strings, HashSet<string> seen)
    {
        int step = wide ? 2 : 1;
        for (int from = 0; from < low.Length && strings.Count < MostLines;)
        {
            int at = low[from..].IndexOf(needle);
            if (at < 0)
            {
                return;
            }

            at += from;
            bool Printable(int index) => index >= 0 && index + step - 1 < content.Length
                && content[index] is >= 0x20 and < 0x7F && (!wide || content[index + 1] == 0);
            int start = at, end = at + needle.Length;
            while (Printable(start - step) && (at - start) / step < Reach)
            {
                start -= step;
            }

            while (Printable(end) && (end - at) / step < Reach)
            {
                end += step;
            }

            string found = wide ? Encoding.Unicode.GetString(content, start, end - start) : Encoding.ASCII.GetString(content, start, end - start);
            string said = string.Create(CultureInfo.InvariantCulture, $"@0x{start:X} {(wide ? "utf-16" : "ascii")}: {found}");
            if (seen.Add(found))
            {
                strings.Add(said);
            }

            from = end;
        }
    }

    /// <summary>Every ground type the area loaded, whole - the water's among them, deep and shallow. Returns how many.</summary>
    private static int Grounds(Func<string, byte[]?> read, IReadOnlyList<string>? loaded, StringBuilder body)
    {
        string[] grounds = [.. (loaded ?? []).Select(path => path.Replace('\\', '/').Trim()).Where(path => path.EndsWith(".gt", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the ground types the area loaded, whole - a name, then the switches poe_data_tools reads as five unnamed bools: {grounds.Length}").AppendLine();
        foreach (string path in grounds)
        {
            if (read(path) is { Length: > 0 } bytes)
            {
                Whole(path, StatDescriptionFiles.Decode(bytes), body);
            }
            else
            {
                body.Append("##### ").Append(path).AppendLine("  (not in the install)");
            }
        }

        return grounds.Length;
    }

    /// <summary>Writes a file's text whole under its path.</summary>
    private static void Whole(string path, string text, StringBuilder body)
    {
        body.AppendLine().Append("##### ").AppendLine(path);
        foreach (string line in text.Split('\n'))
        {
            body.Append("  | ").AppendLine(line.TrimEnd('\r'));
        }
    }

    /// <summary>A loaded path as an object type - without its script's extension - or empty where it is none.</summary>
    private static string TypeOf(string path)
    {
        string clean = path.Replace('\\', '/').Trim();
        if (!clean.StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        foreach (string kind in (string[])[".ot", ".otc"])
        {
            if (clean.EndsWith(kind, StringComparison.OrdinalIgnoreCase))
            {
                return clean[..^kind.Length];
            }
        }

        // A TYPE NAMED BARE, as an entity's path is.
        return clean.Contains('.', StringComparison.Ordinal) ? string.Empty : clean;
    }

    /// <summary>Follows a script through what it extends, collecting the found ones on the way.</summary>
    private static void Reaches(
        Func<string, byte[]?> read, string path, string kind, ConcurrentDictionary<string, Hit> found, HashSet<string> seen, List<string> reached, int depth)
    {
        if (depth > 12 || !seen.Add(path))
        {
            return;
        }

        string text;
        if (found.TryGetValue(path, out Hit? hit) && hit.Text is { } known)
        {
            reached.Add(path);
            text = known;
        }
        else if (read(path) is { Length: > 0 } bytes)
        {
            text = StatDescriptionFiles.Decode(bytes);
        }
        else
        {
            return;
        }

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("extends", StringComparison.Ordinal))
            {
                continue;
            }

            int open = trimmed.IndexOf('"', StringComparison.Ordinal);
            int close = open < 0 ? -1 : trimmed.IndexOf('"', open + 1);
            string parent = close > open ? trimmed[(open + 1)..close] : string.Empty;
            if (parent.Length > 0 && !parent.Equals("nothing", StringComparison.OrdinalIgnoreCase))
            {
                Reaches(read, parent + kind, kind, found, seen, reached, depth + 1);
            }
        }
    }

    /// <summary>The strings each program holds naming a shore, the water's blocking, a markup or deep water.</summary>
    private static string Programs(IReadOnlyList<string>? programs, StringBuilder body)
    {
        body.AppendLine().Append("=== the game's program: its strings naming ").AppendLine(string.Join(", ", ProgramWords));
        if (programs is not { Count: > 0 })
        {
            body.AppendLine("(no program: the install's folder was not found, or holds none of the game's programs)");
            return "no program to search";
        }

        var total = 0;
        foreach (string program in programs)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(program);
            }
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                body.Append("- ").Append(program).Append(": not read, ").AppendLine(failed.Message);
                continue;
            }

            SortedDictionary<string, int> strings = CaptureWaterTextures.Strings(bytes, ProgramBytes);
            total += strings.Count;
            body.Append(CultureInfo.InvariantCulture, $"- {program}: {bytes.Length:N0} bytes, {strings.Count} strings").AppendLine();
            foreach ((string text, int times) in strings.Take(MostStrings))
            {
                body.Append("  ").Append(text);
                if (times > 1)
                {
                    body.Append(CultureInfo.InvariantCulture, $"  (x{times})");
                }

                body.AppendLine();
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{total} strings in the program name one");
    }
}
