using System.Text;

namespace PoEformance.Game.Files;

/// <summary>
/// What the engine's sources say of the threads of a compute shader's group, read once per install: which names SYNC the group (the DX11
/// prelude's barriers, and every function or macro of the declarations reaching one - ParticleGroupBroadcast), and which hold a THREAD'S OWN
/// value (every static that is not const and every group-shared array, and every function or macro reading one); and, for a fragment's body,
/// which names one of its outputs is computed from.
/// </summary>
/// <remarks>
/// WHY (v0.1.454): FXC refuses a group sync inside flow control that varies by thread (X3663, cs_5_0, the profile the tool compiles the
/// particle update with on Windows), while DXC compiles it - so a linked loop group whose body syncs and whose count differs between the
/// threads of a group links and compiles offline and fails only on the card. ParticleLinker says so where it can show it (its loop groups).
///
/// READ FROM THE CAPTURE OF 2026-11-01 23:05 (particles.txt's sources): every BroadcastParticleGroup* fragment calls ParticleGroupBroadcast
/// (DECLARATIONS gpu_particles_group_compute), which calls MEMORY_BARRIER_GROUP(); the DX11 prelude defines MEMORY_BARRIER_GROUP, _ALL and
/// _DEVICE as the *WithGroupSync intrinsics and MEMORY_BARRIER_SUB_GROUP as GroupMemoryBarrier, a fence that syncs no thread (no X3663). The
/// sources' statics that are not const are per-thread state (g_thread_enabled, set from SV_DispatchThreadID by ReadParticleUpdateId;
/// g_cur_data; thread_enabled), every other one a table (bayer_matrix4...); GROUPSHARED arrays are written thread by thread.
///
/// SCANNED, NOT PARSED: comments and preprocessor lines are dropped (both branches of an #if kept - the union, which can only say more), a
/// function is a name and a parameter list followed by a body at the top level of a declarations block, and an output's names follow its
/// assignments through the body's own locals - every condition of the body (if, for, while, switch) counted for every output. An out argument
/// of a call is taken to depend on every name of the call.
/// </remarks>
internal sealed class ShaderThreads
{
    /// <summary>The prelude's barriers that sync the threads of a group - READ in its DX11 text, the macros and the intrinsics they name.</summary>
    public static readonly IReadOnlyList<string> Barriers =
    [
        "MEMORY_BARRIER_GROUP", "MEMORY_BARRIER_ALL", "MEMORY_BARRIER_DEVICE",
        "GroupMemoryBarrierWithGroupSync", "AllMemoryBarrierWithGroupSync", "DeviceMemoryBarrierWithGroupSync",
    ];

    /// <summary>The words that open a statement's control header (its condition in parentheses after it).</summary>
    private static readonly HashSet<string> Controls = new(StringComparer.Ordinal) { "if", "for", "while", "switch" };

    /// <summary>The assignment operators.</summary>
    private static readonly HashSet<string> Assignments = new(StringComparer.Ordinal) { "=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=" };

    /// <summary>The operators of more than one character, longest first.</summary>
    private static readonly string[] Operators = ["<<=", ">>=", "==", "!=", "<=", ">=", "&&", "||", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "::"];

    private ShaderThreads(Dictionary<string, string> syncs, Dictionary<string, string> own)
    {
        Syncs = syncs;
        Own = own;
    }

    /// <summary>Every name whose use syncs the group, with the barrier it comes to (itself for a barrier).</summary>
    public IReadOnlyDictionary<string, string> Syncs { get; }

    /// <summary>Every name of the declarations holding or reading a thread's own value, with what it is - "the static g_thread_enabled".</summary>
    public IReadOnlyDictionary<string, string> Own { get; }

    /// <summary>Reads every declarations block of the sources.</summary>
    public static ShaderThreads Of(ShaderSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var uses = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var own = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ShaderBlock block in source.Declarations.Values)
        {
            foreach (string line in block.Header)
            {
                Macro(line, uses);
            }

            foreach (string line in Defines(block.Body))
            {
                Macro(line, uses);
            }

            List<Token> tokens = Tokens(block.Body);
            Functions(tokens, uses);
            Storage(tokens, own);
        }

