using System.Buffers.Binary;

namespace PoEformance.Game.Files;

/// <summary>A texture's top level as a graphics card hands it to a shader: four floats a texel - red, green, blue, alpha - row by row.</summary>
/// <param name="Width">Texels across.</param>
/// <param name="Height">Texels down.</param>
/// <param name="Texels">Four floats a texel, a channel the format lacks as a card fills it: nought, or one for alpha.</param>
/// <param name="Format">What the header says the texels are, for a reader to check.</param>
public sealed record FloatPicture(int Width, int Height, float[] Texels, string Format)
{
    /// <summary>One channel of one texel.</summary>
    public float At(int x, int y, int channel) => Texels[((y * Width) + x) * 4 + channel];
}

/// <summary>
/// A DDS read as a shader reads it, in floats - for the engine's lookup textures, whose values are numbers rather than colours.
/// </summary>
/// <remarks>
/// WRITTEN FOR <c>Art/2DArt/Lookup/surface_ripple.dds</c>, the engine's ripple texture: 128 square, one level, and its header's
/// four-character code is not four characters at all but the number 116 - the old Direct3D format numbers stand there for
/// formats no letters name, 116 being D3DFMT_A32B32G32R32F, four floats a texel, red first in memory as DXGI's
/// R32G32B32A32_FLOAT is. <see cref="GameArt"/> hands the decoder colours of a byte a channel, which a height that goes below
/// nought or a slope cannot be. So the float formats - the old numbers 111 to 116 and DXGI's float formats - are read here,
/// tightly packed after the header as the format's pitch says, and anything else goes through <see cref="GameArt.Decode"/> and
/// comes out a byte over 255, an sRGB format's colour undone as a card undoes it.
/// </remarks>
public static class FloatArt
{
    private const int Header = 128;
    private const int Dx10Header = 20;

    /// <summary>The DDS's top level as floats, or null - with why - where it is not a DDS this can read.</summary>
    public static FloatPicture? Decode(byte[]? dds, out string why)
    {
        if (dds is not { Length: >= Header } || !dds.AsSpan(0, 4).SequenceEqual("DDS "u8))
        {
            why = "not a DDS";
            return null;
        }

        int height = BitConverter.ToInt32(dds, 12), width = BitConverter.ToInt32(dds, 16);
        if (width is <= 0 or > GameArt.WidestPicture || height is <= 0 or > GameArt.WidestPicture)
        {
            why = $"a size of {width} x {height}";
            return null;
        }

        bool fourCc = (BitConverter.ToUInt32(dds, 80) & 0x4) != 0;
        uint code = BitConverter.ToUInt32(dds, 84);
        if (fourCc && Legacy(code) is { } legacy)
        {
            return Floats(dds, Header, width, height, legacy, $"Direct3D format {code} ({legacy.Name})", out why);
        }

        if (fourCc && dds.AsSpan(84, 4).SequenceEqual("DX10"u8) && dds.Length >= Header + Dx10Header)
        {
            uint format = BitConverter.ToUInt32(dds, Header);
            if (Dxgi(format) is { } dxgi)
            {
                return Floats(dds, Header + Dx10Header, width, height, dxgi, $"DXGI format {format} ({dxgi.Name})", out why);
            }
        }

        if (GameArt.Decode(dds) is not { Ready: true } picture)
        {
            why = fourCc ? $"a format this does not read (code {code})" : "a format this does not read";
            return null;
        }

        bool srgb = fourCc && dds.AsSpan(84, 4).SequenceEqual("DX10"u8) && dds.Length >= Header + 4
                    && BitConverter.ToUInt32(dds, Header) is 29 or 72 or 75 or 78 or 91 or 93 or 99;
        var texels = new float[picture.Width * picture.Height * 4];
        for (var at = 0; at < texels.Length; at++)
        {
            float value = picture.Rgba[at] / 255f;
            texels[at] = srgb && (at & 3) != 3 ? Linear(value) : value;
        }

        why = string.Empty;
        return new FloatPicture(picture.Width, picture.Height, texels, srgb ? "bytes, sRGB undone" : "bytes over 255");
    }

    /// <summary>A float format: its name, how many channels it holds and how wide each is in bytes.</summary>
    private sealed record Layout(string Name, int Channels, int Width);

    /// <summary>The old Direct3D float formats, by number - D3DFMT_R16F to D3DFMT_A32B32G32R32F, red first in memory.</summary>
    private static Layout? Legacy(uint code) => code switch
    {
        111 => new Layout("R16F", 1, 2),
        112 => new Layout("G16R16F", 2, 2),
        113 => new Layout("A16B16G16R16F", 4, 2),
        114 => new Layout("R32F", 1, 4),
        115 => new Layout("G32R32F", 2, 4),
        116 => new Layout("A32B32G32R32F", 4, 4),
        _ => null,
    };

    /// <summary>DXGI's float formats, by number.</summary>
    private static Layout? Dxgi(uint format) => format switch
    {
        2 => new Layout("R32G32B32A32_FLOAT", 4, 4),
        6 => new Layout("R32G32B32_FLOAT", 3, 4),
        10 => new Layout("R16G16B16A16_FLOAT", 4, 2),
        16 => new Layout("R32G32_FLOAT", 2, 4),
        34 => new Layout("R16G16_FLOAT", 2, 2),
        41 => new Layout("R32_FLOAT", 1, 4),
        54 => new Layout("R16_FLOAT", 1, 2),
        _ => null,
    };

    /// <summary>The top level's texels, tightly packed from <paramref name="offset"/>, each channel the format lacks as a card fills it.</summary>
    private static FloatPicture? Floats(byte[] dds, int offset, int width, int height, Layout layout, string format, out string why)
    {
        long size = (long)width * height * layout.Channels * layout.Width;
        if (dds.Length < offset + size)
        {
            why = $"{format}: {dds.Length - offset} bytes of texels where {size} were due";
            return null;
        }

        var texels = new float[width * height * 4];
        ReadOnlySpan<byte> from = dds.AsSpan(offset, (int)size);
        int step = layout.Channels * layout.Width;
        for (var texel = 0; texel < width * height; texel++)
        {
            ReadOnlySpan<byte> one = from.Slice(texel * step, step);
            int at = texel * 4;
            texels[at + 3] = 1f;
            for (var channel = 0; channel < layout.Channels; channel++)
            {
                ReadOnlySpan<byte> bytes = one.Slice(channel * layout.Width, layout.Width);
                texels[at + channel] = layout.Width == 2
                    ? (float)BinaryPrimitives.ReadHalfLittleEndian(bytes)
                    : BinaryPrimitives.ReadSingleLittleEndian(bytes);
            }
        }

        why = string.Empty;
        return new FloatPicture(width, height, texels, format);
    }

    /// <summary>An sRGB value made linear, as a card does when it reads an sRGB format.</summary>
    private static float Linear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
}
