using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The water lit as the engine's water pass lights it (SceneLight.ShadeWater: water.ffx's BuildWaterRefraction and GetWaterMediumColor,
/// lighting.ffx's ComputeDiffuse) - each rule held against numbers worked out by hand from the source.
/// </summary>
public class WaterLightTests
{
    /// <summary>The area's colour, linear, as WaterLook hands it: <c>pow((0.1, 0.19, 0.22), 2.2)</c>.</summary>
    private static readonly Vector3 Medium = new(
        MathF.Pow(0.1f, 2.2f), MathF.Pow(0.19f, 2.2f), MathF.Pow(0.22f, 2.2f));

    /// <summary>Up, the game's minus z - the way to the eye, the flat sea's normal and the way to a sun overhead.</summary>
    private static readonly Vector3 Up = -Vector3.UnitZ;

    private static SceneLight Light(Vector3 sunTravels, float flat = 0.05f, bool reflection = false) =>
        new([], SceneLight.PointShape.Zero, null)
        {
            SunColour = new Vector3(2f),
            SunDirection = Vector3.Normalize(sunTravels),
            Ambient = SceneAmbient.Flat,
            FlatAmbient = flat,
            WaterReflection = reflection,
        };

    private static ShadeProgram.WaterOut Deep(float height = 0f, float through = 0f, float terrain = 0f, Vector4 foam = default) =>
        new(foam, new Vector4(through, terrain, 1f, height));

    private static Vector3 Shaded(
        SceneLight light, ShadeProgram.WaterOut water, Vector3 waves, out float cover, out WaterLight terms, float specular = 0.03f) =>
        light.ShadeWater(
            water, Medium, 1f, Vector3.Zero, waves, Up, 1f, new Vector3(specular), ShadeProgram.WaterGloss, 1f, Vector3.Zero, out cover, out terms);

    [Fact]
    public void THEWATERSColourIsLightLetThroughWrappedRoundItNotAnAlbedo()
    {
        // OVERHEAD the sun meets the flat sea head on: N.L one, the wrap (1 + 3) / 4 one - the medium takes the sun whole and the flat ambient.
        Shaded(Light(Vector3.UnitZ), Deep(), Up, out _, out WaterLight overhead);
        Assert.Equal(1f, overhead.Exponent);
        AssertNear(Medium, overhead.Subsurface);
        AssertNear((Medium * 2f) + (Medium * 0.05f), overhead.Absorbed);

        // AT THE HORIZON N.L is nought: an albedo would take none of the sun, the medium three quarters of it.
        Shaded(Light(new Vector3(1f, 0f, 0f)), Deep(), Up, out _, out WaterLight grazing);
        AssertNear((Medium * 2f * 0.75f) + (Medium * 0.05f), grazing.Absorbed);
    }

    [Fact]
    public void ACRESTIsLiftedTowardsTheScatteringGreenAndATroughDarkened()
    {
        SceneLight light = Light(Vector3.UnitZ);

        // 250 * 100 * 5e-5 = 1.25: an exponent of 1 / 2.25 on a crest a hundred high; 1 / (1 - 0.5) = 2 in a trough forty deep.
        Shaded(light, Deep(height: 100f), Up, out _, out WaterLight crest);
        Shaded(light, Deep(height: -40f), Up, out _, out WaterLight trough);
        Assert.Equal(1f / 2.25f, crest.Exponent, 1e-6f);
        Assert.Equal(2f, trough.Exponent, 1e-6f);

        Vector3 scatter = SceneLight.WaterScatterColour;
        AssertNear(Vector3.Multiply(Pow(Medium / scatter, 1f / 2.25f), scatter), crest.Subsurface);
        Assert.True(crest.Subsurface.Y > Medium.Y * 2f, "a crest is not lifted");
        Assert.True(crest.Subsurface.Y / crest.Subsurface.Z > Medium.Y / Medium.Z, "a crest is not greener");
        Assert.True(trough.Subsurface.Y < Medium.Y * 0.1f, "a trough is not darkened");
    }

    [Fact]
    public void THEREFLECTIONIsNotThinnedByTheWatersDepth()
    {
        SceneLight light = Light(Vector3.UnitZ, reflection: true);
        Vector3 deep = Shaded(light, Deep(through: 0f), Up, out float deepCover, out WaterLight deepTerms);
        Vector3 shallow = Shaded(light, Deep(through: 0.9f), Up, out float shallowCover, out WaterLight shallowTerms);

        Assert.Equal(deepTerms.Specular, shallowTerms.Specular);
        Assert.True(deepTerms.Specular.X > 0f);

        // DEEP, the medium covers whole: what is behind is nought of the pixel.
        Assert.Equal(1f, deepCover, 1e-6f);
        AssertNear(deepTerms.Specular + (deepTerms.Absorbed * deepTerms.Refracted), deep * deepCover);

        // SHALLOW, nine tenths of what lies behind gets through and that much of the refracted: the layer is the rest over its cover.
        float behind = shallowTerms.Refracted * 0.9f;
        Assert.Equal(1f - behind, shallowCover, 1e-6f);
        AssertNear(shallowTerms.Specular + (shallowTerms.Absorbed * shallowTerms.Refracted * 0.1f), shallow * shallowCover);
    }

