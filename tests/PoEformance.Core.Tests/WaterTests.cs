using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The engine's water - an Ocean material - drawn as the area's open water: the values each area's file sets over the engine's measured defaults, and a medium the floor shows through by how much water lies over it.
/// </summary>
public class WaterTests
{
    private const int Size = 96;

    /// <summary>Looking straight down.</summary>
    private const float Down = MathF.PI / 2f;

    /// <summary>nvidian_Ocean.mat as Port's capture wrote it whole.</summary>
    private const string Ocean =
        """{"version":4,"graphinstances":[{"parent":"Metadata/Art/Textures/Environment/forest/nvidian_Ocean.fxgraph"},{"parent":"Metadata/Effects/Graphs/General/BasicColourNormalSpec.fxgraph","custom_parameters":[{"name":"base_normalspec_texture","parameters":[{"path":"Art/Textures/Environment/forest/nvidia_normal_masked.dds","srgb":false},{}]}]}],"shadertype":"Ocean","minimapcolourmultiplier":0.7799999713897705,"splashywater":true}""";

    /// <summary>An Ocean material says so, and the engine's water pass is drawn mixed whatever its graph's mode.</summary>
    [Fact]
    public void ANOCEANMaterialSaysItsShaderTypeAndIsMixed()
    {
        MaterialFile paint = MaterialFile.Parse(Ocean);
        Assert.Equal(ShadeProgram.OceanShader, paint.ShaderType);
        Assert.Equal(2, paint.Parents.Count);
        Assert.True(MonsterModels.Paints.IsWater(paint));
        Assert.Equal(MaterialBlend.Alpha, MaterialBlends.Of("water"));
        Assert.Equal(string.Empty, MaterialFile.Parse("""{"version":4,"graphinstances":[{"parent":"a.fxgraph"}]}""").ShaderType);
    }

    /// <summary>Port's file sets its flow and height offset and no colour, so its water is the engine's colour; The Red Vale's sets its own.</summary>
    [Fact]
    public void ANAREASWaterIsItsFilesValuesOverTheEnginesDefaults()
    {
        WaterLook port = WaterLook.Of(EnvironmentSettings.Read("port.env", Encoding.UTF8.GetBytes(
            """{ "water": { "flow_intensity": 1.5, "flow_turbulence": 1.0, "flow_resolution": 7, "flow_wait_init": true, "height_offset": -15.0 } }""")));
        Assert.Equal(WaterLook.DefaultOpenColour, port.OpenColour);
        Assert.Equal(WaterLook.DefaultMurkiness, port.Murkiness);
        Assert.Equal(1.5f, port.FlowIntensity);
        Assert.Equal(-15f, port.HeightOffset);
        Assert.Equal("flow, height offset from the file, the rest the engine's default", port.Said);

        WaterLook vale = WaterLook.Of(EnvironmentSettings.Read("vale.env", Encoding.UTF8.GetBytes(
            """{ "water": { "open_water_color": [ 0.19043, 0.02095, 0.0017 ], "open_water_murkiness": 0.035 } }""")));
        Assert.Equal(new Vector3(0.19043f, 0.02095f, 0.0017f), vale.OpenColour);
        Assert.Equal(0.035f, vale.Murkiness);
        Assert.Equal(WaterLook.NoFlow, vale.FlowIntensity);

        // LINEAR AS water.ffx MAKES IT, pow 2.2 and not the sRGB curve, the density in w.
        Assert.Equal(MathF.Pow(0.19043f, 2.2f), vale.ColourRegister.X, 5);
        Assert.Equal(0.035f, vale.ColourRegister.W);
        Assert.Equal(WaterLook.EngineDefault, WaterLook.Of(null));

        // THE OCEAN'S WIND: Port sets none, so its ripples are the engine's former constants; Blank Sea's file sets all four.
        Assert.Equal(new Vector4(0f, 0.1f * 4f * 4f / 1.5f, 1f, 1f), port.WindRegister);
        WaterLook sea = WaterLook.Of(EnvironmentSettings.Read("sea.env", Encoding.UTF8.GetBytes(
            """{ "water": { "directionness": 0.748, "wind_direction_angle": -2.44346, "wind_intensity": 0.822, "wind_speed": 0.75099 } }""")));
        Assert.Equal(new Vector4(-2.44346f, 0.822f, 0.75099f, 0.748f), sea.WindRegister);
        Assert.Equal("directionness, wind's way, wind's strength, wind's speed from the file, the rest the engine's default", sea.Said);
    }

    /// <summary>The water program reads the depth behind and the clock, covers by its own alpha, and carries no texture.</summary>
    [Fact]
    public void THEWATERProgramIsAMediumNotAPicture()
    {
        ShadeProgram water = ShadeProgram.Water();
        Assert.True(water.HasAlpha);
        Assert.True(water.UsesDepth);
        Assert.True(water.UsesTime);
        Assert.Empty(water.Textures);
        Assert.Equal([ShadeProgram.WaterGraph], water.Graphs);

        string hlsl = ShadeHlsl.Of(water, 8);
        Assert.Contains("float4 r12 = water;", hlsl, StringComparison.Ordinal);
        Assert.Contains("float4 r13 = flow;", hlsl, StringComparison.Ordinal);
        Assert.Contains("static const bool ShadeHasAlpha = true;", hlsl, StringComparison.Ordinal);
    }

