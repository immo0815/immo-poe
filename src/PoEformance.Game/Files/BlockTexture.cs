using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>The block formats read by <see cref="BlockArt"/> - and handed to the graphics card as they are (ModelGpu).</summary>
public enum BlockFormat
{
    /// <summary>Two 5:6:5 colours and two between, or one between and transparent black - eight bytes a block.</summary>
    Bc1,

    /// <summary>BC1's four-colour block under sixteen four-bit alphas - sixteen bytes.</summary>
    Bc2,

    /// <summary>BC1's four-colour block under a BC4 ramp of alpha - sixteen bytes.</summary>
    Bc3,

    /// <summary>One channel, a ramp between two ends - eight bytes, read as red.</summary>
    Bc4,

    /// <summary>Two BC4 ramps, red and green - sixteen bytes.</summary>
    Bc5,

    /// <summary>Eight modes of partitioned endpoints - sixteen bytes.</summary>
    Bc7,
}

/// <summary>
/// A block-compressed DDS as the file holds it: its format, its size, and where each of its levels lies among its bytes.
/// </summary>
/// <remarks>
/// THE FILE'S OWN LEVELS, KEPT AS BLOCKS (v0.1.326, asked for from the live client): the graphics card reads BC formats as they are, so
/// a texture is handed to it as the file carries it - a BC7 a byte a texel and a BC1 half of one, where the decoded copy it was handed
/// before took four - and the processor decodes the same levels when it needs them (Mipmaps.Kept(BlockTexture)). Port's stage holds
/// 800 million texels at the top level (v0.1.319's capture): 4.3 gigabytes decoded with their levels, 0.8 as the file's blocks.
///
/// ONLY A PLAIN 2D TEXTURE WITH EVERY LEVEL DOWN TO ONE TEXEL IS <see cref="Complete"/> - what the card and the processor then read
/// alike. The top level of anything else still decodes (<see cref="Decode"/>), as it always did; a cube, an array, a volume or a file
/// short of its levels is decoded and its levels built as before (Mipmaps.Of(GamePicture)), so no picture comes out coarser than it
/// was. The levels are read as DirectXTex reads them: the header's mip count, nought being one, each level after the one before.
/// </remarks>
public sealed class BlockTexture
{
    private const int Header = 128;
    private const int Dx10Header = 20;

    private readonly int[] _offsets;

    private BlockTexture(byte[] bytes, BlockFormat format, int width, int height, int[] offsets, bool complete)
    {
        Bytes = bytes;
        Format = format;
        Width = width;
        Height = height;
        _offsets = offsets;
        Complete = complete;
    }

    /// <summary>The file's bytes, whole - each level is a stretch of them.</summary>
    public byte[] Bytes { get; }

    /// <summary>How the blocks are packed.</summary>
    public BlockFormat Format { get; }

    /// <summary>The top level's width, in texels.</summary>
    public int Width { get; }

    /// <summary>The top level's height, in texels.</summary>
    public int Height { get; }

    /// <summary>How many levels the file holds whole, the top included.</summary>
    public int Levels => _offsets.Length;

    /// <summary>Whether this is a plain 2D texture with every level down to one texel - read as it is by the card and the processor alike.</summary>
    public bool Complete { get; }

    /// <summary>The bytes of one block.</summary>
    public int BytesPerBlock => BytesPerBlockOf(Format);

    /// <summary>A level's width: the top's halved for each level, never under one.</summary>
    public int WidthAt(int level) => Math.Max(1, Width >> level);

    /// <summary>A level's height: the top's halved for each level, never under one.</summary>
    public int HeightAt(int level) => Math.Max(1, Height >> level);

    /// <summary>Where a level starts among <see cref="Bytes"/>.</summary>
    public int OffsetAt(int level) => _offsets[level];

    /// <summary>The bytes of one row of a level's blocks - what a card is told a row of it takes.</summary>
    public int PitchAt(int level) => ((WidthAt(level) + 3) / 4) * BytesPerBlock;

    /// <summary>A level's bytes, every row of its blocks.</summary>
    public int SizeAt(int level) => PitchAt(level) * ((HeightAt(level) + 3) / 4);

