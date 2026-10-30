using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// The capture's ground-scatter.txt: what the area's ground strews over itself - every doodad layer (<c>.dlp</c>) its MaterialsLists name
/// after a material and every doodad list (<c>.ddt</c>) its masters name, read and whole, each layer's meshes with their size - and how
/// much of the area each ground type covers.
/// </summary>
/// <remarks>
/// BUILT BEFORE THE SCATTER IS (v0.1.360, asked for from the live client: "draw every doodad we find", the scatter to be the tool's own
/// so a scene built away from its area has it too). The client strews a ground's grass, ferns, leaves and weapons itself, where no
/// file says - and what a layer's number after each mesh and its pairs between nought and one mean, and the doodad list's numbers, no
/// reference names. So the files are written whole, each layer's file beside the part too (<see cref="Folder"/>), and read again offline
/// (<see cref="Replay"/>) - the scatter's rule is read off them and the game's picture, not guessed.
/// </remarks>
public static class CaptureGroundScatter
{
    /// <summary>The folder beside the part's file the layers' and lists' own files are written to, at their install paths.</summary>
    public const string Folder = "ground-scatter";

    /// <summary>
    /// The part's text, and every layer and list file written under <see cref="Folder"/> beside it where <paramref name="folder"/> is given.
    /// </summary>
    /// <param name="read">The install's files.</param>
    /// <param name="loaded">The files the area loaded - its masters among them.</param>
    /// <param name="masters">The install's index of masters, or null.</param>
    /// <param name="grid">The area's grid, for how much each ground type covers - or null.</param>
    /// <param name="folder">The part's folder, or null to write nothing beside it.</param>
    public static string Write(Func<string, byte[]?> read, IReadOnlyList<string> loaded, IReadOnlyList<string>? masters, TerrainGrid? grid, string? folder)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(loaded);
        List<string> named = AreaRoomSet.Masters(loaded, masters);
        var body = new StringBuilder();
        var layers = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var lists = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // EVERY MASTER'S MATERIALS LIST, group by group: each material and the layers riding with it.
        body.AppendLine("=== what each ground type is painted with and what each material strews (the MaterialsLists of the area's masters)");
        foreach (string master in named.Select(one => one.Replace('\\', '/').Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (read(master) is not { Length: > 0 } masterBytes)
            {
                continue;
            }

            MasterFile file = MasterFile.Read(masterBytes);
            foreach ((string key, string value) in file.Values)
            {
                bool materials = string.Equals(key, "MaterialsList", StringComparison.OrdinalIgnoreCase);
                bool doodads = string.Equals(key, "Doodads", StringComparison.OrdinalIgnoreCase);
                if (!materials && !doodads)
                {
                    continue;
                }

                string? path = MasterFile.Candidates(master, value).FirstOrDefault(one => read(one) is { Length: > 0 });
                if (path is null)
                {
                    body.Append("  ").Append(master).Append(": ").Append(key).Append(' ').Append(value).AppendLine(" - not in the install");
                    continue;
                }

                if (doodads)
                {
                    lists.TryAdd(path, master);
                    continue;
                }

                GroundMaterials list = GroundMaterials.Read(read(path));
                body.Append("  ").Append(master).Append(": ").Append(path).Append(list.Ready ? string.Empty : " - read to " + list.Why).AppendLine();
                foreach (GroundGroup group in list.Groups)
                {
                    body.Append("    \"").Append(group.Name).Append('"');
                    if (group.Weights.Count > 0)
                    {
                        body.Append(" weights ").Append(string.Join(' ', group.Weights)).Append(", then ").Append(group.Trailing.ToString(CultureInfo.InvariantCulture));
                    }

                    body.AppendLine();
                    foreach (GroundChoice choice in group.Choices.Concat(group.Extras))
                    {
                        body.Append("      ").Append(choice.Material);
                        foreach (string layer in choice.Layers)
                        {
                            string at = layer.Replace('\\', '/').Trim();
                            body.Append(" + ").Append(at);
                            if (!layers.TryGetValue(at, out List<string>? by))
                            {
                                by = [];
                                layers[at] = by;
                            }

                            by.Add(string.Create(CultureInfo.InvariantCulture, $"{Tail(choice.Material)} in \"{group.Name}\""));
                        }

                        body.AppendLine();
                    }
                }
            }
        }

        // HOW MUCH OF THE AREA EACH GROUND TYPE COVERS - the game's own corner types.
        if (grid?.Ground is { } ground)
        {
            body.AppendLine().AppendLine("=== the ground types laid in the area, by the tile corners each covers (the game's own TileCornerData)");
            long all = ground.TotalCorners.Sum();
            for (var type = 0; type < ground.Types.Count && type < ground.TotalCorners.Count; type++)
            {
                if (ground.TotalCorners[type] > 0)
                {
                    body.Append(string.Create(CultureInfo.InvariantCulture,
                        $"  {(ground.Types[type].Length > 0 ? ground.Types[type] : "(null slot)")}: {ground.TotalCorners[type]} corners, {100.0 * ground.TotalCorners[type] / Math.Max(1, all):0.#}%"));
                    body.AppendLine();
                }
            }
        }

        // EVERY LAYER, read and whole, each mesh with its size; its file beside the part.
        body.AppendLine().AppendLine("=== every doodad layer (.dlp), read then whole - each mesh's triangles and box (x by y, z from least to most) beside its line");
        int meshes = 0, unread = 0;
        foreach ((string path, List<string> by) in layers)
        {
            body.Append("##### ").Append(path).Append("  (").Append(string.Join(", ", by.Distinct())).AppendLine(")");
            byte[]? bytes = read(path);
            if (bytes is not { Length: > 0 })
            {
                unread++;
                body.AppendLine("  (not in the install)");
                continue;
            }

            Beside(folder, path, bytes);
            DoodadLayer layer = DoodadLayer.Read(bytes);
            body.AppendLine(Said(layer));
            foreach (DoodadLayerEntry entry in layer.Entries)
            {
                meshes++;
                body.Append(string.Create(CultureInfo.InvariantCulture, $"  mesh {entry.Mesh}: number {entry.Number:0.####}, {entry.Points.Count} pairs"));
                if (entry.Points.Count > 0)
                {
                    body.Append(' ').Append(string.Join(' ', entry.Points.Select(one => string.Create(CultureInfo.InvariantCulture, $"({one.A:0.###},{one.B:0.###})"))));
                }

                FixedMesh fixedMesh = FixedMesh.Read(read(entry.Mesh));
                body.Append(fixedMesh.Ready
                    ? string.Create(CultureInfo.InvariantCulture, $"; {fixedMesh.Mesh.Triangles} triangles, box {Size(fixedMesh.Mesh)}")
                    : "; the mesh did not read: " + fixedMesh.Why);
                body.AppendLine();
            }

            Whole(body, bytes);
        }

        // EVERY DOODAD LIST, read and whole; its file beside the part.
        body.AppendLine().AppendLine("=== every doodad list (.ddt) the masters name, read then whole");
        foreach ((string path, string master) in lists)
        {
            body.Append("##### ").Append(path).Append("  (Doodads of ").Append(master).AppendLine(")");
            byte[] bytes = read(path)!;
            Beside(folder, path, bytes);
            body.AppendLine(Said(GroundDoodadList.Read(bytes)));
            Whole(body, bytes);
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"ground scatter: {named.Count} masters, {layers.Count} doodad layers ({unread} not in the install) over {meshes} meshes, {lists.Count} doodad lists - what the client strews over the ground itself, where no file says")
            + "\n\n" + body;
    }

