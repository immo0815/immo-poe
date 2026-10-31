namespace PoEformance.Game.Files;

/// <summary>
/// A ground type (<c>.gt</c>) read whole: its name, the flags under it and the string a few carry after them.
/// </summary>
/// <remarks>
/// <see cref="GroundType"/> reads the name alone, which is all the ground painter needs; this is poe_data_tools'
/// <c>gt</c> grammar (<c>file_parsers/gt/parser.rs</c>) whole: lines trimmed and the blank ones dropped (no
/// comments), the first a single unquoted word, the second two to five noughts or ones, and an optional third a
/// quoted string. The reference names none of the flags (<c>bool1</c> to <c>bool5</c>) or the string
/// (<c>string1</c>), and nor does this. Sulphuric Caverns' <c>black_inside_wall.gt</c> is
/// <c>BlackInsideWall</c> then <c>0 1 0 0</c>.
/// </remarks>
/// <param name="Name">The first line.</param>
/// <param name="Flags">The flags of the second line, two to five, in order.</param>
/// <param name="Extra">The quoted third line, or null where the file has none.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record GroundTypeFile(string Name, IReadOnlyList<bool> Flags, string? Extra, string Why)
{
    /// <summary>What a ground type's file name ends with.</summary>
    public const string Extension = ".gt";

    /// <summary>Nothing read.</summary>
    public static GroundTypeFile None { get; } = new(string.Empty, [], null, "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static GroundTypeFile Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static GroundTypeFile Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        FileLines lines = FileLines.Of(text, comment: null);
        if (lines.Count == 0)
        {
            return None;
        }

        var name = new LineWords(lines[0]);
        if (!name.Word(out string read) || !name.End)
        {
            return new GroundTypeFile(string.Empty, [], null, $"{lines.Said(0)} is not one unquoted name");
        }

        var flags = new List<bool>(5);
        if (lines.Count > 1)
        {
            var words = new LineWords(lines[1]);
            while (flags.Count < 5 && (flags.Count == 0 || words.Space()) && words.Bool(out bool flag))
            {
                flags.Add(flag);
            }

            if (flags.Count < 2 || !words.End)
            {
                return new GroundTypeFile(read, flags, null, $"{lines.Said(1)} is not two to five flags");
            }
        }
        else
        {
            return new GroundTypeFile(read, flags, null, "no line of flags");
        }

        string? extra = null;
        if (lines.Count > 2)
        {
            var words = new LineWords(lines[2]);
            if (!words.Quoted(out string quoted) || !words.End)
            {
                return new GroundTypeFile(read, flags, null, $"{lines.Said(2)} is not a quoted string");
            }

            extra = quoted;
        }

        return new GroundTypeFile(read, flags, extra, lines.Count > 3 ? $"{lines.Said(3)} is past the file's three lines" : string.Empty);
    }
}
