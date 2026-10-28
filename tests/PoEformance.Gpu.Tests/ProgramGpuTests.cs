using System.Diagnostics;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Gpu.Tests;

/// <summary>
/// Materials' shade programs drawn by the card as their own shaders, held against MeshPicture running the same programs.
/// </summary>
/// <remarks>
/// THE GAME'S OWN MATERIALS, every one tests/fixtures/shaders holds whole - not graphs written for the
/// test - so the ops are the ones the game uses, in the orders it uses them: world-projected tiling,
/// vertex paint, dust laid by occlusion, gloss, contact fades, a parallax march. Their textures are
/// smooth ramps, one per path, so a read at a slightly different level - the card's per pixel, the
/// processor's per triangle - reads nearly the same colour, and what is compared is the arithmetic.
///
/// ONE CARD FOR THE CLASS (<see cref="CardFixture"/>), so a program's shaders are compiled once however
/// many pictures and tests it is in; the first picture of each waits for them as the pane would.
/// </remarks>
public class ProgramGpuTests(CardFixture card) : IClassFixture<CardFixture>
{
    private const int Side = ModelGpuTests.Side;

    /// <summary>
    /// The test sheet's half-width in the game's units - a tile is 250 across.
    /// </summary>
    /// <remarks>
    /// AT THE GAME'S SCALE, NOT ONE UNIT, because the materials are authored for it: Dung01c marches
    /// its parallax 21 units deep, which on a sheet two units wide runs the ray through a dozen
    /// repeats of the height texture between two neighbouring pixels. The march is then chaos, and
    /// which of it a picture shows is decided by the level each read takes - the card's per pixel,
    /// the processor's per triangle - so the first run of these tests failed on exactly the two
    /// materials with a march, each picture right by its own rule. At this size a neighbouring pixel
    /// differs by as little as on every other material (measured on the processor: 52 levels a
    /// step at one unit, 6 at a hundred).
    /// </remarks>
    private const float Scale = 100f;

    /// <summary>How long a picture waits for its shaders before the wait is the failure.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    private static readonly Dictionary<string, Mipmaps> Sheets = new(StringComparer.Ordinal);

    [GpuFact]
    public void EVERYFIXTUREMaterialIsDrawnByTheCardAsTheProcessorDrawsItUnderTheLamp()
        => Every(program => new ModelScene(Hill(layered: false), Turn: 0.4f, Tilt: 0.5f, Blends: [MaterialBlend.Opaque], Shades: [program], Time: 1.25f));

    [GpuFact]
    public void EVERYFIXTUREMaterialIsDrawnByTheCardAsTheProcessorDrawsItUnderTheGamesLight()
    {
        PointLight[] points = [new(new Vector3(0.5f, 0.8f, -0.3f) * Scale, new Vector3(2f, 1f, 0.5f), 1.5f * Scale, 0.4f, "warm")];
        var light = new SceneLight(points, SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(1.4f, 1.3f, 1.1f),
            SunDirection = Vector3.Normalize(new Vector3(0.4f, -1f, 0.3f)),
            SunShadows = true,
            ShadowSide = ShadowMap.Coarse,
            FlatAmbient = 0.1f,
            Exposure = 1.2f,
        };
        Every(program => new ModelScene(
            Hill(layered: false), Turn: -0.3f, Tilt: 0.45f, Blends: [MaterialBlend.Opaque], Shades: [program], Light: light, Time: 0.5f));
    }

    /// <summary>A material as a mixed layer over a solid floor: its alpha, its fade by the depth behind, its discards.</summary>
    [GpuFact]
    public void AMIXEDLayerIsLaidOverTheSolidFloorAsTheProcessorLaysIt()
        => Every(
            program => new ModelScene(
                Hill(layered: true),
                Turn: 0.25f,
                Tilt: 0.55f,
                Skins: [Ramp("floor"), null],
                Blends: [MaterialBlend.Opaque, MaterialBlend.Alpha],
                Shades: [null, program],
                Time: 2f),
            program => program.HasAlpha || program.UsesDepth);

