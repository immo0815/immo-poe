using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// A material written whole for a capture: the file, the Input nodes its graphs name, what it compiles to - and its graphs, once each, whole.
/// </summary>
/// <remarks>
/// ONE WRITER FOR EVERY PART THAT ASKS WHAT A MATERIAL IS: the ground's makings (CaptureGround) and the model panes'
/// materials (CapturePanes). A material is drawn from its graphs or not at all, and the question "why does it come out
/// plain" is answered by the graphs and what the compile left out, side by side with the file - not by its name.
/// </remarks>
internal static class CaptureMaterials
{
    /// <summary>
    /// One material whole, what its graphs name and what it compiles to; the graphs it is an instance of are added to <paramref name="graphs"/> once, to be written after with <see cref="Graphs"/>.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="material">The material, a graph selector after it or not.</param>
    /// <param name="paints">The compile cache.</param>
    /// <param name="said">Where it is written.</param>
    /// <param name="graphs">The graphs met so far, in order.</param>
    /// <param name="seen">The graphs met so far, to keep each once.</param>
    /// <returns>Whether the file was in the install.</returns>
    public static bool Material(
        Func<string, byte[]?> read, string material, MonsterModels.Paints paints, StringBuilder said, List<string> graphs, HashSet<string> seen)
    {
        string file = Clean(MaterialFile.Bare(material));
        byte[]? content = read(file);
        if (content is not { Length: > 0 })
        {
            said.AppendLine("(not in the install)");
            return false;
        }

        foreach (string line in StatDescriptionFiles.Decode(content).TrimEnd().Split('\n'))
        {
            said.Append("  | ").AppendLine(line.TrimEnd('\r'));
        }

        (ShadeCompile compiled, IReadOnlyList<string> inputs) = paints.Compiled(read, file);
        said.Append("graphs name: ").AppendLine(inputs.Count > 0 ? string.Join(", ", inputs) : "no Input node");
        said.Append("program: ").AppendLine(compiled.Program switch
        {
            null => "none - " + (compiled.Skipped.Count > 0 ? string.Join("; ", compiled.Skipped) : "no graph compiled"),
            { Plain: >= 0 } plain => "a plain read of " + plain.Textures[plain.Plain].Path,
            { } program => Say(program.Textures.Count) + " textures read: " + string.Join(", ", program.Textures.Select(texture => texture.Path))
                + (compiled.Skipped.Count > 0 ? " · left out: " + string.Join("; ", compiled.Skipped) : string.Empty),
        });

        // EACH TEXTURE'S HEADER, so a texture that did not read - which leaves the whole program undrawn - says what format it is
        // in (asked for when the rocks' spray first read a 16-bit noise, v0.1.234).
        if (compiled.Program is { Plain: < 0 } drawn)
        {
            foreach (ShadeTexture texture in drawn.Textures.Concat(drawn.Glossy?.Textures ?? []).DistinctBy(one => one.Path, StringComparer.OrdinalIgnoreCase))
            {
                said.Append("  texture ").Append(texture.Path).Append(texture.Srgb ? " (sRGB): " : ": ")
                    .AppendLine(ModelDump.Header(GameArt.ReadRaw(read, texture.Path)));
            }
        }

        foreach (string graph in MaterialFile.Read(content).Parents)
        {
            if (graph.Length > 0 && seen.Add(Clean(graph)))
            {
                graphs.Add(Clean(graph));
            }
        }

        return true;
    }

    /// <summary>Each graph once, whole, under a heading saying whose it is.</summary>
    public static void Graphs(Func<string, byte[]?> read, IEnumerable<string> graphs, string whose, StringBuilder said)
    {
        foreach (string graph in graphs)
        {
            said.AppendLine().Append("##### ").Append(graph).Append("  (").Append(whose).AppendLine(")");
            byte[]? text = read(graph);
            said.AppendLine(text is { Length: > 0 } ? StatDescriptionFiles.Decode(text).TrimEnd() : "(not in the install)");
        }
    }

    private static string Say(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static string Clean(string path) => path.Replace('\\', '/').Trim();
}
