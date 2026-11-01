using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's particles part: everything the engine's GPU particles are made of that a port of them needs - each particle effect the
/// model panes' monsters and their parts name with its emitters read out, the graphs of their update and render materials and the engine's
/// own particle graphs whole with every node they use, and the shader sources defining those nodes whole, through every declaration they
/// include.
/// </summary>
/// <remarks>
/// DECIDED FROM THE LIVE CLIENT (2026-11-01, the Exile's Staff's lantern burns in the game and not in the Monster Book): the engine's GPU
/// particle system built generically - its update graphs compiled for the card and simulated there, its render graphs drawn, for every
/// effect - and a capture part gathering the engine's particle sources first. Three earlier parts carry pieces of it - monster-files.txt the
/// lantern's effect files (v0.1.401), render-debug.txt the engine's graphs and, because they name nodes the compile does not know,
/// gpuparticles.ffx and gputrails.ffx whole - and of the 721 node types the particle graphs of v0.1.403's capture use, 719 were defined
/// in those and the earlier captures' light-extras.txt and water.txt (<c>&lt;Group&gt;</c> is the graph's own grouping, and
/// ActorVisualSizeInformation was in none). But render-debug writes the sources naming an unknown node only as long as the compile does not know them, the twelve
/// most first, and monster-files the Monster Book's monster alone; this part keeps the whole set together for every pane, whatever the
/// compile learns.
///
/// WHAT IT IS NOT: a reading of the engine's C++ - the emitter's own logic (how many it spawns and when, its time, its flags) is filled in by
/// the processor into each emitter's data (gpu_particles_emitter_data's ParticleEmitterData), and only its fields' names stand in the
/// sources. The stage order the update compute shader runs its graphs in is shader-stages.txt's (ParticlesSetup to ParticlesWriteFinal).
/// </remarks>
public static partial class CaptureParticles
{
    /// <summary>The engine's own folder of particle graphs.</summary>
    public const string EngineFolder = "Metadata/EngineGraphs/GpuParticles/";

    /// <summary>What an effect file ends in: particles, effect packs, trails.</summary>
    private static readonly string[] EffectKinds = [".pet", ".epk", ".trl"];

    /// <summary>The files read as text and followed for what they name.</summary>
    private static readonly string[] Texts = [".ao", ".aoc", ".ot", ".otc", ".pet", ".epk", ".trl", ".mat", ".fxgraph"];

    /// <summary>Most files the walk reads.</summary>
    private const int MostFiles = 800;

    /// <summary>Most hops from a model's own file.</summary>
    private const int MostHops = 7;

    /// <summary>The characters the effect files and graphs may take whole, and the most one may.</summary>
    private const int GraphChars = 24 * 1024 * 1024, LargestGraph = 1024 * 1024;

    /// <summary>The characters the defining sources may take whole, and the most one may.</summary>
    private const int SourceChars = 12 * 1024 * 1024, LargestSource = 2 * 1024 * 1024;


