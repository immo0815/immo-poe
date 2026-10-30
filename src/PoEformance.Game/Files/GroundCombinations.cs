namespace PoEformance.Game.Files;

/// <summary>One line of a ground combination file: three ground types, slashes forward.</summary>
public sealed record GroundCombination(string First, string Second, string Third);

/// <summary>
/// A ground combination file (<c>.gcf</c>): lines of three ground types each.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>gcf</c> grammar (<c>file_parsers/gcf/parser.rs</c>): lines trimmed, the blank and <c>//</c>
/// ones dropped, a <c>version</c> line, then a line per combination - exactly three quoted <c>.gt</c> paths
/// separated by spaces, nothing else. The reference names none of the three (<c>gt_files</c>). Read beside the
/// edge combinations (<see cref="EdgeCombinations"/>), whose first two make the third where two rooms meet; whether
/// a ground combination is the same rule for corners is not measured.
/// </remarks>
/// <param name="Version">Off the version line.</param>
/// <param name="Lines">The combinations in the file's order.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record GroundCombinations(int Version, IReadOnlyList<GroundCombination> Lines, string Why)
{
    /// <summary>What a ground combination file's name ends with.</summary>
    public const string Extension = ".gcf";

    /// <summary>Nothing read.</summary>
    public static GroundCombinations None { get; } = new(0, [], "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static GroundCombinations Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static GroundCombinations Parse(string? text)
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

        var combinations = new List<GroundCombination>(lines.Count - 1);
        var grounds = new string[3];
        for (; at < lines.Count; at++)
        {
            var words = new LineWords(lines[at]);
            int read = 0;
            while (read < 3 && (read == 0 || words.Space()) && words.QuotedPath(GroundTypeFile.Extension, out grounds[read]))
            {
                read++;
            }

            if (read < 3 || !words.End)
            {
                return new GroundCombinations(version, combinations, $"{lines.Said(at)} is not three quoted ground types");
            }

            combinations.Add(new GroundCombination(grounds[0], grounds[1], grounds[2]));
        }

        return new GroundCombinations(version, combinations, string.Empty);
    }
}
