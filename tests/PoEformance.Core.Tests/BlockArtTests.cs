using System.Diagnostics;
using System.Text;
using PoEformance.Game.Files;
using Xunit.Abstractions;

namespace PoEformance.Core.Tests;

/// <summary>The BC1 and BC7 block decoder, held against Pfim pixel for pixel - it replaced Pfim for those two, and must draw the same.</summary>
/// <remarks>
/// RUN ALONE (<see cref="AloneOnTheMachine"/>): the decoder is faster than Pfim by decoding its rows on every core, and Pfim on one.
/// While the other test classes ran beside it on every core the two came out equal (CI, v0.1.314: Pfim 342.2 ms, blocks 343.4 ms),
/// and the best of three does not help when all three are taken on a full machine.
/// </remarks>
[Collection(nameof(AloneOnTheMachine))]
public class BlockArtTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void EVERYBC7ModeComesOutAsPfimDecodesIt(int mode)
    {
        // RANDOM BLOCKS OF ONE MODE: its bit set after `mode` noughts - eight noughts is the reserved mode, transparent black.
        var random = new Random(7000 + mode);
        byte[] blocks = new byte[16 * 16 * 16];
        random.NextBytes(blocks);
        for (var at = 0; at < blocks.Length; at += 16)
        {
            blocks[at] = mode < 8 ? (byte)((blocks[at] & ~((2 << mode) - 1)) | (1 << mode)) : (byte)0;
        }

        Same(Dds(64, 64, Dx10(98), blocks));
    }

    [Fact]
    public void BC7OfEveryModeAtOnceCutAtItsEdgesAndLargeEnoughForEveryCoreIsPfimsToo()
    {
        var random = new Random(8);
        foreach ((int width, int height) in new[] { (13, 7), (1, 1), (6, 3), (520, 516) })
        {
            int count = ((width + 3) / 4) * ((height + 3) / 4);
            byte[] blocks = new byte[count * 16];
            random.NextBytes(blocks);
            Same(Dds(width, height, Dx10(99), blocks));
        }
    }

    [Fact]
    public void BC1BothWaysRoundItsTwoColoursIsPfims()
    {
        // RANDOM ENDPOINTS FALL BOTH WAYS ROUND - four colours where the first is greater, three and transparent black where it is not -
        // and some are equal.
        var random = new Random(1);
        foreach ((int width, int height, byte[] header) in new[] { (64, 64, FourCc("DXT1")), (13, 7, Dx10(71)), (516, 520, Dx10(72)) })
        {
            int count = ((width + 3) / 4) * ((height + 3) / 4);
            byte[] blocks = new byte[count * 8];
            random.NextBytes(blocks);
            for (var at = 0; at < blocks.Length; at += 64)
            {
                blocks[at + 2] = blocks[at];
                blocks[at + 3] = blocks[at + 1];
            }

            Same(Dds(width, height, header, blocks));
        }
    }

    /// <summary>BC2 - sixteen nibbles of alpha over BC1's four-colour block - is Pfim's pixel for pixel, by its code and its DXGI number.</summary>
    [Fact]
    public void BC2IsPfims()
    {
        var random = new Random(2);
        foreach ((int width, int height, byte[] header) in new[] { (64, 64, FourCc("DXT3")), (13, 7, Dx10(74)) })
        {
            byte[] blocks = RandomBlocks(random, width, height, 16);
            Same(Dds(width, height, header, blocks));
        }
    }

    /// <summary>BC3's colours are Pfim's; its alpha ramp the card's - the exact fraction rounded, where Pfim truncates - a level apart at most.</summary>
    [Fact]
    public void BC3IsPfimsInColourAndTheCardsInAlpha()
    {
        var random = new Random(3);
        foreach ((int width, int height, byte[] header) in new[] { (64, 64, FourCc("DXT5")), (13, 7, Dx10(77)) })
        {
            byte[] blocks = RandomBlocks(random, width, height, 16);
            byte[] dds = Dds(width, height, header, blocks);
            Near(dds, channel => channel == 3);
            Ramps(dds, blocks, width, height, 16, 0, 3);
        }
    }

    /// <summary>BC4 reads as a card reads one, red alone: Pfim's to the byte, which rounds its ramp as the card does.</summary>
    [Fact]
    public void BC4IsPfimsAndRedAlone()
    {
        var random = new Random(4);
        foreach ((int width, int height, byte[] header) in new[] { (64, 64, FourCc("ATI1")), (13, 7, FourCc("BC4U")), (516, 520, Dx10(80)) })
        {
            byte[] blocks = RandomBlocks(random, width, height, 8);
            byte[] dds = Dds(width, height, header, blocks);
            Same(dds);
            Ramps(dds, blocks, width, height, 8, 0, 0);
        }
    }

    /// <summary>BC5 reads as a card reads one, red and green: each ramp the card's, Pfim's truncated ones a level apart at most.</summary>
    [Fact]
    public void BC5IsTheCardsRedAndGreen()
    {
        var random = new Random(5);
        foreach ((int width, int height, byte[] header) in new[] { (64, 64, FourCc("ATI2")), (13, 7, FourCc("BC5U")), (64, 32, Dx10(83)) })
        {
            byte[] blocks = RandomBlocks(random, width, height, 16);
            byte[] dds = Dds(width, height, header, blocks);
            Near(dds, channel => channel is 0 or 1);
            Ramps(dds, blocks, width, height, 16, 0, 0);
            Ramps(dds, blocks, width, height, 16, 8, 1);
            GamePicture ours = BlockArt.Decode(dds)!.Value;
            for (var at = 0; at < ours.Rgba.Length; at += 4)
            {
                Assert.Equal((0, 255), (ours.Rgba[at + 2], ours.Rgba[at + 3]));
            }
        }
    }

    [Fact]
    public void WHATItDoesNotReadItLeavesToPfim()
    {
        Assert.Null(BlockArt.Decode(TileFilesTests.Dds()));
        // SIGNED BC5 AND BC6H's HALVES: what a card reads them as is not a byte a channel.
        Assert.Null(BlockArt.Decode(Dds(64, 64, Dx10(84), new byte[16 * 16 * 16])));
        Assert.Null(BlockArt.Decode(Dds(64, 64, Dx10(95), new byte[16 * 16 * 16])));

        // A FILE TOO SHORT FOR ITS TOP LEVEL is not read here either.
        Assert.Null(BlockArt.Decode(Dds(64, 64, Dx10(98), new byte[100])));
    }

    [Fact]
    public void ITISFasterThanPfim()
    {
        var random = new Random(3);
        byte[] blocks = new byte[256 * 256 * 16];
        random.NextBytes(blocks);
        byte[] dds = Dds(1024, 1024, Dx10(98), blocks);
        GameArt.DecodeWithPfim(dds);
        BlockArt.Decode(dds);

        // THE BEST OF THREE EACH, so a test machine busy with other tests does not decide it.
        double pfim = double.MaxValue, ours = double.MaxValue;
        for (var again = 0; again < 3; again++)
        {
            long started = Stopwatch.GetTimestamp();
            GameArt.DecodeWithPfim(dds);
            pfim = Math.Min(pfim, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            started = Stopwatch.GetTimestamp();
            BlockArt.Decode(dds);
            ours = Math.Min(ours, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        output.WriteLine($"BC7 1024 square: Pfim {pfim:0.0} ms, blocks {ours:0.0} ms");
        Assert.True(ours < pfim, $"Pfim {pfim:0.0} ms, blocks {ours:0.0} ms");
    }

    /// <summary>Random blocks enough for a picture, a block's ends both ways round - equal now and then.</summary>
    private static byte[] RandomBlocks(Random random, int width, int height, int perBlock)
    {
        byte[] blocks = new byte[((width + 3) / 4) * ((height + 3) / 4) * perBlock];
        random.NextBytes(blocks);
        for (var at = 0; at < blocks.Length; at += perBlock * 9)
        {
            blocks[at + 1] = blocks[at];
        }

        return blocks;
    }

    /// <summary>
    /// The blocks' one-channel ramps held against the card's arithmetic written out apart: each texel the exact fraction between the
    /// block's two ends at <paramref name="at"/> its code names, rounded - in <paramref name="channel"/> of the picture.
    /// </summary>
    private static void Ramps(byte[] dds, byte[] blocks, int width, int height, int perBlock, int at, int channel)
    {
        GamePicture ours = BlockArt.Decode(dds) ?? throw new InvalidOperationException("the blocks did not read it");
        int wide = (width + 3) / 4;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int block = (((y / 4) * wide) + (x / 4)) * perBlock;
                int first = blocks[block + at], second = blocks[block + at + 1];
                ulong codes = BitConverter.ToUInt64(blocks, block + at) >> 16;
                int code = (int)((codes >> ((((y % 4) * 4) + (x % 4)) * 3)) & 7);
                double value = code switch
                {
                    0 => first,
                    1 => second,
                    _ when first > second => (((8 - code) * first) + ((code - 1) * second)) / 7.0,
                    6 => 0,
                    7 => 255,
                    _ => (((6 - code) * first) + ((code - 1) * second)) / 5.0,
                };
                Assert.Equal((byte)Math.Round(value), ours.Rgba[(((y * width) + x) * 4) + channel]);
            }
        }
    }

    /// <summary>Pfim's pixels to the byte but in the channels <paramref name="ramped"/> picks, which may be a level apart.</summary>
    private static void Near(byte[] dds, Func<int, bool> ramped)
    {
        GamePicture pfim = GameArt.DecodeWithPfim(dds) ?? throw new InvalidOperationException("Pfim did not read it");
        GamePicture ours = BlockArt.Decode(dds) ?? throw new InvalidOperationException("the blocks did not read it");
        Assert.Equal((pfim.Width, pfim.Height), (ours.Width, ours.Height));
        var apart = 0;
        for (var at = 0; at < pfim.Rgba.Length; at++)
        {
            int difference = Math.Abs(pfim.Rgba[at] - ours.Rgba[at]);
            Assert.True(ramped(at % 4) ? difference <= 1 : difference == 0, $"byte {at}: Pfim {pfim.Rgba[at]}, blocks {ours.Rgba[at]}");
            apart += difference;
        }

        // AND THEY DO DIFFER where Pfim truncates: a ramp of random ends falls on a fraction most of the time.
        Assert.True(apart > 0, "the ramps came out as Pfim's truncated ones");
        Assert.Equal(ours.Rgba, GameArt.Decode(dds)!.Value.Rgba);
    }

    private static void Same(byte[] dds)
    {
        GamePicture pfim = GameArt.DecodeWithPfim(dds) ?? throw new InvalidOperationException("Pfim did not read it");
        GamePicture ours = BlockArt.Decode(dds) ?? throw new InvalidOperationException("the blocks did not read it");
        Assert.Equal((pfim.Width, pfim.Height), (ours.Width, ours.Height));
        for (var at = 0; at < pfim.Rgba.Length; at += 4)
        {
            if (!pfim.Rgba.AsSpan(at, 4).SequenceEqual(ours.Rgba.AsSpan(at, 4)))
            {
                Assert.Fail($"pixel {at / 4 % pfim.Width}, {at / 4 / pfim.Width}: Pfim {string.Join(' ', pfim.Rgba[at..(at + 4)])}, blocks {string.Join(' ', ours.Rgba[at..(at + 4)])}");
            }
        }

        // AND GameArt TAKES THE BLOCKS' PICTURE.
        Assert.Equal(ours.Rgba, GameArt.Decode(dds)!.Value.Rgba);
    }

    /// <summary>A DX10 header naming a DXGI format, a 2D texture of one.</summary>
    internal static byte[] Dx10(int format)
    {
        byte[] header = new byte[20];
        BitConverter.GetBytes(format).CopyTo(header, 0);
        BitConverter.GetBytes(3).CopyTo(header, 4);
        BitConverter.GetBytes(1).CopyTo(header, 12);
        return header;
    }

    /// <summary>No DX10 header - the four characters name the format.</summary>
    internal static byte[] FourCc(string code) => Encoding.ASCII.GetBytes(code);

    /// <summary>
    /// A DDS: the old header saying <paramref name="levels"/> levels, the DX10 one where <paramref name="format"/> is twenty bytes, then the blocks.
    /// </summary>
    internal static byte[] Dds(int width, int height, byte[] format, byte[] blocks, int levels = 1)
    {
        using var stream = new MemoryStream();
        using var write = new BinaryWriter(stream);
        write.Write(Encoding.ASCII.GetBytes("DDS "));
        write.Write(124);
        write.Write(levels > 1 ? 0x21007 : 0x1007);
        write.Write(height);
        write.Write(width);
        write.Write(0);
        write.Write(0);
        write.Write(levels);
        write.Write(new byte[44]);
        write.Write(32);
        write.Write(0x4);
        write.Write(format.Length == 4 ? format : Encoding.ASCII.GetBytes("DX10"));
        write.Write(new byte[20]);
        write.Write(0x1000);
        write.Write(new byte[16]);
        if (format.Length != 4)
        {
            write.Write(format);
        }

        write.Write(blocks);
        return stream.ToArray();
    }
}

/// <summary>Tests that measure time: xUnit runs this collection after every other one, with nothing beside it.</summary>
[CollectionDefinition(nameof(AloneOnTheMachine), DisableParallelization = true)]
public sealed class AloneOnTheMachine;
