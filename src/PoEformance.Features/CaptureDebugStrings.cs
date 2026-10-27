using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PoEformance.Features;

/// <summary>
/// The capture's debug strings part: every string the game's program, its data tables and its install's paths hold that names a debug,
/// cheat, developer or test function, a console or visualisation switch, or an assert or a log - grouped, each with where it stands.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (2026-10-27): GGG seems to have left many debug variables and functions in the game, and a list of
/// them, sorted by relevance, was wanted. The program is not in the repository and only a capture reaches it, so it is a part
/// (CLAUDE.md, "a question the capture cannot answer is a new part"). The words are the four groups chosen there - debug or dbg in the
/// name; cheat, dev and test; the console and visualisation; asserts and logging - and a string is listed under the first group one of
/// its words belongs to. The relevance is not decided here: the offline reader (<see cref="Report"/>) sorts what was found by its
/// shape - a C++ type name, an identifier, a chat command, a path, a message - and that sorting is read, not guessed.
///
/// SEARCHED AS THE CARD SEARCHES, NOT RUN BY RUN: the program holds millions of printable runs and a few thousand hold a word, so the
/// words are found first over the whole buffer (<see cref="SearchValues{T}"/>, vectorised, any case) and only a hit is widened to the
/// run round it. UTF-16 is the buffer itself read as characters, at either alignment; ASCII is widened a chunk at a time. Each of the
/// three runs on its own core, and the data tables on every core.
/// </remarks>
public static class CaptureDebugStrings
{
    /// <summary>One group of words, as the list is sorted by.</summary>
    /// <param name="Name">Its heading.</param>
    /// <param name="Words">Its words, lower case, any of which puts a string in it.</param>
    public sealed record WordGroup(string Name, IReadOnlyList<string> Words);

    /// <summary>
    /// The groups, in the order a string is given to the first that holds one of its words. Declared before <see cref="Words"/>, which a
    /// static initializer reads in order.
    /// </summary>
    public static IReadOnlyList<WordGroup> Groups { get; } =
    [
        new("debug", ["debug", "dbg"]),
        new("cheat, dev, test",
        [
            "cheat", "devmode", "dev_mode", "developer", "dev_", "_dev", "test_", "_test", "testing", "unittest",
            "internalonly", "internal_only", "engineonly", "engine_only", "godmode", "god_mode", "noclip", "no_clip",
        ]),
        new("console, visualisation",
        [
            "console", "show_", "draw_", "toggle", "visualis", "visualiz", "wireframe", "profiler", "profiling", "overlay", "gizmo",
            "freecam", "free_cam", "flycam", "fly_cam", "stats_", "_stats", "fps",
        ]),
        new("asserts, logging",
        [
            "assert", "log_", "_log", "logging", "trace", "dump", "verbose", "crash", "callstack", "stacktrace", "breakpoint",
        ]),
    ];

    /// <summary>Every word of every group, in the groups' order.</summary>
    public static IReadOnlyList<string> Words { get; } = [.. Groups.SelectMany(group => group.Words)];

    /// <summary>
    /// The words the install's startup walk keeps a path by, anywhere in the install - fewer than <see cref="Words"/>, because a path is
    /// tested in the walk over all four million: the plain <c>test</c> beside the debug words (a folder such as <c>__TestTextures</c>
    /// has no underscore after it), and the console and visualisation words a file would carry.
    /// </summary>
    public static IReadOnlyList<string> PathWords { get; } =
        ["debug", "dbg", "cheat", "test", "developer", "internalonly", "engineonly", "wireframe", "visualis", "visualiz", "profiler", "gizmo", "console"];

    /// <summary>The heading kinds, as <see cref="Read"/> parses them back.</summary>
    public const string ProgramKind = "program strings";

    /// <inheritdoc cref="ProgramKind"/>
    public const string TableKind = "data table strings";

    /// <inheritdoc cref="ProgramKind"/>
    public const string PathKind = "install paths";

    /// <summary>The shortest run kept - three letters alone (dbg, fps) say nothing.</summary>
    private const int Shortest = 4;

    /// <summary>The longest run kept - a longer one is no name.</summary>
    private const int Longest = 1024;

    /// <summary>How many bytes are widened to characters at a time for the ASCII search.</summary>
    private const int Chunk = 1 << 20;

