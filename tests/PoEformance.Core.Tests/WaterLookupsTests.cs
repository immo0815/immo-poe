using System.Buffers.Binary;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The engine's water lookups: read as floats as a card reads them, and each taken only where it goes with what the shaders read it as -
/// the ripple texture's green and blue with its red's slope, a profile's red with the cross-section gradient's.
/// </summary>
public sealed class WaterLookupsTests
{
    [Fact]
    public void THEOLDFloatFormatsAndDxgisAreReadAsACardReadsThem()
    {
        // 116, four floats a texel, red first.
        FloatPicture four = FloatArt.Decode(Legacy(2, 1, 116, Floats(1, 2, 3, 4, 5, 6, 7, 8)), out _)!;
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f], four.Texels);
        Assert.Equal("Direct3D format 116 (A32B32G32R32F)", four.Format);

        // 113, four halves; 111, one half - green and blue nought, alpha one.
        Assert.Equal([0.5f, -1f, 2f, 0.25f], FloatArt.Decode(Legacy(1, 1, 113, Halves(0.5f, -1f, 2f, 0.25f)), out _)!.Texels);
        Assert.Equal([-0.75f, 0f, 0f, 1f], FloatArt.Decode(Legacy(1, 1, 111, Halves(-0.75f)), out _)!.Texels);

        // DXGI's R32G32_FLOAT behind its own header.
        Assert.Equal([3f, -4f, 0f, 1f], FloatArt.Decode(Dx10(1, 1, 16, Floats(3, -4)), out _)!.Texels);

        // Short texels are refused, with how short.
        Assert.Null(FloatArt.Decode(Legacy(2, 2, 116, Floats(1, 2, 3, 4)), out string why));
        Assert.Contains("16 bytes of texels where 64 were due", why, StringComparison.Ordinal);
        Assert.Null(FloatArt.Decode([1, 2, 3], out _));
    }

    [Fact]
    public void THERIPPLETextureIsTakenWhereItsGreenAndBlueAreItsRedsSlope()
    {
        WaterLookups lookups = WaterLookups.Read(Install);

        Assert.NotNull(lookups.Ripples);
        Assert.True(lookups.Ripples!.Engine);
        Assert.Equal(128, lookups.Ripples.Width);
        Assert.Equal(Ripple(64, 37, 0), lookups.Ripples.Texels[((37 * 128) + 64) * 4]);
        Assert.Contains(lookups.Lines, line => line.StartsWith("  green against the red's difference a texel along x: correlation 1", StringComparison.Ordinal));
        Assert.StartsWith("the ripples over the engine's ripple texture (surface_ripple.dds)", lookups.Summary, StringComparison.Ordinal);

        // GREEN AND BLUE THAT ARE NOT ITS SLOPE - a decoding gone wrong reads so - and a texture of another size: the stand-in stays.
        WaterLookups noise = WaterLookups.Read(path => Same(path, WaterLookups.RipplePath) ? RippleFile(slope: false) : null);
        Assert.Null(noise.Ripples);
        Assert.Contains("do not go with its red's slope", noise.Summary, StringComparison.Ordinal);
        WaterLookups small = WaterLookups.Read(path => Same(path, WaterLookups.RipplePath) ? Legacy(64, 64, 116, new byte[64 * 64 * 16]) : null);
        Assert.Contains("is 64 x 64, not 128 square", small.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void THESWELLTakesTheProfileWhoseRedsRiseIsTheGradientsRed()
    {
        WaterLookups lookups = WaterLookups.Read(Install);

        // THE SECOND PROFILE GOES WITH THE GRADIENT, the first does not: the second is the swell's, at its own size.
        WaterSheet swells = lookups.Swells!;
        Assert.True(swells.Engine);
        Assert.Equal((ProfileWidth, ProfileHeight, 4), (swells.Width, swells.Height, swells.Channels));
        Assert.Contains("wave_profile.dds, its rise wave_profile_gradient.dds read at its texels", swells.Said, StringComparison.Ordinal);

        // ITS RED THE PROFILE'S, its rise the gradient read at the profile's texel centres - the gradient being half as wide.
        int x = 10, y = 3;
        int at = ((y * ProfileWidth) + x) * 4;
        Assert.Equal(Profile(x, y), swells.Texels[at]);
        float u = (x + 0.5f) / ProfileWidth;
        Assert.Equal(Rise(u, y), swells.Texels[at + 1], 0.02f);

        // ITS GREEN AND ALPHA, the trail foam's strength and the front's phase, as the profile holds them.
        Assert.Equal((0.25f, 0.75f), (swells.Texels[at + 2], swells.Texels[at + 3]));

        // READ AS THE CARD READS IT, wrapping along a width that is no power of two.
        var three = new WaterSheet(3, 1, 4, [0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 2f, 0f, 0f, 0f], true, "three");
        Assert.Equal(1f, ShadeProgram.Swell(three, 0.5f, 0.5f, out _), 5);
        Assert.Equal(1f, ShadeProgram.Swell(three, 1f, 0.5f, out _), 5);   // between the last texel's centre and the first's
    }

    [Fact]
    public void WITHOUTAGradientNoProfileIsTakenForNothingTellsThemApart()
    {
        WaterLookups lookups = WaterLookups.Read(path => Same(path, WaterLookups.GradientPath) ? null : Install(path));

        Assert.Null(lookups.Swells);
        Assert.Contains("nothing else tells its profile apart", lookups.Summary, StringComparison.Ordinal);
        Assert.Null(WaterLookups.Read(null).Ripples);
    }

    /// <summary>
    /// THE ENGINE'S OWN FILES (tests/fixtures/water-lookups, Port's v0.1.231 capture): the ripple texture's green and blue are its red's slope
    /// exactly, and of the profiles the one that goes with the gradient is wave_profile.dds - not the Gerstner profile the program also names.
    /// </summary>
    [Fact]
    public void THEENGINESOwnLookupsAreTakenAndTheGerstnerProfileIsNot()
    {
        WaterLookups lookups = WaterLookups.Read(Engine);

        Assert.True(lookups.Ripples is { Engine: true, Width: 128, Height: 128, Channels: 4 });
        Assert.Contains("  green against the red's difference a texel along x: correlation 1, green 1 of it; along y -0.002", lookups.Lines);
        Assert.Contains("  blue against the red's difference a texel along y: correlation 1, blue 1 of it; along x -0.002", lookups.Lines);
        Assert.True(lookups.Swells is { Engine: true, Width: 128, Height: 128, Channels: 4 });
        Assert.Contains("TAKEN as the swell's cross-section: wave_profile.dds with wave_profile_gradient.dds", lookups.Lines);
        Assert.Contains(lookups.Lines, line => line.StartsWith("  against wave_profile_gradient.dds: its red with this red's difference a unit of the phase 0.028", StringComparison.Ordinal));
    }

    /// <summary>
    /// THE FOAM ON THE ENGINE'S PROFILE rolls in in bands: none far out where the swell stands whole, its trail behind the break within a
    /// colour unit of the shore, none in the last tenth where the shallows clear it - and thinner over water under 25 units deep.
    /// </summary>
    [Fact]
    public void THEFOAMRollsInBandsOnTheEnginesProfile()
    {
        WaterSheet sheet = WaterLookups.Read(Engine).Swells!;
        ShoreField shore = ShoreField.Of(WaterTests.Bay(), shape => shape == 0, shape => shape > 0)!;
        Vector4 swell = WaterLook.EngineDefault.SwellRegister;
        (float Cover, float Most) Over(float out_, float depth)
        {
            float sum = 0f, most = 0f;
            var count = 0;
            for (float clock = 0f; clock < 20f; clock += 0.25f)
            {
                for (float y = -400f; y <= 400f; y += 50f)
                {
                    Vector4 foam = ShadeProgram.Foam(sheet, new Vector2(1000f - out_, y), swell, clock, depth, shore);
                    sum += foam.W;
                    most = MathF.Max(most, foam.W);
                    count++;
                }
            }

            return (sum / count, most);
        }

        Assert.InRange(Over(500f, 100f).Cover, 0.1f, 0.3f);
        Assert.InRange(Over(500f, 100f).Most, 0.3f, 0.6f);
        Assert.Equal(0f, Over(1500f, 100f).Cover, 3);
        Assert.Equal(0f, Over(50f, 100f).Cover, 3);
        Assert.True(Over(500f, 5f).Cover < Over(500f, 100f).Cover / 2f);

        // THE STAND-IN CARRIES NO FOAM: nothing covered, whatever its colour.
        Assert.Equal(0f, ShadeProgram.Foam(ShadeProgram.Swells, new Vector2(500f, 0f), swell, 3f, 100f, shore).W);
    }

    /// <summary>
    /// THE PROBE'S SWELL LINE IS THE FOAM DRAWN, term by term: the same cover at every place and clock, the shore's distance the field's,
    /// and the field's cell named - asked for when Port's channel showed foam in rectangles with the field waves off.
    /// </summary>
    [Fact]
    public void THEPROBESSwellIsTheFoamDrawn()
    {
        WaterSheet sheet = WaterLookups.Read(Engine).Swells!;
        ShoreField shore = ShoreField.Of(WaterTests.Bay(), shape => shape == 0, shape => shape > 0)!;
        Vector4 swell = WaterLook.EngineDefault.SwellRegister;
        var covered = 0;
        for (float clock = 0f; clock < 10f; clock += 0.5f)
        {
            foreach (float out_ in (float[])[50f, 300f, 500f, 800f, 1500f])
            {
                var place = new Vector2(1000f - out_, 0f);
                ProbeSwell probe = ShadeProgram.SwellProbe(sheet, place, swell, clock, 100f, shore);
                Assert.Equal(ShadeProgram.Foam(sheet, place, swell, clock, 100f, shore).W, probe.Cover);
                shore.At(place, out float near, out float ocean);
                Assert.Equal(near, probe.ShoreDist);
                Assert.Equal(ocean, probe.OceanDist);
                Assert.Equal("water", probe.Cell);
                covered += probe.Cover > 0.05f ? 1 : 0;
            }
        }

        Assert.True(covered > 5, $"{covered} probes covered");
        Assert.Equal("land", ShadeProgram.SwellProbe(sheet, new Vector2(2000f, 0f), swell, 0f, 100f, shore).Cell);
    }

    /// <summary>The engine's lookups as the capture writes them, out of tests/fixtures/water-lookups.</summary>
    private static byte[]? Engine(string path)
    {
        string file = Path.Combine(ComponentSweepTests.Fixture("water-lookups"), "water-lookup-" + Path.GetFileName(path).ToLowerInvariant());
        return File.Exists(file) ? File.ReadAllBytes(file) : null;
    }

    private const int ProfileWidth = 64;
    private const int ProfileHeight = 8;

    /// <summary>An install holding a ripple texture whose green and blue are its red's slope, a decoy profile, the swell's profile and its gradient half as wide.</summary>
    internal static byte[]? Install(string path)
    {
        if (Same(path, WaterLookups.RipplePath))
        {
            return RippleFile(slope: true);
        }

        if (Same(path, WaterLookups.ProfilePaths[0]))
        {
            // A DECOY: a profile whose rise is no part of the gradient.
            return Legacy(ProfileWidth, ProfileHeight, 116, Texels(ProfileWidth, ProfileHeight, (x, y) => (MathF.Cos(9f * x * y), 0f, 0f, 1f)));
        }

        if (Same(path, WaterLookups.ProfilePaths[1]))
        {
            return Legacy(ProfileWidth, ProfileHeight, 116, Texels(ProfileWidth, ProfileHeight, (x, y) => (Profile(x, y), 0.25f, 0.5f, 0.75f)));
        }

        if (Same(path, WaterLookups.GradientPath))
        {
            const int width = ProfileWidth / 2;
            return Legacy(width, ProfileHeight, 115, Texels(width, ProfileHeight, (x, y) => (Rise((x + 0.5f) / width, y), 0f, 0f, 1f), channels: 2));
        }

        return null;
    }

    private static bool Same(string path, string other) => string.Equals(path, other, StringComparison.OrdinalIgnoreCase);

    private static float Profile(int x, int y) => 0.5f + (0.1f * (1 + y) * MathF.Sin(2f * MathF.PI * (x + 0.5f) / ProfileWidth));

    /// <summary>The profile's rise a unit of phase.</summary>
    private static float Rise(float u, int y) => 0.1f * (1 + y) * 2f * MathF.PI * MathF.Cos(2f * MathF.PI * u);

    private static float Ripple(int x, int y, int channel)
    {
        const float k = 2f * MathF.PI * 3f / 128f, l = 2f * MathF.PI * 2f / 128f;
        return channel switch
        {
            0 => 0.5f + (0.3f * MathF.Sin(k * x) * MathF.Cos(l * y)),
            1 => 0.3f * k * MathF.Cos(k * x) * MathF.Cos(l * y),
            2 => -0.3f * l * MathF.Sin(k * x) * MathF.Sin(l * y),
            _ => 1f,
        };
    }

    private static byte[] RippleFile(bool slope) => Legacy(128, 128, 116, Texels(128, 128, (x, y) =>
        slope ? (Ripple(x, y, 0), Ripple(x, y, 1), Ripple(x, y, 2), 1f)
              : (Ripple(x, y, 0), MathF.Sin(x * y * 0.37f), MathF.Cos((x * 7) + (y * 13)), 1f)));

    private static byte[] Texels(int width, int height, Func<int, int, (float R, float G, float B, float A)> texel, int channels = 4)
    {
        var bytes = new byte[width * height * channels * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                (float r, float g, float b, float a) = texel(x, y);
                float[] values = [r, g, b, a];
                for (var channel = 0; channel < channels; channel++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan((((y * width) + x) * channels + channel) * 4), values[channel]);
                }
            }
        }

        return bytes;
    }

    private static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var at = 0; at < values.Length; at++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at * 4), values[at]);
        }

        return bytes;
    }

    private static byte[] Halves(params float[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var at = 0; at < values.Length; at++)
        {
            BinaryPrimitives.WriteHalfLittleEndian(bytes.AsSpan(at * 2), (Half)values[at]);
        }

        return bytes;
    }

    /// <summary>A DDS whose four-character code is an old Direct3D format number, its texels straight after the header.</summary>
    internal static byte[] Legacy(int width, int height, uint code, byte[] texels)
    {
        byte[] dds = Header(width, height, code);
        return [.. dds, .. texels];
    }

    /// <summary>A DDS behind DX10's header, of a DXGI format.</summary>
    private static byte[] Dx10(int width, int height, uint format, byte[] texels)
    {
        byte[] dds = Header(width, height, 0x30315844);   // "DX10"
        var extra = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(extra, format);
        BinaryPrimitives.WriteUInt32LittleEndian(extra.AsSpan(4), 3);   // a texture of two dimensions
        BinaryPrimitives.WriteUInt32LittleEndian(extra.AsSpan(12), 1);  // one of them
        return [.. dds, .. extra, .. texels];
    }

    private static byte[] Header(int width, int height, uint code)
    {
        var dds = new byte[128];
        "DDS "u8.CopyTo(dds);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(4), 124);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(8), 0x1007);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(12), height);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(16), width);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(28), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(76), 32);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(80), 0x4);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(84), code);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(108), 0x1000);
        return dds;
    }
}
