using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's water shore part: what the install says of what the engine's shore field is made from - every object script
/// naming the water's blocking or flow, the area's object types that reach one, the shader sources naming the field and the program's
/// strings naming a shore.
/// </summary>
/// <remarks>
/// WRITTEN FOR PORT'S SHIP (asked for from the live client, v0.1.293, with an in-game picture): the game's swell runs between the ship
/// and the quay, while the tool's runs in under one side of the hull and out of the other, its shore field made from the ground and
/// the spraying rocks alone (<see cref="ShoreField"/>). The engine makes its field (<c>shoreline_coord_tex_sampler</c>) at run time
/// from something no file read so far names - but the game's program holds two script commands, <c>SetWaterBlocking</c> and
/// <c>SetWaterFlow</c>, beside <c>RiverMarkupField</c> and <c>MarkupFlowConstraints</c>: an object's script may block the water.
/// Which object types call them, and with what, is what this part reads: every <c>.ot</c> and <c>.otc</c> under Metadata/, searched.
/// </remarks>
public static class CaptureWaterShore
{
    /// <summary>The words an object script is found by, any case.</summary>
    public static IReadOnlyList<string> ScriptWords { get; } = ["SetWaterBlocking", "SetWaterFlow", "WaterBlocking", "water_block", "water_flow"];

    /// <summary>The folder the install's walk keeps the object scripts from.</summary>
    public const string Folder = "Metadata/";

    /// <summary>The object scripts' extensions.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".ot", ".otc"];

    /// <summary>The words a shader source is found by - the field and whatever might make it.</summary>
    public static IReadOnlyList<string> ShaderWords { get; } =
        ["shoreline", "WaterBlocking", "water_blocking", "RiverMarkup", "MarkupFlow", "JumpFlood", "jump_flood"];

    /// <summary>The words the program's strings are found by.</summary>
    public static IReadOnlyList<string> ProgramWords { get; } = ["shore", "waterblock", "water_block", "setwater", "markup"];

    /// <summary>Most found scripts written whole.</summary>
    private const int MostWhole = 60;

    /// <summary>Most sources written whole.</summary>
    private const int MostShaders = 6;

    /// <summary>Most strings written a program.</summary>
    private const int MostStrings = 600;

    /// <summary>Paths a worker reads in a row - side by side in their bundle, so its last chunks serve the next file.</summary>
    private const int Run = 256;

    /// <summary>The heading the found scripts' lines are listed under, which <see cref="Calls"/> reads back.</summary>
    public const string CallsHeading = "=== the lines naming them, script by script";

    private static readonly byte[][] ProgramBytes = [.. ProgramWords.Select(word => Encoding.ASCII.GetBytes(word.ToLowerInvariant()))];

    /// <summary>One line of a script naming one of the words.</summary>
    /// <param name="Path">The script.</param>
    /// <param name="Line">The line, trimmed.</param>
    public readonly record struct Call(string Path, string Line);

    /// <summary>The part's text: a line saying what it holds, then the scripts, the area's types, the sources and the strings. Never throws on a missing file.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="scripts">Every object script under <see cref="Folder"/>, as the install's walk found them.</param>
    /// <param name="loaded">The files the area loaded - its object types among them.</param>
    /// <param name="shaders">Every shader source in the install, as its walk found them.</param>
    /// <param name="programs">The game's program files, read from disk.</param>
    public static string Write(
        Func<string, byte[]?> read, IReadOnlyList<string>? scripts, IReadOnlyList<string>? loaded, IReadOnlyList<string>? shaders, IReadOnlyList<string>? programs)
    {
        ArgumentNullException.ThrowIfNull(read);
        var body = new StringBuilder();

        // EVERY SCRIPT SEARCHED, on every core: in path order, runs of neighbours to one worker, so a bundle's last unpacked chunks
        // serve the files after.
        string[] paths = [.. (scripts ?? []).Order(StringComparer.OrdinalIgnoreCase)];
        var found = new ConcurrentDictionary<string, (string Text, List<string> Lines)>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        int unread = 0;
        var clock = Stopwatch.StartNew();
        Parallel.ForEach(Partitioner.Create(0, Math.Max(1, paths.Length), Run), range =>
        {
            long mine = 0;
            int missed = 0;
            for (int at = range.Item1; at < Math.Min(range.Item2, paths.Length); at++)
            {
                if (read(paths[at]) is not { Length: > 0 } content)
                {
                    missed++;
                    continue;
                }

                mine += content.Length;
                string text = StatDescriptionFiles.Decode(content);
                if (!ScriptWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                List<string> lines = [.. text.Split('\n').Select(line => line.Trim()).Where(line => ScriptWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)))];
                found[paths[at]] = (text, lines);
            }

            Interlocked.Add(ref bytes, mine);
            Interlocked.Add(ref unread, missed);
        });
        clock.Stop();

