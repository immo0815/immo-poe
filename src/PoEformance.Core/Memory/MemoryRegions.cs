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