    [Fact]
    public void THESURFACESReflectivityIsTheEnginesFresnelOverTheAreasReflectiveness()
    {
        // HEAD ON: Fresnel(1) = exp2(-12.53789), and the reflectivity lerps it to one by the specular, 0.03.
        Shaded(Light(Vector3.UnitZ), Deep(), Up, out _, out WaterLight head, specular: 0.03f);
        float fresnel = MathF.Pow(2f, -5.55473f - 6.98316f);
        Assert.Equal(fresnel + (0.03f * (1f - fresnel)), head.Reflectivity, 1e-6f);
        Assert.Equal(1f - head.Reflectivity, head.Refracted, 1e-6f);

        // AT THE FLOOR'S EDGE the ratio takes the reflectivity away and gives the refracted back.
        Shaded(Light(Vector3.UnitZ), Deep(terrain: 0.5f), Up, out _, out WaterLight edge, specular: 0.03f);
        Assert.Equal(head.Reflectivity * 0.5f, edge.Reflectivity, 1e-6f);
        Assert.Equal((1f - edge.Reflectivity) + (edge.Reflectivity * 0.5f), edge.Refracted, 1e-6f);
    }

    [Fact]
    public void THEWATERIsDroppedWhereItHasThinnedToNothingOverTheFloor()
    {
        Vector3 lit = Shaded(Light(Vector3.UnitZ), Deep(terrain: 1f), Up, out float cover, out _);
        Assert.Equal(Vector3.Zero, lit);
        Assert.False(cover > 0f);
    }

    [Fact]
    public void THEFOAMIsTheAlbedoAndHidesWhatLiesUnderIt()
    {
        // HALF COVERED BY WHITE FOAM: the albedo half white, the medium half itself, what lies behind hidden by 1 - 0.5^3.
        Shaded(Light(Vector3.UnitZ), Deep(through: 1f, foam: new Vector4(0.5f, 0.5f, 0.5f, 0.5f)), Up, out float cover, out WaterLight terms);
        AssertNear(new Vector3(0.5f), terms.Foam);
        AssertNear(Medium * 0.5f, terms.Subsurface);
        Assert.Equal(0.875f, terms.Hidden, 1e-6f);
        Assert.Equal(1f - ((1f - 0.875f) * terms.Refracted), cover, 1e-6f);
    }

    [Fact]
    public void THEWATERSGlossyTwinHandsItsPartsToTheLight()
    {
        ShadeProgram water = ShadeProgram.Water();
        Assert.False(water.LightsWater);
        Assert.True(water.Glossy!.LightsWater);

        string plain = ShadeHlsl.Of(water, 18);
        string glossy = ShadeHlsl.Of(water.Glossy, 18);
        Assert.Contains("static const bool ShadeWater = false;", plain, StringComparison.Ordinal);
        Assert.Contains("said.waterState = float4(0.0, 0.0, 0.0, 0.0);", plain, StringComparison.Ordinal);
        Assert.Contains("static const bool ShadeWater = true;", glossy, StringComparison.Ordinal);
        Assert.Matches("said.waterFoam = r[0-9]+;", glossy);
        Assert.Matches("said.waterState = r[0-9]+;", glossy);
    }

    [Fact]
    public void ANAREASScatteringIsItsFilesOrTheEngines()
    {
        WaterLook set = WaterLook.Of(EnvironmentSettings.Read("set.env", System.Text.Encoding.UTF8.GetBytes("""{ "water": { "subsurface_scattering": 1.8 } }""")));
        Assert.Equal(1.8f, set.SubsurfaceScattering);
        Assert.Contains("scattering", set.Said, StringComparison.Ordinal);
        Assert.Equal(1f, WaterLook.EngineDefault.SubsurfaceScattering);
    }

    /// <summary>The refraction index: The Red Vale's file sets 1.30095 (gallows/1_5.env); where none is set the engine's 1.33, measured in Port's parsed environment.</summary>
    [Fact]
    public void ANAREASRefractionIndexIsItsFilesOrTheEngines()
    {
        WaterLook vale = WaterLook.Of(EnvironmentSettings.Read("vale.env", System.Text.Encoding.UTF8.GetBytes("""{ "water": { "refraction_index": 1.30095 } }""")));
        Assert.Equal(1.30095f, vale.RefractionIndex);
        Assert.Contains("refraction index", vale.Said, StringComparison.Ordinal);
        Assert.Contains("refraction index 1.301", vale.Line(), StringComparison.Ordinal);
        Assert.Equal(1.33f, WaterLook.EngineDefault.RefractionIndex);
        Assert.Equal(1.33f, WaterLook.Of(EnvironmentSettings.Read("port.env", System.Text.Encoding.UTF8.GetBytes("""{ "water": { "flow_intensity": 1.5 } }"""))).RefractionIndex);
    }

    private static Vector3 Pow(Vector3 value, float power) => new(MathF.Pow(value.X, power), MathF.Pow(value.Y, power), MathF.Pow(value.Z, power));

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        float tolerance = 1e-5f + (1e-4f * MathF.Max(expected.Length(), actual.Length()));
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }
}
