using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// One model book's pane as it stood when the capture key went down: its lines under the picture and the model it shows.
/// </summary>
/// <param name="Book">Which book's pane - "monster", "tile", ...</param>
/// <param name="Lines">The lines under the picture, as the pane's info fold shows them.</param>
/// <param name="Model">The model shown.</param>
public sealed record CapturePane(string Book, IReadOnlyList<string> Lines, MonsterModel Model);

/// <summary>
/// The capture's model-panes part: every model pane's lines under its picture, every doodad file of the room it shows with what
/// became of its lines, and each file that did not draw written whole with what it extends.
/// </summary>
/// <remarks>
/// WRITTEN FOR PORT'S WATER. The harbour tile brings 31 lines of WaterPlane3x3.ao, the Monster Book's stage drew none of
/// them, and the probe over the water found only the ocean's black floor - so the plane was not in the picture at all. Why
/// was said nowhere the capture could reach: the stage dropped the room's own lines, and those said only how many doodads in
/// all were left out and which ONE did not draw first. A file not drawn, a cap that ran out before the tiles' doodads, and a
/// plane laid somewhere else are three causes the picture cannot tell apart; the counts per file can.
///
/// PRINT BOTH HALVES: each file's line carries where its first line puts it and the box its mesh spans, so a doodad laid
/// where nobody looks can be told from one never laid; and a file that did not draw is written whole, with every file it
/// extends, so what it is made of is read rather than guessed.
/// </remarks>
public static class CapturePanes
{
    /// <summary>Most files written whole - the doodad files that did not draw, the first named first.</summary>
    private const int MostWhole = 32;

    /// <summary>Most files one extends chain is followed through.</summary>
    private const int MostDeep = 16;

