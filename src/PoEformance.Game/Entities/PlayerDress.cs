using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Items;

namespace PoEformance.Game.Entities;

/// <summary>
/// The local player as the game draws him, taken at a press: his entity's path, the class's name, the body his Animated component loaded,
/// and the .ao of every slot's piece the game composed him from (Render.Pieces, read by <see cref="PlayerPieces"/>) - cosmetics included -
/// with his weapon sets (<see cref="Sets"/>), his equipment looks that name both sets' cosmetics (<see cref="Looks"/>), the set in hand and
/// the animation pack and timeline of its stance (Actor's AnimationSet).
/// </summary>
/// <remarks>
/// A SNAPSHOT, NOT A LIVE FEED (DECIDED from the live client, 2026-10-31): taken when the button is pressed and kept until it is pressed
/// again, so the picture does not rebuild while he walks. Only his own character (DECIDED from the live client). What it hands the model
/// is files alone - the body, the pieces, the pack - so the build is the Monster Book's: his body's own lines, the pieces hung beside them,
/// and their ObjectHiding (PieceHiding).
/// THE SET IN HAND AND ITS HANDS ARE THE FILES' TO SAY (PlayerStance in Features): which set's stance has the Actor's timeline, and which
/// piece is which hand by the looks. <see cref="Read"/> fills them from the composition alone, so a dress the files never saw still holds
/// the pieces in hand: the composition's held slots (schema PieceSlots) - +0x08 filled, the first set is in hand, its piece in the main hand;
/// else the second's main hand at +0x30 and off hand at +0x58 - the two captures' measurement, said where it stands in.
/// THE OTHER SET IS DRAWN AS SWITCHED TO (DECIDED from the live client, 2026-10-31: the weapon set in hand, switchable): <see cref="Variety"/>
/// takes the set.
/// </remarks>
/// <param name="Path">His entity's metadata path - <c>Metadata/Characters/Int/IntFourb</c>.</param>
/// <param name="Class">What the game calls his class (Render.Name reads it for the own character - the schema's note), or empty.</param>
/// <param name="Body">The .ao his Animated component loaded - <c>Metadata/Characters/Int/IntFourB.ao</c> - or empty.</param>
/// <param name="Pieces">The slots' pieces as drawn, each .ao once, in the composition's order.</param>
/// <param name="Why">Why something is missing, or empty.</param>
public sealed record PlayerDress(string Path, string Class, string Body, IReadOnlyList<string> Pieces, string Why)
{
    /// <summary>Nothing taken.</summary>
    public static PlayerDress None { get; } = new(string.Empty, string.Empty, string.Empty, [], "not taken");

    /// <summary>The sockets of his body the held pieces hang at - CHOSEN from the live client (2026-10-31), see <see cref="Variety"/>.</summary>
    public const string MainSocket = "R_Weapon";

    /// <inheritdoc cref="MainSocket"/>
    public const string OffSocket = "L_Weapon";

    /// <summary>
    /// The inventories of his two weapon sets (Inventories.dat's Weapon1, Offhand1, Weapon2 and Offhand2 - a row plus one): MEASURED, the
    /// staff in 3 and the wand and focus in 15 and 16 in both captures of 2026-10-31, a weapon swap between them moving nothing.
    /// </summary>
    public static IReadOnlyList<(int Main, int Off)> SetInventories { get; } = [(3, 4), (15, 16)];

    /// <summary>The .ast of the animation pack of the stance in hand (Actor.AnimationSet -> Pack), or empty.</summary>
    public string Moves { get; init; } = string.Empty;

    /// <summary>The timeline (.atl) of the stance in hand (Actor.AnimationSet -> Timeline), or empty.</summary>
    public string Timeline { get; init; } = string.Empty;

    /// <summary>His weapon sets, in <see cref="SetInventories"/>' order - always as many, a set's items empty where a hand is empty.</summary>
    public IReadOnlyList<PlayerSet> Sets { get; init; } = [];

    /// <summary>His equipment looks (schema Inventories.Looks) - both sets' cosmetics among them - or empty where they did not read.</summary>
    public IReadOnlyList<EquipmentLook> Looks { get; init; } = [];

