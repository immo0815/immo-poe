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

        string said = CaptureGroundScreen.Write(_ => null, snapshot, [], null, 100, 100, "black", out GamePicture? picture);

        Assert.Equal("ground on screen: the area's tiles were not read\n", said);
        Assert.Null(picture);
    }

    private static byte[] Pixel(CaptureGroundScreen.Canvas canvas, int x, int y)
    {
        int at = ((y * canvas.Width) + x) * 4;
        return canvas.Rgba[at..(at + 4)];
    }
}
