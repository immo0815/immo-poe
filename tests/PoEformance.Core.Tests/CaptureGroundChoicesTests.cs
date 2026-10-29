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
    /// A sub-tile of one ground whose group offers two noises, and a picture of the first drawn six units along x and four back along y
    /// from where the tool places it - as the game's picture of Port's pier lay a few units off the tool's places: the first wins, lined up
    /// at that shift, clearly over its controls, and the picture is tinted.
    /// </summary>
    [Fact]
    public void THEMATERIALWhosePatternThePictureShowsWinsLinedUp()
    {
        var grounds = new SubTileGrounds(["Metadata/Test/sand.gt", "Metadata/Test/sand.gt", "Metadata/Test/sand.gt", "Metadata/Test/sand.gt"], ["Sand"], string.Empty)
        {
            Gridded = true,
            Materials = ["Art/noise-b.mat"],
            Choices = [[("Art/noise-a.mat", 30), ("Art/noise-b.mat", 70)]],
        };

        string said = Measured(grounds, out GamePicture? picture);

        Assert.StartsWith("material choices: 10000 pixels in 1 sub-tiles of at least 200 pixels", said, StringComparison.Ordinal);
        Assert.Contains("1 of 1 sub-tiles have one - 1 of the 1 of one ground, the clean test", said, StringComparison.Ordinal);
        Assert.Contains("the clear winners lined up shifted by (+6, -4) units at the median", said, StringComparison.Ordinal);
        Assert.Contains(" noise-a weight 30 (30%), red: won 1 (10000 px);", said, StringComparison.Ordinal);
        Assert.Contains(" noise-b weight 70 (70%), the painter's, green: won 0 (0 px);", said, StringComparison.Ordinal);
        Assert.Contains("    t at tile 0, 0 · 1 sub-tiles: noise-a x1", said, StringComparison.Ordinal);
        Assert.Contains(" *noise-a r +", said, StringComparison.Ordinal);
        Assert.Contains(" at (+6, -4); noise-b r ", said, StringComparison.Ordinal);
        GamePicture tinted = Assert.IsType<GamePicture>(picture);
        Assert.True(tinted.Rgba[((50 * Wide) + 50) * 4] > tinted.Rgba[(((50 * Wide) + 50) * 4) + 2]);
    }

    /// <summary>
    /// A sub-tile naming a ground the MaterialsList does not is held against every material of the list, under that ground's name -
    /// the painter's being the group with no name's (CHOSEN) - so the capture says which the game drew there.
    /// </summary>
    [Fact]
    public void ANUNLISTEDGroundIsHeldAgainstEveryMaterialOfTheList()
    {
        const string Deep = "Metadata/Test/stromatolite_deep.gt";
        var grounds = new SubTileGrounds([Deep, Deep, Deep, Deep], [""], string.Empty)
        {
            Gridded = true,
            Materials = ["Art/noise-b.mat"],
            Choices = [[("Art/noise-b.mat", 70)]],
            Unlisted = ["StromatoliteDeep"],
            Everything = [("Art/noise-b.mat", 0), ("Art/noise-a.mat", 0)],
        };

        string said = Measured(grounds, out _);

        Assert.Contains("  StromatoliteDeep, which the list does not name · 1 sub-tiles of it alone on the screen: noise-b weight 0, the painter's, red: won 0 (0 px); noise-a weight 0, green: won 1 (10000 px);", said, StringComparison.Ordinal);
        Assert.Contains("    t c1r1 · 10000 px · StromatoliteDeep, which the list does not name · *noise-a r +", said, StringComparison.Ordinal);
    }

    private const int Wide = 100, Tall = 100;

    /// <summary>The choices measured over one sub-tile a picture wide, the picture the first noise as its own program draws it, each place shifted by (6, -4).</summary>
    private static string Measured(SubTileGrounds grounds, out GamePicture? picture)
    {
        var mesh = SkinnedMesh.Of(
            [new Vector3(0f, 0f, 0f), new Vector3(250f, 0f, 0f), new Vector3(0f, 250f, 0f), new Vector3(250f, 250f, 0f)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [0, 1, 2, 1, 3, 2],
            Vector3.Zero,
            new Vector3(250f, 250f, 0f));
        var laid = new LaidGround("Metadata/Test/t.tdt", new TilePiece(0, 3, 0, 0, 0, 0, Whole: true), mesh, [new GroundPiece(1, 1, 2)], Matrix4x4.Identity);

        // THE CAMERA: the sub-tile's 250 units across the picture's hundred pixels, x right and y down.
        var matrix = new float[16];
        matrix[0] = 2f / 250f;
        matrix[12] = -1f;
        matrix[5] = -2f / 250f;
        matrix[13] = 1f;
        matrix[15] = 1f;

        ShadeProgram noiseA = Noise(seed: 1), noiseB = Noise(seed: 2);
        ShadeProgram? Programs(string material) => material switch
        {
            "Art/noise-a.mat" => noiseA,
            "Art/noise-b.mat" => noiseB,
            _ => null,
        };

        // THE PICTURE: the first noise as its own program draws it, each pixel's place shifted by (6, -4).
        var reader = new ShadeProgram.GroundReader(noiseA);
        reader.Triangle(Vector3.Zero, new Vector3(250f, 0f, 0f), new Vector3(0f, 250f, 0f), Vector2.Zero, new Vector2(Wide, 0f), new Vector2(0f, Tall), GroundPainter.GroundScale);
        var bgr = new byte[Wide * Tall * 3];
        for (var y = 0; y < Tall; y++)
        {
            for (var x = 0; x < Wide; x++)
            {
                var place = new Vector3(((x + 0.5f) * 2.5f) + 6f, ((y + 0.5f) * 2.5f) - 4f, 0f);
                var value = (byte)Math.Clamp((int)(reader.BrightnessAt(place, GroundPainter.GroundScale) * 255f), 0, 255);
                int at = ((y * Wide) + x) * 3;
                bgr[at] = bgr[at + 1] = bgr[at + 2] = value;
            }
        }

        return CaptureGroundChoices.Measure([new CaptureGroundChoices.Piece(laid, [grounds])], matrix, Wide, Tall, bgr, Programs, out picture);
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

    /// <summary>A material reading one texture of noise at its coordinates, 64 texels to the unit - a texel about four units of the world.</summary>
    private static ShadeProgram Noise(int seed)
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
        var random = new Random(seed);
        var pixels = new byte[64 * 64 * 4];
        for (var at = 0; at < 64 * 64; at++)
        {
            var value = (byte)random.Next(20, 236);
            pixels[at * 4] = pixels[(at * 4) + 1] = pixels[(at * 4) + 2] = value;
            pixels[(at * 4) + 3] = 255;
        }

        return compiled.Program.With([Mipmaps.Of(new GamePicture(64, 64, pixels))]);
    }
}