    /// <summary>
    /// The water's waves, glinting in the sun, reflecting the ambient and the hill, and lifting the surface, laid over the solid floor as the processor lays them - the engine's octaves, the glints' lobe, the reflection, the screen's ray and PlacedWaves, on the card.
    /// </summary>
    [GpuFact]
    public void THEWATERSWavesAreLitByTheCardAsTheProcessorLightsThem()
    {
        ModelGpu? gpu = card.Gpu;
        Assert.True(gpu is not null, card.Why);
        using ModelTarget target = gpu.Target(Side);
        var light = new SceneLight([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(1.4f, 1.3f, 1.1f),
            SunDirection = Vector3.Normalize(new Vector3(0.4f, -1f, 0.3f)),
            FlatAmbient = 0.3f,
            Exposure = 1.2f,
            WaterGlints = true,
            WaterReflection = true,
            WaterLift = 3f,
            WaterScene = true,
        };
        ShadeProgram waves = ShadeProgram.Water().Glossy!;
        var scene = new ModelScene(
            Hill(layered: true),
            Turn: 0.25f,
            Tilt: 0.55f,
            Skins: [Ramp("floor"), null],
            Blends: [MaterialBlend.Opaque, MaterialBlend.Alpha],
            Shades: [null, waves],
            Light: light,
            Time: 2f,
            Water: WaterLook.EngineDefault with { FlowIntensity = 1.5f });
        Assert.Null(Mismatch(gpu, target, scene));
    }

    /// <summary>
    /// The same in perspective (CameraPose, v0.1.324): every pixel lit from its own way to the eye, the depth behind read back from the
    /// reversed buffer, and the screen's ray marched as the engine marches it over the depths' reciprocals - ScreenReflection.MarchSeen.
    /// </summary>
    [GpuFact]
    public void THEWATERSWavesInPerspectiveAreLitByTheCardAsTheProcessorLightsThem()
    {
        ModelGpu? gpu = card.Gpu;
        Assert.True(gpu is not null, card.Why);
        using ModelTarget target = gpu.Target(Side);
        var light = new SceneLight([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(1.4f, 1.3f, 1.1f),
            SunDirection = Vector3.Normalize(new Vector3(0.4f, -1f, 0.3f)),
            FlatAmbient = 0.3f,
            Exposure = 1.2f,
            WaterGlints = true,
            WaterReflection = true,
            WaterLift = 3f,
            WaterScene = true,
        };
        SkinnedMesh hill = Hill(layered: true);
        var scene = new ModelScene(
            hill,
            Skins: [Ramp("floor"), null],
            Blends: [MaterialBlend.Opaque, MaterialBlend.Alpha],
            Shades: [null, ShadeProgram.Water().Glossy!],
            Light: light,
            Time: 2f,
            Water: WaterLook.EngineDefault with { FlowIntensity = 1.5f },
            Pose: CameraPose.Fitted(hill, 0.25f, 0.55f, parallel: false));
        Assert.Null(Mismatch(gpu, target, scene));
    }

    /// <summary>A material as a mixed layer over a solid floor in perspective: its fade by the depth behind along each pixel's own ray.</summary>
    [GpuFact]
    public void AMIXEDLayerInPerspectiveIsLaidOverTheSolidFloorAsTheProcessorLaysIt()
    {
        SkinnedMesh hill = Hill(layered: true);
        Every(
            program => new ModelScene(
                hill,
                Skins: [Ramp("floor"), null],
                Blends: [MaterialBlend.Opaque, MaterialBlend.Alpha],
                Shades: [null, program],
                Time: 2f,
                Pose: CameraPose.Fitted(hill, 0.25f, 0.55f, parallel: false)),
            program => program.HasAlpha || program.UsesDepth);
    }

    /// <summary>
    /// The ocean's swell over a room's shore - the shore field and the swell's cross-section read on the card as the processor reads them, the ripples thinning at the shoreline.
    /// </summary>
    [GpuFact]
    public void THEOCEANSSwellOverAShoreIsDrawnByTheCardAsByTheProcessor()
    {
        ModelGpu? gpu = card.Gpu;
        Assert.True(gpu is not null, card.Why);
        using ModelTarget target = gpu.Target(Side);
        var light = new SceneLight([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(1.4f, 1.3f, 1.1f),
            SunDirection = Vector3.Normalize(new Vector3(0.4f, -1f, 0.3f)),
            FlatAmbient = 0.3f,
            WaterGlints = true,
            WaterReflection = true,
            WaterLift = 2f,
        };

        var scene = new ModelScene(
            Hill(layered: true),
            Turn: 0.25f,
            Tilt: 0.55f,
            Skins: [Ramp("floor"), null],
            Blends: [MaterialBlend.Opaque, MaterialBlend.Alpha],
            Shades: [null, ShadeProgram.Water().Glossy!],
            Light: light,
            Time: 3.5f,
            Water: WaterLook.EngineDefault with { Shore = BayShore() });
        Assert.Null(Mismatch(gpu, target, scene));
    }

    /// <summary>
    /// The lifted sea drawn as one surface, on the card as on the processor (MeshPicture.Drawing's Surface): two sheets of one water shape
    /// over a floor, the lower first in the mesh, seen at a slant - each pixel the nearer sheet's - and a mixed sheet under both drawn
    /// after them, which tests against the solid floor alone once the card has put its depth back.
    /// </summary>
    [GpuFact]
    public void THELIFTEDSeaIsOneSurfaceOnTheCardAsOnTheProcessor()
    {
        ModelGpu? gpu = card.Gpu;
        Assert.True(gpu is not null, card.Why);
        using ModelTarget target = gpu.Target(Side);
        var light = new SceneLight([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(1.4f, 1.3f, 1.1f),
            SunDirection = Vector3.Normalize(new Vector3(0.3f, 0.2f, 1f)),
            FlatAmbient = 0.3f,
            WaterGlints = true,
            WaterReflection = true,
            WaterLift = 2f,
            WaterScene = true,
        };

        var scene = new ModelScene(
            Stacked(),
            Turn: 0.3f,
            Tilt: 0.6f,
            Skins: [Ramp("floor"), null, Ramp("glass")],
            Blends: [MaterialBlend.Opaque, MaterialBlend.Alpha, MaterialBlend.Alpha],
            Shades: [null, ShadeProgram.Water().Glossy!, null],
            Light: light,
            Time: 2f);
        Assert.Null(Mismatch(gpu, target, scene));
    }

    /// <summary>
    /// The engine's beach waves over a beach rising out of the sea - their sheet over the ground, drawn over the solid part writing its
    /// depth, the foam and the film as the graph mixes them - drawn alike by both, under the picture's lamp and the game's light.
    /// </summary>
    [GpuFact]
    public void THEBEACHWavesOnTheCardAsOnTheProcessor()
    {
        ModelGpu? gpu = card.Gpu;
        Assert.True(gpu is not null, card.Why);
        using ModelTarget target = gpu.Target(Side);
        (SkinnedMesh beach, ShoreField shore) = Beach();
        ShadeProgram program = ShadeProgram.Beach(new ShadeTexture("foam", true), new ShadeTexture("foam bumps", false), new ShadeTexture("ripples", false));
        var light = new SceneLight([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(1.4f, 1.3f, 1.1f),
            SunDirection = Vector3.Normalize(new Vector3(0.3f, 0.2f, 1f)),
            FlatAmbient = 0.3f,
        };

        foreach ((ShadeProgram shade, SceneLight? lit) in new[] { (Bound(program), (SceneLight?)null), (Bound(program.Glossy!), light) })
        {
            var scene = new ModelScene(
                beach,
                Turn: 0.3f,
                Tilt: 0.7f,
                Skins: [Ramp("sand"), null],
                Blends: [MaterialBlend.Opaque, MaterialBlend.Alpha],
                Shades: [null, shade],
                Light: lit,
                Time: 1.7f,
                Water: WaterLook.EngineDefault with { Shore = shore });
            Assert.Null(Mismatch(gpu, target, scene));
        }
    }

    /// <summary>
    /// Two of the fixtures' grounds mixed per pixel by a mask's green (ShadeProgram.Grounds) - the mask read at the mesh's own coordinates,
    /// the grounds at the place's - drawn alike by both, under the picture's lamp and the game's light.
    /// </summary>
    [GpuFact]
    public void TWOGroundsMixedOnTheCardAsOnTheProcessor()
    {
        ModelGpu? gpu = card.Gpu;
        Assert.True(gpu is not null, card.Why);
        using ModelTarget target = gpu.Target(Side);
        ShadeProgram[] grounds =
        [
            .. Programs().Where(one => one.Name.Contains("G_VST_Sand01", StringComparison.Ordinal) || one.Name.Contains("TallDune1c", StringComparison.Ordinal))
                .Where(one => !one.Name.EndsWith("(glossy)", StringComparison.Ordinal))
                .Select(one => Bound(one.Program)),
        ];
        Assert.Equal(2, grounds.Length);
        ShadeProgram? mixed = ShadeProgram.Grounds(grounds[0], grounds[1], new ShadeTexture("mask", false), Ramp("mask"), 1f / 600f);
        Assert.NotNull(mixed);
        var light = new SceneLight([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(1.4f, 1.3f, 1.1f),
            SunDirection = Vector3.Normalize(new Vector3(0.3f, 0.2f, 1f)),
            FlatAmbient = 0.3f,
        };

        // THE BLOCK'S OWN COORDINATES, nought to one across the quad - the mask's - while the grounds take the place's.
        Vector3[] places = [new(-400f, -400f, 0f), new(400f, -400f, 0f), new(400f, 400f, 0f), new(-400f, 400f, 0f)];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        SkinnedMesh quad = SkinnedMesh.Of(
            places, normals, [0, 1, 2, 0, 2, 3], new Vector3(-400f, -400f, 0f), new Vector3(400f, 400f, 0f),
            [.. places.Select(one => new Vector2((one.X + 400f) / 800f, (one.Y + 400f) / 800f))]);
        foreach (SceneLight? lit in new[] { null, light })
        {
            var scene = new ModelScene(quad, Turn: 0.3f, Tilt: 0.9f, Skins: [null], Blends: [MaterialBlend.Opaque], Shades: [mixed], Light: lit);
            Assert.Null(Mismatch(gpu, target, scene));
        }
    }

    /// <summary>
    /// A beach rising a tenth a unit along x out of water standing at nought - its ground, and the beach waves' sheet over it in the shore's
    /// band (ShoreField.Beach) - with its shore. Up is minus z.
    /// </summary>
    private static (SkinnedMesh Mesh, ShoreField Shore) Beach()
    {
        Vector3[] places =
        [
            new(-2000f, -2000f, 0f), new(2000f, -2000f, 0f), new(2000f, 2000f, 0f), new(-2000f, 2000f, 0f),
            new(-3000f, -3000f, 300f), new(3000f, -3000f, -300f), new(3000f, 3000f, -300f), new(-3000f, 3000f, 300f),
        ];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        Vector2[] spots = [.. places.Select(one => new Vector2(one.X / 1500f, one.Y / 1500f))];
        SkinnedMesh both = SkinnedMesh.Of(
            places, normals, [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7], new Vector3(-3000f, -3000f, -300f), new Vector3(3000f, 3000f, 300f),
            spots, [new MeshShape("Water", 0, 6), new MeshShape("Sand", 6, 6)]);
        ShoreField shore = ShoreField.Of(both, shape => shape == 0, shape => shape == 1)!;
        SkinnedMesh sand = SkinnedMesh.Of(
            places[4..], normals[4..], [0, 1, 2, 0, 2, 3], new Vector3(-3000f, -3000f, -300f), new Vector3(3000f, 3000f, 300f), spots[4..]);
        return (SkinnedMesh.Joined([new MeshJoin(sand, null, null, null), new MeshJoin(shore.Beach()!, null, null, null)]), shore);
    }

    /// <summary>
    /// A floor; one water shape of two sheets over it, 40 and 80 units up, the lower first; and a mixed sheet 20 up under both. Up is minus z.
    /// </summary>
    private static SkinnedMesh Stacked()
    {
        var places = new List<Vector3> { new(-400f, -400f, 0f), new(400f, -400f, 0f), new(400f, 400f, 0f), new(-400f, 400f, 0f) };
        foreach ((float half, float height) in (ReadOnlySpan<(float, float)>)[(375f, 40f), (375f, 80f), (200f, 20f)])
        {
            places.AddRange([new(-half, -half, -height), new(half, -half, -height), new(half, half, -height), new(-half, half, -height)]);
        }

        var indices = new List<int>();
        for (var quad = 0; quad < places.Count / 4; quad++)
        {
            int first = quad * 4;
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }

        var normals = new Vector3[places.Count];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        return SkinnedMesh.Of(
            [.. places], normals, [.. indices], new Vector3(-400f, -400f, -80f), new Vector3(400f, 400f, 0f),
            [.. places.Select(one => new Vector2((one.X + 400f) / 800f, (one.Y + 400f) / 800f))],
            [new MeshShape("Floor", 0, 6), new MeshShape("Water", 6, 12), new MeshShape("Glass", 18, 6)]);
    }

    /// <summary>
    /// A material cut out on the alpha its graphs leave - over a shore, half a second in, since a rock's alpha is the sea's spray on it
    /// (CrashingFoamSurface) and without a shore nothing of it stands: 49% of the picture drawn on the processor at this moment.
    /// </summary>
    [GpuFact]
    public void ACUTOUTMaterialIsCutWhereTheProcessorCutsIt()
        => Every(
            program => new ModelScene(
                Hill(layered: false), Turn: 0.4f, Tilt: 0.5f, Blends: [MaterialBlend.Cutout], Shades: [program], Time: 0.5f,
                Water: WaterLook.EngineDefault with { Shore = BayShore() }),
            program => program.HasAlpha);

    /// <summary>A material's compiled shaders are kept in the folder handed in, and a new card reads them back rather than compiling again.</summary>
    [GpuFact]
    public void COMPILEDShadersAreKeptOnDiskAndReadBack()
    {
        string folder = Path.Combine(Path.GetTempPath(), "poeformance-shaders-" + Guid.NewGuid().ToString("N"));
        try
        {
            (string _, ShadeProgram program) = Programs().First();
            var scene = new ModelScene(Hill(layered: false), Turn: 0.4f, Tilt: 0.5f, Blends: [MaterialBlend.Opaque], Shades: [Bound(program)]);
            using (ModelGpu? first = ModelGpu.Warp(out string why, folder))
            {
                Assert.True(first is not null, why);
                Assert.True(Ready(first, scene, out why), why);
            }

            string[] kept = Directory.GetFiles(folder, "*.cso");
            Assert.NotEmpty(kept);

            using ModelGpu? again = ModelGpu.Warp(out string said, folder);
            Assert.True(again is not null, said);
            Assert.True(Ready(again, scene, out said), said);
            Assert.Equal(kept.Length, Directory.GetFiles(folder, "*.cso").Length);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>Every fixture program the filter takes, drawn by both - and every one that differs named, not only the first.</summary>
    private void Every(Func<ShadeProgram, ModelScene> scene, Func<ShadeProgram, bool>? which = null)
    {
        ModelGpu? gpu = card.Gpu;
        Assert.True(gpu is not null, card.Why);
        using ModelTarget target = gpu.Target(Side);
        var failed = new List<string>();
        var drawn = 0;
        foreach ((string name, ShadeProgram program) in Programs())
        {
            if (which is not null && !which(program))
            {
                continue;
            }

            drawn++;
            if (Mismatch(gpu, target, scene(Bound(program))) is { } mismatch)
            {
                failed.Add($"{name}: {mismatch}");
            }
        }

        Assert.True(drawn > 0, "no fixture program was drawn");
        Assert.True(failed.Count == 0, string.Join(Environment.NewLine, failed));
    }

    private static string? Mismatch(ModelGpu gpu, ModelTarget target, ModelScene scene)
    {
        var canvas = new MeshPicture.Canvas(Side) { Light = scene.Light, Time = scene.Time, Water = scene.Water ?? WaterLook.EngineDefault, Pose = scene.Pose };
        byte[] cpu = [.. MeshPicture.Of(
            scene.Mesh, canvas, scene.Turn, scene.Tilt, scene.Ink, scene.Skin, scene.Zoom, scene.Pan, scene.Skins, scene.Blends, scene.Shades).Rgba];
        if (!Ready(gpu, scene, out string why) || !gpu.Draw(target, scene, out why))
        {
            return why;
        }

        return ModelGpuTests.Mismatch(cpu, gpu.Read(target));
    }

    /// <summary>Waits for a picture's shaders as the pane does, asking again until the card says yes or something other than compiling.</summary>
    private static bool Ready(ModelGpu gpu, in ModelScene scene, out string why)
    {
        var clock = Stopwatch.StartNew();
        while (!gpu.Can(scene, out why))
        {
            if (!why.Contains("compiling", StringComparison.Ordinal) || clock.Elapsed > Patience)
            {
                return false;
            }

            Thread.Sleep(10);
        }

        return true;
    }

    /// <summary>A program with a sheet for every texture it reads - waves where it marches a parallax, ramps elsewhere.</summary>
    /// <remarks>
    /// THE LEVEL A READ TAKES IS THE ONE THING THE TWO PICTURES DECIDE DIFFERENTLY, by design: the card
    /// per pixel from the screen's derivatives, as the game, the processor per triangle. Where the
    /// coordinates are straight across a triangle the two agree, and a ramp's wrap - 220 back to 30 -
    /// is drawn alike. After a parallax march they are not straight, and that seam is where the two
    /// levels part: measured on the processor, forcing every read to the top level moves 2.4% of
    /// Dung01c's pixels with ramps and 0.1% with waves, which have no seam and lose next to nothing to
    /// the first few levels. A wave is not used for every material because a level changes it more far
    /// down: RockyLedgec, tiled densely, moves 46% with ramps and 86% with waves under the same forcing,
    /// and its straight coordinates draw alike either way.
    /// </remarks>
    private static ShadeProgram Bound(ShadeProgram program)
        => program.With([.. program.Textures.Select(one => (Mipmaps?)(program.UsesTangents ? Wave(one.Path) : Ramp(one.Path)))]);

    /// <summary>
    /// A seamless wave, its own per path - red a cosine across, green a sine down, alpha one along the diagonal - so the wrap has no seam and the first levels hardly differ.
    /// </summary>
    private static Mipmaps Wave(string path)
    {
        lock (Sheets)
        {
            string key = path + "~wave";
            if (Sheets.TryGetValue(key, out Mipmaps? had))
            {
                return had;
            }

            int seed = Seed(path);
            const int side = 64;
            float phase = (seed & 0xFF) / 255f * MathF.Tau;
            float blue = 0.2f + (0.6f * (((seed >> 8) & 0xFF) / 255f));
            var rgba = new byte[side * side * 4];
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    int at = ((y * side) + x) * 4;
                    float u = MathF.Tau * x / side, v = MathF.Tau * y / side;
                    rgba[at] = (byte)(128 + (90 * MathF.Cos(u + phase)));
                    rgba[at + 1] = (byte)(128 + (90 * MathF.Sin(v - phase)));
                    rgba[at + 2] = (byte)(blue * 255f);
                    rgba[at + 3] = (byte)(160 + (80 * MathF.Cos(u + v)));
                }
            }

            Mipmaps made = Mipmaps.Of(new GamePicture(side, side, rgba))!;
            Sheets[key] = made;
            return made;
        }
    }

    private static int Seed(string path)
    {
        var seed = 0;
        foreach (char letter in path)
        {
            seed = unchecked((seed * 31) + letter);
        }

        return seed;
    }

    /// <summary>
    /// A smooth ramp, its own per path - red across, green down, blue and alpha from the path - so a read at a nearby level is nearly the same colour.
    /// </summary>
    private static Mipmaps Ramp(string path)
    {
        lock (Sheets)
        {
            if (Sheets.TryGetValue(path, out Mipmaps? had))
            {
                return had;
            }

            int seed = Seed(path);
            const int side = 64;
            float blue = 0.2f + (0.6f * ((seed & 0xFF) / 255f));
            var rgba = new byte[side * side * 4];
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    int at = ((y * side) + x) * 4;
                    rgba[at] = (byte)(30 + (x * 190 / (side - 1)));
                    rgba[at + 1] = (byte)(40 + (y * 180 / (side - 1)));
                    rgba[at + 2] = (byte)(blue * 255f);
                    rgba[at + 3] = (byte)(60 + ((x + y) * 195 / ((2 * side) - 2)));
                }
            }

            Mipmaps made = Mipmaps.Of(new GamePicture(side, side, rgba))!;
            Sheets[path] = made;
            return made;
        }
    }

    /// <summary>A quay along x from 50 on over the hill's middle, the sea towards minus x - the field's own frame is the scene's.</summary>
    private static ShoreField BayShore()
    {
        Vector3[] places =
        [
            new(-800f, -800f, 0f), new(800f, -800f, 0f), new(800f, 800f, 0f), new(-800f, 800f, 0f),
            new(-800f, -800f, 300f), new(50f, -800f, 300f), new(50f, 800f, 300f), new(-800f, 800f, 300f),
            new(50f, -800f, -100f), new(800f, -800f, -100f), new(800f, 800f, -100f), new(50f, 800f, -100f),
        ];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        SkinnedMesh bay = SkinnedMesh.Of(
            places, normals, [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11], new Vector3(-800f, -800f, -100f), new Vector3(800f, 800f, 300f),
            [.. places.Select(one => new Vector2(one.X, one.Y))],
            [new MeshShape("Water", 0, 6), new MeshShape("Sea floor", 6, 6), new MeshShape("Quay", 12, 6)]);
        ShoreField? shore = ShoreField.Of(bay, shape => shape == 0, shape => shape > 0);
        Assert.NotNull(shore);
        return shore;
    }

    /// <summary>
    /// A rolling sheet <see cref="Scale"/> either way of the middle facing the camera, with coordinates tiled half again, normals that turn every way and a vertex colour that ramps - and where asked a second sheet a little in front of it, a shape of its own.
    /// </summary>
    private static SkinnedMesh Hill(bool layered)
    {
        const int cells = 24;
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var spots = new List<Vector2>();
        var colours = new List<byte>();
        var indices = new List<int>();
        var shapes = new List<MeshShape>();
        foreach (float lift in layered ? [0f, 0.12f] : (float[])[0f])
        {
            int start = positions.Count;
            int from = indices.Count;
            for (var j = 0; j <= cells; j++)
            {
                for (var i = 0; i <= cells; i++)
                {
                    float x = -1f + (2f * i / cells), z = -1f + (2f * j / cells);
                    float y = (0.25f * MathF.Sin(3f * x) * MathF.Cos(2f * z)) + lift;
                    positions.Add(new Vector3(x, y, z) * Scale);
                    normals.Add(Vector3.Normalize(new Vector3(-0.75f * MathF.Cos(3f * x) * MathF.Cos(2f * z), 1f, 0.5f * MathF.Sin(3f * x) * MathF.Sin(2f * z))));
                    spots.Add(new Vector2((x * 1.5f) + 0.5f, (z * 1.5f) + 0.5f));
                    colours.AddRange([(byte)(i * 255 / cells), (byte)(j * 255 / cells), 128, (byte)(255 - (i * 4))]);
                }
            }

            for (var j = 0; j < cells; j++)
            {
                for (var i = 0; i < cells; i++)
                {
                    int a = start + (j * (cells + 1)) + i, b = a + cells + 1;
                    indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
                }
            }

            shapes.Add(new MeshShape($"sheet {shapes.Count}", from, indices.Count - from));
        }

        return SkinnedMesh.Of(
            [.. positions], [.. normals], [.. indices], new Vector3(-1f, -0.3f, -1f) * Scale, new Vector3(1f, 0.4f, 1f) * Scale,
            [.. spots], shapes, colours: [.. colours]);
    }

    /// <summary>Every material in tests/fixtures/shaders whose graphs are all there, compiled - its plain and its glossy program.</summary>
    private static IEnumerable<(string Name, ShadeProgram Program)> Programs()
    {
        string root = Folder();
        foreach (string material in Directory.GetFiles(root, "*.mat").Order(StringComparer.Ordinal))
        {
            IReadOnlyList<ShaderInstance> instances = ShaderGraph.Instances(File.ReadAllBytes(material));
            if (instances.Count == 0 || instances.Any(one => !File.Exists(Path.Combine(root, Flat(one.Parent)))))
            {
                continue;
            }

            ShadeCompile compiled = ShadeProgram.Compile(
                [.. instances.Select(one => (one, ShaderGraph.Read(File.ReadAllBytes(Path.Combine(root, Flat(one.Parent))))))]);
            string name = Path.GetFileNameWithoutExtension(material);
            if (compiled.Program is { } program)
            {
                yield return (name, program);
                if (program.Glossy is { } glossy)
                {
                    yield return (name + " (glossy)", glossy);
                }
            }
        }
    }

    private static string Flat(string path) => path.Replace("/", "__", StringComparison.Ordinal);

    private static string Folder()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "tests", "fixtures", "shaders");
    }
}

/// <summary>The WARP card <see cref="ProgramGpuTests"/> shares, made once for the class.</summary>
public sealed class CardFixture : IDisposable
{
    public CardFixture()
    {
        string why = "Direct3D 11 is Windows'";
        Gpu = OperatingSystem.IsWindows() ? ModelGpu.Warp(out why) : null;
        Why = why;
    }

    /// <summary>The card, or null where it would not start.</summary>
    public ModelGpu? Gpu { get; }

    /// <summary>Why there is none, or empty.</summary>
    public string Why { get; }

    /// <inheritdoc/>
    public void Dispose() => Gpu?.Dispose();
}
