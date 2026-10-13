using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's picture of the ground on the screen: the game's own picture taken over pixel for pixel, lines and names drawn on it,
/// and a line saying why where there is nothing to project.
/// </summary>
public class CaptureGroundScreenTests
{
    [Fact]
    public void THEGAMESPictureIsTakenOverPixelForPixelBlueFirstToRgba()
    {
        byte[] bgr = [10, 20, 30, 40, 50, 60];

        var canvas = new CaptureGroundScreen.Canvas(2, 1, bgr);

        Assert.Equal([30, 20, 10, 255, 60, 50, 40, 255], canvas.Rgba);
    }

    [Fact]
    public void ALINEIsDrawnEndToEndAndOneFarOffThePictureNotAtAll()
    {
        var canvas = new CaptureGroundScreen.Canvas(10, 10, null);

        canvas.Line(new Vector2(1, 1), new Vector2(8, 1), [255, 0, 255]);
        canvas.Line(new Vector2(-1e6f, 5), new Vector2(5, 5), [255, 255, 255]);
        canvas.Line(new Vector2(-5, 9), new Vector2(-1, 9), [255, 255, 255]);

        for (var x = 1; x <= 8; x++)
        {
            Assert.Equal(new byte[] { 255, 0, 255, 255 }, Pixel(canvas, x, 1));
        }

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(canvas, 0, 1));
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(canvas, 5, 5));
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(canvas, 0, 9));
    }

    /// <summary>A sub-tile's name, white on a black rim, centred on its point - a glyph's lit pixel white, the rim round it black.</summary>
    [Fact]
    public void ANAMEIsWrittenWhiteOnABlackRimCentredOnItsPoint()
    {
        var canvas = new CaptureGroundScreen.Canvas(40, 30, [.. Enumerable.Repeat((byte)128, 40 * 30 * 3)]);

        canvas.Label("1", 20, 15);

        // "1" is 3 by 5 pixels at three a pixel, its top row lit in the middle only: (left + 3 .. left + 5, top .. top + 2).
        int left = 20 - (4 * 3 / 2), top = 15 - (5 * 3 / 2);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(canvas, left + 4, top + 1));
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(canvas, left + 2, top + 1));
        Assert.Equal(new byte[] { 128, 128, 128, 255 }, Pixel(canvas, 0, 0));
    }

    /// <summary>
    /// A wide shape's triangle filled half seen through by the mask's green - red where it is one, the second ground's - every pixel
    /// whose middle it covers counted, and none outside it touched.
    /// </summary>
    [Fact]
    public void ATRIANGLEIsTintedByTheMasksGreenAndItsPixelsCounted()
    {
        var canvas = new CaptureGroundScreen.Canvas(10, 10, [.. Enumerable.Repeat((byte)100, 10 * 10 * 3)]);
        var mask = new GamePicture(1, 1, [0, 255, 0, 255]);

        (int pixels, int second) = canvas.Tint(new Vector2(0, 0), new Vector2(10.2f, 0), new Vector2(0, 10.2f), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, mask);

        // The pixels whose middles lie in the triangle, none on its edge: x + y + 1 <= 10.2, row y holding 10 - y of them.
        Assert.Equal(55, pixels);
        Assert.Equal(pixels, second);
        Assert.Equal(new byte[] { (100 + 255) >> 1, (100 + 40) >> 1, 100 >> 1, 255 }, Pixel(canvas, 1, 1));
        Assert.Equal(new byte[] { 100, 100, 100, 255 }, Pixel(canvas, 9, 9));
    }

    /// <summary>
    /// The mask's coordinates interpolate as the card's do, over the inverse of the clip w: half way across the screen between a
    /// corner at u nought, w one and a corner at u one, w four, the coordinate is a fifth, not a half.
    /// </summary>
    [Fact]
    public void THEMASKSCoordinatesAreInterpolatedOverTheInverseOfW()
    {
        // A mask ten texels across whose green climbs 25 a texel - read at u 0.2 it is between texels one and two, 37.5.
        byte[] rgba = new byte[10 * 4];
        for (var texel = 0; texel < 10; texel++)
        {
            rgba[(texel * 4) + 1] = (byte)(texel * 25);
            rgba[(texel * 4) + 3] = 255;
        }

        var mask = new GamePicture(10, 1, rgba);
        var canvas = new CaptureGroundScreen.Canvas(101, 3, null);

        // A thin triangle along row one, pixel 50's middle half way between the corners at x 0.5 and x 100.5.
        canvas.Tint(new Vector2(0.5f, 0f), new Vector2(100.5f, 1.5f), new Vector2(100.5f, -1.5f), new Vector3(0f, 0f, 1f), new Vector3(0.25f, 0f, 0.25f), new Vector3(0.25f, 0f, 0.25f), mask);

        Assert.Equal((byte)((int)(255f * (37.5f / 255f)) >> 1), Pixel(canvas, 50, 0)[0]);
    }

    /// <summary>
    /// A sub-tile's four grid corners are held against the game's own corner types where the drawing puts each corner: laid as the game
    /// laid it they are the game's, each to each; mirrored, the two sides change places and they are not - which a check by the four
    /// names in any order could not tell.
    /// </summary>
    /// <remarks>
    /// Two area tiles across, one down: the left column of corners the ground "a", the middle "b", the right unnamed. The sub-tile c1r1's
    /// corners lie at mesh (0, 0), (250, 0), (0, 250) and (250, 250) - TileDefinition.CornersOf's order - so drawn in place they fall on
    /// the area's corners (0, 0), (1, 0), (0, 1) and (1, 1).
    /// </remarks>
    [Fact]
    public void ASUBTILESGridCornersAreHeldAgainstTheGamesWhereTheDrawingPutsThem()
    {
        const int corner = TerrainGroundTypes.BytesPerCorner;
        var corners = new byte[3 * 2 * corner];
        foreach ((int x, int y, byte type) in new[] { (0, 0, (byte)1), (0, 1, (byte)1), (1, 0, (byte)2), (1, 1, (byte)2) })
        {
            corners[((y * 3) + x) * corner] = type;
        }

        TerrainGroundTypes types = Assert.IsType<TerrainGroundTypes>(TerrainGroundTypes.From(["", "Metadata/Test/a.gt", "Metadata/Test/b.gt"], corners, 2, 1));
        string[] grid = ["Metadata/Test/a.gt", "Metadata/Test/b.gt", "Metadata/Test/a.gt", "Metadata/Test/b.gt"];
        var piece = new GroundPiece(1, 1, 2);
        LaidGround Placed(Matrix4x4 place) => new("Metadata/Test/t.tdt", new TilePiece(0, 3, 0, 0, 0, 0, Whole: true), SkinnedMesh.None, [piece], place);

        Assert.True(CaptureGroundScreen.AsTheGame(Placed(Matrix4x4.Identity), piece, grid, types));
        Assert.False(CaptureGroundScreen.AsTheGame(Placed(Matrix4x4.CreateScale(-1f, 1f, 1f) * Matrix4x4.CreateTranslation(250f, 0f, 0f)), piece, grid, types));

        // The right tile's corners: "b", then the unnamed, which the grid's empty name is.
        Assert.True(CaptureGroundScreen.AsTheGame(Placed(Matrix4x4.CreateTranslation(250f, 0f, 0f)), piece, ["Metadata/Test/b.gt", "", "Metadata/Test/b.gt", ""], types));
    }

    [Fact]
    public void WITHNoTilesReadItSaysWhyAndDrawsNothing()
    {
        var snapshot = new WorldSnapshot(true, null, [], new float[16]);

        string said = CaptureGroundScreen.Write(_ => null, snapshot, [], [], null, 100, 100, "black", out GamePicture? picture);

        Assert.Equal("ground on screen: the area's tiles were not read\n", said);
        Assert.Null(picture);
    }

    /// <summary>
    /// Of the area's masters, the one whose MaterialsList names the laid grounds paints them - not the first, whose list here holds the
    /// unnamed group alone, as Act 4's world-map master does - and every master's count is said.
    /// </summary>
    [Fact]
    public void THEAREASMasterNamingTheMostOfItsLaidGroundsIsTheOneThatPaints()
    {
        const string World = "Metadata/Terrain/WorldMaps/Act4/master.tsi", Own = "Metadata/Terrain/Gallows/Act4/4_3_1/master.tsi";
        const string Sand = "Metadata/Terrain/Islands/sand.gt", Ocean = "Metadata/Terrain/Islands/ocean.gt";
        byte[]? Read(string path) => path switch
        {
            World or Own => CaptureGroundTests.Text("version 3\nMaterialsList\t\"materials.mtd\"\n"),
            "Metadata/Terrain/WorldMaps/Act4/materials.mtd" => CaptureGroundTests.Text("version 5\n1 0 \"Art/black.mat\"\n"),
            "Metadata/Terrain/Gallows/Act4/4_3_1/materials.mtd" => CaptureGroundTests.Text(
                "version 5\n1 0 \"Art/cobble.mat\"\n\"Sand\" 1 0 \"Art/sand.mat\"\n\"Ocean\" 1 0 \"Art/ocean.mat\"\n"),
            Sand => CaptureGroundTests.Text("Sand\n1 1 0 0\n"),
            Ocean => CaptureGroundTests.Text("Ocean\n1 1 0 0\n"),
            _ => null,
        };

        (GroundPainter? painter, string master, IReadOnlyList<string> weighed) =
            CaptureGroundScreen.MasterOf(Read, [World, Own], [Sand, Ocean], new MonsterModels.Paints());

        Assert.NotNull(painter);
        Assert.Equal(Own, master);
        Assert.Equal(
            [
                World + ": its MaterialsList Metadata/Terrain/WorldMaps/Act4/materials.mtd names 0 of 2",
                Own + ": its MaterialsList Metadata/Terrain/Gallows/Act4/4_3_1/materials.mtd names 2 of 2",
            ],
            weighed);
    }

    private static byte[] Pixel(CaptureGroundScreen.Canvas canvas, int x, int y)
    {
        int at = ((y * canvas.Width) + x) * 4;
        return canvas.Rgba[at..(at + 4)];
    }
}
