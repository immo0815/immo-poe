using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// A room built from a pool of tiles: a tile alike to each slot, the heaviest where several answer, laid the way its named sides meet the slot's - and the grid handed back in the area's own shape.
/// </summary>
public class RoomBuilderTests
{
    private const string Edge = "Metadata/Terrain/Woods/edge.et";
    private const string Wall = "Metadata/Terrain/Woods/wall.et";
    private const string Ground = "Metadata/Terrain/Woods/ground.gt";

    /// <summary>A two-slot room: the first slot asks an edge below and a wall to its right, the second asks nothing.</summary>
    private const string TwoSlots = """
        version 31
        3
        "Metadata/Terrain/Woods/edge.et"
        "Metadata/Terrain/Woods/wall.et"
        "Metadata/Terrain/Woods/ground.gt"
        5 3
        1 1
        "roomtag"
        0 1
        k 2 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0

        1 0
        1 0
        1 0
        1 0
        0
        1
        3 4 0.5 "spawn"
        0
        0
        0
        0
        k 1 1 1 2 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 k 1 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0
        0
        0
        0
        """;

    /// <summary>A room of one slot two tiles wide - RoomLayoutTests' own fixture.</summary>
    private const string WideSlot = """
        version 31
        2
        "Metadata/Terrain/Woods/edge.et"
        "Metadata/Terrain/Woods/ground.gt"
        5 3
        1 1
        "roomtag"
        0 1
        k 2 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0

        1 0
        1 0
        1 0
        1 0
        0
        1
        3 4 0.5 "spawn"
        0
        0
        0
        0
        k 2 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 n
        0
        0
        0
        """;

    private static RoomBuildTile Tile(string name, int weight, string down, string right, string up, string left, int width = 1, int height = 1)
        => new(
            "Metadata/Terrain/Woods/Tiles/" + name + ".tdt",
            new TileIdentity(width, height, string.Empty, [down, right, up, left], [Ground, Ground, Ground, Ground]),
            weight,
            []);

    [Fact]
    public void EachSlotTakesTheHeaviestTileAlikeLaidTheWayItsNamedSidesMeet()
    {
        RoomLayout room = RoomLayout.Parse(TwoSlots);
        Assert.True(room.Ready, room.Why);

        RoomBuildTile floor = Tile("floor", 1, Edge, Edge, Edge, Edge);
        RoomBuildTile heavy = Tile("heavy", 10, Edge, Edge, Edge, Edge);

        // The corner names the wall below and the edge to its right - the other way round from the slot.
        RoomBuildTile corner = Tile("corner", 1, Wall, Edge, string.Empty, string.Empty);

        RoomBuild build = RoomBuilder.Build(room, [floor, corner, heavy]);

        Assert.Equal(string.Empty, build.Why);
        Assert.NotNull(build.Tiles);
        Assert.Equal(2, build.Slots.Count);
        Assert.Equal(2, build.Filled);
        Assert.Equal(1, build.Settled);
        Assert.Equal(0, build.Unmet);
        Assert.Contains("2 of 2 slots filled, 1 settled by one tile, 1 chosen among several by weight", build.Said, StringComparison.Ordinal);

        // The first slot: only the corner answers, and it is laid turned over so its wall lies below and its edge to the right.
        RoomBuildSlot first = build.Slots[0];
        Assert.Equal(corner.Path, first.Tile);
        Assert.Equal(1, first.Candidates);
        Assert.Equal((2, 2), (first.Fits, first.Named));
        TileOrientation placed = TileOrientation.OfPlacement(first.Placement);
        Assert.True(placed.Mirrored);
        Assert.Equal((0, -1, -1, 0), placed.Turn);

        // The second asks nothing, so every one-by-one tile answers; the heaviest wins, laid as authored - placement 3 is the identity.
        RoomBuildSlot second = build.Slots[1];
        Assert.Equal(heavy.Path, second.Tile);
        Assert.Equal(3, second.Candidates);
        Assert.Equal(3, second.Placement);
        Assert.Equal((1, 0, 0, 1), TileOrientation.OfPlacement(3).Turn);

        // The grid as an area would hold it.
        Assert.Equal((2, 1), (build.Tiles.Width, build.Tiles.Height));
        Assert.Equal(corner.Path, build.Tiles.PathAt(0, 0));
        Assert.Equal(heavy.Path, build.Tiles.PathAt(1, 0));
        Assert.Equal(first.Placement, build.Tiles.PlacementAt(0, 0));
        Assert.Equal((0, 0), build.Tiles.SubAt(1, 0));

        // Without the corner the first slot stays empty and says so; the room is still built.
        RoomBuild short_ = RoomBuilder.Build(room, [floor, heavy]);
        Assert.NotNull(short_.Tiles);
        Assert.Equal(1, short_.Unfilled);
        Assert.Equal(string.Empty, short_.Slots[0].Tile);
        Assert.Equal(-1, short_.Tiles.IdAt(0, 0));
        Assert.Contains("1 with no tile alike", short_.Said, StringComparison.Ordinal);
    }

