using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>The "around you" block's key: written and read back, kept inside the area, and told apart from a tile's and a room's.</summary>
public class AreaBlockTests
{
    private const int TilesX = 20, TilesY = 12;

    [Fact]
    public void AKEYReadsBackAsItWasWrittenWithTheAreasMasters()
    {
        var block = new AreaBlock(4, 7, 9, -1234, ["Metadata/Terrain/Gallows/Act1/1_5/master.tsi", "Metadata/Terrain/WorldMaps/Act1/master.tsi"]);
        string key = block.ToString();
        Assert.Equal("around you|at=4,7,9,-1234+masters=Metadata/Terrain/Gallows/Act1/1_5/master.tsi;Metadata/Terrain/WorldMaps/Act1/master.tsi", key);
        Assert.True(AreaBlock.IsKey(key));
        Assert.True(AreaBlock.TryRead(key, out AreaBlock read));
        Assert.Equal((4, 7, 9, -1234), (read.X, read.Y, read.Size, read.Area));
        Assert.Equal(block.Masters, read.Masters);

        // NONE IS A TILE'S OR A ROOM'S KEY, nor a block of a side the slider does not offer.
        Assert.False(AreaBlock.IsKey("Metadata/Terrain/Woods/Features_RedvaleRiver/curve5.tdt"));
        Assert.False(AreaBlock.IsKey("Metadata/Terrain/Gallows/Act1/1_5/Rooms/curve5.arm|laid=1,1,0,5"));
        Assert.False(AreaBlock.TryRead("around you|at=4,7,99,5", out _));
        Assert.False(AreaBlock.TryRead("around you|masters=a.tsi", out _));
        Assert.True(AreaBlock.TryRead("around you|at=0,0,3,5", out AreaBlock bare));
        Assert.Empty(bare.Masters);
    }

