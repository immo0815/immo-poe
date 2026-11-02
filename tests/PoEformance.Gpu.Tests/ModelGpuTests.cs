using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Gpu.Tests;

/// <summary>A fact that only runs where Direct3D 11 is.</summary>
public sealed class GpuFactAttribute : FactAttribute
{
    public GpuFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Direct3D 11 is Windows'";
        }
    }
}

/// <summary>
/// The graphics card's picture of a model held against MeshPicture's, pixel for pixel.
/// </summary>
/// <remarks>
/// THE PROCESSOR'S PICTURE IS THE REFERENCE - it is the one every other test in the project measures -
/// so what is asked here is only whether the card draws the same one. Not to the byte: the card fills a
/// pixel on an edge two triangles share once where the processor fills it for both, and filters a
/// texture with its own fixed-point weights. So each comparison allows a small share of pixels to
/// differ by more than a few levels, and says which and by how much when it fails.
/// </remarks>
public class ModelGpuTests
{
    internal const int Side = 192;

    /// <summary>How far apart a channel may be and still count as the same, in levels of 255.</summary>
    private const int Levels = 4;

    [GpuFact]
    public void EVERYShaderCompilesOnTheCard()
    {
        using ModelGpu? gpu = ModelGpu.Warp(out string why);
        Assert.True(gpu is not null, why);
    }

    [GpuFact]
    public void ANINKBoxTurnedAndTiltedIsTheProcessorsBox()
        => Same(new ModelScene(Box(), Turn: 0.6f, Tilt: 0.4f));

    [GpuFact]
    public void ATEXTUREDQuadIsReadAsTheProcessorReadsItMagnifiedAndShrunk()
    {
        SkinnedMesh quad = Layers(1);
        Same(new ModelScene(quad, Turn: 0.3f, Tilt: 0.2f, Skin: Checker(32, 8)));
        Same(new ModelScene(quad, Turn: 0.3f, Tilt: 0.2f, Skin: Checker(1024, 16)));
    }

    [GpuFact]
    public void CUTOUTMixedAndAddedShapesLieOverTheSolidOneAsTheProcessorLaysThem()
    {
        SkinnedMesh layers = Layers(4);
        Mipmaps solid = Plain(new Vector4(0.6f, 0.3f, 0.2f, 1f));
        Mipmaps holed = Checker(64, 8, alpha: true);
        Mipmaps half = Plain(new Vector4(0.1f, 0.5f, 0.9f, 0.5f));
        Mipmaps glow = Plain(new Vector4(0.2f, 0.4f, 0.1f, 1f));
        Same(new ModelScene(
            layers,
            Turn: 0.2f,
            Tilt: 0.15f,
            Skins: [solid, holed, half, glow],
            Blends: [MaterialBlend.Opaque, MaterialBlend.Cutout, MaterialBlend.Alpha, MaterialBlend.Additive]));
    }

    [GpuFact]
    public void AMIXEDShapeOverNothingIsHalfCoveredAndAShadowOnlyOneIsNotDrawn()
    {
        SkinnedMesh layers = Layers(2);
        Same(new ModelScene(
            layers,
            Skins: [Plain(new Vector4(0.9f, 0.2f, 0.2f, 0.5f)), Plain(Vector4.One)],
            Blends: [MaterialBlend.Alpha, MaterialBlend.ShadowOnly]));
    }

    [GpuFact]
    public void APOSEIsDrawnWhereItStandsInTheBoxOfTheStillMesh()
    {
        SkinnedMesh box = Box();
        Vector3[] moved = [.. box.Positions.Select(one => one + new Vector3(0.4f, 0f, -0.2f))];
        GamePicture expected = MeshPicture.Of(box, new MeshPicture.Canvas(Side), moved, box.Normals, 0.6f, 0.4f);
        Compared(expected.Rgba, Drawn(new ModelScene(box, Turn: 0.6f, Tilt: 0.4f, Positions: moved, Normals: box.Normals)));
    }

    /// <summary>A swaying shape is drawn where its group's matrix puts it - the vertex shader's sway, held against the processor's moved vertices.</summary>
    [GpuFact]
    public void ASWAYEDShapeIsDrawnWhereItsMatrixPutsIt()
    {
        SkinnedMesh box = Box();
        Matrix4x4 sway = Matrix4x4.CreateRotationX(0.15f) * Matrix4x4.CreateTranslation(0.3f, 0f, -0.2f);
        Vector3[] moved = [.. box.Positions.Select(one => Vector3.Transform(one, sway))];
        Vector3[] turned = [.. box.Normals.Select(one => Vector3.TransformNormal(one, sway))];
        GamePicture expected = MeshPicture.Of(box, new MeshPicture.Canvas(Side), moved, turned, 0.6f, 0.4f);
        Compared(expected.Rgba, Drawn(new ModelScene(
            box, Turn: 0.6f, Tilt: 0.4f, Sways: [.. Enumerable.Repeat(0, box.Shapes.Count)], Swayed: [sway])));
    }

