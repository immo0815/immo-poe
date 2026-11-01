using System.Globalization;
using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// A light's style (v0.1.414): style 1 runs its colour through 25 keys at ten a second - MEASURED off the game's own light list, where the
/// Exile's Staff's lamp stood every tick of the 08:31 capture's bone watch.
/// </summary>
public sealed class LightStyleTests
{
    /// <summary>
    /// The keys stand at every tenth of a second, joined by straight lines, and come round every 2.5 s; no style, and any style not measured,
    /// is the colour as written.
    /// </summary>
    [Fact]
    public void STYLEOneRunsTwentyFiveKeysTenASecondJoinedByStraightLines()
    {
        Assert.Equal(1f, LightStyle.Factor(1, 0), 5);
        Assert.Equal(0.84f, LightStyle.Factor(1, 0.1), 5);
        Assert.Equal(0.80f, LightStyle.Factor(1, 0.2), 5);
        Assert.Equal(0.72f, LightStyle.Factor(1, 0.3), 5);
        Assert.Equal(0.92f, LightStyle.Factor(1, 0.05), 4);
        Assert.Equal(0.92f, LightStyle.Factor(1, 2.45), 4);
        Assert.Equal(2.5f, LightStyle.Period, 5);
        for (var k = 0; k < 25; k++)
        {
            Assert.Equal(LightStyle.Factor(1, k / 10.0), LightStyle.Factor(1, (k / 10.0) + 2.5), 4);
            Assert.Equal(LightStyle.Factor(1, k / 10.0), LightStyle.Factor(1, (k / 10.0) - 7.5), 4);
        }

        Assert.Equal(1f, LightStyle.Factor(0, 1.234));
        Assert.Equal(1f, LightStyle.Factor(2, 1.234));
        Assert.Equal(1f, LightStyle.Factor(1, double.NaN));
        Assert.True(LightStyle.Measured(1) && LightStyle.Measured(0) && !LightStyle.Measured(3));
        Assert.True(LightStyle.Flickers(1) && !LightStyle.Flickers(0));
    }

    /// <summary>
    /// THE GAME'S OWN LAMP: the factor its light list held every tick for 10.8 s is style 1's loop to under a hundredth everywhere, once the
    /// loop is set where the game's stood - which no file says.
    /// </summary>
    [Fact]
    public void THEGAMESLampFlickersAsStyleOne()
    {
        var reads = new List<(double At, float Factor)>();
        foreach (string line in File.ReadLines(ComponentSweepTests.Fixture(Path.Combine("light-style", "staff-lamp-flicker.tsv"))))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            reads.Add((double.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture)));
        }

        Assert.True(reads.Count > 1000, $"{reads.Count} reads");
        Assert.InRange(reads.Min(one => one.Factor), 0.719f, 0.721f);
        Assert.InRange(reads.Max(one => one.Factor), 0.99f, 1.001f);

        // WHERE THE LOOP STOOD: every millisecond of a period tried, the one leaving least taken.
        double best = double.MaxValue, worst = 0, shift = 0;
        for (var ms = 0; ms < 2500; ms++)
        {
            double offset = ms / 1000.0, sum = 0, most = 0;
            foreach ((double at, float factor) in reads)
            {
                double gap = Math.Abs(LightStyle.Factor(1, at + offset) - factor);
                sum += gap * gap;
                most = Math.Max(most, gap);
            }

            double rms = Math.Sqrt(sum / reads.Count);
            if (rms < best)
            {
                (best, worst, shift) = (rms, most, offset);
            }
        }

        Assert.True(best < 0.003, $"the loop at {shift:0.000} s leaves {best:0.0000} at the root mean square");
        Assert.True(worst < 0.012, $"the loop at {shift:0.000} s leaves {worst:0.0000} at most");
    }

    /// <summary>
    /// A carried light's style is read from its .ao and flickers its colour as the picture's clock runs; NaN, no clock, is the colour as written.
    /// </summary>
    [Fact]
    public void ACARRIEDLightsStyleFlickersItsColourWithTheClock()
    {
        var lamp = new CarriedLight(-1, new Vector3(1f, 2f, 3f), new Vector3(2.4671f, 0.493089f, 0.0398032f), 150f, 0.5f, "lamp", string.Empty) { Style = 1 };
        var plain = lamp with { Style = 0 };
        var at = new PointLight[2];
        Assert.Equal(2, CarriedLights.At([lamp, plain], [], Matrix4x4.Identity, at, 0.3));
        Assert.Equal(lamp.Colour * 0.72f, at[0].Colour);
        Assert.Equal(1, at[0].Style);
        Assert.Equal(plain.Colour, at[1].Colour);
        Assert.Equal(2, CarriedLights.At([lamp, plain], [], Matrix4x4.Identity, at));
        Assert.Equal(lamp.Colour, at[0].Colour);
        Assert.True(CarriedLights.Flicker([plain, lamp]));
        Assert.False(CarriedLights.Flicker([plain]));
    }
}
