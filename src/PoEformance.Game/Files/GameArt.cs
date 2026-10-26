using System.Buffers.Binary;
using System.Text;

namespace PoEformance.Game.Files;

/// <summary>A decoded picture: how big it is, and its pixels as red, green, blue, alpha.</summary>
public readonly record struct GamePicture(int Width, int Height, byte[] Rgba)
{
    /// <summary>Whether there is anything to draw.</summary>
    public bool Ready => Width > 0 && Height > 0 && Rgba.Length >= Width * Height * 4;
}

/// <summary>
/// An item's own picture, out of the install.
/// </summary>
/// <remarks>
/// An item in memory names its art - <c>Art/2DItems/Weapons/Bows/Bow1.dds</c> - and that file is
/// in the game's own bundles. What comes out of the bundle is one of THREE things, and only the
/// last is what anybody would guess (owner, who reverse-engineered a PoE2 install; neither
/// LibGGPK3 nor GameHelper2 covers this - the first is a PoE1-era tool and the second does not
/// decode textures at all).
///
/// A SIGNPOST. A file whose content starts with <c>*</c> is not a picture: the rest of it is the
/// path of the file that actually holds one, and THAT one may be compressed or not, or point
/// somewhere else again. It is how variants share one texture without storing it twice.
///
/// BROTLI, behind four bytes that are not part of it - the decompressed size, which is both the
/// buffer to allocate and the check that this really was one.
///
/// OR JUST A DDS, openable as it is.
///
/// Which of the three is worked out from the CONTENT rather than the name, because the name is
/// <c>.dds</c> in all three cases. Then it is a DDS in whichever block format that texture used
/// - BC1 through BC7 all turn up, and Pfim decodes all of them. The one-channel BC4 - grunge masks,
/// height maps - comes out of Pfim in a shape of its own and is laid out as a graphics card reads it.
/// </remarks>
public static class GameArt
{
    /// <summary>How many signposts will be followed before giving up.</summary>
    /// <remarks>A file that points at itself is a loop, and this runs while a window is open.</remarks>
    public const int MostHops = 8;

    /// <summary>The longest a signpost's path may be, past which it is not one.</summary>
    public const int LongestPath = 4096;

    /// <summary>How big a picture may be, so a wrong read cannot ask for a gigabyte.</summary>
    public const int WidestPicture = 8192;

    /// <summary>The largest a texture may say it decompresses to.</summary>
    /// <remarks>
    /// The size is four bytes off the front of the file, so a file that is not one at all is a
    /// buffer of whatever number happened to be there.
    /// </remarks>
    public const int BiggestTexture = 64 * 1024 * 1024;

    /// <summary>What a DDS starts with.</summary>
    private static ReadOnlySpan<byte> DdsMagic => "DDS "u8;

    /// <summary>
    /// Reads an art path out of an install and decodes it, or returns null when it will not.
    /// </summary>
    /// <param name="files">The install.</param>
    /// <param name="artPath">As the item spells it - the case and slashes do not matter.</param>
    public static GamePicture? Read(GameFiles? files, string? artPath)
    {
        byte[]? dds = ReadRaw(files, artPath);
        return dds is null ? null : Decode(dds);
    }

    /// <summary>
    /// Finds the bytes of the picture a path names, following headers and signposts.
    /// </summary>
    /// <remarks>Separate from decoding so that what the install holds can be checked on its own.</remarks>
    public static byte[]? ReadRaw(GameFiles? files, string? artPath)
        => files is null ? null : ReadRaw(files.Read, artPath);

    /// <summary>
    /// The same, through a read function rather than an open install.
    /// </summary>
    /// <remarks>
    /// FOR CALLERS THAT ALREADY READ THROUGH A FUNCTION, of which the monster model walk is one -
    /// and it was calling <see cref="Decode"/> on a bare read, which is this method without the
    /// two hops that matter. A texture that is a signpost, or one behind a compressed header,
    /// comes back as no picture: a monster with a mesh and no colour, and nothing anywhere saying
    /// why. The sample it was checked against happened to be a plain .dds, which is exactly the
    /// case that proves nothing.
    /// </remarks>
    public static byte[]? ReadRaw(Func<string, byte[]?>? read, string? artPath)
    {
        if (read is null || string.IsNullOrWhiteSpace(artPath))
        {
            return null;
        }

        string path = artPath.Replace('\\', '/').Trim();

        for (int hop = 0; hop < MostHops; hop++)
        {
            byte[]? found = read(path);
            if (found is null)
            {
                return null;
            }

            if (Signpost(found) is not { } pointsAt)
            {
                // Not a signpost, so this is the picture - compressed or not.
                return Unpack(found);
            }

            path = pointsAt;
        }

        return null;
    }

