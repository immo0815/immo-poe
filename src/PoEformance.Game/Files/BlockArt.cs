using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PoEformance.Game.Files;

/// <summary>
/// The block-compressed textures the game's art is - BC1, BC2, BC3, BC4, BC5 and BC7 - decoded here rather than by Pfim, any level of them.
/// </summary>
/// <remarks>
/// MEASURED BEFORE IT WAS WRITTEN (v0.1.286's capture, a Necropolis stage): decoding was 110 of a stage's 128 thread-seconds, and
/// BC7 98 of them - 181 textures at 222 ns a pixel, where BC1 took 30. Pfim's BC7 is DirectXTex's decoder as written, allocating
/// its endpoints, weights and colours anew for every 4 by 4 block, and it decodes every mip level of the file while GameArt keeps
/// the top one. This is the same arithmetic - the same tables, the same rounding, Pfim's own float steps for BC1's in-between
/// colours - with nothing allocated per block, the level asked for alone, its rows of blocks on every core where a picture is large.
/// GameArtTests hold it against Pfim pixel for pixel over blocks of every BC7 mode, both BC1 modes and sizes that cut blocks short.
///
/// BC2 TO BC5 FROM v0.1.326, when the card was handed the file's own blocks (BlockTexture) and the processor's pixels became the
/// reference for the card's own decoding: BC2's and BC3's colours are BC1's four-colour arithmetic, BC2's alpha its nibble times 17,
/// and the one-channel ramps of BC3's alpha, BC4 and BC5 are worked out as the card works them out - the exact fraction, rounded to
/// the nearest byte. Pfim rounds BC4 so and TRUNCATES BC3's alpha and BC5 (Bc5Dds.ExtractGradient), so those two come out up to a
/// level apart from what it gave (CHOSEN: the card's). BC4 reads as a card reads one, (r, 0, 0, 1); BC5 as (r, g, 0, 1).
///
/// WHAT IT DOES NOT READ IT LEAVES TO PFIM: any other format, and a file too short for its top level, answer null here.
/// </remarks>
public static class BlockArt
{
    /// <summary>Most blocks a picture may have before its rows are decoded on every core - a 512-pixel square.</summary>
    private const int ParallelBlocks = 128 * 128;

    /// <summary>
    /// The top level of a block-compressed DDS as red, green, blue, alpha bytes - or null for any other format, or one too short to hold it.
    /// </summary>
    public static GamePicture? Decode(byte[]? dds) => BlockTexture.Of(dds) is { } texture ? texture.Decode(0) : null;

    /// <summary>
    /// One level of blocks as red, green, blue, alpha bytes: <paramref name="width"/> by <paramref name="height"/> texels from
    /// <paramref name="offset"/> on, the blocks past its right and bottom edges cut short.
    /// </summary>
    internal static GamePicture Level(byte[] bytes, int offset, BlockFormat format, int width, int height)
    {
        int blocksWide = (width + 3) / 4;
        int blocksHigh = (height + 3) / 4;
        int bytesPerBlock = BlockTexture.BytesPerBlockOf(format);
        var rgba = new byte[width * height * 4];
        if ((long)blocksWide * blocksHigh >= ParallelBlocks)
        {
            Parallel.For(0, blocksHigh, row => Row(bytes, offset, format, bytesPerBlock, width, height, blocksWide, row, rgba));
        }
        else
        {
            for (var row = 0; row < blocksHigh; row++)
            {
                Row(bytes, offset, format, bytesPerBlock, width, height, blocksWide, row, rgba);
            }
        }

        return new GamePicture(width, height, rgba);
    }