    /// <summary>The part's text: a line saying what it found, then the walk, the emitters, the node census, the sources and the files whole.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="panes">The model panes as they stood at the press.</param>
    /// <param name="engineGraphs">Every path under Metadata/EngineGraphs/, as the install's walk found them.</param>
    /// <param name="shaders">Every shader source in the install, as its walk found them.</param>
    /// <param name="programs">The game's program files on disk, where the engine's DX11 prelude is read from (ShaderPrelude); null or empty for none.</param>
    public static string Write(Func<string, byte[]?> read, IReadOnlyList<CapturePane>? panes, IReadOnlyList<string>? engineGraphs, IReadOnlyList<string>? shaders, IReadOnlyList<string>? programs = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        var body = new StringBuilder();

        // THE SEEDS: every pane's monster's own files and every part he wears, then the engine's particle graphs.
        var seeds = new List<(string Path, string By)>();
        foreach (CapturePane pane in panes ?? [])
        {
            foreach (string file in pane.Variety?.AoFiles ?? [])
            {
                seeds.Add((file, $"the {pane.Book} book's monster"));
            }

            foreach (PartFit part in (pane.Model.Actor ?? pane.Model).Fitted)
            {
                seeds.Add((part.File, $"a part of the {pane.Book} book's monster"));
            }
        }

        string[] engine = [.. (engineGraphs ?? [])
            .Select(Clean)
            .Where(path => path.StartsWith(EngineFolder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        seeds.AddRange(engine.Select(path => (path, "the engine's particle graphs")));

        List<Walked> walked = Walk(read, seeds, body);
        int emitters = Emitters(walked, body);
        Dictionary<string, string> graphs = walked
            .Where(one => one.Text is not null && one.Path.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(one => one.Path, one => one.Text!, StringComparer.OrdinalIgnoreCase);
        string compiled = CaptureRenderDebug.Compiled(graphs, body);
        (string Text, string File)? prelude = programs is { Count: > 0 } ? ShaderPrelude.Read(programs) : null;
        (int defined, int missing, int sources, IReadOnlyDictionary<string, string> texts) = Sources(read, shaders, graphs, body, prelude);

        // THE LINK: every emitter's shaders composed as the engine composes them, the update linked from the sources.
        var known = walked.Where(one => one.Text is not null).ToDictionary(one => one.Path, one => one.Text!, StringComparer.OrdinalIgnoreCase);
        string? Text(string path)
            => known.TryGetValue(Clean(path), out string? had) ? had : read(Clean(path)) is { Length: > 0 } bytes ? StatDescriptionFiles.Decode(bytes) : null;
        string link = Link(Text, ShaderSource.Of(texts.Select(one => (one.Key, one.Value))), walked.Where(one => one.Path.EndsWith(".pet", StringComparison.OrdinalIgnoreCase)).Select(one => one.Path), body, prelude?.Text);

        // THE FILES WHOLE LAST - the effects, the materials and the graphs - in the form the offline readers read back.
        body.AppendLine().AppendLine("=== the effect files, materials and graphs whole");
        int whole = CaptureLightExtras.Wholes(
            [.. walked.Where(one => one.Text is not null && !IsObject(one.Path)).Select(one => (one.Path, one.Text!, $"hop {one.Hop}, named by {one.By}"))],
            GraphChars, LargestGraph, "files", body);

        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"particles: {walked.Count(one => IsEffect(one.Path))} effect files from {panes?.Count ?? 0} panes, {emitters} emitters; {graphs.Count} graphs ({engine.Length} the engine's particle graphs), {compiled}; {defined} node types and declarations defined in {sources} sources written whole, {missing} defined by none; {link}; {whole} files written whole");
        return head + Environment.NewLine + body;
    }

    /// <summary>One file the walk read: its path, its hop, who named it, its size and its text where it is one.</summary>
    private sealed record Walked(string Path, int Hop, string By, int Bytes, string? Text);

    /// <summary>
    /// The files hop by hop: from a model's own .ao, what it extends and the effects it names; from an effect, a material or a graph,
    /// everything it names - the textures and meshes listed, not followed.
    /// </summary>
    private static List<Walked> Walk(Func<string, byte[]?> read, IReadOnlyList<(string Path, string By)> seeds, StringBuilder body)
    {
        body.AppendLine().AppendLine("=== 1. the walk: from each pane's monster's own files and his parts' - what each extends and the effects each names - then every file an effect, a material or a graph names; and the engine's particle graphs (Metadata/EngineGraphs/GpuParticles/) and what they name - each file with its size, hop, what named it and its kind");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Hop, string By)>();
        void Offer(string path, int hop, string by)
        {
            string clean = Clean(path);
            if (clean.Length > 0 && seen.Count < MostFiles * 4 && seen.Add(clean))
            {
                queue.Enqueue((clean, hop, by));
            }
        }

        foreach ((string path, string by) in seeds)
        {
            Offer(path, 0, by);
        }

        var walked = new List<Walked>();
        body.AppendLine("  file\tpath\tbytes\thop\tnamed by\tkind");
        while (queue.Count > 0 && walked.Count < MostFiles)
        {
            (string path, int hop, string by) = queue.Dequeue();
            string extension = Path.GetExtension(path);
            bool text = Texts.Contains(extension, StringComparer.OrdinalIgnoreCase);
            byte[]? bytes = text || extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ? read(path) : null;
            if (bytes is not { Length: > 0 } && (text || extension.Equals(".dds", StringComparison.OrdinalIgnoreCase)))
            {
                body.Append(CultureInfo.InvariantCulture, $"  file\t{path}\tnot in the install\t{hop}\t{by}").AppendLine();
                walked.Add(new Walked(path, hop, by, 0, null));
                continue;
            }

            string kind = text ? "text" : extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ? ModelDump.Header(bytes!) : "named, not read";
            body.Append(CultureInfo.InvariantCulture, $"  file\t{path}\t{bytes?.Length ?? 0}\t{hop}\t{by}\t{kind}").AppendLine();
            string? decoded = text ? StatDescriptionFiles.Decode(bytes!) : null;
            walked.Add(new Walked(path, hop, by, bytes?.Length ?? 0, decoded));
            if (decoded is null || hop >= MostHops)
            {
                continue;
            }

            bool model = IsObject(path);
            foreach (string next in CaptureLightExtras.NamedIn(decoded))
            {
                string kindOf = Path.GetExtension(next);

                // FROM AN OBJECT only what it extends and the effects it names - not every mesh, rig or sound of it.
                if (model && !(IsObject(next) ? Extends(decoded, next) : EffectKinds.Contains(kindOf, StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Offer(next, hop + 1, path);
            }
        }

        if (queue.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"  (cut: {queue.Count} more files named past the {MostFiles} the walk reads)").AppendLine();
        }

        if (!walked.Any(one => IsEffect(one.Path)))
        {
            body.AppendLine("  (no effect named by any pane's monster or part)");
        }

        return walked;
    }

    /// <summary>Every particle effect's emitters as its JSON writes them, one line each with the fields a port reads first. Returns how many.</summary>
    private static int Emitters(List<Walked> walked, StringBuilder body)
    {
        body.AppendLine().AppendLine("=== 2. each particle effect's emitters (.pet, JSON after its version line): the pool (particles_count_min..max), each one's life (particle_duration_min..max), how they come (is_continuous, emit_burst, emit_once, emit_chance, emitters_per_sec, the interval's active, pause and start), the group, the mesh, the update material, the render passes (material, face_lock, optional) and the locks set");
        int count = 0;
        foreach (Walked one in walked.Where(one => one.Text is not null && one.Path.EndsWith(".pet", StringComparison.OrdinalIgnoreCase)))
        {
            int start = one.Text!.IndexOf('{', StringComparison.Ordinal);
            if (start < 0)
            {
                body.Append("  ").Append(one.Path).AppendLine(": no JSON in it");
                continue;
            }

            try
            {
                using var json = JsonDocument.Parse(one.Text[start..], new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                foreach (JsonProperty list in json.RootElement.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.Array))
                {
                    var index = 0;
                    foreach (JsonElement emitter in list.Value.EnumerateArray())
                    {
                        count++;
                        body.Append(CultureInfo.InvariantCulture, $"  {one.Path} {list.Name}[{index++}]: ").AppendLine(Emitter(emitter));
                    }
                }
            }
            catch (JsonException failed)
            {
                body.Append("  ").Append(one.Path).Append(": does not read as JSON - ").AppendLine(failed.Message);
            }
        }

        if (count == 0)
        {
            body.AppendLine("  (none)");
        }

        return count;
    }

    /// <summary>One emitter in a line.</summary>
    private static string Emitter(JsonElement emitter)
    {
        if (emitter.ValueKind != JsonValueKind.Object)
        {
            return "not an object: " + emitter.GetRawText();
        }

        string Field(string name) => emitter.TryGetProperty(name, out JsonElement value) ? Said(value) : "-";
        var locks = new List<string>();
        var passes = new List<string>();
        foreach (JsonProperty property in emitter.EnumerateObject())
        {
            if (property.Name.StartsWith("lock_", StringComparison.Ordinal)
                && !(property.Value.ValueKind == JsonValueKind.False || (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == "Disabled")))
            {
                locks.Add($"{property.Name} {Said(property.Value)}");
            }
        }

        if (emitter.TryGetProperty("render_passes", out JsonElement renders) && renders.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement pass in renders.EnumerateArray())
            {
                string material = pass.TryGetProperty("render_material", out JsonElement m) ? Said(m) : "-";
                string face = pass.TryGetProperty("face_lock", out JsonElement f) ? Said(f) : "-";
                bool optional = pass.TryGetProperty("optional", out JsonElement o) && o.ValueKind == JsonValueKind.True;
                passes.Add($"{material} facing {face}{(optional ? " (optional)" : string.Empty)}");
            }
        }

        string interval = emitter.TryGetProperty("interval", out JsonElement every) && every.ValueKind == JsonValueKind.Object
            ? string.Join(' ', every.EnumerateObject().Select(property => $"{property.Name} {Said(property.Value)}"))
            : "-";
        return $"pool {Field("particles_count_min")}..{Field("particles_count_max")} ({Field("count_type")}), life {Field("particle_duration_min")}..{Field("particle_duration_max")} s, "
            + $"continuous {Field("is_continuous")}, burst {Field("emit_burst")}, once {Field("emit_once")}, chance {Field("emit_chance")}, per second {Field("emitters_per_sec")}, "
            + $"emitter {Field("emitter_duration_min")}..{Field("emitter_duration_max")} s, interval [{interval}], group {Field("group_size")}, mesh {Field("particle_mesh")}, "
            + $"update {Field("update_material")}, render [{string.Join("; ", passes)}], locks [{string.Join(", ", locks)}], bounding {Field("bounding_size")}, culling {Field("culling_prio")}, persistent {Field("is_persistent")}";
    }

    /// <summary>A JSON value as written, a string unquoted.</summary>
    private static string Said(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() is { Length: > 0 } text ? text : "\"\"",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };

    /// <summary>
    /// Where every node type the graphs use is defined - a FRAGMENT, an EXTENSIONPOINT (an Input node reads one), and the DECLARATIONS
    /// they include, through every include - and those sources whole, the most definitions first. Returns how many names were defined,
    /// how many none defines, and how many sources were written.
    /// </summary>
    private static (int Defined, int Missing, int Sources, IReadOnlyDictionary<string, string> Texts) Sources(
        Func<string, byte[]?> read, IReadOnlyList<string>? shaders, Dictionary<string, string> graphs, StringBuilder body, (string Text, string File)? prelude)
    {
        body.AppendLine().AppendLine("=== 4. where each node type the graphs use is defined: its FRAGMENT, the EXTENSIONPOINT an Input node reads, or none; then every DECLARATIONS block those include, through every include");
        if (shaders is not { Count: > 0 })
        {
            body.AppendLine("(no shader sources: the install walk found none, or it had not run when the key went down)");
            return (0, 0, 0, new Dictionary<string, string>());
        }

        // EVERY DEFINITION IN THE INSTALL'S SOURCES, read on every core: its kind, its source, its includes.
        var texts = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var definitions = new ConcurrentDictionary<string, ConcurrentBag<(string Kind, string Path, string[] Includes)>>(StringComparer.Ordinal);
        Parallel.ForEach(shaders.Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase), path =>
        {
            if (read(path) is not { Length: > 0 } bytes)
            {
                return;
            }

            string text = StatDescriptionFiles.Decode(bytes);
            texts[path] = text;
            foreach (Match match in Definition().Matches(text))
            {
                definitions.GetOrAdd(match.Groups[2].Value, _ => []).Add((match.Groups[1].Value, path, Includes(text, match.Index + match.Length)));
            }
        });

        // THE NODE TYPES, an Input node by the extension point it reads where no fragment is named after it.
        var types = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string text in graphs.Values)
        {
            foreach (ShaderNode node in ShaderGraph.Read(Encoding.UTF8.GetBytes(text)).Nodes)
            {
                types[node.Type] = types.GetValueOrDefault(node.Type) + 1;
            }
        }

        var needed = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();
        var queue = new Queue<string>();
        foreach ((string type, int uses) in types)
        {
            string name = definitions.ContainsKey(type) ? type : type.StartsWith("Input", StringComparison.Ordinal) && definitions.ContainsKey(type[5..]) ? type[5..] : string.Empty;
            if (name.Length == 0)
            {
                missing.Add(type);
                body.Append(CultureInfo.InvariantCulture, $"  node\t{type}\t{uses} uses\t{(Handled(type) ? "known to the compile" : "NOT KNOWN")}\tdefined by no source").AppendLine();
                continue;
            }

            var where = definitions[name].ToArray();
            body.Append(CultureInfo.InvariantCulture, $"  node\t{type}\t{uses} uses\t{(Handled(type) ? "known to the compile" : "NOT KNOWN")}\t{string.Join(", ", where.Select(one => $"{one.Kind} {name} in {one.Path}").Distinct(StringComparer.Ordinal))}").AppendLine();
            if (needed.TryAdd(name, where[0].Path))
            {
                queue.Enqueue(name);
            }
        }

        // THE DECLARATIONS THEY INCLUDE, through every include.
        while (queue.Count > 0)
        {
            string name = queue.Dequeue();
            if (!definitions.TryGetValue(name, out ConcurrentBag<(string Kind, string Path, string[] Includes)>? found))
            {
                continue;
            }

            foreach (string include in found.SelectMany(one => one.Includes).Distinct(StringComparer.Ordinal))
            {
                if (needed.ContainsKey(include))
                {
                    continue;
                }

                if (definitions.TryGetValue(include, out ConcurrentBag<(string Kind, string Path, string[] Includes)>? declared))
                {
                    (string kind, string path, _) = declared.First();
                    needed[include] = path;
                    queue.Enqueue(include);
                    body.Append(CultureInfo.InvariantCulture, $"  include\t{include}\t{kind} in {path}\tincluded by {name}").AppendLine();
                }
                else
                {
                    missing.Add(include);
                    body.Append(CultureInfo.InvariantCulture, $"  include\t{include}\tdefined by no source\tincluded by {name}").AppendLine();
                }
            }
        }

        // THE SOURCES WHOLE, the most definitions asked for first.
        var bySource = needed.GroupBy(one => one.Value, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== 5. the sources defining them whole: {bySource.Length}, each with how many of the names above it defines").AppendLine();
        var wholes = bySource.Where(group => texts.ContainsKey(group.Key)).Select(group => (group.Key, texts[group.Key], $"defines {group.Count()}: {string.Join(", ", group.Select(one => one.Key).Take(40))}{(group.Count() > 40 ? ", ..." : string.Empty)}")).ToList();

        // THE .hlsl FILES THEIR CODE INCLUDES (Shaders/Include/Quaternions.hlsl, HalfUtility.hlsl - asked for by v0.1.429's link, which compiles
        // nothing without them), through their own includes, read from the install whole.
        if (prelude is { } engine)
        {
            wholes.Add((PreludePath, engine.Text, $"the text the engine puts before every shader for Direct3D 11, read from {Path.GetFileName(engine.File)}"));
        }

        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(wholes.SelectMany(one => HlslIncludes(one.Item2)));
        while (pending.Count > 0)
        {
            string path = Clean(pending.Dequeue());
            if (!included.Add(path) || read(path) is not { Length: > 0 } bytes)
            {
                continue;
            }

            string text = StatDescriptionFiles.Decode(bytes);
            texts[path] = text;
            wholes.Add((path, text, "an .hlsl the sources' code includes"));
            foreach (string next in HlslIncludes(text))
            {
                pending.Enqueue(next);
            }
        }

        int written = CaptureLightExtras.Wholes(wholes, SourceChars, LargestSource, "sources", body);
        return (needed.Count, missing.Distinct(StringComparer.Ordinal).Count(), written, texts);
    }

    /// <summary>The includes of a definition's head: its lines from the name to the body's opening <c>{{</c>.</summary>
    private static string[] Includes(string text, int from)
    {
        int open = text.IndexOf("{{", from, StringComparison.Ordinal);
        int next = Definition().Match(text, from) is { Success: true } after ? after.Index : text.Length;
        int end = open < 0 ? next : Math.Min(open, next);
        var includes = new List<string>();
        foreach (string line in text[from..end].Split('\n'))
        {
            if (Include().Match(line) is { Success: true } match)
            {
                includes.Add(match.Groups[1].Value);
            }
        }

        return [.. includes];
    }

    /// <summary>The files a source's code includes by <c>#include "path"</c>.</summary>
    private static IEnumerable<string> HlslIncludes(string text)
    {
        foreach (Match match in HlslInclude().Matches(text))
        {
            yield return match.Groups[1].Value;
        }
    }

    /// <summary>Whether the compile handles a node type: evaluates it, or carries it through as an output.</summary>
    private static bool Handled(string type) => ShadeProgram.Knows(type) || ShadeProgram.Writes(type);

    /// <summary>Whether a path is an object's file - an .ao, .aoc, .ot or .otc.</summary>
    private static bool IsObject(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ao" or ".aoc" or ".ot" or ".otc";

    /// <summary>Whether a path is an effect's file.</summary>
    private static bool IsEffect(string path) => EffectKinds.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether an object's text extends <paramref name="path"/>, written with or without its suffix.</summary>
    private static bool Extends(string text, string path)
    {
        string bare = Path.ChangeExtension(path, null) ?? path;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("extends", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(bare, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Clean(string path) => path.Replace('\\', '/').Trim();

    /// <summary>A definition's head line in an .ffx: its kind and its name.</summary>
    [GeneratedRegex(@"^(FRAGMENT|EXTENSIONPOINT|DECLARATIONS)\s+(\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Definition();

    /// <summary>An HLSL include line of a source's code.</summary>
    [GeneratedRegex(@"#include\s+""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex HlslInclude();

    /// <summary>An include line of a definition's head.</summary>
    [GeneratedRegex(@"^\s*include\s+(\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex Include();
}
