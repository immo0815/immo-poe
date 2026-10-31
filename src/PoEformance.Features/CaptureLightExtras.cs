using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's light extras part: what the install holds of the game's light beyond the sun that the tool does not draw yet - the
/// clouds' shadows, the screen-space fog, the sun's screen-space shadow, the roof sections hidden round the player, and the player's
/// light: every environment's sections of them key by key, every shader source and graph naming them whole, the program's strings
/// naming them, the textures those name with a picture of each, and the roof-section materials the model panes' models wear.
/// </summary>
/// <remarks>
/// WRITTEN FROM THE RED VALE'S light-against-game.txt (v0.1.372): where the pane draws full sun on the ground the game is about four
/// times darker, and the game's player light is 2.4 to 2.9 times the tool's within 400 units of the player. The area's file sets a
/// <c>clouds</c> and a <c>screenspace_fog</c> section, while the sources captured so far (water.txt's) only DECLARE the clouds'
/// uniforms (common.ffx), read clouds_velocity in one fragment (utilitynodes.ffx: CloudWindDirection), hand the fog's numbers to graphs
/// as fragments (effects.ffx: SSFLayerCountThicknessTurbulence, SSFColor, SSFDisperseRadiusFeathering) and read the sun's shadow off a
/// screen-space map (lighting.ffx: screenspace_shadowmap) made by a pass not captured. And the game hides "roof section" doodads round the player -
/// trees whose .mat says <c>"roofsection": true</c> (RoofFade_JungleTreeBranches01c.mat, OFO_BlackForestTrees01*_fadec.mat): the engine
/// keeps big objects from standing between the camera and the player (said from the live client). Each is to be built from the
/// engine's own code next, so its code and data are gathered first - CLAUDE.md's "a question the capture cannot answer is a new part,
/// built BEFORE the question is asked".
///
/// EACH SECTION IS BOUNDED (<see cref="Bounds"/>) and says what it cut. Its list lines are tab-separated records after a tag under a
/// numbered heading - the form <see cref="Replay"/> reads back - and every file written whole has its lines marked <c>  | </c>, so no
/// line of a source (whose comments draw rules of = signs) is taken for a heading.
/// </remarks>
public static class CaptureLightExtras
{
    /// <summary>The words an environment's section is taken whole by, any case: its name holding one - clouds, screenspace_fog, player_light.</summary>
    public static IReadOnlyList<string> SectionWords { get; } = ["cloud", "fog", "player_light"];

    /// <summary>The words a key of any other section is taken by, any case: its own name holding one.</summary>
    public static IReadOnlyList<string> KeyWords { get; } = ["cloud", "ssf", "fog", "player_light", "playerlight", "roof"];

    /// <summary>
    /// The words a shader source is found by: the clouds' and the fog's uniforms and fragments, the screen-space shadow, the roof
    /// sections' fade, the player's light and the clouds' fade radius - and the three fragments of <see cref="GraphNodes"/> none of those
    /// spell, so every source defining a node the graphs are found by is found too. Any case, but for <c>SSF</c> - see
    /// <see cref="SourceNamed"/>.
    /// </summary>
    /// <remarks>
    /// THE LAST THREE MEASURED: in v0.1.372's water.txt SeeThroughFade stands in legacy.ffx, which spells none of the words before it - the
    /// engine's fade of what stands between the camera and the player, the roof sections' likeliest mechanism, would have been left out.
    /// </remarks>
    public static IReadOnlyList<string> SourceWords { get; } =
    [
        "clouds_", "CloudWind", "ssf_", "SSF", "screenspace_fog", "screenspace_shadow", "ScreenspaceShadow", "screen_space_shadow",
        "roofsection", "roof_section", "RoofSection", "roof_fade", "player_light", "PlayerLight", "fade_radius",
        "ScreenspaceFog", "RoofFade", "SeeThroughFade",
    ];

    /// <summary>
    /// The graph nodes a graph is found by, as written: the fog's three fragments (effects.ffx) and the clouds' wind (utilitynodes.ffx),
    /// and four more of the same parts of the light.
    /// </summary>
    /// <remarks>
    /// THE LAST FOUR MEASURED in the sources The Red Vale's v0.1.372 water.txt holds whole: <c>ScreenspaceFog</c> (utilitynodes.ffx)
    /// marches the fog's layers through a fog texture (<c>fog_layer_tex</c>, RaymarchFogColor); <c>RoofFade</c> (internalonly.ffx,
    /// engineonly) hands a graph the object uniform <c>roof_fade</c>; <c>SeeThroughFade</c> (legacy.ffx, engineonly) fades what stands
    /// near the line from the camera to <c>player_position</c> - the engine keeping big objects from blocking the view; and
    /// <c>WriteScreenspaceShadows</c> (utilitynodes.ffx) writes the value lighting.ffx reads the sun's shadow from. A graph naming one is
    /// where the engine applies it.
    /// </remarks>
    public static IReadOnlyList<string> GraphNodes { get; } =
    [
        "SSFLayerCountThicknessTurbulence", "SSFColor", "SSFDisperseRadiusFeathering", "CloudWindDirection",
        "ScreenspaceFog", "RoofFade", "SeeThroughFade", "WriteScreenspaceShadows",
    ];

    /// <summary>The words a graph's or a texture's path is found by, any case.</summary>
    public static IReadOnlyList<string> PathWords { get; } = ["cloud", "fog", "roof"];

    /// <summary>The words the program's strings are found by, any case - see <see cref="Holds"/> for the two kept narrower.</summary>
    public static IReadOnlyList<string> ProgramWords { get; } = ["cloud", "fog", "ssf", "roof", "playerlight", "player_light"];

    /// <summary>The file the offline reader writes beside the part.</summary>
    public const string ReplayFile = "light-extras-replay.txt";

    /// <summary>The engine's own graphs' folder: a texture one of them names is the engine's, whatever its path.</summary>
    private const string EngineFolder = "Metadata/EngineGraphs/";

    /// <summary>The longest value one environment's line repeats - a colour is some thirty characters, a curve hundreds.</summary>
    private const int LongestValue = 200;

    /// <summary>The longest example value a key's line shows.</summary>
    private const int LongestExample = 96;

    /// <summary>Distinct example values kept per key.</summary>
    private const int MostExamples = 6;

    /// <summary>The examples that name the file that wrote them.</summary>
    private const int MostNamed = 3;

    /// <summary>The widest a texture is decoded at, either side - its header is read whatever its size.</summary>
    private const int WidestDecoded = 4096;

    /// <summary>Paths a worker reads in a row - side by side in their bundle, so its last chunks serve the next file (CaptureWaterShore's).</summary>
    private const int Run = 256;