    /// <summary>
    /// Where a file points, or null when it is a picture rather than a signpost.
    /// </summary>
    /// <remarks>
    /// A star and then a path. Checked for being a PLAUSIBLE path rather than just for the star,
    /// because a compressed texture begins with its size and one size in every 256 begins with
    /// the same byte - and reading a picture as a path finds nothing at all.
    /// </remarks>
    public static string? Signpost(byte[]? content)
    {
        if (content is not { Length: > 1 } || content[0] != (byte)'*')
        {
            return null;
        }

        ReadOnlySpan<byte> rest = content.AsSpan(1);
        if (rest.Length > LongestPath)
        {
            return null;
        }

        foreach (byte one in rest)
        {
            // Printable, and nothing a path does not contain. A size read as text is almost
            // always out of this range at the first or second byte.
            if (one is (< 0x20 or > 0x7E) and not 0)
            {
                return null;
            }
        }

        string where = Encoding.UTF8.GetString(rest).TrimEnd('\0').Trim();
        return where.Length > 0 && where.Contains('.') ? where : null;
    }

    /// <summary>
    /// Gets the DDS out of a file, decompressing it when it is compressed.
    /// </summary>
    /// <remarks>
    /// The compressed ones are Brotli behind four bytes that are not part of it: the size it
    /// comes out at. Taken as the buffer to allocate AND as the check - a decompression that
    /// does not fill it exactly was not this.
    /// </remarks>
    public static byte[]? Unpack(byte[]? content)
    {
        if (content is not { Length: > 4 })
        {
            return null;
        }

        if (content.AsSpan().StartsWith(DdsMagic))
        {
            return content;
        }

        uint size = BitConverter.ToUInt32(content, 0);
        if (size is 0 or > BiggestTexture)
        {
            // Not a size, so not the compressed shape. Handed on as it is rather than refused -
            // Pfim may still know what it is, and refusing here would be this deciding that a
            // texture it does not recognise cannot be one.
            return content;
        }

        var plain = new byte[size];
        return System.IO.Compression.BrotliDecoder.TryDecompress(content.AsSpan(4), plain, out int written)
               && written == plain.Length
            ? plain
            : content;
    }

    /// <summary>
    /// Turns a DDS into plain pixels, or returns null when it is not one this can read.
    /// </summary>
    /// <remarks>
    /// Pfim answers in DIRECTX byte order, which is blue first - so handing its bytes straight
    /// to anything expecting red first gives a picture whose colours are swapped, and a swapped
    /// icon looks like a real icon. Only the top mipmap is taken; the smaller copies after it
    /// are the same picture again.
    /// </remarks>
    public static GamePicture? Decode(byte[]? dds)
    {
        if (dds is not { Length: > 4 })
        {
            return null;
        }

        if (TwoChannels(dds) is { } paired)
        {
            return paired;
        }

        if (TwoWideChannels(dds) is { } wide)
        {
            return wide;
        }

        // BC1 AND BC7 BY THE BLOCK DECODER, to Pfim's own pixels - see BlockArt for why.
        return BlockArt.Decode(dds) ?? DecodeWithPfim(dds);
    }