    /// <summary>One row of blocks into the picture, the blocks past its right and bottom edges cut short.</summary>
    private static void Row(byte[] dds, int offset, BlockFormat format, int bytesPerBlock, int width, int height, int blocksWide, int row, byte[] rgba)
    {
        Span<uint> pixels = stackalloc uint[16];
        Span<uint> picture = MemoryMarshal.Cast<byte, uint>(rgba.AsSpan());
        int top = row * 4;
        int rows = Math.Min(4, height - top);
        for (var column = 0; column < blocksWide; column++)
        {
            ReadOnlySpan<byte> block = dds.AsSpan(offset + (((row * blocksWide) + column) * bytesPerBlock), bytesPerBlock);
            switch (format)
            {
                case BlockFormat.Bc1:
                    Bc1(block, pixels, threeColours: true);
                    break;
                case BlockFormat.Bc2:
                    Bc2(block, pixels);
                    break;
                case BlockFormat.Bc3:
                    Bc3(block, pixels);
                    break;
                case BlockFormat.Bc4:
                    Bc4(block, pixels);
                    break;
                case BlockFormat.Bc5:
                    Bc5(block, pixels);
                    break;
                default:
                    Bc7(block, pixels);
                    break;
            }

            int left = column * 4;
            int columns = Math.Min(4, width - left);
            for (var y = 0; y < rows; y++)
            {
                pixels.Slice(y * 4, columns).CopyTo(picture.Slice(((top + y) * width) + left, columns));
            }
        }
    }

