using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// One wobble core laid in a picture: its rig played, and where its core stands - what turns the doodads of its sector.
/// </summary>
/// <param name="rig">The core's rig and animation.</param>
/// <param name="core">Where the core's line puts its model, in the picture's frame - the room's laying, mirror and all, included.</param>
/// <param name="lines">How many doodad lines it sways.</param>
public sealed class RoomSway(WobbleRig rig, Matrix4x4 core, int lines)
{
    private readonly Matrix4x4 _undo = Matrix4x4.Invert(core, out Matrix4x4 undo) ? undo : Matrix4x4.Identity;

    /// <summary>The core's rig and animation.</summary>
    public WobbleRig Rig => rig;

    /// <summary>Where the core's line puts its model, in the picture's frame.</summary>
    public Matrix4x4 Core => core;

    /// <summary>How many doodad lines it sways.</summary>
    public int Lines => lines;

    /// <summary>
    /// The sway at a moment in the picture's frame, row vectors: into the core's frame, turned by the bone, back out -
    /// <c>inverse(core) * bone * core</c>, so a mirrored room sways as the mirror image of the written one.
    /// </summary>
    public Matrix4x4 At(float seconds) => _undo * Rig.At(seconds) * Core;

    /// <summary>The same core placed further - the whole picture moved, as a stage moves the room it is built round.</summary>
    public RoomSway Placed(Matrix4x4 place) => place.IsIdentity ? this : new RoomSway(rig, core * place, lines);
}

/// <summary>
/// Which of a room's doodad lines a wobble core sways - the rule the game's files and program write, worked out per room.
/// </summary>
/// <remarks>
/// THE RULE, AS FAR AS IT IS WRITTEN (v0.1.290's capture, Port): a core's object type carries <c>WobblePoint { sector; bone }</c>; the
/// room names the sector as one of its zones (<see cref="RoomZone"/>, Port's <c>"caravan" 26 -102 261 454</c>, the ship's footprint);
/// and a doodad whose type sets <c>no_wobble</c> (DoodadNoWobble, a construction event the program names) is left still. So a line
/// sways when its cell lies in a zone of the core's sector's name and its type does not set no_wobble - the ship
/// (<c>Doodad</c>) and the deck's load inside its footprint, not the harbour's water planes (<c>DoodadNoWobble</c>). In the game's
/// memory the WobblePoint holds its sector as the Sector component of an entity of its own (the census in wobble.txt, v0.1.291),
/// whose list is what the next capture names - until then this is a reading of the files, said under the picture.
/// A tile's own doodads are not asked: a zone is the room's, in the room's cells.
/// </remarks>
internal sealed class RoomSways
{
    private readonly List<(RoomDoodad Line, WobbleRig? Rig, RoomZone[] Zones)> _cores;
    private readonly Dictionary<string, bool> _sways = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, byte[]?> _read;

    private RoomSways(Func<string, byte[]?> read, List<(RoomDoodad, WobbleRig?, RoomZone[])> cores, string said)
    {
        _read = read;
        _cores = cores;
        Said = said;
    }

    /// <summary>What was found, for the line under the picture - empty where the room has no wobble core.</summary>
    public string Said { get; }

    /// <summary>The cores found, in the room's order - a line's group indexes these.</summary>
    public int Count => _cores.Count;

    /// <summary>
    /// The room's wobble cores played, or null where it names none: every line whose type's <c>.ot</c> carries a WobblePoint, its rig read.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="read">How to get a file out of the install.</param>
    /// <param name="unpack">How to undo Oodle, for the rig's keyframes.</param>
    public static RoomSways? Of(RoomLayout room, Func<string, byte[]?> read, Func<ReadOnlyMemory<byte>, int, byte[]?>? unpack)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(read);

        // NO ZONE, NO SECTOR, NO SWAY - and no file read for it: a room's scripted types are asked only where a sector could stand.
        if (room.Zones.Count == 0)
        {
            return null;
        }

        var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cores = new List<(RoomDoodad, WobbleRig?, RoomZone[])>();
        var said = new List<string>();
        var points = new Dictionary<string, (string Sector, WobbleRig? Rig)?>(StringComparer.OrdinalIgnoreCase);
        foreach (RoomDoodad line in room.Doodads)
        {
            // ONLY A SCRIPTED TYPE CAN BE A CORE - a plain prop's type is Doodad, read once below for its no_wobble.
            if (line.Stub.Length == 0 || line.IsProp)
            {
                continue;
            }

            // A CORE WHOSE RIG DOES NOT PLAY STILL CLAIMS ITS SECTOR: what it would sway is the files' rule either way, and it sways nothing.
            if (!points.TryGetValue(line.Stub, out (string Sector, WobbleRig? Rig)? point))
            {
                point = null;
                if (asked.Add(line.Stub) && WobbleRig.Point(read, line.Stub) is { } named)
                {
                    WobbleRig? rig = WobbleRig.Of(read, unpack, line.Stub, out string why);
                    said.Add(rig is null ? $"{line.Stub}: not played - {why}" : rig.Said());
                    point = (named.Sector, rig);
                }

                points[line.Stub] = point;
            }

            if (point is not { } core)
            {
                continue;
            }

            RoomZone[] zones = [.. room.Zones.Where(zone => string.Equals(zone.Name, core.Sector, StringComparison.OrdinalIgnoreCase))];
            if (zones.Length == 0)
            {
                said.Add($"{line.Stub}: the room names no zone {core.Sector}, so it sways nothing");
                continue;
            }

            cores.Add((line, core.Rig, zones));
        }

        return said.Count == 0 ? null : new RoomSways(read, cores, string.Join("; ", said));
    }

    /// <summary>Whether a line is one of the cores - and which.</summary>
    public int CoreOf(RoomDoodad line)
    {
        for (var one = 0; one < _cores.Count; one++)
        {
            if (_cores[one].Line == line)
            {
                return one;
            }
        }

        return -1;
    }

    /// <summary>The rig of a core, or null where it did not play.</summary>
    public WobbleRig? RigOf(int core) => _cores[core].Rig;

    /// <summary>The core whose sector a line stands in and whose type may be swayed, or -1 - the first core where two would claim it.</summary>
    public int GroupOf(RoomDoodad line)
    {
        if (line.Stub.Length == 0)
        {
            return -1;
        }

        for (var one = 0; one < _cores.Count; one++)
        {
            (RoomDoodad core, _, RoomZone[] zones) = _cores[one];
            if (line == core || !zones.Any(zone => zone.Holds(line.X, line.Y)))
            {
                continue;
            }

            if (!_sways.TryGetValue(line.Stub, out bool sways))
            {
                sways = WobbleRig.Sways(_read, line.Stub);
                _sways[line.Stub] = sways;
            }

            return sways ? one : -1;
        }

        return -1;
    }

    /// <summary>A count for the line under the picture.</summary>
    public static string Counted(IReadOnlyList<RoomSway> sways) => sways.Count == 0
        ? string.Empty
        : string.Join("; ", sways.Select(one => string.Create(
            CultureInfo.InvariantCulture,
            $"{one.Lines} doodads of the zone {one.Rig.Sector} sway by {one.Rig.Animation.Name} of {Path.GetFileName(one.Rig.Skeleton)} ({one.Rig.Seconds:0.#} s a loop), the bone {one.Rig.Bone}")));
}
