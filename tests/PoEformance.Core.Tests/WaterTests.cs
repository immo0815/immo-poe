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

    /// <summary>The waves' slope is their height's own: worked out exactly, it agrees with the height's differences everywhere it is asked.</summary>
    [Fact]
    public void THEWAVESSlopeIsTheirHeightsOwn()
    {
        const float h = 0.05f;
        Vector2[] drifts = [Vector2.Zero, ShadeProgram.WaveDrift * 1.5f, new(-40f, 90f)];
        float[] clocks = [0f, 0.7f, 2.9f, 13.25f];
        var checkedOnes = 0;
        foreach (Vector2 drift in drifts)
        {
            foreach (float clock in clocks)
            {
                for (var x = -900f; x <= 900f; x += 137f)
                {
                    for (var y = -700f; y <= 700f; y += 151f)
                    {
                        ShadeProgram.Ripples(new Vector2(x, y), drift, clock, out Vector2 slope);
                        float dx = (ShadeProgram.Ripples(new Vector2(x + h, y), drift, clock, out _) - ShadeProgram.Ripples(new Vector2(x - h, y), drift, clock, out _)) / (2f * h);
                        float dy = (ShadeProgram.Ripples(new Vector2(x, y + h), drift, clock, out _) - ShadeProgram.Ripples(new Vector2(x, y - h), drift, clock, out _)) / (2f * h);
                        Assert.True(MathF.Abs(slope.X - dx) < 2e-3f + (0.02f * MathF.Abs(dx)), $"at ({x}, {y}) t {clock}: slope x {slope.X}, differences {dx}");
                        Assert.True(MathF.Abs(slope.Y - dy) < 2e-3f + (0.02f * MathF.Abs(dy)), $"at ({x}, {y}) t {clock}: slope y {slope.Y}, differences {dy}");
                        checkedOnes++;
                    }
                }
            }
        }

        Assert.True(checkedOnes > 1000);
    }

    /// <summary>The waves move with the clock even where nothing drifts - the engine's octaves fade in and out - and they lean, so the light sees them.</summary>
    [Fact]
    public void THEWAVESMoveWithTheClockAndLean()
    {
        var place = new Vector2(310f, -145f);
        float still = ShadeProgram.Ripples(place, Vector2.Zero, 0.3f, out _);
        Assert.NotEqual(still, ShadeProgram.Ripples(place, Vector2.Zero, 1.1f, out _));
        Assert.NotEqual(
            ShadeProgram.Ripples(place, ShadeProgram.WaveDrift * 1.5f, 1.1f, out _),
            ShadeProgram.Ripples(place, Vector2.Zero, 1.1f, out _));

        float steepest = 0f;
        for (var x = 0f; x < 2000f; x += 25f)
        {
            ShadeProgram.Ripples(new Vector2(x, x * 0.37f), ShadeProgram.WaveDrift * 1.5f, 4f, out Vector2 slope);
            steepest = MathF.Max(steepest, slope.Length());
        }

        // A FEW DEGREES AT PORT'S FLOW: steep enough for a glint to move about, not a choppy sea.
        Assert.InRange(steepest, 0.01f, 0.5f);
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
        Assert.Contains("float4 ShadeWaves(float2 place, float2 drift, float clock)", wavy, StringComparison.Ordinal);
        Assert.Contains("said.waves = r", wavy, StringComparison.Ordinal);

        // THE TURNS ARE THE PROCESSOR'S NUMBERS, to the bit.
        Assert.Contains(ShadeHlsl.Number(MathF.Cos(0.5f)), wavy, StringComparison.Ordinal);
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