        var syncs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string barrier in Barriers)
        {
            syncs[barrier] = barrier;
        }

        Spread(uses, syncs, (_, _, barrier) => barrier);
        Spread(uses, own, (name, _, what) => $"{name}, which reads {what}");
        return new ShaderThreads(syncs, own);
    }

    /// <summary>The first name a piece of code uses that syncs the group, and the barrier it comes to; null where none does.</summary>
    public (string Name, string Barrier)? SyncIn(string code)
    {
        foreach (Token token in Tokens(code))
        {
            if (token.Name && !token.Member && Syncs.TryGetValue(token.Text, out string? barrier))
            {
                return (token.Text, barrier);
            }
        }

        return null;
    }

    /// <summary>
    /// The names a fragment's body computes one of its outputs from: what every assignment to it reads, through the body's own locals and
    /// their assignments in turn, and every condition of the body - the output itself among them.
    /// </summary>
    /// <param name="body">The fragment's code.</param>
    /// <param name="output">The output port's name.</param>
    public static HashSet<string> ComputedFrom(string body, string output)
    {
        List<Token> tokens = Tokens(body);
        var reads = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var conditions = new HashSet<string>(StringComparer.Ordinal);
        var start = 0;
        var depth = 0;
        for (var at = 0; at < tokens.Count; at++)
        {
            string text = tokens[at].Text;
            if (text is "(" or "[")
            {
                depth++;
            }
            else if (text is ")" or "]")
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && text is ";" or "{" or "}")
            {
                Statement(tokens, start, at, reads, conditions);
                start = at + 1;
            }
        }

        Statement(tokens, start, tokens.Count, reads, conditions);

        var from = new HashSet<string>(StringComparer.Ordinal) { output };
        var waiting = new Queue<string>([output, .. conditions]);
        from.UnionWith(conditions);
        while (waiting.TryDequeue(out string? name))
        {
            foreach (string read in reads.GetValueOrDefault(name) ?? [])
            {
                if (from.Add(read))
                {
                    waiting.Enqueue(read);
                }
            }
        }

        return from;
    }

    /// <summary>One statement's reads: its control headers' names as conditions, then its assignment's target reading its right side.</summary>
    private static void Statement(List<Token> tokens, int start, int end, Dictionary<string, HashSet<string>> reads, HashSet<string> conditions)
    {
        // A CONTROL HEADER - if (c), for (...; c; ...), while (c), switch (c) - and else and do before what they govern.
        while (start < end && tokens[start].Name && (Controls.Contains(tokens[start].Text) || tokens[start].Text is "else" or "do"))
        {
            if (!Controls.Contains(tokens[start].Text) || start + 1 >= end || tokens[start + 1].Text != "(")
            {
                start++;
                continue;
            }

            int close = Closing(tokens, start + 1, end);
            for (int at = start + 2; at < close; at++)
            {
                if (tokens[at].Name && !tokens[at].Member)
                {
                    conditions.Add(tokens[at].Text);
                }
            }

            start = Math.Min(end, close + 1);
        }

        if (start >= end)
        {
            return;
        }

        int assign = -1;
        var depth = 0;
        for (int at = start; at < end && assign < 0; at++)
        {
            string text = tokens[at].Text;
            if (text is "(" or "[")
            {
                depth++;
            }
            else if (text is ")" or "]")
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && !tokens[at].Name && Assignments.Contains(text))
            {
                assign = at;
            }
        }

        if (assign < 0)
        {
            // NO ASSIGNMENT - a call, a declaration, an increment: every name it uses may be written from every other (an out argument).
            var named = new List<string>();
            for (int at = start; at < end; at++)
            {
                if (tokens[at].Name && !tokens[at].Member)
                {
                    named.Add(tokens[at].Text);
                }
            }

            foreach (string name in named)
            {
                Into(reads, name).UnionWith(named);
            }

            return;
        }

        // THE TARGET: the last name of the left side outside brackets that is no member (float3 foo, foo.xy, foo[i]); what is inside its
        // brackets and the whole right side are what it reads, and itself where the operator reads it too (+=).
        string? target = null;
        var reading = new List<string>();
        depth = 0;
        for (int at = start; at < assign; at++)
        {
            Token token = tokens[at];
            if (token.Text == "[")
            {
                depth++;
            }
            else if (token.Text == "]")
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (token.Name && !token.Member)
            {
                if (depth == 0)
                {
                    target = token.Text;
                }
                else
                {
                    reading.Add(token.Text);
                }
            }
        }

        if (target is null)
        {
            return;
        }

        for (int at = assign + 1; at < end; at++)
        {
            if (tokens[at].Name && !tokens[at].Member)
            {
                reading.Add(tokens[at].Text);
            }
        }

        if (tokens[assign].Text != "=")
        {
            reading.Add(target);
        }

        Into(reads, target).UnionWith(reading);
    }

    /// <summary>The token closing the parenthesis opened at <paramref name="open"/>, or <paramref name="end"/> where none does.</summary>
    private static int Closing(List<Token> tokens, int open, int end)
    {
        var depth = 0;
        for (int at = open; at < end; at++)
        {
            if (tokens[at].Text == "(")
            {
                depth++;
            }
            else if (tokens[at].Text == ")" && --depth == 0)
            {
                return at;
            }
        }

        return end;
    }

    private static HashSet<string> Into(Dictionary<string, HashSet<string>> map, string name)
    {
        if (!map.TryGetValue(name, out HashSet<string>? set))
        {
            map[name] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        return set;
    }

    /// <summary>Every name reaching one the map holds is added with what <paramref name="say"/> makes of the name it reached, until none is.</summary>
    private static void Spread(Dictionary<string, HashSet<string>> uses, Dictionary<string, string> held, Func<string, string, string, string> say)
    {
        bool grew;
        do
        {
            grew = false;
            foreach ((string name, HashSet<string> used) in uses)
            {
                if (held.ContainsKey(name))
                {
                    continue;
                }

                foreach (string one in used)
                {
                    if (one != name && held.TryGetValue(one, out string? what))
                    {
                        held[name] = say(name, one, what);
                        grew = true;
                        break;
                    }
                }
            }
        }
        while (grew);
    }

    /// <summary>A <c>#define NAME(args) code</c> line's name and the names its code uses.</summary>
    private static void Macro(string line, Dictionary<string, HashSet<string>> uses)
    {
        string trimmed = line.Trim();
        if (!trimmed.StartsWith("#define", StringComparison.Ordinal))
        {
            return;
        }

        List<Token> tokens = Tokens(trimmed["#define".Length..]);
        if (tokens.Count == 0 || !tokens[0].Name)
        {
            return;
        }

        HashSet<string> used = Into(uses, tokens[0].Text);
        for (var at = 1; at < tokens.Count; at++)
        {
            if (tokens[at].Name && !tokens[at].Member)
            {
                used.Add(tokens[at].Text);
            }
        }
    }

    /// <summary>A code's <c>#define</c> lines, each with its continued lines (<c>\</c>) joined.</summary>
    private static IEnumerable<string> Defines(string code)
    {
        string[] lines = code.Split('\n');
        for (var at = 0; at < lines.Length; at++)
        {
            if (!lines[at].TrimStart().StartsWith("#define", StringComparison.Ordinal))
            {
                continue;
            }

            var line = new StringBuilder(lines[at].TrimEnd());
            while (line.Length > 0 && line[^1] == '\\' && at + 1 < lines.Length)
            {
                line.Length--;
                line.Append(' ').Append(lines[++at].TrimEnd());
            }

            yield return line.ToString();
        }
    }

    /// <summary>Every function defined at the top level of a declarations block, with the names its body uses.</summary>
    private static void Functions(List<Token> tokens, Dictionary<string, HashSet<string>> uses)
    {
        var depth = 0;
        for (var at = 0; at < tokens.Count; at++)
        {
            string text = tokens[at].Text;
            if (text == "{")
            {
                depth++;
                continue;
            }

            if (text == "}")
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth > 0 || !tokens[at].Name || at + 1 >= tokens.Count || tokens[at + 1].Text != "(")
            {
                continue;
            }

            // A NAME, ITS PARAMETERS, AN OPTIONAL RETURN SEMANTIC, THEN ITS BODY - a call or an attribute has no body after it.
            int close = Closing(tokens, at + 1, tokens.Count);
            int body = close + 1;
            if (body + 1 < tokens.Count && tokens[body].Text == ":" && tokens[body + 1].Name)
            {
                body += 2;
            }

            if (body >= tokens.Count || tokens[body].Text != "{")
            {
                continue;
            }

            HashSet<string> used = Into(uses, text);
            var inside = 0;
            int end = body;
            for (; end < tokens.Count; end++)
            {
                if (tokens[end].Text == "{")
                {
                    inside++;
                }
                else if (tokens[end].Text == "}" && --inside == 0)
                {
                    break;
                }
                else if (tokens[end].Name && !tokens[end].Member)
                {
                    used.Add(tokens[end].Text);
                }
            }

            at = end;
        }
    }

    /// <summary>
    /// The top-level statics that are not const and the group-shared arrays of a declarations block - storage each thread writes for itself.
    /// </summary>
    private static void Storage(List<Token> tokens, Dictionary<string, string> own)
    {
        var depth = 0;
        for (var at = 0; at < tokens.Count; at++)
        {
            string text = tokens[at].Text;
            if (text == "{")
            {
                depth++;
                continue;
            }

            if (text == "}")
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            string? kind = text switch
            {
                "static" when at + 1 < tokens.Count && tokens[at + 1].Text != "const" => "the static",
                "GROUPSHARED" or "groupshared" => "the group-shared",
                _ => null,
            };
            if (depth > 0 || kind is null || !tokens[at].Name)
            {
                continue;
            }

            // EACH DECLARATOR UP TO ITS = OR [, the declaration to its semicolon; a parenthesis first makes it a function, which is no storage.
            var name = string.Empty;
            var open = 0;
            var declarator = true;
            int end = at + 1;
            for (; end < tokens.Count && !(open == 0 && tokens[end].Text == ";"); end++)
            {
                string one = tokens[end].Text;
                if (open == 0 && declarator && one == "(")
                {
                    name = string.Empty;
                    break;
                }

                if (one is "(" or "[" or "{")
                {
                    open++;
                    declarator = false;
                }
                else if (one is ")" or "]" or "}")
                {
                    open = Math.Max(0, open - 1);
                }
                else if (open == 0 && one == "=")
                {
                    declarator = false;
                }
                else if (open == 0 && one == ",")
                {
                    Add(own, name, kind);
                    name = string.Empty;
                    declarator = true;
                }
                else if (open == 0 && declarator && tokens[end].Name)
                {
                    name = one;
                }
            }

            Add(own, name, kind);
            at = end;
        }

        static void Add(Dictionary<string, string> own, string name, string kind)
        {
            if (name.Length > 0)
            {
                own.TryAdd(name, $"{kind} {name}");
            }
        }
    }

    /// <summary>One word or mark of a piece of code: a name (and whether it follows a dot, a member), a number, an operator.</summary>
    internal readonly record struct Token(string Text, bool Name, bool Member);

    /// <summary>
    /// A piece of code as its words and marks, without its comments, its preprocessor lines and its literals' insides - a number stands as
    /// <c>0</c>, a string as <c>""</c>.
    /// </summary>
    internal static List<Token> Tokens(string code)
    {
        var tokens = new List<Token>();
        var at = 0;
        var lineStart = true;
        while (at < code.Length)
        {
            char c = code[at];
            if (c == '\n')
            {
                lineStart = true;
                at++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                at++;
                continue;
            }

            if (c == '#' && lineStart)
            {
                // A PREPROCESSOR LINE, with its continuations.
                while (at < code.Length && code[at] != '\n')
                {
                    int next = at + 1;
                    if (code[at] == '\\')
                    {
                        next += next < code.Length && code[next] == '\r' ? 1 : 0;
                        next += next < code.Length && code[next] == '\n' ? 1 : 0;
                    }

                    at = next;
                }

                continue;
            }

            lineStart = false;
            if (c == '/' && at + 1 < code.Length && code[at + 1] == '/')
            {
                while (at < code.Length && code[at] != '\n')
                {
                    at++;
                }

                continue;
            }

            if (c == '/' && at + 1 < code.Length && code[at + 1] == '*')
            {
                int close = code.IndexOf("*/", at + 2, StringComparison.Ordinal);
                at = close < 0 ? code.Length : close + 2;
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = at;
                while (at < code.Length && (char.IsAsciiLetterOrDigit(code[at]) || code[at] == '_'))
                {
                    at++;
                }

                bool member = tokens.Count > 0 && tokens[^1].Text == ".";
                tokens.Add(new Token(code[start..at], true, member));
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && at + 1 < code.Length && char.IsAsciiDigit(code[at + 1])))
            {
                while (at < code.Length && (char.IsAsciiLetterOrDigit(code[at]) || code[at] == '.' || code[at] == '_'
                    || (code[at] is '+' or '-' && code[at - 1] is 'e' or 'E')))
                {
                    at++;
                }

                tokens.Add(new Token("0", false, false));
                continue;
            }

            if (c == '"')
            {
                int close = code.IndexOf('"', at + 1);
                at = close < 0 ? code.Length : close + 1;
                tokens.Add(new Token("\"\"", false, false));
                continue;
            }

            string? mark = null;
            foreach (string op in Operators)
            {
                if (string.CompareOrdinal(code, at, op, 0, op.Length) == 0)
                {
                    mark = op;
                    break;
                }
            }

            mark ??= c.ToString();
            tokens.Add(new Token(mark, false, false));
            at += mark.Length;
        }

        return tokens;
    }
}
