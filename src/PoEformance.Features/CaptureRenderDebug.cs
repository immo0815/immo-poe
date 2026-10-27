using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's render debug part: the engine's own debug views - every graph under <c>Metadata/EngineGraphs/</c>, the
/// <c>debug_*.fxgraph</c> first, whole and followed through what they name - with what the compile makes of each, the shader sources
/// of the debug pass, and the program's strings round the table that names the views.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (2026-10-27), from v0.1.314's debug-strings.txt: the program names 49 debug graphs
/// (<c>debug_albedo</c>, <c>debug_walkability</c>, <c>debug_ocean_markup_deep</c>, <c>debug_overdraw</c> and on) in one table in
/// <c>.rdata</c>, with <c>Performance Overlay</c> and <c>Trail Wireframe</c> among them, and the install holds 50. How the game turns
/// one on is not known, and is not needed: the tool compiles graphs itself (<see cref="ShadeProgram"/>), so a view is built in the
/// Monster Book from its graph. Three questions decide which can be: what each graph is (written whole), which of its nodes the
/// compile already knows (<see cref="Compiled"/>, here and in the replay, so a compile that learns a node is held against the same
/// graphs offline), and where the nodes it does not know are written (the sources naming them, whole). The program's strings between
/// the graphs' paths are printed as they stand, since a label a view is listed by sits beside its path - not read into a mapping here.
///
/// THE MARKUP VIEWS ARE THE FIRST REASON: <c>debug_ocean_markup</c>, <c>_deep</c>, <c>_land</c>, <c>debug_river_markup</c> and
/// <c>debug_flood_wave_markup</c> must read the ocean markup the water's way in was chosen without (CLAUDE.md, OceanMarkupField) -
/// whatever texture or field they bind is named in them if anywhere.
/// </remarks>
public static class CaptureRenderDebug
{
    /// <summary>The folder the engine's own graphs are in, which the install's startup walk keeps whole.</summary>
    public const string Folder = "Metadata/EngineGraphs/";

    /// <summary>The prefix of the debug views' graphs, as the program spells it.</summary>
    public const string DebugPrefix = "Metadata/EngineGraphs/debug_";

    /// <summary>Bytes of the program printed before its first debug graph's path and after its last.</summary>
    private const int Margin = 0x1000;

    /// <summary>The widest window printed - the program's table is some 7 KB.</summary>
    private const int WidestWindow = 128 * 1024;

    /// <summary>The shortest run printed from the window.</summary>
    private const int ShortestRun = 3;

    /// <summary>Most graphs written whole.</summary>
    private const int MostGraphs = 256;

    /// <summary>Most textures read for their header.</summary>
    private const int MostTextures = 128;

    /// <summary>Most shader sources naming an unknown node written whole.</summary>
    private const int MostNaming = 12;

    /// <summary>The largest source written whole; a larger one is named with its size.</summary>
    private const int LargestSource = 512 * 1024;

    private static readonly byte[] DebugBytes = Encoding.ASCII.GetBytes(DebugPrefix);

    private static readonly byte[] FolderBytes = Encoding.ASCII.GetBytes(Folder);

