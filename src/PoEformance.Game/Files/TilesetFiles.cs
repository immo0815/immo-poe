using System.Globalization;

namespace PoEformance.Game.Files;

/// <summary>
/// A ground type (<c>.gt</c>): the name a tile's corner is filed under.
/// </summary>
/// <remarks>
/// A NAME AND FLAGS, nothing to draw - <c>Stromatolite</c> then <c>1 1 0 0</c>. The name is the part
/// that matters: it is what a tileset's MaterialsList (<see cref="GroundMaterials"/>) keys its ground
/// materials by, so a tile says WHICH ground lies at a corner and the tileset says what it looks like.
/// poe_data_tools' <c>gt</c> parser reads the first line as the name, unquoted, and leaves the flags
/// unnamed; so does this.
/// </remarks>
public static class GroundType
{
    /// <summary>A ground type's name - its first line - or empty.</summary>
    public static string Name(byte[]? content)
        => content is { Length: > 0 } ? NameOf(StatDescriptionFiles.Decode(content)) : string.Empty;

    /// <summary>The first non-empty line, trimmed. Public so the format can be tested without an install.</summary>
    public static string NameOf(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }
}

/// <summary>One ground material a group offers, with the doodad layers (<c>.dlp</c>) that ride along with it.</summary>
public sealed record GroundChoice(string Material, IReadOnlyList<string> Layers);

/// <summary>
/// One group of a MaterialsList: the ground materials a ground type is drawn with, and their weights.
/// </summary>
/// <param name="Name">The ground type's name as a <c>.gt</c> spells it, or empty for the group with none.</param>
/// <param name="Choices">The materials, in file order.</param>
/// <param name="Weights">One weight per choice; empty where there is a single choice, which the file then omits.</param>
/// <param name="Trailing">The number after the weights - 16 or 32 in the files seen, unnamed in every reference. -1 where there are no weights.</param>
/// <param name="ExtraNumber">The number before the extra choices - unnamed. -1 where there are none.</param>
/// <param name="ExtraFlag">The 0-or-1 beside it - unnamed.</param>
/// <param name="Extras">The extra choices, in file order.</param>
public sealed record GroundGroup(
    string Name,
    IReadOnlyList<GroundChoice> Choices,
    IReadOnlyList<int> Weights,
    int Trailing,
    int ExtraNumber,
    bool ExtraFlag,
    IReadOnlyList<GroundChoice> Extras);

/// <summary>
/// A tileset's MaterialsList (<c>.mtd</c>): which materials each ground type is drawn with.
/// </summary>
/// <remarks>
/// THE FILE THAT SAYS WHAT THE GROUND LOOKS LIKE, and it belongs to the TILESET, not the tile: the
/// same <c>cliffcvm_stroma1</c> is sand in one area and clay in another because two tilesets map the
/// name <c>Stromatolite</c> to different materials. The grammar is poe_data_tools' <c>mtd</c> parser,
/// to the condition:
///
///     version N
///     ["Name"] A B                 a group: an optional ground-type name, then two counts
///         "a.mat" ["x.dlp" ...]    A choices, each with its doodad layers
///         w1 .. wA T               ONLY WHERE A > 1: a weight per choice, then one more number
///         n f                      ONLY WHERE B > 0: a number and a 0-or-1
///         "b.mat" ...              B extra choices
///
/// Comments are <c>//</c> to the end of the line and <c>/* */</c>. A choice's layers are the quoted
/// strings after it that end in <c>.dlp</c> - which is also how a group's last choice ends where the
/// next group's quoted name begins. WHAT THE NUMBERS MEAN beyond "a weight per choice" is not known:
/// the trailing one, the extra pair, and how the game picks among weighted choices are unnamed in
/// poe_data_tools too, and are carried here as read.
/// </remarks>
public sealed class GroundMaterials
{
    /// <summary>What a MaterialsList's file name ends with.</summary>
    public const string Extension = ".mtd";

