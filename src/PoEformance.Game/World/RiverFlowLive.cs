using System.Buffers.Binary;
using System.Globalization;
using PoEformance.Core.Memory;
using PoEformance.Game.Files;

namespace PoEformance.Game.World;

/// <summary>
/// The current area's river flow map, read live: the cache the program keeps its markup fields in found once a launch by its shape, and
/// the current entry's river field read whenever the area's instance changes - see <see cref="Current"/>.
/// </summary>
/// <remarks>
/// WHAT IS READ, MEASURED (river-flow.txt over four instances of The Red Vale in two launches, v0.1.353 to v0.1.356; the schema's
/// RiverFlowCache, RiverFlowRoot, RiverFlowFields, RiverFlowField): the program keeps a cache of four entries in its own writable data -
/// nodes of 0x30 bytes, each a std::shared_ptr to a markup object, two links and a key that grows with every instance made - its most and
/// least recent node's places in the two words before the current entry's std::shared_ptr, which always holds the current instance's
/// object (the head node holds the same). The object is std::make_shared's, its control block sixteen bytes before it; its first words a
/// std::vector of field pointers, the second the river's flow map (the first null in The Red Vale, the third the guiding fog's); the
/// field a std::vector of four floats a texel and the grid's size at +0x40.
///
/// FOUND BY ITS SHAPE, NOT A CODE PATTERN (DECIDED from the live client, v0.1.356): no code reads the cache by its own address, and its
/// place moved with a new build. So the program's writable data is searched once a launch for a word pair that is a std::shared_ptr of
/// make_shared's (its control block sixteen bytes before the object), the two words before it places in the program at most
/// <see cref="NodesWithin"/> before it, the first of them holding the same object, and the object's first words a vector of two to eight
/// pointers - milliseconds, on a thread of its own. A river field is believed only where its texels read as one: the flow only where the
/// distance to the bank is not below nought.
/// </remarks>
public sealed class RiverFlowLive
{
    /// <summary>How far before the current entry the cache's nodes may lie - MEASURED within 0x128, CHOSEN with room.</summary>
    public const int NodesWithin = 0x400;

    /// <summary>Which field of the object is the river's flow map - MEASURED in four instances (the second; the guiding fog's the third).</summary>
    public const int RiverField = 1;

    /// <summary>The fewest and most fields an object may hold to be the cache's - CHOSEN round the three measured.</summary>
    private const int FewestFields = 2, MostFields = 8;

    /// <summary>The smallest and largest grid side believed - CHOSEN.</summary>
    private const int LeastSide = 8, MostSide = 4096;

    /// <summary>How long a search that found nothing waits before it is run again - CHOSEN (the cache fills as an area with markup loads).</summary>
    public static readonly TimeSpan SearchAgain = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often the current entry's word is read again - CHOSEN: an instance changes with the area, and a quarter of a second late is
    /// unseen, while every model pane asks every frame.
    /// </summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.25);

    private readonly TimeSpan _checkEvery;
    private readonly object _gate = new();
    private long _checkedAt;
    private ulong _module;
    private ulong _current;
    private bool _busy;
    private long _searchedAt;
    private ulong _root;
    private ulong _reading;
    private RiverFlowMap? _map;
    private string _said = "not looked for yet";

    /// <summary>A live read checking the current entry every <paramref name="checkEvery"/>, <see cref="CheckEvery"/> where none is given.</summary>
    public RiverFlowLive(TimeSpan? checkEvery = null) => _checkEvery = checkEvery ?? CheckEvery;

    /// <summary>Where the live read stands, for the lines under a picture.</summary>
    public string Said
    {
        get
        {
            lock (_gate)
            {
                return _said;
            }
        }
    }

    /// <summary>
    /// The current area's river flow map, or null - never blocking: the search and a new instance's read run on a thread of their own, and
    /// until they are done the last map read for this instance, or none, is handed back. Cheap to call every frame: one word is read every <see cref="CheckEvery"/>.
    /// </summary>
    public RiverFlowMap? Current(IMemoryReader reader, IMemoryRegions? regions)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (regions is null || !reader.IsAttached || reader.ModuleSize == 0)
        {
            return null;
        }