    /// <summary>Most strings written a group of a kind - far past what a program holds, a guard against a pathological file.</summary>
    private const int MostListed = 20000;

    /// <summary>Data tables a worker reads in a row - side by side in their bundle, so its last chunks serve the next.</summary>
    private const int Run = 64;

    /// <summary>Characters a chunk reads past its own end, so a word across its edge is found by it.</summary>
    private static readonly int Overlap = Words.Max(word => word.Length) - 1;

    private static readonly SearchValues<string> Finder = SearchValues.Create([.. Words], StringComparison.OrdinalIgnoreCase);

    /// <summary>One string found, as the part writes it and the offline reader reads it back.</summary>
    /// <param name="Kind">Where it was found: <see cref="ProgramKind"/>, <see cref="TableKind"/> or <see cref="PathKind"/>.</param>
    /// <param name="Group">Its group's name.</param>
    /// <param name="Text">The string.</param>
    /// <param name="Wide">Whether it is UTF-16.</param>
    /// <param name="Where">The program's section and relative address of its first standing (a star after an executable section), the first table, or nothing for a path.</param>
    /// <param name="Times">How often it stands.</param>
    /// <param name="Places">In how many files.</param>
    /// <param name="Holds">The words it holds.</param>
    public sealed record Entry(string Kind, string Group, string Text, bool Wide, string Where, int Times, int Places, IReadOnlyList<string> Holds);

    /// <summary>How often a string stands, where first, and in how many files - while the search runs.</summary>
    internal sealed class Tally
    {
        public int Times;
        public long First = long.MaxValue;
        public string Where = string.Empty;
        public int Places;
    }

    /// <summary>One section of the program, to say where a string stands.</summary>
    private readonly record struct Section(string Name, long Raw, long Size, long Virtual, bool Code);

    /// <summary>The part's text: a line saying what it found, the words, then the program's, the tables' and the paths' strings group by group. Never throws on a missing file.</summary>
    /// <param name="read">How to get a file out of the install, by path - null where there is no install to read, and the tables are left out.</param>
    /// <param name="programs">The game's program files, read from disk.</param>
    /// <param name="tables">Every data table, as the install's walk found them.</param>
    /// <param name="paths">Every install path holding one of <see cref="PathWords"/>, as the install's walk found them.</param>
    /// <param name="search">How far it is, and its stop - for the capture's window.</param>
    public static string Write(
        Func<string, byte[]?>? read, IReadOnlyList<string>? programs, IReadOnlyList<string>? tables, IReadOnlyList<string>? paths,
        CaptureWaterShore.Search? search = null)
    {
        search ??= new CaptureWaterShore.Search();
        var body = new StringBuilder();
        var clock = Stopwatch.StartNew();
        string[] datas = read is null ? [] : [.. (tables ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        search.Begin((programs?.Count ?? 0) + datas.Length);

        body.AppendLine().AppendLine("=== the words, any case, ASCII or UTF-16 - a string goes under the first group holding one of its words");
        foreach (WordGroup group in Groups)
        {
            body.Append("- ").Append(group.Name).Append(": ").AppendLine(string.Join(", ", group.Words));
        }

        body.Append("- install paths are kept by: ").AppendLine(string.Join(", ", PathWords));

        int inProgram = Programs(programs, search, body);
        (int inTables, int tablesNaming) = Tables(read, datas, search, body);

        string[] kept = [.. (paths ?? []).Distinct(StringComparer.OrdinalIgnoreCase)];
        Listed(PathKind, kept.Select(path => (path, "P", "-", 1, 1)), body, "(no paths: the install walk found none, or it had not run when the key went down)");
        clock.Stop();

        string stopped = search.Stopped ? $" - STOPPED after {search.Done} of {search.Of} files" : string.Empty;
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"debug strings: {inProgram} strings in the program, {inTables} in {tablesNaming} of {datas.Length} data tables and {kept.Length} install paths hold one of {Words.Count} debug, cheat, test, console or logging words ({clock.Elapsed.TotalSeconds:0.0} s{stopped})");
        return head + Environment.NewLine + body;
    }

    /// <summary>Every program's strings, its three views on three cores - the count of strings found.</summary>
    private static int Programs(IReadOnlyList<string>? programs, CaptureWaterShore.Search search, StringBuilder body)
    {
        if (programs is not { Count: > 0 })
        {
            body.AppendLine().Append("=== ").Append(ProgramKind).AppendLine(", group -: 0");
            body.AppendLine("(no program: the install's folder was not found, or holds none of the game's programs)");
            return 0;
        }

        var rows = new List<(string Text, string Encoding, string Where, int Times, int Places)>();
        foreach (string program in programs)
        {
            if (search.Stopped)
            {
                break;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(program);
            }
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                body.AppendLine().Append("=== the program ").Append(program).Append(": not read, ").AppendLine(failed.Message);
                search.Searched(1);
                continue;
            }

            var clock = Stopwatch.StartNew();
            Section[] sections = Sections(bytes, out ulong imageBase);
            var narrow = new Dictionary<(string, bool), Tally>();
            var even = new Dictionary<(string, bool), Tally>();
            var odd = new Dictionary<(string, bool), Tally>();
            Parallel.Invoke(
                () => Narrow(bytes, narrow, program),
                () => Wide(bytes, 0, even, program),
                () => Wide(bytes, 1, odd, program));
            Merge(even, narrow);
            Merge(odd, narrow);
            clock.Stop();
            search.Searched(1);
            search.Hit();

            body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the program {program}: {bytes.Length:N0} bytes, image base 0x{imageBase:X}, {narrow.Count} strings in {clock.Elapsed.TotalMilliseconds:0} ms").AppendLine();
            foreach (Section section in sections)
            {
                body.Append(CultureInfo.InvariantCulture, $"  {section.Name}{(section.Code ? "*" : string.Empty)}  file 0x{section.Raw:X} +0x{section.Size:X}  at 0x{section.Virtual:X}").AppendLine();
            }

            rows.AddRange(narrow.Select(one => (one.Key.Item1, one.Key.Item2 ? "W" : "A", Located(one.Value.First, sections), one.Value.Times, 1)));
        }

        Listed(ProgramKind, rows, body, "(no strings)");
        return rows.Count;
    }

