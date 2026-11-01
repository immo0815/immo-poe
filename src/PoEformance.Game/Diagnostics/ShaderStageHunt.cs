using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PoEformance.Core.Memory;
using PoEformance.Game.Files;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// The engine's shader stages measured for the capture (shader-stages.txt): the order the client runs them in, read off its own
/// table in the running program, and every link of the install's graphs from a node of one stage to a node of another - the
/// vertex shader's values read by the pixel shader among them.
/// </summary>
/// <remarks>
/// WHY (v0.1.365): The Red Vale's exit to the Grelwood drew its light as a solid white tube. Its material, light_raysc.mat, masks
/// itself in AddMaskMuddleTex by a texture read at <c>InputVertexUV</c> taken at VertexInit, a node the compile does not know, so
/// the whole alpha chain - the mask, Alpha_SmoothStep, SoftAlphaParticle, FresnelFade - was lost and the tube added at alpha one;
/// and its ScaleMoveUV scrolls the colour by writing <c>VertexUV</c> at WorldTransform, a vertex stage the compile does not run.
/// The stage sweep (v0.1.348) counts the node missing in 1,846 rooms. The engine's own graphs say how the coordinates go -
/// fixed.fxgraph writes VertexUV from the mesh's (VertexLocalUV) at VertexInit and the pixel side's UV from the vertex side's
/// last (FromVertexUV) at PixelInit, and the trails link a vertex stage's input straight to a pixel stage's writer
/// (InputVertexTrailPhase@VertexInit to TrailPhase@VertexToPixel) - but which vertex stage runs before which is in no file read:
/// the links of the 335 graphs earlier captures hold mostly follow the names' order and in places run against it (face_camera.fxgraph reads at
/// WorldTransform and writes at WorldTransform_Init). DECIDED from the live client: measure before compiling any of it.
///
/// THE TABLE (the schema's "THE ORDER OF A MATERIAL'S SHADER STAGES IS A TABLE IN THE CLIENT", 2026-10-09): an array of MSVC
/// std::string in the client's image, 0x20 an entry - sixteen bytes of text or a pointer to it, the size, the capacity (fifteen
/// for a name kept inline, one less than a multiple of sixteen once on the heap) - of which ten entries were seen, UVSetup to
/// PreLighting_Final; the vertex side's were not. So every eight-byte place of the image is read as one, the heap's names read,
/// and runs of them 0x20 to 0x60 apart kept where they hold two stages the graphs name or more - up to 16 runs of 512 entries written,
/// its entries no graph names among them, and the best taken as the table where it holds three, written whole on its own. The two decoys the schema names, the literal pool and the graphs' own JSON, are no std::string.
///
/// THE LINKS: a node naming a stage fed, through nodes naming none, by one naming another; counted by graph, by stage pair - held
/// against the table's order - and by the two ends' types, the ones from a vertex stage to a pixel stage apart, and every write
/// of the coordinates with what feeds it. The graphs are the install's, every one the startup walk found under Metadata/.
/// </remarks>
public static class ShaderStageHunt
{
    /// <summary>How much of the image one read copies.</summary>
    private const int ChunkBytes = 4 * 1024 * 1024;

    /// <summary>The smallest read a failed chunk is split down to - a page.</summary>
    private const int PageBytes = 4096;

    /// <summary>The longest string an entry is taken to hold - a stage's name is under thirty letters.</summary>
    private const int LongestText = 255;

    /// <summary>The largest capacity an entry is taken to have.</summary>
    private const ulong LargestCapacity = 0xFFFF;

    /// <summary>How far apart a run's entries may stand: a bare std::string, or one leading a record of up to 0x60 bytes.</summary>
    private static readonly int[] Strides = [0x20, 0x28, 0x30, 0x38, 0x40, 0x48, 0x50, 0x58, 0x60];

    /// <summary>The fewest entries a run is kept with.</summary>
    private const int FewestEntries = 3;

    /// <summary>The fewest stage names a run is kept with.</summary>
    private const int FewestStages = 2;

    /// <summary>The fewest stage names the best run is taken as the table with - the order every later section reads.</summary>
    private const int FewestTableStages = 3;

    /// <summary>Most runs written, and most entries a run.</summary>
    private const int MostRuns = 16;

    /// <summary>Most entries a run writes.</summary>
    private const int MostEntries = 512;

    /// <summary>Most lines a tally writes; the rest are counted.</summary>
    private const int MostLines = 400;

    /// <summary>Graphs named as examples on a tally's line.</summary>
    private const int Examples = 3;

    /// <summary>Where the engine's own graphs are - written first among the examples.</summary>
    private const string EngineFolder = "Metadata/EngineGraphs/";

