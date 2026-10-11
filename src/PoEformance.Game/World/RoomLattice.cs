using System.Globalization;
using PoEformance.Game.Files;

namespace PoEformance.Game.World;

/// <summary>
/// Where an area lays the rooms cut to its module: from the outer buffer, every module side less the overlap, both ways.
/// </summary>
/// <remarks>
/// MEASURED ON THE COMMITTED CAPTURES before anything was built on it, after GGG's procedural-generation
/// talk (Rhys Abraham, ExileCon) said rooms are laid overlapping by one tile along every boundary. A master
/// names three numbers no reader here used - RoomUnitSize, RoomOverlap and OuterBufferSize - and every room
/// of the area's own set writes the first two again on its dimension line (RoomLayout.ModuleSide). Held
/// against where the doodads found the rooms:
///
///   - every room of the six captures, 548 of them, is its overlap plus a whole number of (side - overlap)
///     tiles each way - Augury's 5, 9, 13 and 21 on "5 1";
///   - every place of a room of the area's module with a vote behind it lies at OuterBufferSize plus a
///     whole number of (side - overlap) tiles: Augury 29 places of 29 (buffer 4, step 4), Marrow's boss
///     at 58, 10 (buffer 10, step 8), Sulphuric Caverns' boss at 7, 7 (buffer 7, step 9); Oasis, Steppe and
///     Steaming Springs, whose captures predate area-masters.txt, put every such room whose doodads stand
///     within ten units of their lines on one residue, 1, of their own step;
///   - the doodad places off it were the weaker of two places of one room - Augury's Small_End_01 at 44, 12
///     with ten hits and at 42, 12, 43, 13 and 43, 11 with nine or ten, lever_01 at 36, 24 with twenty and at
///     30, 24 with twelve - standing 25 to 97 units from their lines where those on it stood eight, or votes
///     of three lines a hundred units off. Three footprints the tiles drew once for several rooms lay off it
///     too (Steppe's ground_fill_03/checkpoint_03/rare_03, Steaming Springs' open_fill and creek_cnr), the
///     kind of place no doodad confirms; the lattice leaves them out with the rest;
///   - and the grid comes out whole: Augury's 53 tiles are 4 + 11 x 4 + 1 + 4, Marrow's 77 are
///     10 + 7 x 8 + 1 + 10.
///
/// A ROOM OF ANOTHER MODULE IS NOT ON IT: a fill room ("3 0", "5 0", "1 0") or a league's ("3 1") lies
/// wherever the generator put it, and <see cref="Fits"/> says no for it. So the lattice is asked only of a
/// room whose module is the master's.
/// </remarks>
/// <param name="Side">The module's side in tiles - the master's RoomUnitSize.</param>
/// <param name="Overlap">How many tiles neighbouring modules share - RoomOverlap.</param>
/// <param name="Buffer">How many tiles of outer ground lie before the first module - OuterBufferSize.</param>
public readonly record struct RoomLattice(int Side, int Overlap, int Buffer)
{
    /// <summary>How far apart two neighbouring modules start, in tiles.</summary>
    public int Step => Side - Overlap;

    /// <summary>Whether a room's corner on the tile grid lies on the lattice.</summary>
    public bool Holds(int x, int y) => Step > 0 && Residue(x) == 0 && Residue(y) == 0;

    /// <summary>The first coordinate at or after nought that lies on the lattice.</summary>
    public int First => Residue(0) == 0 ? 0 : Step - Residue(0);

    /// <summary>Whether a room is cut to this module: its dimension line names the side and the overlap, and its grid is the overlap plus whole steps each way.</summary>
    public bool Fits(RoomLayout room)
    {
        ArgumentNullException.ThrowIfNull(room);
        return Step > 0
            && room.ModuleSide == Side
            && room.ModuleOverlap == Overlap
            && room.Width > Overlap && (room.Width - Overlap) % Step == 0
            && room.Height > Overlap && (room.Height - Overlap) % Step == 0;
    }

    /// <summary>The lattice in a line's words.</summary>
    public string Said => string.Create(CultureInfo.InvariantCulture, $"every {Step} tiles from {Buffer}");

    private int Residue(int at) => (((at - Buffer) % Step) + Step) % Step;
}

/// <summary>
/// An area's room lattices, one per module its masters name - see <see cref="RoomLattice"/>.
/// </summary>
/// <remarks>
/// SEVERAL MASTERS, ONE ANSWER OR NONE. A map may keep several masters in its folder (UberDoryani three,
/// SwampTower six). A room is given the lattice of the masters whose module is its own; where those masters
/// disagree on the buffer, it is given none - a lattice taken from the wrong master would push a room off
/// the place it stands on.
/// </remarks>
public sealed class RoomLattices
{
    private readonly IReadOnlyList<RoomLattice> _all;

    private RoomLattices(IReadOnlyList<RoomLattice> all) => _all = all;

    /// <summary>No lattice for any room.</summary>
    public static RoomLattices None { get; } = new([]);

    /// <summary>Every distinct lattice the masters name, in the order found.</summary>
    public IReadOnlyList<RoomLattice> All => _all;

    /// <summary>The lattices of the masters given - each master naming all three numbers counts once.</summary>
    public static RoomLattices From(IEnumerable<MasterFile> masters)
    {
        ArgumentNullException.ThrowIfNull(masters);
        var all = new List<RoomLattice>();
        foreach (MasterFile master in masters)
        {
            if (master.RoomUnitSize is { } side && master.RoomOverlap is { } overlap && master.OuterBufferSize is { } buffer && side > overlap && overlap >= 0)
            {
                var lattice = new RoomLattice(side, overlap, buffer);
                if (!all.Contains(lattice))
                {
                    all.Add(lattice);
                }
            }
        }

        return all.Count == 0 ? None : new RoomLattices(all);
    }

    /// <summary>The lattice a room of this module lies on, or null where no master names its module, or the masters naming it disagree.</summary>
    public RoomLattice? For(RoomLayout room)
    {
        ArgumentNullException.ThrowIfNull(room);
        RoomLattice? found = null;
        foreach (RoomLattice lattice in _all)
        {
            if (!lattice.Fits(room))
            {
                continue;
            }

            if (found is { } before && before.Buffer != lattice.Buffer)
            {
                return null;
            }

            found = lattice;
        }

        return found;
    }
}