    /// <summary>A pixel as four bytes in memory, red first.</summary>
    private static uint Rgba(int r, int g, int b, int a) => (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    /// <summary>
    /// One BC1 block: two 5:6:5 colours and the two between them, or the one between and transparent black - as Pfim works them out,
    /// in floats, each channel scaled to 255 and rounded half up after the blend, so the pixels are its pixels. BC2's and BC3's colour
    /// blocks are the same arithmetic with four colours whichever way round the two lie (<paramref name="threeColours"/> false).
    /// </summary>
    [SkipLocalsInit]
    private static void Bc1(ReadOnlySpan<byte> block, Span<uint> pixels, bool threeColours)
    {
        const float F5 = 255f / 31f;
        const float F6 = 255f / 63f;
        int c0 = block[0] | (block[1] << 8);
        int c1 = block[2] | (block[3] << 8);
        float r0 = ((c0 & 0xF800) >> 11) * F5, g0 = ((c0 & 0x7E0) >> 5) * F6, b0 = (c0 & 0x1F) * F5;
        float r1 = ((c1 & 0xF800) >> 11) * F5, g1 = ((c1 & 0x7E0) >> 5) * F6, b1 = (c1 & 0x1F) * F5;

        Span<uint> colours = stackalloc uint[4];
        colours[0] = Rgba((byte)(r0 + 0.5f), (byte)(g0 + 0.5f), (byte)(b0 + 0.5f), 255);
        colours[1] = Rgba((byte)(r1 + 0.5f), (byte)(g1 + 0.5f), (byte)(b1 + 0.5f), 255);
        if (c0 > c1 || !threeColours)
        {
            colours[2] = Blend(1f / 3);
            colours[3] = Blend(2f / 3);
        }
        else
        {
            colours[2] = Blend(0.5f);
            colours[3] = 0;
        }

        for (var y = 0; y < 4; y++)
        {
            int codes = block[4 + y];
            for (var x = 0; x < 4; x++)
            {
                pixels[(y * 4) + x] = colours[(codes >> (x * 2)) & 3];
            }
        }

        uint Blend(float blend) => Rgba(
            (byte)(r0 + (blend * (r1 - r0)) + 0.5f),
            (byte)(g0 + (blend * (g1 - g0)) + 0.5f),
            (byte)(b0 + (blend * (b1 - b0)) + 0.5f),
            255);
    }

    /// <summary>One BC2 block: sixteen alphas of four bits, each times 17, over BC1's four-colour block.</summary>
    [SkipLocalsInit]
    private static void Bc2(ReadOnlySpan<byte> block, Span<uint> pixels)
    {
        Bc1(block[8..], pixels, threeColours: false);
        ulong alphas = BinaryPrimitives.ReadUInt64LittleEndian(block);
        for (var pixel = 0; pixel < 16; pixel++)
        {
            uint alpha = (uint)((alphas >> (pixel * 4)) & 0xF) * 17;
            pixels[pixel] = (pixels[pixel] & 0x00FFFFFFu) | (alpha << 24);
        }
    }

    /// <summary>One BC3 block: BC4's ramp as the alpha, over BC1's four-colour block.</summary>
    [SkipLocalsInit]
    private static void Bc3(ReadOnlySpan<byte> block, Span<uint> pixels)
    {
        Bc1(block[8..], pixels, threeColours: false);
        Span<byte> ramp = stackalloc byte[8];
        Ramp(block[0], block[1], ramp);
        ulong codes = Codes(block);
        for (var pixel = 0; pixel < 16; pixel++)
        {
            pixels[pixel] = (pixels[pixel] & 0x00FFFFFFu) | ((uint)ramp[(int)((codes >> (pixel * 3)) & 7)] << 24);
        }
    }

    /// <summary>One BC4 block as a card reads it: its channel red, green and blue nought, alpha whole.</summary>
    [SkipLocalsInit]
    private static void Bc4(ReadOnlySpan<byte> block, Span<uint> pixels)
    {
        Span<byte> ramp = stackalloc byte[8];
        Ramp(block[0], block[1], ramp);
        ulong codes = Codes(block);
        for (var pixel = 0; pixel < 16; pixel++)
        {
            pixels[pixel] = ramp[(int)((codes >> (pixel * 3)) & 7)] | 0xFF000000u;
        }
    }

    /// <summary>One BC5 block as a card reads it: two BC4 ramps, red then green, blue nought, alpha whole.</summary>
    [SkipLocalsInit]
    private static void Bc5(ReadOnlySpan<byte> block, Span<uint> pixels)
    {
        Span<byte> reds = stackalloc byte[8];
        Span<byte> greens = stackalloc byte[8];
        Ramp(block[0], block[1], reds);
        Ramp(block[8], block[9], greens);
        ulong red = Codes(block), green = Codes(block[8..]);
        for (var pixel = 0; pixel < 16; pixel++)
        {
            int shift = pixel * 3;
            pixels[pixel] = reds[(int)((red >> shift) & 7)] | ((uint)greens[(int)((green >> shift) & 7)] << 8) | 0xFF000000u;
        }
    }

    /// <summary>
    /// A one-channel block's eight values: its two ends, then six between them where the first is greater, else four between, nought
    /// and whole - each the exact fraction rounded to the nearest byte (it is never a half), as BC4's are by Pfim and by the card.
    /// </summary>
    private static void Ramp(byte first, byte second, Span<byte> ramp)
    {
        ramp[0] = first;
        ramp[1] = second;
        if (first > second)
        {
            for (var step = 1; step < 7; step++)
            {
                ramp[1 + step] = (byte)(((((7 - step) * first) + (step * second)) * 2 + 7) / 14);
            }

            return;
        }

        for (var step = 1; step < 5; step++)
        {
            ramp[1 + step] = (byte)(((((5 - step) * first) + (step * second)) * 2 + 5) / 10);
        }

        ramp[6] = 0;
        ramp[7] = 255;
    }

    /// <summary>A one-channel block's sixteen three-bit codes, the first texel's lowest, after its two ends.</summary>
    private static ulong Codes(ReadOnlySpan<byte> block) => BinaryPrimitives.ReadUInt64LittleEndian(block) >> 16;

    /// <summary>One BC7 mode's layout - DirectXTex's ms_aInfo, as Pfim carries it.</summary>
    private readonly record struct Mode(
        int Partitions, int PartitionBits, int PBits, int RotationBits, int IndexModeBits, int IndexPrec, int IndexPrec2,
        int R, int G, int B, int A, int RP, int GP, int BP, int AP);

    private static readonly Mode[] Modes =
    [
        new(2, 4, 6, 0, 0, 3, 0, 4, 4, 4, 0, 5, 5, 5, 0),
        new(1, 6, 2, 0, 0, 3, 0, 6, 6, 6, 0, 7, 7, 7, 0),
        new(2, 6, 0, 0, 0, 2, 0, 5, 5, 5, 0, 5, 5, 5, 0),
        new(1, 6, 4, 0, 0, 2, 0, 7, 7, 7, 0, 8, 8, 8, 0),
        new(0, 0, 0, 2, 1, 2, 3, 5, 5, 5, 6, 5, 5, 5, 6),
        new(0, 0, 0, 2, 0, 2, 2, 7, 7, 7, 8, 7, 7, 7, 8),
        new(0, 0, 2, 0, 0, 4, 0, 7, 7, 7, 7, 8, 8, 8, 8),
        new(1, 6, 4, 0, 0, 2, 0, 5, 5, 5, 5, 6, 6, 6, 6),
    ];

    /// <summary>
    /// One BC7 block, as DirectXTex's D3DX_BC7::Decode reads it: the mode from the first set bit, its partition, rotation and index
    /// mode, the endpoints channel by channel with their p-bits, unquantised, the indices with their anchors a bit short, and each
    /// pixel interpolated in 64ths and rounded. A reserved mode is transparent black.
    /// </summary>
    [SkipLocalsInit]
    private static void Bc7(ReadOnlySpan<byte> block, Span<uint> pixels)
    {
        ulong lo = BinaryPrimitives.ReadUInt64LittleEndian(block);
        ulong hi = BinaryPrimitives.ReadUInt64LittleEndian(block[8..]);
        int number = BitOperations.TrailingZeroCount(lo);
        if (number >= 8)
        {
            pixels.Clear();
            return;
        }

        Mode mode = Modes[number];
        int at = number + 1;
        int shape = Take(lo, hi, ref at, mode.PartitionBits);
        int rotation = Take(lo, hi, ref at, mode.RotationBits);
        int indexMode = Take(lo, hi, ref at, mode.IndexModeBits);
        int ends = (mode.Partitions + 1) << 1;

        // ENDPOINTS AS BYTES, four channels each, read channel by channel across them all as the block lays them out.
        Span<int> ends4 = stackalloc int[24];
        Span<int> precision = [mode.R, mode.G, mode.B, mode.A];
        Span<int> withP = [mode.RP, mode.GP, mode.BP, mode.AP];
        for (var channel = 0; channel < 3; channel++)
        {
            for (var one = 0; one < ends; one++)
            {
                ends4[(one * 4) + channel] = Take(lo, hi, ref at, precision[channel]);
            }
        }

        for (var one = 0; one < ends; one++)
        {
            ends4[(one * 4) + 3] = mode.A != 0 ? Take(lo, hi, ref at, mode.A) : 255;
        }

        if (mode.PBits != 0)
        {
            Span<int> p = stackalloc int[6];
            for (var one = 0; one < mode.PBits; one++)
            {
                p[one] = Take(lo, hi, ref at, 1);
            }

            for (var one = 0; one < ends; one++)
            {
                int bit = p[one * mode.PBits / ends];
                for (var channel = 0; channel < 4; channel++)
                {
                    if (precision[channel] != withP[channel])
                    {
                        ends4[(one * 4) + channel] = (byte)((ends4[(one * 4) + channel] << 1) | bit);
                    }
                }
            }
        }

        for (var one = 0; one < ends; one++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                ends4[(one * 4) + channel] = Unquantised(ends4[(one * 4) + channel], withP[channel]);
            }

            ends4[(one * 4) + 3] = withP[3] > 0 ? Unquantised(ends4[(one * 4) + 3], withP[3]) : 255;
        }

        ReadOnlySpan<byte> regions = mode.Partitions switch
        {
            1 => Partitions2.Slice(shape * 16, 16),
            2 => Partitions3.Slice(shape * 16, 16),
            _ => default,
        };
        int anchor2 = mode.Partitions switch { 1 => Fixups2[shape], 2 => Fixups3[shape * 2], _ => -1 };
        int anchor3 = mode.Partitions == 2 ? Fixups3[(shape * 2) + 1] : -1;

        Span<int> first = stackalloc int[16];
        for (var pixel = 0; pixel < 16; pixel++)
        {
            bool anchor = pixel == 0 || pixel == anchor2 || pixel == anchor3;
            first[pixel] = Take(lo, hi, ref at, anchor ? mode.IndexPrec - 1 : mode.IndexPrec);
        }

        Span<int> second = stackalloc int[16];
        if (mode.IndexPrec2 != 0)
        {
            for (var pixel = 0; pixel < 16; pixel++)
            {
                second[pixel] = Take(lo, hi, ref at, pixel == 0 ? mode.IndexPrec2 - 1 : mode.IndexPrec2);
            }
        }

        // WHICH INDICES WEIGH THE COLOUR AND WHICH THE ALPHA, chosen once a block: one set for both, or the index mode's way round.
        bool apart = mode.IndexPrec2 != 0;
        bool swapped = apart && indexMode != 0;
        ReadOnlySpan<int> colourIndices = swapped ? second : first;
        ReadOnlySpan<int> alphaIndices = apart && !swapped ? second : first;
        ReadOnlySpan<byte> colourWeights = Weights(swapped ? mode.IndexPrec2 : mode.IndexPrec);
        ReadOnlySpan<byte> alphaWeights = Weights(apart && !swapped ? mode.IndexPrec2 : mode.IndexPrec);
        for (var pixel = 0; pixel < 16; pixel++)
        {
            int region = regions.IsEmpty ? 0 : regions[pixel];
            int low = region * 8, high = (region * 8) + 4;
            int colourWeight = colourWeights[colourIndices[pixel]];
            int alphaWeight = alphaWeights[alphaIndices[pixel]];

            int r = Between(ends4[low], ends4[high], colourWeight);
            int g = Between(ends4[low + 1], ends4[high + 1], colourWeight);
            int b = Between(ends4[low + 2], ends4[high + 2], colourWeight);
            int a = Between(ends4[low + 3], ends4[high + 3], alphaWeight);
            switch (rotation)
            {
                case 1:
                    (r, a) = (a, r);
                    break;
                case 2:
                    (g, a) = (a, g);
                    break;
                case 3:
                    (b, a) = (a, b);
                    break;
            }

            pixels[pixel] = Rgba(r, g, b, a);
        }
    }

