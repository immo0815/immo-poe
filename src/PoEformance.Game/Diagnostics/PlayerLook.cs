using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Entities;
using PoEformance.Game.Items;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// What the game holds of the local player's look, read at the press: his entity and components, the model his Animated component loaded,
/// the animation he plays, every inventory he carries with its items, the Inventories component and its lists whole, and every game file
/// a walk of pointers out from his components reaches - for the capture's player look part.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (2026-10-31): the player put on a stage as the game draws him - his class, his equipment, and his
/// cosmetics (MTX) where they can be had - not a stock body. What the files say is settled enough to build from (the class's body is the
/// Animated component's .ao; an item's look is its BaseItemTypes row's ItemVisualIdentity, whose per-body .ao columns PoE2 fills;
/// an MTX is an ItemisedVisualEffect over another visual identity, applied per slot) - but WHICH cosmetic a slot wears is in no file, and
/// no reference reads it for PoE2. So this part measures before anything is built:
///
/// - THE PLAYER as the entity list has him, his components by name, the model his Animated component loaded (ModelInfoPtr -> file record
///   -> name), and Actor's AnimationId.
/// - EVERY INVENTORY StashReader reads, each item with its path, base, unique and visual identity (ItemReader) - every id written whole up
///   to <see cref="MostPlayerId"/>, so the worn slots past StashReader.KindOf's 2 to 12 (a second weapon set, a third, a third ring - Inventories.dat
///   rows plus one, a reading the part's files side checks against the table) are in the file whatever KindOf calls them.
/// - THE INVENTORIES COMPONENT WHOLE and every list its words begin and end, each word said for what it points at, one hop on: a lead from
///   the committed captures (a list at +0x120 of entries naming two rows of tables each, one of them a row of ItemVisualIdentity's size
///   apart) says it may be the slots' visuals - PoE1's InventoryVisual records' counterpart. Not settled; written so it can be.
/// - ANIMATED, PLAYER AND PLAYERCLASS WHOLE, the same way: the attached pieces' list, if the game keeps one, starts in one of them.
/// - THE POINTER WEB: from each component, every pointer followed out to <see cref="WebDepth"/> hops, each block read
///   <see cref="WebBlock"/> bytes, and every game file path it reaches - an MSVC string (wide or narrow) or a wide C string that names a
///   file under Metadata/ or Art/ - set down with the chain that reaches it. The file records of the .ao the player wears are such strings
///   (FileInfoValue.Name), so the pieces and cosmetics the client composed his look from are named here if they hang off his components.
///   Past the module's image (its code and statics lead everywhere). CHOSEN: the depth, the block, the most blocks and the time.
///
/// Take it three times for the cosmetics: as he stands, after changing one MTX, and after swapping weapon sets - what moves is what names them.
/// </remarks>
public static class PlayerLook
{
    /// <summary>The highest inventory id whose items are written whole - the player's own run well under it, stash tabs above it in their thousands.</summary>
    public const int MostPlayerId = 200;

    /// <summary>How much of a component is written whole where its pool cell is not known.</summary>
    public const int ComponentBytes = 0x400;

    /// <summary>The most of a list begun and ended in a component that is written whole.</summary>
    public const int ListBytes = 0x1000;

    /// <summary>How much of whatever a written word points at is written one hop on.</summary>
    public const int HopBytes = 0x80;

    /// <summary>The most words one hop on a dump follows.</summary>
    public const int MostHops = 96;

    /// <summary>How many hops the pointer web goes out from a component - CHOSEN.</summary>
    public const int WebDepth = 4;

    /// <summary>How much of each block the web reads - CHOSEN.</summary>
    public const int WebBlock = 0x200;

    /// <summary>The most blocks the web reads - CHOSEN.</summary>
    public const int MostBlocks = 150_000;

    /// <summary>The most time the web takes - CHOSEN.</summary>
    public static readonly TimeSpan WebTime = TimeSpan.FromSeconds(60);

    /// <summary>The most chains written a path.</summary>
    public const int MostChains = 3;

