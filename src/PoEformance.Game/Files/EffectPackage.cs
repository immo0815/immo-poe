using System.Text.Json;

namespace PoEformance.Game.Files;

/// <summary>One line - or one block - of an effect package, by the keyword it starts with.</summary>
/// <param name="Kind">The keyword, as written (<c>AttachedObject</c>, <c>ParticleEffect</c>, <c>RenderPasses</c> and the rest).</param>
public abstract record PackageEffect(string Kind);

/// <summary>A keyword alone: <c>ApplyToAllPasses</c> or <c>ParentOnlyEffects</c>.</summary>
public sealed record PackageFlag(string Kind) : PackageEffect(Kind);

/// <summary><c>AttachedObject "point" "x.ao" ["ignore_errors"]</c>: an object hung on a named attachment point.</summary>
public sealed record PackageAttached(string Point, string Ao, bool IgnoreErrors) : PackageEffect("AttachedObject");

/// <summary><c>AttachedObjectBoneIndex n "x.ao"</c>: an object hung on a bone by its index.</summary>
public sealed record PackageAttachedToBone(uint Bone, string Ao) : PackageEffect("AttachedObjectBoneIndex");

/// <summary>
/// <c>AttachedObjectEx ["point"] N "a.ao" ... n n f f [r [r]] ["include_aux"] ["multi_attach"] ["ignore_errors"]</c>.
/// </summary>
/// <param name="Point">The quoted attachment point, or null where the line starts with the count.</param>
/// <param name="Files">The counted <c>.ao</c> or <c>.fmt</c> files.</param>
/// <param name="Numbers">The two whole numbers after them, unnamed (the reference's <c>uints1</c>).</param>
/// <param name="Floats">The two floats after those, unnamed.</param>
/// <param name="Rotations">The up to two signed whole numbers after those (the reference's <c>rotations</c>).</param>
/// <param name="IncludeAux">Whether <c>"include_aux"</c> follows.</param>
/// <param name="MultiAttach">Whether <c>"multi_attach"</c> follows. The reference reads the two words in this order and swaps them as it builds its record; they are kept here by the word.</param>
/// <param name="IgnoreErrors">Whether <c>"ignore_errors"</c> follows.</param>
public sealed record PackageAttachedEx(
    string? Point,
    IReadOnlyList<string> Files,
    (uint First, uint Second) Numbers,
    (float First, float Second) Floats,
    (int? First, int? Second) Rotations,
    bool IncludeAux,
    bool MultiAttach,
    bool IgnoreErrors) : PackageEffect("AttachedObjectEx");

/// <summary>The bones of one side of a <c>ChildAttachedObject</c>: a list with a group index, or one parent bone and its children.</summary>
/// <param name="Names">The quoted list after <c>from_bone</c> (or <c>to_bone</c>) - one name where there are children.</param>
/// <param name="Children">The quoted list after <c>from_child_bone</c>, or null where the line names none.</param>
/// <param name="Index">The group index the line ends with, null for -1.</param>
public sealed record PackageBones(IReadOnlyList<string> Names, IReadOnlyList<string>? Children, uint? Index);

/// <summary><c>ChildAttachedObject "x.ao" from_bone ... [to_bone ...]</c>: an object hung from bones of the parent to bones of its own.</summary>
public sealed record PackageChild(string Ao, PackageBones From, PackageBones? To) : PackageEffect("ChildAttachedObject");

/// <summary>
/// <c>ParticleEffect "glob" "x.pet" ["limit" n n] ["ignore_errors"]</c>, or a <c>TrailEffect</c> naming a <c>.trl</c>.
/// </summary>
/// <param name="Kind">ParticleEffect or TrailEffect.</param>
/// <param name="Glob">The first quoted string - which bones it is laid on, by the reference's name.</param>
/// <param name="File">The <c>.pet</c> or <c>.trl</c>.</param>
/// <param name="Limit">The two numbers after <c>"limit"</c>, or null.</param>
/// <param name="IgnoreErrors">Whether <c>"ignore_errors"</c> follows.</param>
public sealed record PackageEmitter(string Kind, string Glob, string File, (uint First, uint Second)? Limit, bool IgnoreErrors) : PackageEffect(Kind);