    /// <summary>
    /// A swaying caster casts where it sways, cast again every frame over the still floor's kept shadow - the stage's corners holding the
    /// shadow's frame, as a ship sways a few units inside its room's.
    /// </summary>
    [GpuFact]
    public void ASWAYINGCasterCastsWhereItSwaysOverTheKeptFloor()
    {
        var light = new SceneLight([], SceneLight.PointShape.Zero, null)
        {
            SunColour = new Vector3(1.6f, 1.4f, 1.2f),
            SunDirection = Vector3.Normalize(new Vector3(1.1f, -1f, 0.6f)),
            SunShadows = true,
        };
        SkinnedMesh stage = Posable(out int first);
        IReadOnlyList<Mipmaps?> skins = [Plain(new Vector4(0.7f, 0.6f, 0.4f, 1f)), Checker(64, 8, alpha: true)];
        IReadOnlyList<MaterialBlend> blends = [MaterialBlend.Opaque, MaterialBlend.Cutout];
        int[] sways = [-1, 0];
        using ModelGpu? gpu = ModelGpu.Warp(out string why);
        Assert.True(gpu is not null, why);
        using ModelTarget target = gpu.Target(Side);
        foreach (Vector3 shift in (Vector3[])[Vector3.Zero, new(0.5f, 0.2f, -0.4f), new(-0.3f, 0.1f, 0.2f)])
        {
            Matrix4x4 sway = Matrix4x4.CreateTranslation(shift);
            Vector3[] moved = [.. stage.Positions.Select((one, at) => at >= first ? Vector3.Transform(one, sway) : one)];
            var scene = new ModelScene(stage, Turn: 0.3f, Tilt: 0.2f, Skins: skins, Blends: blends, Light: light, Sways: sways, Swayed: [sway]);
            GamePicture expected = MeshPicture.Of(
                stage, new MeshPicture.Canvas(Side) { Light = light }, moved, stage.Normals, 0.3f, 0.2f, default, null, 1f, default, skins, blends);
            Assert.True(gpu.Can(scene, out why), why);
            Assert.True(gpu.Draw(target, scene, out why), why);
            Compared(expected.Rgba, gpu.Read(target));
        }

        // CAST OVER THE KEPT FLOOR on every frame: a sway moves every frame.
        Assert.Equal(3, gpu.ShadowsStaged);
    }

    // ---- the game's light ----

    /// <summary>
    /// The sun's shadow falls where the processor casts it - a cut-out caster's through its holes - and a map kept for the next frame draws the same.
    /// </summary>
    [GpuFact]
    public void THESUNSShadowFallsWhereTheProcessorCastsItThroughACutoutsHoles()
    {
        var light = new SceneLight([], SceneLight.PointShape.Zero, null)
        {
            SunColour = new Vector3(1.6f, 1.4f, 1.2f),
            // SLANTED, so the shadow falls beside the caster rather than behind it from the camera.
            SunDirection = Vector3.Normalize(new Vector3(1.1f, -1f, 0.6f)),
            SunShadows = true,
        };
        Same(
            new ModelScene(
                Stage(),
                Turn: 0.3f,
                Tilt: 0.2f,
                Skins: [Plain(new Vector4(0.7f, 0.6f, 0.4f, 1f)), Checker(64, 8, alpha: true)],
                Blends: [MaterialBlend.Opaque, MaterialBlend.Cutout],
                Light: light),
            draws: 2);
    }

    /// <summary>Every view the light panel offers - the lit picture, the engine's debug channels, the lighting alone - draws the processor's picture.</summary>
    [GpuFact]
    public void EVERYViewIsTheProcessorsPicture()
    {
        foreach (PictureView view in Enum.GetValues<PictureView>())
        {
            var light = new SceneLight([], SceneLight.PointShape.Zero, null)
            {
                SunColour = new Vector3(1.6f, 1.4f, 1.2f),
                SunDirection = Vector3.Normalize(new Vector3(1.1f, -1f, 0.6f)),
                Exposure = 1.5f,
                View = view,
            };
            Same(
                new ModelScene(
                    Stage(),
                    Turn: 0.3f,
                    Tilt: 0.2f,
                    Skins: [Plain(new Vector4(0.7f, 0.6f, 0.4f, 1f)), Checker(64, 8, alpha: true)],
                    Blends: [MaterialBlend.Opaque, MaterialBlend.Cutout],
                    Light: light),
                draws: 2);
        }
    }

