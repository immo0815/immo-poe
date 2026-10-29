using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The engine's river and standing water: their shader types drawn as the area's water on surfaces of their own - the river's
/// GetFlowSurfaceField and GetFlowingFoamColor at no flow, line for line, and standing water the ocean's ripples over no shore - lifted
/// and given a shore never, as the ocean is.
/// </summary>
public class RiverTests
{
    /// <summary>nvidian_FlowingWater.mat as the stage sweep's capture carried it (v0.1.348).</summary>
    private const string Flowing =
        """{"version":4,"graphinstances":[{"parent":"Metadata/Effects/Graphs/General/BasicColourNormalSpec.fxgraph","custom_parameters":[{"name":"base_color_texture","parameters":[{"srgb":true},{}]},{"name":"base_normalspec_texture","parameters":[{"path":"Art/Textures/Environment/forest/nvidia_normal_masked.dds","srgb":false},{}]}]}],"shadertype":"FlowingWater","splashywater":true}""";

    /// <summary>nvidian_NonsplashyFlowingWater.mat, the same capture: a shader type and nothing else.</summary>
    private const string Nonsplashy = """{"version":4,"shadertype":"NonsplashyFlowingWater"}""";

    /// <summary>nvidian_NonsplashyWater.mat, the same capture.</summary>
    private const string Standing =
        """{"version":4,"defaultgraph":{"version":3,"shader_group":["Material"]},"graphinstances":[{"parent":"Metadata/Art/Textures/Environment/forest/nvidian_NonsplashyWater.fxgraph"},{"parent":"Metadata/Art/Textures/Environment/forest/nvidian_NonsplashyWater.fxgraph"},{"parent":"Metadata/Effects/Graphs/General/BasicWater.fxgraph"}],"shadertype":"NonsplashyWater"}""";

    /// <summary>nvidian_Swamp_Water.mat, the same capture.</summary>
    private const string Swamp =
        """{"version":4,"graphinstances":[{"parent":"Metadata/Effects/Graphs/General/ForceAlpha.fxgraph"},{"parent":"Metadata/Effects/Graphs/General/BasicWater.fxgraph"}],"shadertype":"Swamp_Water","minimapcolourmultiplier":0.7200000286102295}""";

    [Fact]
    public void EACHWaterShaderTypeTakesItsKind()
    {
        Assert.Equal(WaterKind.Ocean, ShadeProgram.KindOf("Ocean"));
        Assert.Equal(WaterKind.Ocean, ShadeProgram.KindOf("water1"));
        Assert.Equal(WaterKind.River, ShadeProgram.KindOf("FlowingWater"));
        Assert.Equal(WaterKind.River, ShadeProgram.KindOf("NonsplashyFlowingWater"));
        Assert.Equal(WaterKind.Still, ShadeProgram.KindOf("NonsplashyWater"));
        Assert.Equal(WaterKind.Still, ShadeProgram.KindOf("Swamp_Water"));
        Assert.Equal(WaterKind.None, ShadeProgram.KindOf(string.Empty));
        Assert.Equal(WaterKind.None, ShadeProgram.KindOf(null));
        Assert.Equal(WaterKind.None, ShadeProgram.KindOf("Lava"));
    }