    /// <summary>
    /// The floor shows through shallow water and not through deep: a red floor under a water layer in two halves, ten units over it and three hundred, at the engine's density.
    /// </summary>
    /// <remarks>
    /// exp(-10 * 0.01) lets nine tenths of the floor through; exp(-300 * 0.01) a twentieth - and the
    /// water's own colour is dark, so the deep half is dark where the shallow one is still red.
    /// </remarks>
    [Fact]
    public void THEFLOORShowsThroughShallowWaterAndNotThroughDeep()
    {
        SkinnedMesh scene = Scene(10f, 300f);
        GamePicture picture = Pictured(scene, WaterLook.EngineDefault);

        byte shallow = RedAt(picture, scene, new Vector3(-5f, 0f, -10f));
        byte deep = RedAt(picture, scene, new Vector3(5f, 0f, -300f));
        Assert.True(shallow > 150, $"the floor under ten units of water reads {shallow} red");
        Assert.True(deep < 60, $"the floor under three hundred units of water reads {deep} red");
    }

    /// <summary>
    /// The water thins out at the floor as the engine's pass thins it: two units over the floor, at a density that would hide nearly half of it, the floor reads nearly bare.
    /// </summary>
    /// <remarks>
    /// Unthinned, exp(-2 * 0.3) lets 55% of the floor through; thinned by saturate(2 * 0.03) as
    /// WaterComputeRefraction does, 96%. A hundred units down nothing of it is left either way.
    /// </remarks>
    [Fact]
    public void THESHOREFadesTheWaterOutAtTheFloor()
    {
        SkinnedMesh scene = Scene(2f, 100f);
        var thick = new WaterLook(new Vector3(0f, 0f, 0.5f), 0.3f, WaterLook.NoFlow, 0f, "a thick blue water");
        Vector3 shore = new(-5f, 0f, -2f);

        byte bare = RedAt(Pictured(scene, thick with { Murkiness = 0f }), scene, shore);
        GamePicture picture = Pictured(scene, thick);
        byte through = RedAt(picture, scene, shore);
        byte deep = RedAt(picture, scene, new Vector3(5f, 0f, -100f));
        Assert.True(bare - through < 15, $"the floor two units under the water reads {through} red, {bare} bare");
        Assert.True(deep < 30, $"the floor a hundred units under the water reads {deep} red");
    }

    /// <summary>A Monster Book build draws an Ocean material with the water program, mixed, and says so in its report.</summary>
    [Fact]
    public void THEPAINTSDrawAnOceanMaterialAsTheWater()
    {
        byte[]? Read(string path) => path.EndsWith("ocean.mat", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8.GetBytes(Ocean) : null;
        var paints = new MonsterModels.Paints();

        Assert.Equal(("water", "water"), paints.Modes(Read, "Art/ocean.mat"));
        ShadeCompile compiled = paints.Compiled(Read, "Art/ocean.mat").Compiled;
        Assert.NotNull(compiled.Program);
        Assert.Equal([ShadeProgram.WaterGraph], compiled.Program.Graphs);
        Assert.Contains("drawn as the area's water", Assert.Single(compiled.Skipped), StringComparison.Ordinal);
        Assert.NotNull(paints.Shade(Read, "Art/ocean.mat").Program);
    }

    /// <summary>A red floor under the scene's water, drawn looking down.</summary>
    private static GamePicture Pictured(SkinnedMesh scene, WaterLook water) => MeshPicture.Of(
        scene, new MeshPicture.Canvas(Size) { Water = water }, tilt: Down,
        skins: [Sheet(220, 30, 30), null],
        blends: [MaterialBlend.Opaque, MaterialBlend.Alpha],
        shades: [null, ShadeProgram.Water()]);

    /// <summary>
    /// The ripple texture's green and blue are its red's slope a texel, as the engine's texture's are: they agree with the red's differences at every texel, across the wrap too.
    /// </summary>
    [Fact]
    public void THERIPPLETexturesSlopeIsItsRedsOwn()
    {
        const int side = ShadeProgram.RippleSide;
        ReadOnlySpan<float> texels = ShadeProgram.RippleSheet;
        Assert.Equal(side * side * 4, texels.Length);
        float Red(int x, int y) => ShadeProgram.RippleSheet[((((y + side) % side) * side) + ((x + side) % side)) * 4];
        float steepest = 0f, worst = 0f, lowest = 1f, highest = 0f;
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                int at = ((y * side) + x) * 4;
                float dx = (Red(x + 1, y) - Red(x - 1, y)) / 2f, dy = (Red(x, y + 1) - Red(x, y - 1)) / 2f;
                worst = MathF.Max(worst, MathF.Max(MathF.Abs(texels[at + 1] - dx), MathF.Abs(texels[at + 2] - dy)));
                steepest = MathF.Max(steepest, MathF.Max(MathF.Abs(texels[at + 1]), MathF.Abs(texels[at + 2])));
                lowest = MathF.Min(lowest, texels[at]);
                highest = MathF.Max(highest, texels[at]);
                Assert.Equal(1f, texels[at + 3]);
            }
        }

