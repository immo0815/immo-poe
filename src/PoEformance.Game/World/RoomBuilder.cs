using System.Globalization;
using PoEformance.Game.Files;

namespace PoEformance.Game.World;

/// <summary>A tile a room may be built from: its file, its identity, and the weight its list gives it.</summary>
/// <param name="Path">The <c>.tdt</c>.</param>
/// <param name="Identity">What it is, in a slot's terms.</param>
/// <param name="Weight">The weight its tile list draws it by - nought where the list gives none.</param>
/// <param name="Words">The words after it in its tile list, as written - see TileListEntry.</param>
public sealed record RoomBuildTile(string Path, TileIdentity Identity, int Weight, IReadOnlyList<string> Words);

/// <summary>One slot of a built room: what it asked, how many tiles answered, and the one laid - or none.</summary>
/// <param name="Column">The slot's record's column in the room.</param>
/// <param name="Line">Its line.</param>
/// <param name="Left">The first column of its footprint in the room - the record's own or not, by its origin (RoomSlot.Corner); -1 for a rim tile.</param>
/// <param name="Top">The first line of its footprint.</param>
/// <param name="Width">Its width in tiles.</param>
/// <param name="Height">Its height.</param>
/// <param name="Said">What it asked, in a line's words.</param>
/// <param name="Candidates">How many tiles of the pool answered it.</param>
/// <param name="Tile">The tile laid, or empty where none answered.</param>
/// <param name="Placement">How the tile was laid, 0 to 7 - see TileOrientation.Placement - or -1 where none was.</param>
/// <param name="Fits">How many of the slot's named sides and corners the tile meets with the same type, laid that way.</param>
/// <param name="Named">How many sides and corners the slot names.</param>
public sealed record RoomBuildSlot(int Column, int Line, int Left, int Top, int Width, int Height, string Said, int Candidates, string Tile, int Placement, int Fits, int Named);

/// <summary>A room built from a pool of tiles - its grid as an area would hold it, and what each slot got.</summary>
/// <param name="Tiles">The tiles laid, as the area's own grid holds them - the room's grid and every footprint that reaches past it - or null where nothing could be built.</param>
/// <param name="Slots">Every slot that asked for a tile, in the grid's order.</param>
/// <param name="Why">Why nothing was built, or empty.</param>
public sealed record RoomBuild(TerrainTiles? Tiles, IReadOnlyList<RoomBuildSlot> Slots, string Why)
{
    /// <summary>The column of <see cref="Tiles"/> the room's own first column lies on - one where rim tiles reach past the grid.</summary>
    public int X { get; init; }

    /// <summary>The line of <see cref="Tiles"/> the room's own first line lies on.</summary>
    public int Y { get; init; }

    /// <summary>Slots given a tile.</summary>
    public int Filled => Slots.Count(one => one.Tile.Length > 0);

    /// <summary>Slots no tile of the pool answered.</summary>
    public int Unfilled => Slots.Count - Filled;

    /// <summary>Slots exactly one tile answered - settled by the files, not chosen.</summary>
    public int Settled => Slots.Count(one => one.Candidates == 1);

    /// <summary>Slots whose tile, laid every way, still leaves a named side or corner unmet - the type families the sets allow and the sides do not.</summary>
    public int Unmet => Slots.Count(one => one.Tile.Length > 0 && one.Fits < one.Named);

    /// <summary>The build in a line.</summary>
    public string Said
        => Why.Length > 0
            ? Why
            : string.Create(
                CultureInfo.InvariantCulture,
                $"built from the tileset: {Filled} of {Slots.Count} slots filled, {Settled} settled by one tile, {Slots.Count - Settled - Unfilled} chosen among several by weight{(Unmet > 0 ? $", {Unmet} laid with a named side unmet" : string.Empty)}{(Unfilled > 0 ? $", {Unfilled} with no tile alike" : string.Empty)}");
}