    /// <summary>
    /// The part's text: a line saying what it holds, then each pane, then the files that did not draw, whole. Never throws.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path - or null, and the files are only named.</param>
    /// <param name="panes">The panes showing a model.</param>
    public static string Write(Func<string, byte[]?>? read, IReadOnlyList<CapturePane> panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        var body = new StringBuilder();
        var unlaid = new List<DoodadFile>();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        foreach (CapturePane pane in panes)
        {
            body.AppendLine().Append("=== the ").Append(pane.Book).AppendLine(" book's pane");
            MonsterModel model = pane.Model;
            body.Append("  shows\t").AppendLine(model.Ready ? model.Mesh_ : "nothing - " + model.Why);
            if (model.Stage.Length > 0)
            {
                body.Append("  stage\t").AppendLine(model.Stage);
            }

            foreach (string line in pane.Lines)
            {
                body.Append("  info\t").AppendLine(line);
            }

            if (model.StageMove.Length > 0)
            {
                body.Append("  stage's own lines\t").AppendLine(model.StageMove);
            }

            if (model.DoodadFiles.Count == 0)
            {
                continue;
            }

            files += model.DoodadFiles.Count;
            Files(body, model.DoodadFiles);
            foreach (DoodadFile file in model.DoodadFiles)
            {
                if (file.Missing > 0 && named.Add(file.Ao))
                {
                    unlaid.Add(file);
                }
            }
        }

        if (unlaid.Count > 0)
        {
            body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the doodad files that did not draw, whole, with what each extends ({unlaid.Count})").AppendLine();
            foreach (DoodadFile file in unlaid.Take(MostWhole))
            {
                Chain(body, read, file.Ao);
            }

            if (unlaid.Count > MostWhole)
            {
                body.AppendLine().Append(CultureInfo.InvariantCulture, $"{unlaid.Count - MostWhole} more not written - named in the lines above").AppendLine();
            }
        }

        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture,
            $"{panes.Count} model panes showing a model, {files} doodad files of their rooms with what became of each one's lines, {unlaid.Count} that did not draw written whole")
            .AppendLine();
        said.Append(body);
        return said.ToString();
    }

    /// <summary>A room's doodad files, a line each: the counts, the box and first place, the materials, and why it did not draw.</summary>
    private static void Files(StringBuilder body, IReadOnlyList<DoodadFile> files)
    {
        int lines = 0, laid = 0, missing = 0, capped = 0, unseen = 0;
        foreach (DoodadFile file in files)
        {
            lines += file.Lines;
            laid += file.Laid;
            missing += file.Missing;
            capped += file.Capped;
            unseen += file.Unseen;
        }

        body.Append(CultureInfo.InvariantCulture,
            $"  doodad files: {files.Count}, {lines} lines - {laid} laid, {missing} not drawn, {capped} left out by the cap, {unseen} unseen").AppendLine();
        body.AppendLine("    lines\tlaid\tnot drawn\tcapped\tunseen\ttriangles\tbox in its own frame\tfirst line at\tfile\tmaterials (blend)\twhy not drawn");
        foreach (DoodadFile file in files)
        {
            body.Append(CultureInfo.InvariantCulture,
                $"    {file.Lines}\t{file.Laid}\t{file.Missing}\t{file.Capped}\t{file.Unseen}\t{file.Triangles}\t{Box(file)}\t{Point(file.First)}\t{file.Ao}\t{Materials(file)}\t{file.Why}")
                .AppendLine();
        }
    }

    /// <summary>The mesh's box, or a dash where it did not load.</summary>
    private static string Box(DoodadFile file)
        => file.Triangles == 0 && file.Least == file.Most ? "-" : Point(file.Least) + " .. " + Point(file.Most);

    private static string Point(Vector3 at) => string.Create(CultureInfo.InvariantCulture, $"{at.X:0.#} {at.Y:0.#} {at.Z:0.#}");

    /// <summary>Each distinct material with the blend word its shapes carry, or a dash.</summary>
    private static string Materials(DoodadFile file)
    {
        if (file.Materials.Count == 0)
        {
            return "-";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var said = new List<string>();
        for (var shape = 0; shape < file.Materials.Count; shape++)
        {
            string mode = shape < file.Modes.Count && file.Modes[shape].Length > 0 ? file.Modes[shape] : "-";
            string one = $"{file.Materials[shape]} ({mode})";
            if (seen.Add(one))
            {
                said.Add(one);
            }
        }

        return string.Join(", ", said);
    }

    /// <summary>
    /// One .ao written whole, then every file it extends, breadth first - each read as the model walk reads it, the
    /// extension added where the extends line has none.
    /// </summary>
    private static void Chain(StringBuilder body, Func<string, byte[]?>? read, string ao)
    {
        var queue = new Queue<(string Path, string Why)>();
        queue.Enqueue((ao, "did not draw"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int written = 0;
        while (queue.Count > 0 && written < MostDeep)
        {
            (string path, string why) = queue.Dequeue();
            string said = path.Replace('\\', '/').Trim();
            if (said.Length == 0 || !seen.Add(said))
            {
                continue;
            }

            body.AppendLine().Append("##### ").Append(said).Append("  (").Append(why).AppendLine(")");
            if (read is null)
            {
                body.AppendLine("  (no install to read it from)");
                continue;
            }

            (byte[]? bytes, string found) = Object(read, said);
            if (bytes is not { Length: > 0 })
            {
                body.AppendLine("  (not in the install)");
                continue;
            }

            if (!string.Equals(found, said, StringComparison.Ordinal))
            {
                body.Append("  read as ").AppendLine(found);
            }

            written++;
            CaptureReport.Whole(body, bytes);
            foreach (string up in AnimatedObject.Read(bytes).Extends)
            {
                if (!up.Equals("nothing", StringComparison.OrdinalIgnoreCase))
                {
                    queue.Enqueue((up, "extended by " + said));
                }
            }
        }
    }

    /// <summary>An .ao's bytes as written, or with the extension added where the path has none - see AnimatedObject.Read.</summary>
    private static (byte[]? Bytes, string Path) Object(Func<string, byte[]?> read, string path)
    {
        if (read(path) is { Length: > 0 } bytes)
        {
            return (bytes, path);
        }

        int slash = path.LastIndexOf('/');
        string suffixed = path + AnimatedObject.Suffix;
        return path.IndexOf('.', slash + 1) < 0 ? (read(suffixed), suffixed) : (null, path);
    }
}
