using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// Every .ao in the install by the simulation its ClientAnimationController names - which sets there are, how many files carry each, what
/// their rigs simulate, and how much the game uses them - for choosing which simulation to measure and build next.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (2026-11-01), once the Exile's Staff's lamp swung (BoneSwing, v0.1.416): "get an overview of how the rest
/// of the .ao files are spread; once we know which are used most we can think about building more." A piece's simulation is the .ao's own -
/// the lamp's ten keys (bend_stiffness 800, the viscosities nought, ...), the player's cape's six others - and the tool swings only the set
/// measured. So the part reads every .ao the startup walk found, follows what each extends, and groups the files by the simulation keys
/// their chain carries (every ClientAnimationController key but the rig's, a socket's and the attachment's, nearest file first); reads each
/// simulated file's rig for its phys_ bones - rods (a phys_ bone on a bone not simulated, none below it: what the lamp is) against chains and
/// skinned cloth; and counts how much each is used: the rows of ItemVisualIdentity and MonsterVarieties naming it, the .ao files attaching
/// it, and the rows reaching it through those attachments.
/// </remarks>
public static class CaptureAoSimulations
{
    /// <summary>Where the startup walk looks for the files.</summary>
    public const string Folder = "Metadata/";

    /// <summary>What the startup walk keeps.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [AnimatedObject.Suffix];

    /// <summary>The part's numbers beside it, a line a simulated file, for the replay (<see cref="Replay"/>).</summary>
    public const string DataFile = "ao-simulations.tsv";

    /// <summary>The most rigs read - past it a file's rig is said not read (CHOSEN: every one in practice, a cap against a surprise).</summary>
    public const int MostRigs = 8000;

    /// <summary>How many attachments up a file's users are followed (CHOSEN, as the model walk's depth).</summary>
    public const int MostUp = 4;

    /// <summary>Files a worker reads in a row - side by side in their bundle (CaptureWaterShore's).</summary>
    private const int Run = 256;

    /// <summary>The ClientAnimationController keys that say nothing of a simulation: the rig, a socket and its frame, the attachment's pairing.</summary>
    private static readonly HashSet<string> NotSimulation = new(StringComparer.Ordinal)
    {
        "skeleton", "socket", "parent", "translation", "rotation", "scale", "attachment_bones",
    };

    /// <summary>The entry keys whose value is another .ao, as the model walk follows them.</summary>
    private static readonly string[] Hangs = ["ao", "fixed_ao", "attached_object", "attached_slaved_animation_object"];

    /// <summary>How far it is, and its stop - for the capture's window.</summary>
    public sealed class Running
    {
        private long _done;
        private long _of;
        private volatile bool _stopped;
        private volatile string _phase = "starting";

        /// <summary>Files done in the phase.</summary>
        public long Done => Interlocked.Read(ref _done);

        /// <summary>Files in the phase.</summary>
        public long Of => Interlocked.Read(ref _of);

        /// <summary>Whether it was told to stop.</summary>
        public bool Stopped => _stopped;

        /// <summary>The window's line.</summary>
        public string Line => Of > 0 ? $"{_phase}: {Done:N0} of {Of:N0}" : _phase;

        /// <summary>The window's bar.</summary>
        public float Fraction => Of > 0 ? (float)((double)Done / Of) : 0f;

        /// <summary>Stops it, keeping what it read.</summary>
        public void Stop() => _stopped = true;

        internal void Begin(string phase, long of)
        {
            _phase = phase;
            Interlocked.Exchange(ref _done, 0);
            Interlocked.Exchange(ref _of, of);
        }

        internal void Did(int files) => Interlocked.Add(ref _done, files);
    }

    /// <summary>One .ao as its own file writes it: what it extends, its ClientAnimationController lines, and the .ao files it hangs.</summary>
    private sealed record Own(IReadOnlyList<string> Extends, IReadOnlyDictionary<string, string> Controller, IReadOnlyList<string> Hangs);

    /// <summary>What a rig simulates: its bones, its phys_ bones, how many hang as rods, how many chains and the longest, how many are skinned.</summary>
    internal readonly record struct RigShape(bool Read, int Bones, int Physics, int Rods, int Chains, int Longest, int Skinned)
    {
        public string Said => !Read ? "rig not read"
            : Physics == 0 ? $"{Bones} bones, none simulated"
            : $"{Bones} bones, {Physics} simulated: {Rods} rods, {Chains} chains (the longest {Longest}){(Skinned > 0 ? $", {Skinned} skinned" : string.Empty)}";
    }

