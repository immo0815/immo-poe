using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's water textures part: the textures naming the water read for their size, the ripple's 128 square marked and decoded, and the program's strings naming one.
/// </summary>
public sealed class CaptureWaterTexturesTests
{
    private const string Ripple = "Art/Textures/Engine/Water/ripple_height.dds";
    private const string Big = "Art/Textures/Environment/ocean_floor.dds";
    private const string Noise = "Art/Textures/Misc/noise_soft.dds";
    private const string Missing = "Art/Textures/Misc/water_missing.dds";

    [Fact]
    public void THERIPPLESSizeIsMarkedAndDecodedFirstAndTheProgramsStringsNamingOneAreListed()
    {
        string program = Path.Combine(Path.GetTempPath(), $"poef-water-program-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(program, Program());
        try
        {
            string text = CaptureWaterTextures.Write(Install, [Noise, Big, Ripple, Missing, Ripple], [program], out IReadOnlyList<WaterTexture> pictures)
                .ReplaceLineEndings("\n");

            Assert.StartsWith(
                "4 textures name the water, 3 read, 1 at the ripple's 128 square, 2 decoded; 3 strings in the program name them\n", text, StringComparison.Ordinal);

            // THE STRONGEST WORDS FIRST, the ripple's size marked.
            Assert.True(text.IndexOf("<<" + Ripple, StringComparison.Ordinal) < text.IndexOf("  " + Big, StringComparison.Ordinal));
            Assert.Contains("  " + Missing + "  (did not read)", text, StringComparison.Ordinal);

            // THE RIPPLE'S SIZE DECODED FIRST, the one past the widest not at all.
            Assert.Equal([Ripple, Noise], pictures.Select(one => one.Path));
            Assert.Equal("water-texture-1-ripple_height-r.png", pictures[0].FileOf('r'));
            Assert.Equal(128, pictures[0].Picture.Width);
            Assert.Contains("1. " + Ripple + " - 128 x 128", text, StringComparison.Ordinal);

            // THE PROGRAM'S STRINGS, a path first, one byte or two a character, at either alignment.
            Assert.Contains("  Art/Textures/Engine/water_surface.dds\n", text, StringComparison.Ordinal);
            Assert.Contains("  ripple heightfield and gradient\n", text, StringComparison.Ordinal);
            Assert.Contains("  OceanWaves  (x2)\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("unrelated", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(program);
        }
    }

    [Fact]
    public void BEFORETheInstallWalkHasRunItSaysSo()
    {
        string text = CaptureWaterTextures.Write(Install, [], [], out IReadOnlyList<WaterTexture> pictures);

        Assert.StartsWith("no water textures listed; no program to search", text, StringComparison.Ordinal);
        Assert.Empty(pictures);
    }

    /// <summary>A program's bytes: a path in single bytes, a phrase in two at an odd offset, one word twice, and strings with none.</summary>
    private static byte[] Program()
    {
        var bytes = new List<byte>();
        bytes.AddRange([0x4D, 0x5A, 0x90, 0x00, 0x03]);
        bytes.AddRange(Encoding.ASCII.GetBytes("Art/Textures/Engine/water_surface.dds"));
        bytes.Add(0);
        bytes.AddRange(Encoding.ASCII.GetBytes("unrelated text here"));
        bytes.AddRange([0x00, 0xFF, 0x01]);
        bytes.AddRange(Encoding.Unicode.GetBytes("ripple heightfield and gradient"));
        bytes.AddRange([0x00, 0x00, 0x07]);
        bytes.AddRange(Encoding.ASCII.GetBytes("OceanWaves"));
        bytes.Add(0);
        bytes.AddRange(Encoding.ASCII.GetBytes("OceanWaves"));
        bytes.Add(0);
        bytes.AddRange(Encoding.ASCII.GetBytes("wave"));
        bytes.Add(0);
        return [.. bytes];
    }

    private static byte[]? Install(string path) => path switch
    {
        Ripple => GameArtTests.Dds(128, 128, Texels(128 * 128)),
        Noise => GameArtTests.Dds(64, 32, Texels(64 * 32)),

        // PAST THE WIDEST DECODED: the header alone is read, its texels never.
        Big => GameArtTests.Dds(2048, 2048, []),
        _ => null,
    };

    private static byte[] Texels(int count)
    {
        var bgra = new byte[count * 4];
        for (var at = 0; at < bgra.Length; at++)
        {
            bgra[at] = (byte)(at * 7);
        }

        return bgra;
    }
}
