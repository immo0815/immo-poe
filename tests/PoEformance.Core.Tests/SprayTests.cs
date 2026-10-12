using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The sea's spray on what stands in it - effects.ffx's CrashingFoamSurface, a node of the rocks' own material (OceanSprayFoam.fxgraph).
/// </summary>
public class SprayTests
{
    /// <summary>
    /// OceanSprayFoam's shape, cut down: the foam's three textures and the wave profile, the place lifted by the spray's height, the
    /// TBN basis's normal, the curvature and the mask's height - its colour into the albedo's xyz, its wetness into the w.
    /// </summary>
    private const string Graph = """
        {"nodes":[
          {"type":"InputWorldPos","index":0,"stage":"UVSetup"},
          {"type":"InputTbnBasis","index":0,"stage":"UVSetup"},
          {"type":"InputTexture","index":0,"parameters":[{"path":"Art/SeaFoam_colour.dds","srgb":true}]},
          {"type":"InputTexture","index":1,"parameters":[{"path":"Art/SeaFoam_normal.dds","srgb":false}]},
          {"type":"InputTexture","index":2,"parameters":[{"path":"Art/gerstner_wave_profile.dds","srgb":false}]},
          {"type":"InputTexture","index":3,"parameters":[{"path":"Art/flow_muddle_blur_01_R16G16.dds","srgb":false}]},
          {"type":"ConstantFloat","index":5,"parameters":[{"value":-75.0}],"custom_parameter":"OceanSpray_Height"},
          {"type":"Add3","index":0},
          {"type":"ConstantFloat3","index":0,"parameters":[{"value":[0.0,0.0,1.0]}]},
          {"type":"Transform","index":0},
          {"type":"ConstantFloat","index":3,"parameters":[{"value":0.2741}]},
          {"type":"ConstantFloat","index":6,"parameters":[{"value":350.0}],"custom_parameter":"Spray_MaskHeight"},
          {"type":"CrashingFoamSurface","index":0},
          {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
         "links":[
          {"src":{"type":"InputWorldPos","index":0,"stage":"UVSetup","variable":"output"},"dst":{"type":"Add3","index":0,"variable":"a"}},
          {"src":{"type":"ConstantFloat","index":5,"variable":"output"},"dst":{"type":"Add3","index":0,"variable":"b","swizzle":"z"}},
          {"src":{"type":"ConstantFloat3","index":0,"variable":"output"},"dst":{"type":"Transform","index":0,"variable":"input"}},
          {"src":{"type":"InputTbnBasis","index":0,"stage":"UVSetup","variable":"output"},"dst":{"type":"Transform","index":0,"variable":"inmatrix"}},
          {"src":{"type":"InputTexture","index":0,"variable":"out_texture"},"dst":{"type":"CrashingFoamSurface","index":0,"variable":"foam_color_tex"}},
          {"src":{"type":"InputTexture","index":1,"variable":"out_texture"},"dst":{"type":"CrashingFoamSurface","index":0,"variable":"foam_normal_tex"}},
          {"src":{"type":"InputTexture","index":2,"variable":"out_texture"},"dst":{"type":"CrashingFoamSurface","index":0,"variable":"wave_shape_precomp_tex"}},
          {"src":{"type":"InputTexture","index":3,"variable":"out_texture"},"dst":{"type":"CrashingFoamSurface","index":0,"variable":"wave_coord_noise_tex"}},
          {"src":{"type":"Add3","index":0,"variable":"output"},"dst":{"type":"CrashingFoamSurface","index":0,"variable":"world_pos"}},
          {"src":{"type":"Transform","index":0,"variable":"output"},"dst":{"type":"CrashingFoamSurface","index":0,"variable":"world_normal"}},
          {"src":{"type":"ConstantFloat","index":3,"variable":"output"},"dst":{"type":"CrashingFoamSurface","index":0,"variable":"curvature"}},
          {"src":{"type":"ConstantFloat","index":6,"variable":"output"},"dst":{"type":"CrashingFoamSurface","index":0,"variable":"spray_height"}},
          {"src":{"type":"CrashingFoamSurface","index":0,"variable":"foam_color","swizzle":"xyz"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}},
          {"src":{"type":"CrashingFoamSurface","index":0,"variable":"wetness"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"w"}}]}
        """;

