using System.Globalization;
using System.Numerics;
using PoEformance.Game.World;

namespace PoEformance.Game.Files;

/// <summary>
/// A tile's own doodad list (<c>.tmd</c>): the doodads a tile brings with it wherever it is laid, beside the ones its room names.
/// </summary>
/// <remarks>
/// NEITHER REFERENCE READS IT - poe_data_tools has no parser for it and annalithic's poeformats none either - so the
/// grammar is the files' own, read off the 206 lines of Marrow's capture (v0.1.183) and checked line by line:
///
///     version 8
///     x y n [n pairs] z angle qx qy qz qw bool scale "ao" "" "fmt" "type" number
///
/// x and y in the tile's cells, 23 to a tile, as a room's doodad lines count them - negative and past the tile's
/// edge as well; a counted run of float pairs, the first the exact place in world units (as a room line's from version
/// 34: 251.163 for cell 23); z in the area's own frame, up being minus - every TileUpperDoodad sits at -250; the angle
/// in radians with its quaternion after it (half the angle, as the lines bear out); one bool; the scale; then FOUR
/// quoted strings - the .ao (a spawner's "x.fmt;y.ao"), one always empty, the .fmt a spawner draws, and the entity
/// type the game makes of it (DoodadNoBlocking, TileUpperDoodad, WallSeals, ...) - and one number. Laid against a
/// room's line (RoomLayout) the differences are the height before the angle rather than a counted run after the bools,
/// one bool rather than two, and four strings rather than two; a room parser fed one of these refuses it.
///
/// EVERY LINE IS COUNTED BEFORE IT IS TAKEN: eleven numbers before the first quote and two more per pair, four quoted
/// strings after. A line that is not that shape is refused and counted, and the first refusal is said - a field read
/// from the wrong place is the fault this guards against, and a refused line is cheaper than a wrongly placed doodad.
/// </remarks>
public sealed record TileDoodadList(int Version, IReadOnlyList<RoomDoodad> Doodads)
{
    /// <summary>What a doodad list's file name ends with.</summary>
    public const string Extension = ".tmd";

    /// <summary>Nothing read.</summary>
    public static TileDoodadList None { get; } = new(0, []) { Why = "nothing to read" };

    /// <summary>Why the file read short - the first line refused, and how many were - or empty.</summary>
    public string Why { get; init; } = string.Empty;

    /// <summary>The entity type of a tall wall's top - the one type whose lines the game puts down by the way they face.</summary>
    public const string WallTop = "Metadata/MiscellaneousObjects/TileUpperDoodad";

    /// <summary>
    /// Whether the game puts this line down on a tile laid <paramref name="placed"/>: every line but a wall top, and a wall
    /// top only where it faces the camera's side once laid.
    /// </summary>
    /// <remarks>
    /// MEASURED, NOT REASONED (Marrow, v0.1.186, tile-doodads-replay.txt): a wall top's list carries a line for every way the
    /// top may face - one place, two to six lines differing in angle and model, the .fmt named "directional" - and the game
    /// puts down only some. Turned by the tile's placement and read as the entities' own angles are (RotationCurrent: nought
    /// along -y, so the facing is (sin a, -cos a)), the lines put down face 180, 225 or 270 degrees and no others: 214 of
    /// 214 there, 300 of 300 at the other five not, lone lines and groups alike, mirrored tiles and turned. The other way
    /// of reading the angle, (cos a, sin a), splits too, but needs one set of facings for mirrored tiles and another for
    /// turned ones - which is how the convention was told. So a top is put down where its facing has no part along +x or
    /// +y: down the screen, since the map draws both axes upwards - towards the camera, which is to say on a far wall,
    /// where it hides nothing.
    /// </remarks>
    public static bool Shown(RoomDoodad doodad, TileOrientation placed)
    {
        if (!doodad.Stub.Equals(WallTop, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        (int xx, int xy, int yx, int yy) = placed.Turn;
        (float sin, float cos) = MathF.SinCos(doodad.Turn);
        float x = (xx * sin) - (xy * cos), y = (yx * sin) - (yy * cos);
        return x < Leaning && y < Leaning;
    }

    /// <summary>How far a facing may lean along +x or +y and still count as not leaning there - a float's slack, far under any angle the files carry.</summary>
    private const float Leaning = 0.01f;

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static TileDoodadList Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static TileDoodadList Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        var version = 0;
        var doodads = new List<RoomDoodad>();
        var refused = 0;
        string first = string.Empty;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("version", StringComparison.Ordinal))
            {
                _ = int.TryParse(line.AsSpan(7).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out version);
                continue;
            }

            if (Doodad(line, out RoomDoodad doodad, out string why))
            {
                doodads.Add(doodad);
            }
            else
            {
                refused++;
                first = first.Length > 0 ? first : $"{why}: {line}";
            }
        }

        return new TileDoodadList(version, doodads)
        {
            Why = refused > 0 ? string.Create(CultureInfo.InvariantCulture, $"{refused} lines refused, the first {first}") : string.Empty,
        };
    }

    /// <summary>One line, counted before it is taken - see the remarks.</summary>
    private static bool Doodad(string line, out RoomDoodad doodad, out string why)
    {
        doodad = default;
        int quote = line.IndexOf('"', StringComparison.Ordinal);
        if (quote < 0)
        {
            why = "no quoted file";
            return false;
        }

        string[] numbers = line[..quote].Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length < 3 || !Whole(numbers[2], out int pairs) || pairs < 0 || numbers.Length != 11 + (pairs * 2))
        {
            why = string.Create(CultureInfo.InvariantCulture, $"{numbers.Length} numbers before the file, not 11 and two a pair");
            return false;
        }

        var strings = new List<string>(4);
        for (int at = quote; at >= 0 && at < line.Length;)
        {
            int close = line.IndexOf('"', at + 1);
            if (close < 0)
            {
                why = "an unclosed quote";
                return false;
            }

            strings.Add(line[(at + 1)..close]);
            at = line.IndexOf('"', close + 1);
        }

        if (strings.Count != 4)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"{strings.Count} quoted strings, not 4");
            return false;
        }

        // x y n [pairs] z angle qx qy qz qw bool scale - the angle three past the pairs, the scale last.
        int afterPairs = 3 + (pairs * 2);
        float exactX = 0f, exactY = 0f;
        if (!Whole(numbers[0], out int x) || !Whole(numbers[1], out int y)
            || !Real(numbers[afterPairs], out float z) || !Real(numbers[afterPairs + 1], out float angle) || !Real(numbers[^1], out float scale)
            || (pairs > 0 && !(Real(numbers[3], out exactX) && Real(numbers[4], out exactY))))
        {
            why = "a field that is not a number";
            return false;
        }

        Vector2? exact = pairs > 0 ? new Vector2(exactX, exactY) : null;
        doodad = new RoomDoodad(x, y, angle, scale, strings[0].Replace('\\', '/'), strings[3]) { Height = z, Exact = exact };
        why = string.Empty;
        return true;
    }

    private static bool Whole(string word, out int value)
        => int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static bool Real(string word, out float value)
        => float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
