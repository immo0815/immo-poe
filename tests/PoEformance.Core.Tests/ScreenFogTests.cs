using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The Red Vale's screen-space fog, compiled from its own files (tests/fixtures/fog: the player's environment attachment, its pass's material
/// and both graphs, from v0.1.380's capture) and held against the engine's group and nodes written out here apart from the compiler.
/// </summary>
public class ScreenFogTests
{
    private const string FogGraph = "Metadata/Effects/Graphs/Environment/ScreenspaceFogFlat.fxgraph";

    [Fact]
    public void THEREDVALESFogCompilesWholeFromItsOwnFiles()
    {
        ShadeCompile compiled = ShadeProgram.CompileScreen(Chain());

        Assert.True(compiled.Program is not null, string.Join(" | ", compiled.Skipped));
        Assert.Empty(compiled.Skipped);
        Assert.True(compiled.Program.Screen);
        Assert.Equal([FogGraph], compiled.Program.Graphs);
        Assert.Equal(
            ["Art/particles/FXLib/tileables/16bit/flow_muddle_blur_01_R16G16.dds", "Art/particles/environment_effects/Act1_FOUR/g1_1/Textures/gfx_fog/BakedFog_Uncompressed.dds"],
            compiled.Program.Textures.Select(one => one.Path).OrderBy(one => one, StringComparer.Ordinal));
        Assert.All(compiled.Program.Textures, one => Assert.False(one.Srgb));
        Assert.True(compiled.Program.UsesTime);
    }

    /// <summary>
    /// The compiled fog at many pixels, held against the engine's code written out here apart from the compiler: the graph's links with the
    /// material's values, the group's HLSL, and the fragments' own - their hashes, nodes and reads each written again.
    /// </summary>
    [Fact]
    public void THEFOGRunsAsTheEnginesGroupAndNodesWriteIt()
    {
        ShadeProgram program = Bound(out Mipmaps flowMap, out Mipmaps fogTexture);
        var registers = new Vector4[program.Registers];
        var compared = 0;
        var covered = 0;
        foreach (float time in (ReadOnlySpan<float>)[0f, 7.3f, 61.25f, 333.9f])
        {
            foreach (ScreenPixel pixel in Pixels())
            {
                Vector4 compiled = program.RunScreen(registers, time, pixel);
                Vector4 engine = Engine(pixel, time, flowMap, fogTexture);
                Assert.True(
                    Close(compiled, engine),
                    $"at {pixel.Surface} from {pixel.Camera}, {time} s: compiled {compiled}, the engine's {engine}");
                compared++;
                covered += engine.W > 0.02f && engine.X > 0.01f ? 1 : 0;
            }
        }

        // NOT A COMPARISON OF NOTHINGS: most pixels look through the fog.
        Assert.Equal(4 * Pixels().Count, compared);
        Assert.True(covered > compared / 3, $"{covered} of {compared} covered");
    }

    /// <summary>The fog is no flat colour: it thins round the player, thickens with the path through it, and moves with the clock.</summary>
    [Fact]
    public void THEFOGThinsRoundThePlayerAndThickensAlongItsPath()
    {
        ShadeProgram program = Bound(out _, out _);
        var registers = new Vector4[program.Registers];
        var camera = new Vector3(0f, 0f, -2200f);
        ScreenPixel At(Vector3 surface, Vector3 player)
            => new(camera, surface, new Vector2(10.5f, 20.5f), Fog, Shape, player, new Vector3(1.5f, 1.4f, 1.2f), new Vector3(0.3f, 0.32f, 0.35f));

        // FAR FROM THE PLAYER the fog covers; at his feet the feathering takes 0.35 of every layer's density away.
        float far = program.RunScreen(registers, 3f, At(new Vector3(4000f, 4000f, -50f), new Vector3(0f, 0f, -50f))).W;
        float near = program.RunScreen(registers, 3f, At(new Vector3(4000f, 4000f, -50f), new Vector3(4000f, 4000f, -50f))).W;
        Assert.True(far > near, $"far {far}, near {near}");

        // A SURFACE ABOVE THE FOG'S TOP (z -400, up is minus z) has nothing between it and the eye.
        Assert.Equal(0f, program.RunScreen(registers, 3f, At(new Vector3(3000f, 3000f, -600f), new Vector3(0f, 0f, 0f))).W, 6);
    }

