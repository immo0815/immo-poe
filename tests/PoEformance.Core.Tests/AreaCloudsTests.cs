using System.Numerics;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>The clouds' shadow on the sun's light - see AreaClouds, which the card's Clouded follows line for line.</summary>
public sealed class AreaCloudsTests
{
    private static readonly AreaClouds RedVale = new(0.8f, 0.9f, 0.55f, 0.75f, 30f, 2.939f, 800f, 0.15f, 0.15f);

    /// <summary>
    /// GetCloudsIntensity as shadows.ffx writes it, transcribed apart from AreaClouds - float3 for float3, its own player fade, its own
    /// SmoothStep - over the same alpha read, at places near and far from the player, the light both ways round, through time.
    /// </summary>
    [Fact]
    public void GETCLOUDSIntensityIsShadowsFfxsAsWritten()
    {
        float[] alpha = Blobs(64, 3);
        var player = new Vector2(1500f, -700f);
        foreach (Vector3 light in (Vector3[])[Vector3.Normalize(new Vector3(0.3f, -0.4f, 1f)), Vector3.Normalize(new Vector3(-0.8f, -0.2f, -0.5f))])
        {
            foreach (float time in (float[])[0f, 2.5f, 47f])
            {
                for (var at = 0; at < 40; at++)
                {
                    var area = new Vector2(1500f + (at * 37f) - 700f, -700f + (at * at * 1.3f) - 500f);
                    float engine = Engine(new Vector3(area, -120f), light, RedVale.Velocity, time, RedVale, player, (u, v) => AreaClouds.Sample(alpha, 64, 64, u, v));
                    float tool = RedVale.At(area, light, player, time, alpha, 64, 64);
                    Assert.True(MathF.Abs(engine - tool) < 1e-5f, $"at {area}, time {time}, light {light}: the engine's {engine}, AreaClouds' {tool}");
                }
            }
        }
    }

    /// <summary>
    /// The noise slides along the clouds' x axis by their speed times the clock, so the pattern at a time is the pattern at nought moved back
    /// along that axis by as much: the place a cloud stood at nought it stands at less x speed t. Far from the player, where nothing fades it.
    /// </summary>
    [Fact]
    public void THEPATTERNMovesAsTheEnginesTextureSlides()
    {
        float[] alpha = Blobs(64, 5);
        var light = Vector3.Normalize(new Vector3(0.2f, 0.5f, 1f));
        var far = new Vector2(1e7f, 1e7f);
        (Vector2 along, float speed) = RedVale.Drift;
        Assert.Equal(30f, speed, 4);
        Assert.True(Vector2.Distance(new Vector2(MathF.Cos(RedVale.Angle), MathF.Sin(RedVale.Angle)), along) < 1e-6f, $"the noise's x axis {along}");
        var moved = 0;
        for (var at = 0; at < 60; at++)
        {
            var area = new Vector2(at * 53f, (at % 7) * 211f);
            const float time = 9.5f;
            float before = RedVale.At(area, light, far, 0f, alpha, 64, 64);
            float after = RedVale.At(area - (along * speed * time), light, far, time, alpha, 64, 64);
            Assert.True(MathF.Abs(before - after) < 1e-4f, $"at {area}: {before} at nought, {after} moved back by the slide");
            moved += MathF.Abs(RedVale.At(area, light, far, time, alpha, 64, 64) - before) > 1e-3f ? 1 : 0;
        }

        Assert.True(moved > 30, $"{moved} of 60 places changed with the clock");
    }

    /// <summary>
    /// The light's sign does not matter (the step up the light is over its own z), a sun at the horizon lays nothing, nor do clouds of no
    /// intensity; the player's own place takes both fades whole.
    /// </summary>
    [Fact]
    public void THELIGHTSWayAndTheHorizonAndThePlayer()
    {
        float[] alpha = Blobs(64, 9);
        var light = Vector3.Normalize(new Vector3(0.6f, -0.3f, 0.9f));
        var area = new Vector2(320f, 1290f);
        Assert.Equal(RedVale.At(area, light, area, 3f, alpha, 64, 64), RedVale.At(area, -light, area, 3f, alpha, 64, 64), 6);
        Assert.Equal(1f, RedVale.At(area, new Vector3(1f, 0f, 0f), area, 3f, alpha, 64, 64));
        Assert.Equal(1f, (RedVale with { Intensity = 0f }).At(area, light, new Vector2(1e7f), 3f, alpha, 64, 64));
        Assert.Equal(1f, AreaClouds.Fade(0f, 800f));
        Assert.Equal(0f, AreaClouds.Fade(800f, 800f));

        // AT THE PLAYER the fade is one: the noise lifted by pre_fade before the step and the step by post_fade after it.
        Vector2 spot = RedVale.NoiseAt(area, light, 3f)!.Value;
        float read = AreaClouds.Sample(alpha, 64, 64, spot.X, spot.Y);
        float faded = Math.Clamp(read + RedVale.PreFade, 0f, 1f);
        float expected = 1f + ((Math.Clamp(AreaClouds.MathSmoothStep(faded, RedVale.Midpoint, RedVale.Sharpness) + RedVale.PostFade, 0f, 1f) - 1f) * RedVale.Intensity);
        Assert.Equal(expected, RedVale.At(area, light, area, 3f, alpha, 64, 64), 6);
    }