    /// <summary>
    /// A slot asking one ground at every corner and no edge takes that ground's section of the master's fill list - the
    /// heaviest of it alike to the slot - over the tile list; a slot asking anything else, or a section with no tile alike,
    /// is left to the tile list.
    /// </summary>
    /// <remarks>
    /// AS SULPHURIC CAVERNS' BOSS ROOM AND AUGURY LAY THEM (RoomBuilder's remarks): the tile list answers such a slot too -
    /// every floor tile of one ground is alike to it - and the game laid the fill list's tile all the same.
    /// </remarks>
    [Fact]
    public void ASlotAskingOneGroundAllRoundIsFilledFromTheFillList()
    {
        RoomLayout room = RoomLayout.Parse(TwoSlots.Replace(
            "k 1 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0",
            "k 1 1 0 0 0 0 0 0 0 0 0 0 0 0 3 3 3 3 0 0 0 0 0",
            StringComparison.Ordinal));
        Assert.True(room.Ready, room.Why);
        Assert.Equal(Ground, RoomBuilder.OneGround(room, room.SlotAt(1, 0)));
        Assert.Null(RoomBuilder.OneGround(room, room.SlotAt(0, 0)));

        RoomBuildTile heavy = Tile("heavy", 10, Edge, Edge, Edge, Edge);
        RoomBuildTile corner = Tile("corner", 1, Wall, Edge, string.Empty, string.Empty);
        RoomBuildTile light = Tile("fill_light", 1, string.Empty, string.Empty, string.Empty, string.Empty);
        RoomBuildTile weighty = Tile("fill_weighty", 5, string.Empty, string.Empty, string.Empty, string.Empty);
        var fills = new Dictionary<string, IReadOnlyList<RoomBuildTile>>(StringComparer.OrdinalIgnoreCase) { [Ground] = [light, weighty] };

        RoomBuild build = RoomBuilder.Build(room, [corner, heavy], fills);
        Assert.Equal(corner.Path, build.Slots[0].Tile);
        Assert.False(build.Slots[0].FromFillList);
        Assert.Equal(weighty.Path, build.Slots[1].Tile);
        Assert.True(build.Slots[1].FromFillList);
        Assert.Equal(2, build.Slots[1].Candidates);
        Assert.Equal(weighty.Path, build.Tiles!.PathAt(1, 0));
        Assert.Contains("1 of them from the master's fill list", build.Said, StringComparison.Ordinal);

        // WITHOUT THE FILL LIST, or with a section no tile of which is alike, the tile list answers as before.
        Assert.Equal(heavy.Path, RoomBuilder.Build(room, [corner, heavy]).Slots[1].Tile);
        RoomBuildTile big = Tile("fill_big", 9, string.Empty, string.Empty, string.Empty, string.Empty, width: 2, height: 2);
        RoomBuild unlike = RoomBuilder.Build(room, [corner, heavy], new Dictionary<string, IReadOnlyList<RoomBuildTile>> { [Ground] = [big] });
        Assert.Equal(heavy.Path, unlike.Slots[1].Tile);
        Assert.False(unlike.Slots[1].FromFillList);
    }