    /// <summary>
    /// A monster posed on a stage is sent alone and cast over the stage's kept shadow - and the picture is the processor's of the whole mesh, on the frame a pose arrives, on the frames it holds, and for new arrays.
    /// </summary>
    [GpuFact]
    public void AMONSTERPosedOnAStageIsSentAndCastAloneAndDrawnAsTheWholeMesh()
    {
        var light = new SceneLight([], SceneLight.PointShape.Zero, null)
        {
            SunColour = new Vector3(1.6f, 1.4f, 1.2f),
            SunDirection = Vector3.Normalize(new Vector3(1.1f, -1f, 0.6f)),
            SunShadows = true,
        };
        SkinnedMesh stage = Posable(out int first);
        IReadOnlyList<Mipmaps?> skins = [Plain(new Vector4(0.7f, 0.6f, 0.4f, 1f)), Checker(64, 8, alpha: true)];
        IReadOnlyList<MaterialBlend> blends = [MaterialBlend.Opaque, MaterialBlend.Cutout];
        using ModelGpu? gpu = ModelGpu.Warp(out string why);
        Assert.True(gpu is not null, why);
        using ModelTarget target = gpu.Target(Side);
        Vector3[] posed = [.. stage.Positions];
        Vector3[] normals = [.. stage.Normals];

        // A POSE, HELD, ANOTHER, AND THE LAST AGAIN IN NEW ARRAYS - the four ways a pose reaches the card.
        (Vector3 Shift, long Key, bool Fresh)[] steps =
        [
            (Vector3.Zero, 1, false), (Vector3.Zero, 1, false), (new Vector3(0.5f, 0.2f, -0.4f), 2, false), (new Vector3(0.5f, 0.2f, -0.4f), 3, true),
        ];
        foreach ((Vector3 shift, long key, bool fresh) in steps)
        {
            if (fresh)
            {
                posed = [.. posed];
                normals = [.. normals];
            }

            for (int at = first; at < posed.Length; at++)
            {
                posed[at] = stage.Positions[at] + shift;
            }

            var scene = new ModelScene(
                stage, Turn: 0.3f, Tilt: 0.2f, Skins: skins, Blends: blends, Positions: posed, Normals: normals, Light: light,
                PoseFrom: first, PoseKey: key);
            GamePicture expected = MeshPicture.Of(
                stage, new MeshPicture.Canvas(Side) { Light = light }, posed, normals, 0.3f, 0.2f, default, null, 1f, default, skins, blends);
            Assert.True(gpu.Can(scene, out why), why);
            Assert.True(gpu.Draw(target, scene, out why), why);
            Compared(expected.Rgba, gpu.Read(target));
        }

        // CAST OVER THE KEPT STAGE on the three frames that drew a map: the first pose's, the second's and the new arrays'.
        Assert.Equal(3, gpu.ShadowsStaged);
    }

    /// <summary>A shadow-only shape is not drawn but casts, on the coarse map the moving sun draws.</summary>
    [GpuFact]
    public void ASHADOWOnlyShapeCastsOnTheCoarseMap()
    {
        var light = new SceneLight([], SceneLight.PointShape.Zero, null)
        {
            SunColour = new Vector3(2f, 2f, 2f),
            SunDirection = Vector3.Normalize(new Vector3(-0.4f, -1f, 0.5f)),
            SunShadows = true,
            ShadowSide = ShadowMap.Coarse,
            FlatAmbient = 0.08f,
        };
        Same(
            new ModelScene(
                Stage(),
                Turn: -0.2f,
                Tilt: 0.25f,
                Skins: [Plain(new Vector4(0.5f, 0.6f, 0.7f, 1f)), Plain(Vector4.One)],
                Blends: [MaterialBlend.Opaque, MaterialBlend.ShadowOnly],
                Light: light),
            draws: 2);
    }