    /// <summary>The loop's count is the material's constant; a count from anything else is refused and named.</summary>
    [Fact]
    public void AGROUPWhoseCountIsNoConstantIsRefusedAndNamed()
    {
        IReadOnlyList<(ShaderInstance Instance, ShaderGraph Graph)> chain = Chain();
        string text = File.ReadAllText(Path.Combine(ComponentSweepTests.Fixture("fog"), "ScreenspaceFogFlat.fxgraph")).Replace("\r\n", "\n", StringComparison.Ordinal);

        // THE COUNT FROM THE CLOCK: the link into layers_count moved from the ConstantInt to Time #2.
        string moved = text.Replace(
            "\"type\": \"ConstantInt\",\n                \"index\": 0,\n                \"variable\": \"output\"",
            "\"type\": \"Time\",\n                \"index\": 2,\n                \"variable\": \"output\"",
            StringComparison.Ordinal);
        Assert.NotEqual(text, moved);
        ShadeCompile compiled = ShadeProgram.CompileScreen([chain[0], (chain[1].Instance, ShaderGraph.Read(System.Text.Encoding.UTF8.GetBytes(moved)))]);

        Assert.Null(compiled.Program);
        Assert.Contains(compiled.Skipped, one => one.Contains("ScreenspaceFogFlatRaymarchGroup with a layers_count that is not a constant", StringComparison.Ordinal));
    }

    /// <summary>A node only a screen pass hands its value is refused on a mesh's surface, as it was before it was known - never read as nought.</summary>
    [Fact]
    public void ASCREENPassesNodeOnAMeshsSurfaceIsRefused()
    {
        ShaderGraph graph = ShaderGraph.Read(System.Text.Encoding.UTF8.GetBytes("""
            {
                "nodes": [
                    { "type": "CameraPos", "index": 0 },
                    { "type": "AlbedoColor", "index": 0, "stage": "Texturing" }
                ],
                "links": [
                    { "src": { "type": "CameraPos", "index": 0, "variable": "cam_pos" }, "dst": { "type": "AlbedoColor", "index": 0, "stage": "Texturing", "variable": "input", "swizzle": "xyz" } }
                ]
            }
            """));
        ShadeCompile compiled = ShadeProgram.Compile([(new ShaderInstance("Metadata/Test.fxgraph", new Dictionary<string, ShaderValue[]>()), graph)]);

        Assert.Null(compiled.Program);
        Assert.Contains(compiled.Skipped, one => one.Contains("CameraPos on a mesh's surface", StringComparison.Ordinal));
    }

