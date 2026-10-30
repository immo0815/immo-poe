namespace PoEformance.Game.Files;

/// <summary>The numbers a chest group's header carries: two floats and two or three whole numbers, all unnamed.</summary>
/// <param name="Uint3">The third whole number, which a file of version 3 and up writes; null before.</param>
public sealed record ChestNumbers(float Float1, float Float2, uint Uint1, uint Uint2, uint? Uint3);

/// <summary>One line of a chest group: a weight and the chest types it is shared among.</summary>
public sealed record ChestEntry(uint Weight, IReadOnlyList<string> Chests);

/// <summary>One group of a chest table: the areas it is for, its numbers, and its weighted chests.</summary>
/// <param name="Areas">The areas the header names; <c>Default</c> alone for the file's first group, which names none.</param>
/// <param name="Numbers">The header's numbers, or null where a named header carries none or a lone float.</param>
/// <param name="Lone">The lone float a named header may carry in their place (the reference's <c>NumLine::V2</c>), or null.</param>
/// <param name="Entries">The weighted chest lines, in order.</param>
public sealed record ChestGroup(IReadOnlyList<string> Areas, ChestNumbers? Numbers, float? Lone, IReadOnlyList<ChestEntry> Entries);

/// <summary>
/// An area's chest table (<c>.cht</c>), the file a master names under <c>ChestData</c>.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>cht</c> grammar (<c>file_parsers/cht/parser.rs</c>): lines trimmed, the blank ones dropped and
/// - unlike the other master's tables - those starting with <c>#</c>, not <c>//</c>; a <c>version</c> line; then
/// the default group, whose header is its numbers alone (<c>0.40 0.60 5 70</c>, Augury), and any number of named
/// groups, whose header is a quoted list of areas and optionally the numbers or one float; each group's lines a
/// weight and a quoted list of chest types joined by <c>", "</c> (<c>10 "VaalStoneChest01, VaalStoneChest02"</c>).
/// The numbers' third whole number comes in with version 3 (<c>0.3 0.2 0 15 20</c>, the Titan Grotto). None of
/// the numbers is named in the reference, and nothing here reads them for their meaning.
/// </remarks>
/// <param name="Version">Off the version line.</param>
/// <param name="Groups">The default group first, then the named ones.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record ChestTable(int Version, IReadOnlyList<ChestGroup> Groups, string Why)
{
    /// <summary>What a chest table's file name ends with.</summary>
    public const string Extension = ".cht";

    /// <summary>Nothing read.</summary>
    public static ChestTable None { get; } = new(0, [], "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static ChestTable Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static ChestTable Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        FileLines lines = FileLines.Of(text, "#");
        int at = 0;
        if (lines.Version(ref at) is not int version)
        {
            return None with { Why = $"{lines.Said(0)} is not a version line" };
        }

        var groups = new List<ChestGroup>();
        var header = new LineWords(at < lines.Count ? lines[at] : default);
        if (!Numbers(ref header, version, out ChestNumbers? numbers) || !header.End)
        {
            return new ChestTable(version, groups, $"{lines.Said(at)} is not the default group's numbers");
        }

        at++;
        groups.Add(new ChestGroup(["Default"], numbers, null, Entries(lines, ref at)));

        while (at < lines.Count)
        {
            var named = new LineWords(lines[at]);
            if (!named.QuotedList(out List<string> areas))
            {
                return new ChestTable(version, groups, $"{lines.Said(at)} is neither a chest line nor a group's header");
            }

            ChestNumbers? own = null;
            float? lone = null;
            LineWords mark = named;
            if (named.Space())
            {
                LineWords before = named;
                if (!Numbers(ref named, version, out own) || !named.End)
                {
                    named = before;
                    own = null;
                    if (named.Float(out float single))
                    {
                        lone = single;
                    }
                    else
                    {
                        named = mark;
                    }
                }
            }

            if (!named.End)
            {
                return new ChestTable(version, groups, $"{lines.Said(at)} is not a quoted list of areas and its numbers");
            }

            at++;
            groups.Add(new ChestGroup(areas, own, lone, Entries(lines, ref at)));
        }

        return new ChestTable(version, groups, string.Empty);
    }

    private static List<ChestEntry> Entries(FileLines lines, ref int at)
    {
        var entries = new List<ChestEntry>();
        for (; at < lines.Count; at++)
        {
            var words = new LineWords(lines[at]);
            if (!words.UInt(out uint weight) || !words.Space() || !words.QuotedList(out List<string> chests) || !words.End)
            {
                break;
            }

            entries.Add(new ChestEntry(weight, chests));
        }

        return entries;
    }

    private static bool Numbers(ref LineWords words, int version, out ChestNumbers? numbers)
    {
        numbers = null;
        if (!words.Float(out float float1) || !words.Space() || !words.Float(out float float2)
            || !words.Space() || !words.UInt(out uint uint1) || !words.Space() || !words.UInt(out uint uint2))
        {
            return false;
        }

        uint? uint3 = null;
        if (version >= 3)
        {
            if (!words.Space() || !words.UInt(out uint third))
            {
                return false;
            }

            uint3 = third;
        }

        numbers = new ChestNumbers(float1, float2, uint1, uint2, uint3);
        return true;
    }
}
