using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The engine's wetness pass, the processor's half of it: what reads the wetness compiles and reads it dry, the card's read is written
/// where the pixel stands, and the beach waves cast theirs as BreakingWavesFragment writes it.
/// </summary>
public class WetnessTests
{
    private const string WetPath = "Metadata/EngineGraphs/specular_wetness_mult.fxgraph";

    private const int Size = 32;

    /// <summary>A tilt that looks straight down - see MeshPicture.Camera.</summary>
    private const float Down = MathF.PI / 2f;

    /// <summary>A plain blue colour, alpha one, written where the wetness graph reads it from.</summary>
    private const string Blue =
        """
        {"nodes":[
          {"type":"ConstantPixel4","index":0,"parameters":[{"value":[0.05,0.05,0.9,1.0]}]},
          {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
         "links":[
          {"src":{"type":"ConstantPixel4","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input"}}]}
        """;

    /// <summary>
    /// specular_wetness_mult.fxgraph as the install holds it (tests/fixtures/shaders): its colour compiles - ReadPixelWetness being the
    /// card's wetness pass's - and the processor, which casts none, draws the colour exactly as the graph before it left it.
    /// </summary>
    [Fact]
    public void ARECEIVERCompilesAndReadsDryOnTheProcessor()
    {
        ShadeProgram plain = Compiled((Instance("Metadata/Blue.fxgraph"), Graph(Blue)));
        ShadeProgram wet = Compiled((Instance("Metadata/Blue.fxgraph"), Graph(Blue)), (Instance(WetPath), Fixture(WetPath)));
        Assert.False(plain.ReadsWetness);
        Assert.True(wet.ReadsWetness);
        Assert.Equal(["Metadata/Blue.fxgraph", WetPath], wet.Graphs);

        SkinnedMesh floor = Floor();
        GamePicture dry = MeshPicture.Of(floor, Size, tilt: Down, shades: [plain]);
        GamePicture read = MeshPicture.Of(floor, Size, tilt: Down, shades: [wet]);
        Assert.Contains(dry.Rgba, one => one > 0);
        Assert.Equal(dry.Rgba, read.Rgba);
    }

    /// <summary>The card reads the wetness pass where the pixel stands - ShadePixel, set before the run - from its own slot.</summary>
    [Fact]
    public void THECARDReadsTheWetnessWhereThePixelStands()
    {
        ShadeProgram wet = Compiled((Instance("Metadata/Blue.fxgraph"), Graph(Blue)), (Instance(WetPath), Fixture(WetPath)));
        string hlsl = ShadeHlsl.Of(wet, 18);
        Assert.Contains("static float2 ShadePixel = float2(-1.0, -1.0);", hlsl, StringComparison.Ordinal);
        Assert.Contains($"Texture2D<float> ShadeWetness : register(t{ShadeHlsl.WetnessSlot});", hlsl, StringComparison.Ordinal);
        Assert.Contains("ShadeWetness.Load(int3(int2(ShadePixel), 0))", hlsl, StringComparison.Ordinal);

        // A PROGRAM THAT READS NONE WRITES NO READ.
        Assert.DoesNotContain("ShadeWetness.Load", ShadeHlsl.Of(Compiled((Instance("Metadata/Blue.fxgraph"), Graph(Blue))), 18), StringComparison.Ordinal);
    }

