using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// A material's own light (v0.1.402): the emissive its graphs write is carried by the program the game's light draws (ShadeProgram.Glossy)
/// and added to the lit colour before the exposure and the grade, as the engine's EndLighting adds <c>emissionData.emission</c> - the
/// Exile's Staff's lantern, whose glass the compile followed into the emissive and no drawing showed.
/// </summary>
public sealed class EmissiveTests
{
    /// <summary>A grey colour and an emissive of its own, as WandererStaffc.mat's channel graphs leave them - constants here.</summary>
    private const string Glowing = """
        {"version":3,"nodes":[
          {"type":"ConstantPixel3","index":0,"parameters":[{"value":[0.25,0.25,0.25]}]},
          {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"},
          {"type":"ConstantPixel3","index":1,"parameters":[{"value":[0.4,0.2,0.1]}]},
          {"type":"EmissiveColor","index":0,"stage":"Texturing"}],
         "links":[
          {"src":{"type":"ConstantPixel3","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}},
          {"src":{"type":"ConstantPixel3","index":1,"variable":"output"},"dst":{"type":"EmissiveColor","index":0,"stage":"Texturing","variable":"input"}}]}
        """;

    /// <summary>The same colour with no emissive.</summary>
    private const string Plain = """
        {"version":3,"nodes":[
          {"type":"ConstantPixel3","index":0,"parameters":[{"value":[0.25,0.25,0.25]}]},
          {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
         "links":[
          {"src":{"type":"ConstantPixel3","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}]}
        """;

    /// <summary>
    /// The emissive is carried by the program the game's light draws - a twin of its own where the material has no gloss - and not by the
    /// plain one the picture's own lamp draws; a material with none gets no twin, as before.
    /// </summary>
    [Fact]
    public void AMATERIALSOwnLightIsCarriedByTheProgramTheGamesLightDraws()
    {
        ShadeCompile glowing = ShadeProgram.Compile([(Instance(), Graph(Glowing))]);
        Assert.NotNull(glowing.Program);
        Assert.False(glowing.Program.HasEmissive);
        ShadeProgram twin = Assert.IsType<ShadeProgram>(glowing.Program.Glossy);
        Assert.True(twin.HasEmissive);
        Assert.False(twin.HasSpecular);
        Assert.False(twin.HasGloss);

        ShadeCompile plain = ShadeProgram.Compile([(Instance(), Graph(Plain))]);
        Assert.NotNull(plain.Program);
        Assert.Null(plain.Program.Glossy);
    }

    /// <summary>
    /// Added to the lit colour before the exposure, as EndLighting adds it - in a dark room the colour is the emissive times the exposure; off
    /// by its switch; kept in "Lighting Only", whose engine graph (debug_lighting_only.fxgraph) replaces the albedo alone; and not in a channel
    /// view, which writes its channel in place of the light.
    /// </summary>
    [Fact]
    public void THEEMISSIVEIsAddedBeforeTheExposure()
    {
        var emission = new Vector3(0.4f, 0.2f, 0.1f);
        Vector3 Shade(SceneLight light)
            => light.Shade(new Vector3(0.25f), Vector3.Zero, -Vector3.UnitY, -Vector3.UnitY, 1f, false, default, 0f, default, 1f, emission);

        var dark = new SceneLight([], SceneLight.PointShape.Zero, null) { Ambient = SceneAmbient.None, Exposure = 2f };
        Assert.Equal(emission * 2f, Shade(dark));
        Assert.Equal(Vector3.Zero, Shade(new SceneLight([], SceneLight.PointShape.Zero, null) { Ambient = SceneAmbient.None, Exposure = 2f, Emissive = false }));
        Assert.Equal(emission * 2f, Shade(new SceneLight([], SceneLight.PointShape.Zero, null) { Ambient = SceneAmbient.None, Exposure = 2f, View = PictureView.LightingOnly }));
        Assert.Equal(new Vector3(0.25f), Shade(new SceneLight([], SceneLight.PointShape.Zero, null) { Ambient = SceneAmbient.None, Exposure = 2f, View = PictureView.Albedo }));

        // THE TERMS SAY IT: the split's own field, and the unexposed colour holds it.
        _ = dark.Shade(new Vector3(0.25f), Vector3.Zero, -Vector3.UnitY, -Vector3.UnitY, 1f, false, default, 0f, default, 1f, out SceneLight.LightTerms terms, emission);
        Assert.Equal(emission, terms.Emission);
        Assert.Equal(emission, terms.Unexposed);
    }

    /// <summary>The processor's picture under the game's light: a material in a dark room shows its own light alone, and nothing with the switch off.</summary>
    [Fact]
    public void ANEMISSIVEMaterialGlowsInADarkRoomOnTheProcessor()
    {
        ShadeProgram twin = ShadeProgram.Compile([(Instance(), Graph(Glowing))]).Program!.Glossy!.With([]);
        var dark = new SceneLight([], SceneLight.PointShape.Zero, null) { Ambient = SceneAmbient.None };
        GamePicture glowing = MeshPicture.Of(Quad(), new MeshPicture.Canvas(32) { Light = dark }, shades: [twin]);
        GamePicture unlit = MeshPicture.Of(
            Quad(), new MeshPicture.Canvas(32) { Light = new SceneLight([], SceneLight.PointShape.Zero, null) { Ambient = SceneAmbient.None, Emissive = false } },
            shades: [twin]);

        int middle = ((16 * 32) + 16) * 4;
        Assert.InRange(glowing.Rgba[middle], Srgb(0.4f) - 1, Srgb(0.4f) + 1);
        Assert.InRange(glowing.Rgba[middle + 1], Srgb(0.2f) - 1, Srgb(0.2f) + 1);
        Assert.InRange(glowing.Rgba[middle + 2], Srgb(0.1f) - 1, Srgb(0.1f) + 1);
        Assert.Equal(0, unlit.Rgba[middle]);
        Assert.Equal(0, unlit.Rgba[middle + 1]);
    }

    private static ShaderGraph Graph(string json)
    {
        ShaderGraph graph = ShaderGraph.Read(Encoding.UTF8.GetBytes(json));
        Assert.True(graph.Ready);
        return graph;
    }

    private static ShaderInstance Instance() => new("Metadata/Test.fxgraph", new Dictionary<string, ShaderValue[]>());

    /// <summary>The sRGB byte for a linear value.</summary>
    private static byte Srgb(float linear)
    {
        float encoded = linear <= 0.0031308f ? linear * 12.92f : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
    }

    /// <summary>A quad facing the default camera - ShadeProgramTests' own.</summary>
    private static SkinnedMesh Quad()
    {
        Vector3[] places = [new(-10f, 0f, -10f), new(10f, 0f, -10f), new(10f, 0f, 10f), new(-10f, 0f, 10f)];
        var normals = new Vector3[4];
        Array.Fill(normals, new Vector3(0f, -1f, 0f));
        var spots = new Vector2[4];
        Array.Fill(spots, new Vector2(0.5f, 0.5f));
        return SkinnedMesh.Of(places, normals, [0, 1, 2, 0, 2, 3], new Vector3(-10f, -1f, -10f), new Vector3(10f, 1f, 10f), spots, [new MeshShape("Quad", 0, 6)]);
    }
}
