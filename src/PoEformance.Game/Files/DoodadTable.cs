namespace PoEformance.Game.Files;

/// <summary>One doodad line of a doodad table: a weight (or <c>All</c>), the <c>.ao</c>, and what may follow it, unnamed.</summary>
/// <param name="Weight">The weight, or null where the line says <c>All</c>.</param>
/// <param name="Ao">The quoted <c>.ao</c>, slashes forward.</param>
/// <param name="Uint1">The whole number after it, or null.</param>
/// <param name="D">Whether a <c>D</c> follows.</param>
/// <param name="Float1">The float the line may end with, or null.</param>
public sealed record DoodadTableLine(float? Weight, string Ao, uint? Uint1, bool D, float? Float1)
{
    /// <summary>Whether the line's weight is <c>All</c>.</summary>
    public bool All => Weight is null;
}

/// <summary>One group of a doodad table: its name (<c>Default</c> for the first), what its header carries after it, and its doodads.</summary>
/// <param name="Name">The group's name, unquoted for the first group and quoted for the rest.</param>
/// <param name="D">The unquoted word a header may carry after the name, or null.</param>
/// <param name="Float1">The float a header may end with, or null.</param>
/// <param name="Lines">The doodad lines, in order.</param>
public sealed record DoodadTableGroup(string Name, string? D, float? Float1, IReadOnlyList<DoodadTableLine> Lines);

/// <summary>
/// An area's doodad table (<c>.ddt</c>), the file a master names under <c>Doodads</c>: the props the generator
/// may scatter, by group.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>ddt</c> grammar (<c>file_parsers/ddt/parser.rs</c>): lines trimmed, the blank and <c>//</c>
/// ones dropped; a <c>version</c> line; a line of a float and up to two whole numbers (<c>0.25</c>, Marrow;
/// <c>1 1</c>, Sulphuric Caverns); optionally a line of one whole number; the default group - an unquoted name
/// (<c>Default</c>) - and any number of quoted ones (<c>"BalbalaArena"</c>, Traitor's Passage), each header
/// optionally followed by an unquoted word and a float; a doodad line a float weight or <c>All</c>, the quoted
/// <c>.ao</c>, and optionally a whole number that is not the front of a float, a <c>D</c> and a float
/// (<c>4 "Metadata/Terrain/Doodads/Desert/Badlands/SmallMiscBones01.ao"</c>). The reference calls the first float
/// <c>scale</c> and names nothing else.
/// </remarks>
/// <param name="Version">Off the version line.</param>
/// <param name="Scale">The first float of the line after the version.</param>
/// <param name="ScaleNumbers">The up to two whole numbers after it.</param>
/// <param name="Uint1">The optional line of one whole number, or null.</param>
/// <param name="Groups">The default group first, then the named ones.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record DoodadTable(int Version, float Scale, IReadOnlyList<uint> ScaleNumbers, uint? Uint1, IReadOnlyList<DoodadTableGroup> Groups, string Why)
{
    /// <summary>What a doodad table's file name ends with.</summary>
    public const string Extension = ".ddt";

    /// <summary>Nothing read.</summary>
    public static DoodadTable None { get; } = new(0, 0, [], null, [], "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static DoodadTable Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static DoodadTable Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        FileLines lines = FileLines.Of(text, "//");
        int at = 0;
        if (lines.Version(ref at) is not int version)
        {
            return None with { Why = $"{lines.Said(0)} is not a version line" };
        }

        var first = new LineWords(at < lines.Count ? lines[at] : default);
        if (!first.Float(out float scale))
        {
            return new DoodadTable(version, 0, [], null, [], $"{lines.Said(at)} does not start with a float");
        }

        var numbers = new List<uint>(2);
        while (numbers.Count < 2)
        {
            LineWords mark = first;
            if (first.Space() && first.UInt(out uint number))
            {
                numbers.Add(number);
                continue;
            }

            first = mark;
            break;
        }

        if (!first.End)
        {
            return new DoodadTable(version, scale, numbers, null, [], $"{lines.Said(at)} is not a float and up to two whole numbers");
        }

        at++;
        uint? uint1 = null;
        if (at < lines.Count)
        {
            var lone = new LineWords(lines[at]);
            if (lone.UInt(out uint value) && lone.End)
            {
                uint1 = value;
                at++;
            }
        }

        var groups = new List<DoodadTableGroup>();
        while (at < lines.Count && Header(lines[at], quoted: groups.Count > 0, out string name, out string? d, out float? float1))
        {
            at++;
            var doodads = new List<DoodadTableLine>();
            for (; at < lines.Count && Doodad(lines[at], out DoodadTableLine? line); at++)
            {
                doodads.Add(line!);
            }

            groups.Add(new DoodadTableGroup(name, d, float1, doodads));
        }

        string why = groups.Count == 0 ? $"{lines.Said(at)} is not the default group's header"
            : at < lines.Count ? $"{lines.Said(at)} is neither a doodad line nor a quoted group's header"
            : string.Empty;
        return new DoodadTable(version, scale, numbers, uint1, groups, why);
    }

    private static bool Header(ReadOnlySpan<char> line, bool quoted, out string name, out string? d, out float? float1)
    {
        d = null;
        float1 = null;
        var words = new LineWords(line);
        if (quoted ? !words.Quoted(out name) : !words.Word(out name))
        {
            return false;
        }

        LineWords mark = words;
        if (words.Space() && words.Word(out string word))
        {
            d = word;
        }
        else
        {
            words = mark;
        }

        mark = words;
        if (words.Space() && words.Float(out float value))
        {
            float1 = value;
        }
        else
        {
            words = mark;
        }

        return words.End;
    }

    private static bool Doodad(ReadOnlySpan<char> text, out DoodadTableLine? line)
    {
        line = null;
        var words = new LineWords(text);
        float? weight = null;
        if (!words.Literal("All"))
        {
            if (!words.Float(out float value))
            {
                return false;
            }

            weight = value;
        }

        if (!words.Space() || !words.QuotedPath(".ao", out string ao))
        {
            return false;
        }

        uint? uint1 = null;
        LineWords mark = words;
        if (words.Space() && words.SafeUInt(out uint number))
        {
            uint1 = number;
        }
        else
        {
            words = mark;
        }

        bool d = false;
        mark = words;
        if (words.Space() && words.Literal("D"))
        {
            d = true;
        }
        else
        {
            words = mark;
        }

        float? float1 = null;
        mark = words;
        if (words.Space() && words.Float(out float last))
        {
            float1 = last;
        }
        else
        {
            words = mark;
        }

        if (!words.End)
        {
            return false;
        }

        line = new DoodadTableLine(weight, ao, uint1, d, float1);
        return true;
    }
}