    /// <summary>The set in hand, nought for the first, or -1 where nothing said.</summary>
    public int InHand { get; init; } = -1;

    /// <summary>
    /// The composition's pieces that are a hand's, of either set - never hung as skins; the set drawn hangs its own in its hands.
    /// </summary>
    public IReadOnlyList<string> Held { get; init; } = [];

    /// <summary>What reading his stance came to, line by line (PlayerStance), for the Monster Book's account and the capture.</summary>
    public IReadOnlyList<string> Stance { get; init; } = [];

    /// <summary>The set the Monster Book shows him with first: the one in hand, else the first.</summary>
    public int Shown => InHand >= 0 && InHand < Sets.Count ? InHand : 0;

    /// <summary>Whether there is a body to draw.</summary>
    public bool Ready => Body.Length > 0;

    /// <summary>Reads the local player as the game draws him now - see the class's remarks.</summary>
    public static PlayerDress Read(IMemoryReader reader, OffsetSchema schema, ulong gameStatesStatic)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        GameChainAddresses chain = GameChain.Resolve(reader, schema, gameStatesStatic);
        var entities = new EntityReader(reader, schema);
        if (chain.PlayerEntity == 0 || entities.ReadIdentity(chain.PlayerEntity) is not { } player)
        {
            return None with { Why = "no player read - not in an area, or the chain did not resolve" };
        }