    /// <summary>The first stage of the pixel side in the engine's own graphs: the trails write their pixel values there.</summary>
    private const string FirstPixelStage = "VertexToPixel";

    /// <summary>
    /// The vertex side's stages by name, where the table is not found: the vertex extension points' default stages
    /// (inputoutput.ffx: LocalTransform, WorldTransform, ProjectionTransform) and the stages the engine's graphs use beside them.
    /// </summary>
    private static readonly string[] VertexFamilies = ["VertexInit", "LocalTransform", "Animation", "WorldTransform", "ProjectionTransform", "VertexOutput"];

    /// <summary>The writers of the coordinates, whose feeders are written apart.</summary>
    private static readonly HashSet<string> CoordinateWriters = new(StringComparer.Ordinal) { "VertexUV", "VertexUV2", "UV", "UV2" };

    /// <summary>A capture's progress and its stop.</summary>
    public sealed class Running
    {
        private int _phase;
        private int _done;
        private int _of;
        private int _stop;

        /// <summary>Whether it was asked to stop.</summary>
        public bool Stopping => Volatile.Read(ref _stop) != 0;

        /// <summary>Asks it to stop, keeping what it found.</summary>
        public void Stop() => Volatile.Write(ref _stop, 1);

        /// <summary>The window's line: where it stands.</summary>
        public string Line => Volatile.Read(ref _phase) switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"reading the install's graphs: {Volatile.Read(ref _done)} of {Volatile.Read(ref _of)}"),
            1 => "copying the program's image",
            2 => "reading the image as std::strings",
            3 => string.Create(CultureInfo.InvariantCulture, $"reading their names on the heap: {Volatile.Read(ref _done)} of {Volatile.Read(ref _of)}"),
            _ => "writing",
        };

        /// <summary>How far it is.</summary>
        public float Fraction
        {
            get
            {
                int of = Volatile.Read(ref _of);
                float share = of > 0 ? Math.Clamp((float)Volatile.Read(ref _done) / of, 0f, 1f) : 0f;
                return Volatile.Read(ref _phase) switch
                {
                    0 => 0.6f * share,
                    1 => 0.6f,
                    2 => 0.7f,
                    3 => 0.8f + (0.15f * share),
                    _ => 0.95f,
                };
            }
        }

        /// <summary>A phase begun over so many steps.</summary>
        internal void Phase(int phase, int of = 0)
        {
            Volatile.Write(ref _done, 0);
            Volatile.Write(ref _of, of);
            Volatile.Write(ref _phase, phase);
        }

        /// <summary>One step of the phase done.</summary>
        internal void Did() => Interlocked.Increment(ref _done);
    }

    /// <summary>One string of the image: where it stands, its text, and how it is kept.</summary>
    internal readonly record struct Entry(int Rva, string Text, bool Inline, long Size, long Capacity);

    /// <summary>A run of strings evenly apart.</summary>
    internal sealed record StringRun(int Stride, IReadOnlyList<Entry> Entries, int Stages);

    /// <summary>What one graph holds of the stages.</summary>
    internal sealed record Facts(
        string Path,
        Dictionary<string, int> Stages,
        HashSet<(string Source, string From, string Target, string To)> Links,
        HashSet<(string Writer, string At, string Feeders, string Via)> Coordinates);

    /// <summary>
    /// The measurement: the install's graphs, then the table in the program's image. Returns the part's lines, the head first.
    /// </summary>
    /// <param name="reader">The game's memory, past the capture's tap.</param>
    /// <param name="read">How to get a file out of the install, or null without one.</param>
    /// <param name="graphs">The install's graph paths, from the startup walk.</param>
    /// <param name="running">Its progress and its stop.</param>
    public static IReadOnlyList<string> Run(IMemoryReader reader, Func<string, byte[]?>? read, IReadOnlyList<string>? graphs, Running running)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(running);
        var clock = Stopwatch.StartNew();
        var body = new List<string>();

        // THE GRAPHS FIRST: their stage names are what the table is known by.
        (List<Facts> facts, string graphsSaid) = Census(read, graphs, running);
        Dictionary<string, (long Nodes, int Graphs)> stages = Stages(facts);
        var names = new HashSet<string>(stages.Keys, StringComparer.Ordinal);
        names.UnionWith(ShadeProgram.Stages);

        (List<StringRun> runs, string imageSaid) = Table(reader, names, running);
        running.Phase(4);
        StringRun? table = runs.Count > 0 && runs[0].Stages >= FewestTableStages ? runs[0] : null;
        IReadOnlyList<string> order = table is null ? [] : [.. table.Entries.Select(entry => entry.Text)];

        body.Add("=== the engine's stage table: the program's image read as MSVC std::string (0x20 bytes: the text inline at a capacity of fifteen, "
            + "else a pointer to it; the size at +0x10, the capacity at +0x18), runs of them 0x20 to 0x60 apart holding two stages the graphs or the compile name or more");
        body.Add(imageSaid);
        Runs(runs, stages, body);

        // THE ORDER EVERY SECTION BELOW READS, whole and apart from the runs' own lines (which stop at MostEntries): the best run, taken only
        // where it holds FewestTableStages stages - what the replay reads back, so it cannot take a run the report did not.
        body.Add(string.Empty);
        body.Add(table is null
            ? string.Create(CultureInfo.InvariantCulture, $"=== the table taken: none - no run holds {FewestTableStages} stages the graphs or the compile name")
            : string.Create(CultureInfo.InvariantCulture, $"=== the table taken: run 1, {order.Count} entries, the order every section below reads"));
        body.AddRange(order.Select((stage, at) => string.Create(CultureInfo.InvariantCulture, $"  table\t{at}\t{stage}")));

        body.Add(string.Empty);
        body.Add("=== this build's compile order (ShadeProgram.Stages) against the table");
        Compile(order, body);

        body.Add(string.Empty);
        body.Add("=== the stages the install's graphs use, by how many graphs, and where the table puts them");
        foreach ((string stage, (long nodes, int count)) in stages.OrderByDescending(one => one.Value.Graphs).ThenBy(one => one.Key, StringComparer.Ordinal))
        {
            int at = IndexIn(order, stage);
            body.Add(string.Create(CultureInfo.InvariantCulture, $"  stage\t{stage}\tgraphs {count}\tnodes {nodes}\t{(at >= 0 ? $"table {at}" : "not in the table")}"));
        }

        int crossing = Links(facts, order, body);
        Coordinates(facts, body);
        body.Add(string.Empty);
        body.Add(graphsSaid);

        string found = table is null
            ? "no table found"
            : string.Create(CultureInfo.InvariantCulture,
                $"the table at module+0x{table.Entries[0].Rva:X}, {table.Entries.Count} entries, holds {stages.Keys.Count(stage => IndexIn(order, stage) >= 0)} of the {stages.Count} stages the graphs use");
        string head = string.Create(CultureInfo.InvariantCulture,
            $"shader stages: {found}; {facts.Count} graphs read, {crossing} links from a vertex stage to a pixel stage{(running.Stopping ? " - STOPPED" : string.Empty)}; {clock.Elapsed.TotalSeconds:0.0} s");
        return [head, string.Empty, .. body];
    }

    /// <summary>Every graph read and its stages and links taken, on every core.</summary>
    private static (List<Facts> Facts, string Said) Census(Func<string, byte[]?>? read, IReadOnlyList<string>? graphs, Running running)
    {
        if (read is null)
        {
            return ([], "graphs: none read - no install");
        }

        string[] paths = [.. (graphs ?? [])
            .Select(path => path.Replace('\\', '/'))
            .Where(path => path.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (paths.Length == 0)
        {
            return ([], "graphs: none read - the install walk found none, or had not run when the key went down");
        }

        running.Phase(0, paths.Length);
        var clock = Stopwatch.StartNew();
        var found = new ConcurrentBag<Facts>();
        int missing = 0, unread = 0;
        Parallel.ForEach(paths, (path, state) =>
        {
            if (running.Stopping)
            {
                state.Stop();
                return;
            }

            if (read(path) is not { Length: > 0 } bytes)
            {
                Interlocked.Increment(ref missing);
            }
            else if (Of(path, bytes) is { } facts)
            {
                found.Add(facts);
            }
            else
            {
                Interlocked.Increment(ref unread);
            }

            running.Did();
        });

        // ENGINE GRAPHS FIRST, then by path, so a line's examples are the same every capture.
        List<Facts> ordered = [.. found.OrderBy(one => one.Path.StartsWith(EngineFolder, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(one => one.Path, StringComparer.OrdinalIgnoreCase)];
        int engine = ordered.Count(one => one.Path.StartsWith(EngineFolder, StringComparison.OrdinalIgnoreCase));
        return (ordered, string.Create(CultureInfo.InvariantCulture,
            $"graphs: {ordered.Count} of the walk's {paths.Length} read ({engine} of them the engine's own, under {EngineFolder}), {missing} not in the install, {unread} with no node and no link (a graph that only sets a blend mode or a draw order, or no graph at all){(running.Stopping ? ", stopped before the rest" : string.Empty)}; {clock.Elapsed.TotalSeconds:0.0} s"));
    }

    /// <summary>One graph's stages, its links across stages and its writes of the coordinates; null for a file that is no graph.</summary>
    internal static Facts? Of(string path, byte[] bytes)
    {
        ShaderGraph graph = ShaderGraph.Read(bytes);
        if (!graph.Ready)
        {
            return null;
        }

        IReadOnlyList<ShaderNode> nodes = graph.Nodes;

        // A LINK'S END NAMES ITS NODE'S STAGE, and one type and index may stand at two stages (trail_default's InputVertexTrailPhase
        // at VertexInit and at VertexOutput_Final), so a node is its type, index and stage - by type and index alone only where that
        // is one node, as ShadeProgram's lookup falls back.
        var exact = new Dictionary<(string, int, string), int>();
        var loose = new Dictionary<(string, int), int>();
        var stages = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var at = 0; at < nodes.Count; at++)
        {
            ShaderNode node = nodes[at];
            exact.TryAdd((node.Type, node.Index, node.Stage), at);
            loose[(node.Type, node.Index)] = loose.ContainsKey((node.Type, node.Index)) ? -1 : at;
            if (node.Stage.Length > 0)
            {
                stages[node.Stage] = stages.GetValueOrDefault(node.Stage) + 1;
            }
        }

        int Resolve(ShaderEnd end) => exact.TryGetValue((end.Type, end.Index, end.Stage), out int at)
            ? at
            : loose.TryGetValue((end.Type, end.Index), out at) ? at : -1;

        var into = new List<int>?[nodes.Count];
        foreach (ShaderLink link in graph.Links)
        {
            int source = Resolve(link.Source);
            int target = Resolve(link.Target);
            if (source >= 0 && target >= 0 && source != target)
            {
                (into[target] ??= []).Add(source);
            }
        }

        var links = new HashSet<(string, string, string, string)>();
        var coordinates = new HashSet<(string, string, string, string)>();
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        var feeders = new SortedSet<string>(StringComparer.Ordinal);
        for (var target = 0; target < nodes.Count; target++)
        {
            ShaderNode end = nodes[target];
            if (end.Stage.Length == 0 || into[target] is not { } direct)
            {
                continue;
            }

            seen.Clear();
            feeders.Clear();
            foreach (int one in direct)
            {
                stack.Push(one);
            }

            // BACK THROUGH THE NODES NAMING NO STAGE to those naming one: a value is taken at its own node's stage.
            while (stack.Count > 0)
            {
                int at = stack.Pop();
                if (!seen.Add(at))
                {
                    continue;
                }

                ShaderNode node = nodes[at];
                if (node.Stage.Length > 0)
                {
                    feeders.Add(node.Type + "@" + node.Stage);
                    if (!string.Equals(node.Stage, end.Stage, StringComparison.Ordinal))
                    {
                        links.Add((node.Type, node.Stage, end.Type, end.Stage));
                    }

                    continue;
                }

                if (into[at] is { Count: > 0 } further)
                {
                    foreach (int one in further)
                    {
                        stack.Push(one);
                    }
                }
                else
                {
                    feeders.Add(node.Type);
                }
            }

            if (CoordinateWriters.Contains(end.Type))
            {
                string via = string.Join(" + ", direct.Select(one => nodes[one].Type).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                coordinates.Add((end.Type, end.Stage, string.Join(" + ", feeders), via));
            }
        }

        return new Facts(path, stages, links, coordinates);
    }

    /// <summary>Every stage the graphs name, with its nodes and the graphs naming it.</summary>
    private static Dictionary<string, (long Nodes, int Graphs)> Stages(List<Facts> facts)
    {
        var stages = new Dictionary<string, (long Nodes, int Graphs)>(StringComparer.Ordinal);
        foreach (Facts one in facts)
        {
            foreach ((string stage, int nodes) in one.Stages)
            {
                (long had, int graphs) = stages.GetValueOrDefault(stage);
                stages[stage] = (had + nodes, graphs + 1);
            }
        }

        return stages;
    }

    /// <summary>The image copied, read as std::strings, and the runs among them that hold stages - most stages first.</summary>
    private static (List<StringRun> Runs, string Said) Table(IMemoryReader reader, HashSet<string> names, Running running)
    {
        if (reader.ModuleBase == 0 || reader.ModuleSize == 0)
        {
            return ([], "image: not read - the program's module has no base or no size (the process not attached, or its main module not readable)");
        }

        if (running.Stopping)
        {
            return ([], "image: not read - stopped before it");
        }

        running.Phase(1);
        var clock = Stopwatch.StartNew();
        var image = new byte[reader.ModuleSize];
        long unread = Copy(reader, image);
        running.Phase(2);
        (List<Entry> entries, int inlined, int onHeap, int heapUnread) = Strings(reader, image, running);
        List<StringRun> runs = Runs(entries, names);
        return (runs, string.Create(CultureInfo.InvariantCulture,
            $"image: module 0x{reader.ModuleBase:X}, 0x{image.Length:X} bytes, 0x{unread:X} of them unreadable; {inlined} std::strings held inline, {onHeap} on the heap ({heapUnread} more whose text did not read); {runs.Count} runs holding stages; {clock.Elapsed.TotalSeconds:0.0} s"));
    }

    /// <summary>The image into <paramref name="image"/>, a chunk at a time and page by page where a chunk will not read; returns the bytes that did not read.</summary>
    internal static long Copy(IMemoryReader reader, byte[] image)
    {
        long unread = 0;
        for (var at = 0; at < image.Length; at += ChunkBytes)
        {
            int length = Math.Min(ChunkBytes, image.Length - at);
            if (reader.TryRead(reader.ModuleBase + (ulong)at, image.AsSpan(at, length)))
            {
                continue;
            }

            // A GUARD PAGE OR A HOLE leaves the chunk unread whole: read its pages one by one, nought where one will not.
            for (int page = at; page < at + length; page += PageBytes)
            {
                Span<byte> slot = image.AsSpan(page, Math.Min(PageBytes, at + length - page));
                if (!reader.TryRead(reader.ModuleBase + (ulong)page, slot))
                {
                    slot.Clear();
                    unread += slot.Length;
                }
            }
        }

        return unread;
    }

    /// <summary>
    /// Every eight-byte place of the image that holds an MSVC std::string: inline where its capacity is fifteen, else its text read
    /// from the heap - on every core.
    /// </summary>
    internal static (List<Entry> Entries, int Inline, int Heap, int HeapUnread) Strings(IMemoryReader reader, byte[] image, Running running)
    {
        var inline = new ConcurrentBag<Entry>();
        var candidates = new ConcurrentBag<(int Rva, ulong Text, long Size, long Capacity)>();
        int slices = (image.Length + ChunkBytes - 1) / ChunkBytes;
        Parallel.For(0, slices, (slice, state) =>
        {
            if (running.Stopping)
            {
                state.Stop();
                return;
            }

            int from = slice * ChunkBytes;
            int to = Math.Min(image.Length - 0x20, from + ChunkBytes - 1);
            for (int at = from; at <= to; at += 8)
            {
                ulong size = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(at + 0x10));
                ulong capacity = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(at + 0x18));

                // MSVC'S CAPACITY IS FIFTEEN INLINE and one less than a multiple of sixteen on the heap (it rounds up by its mask).
                if (size - 1 >= LongestText || capacity < size || capacity > LargestCapacity || (capacity & 0xF) != 0xF)
                {
                    continue;
                }

                if (capacity == 0xF)
                {
                    ReadOnlySpan<byte> text = image.AsSpan(at, (int)size);
                    if (image[at + (int)size] == 0 && Printable(text))
                    {
                        inline.Add(new Entry(at, Encoding.ASCII.GetString(text), true, (long)size, (long)capacity));
                    }

                    continue;
                }

                ulong pointer = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(at));
                if (pointer >= 0x10000 && pointer < 0x8000_0000_0000UL)
                {
                    candidates.Add((at, pointer, (long)size, (long)capacity));
                }
            }
        });

        // THE HEAP'S NAMES, read one by one: the text with the nought MSVC keeps after it.
        (int Rva, ulong Text, long Size, long Capacity)[] heap = [.. candidates];
        running.Phase(3, heap.Length);
        var onHeap = new ConcurrentBag<Entry>();
        var heapUnread = 0;
        Parallel.ForEach(heap, (one, state) =>
        {
            if (running.Stopping)
            {
                state.Stop();
                return;
            }

            Span<byte> text = stackalloc byte[LongestText + 1];
            Span<byte> wanted = text[..((int)one.Size + 1)];
            if (reader.TryRead(one.Text, wanted) && wanted[^1] == 0 && Printable(wanted[..^1]))
            {
                onHeap.Add(new Entry(one.Rva, Encoding.ASCII.GetString(wanted[..^1]), false, one.Size, one.Capacity));
            }
            else
            {
                Interlocked.Increment(ref heapUnread);
            }

            running.Did();
        });

        List<Entry> entries = [.. inline.Concat(onHeap).OrderBy(entry => entry.Rva)];
        return (entries, inline.Count, onHeap.Count, heapUnread);
    }

    /// <summary>Whether every byte is printable ASCII.</summary>
    private static bool Printable(ReadOnlySpan<byte> text)
    {
        foreach (byte one in text)
        {
            if (one is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        return text.Length > 0;
    }

    /// <summary>
    /// The runs of strings evenly apart that hold stages: each stride from the least, a run kept only where half its entries are not in
    /// one kept already (a 0x40 run of every other entry of a 0x20 one); most stages first, then the longest.
    /// </summary>
    internal static List<StringRun> Runs(IReadOnlyList<Entry> entries, IReadOnlySet<string> names)
    {
        var at = new Dictionary<int, Entry>(entries.Count);
        foreach (Entry entry in entries)
        {
            at.TryAdd(entry.Rva, entry);
        }

        var taken = new HashSet<int>();
        var runs = new List<StringRun>();
        foreach (int stride in Strides)
        {
            foreach (Entry first in entries)
            {
                if (at.ContainsKey(first.Rva - stride))
                {
                    continue;
                }

                var members = new List<Entry>();
                for (int rva = first.Rva; at.TryGetValue(rva, out Entry next); rva += stride)
                {
                    members.Add(next);
                }

                int stages = members.Count(member => names.Contains(member.Text));
                // A RUN MOSTLY INSIDE ONE KEPT ALREADY is that one's every other entry and a stray beyond it (v0.1.365's capture: a 0x40 run
                // of the table's and the "GGX" after it) - kept only where half its entries are new.
                if (members.Count < FewestEntries || stages < FewestStages || members.Count(member => !taken.Contains(member.Rva)) * 2 < members.Count)
                {
                    continue;
                }

                runs.Add(new StringRun(stride, members, stages));
                taken.UnionWith(members.Select(member => member.Rva));
            }
        }

        return [.. runs.OrderByDescending(run => run.Stages).ThenByDescending(run => run.Entries.Count).ThenBy(run => run.Entries[0].Rva)];
    }

    /// <summary>The runs written whole, each entry with how many graphs use it as a stage.</summary>
    private static void Runs(List<StringRun> runs, Dictionary<string, (long Nodes, int Graphs)> stages, List<string> body)
    {
        if (runs.Count == 0)
        {
            body.Add("no run of std::strings in the image holds two stages the graphs or the compile name: the table was not found there");
            return;
        }

        for (var number = 0; number < Math.Min(MostRuns, runs.Count); number++)
        {
            StringRun run = runs[number];
            body.Add(string.Create(CultureInfo.InvariantCulture,
                $"run {number + 1}: module+0x{run.Entries[0].Rva:X}, {run.Entries.Count} entries 0x{run.Stride:X} apart, {run.Stages} of them stages the graphs or the compile name"));
            for (var index = 0; index < Math.Min(MostEntries, run.Entries.Count); index++)
            {
                Entry entry = run.Entries[index];
                string used = stages.TryGetValue(entry.Text, out (long Nodes, int Graphs) stage) ? $"graphs {stage.Graphs}" : "no graph's stage";
                body.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  entry\t{number + 1}\t{index}\tmodule+0x{entry.Rva:X}\t{entry.Text}\t{(entry.Inline ? "inline" : "heap")}\t{entry.Size}\t{entry.Capacity}\t{used}"));
            }

            if (run.Entries.Count > MostEntries)
            {
                body.Add(string.Create(CultureInfo.InvariantCulture, $"  ... {run.Entries.Count - MostEntries} entries more"));
            }
        }

        if (runs.Count > MostRuns)
        {
            body.Add(string.Create(CultureInfo.InvariantCulture, $"{runs.Count - MostRuns} runs more, holding fewer stages"));
        }
    }

    /// <summary>The compile's order against the table's: each of its stages' place in the table, and the table's it does not run.</summary>
    private static void Compile(IReadOnlyList<string> order, List<string> body)
    {
        if (order.Count == 0)
        {
            body.Add("no table to hold it against");
            return;
        }

        int last = -1;
        var outOfOrder = new List<string>();
        foreach (string stage in ShadeProgram.Stages)
        {
            int at = IndexIn(order, stage);
            body.Add(string.Create(CultureInfo.InvariantCulture, $"  compile\t{stage}\t{(at >= 0 ? $"table {at}" : "not in the table")}"));
            if (at >= 0)
            {
                if (at < last)
                {
                    outOfOrder.Add(stage);
                }

                last = Math.Max(last, at);
            }
        }

        body.Add(outOfOrder.Count == 0
            ? "the compile's stages run in the table's order"
            : "out of the table's order in the compile: " + string.Join(", ", outOfOrder));
        string[] notRun = [.. order.Where(stage => !ShadeProgram.Stages.Contains(stage, StringComparer.Ordinal))];
        body.Add("the table's entries the compile does not run: " + (notRun.Length == 0 ? "none" : string.Join(", ", notRun)));
    }

    /// <summary>
    /// The links across stages by stage pair - each held against the table's order - and by their ends' types from a vertex stage to a
    /// pixel stage. Returns how many such type pairs there are.
    /// </summary>
    private static int Links(List<Facts> facts, IReadOnlyList<string> order, List<string> body)
    {
        var pairs = new Dictionary<(string From, string To), (int Graphs, string Example)>();
        var crossings = new Dictionary<(string Source, string From, string Target, string To), (int Graphs, List<string> Examples)>();
        foreach (Facts one in facts)
        {
            foreach ((string from, string to) in one.Links.Select(link => (link.From, link.To)).Distinct())
            {
                (int graphs, string? example) = pairs.GetValueOrDefault((from, to));
                pairs[(from, to)] = (graphs + 1, example ?? one.Path);
            }

            foreach ((string source, string from, string target, string to) link in one.Links)
            {
                if (!IsVertex(link.from, order) || IsVertex(link.to, order))
                {
                    continue;
                }

                if (!crossings.TryGetValue(link, out (int Graphs, List<string> Examples) had))
                {
                    had = (0, []);
                }

                if (had.Examples.Count < Examples)
                {
                    had.Examples.Add(one.Path);
                }

                crossings[link] = (had.Graphs + 1, had.Examples);
            }
        }

        body.Add(string.Empty);
        body.Add("=== links from one stage to another: a node naming a stage fed by one naming another, through nodes naming none - by stage pair, "
            + "held against the table's order (forwards: the source's stage runs first)");
        int forwards = 0, backwards = 0, unplaced = 0;
        foreach (((string from, string to), (int graphs, string example)) in pairs.OrderByDescending(one => one.Value.Graphs)
                     .ThenBy(one => one.Key.From, StringComparer.Ordinal).ThenBy(one => one.Key.To, StringComparer.Ordinal))
        {
            string verdict = Verdict(from, to, order);
            switch (verdict)
            {
                case "forwards":
                    forwards++;
                    break;
                case "backwards":
                    backwards++;
                    break;
                default:
                    unplaced++;
                    break;
            }

            body.Add(string.Create(CultureInfo.InvariantCulture, $"  pair\t{from}\t{to}\tgraphs {graphs}\t{verdict}\te.g. {example}"));
        }

        body.Add(string.Create(CultureInfo.InvariantCulture,
            $"stage pairs: {pairs.Count} - {forwards} forwards, {backwards} backwards, {unplaced} with a stage the table does not hold"));

        body.Add(string.Empty);
        string side = IndexIn(order, FirstPixelStage) >= 0
            ? $"the vertex side the table's stages before {FirstPixelStage}, a stage it does not hold by name"
            : $"the vertex side by name, {(order.Count > 0 ? $"the table holding no {FirstPixelStage}" : "the table not found")} ({string.Join(", ", VertexFamilies)}...)";
        body.Add($"=== from a vertex stage to a pixel stage: {side} - by the ends' types, the most graphs first");
        var written = 0;
        foreach (((string source, string from, string target, string to), (int graphs, List<string> examples)) in crossings
                     .OrderByDescending(one => one.Value.Graphs).ThenBy(one => one.Key.Source, StringComparer.Ordinal)
                     .ThenBy(one => one.Key.From, StringComparer.Ordinal).ThenBy(one => one.Key.Target, StringComparer.Ordinal))
        {
            if (++written > MostLines)
            {
                break;
            }

            body.Add(string.Create(CultureInfo.InvariantCulture,
                $"  cross\t{source}@{from}\t{target}@{to}\tgraphs {graphs}\t{string.Join("; ", examples)}"));
        }

        if (crossings.Count > MostLines)
        {
            body.Add(string.Create(CultureInfo.InvariantCulture, $"  ... {crossings.Count - MostLines} more"));
        }

        return crossings.Count;
    }

    /// <summary>Every write of the coordinates, by its stage and what feeds it, the most graphs first.</summary>
    private static void Coordinates(List<Facts> facts, List<string> body)
    {
        var writes = new Dictionary<(string Writer, string At, string Feeders, string Via), (int Graphs, List<string> Examples)>();
        foreach (Facts one in facts)
        {
            foreach ((string writer, string at, string feeders, string via) write in one.Coordinates)
            {
                if (!writes.TryGetValue(write, out (int Graphs, List<string> Examples) had))
                {
                    had = (0, []);
                }

                if (had.Examples.Count < Examples)
                {
                    had.Examples.Add(one.Path);
                }

                writes[write] = (had.Graphs + 1, had.Examples);
            }
        }

        body.Add(string.Empty);
        body.Add("=== the coordinates' path: every write of VertexUV, VertexUV2, UV and UV2 by its stage and what feeds it "
            + "(a node with a stage as type@stage, one with none and nothing feeding it by its type), and via the nodes linked to it - the most graphs first");
        var written = 0;
        foreach (((string writer, string at, string feeders, string via), (int graphs, List<string> examples)) in writes
                     .OrderByDescending(one => one.Value.Graphs).ThenBy(one => one.Key.Writer, StringComparer.Ordinal)
                     .ThenBy(one => one.Key.At, StringComparer.Ordinal).ThenBy(one => one.Key.Feeders, StringComparer.Ordinal)
                     .ThenBy(one => one.Key.Via, StringComparer.Ordinal))
        {
            if (++written > MostLines)
            {
                break;
            }

            body.Add(string.Create(CultureInfo.InvariantCulture,
                $"  coords\t{writer}@{at}\t{(feeders.Length > 0 ? feeders : "nothing")}\tvia {via}\tgraphs {graphs}\t{string.Join("; ", examples)}"));
        }

        if (writes.Count > MostLines)
        {
            body.Add(string.Create(CultureInfo.InvariantCulture, $"  ... {writes.Count - MostLines} more"));
        }
    }

    /// <summary>A stage's place in the table, or -1.</summary>
    private static int IndexIn(IReadOnlyList<string> order, string stage)
    {
        for (var at = 0; at < order.Count; at++)
        {
            if (string.Equals(order[at], stage, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>Whether a link from one stage to another runs with the table's order, against it, or cannot be placed.</summary>
    private static string Verdict(string from, string to, IReadOnlyList<string> order)
    {
        int source = IndexIn(order, from);
        int target = IndexIn(order, to);
        return source < 0 || target < 0 ? "unplaced" : source < target ? "forwards" : "backwards";
    }

    /// <summary>
    /// Whether a stage is the vertex side's: before <see cref="FirstPixelStage"/> in the table where the table holds it, else by its
    /// name's family.
    /// </summary>
    private static bool IsVertex(string stage, IReadOnlyList<string> order)
    {
        int pixel = IndexIn(order, FirstPixelStage);
        if (pixel >= 0)
        {
            int at = IndexIn(order, stage);
            if (at >= 0)
            {
                return at < pixel;
            }
        }

        foreach (string family in VertexFamilies)
        {
            if (stage.StartsWith(family, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A report read back - shader-stages-replay.txt: the table the report took (its <c>table</c> lines - none where it took none), every
    /// stage pair held against it again, and this build's compile order against it.
    /// </summary>
    public static string Replay(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var order = new List<string>();
        var first = new List<string>();
        var pairs = new List<(string From, string To, string Graphs)>();
        var taken = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string[] parts = line.Split('\t');
            if (parts.Length >= 3 && parts[0] == "  table")
            {
                order.Add(parts[2]);
            }
            else if (parts.Length >= 5 && parts[0] == "  entry" && parts[1] == "1")
            {
                first.Add(parts[4]);
            }
            else if (parts.Length >= 4 && parts[0] == "  pair")
            {
                pairs.Add((parts[1], parts[2], parts[3]));
            }

            taken |= line.StartsWith("=== the table taken", StringComparison.Ordinal);
        }

        // A v0.1.365 REPORT WROTE NO TABLE OF ITS OWN: where its head says it took one, that was run 1 whole (it held 81 entries, under the
        // 512 a run writes), so run 1's entries are the table.
        if (!taken && text.StartsWith("shader stages: the table at module+", StringComparison.Ordinal))
        {
            order = first;
        }

        if (order.Count == 0 && pairs.Count == 0)
        {
            return "shader stages replay: the report holds no table and no stage pair" + Environment.NewLine;
        }

        var said = new List<string>
        {
            order.Count > 0
                ? string.Create(CultureInfo.InvariantCulture, $"shader stages replay: a table of {order.Count} entries, {pairs.Count} stage pairs")
                : string.Create(CultureInfo.InvariantCulture, $"shader stages replay: no table taken, {pairs.Count} stage pairs"),
            string.Empty,
            "=== the table the report took",
        };
        said.AddRange(order.Select((stage, at) => string.Create(CultureInfo.InvariantCulture, $"  {at}\t{stage}")));
        said.Add(string.Empty);
        said.Add("=== this build's compile order (ShadeProgram.Stages) against it");
        Compile(order, said);
        said.Add(string.Empty);
        said.Add("=== the stage pairs held against it again");
        int forwards = 0, backwards = 0, unplaced = 0;
        foreach ((string from, string to, string graphs) in pairs)
        {
            string verdict = Verdict(from, to, order);
            forwards += verdict == "forwards" ? 1 : 0;
            backwards += verdict == "backwards" ? 1 : 0;
            unplaced += verdict == "unplaced" ? 1 : 0;
            if (verdict != "forwards")
            {
                said.Add($"  {verdict}\t{from}\t{to}\t{graphs}");
            }
        }

        said.Add(string.Create(CultureInfo.InvariantCulture, $"stage pairs: {forwards} forwards, {backwards} backwards, {unplaced} with a stage the table does not hold"));
        return string.Join(Environment.NewLine, said) + Environment.NewLine;
    }
}
