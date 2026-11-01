using System.Runtime.CompilerServices;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The player's weapon sets as the Monster Book draws them, read from his files: each set's stance and the idle it plays (his class's .act -
/// Characters.ACTFile - and what it extends, the sets' items' stances - BaseItemTypes.ItemStance, else their ItemClasses', in ItemStances),
/// the set in hand (the one whose stance's timeline his Actor holds, PlayerDress.Timeline), each set's animation pack, and the pieces in each
/// set's hands from his equipment looks (PlayerDress.Looks) and the visual identities they name.
/// </summary>
/// <remarks>
/// READ FROM THE FILES (v0.1.397, the 16:16 capture of 2026-10-31 in The Red Vale; v0.1.398, the 22:17 capture after a weapon swap), each
/// step said in <see cref="PlayerDress.Stance"/> and a set's <see cref="PlayerSet.Said"/>:
/// <list type="bullet">
/// <item>A STANCE'S NAME IS ITS HANDS' ITEM STANCES, main then off: Character.act's stances read so throughout - DaggerShield, DaggerBuckler,
///   OneHandSwordSceptre, WandSpiritShield, BowQuiver, ClawClaw - an empty hand adding nothing (Wand alone, Shield alone under Unarmed) and
///   none at all being Unarmed. The ids are ItemStances' own; which a focus is, SpiritShield, the stances' wildcards say too (wand_shield,
///   dagger_focus, unarmed_focus).</item>
/// <item>THE SET IN HAND is the one whose stance's Base timeline is the timeline his Actor holds - base_wand.atl at 16:16, base_staff.atl at
///   22:17, each its set's own; where none is, the composition's say (PlayerDress.Composed).</item>
/// <item>A SET'S PACK is the Actor's for the set in hand, and for the other the .ast its stance's timeline names: an .atl is JSON whose
///   <c>"skeleton"</c> is the pack - base_staff.atl names BaseStaff/OneRig.ast, the very pack the Actor held at 22:17 (MEASURED).</item>
/// <item>ITS IDLE is the nearest Idle up the stance tree, its <c>*</c> the nearest wildcard - <c>idle_*_01</c> and <c>wand_shield</c>, so
///   <c>idle_wand_shield_01</c>, <c>staff</c> so <c>idle_staff_01</c> - and the replace rules of its tags (IntFour.act's ClassSpecific one)
///   applied; taken where the set's pack has an animation of the name.</item>
/// <item>A HAND'S PIECE is the look of its slot (EquipmentLook: set 1's main hand slot 0, its off hand 1, set 2's 2 and 3 - the captures'
///   order) - the held model its visual identity names, the one the game draws where the composition holds it: an MTX row names it in one
///   column of its item class (the wand's column_33, the staff's column_38 at +0x188, the focus's column_81), a base row in AOFile2. A
///   slot whose class row is not the item's gives way to the one held look of the item's class. Where no look serves, the composition's
///   held slot, then the item's own model (its base type's visual identity, decision 6 of the live client: the equipment where no cosmetic
///   reads, the line saying so).</item>
/// </list>
/// NOT READ: a town's stance (<c>WandSpiritShieldTown</c>, Idle::town's <c>idle_relaxed_*_01</c>), the female's persistent layer
/// (CharacterFemale.act's <c>append_persistent PersistentIdleFemale</c>), the additive layers the actions name.
/// </remarks>
public static class PlayerStance
{
    /// <summary>The most .act files followed up a chain.</summary>
    private const int MostHops = 8;

    /// <summary>The tables a resolve reads, opened once an install.</summary>
    private static readonly ConditionalWeakTable<GameFiles, Tables> Opened = [];

    /// <summary>The columns of a visual identity row that are no held model: its id, icon, the model it drops as and the one it is sheathed as.</summary>
    private static readonly HashSet<string> NotHeld = new(StringComparer.Ordinal) { "Id", "DDSFile", "AOFile", "SheathAnimatedObject", "EPKFile", "AnimationLocation" };

    /// <summary>His weapon sets resolved from the files into <paramref name="dress"/> - see the class's remarks.</summary>
    public static PlayerDress Resolve(GameFiles files, QuestTableLayouts? layouts, PlayerDress dress)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(dress);
        if (!dress.Ready)
        {
            return dress;
        }

        var said = new List<string>
        {
            dress.Moves.Length > 0 && dress.Timeline.Length > 0
                ? $"his Actor's stance: the pack {dress.Moves}, the timeline {dress.Timeline}"
                : "no animation pack or timeline read off his Actor (Actor.AnimationSet)",
        };
        if (layouts is null)
        {
            said.Add("data/item-tables.json did not load - his sets and their hands as the composition says them, no stance read");
            return dress with { Stance = said };
        }

        Tables tables = Opened.GetValue(files, one => new Tables(one, layouts));
        if (tables.Why.Length > 0)
        {
            said.Add(tables.Why + " - his sets and their hands as the composition says them");
            return dress with { Stance = said };
        }

        // THE STANCES of every set, from his .act.
        ActStance? root = null;
        string wildcard = string.Empty;
        string act = tables.ActOf(dress.Path);
        if (act.Length == 0)
        {
            said.Add($"no Characters row names {dress.Path} - no .act, no stance");
        }
        else
        {
            (ActStance merged, string merges, List<string> chain) = Merged(files, act);
            said.Add($"his .act: {string.Join(" <- ", chain)}, the default wildcard \"{merges}\"");
            (root, wildcard) = merged.Stances.Count > 0 ? (merged, merges) : (null, merges);
            if (root is null)
            {
                said.Add("no stance read in it");
            }
        }

        int count = dress.Sets.Count;
        var stances = new ActStance?[count];
        var names = new string[count];
        var timelines = new string[count];
        int inHand = -1;
        for (var set = 0; set < count; set++)
        {
            PlayerSet one = dress.Sets[set];
            string mainStance = one.MainItem.Length > 0 ? tables.ItemStanceOf(one.MainItem) : string.Empty;
            string offStance = one.OffItem.Length > 0 ? tables.ItemStanceOf(one.OffItem) : string.Empty;
            names[set] = mainStance + offStance is { Length: > 0 } both ? both : "Unarmed";
            stances[set] = root?.Find(names[set]);
            timelines[set] = stances[set] is { } stance ? TimelineOf(stance) : string.Empty;
            bool held = dress.Timeline.Length > 0 && timelines[set].Length > 0
                && string.Equals(timelines[set], dress.Timeline, StringComparison.OrdinalIgnoreCase);
            if (held && inHand < 0)
            {
                inHand = set;
            }

            said.Add($"set {set + 1}: {Tail(one.MainItem)} + {Tail(one.OffItem)} - item stances \"{mainStance}\" + \"{offStance}\" - stance {names[set]}: "
                + (root is null ? "no .act read"
                    : stances[set] is null ? "not in the .act"
                    : $"its Base timeline {Tail(timelines[set])}{(held ? " - his Actor's, the set in hand" : string.Empty)}"));
        }

        if (inHand < 0)
        {
            inHand = dress.InHand;
            said.Add((dress.Timeline.Length == 0 ? "his Actor named no timeline" : "no set's stance has his Actor's timeline")
                + (inHand >= 0 ? $" - the set in hand as the composition says it: set {inHand + 1}" : " - which set is in hand is not read"));
        }

        // THE HANDS of every set, from his looks; then THE PACK AND THE IDLE of each.
        (List<PlayerSet> sets, List<string> heldPieces) = Hands(files, layouts, tables, dress, inHand, said);
        var packs = new Dictionary<string, AnimationSkeleton>(StringComparer.OrdinalIgnoreCase);
        for (var set = 0; set < count; set++)
        {
            var lines = new List<string>(sets[set].Said);
            string named = timelines[set].Length > 0 ? SkeletonOf(files, timelines[set]) : string.Empty;
            string moves = set == inHand && dress.Moves.Length > 0 ? dress.Moves : named;
            lines.Add(set == inHand && dress.Moves.Length > 0
                ? $"pack: {moves} - his Actor's" + (named.Length == 0 ? string.Empty
                    : string.Equals(named, moves, StringComparison.OrdinalIgnoreCase) ? ", the one its timeline names too" : $" - its timeline names {named}")
                : named.Length > 0 ? $"pack: {named} - the one its stance's timeline {Tail(timelines[set])} names"
                : timelines[set].Length > 0 ? $"pack: its stance's timeline {timelines[set]} names none - his rig's own" : "pack: no stance read - his rig's own");
            if (!packs.TryGetValue(moves, out AnimationSkeleton? pack))
            {
                pack = moves.Length > 0 ? AnimationSkeleton.Read(files, moves) : AnimationSkeleton.None;
                packs[moves] = pack;
            }

            string opening = string.Empty;
            if (stances[set] is { } stance)
            {
                (ActAction? idle, ActStance? from) = stance.ActionOf("Idle");
                if (idle is null || idle.Animation.Length == 0)
                {
                    lines.Add($"idle: stance {stance.Name} has no Idle up its tree");
                }
                else
                {
                    string name = Named(stance, idle, wildcard);
                    bool there = pack.Animations.Any(one => string.Equals(one.Name, name, StringComparison.Ordinal));
                    opening = there ? name : string.Empty;
                    lines.Add($"idle: Idle \"{idle.Animation}\" (written in {(from?.Name is { Length: > 0 } written ? written : "the file's block")}), "
                        + $"wildcard \"{stance.WildcardOf(wildcard)}\" - {name}: "
                        + (there ? $"in the pack, one of its {pack.Animations.Count}" : $"NOT in the pack's {pack.Animations.Count} animations - not chosen"));
                }
            }

            sets[set] = sets[set] with { Stance = names[set], Moves = moves, Opening = opening, Said = lines };
        }

        return dress with { Sets = sets, InHand = inHand, Held = heldPieces, Stance = said };
    }

    /// <summary>
    /// Every set's hands from his looks (the class's remarks) - each set with its pieces and their lines - and every piece of the composition
    /// that is a hand's, of any set, so none is hung as a skin.
    /// </summary>
    private static (List<PlayerSet> Sets, List<string> Held) Hands(
        GameFiles files, QuestTableLayouts layouts, Tables tables, PlayerDress dress, int inHand, List<string> said)
    {
        Visuals visuals = Visuals.Open(files, layouts);
        if (visuals.Why.Length > 0)
        {
            said.Add(visuals.Why + " - the hands as the composition says them");
        }

        said.Add(dress.Looks.Count == 0 ? "his equipment looks (Inventories.Looks) read nothing - the hands as the composition says them, else the items' own models"
            : $"his equipment looks: {string.Join(", ", dress.Looks.Select(one => $"slot {one.Slot} class row {one.ClassRow}{(one.Held ? " held" : string.Empty)} {(one.Visual.Length > 0 ? one.Visual : "(no id)")}"))}");
        var held = new HashSet<string>(dress.Held, StringComparer.OrdinalIgnoreCase);
        foreach (EquipmentLook look in dress.Looks.Where(one => one.Held))
        {
            foreach (string model in visuals.HeldModels(look.Visual))
            {
                if (dress.Pieces.Contains(model, StringComparer.OrdinalIgnoreCase))
                {
                    held.Add(model);
                }
            }
        }

        var claimed = new HashSet<int>();
        var sets = new List<PlayerSet>(dress.Sets.Count);
        for (var set = 0; set < dress.Sets.Count; set++)
        {
            PlayerSet one = dress.Sets[set];
            (string main, string mainSaid) = Hand(tables, visuals, dress, (set, set == inHand), 0, one.MainItem, one.MainPiece, claimed);
            (string off, string offSaid) = Hand(tables, visuals, dress, (set, set == inHand), 1, one.OffItem, one.OffPiece, claimed);
            foreach (string piece in new[] { main, off }.Where(piece => piece.Length > 0))
            {
                held.Add(piece);
            }

            sets.Add(one with { MainPiece = main, OffPiece = off, Said = [.. new[] { mainSaid, offSaid }.Where(line => line.Length > 0)] });
        }

        return (sets, [.. held]);
    }

    /// <summary>One hand's piece and its line - see the class's remarks; <paramref name="composed"/> is the composition's say for it.</summary>
    private static (string Piece, string Said) Hand(
        Tables tables, Visuals visuals, PlayerDress dress, (int Index, bool InHand) set, int hand, string item, string composed, HashSet<int> claimed)
    {
        string which = hand == 0 ? "main hand" : "off hand";
        if (item.Length == 0)
        {
            return composed.Length > 0 ? (composed, $"{which}: {composed} - the composition's held slot, though his inventories name no item there") : (string.Empty, string.Empty);
        }

        int kind = tables.ClassOf(item);
        (int ownRow, string ownId) = visuals.OwnOf(tables, item);
        string own = ownRow >= 0 ? visuals.HeldModels(ownRow).FirstOrDefault() ?? string.Empty : string.Empty;
        string why = string.Empty;
        EquipmentLook? look = null;
        foreach (EquipmentLook one in dress.Looks)
        {
            if (one.Held && !claimed.Contains(one.Slot) && EquipmentLook.SetOf(one.Slot, dress.Sets.Count) == (set.Index, hand))
            {
                look = one;
                break;
            }
        }

        // MEMORY'S CLASS IS THE ROW PLUS ONE (EquipmentLook.ClassRow, MEASURED): v0.1.398 compared the number itself and turned every look away.
        if (look is { } bySlot && kind >= 0 && bySlot.ClassRow >= 0 && bySlot.ClassRow != kind)
        {
            why = $"slot {bySlot.Slot}'s class row {bySlot.ClassRow} is not the item's {kind}";
            look = null;
        }

        if (look is null && kind >= 0)
        {
            List<EquipmentLook> same = [.. dress.Looks.Where(one => one.Held && one.ClassRow == kind && !claimed.Contains(one.Slot))];
            look = same.Count == 1 ? same[0] : null;
            why += (why.Length > 0 ? "; " : string.Empty) + (same.Count == 1 ? $"slot {same[0].Slot} is the one held look of class {kind}" : $"{same.Count} held looks of class {kind}");
        }

        if (look is { } found)
        {
            claimed.Add(found.Slot);
            IReadOnlyList<string> models = visuals.HeldModels(found.Visual);
            string piece = Pick(models, dress.Pieces, own);
            string from = $"the look of slot {found.Slot} ({(found.Visual.Length > 0 ? found.Visual : "no id read")}, class row {found.ClassRow}; the item's {kind})";
            if (piece.Length > 0)
            {
                bool kept = dress.Pieces.Contains(piece, StringComparer.OrdinalIgnoreCase);
                return (piece, $"{which}: {piece} - "
                    + (kept && set.InHand ? "as the game draws it"
                        : kept ? "the composition holds it, not drawn while the other set is in hand"
                        : string.Equals(found.Visual, ownId, StringComparison.OrdinalIgnoreCase) ? "the item's own model" : "its cosmetic")
                    + $", {from}{(models.Count > 1 ? $" - one of {models.Count} held models it names" : string.Empty)}{(why.Length > 0 ? " - " + why : string.Empty)}");
            }

            why = $"{from} names no held model{(why.Length > 0 ? " - " + why : string.Empty)}";
        }

        if (composed.Length > 0)
        {
            return (composed, $"{which}: {composed} - the composition's held slot" + (why.Length > 0 ? $" ({why})" : " (no look of its slot read)"));
        }

        return own.Length > 0
            ? (own, $"{which}: {own} - the item's own model ({Tail(item)}'s base type's visual identity {ownId}): "
                + (why.Length > 0 ? why : "no look of its slot read") + " - a cosmetic it may wear is not drawn")
            : (string.Empty, $"{which}: nothing drawn for {Tail(item)} - " + (why.Length > 0 ? why : "no look of its slot and no model of its own read"));
    }

    /// <summary>The held model to hang: the one the composition draws, else the only one, else the one nearest the item's own model's folder.</summary>
    internal static string Pick(IReadOnlyList<string> models, IReadOnlyList<string> pieces, string own)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(pieces);
        foreach (string one in models)
        {
            if (pieces.Contains(one, StringComparer.OrdinalIgnoreCase))
            {
                return one;
            }
        }

        return models.Count == 0 ? string.Empty
            : models.Count == 1 || own.Length == 0 ? models[0]
            : models.OrderByDescending(one => Shared(one, own)).First();
    }

    /// <summary>How many folders two paths share from the start.</summary>
    private static int Shared(string one, string other)
    {
        string[] a = one.Split('/'), b = other.Split('/');
        var same = 0;
        while (same < a.Length - 1 && same < b.Length - 1 && string.Equals(a[same], b[same], StringComparison.OrdinalIgnoreCase))
        {
            same++;
        }

        return same;
    }

    /// <summary>The .ast an animation timeline (.atl, JSON) names as its <c>"skeleton"</c>, or empty - see the class's remarks.</summary>
    public static string SkeletonOf(GameFiles files, string timeline)
    {
        ArgumentNullException.ThrowIfNull(files);
        byte[]? bytes = timeline.Length > 0 ? files.Read(timeline) : null;
        return bytes is { Length: > 0 } ? SkeletonOf(StatDescriptionFiles.Decode(bytes)) : string.Empty;
    }

    /// <summary>The <c>"skeleton"</c> of a timeline's text, where it names an .ast - else empty.</summary>
    internal static string SkeletonOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        const string Key = "\"skeleton\"";
        for (int at = text.IndexOf(Key, StringComparison.Ordinal); at >= 0; at = text.IndexOf(Key, at + Key.Length, StringComparison.Ordinal))
        {
            int colon = at + Key.Length;
            while (colon < text.Length && char.IsWhiteSpace(text[colon]))
            {
                colon++;
            }

            if (colon >= text.Length || text[colon] != ':')
            {
                continue;
            }

            int open = colon + 1;
            while (open < text.Length && char.IsWhiteSpace(text[open]))
            {
                open++;
            }

            int close = open < text.Length && text[open] == '"' ? text.IndexOf('"', open + 1) : -1;
            if (close > open)
            {
                string value = text[(open + 1)..close].Replace("\\/", "/", StringComparison.Ordinal).Replace("\\\\", "/", StringComparison.Ordinal);
                return value.EndsWith(".ast", StringComparison.OrdinalIgnoreCase) ? value : string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>The .act and what it extends taken together, the furthest first - its block, its default wildcard and the chain's paths.</summary>
    private static (ActStance Root, string Wildcard, List<string> Chain) Merged(GameFiles files, string act)
    {
        var read = new List<ActFile>();
        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (string path = act; path.Length > 0 && read.Count < MostHops && seen.Add(path);)
        {
            ActFile one = ActFile.Read(files.Read(path));
            if (!one.Ready)
            {
                chain.Add(path + " (did not read)");
                break;
            }

            read.Add(one);
            chain.Add(path);
            path = one.Parent;
        }

        (ActStance root, string wildcard) = Merge(read);
        return (root, wildcard, chain);
    }

    /// <summary>A chain of .act files, the file itself first and what it extends after, as one block - the furthest taken first - and its wildcard.</summary>
    internal static (ActStance Root, string Wildcard) Merge(IReadOnlyList<ActFile> nearestFirst)
    {
        ArgumentNullException.ThrowIfNull(nearestFirst);
        var root = new ActStance(string.Empty, string.Empty, null);
        string wildcard = string.Empty;
        for (int one = nearestFirst.Count - 1; one >= 0; one--)
        {
            root.Take(nearestFirst[one].Root);
            if (nearestFirst[one].Wildcard.Length > 0)
            {
                wildcard = nearestFirst[one].Wildcard;
            }
        }

        return (root, wildcard);
    }

    /// <summary>An action's animation in a stance: its <c>*</c> the nearest wildcard, then the replace rules of its tags up the tree.</summary>
    internal static string Named(ActStance stance, ActAction action, string wildcard)
    {
        ArgumentNullException.ThrowIfNull(stance);
        ArgumentNullException.ThrowIfNull(action);
        string name = action.Animation.Replace("*", stance.WildcardOf(wildcard), StringComparison.Ordinal);
        for (ActStance? up = stance; up is not null; up = up.Up)
        {
            foreach ((string tag, string replaced, string by) in up.Replaces)
            {
                if (action.Tags.Contains(tag, StringComparer.Ordinal))
                {
                    name = name.Replace(replaced, by, StringComparison.Ordinal);
                }
            }
        }

        return name;
    }

    /// <summary>The timeline a stance's idle plays from: its Base timeline up the tree, else the Idle's own.</summary>
    internal static string TimelineOf(ActStance stance)
    {
        string base_ = stance.BaseTimelineOf();
        return base_.Length > 0 ? base_ : stance.ActionOf("Idle").Action?.Timeline ?? string.Empty;
    }

    private static string Tail(string path) => path.Length == 0 ? "(empty)" : path[(path.LastIndexOf('/') + 1)..];

    /// <summary>Characters, BaseItemTypes, ItemClasses and ItemStances, opened and indexed - or why not.</summary>
    private sealed class Tables
    {
        private readonly LoadedTable? _characters, _bases, _classes, _stances;
        private readonly Dictionary<string, int> _baseRows = new(StringComparer.OrdinalIgnoreCase);
        private readonly int _charId, _charAct, _baseClass, _baseStance, _baseVisual, _classStance, _stanceId;

        public Tables(GameFiles files, QuestTableLayouts layouts)
        {
            (_characters, string charactersWhy) = QuestTables.Open(files, layouts, "Characters", null, "Id", "ACTFile");
            (_bases, string basesWhy) = QuestTables.Open(files, layouts, "BaseItemTypes", null, "Id");
            (_classes, string classesWhy) = QuestTables.Open(files, layouts, "ItemClasses", null, "Id");
            (_stances, string stancesWhy) = QuestTables.Open(files, layouts, "ItemStances", null, "Id");
            Why = _characters is null ? "Characters: " + charactersWhy
                : _bases is null ? "BaseItemTypes: " + basesWhy
                : _stances is null ? "ItemStances: " + stancesWhy
                : string.Empty;
            _charId = layouts.OffsetOf("Characters", "Id");
            _charAct = layouts.OffsetOf("Characters", "ACTFile");
            _baseClass = layouts.OffsetOf("BaseItemTypes", "ItemClass");
            _baseStance = layouts.OffsetOf("BaseItemTypes", "ItemStance");
            _baseVisual = layouts.OffsetOf("BaseItemTypes", "ItemVisualIdentity");
            _classStance = _classes is null ? -1 : layouts.OffsetOf("ItemClasses", "ItemStance");
            _stanceId = layouts.OffsetOf("ItemStances", "Id");
            ClassesWhy = _classes is null ? classesWhy : string.Empty;
            if (_bases is not null)
            {
                int id = layouts.OffsetOf("BaseItemTypes", "Id");
                for (var row = 0; row < _bases.File.Rows; row++)
                {
                    _baseRows.TryAdd(_bases.File.Text(row, id), row);
                }
            }
        }

        /// <summary>Why the tables do not serve, or empty.</summary>
        public string Why { get; }

        /// <summary>Why ItemClasses did not open, or empty - its stances stand behind a base type's own.</summary>
        public string ClassesWhy { get; }

        /// <summary>The .act of the Characters row whose Id is his path, or empty.</summary>
        public string ActOf(string path)
        {
            for (var row = 0; _characters is not null && row < _characters.File.Rows; row++)
            {
                if (string.Equals(_characters.File.Text(row, _charId), path, StringComparison.OrdinalIgnoreCase))
                {
                    return _characters.File.Text(row, _charAct);
                }
            }

            return string.Empty;
        }

        /// <summary>An item's stance - its base type's ItemStance, else its ItemClass's - by ItemStances' Id, or empty.</summary>
        public string ItemStanceOf(string item)
        {
            if (_bases is null || _stances is null || !_baseRows.TryGetValue(item, out int row))
            {
                return string.Empty;
            }

            int stance = _bases.File.Reference(row, _baseStance).RowIn(_stances.File.Rows);
            if (stance < 0 && _classes is not null && _classStance >= 0)
            {
                int kind = _bases.File.Reference(row, _baseClass).RowIn(_classes.File.Rows);
                stance = kind < 0 ? -1 : _classes.File.Reference(kind, _classStance).RowIn(_stances.File.Rows);
            }

            return stance < 0 ? string.Empty : _stances.File.Text(stance, _stanceId);
        }

        /// <summary>An item's ItemClasses row, or -1 where its base type or ItemClasses does not read.</summary>
        public int ClassOf(string item)
            => _bases is not null && _classes is not null && _baseRows.TryGetValue(item, out int row)
                ? _bases.File.Reference(row, _baseClass).RowIn(_classes.File.Rows)
                : -1;

        /// <summary>An item's base type's ItemVisualIdentity row, among <paramref name="rows"/>, or -1.</summary>
        public int VisualOf(string item, long rows)
            => _bases is not null && _baseVisual >= 0 && _baseRows.TryGetValue(item, out int row)
                ? _bases.File.Reference(row, _baseVisual).RowIn(rows)
                : -1;
    }

    /// <summary>
    /// ItemVisualIdentity, opened for one resolve and let go after it - 17,227 rows of 968 bytes in the captures, too much to keep for a
    /// button pressed now and then - and its rows' held models (the class's remarks).
    /// </summary>
    private sealed class Visuals
    {
        private readonly LoadedTable? _table;
        private readonly Dictionary<string, int> _rows = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<int> _held = [];
        private readonly int _id;

        private Visuals(LoadedTable? table, QuestTableLayouts layouts, string why)
        {
            _table = table;
            Why = why;
            _id = layouts.OffsetOf("ItemVisualIdentity", "Id");
            if (table is null || !layouts.Tables.TryGetValue("ItemVisualIdentity", out List<DatColumn>? columns))
            {
                return;
            }

            // THE COLUMNS A HELD MODEL CAN STAND IN: every single string but the row's id, icon, dropped and sheathed models - AOFile2 and the
            // per-class ones (column_33 the wand's, column_38 the staff's, column_81 the focus's in the captures), in the table's order.
            DatColumns.Layout(columns);
            foreach (DatColumn column in columns)
            {
                if (column.Type == "string" && !column.Array && !NotHeld.Contains(column.Name) && !column.Name.EndsWith("Shape", StringComparison.Ordinal))
                {
                    _held.Add(column.Offset);
                }
            }

            for (var row = 0; row < table.File.Rows; row++)
            {
                _rows.TryAdd(table.File.Text(row, _id), row);
            }
        }

        /// <summary>Why the table does not serve, or empty.</summary>
        public string Why { get; }

        /// <summary>Opens the table for one resolve.</summary>
        public static Visuals Open(GameFiles files, QuestTableLayouts layouts)
        {
            (LoadedTable? table, string why) = QuestTables.Open(files, layouts, "ItemVisualIdentity", null, "Id");
            return table is { Usable: true }
                ? new Visuals(table, layouts, string.Empty)
                : new Visuals(null, layouts, "ItemVisualIdentity: " + (table?.Say ?? why));
        }

        /// <summary>The held models a row names, in its columns' order - .ao files alone.</summary>
        public IReadOnlyList<string> HeldModels(int row)
        {
            if (_table is null || row < 0 || row >= _table.File.Rows)
            {
                return [];
            }

            var models = new List<string>(2);
            foreach (int column in _held)
            {
                string value = _table.File.Text(row, column);
                if (value.EndsWith(".ao", StringComparison.OrdinalIgnoreCase) && !models.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    models.Add(value);
                }
            }

            return models;
        }

        /// <summary>The held models of the row of an Id, or none.</summary>
        public IReadOnlyList<string> HeldModels(string id)
            => id.Length > 0 && _rows.TryGetValue(id, out int row) ? HeldModels(row) : [];

        /// <summary>An item's base type's visual identity: its row and Id, or (-1, empty).</summary>
        public (int Row, string Id) OwnOf(Tables tables, string item)
        {
            int row = _table is null ? -1 : tables.VisualOf(item, _table.File.Rows);
            return row < 0 || _table is null ? (-1, string.Empty) : (row, _table.File.Text(row, _id));
        }
    }
}
