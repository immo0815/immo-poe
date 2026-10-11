using System.Text;
using PoEformance.Features;
using PoEformance.Game.World;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's ground part over a small install: two laid tiles on one template, one ground all round and one mixed, a
/// ground block whose vertex carries two unnamed fields, the master's MaterialsList, a real ground material, and two
/// shader sources of which one names the ground's coordinates.
/// </summary>
public sealed class CaptureGroundTests
{
    private const string Master = "Metadata/Terrain/Maps/Test/master.tsi";
    private const string Floor = "Metadata/Terrain/Maps/Test/floor.tdt";
    private const string Edge = "Metadata/Terrain/Maps/Test/edge.tdt";
    private const string Template = "Art/Models/Terrain/Test/Floor.tgt";
    private const string Mesh = "Art/Models/Terrain/Test/Floor.tgm";
    private const string Sand = "Metadata/Terrain/Maps/Test/sand.gt";
    private const string Rock = "Metadata/Terrain/Maps/Test/rock.gt";
    private const string SandMaterial = "Art/Textures/Environment/desert/Shore/Ground/G_VST_Sand01.mat";
    private const string GroundShader = "Shaders/Renderer/Ground.ffx";

    /// <summary>
    /// The block's format, its two unnamed fields as halves, and that it carries no texture coordinates - the three things a painted ground waits on.
    /// </summary>
    [Fact]
    public void THEGROUNDBlocksAreWrittenByTheirVertexFormatWithTheirFieldsAsHalves()
    {
        string said = Written();

        Assert.Contains("format 0x3 · stride 28 · in 1 sub-tiles of 1 templates · 4 vertices · texture coordinates in 0", said, StringComparison.Ordinal);
        Assert.Contains("no texture coordinates", said, StringComparison.Ordinal);

        // Bit 0's field runs 0..1 in both halves - a coordinate, read as halves; bit 1's is a constant pair.
        Assert.Contains("bit 0: first 00 00 00 00 · 4 distinct over 4 vertices", said, StringComparison.Ordinal);
        Assert.Contains("as halves 0..1 0..1", said, StringComparison.Ordinal);
        Assert.Contains("bit 1: first 00 3C 00 00 · 1 distinct over 4 vertices", said, StringComparison.Ordinal);
        Assert.Contains("as halves 1..1 0..0", said, StringComparison.Ordinal);

        // The template once, though two files lay it - with its mask.
        Assert.Contains($"  {Template}  1x1  ground in 1 of 1  format 0x3  coordinates in 0  mask Art/Models/Terrain/Test/Mask.dds  laid as edge, floor", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The corners by ground name, counted by cells, and the MaterialsList's group for each - the laid name found, the unlaid one said.
    /// </summary>
    [Fact]
    public void THECORNERSAreCountedByCellsAndJoinedToTheMaterialsList()
    {
        string said = Written();

        Assert.Contains("one ground all round: 1 files over 3 cells · mixed: 1 files over 1 cells · no ground named: 0 files over 0 cells", said, StringComparison.Ordinal);
        Assert.Contains("  \"Sand\": at a corner of 2 files over 4 cells; all round in 1 files over 3 cells", said, StringComparison.Ordinal);
        Assert.Contains("  \"Rock\": at a corner of 1 files over 1 cells; all round in 0 files over 0 cells", said, StringComparison.Ordinal);
        Assert.Contains("=== MaterialsList Metadata/Terrain/Maps/Test/materials.mtd  (of " + Master + ")", said, StringComparison.Ordinal);
        Assert.Contains("  \"Sand\"  at a corner of 2 laid files · 2 choices · weights 30 70 · then 16", said, StringComparison.Ordinal);
        Assert.Contains("  \"Rock\"  laid, and not in this list", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ground material with what its graphs read - InputUV is the question - and the graph whole once.
    /// </summary>
    [Fact]
    public void THEGROUNDMaterialsAreWrittenWithTheInputsTheirGraphsRead()
    {
        string said = Written();

        Assert.Contains("##### " + SandMaterial, said, StringComparison.Ordinal);
        Assert.Contains("offered as: \"Sand\"", said, StringComparison.Ordinal);
        Assert.Matches("graphs name: [^\\n]*InputUV", said);
        Assert.Contains("##### Metadata/Materials/Ground/PBRGroundBN.fxgraph  (a graph the ground materials are instances of)", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shader source naming the ground's coordinates is written whole, the other not at all.
    /// </summary>
    [Fact]
    public void ASHADERSourceNamingTheGroundsCoordinatesIsWrittenWhole()
    {
        string said = Written();

        Assert.Contains("1 of 2 sources name one", said, StringComparison.Ordinal);
        Assert.Contains("  " + GroundShader + "  (tile_ground_tiling, ground_scalemove)", said, StringComparison.Ordinal);
        Assert.Contains("float2 GroundUv(float3 pos)", said, StringComparison.Ordinal);
        Assert.DoesNotContain("float4 Unrelated", said, StringComparison.Ordinal);
        Assert.StartsWith("2 tile files laid over 4 cells, 1 templates, 1 of their 1 sub-tile meshes read; 3 cells under one ground all round, 1 mixed; 1 MaterialsLists, 2 ground materials; 1 of 2 shader sources name the ground's coordinates", said, StringComparison.Ordinal);
    }

    /// <summary>No shader list and no terrain are said, not passed over.</summary>
    [Fact]
    public void NOTHINGToSearchIsSaid()
    {
        string said = CaptureGround.Write(Install, null, [Master], null, null);

        Assert.Contains("(the terrain's tiles were not read - nothing laid to write about)", said, StringComparison.Ordinal);
        Assert.Contains("(no shader sources: the install walk found none, or it had not run when the key went down)", said, StringComparison.Ordinal);
    }

    private static string Written()
    {
        // Three cells of the floor, one of the edge.
        int[] ids = [0, 0, 0, 1];
        var tiles = new TerrainTiles([Floor, Edge], ids, new byte[4], new byte[4], new sbyte[4], 2, 2);
        return CaptureGround.Write(Install, tiles, [Master], null, [GroundShader, "Shaders/Renderer/Other.ffx"]).ReplaceLineEndings("\n");
    }

    private static byte[]? Install(string path) => path switch
    {
        Master => Text("version 3\nMaterialsList\t\"materials.mtd\"\n"),
        "Metadata/Terrain/Maps/Test/materials.mtd" => Text(
            "version 5\n\"Sand\" 2 0\n\"" + SandMaterial + "\"\n\"Art/Textures/Test/sand2.mat\"\n30 70 16\n"),
        Floor => Tdt(Template, [Sand, Sand, Sand, Sand]),
        Edge => Tdt(Template, [Sand, Sand, Rock, Rock]),
        Template => Text("version 3\nSize 1 1\nTileMeshRoot \"Art/Models/Terrain/Test/Floor\"\nGroundMask \"Art/Models/Terrain/Test/Mask.dds\"\n"),
        Mesh => Tgm(),
        Sand => Text("Sand\n1 1 0 0\n"),
        Rock => Text("Rock\n1 1 0 0\n"),
        GroundShader => Text("// the ground\nfloat2 GroundUv(float3 pos)\n{\n    return tile_ground_tiling * pos.xy * ground_scalemove_uv.xy;\n}\n"),
        "Shaders/Renderer/Other.ffx" => Text("float4 Unrelated() { return 0; }\n"),
        _ => Fixture(path),
    };

    /// <summary>A file of tests/fixtures/shaders, by its install path - the real ground material and its graph.</summary>
    private static byte[]? Fixture(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string file = Path.Combine(dir.FullName, "tests", "fixtures", "shaders", path.Replace("/", "__", StringComparison.Ordinal));
        return File.Exists(file) ? File.ReadAllBytes(file) : null;
    }

    /// <summary>Text as the game writes it: UTF-16 with its mark.</summary>
    private static byte[] Text(string text) => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)];

    /// <summary>A version 7 definition, one tile by one, naming its template and its four corners' ground types.</summary>
    private static byte[] Tdt(string template, string[] grounds)
    {
        string[] strings = ["", template, "tag", "edge", .. grounds.Distinct(StringComparer.Ordinal)];
        var starts = new Dictionary<string, uint>(StringComparer.Ordinal);
        var table = new List<byte>();
        foreach (string one in strings)
        {
            starts[one] = (uint)(table.Count / 2);
            table.AddRange(Encoding.Unicode.GetBytes(one));
            table.AddRange([0, 0]);
        }

        using var stream = new MemoryStream();
        using var write = new BinaryWriter(stream);
        write.Write(7);
        write.Write(table.Count / 2);
        write.Write(table.ToArray());
        write.Write(starts[string.Empty]);
        write.Write(starts[template]);
        write.Write(starts["tag"]);
        for (var side = 0; side < 4; side++)
        {
            write.Write(starts["edge"]);
        }

        write.Write((sbyte)1);
        write.Write((sbyte)1);
        foreach (string ground in grounds)
        {
            write.Write(starts[ground]);
        }

        write.Write(new byte[48]);
        return stream.ToArray();
    }

    /// <summary>
    /// A sub-tile of no props and a ground of one quad whose vertex format is 0x3: bit 0's field the corner as two halves 0..1, bit 1's the halves 1 and 0.
    /// </summary>
    private static byte[] Tgm()
    {
        using var stream = new MemoryStream();
        using var write = new BinaryWriter(stream);
        write.Write((byte)9);
        for (var one = 0; one < 6; one++)
        {
            write.Write(0f);
        }

        write.Write((ushort)0);
        write.Write((ushort)0);
        write.Write((byte)0);
        write.Write((byte)0);

        // The props: an empty block.
        write.Write("DOLm"u8);
        write.Write((ushort)1);
        write.Write((byte)0);
        write.Write((ushort)0);
        write.Write(0x8u);

        // The ground: one quad, one shape, format 0x3 - no coordinates, two four-byte fields after the tangent.
        write.Write("DOLm"u8);
        write.Write((ushort)1);
        write.Write((byte)1);
        write.Write((ushort)1);
        write.Write(0x3u);
        write.Write(2u);
        write.Write(4u);
        write.Write(0u);
        write.Write(6u);
        foreach (int corner in new[] { 0, 1, 2, 1, 3, 2 })
        {
            write.Write((ushort)corner);
        }

        for (var corner = 0; corner < 4; corner++)
        {
            int across = corner % 2, down = corner / 2;
            write.Write(across * 250f);
            write.Write(down * 250f);
            write.Write(0f);
            write.Write(new byte[] { 0, 0, 127, 0, 127, 0, 0, 0 });
            write.Write(BitConverter.HalfToUInt16Bits((Half)across));
            write.Write(BitConverter.HalfToUInt16Bits((Half)down));
            write.Write(BitConverter.HalfToUInt16Bits((Half)1f));
            write.Write(BitConverter.HalfToUInt16Bits((Half)0f));
        }

        write.Write((ushort)0);
        write.Write(new byte[24]);
        return stream.ToArray();
    }
}
