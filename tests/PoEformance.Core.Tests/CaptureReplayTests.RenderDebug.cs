using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The engine's debug views read back from a capture: render-debug-replay.txt, every graph render-debug.txt writes whole compiled
/// again by today's compile - so a node the compile learns is held against the game's own debug graphs without the game.
/// </summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THERENDERDebugViewsAreCompiledAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, CaptureReport.RenderDebugFile)))
        {
            return;
        }

        string replay = RenderDebugReplay(File.ReadAllLines(CaptureParts.Find(folder, CaptureReport.RenderDebugFile)));
        File.WriteAllText(Path.Combine(folder, "render-debug-replay.txt"), replay);
        Assert.StartsWith("render debug replay: ", replay, StringComparison.Ordinal);
    }

    /// <summary>The graphs the part wrote whole, by today's compile: each one's nodes by type and the node types it does not know.</summary>
    internal static string RenderDebugReplay(string[] lines)
    {
        Dictionary<string, string> graphs = WrittenWhole(lines)
            .Where(one => one.Key.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase) && !one.Value.StartsWith("(not in the install)", StringComparison.Ordinal))
            .ToDictionary(one => one.Key, one => one.Value, StringComparer.OrdinalIgnoreCase);
        var body = new StringBuilder();
        string said = CaptureRenderDebug.Compiled(graphs, body);
        return $"render debug replay: {graphs.Count} graphs read back, {said}" + Environment.NewLine + body;
    }
}