        IReadOnlyDictionary<string, ulong> components = entities.ReadComponents(player.Address, player.Details);
        string body = ModelOf(reader, schema, components.GetValueOrDefault("Animated"));
        ulong render = components.GetValueOrDefault("Render");
        string named = render != 0 && schema.Structs.TryGetValue("Render", out StructDef? fields) && fields.Field("Name") is { } name
            ? reader.ReadStdWString(render + (ulong)name.Offset, 64)
            : string.Empty;
        IReadOnlyList<PlayerPieces.Piece> pieces = PlayerPieces.Slots(PlayerPieces.Read(reader, schema, PlayerPieces.CompositionOf(reader, schema, render)));
        (string moves, string timeline) = StanceOf(reader, schema, components.GetValueOrDefault("Actor"));
        string why = body.Length == 0 ? "his Animated component named no model"
            : pieces.Count == 0 ? "the game's list of the pieces it draws him with (Render.Pieces) read nothing - drawn bare"
            : string.Empty;
        return Composed(new PlayerDress(player.Path, named, body, [.. pieces.Select(one => one.Path)], why)
        {
            Moves = moves,
            Timeline = timeline,
            Looks = EquipmentLook.Read(reader, schema, components.GetValueOrDefault("Inventories")),
        }, SetsOf(reader, schema, chain), PlayerPieces.AtSlot(pieces, schema, "Weapon1"), PlayerPieces.AtSlot(pieces, schema, "Weapon2"),
            PlayerPieces.AtSlot(pieces, schema, "Offhand2"));
    }

    /// <summary>
    /// The dress with its sets and its hands as the composition alone says them (the class's remarks): the items of each set, the pieces of the
    /// composition's held slots as their hands - <paramref name="weapon1"/> the first set's main hand, <paramref name="weapon2"/> and
    /// <paramref name="offhand2"/> the second's - all three never hung as skins, and the first set in hand where its slot is filled.
    /// </summary>
    public static PlayerDress Composed(PlayerDress dress, IReadOnlyList<(string Main, string Off)> items, string weapon1, string weapon2, string offhand2)
    {
        ArgumentNullException.ThrowIfNull(dress);
        ArgumentNullException.ThrowIfNull(items);
        const string Said = "from the composition's held slot (schema PieceSlots) - the files did not say whose it is";
        var sets = new List<PlayerSet>(SetInventories.Count);
        for (var set = 0; set < SetInventories.Count; set++)
        {
            (string main, string off) = set < items.Count ? items[set] : (string.Empty, string.Empty);
            string mainPiece = set == 0 ? weapon1 : set == 1 ? weapon2 : string.Empty;
            string offPiece = set == 1 ? offhand2 : string.Empty;
            sets.Add(new PlayerSet(main, off)
            {
                MainPiece = mainPiece,
                OffPiece = offPiece,
                Said = [.. new[] { ("main hand", mainPiece), ("off hand", offPiece) }
                    .Where(one => one.Item2.Length > 0).Select(one => $"{one.Item1}: {one.Item2} - {Said}")],
            });
        }

        return dress with
        {
            Sets = sets,
            Held = [.. new[] { weapon1, weapon2, offhand2 }.Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
            InHand = weapon1.Length > 0 ? 0 : weapon2.Length > 0 || offhand2.Length > 0 ? 1 : -1,
        };
    }

    /// <summary>The .ao an Animated component loaded (ModelInfoPtr -> AnimatedModelInfo.ModelFileRecordPtr -> FileInfoValue.Name), or empty.</summary>
    public static string ModelOf(IMemoryReader reader, OffsetSchema schema, ulong animated)
    {
        if (animated == 0
            || !schema.Structs.TryGetValue("Animated", out StructDef? fields) || fields.Field("ModelInfoPtr") is not { } at
            || !schema.Structs.TryGetValue("AnimatedModelInfo", out StructDef? info) || info.Field("ModelFileRecordPtr") is not { } file
            || !schema.Structs.TryGetValue("FileInfoValue", out StructDef? value) || value.Field("Name") is not { } name)
        {
            return string.Empty;
        }

        ulong model = reader.ReadPointer(animated + (ulong)at.Offset);
        ulong record = model == 0 ? 0 : reader.ReadPointer(model + (ulong)file.Offset);
        return record == 0 ? string.Empty : reader.ReadStdWString(record + (ulong)name.Offset);
    }

    /// <summary>
    /// The animation pack's .ast and the timeline of the stance in hand, through his Actor (schema Actor.AnimationSet, ActorAnimationSet,
    /// AnimationPack, AnimationPackKeyframes) - each empty where its chain does not read to a file of its kind.
    /// </summary>
    public static (string Moves, string Timeline) StanceOf(IMemoryReader reader, OffsetSchema schema, ulong actor)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        if (actor == 0
            || Offset(schema, "Actor", "AnimationSet") is not { } setAt
            || Offset(schema, "ActorAnimationSet", "Pack") is not { } packAt
            || Offset(schema, "ActorAnimationSet", "Timeline") is not { } timelineAt
            || Offset(schema, "AnimationPack", "Keyframes") is not { } keyframesAt
            || Offset(schema, "AnimationPackKeyframes", "Record") is not { } recordAt
            || Offset(schema, "FileInfoValue", "Name") is not { } nameAt)
        {
            return (string.Empty, string.Empty);
        }

        ulong set = Pointer(reader, actor + setAt);
        ulong pack = set == 0 ? 0 : Pointer(reader, set + packAt);
        ulong keyframes = pack == 0 ? 0 : Pointer(reader, pack + keyframesAt);
        ulong record = keyframes == 0 ? 0 : Pointer(reader, keyframes + recordAt);
        ulong timeline = set == 0 ? 0 : Pointer(reader, set + timelineAt);
        string moves = record == 0 ? string.Empty : reader.ReadStdWString(record + nameAt, 260);
        string line = timeline == 0 ? string.Empty : reader.ReadStdWString(timeline + nameAt, 260);
        return (
            moves.EndsWith(".ast", StringComparison.OrdinalIgnoreCase) ? moves : string.Empty,
            line.EndsWith(".atl", StringComparison.OrdinalIgnoreCase) ? line : string.Empty);
    }

    /// <summary>The items of his weapon sets (<see cref="SetInventories"/>), each hand's first item's path or empty.</summary>
    private static IReadOnlyList<(string Main, string Off)> SetsOf(IMemoryReader reader, OffsetSchema schema, GameChainAddresses chain)
    {
        if (chain.AreaInstance == 0
            || Offset(schema, "AreaInstance", "PlayerInfo") is not { } infoAt
            || Offset(schema, "LocalPlayerStruct", "ServerDataPtr") is not { } dataAt)
        {
            return [];
        }

        ulong serverData = reader.ReadPointer(chain.AreaInstance + infoAt + dataAt);
        if (serverData == 0)
        {
            return [];
        }

        var held = new Dictionary<int, string>();
        var items = new ItemReader(reader, schema);
        foreach (StashInventory inventory in new StashReader(reader, schema).Read(serverData))
        {
            if (inventory.Items.Count > 0 && SetInventories.Any(one => one.Main == inventory.Id || one.Off == inventory.Id))
            {
                held[inventory.Id] = items.Read(inventory.Items[0].Entity).Path;
            }
        }

        return [.. SetInventories.Select(one => (held.GetValueOrDefault(one.Main, string.Empty), held.GetValueOrDefault(one.Off, string.Empty)))];
    }

    /// <summary>A field's offset, or null where the schema has no such struct or field.</summary>
    private static ulong? Offset(OffsetSchema schema, string type, string field)
        => schema.Structs.TryGetValue(type, out StructDef? fields) && fields.Field(field) is { } found ? (ulong)found.Offset : null;

    /// <summary>A plausible pointer read, or nought.</summary>
    private static ulong Pointer(IMemoryReader reader, ulong at)
    {
        ulong value = reader.ReadPointer(at);
        return MemoryReaderExtensions.IsPlausiblePointer(value) ? value : 0;
    }

    /// <summary>
    /// The model the Monster Book draws him as with one of his weapon sets in hand (nought the first): his body's files with the pieces hung
    /// beside them - the set's main hand's at <see cref="MainSocket"/> and its off hand's at <see cref="OffSocket"/>, rigidly (CHOSEN from the
    /// live client, 2026-10-31: no file says which slot takes which socket; Character.ao names both, and the game's pictures hold the wand
    /// in his right hand and the focus in his left - the two-handed staff, gripped with both, hangs at the main hand's too), every other
    /// piece as a skin on his rig at his root, no piece of a hand
    /// (<see cref="Held"/>) among them - and the set's stance's animations, starting with its idle - unscaled.
    /// </summary>
    public MonsterVariety Variety(int set)
    {
        PlayerSet? hands = set >= 0 && set < Sets.Count ? Sets[set] : null;
        var worn = new List<WornPiece>(Pieces.Count + 2);
        if (hands?.MainPiece is { Length: > 0 } main)
        {
            worn.Add(new WornPiece(MainSocket, main) { Held = true });
        }

        if (hands?.OffPiece is { Length: > 0 } off)
        {
            worn.Add(new WornPiece(OffSocket, off) { Held = true });
        }

        foreach (string one in Pieces)
        {
            if (!Held.Contains(one, StringComparer.OrdinalIgnoreCase))
            {
                worn.Add(new WornPiece("<root>", one) { SkinOnly = true });
            }
        }

        return new(Name: Class.Length > 0 ? Class : Tail(Path), AoFiles: Body.Length > 0 ? [Body] : null)
        {
            Worn = worn,
            Moves = MovesOf(set),
            Opening = hands?.Opening ?? string.Empty,
        };
    }

    /// <summary>The pack a set plays from: its own as read, else the Actor's where it is the set in hand - or empty.</summary>
    public string MovesOf(int set)
        => set >= 0 && set < Sets.Count && Sets[set].Moves.Length > 0 ? Sets[set].Moves : set == InHand ? Moves : string.Empty;

    /// <summary>The key the Monster Book shows him under with a set in hand - a path no monster of the table has.</summary>
    public string Key(int set)
    {
        PlayerSet? hands = set >= 0 && set < Sets.Count ? Sets[set] : null;
        return "player:" + Path + "|" + string.Join("|", Pieces) + "|set " + set + "|" + hands?.MainPiece + "|" + hands?.OffPiece + "|"
            + MovesOf(set) + "|" + hands?.Opening;
    }

    private static string Tail(string path) => path[(path.LastIndexOf('/') + 1)..];
}
