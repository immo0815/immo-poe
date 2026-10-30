using System.Globalization;

namespace PoEformance.Game.Files;

/// <summary>One doodad a ground type strews: its weight - a number, or "All" - its <c>.ao</c>, and what follows, unnamed.</summary>
/// <param name="Weight">The weight; null where the file writes "All".</param>
/// <param name="Ao">The doodad's <c>.ao</c>.</param>
/// <param name="Rest">What follows on the line, as written - a whole number, a "D", a number, each optional and unnamed.</param>
public sealed record GroundDoodad(float? Weight, string Ao, string Rest);

/// <summary>A group of a doodad list: the ground type it strews over (empty for the first, "Default"), what follows its name, and its doodads.</summary>
public sealed record GroundDoodadGroup(string Name, string Rest, IReadOnlyList<GroundDoodad> Doodads);

/// <summary>
/// A master's doodad list (<c>.ddt</c>, its <c>Doodads</c> key): the doodads strewn over each ground type - The Red Vale's rusted
/// weapons and helms over its Default ground, its clutter over Stream.
/// </summary>
/// <remarks>
/// THE GRAMMAR IS poe_data_tools' <c>ddt</c> PARSER: a version line; a line of a number (named "scale" there) and up to two whole numbers;
/// an optional line of one whole number; the first group under an unquoted name, then groups under quoted ones - a name, an optional
/// "D" and an optional number - each followed by its doodads, a line each. A group with no doodads of its own stands before the next
/// (The Red Vale: "WoodsSlope" then "ShallowWater" and the weapons). WHERE AND HOW MANY THE GAME STREWS IS IN NO FILE - what the numbers
/// mean is read off the files and the game's picture before any rule is given.
/// </remarks>
public sealed class GroundDoodadList
{
    /// <summary>What a doodad list's file name ends with.</summary>
    public const string Extension = ".ddt";

    /// <summary>Nothing read.</summary>
    public static GroundDoodadList None { get; } = new() { Why = "nothing to read" };

    /// <summary>The version line's number.</summary>
    public int Version { get; private init; }

    /// <summary>The line after the version, as written - a number and up to two whole numbers.</summary>
    public string First { get; private init; } = string.Empty;

    /// <summary>The optional line of one whole number after it, as written, or empty.</summary>
    public string Second { get; private init; } = string.Empty;

    /// <summary>The groups, in file order - the first the unquoted one.</summary>
    public IReadOnlyList<GroundDoodadGroup> Groups { get; private init; } = [];

    /// <summary>Why the file did not read to its end, or empty.</summary>
    public string Why { get; private init; } = string.Empty;

    /// <summary>Whether the whole file read.</summary>
    public bool Ready => Why.Length == 0;

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static GroundDoodadList Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static GroundDoodadList Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        var lines = new List<string>();
        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            {
                lines.Add(line);
            }
        }

        if (lines.Count < 2 || !lines[0].StartsWith("version", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(lines[0].AsSpan(7).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int version))
        {
            return new GroundDoodadList { Why = "no version line" };
        }

        string first = lines[1];
        var at = 2;
        string second = string.Empty;
        if (at < lines.Count && int.TryParse(lines[at], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            second = lines[at];
            at++;
        }

        var groups = new List<GroundDoodadGroup>();
        string? name = null;
        string rest = string.Empty;
        var doodads = new List<GroundDoodad>();
        for (; at < lines.Count; at++)
        {
            string line = lines[at];
            if (Doodad(line) is { } doodad)
            {
                if (name is null)
                {
                    return new GroundDoodadList { Version = version, First = first, Second = second, Groups = groups, Why = "a doodad before any group" };
                }

                doodads.Add(doodad);
                continue;
            }

            // A GROUP'S NAME: the first unquoted, the rest quoted; what follows it kept as written.
            if (name is not null)
            {
                groups.Add(new GroundDoodadGroup(name, rest, doodads));
                doodads = [];
            }

            (name, rest) = Named(line);
        }

        if (name is not null)
        {
            groups.Add(new GroundDoodadGroup(name, rest, doodads));
        }

        return new GroundDoodadList { Version = version, First = first, Second = second, Groups = groups };
    }

    /// <summary>A group's name and what follows it.</summary>
    private static (string Name, string After) Named(string line)
    {
        if (line.StartsWith('"'))
        {
            int close = line.IndexOf('"', 1);
            return close < 0 ? (line.Trim('"'), string.Empty) : (line[1..close], line[(close + 1)..].Trim());
        }

        int space = line.IndexOfAny([' ', '\t']);
        return space < 0 ? (line, string.Empty) : (line[..space], line[space..].Trim());
    }

    /// <summary>A doodad's line - <c>weight "x.ao" ...</c>, the weight a number or "All" - or null.</summary>
    private static GroundDoodad? Doodad(string line)
    {
        int quote = line.IndexOf('"');
        if (quote <= 0)
        {
            return null;
        }

        string weight = line[..quote].Trim();
        int close = line.IndexOf('"', quote + 1);
        if (close < 0)
        {
            return null;
        }

        string ao = line[(quote + 1)..close].Replace('\\', '/');
        if (!ao.EndsWith(".ao", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (weight == "All")
        {
            return new GroundDoodad(null, ao, line[(close + 1)..].Trim());
        }

        return float.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            ? new GroundDoodad(number, ao, line[(close + 1)..].Trim())
            : null;
    }
}
