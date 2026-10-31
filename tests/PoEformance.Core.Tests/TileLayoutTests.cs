using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The tile definition walked whole by zao's layout, and the edge type by poe_data_tools' grammar.
/// </summary>
/// <remarks>
/// WHAT THESE CAN SETTLE: that the walk is zao's <c>tdt.cpp</c> field for field and that it refuses what a misplaced field produces -
/// a file a byte too long or too short, a sub-tile type he does not read. Whether his layout is PoE2's is the capture's tile-layout
/// part to count, over the install's own files; no real tile file is committed here.
/// </remarks>
public sealed class TileLayoutTests
{
    private static readonly string[] Strings =
        ["", "Art/Models/Terrain/Test/Arena.tgt", "arena_tag", "Metadata/Terrain/Test/wall.et", "Metadata/Terrain/Test/sand.gt", "Art/Models/Terrain/Test/Arena.tmd", "Art/Models/Terrain/Test/Sub.tgt"];

    /// <summary>A definition laid out as zao's reader reads it: version, table, references, size, grounds, then everything past them.</summary>
    private static byte[] Tdt(int version = 7, byte flags = TileLayout.FlagGrid | TileLayout.FlagHeights, byte type = 0x03, int width = 2, int height = 1, int extra = 0)
    {
        var table = new List<byte>();
        var starts = new uint[Strings.Length];
        for (var one = 0; one < Strings.Length; one++)
        {
            starts[one] = (uint)(table.Count / 2);
            table.AddRange(Encoding.Unicode.GetBytes(Strings[one]));
            table.AddRange([0, 0]);
        }

        using var stream = new MemoryStream();
        using var write = new BinaryWriter(stream);
        write.Write(version);
        write.Write(table.Count / 2);
        write.Write(table.ToArray());
        if (version >= 7)
        {
            write.Write(starts[0]);
        }

        if (version > 4)
        {
            write.Write(starts[1]);
        }

        write.Write(starts[2]);
        write.Write(starts[3]);
        write.Write(starts[0]);
        write.Write(starts[3]);
        write.Write(starts[0]);
        write.Write((sbyte)width);
        write.Write((sbyte)height);
        for (var corner = 0; corner < 4; corner++)
        {
            write.Write(starts[4]);
        }

        write.Write((ushort)0);
        write.Write((sbyte)width);
        write.Write((sbyte)height);
        write.Write(new byte[] { 1, 6, 1, 6, 3, 3, 3, 3 });
        write.Write(flags);
        write.Write((ushort)(width * height));
        for (var sub = 0; sub < width * height; sub++)
        {
            write.Write(type);
            write.Write((byte)sub);
            if (type is 0x00 or 0x03)
            {
                write.Write(Enumerable.Repeat((byte)7, TileLayout.Cells * TileLayout.Cells).ToArray());
            }

            if (type is 0x00 or 0x04)
            {
                write.Write((ushort)3);
                write.Write(new byte[] { 9, 8, 7 });
            }

            if (version == 4)
            {
                write.Write(starts[sub == 0 ? 1 : 6]);
            }
        }

        if ((flags & TileLayout.FlagExtra) != 0)
        {
            write.Write(5u);
            for (var one = 0; one < 6; one++)
            {
                write.Write(one * 0.5f);
            }
        }

        int corners = (width + 1) * (height + 1);
        if ((flags & TileLayout.FlagGrid) != 0)
        {
            for (var one = 0; one < corners; one++)
            {
                write.Write(one == 0 ? uint.MaxValue : starts[4]);
            }
        }

        if ((flags & TileLayout.FlagHeights) != 0)
        {
            for (var one = 0; one < corners; one++)
            {
                write.Write((byte)(one * 10));
            }
        }

        if (version >= 6)
        {
            write.Write(starts[5]);
        }

        if (version >= 7)
        {
            write.Write(starts[0]);
            write.Write((byte)1);
        }

        write.Write(new byte[extra]);
        return stream.ToArray();
    }

    [Fact]
    public void AFILELaidOutAsZaosIsWalkedToItsLastByte()
    {
        TileLayout layout = TileLayout.Read(Tdt());

        Assert.True(layout.Whole, layout.Stop);
        Assert.Equal(7, layout.Version);
        Assert.Equal("Art/Models/Terrain/Test/Arena.tgt", layout.Template?.Text);
        Assert.Equal((2, 1), (layout.Width, layout.Height));
        Assert.Equal([1, 6, 1, 6, 3, 3, 3, 3], layout.SideOffsets);
        Assert.Equal(2, layout.SubTiles.Count);
        Assert.All(layout.SubTiles, sub => Assert.Equal(TileLayout.Cells * TileLayout.Cells, sub.Fixed!.Length));
        Assert.Equal(6, layout.GroundGrid.Count);
        Assert.Null(layout.GroundGrid[0].Text);
        Assert.Equal("Metadata/Terrain/Test/sand.gt", layout.GroundGrid[5].Text);
        Assert.Equal([0, 10, 20, 30, 40, 50], layout.Heights);
        Assert.Equal((0, 20, 50, 30), layout.CornerHeights);
        Assert.Equal("Art/Models/Terrain/Test/Arena.tmd", layout.Tmd?.Text);
        Assert.Equal(1, layout.LastByte);
        Assert.Empty(layout.Refused);
    }