    /// <summary>One simulated .ao: its set, its rig and the rig's shape, and how much it is used.</summary>
    internal sealed record Simulated(
        string Path, string Set, string Rig, RigShape Shape, int Items, int Monsters, int Attached, int ReachedItems, int ReachedMonsters)
    {
        /// <summary>How much the game uses it: the table rows reaching it, directly or through attachments.</summary>
        public int Used => ReachedItems + ReachedMonsters;
    }

    /// <summary>
    /// The part: every .ao read and grouped, the rigs read, the tables counted; its text and the data file's lines.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="aoFiles">Every .ao the startup walk found.</param>
    /// <param name="itemRows">Each ItemVisualIdentity row's string values, or null where the table did not open - see <see cref="Rows"/>.</param>
    /// <param name="monsterRows">Each MonsterVarieties row's string values, the same.</param>
    /// <param name="tables">What opening the tables said.</param>
    /// <param name="running">How far it is, and its stop.</param>
    /// <param name="data">The data file's text.</param>
    public static List<string> Write(
        Func<string, byte[]?> read,
        IReadOnlyList<string>? aoFiles,
        IReadOnlyList<IReadOnlyList<string>>? itemRows,
        IReadOnlyList<IReadOnlyList<string>>? monsterRows,
        string tables,
        Running running,
        out string data)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(running);
        var clock = Stopwatch.StartNew();
        string[] paths = [.. (aoFiles ?? []).Where(one => one.EndsWith(AnimatedObject.Suffix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

        // EVERY FILE, on every core, each worker a run of neighbours.
        var owns = new ConcurrentDictionary<string, Own>(StringComparer.OrdinalIgnoreCase);
        running.Begin("the .ao files read", paths.Length);
        int unread = 0;
        Parallel.ForEach(Partitioner.Create(0, Math.Max(1, paths.Length), Run), range =>
        {
            int missed = 0, done = 0;
            for (int at = range.Item1; at < Math.Min(range.Item2, paths.Length) && !running.Stopped; at++, done++)
            {
                if (Parsed(read, paths[at]) is { } own)
                {
                    owns[paths[at]] = own;
                }
                else
                {
                    missed++;
                }
            }

            running.Did(done);
            Interlocked.Add(ref unread, missed);
        });
        TimeSpan readTook = clock.Elapsed;

        // EACH FILE'S CHAIN: its simulation and rig from the nearest file writing them; parents outside the walk read as they are met.
        running.Begin("the chains followed", paths.Length);
        var keys = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var chained = new ConcurrentDictionary<string, (Dictionary<string, string> Simulation, string Rig)>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(Partitioner.Create(0, Math.Max(1, paths.Length), Run), range =>
        {
            int done = 0;
            for (int at = range.Item1; at < Math.Min(range.Item2, paths.Length) && !running.Stopped; at++, done++)
            {
                if (!owns.ContainsKey(paths[at]))
                {
                    continue;
                }

                var simulation = new Dictionary<string, string>(StringComparer.Ordinal);
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                string rig = string.Empty;
                foreach (Own one in Chain(read, owns, paths[at]))
                {
                    foreach ((string key, string value) in one.Controller)
                    {
                        seenKeys.Add(key);
                        if (key == "skeleton")
                        {
                            rig = rig.Length > 0 ? rig : value;
                        }
                        else if (!NotSimulation.Contains(key))
                        {
                            simulation.TryAdd(key, Normal(value));
                        }
                    }
                }

                foreach (string key in seenKeys)
                {
                    keys.AddOrUpdate(key, 1, (_, had) => had + 1);
                }

                if (simulation.Count > 0)
                {
                    chained[paths[at]] = (simulation, rig);
                }
            }

            running.Did(done);
        });

        // THE RIGS OF THE SIMULATED FILES, each read once.
        string[] rigs = [.. chained.Values.Select(one => one.Rig).Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        int cut = Math.Max(0, rigs.Length - MostRigs);
        rigs = rigs[..Math.Min(rigs.Length, MostRigs)];
        running.Begin("the rigs read", rigs.Length);
        var shapes = new ConcurrentDictionary<string, RigShape>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(Partitioner.Create(0, Math.Max(1, rigs.Length), 16), range =>
        {
            int done = 0;
            for (int at = range.Item1; at < Math.Min(range.Item2, rigs.Length) && !running.Stopped; at++, done++)
            {
                shapes[rigs[at]] = Shape(AnimationSkeleton.Read(read(rigs[at])));
            }

            running.Did(done);
        });

        // HOW MUCH EACH IS USED: the tables' rows naming it, the .ao files attaching it, and the rows reaching it through attachments.
        Dictionary<string, int> items = Named(itemRows), monsters = Named(monsterRows);
        var attachedBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, Own own) in owns)
        {
            foreach (string hung in own.Hangs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!attachedBy.TryGetValue(hung, out List<string>? by))
                {
                    attachedBy[hung] = by = [];
                }

                by.Add(path);
            }
        }

        var simulated = new List<Simulated>(chained.Count);
        foreach ((string path, (Dictionary<string, string> simulation, string rig)) in chained)
        {
            (int reachedItems, int reachedMonsters) = Reached(path, items, monsters, attachedBy);
            RigShape shape = rig.Length > 0 && shapes.TryGetValue(rig, out RigShape found) ? found : default;
            simulated.Add(new Simulated(
                path, SetOf(simulation), rig, shape, items.GetValueOrDefault(path), monsters.GetValueOrDefault(path),
                attachedBy.TryGetValue(path, out List<string>? by) ? by.Count : 0, reachedItems, reachedMonsters));
        }

        clock.Stop();
        data = Data(simulated);
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"{paths.Length:N0} .ao files read ({unread:N0} not) in {clock.Elapsed.TotalSeconds:0.0} s ({readTook.TotalSeconds:0.0} reading): {simulated.Count:N0} carry a simulation in {simulated.Select(one => one.Set).Distinct(StringComparer.Ordinal).Count():N0} sets; {shapes.Count:N0} rigs read{(cut > 0 ? $", {cut:N0} past {MostRigs:N0} not" : string.Empty)}{(running.Stopped ? " - STOPPED" : string.Empty)}");
        List<string> lines = Report(head, tables, simulated, keys);
        return lines;
    }

    /// <summary>
    /// The part's text from its simulated files - the capture's and the replay's alike: the sets the most used first, each with its keys,
    /// its rigs' simulated bones, its folders and its files; the files used most; and every controller key the chains carry.
    /// </summary>
    internal static List<string> Report(string head, string tables, IReadOnlyList<Simulated> simulated, IReadOnlyDictionary<string, int>? keys)
    {
        var lines = new List<string> { head, string.Empty, "  the tables: " + tables };
        Simulated[][] sets = [.. simulated.GroupBy(one => one.Set, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(one => one.Used).ThenByDescending(one => one.Attached).ThenBy(one => one.Path, StringComparer.OrdinalIgnoreCase).ToArray())
            .OrderByDescending(group => group.Sum(one => one.Used)).ThenByDescending(group => group.Length).ThenBy(group => group[0].Set, StringComparer.Ordinal)];
        string measured = SetOf(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["linear_allowed_contraction"] = "0", ["bend_stiffness"] = "800", ["bend_allowed_angle"] = "0",
            ["normal_viscosity_multiplier"] = "0", ["tangent_viscosity_multiplier"] = "0", ["enclosure_radius"] = "5",
            ["enclosure_angle"] = "5", ["animation_velocity_factor"] = "2", ["animation_force_factor"] = "2", ["use_shock_propagation"] = "false",
        });

        lines.Add(string.Empty);
        lines.Add($"=== the simulation sets, the most used first - used: the rows of ItemVisualIdentity and MonsterVarieties reaching a file, by its name or through up to {MostUp} attachments; each set's keys, its rigs' simulated bones (rods: a phys_ bone on a bone not simulated with none below, as the swung lamp; chains: phys_ bones on phys_ bones), its folders and its files the most used first");
        for (var at = 0; at < sets.Length; at++)
        {
            Simulated[] set = sets[at];
            string rods = Kinds(set);
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"--- set {at + 1}{(string.Equals(set[0].Set, measured, StringComparison.Ordinal) ? " - SWUNG (BoneSwing, v0.1.416)" : string.Empty)}: {set.Length:N0} files, {set.Select(one => one.Rig).Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} rigs; used {set.Sum(one => one.Used):N0} (items {set.Sum(one => one.ReachedItems):N0}, monsters {set.Sum(one => one.ReachedMonsters):N0}; named directly {set.Sum(one => one.Items):N0} and {set.Sum(one => one.Monsters):N0}, attached by {set.Sum(one => one.Attached):N0} .ao lines)"));
            lines.Add("  keys: " + set[0].Set.Replace(";", ", ", StringComparison.Ordinal));
            lines.Add("  rigs: " + rods);
            lines.Add("  folders: " + string.Join(", ", set.GroupBy(one => FolderOf(one.Path), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase).Take(6)
                .Select(group => $"{group.Key} {group.Count()}")));
            foreach (Simulated one in set.Take(8))
            {
                lines.Add($"    {one.Path}  used {one.Used} (items {one.ReachedItems}, monsters {one.ReachedMonsters}, attached by {one.Attached})  {one.Shape.Said}");
            }

            if (set.Length > 8)
            {
                lines.Add($"    ... and {set.Length - 8:N0} more");
            }
        }

        lines.Add(string.Empty);
        lines.Add("=== the 150 simulated .ao files used most - its set's number above, its rig's simulated bones, how it is used");
        Dictionary<string, int> number = sets.Select((set, at) => (set[0].Set, at + 1)).ToDictionary(one => one.Item1, one => one.Item2, StringComparer.Ordinal);
        foreach (Simulated one in simulated.OrderByDescending(one => one.Used).ThenByDescending(one => one.Attached).ThenBy(one => one.Path, StringComparer.OrdinalIgnoreCase).Take(150))
        {
            lines.Add($"  set {number[one.Set]}\t{one.Path}\tused {one.Used} (items {one.ReachedItems}, monsters {one.ReachedMonsters}, attached by {one.Attached})\t{one.Shape.Said}");
        }

        if (keys is { Count: > 0 })
        {
            lines.Add(string.Empty);
            lines.Add("=== every key the ClientAnimationController blocks write, by how many files' chains carry it");
            foreach ((string key, int count) in keys.OrderByDescending(one => one.Value).ThenBy(one => one.Key, StringComparer.Ordinal))
            {
                lines.Add($"  {key}\t{count:N0}{(NotSimulation.Contains(key) ? "\t(not a simulation key)" : string.Empty)}");
            }
        }

        return lines;
    }

    /// <summary>
    /// The replay: the part's text worked out again from its data file - how the sets rank, offline (CaptureReplayTests).
    /// </summary>
    public static List<string> Replay(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var simulated = new List<Simulated>();
        foreach (string line in data.Split('\n'))
        {
            string[] parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length < 15 || line.StartsWith('#'))
            {
                continue;
            }

            int I(int at) => int.Parse(parts[at], CultureInfo.InvariantCulture);
            simulated.Add(new Simulated(
                parts[0], parts[1], parts[2], new RigShape(parts[3] == "1", I(4), I(5), I(6), I(7), I(8), I(9)), I(10), I(11), I(12), I(13), I(14)));
        }

        return Report($"{simulated.Count:N0} simulated .ao files worked out again from {DataFile}", "(not in the data file)", simulated, null);
    }

    /// <summary>Each row's string values, from a table opened with its layout: every string column and string list.</summary>
    public static List<IReadOnlyList<string>> Rows(DatFile table, IReadOnlyList<DatColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);
        var rows = new List<IReadOnlyList<string>>(table.Rows);
        DatColumn[] strings = [.. columns.Where(one => one.Type == "string")];
        for (var row = 0; row < table.Rows; row++)
        {
            var values = new List<string>();
            foreach (DatColumn column in strings)
            {
                if (!column.Array)
                {
                    if (table.Text(row, column.Offset) is { Length: > 0 } one)
                    {
                        values.Add(one);
                    }

                    continue;
                }

                foreach (DatReference element in table.References(row, column.Offset, 8))
                {
                    if (table.TextAt(element.First) is { Length: > 0 } one)
                    {
                        values.Add(one);
                    }
                }
            }

            rows.Add(values);
        }

        return rows;
    }

    /// <summary>A file read and parsed into what the survey keeps of it, or null where it did not read.</summary>
    private static Own? Parsed(Func<string, byte[]?> read, string path)
    {
        if (read(path) is not { Length: > 0 } content)
        {
            return null;
        }

        AnimatedObject ao = AnimatedObject.Read(content);
        if (!ao.Ready)
        {
            return null;
        }

        var controller = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (AoStruct block in ao.Named("ClientAnimationController"))
        {
            foreach (AoEntry entry in block.Entries)
            {
                controller.TryAdd(entry.Key, entry.Value.Trim());
            }
        }

        var hangs = new List<string>();
        foreach ((AoStruct _, AoEntry entry) in ao.Entries())
        {
            if (Array.IndexOf(Hangs, entry.Key) < 0)
            {
                continue;
            }

            // "<socket> <path>[ alias <name>]" - the path is the word ending in .ao.
            foreach (string word in entry.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.EndsWith(AnimatedObject.Suffix, StringComparison.OrdinalIgnoreCase))
                {
                    hangs.Add(word.Replace('\\', '/'));
                }
            }
        }

        return new Own([.. ao.Extends.Where(one => one.Length > 0 && !string.Equals(one, "nothing", StringComparison.OrdinalIgnoreCase))], controller, hangs);
    }

    /// <summary>A file and what it extends, nearest first, up to the model walk's hops; parents outside the walk read and kept as they are met.</summary>
    private static IEnumerable<Own> Chain(Func<string, byte[]?> read, ConcurrentDictionary<string, Own> owns, string path)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
        var queue = new Queue<string>();
        queue.Enqueue(path);
        int hops = 0;
        while (queue.Count > 0 && hops++ < 8)
        {
            string one = queue.Dequeue();
            if (!owns.TryGetValue(one, out Own? own))
            {
                string with = one.EndsWith(AnimatedObject.Suffix, StringComparison.OrdinalIgnoreCase) ? one : one + AnimatedObject.Suffix;
                own = owns.TryGetValue(with, out Own? had) ? had : Parsed(read, with) ?? Parsed(read, one);
                if (own is null)
                {
                    continue;
                }

                owns.TryAdd(with, own);
            }

            yield return own;
            foreach (string up in own.Extends)
            {
                if (seen.Add(up))
                {
                    queue.Enqueue(up);
                }
            }
        }
    }

    /// <summary>A value as the sets compare it: a number in one spelling ("0.000" and "0" alike), a word in lower case.</summary>
    private static string Normal(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number.ToString("R", CultureInfo.InvariantCulture)
            : value.ToLowerInvariant();

    /// <summary>A simulation as one string, its keys in order: what a set is.</summary>
    private static string SetOf(IReadOnlyDictionary<string, string> simulation)
        => string.Join(";", simulation.OrderBy(one => one.Key, StringComparer.Ordinal).Select(one => $"{one.Key}={one.Value}"));

    /// <summary>What a rig simulates.</summary>
    internal static RigShape Shape(AnimationSkeleton rig)
    {
        if (!rig.Ready || SkeletonPose.Of(rig) is not { } pose)
        {
            return default;
        }

        int count = rig.Bones.Count;
        var physics = new bool[count];
        for (var bone = 0; bone < count; bone++)
        {
            physics[bone] = rig.Bones[bone].Name.StartsWith(CaptureMonsterFiles.PhysicsBone, StringComparison.OrdinalIgnoreCase);
        }

        // EACH SIMULATED BONE'S DEPTH among simulated bones, and whether one hangs below it.
        var depth = new int[count];
        var below = new bool[count];
        foreach (int bone in Enumerable.Range(0, count).OrderBy(one => Depth(pose.Parents, one)))
        {
            int parent = pose.Parents[bone];
            if (!physics[bone])
            {
                continue;
            }

            depth[bone] = parent >= 0 && physics[parent] ? depth[parent] + 1 : 1;
            if (parent >= 0 && physics[parent])
            {
                below[parent] = true;
            }
        }

        int simulated = 0, rods = 0, chains = 0, longest = 0, skinned = 0;
        for (var bone = 0; bone < count; bone++)
        {
            if (!physics[bone])
            {
                continue;
            }

            simulated++;
            longest = Math.Max(longest, depth[bone]);
            skinned += rig.Bones[bone].Name.Contains("skinned", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            int parent = pose.Parents[bone];
            bool top = parent < 0 || !physics[parent];
            if (top && !below[bone])
            {
                rods++;
            }
            else if (top)
            {
                chains++;
            }
        }

        return new RigShape(true, count, simulated, rods, chains, longest, skinned);
    }

    private static int Depth(IReadOnlyList<int> parents, int bone)
    {
        int depth = 0;
        for (int up = parents[bone]; up >= 0 && depth < parents.Count; up = parents[up])
        {
            depth++;
        }

        return depth;
    }

    /// <summary>How many rows name each .ao, each row once.</summary>
    private static Dictionary<string, int> Named(IReadOnlyList<IReadOnlyList<string>>? rows)
    {
        var named = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (IReadOnlyList<string> row in rows ?? [])
        {
            foreach (string value in row.Where(one => one.EndsWith(AnimatedObject.Suffix, StringComparison.OrdinalIgnoreCase))
                .Select(one => one.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                named[value] = named.GetValueOrDefault(value) + 1;
            }
        }

        return named;
    }

    /// <summary>The rows reaching a file: those naming it, and those naming a file attaching it, up to <see cref="MostUp"/> attachments up.</summary>
    private static (int Items, int Monsters) Reached(
        string path, Dictionary<string, int> items, Dictionary<string, int> monsters, Dictionary<string, List<string>> attachedBy)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
        var level = new List<string> { path };
        int reachedItems = 0, reachedMonsters = 0;
        for (var up = 0; up <= MostUp && level.Count > 0; up++)
        {
            var next = new List<string>();
            foreach (string one in level)
            {
                reachedItems += items.GetValueOrDefault(one);
                reachedMonsters += monsters.GetValueOrDefault(one);
                if (attachedBy.TryGetValue(one, out List<string>? by))
                {
                    next.AddRange(by.Where(seen.Add));
                }
            }

            level = next;
        }

        return (reachedItems, reachedMonsters);
    }

    /// <summary>A set's rigs by what they simulate.</summary>
    private static string Kinds(IReadOnlyList<Simulated> set)
    {
        int unread = 0, none = 0, rodsOnly = 0, chained = 0;
        int rods = 0, chains = 0, longest = 0, skinned = 0;
        foreach (Simulated one in set)
        {
            RigShape shape = one.Shape;
            if (!shape.Read)
            {
                unread++;
                continue;
            }

            rods += shape.Rods;
            chains += shape.Chains;
            longest = Math.Max(longest, shape.Longest);
            skinned += shape.Skinned;
            if (shape.Physics == 0)
            {
                none++;
            }
            else if (shape.Chains == 0)
            {
                rodsOnly++;
            }
            else
            {
                chained++;
            }
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"files whose rig hangs rods alone {rodsOnly:N0}, with chains {chained:N0}, simulating nothing {none:N0}, not read {unread:N0}; {rods:N0} rods and {chains:N0} chains in all, the longest {longest}, {skinned:N0} skinned simulated bones");
    }

    /// <summary>A file's folder for the tally: its path's first four parts.</summary>
    private static string FolderOf(string path)
    {
        string[] parts = path.Split('/');
        return string.Join('/', parts.Take(Math.Min(4, parts.Length - 1)));
    }

    /// <summary>The data file: a line a simulated .ao.</summary>
    private static string Data(IEnumerable<Simulated> simulated)
    {
        var text = new StringBuilder("# path\tset\trig\trig read\tbones\tsimulated\trods\tchains\tlongest\tskinned\titems\tmonsters\tattached by\treached items\treached monsters\n");
        foreach (Simulated one in simulated.OrderBy(one => one.Path, StringComparer.OrdinalIgnoreCase))
        {
            RigShape shape = one.Shape;
            text.Append(CultureInfo.InvariantCulture,
                $"{one.Path}\t{one.Set}\t{one.Rig}\t{(shape.Read ? 1 : 0)}\t{shape.Bones}\t{shape.Physics}\t{shape.Rods}\t{shape.Chains}\t{shape.Longest}\t{shape.Skinned}\t{one.Items}\t{one.Monsters}\t{one.Attached}\t{one.ReachedItems}\t{one.ReachedMonsters}\n");
        }

        return text.ToString();
    }
}