        ulong current;
        lock (_gate)
        {
            // A NEW LAUNCH: everything found before is gone.
            if (_module != reader.ModuleBase)
            {
                _module = reader.ModuleBase;
                _current = 0;
                _root = 0;
                _map = null;
                _searchedAt = 0;
                _checkedAt = 0;
                _said = "not looked for yet";
            }

            current = _current;
            if (current == 0)
            {
                if (!_busy && (_searchedAt == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(_searchedAt) >= SearchAgain))
                {
                    _busy = true;
                    _said = "looking for the program's markup cache";
                    ulong module = _module;
                    Task.Run(() => Search(reader, regions, module));
                }

                return null;
            }

            // A WORD A QUARTER OF A SECOND, not a frame: until then the map read last stands.
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_checkedAt != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_checkedAt, now) < _checkEvery)
            {
                return _map;
            }

            _checkedAt = now;
        }

        // THE CURRENT ENTRY'S OBJECT - a new one is a new instance.
        if (!reader.TryRead(current, out ulong root) || root == 0)
        {
            lock (_gate)
            {
                _root = 0;
                _map = null;
                _said = "the markup cache holds no current entry - no river here";
                return null;
            }
        }

        lock (_gate)
        {
            if (root == _root)
            {
                return _map;
            }

            // ANOTHER INSTANCE: its map is read behind, and the last one's is not handed out meanwhile.
            _root = 0;
            _map = null;
            if (!_busy && _reading != root)
            {
                _busy = true;
                _reading = root;
                _said = "reading this instance's flow map";
                Task.Run(() => Refresh(reader, current, root));
            }

            return null;
        }
    }

    private void Search(IMemoryReader reader, IMemoryRegions regions, ulong module)
    {
        ulong found = 0;
        string said;
        try
        {
            found = Find(reader, regions, out said);
        }
        catch (Exception problem) when (problem is IOException or InvalidOperationException or ArgumentException)
        {
            said = "the search for the markup cache failed: " + problem.Message;
        }

        lock (_gate)
        {
            _busy = false;
            _searchedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_module == module)
            {
                _current = found;
                _said = said;
            }
        }
    }

    private void Refresh(IMemoryReader reader, ulong current, ulong root)
    {
        RiverFlowMap? map = Read(reader, current, out ulong read, out string said);
        lock (_gate)
        {
            _busy = false;
            _reading = 0;
            if (read == root)
            {
                _root = root;
                _map = map;
            }

            _said = said;
        }
    }

    /// <summary>
    /// The place of the cache's current entry - its std::shared_ptr's pointer - in the program's writable data, or nought; <paramref name="said"/>
    /// says what was looked through and found.
    /// </summary>
    public static ulong Find(IMemoryReader reader, IMemoryRegions regions, out string said)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        ulong image = reader.ModuleBase, end = reader.ModuleBase + reader.ModuleSize;
        var found = new List<ulong>();
        long looked = 0;
        byte[] buffer = new byte[1 << 20];
        foreach (MemoryRegion region in regions.Regions())
        {
            // THE PROGRAM'S OWN WRITABLE DATA: in its image, readable and writable (or a recording's, whose protection nobody asked).
            if (region.Address < image || region.Address >= end
                || (region.Protect != 0 && ((region.Protect & 0x600) != 0 || (region.Protect & 0xFF) is not (0x04 or 0x08 or 0x40 or 0x80))))
            {
                continue;
            }

            ulong last = Math.Min(region.Address + region.Size, end);
            // CHUNKS OVERLAPPING BY 32 BYTES: a place needs the 16 before it and the 16 from it.
            for (ulong from = region.Address; from < last; from += (ulong)buffer.Length - 32)
            {
                int want = (int)Math.Min((ulong)buffer.Length, last - from);
                if (want < 32 || !reader.TryRead(from, buffer.AsSpan(0, want)))
                {
                    continue;
                }

                looked += want;
                for (var at = 16; at + 16 <= want; at += 8)
                {
                    ulong root = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(at));
                    if (root < 0x10 || BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(at + 8)) != root - 0x10
                        || (root & 7) != 0 || !MemoryReaderExtensions.IsPlausiblePointer(root) || (root >= image && root < end))
                    {
                        continue;
                    }

                    ulong place = from + (ulong)at;
                    ulong head = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(at - 16));
                    ulong tail = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(at - 8));
                    if (Near(head, place) && Near(tail, place) && Shaped(reader, head, root) && !found.Contains(place))
                    {
                        found.Add(place);
                    }
                }
            }
        }

        said = found.Count switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"no markup cache in the program's writable data ({looked / 1024.0 / 1024.0:0.0} MB looked through)"),
            1 => string.Create(CultureInfo.InvariantCulture, $"the markup cache's current entry at module+0x{found[0] - image:X} ({looked / 1024.0 / 1024.0:0.0} MB looked through)"),
            _ => string.Create(CultureInfo.InvariantCulture,
                $"{found.Count} places shaped as the markup cache's current entry, the first taken: module+{string.Join(", module+", found.Select(one => $"0x{one - image:X}"))}"),
        };
        return found.Count > 0 ? found[0] : 0;
    }

    /// <summary>Whether a word is a place in the program at most <see cref="NodesWithin"/> before the current entry - a node of the cache.</summary>
    private static bool Near(ulong node, ulong place) => node < place && place - node <= NodesWithin && (node & 7) == 0;

    /// <summary>Whether the head node holds the same object, and the object's first words are a vector of field pointers.</summary>
    private static bool Shaped(IMemoryReader reader, ulong head, ulong root)
    {
        if (!reader.TryRead(head, out ulong held) || held != root || !reader.TryRead(root, out ulong begin) || !reader.TryRead(root + 8, out ulong end))
        {
            return false;
        }

        ulong span = end - begin;
        return end > begin && (begin & 7) == 0 && span % 8 == 0 && span >= FewestFields * 8 && span <= MostFields * 8;
    }

    /// <summary>
    /// The river's flow map of the cache's current entry, or null; <paramref name="root"/> is the object it was read for, and
    /// <paramref name="said"/> what was read or why nothing was.
    /// </summary>
    public static RiverFlowMap? Read(IMemoryReader reader, ulong current, out ulong root, out string said)
    {
        ArgumentNullException.ThrowIfNull(reader);
        root = 0;
        if (!reader.TryRead(current, out root) || root == 0 || !reader.TryRead(current + 8, out ulong control) || control != root - 0x10)
        {
            said = "the markup cache's current entry holds no object";
            return null;
        }

        if (!reader.TryRead(root, out ulong fields) || !reader.TryRead(root + 8, out ulong fieldsEnd) || fieldsEnd <= fields
            || (fieldsEnd - fields) / 8 <= RiverField)
        {
            said = "the current object holds no second field";
            return null;
        }

        if (!reader.TryRead(fields + (RiverField * 8), out ulong holder) || holder == 0)
        {
            said = "this instance has no river field - no river here";
            return null;
        }

        if (!reader.TryRead(holder, out ulong begin) || !reader.TryRead(holder + 8, out ulong end)
            || !reader.TryRead(holder + 0x40, out int width) || !reader.TryRead(holder + 0x44, out int height)
            || width is < LeastSide or > MostSide || height is < LeastSide or > MostSide
            || end - begin != (ulong)width * (ulong)height * 16)
        {
            said = string.Create(CultureInfo.InvariantCulture, $"the river field at 0x{holder:X} is not a grid of four floats a texel");
            return null;
        }

        var bytes = new byte[width * height * 16];
        if (!reader.TryRead(begin, bytes))
        {
            said = string.Create(CultureInfo.InvariantCulture, $"the river field's {bytes.Length} bytes at 0x{begin:X} did not read");
            return null;
        }

        var texels = new float[width * height * 4];
        Buffer.BlockCopy(bytes, 0, texels, 0, bytes.Length);
        int flowing = 0, outside = 0, water = 0;
        for (var at = 0; at < texels.Length; at += 4)
        {
            float x = texels[at], y = texels[at + 1], z = texels[at + 2], w = texels[at + 3];
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(w))
            {
                said = "the river field holds a float that is not finite";
                return null;
            }

            // MINUS NOUGHT IS THE BANK: w >= 0 holds for it, as GetFlowSample's readers compare.
            water += w >= 0f ? 1 : 0;
            if (x != 0f || y != 0f)
            {
                flowing++;
                outside += w >= 0f ? 0 : 1;
            }
        }

        if (flowing == 0 || outside > 0)
        {
            said = string.Create(CultureInfo.InvariantCulture,
                $"the second field does not read as a river's flow map ({flowing} texels flowing, {outside} of them outside the banks)");
            return null;
        }

        said = string.Create(CultureInfo.InvariantCulture,
            $"the river's flow map read live: {width} x {height} texels, water at {(double)water / (width * height):P0} of them, flow at {flowing}");
        return new RiverFlowMap(width, height, texels, said);
    }

    /// <summary>The most nodes the report follows from the head - the cache holds four (MEASURED).</summary>
    private const int NodesShown = 8;

    /// <summary>
    /// The live read for the capture's river-flow.txt, both halves: what the pictures were drawn with (<paramref name="live"/>, the overlay's
    /// own reader's line), the search run again, every node of the cache word by word from its head, the current entry and its object, and
    /// the map read from it with its box and flow - so a picture whose river does not move is told apart from a cache the search missed.
    /// </summary>
    public static IReadOnlyList<string> Report(IMemoryReader reader, IMemoryRegions? regions, string live)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var lines = new List<string>
        {
            "=== the river's flow map read live (RiverFlowLive): the program's markup cache found by its shape, its current entry's river field read",
            "  the pictures': " + live,
        };
        if (regions is null || !reader.IsAttached || reader.ModuleSize == 0)
        {
            lines.Add("  no memory to search - not attached, or the reader lists no regions");
            return lines;
        }

        ulong image = reader.ModuleBase;
        ulong current = Find(reader, regions, out string found);
        lines.Add("  search again: " + found);
        if (current == 0)
        {
            return lines;
        }

        // THE CACHE'S NODES, from the head, each as v0.1.356 measured it: the object, its control block, the nodes towards the head and
        // towards the tail, the key - the head's first link and the tail's second null.
        if (reader.TryRead(current - 16, out ulong head) && reader.TryRead(current - 8, out ulong tail))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  head node module+0x{head - image:X}, tail module+0x{tail - image:X}, the current entry module+0x{current - image:X}"));
            ulong node = head;
            for (var shown = 0; shown < NodesShown && node != 0; shown++)
            {
                if (!reader.TryRead(node, out ulong held) || !reader.TryRead(node + 8, out ulong control) || !reader.TryRead(node + 0x10, out ulong previous)
                    || !reader.TryRead(node + 0x18, out ulong next) || !reader.TryRead(node + 0x20, out ulong key))
                {
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"  node module+0x{node - image:X} did not read"));
                    break;
                }

                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  node module+0x{node - image:X}: object 0x{held:X}, control 0x{control:X}{(control == held - 0x10 ? " (make_shared's)" : string.Empty)}, previous 0x{previous:X}, next 0x{next:X}, key {key}"));
                if (node == tail)
                {
                    break;
                }

                node = next;
            }
        }

        RiverFlowMap? map = Read(reader, current, out ulong root, out string read);
        if (root != 0 && reader.TryRead(root - 8, out ulong counts))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  the current object 0x{root:X}, its control block's counts {(uint)counts} and {(uint)(counts >> 32)}"));
        }

        lines.Add("  read: " + read);
        if (map is null)
        {
            return lines;
        }

        // THE MAP AS DRAWN: its box from the area's origin, and how fast it flows - the speed's median and most over the texels that flow.
        var speeds = new List<float>(map.Width * map.Height);
        for (var at = 0; at < map.Texels.Length; at += 4)
        {
            float x = map.Texels[at], y = map.Texels[at + 1];
            if (x != 0f || y != 0f)
            {
                speeds.Add(MathF.Sqrt((x * x) + (y * y)));
            }
        }

        speeds.Sort();
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  the map's box ({RiverFlowMap.Least:0.###}, {RiverFlowMap.Least:0.###}) to ({RiverFlowMap.Least + map.Size.X:0.###}, {RiverFlowMap.Least + map.Size.Y:0.###}) in the area, {RiverFlowMap.Texel:0.####} a texel; the flow {speeds[speeds.Count / 2]:0.#} a second at the median, {speeds[^1]:0.#} at most, as the map holds it (before twice the area's flow_intensity)"));
        return lines;
    }
}