    /// <summary>The rooms the area placed ride in the key, those reaching the block alone, each read back where it stands.</summary>
    [Fact]
    public void THEPLACEDRoomsReachingTheBlockRideInItsKey()
    {
        IReadOnlyList<(string Room, int X, int Y, int Turn)> rooms = AreaBlock.RoomsIn(10, 10, 5, [
            ("Metadata/Terrain/A/Rooms/inside.arm", 11, 11, 3, 2, 2),
            ("Metadata\\Terrain\\A\\Rooms\\edge.arm", 7, 12, 6, 4, 1),
            ("Metadata/Terrain/A/Rooms/outside.arm", 15, 10, 0, 3, 3),
            ("Metadata/Terrain/A/Rooms/above.arm", 10, 4, 0, 5, 6),
        ]);
        Assert.Equal([("Metadata/Terrain/A/Rooms/inside.arm", 11, 11, 3), ("Metadata/Terrain/A/Rooms/edge.arm", 7, 12, 6)], rooms);

        var block = new AreaBlock(10, 10, 5, 9, [], rooms);
        Assert.True(AreaBlock.TryRead(block.ToString(), out AreaBlock read));
        Assert.Equal(rooms, read.Rooms);
        Assert.EndsWith("+rooms=Metadata/Terrain/A/Rooms/inside.arm@11,11,3;Metadata/Terrain/A/Rooms/edge.arm@7,12,6", block.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the player stood when the block was taken rides in its key and reads back; the key without it - what a stage's build is kept
    /// under - is the block's key as offered, and a place that does not read is no place.
    /// </summary>
    [Fact]
    public void WHERETHEPlayerStoodRidesInTheKeyAndTheBuildIsKeptWithoutIt()
    {
        var offered = new AreaBlock(4, 17, 25, 77, ["Metadata/Terrain/Gallows/Act1/1_5/master.tsi"], [("Metadata/Terrain/A/Rooms/inside.arm", 5, 18, 2)]);
        string taken = (offered with { Stood = new Vector2(4081.842f, 7314.178f) }).ToString();
        Assert.EndsWith("+rooms=Metadata/Terrain/A/Rooms/inside.arm@5,18,2+stood=4081.84,7314.18", taken, StringComparison.Ordinal);
        Assert.True(AreaBlock.TryRead(taken, out AreaBlock read));
        Assert.Equal(new Vector2(4081.84f, 7314.18f), read.Stood);
        Assert.Equal((4, 17, 25, 77), (read.X, read.Y, read.Size, read.Area));

        // THE BUILD'S KEY IS THE BLOCK'S AS OFFERED, wherever in it the player stood - and a key naming no place, or no block, is its own.
        Assert.Equal(offered.ToString(), AreaBlock.Unstood(taken));
        Assert.Equal(offered.ToString(), AreaBlock.Unstood((offered with { Stood = new Vector2(-3f, 5.5f) }).ToString()));
        Assert.Equal(offered.ToString(), AreaBlock.Unstood(offered.ToString()));
        Assert.Equal("Metadata/Terrain/Woods/curve5.tdt", AreaBlock.Unstood("Metadata/Terrain/Woods/curve5.tdt"));
        Assert.True(AreaBlock.TryRead(offered.ToString(), out AreaBlock none));
        Assert.Null(none.Stood);
        Assert.True(AreaBlock.TryRead("around you|at=0,0,3,5+stood=a,7", out AreaBlock unread));
        Assert.Null(unread.Stood);
    }

    /// <summary>
    /// The height of the ground the player stood on rides in the place's word and reads back; a word of x and y alone, as v0.1.373 and v0.1.374
    /// wrote it, still reads, its height unknown; a word whose third number does not read is no place; and the build's key drops the height
    /// with the place, so a block taken again at another height lays no tile again.
    /// </summary>
    [Fact]
    public void THEHEIGHTOfTheGroundHeStoodOnRidesWithThePlaceAndAnOlderKeyStillReads()
    {
        var offered = new AreaBlock(4, 17, 25, 77, ["Metadata/Terrain/Gallows/Act1/1_5/master.tsi"]);
        AreaBlock stood = offered with { Stood = new Vector2(4081.842f, 7314.178f), StoodHeight = -246.538f };
        string taken = stood.ToString();
        Assert.EndsWith("+masters=Metadata/Terrain/Gallows/Act1/1_5/master.tsi+stood=4081.84,7314.18,-246.54", taken, StringComparison.Ordinal);
        Assert.True(AreaBlock.TryRead(taken, out AreaBlock read));
        Assert.Equal(new Vector2(4081.84f, 7314.18f), read.Stood);
        Assert.Equal(-246.54f, read.StoodHeight);
        Assert.Equal(taken, read.ToString());

        // AN OLDER KEY'S TWO NUMBERS: the place, its height unknown - and written back as it was.
        string older = offered.ToString() + "+stood=300,420";
        Assert.True(AreaBlock.TryRead(older, out AreaBlock old));
        Assert.Equal(new Vector2(300f, 420f), old.Stood);
        Assert.Null(old.StoodHeight);
        Assert.Equal(older, old.ToString());

        // A WORD THAT DOES NOT READ IS NO PLACE, a third number too - not a place without its height.
        foreach (string word in new[] { "300,420,a", "300,420,", "300,420,NaN", "300,420,5,6", "300" })
        {
            Assert.True(AreaBlock.TryRead(offered.ToString() + "+stood=" + word, out AreaBlock bad));
            Assert.Null(bad.Stood);
            Assert.Null(bad.StoodHeight);
        }

        // THE BUILD'S KEY DROPS THE HEIGHT WITH THE PLACE: two heights at one place, and none, are one block.
        Assert.Equal(offered.ToString(), AreaBlock.Unstood(taken));
        Assert.Equal(offered.ToString(), AreaBlock.Unstood((stood with { StoodHeight = 12f }).ToString()));
        Assert.Equal(offered.ToString(), AreaBlock.Unstood(older));

        // A HEIGHT THAT IS NO NUMBER, or one with no place, is not written: the place's word stays as an older key's, or there is none.
        Assert.EndsWith("+stood=4081.84,7314.18", (stood with { StoodHeight = float.NaN }).ToString(), StringComparison.Ordinal);
        Assert.Equal(offered.ToString(), (offered with { StoodHeight = 5f }).ToString());
    }

    [Fact]
    public void THEBLOCKIsCentredOnTheTileYouStandOnAndKeptInsideTheArea()
    {
        const int stride = ((TilesX * TerrainGrid.CellsPerTile) + 1) / 2;
        var grid = new TerrainGrid(new byte[stride * TilesY * TerrainGrid.CellsPerTile], stride, TilesY * TerrainGrid.CellsPerTile, TilesX, TilesY, heights: null,
            tiles: new TerrainTiles(["a.tdt"], new int[TilesX * TilesY], new byte[TilesX * TilesY], new byte[TilesX * TilesY], new sbyte[TilesX * TilesY], TilesX, TilesY));

        AreaBlock middle = AreaBlock.Around(10, 6, 5, grid, []);
        Assert.Equal((8, 4, 5), (middle.X, middle.Y, middle.Size));
        Assert.Equal(RoomKey.Stamp(grid), middle.Area);

        // AT THE EDGE IT IS MOVED IN, not cut; past the slider's ends it is held to them.
        Assert.Equal((0, 0), (AreaBlock.Around(1, 0, 9, grid, []).X, AreaBlock.Around(1, 0, 9, grid, []).Y));
        Assert.Equal((TilesX - 9, TilesY - 9), (AreaBlock.Around(19, 11, 9, grid, []).X, AreaBlock.Around(19, 11, 9, grid, []).Y));
        Assert.Equal(AreaBlock.MostSize, AreaBlock.Around(10, 6, 99, grid, []).Size);
        Assert.Equal(AreaBlock.LeastSize, AreaBlock.Around(10, 6, 1, grid, []).Size);
    }

    [Fact]
    public void THEMASTERSAreTheAreasRoomsOwnEachOnce()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Metadata/Terrain/Gallows/Act1/1_5/master.tsi"] = [1],
            ["Metadata/Terrain/WorldMaps/Act1/master.tsi"] = [1],
        };
        IReadOnlyList<string> masters = AreaBlock.MastersOf(files.GetValueOrDefault, [
            "Metadata/Terrain/Gallows/Act1/1_5/Rooms/curve5.arm",
            "Metadata/Terrain/Gallows/Act1/1_5/Rooms/fills/fill_bodies_2.arm",
            "Metadata/Terrain/WorldMaps/Act1/Rooms/Unique/region_graveyard_04.arm",
            "Metadata/Terrain/Elsewhere/Rooms/no_master.arm",
            "Metadata/Terrain/NoRooms/odd.arm",
        ]);
        Assert.Equal(["Metadata/Terrain/Gallows/Act1/1_5/master.tsi", "Metadata/Terrain/WorldMaps/Act1/master.tsi"], masters);
    }
}
