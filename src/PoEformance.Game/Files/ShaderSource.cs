using System.Text;

namespace PoEformance.Game.Files;

/// <summary>What kind of definition an <c>.ffx</c> block is.</summary>
public enum ShaderBlockKind
{
    /// <summary>A node's code: ports, uniforms, includes and a body (<c>FRAGMENT</c>).</summary>
    Fragment,

    /// <summary>Code and uniforms other blocks include by name (<c>DECLARATIONS</c>).</summary>
    Declarations,

    /// <summary>A named value graphs read and write at stages (<c>EXTENSIONPOINT</c>).</summary>
    Extension,

    /// <summary>A loop the nodes carrying its group id run inside (<c>GROUP</c>).</summary>
    Group,
}

/// <summary>One port of a fragment or group: <c>in float a</c>, <c>out uint particle_id : PARTICLE_ID</c>.</summary>
/// <param name="Direction">in, out or inout - a group's <c>loop</c> ports too, and <c>prologue out</c> as written.</param>
/// <param name="Type">The HLSL type.</param>
/// <param name="Name">The name the body uses.</param>
/// <param name="Semantic">
/// What follows the colon, or empty. A semantic binds the port to a value no link names: an <c>out ... : PARTICLE_ID</c> of one fragment is
/// what every <c>in ... : PARTICLE_ID</c> of the others reads (ReadParticleUpdateId writes it, WriteParticleData reads it), and <c>SV_</c>
/// ones are the shader's own inputs.
/// </param>
/// <param name="Condition">The preprocessor lines the port stands inside, one a line (an <c>#else</c> after its <c>#if</c>) - empty where none.</param>
public sealed record ShaderPort(string Direction, string Type, string Name, string Semantic, string Condition);

/// <summary>
/// One uniform line of a block: <c>uniform pipeline cs rw_structBuffer&lt;ParticleData&gt; particles_data_write</c> in declarations, or a node's own
/// <c>uniform float add_const "b" "0" "2" "0" : custom_range</c> in a fragment, which the node's parameters fill in their order.
/// </summary>
/// <param name="Scope">pipeline, object, frame or pass - or empty for a node's own.</param>
/// <param name="Stages">The shader stages it is bound to where written (<c>cs</c>, <c>vs|ps</c>), else empty.</param>
/// <param name="Type">The HLSL type, or the engine's (<c>spline5</c>, <c>texture</c>, <c>sampler</c>).</param>
/// <param name="Name">Its name.</param>
/// <param name="Quoted">The quoted words after the name: a node uniform's label, least, most and default.</param>
/// <param name="Semantic">What follows the colon (<c>custom_range</c>), or empty.</param>
/// <param name="Condition">The preprocessor lines it stands inside, one a line, or empty.</param>
public sealed record ShaderUniform(string Scope, string Stages, string Type, string Name, IReadOnlyList<string> Quoted, string Semantic, string Condition);

/// <summary>One definition of an <c>.ffx</c> source, as written.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Name">Its name - a fragment's is the node type a graph names.</param>
/// <param name="Source">The source file it stands in.</param>
/// <param name="Flags">Its one-word head lines (compute, engineonly, sideeffects, nocode, defaultvisible ...) and <c>cost:</c> as <c>cost:cheap</c>.</param>
/// <param name="Includes">The declarations it includes, in order.</param>
/// <param name="Macros">The macros using it defines (<c>GPU_PARTICLE_DRAW_CALL</c>, <c>GROUP_SIZE_X:64</c> as written).</param>
/// <param name="Uniforms">Its uniform lines.</param>
/// <param name="Ports">Its ports, in order.</param>
/// <param name="Values">An extension point's or group's named lines: <c>group</c>, <c>type</c>, <c>defaultstage</c>, <c>lightingmodel</c>, <c>unroll:</c>.</param>
/// <param name="Bodies">Its code by name: <c>body</c> for a fragment's or declarations' <c>{{ }}</c>; an extension point's <c>generate</c>, <c>read</c> and <c>write</c>; a group's <c>prologue</c>, <c>inner prologue</c>, <c>inner epilogue</c> and <c>epilogue</c>.</param>
/// <param name="Header">The head's preprocessor lines and lines read as nothing else, kept so a reader can say what it skipped.</param>
public sealed record ShaderBlock(
    ShaderBlockKind Kind,
    string Name,
    string Source,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Includes,
    IReadOnlyList<string> Macros,
    IReadOnlyList<ShaderUniform> Uniforms,
    IReadOnlyList<ShaderPort> Ports,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, string> Bodies,
    IReadOnlyList<string> Header)
{
    /// <summary>The block's main code - a fragment's or declarations' body - or empty.</summary>
    public string Body => Bodies.TryGetValue("body", out string? body) ? body : string.Empty;

    /// <summary>Whether a one-word flag is set.</summary>
    public bool Has(string flag) => Flags.Contains(flag, StringComparer.Ordinal);

    /// <summary>A named head value (<c>type</c>, <c>group</c>, <c>defaultstage</c>), or empty.</summary>
    public string Value(string name) => Values.TryGetValue(name, out string? value) ? value : string.Empty;
}

