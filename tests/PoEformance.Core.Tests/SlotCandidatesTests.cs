using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// A room's slots held against a tileset's tiles: which kinds one tile settles, which the generator chooses among - the measure behind building a room without its area.
/// </summary>
public class SlotCandidatesTests
{
    private const string Edge = "Metadata/Terrain/Woods/edge.et";
    private const string Ground = "Metadata/Terrain/Woods/ground.gt";

    /// <summary>A two-slot room in the shape RoomLayoutTests reads: the first slot names an edge and a ground type on every side and corner, the second names nothing.</summary>
    private const string Room = """
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
        k 1 1 1 1 1 1 0 0 0 0 0 0 0 0 2 2 2 2 0 0 0 0 0 k 1 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0
        0
        0
        0
        """;

    private static TileIdentity Tile(int side, string edge, string ground)
        => new(side, side, string.Empty, [edge, edge, edge, edge], [ground, ground, ground, ground]);

    [Fact]
    public void EachKindOfSlotIsAnsweredByTheTilesAlikeToIt()
    {
        RoomLayout layout = RoomLayout.Parse(Room);
        Assert.True(layout.Ready, layout.Why);

        (string, TileIdentity?)[] tiles =
        [
            ("Metadata/Terrain/Woods/Tiles/floor.tdt", Tile(1, Edge, Ground)),
            ("Metadata/Terrain/Woods/Tiles/plain.tdt", Tile(1, string.Empty, string.Empty)),
            ("Metadata/Terrain/Woods/Tiles/big.tdt", Tile(2, Edge, Ground)),
            ("Metadata/Terrain/Woods/Tiles/unread.tdt", null),
        ];

        RoomSlotCandidates found = SlotCandidates.Of("Metadata/Terrain/Woods/Rooms/arena.arm", layout, tiles);

        Assert.Equal(string.Empty, found.Why);
        Assert.Equal(2, found.Kinds.Count);
        Assert.Equal(2, found.Slots);

        // The named slot admits only the tile naming the same types; the unnamed one admits every one-by-one tile.
        Assert.Equal(["Metadata/Terrain/Woods/Tiles/floor.tdt"], found.Kinds[0].Tiles);
        Assert.Equal(["Metadata/Terrain/Woods/Tiles/floor.tdt", "Metadata/Terrain/Woods/Tiles/plain.tdt"], found.Kinds[1].Tiles);
        Assert.False(found.Kinds[0].Kind.Big);
        Assert.Contains("edge", found.Kinds[0].Kind.Said, StringComparison.Ordinal);

        Assert.Equal(1, found.One);
        Assert.Equal(1, found.Several);
        Assert.Equal(0, found.None);
        Assert.Equal(1, found.SlotsSettled);
        Assert.False(found.Settled);
        Assert.Equal("2 slots of 2 kinds · one tile answers 1 kinds (1 slots), several 1, none 0", SlotCandidates.Said(found));

        // With the plain tile gone every kind has one tile, and the room is settled by the files alone.
        RoomSlotCandidates settled = SlotCandidates.Of("arena.arm", layout, [tiles[0], tiles[2]]);
        Assert.True(settled.Settled);
        Assert.EndsWith("settled by the files alone", SlotCandidates.Said(settled), StringComparison.Ordinal);
    }

    [Fact]
    public void ARoomThatDidNotReadSaysSo()
    {
        RoomSlotCandidates found = SlotCandidates.Of("broken.arm", RoomLayout.Read(null), []);
        Assert.Empty(found.Kinds);
        Assert.StartsWith("the room did not read", found.Why, StringComparison.Ordinal);
        Assert.Equal(found.Why, SlotCandidates.Said(found));
    }

    [Fact]
    public void TheAreasMastersAreTheLoadedOnesThenTheIndexsInItsFolders()
    {
        string[] loaded = ["Metadata/Terrain/Maps/X/Rooms/a.arm", "Metadata/Terrain/Maps/X/master.tsi", "Metadata/Terrain/Maps/X/master.tsi"];
        string[] index =
        [
            "Metadata/Terrain/Maps/X/master.tsi",
            "Metadata/Terrain/Maps/X/other.tsi",
            "Metadata/Terrain/Maps/X/Graphs/deeper.tsi",
            "Metadata/Terrain/Maps/Y/elsewhere.tsi",
        ];

        Assert.Equal(["Metadata/Terrain/Maps/X/master.tsi", "Metadata/Terrain/Maps/X/other.tsi"], AreaRoomSet.Masters(loaded, index));
        Assert.Empty(AreaRoomSet.Masters(["Metadata/Monsters/x.ao"], index));
    }
}
