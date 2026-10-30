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