/// <summary>
/// The engine's shader sources - <c>.ffx</c> files - read into their definitions: FRAGMENTs (a node type's code), DECLARATIONS (code and
/// uniforms included by name), EXTENSIONPOINTs (the named values graphs read and write at stages) and GROUPs (loops round grouped nodes).
/// </summary>
/// <remarks>
/// WHY (v0.1.429, DECIDED from the live client, 2026-11-01): the engine's GPU particles are its graphs compiled for the card, and 462 of the
/// node types their graphs use are not ones ShadeProgram evaluates - but 719 of 721 are FRAGMENTs in the install's own sources. So a particle
/// shader is LINKED from those fragments as the engine links it (ParticleLinker), their HLSL taken word for word, rather than ported node by
/// node. This reads the sources; nothing here decides what a fragment means.
///
/// THE GRAMMAR, as every block of the eleven sources particles.txt wrote whole (2026-11-01 16-07-31) spells it: a head line naming the kind
/// and the name at the start of a line; head lines until the code - one-word flags, <c>cost: x</c>, <c>include x</c>, <c>macro x</c>,
/// <c>uniform ...</c>, ports (<c>in</c>, <c>out</c>, <c>inout</c>, a group's <c>loop</c> and <c>prologue out</c>), an extension point's
/// <c>group x</c>, <c>type x</c>, <c>defaultstage x</c>, and preprocessor lines round any of them; then the code between a line <c>{{</c> and a
/// line <c>}}</c>, or a named body (<c>generate: {{ ... }}</c>, <c>read:</c>, <c>write:</c>, a group's <c>prologue:</c> and kin) on one line
/// or several. 1,207 lines open a body and 1,265 close one in those sources: every multi-line body ends at a line that is <c>}}</c> alone.
/// </remarks>
public sealed class ShaderSource
{
    private readonly Dictionary<string, ShaderBlock> _fragments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ShaderBlock> _declarations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ShaderBlock> _extensions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ShaderBlock> _groups = new(StringComparer.Ordinal);

    /// <summary>The fragments by node type.</summary>
    public IReadOnlyDictionary<string, ShaderBlock> Fragments => _fragments;

    /// <summary>The declarations by name.</summary>
    public IReadOnlyDictionary<string, ShaderBlock> Declarations => _declarations;

    /// <summary>The extension points by their full name (<c>ParticlesUpdate::Position</c>).</summary>
    public IReadOnlyDictionary<string, ShaderBlock> Extensions => _extensions;

    /// <summary>The groups by name.</summary>
    public IReadOnlyDictionary<string, ShaderBlock> Groups => _groups;

    /// <summary>The names defined twice, the first kept - the engine's sources define each once, so a second is worth saying.</summary>
    public IList<string> Twice { get; } = [];

    /// <summary>Reads one source's text and adds its definitions.</summary>
    /// <param name="path">The source's path, kept with each definition.</param>
    /// <param name="text">The source's text.</param>
    /// <returns>How many definitions it held.</returns>
    public int Add(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var count = 0;
        var at = 0;
        while (at < lines.Length)
        {
            if (Head(lines[at]) is not { } head)
            {
                at++;
                continue;
            }

            ShaderBlock block = Block(head.Kind, head.Name, path ?? string.Empty, lines, ref at);
            Dictionary<string, ShaderBlock> into = block.Kind switch
            {
                ShaderBlockKind.Fragment => _fragments,
                ShaderBlockKind.Declarations => _declarations,
                ShaderBlockKind.Extension => _extensions,
                _ => _groups,
            };
            if (!into.TryAdd(block.Name, block))
            {
                Twice.Add($"{block.Kind} {block.Name} in {path}");
            }

            count++;
        }

        return count;
    }

    /// <summary>The sources read from texts by path.</summary>
    public static ShaderSource Of(IEnumerable<(string Path, string Text)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var source = new ShaderSource();
        foreach ((string path, string text) in sources)
        {
            source.Add(path, text);
        }

        return source;
    }

