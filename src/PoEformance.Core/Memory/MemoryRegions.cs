namespace PoEformance.Core.Memory;

/// <summary>One committed, readable stretch of the target's address space.</summary>
/// <param name="Address">Where it starts.</param>
/// <param name="Size">How long it is.</param>
/// <param name="Protect">Its page protection as Windows reports it (<c>PAGE_*</c>), nought where nobody asked - a recording or a test.</param>
/// <param name="Type">What backs it (<c>MEM_PRIVATE</c>, <c>MEM_MAPPED</c>, <c>MEM_IMAGE</c>), nought where nobody asked.</param>
/// <remarks>
/// THE PROTECTION AND THE TYPE, so a search of the whole space can say where its time went. A capture's
/// light hunt over sixteen gigabytes was still running after two minutes, and which kind of memory it
/// was crawling through - the heap, a file mapped in, the driver's - is the question that decides what
/// to do about it.
/// </remarks>
public readonly record struct MemoryRegion(ulong Address, ulong Size, uint Protect = 0, uint Type = 0)
{
    /// <summary>The region's kind in words - its type and its protection - for a report.</summary>
    public string Kind
    {
        get
        {
            string type = Type switch
            {
                0x20000 => "private",
                0x40000 => "mapped",
                0x1000000 => "image",
                0 => "unknown",
                _ => $"type 0x{Type:X}",
            };

            string access = (Protect & 0xFF) switch
            {
                0x02 => "r",
                0x04 => "rw",
                0x08 => "copy-on-write",
                0x10 => "x",
                0x20 => "rx",
                0x40 => "rwx",
                0x80 => "rx copy-on-write",
                0 => "-",
                _ => $"0x{Protect & 0xFF:X}",
            };

            string cache = (Protect & 0x200) != 0 ? " uncached" : (Protect & 0x400) != 0 ? " write-combined" : string.Empty;
            return $"{type} {access}{cache}";
        }
    }

    /// <summary>
    /// Whether the driver maps it uncached or write-combined - the graphics card's upload heaps, which a read crawls through: v0.1.404's
    /// bone watch spent 1150 of its threads' seconds on 5.2 GB of them against 48 on 7.4 GB of the heap. False where nobody asked.
    /// </summary>
    public bool Slow => Protect != 0 && (Protect & 0x600) != 0;

    /// <summary>Whether the game can write it - read and write, copy-on-write, or either with execution. True where nobody asked.</summary>
    public bool Writable => Protect == 0 || (Protect & 0xFF) is 0x04 or 0x08 or 0x40 or 0x80;
}

/// <summary>
/// The regions a search for what the game keeps of its own should read: those it can write, not the driver's uncached or write-combined
/// heaps - and how much of each it left out, for the report.
/// </summary>
/// <remarks>
/// WHAT IS LEFT OUT CANNOT HOLD A STATE THE GAME KEEPS ON ITS PROCESSOR: memory it cannot write holds what a file mapped in or the program's
/// image holds, and a write-combined heap is what the processor writes for the card to read - a copy made each frame, not where a state is
/// kept between frames. The river flow hunt leaves out the same (RiverFlowHunt.Walk).
/// </remarks>
/// <param name="all">The target's regions.</param>
public sealed class WritableRegions(IMemoryRegions all) : IMemoryRegions
{
    private long _unwritable;
    private long _slow;

    /// <summary>The bytes left out because the game cannot write them, over the last walk of the regions.</summary>
    public long Unwritable => Volatile.Read(ref _unwritable);

    /// <summary>The bytes left out as the driver's uncached or write-combined heaps, over the last walk.</summary>
    public long Slow => Volatile.Read(ref _slow);

    /// <inheritdoc/>
    public IEnumerable<MemoryRegion> Regions()
    {
        ArgumentNullException.ThrowIfNull(all);
        long unwritable = 0, slow = 0;
        Volatile.Write(ref _unwritable, 0);
        Volatile.Write(ref _slow, 0);
        foreach (MemoryRegion region in all.Regions())
        {
            if (region.Slow)
            {
                slow += (long)region.Size;
                Volatile.Write(ref _slow, slow);
            }
            else if (!region.Writable)
            {
                unwritable += (long)region.Size;
                Volatile.Write(ref _unwritable, unwritable);
            }
            else
            {
                yield return region;
            }
        }
    }
}

/// <summary>
/// A reader that can say what memory the target actually has, rather than only answering
/// questions about addresses somebody already knows.
/// </summary>
/// <remarks>
/// THE GAP THIS CLOSES. Everything in this project finds things by following pointers from a
/// static, which means it can only ever see what is reachable from the roots it knows. Six
/// sessions of hunting a character's quest flags swept about 130 MB that way and found only
/// rules; the one explanation that survives is an object hanging off none of those roots.
/// Enumerating regions is how a search stops depending on that assumption.
///
/// Separate from <see cref="IMemoryReader"/> on purpose: a synthetic reader in a test has no
/// regions to speak of, and requiring it to invent some would put a fiction in every fixture.
/// Code that needs this asks for it and copes when it is absent.
/// </remarks>
public interface IMemoryRegions
{
    /// <summary>
    /// Every committed region that can be read, lowest address first.
    /// </summary>
    /// <remarks>
    /// Lazily, because the list is thousands long on a game and a caller that stops early
    /// should not pay for the rest of it.
    /// </remarks>
    IEnumerable<MemoryRegion> Regions();
}
