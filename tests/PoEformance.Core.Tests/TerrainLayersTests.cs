using PoEformance.Core.Schema;
using PoEformance.Features;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The terrain's two cell grids read raw against the shipped schema, and the capture's report of the second against the first.
/// </summary>
public class TerrainLayersTests
{
    private const ulong AreaInstance = 0x0000_0200_0000_0000;
    private const ulong Walkable = 0x0000_0200_0100_0000;
    private const ulong Landscape = 0x0000_0200_0200_0000;
    private const int Stride = 4;
    private const int Rows = 3;

    /// <summary>A grid of eight by three cells: the walkable one open in its left half, the landscape one 5 where it is open and 0 or 3 beyond.</summary>
    private static (FakeMemoryReader Reader, OffsetSchema Schema) Area(int landscapeBytes = Stride * Rows)
    {
        OffsetSchema schema = RealSessionTests.Schema();
        StructDef terrain = schema.Structs["TerrainMetadata"];
        ulong terrainBase = AreaInstance + (ulong)schema.Structs["AreaInstance"].OffsetOf("TerrainMetadata");

        // Two cells a byte, the even one low: cells 0-3 walkable (1), 4-7 not.
        byte[] walkable = [0x11, 0x11, 0x00, 0x00, 0x11, 0x11, 0x00, 0x00, 0x11, 0x11, 0x00, 0x00];
        byte[] landscape = [0x55, 0x55, 0x00, 0x33, 0x55, 0x55, 0x00, 0x33, 0x55, 0x55, 0x00, 0x33];
        landscape = landscape[..Math.Min(landscape.Length, landscapeBytes)];

        var fake = new FakeMemoryReader()
            .Place(Walkable, walkable)
            .Place(Landscape, landscape)
            .Place<ulong>(terrainBase + (ulong)terrain.OffsetOf("GridWalkableData"), Walkable)
            .Place<ulong>(terrainBase + (ulong)terrain.OffsetOf("GridWalkableData") + 8, Walkable + (ulong)walkable.Length)
            .Place<ulong>(terrainBase + (ulong)terrain.OffsetOf("GridLandscapeData"), Landscape)
            .Place<ulong>(terrainBase + (ulong)terrain.OffsetOf("GridLandscapeData") + 8, Landscape + (ulong)landscape.Length)
            .Place<int>(terrainBase + (ulong)terrain.OffsetOf("BytesPerRow"), Stride);
        return (fake, schema);
    }

    /// <summary>Both grids come back whole, in the walkable grid's packing, and the report counts one against the other cell by cell.</summary>
    [Fact]
    public void BOTHGridsAreReadAndCountedAgainstEachOther()
    {
        (FakeMemoryReader fake, OffsetSchema schema) = Area();
        TerrainLayers layers = TerrainLayers.Read(fake, schema, AreaInstance);

        Assert.True(layers.Ready, layers.Why);
        Assert.Equal((8, 3), (layers.Width, layers.Rows));
        Assert.Equal(1, TerrainLayers.Nibble(layers.Walkable, Stride, 3, 2));
        Assert.Equal(0, TerrainLayers.Nibble(layers.Walkable, Stride, 4, 2));
        Assert.Equal(5, TerrainLayers.Nibble(layers.Landscape, Stride, 0, 0));
        Assert.Equal(3, TerrainLayers.Nibble(layers.Landscape, Stride, 7, 1));
        Assert.Equal(0, TerrainLayers.Nibble(layers.Landscape, Stride, 8, 0));

        string report = CaptureLandscape.Report(layers, grid: null, playerX: null, playerY: null);
        Assert.StartsWith("landscape: walkable grid 12 bytes, landscape grid 12 bytes, 4 bytes a row - 8 x 3 cells", report, StringComparison.Ordinal);

        // Twelve walkable cells, all 5; twelve not, six 0 and six 3 - and the ground left out, none having been read.
        Assert.Matches(@"\n  0 +6 +6 +0\s*\n", report);
        Assert.Matches(@"\n  1 +0 +0 +12\s*\n", report);
        Assert.Contains("the ground under each cell left out", report, StringComparison.Ordinal);
    }

    /// <summary>A second grid of another length is not read with the first one's packing: the report says so and counts nothing.</summary>
    [Fact]
    public void ASECONDGridOfAnotherShapeIsNotCompared()
    {
        (FakeMemoryReader fake, OffsetSchema schema) = Area(landscapeBytes: Stride * 2);
        TerrainLayers layers = TerrainLayers.Read(fake, schema, AreaInstance);

        Assert.False(layers.Ready);
        Assert.Contains("not one shape", layers.Why, StringComparison.Ordinal);
        Assert.Contains("not compared", CaptureLandscape.Report(layers, null, null, null), StringComparison.Ordinal);
        Assert.Equal("no area instance", TerrainLayers.Read(fake, schema, 0).Why);
    }
}