    /// <summary>The pool cells the schema measured (its ComponentPools note) - how much of each is the component's own.</summary>
    private static readonly Dictionary<string, int> Cells = new(StringComparer.Ordinal)
    {
        ["Inventories"] = 0x150,
        ["Animated"] = 0x380,
        ["Player"] = 0x5C0,
        ["PlayerClass"] = 0x180,
        ["Actor"] = 0xCE0,
        ["Render"] = 0x630,
        ["Stats"] = 0x210,
        ["Buffs"] = 0x190,
        ["BuffOrbs"] = 0x90,
        ["Life"] = 0x420,
        ["Positioned"] = 0x550,
        ["Pathfinding"] = 0x5A0,
        ["Targetable"] = 0xA0,
    };

    /// <summary>The components written whole, word by word, with one hop from each pointer.</summary>
    private static readonly string[] Dumped = ["Inventories", "Animated", "Player", "PlayerClass", "Render"];

    /// <summary>A capture's progress and its stop.</summary>
    public sealed class Running
    {
        private int _blocks;
        private int _paths;
        private int _phase;
        private int _stop;

        /// <summary>Whether it was asked to stop.</summary>
        public bool Stopping => Volatile.Read(ref _stop) != 0;

        /// <summary>Asks it to stop, keeping what it found.</summary>
        public void Stop() => Volatile.Write(ref _stop, 1);

        /// <summary>The window's line: where it stands.</summary>
        public string Line => Volatile.Read(ref _phase) switch
        {
            0 => "reading the player, his model and his inventories",
            1 => string.Create(CultureInfo.InvariantCulture,
                $"walking the pointers out from his components: {Volatile.Read(ref _blocks):N0} blocks read, {Volatile.Read(ref _paths):N0} game files named"),
            _ => "writing",
        };

        /// <summary>How far it is.</summary>
        public float Fraction => Volatile.Read(ref _phase) switch
        {
            0 => 0.05f,
            1 => 0.1f + (0.85f * Math.Clamp(Volatile.Read(ref _blocks) / (float)MostBlocks, 0f, 1f)),
            _ => 0.97f,
        };

        internal void Phase(int phase) => Volatile.Write(ref _phase, phase);

        internal void Blocks(int blocks, int paths)
        {
            Volatile.Write(ref _blocks, blocks);
            Volatile.Write(ref _paths, paths);
        }
    }

    /// <summary>One item the player carries, as the part's files side reads it on.</summary>
    /// <param name="Inventory">The inventory's id.</param>
    /// <param name="Kind">What StashReader.KindOf calls the inventory.</param>
    /// <param name="Left">Its column in the inventory.</param>
    /// <param name="Top">Its row.</param>
    /// <param name="Entity">The item entity.</param>
    /// <param name="Path">Its metadata path - BaseItemTypes' Id.</param>
    /// <param name="Base">Its base type's name.</param>
    /// <param name="Unique">Its unique's name, or empty.</param>
    /// <param name="Ivi">Its visual identity's Id (Base+0x30), or empty.</param>
    public sealed record Carried(int Inventory, string Kind, int Left, int Top, ulong Entity, string Path, string Base, string Unique, string Ivi);

    /// <summary>What the memory side hands the files side.</summary>
    /// <param name="Path">The player's entity path.</param>
    /// <param name="Model">The .ao his Animated component loaded, or empty.</param>
    /// <param name="Items">What he carries in the inventories up to <see cref="MostPlayerId"/>.</param>
    /// <param name="Files">Every game file the pointer web reached.</param>
    public sealed record Seen(string Path, string Model, IReadOnlyList<Carried> Items, IReadOnlyList<string> Files)
    {
        /// <summary>The .ao files of the pieces he is drawn with (PlayerPieces), each once, in the composition's order.</summary>
        public IReadOnlyList<string> Pieces { get; init; } = [];
    }

    /// <summary>The part: its lines, and what the files side reads on - null where no player was read.</summary>
    public static (List<string> Lines, Seen? Seen) Run(IMemoryReader reader, OffsetSchema schema, ulong gameStatesStatic, Running running)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(running);
        var lines = new List<string>();
        running.Phase(0);
        GameChainAddresses chain = GameChain.Resolve(reader, schema, gameStatesStatic);
        var entities = new EntityReader(reader, schema);
        if (chain.PlayerEntity == 0 || entities.ReadIdentity(chain.PlayerEntity) is not { } player)
        {
            lines.Add("player look: no player entity read - not in an area, or the chain did not resolve");
            return (lines, null);
        }

