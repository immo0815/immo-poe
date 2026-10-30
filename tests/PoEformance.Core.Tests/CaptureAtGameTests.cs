using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The two parts that hold a stage against the game's picture (CaptureAtGame, CaptureLightCompare, CaptureShadowCasters), end to end over a
/// made stage and a made game picture whose difference from the pane is known.
/// </summary>
public class CaptureAtGameTests
{
    private const int Width = 512, Height = 256;

    /// <summary>
    /// A game picture half the pane's light everywhere comes out a ratio of a half in the sun and in the shade, the neutral fit's s a half,
    /// lined up at no shift - and the replay of the kept numbers says the same.
    /// </summary>
    [Fact]
    public void AGAMEPictureHalfThePanesLightIsMeasuredHalfAndReplayedSo()
    {
        CapturePane pane = Stage(MaterialBlend.Opaque);
        WorldSnapshot shot = Shot();
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out string why)!;
        Assert.True(why.Length == 0, why);
        Assert.Equal(1, own.Factor);
        Assert.Contains(own.Picture.Where((_, at) => at % 4 == 3), alpha => alpha == 255);

        byte[] game = Game(own, 0.5f);
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, game, Width, Height, null, out _)!;
        string report = CaptureLightCompare.Write(scene, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);

        string head = report[..report.IndexOf('\n')];
        Assert.Matches(@"median 0\.(4[89]|5[01])\d*; in the pane's sun 0\.(4[89]|5[01])\d* \(", head);
        Assert.Contains("lined up: the edges' correlation at no shift", report, StringComparison.Ordinal);
        Assert.Contains("best", report, StringComparison.Ordinal);
        Assert.Matches(@"all\s+s 0\.(4[89]|5[01])", report);
        Assert.Equal(3, pictures.Count);

        string folder = Directory.CreateTempSubdirectory("light-against-game").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, CaptureLightCompare.Numbers), numbers);
            string replay = CaptureLightCompare.Replay(folder);
            Assert.StartsWith("light against the game replay: ", replay, StringComparison.Ordinal);
            Assert.Equal(head[head.IndexOf(':', StringComparison.Ordinal)..], replay[..replay.IndexOf('\n')][replay.IndexOf(':', StringComparison.Ordinal)..]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A shadow-only slab whose texture is clear casts a shadow a cut takes away; the game's picture, the pane's own, keeps it - so t comes out
    /// near nought, the game casting as the picture does - and the ground's map by kind is read back by the replay.
    /// </summary>
    [Fact]
    public void ACLEARShadowOnlySlabsShadowIsFoundLostWhenCutAndJudgedAgainstTheGame()
    {
        CapturePane pane = Stage(MaterialBlend.ShadowOnly);
        WorldSnapshot shot = Shot();
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Game(own, 1f), Width, Height, null, out _)!;

        string report = CaptureShadowCasters.Write(null, pane, scene, null, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);
        Assert.StartsWith("shadow casters: ", report, StringComparison.Ordinal);
        Assert.Matches(@"[1-9][\d,]* of them are shadow-only shapes' that a cut", report);
        Assert.Matches(@"t over the [\d,]+ judged -?0(\.0\d*)?\s", report);
        Assert.Matches(@"Art/canopy\.mat\s+1\s+1\s", report);
        Assert.Equal(2, pictures.Count);

        string folder = Directory.CreateTempSubdirectory("shadow-casters").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, CaptureShadowCasters.Numbers), numbers);
            string replay = CaptureShadowCasters.Replay(folder);
            Assert.StartsWith("shadow casters replay: 2048 texels across", replay, StringComparison.Ordinal);
            Assert.Matches(@"a shadow-only shape's a cut takes away: [1-9]", replay);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A floor laid in the area under a slab of the blend asked, lit by a sun from the side.</summary>
    private static CapturePane Stage(MaterialBlend slab)
    {
        Vector3[] places =
        [
            new(-600f, -600f, 0f), new(600f, -600f, 0f), new(600f, 600f, 0f), new(-600f, 600f, 0f),
            new(-120f, -120f, -150f), new(120f, -120f, -150f), new(120f, 120f, -150f), new(-120f, 120f, -150f),
            new(-420f, -420f, -150f), new(-260f, -420f, -150f), new(-260f, -260f, -150f), new(-420f, -260f, -150f),
        ];
        Vector3[] normals = [.. places.Select(_ => -Vector3.UnitZ)];
        Vector2[] spots = [.. places.Select(place => new Vector2((place.X / 1200f) + 0.5f, (place.Y / 1200f) + 0.5f))];
        SkinnedMesh mesh = SkinnedMesh.Of(
            places, normals, [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11], new Vector3(-600f, -600f, -150f), new Vector3(600f, 600f, 0f), spots,
            [new MeshShape("floor", 0, 6), new MeshShape("slab", 6, 6), new MeshShape("rock", 12, 6)]);
        var floor = new byte[16 * 16 * 4];
        for (var at = 0; at < floor.Length; at += 4)
        {
            floor[at] = 150;
            floor[at + 1] = 120;
            floor[at + 2] = 90;
            floor[at + 3] = 255;
        }

        Mipmaps ground = Mipmaps.Of(new GamePicture(16, 16, floor))!;
        Mipmaps clear = Mipmaps.Of(new GamePicture(16, 16, new byte[16 * 16 * 4]))!;
        var model = new MonsterModel(mesh, null, "stage", string.Empty, string.Empty)
        {
            AreaOrigin = new Vector2(5000f, 9000f),
            AreaStamp = 1,
            ShapeMaterials = ["Art/ground.mat", "Art/canopy.mat", "Art/rock.mat"],
            ShapeSources = [RoomModelsGround + "tile", "canopy.ao", "rock.ao"],
            Modes = ["Opaque", slab == MaterialBlend.ShadowOnly ? "AlphaTestWithShadowOnly" : "Opaque", "Opaque"],
            Skins = [ground, slab == MaterialBlend.ShadowOnly ? clear : ground, ground],
        };
        var light = new SceneLight([], SceneLight.PointShape.Zero, null)
        {
            Ambient = SceneAmbient.Flat,
            FlatAmbient = 0.05f,
            SunColour = new Vector3(2f),
            SunDirection = Vector3.Normalize(new Vector3(0.2f, 0.1f, 1f)),
            SunShadows = true,
        };
        return new CapturePane("tile", [], model)
        {
            Light = light,
            LightOn = true,
            Blends = [MaterialBlend.Opaque, slab, MaterialBlend.Opaque],
        };
    }

    /// <summary>The ground source's word, as the room builder writes it.</summary>
    private const string RoomModelsGround = "ground of ";

    /// <summary>A camera looking down on the stage from above and to one side, the stage's corner at (5000, 9000).</summary>
    private static WorldSnapshot Shot()
    {
        var target = new Vector3(5000f, 9000f, 0f);
        Vector3 forward = Vector3.Normalize(new Vector3(0.47f, 0.47f, 0.75f));
        Vector3 eye = target - (forward * 2200f);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 down = Vector3.Cross(right, forward);
        const float focal = 0.5f * Height / 0.2679492f;
        Vector3 ax = (((2f * (Width / 2f) / Width) - 1f) * forward) + (2f * focal / Width * right);
        Vector3 ay = ((1f - (2f * (Height / 2f) / Height)) * forward) - (2f * focal / Height * down);
        const float near = 100f, far = 6000f;
        float a = far / (far - near), b = -far * near / (far - near);
        Vector3 az = a * forward;
        float[] matrix =
        [
            ax.X, ay.X, az.X, forward.X,
            ax.Y, ay.Y, az.Y, forward.Y,
            ax.Z, ay.Z, az.Z, forward.Z,
            -Vector3.Dot(ax, eye), -Vector3.Dot(ay, eye), b - Vector3.Dot(az, eye), -Vector3.Dot(forward, eye),
        ];
        return new WorldSnapshot(true, null, [], matrix);
    }

    /// <summary>A game picture: the pane's own drawn picture, its light times a share, blue green red at the screen's size.</summary>
    private static byte[] Game(CaptureAtGame drawn, float share)
    {
        var bgr = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                int from = (((y / drawn.Factor) * drawn.DrawnWidth) + (x / drawn.Factor)) * 4;
                int to = ((y * Width) + x) * 3;
                for (var c = 0; c < 3; c++)
                {
                    float linear = Decode(drawn.Picture[from + c]) * share;
                    bgr[to + 2 - c] = Encode(linear);
                }
            }
        }

        return bgr;
    }

    private static float Decode(byte value)
    {
        float v = value / 255f;
        return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    private static byte Encode(float linear)
    {
        float v = Math.Clamp(linear, 0f, 1f);
        float e = v <= 0.0031308f ? v * 12.92f : (1.055f * MathF.Pow(v, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)((e * 255f) + 0.5f), 0, 255);
    }
}
