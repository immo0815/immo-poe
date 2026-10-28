using System.Buffers.Binary;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The shore field hunt (ShoreFieldHunt): the scene's planar size, worked out from the terrain's tiles, is found in memory, and what
/// stands beside it - a texture's size, a box, a pair of ints, a small factor - is listed, tallied and read back from the report alone.
/// </summary>
public class ShoreFieldHuntTests
{
    private const ulong Area = 0x10_0000;
    private const ulong Pass = 0x20_0000;
    private const int PassBytes = 0x3000;

    private static OffsetSchema LoadSchema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "schema", "poe2.offsets.json")))
        {
            dir = dir.Parent;
        }

        return SchemaJson.Load(Path.Combine(dir!.FullName, "schema", "poe2.offsets.json"));
    }

    /// <summary>
    /// The scene's size is 250 world units a tile, looked for both ways round; a square area's once; the cells beside it, and the
    /// walkable grid's width as kept where it is a cell more (Port's 153 tiles, kept 3520 wide).
    /// </summary>
    [Fact]
    public void THENEEDLESAreTheScenesSizeBothWaysAndItsCells()
    {
        IReadOnlyList<Core.Diagnostics.FloatNeedle> port = ShoreFieldHunt.Needles(new ShoreFieldHunt.Area(147, 63));
        Assert.Equal([36750f, 15750f], port[0].Values);
        Assert.Equal([15750f, 36750f], port[1].Values);
        Assert.Equal([3381f, 1449f], port[2].Values);
        Assert.All(port, needle => Assert.Equal(Core.Diagnostics.FloatHunt.MostAround, needle.Around));

        IReadOnlyList<Core.Diagnostics.FloatNeedle> square = ShoreFieldHunt.Needles(new ShoreFieldHunt.Area(20, 20, 460));
        Assert.Equal(2, square.Count);

        IReadOnlyList<Core.Diagnostics.FloatNeedle> odd = ShoreFieldHunt.Needles(new ShoreFieldHunt.Area(153, 63, 3520));
        Assert.Equal([3519f, 1449f], odd[2].Values);
        Assert.Equal([3520f, 1449f], odd[3].Values);
    }

    /// <summary>
    /// A pass block holding Port's scene size, the shore texture's size with its inverses, a box of the scene and a dist_to_color: each is
    /// found beside the scene's size, tallied over the windows, and listed again from the report's bytes alone.
    /// </summary>
    [Fact]
    public void WHATSTANDSBesideTheScenesSizeIsListedAndReadBack()
    {
        OffsetSchema schema = LoadSchema();
        int terrainAt = schema.Structs["AreaInstance"].OffsetOf("TerrainMetadata");
        StructDef terrain = schema.Structs["TerrainMetadata"];
        var area = new byte[terrainAt + 0x1000];
        BinaryPrimitives.WriteInt64LittleEndian(area.AsSpan(terrainAt + terrain.OffsetOf("TotalTilesX")), 147);
        BinaryPrimitives.WriteInt64LittleEndian(area.AsSpan(terrainAt + terrain.OffsetOf("TotalTilesY")), 63);
        BinaryPrimitives.WriteInt32LittleEndian(area.AsSpan(terrainAt + terrain.OffsetOf("BytesPerRow")), 1691);

        // PORT'S NUMBERS AS A PASS MIGHT KEEP THEM: the scene, the texture's size and inverses, the box (whose far corner is the scene's
        // size again - a second place), dist_to_color, and the texture's size as ints.
        var pass = new byte[PassBytes];
        Floats(pass, 0x1000, 36750f, 15750f);
        Floats(pass, 0x1010, 1176f, 504f, 1f / 1176f, 1f / 504f);
        Floats(pass, 0x1020, 0f, 0f, 36750f, 15750f);
        Floats(pass, 0x1034, 0.0005f);
        BinaryPrimitives.WriteInt32LittleEndian(pass.AsSpan(0x1040), 1176);
        BinaryPrimitives.WriteInt32LittleEndian(pass.AsSpan(0x1044), 504);

        var memory = new FakeMemoryReader().Place(Area, area).Place(Pass, pass);
        var space = new Space(new MemoryRegion(Area, (ulong)area.Length), new MemoryRegion(Pass, PassBytes));
        string text = string.Join('\n', ShoreFieldHunt.Run(memory, space, schema, Area, new ShoreFieldHunt.Running()));

        Assert.Contains("shore field hunt: scene 36750 15750, cells 3381 1449, tiles 147 63, stored 3382", text, StringComparison.Ordinal);
        Assert.Contains("the walkable grid's size in cells as kept (3382, 1449): 0 places", text, StringComparison.Ordinal);
        Assert.Contains("the scene's planar size (36750, 15750): 2 places", text, StringComparison.Ordinal);
        Assert.Contains("+0x10  a texture's size 1176 x 504 with its inverses, the scene's proportions - a texel 31.25 units", text, StringComparison.Ordinal);
        Assert.Contains("+0x20  a box (0, 0) to (36750, 15750), 36750 x 15750", text, StringComparison.Ordinal);
        Assert.Contains("+0x40  ints 1176 x 504, a texel 31.25 units = the tiles x8", text, StringComparison.Ordinal);
        Assert.Contains("+0x34  0.0005  (1 / 2000)", text, StringComparison.Ordinal);
        Assert.Contains("texture sizes (two whole numbers and their inverses): 1176 x 504 in 2", text, StringComparison.Ordinal);

        // READ BACK from the report alone, the same windows list the same things.
        (ShoreFieldHunt.Area? read, IReadOnlyList<ShoreFieldHunt.Window> windows) = ShoreFieldHunt.Read(text);
        Assert.Equal(new ShoreFieldHunt.Area(147, 63, 3382), read);
        Assert.Equal(2, windows.Count);
        Assert.Equal(Pass + 0x1000, windows[0].At);
        string replay = ShoreFieldHunt.Replay(text);
        Assert.StartsWith("shore field hunt replay: 2 windows read back, the scene 36750 x 15750", replay, StringComparison.Ordinal);
        Assert.Contains("texture sizes (two whole numbers and their inverses): 1176 x 504 in 2", replay, StringComparison.Ordinal);
        Assert.Contains("small positive floats (a dist_to_color among them?): 0.0005 in 2", replay, StringComparison.Ordinal);
    }

    /// <summary>A session that cannot list the game's memory says so, and the replay of a report without a hunt says it found none.</summary>
    [Fact]
    public void NOREGIONSIsSaidAndAReportWithoutAHuntReplaysAsNone()
    {
        IReadOnlyList<string> lines = ShoreFieldHunt.Run(new FakeMemoryReader(), null, LoadSchema(), Area, new ShoreFieldHunt.Running());
        Assert.Equal("shore field hunt: nothing in this session can list the game's memory", lines[^1]);
        Assert.StartsWith("shore field hunt replay: the report names no area", ShoreFieldHunt.Replay("ocean markup: no area instance"), StringComparison.Ordinal);
    }

    private static void Floats(byte[] block, int at, params float[] values)
    {
        for (var one = 0; one < values.Length; one++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(at + (one * 4)), values[one]);
        }
    }

    private sealed class Space(params MemoryRegion[] regions) : IMemoryRegions
    {
        public IEnumerable<MemoryRegion> Regions() => regions;
    }
}