    /// <summary>Every data table's strings, on every core - the count of strings found and of tables holding one.</summary>
    private static (int Strings, int Tables) Tables(Func<string, byte[]?>? read, string[] tables, CaptureWaterShore.Search search, StringBuilder body)
    {
        if (read is null || tables.Length == 0)
        {
            body.AppendLine().Append("=== ").Append(TableKind).AppendLine(", group -: 0");
            body.AppendLine(read is null ? "(no install to read the tables from)" : "(no data tables: the install walk found none, or it had not run when the key went down)");
            return (0, 0);
        }

        var all = new Dictionary<(string, bool), Tally>();
        int naming = 0;
        var gate = new Lock();
        Parallel.ForEach(Partitioner.Create(0, tables.Length, Run), range =>
        {
            var mine = new Dictionary<(string, bool), Tally>();
            int done = 0, named = 0;
            for (int at = range.Item1; at < range.Item2 && !search.Stopped; at++, done++)
            {
                if (read(tables[at]) is not { Length: > 0 } bytes)
                {
                    continue;
                }

                var one = new Dictionary<(string, bool), Tally>();
                Narrow(bytes, one, tables[at]);
                Wide(bytes, 0, one, tables[at]);
                Wide(bytes, 1, one, tables[at]);
                if (one.Count > 0)
                {
                    named++;
                    search.Hit();
                    Merge(one, mine);
                }
            }

            search.Searched(done);
            lock (gate)
            {
                Merge(mine, all);
                naming += named;
            }
        });

        Listed(
            TableKind,
            all.Select(one => (one.Key.Item1, one.Key.Item2 ? "W" : "A", one.Value.Where, one.Value.Times, one.Value.Places)),
            body,
            "(no strings)");
        return (all.Count, naming);
    }

