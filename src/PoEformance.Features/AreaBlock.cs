using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
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
///
/// WHERE THE PLAYER STOOD RIDES IN THE KEY ONCE THE BLOCK IS TAKEN (<see cref="Stood"/>, said from the live client: on the game view the
/// monster stood a few metres off where the game draws the player - MEASURED in The Red Vale's capture, the player at 4081.8, 7314.2 and
/// the monster at the middle of a 25 by 25 block from tile 4, 17, about 75 units off him). A stage stands its monster there. Only the
/// moment of taking writes it - "around you" chosen, or the side's slider let go - and the stage's build is kept under the key without it
/// (<see cref="Unstood"/>): the block is heavy, and one taken again where the player stands elsewhere in it moves the monster and lays
/// no tile again.
///
/// AND THE HEIGHT OF THE GROUND HE STOOD ON (<see cref="StoodHeight"/>, v0.1.375, from a review): v0.1.373 stood the monster on the middle
/// of nine floors round the place - the point and eight on a ring 120 units out - a rule made for a boss's place, where the seat of a throne
/// at the point must not lift him. Round the player it fails wherever what he stands on is narrower than the ring: on a footbridge the
/// river's bed under the translucent water is most of the nine, in a passage the walls' tops - the monster hundreds of units off, and the
/// game view looking at the wrong height with him. Of the floors round the place the one nearest his own height is the one he stood on
/// (StagedModels.Staged). A key written before it names x and y alone and still reads, its height unknown.
/// </remarks>
/// <param name="X">The block's least tile across.</param>
/// <param name="Y">The block's least tile down.</param>
/// <param name="Size">Its side in tiles.</param>
/// <param name="Area">The grid it was taken from - RoomKey.Stamp.</param>
/// <param name="Masters">The masters (<c>.tsi</c>) of the area's rooms, one of which paints its ground and swaps its tiles - none for plain ground.</param>
/// <param name="Rooms">The rooms the area placed that reach the block, each where it stands - their own doodads are laid too (v0.1.360) - or none.</param>
/// <param name="Stood">Where the player stood when the block was taken, in world x and y - where a stage stands its monster - or null: a block offered and not yet taken, or a key from before the place rode in it.</param>
/// <param name="StoodHeight">The height of the ground the player stood on then, in world z - the frame the block's picture keeps - where a stage takes the floor nearest it; or null: no place, or a key from before the height rode in it.</param>
public readonly record struct AreaBlock(
    int X,
    int Y,
    int Size,
    int Area,
    IReadOnlyList<string> Masters,
    IReadOnlyList<(string Room, int X, int Y, int Turn)>? Rooms = null,
    Vector2? Stood = null,
    float? StoodHeight = null)
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

    /// <summary>What starts the word naming the rooms; each <c>path@x,y,turn</c>, separated by <see cref="MasterMark"/>.</summary>
    public const string RoomsWord = "rooms=";

    /// <summary>
    /// What starts the word naming where the player stood when the block was taken; "x,y,z" in world units follows - see <see cref="Stood"/> and
    /// <see cref="StoodHeight"/> - or "x,y" alone, as v0.1.373 and v0.1.374 wrote it.
    /// </summary>
    public const string StoodWord = "stood=";

    /// <summary>
    /// THE ROOMS THE AREA PLACED, ALL OF WHAT IS FOUND (v0.1.360, asked for from the live client: "draw every doodad we find"): the places
    /// the tool's own placing found for the area's rooms, by their doodads or their tiles, kept where a room's footprint reaches the block.
    /// </summary>
    public static IReadOnlyList<(string Room, int X, int Y, int Turn)> RoomsIn(
        int x, int y, int size, IEnumerable<(string Room, int X, int Y, int Turn, int Width, int Height)> standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        var rooms = new List<(string Room, int X, int Y, int Turn)>();
        foreach ((string room, int rx, int ry, int turn, int wide, int tall) in standing)
        {
            if (rx < x + size && rx + Math.Max(1, wide) > x && ry < y + size && ry + Math.Max(1, tall) > y)
            {
                rooms.Add((room.Replace('\\', '/'), rx, ry, turn));
            }
        }

        return rooms;
    }

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

    /// <summary>
    /// The masters standing in the folders of the area's own terrain files, or up to two folders above them - where no room of the area
    /// is known to find one beside (<see cref="MastersOf"/>).
    /// </summary>
    /// <remarks>
    /// A RE-ENTERED INSTANCE LOADS NO ROOM AGAIN: the game's list of what was loaded since the area change then holds the area's ground
    /// types and little else - The Red Vale from the live client (v0.1.375): no room, no master, its ground types under
    /// <c>Gallows/Act1/1_5/GroundTypes/</c> - so no master was found and the area round you came out unpainted, unswapped and unstrewn.
    /// The area's own ground types stand in its master's folder or one under it (<c>1_5/GroundTypes/</c> beside <c>1_5/master.tsi</c>),
    /// so the masters are sought up from them; which of them paints a block is chosen as ever, by the ground types it names. CHOSEN: the
    /// file's own folder and two above it, terrain files alone.
    /// </remarks>
    public static IReadOnlyList<string> MastersNear(Func<string, byte[]?> read, IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(files);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var masters = new List<string>();
        foreach (string file in files)
        {
            string path = file.Replace('\\', '/').Trim();
            if (!path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase)
                || !(path.EndsWith(".gt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            int slash = path.LastIndexOf('/');
            for (var up = 0; up < 3 && slash > 0; up++)
            {
                string folder = path[..slash];
                if (!folders.Add(folder))
                {
                    break;
                }

                string master = folder + "/master.tsi";
                if (read(master) is { Length: > 0 })
                {
                    masters.Add(master);
                    break;
                }

                slash = folder.LastIndexOf('/');
            }
        }

        return masters;
    }

    /// <summary>Whether a key names a block rather than a tile or a room.</summary>
    public static bool IsKey([NotNullWhen(true)] string? key) => key is not null && key.StartsWith(Path + TileKey.Mark, StringComparison.Ordinal);

    /// <summary>The key as a string.</summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Path}{TileKey.Mark}{AtWord}{X},{Y},{Size},{Area}")
            + (Masters is { Count: > 0 } ? TileKey.WordMark + MastersWord + string.Join(MasterMark, Masters) : string.Empty)
            + (Rooms is { Count: > 0 } rooms
                ? TileKey.WordMark + RoomsWord + string.Join(MasterMark, rooms.Select(one => string.Create(CultureInfo.InvariantCulture, $"{one.Room}@{one.X},{one.Y},{one.Turn}")))
                : string.Empty)
            + (Stood is { } stood
                ? string.Create(CultureInfo.InvariantCulture, $"{TileKey.WordMark}{StoodWord}{stood.X:0.##},{stood.Y:0.##}")
                    + (StoodHeight is { } height && float.IsFinite(height) ? string.Create(CultureInfo.InvariantCulture, $",{height:0.##}") : string.Empty)
                : string.Empty);

    /// <summary>
    /// A key without where the player stood - the block's tiles alone, which a stage's build is kept under (StagedModels.Of) - or the key as it
    /// is where it names no place or no block. The height goes with the place: a block taken again at another height lays no tile again.
    /// </summary>
    public static string Unstood(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryRead(key, out AreaBlock block) && block.Stood is not null ? (block with { Stood = null, StoodHeight = null }).ToString() : key;
    }

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
        var rooms = new List<(string Room, int X, int Y, int Turn)>();
        Vector2? stood = null;
        float? height = null;
        ReadOnlySpan<char> words = key.AsSpan(Path.Length + 1);
        foreach (Range range in words.Split(TileKey.WordMark))
        {
            ReadOnlySpan<char> word = words[range];
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
            else if (word.StartsWith(RoomsWord, StringComparison.Ordinal))
            {
                foreach (string one in word[RoomsWord.Length..].ToString().Split(MasterMark, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int mark = one.LastIndexOf('@');
                    string[] place = mark < 0 ? [] : one[(mark + 1)..].Split(',');
                    if (place.Length == 3
                        && int.TryParse(place[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int rx)
                        && int.TryParse(place[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int ry)
                        && int.TryParse(place[2], NumberStyles.None, CultureInfo.InvariantCulture, out int turn) && turn is >= 0 and < 8)
                    {
                        rooms.Add((one[..mark], rx, ry, turn));
                    }
                }
            }
            else if (word.StartsWith(StoodWord, StringComparison.Ordinal))
            {
                // A PLACE THAT DOES NOT READ IS NO PLACE: the monster then stands at the middle, as a key from before the place rode in it.
                // Two numbers are a place whose height is unknown - v0.1.373's and v0.1.374's keys - and three a place with the ground's
                // height; a third that does not read leaves no place, not a place without a height, as any number that does not read.
                string[] at = word[StoodWord.Length..].ToString().Split(',');
                float sz = float.NaN;
                if (at.Length is 2 or 3
                    && Number(at[0], out float sx) && Number(at[1], out float sy) && (at.Length == 2 || Number(at[2], out sz)))
                {
                    stood = new Vector2(sx, sy);
                    height = at.Length == 3 ? sz : null;
                }
            }
        }

        if (!placed)
        {
            return false;
        }

        block = new AreaBlock(x, y, size, area, masters, rooms.Count > 0 ? rooms : null, stood, height);
        return true;

        static bool Number(string said, out float value)
            => float.TryParse(said, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
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
        MonsterModel model = LaidRoomModels.Block(read, grid, X, Y, Size, master, progress: progress, rooms: Rooms);
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
