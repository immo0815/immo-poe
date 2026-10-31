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

        // THE VISUAL IDENTITIES OPENED ONCE for the carried items' rows and the survey of the whole table.
        (LoadedTable? art, string artWhy) = layouts is null ? (null, "data/item-tables.json did not load")
            : QuestTables.Open(files, layouts, "ItemVisualIdentity", null, "Id");
        lines.AddRange(Looks(files, layouts, art, artWhy, seen, named));
        lines.AddRange(Survey(files, layouts, art, artWhy, seen));
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
    private static IEnumerable<string> Looks(
        GameFiles files, QuestTableLayouts? layouts, LoadedTable? art, string artWhy, PlayerLook.Seen seen, List<string> named)
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

    /// <summary>
    /// Every string column of ItemVisualIdentity over the whole table (<see cref="Columns"/>), the bodies held against it read from the
    /// Characters rows and his own model (<see cref="Bodies"/>).
    /// </summary>
    private static IEnumerable<string> Survey(GameFiles files, QuestTableLayouts? layouts, LoadedTable? art, string artWhy, PlayerLook.Seen seen)
    {
        var lines = new List<string>
        {
            string.Empty,
            "=== every string column and string list of ItemVisualIdentity over the whole table - rows filled, extensions, how many values name each body, the folders they lie in, the first three values: PoE2 fills a column per body type there, unnamed in the vendored layout, and the carried items above name the player's alone",
        };
        if (layouts is null || art is not { Usable: true })
        {
            lines.Add("  ItemVisualIdentity: " + (art?.Say ?? artWhy));
            return lines;
        }

        // THE LAYOUT'S OWN STANDING FIRST: a table read on its verified front alone puts the tail's names on other bytes.
        lines.Add("  " + art.Say + (art.Agrees ? string.Empty : " - the names past the verified front may stand on other bytes"));
        (DatFile? characters, string where) = Open(files, "Characters");
        string[] bodies = Bodies(characters, seen.Model);
        lines.Add("  bodies - the file names of the models the Characters rows name under Metadata/Characters/, and his own: "
            + (bodies.Length == 0 ? "none (" + where + ")" : string.Join(", ", bodies)));

        // THE OFFSETS ARE LAID OUT ON THE FIRST ASK for the table's size (DatColumns.Layout).
        if (layouts.RowSizeOf("ItemVisualIdentity") > 0 && layouts.Tables.TryGetValue("ItemVisualIdentity", out List<DatColumn>? columns))
        {
            lines.AddRange(Columns(art.File, columns, bodies));
        }

        return lines;
    }

    /// <summary>
    /// The bodies a visual identity's per-body files may name: the file name of every model the Characters rows name under
    /// Metadata/Characters/ (IntFourB for the Sorceress), and the player's own model's.
    /// </summary>
    internal static string[] Bodies(DatFile? characters, string model)
    {
        var bodies = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path)
        {
            string clean = path.Replace('\\', '/');
            int slash = clean.LastIndexOf('/');
            string name = clean[(slash + 1)..];
            int dot = name.IndexOf('.', StringComparison.Ordinal);
            name = dot < 0 ? name : name[..dot];
            if (name.Length >= 3)
            {
                bodies.Add(name);
            }
        }

        if (model.Length > 0)
        {
            Add(model);
        }

        if (characters is not null)
        {
            IReadOnlyList<int> texts = characters.TextOffsets();
            for (var row = 0; row < characters.Rows; row++)
            {
                foreach (int offset in texts)
                {
                    if (characters.Text(row, offset) is { Length: > 0 } text && text.StartsWith("Metadata/Characters/", StringComparison.OrdinalIgnoreCase))
                    {
                        Add(text);
                    }
                }
            }
        }

        return [.. bodies];
    }

    /// <summary>
    /// Every string column and string list of a visual identity table that holds anything: how many rows fill it and how many values, by
    /// extension, how many values name each body, the folders they lie in, and its first three values.
    /// </summary>
    /// <remarks>
    /// A VALUE NAMES THE LONGEST BODY IT HOLDS, anywhere in its path, case aside - how a per-body file is named is what this measures - and a
    /// shorter body inside it is not counted again there (IntFour inside IntFourB, Four inside IntFour).
    /// </remarks>
    internal static IEnumerable<string> Columns(DatFile table, IReadOnlyList<DatColumn> columns, IReadOnlyList<string> bodies)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(bodies);
        string[] longest = [.. bodies.OrderByDescending(one => one.Length).ThenBy(one => one, StringComparer.OrdinalIgnoreCase)];
        var values = new List<string>();
        var held = 0;
        var strings = 0;
        foreach (DatColumn column in columns)
        {
            if (column.Type != "string")
            {
                continue;
            }

            strings++;
            var rows = 0;
            var count = 0;
            var extensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var folders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var named = new int[longest.Length];
            var first = new List<string>(3);
            for (var row = 0; row < table.Rows; row++)
            {
                Values(table, row, column, values);
                rows += values.Count > 0 ? 1 : 0;
                foreach (string value in values)
                {
                    count++;
                    int slash = value.LastIndexOf('/');
                    int dot = value.LastIndexOf('.');
                    Tally(extensions, dot > slash ? value[dot..].ToLowerInvariant() : "(none)");
                    Tally(folders, slash > 0 ? value[..slash] : "(no folder)");
                    Named(value, longest, named);
                    if (first.Count < 3 && !first.Contains(value, StringComparer.Ordinal))
                    {
                        first.Add(value);
                    }
                }
            }

            if (count == 0)
            {
                continue;
            }

            held++;
            yield return string.Create(CultureInfo.InvariantCulture, $"  {column.Name}{(column.Array ? " (a list)" : string.Empty)}\t{rows:N0} of {table.Rows:N0} rows, {count:N0} values");
            yield return "    extensions: " + Top(extensions, 6);
            yield return "    bodies: " + (named.Any(one => one > 0)
                ? string.Join(" | ", Enumerable.Range(0, longest.Length).Where(at => named[at] > 0).OrderByDescending(at => named[at])
                    .Select(at => string.Create(CultureInfo.InvariantCulture, $"{longest[at]} {named[at]:N0}")))
                : "none named");
            yield return string.Create(CultureInfo.InvariantCulture, $"    folders ({folders.Count:N0}): {Top(folders, 5)}");
            yield return "    first: " + string.Join(" | ", first.Select(one => "\"" + one + "\""));
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"  {held} of {strings} string columns and lists hold anything");
    }

    /// <summary>A row's values in one string column or string list, into a list made once.</summary>
    private static void Values(DatFile table, int row, DatColumn column, List<string> into)
    {
        into.Clear();
        if (!column.Array)
        {
            if (table.Text(row, column.Offset) is { Length: > 0 } one)
            {
                into.Add(one);
            }

            return;
        }

        foreach (DatReference element in table.References(row, column.Offset, 8))
        {
            if (table.TextAt(element.First) is { Length: > 0 } one)
            {
                into.Add(one);
            }
        }
    }

    /// <summary>Counts the longest body a value holds, and the shorter ones only where they stand outside the longer.</summary>
    private static void Named(string value, string[] longest, int[] named)
    {
        char[]? blanked = null;
        for (var at = 0; at < longest.Length; at++)
        {
            string body = longest[at];
            ReadOnlySpan<char> text = blanked is null ? value.AsSpan() : blanked;
            int found = text.IndexOf(body, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                continue;
            }

            named[at]++;

            // EVERY PLACE IT STANDS BLANKED, so a shorter body inside it is not counted there.
            blanked ??= value.ToCharArray();
            Span<char> span = blanked;
            for (int from = found; from >= 0;)
            {
                span.Slice(from, body.Length).Fill('/');
                int next = span[(from + body.Length)..].IndexOf(body, StringComparison.OrdinalIgnoreCase);
                from = next < 0 ? -1 : from + body.Length + next;
            }
        }
    }

    private static void Tally(Dictionary<string, int> into, string key)
        => into[key] = into.GetValueOrDefault(key) + 1;

    /// <summary>The most common keys with their counts, the most first and ties by name.</summary>
    private static string Top(Dictionary<string, int> counts, int most)
        => string.Join(" | ", counts.OrderByDescending(one => one.Value).ThenBy(one => one.Key, StringComparer.Ordinal).Take(most)
            .Select(one => string.Create(CultureInfo.InvariantCulture, $"{one.Key} {one.Value:N0}")));

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
