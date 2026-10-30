namespace PoEformance.Game.Files;

/// <summary>One critter line: a number, the critter's metadata path, its <c>.ao</c>, and the numbers after them, all unnamed.</summary>
/// <param name="Uint1">The number the line starts with.</param>
/// <param name="Stub">The quoted metadata path (<c>Metadata/Critters/Bat/Vampire</c>).</param>
/// <param name="Ao">The quoted <c>.ao</c>, which a file of version 4 and up writes; null before.</param>
/// <param name="Float1">The first float.</param>
/// <param name="Float2">The second, from version 3; null before.</param>
/// <param name="Uint2">The first whole number after the floats.</param>
/// <param name="Uint3">The second.</param>
public sealed record CritterLine(uint Uint1, string Stub, string? Ao, float Float1, float? Float2, uint Uint2, uint Uint3);

/// <summary>One group of a critter table: a name, an optional float, and its critter lines.</summary>
public sealed record CritterGroup(string Name, float? Float, IReadOnlyList<CritterLine> Lines);

/// <summary>
/// An area's critter table (<c>.clt</c>), the file a master names under <c>Critters</c>.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>clt</c> grammar (<c>file_parsers/clt/parser.rs</c>): lines trimmed, the blank and <c>//</c>
/// ones dropped; a <c>version</c> line; a line of one float; then one or more groups - a header, its name quoted
/// or not (<c>Default</c>) and an optional float, and its lines: a whole number, the quoted metadata path, from
/// version 4 the quoted <c>.ao</c>, a float, from version 3 a second, and two whole numbers
/// (<c>1 "Metadata/Critters/Bat/Vampire" "Metadata/Critters/Bat/Vampire.ao" 5.0 20.0 2 6</c>, Sulphuric Caverns',
/// written there behind <c>//</c>). The reference names none of the numbers.
/// </remarks>
/// <param name="Version">Off the version line.</param>
/// <param name="Float1">The float on the line after the version.</param>
/// <param name="Groups">The groups in the file's order.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record CritterTable(int Version, float Float1, IReadOnlyList<CritterGroup> Groups, string Why)
{
    /// <summary>What a critter table's file name ends with.</summary>
    public const string Extension = ".clt";

    /// <summary>Nothing read.</summary>
    public static CritterTable None { get; } = new(0, 0, [], "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static CritterTable Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static CritterTable Parse(string? text)
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

        var single = new LineWords(at < lines.Count ? lines[at] : default);
        if (!single.Float(out float float1) || !single.End)
        {
            return new CritterTable(version, 0, [], $"{lines.Said(at)} is not one float");
        }

        at++;
        var groups = new List<CritterGroup>();
        while (at < lines.Count)
        {
            var header = new LineWords(lines[at]);
            if (!header.Quoted(out string name) && !header.Word(out name))
            {
                break;
            }

            float? own = null;
            LineWords mark = header;
            if (header.Space() && header.Float(out float value))
            {
                own = value;
            }
            else
            {
                header = mark;
            }

            if (!header.End)
            {
                break;
            }

            at++;
            var critters = new List<CritterLine>();
            for (; at < lines.Count && Line(lines[at], version, out CritterLine? line); at++)
            {
                critters.Add(line!);
            }

            groups.Add(new CritterGroup(name, own, critters));
        }

        string why = groups.Count == 0 ? $"{lines.Said(at)} is not a group's header"
            : at < lines.Count ? $"{lines.Said(at)} is neither a critter line nor a group's header"
            : string.Empty;
        return new CritterTable(version, float1, groups, why);
    }

    private static bool Line(ReadOnlySpan<char> text, int version, out CritterLine? line)
    {
        line = null;
        var words = new LineWords(text);
        if (!words.UInt(out uint uint1) || !words.Space() || !words.Quoted(out string stub))
        {
            return false;
        }

        string? ao = null;
        if (version >= 4)
        {
            if (!words.Space() || !words.QuotedPath(".ao", out string path))
            {
                return false;
            }

            ao = path;
        }

        if (!words.Space() || !words.Float(out float float1))
        {
            return false;
        }

        float? float2 = null;
        if (version >= 3)
        {
            if (!words.Space() || !words.Float(out float second))
            {
                return false;
            }

            float2 = second;
        }

        if (!words.Space() || !words.UInt(out uint uint2) || !words.Space() || !words.UInt(out uint uint3) || !words.End)
        {
            return false;
        }

        line = new CritterLine(uint1, stub, ao, float1, float2, uint2, uint3);
        return true;
    }
}