/// <summary>
/// Builds a room from a tileset's pool: a tile alike to each slot, laid the way its named sides meet the slot's - the generator's work done again, without the area.
/// </summary>
/// <remarks>
/// WHAT IS KNOWN AND WHAT IS CHOSEN. A room's slot names types, and which tiles of the pool answer it is
/// the search's own rule (SlotWants): where one answers, the files settle the slot; where several do,
/// the generator chose, and this chooses the heaviest by its list's weight, the first of equals - a room
/// of only valid tiles, not the room of any one instance. A boss arena is the one slot that matters,
/// and it is settled: one tagged tile answers it (slot-candidates.txt over Oasis: 5 of 42 slots settled,
/// the arena among them, the ring round it chosen).
///
/// THE WAY ROUND IS THE MEASURED RULE, not a guess - orientation-replay.txt over Oasis, every reading
/// of the two conventions tried: a slot's side in the room's own axes (down minus v, right plus u) meets
/// the tile's side in the template's axes (down minus y, right plus x) placed as TileOrientation.Turn
/// places it. So each candidate placement is asked whether every side and corner the slot names finds
/// the same type on the tile side lying that way; the first placement meeting all of them is taken, as
/// authored before turns and turns before mirrors, else the one meeting most - and that count is kept
/// per slot, because the sets allow what the sides do not (mudpool against mudpooltrim, see SlotWants).
///
/// THE GRID IT HANDS BACK IS THE AREA'S OWN SHAPE (TerrainTiles): each cell its file, its piece and its
/// placement, the pieces running right and down from the first cell of a slot's footprint as the area
/// lays them. That first cell is the record's own only for origin nought (RoomSlot.Origin): a record
/// may sit at any corner of its footprint, and a rim tile's footprint reaches one tile past the room's
/// grid, so the grid is grown to hold every footprint whole and RoomBuild.X and Y say where the room's
/// own first column and line lie in it. So the room is drawn by the laid-room drawing on a grid of its
/// own, without a line of it changed - what the area knows, built here instead of read.
/// </remarks>
public static class RoomBuilder
{
    /// <summary>The four sides in the files' order - down, right, up, left - as directions, down being minus y.</summary>
    private static readonly (int X, int Y)[] Sides = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>The four corners in the files' order - down-left, down-right, up-right, up-left.</summary>
    private static readonly (int X, int Y)[] Corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)];

    /// <summary>The placements in the order tried: as authored, then the turns, then the mirrored ones - so a symmetric slot takes the plainest way.</summary>
    private static readonly int[] Tried = [.. Enumerable.Range(0, 8)
        .OrderBy(placement => TileOrientation.OfPlacement(placement).Mirrored ? 1 : 0)
        .ThenBy(placement => TileOrientation.OfPlacement(placement).Degrees)];

    /// <summary>
    /// The room built from the pool. Never throws for a room that read.
    /// </summary>
    /// <param name="room">The room, read with its slots.</param>
    /// <param name="pool">The tiles the tileset offers, each with its identity.</param>
    public static RoomBuild Build(RoomLayout room, IReadOnlyList<RoomBuildTile> pool)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(pool);
        if (!room.Ready)
        {
            return new RoomBuild(null, [], $"the room did not read: {room.Why}");
        }

        if (room.Slots.Count == 0)
        {
            return new RoomBuild(null, [], room.SlotsWhy.Length > 0 ? $"the room's grid did not read: {room.SlotsWhy}" : "the room has no slot grid");
        }

        var wants = new SlotWant?[pool.Count];
        for (var at = 0; at < pool.Count; at++)
        {
            wants[at] = SlotWants.Of(pool[at].Identity);
        }

        // THE CHOICES FIRST, the grid after: a footprint may reach past the room's grid (RoomSlot.Corner),
        // and the grid handed back must hold every footprint whole, or the drawing finds the rim tiles
        // broken and leaves them out.
        var slots = new List<RoomBuildSlot>();
        var paths = new List<string>();
        var pathIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var chosenIds = new List<int>();
        int leastX = 0, mostX = room.Width - 1, leastY = 0, mostY = room.Height - 1;
        for (var line = 0; line < room.Height; line++)
        {
            for (var column = 0; column < room.Width; column++)
            {
                RoomSlot slot = room.SlotAt(column, line);
                if (!slot.IsTile)
                {
                    continue;
                }

                SlotWant want = SlotWants.Of(room, slot);
                int best = -1;
                int bestWeight = int.MinValue;
                var candidates = 0;
                for (var at = 0; at < pool.Count; at++)
                {
                    if (wants[at] is { } tile && SlotWants.IsAlike(want, tile))
                    {
                        candidates++;
                        if (pool[at].Weight > bestWeight)
                        {
                            best = at;
                            bestWeight = pool[at].Weight;
                        }
                    }
                }

                string said = SlotWants.Said(want);
                (int left, int top) = slot.Corner(column, line);
                if (best < 0)
                {
                    slots.Add(new RoomBuildSlot(column, line, left, top, slot.Width, slot.Height, said, 0, string.Empty, -1, 0, 0));
                    chosenIds.Add(-1);
                    continue;
                }

                (int placement, int fits, int named) = Placement(room, slot, pool[best].Identity);
                if (!pathIds.TryGetValue(pool[best].Path, out int id))
                {
                    id = paths.Count;
                    paths.Add(pool[best].Path);
                    pathIds[pool[best].Path] = id;
                }

                leastX = Math.Min(leastX, left);
                leastY = Math.Min(leastY, top);
                mostX = Math.Max(mostX, left + slot.Width - 1);
                mostY = Math.Max(mostY, top + slot.Height - 1);
                slots.Add(new RoomBuildSlot(column, line, left, top, slot.Width, slot.Height, said, candidates, pool[best].Path, placement, fits, named));
                chosenIds.Add(id);
            }
        }

        if (slots.Count == 0)
        {
            return new RoomBuild(null, [], "the room's grid holds no slot that asks for a tile");
        }

        if (paths.Count == 0)
        {
            return new RoomBuild(null, slots, "no tile of the pool answers any slot of the room");
        }

        int width = mostX - leastX + 1;
        int height = mostY - leastY + 1;
        var ids = new int[width * height];
        Array.Fill(ids, -1);
        var subX = new byte[width * height];
        var subY = new byte[width * height];
        var placements = new sbyte[width * height];
        Array.Fill(placements, (sbyte)-1);
        for (var one = 0; one < slots.Count; one++)
        {
            RoomBuildSlot slot = slots[one];
            int id = chosenIds[one];
            if (id < 0)
            {
                continue;
            }

            // THE PIECES RUN RIGHT AND DOWN FROM THE FOOTPRINT'S FIRST CELL in the area's own axes, as the
            // area lays them - what the replay measured on every laid file (RoomFinder.CornerOf).
            for (var down = 0; down < slot.Height; down++)
            {
                for (var across = 0; across < slot.Width; across++)
                {
                    int at = ((slot.Top - leastY + down) * width) + slot.Left - leastX + across;
                    ids[at] = id;
                    subX[at] = (byte)across;
                    subY[at] = (byte)down;
                    placements[at] = (sbyte)slot.Placement;
                }
            }
        }

        return new RoomBuild(new TerrainTiles(paths, ids, subX, subY, placements, width, height), slots, string.Empty) { X = -leastX, Y = -leastY };
    }

    /// <summary>
    /// Which way the tile is laid on the slot: the first placement, as authored before turns and turns before mirrors, under which every named side and corner of the slot meets the same type on the tile - else the one meeting most.
    /// </summary>
    /// <returns>The placement, how many named sides and corners it meets, and how many the slot names.</returns>
    public static (int Placement, int Fits, int Named) Placement(RoomLayout room, RoomSlot slot, TileIdentity tile)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(tile);
        string[] edges = [room.Named(slot.Edge(0)), room.Named(slot.Edge(1)), room.Named(slot.Edge(2)), room.Named(slot.Edge(3))];
        string[] grounds = [room.Named(slot.Ground(0)), room.Named(slot.Ground(1)), room.Named(slot.Ground(2)), room.Named(slot.Ground(3))];
        int named = edges.Count(one => one.Length > 0) + grounds.Count(one => one.Length > 0);

        // AN OBLONG TILE LIES ITS OWN WAY OR THE OTHER: a placement whose world x reads the template's y
        // turns the footprint over, so a tile the slot's size as filed takes the placements that keep
        // it, one the slot's size the other way round the placements that turn it.
        bool square = slot.Width == slot.Height;
        bool asFiled = tile.Width == slot.Width && tile.Height == slot.Height;
        int bestPlacement = -1;
        int bestFits = -1;
        foreach (int placement in Tried)
        {
            TileOrientation placed = TileOrientation.OfPlacement(placement);
            bool turnsOver = placed.Turn.Xx == 0;
            if (!square && turnsOver == asFiled)
            {
                continue;
            }

            int fits = Fits(placed, Sides, edges, tile.Edges) + Fits(placed, Corners, grounds, tile.Grounds);
            if (fits > bestFits)
            {
                bestFits = fits;
                bestPlacement = placement;
            }

            if (fits == named)
            {
                break;
            }
        }

        return (bestPlacement, Math.Max(0, bestFits), named);
    }

    /// <summary>
    /// How many of the slot's named sides and corners the tile meets with the same type, laid one given way - for a reader asking whether the game's way was one the sides allow too.
    /// </summary>
    public static int FitsAt(RoomLayout room, RoomSlot slot, TileIdentity tile, int placement)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(tile);
        string[] edges = [room.Named(slot.Edge(0)), room.Named(slot.Edge(1)), room.Named(slot.Edge(2)), room.Named(slot.Edge(3))];
        string[] grounds = [room.Named(slot.Ground(0)), room.Named(slot.Ground(1)), room.Named(slot.Ground(2)), room.Named(slot.Ground(3))];
        TileOrientation placed = TileOrientation.OfPlacement(placement);
        return Fits(placed, Sides, edges, tile.Edges) + Fits(placed, Corners, grounds, tile.Grounds);
    }

    /// <summary>How many of the slot's named sides (or corners) the tile meets with the same type, laid this way.</summary>
    private static int Fits(TileOrientation placed, (int X, int Y)[] directions, string[] slotTypes, IReadOnlyList<string> tileTypes)
    {
        var fits = 0;
        for (var side = 0; side < 4; side++)
        {
            if (slotTypes[side].Length == 0)
            {
                continue;
            }

            int met = Meets(placed, directions, side);
            if (met >= 0 && met < tileTypes.Count && string.Equals(Clean(slotTypes[side]), Clean(tileTypes[met]), StringComparison.OrdinalIgnoreCase))
            {
                fits++;
            }
        }

        return fits;
    }

    /// <summary>
    /// Which side (or corner) of the tile lies the way a slot's side does, in a room laid as written - the measured rule; -1 for none.
    /// </summary>
    /// <remarks>
    /// THE SLOT'S SIDE IS ITS OWN DIRECTION, the room being laid as written; THE TILE'S SIDE goes through its
    /// placement (TileOrientation.Turn, template to world). Where the two directions agree the sides meet.
    /// A room laid turned turns the whole grid afterwards, tiles and all, and the rule holds under it - which
    /// is what the replay measured over five turns.
    /// </remarks>
    public static int Meets(TileOrientation placed, (int X, int Y)[] directions, int side)
    {
        ArgumentNullException.ThrowIfNull(directions);
        (int ax, int ay) = directions[side];
        (int xx, int xy, int yx, int yy) = placed.Turn;
        for (var k = 0; k < directions.Length; k++)
        {
            (int ex, int ey) = directions[k];
            if ((xx * ex) + (xy * ey) == ax && (yx * ex) + (yy * ey) == ay)
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>The sides in the files' order as directions - for a reader holding a build against the area.</summary>
    public static IReadOnlyList<(int X, int Y)> SideDirections => Sides;

    /// <summary>The corners in the files' order as directions.</summary>
    public static IReadOnlyList<(int X, int Y)> CornerDirections => Corners;

    private static string Clean(string type) => type.Replace('\\', '/').Trim();
}
