using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

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

        string replay = ParticlesReplay(File.ReadAllLines(CaptureParts.Find(folder, CaptureReport.ParticlesFile)), folder);
        File.WriteAllText(Path.Combine(folder, "particles-replay.txt"), replay);
        Assert.StartsWith("particles replay: ", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// The graphs the part wrote whole, by today's compile: each one's nodes by type and the node types it does not know; then every emitter's
    /// shaders composed and its update linked again from the sources the part wrote whole, each linked shader written beside the replay
    /// (<c>particles-linked/</c>) for a compiler.
    /// </summary>
    internal static string ParticlesReplay(string[] lines, string? folder = null)
    {
        Dictionary<string, string> whole = WrittenWhole(lines);
        Dictionary<string, string> graphs = whole
            .Where(one => one.Key.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(one => one.Key, one => one.Value, StringComparer.OrdinalIgnoreCase);
        var body = new StringBuilder();
        string said = CaptureRenderDebug.Compiled(graphs, body);

        var source = ShaderSource.Of(whole.Where(one => one.Key.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)).Select(one => (one.Key, one.Value)));
        string linked = CaptureParticles.Link(
            path => whole.TryGetValue(path.Replace('\\', '/').Trim(), out string? text) ? text : null,
            source,
            whole.Keys.Where(path => path.EndsWith(".pet", StringComparison.OrdinalIgnoreCase)),
            body,
            whole.GetValueOrDefault(CaptureParticles.PreludePath));
        if (folder is not null)
        {
            string into = Path.Combine(folder, "particles-linked");
            Directory.CreateDirectory(into);
            foreach ((string path, string text) in WrittenWhole([.. body.ToString().Split('\n').Select(line => line.TrimEnd('\r'))]).Where(one => one.Key.StartsWith("linked/", StringComparison.Ordinal)))
            {
                File.WriteAllText(Path.Combine(into, Path.GetFileName(path)), text);

                // AND EXPANDED, every include put in place by any spelling of its path - the sources come in the install's own case or
                // lower case, and a compiler on a case-sensitive file system finds neither by the other.
                string expanded = ParticleLinker.Expanded(text, include => whole.GetValueOrDefault(include.Replace('\\', '/').Trim()), out _);
                File.WriteAllText(Path.Combine(into, Path.ChangeExtension(Path.GetFileName(path), ".expanded.hlsl")), expanded);
            }

            // The .hlsl files the sources include, beside them as their paths name them, for a compiler's include path.
            foreach ((string path, string text) in whole.Where(one => one.Key.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase) || one.Key.EndsWith(".inc", StringComparison.OrdinalIgnoreCase)))
            {
                string at = Path.Combine(into, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(at)!);
                File.WriteAllText(at, text);
            }
        }

        return $"particles replay: {graphs.Count} graphs read back, {said}; {linked}" + Environment.NewLine + body;
    }
}
