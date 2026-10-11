using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// Every room that may stand in the current area: the <c>.arm</c> files it loaded, and every room of every room set (<c>.rs</c>) it loaded, its loaded masters (<c>.tsi</c>) name, or the masters the install keeps beside its rooms name.
/// </summary>
/// <remarks>
/// WHY NOT THE LOADED FILES ALONE. The loaded-file list counts files loaded since the area change,
/// and a room already in memory from the last instance of the same map is not loaded again: in The
/// Assembly it named ten rooms of a set of fifty-three, and neither the boss room nor the entrance,
/// both of which stood in the area. The set - <c>generate.rs</c> beside the area's master - names
/// every room the generator may lay, and a fresh instance's list carries it. A league's rooms (an
/// Expedition encounter, an Incursion waygate) are not in the set and come from the list.
///
/// THREE ROADS TO THE SET. A re-entered instance is not generated again, and its list carried the
/// master and not the set - seventeen rooms placed as two, the morning after fifty-five placed as
/// nineteen. The master's RoomSet line names the set, so every <c>.tsi</c> of the list is read for it
/// too - see MasterFile. And The Stone Citadel's list carried neither, thirteen rooms of fifty-five,
/// the set and the master both cached from an earlier visit. The install's index knows the masters
/// regardless: of the 458 map graphs RePoE's data carries, 456 name a master sitting directly in the
/// folder above the rooms' <c>Rooms/</c>, and a map may have several (UberDoryani three, SwampTower
/// six). So every master the index lists in that folder - the tool's start walks the index for
/// every <c>.tsi</c> under Metadata/Terrain, see TileBook - is read as well. A set two roads reach is
/// read once.
///
/// ONLY THE TERRAIN'S: a <c>.rs</c> or <c>.tsi</c> under Metadata/Terrain is the area's, and nothing
/// else with the extension is asked for.
///
/// AND THE OVERLAYS, WHICH NO SET NAMES. A master's <c>Overlays</c> line names an overlay list
/// (<c>.toy</c>, see OverlayList) of rooms the generator lays over the layout in a pass of its own -
/// Augury's two, Marrow's three - and none of them is in <c>generate.rs</c>. A fresh instance's list
/// carries the ones it laid (and Steaming Springs' carried the <c>.toy</c> itself); a re-entered one
/// carries none, so the lists a loaded <c>.toy</c> or a master names are read for them too.
/// </remarks>
public static class AreaRoomSet
{
    /// <summary>The folder a room file sits under - the area's, where its masters live.</summary>
    private const string RoomsFolder = "/Rooms/";

    /// <summary>The rooms, without duplicates and in path order.</summary>
    /// <param name="loaded">The files the area loaded.</param>
    /// <param name="read">How to get a file out of the install, for the room sets and the masters. Null for a file that is not there.</param>
    /// <param name="masters">Every master (<c>.tsi</c>) the install has, as its index lists them - or none, before the index was walked.</param>
    public static List<string> Files(IEnumerable<string> loaded, Func<string, byte[]?> read, IReadOnlyList<string>? masters = null)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(read);
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var sets = new List<string>();
        var overlays = new List<string>();
        foreach (string path in loaded)
        {
            if (path.EndsWith(".arm", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(path.Replace('\\', '/'));
            }
            else if (path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
            {
                sets.Add(path);
            }
            else if (path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(OverlayList.Extension, StringComparison.OrdinalIgnoreCase))
            {
                overlays.Add(path);
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string set in sets)
        {
            if (seen.Add(set))
            {
                Add(files, RoomSetFile.Read(read(set)));
            }
        }

        foreach (string list in overlays)
        {
            if (seen.Add(list))
            {
                Add(files, OverlayList.Read(read(list)));
            }
        }

        // THE SETS ALREADY READ ARE PASSED ON, so a set both roads reach is read once.
        foreach (string room in FilesOf(Masters(loaded, masters), read, seen))
        {
            files.Add(room);
        }

        return [.. files];
    }

    /// <summary>
    /// The area's masters (<c>.tsi</c>), each once in the order found: the loaded list's, then the install's in the area's folders - the two roads of the three that go through a master.
    /// </summary>
    /// <remarks>
    /// THE AREA'S FOLDERS are where its loaded rooms, sets and masters sit - a room's folder being the
    /// one above its <c>Rooms/</c>. A master of the index counts where it sits directly in one of them,
    /// not deeper: a map's graphs sit a folder down, and nothing there names a set or a tileset.
    /// </remarks>
    /// <param name="loaded">The files the area loaded.</param>
    /// <param name="masters">Every master the install has, as its index lists them - or none, before the index was walked.</param>
    public static List<string> Masters(IEnumerable<string> loaded, IReadOnlyList<string>? masters)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var named = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in loaded)
        {
            if (path.EndsWith(".arm", StringComparison.OrdinalIgnoreCase))
            {
                string room = path.Replace('\\', '/');
                int rooms = room.IndexOf(RoomsFolder, StringComparison.OrdinalIgnoreCase);
                if (rooms > 0)
                {
                    folders.Add(room[..(rooms + 1)]);
                }
            }
            else if (path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase))
            {
                if (path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(OverlayList.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    folders.Add(Folder(path));
                }
                else if (path.EndsWith(".tsi", StringComparison.OrdinalIgnoreCase))
                {
                    folders.Add(Folder(path));
                    if (seen.Add(path))
                    {
                        named.Add(path);
                    }
                }
            }
        }

        foreach (string master in MastersIn(folders, masters))
        {
            if (seen.Add(master))
            {
                named.Add(master);
            }
        }

        return named;
    }

