using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The particles part read back from a capture: particles-replay.txt, every graph particles.txt writes whole compiled again by today's
/// compile - so a particle node the compile learns is held against the game's own particle graphs without the game.
/// </summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THEPARTICLEGraphsAreCompiledAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, CaptureReport.ParticlesFile)))
        {
            return;
        }

        string replay = ParticlesReplay(File.ReadAllLines(CaptureParts.Find(folder, CaptureReport.ParticlesFile)));
        File.WriteAllText(Path.Combine(folder, "particles-replay.txt"), replay);
        Assert.StartsWith("particles replay: ", replay, StringComparison.Ordinal);
    }

    /// <summary>The graphs the part wrote whole, by today's compile: each one's nodes by type and the node types it does not know.</summary>
    internal static string ParticlesReplay(string[] lines)
    {
        Dictionary<string, string> graphs = WrittenWhole(lines)
            .Where(one => one.Key.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(one => one.Key, one => one.Value, StringComparer.OrdinalIgnoreCase);
        var body = new StringBuilder();
        string said = CaptureRenderDebug.Compiled(graphs, body);
        return $"particles replay: {graphs.Count} graphs read back, {said}" + Environment.NewLine + body;
    }
}
