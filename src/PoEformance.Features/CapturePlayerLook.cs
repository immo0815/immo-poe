using System.Globalization;
using System.Text;
using PoEformance.Game.Diagnostics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The player look part's files side: what the install says of what the memory side read - the class's Characters row, each inventory
/// id's Inventories row, each carried item's visual identity with every file it names, and the .ao files those and the pointer web name,
/// written whole beside the part for the offline reader.
/// </summary>
/// <remarks>
/// SEE PlayerLook for why the part exists. What the files settle and the memory cannot:
///
/// - WHICH SLOT AN INVENTORY ID IS: the research read memory ids as Inventories.dat's rows plus one (GameHelper2's InventoryName enum, the
///   AHK tool's flask id 12), which would make the second weapon set 15 and 16, a third 80 and 81 and a third ring 102 - worn slots
///   StashReader.KindOf calls stash tabs. Each id the player carries is set beside the row it would be, so the reading is checked here.
/// - THE CLASS'S ROW: the Characters row whose strings name the player's path or the model his Animated component loaded, every string
///   it holds by its offset (no layout of it is vendored; dat-schema's PoE2 Characters names AOFile, ACTFile and more).
/// - EACH ITEM'S LOOK: its BaseItemTypes row's ItemVisualIdentity, and the unique's own where its id (Base+0x30) names another, every
///   string column and string list of the row by the vendored layout's names - PoE2's armour fills a column per body type there, unnamed
///   (column_31 and on), the drop model and the held model beside them.
/// - THE FILES WHOLE: every .ao, .aoc, .act, .ot and .otc the rows, the model and the pointer web name, and what each extends, decoded and
///   written under <see cref="Folder"/> beside the part (to <see cref="MostBytes"/>); the binary ones (meshes, rigs, textures) named with
///   their size. The offline reader (<see cref="Replay"/>) reads them again and says how each piece attaches.
/// </remarks>
public static class CapturePlayerLook
{
    /// <summary>Where the files are written beside the part.</summary>
    public const string Folder = "player-look";

    /// <summary>The offline reader's file, beside the capture.</summary>
    public const string ReplayFile = "player-look-replay.txt";

    /// <summary>The most bytes of files written whole - CHOSEN.</summary>
    public const long MostBytes = 8L * 1024 * 1024;

    /// <summary>The most extends hops a file is followed through.</summary>
    private const int MostHops = 4;

    /// <summary>The extensions written whole: the text an object is composed by.</summary>
    private static readonly string[] Texts = [".ao", ".aoc", ".act", ".ot", ".otc"];

    /// <summary>The words the offline reader says a file's lines by - how a piece is skinned, attached, hidden or played.</summary>
    internal static readonly string[] Words =
        ["extends", "skin", "attach", "bone", "remove", "hide", "hidden", "replace", "socket", "mesh", ".sm", ".fmt", ".ast", ".mat", "animation", "Render", "Parent"];

    /// <summary>The part's files side: its lines, the files written under <paramref name="folder"/>'s <see cref="Folder"/>.</summary>
    /// <param name="files">The install, or null.</param>
    /// <param name="layouts">The item tables' layouts (data/item-tables.json), or null.</param>
    /// <param name="seen">What the memory side read.</param>
    /// <param name="folder">Where the part's file is written; the files go under its <see cref="Folder"/>.</param>
    public static IReadOnlyList<string> Write(GameFiles? files, QuestTableLayouts? layouts, PlayerLook.Seen seen, string folder)
    {
        ArgumentNullException.ThrowIfNull(seen);
        var lines = new List<string> { string.Empty, "=== the files side: what the install says of what was read" };
        if (files is null)
        {
            lines.Add("  no install to read");
            return lines;
        }

        lines.AddRange(InventoryRows(files, seen));
        lines.AddRange(ClassRow(files, seen));
        var named = new List<string>();
        if (seen.Model.Length > 0)
        {
            named.Add(seen.Model);
        }

        lines.AddRange(Looks(files, layouts, seen, named));
        named.AddRange(seen.Files);
        lines.AddRange(Whole(files, named, Path.Combine(folder, Folder)));
        return lines;
    }

    /// <summary>Each inventory id the player carries something in, beside Inventories.dat's row one below it.</summary>
    private static IEnumerable<string> InventoryRows(GameFiles files, PlayerLook.Seen seen)
    {
        yield return string.Empty;
        yield return "=== each inventory id he carries something in, beside Inventories.dat's row (id - 1) - the reading that a memory id is a row plus one - every string of that row";
        (DatFile? table, string where) = Open(files, "Inventories");
        if (table is null)
        {
            yield return "  " + where;
            yield break;
        }

        IReadOnlyList<int> texts = table.TextOffsets();
        foreach (int id in seen.Items.Select(one => one.Inventory).Distinct().Order())
        {
            int row = id - 1;
            string said = row >= 0 && row < table.Rows ? Strings(table, row, texts) : "past the table";
            yield return string.Create(CultureInfo.InvariantCulture, $"  {id}\t{seen.Items.First(one => one.Inventory == id).Kind}\trow {row}: {said}");
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"  {where}: {table.Rows} rows of {table.RowSize} bytes");
    }

