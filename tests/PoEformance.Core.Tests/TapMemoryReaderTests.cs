using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;

namespace PoEformance.Core.Tests;

/// <summary>
/// A recording switched on mid-session: what the capture key keeps of memory.
/// </summary>
public class TapMemoryReaderTests
{
    private static readonly KeyValuePair<string, string>[] Statics =
        [new(RecordingFormat.StaticNotePrefix + "GameStates", "1234")];

    /// <summary>Only the reads made while it ran are in it, it ends on its own after its frames, and it replays with its statics.</summary>
    [Fact]
    public async Task ARECORDINGHoldsTheReadsOfItsFramesAndReplays()
    {
        var live = new FakeMemoryReader().Place(0x1000, 7UL).Place(0x2000, 8UL).Place(0x3000, 9UL);
        using var tap = new TapMemoryReader(live);
        Assert.Equal(7UL, tap.Read<ulong>(0x1000));

        var file = new MemoryStream();
        Task<long> done = Assert.IsType<Task<long>>(tap.Start(file, Statics, new TapRecording(2)));
        Assert.True(tap.Recording);
        Assert.Null(tap.Start(new MemoryStream(), Statics, new TapRecording(1)));

        tap.MarkFrame();
        Assert.Equal(8UL, tap.Read<ulong>(0x2000));
        tap.MarkFrame();
        tap.MarkFrame();
        Assert.False(tap.Recording);
        Assert.Equal(9UL, tap.Read<ulong>(0x3000));

        long bytes = await done;
        byte[] recorded = file.ToArray();
        Assert.Equal(recorded.Length, bytes);

        using var replay = ReplayMemoryReader.Load(new MemoryStream(recorded));
        Assert.Equal(0x1234UL, replay.ResolvedStatics["GameStates"]);
        Assert.Equal(8UL, replay.Read<ulong>(0x2000));
        Assert.False(replay.TryRead(0x1000, out ulong _));
        Assert.False(replay.TryRead(0x3000, out ulong _));
    }

    /// <summary>A recording ending closes its file, never the reader it records - the session goes on reading.</summary>
    [Fact]
    public void ARECORDINGEndingLeavesTheReaderOpen()
    {
        var live = new CountedReader();
        using var tap = new TapMemoryReader(live);
        _ = tap.Start(new MemoryStream(), Statics, new TapRecording(1));
        tap.Stop();
        Assert.False(tap.Recording);
        Assert.Equal(0, live.Disposed);
        Assert.NotNull(tap.Start(new MemoryStream(), Statics, new TapRecording(1)));

        tap.Dispose();
        Assert.Equal(1, live.Disposed);
    }

    /// <summary>
    /// A search reads past a running recording: none of its chunks land in the file, which still holds the overlay's own read
    /// - the capture's light hunt went through the recorder once and crawled at its compressor's pace.
    /// </summary>
    [Fact]
    public async Task ASEARCHPastTheTapIsNotRecorded()
    {
        var bytes = new byte[2 * HeapScan.ChunkBytes];
        BitConverter.TryWriteBytes(bytes.AsSpan(0x40), 7.25f);
        var live = new Spaced(new FakeMemoryReader().Place(0x1000_0000, bytes).Place(0x1000, 8UL), new MemoryRegion(0x1000_0000, (ulong)bytes.Length));
        using var tap = new TapMemoryReader(live);
        var file = new MemoryStream();
        Task<long> done = Assert.IsType<Task<long>>(tap.Start(file, Statics, new TapRecording(1) { MaxReadBytes = RecordingFormat.MaxReadLength }));

        FloatHuntResult found = FloatHunt.Run(tap.Untapped, Assert.IsAssignableFrom<IMemoryRegions>(tap.Untapped), [new FloatNeedle("v", [7.25f], 0f)]);
        Assert.Equal(8UL, tap.Read<ulong>(0x1000));
        tap.MarkFrame();
        tap.MarkFrame();
        await done;

        Assert.Equal(0x1000_0040UL, Assert.Single(found.Sightings).At);
        using var replay = ReplayMemoryReader.Load(new MemoryStream(file.ToArray()));
        Assert.Equal(8UL, replay.Read<ulong>(0x1000));
        Assert.DoesNotContain(replay.Regions(), region => region.Address >= 0x1000_0000);
    }

    /// <summary>A reader with regions, as the live one is.</summary>
    private sealed class Spaced(FakeMemoryReader inner, params MemoryRegion[] regions) : IMemoryReader, IMemoryRegions
    {
        public bool IsAttached => true;

        public int ProcessId => inner.ProcessId;

        public ulong ModuleBase => 0;

        public uint ModuleSize => 0;

        public bool TryRead(ulong address, Span<byte> destination) => inner.TryRead(address, destination);

        public IEnumerable<MemoryRegion> Regions() => regions;

        public void Dispose()
        {
        }
    }

    private sealed class CountedReader : IMemoryReader
    {
        public int Disposed { get; private set; }

        public bool IsAttached => true;

        public int ProcessId => 1;

        public ulong ModuleBase => 0;

        public uint ModuleSize => 0;

        public bool TryRead(ulong address, Span<byte> destination) => false;

        public void Dispose() => Disposed++;
    }
}