    /// <summary>The part's text: a line saying what it found, then the program's window, the graphs, the compile's reading of them, the textures and the sources. Never throws on a missing file.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="programs">The game's program files, read from disk.</param>
    /// <param name="graphs">Every path under <see cref="Folder"/>, as the install's walk found them.</param>
    /// <param name="shaders">Every shader source in the install, as its walk found them.</param>
    public static string Write(Func<string, byte[]?> read, IReadOnlyList<string>? programs, IReadOnlyList<string>? graphs, IReadOnlyList<string>? shaders)
    {
        ArgumentNullException.ThrowIfNull(read);
        var clock = Stopwatch.StartNew();
        var body = new StringBuilder();

        var named = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int window = Programs(programs, named, body);

        string[] walked = [.. (graphs ?? []).Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the install's engine graphs ({Folder}, the walk's): {walked.Length}, those the program names marked *").AppendLine();
        if (walked.Length == 0)
        {
            body.AppendLine("(none: the install walk found none, or it had not run when the key went down)");
        }

        foreach (string path in walked)
        {
            body.Append(named.Contains(path) ? "  * " : "    ").AppendLine(path);
        }

        foreach (string path in named.Where(path => !walked.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            body.Append("  ! ").Append(path).AppendLine("  (the program names it, the walk did not find it)");
        }

        // THE DEBUG VIEWS FIRST, the program's before the walk's, then the rest of the folder, then whatever they name.
        IEnumerable<(string Path, string Why)> seeds = named.Where(IsDebug).Select(path => (path, "a debug view the program names"))
            .Concat(walked.Where(IsDebug).Select(path => (path, "a debug view in the install")))
            .Concat(named.Where(path => !IsDebug(path)).Select(path => (path, "an engine graph the program names")))
            .Concat(walked.Where(path => !IsDebug(path) && path.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase)).Select(path => (path, "an engine graph in the install")));
        (Dictionary<string, string> whole, SortedDictionary<string, string> pictures, SortedDictionary<string, string> others, int left) = Graphs(read, seeds, body);

        string compiled = Compiled(whole, body);
        Pictures(read, pictures, others, body);
        int sources = Sources(read, shaders, whole, body);
        clock.Stop();

        int views = whole.Keys.Count(IsDebug);
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"render debug: {views} debug views and {whole.Count - views} other graphs written whole{(left > 0 ? $" ({left} more named past the {MostGraphs})" : string.Empty)}, {compiled}; {named.Count} engine graphs the program names, {window} strings round its table; {pictures.Count} textures named; {sources} shader sources written ({clock.Elapsed.TotalSeconds:0.0} s)");
        return head + Environment.NewLine + body;
    }

    /// <summary>Whether a path is one of the debug views' graphs.</summary>
    public static bool IsDebug(string path) => path.StartsWith(DebugPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every engine graph each program names into <paramref name="named"/>, and the strings round its table of debug views with their
    /// addresses - the count of strings printed.
    /// </summary>
    private static int Programs(IReadOnlyList<string>? programs, SortedSet<string> named, StringBuilder body)
    {
        body.AppendLine().AppendLine("=== the game's program: every string round its table of debug views, at its address (a star after an executable section)")
            .AppendLine("    (the labels a view is listed by sit between the paths - printed as they stand)");
        if (programs is not { Count: > 0 })
        {
            body.AppendLine("(no program: the install's folder was not found, or holds none of the game's programs)");
            return 0;
        }

        var printed = 0;
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

            CaptureDebugStrings.Section[] sections = CaptureDebugStrings.Sections(bytes, out ulong imageBase);
            long first = long.MaxValue, last = -1;
            foreach (int at in Occurrences(bytes, FolderBytes))
            {
                int end = at;
                while (end < bytes.Length && bytes[end] is >= 0x21 and <= 0x7E)
                {
                    end++;
                }

                named.Add(Encoding.ASCII.GetString(bytes, at, end - at));
                if (bytes.AsSpan(at).StartsWith(DebugBytes))
                {
                    first = Math.Min(first, at);
                    last = Math.Max(last, end);
                }
            }

            if (last < 0)
            {
                body.Append(CultureInfo.InvariantCulture, $"- {program}: {bytes.Length:N0} bytes, names no debug view").AppendLine();
                continue;
            }

            long from = Math.Max(0, first - Margin);
            long to = Math.Min(Math.Min(bytes.Length, last + Margin), from + WidestWindow);
            body.Append(CultureInfo.InvariantCulture, $"- {program}: {bytes.Length:N0} bytes, image base 0x{imageBase:X}, the debug views' paths from {CaptureDebugStrings.Located(first, sections)} to {CaptureDebugStrings.Located(last, sections)}; printed from {CaptureDebugStrings.Located(from, sections)}, {to - from:N0} bytes").AppendLine();
            for (long at = from; at < to; at++)
            {
                if (bytes[at] is < 0x20 or > 0x7E)
                {
                    continue;
                }

                long end = at;
                while (end < to && bytes[end] is >= 0x20 and <= 0x7E)
                {
                    end++;
                }

                if (end - at >= ShortestRun)
                {
                    body.Append("  ").Append(CaptureDebugStrings.Located(at, sections)).Append("  ").AppendLine(Encoding.ASCII.GetString(bytes, (int)at, (int)(end - at)));
                    printed++;
                }

                at = end;
            }
        }

        return printed;
    }

    /// <summary>Every place the bytes hold the needle.</summary>
    private static List<int> Occurrences(byte[] bytes, byte[] needle)
    {
        var found = new List<int>();
        for (var from = 0; from < bytes.Length;)
        {
            int hit = bytes.AsSpan(from).IndexOf(needle);
            if (hit < 0)
            {
                break;
            }

            found.Add(from + hit);
            from += hit + needle.Length;
        }

        return found;
    }

    /// <summary>
    /// The graphs whole, each under a <c>##### path  (why)</c> heading with its lines marked <c>  | </c> - the form the offline readers
    /// read files back by - followed through every graph they name; the textures and other files they name gathered.
    /// </summary>
    private static (Dictionary<string, string> Whole, SortedDictionary<string, string> Pictures, SortedDictionary<string, string> Others, int Left) Graphs(
        Func<string, byte[]?> read, IEnumerable<(string Path, string Why)> seeds, StringBuilder body)
    {
        body.AppendLine().AppendLine("=== the graphs whole - the debug views first, then the folder's others, then every graph they name");
        var queue = new Queue<(string Path, string Why)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Offer(string path, string why)
        {
            string clean = Clean(path);
            if (clean.Length > 0 && seen.Add(clean))
            {
                queue.Enqueue((clean, why));
            }
        }

        foreach ((string path, string why) in seeds)
        {
            Offer(path, why);
        }

        var whole = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pictures = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var others = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0 && whole.Count < MostGraphs)
        {
            (string path, string why) = queue.Dequeue();
            body.AppendLine().Append("##### ").Append(path).Append("  (").Append(why).AppendLine(")");
            if (read(path) is not { Length: > 0 } bytes)
            {
                body.AppendLine("  | (not in the install)");
                continue;
            }

            string text = StatDescriptionFiles.Decode(bytes).TrimEnd();
            whole[path] = text;
            Marked(text, body);
            foreach (string file in CaptureWaves.Files(text))
            {
                if (file.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase))
                {
                    Offer(file, "named by " + path);
                }
                else if (file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    pictures.TryAdd(file, path);
                }
                else
                {
                    others.TryAdd(file, path);
                }
            }
        }

        if (queue.Count > 0)
        {
            body.AppendLine().Append(CultureInfo.InvariantCulture, $"({queue.Count} more graphs named, past the {MostGraphs} written: ")
                .Append(string.Join(", ", queue.Select(one => one.Path))).AppendLine(")");
        }

        return (whole, pictures, others, queue.Count);
    }

    /// <summary>
    /// What the compile makes of each graph: its nodes and links, each node type with how often it stands, its stage and whether the
    /// compile knows it - then every node type it does not know with the graphs using it, the debug views' first. Returns a line saying
    /// how many views the compile knows whole. The replay calls it on the graphs read back.
    /// </summary>
    /// <param name="graphs">Each graph's text, by its path.</param>
    /// <param name="said">Where it is written.</param>
    public static string Compiled(IReadOnlyDictionary<string, string> graphs, StringBuilder said)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        ArgumentNullException.ThrowIfNull(said);
        said.AppendLine().AppendLine("=== what the compile makes of them: each graph's nodes by type, and whether ShadeProgram evaluates each or carries it as an output");
        var unknown = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        int views = 0, viewsKnown = 0, unread = 0;
        foreach ((string path, string text) in graphs.OrderBy(one => IsDebug(one.Key) ? 0 : 1).ThenBy(one => one.Key, StringComparer.OrdinalIgnoreCase))
        {
            ShaderGraph graph = ShaderGraph.Read(Encoding.UTF8.GetBytes(text));
            bool debug = IsDebug(path);
            views += debug ? 1 : 0;
            if (!graph.Ready)
            {
                unread++;
                said.Append("- ").Append(path).AppendLine(": does not read as a graph");
                continue;
            }

            IGrouping<string, ShaderNode>[] types = [.. graph.Nodes.GroupBy(node => node.Type, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)];
            string[] missing = [.. types.Select(type => type.Key).Where(type => !Handled(type))];
            viewsKnown += debug && missing.Length == 0 ? 1 : 0;
            said.Append(CultureInfo.InvariantCulture, $"- {path}: {graph.Nodes.Count} nodes, {graph.Links.Count} links, {(missing.Length == 0 ? "every node known" : $"{missing.Length} of {types.Length} node types unknown")}").AppendLine();
            foreach (IGrouping<string, ShaderNode> type in types)
            {
                string stages = string.Join(",", type.Select(node => node.Stage).Where(stage => stage.Length > 0).Distinct(StringComparer.Ordinal));
                said.Append(CultureInfo.InvariantCulture, $"    {type.Key} x{type.Count()}{(stages.Length > 0 ? $" ({stages})" : string.Empty)}: {(ShadeProgram.Knows(type.Key) ? "known" : ShadeProgram.Writes(type.Key) ? "known, an output" : "NOT KNOWN")}").AppendLine();
            }

            foreach (string type in missing)
            {
                if (!unknown.TryGetValue(type, out List<string>? users))
                {
                    unknown[type] = users = [];
                }

                users.Add(path);
            }
        }

        said.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the node types the compile does not know: {unknown.Count}, those the debug views use first").AppendLine();
        foreach ((string type, List<string> users) in unknown.OrderByDescending(one => one.Value.Count(IsDebug)).ThenBy(one => one.Key, StringComparer.Ordinal))
        {
            said.Append(CultureInfo.InvariantCulture, $"  {type}: {users.Count(IsDebug)} debug views, {users.Count} graphs - ").AppendLine(string.Join(", ", users.Select(Short)));
        }

        return string.Create(CultureInfo.InvariantCulture, $"{viewsKnown} of {views} debug views known to the compile node for node, {unknown.Count} node types unknown{(unread > 0 ? $", {unread} not graphs" : string.Empty)}");
    }

    /// <summary>The textures the graphs name with their headers, and the other files with whether the install holds them.</summary>
    private static void Pictures(Func<string, byte[]?> read, SortedDictionary<string, string> pictures, SortedDictionary<string, string> others, StringBuilder body)
    {
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the textures the graphs name: {pictures.Count} - each one's header, and who names it").AppendLine();
        foreach ((string path, string by) in pictures.Take(MostTextures))
        {
            body.Append("  ").Append(path).Append("  (").Append(by).Append("): ").AppendLine(ModelDump.Header(GameArt.ReadRaw(read, path)));
        }

        if (pictures.Count > MostTextures)
        {
            body.Append(CultureInfo.InvariantCulture, $"  ... and {pictures.Count - MostTextures} more").AppendLine();
        }

        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the other files the graphs name: {others.Count} - whether the install holds each").AppendLine();
        foreach ((string path, string by) in others)
        {
            body.Append("  ").Append(path).Append("  (").Append(by).Append("): ")
                .AppendLine(read(path) is { Length: > 0 } held ? string.Create(CultureInfo.InvariantCulture, $"{held.Length:N0} bytes") : "not in the install");
        }
    }

    /// <summary>
    /// The debug pass's sources whole - every source whose path names a debug - then those naming a node type the compile does not
    /// know, the most such types first; every source read on every core. Returns how many were written whole.
    /// </summary>
    private static int Sources(Func<string, byte[]?> read, IReadOnlyList<string>? shaders, Dictionary<string, string> graphs, StringBuilder body)
    {
        string[] types = [.. graphs.Values
            .SelectMany(text => ShaderGraph.Read(Encoding.UTF8.GetBytes(text)).Nodes)
            .Select(node => node.Type)
            .Distinct(StringComparer.Ordinal)
            .Where(type => !Handled(type))];
        body.AppendLine().Append("=== the debug pass's sources (every source whose path names a debug), then those naming a node type the compile does not know: ")
            .AppendLine(types.Length == 0 ? "none" : string.Join(", ", types));
        if (shaders is not { Count: > 0 })
        {
            body.AppendLine("(no shader sources: the install walk found none, or it had not run when the key went down)");
            return 0;
        }

        var found = new ConcurrentBag<(string Path, string Text, string[] Names)>();
        Parallel.ForEach(shaders.Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase), path =>
        {
            bool debug = path.Contains("debug", StringComparison.OrdinalIgnoreCase);
            if (read(path) is not { Length: > 0 } bytes)
            {
                return;
            }

            string text = StatDescriptionFiles.Decode(bytes);
            string[] names = [.. types.Where(type => text.Contains(type, StringComparison.Ordinal))];
            if (debug || names.Length > 0)
            {
                found.Add((path, text, debug ? ["(a debug source)", .. names] : names));
            }
        });

        (string Path, string Text, string[] Names)[] debugs = [.. found.Where(one => one.Names.Length > 0 && one.Names[0] == "(a debug source)").OrderBy(one => one.Path, StringComparer.OrdinalIgnoreCase)];
        (string Path, string Text, string[] Names)[] naming = [.. found.Except(debugs).OrderByDescending(one => one.Names.Length).ThenBy(one => one.Path, StringComparer.OrdinalIgnoreCase)];
        foreach ((string path, _, string[] names) in debugs.Concat(naming))
        {
            body.Append("  ").Append(path).Append("  (").Append(string.Join(", ", names)).AppendLine(")");
        }

        var written = 0;
        foreach ((string path, string text, string[] names) in debugs.Concat(naming.Take(MostNaming)))
        {
            body.AppendLine().Append("##### ").Append(path).Append("  (").Append(string.Join(", ", names)).AppendLine(")");
            if (text.Length > LargestSource)
            {
                body.Append(CultureInfo.InvariantCulture, $"  | ({text.Length:N0} characters, past the {LargestSource / 1024} KB written whole)").AppendLine();
                continue;
            }

            Marked(text.TrimEnd(), body);
            written++;
        }

        if (naming.Length > MostNaming)
        {
            body.AppendLine().Append(CultureInfo.InvariantCulture, $"({naming.Length - MostNaming} more naming an unknown node not written whole - named above)").AppendLine();
        }

        return written;
    }

    /// <summary>A file's text with every line marked <c>  | </c>, so no line of it is taken for a heading.</summary>
    private static void Marked(string text, StringBuilder body)
    {
        foreach (string line in text.Split('\n'))
        {
            body.Append("  | ").AppendLine(line.TrimEnd('\r'));
        }
    }

    /// <summary>Whether the compile handles a node type: evaluates it, or carries it through as an output.</summary>
    private static bool Handled(string type) => ShadeProgram.Knows(type) || ShadeProgram.Writes(type);

    /// <summary>A graph's name without its folder, for lists.</summary>
    private static string Short(string path) => path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) ? path[Folder.Length..] : path;

    private static string Clean(string path) => path.Replace('\\', '/').Trim();
}
