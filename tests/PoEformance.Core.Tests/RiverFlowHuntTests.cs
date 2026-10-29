using System.Buffers.Binary;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The river flow hunt (RiverFlowHunt): the water pass's block is known by its shoreline box, its river box made a grid of 54.35-unit
/// texels, and a list of that grid's length found anywhere in memory is read, set out every way its texels could be, drawn and written
/// beside the report - and read back from there alone.
/// </summary>
public class RiverFlowHuntTests
{
    private const ulong Area = 0x10_0000;
    private const ulong Pass = 0x20_0000;
    private const ulong Holder = 0x30_0000;
    private const ulong Data = 0x40_0000;
    private const int Width = 276, Height = 294;

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
    /// The Red Vale's block as its capture held it (v0.1.351): the fog's box empty, the river's box in both the guiding fog's slot and the
    /// river's - one grid of 276 x 294 texels named by both - and the shoreline's the scene a quarter tile wider, which a place that is not
    /// the block lacks.
    /// </summary>
    [Fact]
    public void THEREDValesBlockHoldsOneGridAndTheShorelineIsItsFingerprint()
    {
        byte[] block = RedValeBlock(0x200);
        var dump = new FloatDump(Pass + 0x200, 0, Pass, block);
        var stranger = new FloatDump(Pass + 0x900, 0, Pass + 0x800, new byte[0x208]);
        IReadOnlyList<RiverFlowHunt.Grid> grids = RiverFlowHunt.Grids([dump, stranger], 18000f, 18750f, out List<string> lines);

        RiverFlowHunt.Grid grid = Assert.Single(grids);
        Assert.Equal("guiding fog and river flow map", grid.Slot);
        Assert.Equal((Width, Height), (grid.Width, grid.Height));
        Assert.Contains("  1 of the 2 places read are the block", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.StartsWith("  fog flow map (+0x84): (0, 0) to (1, 1) - empty", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("- the same box as the guiding fog", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("  shoreline (+0xB4): (-62.5, -62.5) to (18062.5, 18812.5) - the shoreline's", StringComparison.Ordinal));
    }

    /// <summary>A begin and end whose difference is a shape's length is a list, a pair of ints the grid's size a size; anything else neither.</summary>
    [Fact]
    public void ALISTIsFoundByItsLengthAndAGridsSizeByItsInts()
    {
        IReadOnlyList<RiverFlowHunt.Shape> shapes = RiverFlowHunt.Shapes([new RiverFlowHunt.Grid("river flow map", -16.30435f, -16.30435f, 14983.69f, 15961.96f)]);
        Assert.Contains(shapes, shape => shape is { Laid: "texels", Width: Width, Height: Height, Element: 16, Bytes: 1_298_304 });
        Assert.Contains(shapes, shape => shape is { Laid: "cells", Width: 1380, Height: 1470, Element: 1 });
        Assert.Contains(shapes, shape => shape is { Laid: "corners", Width: 277, Height: 295 });

        var lengths = shapes.DistinctBy(shape => shape.Bytes).ToDictionary(shape => shape.Bytes);
        var sizes = new Dictionary<(int, int), RiverFlowHunt.Shape> { [(Width, Height)] = shapes[0] };
        var chunk = new byte[64];
        BinaryPrimitives.WriteUInt64LittleEndian(chunk.AsSpan(8), 0x7FF7_1000_0000);
        BinaryPrimitives.WriteUInt64LittleEndian(chunk.AsSpan(16), 0x7FF7_1000_0000 + 1_298_304UL);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(40), Width);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(44), Height);
        var found = new List<RiverFlowHunt.Hit>();
        RiverFlowHunt.Scan(chunk, 0x1000, lengths, lengths.Keys.Min(), lengths.Keys.Max(), sizes, found);

        RiverFlowHunt.Hit list = Assert.Single(found, hit => hit.Kind == "list");
        Assert.Equal((0x1008UL, 0x7FF7_1000_0000UL, 16), (list.At, list.Data, list.Shape.Element));
        Assert.Equal(0x1028UL, Assert.Single(found, hit => hit.Kind == "size").At);
    }

