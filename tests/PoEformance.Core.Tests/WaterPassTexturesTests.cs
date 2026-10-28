using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The water's two unnamed pass textures' candidates (WaterPassTextures): each measured as water.ffx reads it - a distortion's red and
/// green as a vector centred on nought, the caustics' green - whether it tiles, and each written whole beside the part.
/// </summary>
public sealed class WaterPassTexturesTests
{
    /// <summary>
    /// A vector field that wraps reads as one: its seam a step like any inside, its red and green unalike, its mean vector nought; a ramp
    /// that does not wrap has a seam many steps high, and a grey picture's red and green are one.
    /// </summary>
    [Fact]
    public void ADISTORTIONThatTilesIsToldFromARampAndAGreyPicture()
    {
        const int side = 64;
        GamePicture Picture(Func<int, int, (double R, double G)> texel)
        {
            var rgba = new byte[side * side * 4];
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    (double r, double g) = texel(x, y);
                    int at = ((y * side) + x) * 4;
                    rgba[at] = (byte)Math.Round(r * 255);
                    rgba[at + 1] = (byte)Math.Round(g * 255);
                    rgba[at + 3] = 255;
                }
            }

            return new GamePicture(side, side, rgba);
        }

        // WAVES ACROSS THE PICTURE, so every edge crosses every phase of them, as a noise's edges do.
        GamePicture field = Picture((x, y) => (
            0.5 + (0.4 * Math.Sin(2 * Math.PI * (x + (2 * y)) / side)),
            0.5 + (0.4 * Math.Cos(2 * Math.PI * ((2 * x) - y) / side))));
        string[] lines = [.. WaterPassTextures.Lines(field, distortion: true)];
        Assert.Equal("  read as 64 x 64", lines[0]);
        Assert.InRange(Seam(lines[1]), 0.7, 1.3);
        Assert.InRange(Seam(lines[2]), 0.7, 1.3);
        Assert.InRange(After(lines[5], "correlation "), -0.01, 0.01);
        Assert.StartsWith("  as the distortion (rg * 2 - 1): the mean vector (", lines[6], StringComparison.Ordinal);
        Assert.InRange(After(lines[6], "mean vector ("), -0.01, 0.01);

        GamePicture ramp = Picture((x, y) => (x / (double)(side - 1), x / (double)(side - 1)));
        string[] ramped = [.. WaterPassTextures.Lines(ramp, distortion: false)];
        Assert.True(Seam(ramped[1]) > 20, ramped[1]);
        Assert.InRange(After(ramped[5], "correlation "), 0.999, 1.001);
        Assert.StartsWith("  as the caustics (pow(g, 1 / 4.2) * 2 - 1): the bias's mean ", ramped[6], StringComparison.Ordinal);
    }

    /// <summary>
    /// The candidates are read from the install and written whole under a number of their own, the program's names of the same words
    /// added; a lookup is left to the lookups' own section to write.
    /// </summary>
    [Fact]
    public void THECANDIDATESAreWrittenWholeUnderANumberOfTheirOwn()
    {
        byte[] dds = GameArtTests.Dds(8, 8, new byte[8 * 8 * 4]);
        byte[]? Install(string path) =>
            path is "Art/particles/distortion/muddle_double.dds" or "Art/2DArt/Lookup/noise_curl3.dds" or "Art/Misc/odd_caustic_light.dds" ? dds : null;

        var said = new StringBuilder();
        var files = new List<WaterLookupFile>();
        string summary = WaterPassTextures.Write(Install, ["Art/Misc/odd_caustic_light.dds", "Art/UI/water_icon.dds"], said, files);

        int all = WaterPassTextures.Caustics.Count + WaterPassTextures.Distortions.Count + 1;
        Assert.Equal($"{all} pass-texture candidates, 3 in the install, 3 measured", summary);
        int muddle = WaterPassTextures.Caustics.Count + 1;
        Assert.Equal(
            [$"water-pass-{muddle}-muddle_double.dds", $"water-pass-{all}-odd_caustic_light.dds"],
            files.Select(one => one.File));
        string text = said.ToString().ReplaceLineEndings("\n");
        Assert.Contains("\ndistortion? Art/particles/distortion/muddle_double.dds: ", text, StringComparison.Ordinal);
        Assert.Contains("\ncaustics? Art/Misc/odd_caustic_light.dds: ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("water_icon", text, StringComparison.Ordinal);
    }

    private static double Seam(string line) => After(line, "seam ");

    /// <summary>The number right after a word in a line.</summary>
    private static double After(string line, string word)
    {
        int start = line.LastIndexOf(word, StringComparison.Ordinal) + word.Length;
        int end = start;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] is '.' or '-'))
        {
            end++;
        }

        return double.Parse(line[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }
}