    /// <summary>
    /// A DDS decoded by Pfim alone, its top level as red, green, blue, alpha - or null where Pfim does not read it. What
    /// <see cref="BlockArt"/> is held against, and what reads every format it does not.
    /// </summary>
    public static GamePicture? DecodeWithPfim(byte[]? dds)
    {
        if (dds is not { Length: > 4 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(dds, writable: false);
            using Pfim.IImage image = Pfim.Pfimage.FromStream(stream);

            if (image.Width is <= 0 or > WidestPicture || image.Height is <= 0 or > WidestPicture)
            {
                return null;
            }

            var rgba = new byte[image.Width * image.Height * 4];
            return Lay(image, rgba, Red(dds))
                ? new GamePicture(image.Width, image.Height, rgba)
                : null;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException
                                              or ArgumentException or IndexOutOfRangeException
                                              or InvalidOperationException or OverflowException)
        {
            // Not a texture this can read, which is an item drawn as its name rather than a
            // stash that fails to open.
            return null;
        }
    }

    /// <summary>The DXGI format number of two unsigned bytes a texel, red then green.</summary>
    private const uint R8G8Unorm = 49;

    /// <summary>
    /// A DDS of DXGI's R8G8_UNORM, read here because Pfim does not: its top level as a graphics card reads it, (r, g, 0, 1) - or null for any other.
    /// </summary>
    /// <remarks>
    /// THE GROUND'S BLEND MASKS ARE THIS FORMAT - Port's newmask.dds, mask_3x3.dds and mask_ground.dds, all 256x512 - and
    /// the engine reads red and green apart (ground.ffx: GetEngineMask the red, GetUserMask the green), so the two channels
    /// are the whole of what is in them. Uncompressed and tightly packed, two bytes a texel, right after the DX10 header.
    /// </remarks>
    private static GamePicture? TwoChannels(byte[] dds)
    {
        const int header = 128;
        const int dx10 = 20;
        if (dds.Length < header + dx10
            || !dds.AsSpan(0, 4).SequenceEqual("DDS "u8)
            || (BitConverter.ToUInt32(dds, 80) & 0x4) == 0
            || !dds.AsSpan(84, 4).SequenceEqual("DX10"u8)
            || BitConverter.ToUInt32(dds, header) != R8G8Unorm)
        {
            return null;
        }

        int height = BitConverter.ToInt32(dds, 12);
        int width = BitConverter.ToInt32(dds, 16);
        if (width is <= 0 or > WidestPicture || height is <= 0 or > WidestPicture
            || dds.Length < header + dx10 + ((long)width * height * 2))
        {
            return null;
        }

        var rgba = new byte[width * height * 4];
        ReadOnlySpan<byte> texels = dds.AsSpan(header + dx10, width * height * 2);
        for (var at = 0; at < width * height; at++)
        {
            rgba[at * 4] = texels[at * 2];
            rgba[(at * 4) + 1] = texels[(at * 2) + 1];
            rgba[(at * 4) + 3] = 255;
        }

        return new GamePicture(width, height, rgba);
    }

    /// <summary>
    /// A DDS of two 16-bit channels a texel, red then green - unsigned and normalised, or halves - read here before Pfim, which reads none
    /// of them (or a 32-bit one as four bytes): its top level as a card reads it, (r, g, 0, 1) at a byte a channel, a half held to nought
    /// and one. Null for any other.
    /// </summary>
    /// <remarks>
    /// WRITTEN FOR THE ROCKS' SPRAY (CrashingFoamSurface), whose wave noise is <c>flow_muddle_blur_01_R16G16.dds</c> in a folder named
    /// <c>16bit</c> - the name says the channels and not which of the formats spelling them its header uses, so every one is read by
    /// what the header says: DXGI's R16G16_UNORM (35) and R16G16_FLOAT (34) after a DX10 header; the old Direct3D numbers in the
    /// four-character code, D3DFMT_G16R16 (34) and D3DFMT_G16R16F (112); and the old uncompressed header of 32 bits, red in the low
    /// half and green in the high, no blue and no alpha. The noise is read at its green, a byte of which is a 255th of a wave.
    /// </remarks>
    private static GamePicture? TwoWideChannels(byte[] dds)
    {
        const int header = 128;
        const int dx10 = 20;
        if (dds.Length < header || !dds.AsSpan(0, 4).SequenceEqual("DDS "u8))
        {
            return null;
        }

        uint flags = BitConverter.ToUInt32(dds, 80);
        uint code = BitConverter.ToUInt32(dds, 84);
        bool? halves = null;
        int offset = header;
        if ((flags & 0x4) != 0)
        {
            if (dds.AsSpan(84, 4).SequenceEqual("DX10"u8) && dds.Length >= header + dx10)
            {
                uint format = BitConverter.ToUInt32(dds, header);
                halves = format switch { 35 => false, 34 => true, _ => null };
                offset = header + dx10;
            }
            else
            {
                halves = code switch { 34 => false, 112 => true, _ => null };
            }
        }
        else if ((flags & 0x40) != 0
                 && BitConverter.ToUInt32(dds, 88) == 32
                 && BitConverter.ToUInt32(dds, 92) == 0x0000FFFFu
                 && BitConverter.ToUInt32(dds, 96) == 0xFFFF0000u
                 && BitConverter.ToUInt32(dds, 100) == 0
                 && BitConverter.ToUInt32(dds, 104) == 0)
        {
            halves = false;
        }

        int height = BitConverter.ToInt32(dds, 12);
        int width = BitConverter.ToInt32(dds, 16);
        if (halves is not { } half
            || width is <= 0 or > WidestPicture || height is <= 0 or > WidestPicture
            || dds.Length < offset + ((long)width * height * 4))
        {
            return null;
        }

        var rgba = new byte[width * height * 4];
        ReadOnlySpan<byte> texels = dds.AsSpan(offset, width * height * 4);
        for (var at = 0; at < width * height; at++)
        {
            ReadOnlySpan<byte> one = texels.Slice(at * 4, 4);
            rgba[at * 4] = Wide(one[..2], half);
            rgba[(at * 4) + 1] = Wide(one[2..], half);
            rgba[(at * 4) + 3] = 255;
        }

        return new GamePicture(width, height, rgba);
    }

    /// <summary>A 16-bit channel at a byte: an unsigned one rounded from 65,535ths, a half held to nought and one.</summary>
    private static byte Wide(ReadOnlySpan<byte> two, bool half)
    {
        if (!half)
        {
            return (byte)(((BinaryPrimitives.ReadUInt16LittleEndian(two) * 255) + 32767) / 65535);
        }

        float value = (float)BinaryPrimitives.ReadHalfLittleEndian(two);
        return (byte)MathF.Round((value > 0f ? (value < 1f ? value : 1f) : 0f) * 255f);
    }

    /// <summary>
    /// Whether a DDS says it is BC4, unsigned: one channel, and that channel red.
    /// </summary>
    /// <remarks>
    /// ASKED OF THE HEADER, NOT OF PFIM. Pfim hands a BC4 back as <c>Rgb8</c>, one byte a pixel,
    /// and hands other one-byte formats back the same way - but a graphics card reads them
    /// differently: a BC4 as (r, 0, 0, 1), a luminance texture as (l, l, l, 1), an alpha one as
    /// (0, 0, 0, a). Only the first is known to be what the byte is, so only that one is taken.
    /// The four-character codes and the DX10 header's format number are the ones Pfim itself
    /// reads a BC4 by; the signed and typeless kinds are left out.
    /// </remarks>
    private static bool Red(byte[] dds)
    {
        const int pixelFlags = 80;
        const int fourCharacters = 84;
        const int dx10Format = 128;
        if (dds.Length < dx10Format || (BitConverter.ToUInt32(dds, pixelFlags) & 0x4) == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> code = dds.AsSpan(fourCharacters, 4);
        return code.SequenceEqual("ATI1"u8)
            || code.SequenceEqual("BC4U"u8)
            || (code.SequenceEqual("DX10"u8) && dds.Length >= dx10Format + 4 && BitConverter.ToUInt32(dds, dx10Format) == 80);
    }

    /// <summary>Copies Pfim's pixels out as red, green, blue, alpha.</summary>
    /// <param name="image">What Pfim decoded.</param>
    /// <param name="rgba">Where the pixels go.</param>
    /// <param name="red">Whether a one-byte pixel is a BC4's red - see <see cref="Red"/>.</param>
    private static bool Lay(Pfim.IImage image, byte[] rgba, bool red)
    {
        int wide = image.Format switch
        {
            Pfim.ImageFormat.Rgba32 => 4,
            Pfim.ImageFormat.Rgb24 => 3,
            Pfim.ImageFormat.Rgb8 when red => 1,
            _ => 0,
        };

        if (wide == 0 || image.Stride < image.Width * wide)
        {
            return false;
        }

        byte[] from = image.Data;
        for (int y = 0; y < image.Height; y++)
        {
            int reading = y * image.Stride;
            int writing = y * image.Width * 4;

            if (reading + (image.Width * wide) > image.DataLen)
            {
                return false;
            }

            for (int x = 0; x < image.Width; x++)
            {
                int one = reading + (x * wide);
                int other = writing + (x * 4);
                if (wide == 1)
                {
                    // A BC4 AS A GRAPHICS CARD READS ONE: its channel is red, green and blue are
                    // nought, alpha is whole.
                    rgba[other] = from[one];
                    rgba[other + 3] = 255;
                    continue;
                }

                rgba[other] = from[one + 2];       // blue first out of Pfim, red first into this
                rgba[other + 1] = from[one + 1];
                rgba[other + 2] = from[one];
                rgba[other + 3] = wide == 4 ? from[one + 3] : (byte)255;
            }
        }

        return true;
    }
}