    /// <summary>The block a node type names: its fragment, else the extension point an <c>Input</c> node reads or a writer writes.</summary>
    public ShaderBlock? Node(string type)
    {
        if (_fragments.TryGetValue(type, out ShaderBlock? fragment))
        {
            return fragment;
        }

        if (_extensions.TryGetValue(type, out ShaderBlock? written))
        {
            return written;
        }

        return type.StartsWith("Input", StringComparison.Ordinal) && _extensions.TryGetValue(type[5..], out ShaderBlock? read) ? read : null;
    }

    private static (ShaderBlockKind Kind, string Name)? Head(string line)
    {
        if (line.Length == 0 || !char.IsAsciiLetterUpper(line[0]))
        {
            return null;
        }

        int space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0)
        {
            return null;
        }

        ShaderBlockKind? kind = line.AsSpan(0, space) switch
        {
            "FRAGMENT" => ShaderBlockKind.Fragment,
            "DECLARATIONS" => ShaderBlockKind.Declarations,
            "EXTENSIONPOINT" => ShaderBlockKind.Extension,
            "GROUP" => ShaderBlockKind.Group,
            _ => null,
        };
        string name = line[(space + 1)..].Trim();
        int end = name.IndexOfAny([' ', '\t']);
        return kind is { } found && name.Length > 0 ? (found, end < 0 ? name : name[..end]) : null;
    }

    /// <summary>One block from its head line on; <paramref name="at"/> ends on the line after it.</summary>
    private static ShaderBlock Block(ShaderBlockKind kind, string name, string path, string[] lines, ref int at)
    {
        var flags = new List<string>();
        var includes = new List<string>();
        var macros = new List<string>();
        var uniforms = new List<ShaderUniform>();
        var ports = new List<ShaderPort>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var header = new List<string>();
        var conditions = new List<string>();

        at++;
        while (at < lines.Length && Head(lines[at]) is null)
        {
            string line = lines[at];
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                at++;
                continue;
            }

            // THE MAIN BODY: a line {{ alone, to a line }} alone.
            if (trimmed == "{{")
            {
                bodies["body"] = Code(lines, ref at, string.Empty);
                break;
            }

            // A NAMED BODY: generate:, read:, write:, a group's prologue: and kin - on this line or to a line }} alone.
            int named = trimmed.IndexOf(": {{", StringComparison.Ordinal);
            if (named < 0 && trimmed.EndsWith(":{{", StringComparison.Ordinal))
            {
                named = trimmed.Length - 3;
            }

            if (named > 0 && !trimmed.StartsWith("uniform", StringComparison.Ordinal))
            {
                string key = trimmed[..named].Trim();
                string rest = trimmed[(trimmed.IndexOf("{{", named, StringComparison.Ordinal) + 2)..];
                bodies[key] = Code(lines, ref at, rest);
                continue;
            }

            if (trimmed[0] == '#')
            {
                header.Add(trimmed);
                Condition(conditions, trimmed);
                at++;
                continue;
            }

            string condition = string.Join("\n", conditions);
            string[] words = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            switch (words[0])
            {
                case "include" when words.Length > 1:
                    includes.Add(words[1]);
                    break;
                case "macro" when words.Length > 1:
                    macros.Add(words[1]);
                    break;
                case "uniform":
                    uniforms.Add(Uniform(trimmed, condition));
                    break;
                case "cost:" when words.Length > 1:
                    flags.Add("cost:" + words[1]);
                    break;
                case "in" or "out" or "inout" or "loop" when words.Length >= 3:
                    ports.Add(Port(words[0], trimmed[words[0].Length..], condition));
                    break;
                case "prologue" or "epilogue" when words.Length >= 4 && words[1] is "in" or "out" or "inout":
                    ports.Add(Port(words[0] + " " + words[1], trimmed[(words[0].Length + 1 + words[1].Length)..], condition));
                    break;
                default:
                    if (words.Length == 1 && !words[0].Contains('=', StringComparison.Ordinal) && !words[0].Contains(';', StringComparison.Ordinal))
                    {
                        flags.Add(words[0].TrimEnd(':'));
                    }
                    else if (words.Length == 2 && kind is ShaderBlockKind.Extension or ShaderBlockKind.Group or ShaderBlockKind.Fragment
                        && !words[1].Contains(';', StringComparison.Ordinal))
                    {
                        values[words[0].TrimEnd(':')] = words[1];
                    }
                    else
                    {
                        header.Add(trimmed);
                    }

                    break;
            }

            at++;
        }

        return new ShaderBlock(kind, name, path, flags, includes, macros, uniforms, ports, values, bodies, header);
    }

    /// <summary>
    /// The code from <paramref name="rest"/> (what follows a <c>{{</c> on its own line) to the closing <c>}}</c> - on that same line, or a later
    /// line that is <c>}}</c> alone. <paramref name="at"/> ends on the line after the close.
    /// </summary>
    private static string Code(string[] lines, ref int at, string rest)
    {
        int close = rest.LastIndexOf("}}", StringComparison.Ordinal);
        if (close >= 0)
        {
            at++;
            return rest[..close].Trim();
        }

        var code = new StringBuilder();
        if (rest.Trim().Length > 0)
        {
            code.Append(rest.Trim()).Append('\n');
        }

        at++;
        while (at < lines.Length)
        {
            if (lines[at].Trim() == "}}")
            {
                at++;
                break;
            }

            code.Append(lines[at]).Append('\n');
            at++;
        }

        return code.ToString();
    }

    /// <summary>Keeps the stack of preprocessor conditions a head line stands inside.</summary>
    private static void Condition(List<string> conditions, string line)
    {
        if (line.StartsWith("#if", StringComparison.Ordinal))
        {
            conditions.Add(line);
        }
        else if (line.StartsWith("#endif", StringComparison.Ordinal))
        {
            if (conditions.Count > 0)
            {
                conditions.RemoveAt(conditions.Count - 1);
            }
        }
        else if (line.StartsWith("#el", StringComparison.Ordinal) && conditions.Count > 0)
        {
            conditions[^1] = conditions[^1] + "\n" + line;
        }
    }

    /// <summary>A port from what follows its direction: <c>float3 pos : SEMANTIC // comment</c>.</summary>
    private static ShaderPort Port(string direction, string rest, string condition)
    {
        int comment = rest.IndexOf("//", StringComparison.Ordinal);
        if (comment >= 0)
        {
            rest = rest[..comment];
        }

        string semantic = string.Empty;
        int colon = rest.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            semantic = rest[(colon + 1)..].Trim();
            rest = rest[..colon];
        }

        string[] words = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        string type = words.Length > 1 ? string.Join(' ', words[..^1]) : string.Empty;
        string port = words.Length > 0 ? words[^1] : string.Empty;
        return new ShaderPort(direction, type, port, semantic, condition);
    }

    /// <summary>
    /// A uniform line: <c>uniform [scope] [stages] type name ["quoted" ...] [: semantic]</c>. The scope is one of pipeline, object, frame and pass;
    /// the stages a word of <c>cs</c>, <c>vs</c> and <c>ps</c> joined by <c>|</c>.
    /// </summary>
    private static ShaderUniform Uniform(string line, string condition)
    {
        string rest = line["uniform".Length..].Trim();
        var quoted = new List<string>();
        var plain = new StringBuilder();
        var inQuote = false;
        var word = new StringBuilder();
        foreach (char c in rest)
        {
            if (c == '"')
            {
                if (inQuote)
                {
                    quoted.Add(word.ToString());
                    word.Clear();
                }

                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
            {
                word.Append(c);
            }
            else
            {
                plain.Append(c);
            }
        }

        string unquoted = plain.ToString();
        string semantic = string.Empty;
        int colon = unquoted.LastIndexOf(" : ", StringComparison.Ordinal);
        if (colon >= 0)
        {
            semantic = unquoted[(colon + 3)..].Trim();
            unquoted = unquoted[..colon];
        }

        int comment = unquoted.IndexOf("//", StringComparison.Ordinal);
        if (comment >= 0)
        {
            unquoted = unquoted[..comment];
        }

        var words = new List<string>(unquoted.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        string scope = words.Count > 2 && words[0] is "pipeline" or "object" or "frame" or "pass" ? Take(words) : string.Empty;
        string stages = words.Count > 2 && IsStages(words[0]) ? Take(words) : string.Empty;
        string name = words.Count > 0 ? words[^1] : string.Empty;
        string type = words.Count > 1 ? string.Join(' ', words.Take(words.Count - 1)) : string.Empty;
        return new ShaderUniform(scope, stages, type, name, quoted, semantic, condition);
    }

    private static string Take(List<string> words)
    {
        string first = words[0];
        words.RemoveAt(0);
        return first;
    }

    private static bool IsStages(string word)
        => word.Split('|').All(stage => stage is "cs" or "vs" or "ps" or "gs" or "hs" or "ds");
}
