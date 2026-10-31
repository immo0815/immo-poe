using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Items;

namespace PoEformance.Game.Entities;

/// <summary>
/// The local player as the game draws him, taken at a press: his entity's path, the class's name, the body his Animated component loaded,
/// and the .ao of every slot's piece the game composed him from (Render.Pieces, read by <see cref="PlayerPieces"/>) - cosmetics included,
/// the weapon set in hand alone - with which of them are in his hands, the animation pack and timeline of the stance in hand (Actor's
/// AnimationSet) and the items of both weapon sets.
/// </summary>
/// <remarks>
/// A SNAPSHOT, NOT A LIVE FEED (DECIDED from the live client, 2026-10-31): taken when the button is pressed and kept until it is pressed
/// again, so the picture does not rebuild while he walks. Only his own character (DECIDED from the live client). What it hands the model
/// is files alone - the body, the pieces, the pack - so the build is the Monster Book's: his body's own lines, the pieces hung beside them,
/// and their ObjectHiding (PieceHiding). Which idle the stance plays is the files' to say (PlayerStance, in Features), and sets
/// <see cref="Opening"/>.
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

    /// <summary>The inventories of his two weapon sets (Inventories.dat's Weapon1, Offhand1, Weapon2 and Offhand2 - a row plus one, the 16:16 capture).</summary>
    public static IReadOnlyList<(int Main, int Off)> SetInventories { get; } = [(3, 4), (15, 16)];

    /// <summary>The piece in his main hand's slot (schema PieceSlots.MainHand), or empty.</summary>
    public string MainHand { get; init; } = string.Empty;

    /// <summary>The piece in his off hand's slot (schema PieceSlots.OffHand), or empty.</summary>
    public string OffHand { get; init; } = string.Empty;

    /// <summary>The .ast of the animation pack of the stance in hand (Actor.AnimationSet -> Pack), or empty.</summary>
    public string Moves { get; init; } = string.Empty;

    /// <summary>The timeline (.atl) of the stance in hand (Actor.AnimationSet -> Timeline), or empty.</summary>
    public string Timeline { get; init; } = string.Empty;

    /// <summary>The items' metadata paths of his weapon sets, in <see cref="SetInventories"/>' order - empty where a hand is empty.</summary>
    public IReadOnlyList<(string Main, string Off)> Sets { get; init; } = [];

    /// <summary>The animation his picture starts with - his stance's idle, as PlayerStance reads it from the files - or empty.</summary>
    public string Opening { get; init; } = string.Empty;

    /// <summary>What reading his stance came to, line by line (PlayerStance), for the Monster Book's account and the capture.</summary>
    public IReadOnlyList<string> Stance { get; init; } = [];

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
        return new PlayerDress(player.Path, named, body, [.. pieces.Select(one => one.Path)], why)
        {
            MainHand = PlayerPieces.AtSlot(pieces, schema, "MainHand"),
            OffHand = PlayerPieces.AtSlot(pieces, schema, "OffHand"),
            Moves = moves,
            Timeline = timeline,
            Sets = SetsOf(reader, schema, chain),
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
    /// The model the Monster Book draws him as: his body's files with the pieces hung beside them - the main hand's at <see cref="MainSocket"/>
    /// and the off hand's at <see cref="OffSocket"/>, rigidly (CHOSEN from the live client, 2026-10-31: no file says which slot takes which
    /// socket; Character.ao names both, and the game's picture of the 16:16 capture holds the wand in his right hand and the focus in his
    /// left), the rest as skins on his rig at his root - and his stance's animations, starting with its idle - unscaled.
    /// </summary>
    public MonsterVariety Variety()
        => new(Name: Class.Length > 0 ? Class : Tail(Path), AoFiles: Body.Length > 0 ? [Body] : null)
        {
            Worn = [.. Pieces.Select(one =>
                MainHand.Length > 0 && string.Equals(one, MainHand, StringComparison.OrdinalIgnoreCase) ? new WornPiece(MainSocket, one) { Held = true }
                : OffHand.Length > 0 && string.Equals(one, OffHand, StringComparison.OrdinalIgnoreCase) ? new WornPiece(OffSocket, one) { Held = true }
                : new WornPiece("<root>", one) { SkinOnly = true })],
            Moves = Moves,
            Opening = Opening,
        };

    /// <summary>The key the Monster Book shows him under - a path no monster of the table has.</summary>
    public string Key => "player:" + Path + "|" + string.Join("|", Pieces) + "|" + Moves + "|" + Opening;

    private static string Tail(string path) => path[(path.LastIndexOf('/') + 1)..];
}