    /// <summary>Most choices a group may declare before the count is taken for a misread number.</summary>
    private const int MostChoices = 1024;

    /// <summary>Nothing read.</summary>
    public static GroundMaterials None { get; } = new() { Why = "nothing to read" };

    /// <summary>The file's version.</summary>
    public int Version { get; private init; }

    /// <summary>The groups read, in file order - all of them, or those before the point <see cref="Why"/> names.</summary>
    public IReadOnlyList<GroundGroup> Groups { get; private init; } = [];

    /// <summary>Why the file did not read to its end, or empty.</summary>
    public string Why { get; private init; } = string.Empty;

    /// <summary>Whether the whole file read.</summary>
    public bool Ready => Why.Length == 0;

    /// <summary>The group a ground type's name selects, or null. The group with no name is <c>Named("")</c>.</summary>
    public GroundGroup? Named(string name)
    {
        foreach (GroundGroup group in Groups)
        {
            if (string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return group;
            }
        }

        return null;
    }

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static GroundMaterials Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install.</summary>
    public static GroundMaterials Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        List<Token> tokens = Tokens(text);
        if (tokens.Count < 2 || tokens[0].Quoted || tokens[0].Text != "version" || !Whole(tokens[1], out int version))
        {
            return new GroundMaterials { Why = "no version line" };
        }

        var at = 2;
        var groups = new List<GroundGroup>();
        while (at < tokens.Count)
        {
            string? why = Group(tokens, ref at, out GroundGroup? group);
            if (group is not null)
            {
                groups.Add(group);
            }

            if (why is not null)
            {
                return new GroundMaterials
                {
                    Version = version,
                    Groups = groups,
                    Why = string.Create(CultureInfo.InvariantCulture, $"group {groups.Count + 1}: {why}"),
                };
            }
        }