    [Fact]
    public void ASlotWiderThanTallTakesItsTileAsFiledOrTurnedOverToFit()
    {
        RoomLayout room = RoomLayout.Parse(WideSlot);
        Assert.True(room.Ready, room.Why);

        // A tile the slot's shape lies as filed, its pieces running right from the slot's corner.
        RoomBuild asFiled = RoomBuilder.Build(room, [Tile("wide", 1, string.Empty, string.Empty, string.Empty, string.Empty, width: 2, height: 1)]);
        Assert.NotNull(asFiled.Tiles);
        Assert.Equal(3, asFiled.Slots[0].Placement);
        Assert.Equal((0, 0), asFiled.Tiles.SubAt(0, 0));
        Assert.Equal((1, 0), asFiled.Tiles.SubAt(1, 0));
        Assert.Equal(asFiled.Tiles.PathAt(0, 0), asFiled.Tiles.PathAt(1, 0));

        // A tile the other shape has to be turned a quarter: a placement whose world x reads the template's y.
        RoomBuild turned = RoomBuilder.Build(room, [Tile("tall", 1, string.Empty, string.Empty, string.Empty, string.Empty, width: 1, height: 2)]);
        Assert.NotNull(turned.Tiles);
        TileOrientation placed = TileOrientation.OfPlacement(turned.Slots[0].Placement);
        Assert.Equal(0, placed.Turn.Xx);
        Assert.False(placed.Mirrored);
    }

    [Fact]
    public void ARecordSitsAtTheCornerOfItsFootprintItsOriginNames()
    {
        // The wide slot's 24th number is its origin: 0 is the record at the footprint's first column, as the fixture has it.
        const string Zeros = "0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0";
        string asWritten = $"k 2 1 {Zeros} 0 n";
        Assert.Contains(asWritten, WideSlot, StringComparison.Ordinal);
        RoomBuildTile wide = Tile("wide", 1, string.Empty, string.Empty, string.Empty, string.Empty, width: 2, height: 1);

        // Origin 1, recorded at the second column: the footprint runs left from it, over the same two cells.
        RoomLayout last = RoomLayout.Parse(WideSlot.Replace(asWritten, $"n k 2 1 {Zeros} 1", StringComparison.Ordinal));
        Assert.True(last.Ready, last.Why);
        Assert.Equal(1, last.SlotAt(1, 0).Origin);
        Assert.Equal((0, 0), last.SlotAt(1, 0).Corner(1, 0));
        RoomBuild inside = RoomBuilder.Build(last, [wide]);
        Assert.NotNull(inside.Tiles);
        Assert.Equal((2, 1, 0, 0), (inside.Tiles.Width, inside.Tiles.Height, inside.X, inside.Y));
        Assert.Equal((1, 0, 0, 0), (inside.Slots[0].Column, inside.Slots[0].Line, inside.Slots[0].Left, inside.Slots[0].Top));
        Assert.Equal((0, 0), inside.Tiles.SubAt(0, 0));
        Assert.Equal((1, 0), inside.Tiles.SubAt(1, 0));

        // Origin 1 recorded at the first column - a rim tile: the footprint reaches a column past the grid, and the grid grows to hold it whole.
        RoomLayout rim = RoomLayout.Parse(WideSlot.Replace(asWritten, $"k 2 1 {Zeros} 1 n", StringComparison.Ordinal));
        Assert.True(rim.Ready, rim.Why);
        RoomBuild grown = RoomBuilder.Build(rim, [wide]);
        Assert.NotNull(grown.Tiles);
        Assert.Equal((-1, 0), (grown.Slots[0].Left, grown.Slots[0].Top));
        Assert.Equal((3, 1, 1, 0), (grown.Tiles.Width, grown.Tiles.Height, grown.X, grown.Y));
        Assert.Equal((0, 0), grown.Tiles.SubAt(0, 0));
        Assert.Equal((1, 0), grown.Tiles.SubAt(1, 0));
        Assert.Equal(-1, grown.Tiles.IdAt(2, 0));

        // The other two corners, by arithmetic alone: 2 the last column and line, 3 the first column and last line.
        var three = new RoomSlot('k', [3, 2, .. new int[21], 2]);
        Assert.Equal((3, 4), three.Corner(5, 5));
        Assert.Equal((5, 4), (three with { Numbers = [3, 2, .. new int[21], 3] }).Corner(5, 5));
        Assert.Equal((5, 5), (three with { Numbers = [3, 2, .. new int[21]] }).Corner(5, 5));
    }