    /// <summary>
    /// The node compiles - its three textures read, the wave profile not, since no output takes it - and its outputs on the card are
    /// one sum read twice, each sheet with its own sRGB.
    /// </summary>
    [Fact]
    public void THEROCKSSprayIsCompiledFromTheirOwnGraph()
    {
        ShaderGraph graph = ShaderGraph.Read(Encoding.UTF8.GetBytes(Graph));
        Assert.True(graph.Ready);
        ShadeCompile compiled = ShadeProgram.Compile([(new ShaderInstance("Metadata/Materials/OceanSprayFoam.fxgraph", new Dictionary<string, ShaderValue[]>()), graph)]);

        Assert.Empty(compiled.Skipped);
        ShadeProgram program = Assert.IsType<ShadeProgram>(compiled.Program);
        Assert.Equal(
            ["Art/SeaFoam_colour.dds", "Art/SeaFoam_normal.dds", "Art/flow_muddle_blur_01_R16G16.dds"],
            program.Textures.Select(one => one.Path));
        Assert.True(program.UsesTime);

        string hlsl = ShadeHlsl.Of(program, 13);
        Assert.Contains("ShadeSprayOut ShadeSpray(", hlsl, StringComparison.Ordinal);
        Assert.Single(hlsl.Split("= ShadeSpray(").Skip(1));
        Assert.Contains("wrap, ShadeSheet0, true, ShadeSheet1, false, ShadeSheet2, false);", hlsl, StringComparison.Ordinal);
        Assert.Contains("spray0.colour;", hlsl, StringComparison.Ordinal);
        Assert.Contains("spray0.wetGloss;", hlsl, StringComparison.Ordinal);
    }

    /// <summary>
    /// Port's own rock, ISL_RockLarge01c.mat over PBRDielectricBN, the two wetness graphs and OceanSprayFoam as the install holds them
    /// (tests/fixtures/shaders, from the capture of 2026-10-12): its spray compiles beside its stone, the foam's colour sRGB and the
    /// noise not, and nothing is left out but the screen's wetness, which the engine reads off a pass of its own.
    /// </summary>
    [Fact]
    public void PORTSRockCompilesItsSpray()
    {
        static byte[] Fixture(string path)
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return File.ReadAllBytes(Path.Combine(dir.FullName, "tests", "fixtures", "shaders", path.Replace("/", "__", StringComparison.Ordinal)));
        }

        IReadOnlyList<ShaderInstance> instances = ShaderGraph.Instances(Fixture("Art/Models/Terrain/Islands/Tiles/Island/Textures/Rocks/ISL_RockLarge01c.mat"));
        ShadeCompile compiled = ShadeProgram.Compile([.. instances.Select(one => (one, ShaderGraph.Read(Fixture(one.Parent))))]);