    /// <summary>
    /// A shape hidden whole (MaterialBlend.Hidden, the pane's roof fade) is neither drawn nor cast on the card, as on the processor - the
    /// floor lit where the caster's shadow fell, on the frame that draws the map and the one that keeps it.
    /// </summary>
    [GpuFact]
    public void AHIDDENShapeIsNeitherDrawnNorCast()
    {
        var light = new SceneLight([], SceneLight.PointShape.Zero, null)
        {
            SunColour = new Vector3(2f, 2f, 2f),
            SunDirection = Vector3.Normalize(new Vector3(-0.4f, -1f, 0.5f)),
            SunShadows = true,
            FlatAmbient = 0.08f,
        };
        var hidden = new ModelScene(
            Stage(),
            Turn: -0.2f,
            Tilt: 0.25f,
            Skins: [Plain(new Vector4(0.5f, 0.6f, 0.7f, 1f)), Plain(Vector4.One)],
            Blends: [MaterialBlend.Opaque, MaterialBlend.Hidden],
            Light: light);
        Same(hidden, draws: 2);

        // AS THE FLOOR ALONE: the caster gone from the mesh altogether draws the same picture - both through the whole stage's camera,
        // since a mesh kept to fewer triangles frames its own smaller box.
        CameraPose pose = CameraPose.Fitted(Stage(), -0.2f, 0.25f, parallel: true);
        byte[] alone = Drawn(hidden with { Mesh = Stage().Keeping([true, true, false, false]), Blends = [MaterialBlend.Opaque, MaterialBlend.Opaque], Pose = pose });
        Compared(alone, Drawn(hidden with { Pose = pose }));
    }

    /// <summary>Point lights through their grid, the player's among them, with the exposure on top.</summary>
    [GpuFact]
    public void POINTLightsAndTheExposureLightTheBoxAsTheProcessorLightsIt()
    {
        PointLight[] points =
        [
            new(new Vector3(2.5f, 1.5f, 0f), new Vector3(3f, 1f, 0.5f), 2f, 0.5f, "red"),
            new(new Vector3(-2f, 2f, -1.5f), new Vector3(0.5f, 1f, 3f), 3f, 0.2f, "blue"),
            new(new Vector3(0f, 3f, 2.5f), new Vector3(1f, 1f, 1f), 1.5f, 0.9f, "white"),
        ];
        var light = new SceneLight(points, SceneLight.PointShape.Penumbra, new SceneLight.PlayerLamp(new Vector3(0f, 4f, 0f), new Vector3(0.6f, 0.5f, 0.3f), 3f))
        {
            FlatAmbient = 0.05f,
            Exposure = 1.4f,
        };
        Same(new ModelScene(Box(), Turn: 0.6f, Tilt: 0.4f, Light: light));
        Same(new ModelScene(Ball(), Turn: 0.6f, Tilt: 0.4f, Light: light));
    }

    /// <summary>
    /// The lights a model carries on its bones (SceneLight.Carrying, v0.1.403) light the box on the card as the processor lights it - held in
    /// the card's constants beside the room's through their grid, and alone; the room's uploaded once for every pose's copy.
    /// </summary>
    [GpuFact]
    public void CARRIEDLightsLightTheBoxAsTheProcessorLightsThem()
    {
        PointLight[] room = [new(new Vector3(-2f, 2f, -1.5f), new Vector3(0.5f, 1f, 3f), 3f, 0.2f, "blue")];
        PointLight[] carried =
        [
            new(new Vector3(2.5f, 1.5f, 0f), new Vector3(2.4671f, 0.493089f, 0.0398032f), 2f, 0.5f, "lamp"),
            new(new Vector3(0f, 3f, 2.5f), new Vector3(1f, 1f, 1f), 1.5f, 0.9f, "white"),
        ];
        var light = new SceneLight(room, SceneLight.PointShape.Penumbra, null) { FlatAmbient = 0.05f, Exposure = 1.4f };
        Same(new ModelScene(Box(), Turn: 0.6f, Tilt: 0.4f, Light: light.Carrying(carried)));
        Same(new ModelScene(Ball(), Turn: 0.6f, Tilt: 0.4f, Light: new SceneLight([], SceneLight.PointShape.Penumbra, null) { FlatAmbient = 0.05f }.Carrying(carried)));
    }