        return new GroundMaterials { Version = version, Groups = groups };
    }

    /// <summary>One group from <paramref name="at"/>, or why it did not read.</summary>
    private static string? Group(List<Token> tokens, ref int at, out GroundGroup? group)
    {
        group = null;
        string name = string.Empty;
        if (tokens[at].Quoted)
        {
            name = tokens[at++].Text;
        }

        if (at + 1 >= tokens.Count || !Whole(tokens[at], out int count) || !Whole(tokens[at + 1], out int extras)
            || count > MostChoices || extras > MostChoices)
        {
            return "no two counts";
        }

        at += 2;
        if (Choices(tokens, ref at, count) is not { } choices)
        {
            return "fewer choices than its count";
        }

        int[] weights = [];
        int trailing = -1;
        if (count > 1)
        {
            weights = new int[count];
            for (var one = 0; one < count; one++)
            {
                if (at >= tokens.Count || !Whole(tokens[at++], out weights[one]))
                {
                    return "fewer weights than choices";
                }
            }

            if (at >= tokens.Count || !Whole(tokens[at++], out trailing))
            {
                return "no number after the weights";
            }
        }

        int number = -1;
        var flag = false;
        if (extras > 0)
        {
            if (at + 1 >= tokens.Count || !Whole(tokens[at], out number) || !Whole(tokens[at + 1], out int bit) || bit is not (0 or 1))
            {
                return "no number and flag before the extra choices";
            }

            flag = bit == 1;
            at += 2;
        }

        if (Choices(tokens, ref at, extras) is not { } more)
        {
            return "fewer extra choices than its count";
        }

        group = new GroundGroup(name, choices, weights, trailing, number, flag, more);
        return null;
    }

    /// <summary>A number of choices, each a <c>.mat</c> and the <c>.dlp</c> layers after it, or null.</summary>
    private static List<GroundChoice>? Choices(List<Token> tokens, ref int at, int count)
    {
        var choices = new List<GroundChoice>(count);
        for (var one = 0; one < count; one++)
        {
            if (at >= tokens.Count || !tokens[at].Quoted || !tokens[at].Text.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string material = tokens[at++].Text.Replace('\\', '/');
            List<string>? layers = null;
            while (at < tokens.Count && tokens[at].Quoted && tokens[at].Text.EndsWith(".dlp", StringComparison.OrdinalIgnoreCase))
            {
                (layers ??= []).Add(tokens[at++].Text.Replace('\\', '/'));
            }

            choices.Add(new GroundChoice(material, layers is null ? [] : layers));
        }

        return choices;
    }

    private static bool Whole(Token token, out int value)
    {
        value = 0;
        return !token.Quoted && int.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>A word or a quoted string; <see cref="Quoted"/> says which, since a name may be a number.</summary>
    private readonly record struct Token(string Text, bool Quoted);

    /// <summary>
    /// The text as words and quoted strings, comments dropped.
    /// </summary>
    /// <remarks>
    /// A QUOTE ENDS A WORD and begins a string, and a closing quote ends one with nothing after it -
    /// the <c>"a.mat""b.dlp"</c> poe_data_tools notes as an edge case of the real files.
    /// </remarks>
    private static List<Token> Tokens(string text)
    {
        var tokens = new List<Token>();
        var at = 0;
        while (at < text.Length)
        {
            char c = text[at];
            if (char.IsWhiteSpace(c))
            {
                at++;
                continue;
            }

            if (c == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                int end = text.IndexOf('\n', at);
                at = end < 0 ? text.Length : end + 1;
                continue;
            }

            if (c == '/' && at + 1 < text.Length && text[at + 1] == '*')
            {
                int end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                at = end < 0 ? text.Length : end + 2;
                continue;
            }

            if (c == '"')
            {
                int close = text.IndexOf('"', at + 1);
                if (close < 0)
                {
                    close = text.Length;
                }

                tokens.Add(new Token(text[(at + 1)..close], Quoted: true));
                at = close + 1;
                continue;
            }

            int start = at;
            while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] != '"')
            {
                at++;
            }

            tokens.Add(new Token(text[start..at], Quoted: false));
        }

        return tokens;
    }
}

/// <summary>One material a tileset swaps for another on every tile it places.</summary>
public readonly record struct MaterialOverride(string From, string To);

/// <summary>
/// A tileset's TileMaterialOverrides (<c>.tmo</c>): materials it replaces on the tiles it places.
/// </summary>
/// <remarks>
/// THE OTHER WAY A TILESET CHANGES A TILE'S LOOK: the hive's list turns this tile's
/// <c>RockyLedgec.mat</c> into <c>prismac.mat</c>. poe_data_tools' <c>tmo</c> grammar: a version line,
/// then a line per override of two quoted <c>.mat</c> paths, <c>//</c> lines skipped and anything
/// after the second path ignored.
/// </remarks>
public static class MaterialOverrides
{
    /// <summary>Every override, in file order; none where the file is missing or says nothing.</summary>
    public static IReadOnlyList<MaterialOverride> Read(byte[]? content)
        => content is not { Length: > 0 } ? [] : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install.</summary>
    public static IReadOnlyList<MaterialOverride> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var found = new List<MaterialOverride>();
        foreach (string line in text.Split('\n'))
        {
            ReadOnlySpan<char> rest = line.AsSpan().Trim();
            if (rest.IsEmpty || rest.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (Quoted(ref rest) is { } from && Quoted(ref rest) is { } to
                && from.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)
                && to.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(new MaterialOverride(from.Replace('\\', '/'), to.Replace('\\', '/')));
            }
        }

        return found;
    }

    /// <summary>The next quoted string in the rest of a line, which is moved past it, or null.</summary>
    private static string? Quoted(ref ReadOnlySpan<char> rest)
    {
        int open = rest.IndexOf('"');
        if (open < 0)
        {
            return null;
        }

        int close = rest[(open + 1)..].IndexOf('"');
        if (close < 0)
        {
            return null;
        }

        string value = rest.Slice(open + 1, close).ToString();
        rest = rest[(open + close + 2)..];
        return value;
    }
}

