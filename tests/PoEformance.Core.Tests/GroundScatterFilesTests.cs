using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The ground's scatter files read as poe_data_tools' parsers read them: a doodad layer (<c>.dlp</c>) in both header forms, and The Red
/// Vale's own doodad list (<c>.ddt</c>), as v0.1.359's capture wrote it.
/// </summary>
public class GroundScatterFilesTests
{
    [Fact]
    public void ADOODADLayerOfVersionThreeReadsItsHeadersAndMeshes()
    {
        DoodadLayer layer = DoodadLayer.Parse("""
            version 3
            - RandomScale 0.8 1.2
            - AllowWaving
            - MaxRotation 360
            - SizeMultiplier 1.5
            - Seed 7
            // a comment
            "Art/Models/Terrain/Doodads/Grass/grass_01.fmt" 0.5 (0,0.2) (0.5,1)

            "Art/Models/Terrain/Doodads/Grass/grass_02.fmt" 1
            """);

        Assert.True(layer.Ready, layer.Why);
        Assert.Equal(3, layer.Version);
        Assert.Equal((0.8f, 1.2f), (layer.ScaleMin, layer.ScaleMax));
        Assert.True(layer.AllowWaving);
        Assert.False(layer.AllowOnBlocking);
        Assert.Equal(360, layer.MaxRotation);
        Assert.Equal(7, layer.Seed);
        Assert.Equal([("SizeMultiplier", "1.5")], layer.Headers);
        Assert.Equal(2, layer.Entries.Count);
        Assert.Equal("Art/Models/Terrain/Doodads/Grass/grass_01.fmt", layer.Entries[0].Mesh);
        Assert.Equal(0.5f, layer.Entries[0].Number);
        Assert.Equal([(0f, 0.2f), (0.5f, 1f)], layer.Entries[0].Points);
        Assert.Empty(layer.Entries[1].Points);
    }

    [Fact]
    public void ADOODADLayerBelowVersionThreeReadsItsOneHeaderLine()
    {
        DoodadLayer layer = DoodadLayer.Parse("""
            version 2
            0.7 1.3 1 0 180 4 5 0.25 2 0.5
            "a\b.fmt" 0.3 [0 0.1] [1 0.9]
            """);

        Assert.True(layer.Ready, layer.Why);
        Assert.Equal((0.7f, 1.3f), (layer.ScaleMin, layer.ScaleMax));
        Assert.True(layer.AllowWaving);
        Assert.Equal(180, layer.MaxRotation);
        Assert.Equal([("uint1", "4"), ("uint2", "5"), ("float1", "0.25"), ("AudioType", "2"), ("float2", "0.5")], layer.Headers);
        Assert.Equal("a/b.fmt", layer.Entries[0].Mesh);
        Assert.Equal([(0f, 0.1f), (1f, 0.9f)], layer.Entries[0].Points);

        // A LINE THAT IS NO MESH stops the read and says where.
        Assert.StartsWith("line 3 does not read as a mesh", DoodadLayer.Parse("0 1 0 0\n\"a.fmt\" 1\nnonsense").Why, StringComparison.Ordinal);
    }

    /// <summary>The Red Vale's doodads.ddt, as v0.1.359's capture wrote it whole (shortened): a group with none of its own before the next.</summary>
    [Fact]
    public void THEREDValesDoodadListReadsItsGroups()
    {
        GroundDoodadList list = GroundDoodadList.Parse("""
            version 3
            1
            Default
            5 "Metadata/Terrain/Doodads/Woods/RedVale/RustedChest.ao"
            5 "Metadata/Terrain/Doodads/Woods/RedVale/RustedHelm1.ao"

            "WoodsSlope"
            "ShallowWater"
            5 "Metadata/Terrain/Doodads/Woods/RedVale/RustedChest.ao"
            "Stream"
            1 "Metadata/Terrain/Doodads/Woods/Clutter/Clutter_51_2.ao"
            All "Metadata/Terrain/Doodads/Woods/Clutter/Clutter_53_1.ao" 3 D 0.5
            """);

        Assert.True(list.Ready, list.Why);
        Assert.Equal((3, "1", string.Empty), (list.Version, list.First, list.Second));
        Assert.Equal(["Default", "WoodsSlope", "ShallowWater", "Stream"], list.Groups.Select(one => one.Name));
        Assert.Equal([2, 0, 1, 2], list.Groups.Select(one => one.Doodads.Count));
        Assert.Equal(5f, list.Groups[0].Doodads[0].Weight);
        Assert.Null(list.Groups[3].Doodads[1].Weight);
        Assert.Equal("3 D 0.5", list.Groups[3].Doodads[1].Rest);
    }
}