    [Fact]
    public void ABYTETooManyOrTooFewIsNotWhole()
    {
        TileLayout longer = TileLayout.Read(Tdt(extra: 1));
        Assert.False(longer.Whole);
        Assert.Contains("1 bytes past", longer.Stop, StringComparison.Ordinal);

        byte[] whole = Tdt();
        TileLayout shorter = TileLayout.Read(whole[..^1]);
        Assert.False(shorter.Whole);
        Assert.Contains("needs 1 bytes, 0 are left", shorter.Stop, StringComparison.Ordinal);
        Assert.True(shorter.End <= whole.Length - 1);
    }

    [Fact]
    public void ASUBTileTypeZaoDoesNotReadStopsTheWalkThere()
    {
        TileLayout layout = TileLayout.Read(Tdt(type: 0x05));

        Assert.False(layout.Whole);
        Assert.Contains("type 0x05", layout.Stop, StringComparison.Ordinal);
        Assert.Empty(layout.SubTiles);
    }

    [Fact]
    public void EVERYSubTileTypesBlocksAndTheExtraBlockAreStepped()
    {
        Assert.True(TileLayout.Read(Tdt(type: 0x00)).Whole);
        Assert.True(TileLayout.Read(Tdt(type: 0x04)).Whole);
        Assert.True(TileLayout.Read(Tdt(type: 0x07)).Whole);

        TileLayout extra = TileLayout.Read(Tdt(flags: TileLayout.FlagExtra));
        Assert.True(extra.Whole, extra.Stop);
        Assert.Equal(5u, extra.ExtraWord);
        Assert.Equal(2.5f, extra.ExtraFloats[5]);
        Assert.Empty(extra.GroundGrid);
        Assert.Empty(extra.Heights);
    }

    [Fact]
    public void VERSIONFourCarriesATemplateASubTile()
    {
        TileLayout layout = TileLayout.Read(Tdt(version: 4));

        Assert.True(layout.Whole, layout.Stop);
        Assert.Null(layout.Template);
        Assert.Null(layout.Tmd);
        Assert.Equal(["Art/Models/Terrain/Test/Arena.tgt", "Art/Models/Terrain/Test/Sub.tgt"], layout.SubTiles.Select(sub => sub.Template?.Text));
    }

    [Fact]
    public void THEREPORTCountsTheWholeWalksAndTheToolsGridBesideZaos()
    {
        byte[] edge = Encoding.UTF8.GetBytes("WallEdge #ff0000\nMetadata/Terrain/Test/sand.gt\nwildcard\n1 2 0 1\nvirtual\nMetadata/Terrain/Test/a.et 1\nMetadata/Terrain/Test/b.et 0\n0 2\n");
        string report = CaptureTileLayout.Report(
            [("Metadata/Terrain/Test/Arena.tdt", Tdt(), "listed"), ("Metadata/Terrain/Test/Bad.tdt", Tdt(extra: 2), "laid")],
            [("Metadata/Terrain/Test/wall.et", edge, "tiles")],
            "test\n");

        Assert.StartsWith("tile layout: 2 tile definitions walked by zao's layout (poe-cpp, PoE1, 2022) - 1 to their last byte exactly, 1 not; 1 edge types, 1 read whole", report, StringComparison.Ordinal);
        Assert.Contains("version 7 flags 0x03 (heights, ground grid): 2 files, 1 whole, 1 not", report, StringComparison.Ordinal);
        Assert.Contains("both, the same: 1;", report, StringComparison.Ordinal);
        Assert.Contains("WallEdge (Metadata/Terrain/Test/wall.et): Metadata/Terrain/Test/a.et 1, Metadata/Terrain/Test/b.et 0; numbers 0 2", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ANEDGETypeReadsByPoeDataToolsGrammarAndSaysWhereItStops()
    {
        EdgeTypeFile plain = EdgeTypeFile.Parse("mud_edge\n\"Metadata/Terrain/Test/mud.gt\"\nMetadata/Terrain/Test/sand.gt\n3 1 1\nMetadata/Terrain/Test/middle.gt\n");
        Assert.Equal(string.Empty, plain.Why);
        Assert.Equal("mud_edge", plain.Name);
        Assert.Equal(["Metadata/Terrain/Test/mud.gt", "Metadata/Terrain/Test/sand.gt"], plain.Grounds);
        Assert.Equal(new EdgeTypeNumbers(3, 1, true, null, null, null), plain.Numbers);
        Assert.Equal("Metadata/Terrain/Test/middle.gt", plain.OtherGround);
        Assert.Empty(plain.Virtual);

        // zao's "wetNums", four numbers where poe_data_tools has nothing: kept as where the reading stopped, for the capture to count.
        EdgeTypeFile wet = EdgeTypeFile.Parse("river\nwildcard\nwildcard\n1 2 0 0 0 0\n4 5 6 7\n");
        Assert.NotEqual(string.Empty, wet.Why);
    }
}
