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
            string text = CaptureWaterTextures.Write(
                    Install, [Noise, Big, Ripple, Missing, Ripple], [program], out IReadOnlyList<WaterTexture> pictures, out IReadOnlyList<WaterLookupFile> lookups)
                .ReplaceLineEndings("\n");

            Assert.StartsWith(
                "4 textures name the water, 3 read, 1 at the ripple's 128 square, 2 decoded; 3 strings in the program name them; 6 lookups, 0 read as floats - ",
                text, StringComparison.Ordinal);
            Assert.Empty(lookups);

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
        string text = CaptureWaterTextures.Write(Install, [], [], out IReadOnlyList<WaterTexture> pictures, out IReadOnlyList<WaterLookupFile> lookups);

        Assert.StartsWith("no water textures listed; no program to search", text, StringComparison.Ordinal);
        Assert.Empty(pictures);
        Assert.Empty(lookups);
    }

    [Fact]
    public void THEENGINESLookupsAreReadAsFloatsWrittenWholeAndHeldAgainstWhatTheWaterTakes()
    {
        const string noise = "Art/2DArt/Lookup/noise_curl3.dds";
        string program = Path.Combine(Path.GetTempPath(), $"poef-water-program-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(program, [.. Encoding.ASCII.GetBytes("xyArt/2DArt/Lookup/gerstner_wave_profile.dds"), 0, .. Encoding.ASCII.GetBytes(noise), 0]);
        try
        {
            string text = CaptureWaterTextures.Write(
                    WaterLookupsTests.Install, ["art/2dart/lookup/surface_ripple.dds", "art/textures/water.dds"], [program],
                    out _, out IReadOnlyList<WaterLookupFile> lookups)
                .ReplaceLineEndings("\n");

            // EVERY LOOKUP the walk or the program names, and those the water and the beach waves read, once each - the program's prefix cut off.
            Assert.Contains("; 7 lookups, 4 read as floats - the ripples over the engine's ripple texture (surface_ripple.dds); the swell's cross-section the engine's (wave_profile.dds,", text, StringComparison.Ordinal);
            Assert.Contains("\nArt/2DArt/Lookup/noise_curl3.dds: not in the install\n", text, StringComparison.Ordinal);
            Assert.Contains("  read as 128 x 128, Direct3D format 116 (A32B32G32R32F)\n", text, StringComparison.Ordinal);
            Assert.Contains("  written whole: water-lookup-surface_ripple.dds\n", text, StringComparison.Ordinal);
            Assert.Equal(
                ["water-lookup-gerstner_wave_profile.dds", "water-lookup-surface_ripple.dds", "water-lookup-wave_profile.dds", "water-lookup-wave_profile_gradient.dds"],
                lookups.Select(one => one.File).Order(StringComparer.Ordinal));

            // WHAT THE WATER TAKES, and what it draws with now - the stand-ins, nothing having been handed over in a test.
            Assert.Contains("=== what the water takes of them (WaterLookups), and why\n", text, StringComparison.Ordinal);
            Assert.Contains("TAKEN as the swell's cross-section: wave_profile.dds with wave_profile_gradient.dds\n", text, StringComparison.Ordinal);
            Assert.Contains("now drawn: the ripples over a noise of this tool's", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(program);
        }
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
