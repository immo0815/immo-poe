using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Diagnostics;
using PoEformance.Game.Entities;
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
        lines.AddRange(Drawn(layouts, art, artWhy, seen, named));
        lines.AddRange(Sockets(files, seen));
        lines.AddRange(Stance(files, layouts, seen, Path.Combine(folder, Folder)));
        lines.AddRange(HeldBodies(files, layouts, art, seen, named));
        lines.AddRange(Survey(files, layouts, art, artWhy, seen));
        named.AddRange(seen.Pieces);

        // HIS OBJECT TYPE TOO, server and client halves and what they extend - where an inventory's look may name its socket.
        if (seen.Path.Length > 0)
        {
            named.Add(seen.Path + ".ot");
            named.Add(seen.Path + ".otc");
        }

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
    /// The pieces the game draws him with (PlayerPieces), each beside every ItemVisualIdentity row and column that names it, and each such
    /// row whole - which column of the per-body ones the game took for his body, with the row's others beside it (an MTX boots row naming
    /// IntB beside Int says which wins), and which rows a cosmetic piece belongs to.
    /// </summary>
    private static IEnumerable<string> Drawn(QuestTableLayouts? layouts, LoadedTable? art, string artWhy, PlayerLook.Seen seen, List<string> named)
    {
        var lines = new List<string>
        {
            string.Empty,
            "=== the pieces he is drawn with (Render.Pieces) beside every ItemVisualIdentity row and column naming one - the column the game took for his body, the row's others beside it",
        };
        if (seen.Pieces.Count == 0)
        {
            lines.Add("  none read");
            return lines;
        }

        if (layouts is null || art is not { Usable: true } || !layouts.Tables.TryGetValue("ItemVisualIdentity", out List<DatColumn>? columns))
        {
            lines.Add("  ItemVisualIdentity: " + (art?.Say ?? artWhy));
            return lines;
        }

        foreach ((string piece, IReadOnlyList<(int Row, string Column)> where) in Naming(art.File, columns, seen.Pieces))
        {
            lines.Add("  " + piece + (where.Count == 0 ? "\tno row names it - an attachment a piece's own .ao names, or a file no visual identity holds" : string.Empty));
            foreach ((int row, string column) in where)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"    row {row} {column}"));
            }

            foreach (int row in where.Select(one => one.Row).Distinct())
            {
                lines.AddRange(Row(art.File, row, columns, "    the row whole", named));
            }
        }

        return lines;
    }

    /// <summary>
    /// Where a held piece hangs, as far as one capture says: the body's rig and the sockets its chain defines (BodySockets), every word of
    /// the composition's objects and their hops naming one - a socket's or a bone's name as text, a socket's bone by its number - and the
    /// slots' objects side by side where they differ. A held piece's object differing from an armour piece's where a socket's bone or name
    /// stands is the measurement; a capture after a weapon swap holds it against another piece.
    /// </summary>
    private static IEnumerable<string> Sockets(GameFiles files, PlayerLook.Seen seen)
    {
        var lines = new List<string>
        {
            string.Empty,
            "=== where a held piece hangs: the body's rig and the sockets its .ao chain defines, every word of the pieces' objects (and their hops) naming a socket or one's bone, and the slots' objects side by side where they differ",
        };
        if (seen.Model.Length == 0)
        {
            lines.Add("  no body model read");
            return lines;
        }

        List<AnimatedObject> chain = Chain(files, seen.Model);
        string rigPath = chain.SelectMany(one => one.Named("ClientAnimationController"))
            .SelectMany(block => block.Entries)
            .FirstOrDefault(entry => string.Equals(entry.Key, "skeleton", StringComparison.Ordinal) && entry.Value.Length > 0)?.Value.Trim() ?? string.Empty;
        AnimationSkeleton rig = AnimationSkeleton.Read(files, rigPath);
        IReadOnlyList<BodySocket> sockets = BodySockets.Of(chain, rig);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  rig {(rigPath.Length > 0 ? rigPath : "(the chain names none)")}: {rig.Bones.Count} bones; {sockets.Count} sockets over {chain.Count} files ({string.Join(", ", chain.Select(Name))})"));
        static string Three(string word, Vector3 value)
            => string.Create(CultureInfo.InvariantCulture, $", {word} {value.X:0.###} {value.Y:0.###} {value.Z:0.###}");

        foreach (BodySocket socket in sockets)
        {
            string bone = socket.Bone >= 0 ? string.Create(CultureInfo.InvariantCulture, $" (bone {socket.Bone})")
                : socket.Parent.Length > 0 ? " (no such bone)" : string.Empty;
            lines.Add("    socket " + socket.Name + " -> " + (socket.Parent.Length > 0 ? socket.Parent : "(no parent)") + bone
                + (socket.Rotation != Vector3.Zero ? Three("turned", socket.Rotation) : string.Empty)
                + (socket.Translation != Vector3.Zero ? Three("shifted", socket.Translation) : string.Empty)
                + (socket.Scale != Vector3.One ? Three("scaled", socket.Scale) : string.Empty));
        }

        string[] bones = [.. rig.Bones.Select(one => one.Name)];
        lines.Add("  the rig's bones by number: " + string.Join(", ", bones.Select((one, at) => string.Create(CultureInfo.InvariantCulture, $"{at} {one}"))));
        lines.Add(string.Empty);
        lines.AddRange(Matches(seen.Objects, sockets, bones));
        lines.Add(string.Empty);
        lines.AddRange(Beside(seen));
        return lines;
    }

    /// <summary>A body's .ao and everything it extends, nearest first.</summary>
    private static List<AnimatedObject> Chain(GameFiles files, string path)
    {
        var chain = new List<AnimatedObject>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>([path]);
        while (queue.Count > 0 && chain.Count < MostHops * 2)
        {
            string one = queue.Dequeue().Trim();
            string file = Path.HasExtension(one) ? one : one + ".ao";
            if (!seen.Add(file) || AnimatedObject.Read(files, file) is not { Ready: true } ao)
            {
                continue;
            }

            chain.Add(ao);
            foreach (string up in ao.Extends.Where(up => !up.Contains("nothing", StringComparison.OrdinalIgnoreCase)))
            {
                queue.Enqueue(up);
            }
        }

        return chain;
    }

    /// <summary>
    /// Every word of the objects naming a socket or a bone: a 32-bit number one of the sockets' bones carries (nought left out - it is
    /// everywhere), and narrow or wide text at a word's start spelling a socket's or a bone's name - each with where it was found.
    /// </summary>
    internal static IEnumerable<string> Matches(IReadOnlyList<PlayerLook.PieceObject> objects, IReadOnlyList<BodySocket> sockets, IReadOnlyList<string> bones)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(sockets);
        ArgumentNullException.ThrowIfNull(bones);
        var byBone = new Dictionary<int, List<string>>();
        foreach (BodySocket socket in sockets.Where(one => one.Bone > 0))
        {
            (byBone.TryGetValue(socket.Bone, out List<string>? named) ? named : byBone[socket.Bone] = []).Add(socket.Name);
        }

        var names = new HashSet<string>(sockets.Select(one => one.Name).Concat(bones), StringComparer.OrdinalIgnoreCase);
        yield return string.Create(CultureInfo.InvariantCulture,
            $"  every word naming a socket or a socket's bone, over {objects.Count} blocks of the pieces' objects and their hops:");
        var found = 0;
        foreach (PlayerLook.PieceObject one in objects)
        {
            byte[] bytes = one.Bytes;
            for (var at = 0; at + 4 <= bytes.Length; at += 4)
            {
                int value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
                if (byBone.TryGetValue(value, out List<string>? named))
                {
                    found++;
                    yield return string.Create(CultureInfo.InvariantCulture,
                        $"    {one.Label} +0x{at:X3}\tint {value}\tbone {bones[value]} - socket {string.Join(", ", named)}");
                }

                if (at % 8 != 0)
                {
                    continue;
                }

                string narrow = PlayerLook.Narrow(bytes.AsSpan(at)), wide = PlayerLook.Wide(bytes.AsSpan(at));
                foreach (string text in new[] { narrow, wide }.Where(text => text.Length >= 3 && names.Contains(text)))
                {
                    found++;
                    yield return string.Create(CultureInfo.InvariantCulture, $"    {one.Label} +0x{at:X3}\t\"{text}\"\t{(sockets.Any(socket => string.Equals(socket.Name, text, StringComparison.OrdinalIgnoreCase)) ? "a socket" : "a bone")}");
                }
            }
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"  {found} found");
    }

    /// <summary>
    /// The slots' objects side by side - one column a slot, its piece's file name over it - every 32-bit word where they do not all agree,
    /// as a number and, where it reads as one, as a float: what tells a held piece's object from a skin's.
    /// </summary>
    internal static IEnumerable<string> Beside(PlayerLook.Seen seen)
    {
        ArgumentNullException.ThrowIfNull(seen);
        var slots = new List<(string Name, byte[] Bytes)>();
        foreach ((int at, int inner, string path) in seen.Slots.Where(one => one.Inner == PlayerPieces.SlotInner))
        {
            string label = string.Create(CultureInfo.InvariantCulture, $"+0x{at:X3}");
            if (seen.Objects.FirstOrDefault(one => string.Equals(one.Label, label, StringComparison.Ordinal)) is { } found
                && !slots.Any(one => one.Name.StartsWith(label, StringComparison.Ordinal)))
            {
                slots.Add((label + " " + Name(path), found.Bytes));
            }
        }

        yield return string.Create(CultureInfo.InvariantCulture,
            $"  the slots' objects side by side, every 32-bit word where they differ: {(slots.Count == 0 ? "none read" : string.Join(" | ", slots.Select(one => one.Name)))}");
        if (slots.Count < 2)
        {
            yield break;
        }

        int length = slots.Min(one => one.Bytes.Length);
        for (var at = 0; at + 4 <= length; at += 4)
        {
            int[] values = [.. slots.Select(one => BinaryPrimitives.ReadInt32LittleEndian(one.Bytes.AsSpan(at)))];
            if (values.All(one => one == values[0]))
            {
                continue;
            }

            yield return string.Create(CultureInfo.InvariantCulture, $"    +0x{at:X3}\t") + string.Join(" | ", values.Select(Said));
        }
    }

    /// <summary>A 32-bit word as a number, and as a float beside it where it reads as a plain one.</summary>
    private static string Said(int value)
    {
        float single = BitConverter.Int32BitsToSingle(value);
        return float.IsFinite(single) && single != 0f && MathF.Abs(single) is > 1e-4f and < 1e6f
            ? string.Create(CultureInfo.InvariantCulture, $"{value} ({single:G5})")
            : value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A path's file name.</summary>
    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>A loaded .ao's own file name is not kept, so a chain is named by what it extends.</summary>
    private static string Name(AnimatedObject ao) => ao.Extends.Count > 0 ? "extends " + string.Join(", ", ao.Extends) : "(the root)";

    /// <summary>
    /// ItemVisualHeldBodyModel whole - an item's look held on the body, by class: an .ao and the BONE it hangs at (dat-schema's PoE2 layout:
    /// seven named classes' objects and bones, fourteen unnamed strings after) - each row's visual identity by its Id; rows whose visual
    /// identity a carried item names, or whose strings name a drawn piece, marked.
    /// </summary>
    private static IEnumerable<string> HeldBodies(GameFiles files, QuestTableLayouts? layouts, LoadedTable? art, PlayerLook.Seen seen, List<string> named)
    {
        var lines = new List<string>
        {
            string.Empty,
            "=== ItemVisualHeldBodyModel whole - an item's look held on the body, by class: the .ao and the bone it hangs at; its visual identity by Id, a row marked * where a carried item's visual identity or a drawn piece is its",
        };
        if (layouts is null || !layouts.Tables.ContainsKey("ItemVisualHeldBodyModel"))
        {
            lines.Add("  the vendored layout has no ItemVisualHeldBodyModel");
            return lines;
        }

        (LoadedTable? table, string why) = QuestTables.Open(files, layouts, "ItemVisualHeldBodyModel", null, "MarauderAnimatedObject", "MarauderBone");
        lines.Add("  " + (table?.Say ?? why));
        if (table is not { Usable: true })
        {
            return lines;
        }

        List<DatColumn> columns = layouts.Tables["ItemVisualHeldBodyModel"];
        int visualAt = layouts.OffsetOf("ItemVisualHeldBodyModel", "ItemVisualIdentity");
        int artIdAt = art is { Usable: true } ? layouts.OffsetOf("ItemVisualIdentity", "Id") : -1;
        var carried = new HashSet<string>(seen.Items.Select(one => one.Ivi).Where(one => one.Length > 0), StringComparer.OrdinalIgnoreCase);
        var drawn = new HashSet<string>(seen.Pieces, StringComparer.OrdinalIgnoreCase);
        for (var row = 0; row < table.File.Rows; row++)
        {
            int visual = visualAt >= 0 && art is not null ? table.File.Reference(row, visualAt).RowIn(art.File.Rows) : -1;
            string id = visual >= 0 && artIdAt >= 0 ? art!.File.Text(visual, artIdAt) : string.Empty;
            var said = new List<string>();
            foreach (DatColumn column in columns.Where(one => one.Type == "string" && !one.Array))
            {
                if (table.File.Text(row, column.Offset) is { Length: > 0 } value)
                {
                    said.Add(column.Name + " = " + value);
                    if (PlayerLook.GamePath(value))
                    {
                        named.Add(value);
                    }
                }
            }

            bool mine = carried.Contains(id) || said.Any(one => drawn.Contains(one[(one.IndexOf(" = ", StringComparison.Ordinal) + 3)..]));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {(mine ? "*" : " ")}row {row}: {(id.Length > 0 ? id : visual >= 0 ? "visual identity row " + visual.ToString(CultureInfo.InvariantCulture) : "no visual identity")}\t{string.Join(" | ", said)}"));
        }

        return lines;
    }

    /// <summary>For each path, every row and string column (or string list) of a table holding it exactly, the rows in order.</summary>
    internal static IEnumerable<(string Path, IReadOnlyList<(int Row, string Column)> Where)> Naming(
        DatFile table, IReadOnlyList<DatColumn> columns, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(paths);
        string[] distinct = [.. paths.Distinct(StringComparer.OrdinalIgnoreCase)];
        var found = distinct.ToDictionary(one => one, _ => new List<(int Row, string Column)>(), StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();
        foreach (DatColumn column in columns.Where(one => one.Type == "string"))
        {
            for (var row = 0; row < table.Rows; row++)
            {
                Values(table, row, column, values);
                foreach (string value in values)
                {
                    if (found.TryGetValue(value, out List<(int Row, string Column)>? into))
                    {
                        into.Add((row, column.Name + (column.Array ? " (a list)" : string.Empty)));
                    }
                }
            }
        }

        return [.. distinct.Select(one => (one, (IReadOnlyList<(int Row, string Column)>)[.. found[one].OrderBy(at => at.Row)]))];
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

    /// <summary>
    /// His stance as the Monster Book reads it (PlayerStance over what the memory side read), the pack's every animation with its frames and
    /// whether its bones are his rig's, and the pack's .amd and the timeline written as the install holds them - their formats unread.
    /// </summary>
    private static IEnumerable<string> Stance(GameFiles files, QuestTableLayouts? layouts, PlayerLook.Seen seen, string folder)
    {
        var lines = new List<string>
        {
            string.Empty,
            "=== his weapon sets as the Monster Book reads them (PlayerStance): the pack and timeline his Actor holds, each set's item stances, the set in hand, each set's hands from his equipment looks, its pack and its idle",
        };
        PlayerDress dress = PlayerDress.Composed(
            new PlayerDress(seen.Path, string.Empty, seen.Model, seen.Pieces, string.Empty) { Moves = seen.Moves, Timeline = seen.Timeline, Looks = seen.Looks },
            seen.Sets, seen.Weapon1, seen.Weapon2, seen.Offhand2);
        lines.Add("  the composition alone: " + (dress.InHand >= 0 ? $"set {dress.InHand + 1} in hand" : "no set in hand") + " - "
            + string.Join("; ", dress.Sets.Select((one, at) => $"set {at + 1} {Tail(one.MainPiece)} + {Tail(one.OffPiece)}")));
        PlayerDress read = PlayerStance.Resolve(files, layouts, dress);
        lines.AddRange(read.Stance.Select(one => "  " + one));
        lines.Add(read.InHand >= 0 ? $"  the set in hand: set {read.InHand + 1}" : "  the set in hand: not read");
        for (var set = 0; set < read.Sets.Count; set++)
        {
            PlayerSet one = read.Sets[set];
            lines.Add($"  set {set + 1}{(set == read.InHand ? " (in hand)" : string.Empty)}: {Tail(one.MainItem)} + {Tail(one.OffItem)}, stance {(one.Stance.Length > 0 ? one.Stance : "(none)")}"
                + $" - the idle the Monster Book starts it with: {(one.Opening.Length > 0 ? one.Opening : "(none - the pack's or his rig's first)")}");
            lines.AddRange(one.Said.Select(line => "    " + line));
        }

        lines.Add("  never hung as skins, a hand's: " + (read.Held.Count > 0 ? string.Join(", ", read.Held) : "(none)"));
        if (seen.Moves.Length == 0)
        {
            return lines;
        }

        AnimationSkeleton pack = AnimationSkeleton.Read(files, seen.Moves);
        string rigPath = seen.Model.Length > 0 ? RigOf(files, seen.Model) : string.Empty;
        AnimationSkeleton rig = rigPath.Length > 0 ? AnimationSkeleton.Read(files, rigPath) : AnimationSkeleton.None;
        string packSaid = string.Create(CultureInfo.InvariantCulture, $"  the pack {seen.Moves}: {pack.Bones.Count} bones, {pack.Animations.Count} animations");
        string rigSaid = string.Create(CultureInfo.InvariantCulture, $"his rig {(rigPath.Length > 0 ? rigPath : "(not found)")}: {rig.Bones.Count} bones, {rig.Animations.Count} animations");
        lines.Add(packSaid + (pack.Why.Length > 0 ? " - " + pack.Why : string.Empty) + "; " + rigSaid + " - "
            + (MonsterModels.SameBones(rig, pack) ? "the same bones, name for name and bound alike - the pack poses his mesh"
                : $"NOT the same bones - the pack is not taken; {Unlike(rig, pack)}"));
        foreach (SkeletonAnimation one in pack.Animations)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"    {one.Name}\trate {one.Rate}, {one.Tracks} tracks, {one.Length:N0} bytes of keyframes"));
        }

        // THE OTHER SETS' PACKS, which the Monster Book's switch plays: each named by its stance's timeline, its count and whether it poses him.
        foreach (PlayerSet other in read.Sets.Where(one => one.Moves.Length > 0 && !string.Equals(one.Moves, seen.Moves, StringComparison.OrdinalIgnoreCase)))
        {
            AnimationSkeleton its = AnimationSkeleton.Read(files, other.Moves);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  the pack {other.Moves} (stance {other.Stance}): {its.Bones.Count} bones, {its.Animations.Count} animations")
                + (its.Why.Length > 0 ? " - " + its.Why : string.Empty) + " - "
                + (MonsterModels.SameBones(rig, its) ? "his bones, name for name and bound alike" : $"NOT his bones; {Unlike(rig, its)}"));
        }

        // THE PACK'S METADATA AND THE TIMELINE AS THE INSTALL HOLDS THEM: no reader of either yet, so the bytes, and whether they read as text.
        string amd = seen.Moves.EndsWith(".ast", StringComparison.OrdinalIgnoreCase) ? seen.Moves[..^4] + ".amd" : string.Empty;
        foreach (string path in new[] { amd, seen.Timeline }.Where(one => one.Length > 0))
        {
            byte[]? bytes = GameArt.ReadRaw(files, path);
            if (bytes is not { Length: > 0 })
            {
                lines.Add("  " + path + "\tnot in the install");
                continue;
            }

            string to = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.WriteAllBytes(to, bytes);
            string text = StatDescriptionFiles.Decode(bytes);
            double printable = text.Length == 0 ? 0 : text.Count(one => one is '\t' or '\n' or '\r' || (one >= ' ' && one != '�')) / (double)text.Length;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {path}\t{bytes.Length:N0} bytes written as the install holds them; decoded {text.Length:N0} characters, {printable:P0} printable"));
            if (printable > 0.95)
            {
                lines.AddRange(text.Split('\n').Take(MostTextLines).Select(one => "    | " + one.TrimEnd('\r')));
            }
        }

        return lines;
    }

    /// <summary>A path's file name, or "(empty)".</summary>
    private static string Tail(string path) => path.Length == 0 ? "(empty)" : path[(path.LastIndexOf('/') + 1)..];

    /// <summary>Where two rigs first part: a count, a name or a bind (its widest entry's gap past MonsterModels' hundredth), said with both halves.</summary>
    private static string Unlike(AnimationSkeleton rig, AnimationSkeleton pack)
    {
        if (rig.Bones.Count != pack.Bones.Count)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{rig.Bones.Count} bones against {pack.Bones.Count}");
        }

        for (var one = 0; one < rig.Bones.Count; one++)
        {
            SkeletonBone mine = rig.Bones[one], theirs = pack.Bones[one];
            if (!string.Equals(mine.Name, theirs.Name, StringComparison.Ordinal))
            {
                return string.Create(CultureInfo.InvariantCulture, $"bone {one} is {mine.Name} in his rig and {theirs.Name} in the pack");
            }

            Matrix4x4 gap = mine.Bind - theirs.Bind;
            float widest = new[] { gap.M11, gap.M12, gap.M13, gap.M21, gap.M22, gap.M23, gap.M31, gap.M32, gap.M33, gap.M41, gap.M42, gap.M43 }
                .Max(MathF.Abs);
            if (widest > 0.01f)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"bone {one} ({mine.Name}) is bound otherwise, {widest:0.###} apart at the widest: at {mine.Bind.Translation} in his rig, {theirs.Bind.Translation} in the pack");
            }
        }

        return "they agree";
    }

    /// <summary>The most lines of a text file the stance's section quotes.</summary>
    private const int MostTextLines = 400;

    /// <summary>The skeleton a model's .ao chain names (ClientAnimationController's skeleton), or empty.</summary>
    private static string RigOf(GameFiles files, string model)
    {
        foreach (AnimatedObject one in Chain(files, model))
        {
            foreach (AoStruct block in one.Named("ClientAnimationController"))
            {
                foreach (AoEntry entry in block.Entries)
                {
                    if (string.Equals(entry.Key, "skeleton", StringComparison.Ordinal) && entry.Value.Trim() is { Length: > 0 } rig)
                    {
                        return rig;
                    }
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// What a text file extends, with the file's own extension where it names none - or null. An .act names its parent on a
    /// <c>parent "…"</c> line (IntFour.act's first, behind the byte order mark: <c>parent "Metadata/Characters/CharacterFemale.act"</c>).
    /// </summary>
    internal static string? Extends(string content, string path)
    {
        ArgumentNullException.ThrowIfNull(content);
        string word = path.EndsWith(".act", StringComparison.OrdinalIgnoreCase) ? "parent" : "extends";
        foreach (string raw in content.Split('\n'))
        {
            string line = raw.Trim().TrimStart('﻿');
            int open = line.IndexOf('"', StringComparison.Ordinal);
            int close = open < 0 ? -1 : line.IndexOf('"', open + 1);
            if (line.StartsWith(word, StringComparison.Ordinal) && open >= 0 && close > open)
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
