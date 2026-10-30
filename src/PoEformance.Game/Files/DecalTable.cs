namespace PoEformance.Game.Files;

/// <summary>One decal line: a weight, the decal's <c>.atlas</c>, a tag, and one or two floats, unnamed.</summary>
public sealed record DecalEntry(uint Weight, string Atlas, string Tag, float Float1, float? Float2);

/// <summary>One group of a decal table: the area it is for (<c>Default</c> for the first), an optional float, and its decals.</summary>
public sealed record DecalGroup(string Area, float? Float, IReadOnlyList<DecalEntry> Entries);

/// <summary>
/// An area's decal table (<c>.dct</c>), the file a master names under <c>Decals</c>.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>dct</c> grammar (<c>file_parsers/dct/parser.rs</c>): lines trimmed, the blank and <c>//</c>
/// ones dropped; a <c>version</c> line; a line of one float; the default group - a line <c>Default</c> and its
/// decals - and any number of named groups, a quoted area and an optional float; a decal line a weight, the
/// quoted <c>.atlas</c>, a quoted tag and one or two floats. Marrow's and Traitor's Passage's are
/// <c>version 2</c>, <c>0.6</c>, <c>Default</c> and nothing more. The reference names the floats <c>float1</c>,
/// <c>float2</c> and the file's own <c>float</c>.
/// </remarks>
/// <param name="Version">Off the version line.</param>
/// <param name="Float">The float on the line after the version.</param>
/// <param name="Groups">The default group first, then the named ones.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record DecalTable(int Version, float Float, IReadOnlyList<DecalGroup> Groups, string Why)
{
    /// <summary>What a decal table's file name ends with.</summary>
    public const string Extension = ".dct";

    /// <summary>Nothing read.</summary>
    public static DecalTable None { get; } = new(0, 0, [], "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static DecalTable Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static DecalTable Parse(string? text)
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
        if (!single.Float(out float value) || !single.End)
        {
            return new DecalTable(version, 0, [], $"{lines.Said(at)} is not one float");
        }

        at++;
        if (at >= lines.Count || !lines[at].SequenceEqual("Default"))
        {
            return new DecalTable(version, value, [], $"{lines.Said(at)} is not the Default group");
        }

        at++;
        var groups = new List<DecalGroup> { new("Default", null, Entries(lines, ref at)) };
        while (at < lines.Count)
        {
            var header = new LineWords(lines[at]);
            if (!header.Quoted(out string area))
            {
                break;
            }

            float? own = null;
            LineWords mark = header;
            if (header.Space() && header.Float(out float number))
            {
                own = number;
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
            groups.Add(new DecalGroup(area, own, Entries(lines, ref at)));
        }

        return new DecalTable(version, value, groups, at < lines.Count ? $"{lines.Said(at)} is neither a decal line nor a group's header" : string.Empty);
    }

    private static List<DecalEntry> Entries(FileLines lines, ref int at)
    {
        var entries = new List<DecalEntry>();
        for (; at < lines.Count; at++)
        {
            var words = new LineWords(lines[at]);
            if (!words.UInt(out uint weight) || !words.Space() || !words.QuotedPath(".atlas", out string atlas)
                || !words.Space() || !words.Quoted(out string tag) || !words.Space() || !words.Float(out float float1))
            {
                break;
            }

            float? float2 = null;
            LineWords mark = words;
            if (words.Space() && words.Float(out float second))
            {
                float2 = second;
            }
            else
            {
                words = mark;
            }

            if (!words.End)
            {
                break;
            }

            entries.Add(new DecalEntry(weight, atlas, tag, float1, float2));
        }

        return entries;
    }
}