    /// <summary>The next <paramref name="count"/> bits of the block, least first, from bit <paramref name="at"/>.</summary>
    private static int Take(ulong lo, ulong hi, ref int at, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        ulong bits = at >= 64 ? hi >> (at - 64)
            : at + count > 64 ? (lo >> at) | (hi << (64 - at))
            : lo >> at;
        at += count;
        return (int)(bits & ((1UL << count) - 1));
    }

    /// <summary>An endpoint channel of <paramref name="precision"/> bits widened to eight, its top bits repeated below.</summary>
    private static int Unquantised(int value, int precision)
    {
        int shifted = (byte)(value << (8 - precision));
        return (byte)(shifted | (shifted >> precision));
    }

    /// <summary>The weights in 64ths of an index of <paramref name="precision"/> bits.</summary>
    private static ReadOnlySpan<byte> Weights(int precision) => precision switch
    {
        2 => Weights2,
        3 => Weights3,
        _ => Weights4,
    };

    /// <summary>Between two endpoint channels by a weight in 64ths, rounded.</summary>
    private static int Between(int low, int high, int weight) => ((low * (64 - weight)) + (high * weight) + 32) >> 6;

    private static ReadOnlySpan<byte> Weights2 => [0, 21, 43, 64];

    private static ReadOnlySpan<byte> Weights3 => [0, 9, 18, 27, 37, 46, 55, 64];