    /// <summary>The Characters rows whose strings name the player's path or his model, every string they hold.</summary>
    private static IEnumerable<string> ClassRow(GameFiles files, PlayerLook.Seen seen)
    {
        yield return string.Empty;
        yield return "=== his class: the Characters rows whose strings name his path or the model he loaded - every string of the row by its offset";
        (DatFile? table, string where) = Open(files, "Characters");
        if (table is null)
        {
            yield return "  " + where;
            yield break;
        }

        IReadOnlyList<int> texts = table.TextOffsets();
        var found = 0;
        for (var row = 0; row < table.Rows; row++)
        {
            bool names = false;
            foreach (int offset in texts)
            {
                string text = table.Text(row, offset);
                if (text.Length > 0 && (string.Equals(text, seen.Path, StringComparison.OrdinalIgnoreCase) || string.Equals(text, seen.Model, StringComparison.OrdinalIgnoreCase)))
                {
                    names = true;
                    break;
                }
            }

            if (names)
            {
                found++;
                yield return string.Create(CultureInfo.InvariantCulture, $"  row {row}: {Strings(table, row, texts)}");
            }
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"  {found} rows of {table.Rows} name him ({where})");
    }

    /// <summary>Each carried item past the backpack: its base type's visual identity and the unique's own, every file column of each.</summary>
    private static IEnumerable<string> Looks(GameFiles files, QuestTableLayouts? layouts, PlayerLook.Seen seen, List<string> named)
    {
        var lines = new List<string>
        {
            string.Empty,
            "=== each item he carries past the backpack: its BaseItemTypes row's ItemVisualIdentity, and the unique's own where its visual identity id names another - every string column and string list of the row by the vendored layout's names",
        };
        if (layouts is null)
        {
            lines.Add("  data/item-tables.json did not load");
            return lines;
        }

        (LoadedTable? bases, string basesWhy) = QuestTables.Open(files, layouts, "BaseItemTypes", null, "Id", "Name");
        (LoadedTable? art, string artWhy) = QuestTables.Open(files, layouts, "ItemVisualIdentity", null, "Id");
        if (bases is not { Usable: true } || art is not { Usable: true })
        {
            lines.Add("  BaseItemTypes: " + (bases?.Say ?? basesWhy) + "; ItemVisualIdentity: " + (art?.Say ?? artWhy));
            return lines;
        }

        int idAt = layouts.OffsetOf("BaseItemTypes", "Id"), visualAt = layouts.OffsetOf("BaseItemTypes", "ItemVisualIdentity");
        int artIdAt = layouts.OffsetOf("ItemVisualIdentity", "Id");
        var baseRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var row = 0; row < bases.File.Rows; row++)
        {
            baseRows.TryAdd(bases.File.Text(row, idAt), row);
        }