    /// <summary>
    /// The whole hunt over a made-up memory: the block, a vector holding the river's grid as four floats a texel - a flow running along +x,
    /// faster in the river's middle - read as four floats, its neighbours alike, drawn pointing along +x, written beside the report and read
    /// back from there.
    /// </summary>
    [Fact]
    public void ARIVERSFlowMapInMemoryIsFoundSetOutDrawnAndReadBack()
    {
        OffsetSchema schema = LoadSchema();
        int terrainAt = schema.Structs["AreaInstance"].OffsetOf("TerrainMetadata");
        StructDef terrain = schema.Structs["TerrainMetadata"];
        var area = new byte[terrainAt + 0x1000];
        BinaryPrimitives.WriteInt64LittleEndian(area.AsSpan(terrainAt + terrain.OffsetOf("TotalTilesX")), 72);
        BinaryPrimitives.WriteInt64LittleEndian(area.AsSpan(terrainAt + terrain.OffsetOf("TotalTilesY")), 75);
        BinaryPrimitives.WriteInt32LittleEndian(area.AsSpan(terrainAt + terrain.OffsetOf("BytesPerRow")), 828);

        var map = new byte[Width * Height * 16];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                int at = ((y * Width) + x) * 16;
                float speed = 40f * MathF.Sin(MathF.PI * y / Height);
                BinaryPrimitives.WriteSingleLittleEndian(map.AsSpan(at), speed);
                BinaryPrimitives.WriteSingleLittleEndian(map.AsSpan(at + 4), 0.1f * speed * MathF.Cos(x * 0.05f));
                BinaryPrimitives.WriteSingleLittleEndian(map.AsSpan(at + 12), MathF.Abs(y - (Height / 2f)) * 54f);
            }
        }

        var holder = new byte[0x40];
        BinaryPrimitives.WriteUInt64LittleEndian(holder.AsSpan(0x10), Data);
        BinaryPrimitives.WriteUInt64LittleEndian(holder.AsSpan(0x18), Data + (ulong)map.Length);
        byte[] block = RedValeBlock(0x1000);
        var memory = new FakeMemoryReader().Place(Area, area).Place(Pass, block).Place(Holder, holder).Place(Data, map);
        var space = new Space(
            new MemoryRegion(Area, (ulong)area.Length), new MemoryRegion(Pass, (ulong)block.Length),
            new MemoryRegion(Holder, (ulong)holder.Length), new MemoryRegion(Data, (ulong)map.Length));
        string folder = Path.Combine(Path.GetTempPath(), "river-flow-" + Guid.NewGuid().ToString("N"));
        try
        {
            string text = string.Join('\n', RiverFlowHunt.Run(memory, space, schema, Area, new RiverFlowHunt.Running(), folder));

            Assert.True(text.Contains("river flow hunt: scene 18000 18750, tiles 72 75", StringComparison.Ordinal), text);
            Assert.Contains("guiding fog (+0x94): (-16.30435, -16.30435) to (14983.69, 15961.96) - 276 x 294 texels", text, StringComparison.Ordinal);
            Assert.Contains("=== list 1: a vector of 1298304 bytes at 0x400000, its begin and end at 0x300010 - the guiding fog and river flow map grid's texels 276 x 294, 16 bytes a texel - written to river-flow-1.bin (1298304 bytes)", text, StringComparison.Ordinal);
            Assert.Contains("  as float x4: neighbours alike", text, StringComparison.Ordinal);
            Assert.Contains("  drawn as float x4: the way channels 0 and 1 point", text, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(folder, "river-flow-1.bin")));

            // ALONG +x IN THE MIDDLE: the map's middle row is sector 0 throughout (its arrow along x), its edges still.
            string[] mapRows = [.. text.Split('\n').SkipWhile(line => !line.StartsWith("  drawn as", StringComparison.Ordinal)).Skip(1).TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal))];
            Assert.NotEmpty(mapRows);
            string middle = mapRows[mapRows.Length / 2].Trim();
            Assert.True(middle.All(one => one is '0' or '7' or '1'), middle);
            Assert.Contains('.', mapRows[0]);

            // READ BACK from the report and the file beside it.
            string replay = RiverFlowHunt.Replay(text, folder);
            Assert.StartsWith("=== list 1: a vector of 1298304 bytes", replay, StringComparison.Ordinal);
            Assert.Contains("  as float x4: neighbours alike", replay, StringComparison.Ordinal);
            Assert.StartsWith("river flow hunt replay: the report holds no list read", RiverFlowHunt.Replay("nothing", folder), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>A session that cannot list memory says so; an area without the block's boxes says there is no flow map to seek.</summary>
    [Fact]
    public void NOREGIONSOrNoGridIsSaid()
    {
        Assert.Equal(
            "river flow hunt: nothing in this session can list the game's memory",
            RiverFlowHunt.Run(new FakeMemoryReader(), null, LoadSchema(), Area, new RiverFlowHunt.Running(), null)[^1]);
        IReadOnlyList<RiverFlowHunt.Grid> none = RiverFlowHunt.Grids([], 18000f, 18750f, out List<string> lines);
        Assert.Empty(none);
        Assert.Contains("0 of the 0 places read are the block", lines[0], StringComparison.Ordinal);
    }

    /// <summary>The Red Vale's block (v0.1.351's capture) with its scene's size at <paramref name="place"/>.</summary>
    private static byte[] RedValeBlock(int place)
    {
        var block = new byte[place + 0x200];
        Floats(block, place, 18000f, 18750f, 47812.5f);
        Floats(block, place + 0x84, 0f, 0f, 1f, 1f);
        Floats(block, place + 0x94, -16.30435f, -16.30435f, 14983.69f, 15961.96f);
        Floats(block, place + 0xA4, -16.30435f, -16.30435f, 14983.69f, 15961.96f);
        Floats(block, place + 0xB4, -62.5f, -62.5f, 18062.5f, 18812.5f);
        return block;
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
