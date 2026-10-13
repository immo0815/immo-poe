using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The materials a capture writes whole compiled again from its own files: materials-replay.txt, what the compile of this build makes
/// of the materials model-panes.txt holds - so a new node is held against a real room's materials without the game.
/// </summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THEMATERIALSAreCompiledAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, "model-panes.txt")))
        {
            return;
        }

        string replay = MaterialsReplay(File.ReadAllLines(CaptureParts.Find(folder, "model-panes.txt")));
        File.WriteAllText(Path.Combine(folder, "materials-replay.txt"), replay);
        Assert.StartsWith("materials:", replay, StringComparison.Ordinal);
    }

    /// <summary>Every material model-panes.txt writes whole, through the capture's own writer over the files the capture holds.</summary>
    internal static string MaterialsReplay(string[] lines)
    {
        Dictionary<string, string> files = WrittenWhole(lines);
        byte[]? Read(string path) => files.TryGetValue(path.Replace('\\', '/').Trim(), out string? text) ? Encoding.UTF8.GetBytes(text) : null;

        var paints = new MonsterModels.Paints();
        var said = new StringBuilder();
        var graphs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] materials = [.. files.Keys.Where(one => one.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))];
        said.Append("materials: ").Append(materials.Length).Append(" written whole, ")
            .Append(files.Count - materials.Length).AppendLine(" other files (graphs, doodads) - textures are not in a capture, so their headers read as missing");
        foreach (string material in materials)
        {
            said.AppendLine().Append("##### ").AppendLine(material);
            CaptureMaterials.Material(Read, material, paints, said, graphs, seen);
        }

        return said.ToString();
    }

    /// <summary>
    /// The files a capture's text writes whole under a <c>##### path  (why)</c> heading: a material's lines are marked <c>  | </c>,
    /// anything else runs to the next heading or <c>===</c> section.
    /// </summary>
    internal static Dictionary<string, string> WrittenWhole(string[] lines)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; at < lines.Length; at++)
        {
            if (!lines[at].StartsWith("##### ", StringComparison.Ordinal))
            {
                continue;
            }

            string head = lines[at][6..];
            int cut = head.IndexOf("  (", StringComparison.Ordinal);
            string path = cut > 0 ? head[..cut] : head;
            bool marked = at + 1 < lines.Length && lines[at + 1].StartsWith("  | ", StringComparison.Ordinal);
            var body = new StringBuilder();
            for (int next = at + 1; next < lines.Length
                 && !lines[next].StartsWith("##### ", StringComparison.Ordinal)
                 && !lines[next].StartsWith("=== ", StringComparison.Ordinal); next++)
            {
                if (!marked)
                {
                    body.AppendLine(lines[next]);
                }
                else if (lines[next].StartsWith("  | ", StringComparison.Ordinal))
                {
                    body.AppendLine(lines[next][4..]);
                }
            }

            files.TryAdd(path, body.ToString());
        }

        return files;
    }
}