        var artRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var row = 0; row < art.File.Rows; row++)
        {
            artRows.TryAdd(art.File.Text(row, artIdAt), row);
        }

        layouts.RowSizeOf("ItemVisualIdentity");
        List<DatColumn> columns = layouts.Tables["ItemVisualIdentity"];
        foreach (PlayerLook.Carried item in seen.Items.Where(one => one.Inventory != 1))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  inventory {item.Inventory} ({item.Kind}) at {item.Left}, {item.Top}: {item.Path} | {item.Base} | {item.Unique} | {item.Ivi}"));
            int visual = baseRows.TryGetValue(item.Path, out int baseRow) ? bases.File.Reference(baseRow, visualAt).RowIn(art.File.Rows) : -1;
            if (visual < 0)
            {
                lines.Add("    no BaseItemTypes row or no visual identity for its path");
            }
            else
            {
                lines.AddRange(Row(art.File, visual, columns, "    base's visual identity", named));
            }

            if (item.Ivi.Length > 0 && artRows.TryGetValue(item.Ivi, out int own) && own != visual)
            {
                lines.AddRange(Row(art.File, own, columns, "    its own visual identity (Base+0x30)", named));
            }
        }

        return lines;
    }

    /// <summary>A visual identity row's string columns and string lists that hold anything, by their names; the files among them kept.</summary>
    private static IEnumerable<string> Row(DatFile table, int row, List<DatColumn> columns, string title, List<string> named)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"{title}: row {row}");
        foreach (DatColumn column in columns.Where(one => one.Type == "string"))
        {
            IReadOnlyList<string> values = column.Array
                ? [.. table.References(row, column.Offset, 8).Select(one => table.TextAt(one.First)).Where(one => one.Length > 0)]
                : table.Text(row, column.Offset) is { Length: > 0 } one ? [one] : [];
            if (values.Count == 0)
            {
                continue;
            }

            named.AddRange(values.Where(PlayerLook.GamePath));
            yield return "      " + column.Name + " = " + string.Join(" | ", values);
        }
    }

    /// <summary>
    /// Every text file named - the model, the rows' files, the pointer web's - and what each extends, decoded and written under a folder to
    /// <see cref="MostBytes"/>; the rest named with their size.
    /// </summary>
    private static IEnumerable<string> Whole(GameFiles files, List<string> named, string folder)
    {
        var lines = new List<string>
        {
            string.Empty,
            string.Create(CultureInfo.InvariantCulture,
                $"=== the files named - the model he loaded, the visual identities' files, the pointer web's - the text ones ({string.Join(", ", Texts)}) and what they extend written whole under {Folder}/ beside this file, to {MostBytes / (1024 * 1024)} MB; the rest named"),
        };
        var queue = new Queue<(string Path, int Hop)>(named.Distinct(StringComparer.OrdinalIgnoreCase).Select(one => (one, 0)));
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long written = 0;
        while (queue.Count > 0)
        {
            (string path, int hop) = queue.Dequeue();
            string clean = path.Replace('\\', '/').Trim();
            if (!done.Add(clean))
            {
                continue;
            }

            // THE BINARY ONES NAMED, NOT READ: a texture is megabytes, and how the look is composed is in the text.
            if (!Texts.Any(one => clean.EndsWith(one, StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add("  " + clean + "\tnot text, not read");
                continue;
            }

            byte[]? bytes = GameArt.ReadRaw(files, clean);
            if (bytes is not { Length: > 0 })
            {
                lines.Add("  " + clean + "\tnot in the install");
                continue;
            }

            string content = StatDescriptionFiles.Decode(bytes);
            if (written + content.Length <= MostBytes)
            {
                string to = Path.Combine(folder, clean.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.WriteAllText(to, content, Encoding.UTF8);
                written += content.Length;
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {clean}\t{content.Length:N0} characters, written{(hop > 0 ? $" ({hop} extends hops on)" : string.Empty)}"));
            }
            else
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {clean}\t{content.Length:N0} characters, past the cap - not written"));
            }

            if (hop < MostHops && Extends(content, clean) is { } parent)
            {
                queue.Enqueue((parent, hop + 1));
            }
        }

        return lines;
    }

    /// <summary>What a text file extends, with the file's own extension where it names none - or null.</summary>
    internal static string? Extends(string content, string path)
    {
        foreach (string raw in content.Split('\n'))
        {
            string line = raw.Trim();
            int open = line.IndexOf('"', StringComparison.Ordinal);
            int close = open < 0 ? -1 : line.IndexOf('"', open + 1);
            if (line.StartsWith("extends", StringComparison.Ordinal) && open >= 0 && close > open)
            {
                string parent = line[(open + 1)..close];
                return parent.Length == 0 || parent.Contains("nothing", StringComparison.OrdinalIgnoreCase) ? null
                    : Path.HasExtension(parent) ? parent : parent + Path.GetExtension(path);
            }
        }

        return null;
    }

    /// <summary>A table out of the install by its name, with no layout - or null and where it looked.</summary>
    private static (DatFile? Table, string Where) Open(GameFiles files, string table)
    {
        foreach (string path in QuestTables.Candidates(table))
        {
            if (files.Has(path))
            {
                return DatFile.Parse(files.Read(path)) is { } parsed ? (parsed, path) : (null, path + " did not parse as a .dat");
            }
        }

        return (null, table + ": not found - looked in " + string.Join(", ", QuestTables.Candidates(table)));
    }

    /// <summary>Every string a row holds at the table's text offsets, by its offset.</summary>
    private static string Strings(DatFile table, int row, IReadOnlyList<int> texts)
    {
        var said = new List<string>();
        foreach (int offset in texts)
        {
            if (table.Text(row, offset) is { Length: > 0 } text)
            {
                said.Add(string.Create(CultureInfo.InvariantCulture, $"+{offset} \"{text}\""));
            }
        }

        return said.Count == 0 ? "(no strings)" : string.Join(", ", said);
    }

    /// <summary>
    /// The offline reader: the files the part wrote whole read again - each one's lines naming how a piece is skinned, attached, hidden or
    /// played (<see cref="Words"/>), and what it extends.
    /// </summary>
    public static string Replay(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string root = Directory.EnumerateDirectories(folder, Folder, SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
        if (root.Length == 0)
        {
            return "player look replay: no " + Folder + "/ in " + folder;
        }

        var text = new StringBuilder();
        string[] written = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)];
        text.Append(CultureInfo.InvariantCulture, $"player look replay: {written.Length} files written whole - each one's lines naming {string.Join(", ", Words)} (any case)").AppendLine();
        foreach (string file in written)
        {
            string path = Path.GetRelativePath(root, file).Replace('\\', '/');
            string content = File.ReadAllText(file);
            text.AppendLine().Append(path).AppendLine(Extends(content, path) is { } parent ? "\textends " + parent : string.Empty);
            foreach (string raw in content.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length > 0 && Words.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    text.Append("  ").AppendLine(line);
                }
            }
        }

        return text.ToString();
    }
}
