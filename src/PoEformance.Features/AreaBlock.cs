using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// The key a model pane loads the area round the player under: a block of tiles of the area on screen, laid as the area laid them - see
/// LaidRoomModels.Block.
/// </summary>
/// <remarks>
/// A STAGE FOR EVERY AREA (v0.1.359, DECIDED from the live client): a room is a stage only where it can be placed, and most of an area's
/// rooms cannot - The Red Vale placed 7 of its 187, none of them its river - while every tile the area laid is known with its place and turn.
/// So the Tile Book and the Monster Book offer the block round the player's tile, its side a slider (CHOSEN from the live client: 3 to 25
/// tiles, 9 the usual), laid where the area laid it - and, the picture's origin being a place in the area, drawn with what the area holds
/// there, the river's flow map among it (WaterLook.Flow).
///
/// ITS GROUND IS PAINTED BY THE AREA'S OWN MASTER, CHOSEN AS THE CAPTURE'S GROUND PART CHOOSES IT: the key carries the masters beside the
/// area's rooms (each room's master.tsi beside its Rooms/), and the build takes the one whose MaterialsList names the most of the ground types
/// at the block's tiles (CaptureGroundScreen.MasterOf) - an area's rooms may sit under a world map's master too, whose list names little.
///
/// THE SAME MARKS AS A TILE'S AND A ROOM'S KEY (TileKey.Mark, WordMark): the name before the first, then the place and the masters. A key
/// names one reading of one area's grid (RoomKey.Stamp), so a block is never laid over another area's tiles.
/// </remarks>
/// <param name="X">The block's least tile across.</param>
/// <param name="Y">The block's least tile down.</param>
/// <param name="Size">Its side in tiles.</param>
/// <param name="Area">The grid it was taken from - RoomKey.Stamp.</param>
/// <param name="Masters">The masters (<c>.tsi</c>) of the area's rooms, one of which paints its ground and swaps its tiles - none for plain ground.</param>
public readonly record struct AreaBlock(int X, int Y, int Size, int Area, IReadOnlyList<string> Masters)
{
    /// <summary>The name the key starts with, and what the block's model is called.</summary>
    public const string Path = "around you";

    /// <summary>The smallest side the slider offers, in tiles.</summary>
    public const int LeastSize = 3;

    /// <summary>The largest side the slider offers, in tiles - CHOSEN from the live client.</summary>
    public const int MostSize = 25;

    /// <summary>The side a block starts at, in tiles - CHOSEN from the live client: 2,250 units, about what the game's camera shows round the player.</summary>
    public const int UsualSize = 9;

    /// <summary>What starts the word naming the place; "x,y,size,area" follows.</summary>
    public const string AtWord = "at=";

    /// <summary>What starts the word naming the masters; their paths follow, separated by <see cref="MasterMark"/>.</summary>
    public const string MastersWord = "masters=";

    /// <summary>What separates two masters' paths - in no path.</summary>
    public const char MasterMark = ';';

    /// <summary>The block of <paramref name="size"/> tiles a side round a tile, kept inside the area's grid.</summary>
    /// <param name="tileX">The tile across the block is centred on - the player's.</param>
    /// <param name="tileY">The tile down.</param>
    /// <param name="size">Its side, held to <see cref="LeastSize"/> to <see cref="MostSize"/>.</param>
    /// <param name="grid">The area's grid.</param>
    /// <param name="masters">The masters of the area's rooms - see <see cref="MastersOf"/>.</param>
    public static AreaBlock Around(int tileX, int tileY, int size, TerrainGrid grid, IReadOnlyList<string> masters)
    {
        ArgumentNullException.ThrowIfNull(grid);
        int side = Math.Clamp(size, LeastSize, MostSize);
        int wide = grid.Tiles?.Width ?? 0, tall = grid.Tiles?.Height ?? 0;
        int x = Math.Clamp(tileX - (side / 2), 0, Math.Max(0, wide - side));
        int y = Math.Clamp(tileY - (side / 2), 0, Math.Max(0, tall - side));
        return new AreaBlock(x, y, side, RoomKey.Stamp(grid), masters ?? []);
    }

    /// <summary>
    /// The masters beside an area's rooms, each once in the rooms' order - a room's master.tsi beside its Rooms/ where the install has one
    /// (GroundPainter.MasterBeside); a folder read once whatever number of rooms share it.
    /// </summary>
    public static IReadOnlyList<string> MastersOf(Func<string, byte[]?> read, IEnumerable<string> rooms)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(rooms);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var masters = new List<string>();
        foreach (string room in rooms)
        {
            string path = room.Replace('\\', '/').Trim();
            int at = path.IndexOf("/Rooms/", StringComparison.OrdinalIgnoreCase);
            if (at < 0 || !folders.Add(path[..at]))
            {
                continue;
            }

            string master = GroundPainter.MasterBeside(read, path);
            if (master.Length > 0)
            {
                masters.Add(master);
            }
        }

        return masters;
    }

    /// <summary>Whether a key names a block rather than a tile or a room.</summary>
    public static bool IsKey([NotNullWhen(true)] string? key) => key is not null && key.StartsWith(Path + TileKey.Mark, StringComparison.Ordinal);

    /// <summary>The key as a string.</summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Path}{TileKey.Mark}{AtWord}{X},{Y},{Size},{Area}")
            + (Masters is { Count: > 0 } ? TileKey.WordMark + MastersWord + string.Join(MasterMark, Masters) : string.Empty);

    /// <summary>A key read back - false where it is no block's or its place does not read.</summary>
    public static bool TryRead(string? key, out AreaBlock block)
    {
        block = default;
        if (!IsKey(key))
        {
            return false;
        }

        int x = -1, y = -1, size = 0, area = 0;
        bool placed = false;
        IReadOnlyList<string> masters = [];
        ReadOnlySpan<char> words = key.AsSpan(Path.Length + 1);
        foreach (Range one in words.Split(TileKey.WordMark))
        {
            ReadOnlySpan<char> word = words[one];
            if (word.StartsWith(AtWord, StringComparison.Ordinal))
            {
                string[] parts = word[AtWord.Length..].ToString().Split(',');
                placed = parts.Length == 4
                    && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out x)
                    && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out y)
                    && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out size)
                    && size is >= LeastSize and <= MostSize
                    && int.TryParse(parts[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out area);
            }
            else if (word.StartsWith(MastersWord, StringComparison.Ordinal))
            {
                masters = word[MastersWord.Length..].ToString().Split(MasterMark, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        if (!placed)
        {
            return false;
        }

        block = new AreaBlock(x, y, size, area, masters);
        return true;
    }

    /// <summary>How a combo or a heading names the block.</summary>
    public string Said => string.Create(CultureInfo.InvariantCulture, $"around you ({Size} x {Size} tiles)");

    /// <summary>
    /// The block built - in the area it was taken in alone, whose grid <paramref name="terrain"/> hands back; else a model saying why not.
    /// </summary>
    public MonsterModel Build(Func<string, byte[]?> read, Func<TerrainGrid?>? terrain, ModelProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        TerrainGrid? grid = terrain?.Invoke();
        if (grid?.Tiles is not { } tiles || RoomKey.Stamp(grid) != Area)
        {
            return MonsterModel.None with { Why = "the area this block was taken in is gone - take the area round you again" };
        }

        (string master, string weighed) = MasterFor(read, tiles);
        MonsterModel model = LaidRoomModels.Block(read, grid, X, Y, Size, master, progress: progress);
        return model.Ready ? model with { Move = weighed + "; " + model.Move } : model;
    }

    /// <summary>
    /// Of the masters, the one whose MaterialsList names the most of the ground types at the block's tiles - and the line saying how they
    /// came out. One master is taken as it is; none leaves the ground plain.
    /// </summary>
    private (string Master, string Weighed) MasterFor(Func<string, byte[]?> read, TerrainTiles tiles)
    {
        if (Masters is not { Count: > 0 } masters)
        {
            return (string.Empty, "ground: no master beside the area's rooms - plain");
        }

        if (masters.Count == 1)
        {
            return (masters[0], "ground and swaps by " + masters[0] + ", the area's rooms' master");
        }

        // THE GROUND TYPES AT THE BLOCK'S TILES, which the MaterialsList painting them must name - as the capture's ground part weighs them.
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<int>();
        for (int row = Y; row < Y + Size; row++)
        {
            for (int column = X; column < X + Size; column++)
            {
                int id = tiles.IdAt(column, row);
                if (id >= 0 && seen.Add(id))
                {
                    (TileDefinition definition, _) = TileModels.Defined(read, tiles.Paths[id]);
                    types.UnionWith(definition.Grounds.Concat(definition.GroundGrid).Where(corner => corner.Length > 0));
                }
            }
        }

        (_, string chosen, IReadOnlyList<string> weighed) = CaptureGroundScreen.MasterOf(read, masters, types, new MonsterModels.Paints());
        return chosen.Length > 0
            ? (chosen, "ground and swaps by " + chosen + " - of the area's rooms' masters the one naming the most of the block's grounds: " + string.Join(", ", weighed))
            : (string.Empty, "ground: no master of the area's rooms names a MaterialsList - plain: " + string.Join(", ", weighed));
    }
}
