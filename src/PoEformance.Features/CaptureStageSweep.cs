using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// Every room of every room set in the install built as the Monster Book builds a stage from its tileset, and what fails counted by
/// cause over all of them - the capture part <c>stage-sweep.txt</c>, off until ticked.
/// </summary>
/// <remarks>
/// WHY (asked for from the live client, 2026-10-29, after Crimson Shores' boss stage came out white, pink and holed). Each of its five
/// causes was a gap in a building block every scene shares - the piece search, the tileset's swaps, a shader node, a ground the list
/// does not name, doodad sets - found because one scene happened to show it. Built over every room at once, the gaps rank themselves by
/// how many rooms they reach, and one mend reaches all of them. DECIDED from the live client: every room of every set, each under every
/// master naming its set (a room under another master wears other grounds and swaps), as a capture part off until ticked, run overnight.
///
/// WHAT IT COUNTS, by the four building blocks the live client named: surfaces drawn white and why (a shape no program and no texture
/// colours; ground left plain), the geometry (pieces not whole, slots no tile answered, doodads not drawn and why), what is drawn that the
/// game shows only at times (effect objects), and the water (drawn as the area's water, white, or as an ordinary material of a water
/// shader type). Beside them what the compile left out of a colour still drawn, and what it lost on the other channels - the alpha
/// among them (ShadeCompile.Lost). It holds no picture: whether something LOOKS wrong is the captures' question, not this one's.
///
/// ONE PAINT CACHE FOR EVERY ROOM, lean (MonsterModels.Paints.Lean): a material is compiled once in the whole sweep, a texture read once
/// and only asked whether it reads; and the files read are kept, a gigabyte at most, the oldest dropped first (<see cref="Kept"/>) - a
/// set's rooms share their tiles and doodads. Several rooms are built at once, each on every core inside (<see cref="AtOnce"/>).
/// </remarks>
public static class CaptureStageSweep
{
    /// <summary>The heading the rows follow under - what the offline reader looks for.</summary>
    public const string RowsHeading = "=== every room";

    /// <summary>What separates one cause from the next in a row.</summary>
    private const string Between = " || ";

    /// <summary>What separates a cause from its weight in a row.</summary>
    private const string Weighs = " => ";

    /// <summary>Rooms named as examples beside a cause.</summary>
    private const int Examples = 3;

    /// <summary>How many rooms are built at once - CHOSEN: a quarter of the cores, at least two; each build runs on every core inside, and a big room holds hundreds of megabytes while it is built.</summary>
    private static int AtOnce => Math.Max(2, Environment.ProcessorCount / 4);

    /// <summary>The groups the causes are ranked in, in the report's order, by the word a cause starts with.</summary>
    private static readonly (string Word, string Heading)[] Groups =
    [
        ("room", "rooms that did not build"),
        ("white", "surfaces drawn white - no program and no texture colours the shape"),
        ("ground", "ground left plain or painted by a stand-in"),
        ("geometry", "geometry - pieces, slots and doodads"),
        ("visible", "drawn, where the game shows it only at times"),
        ("water", "water"),
        ("partly", "a colour drawn with part of its graphs left out"),
        ("lost", "what the compile lost besides the colour - the alpha, the normal, the gloss"),
    ];

