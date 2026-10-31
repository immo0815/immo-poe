using System.Buffers.Binary;
using System.Numerics;
using PoEformance.Core.Memory;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The river flow map read live (RiverFlowLive): the program's markup cache found by its shape in the program's writable data, laid out
/// as v0.1.356's two captures of The Red Vale held it, and the current entry's river field read and believed only as a flow field.
/// </summary>
public class RiverFlowLiveTests
{
    private const ulong Module = 0x7FF7_F586_0000;
    private const ulong Data = Module + 0x46F_0000;
    private const ulong Current = Data + 0xD480;
    private const ulong Heap = 0x3D2_1F30_0000;
    private const ulong Root = Heap + 0xA890;
    private const ulong Fields = Heap + 0x2_0000;
    private const ulong Holder = Heap + 0x3_0000;
    private const ulong Texels = Heap + 0x10_0000;
    private const int Width = 24, Height = 20;

    /// <summary>The cache as v0.1.356's second capture held it: four nodes before the current entry, the head first, its object's second field the river.</summary>
    [Fact]
    public void THECACHEIsFoundByItsShapeAndTheRiverReadFromItsCurrentEntry()
    {
        (FakeMemoryReader memory, Space space) = Memory(River());
        ulong found = RiverFlowLive.Find(memory, space, out string said);
        Assert.Equal(Current, found);
        Assert.StartsWith("the markup cache's current entry at module+0x46FD480", said, StringComparison.Ordinal);

        RiverFlowMap map = RiverFlowLive.Read(memory, found, out ulong root, out string read)!;
        Assert.Equal(Root, root);
        Assert.Equal((Width, Height), (map.Width, map.Height));
        Assert.StartsWith("the river's flow map read live: 24 x 20 texels", read, StringComparison.Ordinal);

        // GetFlowSample AT A TEXEL'S CENTRE is that texel: the box three tenths of a texel below the area's origin.
        var flow = new RiverFlow(map, new Vector2(1000f, 500f));
        Vector2 centre = new Vector2(RiverFlowMap.Least + (5.5f * RiverFlowMap.Texel), RiverFlowMap.Least + (10.5f * RiverFlowMap.Texel)) - flow.Origin;
        Vector4 sampled = flow.Sample(centre);
        Assert.Equal(map.Texel4(5, 10).X, sampled.X, 3);
        Assert.Equal(map.Texel4(5, 10).W, sampled.W, 2);

        // HALFWAY BETWEEN TWO TEXELS, their mean; past the far edge, the near one again - wrapping.
        Vector4 between = flow.Sample(centre + new Vector2(RiverFlowMap.Texel / 2f, 0f));
        Assert.Equal((map.Texel4(5, 10).W + map.Texel4(6, 10).W) / 2f, between.W, 2);
        Assert.Equal(flow.Sample(centre).W, flow.Sample(centre + new Vector2(Width * RiverFlowMap.Texel, 0f)).W, 2);
    }

    /// <summary>A shared pointer with no cache's nodes before it is not the cache; nor is a field flowing outside its banks a river.</summary>
    [Fact]
    public void ADECOYIsNoCacheAndAFieldFlowingPastItsBanksNoRiver()
    {
        (FakeMemoryReader memory, Space space) = Memory(River(), nodes: false);
        Assert.Equal(0UL, RiverFlowLive.Find(memory, space, out string said));
        Assert.StartsWith("no markup cache in the program's writable data", said, StringComparison.Ordinal);

        float[] outside = River();
        outside[0] = 30f;
        outside[3] = -54f;
        (memory, _) = Memory(outside);
        Assert.Null(RiverFlowLive.Read(memory, Current, out _, out string refused));
        Assert.StartsWith("the second field does not read as a river's flow map (", refused, StringComparison.Ordinal);
    }

