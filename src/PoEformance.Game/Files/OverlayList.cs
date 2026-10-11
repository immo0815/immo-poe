using System.Globalization;

namespace PoEformance.Game.Files;

/// <summary>One line of an overlay list: a room the generator may lay over the layout, and what the line says of it.</summary>
/// <param name="Group">Which group of the file the line sits in, from nought.</param>
/// <param name="Weight">The number before the path.</param>
/// <param name="Room">The <c>.arm</c> as the line spells it, slashes forward.</param>
/// <param name="Words">Everything after the path, as written: <c>+Flag</c> and <c>-Flag</c>, <c>!flag</c>, <c>key=value</c>, and the eight ways round (<c>I</c>, <c>R90</c> .. <c>FR270</c>).</param>
public sealed record OverlayEntry(int Group, int Weight, string Room, IReadOnlyList<string> Words)
{
    /// <summary>Whether the line carries a word, compared without case - <c>+SuppressUnderlyingDoodads</c>, <c>!door_boss</c>, <c>limit=1</c>.</summary>
    public bool Says(string word)
    {
        foreach (string one in Words)
        {
            if (string.Equals(one, word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// An area's overlay list (<c>.toy</c>), the file a master names under <c>Overlays</c>: the rooms laid over the layout once it is made.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>toy</c> grammar (<c>file_parsers/toy/parser.rs</c>): lines, blank and <c>//</c> ones
/// skipped; a <c>version</c> line; then groups, each a header - two bools, an optional <c>FileOrder</c> or
/// <c>SizeOrder</c>, any number of <c>+Flag</c> or <c>-Flag</c> - and its entries: a weight, the quoted
/// <c>.arm</c>, and in any order <c>!flag</c>, <c>+Flag</c> or <c>-Flag</c>, <c>key=value</c> and the eight
/// ways round, which the parser reads as a turn and a flip (its <c>d4_rotation</c>). A line without a
/// quote is a header; the words are kept as written rather than sorted into those kinds, since none of
/// them is read for its meaning yet.
///
/// WHY IT IS READ: THE OVERLAY ROOMS ARE IN NO ROOM SET. Augury's two (<c>boss_entrance_blank</c>,
/// <c>ledge_intersection_1</c>) and Marrow's three are in no <c>generate.rs</c>, and the loaded-file lists
/// carried both of Augury's and one of Marrow's: the generator lays them in a pass of their own after the
/// layout. GGG's procedural-generation talk
/// describes that pass as pattern matching - after the tiles are laid, a second pass looks for known
/// shapes and replaces them with more elaborate geometry - which is why a re-entered instance, whose
/// loaded-file list carries none of them, needs this road to know them at all (AreaRoomSet).
/// </remarks>
public sealed record OverlayList(int Version, IReadOnlyList<OverlayEntry> Entries)
{
    /// <summary>What an overlay list's file name ends with.</summary>
    public const string Extension = ".toy";

    /// <summary>Nothing read.</summary>
    public static OverlayList None { get; } = new(0, []);

    /// <summary>Each group's header line, as written - the two bools and what follows them.</summary>
    public IReadOnlyList<string> Headers { get; init; } = [];

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static OverlayList Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static OverlayList Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        var version = 0;
        var headers = new List<string>();
        var entries = new List<OverlayEntry>();
        foreach (string raw in text.Split('\n'))
        {
            ReadOnlySpan<char> rest = raw.AsSpan().Trim();
            if (rest.IsEmpty || rest.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (rest.StartsWith("version", StringComparison.Ordinal))
            {
                _ = int.TryParse(rest[7..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out version);
                continue;
            }

            int open = rest.IndexOf('"');
            int close = open >= 0 ? rest[(open + 1)..].IndexOf('"') : -1;
            if (open < 0)
            {
                headers.Add(rest.ToString());
                continue;
            }

            if (close < 0 || headers.Count == 0
                || !int.TryParse(rest[..open].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int weight))
            {
                continue;
            }

            string room = rest.Slice(open + 1, close).ToString().Replace('\\', '/');
            string[] words = rest[(open + close + 2)..].ToString()
                .Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            entries.Add(new OverlayEntry(headers.Count - 1, weight, room, words));
        }

        return new OverlayList(version, entries) { Headers = headers };
    }
}
