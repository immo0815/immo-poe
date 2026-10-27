using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's measure of which material of its group the game drew: every material run at every pixel, its pattern held against the
/// picture's detail.
/// </summary>
public sealed class CaptureGroundChoicesTests
{
    /// <summary>
    /// A sub-tile of one ground whose group offers stripes across x and stripes across y, and a picture of the stripes across x laid where
    /// the materials' coordinates put them: the stripes across x win, clearly, and the picture is tinted.
    /// </summary>
    [Fact]
    public void THEMATERIALWhosePatternThePictureShowsWins()
    {
        const int width = 100, height = 100;
        var mesh = SkinnedMesh.Of(
            [new Vector3(0f, 0f, 0f), new Vector3(250f, 0f, 0f), new Vector3(0f, 250f, 0f), new Vector3(250f, 250f, 0f)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [0, 1, 2, 1, 3, 2],
            Vector3.Zero,
            new Vector3(250f, 250f, 0f));
        var laid = new LaidGround("Metadata/Test/t.tdt", new TilePiece(0, 3, 0, 0, 0, 0, Whole: true), mesh, [new GroundPiece(1, 1, 2)], Matrix4x4.Identity);
        var grounds = new SubTileGrounds(["Metadata/Test/sand.gt", "Metadata/Test/sand.gt", "Metadata/Test/sand.gt", "Metadata/Test/sand.gt"], ["Sand"], string.Empty)
        {
            Gridded = true,
            Materials = ["Art/across-y.mat"],
            Choices = [[("Art/across-x.mat", 30), ("Art/across-y.mat", 70)]],
        };

        var matrix = new float[16];
        matrix[0] = 2f / 250f;
        matrix[12] = -1f;
        matrix[5] = -2f / 250f;
        matrix[13] = 1f;
        matrix[15] = 1f;

        // THE PICTURE: the stripes across x, a texel of eight to the materials' unit, at the world's x times the ground scale.
        var bgr = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width * 250f * GroundPainter.GroundScale;
                byte value = (int)MathF.Floor(u * 8f) % 2 == 0 ? (byte)40 : (byte)210;
                int at = ((y * width) + x) * 3;
                bgr[at] = bgr[at + 1] = bgr[at + 2] = value;
            }
        }

        ShadeProgram acrossX = Striped(alongX: true), acrossY = Striped(alongX: false);
        ShadeProgram? Programs(string material) => material switch
        {
            "Art/across-x.mat" => acrossX,
            "Art/across-y.mat" => acrossY,
            _ => null,
        };

        string said = CaptureGroundChoices.Measure([new CaptureGroundChoices.Piece(laid, [grounds])], matrix, width, height, bgr, Programs, out GamePicture? picture);

        Assert.StartsWith("material choices: 10000 pixels in 1 sub-tiles", said, StringComparison.Ordinal);
        Assert.Contains("1 of 1 sub-tiles have one - 1 of the 1 of one ground with at least 200 pixels, the clean test", said, StringComparison.Ordinal);
        Assert.Contains(" across-x weight 30 (30%), red: won 1 (10000 px);", said, StringComparison.Ordinal);
        Assert.Contains(" across-y weight 70 (70%), the painter's, green: won 0 (0 px);", said, StringComparison.Ordinal);
        Assert.Contains("    t at tile 0, 0 · 1 sub-tiles: across-x x1", said, StringComparison.Ordinal);
        Assert.Contains(" *across-x r +", said, StringComparison.Ordinal);
        Assert.Contains(" across-y r +0.000", said, StringComparison.Ordinal);
        GamePicture tinted = Assert.IsType<GamePicture>(picture);
        Assert.True(tinted.Rgba[((50 * width) + 50) * 4] > tinted.Rgba[(((50 * width) + 50) * 4) + 2]);
    }

    /// <summary>The detail is the brightness less its mean over nine by nine pixels: nought on an even picture, the stripes left on a striped one.</summary>
    [Fact]
    public void THEDETAILIsTheBrightnessLessItsLocalMean()
    {
        const int width = 20, height = 20;
        float[] even = CaptureGroundChoices.Detail([.. Enumerable.Repeat((byte)90, width * height)], width, height);
        Assert.All(even, value => Assert.Equal(0f, value, 4));

        float[] striped = CaptureGroundChoices.Detail([.. Enumerable.Range(0, width * height).Select(pixel => pixel % width % 2 == 0 ? (byte)0 : (byte)100)], width, height);
        Assert.True(striped[(10 * width) + 10] < -40f);
        Assert.True(striped[(10 * width) + 11] > 40f);
    }

    /// <summary>A material reading one striped texture at its coordinates - stripes across x or across y, eight texels to the unit.</summary>
    private static ShadeProgram Striped(bool alongX)
    {
        string json =
            """
            {"nodes":[
              {"type":"InputTexture","index":0,"parameters":[{"format":"DXT1","srgb":true,"path":"Art/stripes.dds"}],"custom_parameter":"AlbedoTransparency_TEX"},
              {"type":"InputUV","index":0,"stage":"Texturing_Init"},
              {"type":"SampleInputTexture","index":0,"parameters":[{"value":"SamplerDynamicWrap"}]},
              {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
             "links":[
              {"src":{"type":"InputTexture","index":0,"variable":"out_texture"},"dst":{"type":"SampleInputTexture","index":0,"variable":"in_texture"}},
              {"src":{"type":"InputUV","index":0,"stage":"Texturing_Init","variable":"output"},"dst":{"type":"SampleInputTexture","index":0,"variable":"uv"}},
              {"src":{"type":"SampleInputTexture","index":0,"variable":"color","swizzle":"xyz"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}]}
            """;
        ShadeCompile compiled = ShadeProgram.Compile(
            [(new ShaderInstance("Metadata/Test.fxgraph", new Dictionary<string, ShaderValue[]>()), ShaderGraph.Read(Encoding.UTF8.GetBytes(json)))]);
        Assert.NotNull(compiled.Program);
        var pixels = new byte[8 * 8 * 4];
        for (var at = 0; at < 8 * 8; at++)
        {
            int column = at % 8, row = at / 8;
            byte value = (alongX ? column : row) % 2 == 0 ? (byte)40 : (byte)210;
            pixels[at * 4] = pixels[(at * 4) + 1] = pixels[(at * 4) + 2] = value;
            pixels[(at * 4) + 3] = 255;
        }

        return compiled.Program.With([Mipmaps.Of(new GamePicture(8, 8, pixels))]);
    }
}