    [Fact]
    public void ATileListLineCarriesItsWeightAndItsWords()
    {
        TileList list = TileList.Parse("""
            // the pool
            include "shared.tst"
            10 "Tiles/a.tdt" R90 R180
            "Tiles/b.tdt"
            5"Tiles/c.tdt"
            """);

        Assert.Equal(["shared.tst"], list.Includes);
        Assert.Equal(["Tiles/a.tdt", "Tiles/b.tdt", "Tiles/c.tdt"], list.Tiles);
        Assert.Equal(3, list.Entries.Count);
        Assert.Equal((10, 2), (list.Entries[0].Weight, list.Entries[0].Words.Count));
        Assert.Equal(["R90", "R180"], list.Entries[0].Words);
        Assert.Null(list.Entries[1].Weight);
        Assert.Empty(list.Entries[1].Words);
        Assert.Equal(5, list.Entries[2].Weight);
    }

    [Fact]
    public void AKeyNamesTheTilesetARoomIsBuiltFrom()
    {
        var key = new RoomKey("Metadata/Terrain/Maps/Oasis/Rooms/Unique/bossman.arm", Built: "Metadata/Terrain/Maps/Oasis/tiles.tst");
        string written = key.ToString();
        Assert.EndsWith("|built=Metadata/Terrain/Maps/Oasis/tiles.tst", written, StringComparison.Ordinal);

        RoomKey read = RoomKey.Read(written);
        Assert.Equal(key.Path, read.Path);
        Assert.Equal(key.Built, read.Built);
        Assert.False(read.TryLaid(out _, out _, out _, out _));
        Assert.Equal(string.Empty, RoomKey.Read("plain.arm").Built);

        // A stage key naming a tileset goes to the build, which without an install says so.
        Assert.Contains("no install", StagedModels.LaidRoom(null, written, () => null).Why, StringComparison.Ordinal);

        // THE FOLDER, which tells two built rooms of one file name apart in a line: Port's boss_01 from Flotsam's.
        Assert.Equal("Port", new RoomKey("Metadata/Terrain/Maps/Port/Rooms/Unique/boss_01.arm", Built: "Metadata/Terrain/Maps/Port/master.tsi").BuiltIn);
        Assert.Equal("4_2_2", new RoomKey("x.arm", Built: "Metadata\\Terrain\\Gallows\\Act4\\4_2_2\\master.tsi").BuiltIn);
        Assert.Equal("Oasis", key.BuiltIn);
        Assert.Equal(string.Empty, new RoomKey("x.arm").BuiltIn);
    }

    /// <summary>A built room turned over in x is a key of its own, written and read back - the Monster Book's switch.</summary>
    [Fact]
    public void ABUILTRoomTurnedOverIsAKeyOfItsOwn()
    {
        var key = new RoomKey("Metadata/Terrain/Maps/Port/Rooms/Unique/boss_01.arm", Built: "Metadata/Terrain/Maps/Port/master.tsi", Mirrored: true);
        string written = key.ToString();
        Assert.EndsWith("|built=Metadata/Terrain/Maps/Port/master.tsi+mirrored", written, StringComparison.Ordinal);

        RoomKey read = RoomKey.Read(written);
        Assert.True(read.Mirrored);
        Assert.Equal(key.Built, read.Built);
        Assert.NotEqual(written, (read with { Mirrored = false }).ToString());
        Assert.False(RoomKey.Read((read with { Mirrored = false }).ToString()).Mirrored);
    }

