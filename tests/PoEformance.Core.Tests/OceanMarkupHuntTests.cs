using System.Buffers.Binary;
using PoEformance.Core.Schema;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The ocean markup hunt (OceanMarkupHunt): a grid of the area's shape is found in the terrain and one hop beyond it, and written
/// as a map with its values counted - the engine's shore, looked for where ClientTileMap::CalculateMapMarkup keeps it.
/// </summary>
public class OceanMarkupHuntTests
{
    private const ulong Area = 0x10_0000;
    private const ulong Walkable = 0x50_0000;
    private const ulong Corners = 0x51_0000;
    private const ulong Markup = 0x52_0000;
    private const ulong Hop = 0x53_0000;
    private const ulong HopGrid = 0x54_0000;

    private static OffsetSchema LoadSchema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "schema", "poe2.offsets.json")))
        {
            dir = dir.Parent;
        }

        return SchemaJson.Load(Path.Combine(dir!.FullName, "schema", "poe2.offsets.json"));
    }

    private static void Vector(byte[] block, int at, ulong begin, int bytes)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(block.AsSpan(at), begin);
        BinaryPrimitives.WriteUInt64LittleEndian(block.AsSpan(at + 8), begin + (ulong)bytes);
        BinaryPrimitives.WriteUInt64LittleEndian(block.AsSpan(at + 16), begin + (ulong)bytes);
    }

    [Fact]
    public void AGRIDOfTheAreasShapeIsFoundInTheTerrainAndOneHopBeyond()
    {
        OffsetSchema schema = LoadSchema();
        int terrainAt = schema.Structs["AreaInstance"].OffsetOf("TerrainMetadata");
        StructDef terrain = schema.Structs["TerrainMetadata"];

        // AN AREA OF 4 BY 3 TILES, 92 by 69 cells: the walkable grid two cells a byte, the corners three bytes each.
        const int tilesX = 4, tilesY = 3, stride = tilesX * 23 / 2, rows = tilesY * 23;
        var area = new byte[terrainAt + OceanMarkupHunt.TerrainBytes];
        var terrainBlock = area.AsSpan(terrainAt);
        BinaryPrimitives.WriteInt64LittleEndian(terrainBlock[terrain.OffsetOf("TotalTilesX")..], tilesX);
        BinaryPrimitives.WriteInt64LittleEndian(terrainBlock[terrain.OffsetOf("TotalTilesY")..], tilesY);
        BinaryPrimitives.WriteInt32LittleEndian(terrainBlock[terrain.OffsetOf("BytesPerRow")..], stride);
        Vector(area, terrainAt + terrain.OffsetOf("GridWalkableData"), Walkable, stride * rows);
        Vector(area, terrainAt + terrain.OffsetOf("TileCornerData"), Corners, (tilesX + 1) * (tilesY + 1) * 3);

        // THE MARKUP: tile corners at twice the tiles, ints, the sea (1) west of x 5 and the land (0) east of it.
        const int width = (tilesX * 2) + 1, height = (tilesY * 2) + 1;
        var markup = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(markup.AsSpan(((y * width) + x) * 4), x < 5 ? 1 : 0);
            }
        }

        Vector(area, terrainAt + 0x300, Markup, markup.Length);

        // ONE HOP BEYOND: an object the terrain points to, a byte grid at four times the tiles in it.
        BinaryPrimitives.WriteUInt64LittleEndian(area.AsSpan(terrainAt + 0x400), Hop);
        var hop = new byte[OceanMarkupHunt.WindowBytes];
        Vector(hop, 0x10, HopGrid, tilesX * 4 * tilesY * 4);

        var walkable = new byte[stride * rows];
        Array.Fill(walkable, (byte)0x11);
        var fake = new FakeMemoryReader()
            .Place(Area, area)
            .Place(Walkable, walkable)
            .Place(Corners, new byte[(tilesX + 1) * (tilesY + 1) * 3])
            .Place(Markup, markup)
            .Place(Hop, hop)
            .Place(HopGrid, new byte[tilesX * 4 * tilesY * 4]);

        IReadOnlyList<string> lines = OceanMarkupHunt.Run(fake, schema, Area);
        string text = string.Join('\n', lines);

        Assert.Contains("terrain+0x300  252 bytes at 520000: tile corners x2 9x7 of 4 bytes", text, StringComparison.Ordinal);
        Assert.Contains("terrain+0x400 -> +0x10  192 bytes at 540000: tiles x4 16x12 of 1 bytes", text, StringComparison.Ordinal);
        Assert.Contains("'0' 1 (0x1, ", text, StringComparison.Ordinal);
        Assert.Contains("35 cells, 35 walkable", text, StringComparison.Ordinal);
        Assert.Contains("  000001111", text, StringComparison.Ordinal);
        Assert.Contains("=== the walkable grid at 9x7", text, StringComparison.Ordinal);

        // READ BACK as the replay reads it: the corner grid's map whole, 35 sea and 28 land.
        OceanMarkupHunt.ReadGrid corners = OceanMarkupHunt.Read(text).First(grid => grid.Heading.StartsWith("terrain+0x300", StringComparison.Ordinal));
        Assert.Equal(7, corners.Rows.Count);
        Assert.All(corners.Rows, row => Assert.Equal("000001111", row));
        Assert.Contains("'0' x35, '1' x28", OceanMarkupHunt.Report([corners]), StringComparison.Ordinal);
    }

    [Fact]
    public void NOAREASaysSoAndThrowsNothing()
    {
        IReadOnlyList<string> lines = OceanMarkupHunt.Run(new FakeMemoryReader(), LoadSchema(), 0);
        Assert.Equal(["ocean markup: no area instance"], lines);
    }
}