    /// <summary>
    /// In perspective (CameraPose, v0.1.324) the card's clip and reversed depth put every vertex, texel and depth where the processor's
    /// rays do - a box, a textured quad at a slant, the sun's light and shadow from each pixel's own way to the eye, and point lights.
    /// </summary>
    [GpuFact]
    public void INPERSPECTIVEEveryPictureIsTheProcessors()
    {
        Same(new ModelScene(Box(), Pose: CameraPose.Fitted(Box(), 0.6f, 0.4f, parallel: false)));
        SkinnedMesh quad = Layers(1);
        // MAGNIFIED EVERYWHERE, so the card's level per block of pixels and the processor's per triangle are both the finest - in
        // perspective they part where a texture is shrunk, the card's following the depth across a triangle.
        Same(new ModelScene(quad, Skin: Checker(32, 8), Pose: new CameraPose(Vector3.Zero, 0.3f, 1.1f, 2.4f)));
        var light = new SceneLight([], SceneLight.PointShape.Zero, null)
        {
            SunColour = new Vector3(1.6f, 1.4f, 1.2f),
            SunDirection = Vector3.Normalize(new Vector3(1.1f, -1f, 0.6f)),
            Exposure = 1.5f,
        };
        Same(
            new ModelScene(
                Stage(),
                Skins: [Plain(new Vector4(0.7f, 0.6f, 0.4f, 1f)), Checker(64, 8, alpha: true)],
                Blends: [MaterialBlend.Opaque, MaterialBlend.Cutout],
                Light: light,
                Pose: CameraPose.Fitted(Stage(), 0.3f, 0.2f, parallel: false)),
            draws: 2);
        PointLight[] points =
        [
            new(new Vector3(2.5f, 1.5f, 0f), new Vector3(3f, 1f, 0.5f), 2f, 0.5f, "red"),
            new(new Vector3(-2f, 2f, -1.5f), new Vector3(0.5f, 1f, 3f), 3f, 0.2f, "blue"),
        ];
        Same(new ModelScene(Ball(), Light: new SceneLight(points, SceneLight.PointShape.Penumbra, null) { FlatAmbient = 0.05f }, Pose: CameraPose.Fitted(Ball(), 0.6f, 0.4f, parallel: false)));
    }

    /// <summary>
    /// A quad running under the eye crosses the near plane: the card's hardware cuts it where the processor's rays stop, and the rest of
    /// it is drawn alike - looked at nearly along it from a third of a unit over its middle.
    /// </summary>
    [GpuFact]
    public void AQUADThroughTheNearPlaneIsCutAsTheProcessorCutsIt()
    {
        var pose = new CameraPose(Vector3.Zero, 0f, 1.3f, 0.5f);
        Assert.True(pose.Eye.Z > -1f && pose.Eye.Z < 0f, $"the eye at {pose.Eye} should stand within the quad's reach");
        Same(new ModelScene(Layers(1), Skin: Checker(32, 8), Pose: pose));
    }

    /// <summary>The diffuse cube turned by the environment, brightened by the sun, shared with GI - and the colour grade after the exposure.</summary>
    [GpuFact]
    public void THECUBEAndTheGradeAreReadAsTheProcessorReadsThem()
    {
        CubeMap? cube = CubeMap.Read(Cube(4), out string why);
        Assert.True(cube is not null, why);
        ColourGrade? grade = ColourGrade.Read(Volume(16, (r, g, b) => ((r * 0.9f) + 0.05f, g, b * 0.8f)), out why);
        Assert.True(grade is not null, why);
        var light = new SceneLight([], SceneLight.PointShape.Zero, null)
        {
            SunColour = new Vector3(0.8f, 0.8f, 0.8f),
            SunDirection = Vector3.Normalize(new Vector3(0.2f, -0.5f, 0.8f)),
            Ambient = SceneAmbient.Cube,
            Cube = cube,
            CubeTurn = SceneLight.CubeTurnFrom(0.4f, 0f, SceneLight.CubeReading.Round),
            CubeBrightness = 1.3f,
            DirectLightEnvRatio = 0.5f,
            GiEnvOcclusion = 0.25f,
            Exposure = 1.2f,
            Grade = grade,
        };
        Same(new ModelScene(Ball(), Turn: 0.6f, Tilt: 0.4f, Light: light));
        Same(new ModelScene(Ball(), Turn: 2.4f, Tilt: -0.5f, Light: light));
    }

    /// <summary>
    /// The scene drawn by both, compared - after each of the card's draws, so the frame that draws the shadow map and the frames that keep it are each held to the processor's.
    /// </summary>
    /// <remarks>
    /// EVERY DRAW, NOT THE LAST: a shadow map read as nothing on the frame it was drawn passed here
    /// while only the second draw was compared - that one read the kept map.
    /// </remarks>
    private static void Same(ModelScene scene, int draws = 1)
    {
        var canvas = new MeshPicture.Canvas(Side) { Light = scene.Light, Pose = scene.Pose };
        GamePicture expected = MeshPicture.Of(
            scene.Mesh, canvas, scene.Turn, scene.Tilt, scene.Ink, scene.Skin, scene.Zoom, scene.Pan, scene.Skins, scene.Blends);
        using ModelGpu? gpu = ModelGpu.Warp(out string why);
        Assert.True(gpu is not null, why);
        Assert.True(gpu.Can(scene, out why), why);
        using ModelTarget target = gpu.Target(Side);
        for (var draw = 0; draw < draws; draw++)
        {
            Assert.True(gpu.Draw(target, scene, out why), why);
            Compared(expected.Rgba, gpu.Read(target));
        }
    }