    /// <summary>The bytes of a block of a format.</summary>
    public static int BytesPerBlockOf(BlockFormat format) => format is BlockFormat.Bc1 or BlockFormat.Bc4 ? 8 : 16;

    /// <summary>How many levels a texture of this size has down to one texel, the top included.</summary>
    public static int FullChain(int width, int height) => BitOperations.Log2((uint)Math.Max(width, height)) + 1;

    /// <summary>One level decoded as red, green, blue, alpha bytes - BlockArt's arithmetic.</summary>
    public GamePicture Decode(int level) => BlockArt.Level(Bytes, OffsetAt(level), Format, WidthAt(level), HeightAt(level));

    /// <summary>
    /// The blocks of a DDS in a format BlockArt reads, and where its levels lie - or null for any other format, or a file too short for its top level.
    /// </summary>
    /// <remarks>Its header alone is read: nothing is decoded or copied, and the bytes are kept as they are.</remarks>
    public static BlockTexture? Of(byte[]? dds)
    {
        if (dds is null || dds.Length < Header || !dds.AsSpan(0, 4).SequenceEqual("DDS "u8) || (BitConverter.ToUInt32(dds, 80) & 0x4) == 0)
        {
            return null;
        }

        BlockFormat? format;
        int offset = Header;
        bool plain = (BitConverter.ToUInt32(dds, 112) & (0x200 | 0x200000)) == 0 && (BitConverter.ToUInt32(dds, 8) & 0x800000) == 0;
        ReadOnlySpan<byte> code = dds.AsSpan(84, 4);
        if (code.SequenceEqual("DX10"u8))
        {
            if (dds.Length < Header + Dx10Header)
            {
                return null;
            }

            format = BitConverter.ToUInt32(dds, Header) switch
            {
                70 or 71 or 72 => BlockFormat.Bc1,
                73 or 74 or 75 => BlockFormat.Bc2,
                76 or 77 or 78 => BlockFormat.Bc3,
                80 => BlockFormat.Bc4,
                83 => BlockFormat.Bc5,
                97 or 98 or 99 => BlockFormat.Bc7,
                _ => null,
            };

            // A 2D TEXTURE, ONE OF IT, NO CUBE: the dimension 3, the cube flag clear, the array one long.
            plain &= BitConverter.ToUInt32(dds, Header + 4) == 3 && (BitConverter.ToUInt32(dds, Header + 8) & 0x4) == 0
                && BitConverter.ToUInt32(dds, Header + 12) <= 1;
            offset = Header + Dx10Header;
        }
        else
        {
            format = code.SequenceEqual("DXT1"u8) ? BlockFormat.Bc1
                : code.SequenceEqual("DXT3"u8) ? BlockFormat.Bc2
                : code.SequenceEqual("DXT5"u8) ? BlockFormat.Bc3
                : code.SequenceEqual("ATI1"u8) || code.SequenceEqual("BC4U"u8) ? BlockFormat.Bc4
                : code.SequenceEqual("ATI2"u8) || code.SequenceEqual("BC5U"u8) ? BlockFormat.Bc5
                : null;
        }

        int height = BitConverter.ToInt32(dds, 12);
        int width = BitConverter.ToInt32(dds, 16);
        if (format is not { } kind || width is <= 0 or > GameArt.WidestPicture || height is <= 0 or > GameArt.WidestPicture)
        {
            return null;
        }

        int full = FullChain(width, height);
        int wanted = Math.Clamp((int)Math.Min(BitConverter.ToUInt32(dds, 28), int.MaxValue), 1, full);
        var offsets = new List<int>(wanted);
        long at = offset;
        int perBlock = BytesPerBlockOf(kind);
        for (var level = 0; level < wanted; level++)
        {
            long size = (long)((Math.Max(1, width >> level) + 3) / 4) * ((Math.Max(1, height >> level) + 3) / 4) * perBlock;
            if (at + size > dds.Length)
            {
                break;
            }

            offsets.Add((int)at);
            at += size;
        }

        return offsets.Count == 0 ? null : new BlockTexture(dds, kind, width, height, [.. offsets], plain && offsets.Count == full);
    }
}