    /// <summary>GetBayerNoise16 over a four by four block of pixel middles is the matrix, each centred in its sixteenth.</summary>
    [Fact]
    public void THEBAYERNoiseIsTheEnginesMatrixOverTheBlock()
    {
        int[] matrix = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                float said = ShadeProgram.Bayer(new Vector2(x + 0.5f, y + 0.5f));
                Assert.Equal((matrix[(x % 4) + ((y % 4) * 4)] + 0.5f) / 16f, said);
            }
        }
    }

    /// <summary>The card's HLSL of the fog: the pass's values, the omni light and the linear colour a screen program hands on.</summary>
    [Fact]
    public void THEFOGSCardProgramReadsThePassAndHandsOnItsOwnLight()
    {
        ShadeProgram program = ShadeProgram.CompileScreen(Chain()).Program!;
        string hlsl = ShadeHlsl.Of(program, 18);

        Assert.Contains("ShadeScreenCamera", hlsl, StringComparison.Ordinal);
        Assert.Contains("ShadeScreenSurface", hlsl, StringComparison.Ordinal);
        Assert.Contains("ShadeBayer(", hlsl, StringComparison.Ordinal);
        Assert.Contains("ShadeHashInt3(int3(", hlsl, StringComparison.Ordinal);
        Assert.Contains("ShadeOmni(", hlsl, StringComparison.Ordinal);
        Assert.Contains("said.colour = colour.xyz;", hlsl, StringComparison.Ordinal);
        Assert.DoesNotContain("said.colour = float3(SrgbOf(", hlsl, StringComparison.Ordinal);
    }

    /// <summary>
    /// The way from the area's .env to the program: its fog keys read, its attachment's render pass found in the .ao's AnimatedRender payload, the
    /// pass's material compiled - and a texture the install does not give back named, never drawn as nothing.
    /// </summary>
    [Fact]
    public void THEREDVALESEnvironmentNamesItsFogAndItsAttachmentsPassCompiles()
    {
        const string environment = """
            {
              "screenspace_fog": { "ssf_color": [ 0.13898, 0.10869, 0.11924 ], "ssf_color_alpha": 0.1, "ssf_layer_count": 5.0, "ssf_disperse_radius": 1300.0, "ssf_feathering": 0.34999 },
              "area": { "player_environment_ao": "Metadata/Effects/weather_attachments/act1/Red_Vale/Red_Vale.ao" }
            }
            """;
        EnvironmentSettings settings = EnvironmentSettings.Read("Metadata/EnvironmentSettings/Gallows/1_5.env", System.Text.Encoding.UTF8.GetBytes(environment));
        Assert.Equal(Fog, settings.FogColour);
        Assert.Equal(Shape, settings.FogShape);
        Assert.Equal("Metadata/Effects/weather_attachments/act1/Red_Vale/Red_Vale.ao", settings.PlayerEnvironmentAo);

        // THE FIXTURES BY THEIR FILE NAMES, the textures not among them.
        string folder = ComponentSweepTests.Fixture("fog");
        var asked = new List<string>();
        byte[]? Read(string path)
        {
            asked.Add(path);
            string file = Path.Combine(folder, Path.GetFileName(path));
            return Path.GetExtension(path) is ".ao" or ".mat" or ".fxgraph" && File.Exists(file) ? File.ReadAllBytes(file) : null;
        }

        ScreenFog fog = ScreenFog.Of(Read, settings);

        Assert.False(fog.Ready);
        Assert.Contains("Metadata/Effects/weather_attachments/act1/Red_Vale/Red_Vale_ao_pass0.mat", asked);
        Assert.Contains(FogGraph, asked);
        Assert.Contains(fog.Said, one => one.Contains("Red_Vale_ao_pass0.mat's texture", StringComparison.Ordinal) && one.Contains("did not read", StringComparison.Ordinal));
    }

    /// <summary>
    /// light-extras' replay reads the attachment's files the capture wrote whole (section 7) and compiles its pass again - the fog held against
    /// today's compiler from a capture alone.
    /// </summary>
    [Fact]
    public void THELIGHTExtrasReplayCompilesTheAttachmentsPassFromTheFilesWrittenWhole()
    {
        string folder = ComponentSweepTests.Fixture("fog");
        var text = new System.Text.StringBuilder();
        text.AppendLine("=== 7. the player's environment attachment");
        text.AppendLine("  attach\tMetadata/Effects/weather_attachments/act1/Red_Vale/Red_Vale_ao_pass0.mat\t3855\t1\tMetadata/Effects/weather_attachments/act1/Red_Vale/Red_Vale.ao\ttext");
        text.AppendLine("  shapefile\tArt/Textures/Environment/Fogs/SSTexturec.mat\t512\t0\ta shape of the model\ttext");
        foreach ((string path, string file) in (ReadOnlySpan<(string, string)>)[
            ("Metadata/Effects/weather_attachments/act1/Red_Vale/Red_Vale_ao_pass0.mat", "Red_Vale_ao_pass0.mat"),
            ("Metadata/Effects/Graphs/General/ScreenspaceQuad.fxgraph", "ScreenspaceQuad.fxgraph"),
            (FogGraph, "ScreenspaceFogFlat.fxgraph")])
        {
            text.AppendLine().Append("##### ").Append(path).AppendLine("  (hop 1)");
            foreach (string line in File.ReadAllText(Path.Combine(folder, file)).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd().Split('\n'))
            {
                text.Append("  | ").AppendLine(line);
            }
        }

        string replayed = PoEformance.Features.CaptureLightExtras.Replay(text.ToString());

        Assert.Contains("shape's file Art/Textures/Environment/Fogs/SSTexturec.mat", replayed, StringComparison.Ordinal);
        Assert.Contains("material Metadata/Effects/weather_attachments/act1/Red_Vale/Red_Vale_ao_pass0.mat: compiled as a screen program", replayed, StringComparison.Ordinal);
        Assert.Contains("BakedFog_Uncompressed.dds", replayed, StringComparison.Ordinal);
    }

    /// <summary>An environment naming no attachment has no fog, and says so.</summary>
    [Fact]
    public void ANENVIRONMENTWithoutAnAttachmentHasNoFog()
    {
        EnvironmentSettings settings = EnvironmentSettings.Read("plain.env", System.Text.Encoding.UTF8.GetBytes("""{ "area": { "dust_color": [ 0.5, 0.5, 0.5 ] } }"""));
        ScreenFog fog = ScreenFog.Of(_ => null, settings);

        Assert.Null(settings.FogColour);
        Assert.Null(settings.FogShape);
        Assert.False(fog.Ready);
        Assert.Contains("names no player_environment_ao", fog.Said[0], StringComparison.Ordinal);
    }

    /// <summary>The Red Vale's .env: ssf_color and its alpha, the disperse radius and the feathering.</summary>
    private static readonly Vector4 Fog = new(0.13898f, 0.10869f, 0.11924f, 0.1f);

    private static readonly Vector2 Shape = new(1300f, 0.34999f);

    /// <summary>The compiled fog with two textures of its own, smooth enough that a read a texel apart reads nearly alike.</summary>
    private static ShadeProgram Bound(out Mipmaps flowMap, out Mipmaps fogTexture)
    {
        ShadeProgram program = ShadeProgram.CompileScreen(Chain()).Program!;
        flowMap = Texture(32, (x, y) => (128 + (100 * MathF.Sin(x * 0.39f)), 128 + (90 * MathF.Cos(y * 0.27f)), 40, 255));
        fogTexture = Texture(64, (x, y) => (60, 90, 128 + (110 * MathF.Sin((x * 0.2f) + (y * 0.13f)) * MathF.Cos(y * 0.11f)), 200));
        Mipmaps flows = flowMap, fogs = fogTexture;
        return program.With([.. program.Textures.Select(one => one.Path.Contains("flow_muddle", StringComparison.Ordinal) ? flows : fogs)]);
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

    /// <summary>Pixels over a stage: the eye high above looking down and across, surfaces under the fog, in it and above it, the player near and far.</summary>
    private static List<ScreenPixel> Pixels()
    {
        var said = new List<ScreenPixel>();
        Vector3[] cameras = [new(-1500f, -1500f, -1900f), new(500f, -2500f, -900f), new(0f, 0f, -150f)];
        Vector3[] surfaces = [new(0f, 0f, 0f), new(800f, 300f, 30f), new(-200f, 900f, -120f), new(2500f, -1800f, -380f), new(40f, 60f, -700f), new(5000f, 5200f, 400f)];
        Vector3[] players = [new(0f, 0f, 0f), new(900f, 400f, -20f), new(-3000f, 0f, 0f)];
        var pixel = 0;
        foreach (Vector3 camera in cameras)
        {
            foreach (Vector3 surface in surfaces)
            {
                foreach (Vector3 player in players)
                {
                    pixel++;
                    said.Add(new ScreenPixel(
                        camera, surface, new Vector2((pixel * 7) + 0.5f, (pixel * 3) + 0.5f), Fog, Shape, player,
                        new Vector3(1.556f, 1.43f, 1.312f), new Vector3(0.21f, 0.24f, 0.3f)));
                }
            }
        }

        return said;
    }

    private static bool Close(Vector4 a, Vector4 b)
    {
        Vector4 gap = Vector4.Abs(a - b);
        Vector4 size = Vector4.Max(Vector4.Abs(a), Vector4.Abs(b));
        return gap.X <= 1e-4f + (1e-4f * size.X) && gap.Y <= 1e-4f + (1e-4f * size.Y) && gap.Z <= 1e-4f + (1e-4f * size.Z) && gap.W <= 1e-4f + (1e-4f * size.W);
    }

    /// <summary>
    /// ScreenspaceFogFlat.fxgraph with the Red Vale's material over it, written from its links, and ScreenspaceFogFlatRaymarchGroup's HLSL -
    /// the processor's reads at their top level but the graph's own level, the light handed in whole.
    /// </summary>
    private static Vector4 Engine(in ScreenPixel pixel, float time, Mipmaps flowMap, Mipmaps fogTexture)
    {
        // OUTSIDE THE GROUP: "Rise speed" 0.01 of the clock; the material's "Height range" (0, 400) and "Layers count" 5; "Use env settings"
        // on, so the bottom colour is ssf_color times "Env fog bottom color mult" (0.5, 0.5, 0.5, 6) and the top ssf_color, the radii the
        // disperse radius's tenth and itself, the fade the feathering.
        float verticalPhase = time * 0.01f;
        var heightRange = new Vector2(0f, 400f);
        float noise = Bayer16(pixel.Pixel);
        Vector4 bottomColour = pixel.FogColour * new Vector4(0.5f, 0.5f, 0.5f, 6f);
        Vector4 topColour = pixel.FogColour;
        float bottomRadius = pixel.FogShape.X * 0.1f, topRadius = pixel.FogShape.X;
        float fadeAmount = pixel.FogShape.Y;
        int layersCount = 5;
        _ = noise;

        // THE PROLOGUE.
        Vector3 start = pixel.Camera, end = pixel.Surface;
        Vector3 delta = end - start;
        float invDeltaZ = MathF.Abs(delta.Z) > 1e-7f ? 1f / delta.Z : 1e7f;
        float heightScaleStart = (-heightRange.X - start.Z) * invDeltaZ;
        float heightScaleEnd = (-heightRange.Y - start.Z) * invDeltaZ;
        layersCount = Math.Max(1, layersCount);
        float globalPhase = (verticalPhase * layersCount) + 0.5f;
        int startLayerIndex = -(int)MathF.Floor(globalPhase);
        float layerPhase = globalPhase - MathF.Floor(globalPhase);
        float invLayersCount = 1f / Math.Max(1, layersCount);
        float baseLayerPath = MathF.Abs(heightRange.Y - heightRange.X) * invLayersCount;
        var radiance = new Vector4(0f, 0f, 0f, 1f);
        for (var loop = 0; loop < layersCount; loop++)
        {
            // THE INNER PROLOGUE.
            int layerSeed = loop + startLayerIndex;
            float layerRatio = (loop + layerPhase) * invLayersCount;
            float ratioEnd = layerRatio - (1f * invLayersCount);
            float scaleStart = Saturate(Mix(heightScaleStart, heightScaleEnd, layerRatio));
            float scaleEnd = Saturate(Mix(heightScaleStart, heightScaleEnd, ratioEnd));
            Vector3 worldStart = start + ((end - start) * scaleStart);
            Vector3 worldEnd = start + ((end - start) * scaleEnd);
            float lengthRatio = (worldStart - worldEnd).Length() / baseLayerPath;

            // THE INSIDE: the fog texture flowing over the layer's first point, thinned round the player, coloured and lit.
            var p = new Vector2(worldStart.X, worldStart.Y);
            Vector2 fogUv = (p * 1.3f * 0.001f) + (Vector2.Zero * time);
            Vector3 hash = HashInts(layerSeed, 0, 0);
            Vector2 flowUv = new Vector2(hash.X, hash.Y) + (p * 1.6f * 0.001f);
            Vector4 flowTexel = Bilinear(flowMap[0], flowUv);
            Vector2 flow = (new Vector2((2f * flowTexel.X) - 1f, (2f * flowTexel.Y) - 1f) * 0.03f) / 0.1f;
            flow += new Vector2(Mix(-0.45f, 0.562f, layerRatio), Mix(0f, 0f, layerRatio));
            float phase = hash.Z + (time * 0.1f);
            Vector4 read = Flowing(fogTexture, fogUv, phase, layerSeed, 0f, flow, 0f);
            float density = Saturate(read.Z);
            float distance = (p - new Vector2(pixel.Player.X, pixel.Player.Y)).Length();
            float fade = fadeAmount * (1f - Saturate(distance / Mix(bottomRadius, topRadius, layerRatio)));
            float alpha = Saturate(density - fade);
            var colour = new Vector4(
                Mix(bottomColour.X, topColour.X, layerRatio), Mix(bottomColour.Y, topColour.Y, layerRatio),
                Mix(bottomColour.Z, topColour.Z, layerRatio), Mix(bottomColour.W, topColour.W, layerRatio) * alpha);
            Vector3 light = pixel.Direct + (pixel.Environment * 1f);
            var layerColour = new Vector4(new Vector3(colour.X, colour.Y, colour.Z) * light, colour.W * 0.2f);

            // THE INNER EPILOGUE.
            float layerFade = 1f;
            if (loop == 0)
            {
                layerFade *= Saturate(2f * layerPhase);
            }

            if (loop == layersCount - 1)
            {
                layerFade *= Saturate(2f * (1f - layerPhase));
            }

            float throughput = MathF.Exp(-layerColour.W * layerFade * lengthRatio);
            radiance = new Vector4(
                (new Vector3(radiance.X, radiance.Y, radiance.Z) * throughput) + (new Vector3(layerColour.X, layerColour.Y, layerColour.Z) * (1f - throughput)),
                radiance.W * throughput);
        }

        // THE EPILOGUE, and OutPixelColor's (res.rgb / max(res.a, Epsilon), res.a).
        var resColour = new Vector4(radiance.X, radiance.Y, radiance.Z, 1f - radiance.W);
        float under = MathF.Max(resColour.W, 1e-4f);
        return new Vector4(resColour.X / under, resColour.Y / under, resColour.Z / under, resColour.W);
    }

    /// <summary>SampleFlowingTexMip2: GetLinearInterpNodes' two nodes, each hash33 of its seed, mixed and PreserveVariance about the mean.</summary>
    private static Vector4 Flowing(Mipmaps texture, Vector2 uv, float phase, float seed, float level, Vector2 flow, float offset)
    {
        var globalPhases = new Vector2(0.5f + (phase * 0.5f), phase * 0.5f);
        var phases = new Vector2(globalPhases.X - MathF.Floor(globalPhases.X), globalPhases.Y - MathF.Floor(globalPhases.Y));
        var seeds = new Vector2(MathF.Floor(globalPhases.X) * 2f, (MathF.Floor(globalPhases.Y) * 2f) + 1f);
        Vector2 weights = Vector2.Min(phases, Vector2.One - phases) * 2f;
        var colour = Vector4.Zero;
        float moment = 0f;
        for (var node = 0; node < 2; node++)
        {
            Vector3 hash = Hash33(new Vector3(seeds[node], seed, 0f));
            Vector2 at = new Vector2(hash.X, hash.Y) + uv + (flow * ((phases[node] * 2f) - 1f + offset));
            moment += weights[node] * weights[node];
            colour += Bilinear(texture[(int)level], at) * weights[node];
        }

        Vector4 mean = Bilinear(texture[texture.Count - 1], new Vector2(0.5f, 0.5f));
        return ((colour - mean) / MathF.Sqrt(moment)) + mean;
    }

    /// <summary>hash33: frac of the place scaled, mixed with itself.</summary>
    private static Vector3 Hash33(Vector3 p3)
    {
        p3 = Fraction(p3 * new Vector3(0.1031f, 0.1030f, 0.0973f));
        p3 += new Vector3(Vector3.Dot(p3, new Vector3(p3.Y, p3.X, p3.Z) + new Vector3(19.19f)));
        return Fraction((new Vector3(p3.X, p3.X, p3.Y) + new Vector3(p3.Y, p3.X, p3.X)) * new Vector3(p3.Z, p3.Y, p3.X));
    }

    /// <summary>hash3i3fStable: hash33UintPcg over the ints' bits, each HashUToF.</summary>
    private static Vector3 HashInts(int x, int y, int z)
    {
        uint vx = unchecked((uint)x), vy = unchecked((uint)y), vz = unchecked((uint)z);
        unchecked
        {
            vx = (vx * 1664525u) + 1013904223u;
            vy = (vy * 1664525u) + 1013904223u;
            vz = (vz * 1664525u) + 1013904223u;
            vx += vy * vz;
            vy += vz * vx;
            vz += vx * vy;
            vx ^= vx >> 16;
            vy ^= vy >> 16;
            vz ^= vz >> 16;
            vx += vy * vz;
            vy += vz * vx;
            vz += vx * vy;
        }

        static float Unit(uint input) => Math.Clamp(BitConverter.UInt32BitsToSingle((input >> 9) | 0x3f800000u) - 1f, 0f, 1f);
        return new Vector3(Unit(vx), Unit(vy), Unit(vz));
    }

    private static float Bayer16(Vector2 pixel)
    {
        int[] matrix = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        return (matrix[((int)pixel.X % 4) + (((int)pixel.Y % 4) * 4)] + 0.5f) / 16f;
    }

    /// <summary>One level read between its four nearest texels, wrapping - SamplerLinearWrap at a level.</summary>
    private static Vector4 Bilinear(GamePicture level, Vector2 at)
    {
        float x = ((at.X - MathF.Floor(at.X)) * level.Width) - 0.5f;
        float y = ((at.Y - MathF.Floor(at.Y)) * level.Height) - 0.5f;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        Vector4 Texel(int tx, int ty)
        {
            tx = ((tx % level.Width) + level.Width) % level.Width;
            ty = ((ty % level.Height) + level.Height) % level.Height;
            int i = ((ty * level.Width) + tx) * 4;
            return new Vector4(level.Rgba[i], level.Rgba[i + 1], level.Rgba[i + 2], level.Rgba[i + 3]) / 255f;
        }

        Vector4 top = Texel(x0, y0) + ((Texel(x0 + 1, y0) - Texel(x0, y0)) * fx);
        Vector4 under = Texel(x0, y0 + 1) + ((Texel(x0 + 1, y0 + 1) - Texel(x0, y0 + 1)) * fx);
        return top + ((under - top) * fy);
    }

    private static Vector3 Fraction(Vector3 v) => v - new Vector3(MathF.Floor(v.X), MathF.Floor(v.Y), MathF.Floor(v.Z));

    private static float Mix(float a, float b, float t) => a + ((b - a) * t);

    private static float Saturate(float value) => Math.Clamp(value, 0f, 1f);

    /// <summary>The pass's material and its graphs, as the attachment's render pass names them.</summary>
    internal static IReadOnlyList<(ShaderInstance Instance, ShaderGraph Graph)> Chain()
    {
        string folder = ComponentSweepTests.Fixture("fog");
        IReadOnlyList<ShaderInstance> instances = ShaderGraph.Instances(File.ReadAllBytes(Path.Combine(folder, "Red_Vale_ao_pass0.mat")));
        return [.. instances.Select(one => (one, ShaderGraph.Read(File.ReadAllBytes(Path.Combine(folder, Path.GetFileName(one.Parent))))))];
    }
}