    /// <summary>
    /// The four materials the stage sweep found drawn as ordinary ones (v0.1.348: 1,120, 188, 254 and 5 rooms) compile to the engine's
    /// water, mixed, under words of their own - which make no shore, no lift and no field waves, the ocean's alone.
    /// </summary>
    [Fact]
    public void ARIVERAndStandingWaterAreDrawnAsTheAreasWaterUnderWordsOfTheirOwn()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Art/Textures/Environment/forest/nvidian_FlowingWater.mat"] = Flowing,
            ["Art/Textures/Environment/forest/nvidian_NonsplashyFlowingWater.mat"] = Nonsplashy,
            ["Art/Textures/Environment/forest/nvidian_NonsplashyWater.mat"] = Standing,
            ["Art/Textures/Environment/forest/nvidian_Swamp_Water.mat"] = Swamp,
        };
        byte[]? Read(string file) => files.TryGetValue(file, out string? text) ? Encoding.UTF8.GetBytes(text) : null;
        var paints = new MonsterModels.Paints();
        foreach ((string path, string text) in files)
        {
            WaterKind kind = ShadeProgram.KindOf(MaterialFile.Parse(text).ShaderType);
            bool river = kind == WaterKind.River;
            ShadeProgram? program = paints.Compiled(Read, path).Compiled.Program;
            Assert.NotNull(program);
            Assert.Equal([river ? ShadeProgram.RiverGraph : ShadeProgram.StillGraph], program.Graphs);
            string word = river ? MonsterModels.Paints.RiverMode : MonsterModels.Paints.StillMode;
            Assert.Equal((word, word), paints.Modes(Read, path));
            Assert.Equal(MaterialBlend.Alpha, MaterialBlends.Of(word));
            Assert.True(MonsterModels.Paints.IsWaterMode(word));
            Assert.NotEqual(MonsterModels.Paints.OceanMode, word);

            ShadeProgram glossy = program.Glossy!;
            Assert.True(glossy.HasWaves && glossy.LightsWater, path);
            Assert.False(program.Lifts || glossy.Lifts, path);
            Assert.Equal(river, glossy.Rivers);
        }

        ShadeProgram ocean = ShadeProgram.Water();
        Assert.True(ocean.Lifts && ocean.Glossy!.Lifts);
        Assert.False(ocean.Rivers);
    }

    /// <summary>
    /// GetFlowSurfaceField at no flow as water.ffx writes it, its float2x2s multiplied as HLSL multiplies them - eight octaves fading in place.
    /// </summary>
    [Fact]
    public void THERIVERSSurfaceIsGetFlowSurfaceFieldAtNoFlow()
    {
        foreach (Vector2 place in (Vector2[])[new(0f, 0f), new(137.5f, -912.25f), new(4210f, 3377f), new(-2600f, 18030f)])
        {
            foreach (float clock in (float[])[0f, 0.7f, 2.9f, 41.3f])
            {
                float height = ShadeProgram.River(place, clock, out Vector2 slope);
                float expected = FlowSurface(place, clock, out Vector2 gradient);
                Near(expected, height);
                Near(gradient.X, slope.X);
                Near(gradient.Y, slope.Y);
            }
        }
    }

    /// <summary>
    /// GetFlowingFoamColor at no flow as written, over a distortion texture: its colour white, its share the engine's at every flow_foam,
    /// and none where the texture's alpha hides it all.
    /// </summary>
    [Fact]
    public void THERIVERSFoamIsGetFlowingFoamColorAtNoFlow()
    {
        GamePicture sheet = Distortion(alpha: true);
        var covered = 0;
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                var place = new Vector2((x * 71.3f) - 400f, (y * 53.9f) + 120f);
                foreach (float clock in (float[])[0.4f, 6.1f])
                {
                    Vector4 foam = ShadeProgram.RiverFoam(sheet, place, clock, 1f);
                    Assert.Equal(new Vector3(1f, 1f, 1f), new Vector3(foam.X, foam.Y, foam.Z));
                    Near(FlowingFoam(sheet, place, clock, 1f), foam.W);
                    Assert.InRange(foam.W, 0f, 1f);
                    covered += foam.W > 0f ? 1 : 0;

                    // flow_foam moves the threshold and the turbulence both - so no more foam everywhere, but the engine's at each.
                    Near(FlowingFoam(sheet, place, clock, 0f), ShadeProgram.RiverFoam(sheet, place, clock, 0f).W);
                    Near(FlowingFoam(sheet, place, clock, 1.3f), ShadeProgram.RiverFoam(sheet, place, clock, 1.3f).W);
                }
            }
        }

        Assert.True(covered > 0, "no foam anywhere over the test's distortion");

        // THE MASK'S ALPHA NOUGHT puts its threshold at one, which nothing reaches.
        GamePicture hidden = Distortion(alpha: false);
        Assert.Equal(0f, ShadeProgram.RiverFoam(hidden, new Vector2(812f, -77f), 3.3f, 1f).W);
    }

    /// <summary>
    /// GetFlowSurfaceField at a flow as water.ffx writes it: each octave scrolled back along the flow turned a little, taller and dropping
    /// less the faster the flow.
    /// </summary>
    [Fact]
    public void THERIVERSSurfaceFlowsAsGetFlowSurfaceFieldAtAFlow()
    {
        foreach (Vector2 flow in (Vector2[])[new(40f, -12f), new(100f, 0f), new(-35.5f, 70.25f), new(0.5f, 0.25f)])
        {
            foreach (Vector2 place in (Vector2[])[new(0f, 0f), new(137.5f, -912.25f), new(4210f, 3377f)])
            {
                foreach (float clock in (float[])[0f, 0.7f, 2.9f, 41.3f])
                {
                    float height = ShadeProgram.River(place, clock, flow, out Vector2 slope);
                    float expected = FlowSurface(place, flow, clock, out Vector2 gradient);
                    Near(expected, height);
                    Near(gradient.X, slope.X);
                    Near(gradient.Y, slope.Y);
                }
            }
        }

        // AT NO FLOW it is the still river's, to the bit.
        Assert.Equal(ShadeProgram.River(new Vector2(12f, 34f), 5.5f, out Vector2 still), ShadeProgram.River(new Vector2(12f, 34f), 5.5f, Vector2.Zero, out Vector2 flowing));
        Assert.Equal(still, flowing);
    }

    /// <summary>
    /// GetFlowingFoamColor at a flow and a strength as written: two nodes of frequency mixed, each node's layers scrolled along the flow
    /// and hashed with its seed - and at no flow the still river's one node, to the bit.
    /// </summary>
    [Fact]
    public void THERIVERSFoamAtAFlowIsGetFlowingFoamColorsTwoNodes()
    {
        GamePicture sheet = Distortion(alpha: true);
        var covered = 0;
        foreach (Vector2 flow in (Vector2[])[new(3.7f, -1.1f), new(42f, 17f), new(-80f, 55f)])
        {
            foreach (float strength in (float[])[0.25f, 0.6f, 1f])
            {
                for (var at = 0; at < 40; at++)
                {
                    var place = new Vector2((at * 71.3f) - 400f, (at * 53.9f) + 120f);
                    Vector4 foam = ShadeProgram.RiverFoam(sheet, place, at * 0.37f, flow, strength);
                    Near(FlowingFoam(sheet, place, at * 0.37f, flow, strength), foam.W);
                    Assert.InRange(foam.W, 0f, 1f);
                    covered += foam.W > 0f ? 1 : 0;
                }
            }
        }

        Assert.True(covered > 0, "no foam anywhere over the test's distortion");
        float stillStrength = Math.Clamp((0.015f * 1f) + 0.25f, 0f, 1f);
        Assert.Equal(ShadeProgram.RiverFoam(sheet, new Vector2(812f, -77f), 3.3f, 1f), ShadeProgram.RiverFoam(sheet, new Vector2(812f, -77f), 3.3f, Vector2.Zero, stillStrength));
    }

    /// <summary>
    /// GetFlowingWaterPlanarStateWorld's foam strength and bubbles over a flow map as written - the curl, the speed, the speed over
    /// shallow water and the march up the flow - and with no map the still river's strength and no bubbles.
    /// </summary>
    [Fact]
    public void THEFOAMSStrengthAndBubblesAreGetFlowingWaterPlanarStateWorlds()
    {
        RiverFlowMap map = Swirl();
        var flow = new RiverFlow(map, new Vector2(300f, -200f));
        var bubbled = 0;
        for (var at = 0; at < 60; at++)
        {
            var place = new Vector2((at * 37.1f) + 50f, (at * 23.3f) + 90f);
            foreach (float depth in (float[])[2f, 40f])
            {
                foreach (float intensity in (float[])[1f, 0.15f])
                {
                    (float foam, float bubbles) = ShadeProgram.RiverFoaming(flow, place, depth, intensity, 1.3f);
                    (float expectedFoam, float expectedBubbles) = FlowingState(flow, place, depth, intensity, 1.3f, out Vector2 expectedFlow);
                    Near(expectedFoam, foam);
                    Near(expectedBubbles, bubbles);
                    Vector2 flowAt = ShadeProgram.RiverFlowAt(flow, place, intensity);
                    Near(expectedFlow.X, flowAt.X);
                    Near(expectedFlow.Y, flowAt.Y);
                    bubbled += bubbles > 0f ? 1 : 0;
                }
            }
        }

        Assert.True(bubbled > 0, "no bubbles anywhere over the swirl");
        Assert.Equal((Math.Clamp((0.015f * 1.3f) + 0.25f, 0f, 1f), 0f), ShadeProgram.RiverFoaming(null, new Vector2(10f, 20f), 5f, 1f, 1.3f));
        Assert.Equal(Vector2.Zero, ShadeProgram.RiverFlowAt(null, new Vector2(10f, 20f), 1f));
    }

    /// <summary>The area's flow_foam, the engine's 1 where its file sets none - carried to the programs in the flow register's w.</summary>
    [Fact]
    public void ANAREASFlowFoamIsReadAndHandedOn()
    {
        Assert.Equal(1f, WaterLook.EngineDefault.FlowFoam);
        Assert.Equal(1f, WaterLook.EngineDefault.FlowRegister.W);
        WaterLook vale = WaterLook.Of(EnvironmentSettings.Read("vale.env", Encoding.UTF8.GetBytes("""{ "water": { "flow_foam": 0.3, "flow_intensity": 0.15 } }""")));
        Assert.Equal(0.3f, vale.FlowFoam);
        Assert.Equal(0.3f, vale.FlowRegister.W);
        Assert.Contains("flow foam 0.3", vale.Line(), StringComparison.Ordinal);
    }

    /// <summary>The card's programs: the river's reads its own surface and foam, standing water the ocean's ripples over a shore of noughts.</summary>
    [Fact]
    public void THECARDDrawsTheRiverAndStandingWaterThroughTheirOwnFunctions()
    {
        string river = ShadeHlsl.Of(ShadeProgram.Water(WaterKind.River).Glossy!, 18);
        Assert.Contains("float4 ShadeRiver(float2 place, float clock, float2 flow)", river, StringComparison.Ordinal);
        Assert.Contains(" = ShadeRiver(r1.xy, ", river, StringComparison.Ordinal);
        Assert.Contains(" = ShadeRiverFoamAt(r1.xy, ", river, StringComparison.Ordinal);
        Assert.Contains(" = ShadeRiverBubbles(r1.xy, ", river, StringComparison.Ordinal);
        Assert.Contains("RiverFlowSheet.Load(", river, StringComparison.Ordinal);
        Assert.Contains("DistortionSheet.GetDimensions", river, StringComparison.Ordinal);
        Assert.DoesNotContain("ShadeFoam(r1", river, StringComparison.Ordinal);
        Assert.DoesNotContain("ShadeFieldAt(", river, StringComparison.Ordinal);

        string still = ShadeHlsl.Of(ShadeProgram.Water(WaterKind.Still).Glossy!, 18);
        Match waves = Regex.Match(still, @" = ShadeWaves\(r1\.xy, r14, r\d+\.xy, (r\d+), r16\);");
        Assert.True(waves.Success, "standing water's waves are not the ocean's");
        Assert.NotEqual("r15", waves.Groups[1].Value);
        Assert.DoesNotContain("ShadeRiverFoam(", still, StringComparison.Ordinal);
        Assert.DoesNotContain("ShadeFieldAt(", still, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadeProgram.Water(WaterKind.None));
    }

    /// <summary>
    /// Standing water's waves are the ocean's with no shore: the wind's ripples twice over, the swell never - whatever shore a drawing holds.
    /// </summary>
    [Fact]
    public void STANDINGWaterRipplesInTheWindWithNoSwell()
    {
        var floor = new Vector2(310f, -95f);
        float alone = ShadeProgram.Ocean(floor, WaterLook.EngineDefault.WindRegister, WaterLook.EngineDefault.SwellRegister, 2.5f, null, out Vector2 slope);
        float ripples = ShadeProgram.WindRipples(floor, WaterLook.EngineDefault.WindRegister, 2.5f, overGround: false, out Vector2 rippleSlope);
        Assert.Equal(ripples * 2f, alone, 5);
        Assert.Equal(rippleSlope * 2f, slope);
    }

    /// <summary>A two-by-two matrix as HLSL's float2x2(a, b, c, d) holds it: rows (a, b) and (c, d).</summary>
    private readonly record struct M2(float A, float B, float C, float D)
    {
        /// <summary>mul(row, matrix).</summary>
        public static Vector2 Mul(Vector2 v, M2 m) => new((v.X * m.A) + (v.Y * m.C), (v.X * m.B) + (v.Y * m.D));

        /// <summary>mul(matrix, column).</summary>
        public static Vector2 Mul(M2 m, Vector2 v) => new((m.A * v.X) + (m.B * v.Y), (m.C * v.X) + (m.D * v.Y));

        /// <summary>mul(matrix, matrix).</summary>
        public static M2 Mul(M2 m, M2 n) => new(
            (m.A * n.A) + (m.B * n.C), (m.A * n.B) + (m.B * n.D),
            (m.C * n.A) + (m.D * n.C), (m.C * n.B) + (m.D * n.D));

        public M2 Transposed => new(A, C, B, D);

        public static M2 operator *(M2 m, float s) => new(m.A * s, m.B * s, m.C * s, m.D * s);

        public static M2 Rotation(float angle) => new(MathF.Cos(angle), MathF.Sin(angle), -MathF.Sin(angle), MathF.Cos(angle));
    }

    /// <summary>water.ffx's GetFlowSurfaceField(planar_pos, flow_intensity, time) with flow_intensity nought, statement for statement.</summary>
    private static float FlowSurface(Vector2 planar, float time, out Vector2 gradient) => FlowSurface(planar, Vector2.Zero, time, out gradient);

    /// <summary>water.ffx's GetFlowSurfaceField(planar_pos, flow_intensity, time), statement for statement.</summary>
    private static float FlowSurface(Vector2 planar, Vector2 flowIntensity, float time, out Vector2 gradient)
    {
        const int octavesCount = 4;
        M2 deltaRotation = M2.Rotation(0.5f);
        float octaveAmp = 0.3f * (flowIntensity.Length() + 3f) * 1f;
        float octaveSpeed = 1f;
        M2 octaveMatrix = new M2(1f, 0f, 0f, 1f) * 0.01f;
        float ampDrop = Lerp(0.2f, 0.9f, Math.Clamp(flowIntensity.Length() / 300f, 0f, 1f));
        float value = 0f;
        gradient = Vector2.Zero;
        for (var i = 0; i < octavesCount * 2; i++)
        {
            float refreshPeriod = 3f / octaveSpeed;
            float timePhase = time / refreshPeriod;
            float octaveRatio = (i / 2) / (octavesCount - 1 + 1e-3f);
            float octavePhase = Frac(timePhase + octaveRatio + ((i % 2) * 0.5f));
            float octaveOpacity = octavePhase * (1f - octavePhase) * 4f;
            float offsetAngle = Lerp(-0.3f, 0.3f, octaveRatio);
            Vector2 scrollOffset = M2.Mul(-flowIntensity, M2.Rotation(offsetAngle)) * octavePhase * octaveSpeed * refreshPeriod;
            Vector2 q = M2.Mul(planar + scrollOffset, octaveMatrix);
            float octave = ShadeProgram.Ripple(q.X, q.Y, out float gx, out float gy);
            value += octave * octaveAmp * octaveOpacity;
            gradient += M2.Mul(new Vector2(gx, gy), octaveMatrix.Transposed) * octaveAmp * octaveOpacity;
            float harmonicChange = i % 2;
            octaveMatrix = M2.Mul(octaveMatrix, deltaRotation) * Lerp(1f, 2f, harmonicChange);
            octaveAmp *= Lerp(1f, ampDrop, harmonicChange);
            octaveSpeed *= Lerp(1f, 1.3f, harmonicChange);
        }

        return value;
    }

    /// <summary>
    /// GetFlowingWaterPlanarStateWorld's foam at no flow - its strength, then GetFlowingFoamColor and its one node - statement for
    /// statement, its share alone.
    /// </summary>
    private static float FlowingFoam(GamePicture sheet, Vector2 planar, float time, float flowFoam)
    {
        // The march's first step with the sample nought: (1 - (0 - 100) * 0.02) * (0 * 0.1 + 0.05) * exp(0) * 0.1.
        float foamIntensity = Math.Clamp((((1f - ((0f - 100f) * 0.02f)) * ((0f * 0.1f) + 0.05f) * MathF.Exp(0f) * 0.1f) * flowFoam) + 0.25f, 0f, 1f);

        // GetLinearInterpNodes(0): the first node whole, at 0.2 a second, its seed nought.
        return FlowingFoamNode(sheet, planar, Vector2.Zero, time, foamIntensity, 0.2f, 0f);
    }

    /// <summary>GetFlowingFoamColor(planar_pos, flow_intensity, time, foam_intensity) as written, its share alone.</summary>
    private static float FlowingFoam(GamePicture sheet, Vector2 planar, float time, Vector2 flowIntensity, float foamIntensity)
    {
        float freq = 0.02f * flowIntensity.Length();
        const float freqSecondary = 10.01f;
        float global = freq * freqSecondary;
        Vector2 phases = new(Frac(0.5f + (global * 0.5f)), Frac(0f + (global * 0.5f)));
        Vector2 seeds = new((MathF.Floor(0.5f + (global * 0.5f)) * 2f) + 0f, (MathF.Floor(0f + (global * 0.5f)) * 2f) + 1f);
        Vector2 weights = new(MathF.Min(phases.X, 1f - phases.X) * 2f, MathF.Min(phases.Y, 1f - phases.Y) * 2f);
        float result = 0f;
        for (var node = 0; node < 2; node++)
        {
            float seed = node == 0 ? seeds.X : seeds.Y;
            float weight = node == 0 ? weights.X : weights.Y;
            result += FlowingFoamNode(sheet, planar, flowIntensity, time, foamIntensity, 0.2f + (seed / freqSecondary), seed) * weight;
        }

        return result;
    }

    /// <summary>GetFlowingFoamColorFixedFreq(planar_pos, flow_intensity, time, foam_intensity, phase_freq, layer_seed) as written, its share alone.</summary>
    private static float FlowingFoamNode(GamePicture sheet, Vector2 planar, Vector2 flowIntensity, float time, float foamIntensity, float phaseMult, float layerSeed)
    {
        const int harmonicsCount = 2;
        M2 turbulenceMat = M2.Rotation(0.5f), distortionMat = M2.Rotation(2f), maskMat = M2.Rotation(-1f);
        float foamA = 0f, maskA = 0f, sumWeight = 0f;
        for (var layerIndex = 0; layerIndex < harmonicsCount * 2; layerIndex++)
        {
            float harmonicIndex = layerIndex / 2;
            float phaseScramble = Read(sheet, planar * 0.0005f * MathF.Pow(2f, harmonicIndex)).X * 0.2f;
            float sublayerIndex = layerIndex % 2;
            float freqMult = harmonicIndex == 0f ? 1f : 1.8f;
            float phase = Frac((time * phaseMult) + (sublayerIndex * 0.5f) + (0.5f * harmonicIndex / harmonicsCount) + phaseScramble);
            float opacity = MathF.Min(phase, 1f - phase) * 2f;
            Vector2 scrolledPos = planar - (flowIntensity * 1.5f * (phase - 0.5f) / phaseMult);
            Vector3 hash = Hash33(new Vector3(layerIndex, layerSeed, 0f));
            Vector2 basePos = (scrolledPos * 0.01f * freqMult) + new Vector2(hash.X, hash.Y);
            float turbulenceRatio = 1f * phase;
            Vector4 turbulence = Read(sheet, M2.Mul(turbulenceMat, basePos / 7f));
            basePos += (new Vector2(turbulence.X, turbulence.Y) - new Vector2(0.5f)) * 2f * turbulenceRatio / phaseMult * 0.2f
                * (0f + (10f * Math.Clamp(foamIntensity - 0.2f, 0f, 1f)));
            Vector4 distortion = Read(sheet, M2.Mul(distortionMat, basePos / 10f));
            basePos += (new Vector2(distortion.X, distortion.Y) - new Vector2(0.5f)) * 100f;
            Vector4 mask = Read(sheet, M2.Mul(maskMat, basePos) * 0.03f);
            float baseNoise = (1f - (((MathF.Sin(basePos.X * 2.7f) * 0.5f) + 0.5f) * ((MathF.Sin(basePos.Y * 2.3f) * 0.5f) + 0.5f))) * 0.3f;
            sumWeight += opacity;
            foamA += Math.Clamp(1f - baseNoise, 0f, 1f) * opacity;
            maskA += mask.W * opacity;
        }

        foamA /= sumWeight;
        maskA /= sumWeight;
        float maskThreshold = Math.Clamp((1f - maskA - Lerp(0.4f, 0.4f, foamIntensity)) * 3f, 0f, 1f);
        foamA = Math.Clamp((foamA - maskThreshold) / (1.3f - maskThreshold) * 1.3f, 0f, 1f);
        float threshold = Lerp(1f, 0.3f, foamIntensity);
        return Math.Clamp((foamA - threshold) / (1.4f - threshold) * 1.4f, 0f, 1f);
    }

    /// <summary>
    /// GetFlowingWaterPlanarStateWorld's foam_intensity and bubbleRatio over GetFlowSample, statement for statement - the flow it takes
    /// too, its flow_intensity.
    /// </summary>
    private static (float Foam, float Bubbles) FlowingState(RiverFlow flow, Vector2 planar, float gBufferDelta, float waterFlowIntensity, float waterFlowFoam, out Vector2 flowIntensity)
    {
        Vector4 Sample(Vector2 at) => FlowSample(flow, at);
        Vector4 flowSample = Sample(planar);
        float speedMult = 2f * waterFlowIntensity;
        flowIntensity = new Vector2(flowSample.X, flowSample.Y) * speedMult;
        float flowLength = flowIntensity.Length();
        flowIntensity *= MathF.Min(flowLength, 100f) / (flowLength + 1e-3f);
        const float eps = 100.1f;
        Vector4 xGradient = (Sample(planar + new Vector2(eps, 0f)) - Sample(planar + new Vector2(-eps, 0f))) / eps;
        Vector4 yGradient = (Sample(planar + new Vector2(0f, eps)) - Sample(planar + new Vector2(0f, -eps))) / eps;
        float foamCurl = xGradient.Y - yGradient.X;
        float foamIntensity = 0f;
        foamIntensity += Math.Clamp(MathF.Abs(foamCurl) * speedMult * 0.5f, 0f, 1f);
        foamIntensity += flowIntensity.Length() * 0.005f;
        foamIntensity += flowIntensity.Length() * Math.Clamp(1f / (gBufferDelta + 5f) * 25f, 0f, 1f) * 0.01f;
        float bubbleRatio = Math.Clamp(MathF.Max(0f, foamIntensity * foamIntensity) * 0.1f, 0f, 1f);
        const float distOffset = -100f;
        Vector2 currPos = planar;
        const float step = 0.5f;
        const int count = 25;
        for (var i = 0; i < count; i++)
        {
            Vector4 sample = Sample(currPos);
            Vector2 flowSpeed = new Vector2(sample.X, sample.Y) * speedMult;
            Vector2 delta = -flowSpeed * step;
            float localIntensity = (1f - ((sample.W + distOffset) * 0.02f)) * ((flowSpeed.Length() * 0.1f) + 0.05f) * MathF.Exp(-(float)i / count * 2f) * 0.1f;
            foamIntensity = MathF.Max(foamIntensity, localIntensity);
            currPos += delta;
        }

        foamIntensity *= waterFlowFoam;
        foamIntensity += 0.25f;
        return (Math.Clamp(foamIntensity, 0f, 1f), bubbleRatio);
    }

    /// <summary>GetFlowSample: the map read at (pos - aabb.xy) / (aabb.zw - aabb.xy) through SamplerLinearWrap at level nought.</summary>
    private static Vector4 FlowSample(RiverFlow flow, Vector2 pos)
    {
        RiverFlowMap map = flow.Map;
        Vector2 least = new Vector2(-0.3f * (1250f / 23f)) - flow.Origin;
        Vector2 size = new(map.Width * (1250f / 23f), map.Height * (1250f / 23f));
        Vector2 uv = (pos - least) / size;
        float sx = (uv.X * map.Width) - 0.5f, sy = (uv.Y * map.Height) - 0.5f;
        int x0 = (int)MathF.Floor(sx), y0 = (int)MathF.Floor(sy);
        float fx = sx - x0, fy = sy - y0;
        Vector4 At(int x, int y) => map.Texel4(((x % map.Width) + map.Width) % map.Width, ((y % map.Height) + map.Height) % map.Height);
        return Vector4.Lerp(Vector4.Lerp(At(x0, y0), At(x0 + 1, y0), fx), Vector4.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx), fy);
    }

    /// <summary>A test flow map: a swirl round the middle, inside banks a radius off it, flowing faster outwards.</summary>
    private static RiverFlowMap Swirl()
    {
        const int width = 20, height = 16;
        var texels = new float[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int at = ((y * width) + x) * 4;
                float dx = x - 10f, dy = y - 8f;
                float bank = (6f - MathF.Sqrt((dx * dx) + (dy * dy))) * (1250f / 23f);
                texels[at] = bank >= 0f ? -dy * 12f : 0f;
                texels[at + 1] = bank >= 0f ? dx * 12f : 0f;
                texels[at + 2] = x == 10 && y == 8 ? 1.5f : 0f;
                texels[at + 3] = bank;
            }
        }

        return new RiverFlowMap(width, height, texels, "a test swirl");
    }

    /// <summary>SamplerLinearWrap at level nought, the texels a fraction of 255.</summary>
    private static Vector4 Read(GamePicture sheet, Vector2 uv)
    {
        float sx = (uv.X * sheet.Width) - 0.5f, sy = (uv.Y * sheet.Height) - 0.5f;
        int x0 = (int)MathF.Floor(sx), y0 = (int)MathF.Floor(sy);
        float fx = sx - x0, fy = sy - y0;
        Vector4 At(int x, int y)
        {
            int wx = ((x % sheet.Width) + sheet.Width) % sheet.Width, wy = ((y % sheet.Height) + sheet.Height) % sheet.Height;
            int at = ((wy * sheet.Width) + wx) * 4;
            return new Vector4(sheet.Rgba[at], sheet.Rgba[at + 1], sheet.Rgba[at + 2], sheet.Rgba[at + 3]) / 255f;
        }

        return Vector4.Lerp(Vector4.Lerp(At(x0, y0), At(x0 + 1, y0), fx), Vector4.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx), fy);
    }

    /// <summary>util.hlsl's hash33.</summary>
    private static Vector3 Hash33(Vector3 p3)
    {
        p3 = new Vector3(Frac(p3.X * 0.1031f), Frac(p3.Y * 0.1030f), Frac(p3.Z * 0.0973f));
        float dotted = Vector3.Dot(p3, new Vector3(p3.Y, p3.X, p3.Z) + new Vector3(19.19f));
        p3 += new Vector3(dotted);
        return new Vector3(Frac((p3.X + p3.Y) * p3.Z), Frac((p3.X + p3.X) * p3.Y), Frac((p3.Y + p3.X) * p3.X));
    }

    /// <summary>A seamless test distortion: red and green waves round the texture, alpha high where shown, nought where hidden.</summary>
    private static GamePicture Distortion(bool alpha)
    {
        const int side = 32;
        var rgba = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                int at = ((y * side) + x) * 4;
                float u = MathF.Tau * x / side, v = MathF.Tau * y / side;
                rgba[at] = (byte)(128 + (100 * MathF.Cos(u + (2f * v))));
                rgba[at + 1] = (byte)(128 + (100 * MathF.Sin((3f * u) - v)));
                rgba[at + 2] = 128;
                rgba[at + 3] = alpha ? (byte)(200 + (55 * MathF.Cos(u - v))) : (byte)0;
            }
        }

        return new GamePicture(side, side, rgba);
    }

    /// <summary>Two numbers the same to a ten-thousandth of the larger, or of one.</summary>
    private static void Near(float expected, float actual)
        => Assert.True(MathF.Abs(expected - actual) <= 1e-4f * MathF.Max(1f, MathF.Abs(expected)), $"{actual} where {expected} was worked out");

    private static float Lerp(float from, float to, float share) => from + ((to - from) * share);

    private static float Frac(float x) => x - MathF.Floor(x);
}
