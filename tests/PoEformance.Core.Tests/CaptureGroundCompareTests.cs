using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's measure of how two grounds are mixed: the mask's green and the corners' share, each held against the game's picture.
/// </summary>
public partial class CaptureGroundCompareTests
{
    /// <summary>The second ground's share runs bilinear from its corners: corner k at (k &amp; 1, k &gt;&gt; 1) of the sub-tile.</summary>
    [Fact]
    public void THESECONDGroundsShareRunsBilinearFromItsCorners()
    {
        Assert.Equal(0.25f, CaptureGroundCompare.Share([0f, 1f, 0f, 1f], 0.25f, 0.9f), 5);
        Assert.Equal(0.75f, CaptureGroundCompare.Share([0f, 0f, 1f, 1f], 0.1f, 0.75f), 5);
        Assert.Equal(0.25f, CaptureGroundCompare.Share([0f, 0f, 0f, 1f], 0.5f, 0.5f), 5);
    }

    /// <summary>The picture's local contrast is its brightness's spread over nine pixels square: nought on an even picture, the stripes' half on stripes.</summary>
    [Fact]
    public void THELOCALContrastIsTheBrightnessSpreadOverNinePixelsSquare()
    {
        const int width = 20, height = 20;
        var even = new byte[width * height * 3];
        Array.Fill(even, (byte)90);
        (byte[] light, float[] contrast) = CaptureGroundCompare.Measures(even, width, height);
        Assert.All(light, one => Assert.Equal(90, one));
        Assert.All(contrast, one => Assert.Equal(0f, one, 3));

        // Stripes a pixel wide, 0 and 200: away from the edges a window holds four or five of each, a spread near a hundred.
        var stripes = new byte[width * height * 3];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            byte value = (pixel % width) % 2 == 0 ? (byte)0 : (byte)200;
            stripes[pixel * 3] = stripes[(pixel * 3) + 1] = stripes[(pixel * 3) + 2] = value;
        }

