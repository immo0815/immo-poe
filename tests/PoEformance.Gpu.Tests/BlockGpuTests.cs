using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Gpu.Tests;

/// <summary>
/// A texture kept as the file's blocks (v0.1.326) goes to the card as they are and draws the processor's picture of the same levels
/// decoded - without the card's copy ever being decoded.
/// </summary>
public class BlockGpuTests
{
    private const int Side = ModelGpuTests.Side;

    /// <summary>
    /// Every block format, magnified so the top level alone is read: random blocks, so every mode, both BC1 ways round and every ramp
    /// are read by the card's decoder and BlockArt's alike.
    /// </summary>
    [GpuFact]
    public void EVERYFormatKeptAsBlocksIsReadByTheCardAsTheProcessorDecodesIt()
    {
        foreach (int format in (int[])[71, 74, 77, 80, 83, 98])
        {
            var random = new Random(format);
            Drawn(Chain(format, 32, (level, wide, high, blocks) => random.NextBytes(blocks)), 0.3f, 0.2f);
        }
    }

    /// <summary>
    /// Shrunk through the file's own levels: each level a smooth ramp of its own tint, so the card read the levels the processor reads,
    /// the game's halvings and not ones built from the top.
    /// </summary>
    [GpuFact]
    public void THEFILESOwnLevelsAreTheOnesTheCardReadsShrunk()
    {
        foreach (int format in (int[])[71, 98])
        {
            Drawn(Chain(format, 1024, (level, wide, high, blocks) => Smooth(format, level, wide, high, blocks)), 0.3f, 0.6f);
        }
    }

    /// <summary>The card's picture of a texture kept as blocks against the processor's of another kept copy of the same file.</summary>
    private static void Drawn(byte[] dds, float turn, float tilt)
    {
        SkinnedMesh quad = Quad();
        Mipmaps card = Mipmaps.Kept(BlockTexture.Of(dds)) ?? throw new InvalidOperationException("not kept as blocks");
        Mipmaps processor = Mipmaps.Kept(BlockTexture.Of(dds))!;
        GamePicture expected = MeshPicture.Of(quad, new MeshPicture.Canvas(Side), turn, tilt, default, processor);
        var scene = new ModelScene(quad, Turn: turn, Tilt: tilt, Skin: card);
        using ModelGpu? gpu = ModelGpu.Warp(out string why);
        Assert.True(gpu is not null, why);
        Assert.True(gpu.Can(scene, out why), why);
        using ModelTarget target = gpu.Target(Side);
        Assert.True(gpu.Draw(target, scene, out why), why);
        ModelGpuTests.Compared(expected.Rgba, gpu.Read(target));

        // AS BLOCKS, AND NEVER DECODED FOR IT.
        Assert.Equal(1, gpu.BlockUploads);
        Assert.False(card.Unpacked);
    }

    /// <summary>A quad four square facing the camera, its coordinates the texture's whole.</summary>
    private static SkinnedMesh Quad() => SkinnedMesh.Of(
        [new(-2f, 0f, -2f), new(2f, 0f, -2f), new(2f, 0f, 2f), new(-2f, 0f, 2f)],
        [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY],
        [0, 1, 2, 0, 2, 3],
        new Vector3(-2f, 0f, -2f),
        new Vector3(2f, 0f, 2f),
        [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)]);

    /// <summary>A square DDS of every level down to one texel, each level's blocks filled by <paramref name="fill"/>.</summary>
    private static byte[] Chain(int format, int side, Action<int, int, int, byte[]> fill)
    {
        int perBlock = format is 71 or 80 ? 8 : 16;
        int levels = BlockTexture.FullChain(side, side);
        var all = new List<byte>();
        for (var level = 0; level < levels; level++)
        {
            int wide = Math.Max(1, side >> level), high = wide;
            var blocks = new byte[((wide + 3) / 4) * ((high + 3) / 4) * perBlock];
            fill(level, wide, high, blocks);
            all.AddRange(blocks);
        }

        using var stream = new MemoryStream();
        using var write = new BinaryWriter(stream);
        write.Write(Encoding.ASCII.GetBytes("DDS "));
        write.Write(124);
        write.Write(0x21007);
        write.Write(side);
        write.Write(side);
        write.Write(0);
        write.Write(0);
        write.Write(levels);
        write.Write(new byte[44]);
        write.Write(32);
        write.Write(0x4);
        write.Write(Encoding.ASCII.GetBytes("DX10"));
        write.Write(new byte[20]);
        write.Write(0x1000);
        write.Write(new byte[16]);
        write.Write(format);
        write.Write(3);
        write.Write(0);
        write.Write(1);
        write.Write(0);
        write.Write(all.ToArray());
        return stream.ToArray();
    }

    /// <summary>
    /// Each block one colour - the ramp across the level at its middle, tinted by the level - as a BC1 block (both ends the colour, every
    /// code nought) or a BC7 mode 6 block (one subset, both ends the colour in seven bits, even, every index nought).
    /// </summary>
    private static void Smooth(int format, int level, int wide, int high, byte[] blocks)
    {
        int perBlock = format == 71 ? 8 : 16;
        int across = (wide + 3) / 4;
        for (var at = 0; at < blocks.Length / perBlock; at++)
        {
            float u = (((at % across) * 4) + 2f) / Math.Max(wide, 4), v = (((at / across) * 4) + 2f) / Math.Max(high, 4);
            int r = (int)(40 + (160 * u)), g = (int)(40 + (160 * v)), b = 60 + (level * 14);
            Span<byte> block = blocks.AsSpan(at * perBlock, perBlock);
            block.Clear();
            if (format == 71)
            {
                int colour = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
                block[0] = block[2] = (byte)colour;
                block[1] = block[3] = (byte)(colour >> 8);
                continue;
            }

            // MODE 6: the mode bit, then red, green, blue and alpha twice each in seven bits, two p-bits, the indices - all nought past them.
            var bits = new BitWriter(block);
            bits.Write(1 << 6, 7);
            foreach (int channel in (int[])[r, g, b, 254])
            {
                bits.Write(channel >> 1, 7);
                bits.Write(channel >> 1, 7);
            }
        }
    }

    /// <summary>Bits into a block, least first.</summary>
    private ref struct BitWriter(Span<byte> block)
    {
        private readonly Span<byte> _block = block;
        private int _at;

        public void Write(int value, int count)
        {
            for (var bit = 0; bit < count; bit++, _at++)
            {
                if (((value >> bit) & 1) != 0)
                {
                    _block[_at >> 3] |= (byte)(1 << (_at & 7));
                }
            }
        }
    }
}
