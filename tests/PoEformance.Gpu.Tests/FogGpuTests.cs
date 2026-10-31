using System.Diagnostics;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Gpu.Tests;

/// <summary>
/// The screen-space fog's pass on the card (ModelGpu's Fogged, ModelShaders.Screened), held against its program run on the processor over the
/// same pixels - the Red Vale's own graphs (tests/fixtures/fog), compiled by the game's compiler, on WARP.
/// </summary>
/// <remarks>
/// THE PROCESSOR DRAWS NO FOG (DECIDED from the live client), so the card is held against what the pass is meant to come to: the program's
/// pixel colour (ShadeProgram.RunScreen) at the floor point behind each pixel - worked out here from the camera, not read back from the card's
/// depth - exposed, put into sRGB and laid over the card's own picture without the fog by its cover.
/// </remarks>
public class FogGpuTests(CardFixture card) : IClassFixture<CardFixture>
{
    private const int Side = ModelGpuTests.Side;

    /// <summary>The floor's height: 60 under the fog's bottom at nought - up is minus z, the fog standing from 0 to -400.</summary>
    private const float Floor = 60f;

    /// <summary>How long a picture waits for its shaders before the wait is the failure.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    [GpuFact]
    public void THEFOGIsLaidOverThePictureAsItsProgramRunsOnTheProcessor()
    {
        ModelGpu? gpu = card.Gpu;
        Assert.True(gpu is not null, card.Why);
        using ModelTarget target = gpu.Target(Side);
        SkinnedMesh floor = Sheet();
        var pose = new CameraPose(new Vector3(200f, -150f, Floor), 0.6f, 0.9f, 2200f);
        var sun = new Vector3(1.4f, 1.3f, 1.1f);
        const float exposure = 1.3f;
        const float clock = 7.3f;
        ScreenFog fog = ScreenFog.Made(Bound(), new Vector4(0.13898f, 0.10869f, 0.11924f, 0.1f), new Vector2(1300f, 0.34999f), new Vector3(0.21f, 0.24f, 0.3f));
        SceneLight Light(ScreenFog? with) => new([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = sun,
            SunDirection = Vector3.Normalize(new Vector3(0.4f, -1f, 0.3f)),
            Ambient = SceneAmbient.Flat,
            FlatAmbient = 0.1f,
            Exposure = exposure,
            Fog = with,
        };

        ModelScene Scene(SceneLight light) => new(floor, Blends: [MaterialBlend.Opaque], Light: light, Time: clock, Pose: pose);

        byte[] bare = Drawn(gpu, target, Scene(Light(null)));
        byte[] fogged = Drawn(gpu, target, Scene(Light(fog)));

        // THE FLOOR POINT BEHIND EACH PIXEL, from the camera: the pixel's share less the centre over the focal length is the view's ray at a
        // depth of one - ModelGpu's clip matrix undone - turned back into the model's space and met with the floor.
        MeshPicture.Camera camera = MeshPicture.Camera.Of(floor, pose);
        Assert.True(camera.Perspective);
        Assert.True(Matrix4x4.Invert(camera.View, out Matrix4x4 back));
        var registers = new Vector4[fog.Program!.Registers];
        var compared = 0;
        var covered = 0;
        for (var py = 4; py < Side; py += 11)
        {
            for (var px = 4; px < Side; px += 11)
            {
                var ray = new Vector3((((px + 0.5f) / Side) - camera.Centre.X) / camera.Scale, (((py + 0.5f) / Side) - camera.Centre.Y) / camera.Scale, 1f);
                Vector3 way = Vector3.TransformNormal(ray, back);
                float along = (Floor - camera.Viewer.Z) / way.Z;
                Vector3 surface = camera.Viewer + (way * along);
                if (!(along > 0f) || MathF.Abs(surface.X) > 2800f || MathF.Abs(surface.Y) > 2800f)
                {
                    continue;
                }

                Vector4 said = fog.Program.RunScreen(
                    registers, clock, new ScreenPixel(camera.Viewer, surface, new Vector2(px + 0.5f, py + 0.5f), fog.Colour, fog.Shape, pose.Focus, sun, fog.Environment));
                float cover = Math.Clamp(said.W, 0f, 1f);
                int at = ((py * Side) + px) * 4;
                for (var part = 0; part < 3; part++)
                {
                    float shown = Srgb(said[part] * exposure);
                    float expected = (shown * cover) + (bare[at + part] / 255f * (1f - cover));
                    Assert.True(
                        MathF.Abs((fogged[at + part] / 255f) - expected) <= 4f / 255f,
                        $"pixel ({px}, {py}), channel {part}: the card {fogged[at + part]}, expected {expected * 255f:0.0} (fog {said}, under it {bare[at + part]})");
                }

                compared++;
                covered += cover > 0.02f ? 1 : 0;
            }
        }

        // NOT A COMPARISON OF NOTHINGS: most of the floor's pixels look through the fog.
        Assert.True(compared > 60, $"{compared} pixels compared");
        Assert.True(covered > compared / 3, $"{covered} of {compared} covered");
    }

    /// <summary>A picture drawn by the card and read back, its shaders waited for as the pane waits.</summary>
    private static byte[] Drawn(ModelGpu gpu, ModelTarget target, in ModelScene scene)
    {
        var clock = Stopwatch.StartNew();
        string why;
        while (!gpu.Can(scene, out why))
        {
            Assert.True(why.Contains("compiling", StringComparison.Ordinal) && clock.Elapsed < Patience, why);
            Thread.Sleep(10);
        }

        Assert.True(gpu.Draw(target, scene, out why), why);
        return gpu.Read(target);
    }

    /// <summary>A floor 5,600 across at <see cref="Floor"/>, facing up (minus z), in squares of 700.</summary>
    private static SkinnedMesh Sheet()
    {
        const int cells = 8;
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var spots = new List<Vector2>();
        var indices = new List<int>();
        for (var j = 0; j <= cells; j++)
        {
            for (var i = 0; i <= cells; i++)
            {
                positions.Add(new Vector3(-2800f + (5600f * i / cells), -2800f + (5600f * j / cells), Floor));
                normals.Add(new Vector3(0f, 0f, -1f));
                spots.Add(new Vector2((float)i / cells, (float)j / cells));
            }
        }

        for (var j = 0; j < cells; j++)
        {
            for (var i = 0; i < cells; i++)
            {
                int a = (j * (cells + 1)) + i, b = a + cells + 1;
                indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
            }
        }

        return SkinnedMesh.Of(
            [.. positions], [.. normals], [.. indices], new Vector3(-2800f, -2800f, Floor - 1f), new Vector3(2800f, 2800f, Floor + 1f),
            [.. spots], [new MeshShape("floor", 0, indices.Count)]);
    }

    /// <summary>The Red Vale's fog compiled from its fixtures, with two smooth textures of its own.</summary>
    private static ShadeProgram Bound()
    {
        string folder = Fixtures();
        IReadOnlyList<ShaderInstance> instances = ShaderGraph.Instances(File.ReadAllBytes(Path.Combine(folder, "Red_Vale_ao_pass0.mat")));
        ShadeCompile compiled = ShadeProgram.CompileScreen(
            [.. instances.Select(one => (one, ShaderGraph.Read(File.ReadAllBytes(Path.Combine(folder, Path.GetFileName(one.Parent))))))]);
        Assert.True(compiled.Program is not null, string.Join(" | ", compiled.Skipped));
        Mipmaps flows = Texture(32, (x, y) => (128 + (100 * MathF.Sin(x * 0.39f)), 128 + (90 * MathF.Cos(y * 0.27f)), 40, 255));
        Mipmaps fogs = Texture(64, (x, y) => (60, 90, 128 + (110 * MathF.Sin((x * 0.2f) + (y * 0.13f)) * MathF.Cos(y * 0.11f)), 200));
        return compiled.Program.With([.. compiled.Program.Textures.Select(one => one.Path.Contains("flow_muddle", StringComparison.Ordinal) ? flows : fogs)]);
    }

    private static Mipmaps Texture(int side, Func<int, int, (float R, float G, float B, float A)> texel)
    {
        var rgba = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                (float r, float g, float b, float a) = texel(x, y);
                int at = ((y * side) + x) * 4;
                rgba[at] = (byte)Math.Clamp(r, 0f, 255f);
                rgba[at + 1] = (byte)Math.Clamp(g, 0f, 255f);
                rgba[at + 2] = (byte)Math.Clamp(b, 0f, 255f);
                rgba[at + 3] = (byte)Math.Clamp(a, 0f, 255f);
            }
        }

        return Mipmaps.Of(new GamePicture(side, side, rgba))!;
    }

    /// <summary>Linear light into sRGB, clamped - the card's table's curve.</summary>
    private static float Srgb(float linear)
    {
        float x = Math.Clamp(linear, 0f, 1f);
        return x <= 0.0031308f ? x * 12.92f : (1.055f * MathF.Pow(x, 1f / 2.4f)) - 0.055f;
    }

    private static string Fixtures()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "tests", "fixtures", "fog");
    }
}
