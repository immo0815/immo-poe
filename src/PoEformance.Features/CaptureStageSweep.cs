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
        @"[A-Za-z0-9_\-./\\]+\.(arm|tdt|tgt|tgm|tmd|ao|aoc|sm|smd|fmt|mat|dds|fxgraph|ot|otc|tst|tsi|gt|et|rs|toy|gft|mtd|tmo|ast|amd|ecf|png)(:\d+)?\b",
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
    /// <param name="folder">Where the part's file is written: the subjects' own files go beside it under <see cref="SampleFolder"/> - none where null.</param>
    public static string Write(Func<string, byte[]?> read, IReadOnlyList<string>? masters, Search? search = null, string? folder = null)
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
        var subjects = new Subjects();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = AtOnce }, (at, loop) =>
        {
            if (search.Stopped)
            {
                loop.Stop();
                return;
            }

            (string master, string room) = jobs[at];
            search.Started(room);
            var own = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
            rows[at] = Built(cached, paints, master, room, own);
            subjects.Add(own);
            search.Did(rows[at]!.Built);
        });

        Row[] done = [.. rows.Where(one => one is not null).Select(one => one!)];
        double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var said = new StringBuilder();
        said.Append(string.Create(CultureInfo.InvariantCulture,
            $"stage sweep: {done.Length} of {jobs.Count} rooms built under {done.Select(one => one.Master).Distinct(StringComparer.OrdinalIgnoreCase).Count()} masters in {seconds / 60.0:0.#} min, {done.Count(one => !one.Built)} did not build{(search.Stopped ? ", STOPPED before the end" : string.Empty)} - {(masters ?? []).Count} masters in the install, {unset} naming no room; the files read kept {kept.Hits:N0} times of {kept.Asks:N0}\n"));
        said.Append(Ranked(done, subjects));
        if (folder is not null)
        {
            said.Append(Samples(cached, done, subjects, Path.Combine(folder, SampleFolder)));
        }

        said.Append(Rows(done));
        return said.ToString();
    }

    /// <summary>The causes ranked group by group, each by how many rooms it reaches - from the rows alone, so the offline reader ranks again what the sweep ranked.</summary>
    public static string Ranked(IReadOnlyList<Row> rows) => Ranked(rows, null);

    /// <summary>The causes ranked, each followed by the files it was met on most - see <see cref="Subjects"/>.</summary>
    internal static string Ranked(IReadOnlyList<Row> rows, Subjects? subjects)
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
                foreach ((string subject, long weight, int rooms) in subjects?.Top(cause, SubjectsShown) ?? [])
                {
                    said.Append(string.Create(CultureInfo.InvariantCulture, $"  \t\t\t  of {subject} - {rooms} rooms, weight {weight}\n"));
                }
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

    /// <summary>
    /// The subjects' files a capture carries (<see cref="SampleFolder"/> beside the part's file) read again with today's readers: each
    /// object built, each material compiled, each tile's definition read - what a mend made offline comes to before the next sweep.
    /// </summary>
    /// <param name="folder">The folder the part's file is in.</param>
    public static string Recheck(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string root = Path.Combine(folder, SampleFolder);
        if (!Directory.Exists(root))
        {
            return $"stage sweep files read again: none - no {SampleFolder}/ beside the part\n";
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            files[Path.GetRelativePath(root, file).Replace('\\', '/')] = file;
        }

        byte[]? Read(string path) => files.TryGetValue(Safe(path).Replace('\\', '/'), out string? file) ? File.ReadAllBytes(file) : null;
        var paints = new MonsterModels.Paints { Lean = true };
        var said = new StringBuilder();
        int objects = 0, objectsBuilt = 0, materials = 0, materialsDrawn = 0, tiles = 0, tilesRead = 0;
        foreach (string path in files.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            string line;
            if (extension == ".ao")
            {
                objects++;
                MonsterModel model = MonsterModels.OfFiles(Read, [path], wearing: true, paints);
                objectsBuilt += model.Ready ? 1 : 0;
                line = model.Ready
                    ? string.Create(CultureInfo.InvariantCulture, $"built, {model.Mesh.Triangles} triangles")
                    : "not built: " + model.Why;
            }
            else if (extension == ".mat")
            {
                materials++;
                MonsterModels.Shade shade = paints.Shade(Read, path);
                ShadeCompile compiled = paints.Compiled(Read, path).Compiled;
                materialsDrawn += compiled.Program is not null ? 1 : 0;
                line = (compiled.Program is not null ? "drawn" : "not drawn")
                    + (shade.Skipped is { Count: > 0 } skipped ? "; left out: " + string.Join("; ", skipped) : string.Empty)
                    + (compiled.Lost.Count > 0 ? "; lost: " + string.Join("; ", compiled.Lost) : string.Empty);
            }
            else if (extension == ".tdt")
            {
                tiles++;
                (TileDefinition definition, string why) = TileModels.Defined(Read, path);
                tilesRead += definition.Ready ? 1 : 0;
                line = definition.Ready ? "read" : "not read: " + why;
            }
            else
            {
                continue;
            }

            said.Append("  ").Append(path).Append('\t').Append(line).Append('\n');
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"stage sweep files read again: {objectsBuilt} of {objects} objects built, {materialsDrawn} of {materials} materials drawn, {tilesRead} of {tiles} tiles read - {files.Count} files beside the part\n") + said;
    }

    /// <summary>One room built under one master and what it came to - never throws.</summary>
    /// <param name="subjects">Where each cause's files are counted, by cause - the material, mesh, object or tile it was met on; none where null.</param>
    internal static Row Built(
        Func<string, byte[]?> read, MonsterModels.Paints paints, string master, string room, Dictionary<string, Dictionary<string, long>>? subjects = null)
    {
        long started = Stopwatch.GetTimestamp();
        var causes = new Dictionary<string, long>(StringComparer.Ordinal);
        void Count(string cause, long weight, string? subject = null)
        {
            if (weight < 0 || (weight == 0 && subject is null))
            {
                return;
            }

            if (weight > 0)
            {
                causes[cause] = causes.GetValueOrDefault(cause) + weight;
            }

            if (subjects is not null && subject is { Length: > 0 })
            {
                Dictionary<string, long> of = subjects.TryGetValue(cause, out Dictionary<string, long>? known) ? known : subjects[cause] = new(StringComparer.OrdinalIgnoreCase);
                of[subject] = of.GetValueOrDefault(subject) + weight;
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
            Count("room: did not build - it threw " + failed.GetType().Name + ": " + Normal(failed.Message), 1, room);
            return new Row(master, room, false, Stopwatch.GetElapsedTime(started).TotalSeconds, 0, causes);
        }

        if (!model.Ready)
        {
            Count("room: did not build - " + Normal(Last(model.Why)), 1, room);
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
                    string named = Paths.Match(file.Why ?? string.Empty) is { Success: true } match ? match.Value : string.Empty;
                    string subject = named.Length > 0 && !string.Equals(named, file.Ao, StringComparison.OrdinalIgnoreCase) ? file.Ao + " -> " + named : file.Ao;
                    Count("geometry: doodad not drawn - " + Normal(file.Why ?? string.Empty) + (set ? " (a set, _set.ao)" : string.Empty), file.Missing, subject);
                }

                if (file.Laid > 0 && file.Ao.Replace('\\', '/').StartsWith("Metadata/Effects/", StringComparison.OrdinalIgnoreCase))
                {
                    Count("visible: an effect object drawn - " + Path.GetFileName(file.Ao), file.Laid, file.Ao);
                }
            }
        }

        return new Row(master, room, model.Ready, Stopwatch.GetElapsedTime(started).TotalSeconds, triangles, causes);
    }

    /// <summary>The room's own counts, each a cause - with the files it names, a few a cause.</summary>
    private static void Accounted(RoomAccount account, Action<string, long, string?> count)
    {
        void Each(string cause, long weight, IReadOnlyList<string> files)
        {
            if (files.Count == 0)
            {
                count(cause, weight, null);
                return;
            }

            // THE CAUSE'S WEIGHT ONCE, its files beside it as subjects at no weight of their own - see Subjects.
            count(cause, weight, files[0]);
            foreach (string file in files.Skip(1))
            {
                count(cause, 0, file);
            }
        }

        Each("geometry: pieces not whole, left out", account.Broken, account.BrokenFiles);
        Each("geometry: pieces whose file has nothing to draw", account.Bare, account.BareFiles);
        count("geometry: pieces laid no way the game's tables said", account.Unturned, null);
        count("geometry: slots no tile of the pool answered", account.Unfilled, null);
        count("geometry: slots laid with a named side or corner unmet", account.Unmet, null);
        Each("geometry: tiles of the lists whose definition did not read", account.PoolUnread, account.UnreadTiles);
        if (account.Unpainted.Length > 0)
        {
            count("ground: unpainted - " + Normal(account.Unpainted), 1, null);
        }

        foreach ((string why, int subTiles) in account.GroundPlain)
        {
            Each("ground: left plain - " + why, subTiles, account.GroundPlainSubjects.GetValueOrDefault(why) ?? []);
        }

        foreach (string name in account.UnlistedGrounds)
        {
            count("ground: not in the MaterialsList, painted as the group with no name (CHOSEN) - " + name, account.GroundUnlisted, account.UnlistedGroundFiles.GetValueOrDefault(name));
        }
    }

    /// <summary>Every shape: what it is drawn with, and why not where nothing colours it - weighed by its triangles.</summary>
    private static void Shapes(Func<string, byte[]?> read, MonsterModels.Paints paints, MonsterModel model, Action<string, long, string?> count)
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
            string subject = material.Length > 0 ? MaterialFile.Bare(material) : source;
            string mode = shape < model.Modes.Count ? model.Modes[shape] : string.Empty;
            bool program = shape < model.Shades.Count && model.Shades[shape] is not null;
            bool texture = (shape < model.Skins.Count ? model.Skins[shape] : model.Skin) is not null;
            bool ground = source.StartsWith(RoomModels.GroundSource, StringComparison.Ordinal);
            bool file = material.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || MaterialFile.Bare(material).EndsWith(".mat", StringComparison.OrdinalIgnoreCase);
            if (mode == "water")
            {
                count(program ? "water: drawn as the area's water" : "water: a water shape drawn white", triangles, subject);
            }

            if (file && paints.Paint(read, MaterialFile.Bare(material)).ShaderType is { Length: > 0 } type
                && !string.Equals(type, ShadeProgram.OceanShader, StringComparison.OrdinalIgnoreCase))
            {
                count("water: a material of the shader type " + type + " drawn as an ordinary material", triangles, subject);
            }

            // WHAT THE COMPILE LOST of a material it drew - the colour's parts, and the other channels.
            MonsterModels.Shade shade = material.Length > 0 && MaterialFile.SelectorOf(material) < 0 ? paints.Shade(read, material) : default;
            if (file)
            {
                foreach (string lost in paints.Compiled(read, material).Compiled.Lost)
                {
                    count("lost: " + lost, triangles, subject);
                }
            }

            if (program || texture)
            {
                foreach (string why in shade.Skipped ?? [])
                {
                    count("partly: " + Normal(why), triangles, subject);
                }

                continue;
            }

            if (material.Length == 0)
            {
                // A GROUND LEFT PLAIN is counted by its sub-tiles from the room's own account; anything else wearing nothing is said here.
                if (!ground)
                {
                    count("white: a shape wearing no material", triangles, subject);
                }

                continue;
            }

            if (MaterialFile.SelectorOf(material) >= 0)
            {
                count("white: a material read through a selector (name.mat:n), not compiled", triangles, subject);
                continue;
            }

            if (shade.Skipped is not { Count: > 0 } skipped)
            {
                count("white: nothing to colour it with", triangles, subject);
                continue;
            }

            foreach (string why in skipped)
            {
                count("white: " + Normal(why), triangles, subject);
            }
        }
    }

    /// <summary>The folder beside the part's file the subjects' own files are written to.</summary>
    public const string SampleFolder = "stage-sweep";

    /// <summary>Subjects named under a cause in the ranking.</summary>
    private const int SubjectsShown = 5;

    /// <summary>Causes of each group whose subjects' files are written - CHOSEN: the first ten by the rooms they reach.</summary>
    private const int SampledCauses = 10;

    /// <summary>Subjects of each such cause whose files are written - CHOSEN.</summary>
    private const int SampledSubjects = 3;

    /// <summary>What the files written come to at most - CHOSEN: a capture's three pieces' worth, packed.</summary>
    private const long SampleBytes = 80L << 20;

    /// <summary>The largest one file written - CHOSEN; a texture larger than <see cref="SampleTexture"/> is left out, and both are listed.</summary>
    private const long SampleFile = 8L << 20;

    /// <summary>The largest texture written - CHOSEN: a ground mask (256 by 512, two bytes a texel) or a lookup, not a material's colour.</summary>
    private const long SampleTexture = 320L << 10;

    /// <summary>How many files one file may bring along that it names.</summary>
    private const int NamedPerFile = 48;

    /// <summary>
    /// Each cause's subjects over every room - the file it was met on, its weight there summed and the rooms it was met in.
    /// </summary>
    /// <remarks>
    /// PRINT BOTH HALVES: a cause says what failed, its subjects on what - the material, the mesh, the object or the tile - so a
    /// mend can be made against the very file, which the sweep then writes beside it (<see cref="Samples"/>). A subject met at no
    /// weight of its own (a room's second file for one count) is still counted in its rooms.
    /// </remarks>
    internal sealed class Subjects
    {
        private readonly Dictionary<string, Dictionary<string, (long Weight, int Rooms)>> _of = new(StringComparer.Ordinal);
        private readonly Lock _lock = new();

        /// <summary>One room's subjects, added in.</summary>
        public void Add(Dictionary<string, Dictionary<string, long>> room)
        {
            lock (_lock)
            {
                foreach ((string cause, Dictionary<string, long> subjects) in room)
                {
                    Dictionary<string, (long Weight, int Rooms)> of = _of.TryGetValue(cause, out var known) ? known : _of[cause] = new(StringComparer.OrdinalIgnoreCase);
                    foreach ((string subject, long weight) in subjects)
                    {
                        (long had, int rooms) = of.GetValueOrDefault(subject);
                        of[subject] = (had + weight, rooms + 1);
                    }
                }
            }
        }

        /// <summary>A cause's subjects met in the most rooms, the heaviest first among those.</summary>
        public IEnumerable<(string Subject, long Weight, int Rooms)> Top(string cause, int count)
        {
            lock (_lock)
            {
                return _of.TryGetValue(cause, out var of)
                    ? [.. of.OrderByDescending(one => one.Value.Rooms).ThenByDescending(one => one.Value.Weight).Take(count).Select(one => (one.Key, one.Value.Weight, one.Value.Rooms))]
                    : [];
            }
        }
    }

    /// <summary>
    /// The files of each group's first causes' first subjects, and the files they name, written under <paramref name="into"/> at their
    /// install paths - and what was written, said.
    /// </summary>
    /// <remarks>
    /// WHAT A MEND IS MADE AGAINST: the sweep runs where the install is, the mend is made where it is not - so the files a cause was met
    /// on travel in the capture (asked for from the live client: the next capture answers which file, then the file itself). A subject
    /// names its files by path; each is written with what it names in its own bytes, read as UTF-16 and as bytes (an object's meshes
    /// and what it extends, a material's graphs, a template's mask), and those again once. Kept to a budget, textures past a mask's
    /// size left out.
    /// </remarks>
    internal static string Samples(Func<string, byte[]?> read, IReadOnlyList<Row> rows, Subjects subjects, string into)
    {
        var rooms = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Row row in rows)
        {
            foreach (string cause in row.Causes.Keys)
            {
                rooms[cause] = rooms.GetValueOrDefault(cause) + 1;
            }
        }

        var written = new Dictionary<string, (long Bytes, string For)>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        long total = 0;
        bool full = false;
        void Write(string path, string cause, int depth)
        {
            if (full || written.ContainsKey(path) || missing.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            byte[]? bytes = read(path);
            bool texture = path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            if (bytes is null || bytes.Length == 0)
            {
                missing.Add(path);
                return;
            }

            if (bytes.LongLength > (texture ? SampleTexture : SampleFile))
            {
                written[path] = (-bytes.LongLength, cause);
                return;
            }

            if (total + bytes.LongLength > SampleBytes)
            {
                full = true;
                return;
            }

            string target = Path.Combine(into, Safe(path));
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? into);
            File.WriteAllBytes(target, bytes);
            written[path] = (bytes.LongLength, cause);
            total += bytes.LongLength;
            if (depth < 2 && !texture)
            {
                foreach (string named in Named(bytes, path).Take(NamedPerFile))
                {
                    Write(named, cause, depth + 1);
                }
            }
        }

        foreach ((string word, _) in Groups)
        {
            IEnumerable<string> causes = rooms.Keys.Where(cause => cause.StartsWith(word, StringComparison.Ordinal))
                .OrderByDescending(cause => rooms[cause]).Take(SampledCauses);
            foreach (string cause in causes)
            {
                foreach ((string subject, _, _) in subjects.Top(cause, SampledSubjects))
                {
                    foreach (Match match in Paths.Matches(subject))
                    {
                        Write(match.Value.Replace('\\', '/'), cause, 0);
                    }
                }
            }
        }

        var said = new StringBuilder();
        said.Append(string.Create(CultureInfo.InvariantCulture,
            $"\n=== the subjects' files, under {SampleFolder}/ at their install paths - {written.Count(one => one.Value.Bytes >= 0)} written, {total / 1048576.0:0.#} MB{(full ? ", STOPPED at the budget" : string.Empty)}; {written.Count(one => one.Value.Bytes < 0)} too large, {missing.Count} not in the install\n"));
        foreach ((string path, (long bytes, string cause)) in written.OrderBy(one => one.Key, StringComparer.OrdinalIgnoreCase))
        {
            said.Append(string.Create(CultureInfo.InvariantCulture, $"  {(bytes >= 0 ? bytes.ToString(CultureInfo.InvariantCulture) : "too large, " + (-bytes).ToString(CultureInfo.InvariantCulture))}\t{path}\tfor {cause}\n"));
        }

        foreach (string path in missing.Order(StringComparer.OrdinalIgnoreCase))
        {
            said.Append("  not in the install\t").Append(path).Append('\n');
        }

        return said.ToString();
    }

    /// <summary>The install paths a file names in its bytes - read as UTF-16 and as single bytes, its own path and duplicates left out.</summary>
    internal static IEnumerable<string> Named(byte[] bytes, string self)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { self };
        foreach (string text in (string[])[System.Text.Encoding.Unicode.GetString(bytes), System.Text.Encoding.Latin1.GetString(bytes)])
        {
            foreach (Match match in Paths.Matches(text))
            {
                string path = match.Value.Replace('\\', '/').TrimStart('/');
                int selector = path.LastIndexOf(':');
                path = selector > 0 ? path[..selector] : path;
                if (path.Contains('/', StringComparison.Ordinal) && seen.Add(path))
                {
                    yield return path;
                }
            }
        }
    }

    /// <summary>An install path as a path under the folder: its parts kept, nothing that climbs out or a file system refuses.</summary>
    internal static string Safe(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        IEnumerable<string> parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part is not ("." or ".."))
            .Select(part => string.Concat(part.Select(letter => Path.GetInvalidFileNameChars().Contains(letter) || letter == ':' ? '_' : letter)));
        return Path.Combine([.. parts]);
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