        IReadOnlyDictionary<string, ulong> components = entities.ReadComponents(player.Address, player.Details);
        string model = ModelOf(reader, schema, components.GetValueOrDefault("Animated"));
        int animation = components.GetValueOrDefault("Actor") is var actor and not 0 && schema.Structs["Actor"].OffsetOf("AnimationId") is var at and >= 0
            ? reader.Read<int>(actor + (ulong)at)
            : -1;
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"player look: {player.Path} at 0x{player.Address:X}, {components.Count} components; model {(model.Length > 0 ? model : "not read")}; animation id {animation}"));

        // THE COMPONENTS, by name - what every dump and chain below names an address by.
        lines.Add(string.Empty);
        lines.Add("=== his components (name, address, how much of it is its own - the schema's pool cell where measured)");
        foreach ((string name, ulong address) in components.OrderBy(one => one.Value))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {name}\t0x{address:X}\t{(Cells.TryGetValue(name, out int cell) ? $"0x{cell:X}" : "-")}"));
        }

        Func<ulong, string> named = Namer(reader, player, components);

        // THE MODEL'S CHAIN as the schema has it, and Animated's entity pointer.
        lines.Add(string.Empty);
        lines.Add("=== the model his Animated component loaded (Animated.ModelInfoPtr -> AnimatedModelInfo.ModelFileRecordPtr -> FileInfoValue.Name)");
        if (components.GetValueOrDefault("Animated") is var animated and not 0)
        {
            ulong info = reader.ReadPointer(animated + (ulong)schema.Structs["Animated"].OffsetOf("ModelInfoPtr"));
            ulong record = info == 0 ? 0 : reader.ReadPointer(info + (ulong)schema.Structs["AnimatedModelInfo"].OffsetOf("ModelFileRecordPtr"));
            ulong entity = reader.ReadPointer(animated + (ulong)schema.Structs["Animated"].OffsetOf("AnimatedEntityPtr"));
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  ModelInfoPtr 0x{info:X} -> record 0x{record:X} -> {(model.Length > 0 ? model : "(no name)")}; AnimatedEntityPtr 0x{entity:X} = {named(entity)}"));
        }
        else
        {
            lines.Add("  no Animated component");
        }

        // EVERY INVENTORY, the player's whole.
        var carried = new List<Carried>();
        lines.Add(string.Empty);
        lines.AddRange(Inventories(reader, schema, chain, carried));

        // THE PIECES HE IS DRAWN WITH, which v0.1.393's pointer web found behind Render.Pieces alone - see PlayerPieces.
        IReadOnlyList<PlayerPieces.Piece> pieces = [];
        lines.Add(string.Empty);
        if (components.GetValueOrDefault("Render") is var render and not 0)
        {
            ulong composition = PlayerPieces.CompositionOf(reader, schema, render);
            pieces = PlayerPieces.Read(reader, schema, composition);
            lines.AddRange(Composed(reader, composition, pieces, named));
        }
        else
        {
            lines.Add("=== the pieces he is drawn with: no Render component");
        }

        // THE COMPONENTS WHOLE, word by word, every list they begin written whole, and one hop on from each pointer.
        foreach (string name in Dumped)
        {
            if (components.GetValueOrDefault(name) is var address and not 0)
            {
                lines.Add(string.Empty);
                lines.AddRange(Whole(reader, name, address, Cells.GetValueOrDefault(name, ComponentBytes), named));
            }
        }

        // THE POINTER WEB.
        running.Phase(1);
        var roots = new List<(string Root, ulong Address, int Bytes)> { ("entity", player.Address, WebBlock) };
        roots.AddRange(components.OrderBy(one => one.Key, StringComparer.Ordinal)
            .Select(one => (one.Key, one.Value, Math.Min(Cells.GetValueOrDefault(one.Key, ComponentBytes), ComponentBytes * 4))));
        Webbed web = Web(reader, roots, WebDepth, WebBlock, MostBlocks, WebTime, running);
        lines.Add(string.Empty);
        lines.AddRange(web.Said());
        running.Phase(2);
        return (lines, new Seen(player.Path, model, carried, [.. web.Paths.Keys.Order(StringComparer.OrdinalIgnoreCase)])
        {
            Pieces = [.. pieces.Select(one => one.Path).Distinct(StringComparer.OrdinalIgnoreCase)],
        });
    }

    /// <summary>
    /// The composition Render.Pieces points at: the pieces found in it (every word, every inner word), then the composition whole word by
    /// word, then every object a word of it points at, <see cref="PlayerPieces.ObjectBytes"/> of each - so its layout and whatever a held
    /// piece's socket is can be read from one capture.
    /// </summary>
    private static List<string> Composed(IMemoryReader reader, ulong composition, IReadOnlyList<PlayerPieces.Piece> pieces, Func<ulong, string> named)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"=== the pieces he is drawn with: Render.Pieces -> 0x{composition:X}, every word of its first 0x{PlayerPieces.Bytes:X} bytes that points at an object one of whose words (its first 0x{PlayerPieces.ObjectBytes:X}) points at a file record naming an .ao - the word, the object's word, the .ao (a slot's piece where the object's word is +0x{PlayerPieces.SlotInner:X})"),
        };
        if (composition == 0)
        {
            lines.Add("  Render.Pieces read nought, or the schema has no such field");
            return lines;
        }

        foreach (PlayerPieces.Piece piece in pieces)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  +0x{piece.At:X3} -> 0x{piece.Object:X} +0x{piece.Inner:X2} -> record 0x{piece.Record:X}\t{piece.Path}{(piece.Inner == PlayerPieces.SlotInner ? string.Empty : "\t(not a slot's word)")}"));
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {pieces.Count} found, {PlayerPieces.Slots(pieces).Count} slots' pieces"));
        lines.Add(string.Empty);
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"=== Render.Pieces' target whole: 0x{PlayerPieces.Bytes:X} bytes at 0x{composition:X}, each word with what it points at"));
        byte[] block = new byte[PlayerPieces.Bytes];
        if (!reader.TryRead(composition, block))
        {
            lines.Add("  did not read");
            return lines;
        }

        lines.AddRange(Words(block, composition, named));
        lines.Add(string.Empty);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"=== each object Render.Pieces' target points at, 0x{PlayerPieces.ObjectBytes:X} bytes of it, each word with what it points at"));
        var done = new HashSet<ulong>();
        byte[] inner = new byte[PlayerPieces.ObjectBytes];
        for (var at = 0; at + 8 <= block.Length; at += 8)
        {
            ulong target = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(at));
            if (!MemoryReaderExtensions.IsPlausiblePointer(target) || !done.Add(target) || !reader.TryRead(target, inner))
            {
                continue;
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  --- +0x{at:X3} -> 0x{target:X} ({named(target)})"));
            lines.AddRange(Words(inner, target, named).Select(one => "  " + one));
        }

        return lines;
    }

    /// <summary>The .ao an Animated component loaded, or empty.</summary>
    private static string ModelOf(IMemoryReader reader, OffsetSchema schema, ulong animated)
    {
        if (animated == 0)
        {
            return string.Empty;
        }

        ulong info = reader.ReadPointer(animated + (ulong)schema.Structs["Animated"].OffsetOf("ModelInfoPtr"));
        ulong record = info == 0 ? 0 : reader.ReadPointer(info + (ulong)schema.Structs["AnimatedModelInfo"].OffsetOf("ModelFileRecordPtr"));
        return record == 0 ? string.Empty : reader.ReadStdWString(record + (ulong)schema.Structs["FileInfoValue"].OffsetOf("Name"));
    }

    /// <summary>Every inventory StashReader reads: the player's own items whole, the rest counted.</summary>
    private static IEnumerable<string> Inventories(IMemoryReader reader, OffsetSchema schema, GameChainAddresses chain, List<Carried> carried)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"=== every inventory StashReader reads (ServerData's PlayerInventories): ids up to {MostPlayerId} with their items whole - id, what KindOf calls it, the grid, the items as left top width height, path | base | unique | visual identity | rarity"),
        };
        ulong serverData = chain.AreaInstance == 0 ? 0 : reader.ReadPointer(
            chain.AreaInstance + (ulong)schema.Structs["AreaInstance"].OffsetOf("PlayerInfo") + (ulong)schema.Structs["LocalPlayerStruct"].OffsetOf("ServerDataPtr"));
        if (serverData == 0)
        {
            lines.Add("  no server data read");
            return lines;
        }

        IReadOnlyList<StashInventory> all = new StashReader(reader, schema).Read(serverData);
        var items = new ItemReader(reader, schema);
        int stashTabs = 0, stashItems = 0;
        foreach (StashInventory inventory in all)
        {
            if (inventory.Id < 1 || inventory.Id > MostPlayerId)
            {
                stashTabs++;
                stashItems += inventory.Items.Count;
                continue;
            }

            string kind = StashReader.KindOf(inventory.Id).ToString().ToLowerInvariant();
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  inventory {inventory.Id}\t{kind}\t{inventory.Columns} x {inventory.Rows}\t{inventory.Items.Count} items{(inventory.Loaded ? string.Empty : ", its list not allocated")}\tat 0x{inventory.Address:X}"));
            foreach (StashedItem stashed in inventory.Items)
            {
                InspectedItem item = items.Read(stashed.Entity);
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"    {stashed.Left} {stashed.Top} {stashed.Width} {stashed.Height}\t{item.Path} | {item.Base} | {item.Unique} | {item.Ivi} | {item.RarityName}\t0x{stashed.Entity:X}"));
                carried.Add(new Carried(inventory.Id, kind, stashed.Left, stashed.Top, stashed.Entity, item.Path, item.Base, item.Unique, item.Ivi));
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {stashTabs} inventories past id {MostPlayerId} or below 1 (stash tabs, shop pages) with {stashItems} items, not written"));
        return lines;
    }

    /// <summary>
    /// A component written whole, word by word, every list it begins and ends written whole the same way, and one hop on from each pointer.
    /// </summary>
    public static List<string> Whole(IMemoryReader reader, string name, ulong address, int bytes, Func<ulong, string> named)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"=== {name} whole: 0x{bytes:X} bytes at 0x{address:X}, each word with what it points at"),
        };
        byte[] block = new byte[bytes];
        if (!reader.TryRead(address, block))
        {
            lines.Add("  did not read");
            return lines;
        }

        lines.AddRange(Words(block, address, named));

        // THE LISTS IT BEGINS AND ENDS: a word that points somewhere and the next that points at most ListBytes past it.
        var hops = new HashSet<ulong>();
        for (var at = 0; at + 16 <= bytes; at += 8)
        {
            ulong begin = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(at));
            ulong end = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(at + 8));
            if (!MemoryReaderExtensions.IsPlausiblePointer(begin) || end <= begin || end - begin > (ulong)ListBytes * 64 || (end - begin) % 8 != 0)
            {
                continue;
            }

            int length = (int)Math.Min(end - begin, (ulong)ListBytes);
            byte[] list = new byte[length];
            if (!reader.TryRead(begin, list))
            {
                continue;
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  --- the list begun at +0x{at:X} and ended at +0x{at + 8:X}: 0x{end - begin:X} bytes at 0x{begin:X}{(begin >= address && begin < address + (ulong)bytes ? ", inside the component" : string.Empty)}{(end - begin > (ulong)ListBytes ? $", the first 0x{ListBytes:X} written" : string.Empty)}"));
            lines.AddRange(Words(list, begin, named).Select(one => "  " + one));
            for (var word = 0; word + 8 <= length; word += 8)
            {
                hops.Add(BinaryPrimitives.ReadUInt64LittleEndian(list.AsSpan(word)));
            }
        }

        for (var word = 0; word + 8 <= bytes; word += 8)
        {
            hops.Add(BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(word)));
        }

        // ONE HOP ON: what each pointer in the component or its lists points at, HopBytes of it.
        var done = 0;
        foreach (ulong target in hops.Where(MemoryReaderExtensions.IsPlausiblePointer).Order())
        {
            if (done >= MostHops)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  ... {hops.Count(MemoryReaderExtensions.IsPlausiblePointer) - done} more pointers not followed"));
                break;
            }

            byte[] hop = new byte[HopBytes];
            if (!reader.TryRead(target, hop))
            {
                continue;
            }

            done++;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  --- one hop on: 0x{HopBytes:X} bytes at 0x{target:X} ({named(target)})"));
            lines.AddRange(Words(hop, target, named).Select(one => "  " + one));
        }

        return lines;
    }

    /// <summary>A block word by word: its offset, its value, and what it is - a pointer named, a string, two floats or two ints.</summary>
    public static IEnumerable<string> Words(byte[] block, ulong address, Func<ulong, string> named)
    {
        for (var at = 0; at + 8 <= block.Length; at += 8)
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(at));
            string said = MemoryReaderExtensions.IsPlausiblePointer(value) ? named(value)
                : value == 0 ? string.Empty
                : Plain(block.AsSpan(at, 8));

            // A WORD IN A POINTER'S RANGE THAT READS AS NOTHING may be two numbers - a float and a nought look like a low address.
            if (said == Unreadable)
            {
                said += "; " + Plain(block.AsSpan(at, 8));
            }

            string text = TextAt(block, at);
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  +0x{at:X3}\t{value:X16}\t{said}{(text.Length > 0 ? (said.Length > 0 ? " | " : string.Empty) + text : string.Empty)}");
        }
    }

    /// <summary>How a word in a pointer's range is said where nothing reads there.</summary>
    private const string Unreadable = "-> unreadable";

    /// <summary>A word that is no pointer said as two floats where both are plain numbers, else two ints.</summary>
    private static string Plain(ReadOnlySpan<byte> word)
    {
        float a = BinaryPrimitives.ReadSingleLittleEndian(word), b = BinaryPrimitives.ReadSingleLittleEndian(word[4..]);
        int i = BinaryPrimitives.ReadInt32LittleEndian(word), j = BinaryPrimitives.ReadInt32LittleEndian(word[4..]);
        bool floats = Number(a) && Number(b) && (MathF.Abs(a) > 1e-6f || MathF.Abs(b) > 1e-6f);
        return floats
            ? string.Create(CultureInfo.InvariantCulture, $"floats {a:G6}, {b:G6}; ints {i}, {j}")
            : string.Create(CultureInfo.InvariantCulture, $"ints {i}, {j}");
    }

    private static bool Number(float value) => float.IsFinite(value) && (value == 0f || (MathF.Abs(value) > 1e-6f && MathF.Abs(value) < 1e7f));

    /// <summary>The characters a block holds from an offset, where they read as a short run of printable UTF-16 or ASCII - else empty.</summary>
    private static string TextAt(byte[] block, int at)
    {
        string wide = Wide(block.AsSpan(at));
        if (wide.Length >= 4)
        {
            return "\"" + wide + "\"";
        }

        string narrow = Narrow(block.AsSpan(at));
        return narrow.Length >= 6 ? "'" + narrow + "'" : string.Empty;
    }

    /// <summary>Printable UTF-16 from the start of a span, to its first nought or first unprintable - at most 64 characters.</summary>
    public static string Wide(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder();
        for (var at = 0; at + 2 <= bytes.Length && text.Length < 64; at += 2)
        {
            char one = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]);
            if (one == '\0')
            {
                break;
            }

            if (one < 0x20 || one > 0x7E)
            {
                return string.Empty;
            }

            text.Append(one);
        }

        return text.ToString();
    }

    /// <summary>Printable ASCII from the start of a span, to its first nought or first unprintable - at most 64 characters.</summary>
    private static string Narrow(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder();
        foreach (byte one in bytes)
        {
            if (one == 0 || text.Length >= 64)
            {
                break;
            }

            if (one < 0x20 || one > 0x7E)
            {
                return string.Empty;
            }

            text.Append((char)one);
        }

        return text.ToString();
    }

    /// <summary>
    /// How a pointer is said: the player or one of his components plus an offset, the module's image plus one, the entity it is, or the
    /// game file whose record it is - else the address alone.
    /// </summary>
    private static Func<ulong, string> Namer(IMemoryReader reader, EntityIdentity player, IReadOnlyDictionary<string, ulong> components)
    {
        var bases = new List<(string Name, ulong From, ulong To)> { ("entity", player.Address, player.Address + 0x200) };
        foreach ((string name, ulong address) in components)
        {
            bases.Add((name, address, address + (ulong)Cells.GetValueOrDefault(name, ComponentBytes)));
        }

        ulong module = reader.ModuleBase, moduleEnd = reader.ModuleBase + reader.ModuleSize;
        var known = new Dictionary<ulong, string>();
        return address =>
        {
            foreach ((string name, ulong from, ulong to) in bases)
            {
                if (address >= from && address < to)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"-> {name}+0x{address - from:X}");
                }
            }

            if (module != 0 && address >= module && address < moduleEnd)
            {
                return string.Create(CultureInfo.InvariantCulture, $"-> module+0x{address - module:X}");
            }

            if (known.TryGetValue(address, out string? said))
            {
                return said;
            }

            Span<byte> probe = stackalloc byte[8];
            if (!reader.TryRead(address, probe))
            {
                known[address] = Unreadable;
                return Unreadable;
            }

            // A FILE RECORD names itself at +0x08 (FileInfoValue.Name); a wide string is itself.
            string recorded = reader.ReadStdWString(address + 8, 260);
            said = GamePath(recorded) ? "-> record of " + recorded
                : GamePath(reader.ReadUtf16(address, 260) is { } wide ? wide : string.Empty) ? "-> \"" + reader.ReadUtf16(address, 260) + "\""
                : "-> heap";
            known[address] = said;
            return said;
        };
    }

    /// <summary>Whether text names a game file: printable, a folder and a name, under Metadata/ or Art/ or with a file's extension.</summary>
    public static bool GamePath(string text)
        => text.Length >= 6 && text.Length <= 260 && text.Contains('/', StringComparison.Ordinal)
            && (text.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Art/", StringComparison.OrdinalIgnoreCase)
                || (text.LastIndexOf('.') is var dot and > 0 && text.Length - dot is >= 3 and <= 8))
            && text.All(one => one is >= ' ' and <= '~');

    /// <summary>What the pointer web found: each game file path with the chains reaching it, and what it read.</summary>
    public sealed record Webbed(Dictionary<string, List<string>> Paths, int Blocks, int Unread, bool Stopped, string Why)
    {
        /// <summary>The report's lines: the files under the items' and characters' folders first, then the rest, each with its chains.</summary>
        public IEnumerable<string> Said()
        {
            yield return string.Create(CultureInfo.InvariantCulture,
                $"=== the pointer web: every game file a pointer chain from his components reaches, {WebDepth} hops at most, {WebBlock:X}-byte blocks: {Blocks:N0} blocks read, {Unread:N0} pointers that did not read, {Paths.Count:N0} files{(Stopped ? " - " + Why : string.Empty)}");
            static int Rank(string path)
                => path.Contains("/Items/", StringComparison.OrdinalIgnoreCase) || path.Contains("Microtransaction", StringComparison.OrdinalIgnoreCase) || path.Contains("/MTX", StringComparison.OrdinalIgnoreCase) ? 0
                    : path.Contains("/Characters/", StringComparison.OrdinalIgnoreCase) ? 1
                    : path.EndsWith(".ao", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".aoc", StringComparison.OrdinalIgnoreCase) ? 2
                    : 3;
            foreach ((string path, List<string> chains) in Paths.OrderBy(one => Rank(one.Key)).ThenBy(one => one.Key, StringComparer.OrdinalIgnoreCase))
            {
                yield return "  " + path;
                foreach (string chain in chains)
                {
                    yield return "      " + chain;
                }
            }
        }
    }

    /// <summary>
    /// The pointer web: breadth first out from each root, every word of every block a pointer read on to the depth given; every MSVC
    /// string (wide or narrow) in a block, and every block that is itself a wide string, naming a game file set down with its chain.
    /// </summary>
    public static Webbed Web(
        IMemoryReader reader, IReadOnlyList<(string Root, ulong Address, int Bytes)> roots, int depth, int block, int mostBlocks, TimeSpan time, Running? running)
    {
        var nodes = new List<(ulong Address, int Parent, int Offset, string Root)>();
        var seen = new HashSet<ulong>();
        var queue = new Queue<(int Node, int Depth, int Bytes)>();
        foreach ((string root, ulong address, int bytes) in roots)
        {
            if (address != 0 && seen.Add(address))
            {
                nodes.Add((address, -1, 0, root));
                queue.Enqueue((nodes.Count - 1, 0, bytes));
            }
        }

        var paths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        ulong module = reader.ModuleBase, moduleEnd = reader.ModuleBase + reader.ModuleSize;
        var clock = Stopwatch.StartNew();
        int blocks = 0, unread = 0;
        string why = string.Empty;
        byte[] buffer = new byte[Math.Max(block, roots.Count == 0 ? 0 : roots.Max(one => one.Bytes))];

        void Found(string path, int node, int offset, string how)
        {
            if (!paths.TryGetValue(path, out List<string>? chains))
            {
                paths[path] = chains = [];
            }

            if (chains.Count < MostChains)
            {
                chains.Add(Chain(nodes, node, offset) + how);
            }
        }

        while (queue.Count > 0)
        {
            if (blocks >= mostBlocks || clock.Elapsed > time || running?.Stopping == true)
            {
                why = running?.Stopping == true ? "stopped" : blocks >= mostBlocks ? $"stopped at {mostBlocks:N0} blocks" : $"stopped after {time.TotalSeconds:0} s";
                break;
            }

            (int node, int level, int bytes) = queue.Dequeue();
            ulong address = nodes[node].Address;
            Span<byte> read = buffer.AsSpan(0, bytes);
            if (!reader.TryRead(address, read))
            {
                // A SHORT READ AT A PAGE'S END: the first 0x40 bytes alone, for a string.
                read = buffer.AsSpan(0, Math.Min(bytes, 0x40));
                if (!reader.TryRead(address, read))
                {
                    unread++;
                    continue;
                }
            }

            blocks++;
            if ((blocks & 0xFF) == 0)
            {
                running?.Blocks(blocks, paths.Count);
            }

            // THE BLOCK ITSELF A WIDE STRING: what a wchar_t* points at.
            if (Wide(read) is { } itself && GamePath(itself))
            {
                Found(itself, node, -1, " (a wide string)");
            }

            for (var at = 0; at + 8 <= read.Length; at += 8)
            {
                // AN MSVC STRING HERE: its size at +0x10 and its capacity at +0x18 (ReadStdWString's rule), wide or narrow.
                if (at + 32 <= read.Length)
                {
                    long size = BinaryPrimitives.ReadInt64LittleEndian(read[(at + 16)..]);
                    long capacity = BinaryPrimitives.ReadInt64LittleEndian(read[(at + 24)..]);
                    if (size >= 6 && size <= 260 && capacity >= size && capacity <= 4096)
                    {
                        string text = capacity < 8 ? string.Empty
                            : reader.ReadStdWString(address + (ulong)at, 260);
                        if (!GamePath(text))
                        {
                            text = capacity < 16 ? Narrow(read.Slice(at, 16)) : reader.ReadStdString(address + (ulong)at, 260);
                        }

                        if (GamePath(text))
                        {
                            Found(text, node, at, string.Empty);
                            continue;
                        }
                    }
                }

                ulong value = BinaryPrimitives.ReadUInt64LittleEndian(read[at..]);
                if (level + 1 > depth || !MemoryReaderExtensions.IsPlausiblePointer(value) || (module != 0 && value >= module && value < moduleEnd)
                    || (value >= address && value < address + (ulong)read.Length) || !seen.Add(value))
                {
                    continue;
                }

                nodes.Add((value, node, at, string.Empty));
                queue.Enqueue((nodes.Count - 1, level + 1, block));
            }
        }

        running?.Blocks(blocks, paths.Count);
        return new Webbed(paths, blocks, unread, why.Length > 0, why);
    }

    /// <summary>A node's chain from its root: the root, then each hop's offset in the block before it, then the offset the find is at.</summary>
    private static string Chain(List<(ulong Address, int Parent, int Offset, string Root)> nodes, int node, int offset)
    {
        var hops = new List<string>();
        int at = node;
        while (at >= 0 && nodes[at].Parent >= 0)
        {
            hops.Add(string.Create(CultureInfo.InvariantCulture, $"+0x{nodes[at].Offset:X}"));
            at = nodes[at].Parent;
        }

        hops.Reverse();
        string root = at >= 0 ? nodes[at].Root : "?";
        return root + (hops.Count > 0 ? " " + string.Join(" -> ", hops) : string.Empty) + (offset >= 0 ? string.Create(CultureInfo.InvariantCulture, $" @ +0x{offset:X}") : string.Empty);
    }
}
