using System.Globalization;

namespace PoEformance.Game.Files;

/// <summary>One line of an edge combination file: two edge types, what they make, and the number after them.</summary>
/// <param name="First">The first edge type (<c>.et</c>) as the line spells it, slashes forward - empty for <c>""</c>.</param>
/// <param name="Second">The second.</param>
/// <param name="Result">The third - what the two make where they meet, if the reading below holds; empty for <c>""</c>.</param>
/// <param name="Number">The number after the three, or null where the line carries none. poe_data_tools reads it as an unnamed <c>uint1</c>.</param>
public sealed record EdgeCombination(string First, string Second, string Result, int? Number);

/// <summary>
/// An area's edge combinations (<c>.ecf</c>), the file a master names under <c>EdgeCombination</c>.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>ecf</c> grammar (<c>file_parsers/ecf/parser.rs</c>): lines, blank and <c>//</c> ones
/// skipped; a <c>version</c> line; then a line per combination - three quoted <c>.et</c> paths, any of them
/// <c>""</c>, and an optional whole number. The parser names none of the four.
///
/// WHAT THE THREE MEAN IS A READING, NOT A MEASUREMENT. GGG's procedural-generation talk (Rhys Abraham,
/// ExileCon) says rooms are laid overlapping by one tile along every boundary, and that in the overlap the
/// two rooms' tile keys are merged by rules - "thick wall + thick wall = thin wall", "wall + wall = no
/// wall". Every master of the committed captures carries <c>RoomOverlap 1</c> and names one of these
/// files, and Augury's first line is exactly the talk's second example: dungeon_thick_wall twice, then
/// <c>""</c>. So the reading taken here is "the first and the second meeting make the third". Whether the
/// game applies it so - and what the number does: Sulphuric Caverns writes coral_thickwall twice making
/// <c>""</c> with 0 and coral_ledge with 1 - is what CaptureReplayTests.JoinsReplay measures at the joins
/// of placed rooms; nothing builds on it before that says so.
/// </remarks>
public sealed record EdgeCombinations(int Version, IReadOnlyList<EdgeCombination> Lines)
{
    /// <summary>What an edge combination file's name ends with.</summary>
    public const string Extension = ".ecf";

    /// <summary>Nothing read.</summary>
    public static EdgeCombinations None { get; } = new(0, []);

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static EdgeCombinations Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static EdgeCombinations Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        var version = 0;
        var lines = new List<EdgeCombination>();
        var quoted = new List<string>(3);
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

            // THREE QUOTED PATHS, an empty one among them as written, then what is left.
            quoted.Clear();
            while (quoted.Count < 3 && rest.Length > 0 && rest[0] == '"')
            {
                int close = rest[1..].IndexOf('"');
                if (close < 0)
                {
                    break;
                }

                quoted.Add(rest.Slice(1, close).ToString().Replace('\\', '/').Trim());
                rest = rest[(close + 2)..].TrimStart();
            }

            if (quoted.Count != 3)
            {
                continue;
            }

            int? number = rest.Length > 0 && int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
            lines.Add(new EdgeCombination(quoted[0], quoted[1], quoted[2], number));
        }

        return new EdgeCombinations(version, lines);
    }
}