/// <summary><c>PlayMiscEffectPackOnBegin "x"</c>, <c>...OnEnd "x"</c> or <c>...AfterDelay "x" f</c>.</summary>
public sealed record PackagePlay(string Kind, string Effect, float? Delay) : PackageEffect(Kind);

/// <summary><c>HideFirstPassAfterDelay f</c> or <c>HideFirstPassAfterDelayForDuration f f</c>.</summary>
public sealed record PackageHideAfter(string Kind, float Delay, float? Duration) : PackageEffect(Kind);

/// <summary><c>HideFirstPassUsingEPKParameter</c>, <c>...TimelineParameter</c> or <c>...DynamicParameter</c>: a parameter and two floats.</summary>
public sealed record PackageHideUsing(string Kind, string Parameter, float Float1, float Float2) : PackageEffect(Kind);

/// <summary>One pass of a <c>RenderPasses</c> block.</summary>
/// <param name="File">The pass's <c>filename</c>.</param>
/// <param name="IsMain">Its <c>is_main</c>.</param>
/// <param name="Type">Its <c>type</c>, or null where it has none.</param>
/// <param name="ApplyOnChildren">Its <c>apply_on_children</c>, or null where it has none.</param>
public sealed record RenderPass(string File, bool IsMain, string? Type, bool? ApplyOnChildren);

/// <summary>A <c>RenderPasses</c> block: <c>RenderPasses { }</c> on one line, or a JSON object of <c>passes</c> over several.</summary>
public sealed record PackagePasses(IReadOnlyList<RenderPass> Passes) : PackageEffect("RenderPasses");

/// <summary>Any other keyword, with the rest of its line as written - the reference's <c>Other</c>.</summary>
public sealed record PackageOther(string Kind, string Rest) : PackageEffect(Kind);