    /// <summary>The tags of the list lines, which <see cref="Replay"/> reads back.</summary>
    private static readonly string[] Tags = ["key", "env", "source", "graph", "program", "string", "texture", "decoded", "undecoded", "pane", "roof"];

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The program's words, lower case, as bytes (CaptureWaterTextures.Strings lowers the runs).</summary>
    private static readonly byte[][] ProgramBytes = [.. ProgramWords.Select(word => Encoding.ASCII.GetBytes(word.ToLowerInvariant()))];

    /// <summary>
    /// Each graph node as a graph file spells it - a JSON string of its own, <c>"type": "SSFColor"</c>, quotes and all - in ASCII or
    /// UTF-8, and in UTF-16.
    /// </summary>
    /// <remarks>
    /// QUOTED, because the roof fade's own textures are named RoofFade_JungleTreeBranches01_colour_BC1.dds and the like: bare, a graph
    /// binding one would read as naming the node. Every node and link end in the graphs read so far is written so.
    /// </remarks>
    private static readonly (byte[] Ascii, byte[] Wide)[] NodeBytes =
        [.. GraphNodes.Select(node => (Encoding.ASCII.GetBytes('"' + node + '"'), Encoding.Unicode.GetBytes('"' + node + '"')))];

    /// <summary>How much each section may write, in characters - the part's own, a test's smaller.</summary>
    /// <param name="EnvironmentChars">The environments' section.</param>
    /// <param name="SourceChars">The shader sources written whole.</param>
    /// <param name="LargestSource">The largest one source written whole.</param>
    /// <param name="GraphChars">The graphs written whole.</param>
    /// <param name="LargestGraph">The largest one graph written whole.</param>
    /// <param name="MostStrings">The program's strings written, a program.</param>
    /// <param name="MostTextures">The textures read for their header.</param>
    /// <param name="MostPreviews">The textures decoded and drawn beside the part.</param>
    /// <param name="WidestPreview">The most texels a side a picture beside the part is drawn at.</param>
    /// <param name="RoofChars">The roof-section materials written whole.</param>
    /// <param name="LargestRoof">The largest one material written whole.</param>
    internal sealed record Bounds(
        int EnvironmentChars, int SourceChars, int LargestSource, int GraphChars, int LargestGraph, int MostStrings, int MostTextures,
        int MostPreviews, int WidestPreview, int RoofChars, int LargestRoof)
    {
        /// <summary>
        /// The part's: some seven megabytes of text at most, and twelve textures drawn at most 256 a side - a capture is cut into pieces
        /// past 29 MB, and a picture of noise 256 a side takes some 200 KB.
        /// </summary>
        /// <remarks>
        /// CHOSEN. The sources are the largest: of v0.1.372's water.txt, common.ffx, utilitynodes.ffx, lighting.ffx, effects.ffx,
        /// internalonly.ffx, legacy.ffx and water.ffx name the words, some 870 thousand characters between them (utilitynodes.ffx alone
        /// 370 thousand); four megabytes leaves room for the sources no part has written yet.
        /// </remarks>
        public static Bounds Part { get; } = new(
            EnvironmentChars: 512 * 1024, SourceChars: 4 * 1024 * 1024, LargestSource: 768 * 1024, GraphChars: 2 * 1024 * 1024,
            LargestGraph: 256 * 1024, MostStrings: 1500, MostTextures: 160, MostPreviews: 12, WidestPreview: 256, RoofChars: 512 * 1024,
            LargestRoof: 64 * 1024);
    }

    /// <summary>
    /// The part's text: a line saying what it holds, then the environments, the sources, the graphs, the program's strings, the textures
    /// and the roof sections - and the textures' pictures to write beside it. Never throws on a missing file.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="environments">Every .env in the install, as its walk found them.</param>
    /// <param name="shaders">Every shader source in the install, as its walk found them.</param>
    /// <param name="graphs">Every graph the walks found - any path, those not ending .fxgraph left out here.</param>
    /// <param name="programs">The game's program files, read from disk.</param>
    /// <param name="panes">The model panes showing a model when the key went down, or null.</param>
    /// <param name="area">The area's own environment, listed first - or null.</param>
    /// <param name="pictures">The decoded textures' pictures, each with the file it goes to beside the part.</param>
    public static string Write(
        Func<string, byte[]?> read, IReadOnlyList<string>? environments, IReadOnlyList<string>? shaders, IReadOnlyList<string>? graphs,
        IReadOnlyList<string>? programs, IReadOnlyList<CapturePane>? panes, string? area, out IReadOnlyList<(string File, GamePicture Picture)> pictures)
        => Write(read, environments, shaders, graphs, programs, panes, area, Bounds.Part, out pictures);

    /// <summary>The same, within the bounds given.</summary>
    internal static string Write(
        Func<string, byte[]?> read, IReadOnlyList<string>? environments, IReadOnlyList<string>? shaders, IReadOnlyList<string>? graphs,
        IReadOnlyList<string>? programs, IReadOnlyList<CapturePane>? panes, string? area, Bounds bounds,
        out IReadOnlyList<(string File, GamePicture Picture)> pictures)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(bounds);
        var clock = Stopwatch.StartNew();
        var body = new StringBuilder();
        var named = new Named();

