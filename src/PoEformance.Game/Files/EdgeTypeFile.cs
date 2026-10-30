namespace PoEformance.Game.Files;

/// <summary>The line of numbers an edge type may carry after its two grounds: two whole numbers and one to four flags, all unnamed.</summary>
public sealed record EdgeTypeNumbers(uint Uint1, uint Uint2, bool Bool1, bool? Bool2, bool? Bool3, bool? Bool4);

/// <summary>One of the two edge types a virtual edge type names, with the flag after it.</summary>
public readonly record struct EdgeTypeVirtual(string Edge, bool Flag);

/// <summary>
/// An edge type (<c>.et</c>): the name a tile's or a slot's side is filed under, the two grounds it lies between,
/// and what some carry after them.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>et</c> grammar (<c>file_parsers/et/parser.rs</c>): lines trimmed, the blank ones dropped (no
/// comments), then
/// <code>
///     name [#hex]                 one unquoted word, and optionally # and hex digits
///     ground                      twice, each "a.gt", a.gt or wildcard, quoted or not
///     ground
///     n n b [b [b [b]]]           optional - two whole numbers, one to four flags
///     other.gt                    optional - one more ground, unquoted
///     virtual                     optional - the word, then
///     a.et b                      twice, an edge type and a flag
///     n n                         two whole numbers
/// </code>
/// The reference names the numbers <c>uint1</c>, <c>uint2</c> and <c>bool1</c> to <c>bool4</c>, the last ground
/// <c>gt_file2</c> and the virtual section's two numbers <c>virtual_rotations</c>; nothing here reads any of them
/// for its meaning yet. A tile's <see cref="TileDefinition.Edges"/> and a room's k slot name these files.
/// </remarks>
/// <param name="Name">The first line's word.</param>
/// <param name="Hex">The hex digits after a <c>#</c> on the first line, or null.</param>
/// <param name="Grounds">The two grounds, slashes forward; <see cref="Wildcard"/> where the file says so.</param>
/// <param name="Numbers">The optional line of numbers, or null.</param>
/// <param name="OtherGround">The optional ground after them, or null.</param>
/// <param name="Virtual">The two edge types of a virtual section, or empty where there is none.</param>
/// <param name="Rotations">The virtual section's two numbers, or null.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record EdgeTypeFile(
    string Name,
    string? Hex,
    IReadOnlyList<string> Grounds,
    EdgeTypeNumbers? Numbers,
    string? OtherGround,
    IReadOnlyList<EdgeTypeVirtual> Virtual,
    (uint First, uint Second)? Rotations,
    string Why)
{
    /// <summary>What an edge type's file name ends with.</summary>
    public const string Extension = ".et";

    /// <summary>A ground the file leaves open, as it writes it.</summary>
    public const string Wildcard = "wildcard";

    /// <summary>Nothing read.</summary>
    public static EdgeTypeFile None { get; } = new(string.Empty, null, [], null, null, [], null, "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static EdgeTypeFile Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static EdgeTypeFile Parse(string? text)
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

        var first = new LineWords(lines[0]);
        if (!first.Word(out string name))
        {
            return None with { Why = $"{lines.Said(0)} has no name" };
        }

        string? hex = null;
        LineWords mark = first;
        if (first.Space() && first.Literal("#") && first.Word(out ReadOnlySpan<char> digits) && IsHex(digits))
        {
            hex = digits.ToString();
        }
        else
        {
            first = mark;
        }

        var result = new EdgeTypeFile(name, hex, [], null, null, [], null, string.Empty);
        if (!first.End)
        {
            return result with { Why = $"{lines.Said(0)} is not a name and an optional #hex" };
        }

        var grounds = new string[2];
        for (int one = 0; one < 2; one++)
        {
            if (one + 1 >= lines.Count || !Ground(lines[one + 1], out grounds[one]))
            {
                return result with { Why = $"{lines.Said(one + 1)} is not a ground (a .gt or {Wildcard})" };
            }
        }

        result = result with { Grounds = grounds };
        int at = 3;

        if (at < lines.Count && NumberLine(lines[at], out EdgeTypeNumbers? numbers))
        {
            result = result with { Numbers = numbers };
            at++;
        }

        if (at < lines.Count)
        {
            var other = new LineWords(lines[at]);
            if (other.Word(out ReadOnlySpan<char> path) && other.End && path.EndsWith(GroundTypeFile.Extension, StringComparison.Ordinal))
            {
                result = result with { OtherGround = LineWords.Forward(path) };
                at++;
            }
        }

        if (at < lines.Count && lines[at].SequenceEqual("virtual"))
        {
            var edges = new EdgeTypeVirtual[2];
            for (int one = 0; one < 2; one++)
            {
                if (at + 1 + one >= lines.Count || !VirtualEdge(lines[at + 1 + one], out edges[one]))
                {
                    return result with { Why = $"{lines.Said(at + 1 + one)} is not an edge type and a flag" };
                }
            }

            var turns = new LineWords(at + 3 < lines.Count ? lines[at + 3] : default);
            if (!turns.UInt(out uint turn1) || !turns.Space() || !turns.UInt(out uint turn2) || !turns.End)
            {
                return result with { Why = $"{lines.Said(at + 3)} is not two whole numbers" };
            }

            result = result with { Virtual = edges, Rotations = (turn1, turn2) };
            at += 4;
        }

        return at < lines.Count ? result with { Why = $"{lines.Said(at)} is not what an edge type carries there" } : result;
    }

    private static bool Ground(ReadOnlySpan<char> line, out string ground)
    {
        var words = new LineWords(line);
        if (!words.Quoted(out ReadOnlySpan<char> inside))
        {
            words = new LineWords(line);
            if (!words.Word(out inside))
            {
                ground = string.Empty;
                return false;
            }
        }

        // AND_THEN IN THE REFERENCE: "wildcard" is matched at the front of the slice, a path by its ending.
        ground = inside.StartsWith(Wildcard, StringComparison.Ordinal) ? Wildcard
            : inside.EndsWith(GroundTypeFile.Extension, StringComparison.Ordinal) ? LineWords.Forward(inside)
            : string.Empty;
        return words.End && ground.Length > 0;
    }

    private static bool NumberLine(ReadOnlySpan<char> line, out EdgeTypeNumbers? numbers)
    {
        numbers = null;
        var words = new LineWords(line);
        if (!words.UInt(out uint uint1) || !words.Space() || !words.UInt(out uint uint2) || !words.Space() || !words.Bool(out bool bool1))
        {
            return false;
        }

        bool? bool2 = Flag(ref words);
        bool? bool3 = bool2 is null ? null : Flag(ref words);
        bool? bool4 = bool3 is null ? null : Flag(ref words);
        if (!words.End)
        {
            return false;
        }

        numbers = new EdgeTypeNumbers(uint1, uint2, bool1, bool2, bool3, bool4);
        return true;
    }

    private static bool? Flag(ref LineWords words)
    {
        LineWords mark = words;
        if (words.Space() && words.Bool(out bool flag))
        {
            return flag;
        }

        words = mark;
        return null;
    }

    private static bool VirtualEdge(ReadOnlySpan<char> line, out EdgeTypeVirtual edge)
    {
        edge = default;
        var words = new LineWords(line);
        if (!words.Word(out ReadOnlySpan<char> path) || !path.EndsWith(Extension, StringComparison.Ordinal)
            || !words.Space() || !words.Bool(out bool flag) || !words.End)
        {
            return false;
        }

        edge = new EdgeTypeVirtual(LineWords.Forward(path), flag);
        return true;
    }

    private static bool IsHex(ReadOnlySpan<char> digits)
    {
        foreach (char one in digits)
        {
            if (!char.IsAsciiHexDigit(one))
            {
                return false;
            }
        }

        return !digits.IsEmpty;
    }
}
