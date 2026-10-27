using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The engine's field waves (ShadeProgram.Field): the card's alone - their HLSL in the water's program - and their wave shape taken
/// from the engine's own file. The processor's water is unchanged by them, which the water's own tests hold.
/// </summary>
public class FieldWaveTests
{
    [Fact]
    public void THEWATERCarriesTheFieldWavesForTheCard()
    {
        ShadeProgram water = ShadeProgram.Water();

        // THE CARD'S PROGRAMS read the field waves once, at the gradients taken outside any branch: the colour its foam, the glossy
        // twin's waves turned by their slope.
        string colour = ShadeHlsl.Of(water, 18);
        string glossy = ShadeHlsl.Of(water.Glossy!, 18);
        foreach (string hlsl in (string[])[colour, glossy])
        {
            Assert.Contains("float2 fieldDx = ddx(place.xy);", hlsl, StringComparison.Ordinal);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(hlsl, "= ShadeFieldAt\\("));
            Assert.Contains(ShadeHlsl.FieldLibrary, hlsl, StringComparison.Ordinal);
        }

        Assert.Contains("field0.colour;", colour, StringComparison.Ordinal);
        Assert.Contains("ShadeFieldTurned(", glossy, StringComparison.Ordinal);

        // THE FIXED LOOKUPS COME BEFORE A PROGRAM'S SHEETS.
        Assert.True(ShadeHlsl.FieldProfileSlot > ShadeHlsl.BeachNoiseSlot);
        Assert.Equal(ShadeHlsl.FieldProfileSlot + 2, ShadeHlsl.FieldFoamNormalSlot);
    }

    [Fact]
    public void THEFIELDWAVESShapeIsTheGerstnerProfileThatNeitherTheSwellNorTheBeachTakes()
    {
        WaterLookups lookups = WaterLookups.Read(path =>
        {
            string file = Path.Combine(ComponentSweepTests.Fixture("water-lookups"), "water-lookup-" + Path.GetFileName(path).ToLowerInvariant());
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        });

        WaterSheet profile = lookups.FieldProfile ?? throw new InvalidOperationException("no field profile");
        Assert.Equal((256, 256, 4), (profile.Width, profile.Height, profile.Channels));
        Assert.Contains("  TAKEN as the field waves' wave shape (gerstner_wave_profile.dds)", lookups.Lines);

        // READ RED AND BLUE as GetGerstnerProfile reads them: the crest's height (blue) peaks at the coordinate nought, a sixth of the
        // wave below the middle at its trough - a trochoid of height 1 / 2 pi - and stands flat at the break phase nought.
        float At(int x, int y, int channel) => profile.Texels[(((y * profile.Width) + x) * 4) + channel];
        Assert.Equal(0.138f, At(0, 128, 2), 3);
        Assert.Equal(-0.159f, At(128, 128, 2), 3);
        Assert.InRange(At(64, 0, 2), -0.002f, 0.002f);
        Assert.Equal(0.156f, At(16, 128, 0), 3);
    }

    [Fact]
    public void THEFOAMStandsInWhiteUntilTheEnginesIsHandedOver()
    {
        Assert.Equal((1, 1), (ShadeProgram.FieldFoamColour.Top.Width, ShadeProgram.FieldFoamColour.Top.Height));
        ShadeProgram.UseFieldFoam(null, null, "nothing read");
        Assert.Contains("the field waves' foam white - nothing read", ShadeProgram.FieldSaid, StringComparison.Ordinal);
    }
}
