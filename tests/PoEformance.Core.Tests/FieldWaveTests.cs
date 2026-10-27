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

    /// <summary>
    /// GetFieldFoamProfile term by term, against values worked out apart from the engine's source (water.ffx, math_utils' PolyStep and
    /// SmoothStepSimple's <c>3x² - 2x²</c>) in double precision: the break lets foam form whole already at 0.66, and the near-shore term
    /// is the whole 0.7 within 150 units of a shore.
    /// </summary>
    [Theory]
    [InlineData(0.5f, 1000f, 0.66f, 0.999230f, 0.350508f, 0.000142f, 0.350651f, 0.640260f)]
    [InlineData(0.85f, 1400f, 0.75f, 0.999998f, 0.625737f, 0.000003f, 0.625740f, 0.750296f)]
    [InlineData(0.2f, 120f, 0.9f, 1f, 0.350744f, 0.7f, 1f, 0.9f)]
    [InlineData(0.95f, 2500f, 0.45f, 0.012481f, 0.141478f, 0f, 0.141478f, 0.556591f)]
    public void THEFIELDFoamsProfileIsTheEnginesTermByTerm(
        float coord, float shore, float breaking, float breakFoam, float wave, float near, float intensity, float sharpness)
    {
        FieldFoamProfile profile = ShadeProgram.FieldFoamProfileAt(coord, shore, breaking);
        Assert.Equal(breakFoam, profile.BreakFoam, 4);
        Assert.Equal(wave, profile.Wave, 4);
        Assert.Equal(near, profile.NearShore, 4);
        Assert.Equal(intensity, profile.Intensity, 4);
        Assert.Equal(sharpness, profile.Sharpness, 4);
        Assert.Equal(profile.Spike + profile.Tail + profile.Constant, (profile.Wave - (0.3f * breaking)) / (1f - (0.3f * breaking)), 4);
    }

    /// <summary>
    /// The probe's break phase is the engine's: one less a five-thousandth of the shore's distance, ramped about <c>0.5 + 0.3 * noise</c>
    /// - math_utils' RampNorm written apart here - and with the wave shape flat the card lifts the water by the height offset alone, so the
    /// view meets it that far up along the view.
    /// </summary>
    [Fact]
    public void THEPROBEWorksOutTheBreakPhaseAsTheEngineRampsIt()
    {
        ShoreField shore = BeachTests.SlopeShore();
        var flat = new WaterSheet(2, 2, 4, new float[16], false, "flat");
        Vector3 toEye = Vector3.Normalize(new Vector3(0.5f, 0.2f, -1f));
        foreach (float x in (ReadOnlySpan<float>)[-1900f, -1000f, -400f])
        {
            var drawn = new Vector3(x, 0f, 0f);
            ProbeField field = ShadeProgram.FieldProbe(
                flat, BeachTests.Half(), Foam(255), Foam(255), drawn, toEye, Vector3.UnitX, Vector3.UnitY, 3f, -15f, shore, on: true);

            // THE SHORE AT NOUGHT, the field's cells 25 apart.
            Assert.InRange(field.ShoreDist, -x - 15f, -x + 15f);
            Assert.Equal(0.5f, field.Noise, 4);
            Assert.Equal(0.65f, field.Centre, 4);
            Assert.Equal(1f - (field.ShoreDist * 0.0002f), field.Raw, 5);
            Assert.Equal(RampNorm(field.Raw, 0.65f), field.BreakPhase, 4);

            // UP IS MINUS Z: the height offset lifts the water fifteen, met that far towards the eye.
            Assert.Equal(-15f, field.Lift, 3);
            Assert.Equal(-15f, field.Place.Z - field.Drawn.Z, 2);
            Assert.Equal(0f, Vector3.Cross(Vector3.Normalize(field.Place - field.Drawn), toEye).Length(), 3);
            Assert.True(field.Off < 0.01f);
        }

        // WORKED OUT APART: 1,000 from the shore the break phase is 0.662, past which the break lets foam form whole.
        ProbeField thousand = ShadeProgram.FieldProbe(
            flat, BeachTests.Half(), Foam(255), Foam(255), new Vector3(-1000f, 0f, 0f), -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 3f, 0f, shore, on: false);
        Assert.Equal(0.662f, thousand.BreakPhase, 2);
        Assert.False(thousand.On);
    }

    /// <summary>
    /// Over the engine's wave shape the crests stand tens of units tall, and the probe finds where the view through the pixel meets the
    /// lifted water - on the view, the lift there, at every moment of a wave.
    /// </summary>
    [Fact]
    public void THEPROBEFindsWhereTheCardsLiftedWaterMeetsTheView()
    {
        WaterSheet profile = WaterLookups.Read(BeachTests.Engine).FieldProfile!;
        ShoreField shore = BeachTests.SlopeShore();
        Vector3 toEye = Vector3.Normalize(new Vector3(0.6f, 0.1f, -1f));
        var drawn = new Vector3(-700f, 0f, 0f);
        float highest = 0f;
        for (float clock = 0f; clock < 5f; clock += 0.25f)
        {
            ProbeField field = ShadeProgram.FieldProbe(
                profile, BeachTests.Half(), Foam(255), Foam(255), drawn, toEye, Vector3.UnitX, Vector3.UnitY, clock, -15f, shore, on: true);
            Assert.True(field.Off < 0.05f, $"missed by {field.Off} at {clock}");
            Assert.Equal(field.Lift, field.Place.Z - field.Drawn.Z, 1);
            Assert.Equal(0f, Vector3.Cross(Vector3.Normalize(field.Place - field.Drawn), toEye).Length(), 3);
            highest = MathF.Max(highest, -(field.Lift + 15f));
        }

        Assert.InRange(highest, 20f, 200f);
    }

    /// <summary>
    /// The foam is the profile's: near the shore the near-shore term's whole foam covers most of the water, far out the break lets
    /// little form and it covers next to nothing - over foam of half its height everywhere.
    /// </summary>
    [Fact]
    public void THEPROBESFoamCoversAsItsProfileSays()
    {
        WaterSheet profile = WaterLookups.Read(BeachTests.Engine).FieldProfile!;
        ShoreField shore = BeachTests.SlopeShore();
        float Mean(float x)
        {
            float sum = 0f;
            var count = 0;
            for (float clock = 0f; clock < 5f; clock += 0.1f)
            {
                ProbeField field = ShadeProgram.FieldProbe(
                    profile, BeachTests.Half(), Foam(128), Foam(255), new Vector3(x, 0f, 0f), -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, clock, 0f, shore, on: true);
                Assert.InRange(field.Cover, 0f, 1f);
                sum += field.Cover;
                count++;
            }

            return sum / count;
        }

        float near = Mean(-60f), far = Mean(-1950f);
        Assert.True(near > 0.6f, $"near the shore {near}");
        Assert.True(far < 0.2f, $"far out {far}");
    }

    /// <summary>The probe's line names every term, and says where the view runs along the water rather than meeting it.</summary>
    [Fact]
    public void THEPROBESLineNamesEveryTerm()
    {
        ShoreField shore = BeachTests.SlopeShore();
        var flat = new WaterSheet(2, 2, 4, new float[16], false, "flat");
        ProbeField field = ShadeProgram.FieldProbe(
            flat, BeachTests.Half(), Foam(128), Foam(255), new Vector3(-1000f, 0f, 0f), -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 1f, -15f, shore, on: true);
        string line = Features.PictureProbe.Fielded(field);
        foreach (string part in (string[])["field waves, drawn on the card", "where the card's lifted water meets the view", "1 - shore x 0.0002 = ",
            "the break phase ", "the noise 0.500, so the break's centre 0.650", "the near-shore term", "covering", "(CHOSEN", "the height offset -15.0 in it"])
        {
            Assert.Contains(part, line, StringComparison.Ordinal);
        }

        ProbeField along = ShadeProgram.FieldProbe(
            flat, BeachTests.Half(), Foam(128), Foam(255), new Vector3(-1000f, 0f, 0f), Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ, 1f, -15f, shore, on: false);
        Assert.True(float.IsNaN(along.Off));
        Assert.Contains("off, the card drawing none", Features.PictureProbe.Fielded(along), StringComparison.Ordinal);
        Assert.Contains("the view runs along the water", Features.PictureProbe.Fielded(along), StringComparison.Ordinal);
    }

    /// <summary>math_utils' RampNorm, written apart from the C#.</summary>
    private static float RampNorm(float value, float center)
    {
        center = Math.Clamp(center, 1e-3f, 1f - 1e-3f);
        if (center > 0.5f)
        {
            float v = 1f - value, c = 1f - center;
            return 1f - (v / MathF.Max((2f * c * 2f * c * (1f - v)) + v, 1e-5f));
        }

        return value / MathF.Max((2f * center * 2f * center * (1f - value)) + value, 1e-5f);
    }

    /// <summary>White foam of one height, or a flat normal at full ao and half roughness where <paramref name="alpha"/> is 255.</summary>
    private static Mipmaps Foam(byte alpha)
    {
        var pixels = new byte[8 * 8 * 4];
        for (var one = 0; one < 64; one++)
        {
            pixels[one * 4] = alpha == 255 ? (byte)128 : (byte)255;
            pixels[(one * 4) + 1] = alpha == 255 ? (byte)128 : (byte)255;
            pixels[(one * 4) + 2] = 255;
            pixels[(one * 4) + 3] = alpha;
        }

        return Mipmaps.Of(new GamePicture(8, 8, pixels))!;
    }

    [Fact]
    public void THEFOAMStandsInWhiteUntilTheEnginesIsHandedOver()
    {
        Assert.Equal((1, 1), (ShadeProgram.FieldFoamColour.Top.Width, ShadeProgram.FieldFoamColour.Top.Height));
        ShadeProgram.UseFieldFoam(null, null, "nothing read");
        Assert.Contains("the field waves' foam white - nothing read", ShadeProgram.FieldSaid, StringComparison.Ordinal);
    }
}