        (int files, int keys) = Environments(read, environments, area, bounds, body, named);
        (int sources, int sourcesWhole) = Sources(read, shaders, bounds, body, named);
        (int found, int searched, int graphsWhole) = Graphs(read, graphs, bounds, body, named);
        string strings = Programs(programs, bounds, body, named);
        var drawn = new List<(string File, GamePicture Picture)>();
        (int textures, int decoded) = Textures(read, named, bounds, body, drawn);
        pictures = drawn;
        (int roofs, int shown) = Roofs(read, panes, bounds, body);
        clock.Stop();

        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"light extras: {(environments is { Count: > 0 } ? $"{files} of {environments.Count} environments set {keys} keys of the clouds, the fog and the player's light" : "no environments")}; {(sources < 0 ? "no shader sources" : $"{sources} shader sources name a word, {sourcesWhole} whole")}; {(searched == 0 ? "no graphs" : $"{found} of {searched} graphs name a node or their path a word, {graphsWhole} whole")}; {strings}; {textures} textures named, {decoded} drawn; {(panes is { Count: > 0 } ? $"{roofs} roof-section materials over {shown} model panes" : "no model pane")} ({clock.Elapsed.TotalSeconds:0.0} s)");
        return head + Environment.NewLine + body;
    }

    /// <summary>
    /// Whether a text holds a word, any case - two of them kept narrower, CHOSEN: <c>roof</c> not where it stands in "proof"
    /// (waterproof, foolproof), and <c>ssf</c> only as the engine spells the screen-space fog - <c>SSF</c> in capitals, beside an
    /// underscore or at a word's start - since three letters stand inside ProcessFrame, passFlags and ClassFactory.
    /// </summary>
    internal static bool Holds(string text, string word)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(word);
        for (int at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (Stands(text, word, at))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one place a word was found at counts - see <see cref="Holds"/>.</summary>
    private static bool Stands(string text, string word, int at)
    {
        if (word.Equals("roof", StringComparison.OrdinalIgnoreCase))
        {
            return at == 0 || char.ToLowerInvariant(text[at - 1]) != 'p';
        }

        if (word.Equals("ssf", StringComparison.OrdinalIgnoreCase))
        {
            int end = at + word.Length;
            return string.CompareOrdinal(text, at, "SSF", 0, 3) == 0
                || (end < text.Length && text[end] == '_')
                || at == 0 || !char.IsAsciiLetter(text[at - 1]);
        }

        return true;
    }

    /// <summary>
    /// The words of <see cref="SourceWords"/> a source names, once each: any case, but for a word of three letters or fewer - <c>SSF</c> -
    /// taken as written, since any case it stands in ProcessFrame and passFlags in every source; a word naming the same as one before it
    /// any case (RoofSection after roofsection) not again, so a source is not ranked twice for one word.
    /// </summary>
    internal static string[] SourceNamed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var named = new List<string>();
        foreach (string word in SourceWords)
        {
            bool held = word.Length <= 3 ? text.Contains(word, StringComparison.Ordinal) : text.Contains(word, StringComparison.OrdinalIgnoreCase);
            if (held && !named.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                named.Add(word);
            }
        }

        return [.. named];
    }

    /// <summary>
    /// Whether a material's JSON says it is a roof section: a member <c>roofsection</c> (or <c>roof_section</c>), any case, at any depth,
    /// set true - or to a number other than nought, or the text "true". Never throws.
    /// </summary>
    /// <remarks>
    /// AT ANY DEPTH, though RoofFade_JungleTreeBranches01c.mat writes it at the top beside <c>graphinstances</c> (v0.1.372's
    /// shadow-casters.txt): a scanner over the tokens costs nothing more and is not wrong where another material nests it.
    /// </remarks>
    internal static bool RoofSection(byte[]? content)
    {
        if (content is not { Length: > 0 })
        {
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(
                Encoding.UTF8.GetBytes(StatDescriptionFiles.Decode(content)),
                new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string name = reader.GetString() ?? string.Empty;
                if (!name.Equals("roofsection", StringComparison.OrdinalIgnoreCase) && !name.Equals("roof_section", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!reader.Read())
                {
                    return false;
                }

                bool set = reader.TokenType switch
                {
                    JsonTokenType.True => true,
                    JsonTokenType.Number => reader.TryGetDouble(out double number) && number != 0,
                    JsonTokenType.String => reader.GetString() is { } said && (said.Equals("true", StringComparison.OrdinalIgnoreCase) || said == "1"),
                    _ => false,
                };
                if (set)
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // A MATERIAL THAT IS NOT JSON is no roof section, and costs this answer alone.
        }

        return false;
    }

    /// <summary>
    /// Section 1: every key of a section whose name holds one of <see cref="SectionWords"/>, and every key elsewhere whose own name holds
    /// one of <see cref="KeyWords"/>, over every environment - how many files set each, a few values each with a file, then every
    /// environment setting one file by file, the area's own first. Returns how many files set one and how many keys.
    /// </summary>
    private static (int Files, int Keys) Environments(
        Func<string, byte[]?> read, IReadOnlyList<string>? environments, string? area, Bounds bounds, StringBuilder said, Named named)
    {
        said.AppendLine().Append("=== 1. the environments' clouds, fog and player light: every key of a section whose name holds ")
            .Append(string.Join(", ", SectionWords)).Append(", and every key elsewhere whose own name holds ").Append(string.Join(", ", KeyWords))
            .AppendLine(" (any case) - how many files set it, and its values, the first three with the file that wrote them");
        if (environments is not { Count: > 0 })
        {
            said.AppendLine("(no environments: the install walk found none, or it had not run when the key went down)");
            return (0, 0);
        }

        var keys = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
        var files = new List<(string Path, List<string> Set)>();
        int failed = 0;
        foreach (string path in environments.Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (read(path) is not { Length: > 0 } bytes)
            {
                continue;
            }

            var set = new List<string>();
            try
            {
                using var document = JsonDocument.Parse(StatDescriptionFiles.Decode(bytes), Lenient);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonProperty section in document.RootElement.EnumerateObject())
                {
                    if (section.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    bool whole = SectionWords.Any(word => section.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
                    foreach (JsonProperty entry in section.Value.EnumerateObject())
                    {
                        if (!whole && !KeyWords.Any(word => Holds(entry.Name, word)))
                        {
                            continue;
                        }

                        string name = section.Name + "." + entry.Name;
                        string value = Compact(entry.Value.GetRawText());
                        if (!keys.TryGetValue(name, out Tally? tally))
                        {
                            tally = new Tally();
                            keys[name] = tally;
                        }

                        tally.Add(value, path);
                        set.Add(name + " " + Shorten(value, LongestValue));

                        // A TEXTURE A CLOUDS OR FOG SECTION NAMES IS ITS OWN, whatever its path says - the section says what it is for.
                        foreach (string file in CaptureWaves.Files(value))
                        {
                            named.Offer(file, 0, "the environment " + path, always: true);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                failed++;
                continue;
            }

            if (set.Count > 0)
            {
                files.Add((path, set));
            }
        }

        int start = said.Length;
        said.Append(CultureInfo.InvariantCulture, $"{files.Count} of {environments.Count} environments set one, {keys.Count} keys{(failed > 0 ? $", {failed} did not read as JSON" : string.Empty)}").AppendLine();
        foreach ((string name, Tally tally) in keys)
        {
            said.Append(CultureInfo.InvariantCulture, $"  key\t{name}\t{tally.Files}\t").AppendLine(string.Join("  |  ", tally.Examples));
        }

        // EVERY FILE WHOLE, the area's own first: the light hunt finds each key the area's file sets at its slot in the parsed copy, so a
        // capture where a key is set names the slot an area's default stands in.
        bool own = area is { Length: > 0 } && files.Any(one => one.Path.Equals(Clean(area), StringComparison.OrdinalIgnoreCase));
        said.AppendLine().Append("=== 1b. every environment setting one, file by file - ").AppendLine(
            area is not { Length: > 0 } ? "the area's own environment was not known"
            : own ? "the area's own (" + Clean(area) + ") first"
            : "the area's own (" + Clean(area) + ") sets none");
        int cut = 0;
        foreach ((string path, List<string> set) in files.OrderBy(one => own && one.Path.Equals(Clean(area!), StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            if (said.Length - start > bounds.EnvironmentChars)
            {
                cut++;
                continue;
            }

            said.Append(CultureInfo.InvariantCulture, $"  env\t{path}\t{set.Count}\t").AppendLine(string.Join("; ", set));
        }

        if (cut > 0)
        {
            said.Append(CultureInfo.InvariantCulture, $"(cut: {cut} files not written - past the section's {bounds.EnvironmentChars / 1024:N0} KB; their keys are counted above)").AppendLine();
        }

        return (files.Count, keys.Count);
    }

    /// <summary>
    /// Section 2: every shader source naming a word of <see cref="SourceWords"/>, each with the words it names - the most words first -
    /// then each whole while the section's characters last. Returns how many name one (-1 with no sources) and how many were written whole.
    /// </summary>
    private static (int Found, int Whole) Sources(Func<string, byte[]?> read, IReadOnlyList<string>? shaders, Bounds bounds, StringBuilder said, Named named)
    {
        said.AppendLine().Append("=== 2. the shader sources naming ").Append(string.Join(", ", SourceWords))
            .AppendLine(" - any case, but SSF as written (three letters stand inside ProcessFrame and passFlags) - the most words first, each whole below while the section lasts");
        if (shaders is not { Count: > 0 })
        {
            said.AppendLine("(no shader sources: the install walk found none, or it had not run when the key went down)");
            return (-1, 0);
        }

        string[] paths = [.. shaders.Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase)];
        var found = new ConcurrentBag<(string Path, string Text, string[] Words)>();
        Parallel.ForEach(paths, path =>
        {
            if (read(path) is not { Length: > 0 } bytes)
            {
                return;
            }

            string text = StatDescriptionFiles.Decode(bytes);
            string[] words = SourceNamed(text);
            if (words.Length > 0)
            {
                found.Add((path, text, words));
            }
        });

        (string Path, string Text, string[] Words)[] ordered = [.. found
            .OrderByDescending(one => one.Words.Length)
            .ThenBy(one => one.Path, StringComparer.OrdinalIgnoreCase)];
        said.Append(CultureInfo.InvariantCulture, $"{ordered.Length} of {paths.Length} sources name one").AppendLine();
        foreach ((string path, string text, string[] words) in ordered)
        {
            said.Append(CultureInfo.InvariantCulture, $"  source\t{path}\t{text.Length}\t").AppendLine(string.Join(", ", words));
            foreach (string file in CaptureWaves.Files(text))
            {
                named.Offer(file, 3, "the source " + path, always: false);
            }
        }

        int whole = Wholes([.. ordered.Select(one => (one.Path, one.Text, string.Join(", ", one.Words)))], bounds.SourceChars, bounds.LargestSource, "sources", said);
        return (ordered.Length, whole);
    }

    /// <summary>
    /// Section 3: every graph naming a node of <see cref="GraphNodes"/>, or whose path holds a word of <see cref="PathWords"/> - searched
    /// on every core, on the bytes, ASCII and UTF-16 - those naming a node first, then the engine's own, each whole while the section's
    /// characters last. Returns how many were found, how many searched and how many written whole.
    /// </summary>
    private static (int Found, int Searched, int Whole) Graphs(Func<string, byte[]?> read, IReadOnlyList<string>? graphs, Bounds bounds, StringBuilder said, Named named)
    {
        string[] paths = [.. (graphs ?? [])
            .Select(Clean)
            .Where(path => path.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        var clock = Stopwatch.StartNew();
        var found = new ConcurrentBag<(string Path, string Text, string[] Nodes, string[] Words)>();
        long bytes = 0;
        int unread = 0;
        Parallel.ForEach(Partitioner.Create(0, Math.Max(1, paths.Length), Run), range =>
        {
            long mine = 0;
            int missed = 0;
            for (int at = range.Item1; at < Math.Min(range.Item2, paths.Length); at++)
            {
                string path = paths[at];
                if (read(path) is not { Length: > 0 } content)
                {
                    missed++;
                    continue;
                }

                mine += content.Length;
                var nodes = new List<string>();
                for (var node = 0; node < NodeBytes.Length; node++)
                {
                    if (content.AsSpan().IndexOf(NodeBytes[node].Ascii) >= 0 || content.AsSpan().IndexOf(NodeBytes[node].Wide) >= 0)
                    {
                        nodes.Add(GraphNodes[node]);
                    }
                }

                string[] words = [.. PathWords.Where(word => Holds(path, word))];
                if (nodes.Count > 0 || words.Length > 0)
                {
                    found.Add((path, StatDescriptionFiles.Decode(content), [.. nodes], words));
                }
            }

            Interlocked.Add(ref bytes, mine);
            Interlocked.Add(ref unread, missed);
        });
        clock.Stop();

        (string Path, string Text, string[] Nodes, string[] Words)[] ordered = [.. found
            .OrderByDescending(one => one.Nodes.Length)
            .ThenBy(one => one.Path.StartsWith(EngineFolder, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(one => one.Path, StringComparer.OrdinalIgnoreCase)];
        said.AppendLine().Append("=== 3. the graphs (.fxgraph) naming ").Append(string.Join(", ", GraphNodes)).Append(", or whose path holds ")
            .Append(string.Join(", ", PathWords))
            .Append(CultureInfo.InvariantCulture, $": {ordered.Length} of {paths.Length} searched ({bytes:N0} bytes, {unread} not read) in {clock.Elapsed.TotalSeconds:0.0} s - those naming a node first, then the engine's own, each whole below while the section lasts")
            .AppendLine();
        if (paths.Length == 0)
        {
            said.AppendLine("(no graphs: the install walks found none, or they had not run when the key went down)");
            return (0, 0, 0);
        }

        foreach ((string path, string text, string[] nodes, string[] words) in ordered)
        {
            said.Append(CultureInfo.InvariantCulture, $"  graph\t{path}\t{text.Length}\t{(nodes.Length > 0 ? string.Join(", ", nodes) : "-")}\t")
                .AppendLine(words.Length > 0 ? string.Join(", ", words) : "-");

            // WHAT A GRAPH APPLYING ONE OF THE FOUR BINDS IS THEIRS, whatever its path - the fog's layers are marched through a texture
            // its graph names (ScreenspaceFog's fog_layer_tex) - and so is what the engine's own graphs bind; another graph's only where
            // its path holds a word, a skill's poison cloud binding its own pictures.
            bool theirs = nodes.Length > 0 || path.StartsWith(EngineFolder, StringComparison.OrdinalIgnoreCase);
            foreach (string file in CaptureWaves.Files(text))
            {
                named.Offer(file, theirs ? 1 : 4, "the graph " + path, always: theirs);
            }
        }

        int whole = Wholes(
            [.. ordered.Select(one => (one.Path, one.Text, (one.Nodes.Length > 0 ? "names " + string.Join(", ", one.Nodes) : "names no node")
                + (one.Words.Length > 0 ? "; its path holds " + string.Join(", ", one.Words) : string.Empty)))],
            bounds.GraphChars, bounds.LargestGraph, "graphs", said);
        return (ordered.Length, paths.Length, whole);
    }

    /// <summary>
    /// Section 4: the strings each program holds naming a word of <see cref="ProgramWords"/> (<see cref="Holds"/>'s narrower two), those
    /// naming a file first. Returns a line saying how many.
    /// </summary>
    private static string Programs(IReadOnlyList<string>? programs, Bounds bounds, StringBuilder said, Named named)
    {
        said.AppendLine().Append("=== 4. the game's program: its strings naming ").Append(string.Join(", ", ProgramWords))
            .AppendLine(" - any case, ssf only as SSF or beside an underscore (it stands inside ProcessFrame), roof not inside proof - those naming a file first; a texture the engine binds itself is named here if anywhere");
        if (programs is not { Count: > 0 })
        {
            said.AppendLine("(no program: the install's folder was not found, or holds none of the game's programs)");
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
                said.Append("  program\t").Append(program).Append("\tnot read\t").AppendLine(failed.Message);
                continue;
            }

            SortedDictionary<string, int> strings = CaptureWaterTextures.Strings(bytes, ProgramBytes);
            KeyValuePair<string, int>[] kept = [.. strings
                .Where(one => ProgramWords.Any(word => Holds(one.Key, word)))
                .OrderByDescending(one => NamesFile(one.Key))
                .ThenBy(one => one.Key, StringComparer.Ordinal)];
            total += kept.Length;
            said.Append(CultureInfo.InvariantCulture, $"  program\t{program}\t{bytes.Length:N0} bytes\t{kept.Length} strings, {strings.Count - kept.Length} more holding a word only inside another").AppendLine();
            foreach ((string text, int times) in kept.Take(bounds.MostStrings))
            {
                said.Append(CultureInfo.InvariantCulture, $"  string\t{times}\t{text}").AppendLine();
            }

            if (kept.Length > bounds.MostStrings)
            {
                said.Append(CultureInfo.InvariantCulture, $"(cut: {kept.Length - bounds.MostStrings} more strings not written - past the {bounds.MostStrings} a program; those naming a file come first)").AppendLine();
            }

            foreach ((string text, _) in kept)
            {
                foreach (string file in CaptureWaves.Files(text))
                {
                    named.Offer(file, 2, "the program", always: false);
                }
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{total} strings in the program name one");
    }

    /// <summary>
    /// Section 5: every texture the sections above name (<see cref="Named"/>) - each one's header and who named it first - and the first
    /// that decode drawn beside the part, each channel's spread said. Returns how many were named and how many drawn.
    /// </summary>
    private static (int Textures, int Decoded) Textures(
        Func<string, byte[]?> read, Named named, Bounds bounds, StringBuilder said, List<(string File, GamePicture Picture)> pictures)
    {
        (string Path, int Rank, string By)[] listed = [.. named.InOrder];
        said.AppendLine().Append(CultureInfo.InvariantCulture,
            $"=== 5. the textures named above: every one an environment's taken section, a graph naming a node or an engine graph names, and those the sources, the program's strings and the other graphs name whose path holds {string.Join(", ", PathWords)} - {listed.Length}, each one's header and who names it first")
            .AppendLine();
        var drawn = new StringBuilder();
        drawn.AppendLine().Append(CultureInfo.InvariantCulture,
            $"=== 5b. the first {bounds.MostPreviews} that decode, at most {WidestDecoded} a side: each channel's spread over the top level, and the pictures beside this file at most {bounds.WidestPreview} texels a side (each texel the mean of the block it stands for) - its colour made opaque, and its alpha where it is not one value")
            .AppendLine();
        int decoded = 0;
        foreach ((string path, _, string by) in listed.Take(bounds.MostTextures))
        {
            byte[]? raw = GameArt.ReadRaw(read, path);
            said.Append("  texture\t").Append(path).Append('\t').Append(by).Append('\t').AppendLine(raw is null ? "(not in the install)" : ModelDump.Header(raw));
            if (raw is null || decoded >= bounds.MostPreviews)
            {
                continue;
            }

            if (raw.Length < 20 || raw[0] != (byte)'D' || raw[1] != (byte)'D' || raw[2] != (byte)'S')
            {
                drawn.Append("  undecoded\t").Append(path).AppendLine("\tnot a DDS");
                continue;
            }

            int height = BitConverter.ToInt32(raw, 12), width = BitConverter.ToInt32(raw, 16);
            if (width is <= 0 or > WidestDecoded || height is <= 0 or > WidestDecoded)
            {
                drawn.Append(CultureInfo.InvariantCulture, $"  undecoded\t{path}\t{width} x {height}, past the {WidestDecoded} decoded").AppendLine();
                continue;
            }

            if (GameArt.Decode(raw) is not { Ready: true } picture)
            {
                drawn.Append("  undecoded\t").Append(path).AppendLine("\tdid not decode");
                continue;
            }

            decoded++;
            string name = Path.GetFileNameWithoutExtension(path);
            GamePicture shrunk = Shrunk(picture, bounds.WidestPreview);
            string colour = string.Create(CultureInfo.InvariantCulture, $"light-extras-{decoded}-{name}-rgb.png");
            pictures.Add((colour, Preview(shrunk, alpha: false)));
            string files = colour;
            if (Varies(picture, 3))
            {
                string alpha = string.Create(CultureInfo.InvariantCulture, $"light-extras-{decoded}-{name}-a.png");
                pictures.Add((alpha, Preview(shrunk, alpha: true)));
                files += ", " + alpha;
            }

            drawn.Append(CultureInfo.InvariantCulture, $"  decoded\t{decoded}\t{path}\t{picture.Width} x {picture.Height}\t{files}").AppendLine();
            foreach (string line in ModelDump.Channels(picture).TrimEnd().Split('\n'))
            {
                drawn.Append("    ").AppendLine(line.TrimEnd('\r'));
            }
        }

        if (listed.Length > bounds.MostTextures)
        {
            said.Append(CultureInfo.InvariantCulture, $"(cut: {listed.Length - bounds.MostTextures} more textures not read - past the {bounds.MostTextures}: ")
                .Append(string.Join(", ", listed.Skip(bounds.MostTextures).Take(24).Select(one => one.Path)))
                .AppendLine(listed.Length - bounds.MostTextures > 24 ? ", ...)" : ")");
        }

        said.Append(drawn);
        return (listed.Length, decoded);
    }

    /// <summary>
    /// Section 6: for every model pane, the materials its model wears whose .mat says <c>"roofsection": true</c> (<see cref="RoofSection"/>),
    /// with the shapes and triangles wearing each and the doodads they came from - then each such material whole. Returns how many roof
    /// sections were found, counted once a pane, and over how many panes.
    /// </summary>
    /// <remarks>
    /// TWO GROUNDS MIXED ARE COUNTED AS THE TWO MATERIALS THEY ARE (v0.1.375), as CapturePanes.Materials writes them: the name a mix is worn
    /// under (GroundBlend) is no file, and counted as one every mix a laid stage wears was a material "not in the install" while its grounds
    /// read. How many mixes a pane wears is its line's last column.
    /// </remarks>
    private static (int Roofs, int Panes) Roofs(Func<string, byte[]?> read, IReadOnlyList<CapturePane>? panes, Bounds bounds, StringBuilder said)
    {
        said.AppendLine().AppendLine("=== 6. the roof sections: every material a model pane's model wears whose .mat says \"roofsection\": true - the shapes and triangles wearing it, and the doodads they came from")
            .AppendLine("    (pane: its book, what it shows, its shapes, the materials they wear, those not in the install, the roof sections among them, the mixes of two grounds among the shapes - each counted as its two grounds)");
        if (panes is not { Count: > 0 })
        {
            said.AppendLine("(no model pane showed a model when the key went down)");
            return (0, 0);
        }

        // EVERY MATERIAL READ ONCE, on every core - a laid block wears hundreds.
        string[] materials = [.. panes
            .SelectMany(pane => pane.Model.ShapeMaterials)
            .SelectMany(material => Files(MaterialFile.Bare(material)))
            .Where(material => material.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        var materialFiles = new ConcurrentDictionary<string, (bool Held, bool Roof, string Text)>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(materials, material =>
        {
            byte[]? bytes = read(material);
            bool roof = RoofSection(bytes);
            materialFiles[material] = (bytes is { Length: > 0 }, roof, roof ? StatDescriptionFiles.Decode(bytes!) : string.Empty);
        });

        var whole = new List<(string Material, string Book)>();
        int roofs = 0;
        foreach (CapturePane pane in panes)
        {
            MonsterModel model = pane.Model;
            IReadOnlyList<MeshShape> shapes = model.Mesh.Shapes;
            var worn = new Dictionary<string, (int Shapes, long Triangles, List<string> Sources)>(StringComparer.OrdinalIgnoreCase);
            var mixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var shape = 0; shape < shapes.Count && shape < model.ShapeMaterials.Count; shape++)
            {
                string wears = MaterialFile.Bare(model.ShapeMaterials[shape]);
                string[] files = Files(wears);
                if (files.Length > 1)
                {
                    mixes.Add(wears);
                }

                string source = shape < model.ShapeSources.Count ? model.ShapeSources[shape] : string.Empty;
                foreach (string material in files)
                {
                    if (material.Length == 0)
                    {
                        continue;
                    }

                    (int count, long triangles, List<string> sources) = worn.TryGetValue(material, out var had) ? had : (0, 0L, []);
                    if (source.Length > 0 && !sources.Contains(source, StringComparer.OrdinalIgnoreCase))
                    {
                        sources.Add(source);
                    }

                    worn[material] = (count + 1, triangles + (shapes[shape].Count / 3), sources);
                }
            }

            string[] theirs = [.. worn.Keys.Where(material => materialFiles.TryGetValue(material, out var one) && one.Roof).Order(StringComparer.OrdinalIgnoreCase)];
            int missing = worn.Keys.Count(material => !materialFiles.TryGetValue(material, out var one) || !one.Held);
            roofs += theirs.Length;
            said.Append(CultureInfo.InvariantCulture,
                    $"  pane\t{pane.Book}\t{(model.Ready ? model.Mesh_ : "nothing - " + model.Why)}\t{shapes.Count}\t{worn.Count}\t{missing}\t{theirs.Length}\t{mixes.Count}")
                .AppendLine();
            foreach (string material in theirs.OrderByDescending(material => worn[material].Triangles))
            {
                (int count, long triangles, List<string> sources) = worn[material];
                said.Append(CultureInfo.InvariantCulture, $"  roof\t{pane.Book}\t{material}\t{count}\t{triangles}\t")
                    .AppendLine(sources.Count == 0 ? "-" : string.Join(", ", sources.Take(6)) + (sources.Count > 6 ? string.Create(CultureInfo.InvariantCulture, $", and {sources.Count - 6} more") : string.Empty));
                if (!whole.Any(one => one.Material.Equals(material, StringComparison.OrdinalIgnoreCase)))
                {
                    whole.Add((material, pane.Book));
                }
            }
        }

        said.AppendLine().Append(CultureInfo.InvariantCulture, $"=== 6b. the roof-section materials whole: {whole.Count} - what else a roof section's material says, and the graphs it is an instance of").AppendLine();
        Wholes([.. whole.Select(one => (one.Material, materialFiles[one.Material].Text, "\"roofsection\": true, worn in the " + one.Book + " book's pane"))], bounds.RoofChars, bounds.LargestRoof, "materials", said);
        return (roofs, panes.Count);
    }

    /// <summary>The files a shape's material is read from: a mix of two grounds its two grounds (see <see cref="Roofs"/>), any other itself.</summary>
    private static string[] Files(string material)
        => GroundBlend.Parts(material, out string first, out string second, out _) ? [MaterialFile.Bare(first), MaterialFile.Bare(second)] : [material];

    /// <summary>
    /// The files whole, each under a <c>##### path  (why)</c> heading with its lines marked, in order while the section's characters last
    /// - one past the largest, or past what is left, named as cut and the rest still offered. Returns how many were written.
    /// </summary>
    private static int Wholes(IReadOnlyList<(string Path, string Text, string Why)> files, int budget, int largest, string kind, StringBuilder said)
    {
        long spent = 0;
        int whole = 0;
        var cut = new List<string>();
        foreach ((string path, string text, string why) in files)
        {
            string trimmed = text.TrimEnd();
            long size = trimmed.Length + (4L * (trimmed.AsSpan().Count('\n') + 1));
            if (size > largest || spent + size > budget)
            {
                cut.Add(path);
                continue;
            }

            said.AppendLine().Append("##### ").Append(path).Append("  (").Append(why).AppendLine(")");
            foreach (string line in trimmed.Split('\n'))
            {
                said.Append("  | ").AppendLine(line.TrimEnd('\r'));
            }

            spent += size;
            whole++;
        }

        if (cut.Count > 0)
        {
            said.AppendLine().Append(CultureInfo.InvariantCulture,
                    $"(cut: {cut.Count} {kind} not written whole - past the section's {budget / 1024:N0} KB, or the {largest / 1024:N0} KB one file may take; named above: ")
                .Append(string.Join(", ", cut.Take(24))).AppendLine(cut.Count > 24 ? ", ...)" : ")");
        }

        return whole;
    }

    /// <summary>A picture shrunk by whole steps to at most <paramref name="widest"/> a side, each texel the mean of the block it stands for.</summary>
    internal static GamePicture Shrunk(GamePicture picture, int widest)
    {
        int step = Math.Max(1, (Math.Max(picture.Width, picture.Height) + widest - 1) / Math.Max(1, widest));
        if (step == 1)
        {
            return picture;
        }

        int width = (picture.Width + step - 1) / step, height = (picture.Height + step - 1) / step;
        var rgba = new byte[width * height * 4];
        Span<int> sums = stackalloc int[4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                sums.Clear();
                int count = 0;
                for (int from = y * step; from < Math.Min(picture.Height, (y + 1) * step); from++)
                {
                    for (int across = x * step; across < Math.Min(picture.Width, (x + 1) * step); across++)
                    {
                        int at = ((from * picture.Width) + across) * 4;
                        for (var part = 0; part < 4; part++)
                        {
                            sums[part] += picture.Rgba[at + part];
                        }

                        count++;
                    }
                }

                int to = ((y * width) + x) * 4;
                for (var part = 0; part < 4; part++)
                {
                    rgba[to + part] = (byte)((sums[part] + (count / 2)) / count);
                }
            }
        }

        return new GamePicture(width, height, rgba);
    }

    /// <summary>A picture's colour made opaque, or its alpha as grey.</summary>
    private static GamePicture Preview(GamePicture picture, bool alpha)
    {
        var rgba = new byte[picture.Rgba.Length];
        for (var at = 0; at + 3 < rgba.Length; at += 4)
        {
            if (alpha)
            {
                rgba[at] = rgba[at + 1] = rgba[at + 2] = picture.Rgba[at + 3];
            }
            else
            {
                rgba[at] = picture.Rgba[at];
                rgba[at + 1] = picture.Rgba[at + 1];
                rgba[at + 2] = picture.Rgba[at + 2];
            }

            rgba[at + 3] = 255;
        }

        return new GamePicture(picture.Width, picture.Height, rgba);
    }

    /// <summary>Whether one channel of a picture is not one value throughout.</summary>
    private static bool Varies(GamePicture picture, int channel)
    {
        byte first = picture.Rgba[channel];
        for (int at = channel; at < picture.Rgba.Length; at += 4)
        {
            if (picture.Rgba[at] != first)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a string names a file - a path, or a picture's extension.</summary>
    private static bool NamesFile(string text) =>
        text.Contains('/', StringComparison.Ordinal) || text.Contains('\\', StringComparison.Ordinal) || Picture(text);

    /// <summary>Whether a path is a picture's.</summary>
    private static bool Picture(string path) =>
        path.Contains(".dds", StringComparison.OrdinalIgnoreCase) || path.Contains(".png", StringComparison.OrdinalIgnoreCase)
        || path.Contains(".tga", StringComparison.OrdinalIgnoreCase);

    /// <summary>A value's JSON on one line.</summary>
    private static string Compact(string value) => string.Join(' ', value.Split((char[])['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A text cut to a length, the cut said.</summary>
    private static string Shorten(string text, int most) => text.Length <= most ? text : text[..most] + "...";

    private static string Clean(string path) => path.Replace('\\', '/').Trim();

    /// <summary>
    /// The offline reader: the part's text read back, each section's records counted and named - what it holds, without the game.
    /// </summary>
    /// <param name="text">A <c>light-extras.txt</c>.</param>
    public static string Replay(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var records = Tags.ToDictionary(tag => tag, _ => new List<string[]>(), StringComparer.Ordinal);
        var whole = new Dictionary<char, HashSet<string>>();
        char section = '0';
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("=== ", StringComparison.Ordinal) && line.Length > 4 && char.IsAsciiDigit(line[4]))
            {
                section = line[4];
                continue;
            }

            if (line.StartsWith("##### ", StringComparison.Ordinal))
            {
                int why = line.IndexOf("  (", StringComparison.Ordinal);
                string path = why > 6 ? line[6..why] : line[6..];
                if (!whole.TryGetValue(section, out HashSet<string>? paths))
                {
                    paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    whole[section] = paths;
                }

                paths.Add(path);
                continue;
            }

            if (!line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith("  | ", StringComparison.Ordinal))
            {
                continue;
            }

            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab > 2 && records.TryGetValue(line[2..tab], out List<string[]>? kept))
            {
                kept.Add(line[(tab + 1)..].Split('\t'));
            }
        }

        HashSet<string> Whole(char of) => whole.TryGetValue(of, out HashSet<string>? paths) ? paths : [];
        string Field(string[] fields, int at) => at < fields.Length ? fields[at] : string.Empty;
        static int Number(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

        List<string[]> keys = records["key"], envs = records["env"], sources = records["source"], graphs = records["graph"];
        List<string[]> programs = records["program"], strings = records["string"], textures = records["texture"], decoded = records["decoded"];
        List<string[]> panes = records["pane"], roofs = records["roof"];
        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture,
            $"light extras replay: {keys.Count} environment keys in {envs.Count} files; {sources.Count} shader sources, {Whole('2').Count} whole; {graphs.Count} graphs, {Whole('3').Count} whole; {strings.Count} program strings in {programs.Count} programs; {textures.Count} textures, {decoded.Count} drawn; {roofs.Count} roof-section materials over {panes.Count} panes")
            .AppendLine();

        said.AppendLine().AppendLine("=== 1. the environments' keys by section, each key with how many files set it - the most first");
        static (string Section, string Key) Split(string name) => name.IndexOf('.', StringComparison.Ordinal) is var dot and > 0
            ? (name[..dot], name[(dot + 1)..])
            : (string.Empty, name);
        foreach (IGrouping<string, string[]> group in keys.GroupBy(key => Split(Field(key, 0)).Section, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            said.Append(CultureInfo.InvariantCulture, $"  {group.Key}: {group.Count()} keys - ")
                .AppendLine(string.Join(", ", group.OrderByDescending(key => Number(Field(key, 1))).ThenBy(key => Field(key, 0), StringComparer.Ordinal)
                    .Select(key => string.Create(CultureInfo.InvariantCulture, $"{Split(Field(key, 0)).Key} {Number(Field(key, 1))}"))));
        }

        said.AppendLine().AppendLine("=== 1b. the environments setting one, the most keys first (the first 40)");
        foreach (string[] env in envs.OrderByDescending(env => Number(Field(env, 1))).ThenBy(env => Field(env, 0), StringComparer.OrdinalIgnoreCase).Take(40))
        {
            said.Append(CultureInfo.InvariantCulture, $"  {Number(Field(env, 1)),3} keys  ").AppendLine(Field(env, 0));
        }

        said.AppendLine().AppendLine("=== 2. the shader sources, the most words first - whole where the part wrote them so");
        foreach (string[] source in sources)
        {
            said.Append("  ").Append(Field(source, 0)).Append("  (").Append(Field(source, 2)).Append(')').AppendLine(Whole('2').Contains(Field(source, 0)) ? " - whole" : string.Empty);
        }

        said.AppendLine().AppendLine("=== 2b. each word, by the sources naming it");
        foreach (string word in SourceWords)
        {
            string[] naming = [.. sources.Where(source => Field(source, 2).Split(", ").Contains(word, StringComparer.Ordinal)).Select(source => Field(source, 0))];
            if (naming.Length > 0)
            {
                said.Append(CultureInfo.InvariantCulture, $"  {word}: {naming.Length} - ").AppendLine(string.Join(", ", naming));
            }
        }

        said.AppendLine().AppendLine("=== 3. the graphs, by the node they name and by the word their path holds");
        foreach (string node in GraphNodes)
        {
            string[] naming = [.. graphs.Where(graph => Field(graph, 2).Split(", ").Contains(node, StringComparer.Ordinal)).Select(graph => Field(graph, 0))];
            said.Append(CultureInfo.InvariantCulture, $"  node {node}: {naming.Length}").AppendLine(naming.Length > 0 ? " - " + string.Join(", ", naming) : string.Empty);
        }

        foreach (string word in PathWords)
        {
            string[] naming = [.. graphs.Where(graph => Field(graph, 3).Split(", ").Contains(word, StringComparer.Ordinal)).Select(graph => Field(graph, 0))];
            said.Append(CultureInfo.InvariantCulture, $"  path {word}: {naming.Length}").AppendLine(naming.Length > 0 ? " - " + string.Join(", ", naming.Take(40)) + (naming.Length > 40 ? ", ..." : string.Empty) : string.Empty);
        }

        said.Append(CultureInfo.InvariantCulture, $"  whole: {Whole('3').Count} - ").AppendLine(string.Join(", ", Whole('3').Order(StringComparer.OrdinalIgnoreCase)));

        said.AppendLine().AppendLine("=== 4. the program's strings");
        foreach (string[] program in programs)
        {
            said.Append("  ").Append(Field(program, 0)).Append(": ").Append(Field(program, 1)).Append(", ").AppendLine(Field(program, 2));
        }

        string[] files = [.. strings.Select(one => Field(one, 1)).Where(NamesFile)];
        said.Append(CultureInfo.InvariantCulture, $"  naming a file: {files.Length}").AppendLine(files.Length > 0 ? " - " + string.Join(", ", files.Take(60)) + (files.Length > 60 ? ", ..." : string.Empty) : string.Empty);

        said.AppendLine().AppendLine("=== 5. the textures, and those drawn");
        foreach (string[] texture in textures)
        {
            string[]? drawn = decoded.FirstOrDefault(one => Field(one, 1).Equals(Field(texture, 0), StringComparison.OrdinalIgnoreCase));
            said.Append("  ").Append(Field(texture, 0)).Append("  (").Append(Field(texture, 1)).Append("): ").Append(Field(texture, 2))
                .AppendLine(drawn is null ? string.Empty : " - drawn " + Field(drawn, 2) + " as " + Field(drawn, 3));
        }

        said.AppendLine().AppendLine("=== 6. the roof sections, pane by pane");
        foreach (string[] pane in panes)
        {
            // THE GROUND MIXES ARE THE LAST COLUMN FROM v0.1.375; a line written before counted each mix as a material, one not in the install.
            string mixes = pane.Length > 6
                ? $", {Field(pane, 6)} ground mixes among the shapes, each counted as its two grounds"
                : " (written before the ground mixes were told apart: each mix among the materials, and among those not in the install)";
            said.Append(CultureInfo.InvariantCulture, $"  the {Field(pane, 0)} book's pane: {Field(pane, 1)} - {Field(pane, 2)} shapes in {Field(pane, 3)} materials, {Field(pane, 4)} not in the install, {Field(pane, 5)} roof sections").AppendLine(mixes);
            foreach (string[] roof in roofs.Where(roof => Field(roof, 0) == Field(pane, 0)))
            {
                said.Append(CultureInfo.InvariantCulture, $"    {Field(roof, 1)}: {Field(roof, 2)} shapes, {Field(roof, 3)} triangles, from {Field(roof, 4)}").AppendLine(Whole('6').Contains(Field(roof, 1)) ? " - whole" : string.Empty);
            }
        }

        return said.ToString();
    }

    /// <summary>One key: in how many files, and its first distinct values, the first few with the file that wrote them.</summary>
    private sealed class Tally
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public int Files { get; private set; }

        public List<string> Examples { get; } = [];

        public void Add(string value, string path)
        {
            Files++;
            string example = Shorten(value, LongestExample);
            if (Examples.Count < MostExamples && _seen.Add(example))
            {
                Examples.Add(Examples.Count < MostNamed ? $"{example} ({path})" : example);
            }
        }
    }

    /// <summary>
    /// The textures the sections name, each with who named it first - taken whatever its path where <c>always</c> says the namer is the
    /// light's own, else only where its path holds a word of <see cref="PathWords"/>.
    /// </summary>
    private sealed class Named
    {
        private readonly Dictionary<string, (int Rank, string By)> _found = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every texture, by its namer's rank - the environments, the light's graphs, the program, the sources, the other graphs - then by path.</summary>
        public IEnumerable<(string Path, int Rank, string By)> InOrder => _found
            .OrderBy(one => one.Value.Rank)
            .ThenBy(one => one.Key, StringComparer.OrdinalIgnoreCase)
            .Select(one => (one.Key, one.Value.Rank, one.Value.By));

        public void Offer(string path, int rank, string by, bool always)
        {
            string clean = Clean(path);
            if (!Picture(clean) || (!always && !PathWords.Any(word => Holds(clean, word))))
            {
                return;
            }

            if (!_found.TryGetValue(clean, out (int Rank, string By) had) || rank < had.Rank)
            {
                _found[clean] = (rank, by);
            }
        }
    }
}
