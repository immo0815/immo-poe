using PoEformance.Core.Memory;
using PoEformance.Core.Schema;

namespace PoEformance.Game.Entities;

/// <summary>
/// The pieces the game draws the player with, read from the object his Render component points at (schema Render.Pieces): every word of
/// its first <see cref="Bytes"/> that points at an object one of whose words, in its first <see cref="ObjectBytes"/>, points at a file record
/// naming an .ao - the slots' pieces as the game composed them, cosmetics included.
/// </summary>
/// <remarks>
/// A STRUCTURAL READ, NOT A LAYOUT. Two captures of one session (v0.1.393, the schema's note) found the slots' .ao files at the target's
/// +0x30 to +0x130 behind an object whose +0x08 is the record, and an attachment of the body armour one hop deeper - which word is which slot,
/// and how long the target is, are not read. So every word is asked and the answer carries where it was found (<see cref="Piece.At"/>,
/// <see cref="Piece.Inner"/>): the capture writes them all, and a reader that wants the slots alone takes the records found at
/// <see cref="SlotInner"/>.
/// </remarks>
public static class PlayerPieces
{
    /// <summary>How much of the composition is searched - CHOSEN, past the furthest slot measured (+0x130).</summary>
    public const int Bytes = 0x200;

    /// <summary>How much of each object it points at is searched for a record - CHOSEN, past the deepest measured (+0x98).</summary>
    public const int ObjectBytes = 0x100;

    /// <summary>Where a slot's object holds its record - MEASURED on all seven slots of two captures (the schema's Render.Pieces note).</summary>
    public const int SlotInner = 0x08;

    /// <summary>The most characters of a record's name read.</summary>
    private const int MostChars = 260;

    /// <summary>One piece found: where in the composition, where in the object it points at, and the .ao its record names.</summary>
    /// <param name="At">The word of the composition that points at the object.</param>
    /// <param name="Inner">The word of that object that points at the record.</param>
    /// <param name="Object">The object's address.</param>
    /// <param name="Record">The file record's address.</param>
    /// <param name="Path">The .ao the record names.</param>
    public readonly record struct Piece(int At, int Inner, ulong Object, ulong Record, string Path);

    /// <summary>The composition the player's Render component points at, or nought where the schema or the read has none.</summary>
    public static ulong CompositionOf(IMemoryReader reader, OffsetSchema schema, ulong render)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        if (render == 0 || !schema.Structs.TryGetValue("Render", out StructDef? fields) || fields.Field("Pieces") is not { } field)
        {
            return 0;
        }

        ulong composition = reader.ReadPointer(render + (ulong)field.Offset);
        return MemoryReaderExtensions.IsPlausiblePointer(composition) ? composition : 0;
    }

    /// <summary>Every piece the composition leads to, in the order of its words and then of the objects' - see the class's remarks.</summary>
    public static IReadOnlyList<Piece> Read(IMemoryReader reader, OffsetSchema schema, ulong composition)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        var pieces = new List<Piece>();
        byte[] block = new byte[Bytes];
        if (composition == 0 || !reader.TryRead(composition, block))
        {
            return pieces;
        }

        int nameAt = schema.Structs.TryGetValue("FileInfoValue", out StructDef? record) && record.Field("Name") is { } name ? name.Offset : 0x08;
        byte[] inner = new byte[ObjectBytes];
        var asked = new Dictionary<ulong, string>();
        for (var at = 0; at + 8 <= Bytes; at += 8)
        {
            ulong target = BitConverter.ToUInt64(block, at);
            if (!MemoryReaderExtensions.IsPlausiblePointer(target) || !reader.TryRead(target, inner))
            {
                continue;
            }

            for (var word = 0; word + 8 <= ObjectBytes; word += 8)
            {
                ulong file = BitConverter.ToUInt64(inner, word);
                if (!MemoryReaderExtensions.IsPlausiblePointer(file))
                {
                    continue;
                }

                // ONE READ A RECORD however many words lead to it - the slots' objects point at their records and at one another.
                if (!asked.TryGetValue(file, out string? path))
                {
                    path = reader.ReadStdWString(file + (ulong)nameAt, MostChars);
                    asked[file] = path;
                }

                if (IsAo(path))
                {
                    pieces.Add(new Piece(at, word, target, file, path));
                }
            }
        }

        return pieces;
    }

    /// <summary>The slots' pieces alone: those found at <see cref="SlotInner"/>, each .ao once, in the composition's order.</summary>
    public static IReadOnlyList<Piece> Slots(IReadOnlyList<Piece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. pieces.Where(one => one.Inner == SlotInner && seen.Add(one.Path))];
    }

    /// <summary>Whether a record's name is an .ao under Metadata/ - printable, a folder and a name.</summary>
    internal static bool IsAo(string path)
        => path.Length > 13 && path.Length <= MostChars
            && path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(".ao", StringComparison.OrdinalIgnoreCase)
            && path.All(one => one is >= ' ' and <= '~');
}