    /// <summary>Strings of one kind, group by group, each line tab-separated: the text, A, W or P, where, how often, in how many files, the words.</summary>
    private static void Listed(string kind, IEnumerable<(string Text, string Encoding, string Where, int Times, int Places)> rows, StringBuilder body, string none)
    {
        var grouped = rows
            .Select(row => (Row: row, Holds: HeldWords(row.Text)))
            .Where(one => one.Holds.Count > 0)
            .GroupBy(one => GroupOf(one.Holds))
            .ToDictionary(group => group.Key, group => group.OrderBy(one => one.Row.Text, StringComparer.OrdinalIgnoreCase).ThenBy(one => one.Row.Encoding, StringComparer.Ordinal).ToList());
        if (grouped.Count == 0)
        {
            body.AppendLine().Append("=== ").Append(kind).AppendLine(", group -: 0").AppendLine(none);
            return;
        }

        foreach (WordGroup group in Groups)
        {
            if (!grouped.TryGetValue(group.Name, out var listed))
            {
                continue;
            }

            body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== {kind}, group {group.Name}: {listed.Count}").AppendLine();
            foreach (var (row, holds) in listed.Take(MostListed))
            {
                body.Append("  ").Append(row.Text).Append('\t').Append(row.Encoding).Append('\t').Append(row.Where)
                    .Append(CultureInfo.InvariantCulture, $"\t{row.Times}\t{row.Places}\t").AppendLine(string.Join(",", holds));
            }

            if (listed.Count > MostListed)
            {
                body.Append(CultureInfo.InvariantCulture, $"  ... and {listed.Count - MostListed} more").AppendLine();
            }
        }
    }

