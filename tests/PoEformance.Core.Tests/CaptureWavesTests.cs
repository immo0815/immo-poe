using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>The beach waves' section of the water textures part: the graphs the program names, followed through what they name.</summary>
public class CaptureWavesTests
{
    /// <summary>
    /// A graph the program names is written whole, and so is a graph it names in turn - once, under the heading the replay reads files
    /// back by - while a texture it names gets its header, and a graph not in the install says so.
    /// </summary>
    [Fact]
    public void THEBEACHWavesGraphsAreWrittenWholeAndFollowedThrough()
    {
        const string breaking = "Metadata/Effects/Graphs/Environment/BreakingOceanWaves.fxgraph";
        const string foam = "Metadata/Effects/Graphs/Environment/BeachFoam.fxgraph";
        var install = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [breaking] = """{"nodes":[{"type":"BreakingWavesFragment","parameters":{"path":"Art/Textures/Environment/Global/BreakingWaves/SeaFoam_colour_Uncompressed.dds"}}],"parent":"Metadata/Effects/Graphs/Environment/BeachFoam.fxgraph"}""",
            [foam] = """{"nodes":[{"type":"BeachFoamSurface","again":"Metadata/Effects/Graphs/Environment/BreakingOceanWaves.fxgraph"}]}""",
        };
        byte[]? Read(string path) => install.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null;

        var said = new StringBuilder();
        string line = CaptureWaves.Write(Read, [], [breaking, "  Metadata/Effects/Graphs/Environment/FloodWaves.fxgraph  "], said);
        string text = said.ToString();

        // THE THREE KNOWN, THE ONE THE PROGRAM ADDS AND THE ONE A GRAPH NAMES - each once, the two in the install whole.
        Assert.Equal("beach waves: 2 of 5 graphs read, 1 textures named", line);
        Assert.Equal(1, Count(text, "##### " + breaking + "  (named by the game's program)"));
        Assert.Equal(1, Count(text, "##### " + foam + "  (named by " + breaking + ")"));
        Assert.Contains("##### Metadata/Effects/Graphs/Environment/FloodWaves.fxgraph  (named by the game's program)\n(not in the install)", text.Replace("\r", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("\"type\":\"BeachFoamSurface\"", text, StringComparison.Ordinal);
        Assert.Contains("SeaFoam_colour_Uncompressed.dds  (" + breaking + "): ", text, StringComparison.Ordinal);

        // AND THE REPLAY READS THEM BACK by their headings.
        string replay = CaptureReplayTests.WavesReplay(text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'));
        Assert.StartsWith("beach waves: 5 graphs written whole", replay, StringComparison.Ordinal);
    }

    private static int Count(string text, string part)
    {
        var count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