    private static byte[] Drawn(ModelScene scene)
    {
        using ModelGpu? gpu = ModelGpu.Warp(out string why);
        Assert.True(gpu is not null, why);
        Assert.True(gpu.Can(scene, out why), why);
        using ModelTarget target = gpu.Target(Side);
        Assert.True(gpu.Draw(target, scene, out why), why);
        return gpu.Read(target);
    }

    /// <summary>
    /// Fewer than one pixel in fifty may be covered by one picture and not the other, and fewer than one in fifty of those both cover may differ by more than <see cref="Levels"/>.
    /// </summary>
    internal static void Compared(byte[] cpu, byte[] gpu)
    {
        string? mismatch = Mismatch(cpu, gpu);
        Assert.True(mismatch is null, mismatch);
    }

    /// <summary>What <see cref="Compared"/> asks, said rather than asserted - null where the two pictures agree.</summary>
    internal static string? Mismatch(byte[] cpu, byte[] gpu)
    {
        if (cpu.Length != gpu.Length)
        {
            return $"the pictures are {cpu.Length} and {gpu.Length} bytes";
        }

        int covered = 0, coverage = 0, colour = 0;
        var worst = new StringBuilder();
        for (var at = 0; at < cpu.Length; at += 4)
        {
            bool inCpu = cpu[at + 3] > 0, inGpu = gpu[at + 3] > 0;
            covered += inCpu || inGpu ? 1 : 0;
            if (inCpu != inGpu)
            {
                coverage++;
                continue;
            }

            if (!inCpu)
            {
                continue;
            }

            int most = 0;
            for (var channel = 0; channel < 4; channel++)
            {
                most = Math.Max(most, Math.Abs(cpu[at + channel] - gpu[at + channel]));
            }

            if (most > Levels)
            {
                colour++;
                if (colour <= 8)
                {
                    int pixel = at / 4;
                    worst.Append(CultureInfo.InvariantCulture,
                        $" ({pixel % Side},{pixel / Side}) cpu {cpu[at]},{cpu[at + 1]},{cpu[at + 2]},{cpu[at + 3]} gpu {gpu[at]},{gpu[at + 1]},{gpu[at + 2]},{gpu[at + 3]};");
                }
            }
        }

        return covered <= Side * Side / 20 ? $"too little drawn to compare: {covered} pixels"
            : coverage * 50 >= covered ? $"{coverage} of {covered} pixels covered by one picture only"
            : colour * 50 >= covered ? $"{colour} of {covered} pixels differ by more than {Levels}:{worst}"
            : null;
    }

    /// <summary>A box two by two by two with a normal per face, twelve triangles - the shape every turn shows three faces of.</summary>
    private static SkinnedMesh Box()
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        Vector3[] faces = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        foreach (Vector3 face in faces)
        {
            Vector3 across = MathF.Abs(face.X) > 0.5f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 down = Vector3.Cross(face, across);
            int at = positions.Count;
            positions.AddRange([face - across - down, face + across - down, face + across + down, face - across + down]);
            normals.AddRange([face, face, face, face]);
            indices.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
        }