    /// <summary>The words a string holds, in <see cref="Words"/>' order.</summary>
    private static List<string> HeldWords(string text) => [.. Words.Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The first group holding one of the words.</summary>
    private static string GroupOf(IReadOnlyList<string> holds) =>
        Groups.First(group => group.Words.Any(word => holds.Contains(word, StringComparer.Ordinal))).Name;

    /// <summary>Every run of printable ASCII in the bytes holding a word, the bytes widened a chunk at a time.</summary>
    internal static void Narrow(ReadOnlySpan<byte> bytes, Dictionary<(string, bool), Tally> into, string where)
    {
        char[] chars = ArrayPool<char>.Shared.Rent(Chunk + Overlap);
        try
        {
            long resume = 0;
            for (var start = 0; start < bytes.Length; start += Chunk)
            {
                int count = Math.Min(Chunk + Overlap, bytes.Length - start);
                int owned = Math.Min(Chunk, count);
                Span<char> view = chars.AsSpan(0, count);
                Encoding.Latin1.GetChars(bytes.Slice(start, count), view);

                // A WORD STARTING IN THIS CHUNK IS ITS OWN, one starting in the overlap the next chunk's; a run already kept is skipped.
                var from = (int)Math.Max(0, resume - start);
                while (from < owned)
                {
                    int hit = view[from..].IndexOfAny(Finder);
                    if (hit < 0 || from + hit >= owned)
                    {
                        break;
                    }

                    int at = start + from + hit;
                    int left = at;
                    while (left > 0 && Printable(bytes[left - 1]))
                    {
                        left--;
                    }

                    int right = at;
                    while (right < bytes.Length && Printable(bytes[right]))
                    {
                        right++;
                    }

                    if (right == at)
                    {
                        from += hit + 1;
                        continue;
                    }

                    Keep(Encoding.ASCII.GetString(bytes[left..right]), false, left, where, into);
                    resume = right;
                    from = right - start;
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    /// <summary>
    /// Every run of UTF-16 holding a word, the bytes read as characters from <paramref name="offset"/> - each character printable ASCII,
    /// its high byte nought.
    /// </summary>
    internal static void Wide(ReadOnlySpan<byte> bytes, int offset, Dictionary<(string, bool), Tally> into, string where)
    {
        if (bytes.Length - offset < 2)
        {
            return;
        }

        ReadOnlySpan<char> chars = MemoryMarshal.Cast<byte, char>(bytes.Slice(offset, (bytes.Length - offset) & ~1));
        var from = 0;
        while (from < chars.Length)
        {
            int hit = chars[from..].IndexOfAny(Finder);
            if (hit < 0)
            {
                break;
            }

            hit += from;
            int left = hit;
            while (left > 0 && Printable(chars[left - 1]))
            {
                left--;
            }

            int right = hit;
            while (right < chars.Length && Printable(chars[right]))
            {
                right++;
            }

            // NOT EVERY HIT IS ASCII: any case matches the Kelvin sign for a k - the run round it then holds no word, and is dropped.
            if (right == hit)
            {
                from = hit + 1;
                continue;
            }

            Keep(new string(chars[left..right]), true, offset + (2L * left), where, into);
            from = right;
        }
    }

    private static bool Printable(byte one) => one is >= 0x20 and <= 0x7E;

    private static bool Printable(char one) => one is >= (char)0x20 and <= (char)0x7E;

    /// <summary>A run kept where it is long enough, not too long, and holds a word as ASCII.</summary>
    private static void Keep(string text, bool wide, long offset, string where, Dictionary<(string, bool), Tally> into)
    {
        if (text.Length is < Shortest or > Longest || text.AsSpan().IndexOfAny(Finder) < 0)
        {
            return;
        }

        ref Tally? tally = ref CollectionsMarshal.GetValueRefOrAddDefault(into, (text, wide), out _);
        tally ??= new Tally { Where = where, Places = 1 };
        tally.Times++;
        tally.First = Math.Min(tally.First, offset);
    }

    /// <summary>One file's or one worker's strings added to another's: times summed, places counted, the first place by name kept.</summary>
    private static void Merge(Dictionary<(string, bool), Tally> from, Dictionary<(string, bool), Tally> into)
    {
        foreach (((string, bool) key, Tally one) in from)
        {
            ref Tally? tally = ref CollectionsMarshal.GetValueRefOrAddDefault(into, key, out bool had);
            if (!had)
            {
                tally = one;
                continue;
            }

            tally!.Times += one.Times;
            if (!string.Equals(tally.Where, one.Where, StringComparison.Ordinal))
            {
                tally.Places += one.Places;
                if (string.CompareOrdinal(one.Where, tally.Where) < 0)
                {
                    tally.Where = one.Where;
                }
            }

            tally.First = Math.Min(tally.First, one.First);
        }
    }

    /// <summary>The program's sections from its PE header, and its image base - none where it is not one.</summary>
    private static Section[] Sections(ReadOnlySpan<byte> bytes, out ulong imageBase)
    {
        imageBase = 0;
        if (bytes.Length < 0x40 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
        {
            return [];
        }

        int pe = BinaryPrimitives.ReadInt32LittleEndian(bytes[0x3C..]);
        if (pe < 0 || pe + 24 > bytes.Length || !bytes.Slice(pe, 4).SequenceEqual("PE\0\0"u8))
        {
            return [];
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(pe + 6)..]);
        int optional = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(pe + 20)..]);
        int header = pe + 24;
        if (header + 32 <= bytes.Length && BinaryPrimitives.ReadUInt16LittleEndian(bytes[header..]) == 0x20B)
        {
            imageBase = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(header + 24)..]);
        }

        var sections = new List<Section>(count);
        for (var one = 0; one < count; one++)
        {
            int entry = header + optional + (40 * one);
            if (entry + 40 > bytes.Length)
            {
                break;
            }

            string name = Encoding.ASCII.GetString(bytes.Slice(entry, 8)).TrimEnd('\0');
            uint virtualAt = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entry + 12)..]);
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entry + 16)..]);
            uint rawAt = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entry + 20)..]);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entry + 36)..]);
            sections.Add(new Section(name, rawAt, rawSize, virtualAt, (flags & 0x20000000) != 0));
        }

        return [.. sections];
    }

    /// <summary>Where a file offset stands: its section (a star after an executable one) and relative address, or the offset itself.</summary>
    private static string Located(long offset, Section[] sections)
    {
        foreach (Section section in sections)
        {
            if (offset >= section.Raw && offset < section.Raw + section.Size)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{section.Name}{(section.Code ? "*" : string.Empty)}:0x{section.Virtual + offset - section.Raw:X}");
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"file:0x{offset:X}");
    }

    /// <summary>The part's strings read back from its text - the offline reader's way in.</summary>
    public static IReadOnlyList<Entry> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new List<Entry>();
        string? kind = null, group = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                int comma = line.IndexOf(", group ", StringComparison.Ordinal);
                int colon = line.LastIndexOf(':');
                (kind, group) = comma > 4 && colon > comma ? (line[4..comma], line[(comma + 8)..colon]) : (null, null);
                continue;
            }

            if (kind is null || group is null || !line.StartsWith("  ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] columns = line[2..].Split('\t');
            if (columns.Length != 6
                || !int.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int times)
                || !int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int places))
            {
                continue;
            }

            entries.Add(new Entry(kind, group, columns[0], columns[1] == "W", columns[2], times, places, columns[5].Split(',', StringSplitOptions.RemoveEmptyEntries)));
        }

        return entries;
    }

    /// <summary>
    /// The strings read back, sorted by their shape for reading by relevance: per kind and group how many, the words' counts, then each
    /// group's C++ type names (demangled), identifiers, chat commands, paths and messages - the program's executable sections apart, as
    /// what they hold is code whose bytes happen to spell a word.
    /// </summary>
    public static string Report(IReadOnlyList<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var said = new StringBuilder();
        int code = entries.Count(entry => entry.Where.Contains("*:", StringComparison.Ordinal));
        said.Append(CultureInfo.InvariantCulture, $"debug strings replay: {entries.Count} strings, {code} of them in the program's executable sections").AppendLine();

        said.AppendLine().AppendLine("=== how many, by kind and group");
        foreach (IGrouping<string, Entry> kind in entries.GroupBy(entry => entry.Kind))
        {
            said.Append("- ").Append(kind.Key).Append(": ").AppendLine(string.Join(", ", kind.GroupBy(entry => entry.Group).Select(group => $"{group.Key} {group.Count()}")));
        }

        said.AppendLine().AppendLine("=== how many hold each word, by kind");
        foreach (IGrouping<string, Entry> kind in entries.GroupBy(entry => entry.Kind))
        {
            said.Append("- ").Append(kind.Key).Append(": ").AppendLine(string.Join(", ",
                kind.SelectMany(entry => entry.Holds).GroupBy(word => word, StringComparer.Ordinal).OrderByDescending(word => word.Count()).Select(word => $"{word.Key} {word.Count()}")));
        }

        foreach (IGrouping<string, Entry> kind in entries.Where(entry => !entry.Where.Contains("*:", StringComparison.Ordinal)).GroupBy(entry => entry.Kind))
        {
            foreach (IGrouping<string, Entry> group in kind.GroupBy(entry => entry.Group))
            {
                foreach (IGrouping<string, Entry> shape in group.GroupBy(entry => ShapeOf(entry.Text)).OrderBy(shape => Array.IndexOf(Shapes, shape.Key)))
                {
                    string[] texts = [.. shape.Select(entry => shape.Key == TypeShape ? Demangled(entry.Text) : entry.Text).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase)];
                    said.AppendLine().Append(CultureInfo.InvariantCulture, $"=== {kind.Key}, {group.Key}, {shape.Key}: {texts.Length}").AppendLine();
                    foreach (string text in texts)
                    {
                        said.Append("  ").AppendLine(text);
                    }
                }
            }
        }

        return said.ToString();
    }

    private const string TypeShape = "C++ type names";

    /// <summary>The shapes a string is sorted by, most telling first.</summary>
    private static readonly string[] Shapes = [TypeShape, "identifiers", "chat commands", "paths and files", "messages and the rest"];

    /// <summary>A string's shape: an MSVC type descriptor, one word of code, a chat command, a path, or anything else.</summary>
    private static string ShapeOf(string text)
    {
        if (text.StartsWith(".?A", StringComparison.Ordinal))
        {
            return TypeShape;
        }

        if (text.Length > 1 && text[0] == '/' && text.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz_0123456789") < 0)
        {
            return Shapes[2];
        }

        if (text.Contains('/', StringComparison.Ordinal) || text.Contains('\\', StringComparison.Ordinal))
        {
            return Shapes[3];
        }

        return text.AsSpan().IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_0123456789:.") < 0 ? Shapes[1] : Shapes[4];
    }

    /// <summary>
    /// An MSVC type descriptor's name as C++ writes it - <c>.?AVDebugRenderer@Render@@</c> as <c>Render::DebugRenderer</c>; a template
    /// or anything else it cannot read whole is left as it is.
    /// </summary>
    internal static string Demangled(string text)
    {
        int start = text.StartsWith(".?AW4", StringComparison.Ordinal) ? 5 : text.StartsWith(".?AV", StringComparison.Ordinal) || text.StartsWith(".?AU", StringComparison.Ordinal) ? 4 : -1;
        int end = text.IndexOf("@@", StringComparison.Ordinal);
        if (start < 0 || end <= start || text.Contains("?$", StringComparison.Ordinal))
        {
            return text;
        }

        string[] names = text[start..end].Split('@');
        Array.Reverse(names);
        return string.Join("::", names);
    }
}