    private static ReadOnlySpan<byte> Weights4 => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    /// <summary>Each of the 64 two-subset shapes' subset per pixel - DirectXTex's g_aPartitionTable[1].</summary>
    private static ReadOnlySpan<byte> Partitions2 =>
    [
        0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
        0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1,
        0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1,
        0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1,
        0, 0, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1,
        0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1,
        0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1,
        0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 1,
        0, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0,
        0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0,
        0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0,
        0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1,
        0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0,
        0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0,
        0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0,
        0, 0, 1, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 0, 0,
        0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0,
        0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0,
        0, 1, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 1, 0,
        0, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0,
        0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
        0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1,
        0, 1, 0, 1, 1, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 0,
        0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0,
        0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0,
        0, 1, 0, 1, 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1, 0,
        0, 1, 1, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 1,
        0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1, 0, 0, 1, 0, 1,
        0, 1, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 1, 0,
        0, 0, 0, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 0, 0, 0,
        0, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1, 0, 0,
        0, 0, 1, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1, 1, 0, 0,
        0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 1, 1, 0,
        0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0, 1, 1,
        0, 1, 1, 0, 0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1,
        0, 0, 0, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0, 0,
        0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0,
        0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0,
        0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0,
        0, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1,
        0, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 1,
        0, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0,
        0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 0,
        0, 1, 1, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 1,
        0, 1, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0, 1,
        0, 1, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1,
        0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1,
        0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
        0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0,
        0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0,
        0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1,
    ];