/// <summary>
/// An effect package (<c>.epk</c>): what an effect hangs on its object - attached objects, particles, trails, render
/// passes and the timing of its first pass.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>epk</c> grammar (<c>file_parsers/epk/parser.rs</c>), with its own repairs of the files first:
/// the text trimmed; a closing <c>""</c> made <c>"</c>, where an <c>AttachedObject</c> ends the file; a line break
/// after <c>"&lt;root&gt;"</c> joined to the next line (its <c>"&lt;root&gt;"\s*\n</c> regex, done here by hand);
/// then lines trimmed AT THEIR END ONLY, the blank and <c>//</c> ones dropped - so an indented line stops the file,
/// as there. Each line is an effect by its first word, the rest of the line its arguments; a known keyword whose
/// arguments do not read stops the file rather than falling to <see cref="PackageOther"/>, as the reference's
/// <c>dispatch!</c> does. A <c>RenderPasses</c> line alone opens a block - <c>{</c>, any lines, <c>}</c> - read as
/// JSON once its trailing commas are taken out (<c>remove_trailing</c>); a block that does not read is, again as
/// there, the keyword as <see cref="PackageOther"/> and every line after it an effect of its own.
/// </remarks>
/// <param name="Effects">The effects in the file's order.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record EffectPackage(IReadOnlyList<PackageEffect> Effects, string Why)
{
    /// <summary>What an effect package's file name ends with.</summary>
    public const string Extension = ".epk";

    /// <summary>Nothing read.</summary>
    public static EffectPackage None { get; } = new([], "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static EffectPackage Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static EffectPackage Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        FileLines lines = FileLines.Of(Repaired(text), "//", endOnly: true);
        var effects = new List<PackageEffect>(lines.Count);
        int at = 0;
        while (at < lines.Count)
        {
            if (Passes(lines, ref at, out PackagePasses? passes))
            {
                effects.Add(passes!);
                continue;
            }

            PackageEffect? effect = Effect(lines[at]);
            if (effect is null)
            {
                return new EffectPackage(effects, $"{lines.Said(at)} is not an effect this reads");
            }

            effects.Add(effect);
            at++;
        }

        return new EffectPackage(effects, string.Empty);
    }

    /// <summary>The reference's three repairs before the lines are cut, in its order.</summary>
    private static string Repaired(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.EndsWith("\"\"", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1];
        }

        const string Root = "\"<root>\"";
        int found = trimmed.IndexOf(Root, StringComparison.Ordinal);
        if (found < 0)
        {
            return trimmed;
        }

        var joined = new System.Text.StringBuilder(trimmed.Length);
        int from = 0;
        while (found >= 0)
        {
            int after = found + Root.Length;
            int scan = after;
            int newline = -1;
            while (scan < trimmed.Length && char.IsWhiteSpace(trimmed[scan]))
            {
                if (trimmed[scan] == '\n')
                {
                    newline = scan;
                }

                scan++;
            }

            if (newline < 0)
            {
                _ = joined.Append(trimmed, from, after - from);
                from = after;
            }
            else
            {
                // GREEDY \s* THEN \n: the match runs to the LAST line break of the whitespace, and becomes one space.
                _ = joined.Append(trimmed, from, after - from).Append(' ');
                from = newline + 1;
            }

            found = trimmed.IndexOf(Root, from, StringComparison.Ordinal);
        }

        return joined.Append(trimmed, from, trimmed.Length - from).ToString();
    }

    private static bool Passes(FileLines lines, ref int at, out PackagePasses? passes)
    {
        passes = null;
        var single = new LineWords(lines[at]);
        if (single.Literal("RenderPasses") && single.Space() && single.Literal("{"))
        {
            single.Spaces();
            if (single.Literal("}") && single.End)
            {
                passes = new PackagePasses([]);
                at++;
                return true;
            }
        }

        if (!lines[at].SequenceEqual("RenderPasses") || at + 1 >= lines.Count || !lines[at + 1].SequenceEqual("{"))
        {
            return false;
        }

        int close = at + 2;
        while (close < lines.Count && !lines[close].SequenceEqual("}"))
        {
            close++;
        }

        if (close >= lines.Count)
        {
            return false;
        }

        var block = new System.Text.StringBuilder("{");
        for (int line = at + 2; line < close; line++)
        {
            _ = block.Append(lines[line]);
        }

        List<RenderPass>? read = RenderPasses(WithoutTrailingCommas(block.Append('}').ToString()));
        if (read is null)
        {
            return false;
        }

        passes = new PackagePasses(read);
        at = close + 1;
        return true;
    }

    /// <summary>The reference's <c>remove_trailing</c>: a comma and any whitespace before <c>]</c>, <c>}</c> or <c>)</c> taken out.</summary>
    private static string WithoutTrailingCommas(string json)
    {
        var kept = new System.Text.StringBuilder(json.Length);
        for (int at = 0; at < json.Length; at++)
        {
            if (json[at] == ',')
            {
                int next = at + 1;
                while (next < json.Length && char.IsWhiteSpace(json[next]))
                {
                    next++;
                }

                if (next < json.Length && json[next] is ']' or '}' or ')')
                {
                    at = next - 1;
                    continue;
                }
            }

            _ = kept.Append(json[at]);
        }

        return kept.ToString();
    }

    /// <summary>
    /// The passes as serde reads them into the reference's <c>RenderPasses</c>: an object whose <c>passes</c> is an
    /// array of objects with a string <c>filename</c> and a bool <c>is_main</c>, and optionally a string <c>type</c>
    /// and a bool <c>apply_on_children</c> (null as absent); other members ignored. Null where it is not that.
    /// </summary>
    private static List<RenderPass>? RenderPasses(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("passes", out JsonElement array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var passes = new List<RenderPass>(array.GetArrayLength());
            foreach (JsonElement pass in array.EnumerateArray())
            {
                if (pass.ValueKind != JsonValueKind.Object
                    || !pass.TryGetProperty("filename", out JsonElement file) || file.ValueKind != JsonValueKind.String
                    || !pass.TryGetProperty("is_main", out JsonElement main) || main.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return null;
                }

                string? type = null;
                if (pass.TryGetProperty("type", out JsonElement typed) && typed.ValueKind != JsonValueKind.Null)
                {
                    if (typed.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    type = typed.GetString();
                }

                bool? children = null;
                if (pass.TryGetProperty("apply_on_children", out JsonElement apply) && apply.ValueKind != JsonValueKind.Null)
                {
                    if (apply.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        return null;
                    }

                    children = apply.GetBoolean();
                }

                passes.Add(new RenderPass(file.GetString() ?? string.Empty, main.GetBoolean(), type, children));
            }

            return passes;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PackageEffect? Effect(ReadOnlySpan<char> line)
    {
        var words = new LineWords(line);
        if (!words.Word(out ReadOnlySpan<char> keyword))
        {
            return null;
        }

        words.Spaces();
        string kind = keyword.ToString();
        PackageEffect? effect = kind switch
        {
            "AttachedObject" => Attached(ref words),
            "AttachedObjectEx" => AttachedEx(ref words),
            "AttachedObjectBoneIndex" => words.UInt(out uint bone) && words.Space() && words.QuotedPath(".ao", out string ao)
                ? new PackageAttachedToBone(bone, ao) : null,
            "ChildAttachedObject" => Child(ref words),
            "ParticleEffect" => Emitter(ref words, kind, ".pet"),
            "TrailEffect" => Emitter(ref words, kind, ".trl"),
            "ParentOnlyEffects" or "ApplyToAllPasses" => new PackageFlag(kind),
            "PlayMiscEffectPackOnEnd" or "PlayMiscEffectPackOnBegin" => words.Quoted(out string pack) ? new PackagePlay(kind, pack, null) : null,
            "PlayMiscEffectPackAfterDelay" => words.Quoted(out string pack) && words.Space() && words.Float(out float delay)
                ? new PackagePlay(kind, pack, delay) : null,
            "HideFirstPassAfterDelay" => words.Float(out float delay) ? new PackageHideAfter(kind, delay, null) : null,
            "HideFirstPassAfterDelayForDuration" => words.Float(out float delay) && words.Space() && words.Float(out float duration)
                ? new PackageHideAfter(kind, delay, duration) : null,
            "HideFirstPassUsingEPKParameter" or "HideFirstPassUsingTimelineParameter" or "HideFirstPassUsingDynamicParameter"
                => words.Word(out string parameter) && words.Space() && words.Float(out float float1) && words.Space() && words.Float(out float float2)
                    ? new PackageHideUsing(kind, parameter, float1, float2) : null,
            _ => new PackageOther(kind, words.Rest().ToString()),
        };

        return effect is not null && words.End ? effect : null;
    }

    private static PackageAttached? Attached(ref LineWords words)
    {
        if (!words.Quoted(out string point))
        {
            return null;
        }

        // SPACE0, NOT SPACE1: the reference notes files with no space between the two strings.
        words.Spaces();
        if (!words.QuotedPath(".ao", out string ao))
        {
            return null;
        }

        return new PackageAttached(point, ao, Word(ref words, "\"ignore_errors\""));
    }

    private static PackageAttachedEx? AttachedEx(ref LineWords words)
    {
        string? point = null;
        LineWords mark = words;
        if (words.Quoted(out string named) && words.Space())
        {
            point = named;
        }
        else
        {
            words = mark;
        }

        if (!words.UInt(out uint count) || count > words.Left)
        {
            return null;
        }

        var files = new List<string>((int)count);
        for (uint one = 0; one < count; one++)
        {
            if (!words.Space() || !words.Quoted(out ReadOnlySpan<char> file)
                || !(file.EndsWith(".ao", StringComparison.Ordinal) || file.EndsWith(".fmt", StringComparison.Ordinal)))
            {
                return null;
            }

            files.Add(LineWords.Forward(file));
        }

        if (!words.Space() || !words.UInt(out uint uint1) || !words.Space() || !words.UInt(out uint uint2)
            || !words.Space() || !words.Float(out float float1) || !words.Space() || !words.Float(out float float2))
        {
            return null;
        }

        int? rotation1 = Signed(ref words);
        int? rotation2 = Signed(ref words);
        bool aux = Word(ref words, "\"include_aux\"");
        bool multi = Word(ref words, "\"multi_attach\"");
        bool ignore = Word(ref words, "\"ignore_errors\"");
        return new PackageAttachedEx(point, files, (uint1, uint2), (float1, float2), (rotation1, rotation2), aux, multi, ignore);
    }

    private static PackageChild? Child(ref LineWords words)
    {
        if (!words.QuotedPath(".ao", out string ao) || !words.Space() || Bones(ref words, "from") is not PackageBones from)
        {
            return null;
        }

        LineWords mark = words;
        PackageBones? to = words.Space() ? Bones(ref words, "to") : null;
        if (to is null)
        {
            words = mark;
        }

        return new PackageChild(ao, from, to);
    }

    /// <summary>
    /// <c>from_bone "a, b" from_bone_group_index n</c>, or <c>from_bone "a" from_child_bone "b, c"
    /// from_child_bone_group_index n</c> - the parent's form tried first, as the reference's <c>alt</c>.
    /// </summary>
    private static PackageBones? Bones(ref LineWords words, string side)
    {
        LineWords mark = words;
        if (!words.Literal(side + "_bone") || !words.Space() || !words.QuotedList(out List<string> names))
        {
            words = mark;
            return null;
        }

        LineWords listed = words;
        if (words.Space() && words.Literal(side + "_bone_group_index") && words.Space() && words.NullableUInt(out uint? index))
        {
            return new PackageBones(names, null, index);
        }

        words = listed;
        if (words.Space() && words.Literal(side + "_child_bone") && words.Space() && words.QuotedList(out List<string> children)
            && words.Space() && words.Literal(side + "_child_bone_group_index") && words.Space() && words.NullableUInt(out uint? childIndex)
            && names.Count == 1)
        {
            // THE REFERENCE ASSERTS ONE PARENT where there are children, which would stop it; this fails the line instead.
            return new PackageBones(names, children, childIndex);
        }

        words = mark;
        return null;
    }

    private static PackageEmitter? Emitter(ref LineWords words, string kind, string extension)
    {
        if (!words.Quoted(out string glob) || !words.Space() || !words.QuotedPath(extension, out string file))
        {
            return null;
        }

        (uint, uint)? limit = null;
        LineWords mark = words;
        if (words.Space() && words.Literal("\"limit\"") && words.Space() && words.UInt(out uint first) && words.Space() && words.UInt(out uint second))
        {
            limit = (first, second);
        }
        else
        {
            words = mark;
        }

        return new PackageEmitter(kind, glob, file, limit, Word(ref words, "\"ignore_errors\""));
    }

    /// <summary>An optional space and <paramref name="word"/> (<c>opt(P(space1, literal(...)))</c>).</summary>
    private static bool Word(ref LineWords words, string word)
    {
        LineWords mark = words;
        if (words.Space() && words.Literal(word))
        {
            return true;
        }

        words = mark;
        return false;
    }

    private static int? Signed(ref LineWords words)
    {
        LineWords mark = words;
        if (words.Space() && words.Int(out int value))
        {
            return value;
        }

        words = mark;
        return null;
    }
}
