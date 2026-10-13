using System.Globalization;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The engine's beach waves read back from a capture: waves-replay.txt, every graph water-textures.txt writes whole under the beach
/// waves' section parsed as the compile parses a graph - its nodes by type, which the compile knows, and the files its parameters name.
/// </summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THEBEACHWavesGraphsAreReadAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, "water-textures.txt")))
        {
            return;
        }

        string replay = WavesReplay(File.ReadAllLines(CaptureParts.Find(folder, "water-textures.txt")));
        File.WriteAllText(Path.Combine(folder, "waves-replay.txt"), replay);
        Assert.StartsWith("beach waves:", replay, StringComparison.Ordinal);
    }

    /// <summary>Every graph the part wrote whole, parsed: how many nodes and links, each node type with how often it stands and whether the compile knows it, and every file a parameter names.</summary>
    internal static string WavesReplay(string[] lines)
    {
        Dictionary<string, string> files = WrittenWhole(lines);
        string[] graphs = [.. files.Keys.Where(one => one.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)];
        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture, $"beach waves: {graphs.Length} graphs written whole").AppendLine();
        foreach (string path in graphs)
        {
            said.AppendLine().Append("##### ").AppendLine(path);
            string text = files[path];
            if (text.StartsWith("(not in the install)", StringComparison.Ordinal))
            {
                said.AppendLine("  not in the install");
                continue;
            }

            ShaderGraph graph = ShaderGraph.Read(Encoding.UTF8.GetBytes(text));
            said.Append(CultureInfo.InvariantCulture, $"  {graph.Nodes.Count} nodes, {graph.Links.Count} links").AppendLine();
            foreach (IGrouping<string, ShaderNode> type in graph.Nodes.GroupBy(node => node.Type).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                said.Append(CultureInfo.InvariantCulture, $"  {type.Key} x{type.Count()} ({type.First().Stage}): ")
                    .AppendLine(ShadeProgram.Knows(type.Key) ? "known to the compile" : "NOT known to the compile");
            }

            foreach (string named in graph.Nodes.SelectMany(node => node.Parameters).Select(value => value.Path).Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                said.Append("  names ").AppendLine(named);
            }
        }

        return said.ToString();
    }
}
