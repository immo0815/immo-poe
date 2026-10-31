using System.Runtime.CompilerServices;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The player's stance and the idle it plays, read from his files: his class's .act (Characters.ACTFile) and what it extends, his weapon
/// sets' items' stances (BaseItemTypes.ItemStance, else their ItemClasses', in ItemStances), and the timeline his Actor holds for the stance
/// in hand (PlayerDress.Timeline) to say which set that is.
/// </summary>
/// <remarks>
/// READ FROM THE FILES (v0.1.397, the 16:16 capture of 2026-10-31 in The Red Vale), each step said in <see cref="PlayerDress.Stance"/>:
/// <list type="bullet">
/// <item>A STANCE'S NAME IS ITS HANDS' ITEM STANCES, main then off: Character.act's stances read so throughout - DaggerShield, DaggerBuckler,
///   OneHandSwordSceptre, WandSpiritShield, BowQuiver, ClawClaw - an empty hand adding nothing (Wand alone, Shield alone under Unarmed) and
///   none at all being Unarmed. The ids are ItemStances' own; which a focus is, SpiritShield, the stances' wildcards say too (wand_shield,
///   dagger_focus, unarmed_focus).</item>
/// <item>THE SET IN HAND is the one whose stance's Base timeline is the timeline his Actor holds (base_wand.atl in the capture, the Wand
///   stance's own) - the composition draws only the set in hand and nothing read yet says which set that is.</item>
/// <item>ITS IDLE is the nearest Idle up the stance tree, its <c>*</c> the nearest wildcard - <c>idle_*_01</c> and <c>wand_shield</c>, so
///   <c>idle_wand_shield_01</c> - and the replace rules of its tags (IntFour.act's ClassSpecific one) applied; taken where the pack his Actor
///   holds has an animation of the name.</item>
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

    /// <summary>His stance's idle and the account of reading it, into <paramref name="dress"/> - see the class's remarks.</summary>
    public static PlayerDress Resolve(GameFiles files, QuestTableLayouts? layouts, PlayerDress dress)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(dress);
        var said = new List<string>();
        if (!dress.Ready)
        {
            return dress;
        }

        if (dress.Moves.Length == 0 || dress.Timeline.Length == 0)
        {
            said.Add("no animation pack or timeline read off his Actor (Actor.AnimationSet) - his rig's own bind pose");
            return dress with { Stance = said };
        }

        said.Add($"his Actor's stance: the pack {dress.Moves}, the timeline {dress.Timeline}");
        if (layouts is null)
        {
            said.Add("data/item-tables.json did not load - no stance read");
            return dress with { Stance = said };
        }

        Tables tables = Opened.GetValue(files, one => new Tables(one, layouts));
        if (tables.Why.Length > 0)
        {
            said.Add(tables.Why);
            return dress with { Stance = said };
        }

        string act = tables.ActOf(dress.Path);
        if (act.Length == 0)
        {
            said.Add($"no Characters row names {dress.Path} - no .act");
            return dress with { Stance = said };
        }

        (ActStance root, string wildcard, List<string> chain) = Merged(files, act);
        said.Add($"his .act: {string.Join(" <- ", chain)}, the default wildcard \"{wildcard}\"");
        if (root.Stances.Count == 0)
        {
            said.Add("no stance read in it");
            return dress with { Stance = said };
        }

        ActStance? chosen = null;
        for (var set = 0; set < dress.Sets.Count; set++)
        {
            (string main, string off) = dress.Sets[set];
            string mainStance = main.Length > 0 ? tables.ItemStanceOf(main) : string.Empty;
            string offStance = off.Length > 0 ? tables.ItemStanceOf(off) : string.Empty;
            string name = mainStance + offStance;
            name = name.Length > 0 ? name : "Unarmed";
            ActStance? stance = root.Find(name);
            string timeline = stance is null ? string.Empty : TimelineOf(stance);
            bool held = stance is not null && string.Equals(timeline, dress.Timeline, StringComparison.OrdinalIgnoreCase);
            said.Add($"set {set + 1}: {Tail(main)} + {Tail(off)} - item stances \"{mainStance}\" + \"{offStance}\" - stance {name}: "
                + (stance is null ? "not in the .act" : $"its Base timeline {Tail(timeline)}{(held ? " - his Actor's, the set in hand" : string.Empty)}"));
            if (held && chosen is null)
            {
                chosen = stance;
            }
        }

        if (chosen is null)
        {
            said.Add("no set's stance has his Actor's timeline - no idle chosen");
            return dress with { Stance = said };
        }

        (ActAction? idle, ActStance? from) = chosen.ActionOf("Idle");
        if (idle is null || idle.Animation.Length == 0)
        {
            said.Add($"stance {chosen.Name}: no Idle up its tree");
            return dress with { Stance = said };
        }

        string name_ = Named(chosen, idle, wildcard);
        AnimationSkeleton pack = AnimationSkeleton.Read(files, dress.Moves);
        bool there = pack.Animations.Any(one => string.Equals(one.Name, name_, StringComparison.Ordinal));
        said.Add($"stance {chosen.Name}: Idle \"{idle.Animation}\" (written in {(from?.Name is { Length: > 0 } written ? written : "the file's block")}), "
            + $"wildcard \"{chosen.WildcardOf(wildcard)}\" - {name_}: "
            + (there ? $"in the pack, one of its {pack.Animations.Count}" : $"NOT in the pack's {pack.Animations.Count} animations - not chosen"));
        return dress with { Opening = there ? name_ : string.Empty, Stance = said };
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
        private readonly int _charId, _charAct, _baseClass, _baseStance, _classStance, _stanceId;

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
    }
}
