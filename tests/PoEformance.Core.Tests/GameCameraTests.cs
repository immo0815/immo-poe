using System.Numerics;
using PoEformance.Game.Files;
using PoEformance.Game.World;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The game's camera taken apart (GameCamera) and drawn through (MeshPicture.Camera.Through): every point lands where WorldToScreen puts it.
/// </summary>
public class GameCameraTests
{
    /// <summary>Augury's camera as its capture wrote it (tests/fixtures/captures/augury/capture.txt), on its 3440 x 1440 screen.</summary>
    private static readonly float[] Augury =
        [1.105f, 1.983f, 0.477f, 0.467f, -1.105f, 1.983f, 0.477f, 0.467f, 0f, -2.462f, 0.769f, 0.751f, 4178.573f, -33445.98f, -5932.682f, -5724.86f];

    private static readonly Vector3 AuguryPlayer = new(6581.521f, 10364.13f, -87.825f);

    /// <summary>The camera's own arithmetic puts every point where WorldToScreen does - over the ground round the player and above it.</summary>
    [Fact]
    public void THECAMERAPutsEveryPointWhereWorldToScreenDoes()
    {
        GameCamera camera = GameCamera.Of(Augury, 3440, 1440)!;
        foreach (Vector3 point in Around(AuguryPlayer))
        {
            ScreenPoint expected = WorldToScreen.Project(Augury, point.X, point.Y, point.Z, 3440, 1440);
            Vector2 got = camera.Pixel(point);
            Assert.True(MathF.Abs(got.X - expected.X) < 0.02f && MathF.Abs(got.Y - expected.Y) < 0.02f, $"{point}: {got} against ({expected.X}, {expected.Y})");
        }

        // THE FRAME IS THE GAME'S, LEFT-HANDED: right x down = -forward, the improper frame CameraPose.Turning builds.
        Assert.True(Vector3.Distance(Vector3.Cross(camera.Right, camera.Down), -camera.Forward) < 1e-4f);
        Assert.InRange(Vector3.Distance(camera.Eye, AuguryPlayer), 2100f, 2150f);
        Assert.InRange(2d * Math.Atan(1440d / (2d * camera.FocalY)) * 180d / Math.PI, 29.9, 30.1);
    }

    /// <summary>A camera made to order - looking off its middle, a frustum a side panel would make lopsided - comes back as it was made.</summary>
    [Fact]
    public void ACAMERAMadeToOrderComesBackAsItWasMade()
    {
        const int width = 2560, height = 1440;
        var eye = new Vector3(1200f, -300f, -1800f);
        Vector3 forward = Vector3.Normalize(new Vector3(0.4f, 0.55f, 0.73f));
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 down = Vector3.Cross(right, forward);
        Assert.True(Vector3.Distance(Vector3.Cross(right, down), -forward) < 1e-5f);
        const float focal = 2100f, near = 120f, far = 3600f;
        var principal = new Vector2(1180f, 760f);
        float[] matrix = Matrix(eye, right, down, forward, focal, principal, width, height, near, far);

        GameCamera camera = GameCamera.Of(matrix, width, height)!;
        Assert.True(Vector3.Distance(camera.Eye, eye) < 0.05f, camera.Eye.ToString());
        Assert.True(Vector3.Distance(camera.Forward, forward) < 1e-5f);
        Assert.True(Vector3.Distance(camera.Right, right) < 1e-5f);
        Assert.True(Vector3.Distance(camera.Down, down) < 1e-5f);
        Assert.InRange(camera.FocalX, focal - 0.05, focal + 0.05);
        Assert.InRange(camera.FocalY, focal - 0.05, focal + 0.05);
        Assert.True(Vector2.Distance(camera.Principal, principal) < 0.01f, camera.Principal.ToString());
        Assert.InRange(camera.Near, near - 0.5f, near + 0.5f);
        Assert.InRange(camera.Far, far - 5f, far + 5f);
        Assert.InRange(camera.Skew, -1e-6, 1e-6);

        foreach (Vector3 point in Around(eye + (forward * 1500f)))
        {
            ScreenPoint expected = WorldToScreen.Project(matrix, point.X, point.Y, point.Z, width, height);
            Vector2 got = camera.Pixel(point);
            Assert.True(MathF.Abs(got.X - expected.X) < 0.02f && MathF.Abs(got.Y - expected.Y) < 0.02f, $"{point}: {got} against ({expected.X}, {expected.Y})");
        }
    }