    /// <summary>Live: nothing the first call - the search runs behind - then this instance's map, and none once the entry is gone.</summary>
    [Fact]
    public void THELIVEReadHandsTheMapBackOnceFoundAndRead()
    {
        (FakeMemoryReader memory, Space space) = Memory(River());
        var live = new RiverFlowLive(TimeSpan.Zero);
        Assert.Null(live.Current(memory, space));
        RiverFlowMap? map = null;
        Assert.True(SpinWait.SpinUntil(() => (map = live.Current(memory, space)) is not null, TimeSpan.FromSeconds(10)), live.Said);
        Assert.Equal(Width, map!.Width);

        memory.Place(Current, new byte[16]);
        Assert.Null(live.Current(memory, space));
        Assert.StartsWith("the markup cache holds no current entry", live.Said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The search and the read run on a thread of their own, never the shared pool's: main's CI run 388 found the live read still looking
    /// for the cache after ten seconds, its search of milliseconds queued behind every other test's parallel loops.
    /// </summary>
    [Fact]
    public void THESEARCHAndTheReadRunOffTheSharedPool()
    {
        (FakeMemoryReader memory, Space space) = Memory(River());
        var watched = new Watched(memory, Environment.CurrentManagedThreadId);
        var live = new RiverFlowLive(TimeSpan.Zero);
        Assert.Null(live.Current(watched, space));
        Assert.True(SpinWait.SpinUntil(() => live.Current(watched, space) is not null, TimeSpan.FromSeconds(10)), live.Said);
        Assert.True(watched.Behind > 0, "nothing was read behind the caller");
        Assert.Equal(0, watched.BehindOnThePool);
    }

    /// <summary>
    /// The capture's half of it: the search run again, the cache's nodes from the head, the current object and the map's box - both halves
    /// of what the pictures were drawn with.
    /// </summary>
    [Fact]
    public void THEREPORTWritesTheCacheNodeByNodeAndTheMapItRead()
    {
        (FakeMemoryReader memory, Space space) = Memory(River());
        string text = string.Join('\n', RiverFlowLive.Report(memory, space, "drawn with nothing yet"));
        Assert.Contains("  the pictures': drawn with nothing yet", text, StringComparison.Ordinal);
        Assert.Contains("  search again: the markup cache's current entry at module+0x46FD480", text, StringComparison.Ordinal);
        Assert.Contains("  head node module+0x46FD358, tail module+0x46FD388, the current entry module+0x46FD480", text, StringComparison.Ordinal);
        Assert.Contains($"  node module+0x46FD358: object 0x{Root:X}, control 0x{Root - 0x10:X} (make_shared's), previous 0x0, next 0x{Data + 0xD3E8:X}, key 16", text, StringComparison.Ordinal);
        Assert.Contains("  read: the river's flow map read live: 24 x 20 texels", text, StringComparison.Ordinal);
        Assert.Contains("  the map's box (-16.304, -16.304) to (1288.044, 1070.652) in the area, 54.3478 a texel; the flow 52.3 a second at the median, 64 at most", text, StringComparison.Ordinal);
    }

    /// <summary>A river along x in the grid's middle rows: its flow only where w, the distance to the bank, is not below nought.</summary>
    private static float[] River()
    {
        var texels = new float[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                int at = ((y * Width) + x) * 4;
                float bank = (4f - MathF.Abs(y - 10f)) * RiverFlowMap.Texel;
                texels[at] = bank >= 0f ? 40f + x : 0f;
                texels[at + 1] = bank >= 0f ? 0.5f * x : 0f;
                texels[at + 3] = bank;
            }
        }

        return texels;
    }

    /// <summary>
    /// The program's data with the cache in it - four nodes, the head and tail before the current entry, the current std::shared_ptr - and
    /// the heap with the object, its fields, the holder and the texels.
    /// </summary>
    private static (FakeMemoryReader Memory, Space Space) Memory(float[] texels, bool nodes = true)
    {
        var data = new byte[0x1_0000];
        void Word(ulong at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan((int)(at - Data)), value);

        // THE NODES as v0.1.356's second capture held them: the current at +0x358, the head; +0x388 the tail.
        if (nodes)
        {
            Word(Data + 0xD358, Root);
            Word(Data + 0xD360, Root - 0x10);
            Word(Data + 0xD370, Data + 0xD3E8);
            Word(Data + 0xD378, 16);
            Word(Data + 0xD470, Data + 0xD358);
            Word(Data + 0xD478, Data + 0xD388);
        }

        Word(Current, Root);
        Word(Current + 8, Root - 0x10);

        var root = new byte[0x40];
        BinaryPrimitives.WriteUInt64LittleEndian(root.AsSpan(0x10), Fields);
        BinaryPrimitives.WriteUInt64LittleEndian(root.AsSpan(0x18), Fields + 24);
        var fields = new byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(fields.AsSpan(8), Holder);
        var holder = new byte[0x60];
        BinaryPrimitives.WriteUInt64LittleEndian(holder.AsSpan(0), Texels);
        BinaryPrimitives.WriteUInt64LittleEndian(holder.AsSpan(8), Texels + (ulong)(texels.Length * 4));
        BinaryPrimitives.WriteInt32LittleEndian(holder.AsSpan(0x40), Width);
        BinaryPrimitives.WriteInt32LittleEndian(holder.AsSpan(0x44), Height);
        var bytes = new byte[texels.Length * 4];
        Buffer.BlockCopy(texels, 0, bytes, 0, bytes.Length);

        var memory = new FakeMemoryReader { ModuleBase = Module, ModuleSize = 0x480_0000 }
            .Place(Data, data).Place(Root - 0x10, root).Place(Fields, fields).Place(Holder, holder).Place(Texels, bytes);
        var space = new Space(
            new MemoryRegion(Data, (ulong)data.Length, Protect: 0x04, Type: 0x100_0000),
            new MemoryRegion(Root - 0x10, (ulong)root.Length, Protect: 0x04, Type: 0x2_0000));
        return (memory, space);
    }

    private sealed class Space(params MemoryRegion[] regions) : IMemoryRegions
    {
        public IEnumerable<MemoryRegion> Regions() => regions;
    }

    /// <summary>A reader counting the reads made off the caller's thread, and how many of them the shared pool's threads made.</summary>
    private sealed class Watched(IMemoryReader inner, int caller) : IMemoryReader
    {
        private int _behind;
        private int _onThePool;

        public int Behind => Volatile.Read(ref _behind);

        public int BehindOnThePool => Volatile.Read(ref _onThePool);

        public bool IsAttached => inner.IsAttached;

        public int ProcessId => inner.ProcessId;

        public ulong ModuleBase => inner.ModuleBase;

        public uint ModuleSize => inner.ModuleSize;

        public bool TryRead(ulong address, Span<byte> destination)
        {
            if (Environment.CurrentManagedThreadId != caller)
            {
                Interlocked.Increment(ref _behind);
                if (Thread.CurrentThread.IsThreadPoolThread)
                {
                    Interlocked.Increment(ref _onThePool);
                }
            }

            return inner.TryRead(address, destination);
        }

        public void Dispose() => inner.Dispose();
    }
}
