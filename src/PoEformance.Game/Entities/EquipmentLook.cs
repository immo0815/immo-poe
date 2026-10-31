using PoEformance.Core.Memory;
using PoEformance.Core.Schema;

namespace PoEformance.Game.Entities;

/// <summary>
/// One entry of the player's equipment looks (schema Inventories.Looks, EquipmentLook): a worn slot, its item's class row, whether an item
/// stance names it - a held item - and the Id of the visual identity the slot is drawn with, a cosmetic's where one is applied.
/// </summary>
/// <remarks>
/// BOTH WEAPON SETS ARE HERE, the one not in hand included - which is what the Monster Book's set switch draws the other set from. MEASURED in
/// the two captures of 2026-10-31 (16:16 with the wand and focus in hand, 22:17 after a swap to the staff): the same 13 entries byte for byte,
/// the staff's at slot 0 (inventory 3, set 1's main hand), the wand's at 2 (inventory 15, set 2's main hand), the focus's at 3 (inventory 16,
/// set 2's off hand). That set 1's off hand is slot 1 is the order's reading, an empty hand in both; see <see cref="SetOf"/>.
/// </remarks>
/// <param name="Slot">The visual slot - the list's sort key.</param>
/// <param name="ItemClass">The item's ItemClasses row, by every reading of the captures (the schema's note), or -1.</param>
/// <param name="Held">Whether an item stance names it - the three held items of the captures, nothing worn.</param>
/// <param name="Visual">The Id of the ItemVisualIdentity row it is drawn with, or empty where it did not read.</param>
public readonly record struct EquipmentLook(int Slot, int ItemClass, bool Held, string Visual)
{
    /// <summary>The most entries believed - a guard on a count that comes from memory; the captures held 13.</summary>
    public const int MostLooks = 64;

    /// <summary>The most characters of an Id read.</summary>
    private const int MostIdChars = 128;

    /// <summary>The weapon sets the looks' first slots hold, two slots a set - main then off (the remarks' reading).</summary>
    public const int SetSlots = 2;

    /// <summary>The weapon set (nought for the first) and hand (nought main, one off) a held slot stands for, or (-1, -1) past the sets read.</summary>
    public static (int Set, int Hand) SetOf(int slot, int sets)
        => slot >= 0 && slot < sets * SetSlots ? (slot / SetSlots, slot % SetSlots) : (-1, -1);

    /// <summary>The player's looks off his Inventories component - empty where the schema has no Looks or the list does not read whole.</summary>
    public static IReadOnlyList<EquipmentLook> Read(IMemoryReader reader, OffsetSchema schema, ulong inventories)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        if (inventories == 0
            || !schema.Structs.TryGetValue("Inventories", out StructDef? component) || component.Field("Looks") is not { } looksAt
            || !schema.Structs.TryGetValue("EquipmentLook", out StructDef? look) || !look.Constants.TryGetValue("Size", out long size) || size <= 0
            || look.Field("Slot") is not { } slotAt || look.Field("ItemClass") is not { } classAt
            || look.Field("Stance") is not { } stanceAt || look.Field("Visual") is not { } visualAt
            || !schema.Structs.TryGetValue("ItemVisualIdentityRow", out StructDef? row) || row.Field("IdPtr") is not { } idAt)
        {
            return [];
        }

        ulong first = reader.ReadPointer(inventories + (ulong)looksAt.Offset);
        ulong last = reader.ReadPointer(inventories + (ulong)looksAt.Offset + 8);
        if (!MemoryReaderExtensions.IsPlausiblePointer(first) || last <= first || (last - first) % (ulong)size != 0
            || (last - first) / (ulong)size > MostLooks)
        {
            return [];
        }

        // ONE READ FOR THE WHOLE LIST, then one pointer and one string a row: thirteen entries in the captures.
        byte[] block = new byte[(int)(last - first)];
        if (!reader.TryRead(first, block))
        {
            return [];
        }

        var looks = new List<EquipmentLook>(block.Length / (int)size);
        for (var at = 0; at < block.Length; at += (int)size)
        {
            ulong visual = BitConverter.ToUInt64(block, at + visualAt.Offset);
            ulong id = MemoryReaderExtensions.IsPlausiblePointer(visual) ? reader.ReadPointer(visual + (ulong)idAt.Offset) : 0;
            string named = MemoryReaderExtensions.IsPlausiblePointer(id) ? reader.ReadUtf16(id, MostIdChars) : string.Empty;
            looks.Add(new EquipmentLook(
                block[at + slotAt.Offset],
                BitConverter.ToInt32(block, at + classAt.Offset),
                MemoryReaderExtensions.IsPlausiblePointer(BitConverter.ToUInt64(block, at + stanceAt.Offset)),
                IsId(named) ? named : string.Empty));
        }

        return looks;
    }

    /// <summary>Whether text reads as a table's Id: printable, no path, not empty.</summary>
    internal static bool IsId(string text)
        => text.Length is > 0 and <= MostIdChars && !text.Contains('/', StringComparison.Ordinal) && text.All(one => one is > ' ' and <= '~');
}
