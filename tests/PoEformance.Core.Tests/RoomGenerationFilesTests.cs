using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The files a master names for the generator's own rules - its edge combinations (.ecf) and its overlays (.toy) - and the room lattice its numbers make.
/// </summary>
/// <remarks>The texts are Augury's, Marrow's and Sulphuric Caverns', as their captures' area-masters.txt writes them whole.</remarks>
public class RoomGenerationFilesTests
{
    /// <summary>Three quoted edge types a line, an empty one among them, and the number after them where there is one - as poe_data_tools reads them.</summary>
    [Fact]
    public void ANEDGECombinationLineIsTwoEdgesWhatTheyMakeAndANumber()
    {
        EdgeCombinations read = EdgeCombinations.Parse(
            "version 1\r\n"
            + "\"Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_thickwall.et\" \"Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_thickwall.et\" \"\" 0\r\n"
            + "// a comment\r\n"
            + "\"Metadata\\Terrain\\Islands\\Tiles\\CoralDungeon\\coral_thickwall.et\" \"Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_thickwall.et\" \"Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_ledge.et\" 1\r\n"
            + "\"Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_thinwall.et\" \"Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_thinwall.et\" \"Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_thinwall.et\"\r\n"
            + "\"only two\" \"\"\r\n");

        Assert.Equal(1, read.Version);
        Assert.Equal(3, read.Lines.Count);
        Assert.Equal(
            new EdgeCombination("Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_thickwall.et", "Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_thickwall.et", string.Empty, 0),
            read.Lines[0]);
        Assert.Equal(("Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_thickwall.et", "Metadata/Terrain/Islands/Tiles/CoralDungeon/coral_ledge.et", 1), (read.Lines[1].First, read.Lines[1].Result, read.Lines[1].Number));
        Assert.Null(read.Lines[2].Number);
        Assert.Same(EdgeCombinations.None, EdgeCombinations.Read(null));
    }

    /// <summary>A group's header, then its rooms with their weights and words, kept as written - Augury's list and Marrow's.</summary>
    [Fact]
    public void ANOVERLAYListIsGroupsOfWeightedRoomsAndTheirWords()
    {
        OverlayList augury = OverlayList.Parse(
            "version 1\n0 1\n"
            + "100 \"Metadata/Terrain/Maps/Augury/Rooms/Overlays/boss_entrance_blank.arm\" limit=1 !door_boss +OverlayFeatureRooms +OverlayFeatureTiles +SuppressUnderlyingDoodads\n"
            + "100 \"Metadata/Terrain/Maps/Augury/Rooms/Overlays/ledge_intersection_1.arm\" +SuppressUnderlyingDoodads\n"
            + "1 1 SizeOrder -Flag\n"
            + "\n"
            + "5 \"Metadata/Terrain/Maps/Augury/Rooms/Overlays/other.arm\" FR90\n");

        Assert.Equal(1, augury.Version);
        Assert.Equal(["0 1", "1 1 SizeOrder -Flag"], augury.Headers);
        Assert.Equal(3, augury.Entries.Count);

        OverlayEntry boss = augury.Entries[0];
        Assert.Equal((0, 100, "Metadata/Terrain/Maps/Augury/Rooms/Overlays/boss_entrance_blank.arm"), (boss.Group, boss.Weight, boss.Room));
        Assert.Equal(["limit=1", "!door_boss", "+OverlayFeatureRooms", "+OverlayFeatureTiles", "+SuppressUnderlyingDoodads"], boss.Words);
        Assert.True(boss.Says("+suppressunderlyingdoodads"));
        Assert.False(augury.Entries[1].Says("limit=1"));
        Assert.Equal((1, 5), (augury.Entries[2].Group, augury.Entries[2].Weight));
        Assert.Equal(["FR90"], augury.Entries[2].Words);
    }

    /// <summary>A lattice's places: from the buffer, every side less the overlap - Augury's every four tiles from four; and the first such place at or after nought.</summary>
    [Fact]
    public void THELATTICEHoldsTheBufferAndEveryStepFromIt()
    {
        var augury = new RoomLattice(5, 1, 4);
        Assert.Equal(4, augury.Step);
        Assert.True(augury.Holds(4, 28));
        Assert.True(augury.Holds(0, 44));
        Assert.False(augury.Holds(42, 12));
        Assert.Equal(0, augury.First);

        var marrow = new RoomLattice(9, 1, 10);
        Assert.True(marrow.Holds(58, 10));
        Assert.False(marrow.Holds(58, 12));
        Assert.Equal(2, marrow.First);
        Assert.Equal("every 8 tiles from 10", marrow.Said);

        Assert.False(new RoomLattice(1, 1, 0).Holds(0, 0));
    }

    /// <summary>
    /// A room is held to the lattice of its own module - its dimension line and its grid - and to none where the masters naming its module disagree, or none names it.
    /// </summary>
    [Fact]
    public void AROOMIsHeldToTheLatticeOfItsOwnModuleAlone()
    {
        RoomLayout small = Room("5 1", 5, 5);
        RoomLayout large = Room("5 1", 13, 9);
        RoomLayout fill = Room("3 0", 3, 3);
        RoomLayout odd = Room("5 1", 6, 5);

        RoomLattices lattices = RoomLattices.From([MasterFile.Parse("RoomUnitSize 5\nRoomOverlap 1\nOuterBufferSize 4\n"), MasterFile.Parse("RoomSet generate.rs\n")]);
        Assert.Equal(new RoomLattice(5, 1, 4), lattices.For(small));
        Assert.Equal(new RoomLattice(5, 1, 4), lattices.For(large));
        Assert.Null(lattices.For(fill));
        Assert.Null(lattices.For(odd));

        RoomLattices disagree = RoomLattices.From([MasterFile.Parse("RoomUnitSize 5\nRoomOverlap 1\nOuterBufferSize 4\n"), MasterFile.Parse("RoomUnitSize 5\nRoomOverlap 1\nOuterBufferSize 5\n")]);
        Assert.Equal(2, disagree.All.Count);
        Assert.Null(disagree.For(small));

        Assert.Empty(RoomLattices.From([MasterFile.None]).All);
        Assert.Null(RoomLattices.None.For(small));
    }

    /// <summary>
    /// The dimension line's overlap is a version 22 field: an older room names its module's side alone.
    /// </summary>
    [Fact]
    public void ANOLDERRoomNamesItsSideAlone()
    {
        RoomLayout old = Room("7 7", 7, 7, version: 21);
        Assert.Equal((7, -1), (old.ModuleSide, old.ModuleOverlap));
        Assert.False(new RoomLattice(7, 1, 1).Fits(old));
    }

    /// <summary>A room of the module and grid given, every slot empty and no doodad - the module and the size are what a lattice asks of it.</summary>
    private static RoomLayout Room(string module, int width, int height, int version = 36)
    {
        // Point-of-interest groups: six from 29, ten below 26; a group is a count below 32 and lines up to -1 from it.
        bool terminated = version >= 32;
        int groups = version >= 29 ? 6 : 10;
        var lines = new List<string> { $"version {version}", "0", module, "0", "\"\"", "0", $"k {width} {height} 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0" };
        lines.AddRange(Enumerable.Repeat(terminated ? "-1" : "0", groups));
        if (version >= 35)
        {
            lines.Add("\"\"");
        }

        lines.AddRange(Enumerable.Repeat(string.Join(' ', Enumerable.Repeat("n", width)), height));
        lines.Add(terminated ? "-1" : "0");
        RoomLayout room = RoomLayout.Parse(string.Join('\n', lines));
        Assert.True(room.Ready, room.Why);
        return room;
    }
}