/// <summary>
/// A tileset (<c>.tsi</c>): the files an area's terrain is built from, one per key.
/// </summary>
/// <remarks>
/// A LINE PER KEY - <c>TileSet "tiles.tst"</c>, <c>MaterialsList "materials.mtd"</c>,
/// <c>TileMaterialOverrides "material_overrides.tmo"</c> - with the value quoted or not. A bare name
/// is a file BESIDE the tileset, which is how annalithic's Tsi.cs resolves them; a value starting at
/// <c>Metadata/</c> or <c>Art/</c> is already whole.
/// </remarks>
public static class TilesetFile
{
    /// <summary>What a tileset's file name ends with.</summary>
    public const string Extension = ".tsi";

    /// <summary>A key's value on its own line, unquoted and with forward slashes, or null.</summary>
    public static string? Value(string? text, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(key, StringComparison.Ordinal)
                && trimmed.Length > key.Length && char.IsWhiteSpace(trimmed[key.Length]))
            {
                return trimmed[key.Length..].Trim().Trim('"').Replace('\\', '/');
            }
        }

        return null;
    }

    /// <summary>A path a file names, resolved: a whole one as it is, a bare name beside the file naming it.</summary>
    public static string Beside(string owner, string named)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(named);
        string value = named.Replace('\\', '/').Trim();
        if (value.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Art/", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        int slash = owner.LastIndexOf('/');
        return slash >= 0 ? owner[..(slash + 1)] + value : value;
    }
}

/// <summary>One line of a tile list: the tile, the weight the generator draws it by, and the words after it.</summary>
/// <param name="Tile">The <c>.tdt</c> as the line spells it.</param>
/// <param name="Weight">The number before the path, or null where the line carries none.</param>
/// <param name="Words">The unquoted words after the path, as written - poe_data_tools calls them rotations and reads no further; what they say is not settled here, and a capture's tileset-lists.txt is where to look.</param>
public sealed record TileListEntry(string Tile, int? Weight, IReadOnlyList<string> Words);

/// <summary>
/// A tileset's tile list (<c>.tst</c>): the tiles it may place, and the other lists it takes in whole.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>tst</c> grammar (<c>file_parsers/tst/parser.rs</c>): lines, <c>//</c> ones skipped;
/// first any number of <c>include "other.tst"</c>, then a line per tile - an optional weight (a space
/// after it optional too), the quoted <c>.tdt</c>, and any number of unquoted words. AN INCLUDED LIST
/// IS PART OF THIS ONE, so a tileset can place a tile its own file never names; reading the lists by
/// searching their text missed exactly those.
/// </remarks>
public sealed record TileList(IReadOnlyList<string> Includes, IReadOnlyList<string> Tiles)
{
    /// <summary>What a tile list's file name ends with.</summary>
    public const string Extension = ".tst";

    /// <summary>Nothing read.</summary>
    public static TileList None { get; } = new([], []);