        Assert.True(steepest > 0.01f, $"steepest {steepest}");
        Assert.True(worst < 0.05f * steepest, $"worst {worst} of {steepest}");
        Assert.InRange(lowest, 0f, 0.3f);
        Assert.InRange(highest, 0.7f, 1f);
    }

    /// <summary>The engine's former constants, Blank Sea's file and Yaochi's - three winds as the areas set them.</summary>
    private static readonly Vector4[] Winds =
    [
        WaterLook.EngineDefault.WindRegister,
        new(-2.44346f, 0.822f, 0.75099f, 0.748f),
        new(3.14159f, 4f, 0.5f, 0f),
    ];

    /// <summary>
    /// The ocean's slope is its height's own over the texture's texels: the texels' slope mixed, as the engine mixes it, agrees with the height's differences a texel apart everywhere it is asked.
    /// </summary>
    /// <remarks>
    /// Not to the bit: a linear read's height is flat-sided between texel centres while its mixed slope is not, so the two part by
    /// a little inside a texel - the engine's own surface does the same - and the differences are taken across a finest texel.
    /// </remarks>
    [Fact]
    public void THEOCEANSSlopeIsItsHeightsOwn()
    {
        const float h = 1f;
        float[] clocks = [0f, 0.7f, 2.9f, 13.25f];
        double errors = 0, slopes = 0;
        var checkedOnes = 0;
        foreach (Vector4 wind in Winds)
        {
            foreach (float clock in clocks)
            {
                for (var x = -900f; x <= 900f; x += 137f)
                {
                    for (var y = -700f; y <= 700f; y += 151f)
                    {
                        ShadeProgram.Ocean(new Vector2(x, y), wind, Swell, clock, null, out Vector2 slope);
                        float dx = (ShadeProgram.Ocean(new Vector2(x + h, y), wind, Swell, clock, null, out _) - ShadeProgram.Ocean(new Vector2(x - h, y), wind, Swell, clock, null, out _)) / (2f * h);
                        float dy = (ShadeProgram.Ocean(new Vector2(x, y + h), wind, Swell, clock, null, out _) - ShadeProgram.Ocean(new Vector2(x, y - h), wind, Swell, clock, null, out _)) / (2f * h);
                        errors += ((slope.X - dx) * (slope.X - dx)) + ((slope.Y - dy) * (slope.Y - dy));
                        slopes += (slope.X * slope.X) + (slope.Y * slope.Y);
                        checkedOnes++;
                    }
                }
            }
        }

        Assert.True(checkedOnes > 1000);
        double share = Math.Sqrt(errors / slopes);
        Assert.True(share < 0.2, $"the slope parts from the differences by {share:P1}");
    }

    /// <summary>
    /// The ocean's ripples roll on steadily and never start over: from frame to frame at sixty a second they change by about the same everywhere, for half a minute - the river's octaves slid and faded back to their start every one and a half to three seconds, which read as a short film looped.
    /// </summary>
    [Fact]
    public void THEOCEANSRipplesRollOnWithoutStartingOver()
    {
        const float frame = 1f / 60f;
        Vector2[] places = [new(310f, -145f), new(-820f, 433f), new(57f, 1290f)];
        foreach (Vector4 wind in Winds)
        {
            foreach (Vector2 place in places)
            {
                float was = ShadeProgram.Ocean(place, wind, Swell, 0f, null, out _);
                double sum = 0;
                float most = 0f;
                const int frames = 30 * 60;
                for (var one = 1; one <= frames; one++)
                {
                    float now = ShadeProgram.Ocean(place, wind, Swell, one * frame, null, out _);
                    float change = MathF.Abs(now - was);
                    sum += change;
                    most = MathF.Max(most, change);
                    was = now;
                }

                double mean = sum / frames;
                Assert.True(mean > 1e-4, $"{wind} at {place}: the ripples stand still");
                Assert.True(most < mean * 8, $"{wind} at {place}: a frame jumps by {most} where they change by {mean:0.####} on average");
            }
        }
    }

    /// <summary>The ripples lean, so the light sees them - a few degrees under the engine's former constants, steep enough for a glint to move about, not a choppy sea.</summary>
    [Fact]
    public void THEOCEANSRipplesLean()
    {
        float steepest = 0f;
        for (var x = 0f; x < 2000f; x += 25f)
        {
            ShadeProgram.Ocean(new Vector2(x, x * 0.37f), WaterLook.EngineDefault.WindRegister, Swell, 4f, null, out Vector2 slope);
            steepest = MathF.Max(steepest, slope.Length());
        }

        Assert.InRange(steepest, 0.02f, 0.6f);
    }

    /// <summary>The ocean's swell where a file sets none.</summary>
    private static readonly Vector4 Swell = WaterLook.EngineDefault.SwellRegister;

    /// <summary>No wind: the ripples stand at nought, so what moves is the swell alone.</summary>
    private static readonly Vector4 Still = new(0f, 0f, 1f, 1f);

    /// <summary>
    /// A bay: water 6,000 across at nought over a sea floor 300 down, and a quay 100 up along x from 1,000 on - the shore a line at x = 1,000, the open sea towards minus x. Up is minus z.
    /// </summary>
    private static SkinnedMesh Bay()
    {
        Vector3[] places =
        [
            new(-3000f, -3000f, 0f), new(3000f, -3000f, 0f), new(3000f, 3000f, 0f), new(-3000f, 3000f, 0f),
            new(-3000f, -3000f, 300f), new(1000f, -3000f, 300f), new(1000f, 3000f, 300f), new(-3000f, 3000f, 300f),
            new(1000f, -3000f, -100f), new(3000f, -3000f, -100f), new(3000f, 3000f, -100f), new(1000f, 3000f, -100f),
        ];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11];
        return SkinnedMesh.Of(
            places, normals, indices, new Vector3(-3000f, -3000f, -100f), new Vector3(3000f, 3000f, 300f),
            [.. places.Select(one => new Vector2(one.X, one.Y))],
            [new MeshShape("Water", 0, 6), new MeshShape("Sea floor", 6, 6), new MeshShape("Quay", 12, 6)]);
    }

    private static ShoreField BayShore()
    {
        ShoreField? shore = ShoreField.Of(Bay(), shape => shape == 0, shape => shape > 0);
        Assert.NotNull(shore);
        return shore;
    }

    /// <summary>
    /// A room's shore is made from its own ground and water: nought at the quay's edge, a colour unit 2,000 out to sea, negative on the quay, and the distance out to sea one less - the swell rolls in square to the shore.
    /// </summary>
    [Fact]
    public void AROOMSShoreIsItsGroundOverItsWater()
    {
        ShoreField shore = BayShore();
        shore.At(new Vector2(-1000f, 0f), out float far, out float farOcean);
        shore.At(new Vector2(990f, 0f), out float edge, out _);
        shore.At(new Vector2(1500f, 0f), out float quay, out _);
        Assert.InRange(far, 0.95f, 1.05f);
        Assert.Equal(1f - far, farOcean, 4);
        Assert.InRange(edge, -0.02f, 0.03f);
        Assert.InRange(quay, -0.3f, -0.2f);

        // THE SLOPE OUT TO SEA AS THE ENGINE TAKES IT, negated: the distance out to sea grows towards the shore, plus x.
        shore.Fields(new Vector2(0f, 0f), out _, out _, out Vector2 slope);
        Assert.True(slope.X < 0f && MathF.Abs(slope.Y) < 1e-5f, $"slope {slope}");

        // NO SHORE WITHOUT LAND, AND NONE WITHOUT WATER.
        Assert.Null(ShoreField.Of(Bay(), shape => shape == 0, shape => shape == 1));
        Assert.Null(ShoreField.Of(Bay(), shape => false, shape => true));
    }

    /// <summary>The swell's crests roll in to the shore - the nearest crest to a place moves towards the quay as the clock runs.</summary>
    [Fact]
    public void THESWELLRollsInToTheShore()
    {
        ShoreField shore = BayShore();
        float Crest(float clock, float near)
        {
            float best = near, highest = float.MinValue;
            for (float x = near - 200f; x <= near + 200f; x += 2f)
            {
                float height = ShadeProgram.Ocean(new Vector2(x, 0f), Still, Swell, clock, shore, out _);
                if (height > highest)
                {
                    highest = height;
                    best = x;
                }
            }

            return best;
        }

        float crest = Crest(3f, -500f);
        for (var step = 1; step <= 10; step++)
        {
            float next = Crest(3f + (step * 0.1f), crest);
            Assert.True(next > crest, $"at {3f + (step * 0.1f)} s the crest at {crest} went to {next}");
            crest = next;
        }

        // AND IT STANDS: as high as the engine's height, nought over the open sea's half.
        float lowest = float.MaxValue, highest = float.MinValue;
        for (float x = -1500f; x <= 0f; x += 5f)
        {
            float height = ShadeProgram.Ocean(new Vector2(x, 0f), Still, Swell, 3f, shore, out _);
            lowest = MathF.Min(lowest, height);
            highest = MathF.Max(highest, height);
        }

        Assert.InRange(highest - lowest, 0.4f * Swell.X, 1.01f * Swell.X);
    }

    /// <summary>The ripples thin to nothing at the shoreline, as the engine's shallowRippleMult has them, and the swell's slope is its height's own.</summary>
    [Fact]
    public void THERIPPLESThinAtTheShoreAndTheSwellsSlopeIsItsOwn()
    {
        ShoreField shore = BayShore();
        Vector4 wind = WaterLook.EngineDefault.WindRegister;
        var flat = new Vector4(0f, Swell.Y, Swell.Z, 0f);
        float Spread(float x)
        {
            float lowest = float.MaxValue, highest = float.MinValue;
            for (float y = -1000f; y <= 1000f; y += 7f)
            {
                float height = ShadeProgram.Ocean(new Vector2(x, y), wind, flat, 2f, shore, out _);
                lowest = MathF.Min(lowest, height);
                highest = MathF.Max(highest, height);
            }

            return highest - lowest;
        }

        Assert.True(Spread(990f) < 0.2f * Spread(-1000f), $"at the shore {Spread(990f)}, out at sea {Spread(-1000f)}");

        const float h = 1f;
        double errors = 0, slopes = 0;
        foreach (float clock in new[] { 0.5f, 3f, 11.7f })
        {
            for (var x = -1400f; x <= 900f; x += 37f)
            {
                var place = new Vector2(x, 13f);
                ShadeProgram.Ocean(place, Still, Swell, clock, shore, out Vector2 slope);
                float dx = (ShadeProgram.Ocean(place + new Vector2(h, 0f), Still, Swell, clock, shore, out _)
                    - ShadeProgram.Ocean(place - new Vector2(h, 0f), Still, Swell, clock, shore, out _)) / (2f * h);
                errors += (slope.X - dx) * (slope.X - dx);
                slopes += slope.X * slope.X;
            }
        }

        // NOT TO THE BIT: the engine's own slope leaves out the shore's slowing and the crest's sharpening, which the height has.
        double share = Math.Sqrt(errors / slopes);
        Assert.True(share < 0.35, $"the swell's slope parts from the differences by {share:P1}");
    }

    /// <summary>The water carries a glossy twin with the waves - its specular the area's reflectiveness, its gloss the engine's - and only the twin reads them.</summary>
    [Fact]
    public void THEWATERSGlossyTwinCarriesTheWaves()
    {
        ShadeProgram water = ShadeProgram.Water();
        Assert.False(water.HasWaves);
        Assert.False(water.HasSpecular);

        ShadeProgram? waves = water.Glossy;
        Assert.NotNull(waves);
        Assert.True(waves.HasWaves);
        Assert.True(waves.HasSpecular);
        Assert.True(waves.HasGloss);
        Assert.True(waves.HasAlpha);
        Assert.True(waves.UsesDepth);
        Assert.True(waves.UsesTime);
        Assert.Empty(waves.Textures);

        string plain = ShadeHlsl.Of(water, 9);
        Assert.Contains("static const bool ShadeHasWaves = false;", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("ShadeWaves(", plain, StringComparison.Ordinal);
        Assert.Contains("said.waves = float3(0.0, 0.0, 0.0);", plain, StringComparison.Ordinal);

        string wavy = ShadeHlsl.Of(waves, 9);
        Assert.Contains("static const bool ShadeHasWaves = true;", wavy, StringComparison.Ordinal);
        Assert.Contains("float4 ShadeWaves(float2 place, float4 wind, float clock, float4 shore, float4 swell)", wavy, StringComparison.Ordinal);
        Assert.Contains("said.waves = r", wavy, StringComparison.Ordinal);

        // THE ENGINE'S STEADY SCROLL, as the processor writes it - no phase that wraps.
        Assert.Contains("float qy = (place.x * m01) + (place.y * m11) - (time * 1.5 * shift);", wavy, StringComparison.Ordinal);
        Assert.DoesNotContain("floor(phase)", wavy, StringComparison.Ordinal);
    }

    /// <summary>An area's reflectiveness is its file's where it sets one, the engine's 0.03 where not, and reaches the program in the flow's y.</summary>
    [Fact]
    public void ANAREASReflectivenessIsItsFilesOrTheEngines()
    {
        WaterLook set = WaterLook.Of(EnvironmentSettings.Read("set.env", Encoding.UTF8.GetBytes("""{ "water": { "reflectiveness": 0.126 } }""")));
        Assert.Equal(0.126f, set.Reflectiveness);
        Assert.Equal(0.126f, set.FlowRegister.Y);
        Assert.Contains("reflectiveness", set.Said, StringComparison.Ordinal);
        Assert.Equal(WaterLook.DefaultReflectiveness, WaterLook.EngineDefault.Reflectiveness);
        Assert.Equal(0.03f, WaterLook.EngineDefault.FlowRegister.Y);
    }

    /// <summary>
    /// Under the game light the waves glint and reflect where the switches say, and with both off - or under the picture's lamp - the water is drawn as it was without them.
    /// </summary>
    [Fact]
    public void THEWAVESGlintAndReflectByTheSwitchesAndOnlyUnderTheGameLight()
    {
        SkinnedMesh scene = Scene(40f, 300f);
        ShadeProgram plain = ShadeProgram.Water();
        ShadeProgram waves = plain.Glossy!;
        WaterLook port = WaterLook.EngineDefault with { FlowIntensity = 1.5f };
        SceneLight Light(bool glints, bool reflection) => new([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(2f),
            SunDirection = Vector3.UnitZ,
            Ambient = SceneAmbient.Flat,
            FlatAmbient = 0.4f,
            WaterGlints = glints,
            WaterReflection = reflection,
        };

        byte[] Drawn(ShadeProgram program, SceneLight? light) => [.. MeshPicture.Of(
            scene, new MeshPicture.Canvas(Size) { Water = port, Light = light, Time = 2f }, tilt: Down,
            skins: [Sheet(220, 30, 30), null],
            blends: [MaterialBlend.Opaque, MaterialBlend.Alpha],
            shades: [null, program]).Rgba];

        byte[] none = Drawn(plain, Light(glints: false, reflection: false));
        Assert.Equal(none, Drawn(waves, Light(glints: false, reflection: false)));
        Assert.Equal(Drawn(plain, null), Drawn(waves, null));

        long Brightness(byte[] rgba)
        {
            long sum = 0;
            for (var at = 0; at < rgba.Length; at += 4)
            {
                sum += rgba[at] + rgba[at + 1] + rgba[at + 2];
            }

            return sum;
        }

        Assert.True(Brightness(Drawn(waves, Light(glints: true, reflection: false))) > Brightness(none), "the sun straight down does not glint on the waves");
        Assert.True(Brightness(Drawn(waves, Light(glints: false, reflection: true))) > Brightness(none), "the flat ambient is not reflected");
    }

    /// <summary>
    /// A water shape is split until no edge is longer than asked, the two halves of a quad sharing every new vertex - so a lifted sea has no seams - its area and every other shape as they were.
    /// </summary>
    [Fact]
    public void AWATERShapeIsSplitFineWithoutSeamsAndTheRestIsLeft()
    {
        SkinnedMesh mesh = Quads();
        SkinnedMesh fine = mesh.Refined([true, false], RoomModels.WaterEdge);

        Assert.Equal(2, fine.Shapes.Count);
        MeshShape water = fine.Shapes[0];
        float longest = 0f, area = 0f;
        for (int at = water.From; at < water.From + water.Count; at += 3)
        {
            Vector3 a = fine.Positions[fine.Indices[at]], b = fine.Positions[fine.Indices[at + 1]], c = fine.Positions[fine.Indices[at + 2]];
            longest = MathF.Max(longest, MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a))));
            area += Vector3.Cross(b - a, c - a).Length() / 2f;
        }

        Assert.True(longest <= RoomModels.WaterEdge, $"an edge of {longest}");
        Assert.Equal(750f * 750f, area, 750f * 750f * 1e-4f);

        // HALVED FIVE TIMES, both halves alike: a grid of 33 by 33, and no place held twice.
        Vector3[] placesOfWater = [.. Enumerable.Range(water.From, water.Count).Select(at => fine.Positions[fine.Indices[at]]).Distinct()];
        Assert.Equal(33 * 33, placesOfWater.Length);
        Assert.Equal(2 * 1024 * 3, water.Count);

        // THE OTHER SHAPE as it was: one triangle on its own three places.
        MeshShape other = fine.Shapes[1];
        Assert.Equal(3, other.Count);
        Assert.Equal(
            [mesh.Positions[mesh.Indices[6]], mesh.Positions[mesh.Indices[7]], mesh.Positions[mesh.Indices[8]]],
            new[] { fine.Positions[fine.Indices[other.From]], fine.Positions[fine.Indices[other.From + 1]], fine.Positions[fine.Indices[other.From + 2]] });
        Assert.All(fine.Normals, one => Assert.Equal(1f, one.Length(), 4));

        // A ROOM SPLITS ITS WATER ALONE, by the shapes' blend words.
        Assert.Same(mesh, RoomModels.Watered(mesh, ["opaque", "opaque"]));
        Assert.Equal(water.Count, RoomModels.Watered(mesh, ["water", "opaque"]).Shapes[0].Count);
    }

    /// <summary>
    /// The game light's lift raises the water's vertices by its waves, and the picture changes with it.
    /// </summary>
    [Fact]
    public void THELIFTRaisesTheWaterByItsWavesUnderTheGameLightAlone()
    {
        SkinnedMesh scene = Quads().Refined([true, false], RoomModels.WaterEdge);
        ShadeProgram waves = ShadeProgram.Water().Glossy!;
        WaterLook port = WaterLook.EngineDefault with { FlowIntensity = 1.5f };
        SceneLight Light(float lift) => new([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(1.5f),
            SunDirection = Vector3.Normalize(new Vector3(0.3f, 0.2f, 1f)),
            Ambient = SceneAmbient.Flat,
            FlatAmbient = 0.3f,
            WaterLift = lift,
        };

        byte[] Drawn(SceneLight? light) => [.. MeshPicture.Of(
            scene, new MeshPicture.Canvas(Size) { Water = port, Light = light, Time = 2f }, turn: 0.4f, tilt: 0.35f,
            skins: [null, Sheet(220, 30, 30)],
            blends: [MaterialBlend.Alpha, MaterialBlend.Opaque],
            shades: [waves, null]).Rgba];

        Assert.NotEqual(Drawn(Light(0f)), Drawn(Light(3f)));
    }

    /// <summary>
    /// The engine's march over the solid depth: a fiftieth of the picture a step, a surface hit where it lies in front of the ray by less than fifty units, and the cube's share growing with the step.
    /// </summary>
    [Fact]
    public void THERAYMeetsTheFirstSurfaceInFrontOfItWithinItsThickness()
    {
        const int size = 100;
        var depth = new float[size * size];
        Array.Fill(depth, float.MaxValue);

        // A WALL down column 70, at depth 100: the ray runs right along row 10 and deeper by one a unit.
        for (var y = 0; y < size; y++)
        {
            depth[(y * size) + 70] = 100f;
        }

        // A STEP IS TWO PIXELS here, so from column 10 the ray reaches column 70 at its thirtieth step - past twenty.
        Assert.True(ScreenReflection.March(10, 10, 80f, new Vector2(1f, 0f), 1f, size, depth, out _, out _) < 0);

        // FROM COLUMN 40 it reaches the wall between its fifteenth and sixteenth step, ray depth about 110: hit.
        int step = ScreenReflection.March(40, 10, 80f, new Vector2(1f, 0f), 1f, size, depth, out int hitX, out int hitY);
        Assert.InRange(step, 14, 15);
        Assert.Equal(70, hitX);
        Assert.Equal(10, hitY);

        // TOO FAR BEHIND THE WALL - more than fifty - or in front of it, no hit.
        Assert.True(ScreenReflection.March(40, 10, 30f, new Vector2(1f, 0f), 1f, size, depth, out _, out _) < 0);
        Assert.True(ScreenReflection.March(40, 10, 10f, new Vector2(1f, 0f), 1f, size, depth, out _, out _) < 0);

        // STRAIGHT AT THE EYE it moves nowhere in the picture, and finds nothing.
        Assert.True(ScreenReflection.March(40, 10, 80f, Vector2.Zero, -1f, size, depth, out _, out _) < 0);

        Assert.Equal(1f, ScreenReflection.EnvironmentShare(-1));
        Assert.Equal(MathF.Pow(0.025f, 5f), ScreenReflection.EnvironmentShare(0), 9);
        Assert.Equal(MathF.Pow(19.5f / 20f, 5f), ScreenReflection.EnvironmentShare(19), 6);
    }

    /// <summary>
    /// A red wall rising behind the water shows in it where the scene is reflected, and not where only the cube is - its hue, not more light.
    /// </summary>
    [Fact]
    public void THESCENEShowsInTheWaterWhereItIsReflected()
    {
        SkinnedMesh scene = Walled();
        ShadeProgram waves = ShadeProgram.Water().Glossy!;
        SceneLight Light(bool mirroring) => new([], SceneLight.PointShape.Penumbra, null)
        {
            SunColour = new Vector3(1.5f),
            SunDirection = Vector3.Normalize(new Vector3(0.3f, 0.2f, 1f)),
            Ambient = SceneAmbient.Flat,
            FlatAmbient = 0.3f,
            WaterReflection = true,
            WaterScene = mirroring,
        };

        byte[] Drawn(bool mirroring) => [.. MeshPicture.Of(
            scene, new MeshPicture.Canvas(Size) { Light = Light(mirroring), Time = 2f }, turn: 0f, tilt: 0.3f,
            skins: [null, Sheet(30, 30, 30), Sheet(240, 20, 20)],
            blends: [MaterialBlend.Alpha, MaterialBlend.Opaque, MaterialBlend.Opaque],
            shades: [waves, null, null]).Rgba];

        // THE WALL'S HUE IN THE WATER: where a ray meets it, the wall's red stands in for the flat ambient's grey,
        // so red leads green by more - the wall being no brighter than the ambient, it does not add red.
        byte[] cube = Drawn(mirroring: false);
        byte[] mirrored = Drawn(mirroring: true);
        long cubeLead = 0, mirroredLead = 0;
        for (var at = 0; at < cube.Length; at += 4)
        {
            if (cube[at + 1] > 60)
            {
                cubeLead += cube[at] - cube[at + 1];
                mirroredLead += mirrored[at] - mirrored[at + 1];
            }
        }

        Assert.True(mirroredLead > cubeLead + 1000, $"the wall does not show in the water: red leads green by {mirroredLead}, {cubeLead} with the cube alone");
    }

    /// <summary>The water quad over its floor, and a red wall rising from the water on its far side - minus y, away from the camera at no turn.</summary>
    private static SkinnedMesh Walled()
    {
        Vector3[] places =
        [
            new(-375f, -375f, -60f), new(375f, -375f, -60f), new(375f, 375f, -60f), new(-375f, 375f, -60f),
            new(-400f, -400f, 0f), new(400f, -400f, 0f), new(400f, 400f, 0f), new(-400f, 400f, 0f),
            new(-375f, -375f, 0f), new(375f, -375f, 0f), new(375f, -375f, -500f), new(-375f, -375f, -500f),
        ];
        Vector3[] normals =
        [
            .. Enumerable.Repeat(new Vector3(0f, 0f, -1f), 8),
            .. Enumerable.Repeat(new Vector3(0f, 1f, 0f), 4),
        ];
        Vector2[] spots = [.. places.Select(one => new Vector2((one.X + 400f) / 800f, (one.Y + 400f) / 800f))];
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11];
        return SkinnedMesh.Of(
            places, normals, indices, new Vector3(-400f, -400f, -500f), new Vector3(400f, 400f, 0f),
            spots, [new MeshShape("Water", 0, 6), new MeshShape("Floor", 6, 6), new MeshShape("Wall", 12, 6)]);
    }

    /// <summary>A water quad 750 a side - two triangles - 60 units over a floor triangle of its own shape.</summary>
    private static SkinnedMesh Quads()
    {
        Vector3[] places =
        [
            new(-375f, -375f, -60f), new(375f, -375f, -60f), new(375f, 375f, -60f), new(-375f, 375f, -60f),
            new(-400f, -400f, 0f), new(400f, -400f, 0f), new(0f, 400f, 0f),
        ];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        Vector2[] spots = [.. places.Select(one => new Vector2((one.X + 400f) / 800f, (one.Y + 400f) / 800f))];
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6];
        return SkinnedMesh.Of(
            places, normals, indices, new Vector3(-400f, -400f, -60f), new Vector3(400f, 400f, 0f),
            spots, [new MeshShape("Water", 0, 6), new MeshShape("Floor", 6, 3)]);
    }

    /// <summary>An ocean's paints carry the waves in the twin the game light draws with.</summary>
    [Fact]
    public void THEPAINTSLightAnOceanWithItsWaves()
    {
        byte[]? Read(string path) => path.EndsWith("ocean.mat", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8.GetBytes(Ocean) : null;
        var paints = new MonsterModels.Paints();
        ShadeProgram? lit = paints.Shade(Read, "Art/ocean.mat").Glossy;
        Assert.NotNull(lit);
        Assert.True(lit.HasWaves);
    }

    /// <summary>The red where a point of the scene lands.</summary>
    private static byte RedAt(GamePicture picture, SkinnedMesh scene, Vector3 point)
    {
        Vector3 placed = MeshPicture.Camera.Of(scene, 0f, Down, 1f, Vector2.Zero).Place(point);
        int x = Math.Clamp((int)(placed.X * Size), 0, Size - 1);
        int y = Math.Clamp((int)(placed.Y * Size), 0, Size - 1);
        int at = ((y * Size) + x) * 4;
        Assert.True(picture.Rgba[at + 3] > 0, $"nothing drawn at {point}");
        return picture.Rgba[at];
    }

    /// <summary>A floor twenty across at height nought, and water over it in two halves: the one over negative x <paramref name="low"/> above it, the other <paramref name="high"/>. Up is minus z.</summary>
    private static SkinnedMesh Scene(float low, float high)
    {
        Vector3[] places =
        [
            new(-10f, -10f, 0f), new(10f, -10f, 0f), new(10f, 10f, 0f), new(-10f, 10f, 0f),
            new(-10f, -10f, -low), new(0f, -10f, -low), new(0f, 10f, -low), new(-10f, 10f, -low),
            new(0f, -10f, -high), new(10f, -10f, -high), new(10f, 10f, -high), new(0f, 10f, -high),
        ];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        Vector2[] spots = [.. places.Select(one => new Vector2((one.X + 10f) / 20f, (one.Y + 10f) / 20f))];
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11];
        return SkinnedMesh.Of(
            places, normals, indices, new Vector3(-10f, -10f, -Math.Max(low, high)), new Vector3(10f, 10f, 0f),
            spots, [new MeshShape("Floor", 0, 6), new MeshShape("Water", 6, 12)]);
    }

    /// <summary>A texture of one colour.</summary>
    private static Mipmaps Sheet(byte red, byte green, byte blue)
    {
        var pixels = new byte[8 * 8 * 4];
        for (var one = 0; one < 8 * 8; one++)
        {
            pixels[one * 4] = red;
            pixels[(one * 4) + 1] = green;
            pixels[(one * 4) + 2] = blue;
            pixels[(one * 4) + 3] = 255;
        }

        Mipmaps? levels = Mipmaps.Of(new GamePicture(8, 8, pixels));
        Assert.NotNull(levels);
        return levels;
    }
}