    /// <summary>The Red Vale's clouds as its .env writes them, the fade radius left to the engine's measured 800; a key left out is no clouds, said why.</summary>
    [Fact]
    public void THEREDVALESCloudsAreReadFromItsEnvironment()
    {
        const string environment = """
            {
              "clouds": { "clouds_scale": 0.8, "clouds_intensity": 0.9, "clouds_midpoint": 0.55, "clouds_sharpness": 0.75, "clouds_speed": 30.0,
                          "clouds_angle": 2.939, "clouds_pre_fade": 0.15, "clouds_post_fade": 0.15 }
            }
            """;
        AreaClouds? read = AreaClouds.Of(EnvironmentSettings.Read("Metadata/EnvironmentSettings/Gallows/1_5.env", System.Text.Encoding.UTF8.GetBytes(environment)), out string why);
        Assert.Equal(RedVale, read);
        Assert.Contains("clouds_fade_radius not set - the engine's 800 (measured)", why, StringComparison.Ordinal);

        AreaClouds? none = AreaClouds.Of(
            EnvironmentSettings.Read("partial.env", System.Text.Encoding.UTF8.GetBytes("""{ "clouds": { "clouds_scale": 0.8 } }""")), out string noneWhy);
        Assert.Null(none);
        Assert.Contains("leaves out clouds_intensity", noneWhy, StringComparison.Ordinal);
    }

    /// <summary>GetCloudsIntensity transcribed from shadows.ffx alone: its float3s, its player fade, math_utils' SmoothStep.</summary>
    private static float Engine(Vector3 position, Vector3 lightDir, Vector2 velocity, float time, AreaClouds clouds, Vector2 player, Func<float, float, float> sample)
    {
        float playerDist = (player - new Vector2(position.X, position.Y)).Length();
        float t = Math.Clamp(1f - (playerDist / clouds.FadeRadius), 0f, 1f);
        float playerFade = t * t * (3f - (2f * t));
        float cloudSpeed = velocity.Length();
        Vector3 xVec = cloudSpeed > 0.1f ? Vector3.Normalize(new Vector3(velocity, 0f)) : Vector3.UnitX;
        Vector3 yVec = Vector3.Normalize(new Vector3(-xVec.Y, xVec.X, 0f));
        const float cloudsHeight = 1000f;
        Vector3 cloudsRefPos = position - (lightDir / lightDir.Z * cloudsHeight);
        var lightTexPos = new Vector2(Vector3.Dot(xVec, cloudsRefPos), Vector3.Dot(yVec, cloudsRefPos));
        float freq = 0.001f * clouds.Scale;
        Vector2 uv = (lightTexPos + (new Vector2(cloudSpeed, 0f) * time)) * freq;
        float fadedNoise = Saturate(sample(uv.X, uv.Y) + (playerFade * clouds.PreFade));
        return Lerp(1f, Saturate(SmoothStep(fadedNoise, clouds.Midpoint, clouds.Sharpness) + (playerFade * clouds.PostFade)), clouds.Intensity);

        static float SmoothStep(float val, float center, float curve)
        {
            float power = (2f / MathF.Max(1e-3f, 1f - curve)) - 1f;
            return Saturate(1f - center
                + (-MathF.Pow(Saturate(1f - MathF.Max(center, val)), power) * MathF.Pow(Saturate(1f - center), -(power - 1f)))
                + (MathF.Pow(Saturate(MathF.Min(center, val)), power) * MathF.Pow(Saturate(center), -(power - 1f))));
        }

        static float Lerp(float a, float b, float s) => a + (s * (b - a));
    }

    private static float Saturate(float value) => Math.Clamp(value, 0f, 1f);

    /// <summary>Smooth blobs: a wrapping grid of eight by eight random values read bilinearly, <paramref name="side"/> a side.</summary>
    private static float[] Blobs(int side, int seed)
    {
        var random = new Random(seed);
        var grid = new float[64];
        for (var at = 0; at < grid.Length; at++)
        {
            grid[at] = random.NextSingle();
        }

        var alpha = new float[side * side];
        for (var row = 0; row < side; row++)
        {
            for (var column = 0; column < side; column++)
            {
                alpha[(row * side) + column] = AreaClouds.Sample(grid, 8, 8, (column + 0.5f) / side, (row + 0.5f) / side);
            }
        }

        return alpha;
    }
}