    /// <summary>Each tile line whole - the weight and the words beside the path. Parallel to <see cref="Tiles"/>.</summary>
    public IReadOnlyList<TileListEntry> Entries { get; init; } = [];

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static TileList Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install.</summary>
    public static TileList Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        var includes = new List<string>();
        var tiles = new List<string>();
        var entries = new List<TileListEntry>();
        foreach (string line in text.Split('\n'))
        {
            ReadOnlySpan<char> rest = line.AsSpan().Trim();
            if (rest.IsEmpty || rest.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            int open = rest.IndexOf('"');
            int close = open >= 0 ? rest[(open + 1)..].IndexOf('"') : -1;
            if (close < 0)
            {
                continue;
            }

            string quoted = rest.Slice(open + 1, close).ToString().Replace('\\', '/');
            if (rest.StartsWith("include", StringComparison.Ordinal))
            {
                if (quoted.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                {
                    includes.Add(quoted);
                }
            }
            else if (quoted.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase))
            {
                tiles.Add(quoted);

                // THE WEIGHT IS WHATEVER DIGITS COME BEFORE THE QUOTE, space or none between - the
                // grammar's own edge case - and the words are what follows the closing quote.
                ReadOnlySpan<char> before = rest[..open].Trim();
                int? weight = before.Length > 0 && int.TryParse(before, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : null;
                string after = rest[(open + close + 2)..].ToString();
                string[] words = after.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                entries.Add(new TileListEntry(quoted, weight, words));
            }
        }

        return new TileList(includes, tiles) { Entries = entries };
    }
}

/// <summary>One line of a fill list: the weight, the <c>.tdt</c> or <c>.arm</c>, and the words after it.</summary>
/// <param name="Section">The name of the section the line sits in.</param>
/// <param name="Weight">The number before the path - not optional here, unlike a tile list's.</param>
/// <param name="Path">The file as the line spells it, slashes forward.</param>
/// <param name="Words">The unquoted words after the path - poe_data_tools calls them rotations, as in a tile list.</param>
public sealed record FillListEntry(string Section, int Weight, string Path, IReadOnlyList<string> Words);

/// <summary>
/// An area's fill list (<c>.gft</c>), the file a master names under <c>FillTiles</c>: named sections of
/// weighted tiles and rooms.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>gft</c> grammar (<c>file_parsers/gft/parser.rs</c>): lines, <c>//</c> ones and blank ones
/// skipped; a <c>version</c> line; in version 1 a count of sections; then each section - a header of a quoted
/// name and an optional number, in version 1 a count of its files, and a line per file: a weight, the quoted
/// <c>.arm</c> or <c>.tdt</c>, and any number of unquoted words. The counts are skipped here rather than
/// trusted: a line that starts with a quote opens a section and a line that starts with a number and quotes a
/// path is a file, whatever the version.
///
/// WHY IT IS READ: every area lays tile files no tile list names - Marrow's mine_abyss_fill tiles sit beside
/// its gapfill rooms and in no list - and a master's FillTiles line is the one other place a master names
/// tiles (RePoE's VaalFactory: <c>FillTiles random_fill_tiles.gft</c>).
/// </remarks>
public sealed record FillList(int Version, IReadOnlyList<FillListEntry> Entries)
{
    /// <summary>What a fill list's file name ends with.</summary>
    public const string Extension = ".gft";

    /// <summary>Nothing read.</summary>
    public static FillList None { get; } = new(0, []);

    /// <summary>The section names in the order the file gives them.</summary>
    public IReadOnlyList<string> Sections { get; init; } = [];

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static FillList Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install.</summary>
    public static FillList Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        var version = 0;
        string section = string.Empty;
        var sections = new List<string>();
        var entries = new List<FillListEntry>();
        foreach (string line in text.Split('\n'))
        {
            ReadOnlySpan<char> rest = line.AsSpan().Trim();
            if (rest.IsEmpty || rest.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (rest.StartsWith("version", StringComparison.Ordinal))
            {
                _ = int.TryParse(rest[7..].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out version);
                continue;
            }

            int open = rest.IndexOf('"');
            int close = open >= 0 ? rest[(open + 1)..].IndexOf('"') : -1;
            if (close < 0)
            {
                // A count of sections or of a section's files.
                continue;
            }

            string quoted = rest.Slice(open + 1, close).ToString().Replace('\\', '/');
            if (open == 0)
            {
                section = quoted;
                sections.Add(section);
                continue;
            }

            if (int.TryParse(rest[..open].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int weight))
            {
                string[] words = rest[(open + close + 2)..].ToString()
                    .Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                entries.Add(new FillListEntry(section, weight, quoted, words));
            }
        }

        return new FillList(version, entries) { Sections = sections };
    }
}