        string[] named = [.. found.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the object scripts (.ot, .otc under {Folder}) naming {string.Join(", ", ScriptWords)}, any case: {named.Length} of {paths.Length} searched ({bytes:N0} bytes, {unread} not read) in {clock.Elapsed.TotalSeconds:0.0} s").AppendLine();
        if (paths.Length == 0)
        {
            body.AppendLine("(no scripts: the install walk found none, or it had not run when the key went down)");
        }

        body.AppendLine().AppendLine(CallsHeading);
        foreach (string path in named)
        {
            foreach (string line in found[path].Lines)
            {
                body.Append("  ").Append(path).Append("  |  ").AppendLine(line);
            }
        }

        // THE AREA'S OWN: every loaded object type followed through what it extends, and the found scripts it reaches.
        string[] types = [.. (loaded ?? []).Select(TypeOf).Where(type => type.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        var reaching = new List<(string Type, string[] Reached)>();
        foreach (string type in types)
        {
            var reached = new List<string>();
            foreach (string kind in Extensions)
            {
                Reach(read, type + kind, kind, found, new HashSet<string>(StringComparer.OrdinalIgnoreCase), reached, depth: 0);
            }

            if (reached.Count > 0)
            {
                reaching.Add((type, [.. reached]));
            }
        }

        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the area's loaded object types reaching a found script through what they extend: {reaching.Count} of {types.Length}").AppendLine();
        foreach ((string type, string[] reached) in reaching)
        {
            body.Append("  ").Append(type).Append("  ->  ").AppendLine(string.Join(", ", reached));
        }

        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the found scripts whole, the first {MostWhole}").AppendLine();
        foreach (string path in named.Take(MostWhole))
        {
            body.AppendLine().Append("##### ").AppendLine(path);
            foreach (string line in found[path].Text.Split('\n'))
            {
                body.Append("  | ").AppendLine(line.TrimEnd('\r'));
            }
        }

        int sources = CaptureSources.Naming(read, shaders, ShaderWords, MostShaders, body);
        string strings = Programs(programs, body);
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"water shore: {named.Length} of {paths.Length} object scripts name the water's blocking or flow ({clock.Elapsed.TotalSeconds:0.0} s), {reaching.Count} of the area's {types.Length} loaded object types reach one; {(sources < 0 ? "no shader sources" : $"{sources} shader sources name the shore field's words")}; {strings}");
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
                inside = line == CallsHeading;
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

    /// <summary>What the calls say, worked out: each command with the scripts calling it and its arguments.</summary>
    public static string Report(IReadOnlyList<Call> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture, $"water shore replay: {calls.Count} lines in {calls.Select(call => call.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()} scripts").AppendLine();
        foreach (string word in ScriptWords)
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

    /// <summary>A loaded path as an object type - without its script's extension - or empty where it is none.</summary>
    private static string TypeOf(string path)
    {
        string clean = path.Replace('\\', '/').Trim();
        if (!clean.StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        foreach (string kind in Extensions)
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
    private static void Reach(
        Func<string, byte[]?> read, string path, string kind, ConcurrentDictionary<string, (string Text, List<string> Lines)> found,
        HashSet<string> seen, List<string> reached, int depth)
    {
        if (depth > 12 || !seen.Add(path))
        {
            return;
        }

        string text;
        if (found.TryGetValue(path, out (string Text, List<string> Lines) hit))
        {
            reached.Add(path);
            text = hit.Text;
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
                Reach(read, parent + kind, kind, found, seen, reached, depth + 1);
            }
        }
    }

    /// <summary>The strings each program holds naming a shore, the water's blocking or a markup.</summary>
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