    /// <summary>A layer as read, in a line or a few.</summary>
    public static string Said(DoodadLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        string headers = layer.Headers.Count == 0 ? "none" : string.Join("; ", layer.Headers.Select(one => one.Key + (one.Value.Length > 0 ? " " + one.Value : string.Empty)));
        return string.Create(CultureInfo.InvariantCulture,
            $"  read: version {layer.Version}, scale {layer.ScaleMin:0.###} to {layer.ScaleMax:0.###}, waving {(layer.AllowWaving ? "yes" : "no")}, on blocking {(layer.AllowOnBlocking ? "yes" : "no")}, rotation {(layer.MaxRotation is { } turn ? turn.ToString(CultureInfo.InvariantCulture) + "°" : "unset")}, seed {(layer.Seed is { } seed ? seed.ToString(CultureInfo.InvariantCulture) : "unset")}, other headers {headers}, {layer.Entries.Count} meshes{(layer.Ready ? string.Empty : " - read to " + layer.Why)}");
    }

    /// <summary>A doodad list as read, a line a group.</summary>
    public static string Said(GroundDoodadList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var said = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
            $"  read: version {list.Version}, first line \"{list.First}\", second \"{list.Second}\", {list.Groups.Count} groups{(list.Ready ? string.Empty : " - read to " + list.Why)}"));
        foreach (GroundDoodadGroup group in list.Groups)
        {
            said.AppendLine().Append(string.Create(CultureInfo.InvariantCulture,
                $"    \"{group.Name}\"{(group.Rest.Length > 0 ? " " + group.Rest : string.Empty)}: {group.Doodads.Count} doodads"));
            if (group.Doodads.Count > 0)
            {
                said.Append(string.Create(CultureInfo.InvariantCulture,
                    $", weights {string.Join(' ', group.Doodads.Select(one => one.Weight is { } weight ? weight.ToString("0.##", CultureInfo.InvariantCulture) : "All").Distinct())}"));
            }
        }

        return said.ToString();
    }

    /// <summary>
    /// The layers and lists written beside a capture's ground-scatter.txt read again with today's readers - ground-scatter-replay.txt.
    /// </summary>
    /// <param name="folder">The folder the part's file is in.</param>
    public static string Replay(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string root = Path.Combine(folder, Folder);
        if (!Directory.Exists(root))
        {
            return "ground scatter replay: no " + Folder + " folder beside the part - a capture before v0.1.360, or the part was off\n";
        }

        var said = new StringBuilder();
        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        int layers = 0, lists = 0;
        foreach (string file in files)
        {
            string path = Path.GetRelativePath(root, file).Replace('\\', '/');
            byte[] bytes = File.ReadAllBytes(file);
            if (path.EndsWith(DoodadLayer.Extension, StringComparison.OrdinalIgnoreCase))
            {
                layers++;
                DoodadLayer layer = DoodadLayer.Read(bytes);
                said.Append("##### ").AppendLine(path).AppendLine(Said(layer));
                foreach (DoodadLayerEntry entry in layer.Entries)
                {
                    said.Append(string.Create(CultureInfo.InvariantCulture, $"  mesh {entry.Mesh}: number {entry.Number:0.####}, {entry.Points.Count} pairs"));
                    if (entry.Points.Count > 0)
                    {
                        said.Append(' ').Append(string.Join(' ', entry.Points.Select(one => string.Create(CultureInfo.InvariantCulture, $"({one.A:0.###},{one.B:0.###})"))));
                    }

                    said.AppendLine();
                }
            }
            else if (path.EndsWith(GroundDoodadList.Extension, StringComparison.OrdinalIgnoreCase))
            {
                lists++;
                said.Append("##### ").AppendLine(path).AppendLine(Said(GroundDoodadList.Read(bytes)));
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"ground scatter replay: {layers} doodad layers and {lists} doodad lists read again from {Folder}/\n\n") + said;
    }

    /// <summary>A file written under <see cref="Folder"/> at its install path, where a folder is given.</summary>
    private static void Beside(string? folder, string path, byte[] bytes)
    {
        if (folder is null)
        {
            return;
        }

        string[] parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(one => one is "." or ".." || one.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return;
        }

        string at = Path.Combine([folder, Folder, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(at)!);
        File.WriteAllBytes(at, bytes);
    }

    /// <summary>A file's text whole, a line each, as the masters' part writes them.</summary>
    private static void Whole(StringBuilder body, byte[] bytes)
    {
        foreach (string line in StatDescriptionFiles.Decode(bytes).ReplaceLineEndings("\n").Split('\n'))
        {
            body.Append("  | ").AppendLine(line.TrimEnd());
        }
    }

    /// <summary>A mesh's box: across, along and from least to most z.</summary>
    private static string Size(SkinnedMesh mesh)
    {
        Vector3 size = mesh.Most - mesh.Least;
        return string.Create(CultureInfo.InvariantCulture, $"{size.X:0.#} x {size.Y:0.#}, z {mesh.Least.Z:0.#} to {mesh.Most.Z:0.#}");
    }

    private static string Tail(string path) => path[(path.Replace('\\', '/').LastIndexOf('/') + 1)..];
}
