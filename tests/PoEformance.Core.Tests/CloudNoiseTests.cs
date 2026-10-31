using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>The clouds' noise measured against the game's picture - see CloudNoise.</summary>
public sealed class CloudNoiseTests
{
    private static readonly CloudNoise.Clouds RedVale = new(0.8f, 0.9f, 0.55f, 0.75f, 30f, 2.939f, 800f, 0.15f, 0.15f);

    /// <summary>
    /// Ground lit through one candidate's clouds, laid by one way of making clouds_velocity and one slide along it, is found to be that
    /// candidate, that way and that slide - and a candidate of other blobs stands well under it.
    /// </summary>
    [Fact]
    public void THEGROUNDSLightFollowsTheCandidateItWasLitThroughAtItsWayAndSlide()
    {
        GamePicture truth = Blobs(1), other = Blobs(2);
        var sun = Vector3.Normalize(new Vector3(0.3f, -0.4f, 1f));
        var velocity = new Vector2(MathF.Cos(RedVale.Angle), MathF.Sin(RedVale.Angle));
        Vector2 x = Vector2.Normalize(velocity), y = new(-x.Y, x.X);
        const float slide = 0.3f;
        float frequency = 0.001f * RedVale.Scale;
        float[] alpha = [.. Enumerable.Range(0, truth.Width * truth.Height).Select(at => truth.Rgba[(at * 4) + 3] / 255f)];
        var random = new Random(7);
        var points = new List<CloudNoise.Point>();
        Vector2 up = new Vector2(sun.X, sun.Y) / sun.Z * CloudNoise.Height;
        for (var at = 0; at < 4000; at++)
        {
            var area = new Vector2(random.NextSingle() * 4000f, random.NextSingle() * 4000f);
            Vector2 reference = area - up;
            float a = CloudNoise.Sample(alpha, truth.Width, truth.Height, (Vector2.Dot(x, reference) * frequency) + slide, Vector2.Dot(y, reference) * frequency);
            float game = 0.3f + (0.7f * CloudNoise.Factor(RedVale, a, 0f)) + ((random.NextSingle() - 0.5f) * 0.05f);
            points.Add(new CloudNoise.Point(area, game));
        }

        string said = CloudNoise.Measure(RedVale, sun, new Vector2(1e7f), points, [("Art/truth.dds", truth), ("Art/other.dds", other), ("Art/missing.dds", null)]);

        Assert.True(said.Contains("taken: Art/truth.dds - clear", StringComparison.Ordinal), said);
        Assert.Matches(@"candidate\tArt/truth\.dds\t64x64\tbest r 0\.9\d+ by clouds_velocity = speed \(cos a, sin a\), slid 0\.(29|30)\d+", said);
        Assert.Contains("candidate\tArt/missing.dds\tdid not read", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// math_utils' SmoothStep as its formula has it - the centre goes to itself, the ends to nought and one, rising between - and the factor is
    /// one with no intensity, the player's fade one at him and nought past the radius.
    /// </summary>
    [Fact]
    public void THEFACTORIsGetCloudsIntensitys()
    {
        Assert.Equal(0.55f, CloudNoise.MathSmoothStep(0.55f, 0.55f, 0.75f), 5);
        Assert.Equal(0f, CloudNoise.MathSmoothStep(0f, 0.55f, 0.75f), 4);
        Assert.Equal(1f, CloudNoise.MathSmoothStep(1f, 0.55f, 0.75f), 4);
        float last = -1f;
        foreach (float value in new[] { 0.1f, 0.3f, 0.5f, 0.6f, 0.8f, 0.95f })
        {
            float shaped = CloudNoise.MathSmoothStep(value, 0.55f, 0.75f);
            Assert.True(shaped > last, $"{value}: {shaped} after {last}");
            last = shaped;
        }

        Assert.Equal(1f, CloudNoise.Factor(RedVale with { Intensity = 0f }, 0.2f, 0f));
        Assert.Equal(1f, CloudNoise.Fade(0f, 800f));
        Assert.Equal(0f, CloudNoise.Fade(900f, 800f));
        Assert.True(CloudNoise.Factor(RedVale, 0.05f, 0f) < CloudNoise.Factor(RedVale, 0.95f, 0f));
    }

    /// <summary>Smooth blobs in the alpha: a wrapping grid of eight by eight random values, read bilinearly at 64 a side.</summary>
    private static GamePicture Blobs(int seed)
    {
        var random = new Random(seed);
        var grid = new float[64];
        for (var at = 0; at < grid.Length; at++)
        {
            grid[at] = random.NextSingle();
        }

        var rgba = new byte[64 * 64 * 4];
        for (var row = 0; row < 64; row++)
        {
            for (var column = 0; column < 64; column++)
            {
                float value = CloudNoise.Sample(grid, 8, 8, (column + 0.5f) / 64f, (row + 0.5f) / 64f);
                int to = ((row * 64) + column) * 4;
                rgba[to] = rgba[to + 1] = rgba[to + 2] = 128;
                rgba[to + 3] = (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
            }
        }

        return new GamePicture(64, 64, rgba);
    }
}
