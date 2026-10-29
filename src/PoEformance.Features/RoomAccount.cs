namespace PoEformance.Features;

/// <summary>
/// What laying or building a room came to, counted rather than said - the stage sweep's reading of one build (CaptureStageSweep).
/// </summary>
/// <remarks>
/// THE SAME NUMBERS AS THE LINES UNDER THE PICTURE (MonsterModel.Move), kept as numbers: the lines say them for one room, the sweep adds
/// them up over every room of every set, and a sum over sentences would mean reading the sentences back. What a shape is drawn with is
/// not here - the room's own lists say it shape by shape (MonsterModel.Shades, Skins, ShapeMaterials), and the paint cache why.
/// </remarks>
public sealed record RoomAccount
{
    /// <summary>Tile pieces laid.</summary>
    public int Pieces { get; init; }

    /// <summary>Pieces not whole - the tiles laid do not come out the file's size that way round - and left out.</summary>
    public int Broken { get; init; }

    /// <summary>Pieces whose file has nothing to draw, neither props nor ground.</summary>
    public int Bare { get; init; }

    /// <summary>Pieces left out to keep the picture turnable.</summary>
    public int Capped { get; init; }

    /// <summary>Pieces laid no way the game's tables said, drawn as their files have them.</summary>
    public int Unturned { get; init; }

    /// <summary>The distinct tile files laid.</summary>
    public int TileFiles { get; init; }

    /// <summary>The swaps the master's TileMaterialOverrides list - nought where it names none.</summary>
    public int Swaps { get; init; }

    /// <summary>Tile files a swap reached.</summary>
    public int SwappedTiles { get; init; }

    /// <summary>Doodad files of the tiles a swap reached.</summary>
    public int SwappedDoodadFiles { get; init; }

    /// <summary>Why the ground is not painted at all - no master, no MaterialsList - or empty where it is.</summary>
    public string Unpainted { get; init; } = string.Empty;

    /// <summary>Ground sub-tiles painted, of one ground or more.</summary>
    public int GroundPainted { get; init; }

    /// <summary>Ground sub-tiles left plain, by why - GroundPainter's own reasons.</summary>
    public IReadOnlyDictionary<string, int> GroundPlain { get; init; } = new Dictionary<string, int>();

    /// <summary>The tiles each reason in <see cref="GroundPlain"/> was met on, with their templates and masks, a few a reason.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GroundPlainSubjects { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Tile files of the pieces not whole, a few.</summary>
    public IReadOnlyList<string> BrokenFiles { get; init; } = [];

    /// <summary>Tile files of the pieces with nothing to draw, a few.</summary>
    public IReadOnlyList<string> BareFiles { get; init; } = [];

    /// <summary>Tiles of the tileset's lists whose definition did not read, a few.</summary>
    public IReadOnlyList<string> UnreadTiles { get; init; } = [];

    /// <summary>Ground sub-tiles naming a ground the MaterialsList does not, painted as the group with no name.</summary>
    public int GroundUnlisted { get; init; }

    /// <summary>The grounds those name, by name.</summary>
    public IReadOnlyList<string> UnlistedGrounds { get; init; } = [];

    /// <summary>The ground type file (<c>.gt</c>) of each of <see cref="UnlistedGrounds"/>, by its name.</summary>
    public IReadOnlyDictionary<string, string> UnlistedGroundFiles { get; init; } = new Dictionary<string, string>();

    /// <summary>Slots a build from the tileset asked to fill - nought for a room as the area laid it.</summary>
    public int Slots { get; init; }

    /// <summary>Slots no tile of the pool answered.</summary>
    public int Unfilled { get; init; }

    /// <summary>Slots laid with a named side or corner unmet.</summary>
    public int Unmet { get; init; }

    /// <summary>Tiles of the tileset's lists whose definition did not read, left out of the pool.</summary>
    public int PoolUnread { get; init; }
}
