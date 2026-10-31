using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;

namespace PoEformance.Game.Entities;

/// <summary>
/// The local player as the game draws him, taken at a press: his entity's path, the class's name, the body his Animated component loaded,
/// and the .ao of every slot's piece the game composed him from (Render.Pieces, read by <see cref="PlayerPieces"/>) - cosmetics included,
/// the weapon set in hand alone.
/// </summary>
/// <remarks>
/// A SNAPSHOT, NOT A LIVE FEED (DECIDED from the live client, 2026-10-31): taken when the button is pressed and kept until it is pressed
/// again, so the picture does not rebuild while he walks. Only his own character (DECIDED from the live client). What it hands the model
/// is files alone - the body and the pieces - so the build is the Monster Book's: his body's own lines, the pieces hung beside them at his
/// root as skins, and their ObjectHiding (PieceHiding).
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
        string why = body.Length == 0 ? "his Animated component named no model"
            : pieces.Count == 0 ? "the game's list of the pieces it draws him with (Render.Pieces) read nothing - drawn bare"
            : string.Empty;
        return new PlayerDress(player.Path, named, body, [.. pieces.Select(one => one.Path)], why);
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
    /// The model the Monster Book draws him as: his body's files with the pieces hung at his root, each drawn only as a skin on his rig - a
    /// held piece names no socket, and none has been read yet (WornPiece.SkinOnly) - unscaled.
    /// </summary>
    public MonsterVariety Variety()
        => new(Name: Class.Length > 0 ? Class : Tail(Path), AoFiles: Body.Length > 0 ? [Body] : null)
        {
            Worn = [.. Pieces.Select(one => new WornPiece("<root>", one) { SkinOnly = true })],
        };

    /// <summary>The key the Monster Book shows him under - a path no monster of the table has.</summary>
    public string Key => "player:" + Path + "|" + string.Join("|", Pieces);

    private static string Tail(string path) => path[(path.LastIndexOf('/') + 1)..];
}
