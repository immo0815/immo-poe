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