        return SkinnedMesh.Of([.. positions], [.. normals], [.. indices], new Vector3(-1f), new Vector3(1f));
    }

    /// <summary>
    /// Quads facing the camera one behind another, a shape each, the first furthest - the camera looks along minus y, so the nearer is the larger y.
    /// </summary>
    private static SkinnedMesh Layers(int count)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var spots = new List<Vector2>();
        var indices = new List<int>();
        var shapes = new List<MeshShape>();
        for (var layer = 0; layer < count; layer++)
        {
            float y = layer * 0.5f, shrink = 1f - (layer * 0.15f);
            int at = positions.Count;
            positions.AddRange(
            [
                new Vector3(-shrink, y, -shrink), new Vector3(shrink, y, -shrink), new Vector3(shrink, y, shrink), new Vector3(-shrink, y, shrink),
            ]);
            normals.AddRange([Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY]);
            spots.AddRange([new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)]);
            shapes.Add(new MeshShape($"layer {layer}", indices.Count, 6));
            indices.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
        }

        return SkinnedMesh.Of(
            [.. positions], [.. normals], [.. indices], new Vector3(-1f, 0f, -1f), new Vector3(1f, Math.Max(0.5f, (count - 1) * 0.5f), 1f),
            [.. spots], shapes);
    }

    /// <summary>A ball of radius one, its normals its places - so a light is read from every direction, the cube's seams and the grade's whole table among them.</summary>
    private static SkinnedMesh Ball()
    {
        const int rings = 24, segments = 48;
        var positions = new List<Vector3>();
        var indices = new List<int>();
        for (var ring = 0; ring <= rings; ring++)
        {
            float polar = MathF.PI * ring / rings;
            for (var segment = 0; segment <= segments; segment++)
            {
                float round = 2f * MathF.PI * segment / segments;
                positions.Add(new Vector3(MathF.Sin(polar) * MathF.Cos(round), MathF.Sin(polar) * MathF.Sin(round), MathF.Cos(polar)));
            }
        }

        for (var ring = 0; ring < rings; ring++)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                int a = (ring * (segments + 1)) + segment, b = a + segments + 1;
                indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
            }
        }

        Vector3[] places = [.. positions];
        return SkinnedMesh.Of(places, places, [.. indices], new Vector3(-1f), new Vector3(1f));
    }

    /// <summary>
    /// A floor four square facing the camera and a caster above it, nearer the camera, a shape each - floor first.
    /// </summary>
    private static SkinnedMesh Stage()
    {
        Vector3[] positions =
        [
            new(-2f, 0f, -2f), new(2f, 0f, -2f), new(2f, 0f, 2f), new(-2f, 0f, 2f),
            new(-0.6f, 1f, -0.6f), new(0.6f, 1f, -0.6f), new(0.6f, 1f, 0.6f), new(-0.6f, 1f, 0.6f),
        ];
        Vector3[] normals = [.. Enumerable.Repeat(Vector3.UnitY, 8)];
        Vector2[] spots = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f), new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7];
        return SkinnedMesh.Of(
            positions, normals, indices, new Vector3(-2f, 0f, -2f), new Vector3(2f, 1f, 2f), spots,
            [new MeshShape("floor", 0, 6), new MeshShape("caster", 6, 6)]);
    }

    /// <summary>
    /// <see cref="Stage"/> with a monster joined last, as StagedModels joins one: the floor's own vertices, eight corners round all the monster may reach - so he stays inside the stage's shadow frame - and the caster, his, from <paramref name="first"/> on.
    /// </summary>
    private static SkinnedMesh Posable(out int first)
    {
        first = 12;
        Vector3[] positions =
        [
            new(-2f, 0f, -2f), new(2f, 0f, -2f), new(2f, 0f, 2f), new(-2f, 0f, 2f),
            new(-2.5f, -0.5f, -2.5f), new(2.5f, -0.5f, -2.5f), new(2.5f, -0.5f, 2.5f), new(-2.5f, -0.5f, 2.5f),
            new(-2.5f, 1.8f, -2.5f), new(2.5f, 1.8f, -2.5f), new(2.5f, 1.8f, 2.5f), new(-2.5f, 1.8f, 2.5f),
            new(-0.6f, 1f, -0.6f), new(0.6f, 1f, -0.6f), new(0.6f, 1f, 0.6f), new(-0.6f, 1f, 0.6f),
        ];
        Vector3[] normals = [.. Enumerable.Repeat(Vector3.UnitY, positions.Length)];
        Vector2[] spots = [.. Enumerable.Range(0, positions.Length).Select(at => (at % 4) switch { 0 => new Vector2(0f, 0f), 1 => new Vector2(1f, 0f), 2 => new Vector2(1f, 1f), _ => new Vector2(0f, 1f) })];
        int[] indices = [0, 1, 2, 0, 2, 3, 12, 13, 14, 12, 14, 15];
        return SkinnedMesh.Of(
            positions, normals, indices, new Vector3(-2f, 0f, -2f), new Vector3(2f, 1f, 2f), spots,
            [new MeshShape("floor", 0, 6), new MeshShape("monster", 6, 6)]);
    }

    /// <summary>A 32-bit colour cube by the old header's masks, each face its own ramp, so a wrong face or a wrong texel shows.</summary>
    private static byte[] Cube(int size)
    {
        var dds = new byte[128 + (6 * size * size * 4)];
        "DDS "u8.CopyTo(dds);
        BitConverter.TryWriteBytes(dds.AsSpan(4), 124);
        BitConverter.TryWriteBytes(dds.AsSpan(12), size);
        BitConverter.TryWriteBytes(dds.AsSpan(16), size);
        BitConverter.TryWriteBytes(dds.AsSpan(80), 0x41u);
        BitConverter.TryWriteBytes(dds.AsSpan(88), 32);
        BitConverter.TryWriteBytes(dds.AsSpan(92), 0x00FF0000u);
        BitConverter.TryWriteBytes(dds.AsSpan(96), 0x0000FF00u);
        BitConverter.TryWriteBytes(dds.AsSpan(100), 0x000000FFu);
        BitConverter.TryWriteBytes(dds.AsSpan(104), 0xFF000000u);
        BitConverter.TryWriteBytes(dds.AsSpan(112), 0x200u | (0x3Fu << 10));
        for (var face = 0; face < 6; face++)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    int at = 128 + (((face * size * size) + (y * size) + x) * 4);
                    dds[at] = (byte)(40 + (face * 30));
                    dds[at + 1] = (byte)(255 * y / (size - 1));
                    dds[at + 2] = (byte)(255 * x / (size - 1));
                    dds[at + 3] = 255;
                }
            }
        }

        return dds;
    }

    /// <summary>A DX10 DDS volume of R8G8B8A8, its texels from a function of their centres - LightHuntTests' table.</summary>
    internal static byte[] Volume(int side, Func<float, float, float, (float R, float G, float B)> texel)
    {
        var dds = new byte[148 + (side * side * side * 4)];
        "DDS "u8.CopyTo(dds);
        BitConverter.TryWriteBytes(dds.AsSpan(4), 124);
        BitConverter.TryWriteBytes(dds.AsSpan(8), 0x800000u | 0x1007u);
        BitConverter.TryWriteBytes(dds.AsSpan(12), side);
        BitConverter.TryWriteBytes(dds.AsSpan(16), side);
        BitConverter.TryWriteBytes(dds.AsSpan(24), side);
        BitConverter.TryWriteBytes(dds.AsSpan(28), 1);
        BitConverter.TryWriteBytes(dds.AsSpan(76), 32);
        BitConverter.TryWriteBytes(dds.AsSpan(80), 0x4u);
        "DX10"u8.CopyTo(dds.AsSpan(84));
        BitConverter.TryWriteBytes(dds.AsSpan(112), 0x200000u);
        BitConverter.TryWriteBytes(dds.AsSpan(128), 28);
        BitConverter.TryWriteBytes(dds.AsSpan(132), 4);
        BitConverter.TryWriteBytes(dds.AsSpan(140), 1);
        int at = 148;
        for (var z = 0; z < side; z++)
        {
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    (float r, float g, float b) = texel((x + 0.5f) / side, (y + 0.5f) / side, (z + 0.5f) / side);
                    dds[at++] = (byte)MathF.Round(r * 255f);
                    dds[at++] = (byte)MathF.Round(g * 255f);
                    dds[at++] = (byte)MathF.Round(b * 255f);
                    dds[at++] = 255;
                }
            }
        }

        return dds;
    }

    /// <summary>One colour, alpha and all, a texel four square.</summary>
    internal static Mipmaps Plain(Vector4 colour)
    {
        var rgba = new byte[4 * 4 * 4];
        for (var at = 0; at < rgba.Length; at += 4)
        {
            rgba[at] = (byte)(colour.X * 255f);
            rgba[at + 1] = (byte)(colour.Y * 255f);
            rgba[at + 2] = (byte)(colour.Z * 255f);
            rgba[at + 3] = (byte)(colour.W * 255f);
        }

        return Mipmaps.Of(new GamePicture(4, 4, rgba))!;
    }

    /// <summary>A checker of squares, coloured by place so a texel read from the wrong spot shows - with holes where asked.</summary>
    private static Mipmaps Checker(int side, int square, bool alpha = false)
    {
        var rgba = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                int at = ((y * side) + x) * 4;
                bool dark = ((x / square) + (y / square)) % 2 == 0;
                rgba[at] = (byte)(x * 255 / side);
                rgba[at + 1] = (byte)(y * 255 / side);
                rgba[at + 2] = dark ? (byte)40 : (byte)220;
                rgba[at + 3] = alpha && dark ? (byte)0 : (byte)255;
            }
        }

        return Mipmaps.Of(new GamePicture(side, side, rgba))!;
    }
}