    /// <summary>
    /// BreakingWavesFragment's wetness: wet through under the film far out, the shore's band of eight tenths up the sand, and dry far
    /// past it - the larger of the film's subsurface past a tenth and the band.
    /// </summary>
    [Fact]
    public void THEBEACHCastsItsWetnessAsWritten()
    {
        WaterSheet profile = WaterLookups.Read(BeachTests.Engine).BeachProfile!;
        ShoreField shore = BeachTests.SlopeShore();
        WaterSheet noise = BeachTests.Half();
        float Wet(float x, float clock)
            => ShadeProgram.Beaching(profile, noise, new Vector2(x, 0f), clock, 0f, Vector3.One, shore, default(BeachTests.Foamy), 0, 1, 2).Wetness;

        for (float clock = 0f; clock < 5f; clock += 0.5f)
        {
            Assert.Equal(1f, Wet(-1000f, clock), 4);
            Assert.True(Wet(300f, clock) >= 0.8f - 1e-4f, $"{Wet(300f, clock)} at {clock}");
            Assert.Equal(0f, Wet(1200f, clock), 4);
        }

        // NO SHORE, NONE.
        Assert.Equal(0f, ShadeProgram.Beaching(profile, noise, Vector2.Zero, 1f, 0f, Vector3.One, null, default(BeachTests.Foamy), 0, 1, 2).Wetness);
    }

    /// <summary>The beach's program carries the wetness it casts - the flat one and the glossy - bound with the same sheets, and written for the card.</summary>
    [Fact]
    public void THEBEACHProgramCarriesItsWetness()
    {
        ShadeProgram beach = ShadeProgram.Beach(new ShadeTexture("foam", true), new ShadeTexture("foam bumps", false), new ShadeTexture("ripples", false));
        ShadeProgram wetness = Assert.IsType<ShadeProgram>(beach.Wetness);
        Assert.Same(wetness, beach.Glossy!.Wetness);
        Assert.Equal(beach.Textures, wetness.Textures);
        Assert.True(wetness.HasAlpha);
        Assert.Contains(".wetness.xxxx;", ShadeHlsl.Of(wetness, 18), StringComparison.Ordinal);

        Mipmaps?[] sheets = [Sheet(), Sheet(), Sheet()];
        ShadeProgram bound = beach.With(sheets);
        ShadeProgram boundWetness = Assert.IsType<ShadeProgram>(bound.Wetness);
        Assert.Equal(sheets, boundWetness.Sheets);
    }

    private static ShadeProgram Compiled(params (ShaderInstance Instance, ShaderGraph Graph)[] chain)
    {
        ShadeCompile compiled = ShadeProgram.Compile(chain);
        Assert.Empty(compiled.Skipped);
        return Assert.IsType<ShadeProgram>(compiled.Program);
    }

    /// <summary>A floor twenty across.</summary>
    private static SkinnedMesh Floor()
    {
        Vector3[] places = [new(-10f, -10f, 0f), new(10f, -10f, 0f), new(10f, 10f, 0f), new(-10f, 10f, 0f)];
        var normals = new Vector3[4];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        Vector2[] spots = [.. places.Select(one => new Vector2((one.X + 10f) / 20f, (one.Y + 10f) / 20f))];
        return SkinnedMesh.Of(places, normals, [0, 1, 2, 0, 2, 3], new Vector3(-10f, -10f, -1f), new Vector3(10f, 10f, 0f), spots, [new MeshShape("Floor", 0, 6)]);
    }

    private static Mipmaps Sheet()
    {
        var pixels = new byte[8 * 8 * 4];
        Array.Fill(pixels, (byte)200);
        Mipmaps? levels = Mipmaps.Of(new GamePicture(8, 8, pixels));
        Assert.NotNull(levels);
        return levels;
    }

    private static ShaderInstance Instance(string parent) => new(parent, new Dictionary<string, ShaderValue[]>());

    private static ShaderGraph Graph(string json)
    {
        ShaderGraph graph = ShaderGraph.Read(Encoding.UTF8.GetBytes(json));
        Assert.True(graph.Ready);
        return graph;
    }

    private static ShaderGraph Fixture(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        ShaderGraph graph = ShaderGraph.Read(File.ReadAllBytes(Path.Combine(dir.FullName, "tests", "fixtures", "shaders", path.Replace("/", "__", StringComparison.Ordinal))));
        Assert.True(graph.Ready);
        return graph;
    }
}