    /// <summary>A room whose ground keeps its shapes across the mask's atlas on the mask's cells is a key of its own - the Monster Book's switch.</summary>
    [Fact]
    public void AROOMWhoseGroundStaysOnTheMasksCellsIsAKeyOfItsOwn()
    {
        var key = new RoomKey("Metadata/Terrain/Maps/Port/Rooms/Unique/boss_01.arm", Built: "Metadata/Terrain/Maps/Port/master.tsi", Mirrored: true, MaskCells: true);
        string written = key.ToString();
        Assert.EndsWith("+mirrored+maskcells", written, StringComparison.Ordinal);

        RoomKey read = RoomKey.Read(written);
        Assert.True(read.MaskCells);
        Assert.True(read.Mirrored);
        Assert.False(RoomKey.Read((read with { MaskCells = false }).ToString()).MaskCells);
        Assert.Equal("x.arm", new RoomKey("x.arm").ToString());
    }

    /// <summary>
    /// A grid turned over in x: each piece at the columns its mirror image covers, laid the way a mirror sends it, each tile naming its
    /// place counted from its piece's least corner again.
    /// </summary>
    [Fact]
    public void AGRIDTurnedOverLaysEachPieceWhereItsMirrorImageLies()
    {
        // A two-by-one file laid as filed at columns 0 and 1 of three, and a one-by-one turned 90 at column 2.
        var tiles = new TerrainTiles(["two.tdt", "one.tdt"], [0, 0, 1], [0, 1, 0], [0, 0, 0], [3, 3, 6], 3, 1);

        TerrainTiles turned = tiles.MirroredInX(id => id == 0 ? (2, 1) : (1, 1));

        Assert.Equal([1, 0, 0], new[] { turned.IdAt(0, 0), turned.IdAt(1, 0), turned.IdAt(2, 0) });
        Assert.Equal((0, 0), turned.SubAt(1, 0));
        Assert.Equal((1, 0), turned.SubAt(2, 0));
        Assert.Equal(1, turned.PlacementAt(1, 0));
        Assert.Equal(TileOrientation.MirroredInX(6), turned.PlacementAt(0, 0));
        Assert.Equal([new TilePiece(0, 1, 1, 0, 2, 0, Whole: true), new TilePiece(1, TileOrientation.MirroredInX(6), 0, 0, 0, 0, Whole: true)],
            turned.Pieces(0, 0, 3, 1, id => id == 0 ? (2, 1) : (1, 1)).OrderBy(one => one.Id));
    }

    /// <summary>
    /// A built grid's tile sizes come from every tile the build could lay - the master's fill list's as well as the pool's - so a 3x3
    /// fill tile turned over stays one whole piece rather than three columns each naming the first.
    /// </summary>
    [Fact]
    public void ABUILTGridsFillTilesKeepTheirSizeTurnedOver()
    {
        RoomBuildTile pooled = Tile("Pooled", 1, "", "", "", "");
        RoomBuildTile fill = Tile("Building_Fill_01", 1, "", "", "", "", width: 3, height: 3);
        string[] paths = [fill.Path.Replace('/', '\\'), pooled.Path, "Metadata/Terrain/Blank.tdt"];

        Func<int, (int Width, int Height)> sizes = BuiltRoomModels.SizesOf(paths, [pooled, fill]);

        Assert.Equal((3, 3), sizes(0));
        Assert.Equal((1, 1), sizes(1));
        Assert.Equal((1, 1), sizes(2));
        Assert.Equal((1, 1), sizes(7));

        // The fill tile laid as filed over the whole of a 3x3 grid, its tiles naming their places: turned over, still whole.
        var ids = new int[9];
        var subX = new byte[9];
        var subY = new byte[9];
        for (var at = 0; at < 9; at++)
        {
            (subX[at], subY[at]) = ((byte)(at % 3), (byte)(at / 3));
        }

        var tiles = new TerrainTiles(paths, ids, subX, subY, [.. Enumerable.Repeat((sbyte)3, 9)], 3, 3);
        TerrainTiles turned = tiles.MirroredInX(sizes);
        Assert.Equal([new TilePiece(0, 1, 0, 0, 2, 2, Whole: true)], turned.Pieces(0, 0, 3, 3, sizes));
    }
}
