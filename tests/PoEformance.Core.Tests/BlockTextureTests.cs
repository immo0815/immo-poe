using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// A block-compressed file's own levels, kept as its blocks and decoded only when asked (v0.1.326) - read where the file lays them,
/// the top one the picture decoded before, and decoded once whoever asks.
/// </summary>
public class BlockTextureTests
{
    /// <summary>
    /// A file with every level is kept whole: each level where the file lays it, decoded as that level alone would be, and nothing decoded
    /// until asked - its size and levels known before.
    /// </summary>
    [Theory]
    [InlineData(98, 64, 64)]
    [InlineData(71, 128, 32)]
    [InlineData(80, 16, 64)]
    [InlineData(83, 8, 8)]
    public void AFILEWithEveryLevelIsKeptAndEachLevelDecodesWhereTheFileLaysIt(int format, int width, int height)
    {
        int levels = BlockTexture.FullChain(width, height);
        (byte[] dds, byte[][] each) = Chain(format, width, height, levels, new Random(format));
        BlockTexture? blocks = BlockTexture.Of(dds);
        Assert.NotNull(blocks);
        Assert.True(blocks.Complete);
        Assert.Equal(levels, blocks.Levels);

        Mipmaps? kept = Mipmaps.Kept(blocks);
        Assert.NotNull(kept);
        Assert.False(kept.Unpacked);
        Assert.Equal((width, height, levels), (kept.Width, kept.Height, kept.Count));
        Assert.Same(blocks, kept.Blocks);

        // THE TOP LEVEL IS THE PICTURE DECODED BEFORE, and every other the file's own, as that level decodes on its own.
        Assert.Equal(GameArt.Decode(dds)!.Value.Rgba, kept.Top.Rgba);
        Assert.True(kept.Unpacked);
        for (var level = 0; level < levels; level++)
        {
            int wide = Math.Max(1, width >> level), high = Math.Max(1, height >> level);
            GamePicture alone = BlockArt.Decode(BlockArtTests.Dds(wide, high, BlockArtTests.Dx10(format), each[level]))!.Value;
            Assert.Equal((wide, high), (kept[level].Width, kept[level].Height));
            Assert.Equal(alone.Rgba, kept[level].Rgba);
        }

        // A LEVEL PAST THE LAST IS THE LAST, as a picture built from a decoded top answers.
        Assert.Same(kept[levels - 1].Rgba, kept[levels + 3].Rgba);
        Assert.True(kept.UnpackTicks > 0);
    }

    /// <summary>
    /// What is not a plain texture with every level is not kept: a file short of its levels, a cube, an array - each decodes its top as before
    /// and builds its levels from it, so no picture comes out coarser than it was.
    /// </summary>
    [Fact]
    public void ANYTHINGShortOfEveryLevelIsDecodedAsBefore()
    {
        (byte[] whole, _) = Chain(98, 64, 64, 7, new Random(1));

        // THE HEADER SAYS FEWER LEVELS THAN THE CHAIN HAS.
        (byte[] fewer, _) = Chain(98, 64, 64, 3, new Random(1));
        Assert.False(BlockTexture.Of(fewer)!.Complete);
        Assert.Null(Mipmaps.Kept(BlockTexture.Of(fewer)));

        // THE FILE ENDS BEFORE ITS LAST LEVELS: the levels it holds are where they are, the top still decodes.
        BlockTexture? cut = BlockTexture.Of(whole[..^20]);
        Assert.NotNull(cut);
        Assert.False(cut.Complete);
        Assert.True(cut.Levels < 7);
        Assert.Equal(GameArt.Decode(whole)!.Value.Rgba, BlockArt.Decode(whole[..^20])!.Value.Rgba);

        // A CUBE BY THE OLD HEADER, AN ARRAY BY THE DX10 ONE.
        byte[] cube = [.. whole];
        BitConverter.GetBytes(0x200 | 0xFC00).CopyTo(cube, 112);
        Assert.False(BlockTexture.Of(cube)!.Complete);
        byte[] array = [.. whole];
        BitConverter.GetBytes(6).CopyTo(array, 128 + 12);
        Assert.False(BlockTexture.Of(array)!.Complete);

        // AND A WHOLE ONE IS.
        Assert.True(BlockTexture.Of(whole)!.Complete);
    }

    /// <summary>Decoded once, whoever asks first: many threads reading at once and a background unpack beside them get the one set of levels.</summary>
    [Fact]
    public void ITISDecodedOnceWhoeverAsksFirst()
    {
        (byte[] dds, _) = Chain(98, 256, 256, 9, new Random(9));
        Mipmaps kept = Mipmaps.Kept(BlockTexture.Of(dds))!;
        var tops = new GamePicture[16];
        Parallel.Invoke(
            () => Mipmaps.Unpack([kept], 4, CancellationToken.None),
            () => Parallel.For(0, tops.Length, one => tops[one] = kept[one % 3]));
        for (var one = 0; one < tops.Length; one++)
        {
            Assert.Same(kept[one % 3].Rgba, tops[one].Rgba);
        }
    }

    /// <summary>An unpacking stopped before it began decodes nothing, and what it left is decoded at its first read.</summary>
    [Fact]
    public void ASTOPPEDUnpackingLeavesTheRestForTheirFirstRead()
    {
        (byte[] dds, _) = Chain(71, 64, 64, 7, new Random(2));
        Mipmaps kept = Mipmaps.Kept(BlockTexture.Of(dds))!;
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Mipmaps.Unpack([kept], 4, stop.Token);
        Assert.False(kept.Unpacked);
        Assert.Equal(GameArt.Decode(dds)!.Value.Rgba, kept.Top.Rgba);
    }

    /// <summary>A DDS of random blocks with <paramref name="levels"/> levels one after another, and each level's blocks apart.</summary>
    private static (byte[] Dds, byte[][] Levels) Chain(int format, int width, int height, int levels, Random random)
    {
        int perBlock = format is >= 70 and <= 72 or 80 ? 8 : 16;
        var each = new byte[levels][];
        for (var level = 0; level < levels; level++)
        {
            int wide = Math.Max(1, width >> level), high = Math.Max(1, height >> level);
            each[level] = new byte[((wide + 3) / 4) * ((high + 3) / 4) * perBlock];
            random.NextBytes(each[level]);
        }

        return (BlockArtTests.Dds(width, height, BlockArtTests.Dx10(format), [.. each.SelectMany(one => one)], levels), each);
    }
}