    /// <summary>
    /// A picture drawn through the game's camera over a stage laid in the area - its space the world less its corner - puts each point on the
    /// game's pixel, shrunk onto the square canvas or in two halves side by side.
    /// </summary>
    /// <remarks>
    /// ONE FOCAL LENGTH: the lens takes the mean of the matrix's two. Augury's matrix as its capture wrote it, to three places, holds them
    /// 2687.6 against 2686.6 - the rounding's - so a point is held to the difference times its distance from the principal point.
    /// </remarks>
    [Theory]
    [InlineData(1720, 0.5f, 0f)]
    [InlineData(1720, 1f, 0f)]
    [InlineData(1720, 1f, 1720f)]
    public void THELENSPutsTheStageOnTheGamesPixels(int side, float shrink, float offset)
    {
        GameCamera game = GameCamera.Of(Augury, 3440, 1440)!;
        var origin = new Vector2(5250f, 9000f);
        MeshPicture.Camera lens = MeshPicture.Camera.Through(game, origin, side, new Vector2(offset, 0f), shrink);
        Assert.True(lens.Ready && lens.Perspective);
        float unequal = (float)Math.Abs((game.FocalX / game.FocalY) - 1d);
        foreach (Vector3 point in Around(AuguryPlayer))
        {
            ScreenPoint expected = WorldToScreen.Project(Augury, point.X, point.Y, point.Z, 3440, 1440);
            Vector3 placed = lens.Place(point - new Vector3(origin, 0f));
            var pixel = new Vector2((placed.X * side / shrink) + offset, placed.Y * side / shrink);
            Vector2 off = new Vector2(expected.X, expected.Y) - game.Principal;
            float slackX = 0.05f + (unequal * MathF.Abs(off.X)), slackY = 0.05f + (unequal * MathF.Abs(off.Y));
            Assert.True(MathF.Abs(pixel.X - expected.X) < slackX && MathF.Abs(pixel.Y - expected.Y) < slackY, $"{point}: {pixel} against ({expected.X}, {expected.Y})");
        }

        // A CAMERA WHOSE FOCAL LENGTHS AGREE, as the game's own do, lands to a twentieth of a pixel.
        const int width = 2560, height = 1440;
        var eye = new Vector3(1200f, -300f, -1800f);
        Vector3 forward = Vector3.Normalize(new Vector3(0.4f, 0.55f, 0.73f));
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        float[] matrix = Matrix(eye, right, Vector3.Cross(right, forward), forward, 2100f, new Vector2(1180f, 760f), width, height, 120f, 3600f);
        GameCamera exact = GameCamera.Of(matrix, width, height)!;
        MeshPicture.Camera through = MeshPicture.Camera.Through(exact, origin, side, new Vector2(offset, 0f), shrink);
        foreach (Vector3 point in Around(eye + (forward * 1500f)))
        {
            ScreenPoint expected = WorldToScreen.Project(matrix, point.X, point.Y, point.Z, width, height);
            Vector3 placed = through.Place(point - new Vector3(origin, 0f));
            var pixel = new Vector2((placed.X * side / shrink) + offset, placed.Y * side / shrink);
            Assert.True(Vector2.Distance(pixel, new Vector2(expected.X, expected.Y)) < 0.05f, $"{point}: {pixel} against ({expected.X}, {expected.Y})");
        }
    }

    /// <summary>A floor drawn through the lens covers the pixel under the player and nothing past the rows asked for.</summary>
    [Fact]
    public void AFLOORDrawnThroughTheLensFillsThePlayersPixelAndOnlyTheRowsAskedFor()
    {
        GameCamera game = GameCamera.Of(Augury, 3440, 1440)!;
        var origin = new Vector2(5250f, 9000f);
        const int side = 430;
        const float shrink = 0.125f;
        var canvas = new MeshPicture.Canvas(side, threads: 2)
        {
            Lens = MeshPicture.Camera.Through(game, origin, side, default, shrink),
            Rows = 180,
        };
        Vector3 player = AuguryPlayer - new Vector3(origin, 0f);
        float half = 3000f;
        Vector3[] places =
        [
            new(player.X - half, player.Y - half, 0f), new(player.X + half, player.Y - half, 0f), new(player.X + half, player.Y + half, 0f), new(player.X - half, player.Y + half, 0f),
        ];
        SkinnedMesh floor = SkinnedMesh.Of(places, [.. places.Select(_ => -Vector3.UnitZ)], [0, 1, 2, 0, 2, 3], places[0], places[2]);
        GamePicture picture = MeshPicture.Of(floor, canvas);

        ScreenPoint under = WorldToScreen.Project(Augury, AuguryPlayer.X, AuguryPlayer.Y, 0f, 3440, 1440);
        int x = (int)(under.X * shrink), y = (int)(under.Y * shrink);
        Assert.Equal(255, picture.Rgba[(((y * side) + x) * 4) + 3]);
        for (int row = 180; row < side; row++)
        {
            Assert.Equal(0, picture.Rgba[(((row * side) + (side / 2)) * 4) + 3]);
        }
    }

    /// <summary>Points round a place: on its level and above it, out to a few hundred units each way.</summary>
    private static IEnumerable<Vector3> Around(Vector3 middle)
    {
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                yield return middle + new Vector3(dx * 300f, dy * 300f, 0f);
                yield return middle + new Vector3(dx * 300f, dy * 300f, -250f);
            }
        }
    }

    /// <summary>The column-major world-to-clip matrix of a camera, as the game keeps it: depth nought at the near plane, one at the far.</summary>
    private static float[] Matrix(Vector3 eye, Vector3 right, Vector3 down, Vector3 forward, float focal, Vector2 principal, int width, int height, float near, float far)
    {
        Vector3 ax = (((2f * principal.X / width) - 1f) * forward) + (2f * focal / width * right);
        Vector3 ay = ((1f - (2f * principal.Y / height)) * forward) - (2f * focal / height * down);
        float a = far / (far - near), b = -far * near / (far - near);
        Vector3 az = a * forward;
        Vector3 aw = forward;
        return
        [
            ax.X, ay.X, az.X, aw.X,
            ax.Y, ay.Y, az.Y, aw.Y,
            ax.Z, ay.Z, az.Z, aw.Z,
            -Vector3.Dot(ax, eye), -Vector3.Dot(ay, eye), b - Vector3.Dot(az, eye), -Vector3.Dot(aw, eye),
        ];
    }
}