    /// <summary>The install's masters sitting directly in the folders given - an area's, from its graph's folder (AreaGraphs.FoldersOf) when nothing of it is loaded.</summary>
    /// <param name="folders">The folders, each with its trailing slash.</param>
    /// <param name="masters">Every master the install has, as its index lists them - or none.</param>
    public static List<string> MastersIn(IEnumerable<string> folders, IReadOnlyList<string>? masters)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var named = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] wanted = [.. folders];
        foreach (string master in masters ?? [])
        {
            foreach (string folder in wanted)
            {
                if (master.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && master.IndexOf('/', folder.Length) < 0)
                {
                    if (seen.Add(master))
                    {
                        named.Add(master);
                    }

                    break;
                }
            }
        }

        return named;
    }

    /// <summary>
    /// The room lattices the area's masters name - the loaded list's and the install's in the area's folders, as <see cref="Masters"/> finds them. See RoomLattice.
    /// </summary>
    /// <param name="loaded">The files the area loaded.</param>
    /// <param name="read">How to get a file out of the install.</param>
    /// <param name="masters">Every master the install has, as its index lists them - or none.</param>
    public static RoomLattices Lattices(IEnumerable<string> loaded, Func<string, byte[]?> read, IReadOnlyList<string>? masters = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        return RoomLattices.From(Masters(loaded, masters).Select(master => MasterFile.Read(read(master))));
    }

    /// <summary>The rooms the masters' sets and overlay lists name, without duplicates and in path order - an area's rooms from its masters alone, nothing loaded.</summary>
    /// <param name="masters">The masters to read.</param>
    /// <param name="read">How to get a file out of the install.</param>
    public static List<string> FilesOf(IEnumerable<string> masters, Func<string, byte[]?> read)
        => FilesOf(masters, read, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The rooms the masters' sets and overlay lists name, skipping the files already read - see <see cref="Files"/>.</summary>
    private static List<string> FilesOf(IEnumerable<string> masters, Func<string, byte[]?> read, HashSet<string> seen)
    {
        ArgumentNullException.ThrowIfNull(masters);
        ArgumentNullException.ThrowIfNull(read);
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string master in masters)
        {
            MasterFile file = MasterFile.Read(read(master));
            if (Named(master, file.RoomSet, read, seen) is { } set)
            {
                Add(files, RoomSetFile.Read(set));
            }

            if (Named(master, file.Overlays, read, seen) is { } overlays)
            {
                Add(files, OverlayList.Read(overlays));
            }
        }

        return [.. files];
    }

    /// <summary>
    /// The file a master names, read AS WRITTEN, THEN BESIDE THE MASTER, as RePoE resolves it - the first that reads - or null where none does, or the file was read already.
    /// </summary>
    private static byte[]? Named(string master, string named, Func<string, byte[]?> read, HashSet<string> seen)
    {
        foreach (string candidate in MasterFile.Candidates(master, named))
        {
            if (!seen.Add(candidate))
            {
                return null;
            }

            byte[]? bytes = read(candidate);
            if (bytes is { Length: > 0 })
            {
                return bytes;
            }

            seen.Remove(candidate);
        }

        return null;
    }

    /// <summary>The folder a path sits in, with its trailing slash - empty for a bare name.</summary>
    private static string Folder(string path)
    {
        string straight = path.Replace('\\', '/');
        int slash = straight.LastIndexOf('/');
        return slash < 0 ? string.Empty : straight[..(slash + 1)];
    }

    private static void Add(SortedSet<string> files, RoomSetFile set)
    {
        foreach (RoomSetEntry room in set.Rooms)
        {
            files.Add(room.Arm);
        }
    }

    private static void Add(SortedSet<string> files, OverlayList overlays)
    {
        foreach (OverlayEntry room in overlays.Entries)
        {
            files.Add(room.Room);
        }
    }
}