    /// <summary>A path in a reason, kept to its kind - so one reason over many files ranks as one.</summary>
    private static readonly Regex Paths = new(
        @"[A-Za-z0-9_\-./\\]+\.(arm|tdt|tgt|tgm|ao|aoc|sm|smd|fmt|mat|dds|fxgraph|ot|otc|tst|tsi|gt|et|rs|toy|gft|mtd|tmo|ast|amd|ecf|png)(:\d+)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A number standing alone in a reason - a count, which differs room by room.</summary>
    private static readonly Regex Numbers = new(@"\b\d+(\.\d+)?\b", RegexOptions.CultureInvariant);

    /// <summary>How far a sweep is, for the capture's window - and its stop.</summary>
    public sealed class Search
    {
        private long _done;
        private long _of;
        private long _failed;
        private volatile bool _stopped;
        private volatile string _now = string.Empty;

        /// <summary>Rooms built or tried.</summary>
        public long Done => Interlocked.Read(ref _done);

        /// <summary>Rooms to build.</summary>
        public long Of => Interlocked.Read(ref _of);

        /// <summary>Rooms that did not build.</summary>
        public long Failed => Interlocked.Read(ref _failed);

        /// <summary>The room last started, for the window.</summary>
        public string Now => _now;

        /// <summary>Whether it was told to stop.</summary>
        public bool Stopped => _stopped;

        /// <summary>Stops it, keeping what it built.</summary>
        public void Stop() => _stopped = true;

        internal void Begin(long of) => Interlocked.Exchange(ref _of, of);

        internal void Started(string room) => _now = room;

        internal void Did(bool built)
        {
            Interlocked.Increment(ref _done);
            if (!built)
            {
                Interlocked.Increment(ref _failed);
            }
        }
    }

    /// <summary>One room built under one master: what it came to, cause by cause, each with its weight.</summary>
    /// <param name="Master">The master (<c>.tsi</c>) it was built under.</param>
    /// <param name="Room">The room (<c>.arm</c>).</param>
    /// <param name="Built">Whether anything was drawn.</param>
    /// <param name="Seconds">How long the build took.</param>
    /// <param name="Triangles">The triangles drawn.</param>
    /// <param name="Causes">Each cause with its weight - triangles for a shape's, sub-tiles for the ground's, lines for a doodad's, else a count.</param>
    public sealed record Row(string Master, string Room, bool Built, double Seconds, long Triangles, IReadOnlyDictionary<string, long> Causes);

    /// <summary>The part's text: a line saying what it holds, the causes ranked group by group, then every room a row. Never throws on a missing file.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="masters">Every master (<c>.tsi</c>) in the install, as its walk found them.</param>
    /// <param name="search">How far it is, and its stop - for the capture's window.</param>
    public static string Write(Func<string, byte[]?> read, IReadOnlyList<string>? masters, Search? search = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        search ??= new Search();
        long started = Stopwatch.GetTimestamp();
        var kept = new Kept(read, KeptBytes);
        Func<string, byte[]?> cached = kept.Read;

        // EVERY ROOM UNDER EVERY MASTER NAMING ITS SET - the set's rooms and its overlays', as an area's are found (AreaRoomSet.FilesOf).
        var jobs = new List<(string Master, string Room)>();
        int unset = 0;
        foreach (string master in (masters ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            List<string> rooms = AreaRoomSet.FilesOf([master], cached);
            unset += rooms.Count == 0 ? 1 : 0;
            jobs.AddRange(rooms.Select(room => (master, room)));
        }

        search.Begin(jobs.Count);
        var paints = new MonsterModels.Paints { Lean = true };
        var rows = new Row?[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = AtOnce }, (at, loop) =>
        {
            if (search.Stopped)
            {
                loop.Stop();
                return;
            }

            (string master, string room) = jobs[at];
            search.Started(room);
            rows[at] = Built(cached, paints, master, room);
            search.Did(rows[at]!.Built);
        });

        Row[] done = [.. rows.Where(one => one is not null).Select(one => one!)];
        double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var said = new StringBuilder();
        said.Append(string.Create(CultureInfo.InvariantCulture,
            $"stage sweep: {done.Length} of {jobs.Count} rooms built under {done.Select(one => one.Master).Distinct(StringComparer.OrdinalIgnoreCase).Count()} masters in {seconds / 60.0:0.#} min, {done.Count(one => !one.Built)} did not build{(search.Stopped ? ", STOPPED before the end" : string.Empty)} - {(masters ?? []).Count} masters in the install, {unset} naming no room; the files read kept {kept.Hits:N0} times of {kept.Asks:N0}\n"));
        said.Append(Ranked(done));
        said.Append(Rows(done));
        return said.ToString();
    }

    /// <summary>The causes ranked group by group, each by how many rooms it reaches - from the rows alone, so the offline reader ranks again what the sweep ranked.</summary>
    public static string Ranked(IReadOnlyList<Row> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var causes = new Dictionary<string, (int Rooms, HashSet<string> Masters, long Weight, List<string> Examples)>(StringComparer.Ordinal);
        foreach (Row row in rows)
        {
            foreach ((string cause, long weight) in row.Causes)
            {
                if (!causes.TryGetValue(cause, out var tally))
                {
                    tally = (0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, []);
                }

                tally.Masters.Add(row.Master);
                if (tally.Examples.Count < Examples)
                {
                    tally.Examples.Add(Path.GetFileNameWithoutExtension(row.Room) + " (" + Path.GetFileName(Path.GetDirectoryName(row.Master.Replace('\\', '/')) ?? row.Master) + ")");
                }

                causes[cause] = (tally.Rooms + 1, tally.Masters, tally.Weight + weight, tally.Examples);
            }
        }

        long triangles = rows.Sum(one => one.Triangles);
        long white = rows.Sum(one => one.Causes.Where(cause => cause.Key.StartsWith("white", StringComparison.Ordinal)).Sum(cause => cause.Value));
        var said = new StringBuilder();
        said.Append(string.Create(CultureInfo.InvariantCulture,
            $"rooms {rows.Count}, built {rows.Count(one => one.Built)}, triangles drawn {triangles:N0}, of them white {white:N0} ({(triangles > 0 ? 100.0 * white / triangles : 0):0.##}%); rooms with a white surface {rows.Count(one => one.Causes.Keys.Any(cause => cause.StartsWith("white", StringComparison.Ordinal)))}, with ground left plain {rows.Count(one => one.Causes.Keys.Any(cause => cause.StartsWith("ground: left plain", StringComparison.Ordinal)))}, with a doodad not drawn {rows.Count(one => one.Causes.Keys.Any(cause => cause.StartsWith("geometry: doodad", StringComparison.Ordinal)))}\n"));
        foreach ((string word, string heading) in Groups)
        {
            var group = causes.Where(one => one.Key.StartsWith(word, StringComparison.Ordinal)).OrderByDescending(one => one.Value.Rooms).ThenByDescending(one => one.Value.Weight).ToList();
            said.Append("\n=== ").Append(heading).Append(string.Create(CultureInfo.InvariantCulture, $" - {group.Count} causes, by the rooms they reach\n"));
            said.Append("  rooms\tmasters\tweight\tcause\texamples\n");
            foreach ((string cause, var tally) in group)
            {
                said.Append(string.Create(CultureInfo.InvariantCulture, $"  {tally.Rooms}\t{tally.Masters.Count}\t{tally.Weight}\t{cause}\t{string.Join(", ", tally.Examples)}\n"));
            }
        }

        // AND BY NODE ALONE, the compile's reasons being "node in graph": one node unknown in many graphs is one thing to build.
        var nodes = new Dictionary<string, (int Rooms, long Weight)>(StringComparer.Ordinal);
        foreach (Row row in rows)
        {
            foreach (string node in row.Causes.Keys.Where(cause => cause.StartsWith("white:", StringComparison.Ordinal) || cause.StartsWith("partly:", StringComparison.Ordinal) || cause.StartsWith("lost:", StringComparison.Ordinal))
                .Select(NodeOf).Where(node => node.Length > 0).Distinct(StringComparer.Ordinal))
            {
                (int count, long weight) = nodes.GetValueOrDefault(node);
                nodes[node] = (count + 1, weight + row.Causes.Where(cause => NodeOf(cause.Key) == node).Sum(cause => cause.Value));
            }
        }

        said.Append(string.Create(CultureInfo.InvariantCulture, $"\n=== the compile's reasons by node alone - {nodes.Count} nodes, by the rooms they reach\n"));
        said.Append("  rooms\tweight\tnode\n");
        foreach ((string node, (int count, long weight)) in nodes.OrderByDescending(one => one.Value.Rooms).ThenByDescending(one => one.Value.Weight))
        {
            said.Append(string.Create(CultureInfo.InvariantCulture, $"  {count}\t{weight}\t{node}\n"));
        }

        return said.ToString();
    }

    /// <summary>The rows, a room a line, tab-separated: master, room, built, seconds, triangles, then its causes.</summary>
    public static string Rows(IReadOnlyList<Row> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var said = new StringBuilder();
        said.Append('\n').Append(RowsHeading).Append(": master, room, built, seconds, triangles, then each cause and its weight (cause").Append(Weighs).Append("weight, separated by").Append(Between).Append(")\n");
        foreach (Row row in rows.OrderBy(one => one.Master, StringComparer.OrdinalIgnoreCase).ThenBy(one => one.Room, StringComparer.OrdinalIgnoreCase))
        {
            said.Append(string.Create(CultureInfo.InvariantCulture, $"{row.Master}\t{row.Room}\t{(row.Built ? "yes" : "no")}\t{row.Seconds:0.###}\t{row.Triangles}\t"));
            said.Append(string.Join(Between, row.Causes.OrderBy(one => one.Key, StringComparer.Ordinal).Select(one => one.Key + Weighs + one.Value.ToString(CultureInfo.InvariantCulture))));
            said.Append('\n');
        }

        return said.ToString();
    }

    /// <summary>The rows read back from the part's text - the offline reader's way in.</summary>
    public static IReadOnlyList<Row> RowsOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<Row>();
        var inside = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith(RowsHeading, StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            string[] cells = line.Split('\t');
            if (!inside || cells.Length < 6
                || !double.TryParse(cells[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                || !long.TryParse(cells[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long triangles))
            {
                continue;
            }

            var causes = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (string one in cells[5].Split(Between, StringSplitOptions.RemoveEmptyEntries))
            {
                int at = one.LastIndexOf(Weighs, StringComparison.Ordinal);
                if (at > 0 && long.TryParse(one[(at + Weighs.Length)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long weight))
                {
                    causes[one[..at]] = weight;
                }
            }

            rows.Add(new Row(cells[0], cells[1], cells[2] == "yes", seconds, triangles, causes));
        }

        return rows;
    }

    /// <summary>The offline reader's report: the causes ranked again from the rows of a capture's part.</summary>
    public static string Report(string text)
    {
        IReadOnlyList<Row> rows = RowsOf(text);
        return string.Create(CultureInfo.InvariantCulture, $"stage sweep replay: {rows.Count} rows read back\n") + Ranked(rows);
    }

    /// <summary>One room built under one master and what it came to - never throws.</summary>
    internal static Row Built(Func<string, byte[]?> read, MonsterModels.Paints paints, string master, string room)
    {
        long started = Stopwatch.GetTimestamp();
        var causes = new Dictionary<string, long>(StringComparer.Ordinal);
        void Count(string cause, long weight)
        {
            if (weight > 0)
            {
                causes[cause] = causes.GetValueOrDefault(cause) + weight;
            }
        }

        MonsterModel model;
        try
        {
            model = BuiltRoomModels.Built(
                read, room, master, shaded: true, RoomModels.EveryDoodad, tools: false, progress: null, DoodadHeight.File, mirrored: false,
                maskCells: false, groundHeights: false, paints);
        }
        catch (Exception failed) when (failed is not OutOfMemoryException)
        {
            Count("room: did not build - it threw " + failed.GetType().Name + ": " + Normal(failed.Message), 1);
            return new Row(master, room, false, Stopwatch.GetElapsedTime(started).TotalSeconds, 0, causes);
        }

        if (!model.Ready)
        {
            Count("room: did not build - " + Normal(Last(model.Why)), 1);
        }

        if (model.Account is { } account)
        {
            Accounted(account, Count);
        }

        long triangles = 0;
        if (model.Ready)
        {
            triangles = model.Mesh.Triangles;
            Shapes(read, paints, model, Count);
            foreach (DoodadFile file in model.DoodadFiles)
            {
                if (file.Missing > 0)
                {
                    bool set = file.Ao.EndsWith("_set.ao", StringComparison.OrdinalIgnoreCase);
                    Count("geometry: doodad not drawn - " + Normal(file.Why) + (set ? " (a set, _set.ao)" : string.Empty), file.Missing);
                }

                if (file.Laid > 0 && file.Ao.Replace('\\', '/').StartsWith("Metadata/Effects/", StringComparison.OrdinalIgnoreCase))
                {
                    Count("visible: an effect object drawn - " + Path.GetFileName(file.Ao), file.Laid);
                }
            }
        }

        return new Row(master, room, model.Ready, Stopwatch.GetElapsedTime(started).TotalSeconds, triangles, causes);
    }

    /// <summary>The room's own counts, each a cause.</summary>
    private static void Accounted(RoomAccount account, Action<string, long> count)
    {
        count("geometry: pieces not whole, left out", account.Broken);
        count("geometry: pieces whose file has nothing to draw", account.Bare);
        count("geometry: pieces laid no way the game's tables said", account.Unturned);
        count("geometry: slots no tile of the pool answered", account.Unfilled);
        count("geometry: slots laid with a named side or corner unmet", account.Unmet);
        count("geometry: tiles of the lists whose definition did not read", account.PoolUnread);
        if (account.Unpainted.Length > 0)
        {
            count("ground: unpainted - " + Normal(account.Unpainted), 1);
        }

        foreach ((string why, int subTiles) in account.GroundPlain)
        {
            count("ground: left plain - " + why, subTiles);
        }

        foreach (string name in account.UnlistedGrounds)
        {
            count("ground: not in the MaterialsList, painted as the group with no name (CHOSEN) - " + name, account.GroundUnlisted);
        }
    }

    /// <summary>Every shape: what it is drawn with, and why not where nothing colours it - weighed by its triangles.</summary>
    private static void Shapes(Func<string, byte[]?> read, MonsterModels.Paints paints, MonsterModel model, Action<string, long> count)
    {
        IReadOnlyList<MeshShape> shapes = model.Mesh.Shapes;
        for (var shape = 0; shape < shapes.Count; shape++)
        {
            long triangles = shapes[shape].Count / 3;
            if (triangles == 0)
            {
                continue;
            }

            string material = shape < model.ShapeMaterials.Count ? model.ShapeMaterials[shape] : string.Empty;
            string source = shape < model.ShapeSources.Count ? model.ShapeSources[shape] : string.Empty;
            string mode = shape < model.Modes.Count ? model.Modes[shape] : string.Empty;
            bool program = shape < model.Shades.Count && model.Shades[shape] is not null;
            bool texture = (shape < model.Skins.Count ? model.Skins[shape] : model.Skin) is not null;
            bool ground = source.StartsWith(RoomModels.GroundSource, StringComparison.Ordinal);
            bool file = material.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || MaterialFile.Bare(material).EndsWith(".mat", StringComparison.OrdinalIgnoreCase);
            if (mode == "water")
            {
                count(program ? "water: drawn as the area's water" : "water: a water shape drawn white", triangles);
            }

            if (file && paints.Paint(read, MaterialFile.Bare(material)).ShaderType is { Length: > 0 } type
                && !string.Equals(type, ShadeProgram.OceanShader, StringComparison.OrdinalIgnoreCase))
            {
                count("water: a material of the shader type " + type + " drawn as an ordinary material", triangles);
            }

            // WHAT THE COMPILE LOST of a material it drew - the colour's parts, and the other channels.
            MonsterModels.Shade shade = material.Length > 0 && MaterialFile.SelectorOf(material) < 0 ? paints.Shade(read, material) : default;
            if (file)
            {
                foreach (string lost in paints.Compiled(read, material).Compiled.Lost)
                {
                    count("lost: " + lost, triangles);
                }
            }

            if (program || texture)
            {
                foreach (string why in shade.Skipped ?? [])
                {
                    count("partly: " + Normal(why), triangles);
                }

                continue;
            }

            if (material.Length == 0)
            {
                // A GROUND LEFT PLAIN is counted by its sub-tiles from the room's own account; anything else wearing nothing is said here.
                if (!ground)
                {
                    count("white: a shape wearing no material", triangles);
                }

                continue;
            }

            if (MaterialFile.SelectorOf(material) >= 0)
            {
                count("white: a material read through a selector (name.mat:n), not compiled", triangles);
                continue;
            }

            if (shade.Skipped is not { Count: > 0 } skipped)
            {
                count("white: nothing to colour it with", triangles);
                continue;
            }

            foreach (string why in skipped)
            {
                count("white: " + Normal(why), triangles);
            }
        }
    }

    /// <summary>A compile's reason's node - the text before " in " - or empty where it has none.</summary>
    private static string NodeOf(string cause)
    {
        int colon = cause.IndexOf(": ", StringComparison.Ordinal);
        string reason = colon >= 0 ? cause[(colon + 2)..] : cause;
        int channel = reason.IndexOf(": ", StringComparison.Ordinal);
        reason = channel >= 0 && reason.IndexOf(" in ", StringComparison.Ordinal) > channel ? reason[(channel + 2)..] : reason;
        int at = reason.IndexOf(" in ", StringComparison.Ordinal);
        return at > 0 ? reason[..at] : string.Empty;
    }

    /// <summary>The last of a build's "; "-joined lines - the reason itself, the lines before it being what went right.</summary>
    private static string Last(string why)
    {
        int at = why.LastIndexOf("; ", StringComparison.Ordinal);
        return at >= 0 ? why[(at + 2)..] : why;
    }

    /// <summary>A reason with its paths kept to their kind and its counts to a hash - so one reason over many rooms ranks as one.</summary>
    internal static string Normal(string why)
        => Numbers.Replace(Paths.Replace(why ?? string.Empty, match => "*." + match.Groups[1].Value.ToLowerInvariant()), "#");

    /// <summary>The bytes the files read are kept to - CHOSEN: a gigabyte.</summary>
    private const long KeptBytes = 1L << 30;

    /// <summary>The files read, kept to a budget, the oldest dropped first - a set's rooms share their tiles and doodads.</summary>
    private sealed class Kept(Func<string, byte[]?> read, long budget)
    {
        private readonly Dictionary<string, (byte[]? Bytes, LinkedListNode<string> Node)> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _order = new();
        private readonly Lock _lock = new();
        private long _bytes;
        private long _asks;
        private long _hits;

        /// <summary>Files asked for.</summary>
        public long Asks => Interlocked.Read(ref _asks);

        /// <summary>Files found kept.</summary>
        public long Hits => Interlocked.Read(ref _hits);

        public byte[]? Read(string path)
        {
            Interlocked.Increment(ref _asks);
            lock (_lock)
            {
                if (_files.TryGetValue(path, out var kept))
                {
                    _order.Remove(kept.Node);
                    _order.AddLast(kept.Node);
                    Interlocked.Increment(ref _hits);
                    return kept.Bytes;
                }
            }

            byte[]? bytes = read(path);
            long size = bytes?.LongLength ?? 0;
            if (size > budget / 16)
            {
                return bytes;
            }

            lock (_lock)
            {
                if (_files.ContainsKey(path))
                {
                    return bytes;
                }

                _files[path] = (bytes, _order.AddLast(path));
                _bytes += size;
                while (_bytes > budget && _order.First is { } oldest)
                {
                    _order.RemoveFirst();
                    if (_files.Remove(oldest.Value, out var dropped))
                    {
                        _bytes -= dropped.Bytes?.LongLength ?? 0;
                    }
                }
            }

            return bytes;
        }
    }
}
