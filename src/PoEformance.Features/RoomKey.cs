using System.Globalization;
using System.Runtime.CompilerServices;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// The key the tile book loads a room under: its path, then how many doodads it may place and whether the editor's tools are among them.
/// </summary>
/// <remarks>
/// THE TILE'S ARRANGEMENT - see <see cref="TileKey"/> - for the choices a room has: changing one is a
/// different key, so the portrait reloads without knowing about rooms. The usual choices write the
/// bare path, which keeps every key written before the choices existed meaning what it meant.
/// </remarks>
/// <param name="Path">The room's <c>.arm</c>.</param>
/// <param name="Doodads">Most doodads placed - see <see cref="RoomModels.UsualDoodads"/>.</param>
/// <param name="Tools">Whether the level editor's tools are placed too - see <see cref="RoomModels.IsTool"/>.</param>
/// <param name="Laid">Where the room is drawn as the area laid it - "x,y,turn,area" - or empty for the room as its file has it; see LaidRoomModels.</param>
/// <param name="Fitted">Whether a laid room's pieces are fitted to the area's ground rather than set at their tiles' own levels, the usual from v0.1.315 - see LaidRoomModels. An older key's word for the levels, "level", is the usual now and reads as it.</param>
/// <param name="Heights">How high a doodad that carries a height in its line is set - see <see cref="DoodadHeight"/>.</param>
/// <param name="Hidden">Whether a laid room keeps what the game's camera cannot see too - see LaidRoomModels.Hiding. Off, the usual, leaves it out.</param>
/// <param name="Cut">How far under the area's ground a laid room leaves everything out, in world units; nought, the usual, leaves nothing out.</param>
/// <param name="Built">The tileset the room is built from without the area - a tile list or the master naming it - or empty; see BuiltRoomModels. A key names a place or a tileset, not both: the place wins where both are written.</param>
/// <param name="Mirrored">Whether a room built from the tileset is laid turned over in x, as the game lays it in some instances - see BuiltRoomModels.</param>
/// <param name="MaskCells">Whether a laid or built room's ground keeps the shapes across its mask's atlas on the mask's cells, squares and all, rather than mixed by their sub-tiles' corners - see GroundPainter.</param>
/// <param name="GroundHeights">Whether a laid or built room's two grounds are weighed by their heights where their graphs turn that on, as the engine's height blending weighs them - see GroundPainter.</param>
public readonly record struct RoomKey(
    string Path,
    int Doodads = RoomModels.UsualDoodads,
    bool Tools = false,
    string Laid = "",
    bool Fitted = false,
    DoodadHeight Heights = DoodadHeight.File,
    bool Hidden = false,
    int Cut = 0,
    string Built = "",
    bool Mirrored = false,
    bool MaskCells = false,
    bool GroundHeights = false)
{
    /// <summary>What starts the word naming the tileset a room is built from.</summary>
    public const string BuiltWord = "built=";

    /// <summary>
    /// The folder the tileset <see cref="Built"/> names sits in - "Port" for Maps/Port/master.tsi - or empty where the room is not built.
    /// </summary>
    /// <remarks>
    /// WHAT TELLS TWO BUILT ROOMS APART IN A LINE: most bosses' rooms are called boss_01.arm, so a boss of two maps - Malgor of
    /// Port and of Flotsam - listed two rows reading the same, and the area's own id ran past the stage combo's width.
    /// </remarks>
    public string BuiltIn
    {
        get
        {
            string built = Built.Replace('\\', '/').TrimEnd('/');
            int file = built.LastIndexOf('/');
            int folder = file > 0 ? built.LastIndexOf('/', file - 1) : -1;
            return file > 0 ? built[(folder + 1)..file] : built;
        }
    }

    /// <summary>The deepest cut the slider offers, in world units.</summary>
    public const int MostCut = 2000;

    /// <summary>The word saying a laid room keeps what the camera cannot see.</summary>
    public const string HiddenWord = "hidden";

    /// <summary>What starts the word naming the cut; its number follows.</summary>
    public const string CutWord = "cut=";

    /// <summary>What starts the word naming the place the room is laid at.</summary>
    public const string LaidWord = "laid=";

    /// <summary>The word saying a built room is laid turned over in x.</summary>
    public const string MirroredWord = "mirrored";

    /// <summary>The word saying the ground's shapes across its mask's atlas stay on the mask's cells.</summary>
    public const string MaskCellsWord = "maskcells";

    /// <summary>The word saying two grounds are weighed by their heights.</summary>
    public const string GroundHeightsWord = "groundheights";

    /// <summary>What starts the word naming the cap; its number follows.</summary>
    public const string DoodadsWord = "doodads=";

    /// <summary>The word saying the editor's tools are placed.</summary>
    public const string ToolsWord = "tools";

    /// <summary>The word saying a laid room's pieces are fitted to the area's ground.</summary>
    public const string FittedWord = "fitted";

    /// <summary>What starts the word naming how doodads with a height are set where it is not the usual; "ground" or "added" follows.</summary>
    public const string HeightsWord = "heights=";

    /// <summary>The key as a string - the bare path at the usual choices.</summary>
    public override string ToString()
    {
        if (Doodads == RoomModels.UsualDoodads && !Tools && Laid.Length == 0 && !Fitted && Heights == DoodadHeight.File && !Hidden && Cut <= 0 && Built.Length == 0 && !Mirrored && !MaskCells && !GroundHeights)
        {
            return Path;
        }

        var words = new List<string>(8);
        if (Doodads != RoomModels.UsualDoodads)
        {
            words.Add(string.Create(CultureInfo.InvariantCulture, $"{DoodadsWord}{Doodads}"));
        }

        if (Tools)
        {
            words.Add(ToolsWord);
        }

        if (Laid.Length > 0)
        {
            words.Add(LaidWord + Laid);
        }

        if (Fitted)
        {
            words.Add(FittedWord);
        }

        if (Heights != DoodadHeight.File)
        {
            words.Add(HeightsWord + (Heights == DoodadHeight.Ground ? "ground" : "added"));
        }

        if (Hidden)
        {
            words.Add(HiddenWord);
        }

        if (Cut > 0)
        {
            words.Add(string.Create(CultureInfo.InvariantCulture, $"{CutWord}{Cut}"));
        }

        if (Built.Length > 0)
        {
            words.Add(BuiltWord + Built);
        }

        if (Mirrored)
        {
            words.Add(MirroredWord);
        }

        if (MaskCells)
        {
            words.Add(MaskCellsWord);
        }

        if (GroundHeights)
        {
            words.Add(GroundHeightsWord);
        }

        return string.Concat(Path, TileKey.Mark.ToString(), string.Join(TileKey.WordMark, words));
    }

    /// <summary>The place the room is laid at, read out of <see cref="Laid"/> - false where it names none or does not read.</summary>
    public bool TryLaid(out int x, out int y, out int turn, out int area)
    {
        x = y = turn = area = 0;
        string[] parts = Laid.Split(',');
        return parts.Length == 4
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out x)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out y)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out turn)
            && turn is >= 0 and < 8
            && int.TryParse(parts[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out area);
    }

    /// <summary>A place written the way <see cref="Laid"/> holds it.</summary>
    public static string LaidAt(int x, int y, int turn, int area)
        => string.Create(CultureInfo.InvariantCulture, $"{x},{y},{turn},{area}");

    /// <summary>
    /// The number a key names an area's grid by - the <c>area</c> of <see cref="LaidAt"/>.
    /// </summary>
    /// <remarks>
    /// THE GRID'S OWN IDENTITY and not its hash: a place is a tile of one reading of one area's grid,
    /// and a grid read anew is a new grid to the key, so a room is never laid over somebody else's
    /// tiles. The tile book and the monster book stamp the same way, so a key either writes loads
    /// in both.
    /// </remarks>
    public static int Stamp(TerrainGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return RuntimeHelpers.GetHashCode(grid);
    }

    /// <summary>A key read back. A cap outside the slider's ends, or a word this does not know, leaves the usual.</summary>
    public static RoomKey Read(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        int mark = key.IndexOf(TileKey.Mark, StringComparison.Ordinal);
        if (mark < 0)
        {
            return new RoomKey(key);
        }

        int doodads = RoomModels.UsualDoodads;
        var tools = false;
        var fitted = false;
        var heights = DoodadHeight.File;
        var hidden = false;
        var cut = 0;
        string laid = string.Empty;
        string built = string.Empty;
        var mirrored = false;
        var maskCells = false;
        var groundHeights = false;
        ReadOnlySpan<char> words = key.AsSpan(mark + 1);
        foreach (Range one in words.Split(TileKey.WordMark))
        {
            ReadOnlySpan<char> word = words[one];
            if (word.StartsWith(DoodadsWord, StringComparison.Ordinal)
                && int.TryParse(word[DoodadsWord.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && number is >= RoomModels.LeastDoodads and <= RoomModels.MostDoodads)
            {
                doodads = number;
            }
            else if (word.SequenceEqual(ToolsWord))
            {
                tools = true;
            }
            else if (word.StartsWith(LaidWord, StringComparison.Ordinal))
            {
                laid = word[LaidWord.Length..].ToString();
            }
            else if (word.SequenceEqual(FittedWord))
            {
                fitted = true;
            }
            else if (word.StartsWith(HeightsWord, StringComparison.Ordinal))
            {
                ReadOnlySpan<char> how = word[HeightsWord.Length..];
                heights = how.SequenceEqual("ground") ? DoodadHeight.Ground : how.SequenceEqual("added") ? DoodadHeight.Added : DoodadHeight.File;
            }
            else if (word.SequenceEqual(HiddenWord))
            {
                hidden = true;
            }
            else if (word.StartsWith(CutWord, StringComparison.Ordinal)
                && int.TryParse(word[CutWord.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int deep)
                && deep is > 0 and <= MostCut)
            {
                cut = deep;
            }
            else if (word.StartsWith(BuiltWord, StringComparison.Ordinal))
            {
                built = word[BuiltWord.Length..].ToString();
            }
            else if (word.SequenceEqual(MirroredWord))
            {
                mirrored = true;
            }
            else if (word.SequenceEqual(MaskCellsWord))
            {
                maskCells = true;
            }
            else if (word.SequenceEqual(GroundHeightsWord))
            {
                groundHeights = true;
            }
        }

        RoomKey read = new(key[..mark], doodads, tools, laid, fitted, heights, hidden, cut, built, mirrored, maskCells, groundHeights);
        return read.Laid.Length == 0 || read.TryLaid(out _, out _, out _, out _) ? read : read with { Laid = string.Empty };
    }
}

/// <summary>
/// How high a doodad whose line carries a height is set: on the ground, at the height, or at the ground plus the height.
/// </summary>
/// <remarks>
/// AT THE HEIGHT IS THE USUAL, AND THE GAME SAID SO: seepage's 2x2_offices_01 laid at its place in the
/// area came out with every doodad where the game has it once they were set at their lines' heights -
/// on the ground, the shelves and pots and gem piles had sunk into floors raised around them or stood on
/// air - and the one pot read from memory sits at exactly its line's -115. A doodad whose line carries
/// none is on the ground whichever is chosen. The other two stay to hold against the game.
/// </remarks>
public enum DoodadHeight : byte
{
    /// <summary>At the height itself, in the area's own z - the usual.</summary>
    File,

    /// <summary>On the ground under it, the height ignored - every picture before the height was read.</summary>
    Ground,

    /// <summary>At the ground under it plus the height.</summary>
    Added,
}
