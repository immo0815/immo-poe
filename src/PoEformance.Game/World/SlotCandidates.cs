using System.Globalization;
using PoEformance.Game.Files;

namespace PoEformance.Game.World;

/// <summary>One kind of slot in a room - what it asks of its tile - and how many of the room's slots ask it.</summary>
/// <param name="Width">The slot's width in tiles.</param>
/// <param name="Height">Its height.</param>
/// <param name="Tag">The tag it names, or empty.</param>
/// <param name="Edges">The edge types it names, sorted, an empty string for a side it leaves unnamed.</param>
/// <param name="Grounds">The corner ground types it names, sorted, likewise.</param>
/// <param name="Slots">How many slots of the room are of this kind.</param>
/// <param name="Big">Whether the kind is bigger than one tile - the slots the tile placing anchors on.</param>
/// <param name="Said">The ask in a line's words - see SlotWants.Said.</param>
public sealed record SlotKind(int Width, int Height, string Tag, IReadOnlyList<string> Edges, IReadOnlyList<string> Grounds, int Slots, bool Big, string Said);

/// <summary>A kind of slot with the tiles that answer it.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Tiles">The tiles alike to it, as they were handed in, in that order.</param>
public sealed record SlotKindAlike(SlotKind Kind, IReadOnlyList<string> Tiles);

/// <summary>
/// A room's slots held against a set of tiles: each kind of slot once with the tiles that answer it - or why the room could not be read.
/// </summary>
/// <param name="Room">The room's file.</param>
/// <param name="Kinds">Each kind of slot, in the order its first slot comes in the grid.</param>
/// <param name="Why">Why there are no kinds, or empty.</param>
public sealed record RoomSlotCandidates(string Room, IReadOnlyList<SlotKindAlike> Kinds, string Why)
{
    /// <summary>How many slots the room has that ask for a tile.</summary>
    public int Slots => Kinds.Sum(one => one.Kind.Slots);

    /// <summary>How many kinds exactly one tile answers - the slots the room could be built on without the area.</summary>
    public int One => Kinds.Count(one => one.Tiles.Count == 1);

    /// <summary>How many kinds several tiles answer - where the generator chose, and a build without the area would guess.</summary>
    public int Several => Kinds.Count(one => one.Tiles.Count > 1);

    /// <summary>How many kinds no tile answers - an ask the set does not meet, or a tile that did not read.</summary>
    public int None => Kinds.Count(one => one.Tiles.Count == 0);

    /// <summary>How many of the room's slots are of a kind exactly one tile answers.</summary>
    public int SlotsSettled => Kinds.Where(one => one.Tiles.Count == 1).Sum(one => one.Kind.Slots);

    /// <summary>Whether every kind of slot has exactly one tile - the room could be built from the set alone.</summary>
    public bool Settled => Kinds.Count > 0 && Kinds.All(one => one.Tiles.Count == 1);
}

/// <summary>
/// Which tiles of a set answer each kind of slot in a room - by the one rule a slot judges a tile by (SlotWants).
/// </summary>
/// <remarks>
/// THE MEASURE BEHIND BUILDING A ROOM WITHOUT THE AREA. A room's file names slot types and not tiles;
/// the generator picks a tile for each slot from the tileset when the area is made, by a rule no
/// reference writes down. What CAN be read off the files is how many tiles of the tileset each kind
/// of slot admits by the rule the room search already uses - the size either way round, the tag where
/// named, every named edge and ground type among the tile's. A kind one tile answers is settled by the
/// files; a kind several answer is the generator's choice. Sinter Rift's boss room was the first
/// number: 17 of 17 big slots alike to the tile laid there (RoomTileFinder) - which says the laid tile
/// answers, not that it answers alone. This says that.
///
/// IN THIS LAYER because the rule lives here, internal, and this is its one public reading; the
/// capture writes it (CaptureReport.SlotCandidates) and the tests read it without an install.
/// </remarks>
public static class SlotCandidates
{
    /// <summary>
    /// Every kind of slot in the room with the tiles of the set that answer it. Never throws for a room that read.
    /// </summary>
    /// <param name="room">The room's file, for the report.</param>
    /// <param name="layout">The room as read, with its slots.</param>
    /// <param name="tiles">The set's tiles, each with its identity - null where the file did not read, and such a tile answers nothing.</param>
    public static RoomSlotCandidates Of(string room, RoomLayout layout, IReadOnlyList<(string Path, TileIdentity? Identity)> tiles)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tiles);
        if (!layout.Ready)
        {
            return new RoomSlotCandidates(room, [], $"the room did not read: {layout.Why}");
        }

        if (layout.Slots.Count == 0)
        {
            return new RoomSlotCandidates(room, [], layout.SlotsWhy.Length > 0 ? $"the room's grid did not read: {layout.SlotsWhy}" : "the room has no slot grid");
        }

        // EACH KIND ONCE, in the order its first slot comes - a verdict is per kind, as in the search.
        var kinds = new List<SlotWant>();
        var counts = new List<int>();
        var kindOf = new Dictionary<SlotWant, int>();
        for (var line = 0; line < layout.Height; line++)
        {
            for (var column = 0; column < layout.Width; column++)
            {
                RoomSlot slot = layout.SlotAt(column, line);
                if (!slot.IsTile)
                {
                    continue;
                }

                SlotWant want = SlotWants.Of(layout, slot);
                if (!kindOf.TryGetValue(want, out int kind))
                {
                    kind = kinds.Count;
                    kinds.Add(want);
                    counts.Add(0);
                    kindOf[want] = kind;
                }

                counts[kind]++;
            }
        }

        if (kinds.Count == 0)
        {
            return new RoomSlotCandidates(room, [], "the room's grid holds no slot that asks for a tile");
        }

        // THE SET'S TILES IN THE SLOTS' TERMS, each once however many kinds ask.
        var laid = new SlotWant?[tiles.Count];
        for (var at = 0; at < tiles.Count; at++)
        {
            laid[at] = SlotWants.Of(tiles[at].Identity);
        }

        var found = new List<SlotKindAlike>(kinds.Count);
        for (var kind = 0; kind < kinds.Count; kind++)
        {
            SlotWant want = kinds[kind];
            var alike = new List<string>();
            for (var at = 0; at < tiles.Count; at++)
            {
                if (laid[at] is { } tile && SlotWants.IsAlike(want, tile))
                {
                    alike.Add(tiles[at].Path);
                }
            }

            found.Add(new SlotKindAlike(
                new SlotKind(want.Width, want.Height, want.Tag, want.Edges, want.Grounds, counts[kind], want.Width > 1 || want.Height > 1, SlotWants.Said(want)),
                alike));
        }

        return new RoomSlotCandidates(room, found, string.Empty);
    }

    /// <summary>A room's measure in one line: slots, kinds, and how many kinds one, several or no tile answers.</summary>
    public static string Said(RoomSlotCandidates room)
    {
        ArgumentNullException.ThrowIfNull(room);
        if (room.Why.Length > 0)
        {
            return room.Why;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{room.Slots} slots of {room.Kinds.Count} kinds · one tile answers {room.One} kinds ({room.SlotsSettled} slots), several {room.Several}, none {room.None}{(room.Settled ? " · settled by the files alone" : string.Empty)}");
    }
}
