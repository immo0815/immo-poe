using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The install's shader sources that name any of a list of words, whole, for a capture part - the ground's, the water's.
/// </summary>
/// <remarks>
/// WHOLE, because a line without its fragment was what the tile dump's first search gave, and "pos" in it could not be
/// told from anything. Every source is read to be searched; only those naming a word are written.
/// </remarks>
internal static class CaptureSources
{
    /// <summary>
    /// Writes the sources naming any of <paramref name="words"/>: a heading, each found with the words it names, then the first <paramref name="most"/> whole. Returns how many were found.
    /// </summary>
    public static int Naming(Func<string, byte[]?> read, IReadOnlyList<string>? shaders, IReadOnlyList<string> words, int most, StringBuilder said)
    {
        said.AppendLine().Append("=== shader sources naming ").AppendLine(string.Join(", ", words));
        if (shaders is not { Count: > 0 })
        {
            said.AppendLine("(no shader sources: the install walk found none, or it had not run when the key went down)");
            return -1;
        }

        var found = new List<(string Path, string Text, string Words)>();
        foreach (string path in shaders.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (read(Clean(path)) is not { Length: > 0 } bytes)
            {
                continue;
            }

            string text = StatDescriptionFiles.Decode(bytes);
            string[] named = [.. words.Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))];
            if (named.Length > 0)
            {
                found.Add((Clean(path), text, string.Join(", ", named)));
            }
        }

        said.Append(CultureInfo.InvariantCulture, $"{found.Count} of {shaders.Count} sources name one").AppendLine();
        foreach ((string path, _, string named) in found)
        {
            said.Append("  ").Append(path).Append("  (").Append(named).AppendLine(")");
        }

        foreach ((string path, string text, _) in found.Take(most))
        {
            said.AppendLine().Append("##### ").AppendLine(path);
            said.AppendLine(text.TrimEnd());
        }

        if (found.Count > most)
        {
            said.AppendLine().Append(CultureInfo.InvariantCulture, $"{found.Count - most} more not written whole - named above").AppendLine();
        }

        return found.Count;
    }

    private static string Clean(string path) => path.Replace('\\', '/').Trim();
}