        Assert.Equal(["ReadPixelWetness in specular_wetness_mult"], compiled.Skipped);
        ShadeProgram program = Assert.IsType<ShadeProgram>(compiled.Program);
        Assert.Equal(
            [
                ("Art/Textures/Environment/Global/BreakingWaves/SeaFoam_colour_Uncompressed.dds", true),
                ("Art/Textures/Environment/Global/BreakingWaves/SeaFoam_normal_Uncompressed.dds", false),
                ("Art/particles/FXLib/tileables/16bit/flow_muddle_blur_01_R16G16.dds", false),
            ],
            program.Textures.Skip(2).Select(one => (one.Path, one.Srgb)));
        string hlsl = ShadeHlsl.Of(program, 13);
        Assert.Single(hlsl.Split("= ShadeSpray(").Skip(1));
        Assert.Contains("ShadeSheet2, true, ShadeSheet3, false, ShadeSheet4, false);", hlsl, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wet band stands where the engine puts it on a face looking out to sea 100 units off the shore: the most a little under the
    /// water's line, none 280 above it (<c>0.8 + z / 350</c>) nor 150 below it, and none on land 200 past the shore.
    /// </summary>
    [Fact]
    public void THEWETBandStandsAboveTheWater()
    {
        ShoreField shore = BayShore();
        float Wet(float x, float z) => Spray(new Vector3(x, 0f, z), 0f, shore).Wetness;

        Assert.Equal(0f, Wet(900f, -300f));
        Assert.Equal(0f, Wet(900f, 160f));
        Assert.True(Wet(900f, 50f) > Wet(900f, -200f) && Wet(900f, -200f) > 0.3f, $"{Wet(900f, 50f)} {Wet(900f, -200f)}");
        Assert.Equal(0f, Wet(1500f, 50f));
    }

    /// <summary>
    /// The spray runs up the rock on the crashing wave: at the water's line the foam stands the whole wave long; 200 units above it, it
    /// comes only as the spray peaks, once every 1,000 / 200 seconds - and a place further out to sea has its crash earlier.
    /// </summary>
    [Fact]
    public void THESPRAYRunsUpTheRockOnTheCrashingWave()
    {
        ShoreField shore = BayShore();
        (float Least, float Most) Over(float x, float z)
        {
            float least = float.MaxValue, most = float.MinValue;
            for (var step = 0; step < 100; step++)
            {
                float cover = Spray(new Vector3(x, 0f, z), step * 0.05f, shore).Colour.W;
                least = MathF.Min(least, cover);
                most = MathF.Max(most, cover);
            }

            return (least, most);
        }

        (float lineLeast, _) = Over(900f, 50f);
        Assert.True(lineLeast > 0.5f, $"least {lineLeast}");
        (float highLeast, float highMost) = Over(900f, -200f);
        Assert.True(highLeast < 0.05f && highMost > 0.3f, $"{highLeast} to {highMost}");

        // THE CRASH ROLLS IN: the time of the most spray 200 above the water comes later nearer the shore.
        float Peak(float x)
        {
            (float best, float at) = (float.MinValue, 0f);
            for (var step = 0; step < 250; step++)
            {
                float cover = Spray(new Vector3(x, 0f, -200f), step * 0.02f, shore).Colour.W;
                if (cover > best)
                {
                    (best, at) = (cover, step * 0.02f);
                }
            }

            return at;
        }

        float lag = (Peak(950f) - Peak(750f) + 5f) % 5f;
        Assert.InRange(lag, 0.5f, 1.5f);
    }

    /// <summary>No shore, no spray: the colour clear and the surface dry, as GetShoreData's absent shoreline would leave nothing to crash.</summary>
    [Fact]
    public void NOSHOREMeansNoSpray()
    {
        ShadeProgram.Spray spray = Spray(new Vector3(900f, 0f, 50f), 1f, null);
        Assert.Equal(Vector4.Zero, spray.Colour);
        Assert.Equal(0f, spray.Wetness);
    }

    /// <summary>The noise's 16-bit texels at a byte a channel, (r, g, 0, 1), from every header that spells two 16-bit channels.</summary>
    [Theory]
    [InlineData("dx10-unorm")]
    [InlineData("dx10-half")]
    [InlineData("legacy-unorm")]
    [InlineData("legacy-half")]
    [InlineData("masks")]
    public void TWOWIDEChannelsAreReadAsACardReadsThem(string header)
    {
        bool half = header.EndsWith("half", StringComparison.Ordinal);
        float[] values = [0f, 1f, 0.5f, 0.25f];
        var texels = new byte[values.Length * 2];
        for (var at = 0; at < values.Length; at++)
        {
            ushort raw = half ? BitConverter.HalfToUInt16Bits((Half)values[at]) : (ushort)MathF.Round(values[at] * 65535f);
            BitConverter.TryWriteBytes(texels.AsSpan(at * 2, 2), raw);
        }

        GamePicture picture = GameArt.Decode(Dds(header, 2, 1, texels)) ?? throw new InvalidOperationException("not read");
        Assert.Equal((2, 1), (picture.Width, picture.Height));
        Assert.Equal(new byte[] { 0, 255, 0, 255, 128, 64, 0, 255 }, picture.Rgba);
    }

    private static ShadeProgram.Spray Spray(Vector3 place, float clock, ShoreField? shore)
        => ShadeProgram.Spraying(place, new Vector3(-1f, 0f, 0f), 0.2741f, 350f, clock, shore, new Flat(), 0, 1, 2);

    private static ShoreField BayShore()
        => ShoreField.Of(WaterTests.Bay(), shape => shape == 0, shape => shape > 0) ?? throw new InvalidOperationException("no shore");

    /// <summary>The foam half covering, a flat normal at full ao and a fifth rough, and the noise's green at a half.</summary>
    private readonly struct Flat : ShadeProgram.ISprayTextures
    {
        public Vector4 Read(int sheet, Vector2 uv) => sheet switch
        {
            0 => new Vector4(1f, 1f, 1f, 0.5f),
            1 => new Vector4(0.5f, 0.5f, 1f, 0.2f),
            _ => new Vector4(0f, 0.5f, 0f, 1f),
        };
    }

    /// <summary>A DDS of two 16-bit channels under the header asked for.</summary>
    private static byte[] Dds(string header, int width, int height, byte[] texels)
    {
        bool dx10 = header.StartsWith("dx10", StringComparison.Ordinal);
        var dds = new byte[128 + (dx10 ? 20 : 0) + texels.Length];
        "DDS "u8.CopyTo(dds);
        BitConverter.TryWriteBytes(dds.AsSpan(4), 124);
        BitConverter.TryWriteBytes(dds.AsSpan(12), height);
        BitConverter.TryWriteBytes(dds.AsSpan(16), width);
        BitConverter.TryWriteBytes(dds.AsSpan(76), 32);
        switch (header)
        {
            case "masks":
                BitConverter.TryWriteBytes(dds.AsSpan(80), 0x40u);
                BitConverter.TryWriteBytes(dds.AsSpan(88), 32u);
                BitConverter.TryWriteBytes(dds.AsSpan(92), 0x0000FFFFu);
                BitConverter.TryWriteBytes(dds.AsSpan(96), 0xFFFF0000u);
                break;
            case "legacy-unorm":
            case "legacy-half":
                BitConverter.TryWriteBytes(dds.AsSpan(80), 0x4u);
                BitConverter.TryWriteBytes(dds.AsSpan(84), header == "legacy-half" ? 112u : 34u);
                break;
            default:
                BitConverter.TryWriteBytes(dds.AsSpan(80), 0x4u);
                "DX10"u8.CopyTo(dds.AsSpan(84));
                BitConverter.TryWriteBytes(dds.AsSpan(128), header == "dx10-half" ? 34u : 35u);
                BitConverter.TryWriteBytes(dds.AsSpan(132), 3u);
                break;
        }

        texels.CopyTo(dds.AsSpan(dds.Length - texels.Length));
        return dds;
    }
}