    /// <summary>Each of the 64 three-subset shapes' subset per pixel - g_aPartitionTable[2].</summary>
    private static ReadOnlySpan<byte> Partitions3 =>
    [
        0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 1, 2, 2, 2, 2,
        0, 0, 0, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 2, 1,
        0, 0, 0, 0, 2, 0, 0, 1, 2, 2, 1, 1, 2, 2, 1, 1,
        0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 1, 0, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2,
        0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 2, 2,
        0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2,
        0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2,
        0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2,
        0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2,
        0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2,
        0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2, 1, 2, 2, 2,
        0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0, 2, 2, 2, 0,
        0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2,
        0, 1, 1, 1, 0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0,
        0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2,
        0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1,
        0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2, 0, 2, 2, 2,
        0, 0, 0, 1, 0, 0, 0, 1, 2, 2, 2, 1, 2, 2, 2, 1,
        0, 0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2,
        0, 0, 0, 0, 1, 1, 0, 0, 2, 2, 1, 0, 2, 2, 1, 0,
        0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1, 0, 0, 0, 0,
        0, 0, 1, 2, 0, 0, 1, 2, 1, 1, 2, 2, 2, 2, 2, 2,
        0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1, 0, 1, 1, 0,
        0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1,
        0, 0, 2, 2, 1, 1, 0, 2, 1, 1, 0, 2, 0, 0, 2, 2,
        0, 1, 1, 0, 0, 1, 1, 0, 2, 0, 0, 2, 2, 2, 2, 2,
        0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1,
        0, 0, 0, 0, 2, 0, 0, 0, 2, 2, 1, 1, 2, 2, 2, 1,
        0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 2, 2, 2,
        0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 2, 0, 0, 1, 1,
        0, 0, 1, 1, 0, 0, 1, 2, 0, 0, 2, 2, 0, 2, 2, 2,
        0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0,
        0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0,
        0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0,
        0, 1, 2, 0, 2, 0, 1, 2, 1, 2, 0, 1, 0, 1, 2, 0,
        0, 0, 1, 1, 2, 2, 0, 0, 1, 1, 2, 2, 0, 0, 1, 1,
        0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0, 1, 1,
        0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2,
        0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1,
        0, 0, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2, 1, 1, 2, 2,
        0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 1, 1,
        0, 2, 2, 0, 1, 2, 2, 1, 0, 2, 2, 0, 1, 2, 2, 1,
        0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 0, 1, 0, 1,
        0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1,
        0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2,
        0, 2, 2, 2, 0, 1, 1, 1, 0, 2, 2, 2, 0, 1, 1, 1,
        0, 0, 0, 2, 1, 1, 1, 2, 0, 0, 0, 2, 1, 1, 1, 2,
        0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2,
        0, 2, 2, 2, 0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2,
        0, 0, 0, 2, 1, 1, 1, 2, 1, 1, 1, 2, 0, 0, 0, 2,
        0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2,
        0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2,
        0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2, 2, 2, 2, 2,
        0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2,
        0, 0, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2,
        0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1,
        0, 2, 2, 2, 1, 2, 2, 2, 0, 2, 2, 2, 1, 2, 2, 2,
        0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        0, 1, 1, 1, 2, 0, 1, 1, 2, 2, 0, 1, 2, 2, 2, 0,
    ];

    /// <summary>Each two-subset shape's second anchor pixel - g_aFixUp[1]; the first is always pixel nought.</summary>
    private static ReadOnlySpan<byte> Fixups2 =>
    [
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
        15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2,
        15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6,
        6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15,
    ];

    /// <summary>Each three-subset shape's second and third anchor pixels - g_aFixUp[2].</summary>
    private static ReadOnlySpan<byte> Fixups3 =>
    [
        3, 15, 3, 8, 15, 8, 15, 3, 8, 15, 3, 15, 15, 3, 15, 8,
        8, 15, 8, 15, 6, 15, 6, 15, 6, 15, 5, 15, 3, 15, 3, 8,
        3, 15, 3, 8, 8, 15, 15, 3, 3, 15, 3, 8, 6, 15, 10, 8,
        5, 3, 8, 15, 8, 6, 6, 10, 8, 15, 5, 15, 15, 10, 15, 8,
        8, 15, 15, 3, 3, 15, 5, 10, 6, 10, 10, 8, 8, 9, 15, 10,
        15, 6, 3, 15, 15, 8, 5, 15, 15, 3, 15, 6, 15, 6, 15, 8,
        3, 15, 15, 3, 5, 15, 5, 15, 5, 15, 8, 15, 5, 15, 10, 15,
        5, 15, 10, 15, 8, 15, 13, 15, 15, 3, 12, 15, 3, 15, 3, 8,
    ];
}