        (_, float[] striped) = CaptureGroundCompare.Measures(stripes, width, height);
        Assert.InRange(striped[(10 * width) + 10], 99f, 100.1f);
    }

    /// <summary>
    /// A sub-tile whose second ground has the two corners on its far side, over a picture that brightens the same way: the corners'
    /// share follows the picture all but exactly, the mask - none here - is even, and the picture is tinted where measured.
    /// </summary>
    /// <remarks>
    /// The sub-tile c1r1 lies at mesh x and y 0 to 250 (TileDefinition.CornersOf) and the camera lays that square over the whole
    /// picture, so its corners 1 and 3 - the second ground's - fall on the picture's right side.
    /// </remarks>
    [Fact]
    public void ASUBTILEWhoseGroundFollowsItsCornersIsSaidToFollowThem()
    {
        const int width = 100, height = 100;
        var mesh = SkinnedMesh.Of(
            [new Vector3(0f, 0f, 0f), new Vector3(250f, 0f, 0f), new Vector3(0f, 250f, 0f), new Vector3(250f, 250f, 0f)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [0, 1, 2, 1, 3, 2],
            Vector3.Zero,
            new Vector3(250f, 250f, 0f));
        var piece = new GroundPiece(1, 1, 2);
        var laid = new LaidGround("Metadata/Test/t.tdt", new TilePiece(0, 3, 0, 0, 0, 0, Whole: true), mesh, [piece], Matrix4x4.Identity);
        var grounds = new SubTileGrounds(["", "Metadata/Test/sand.gt", "", "Metadata/Test/sand.gt"], ["", "TropicalFill"], string.Empty)
        {
            Gridded = true,
            Seconds = [false, true, false, true],
        };

        // Clip x and y from the world's, column-major: the square 0 to 250 across the picture, y down the screen.
        var matrix = new float[16];
        matrix[0] = 2f / 250f;
        matrix[12] = -1f;
        matrix[5] = -2f / 250f;
        matrix[13] = 1f;
        matrix[15] = 1f;
        var bgr = new byte[width * height * 3];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            byte value = (byte)(2 * (pixel % width));
            bgr[pixel * 3] = bgr[(pixel * 3) + 1] = bgr[(pixel * 3) + 2] = value;
        }

        string said = CaptureGroundCompare.Measure([new CaptureGroundCompare.Piece(laid, [grounds], null)], matrix, width, height, bgr, out GamePicture? picture);

        Assert.StartsWith("mask against corners: 10000 pixels where the nearest ground is a sub-tile of two grounds, 1 such sub-tiles", said, StringComparison.Ordinal);
        Assert.Contains("  (the group with no name) + TropicalFill · 10000 pixels in 1 sub-tiles", said, StringComparison.Ordinal);
        Assert.Contains("    brightness: mask r n/a (the field is even) · corners r ", said, StringComparison.Ordinal);
        double r = double.Parse(Corners().Match(said).Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.InRange(r, 0.999, 1.0);
        Assert.Contains("    t c1r1 · 10000 px · (the group with no name) + TropicalFill · corners 1 2 1 2 · brightness n/a +1.00", said, StringComparison.Ordinal);

        // Tinted where measured: the left side towards blue, the right towards red.
        GamePicture tinted = Assert.IsType<GamePicture>(picture);
        Assert.True(tinted.Rgba[(50 * width + 2) * 4 + 2] > tinted.Rgba[(50 * width + 2) * 4]);
        Assert.True(tinted.Rgba[(50 * width + 97) * 4] > tinted.Rgba[(50 * width + 97) * 4 + 2]);
    }

    /// <summary>
    /// Weighed by the grounds' heights as the engine weighs them: the first ground writes none (nought), the second writes one and turns
    /// the height blending on - so every pixel is weighed, the first ground's height under a half on all of them, and the corners' share
    /// is pulled towards the second ground; the heights' picture is drawn. Without programs nothing is weighed and the line says why.
    /// </summary>
    [Fact]
    public void BOTHCandidatesAreWeighedByTheGroundsHeightsWhereTheirProgramsReadThem()
    {
        const int width = 100, height = 100;
        (CaptureGroundCompare.Piece piece, float[] matrix, byte[] bgr) = Square(width, height, ["Art/plain.mat", "Art/raised.mat"]);
        ShadeProgram plain = Compiled(Ground(0.2f, null, blends: false));
        ShadeProgram raised = Compiled(Ground(0.8f, 1f, blends: true)).Heighted!;
        ShadeProgram? Programs(string material) => material switch
        {
            "Art/plain.mat" => plain,
            "Art/raised.mat" => raised,
            _ => null,
        };

        string said = CaptureGroundCompare.Measure([piece], matrix, width, height, bgr, Programs, out _, out GamePicture? heights);

        Assert.Contains("mask by heights, corners by heights: 10000 pixels weighed", said, StringComparison.Ordinal);
        Assert.Contains("    heights: 10000 pixels weighed · the first ground's height under 0.5 on 100.0% of them, the second's on 0.0%", said, StringComparison.Ordinal);
        Assert.Contains(" · corners by heights r +", said, StringComparison.Ordinal);
        Assert.Contains(" · by heights brightness ", said, StringComparison.Ordinal);
        Assert.NotNull(heights);

        // A share of a half weighed by a height of nought against one: the second ground takes 0.5 / (0.5 + 0.5 / 101) of it.
        Assert.Equal(0.5f / (0.5f + (0.5f / 101f)), ShadeProgram.WeighedShare(0.5f, 0f, 1f), 5);

        string unread = CaptureGroundCompare.Measure([piece], matrix, width, height, bgr, null, out _, out GamePicture? none);
        Assert.Contains("    heights: not weighed - no programs to read the heights with", unread, StringComparison.Ordinal);
        Assert.Null(none);
    }

    /// <summary>The one-sub-tile square of <see cref="ASUBTILEWhoseGroundFollowsItsCornersIsSaidToFollowThem"/>, its two grounds' materials given.</summary>
    private static (CaptureGroundCompare.Piece Piece, float[] Matrix, byte[] Bgr) Square(int width, int height, IReadOnlyList<string> materials)
    {
        var mesh = SkinnedMesh.Of(
            [new Vector3(0f, 0f, 0f), new Vector3(250f, 0f, 0f), new Vector3(0f, 250f, 0f), new Vector3(250f, 250f, 0f)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [0, 1, 2, 1, 3, 2],
            Vector3.Zero,
            new Vector3(250f, 250f, 0f));
        var laid = new LaidGround("Metadata/Test/t.tdt", new TilePiece(0, 3, 0, 0, 0, 0, Whole: true), mesh, [new GroundPiece(1, 1, 2)], Matrix4x4.Identity);
        var grounds = new SubTileGrounds(["", "Metadata/Test/sand.gt", "", "Metadata/Test/sand.gt"], ["", "TropicalFill"], string.Empty)
        {
            Gridded = true,
            Seconds = [false, true, false, true],
            Materials = materials,
        };

        var matrix = new float[16];
        matrix[0] = 2f / 250f;
        matrix[12] = -1f;
        matrix[5] = -2f / 250f;
        matrix[13] = 1f;
        matrix[15] = 1f;
        var bgr = new byte[width * height * 3];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            byte value = (byte)(2 * (pixel % width));
            bgr[pixel * 3] = bgr[(pixel * 3) + 1] = bgr[(pixel * 3) + 2] = value;
        }

        return (new CaptureGroundCompare.Piece(laid, [grounds], null), matrix, bgr);
    }

    private static ShadeProgram Compiled(string json)
    {
        ShadeCompile compiled = ShadeProgram.Compile(
            [(new ShaderInstance("Metadata/Test.fxgraph", new Dictionary<string, ShaderValue[]>()), ShaderGraph.Read(System.Text.Encoding.UTF8.GetBytes(json)))]);
        Assert.NotNull(compiled.Program);
        return compiled.Program;
    }

    /// <summary>A grey ground, with a constant height where one is given and the fragment turning the height blending on where asked.</summary>
    private static string Ground(float grey, float? height, bool blends)
    {
        string g = grey.ToString(CultureInfo.InvariantCulture);
        string nodes = $$"""{"type":"ConstantPixel3","index":0,"parameters":[{"value":[{{g}},{{g}},{{g}}]}]},{"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}""";
        string links = """{"src":{"type":"ConstantPixel3","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}""";
        if (height is { } h)
        {
            nodes += $$""",{"type":"ConstantFloat","index":0,"parameters":[{"value":{{h.ToString(CultureInfo.InvariantCulture)}}}]},{"type":"Height","index":0,"stage":"Texturing_Init"}""";
            links += """,{"src":{"type":"ConstantFloat","index":0,"variable":"output"},"dst":{"type":"Height","index":0,"stage":"Texturing_Init","variable":"input"}}""";
        }

        if (blends)
        {
            nodes += """,{"type":"EnableGroundHeightBlending","index":0}""";
        }

        return "{\"nodes\":[" + nodes + "],\"links\":[" + links + "]}";
    }

    [GeneratedRegex(@"brightness: mask r n/a \(the field is even\) · corners r ([+-][0-9.]+)")]
    private static partial Regex Corners();
}
