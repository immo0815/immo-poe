using System.Diagnostics;

namespace PoEformance.Game.Files;

/// <summary>
/// A texture and the halved copies of it, down to one texel each way.
/// </summary>
/// <remarks>
/// WHY A PICTURE OF A MONSTER LOOKED LIKE A BAD TELEVISION SIGNAL. A boss's skin is 2048 texels
/// across and its portrait a few hundred pixels, so one pixel stands for a hundred texels or more -
/// and a sampler that picks ONE of them, as this one did, picks a different one for every
/// neighbouring pixel. Wherever the texture has detail, which is everywhere on a monster, the
/// picture comes out as grain, and the grain crawls as the model turns. It was reported from the
/// live client as "grisselig", like poor reception, with a video that showed exactly that.
///
/// THE ANSWER IS AS OLD AS TEXTURED GRAPHICS: keep the texture at every halving, and read from
/// the level whose texels are about the size of a pixel. Each level is the box average of four
/// texels of the one above, so a pixel that stands for a sixteen-by-sixteen patch reads its mean
/// off level four instead of one texel picked out of 256. Built once when the model is read, on the
/// load's own task, and shared by every frame drawn from it: a 2048 square top costs a third again
/// of itself in smaller copies, and one pass over the texels.
///
/// THE GAME'S OWN FILES CARRY THESE LEVELS TOO, and from v0.1.326 a block-compressed file with every
/// level is read by them (<see cref="Kept(BlockTexture?)"/>): kept as its blocks, which the graphics card
/// reads as they are, and decoded for the processor only when it asks - behind the load, or at its
/// first read. Every other texture is decoded at its top level and halved here as before.
/// </remarks>
public sealed class Mipmaps
{
    private readonly BlockTexture? _blocks;
    private readonly object? _gate;
    private GamePicture[]? _levels;
    private long _unpacking;

    private Mipmaps(GamePicture[] levels)
    {
        _levels = levels;
        Count = levels.Length;
        Width = levels[0].Width;
        Height = levels[0].Height;
    }

    private Mipmaps(BlockTexture blocks)
    {
        _blocks = blocks;
        _gate = new object();
        Count = blocks.Levels;
        Width = blocks.Width;
        Height = blocks.Height;
    }

    /// <summary>How many levels there are, the top included.</summary>
    public int Count { get; }

    /// <summary>The top level's width, in texels - known without decoding anything.</summary>
    public int Width { get; }

    /// <summary>The top level's height, in texels - known without decoding anything.</summary>
    public int Height { get; }

    /// <summary>The file's own blocks every level is decoded from, or null where the levels were built from a decoded picture.</summary>
    /// <remarks>What the graphics card is handed as it is (ModelGpu's SkinTexture), where it reads the format.</remarks>
    public BlockTexture? Blocks => _blocks;

    /// <summary>Whether the levels are decoded - always, where they were built from a picture.</summary>
    public bool Unpacked => Volatile.Read(ref _levels) is not null;

    /// <summary>How long decoding the file's levels took, in Stopwatch ticks summed over threads - nought until it has been done.</summary>
    public long UnpackTicks => Interlocked.Read(ref _unpacking);

    /// <summary>The texture as decoded, full size.</summary>
    /// <remarks>Decoded here, on the asking thread, where its blocks have not been yet - see <see cref="Unpack"/>.</remarks>
    public GamePicture Top => Levels()[0];

    /// <summary>One level, the top being nought. A level past the last is the last.</summary>
    /// <remarks>Decoded here, on the asking thread, where its blocks have not been yet - see <see cref="Unpack"/>.</remarks>
    public GamePicture this[int level]
    {
        get
        {
            GamePicture[] levels = _levels ?? Levels();
            return levels[Math.Clamp(level, 0, levels.Length - 1)];
        }
    }

    /// <summary>Builds the levels for a picture, or null where there is no picture.</summary>
    public static Mipmaps? Of(GamePicture? top)
    {
        if (top is not { Ready: true } first)
        {
            return null;
        }

        var levels = new List<GamePicture>(12) { first };
        GamePicture above = first;
        while (above.Width > 1 || above.Height > 1)
        {
            above = Halved(above);
            levels.Add(above);
        }

        return new Mipmaps([.. levels]);
    }

    /// <summary>
    /// The file's own levels, kept as its blocks and decoded the first time the processor asks for one - or null where the file does
    /// not carry a plain texture with every level (BlockTexture.Complete).
    /// </summary>
    /// <remarks>
    /// THE GAME'S LEVELS, NOT ONES BUILT HERE (DECIDED from the live client, v0.1.326): the card can only be handed the levels the file
    /// carries, and the processor's picture is the card's reference, so both read them. The top level is the one decoded before; the
    /// smaller ones are the game's own halvings in place of the box averages <see cref="Of(GamePicture?)"/> builds.
    /// </remarks>
    public static Mipmaps? Kept(BlockTexture? blocks) => blocks is { Complete: true } whole ? new Mipmaps(whole) : null;

    /// <summary>Decodes every level of the file's blocks now, where they are not yet - on the calling thread, a large level's rows on every core.</summary>
    /// <remarks>
    /// ONCE, WHOEVER ASKS FIRST: a background unpack and a picture that cannot wait for it decode the same texture one after the other,
    /// the second finding it done.
    /// </remarks>
    public void Unpack() => Levels();

    /// <summary>
    /// Decodes many textures' blocks behind whatever asked, a texture to a worker - stopped between textures where asked to. Those
    /// decoded already cost nothing.
    /// </summary>
    /// <param name="textures">The textures, any of them null or decoded.</param>
    /// <param name="threads">How many workers may decode at once.</param>
    /// <param name="stop">Stops it between textures.</param>
    public static void Unpack(IReadOnlyList<Mipmaps> textures, int threads, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(textures);
        try
        {
            Parallel.ForEach(
                textures,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads), CancellationToken = stop },
                one => one.Unpack());
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // STOPPED BY WHOEVER ASKED - what was decoded stays decoded, the rest is decoded when it is first read.
        }
    }

    /// <summary>The levels, decoded from the blocks the first time they are asked for.</summary>
    private GamePicture[] Levels()
    {
        GamePicture[]? levels = Volatile.Read(ref _levels);
        if (levels is not null)
        {
            return levels;
        }

        lock (_gate!)
        {
            levels = _levels;
            if (levels is not null)
            {
                return levels;
            }

            long started = Stopwatch.GetTimestamp();
            BlockTexture blocks = _blocks!;
            levels = new GamePicture[blocks.Levels];
            for (var level = 0; level < levels.Length; level++)
            {
                levels[level] = blocks.Decode(level);
            }

            Interlocked.Add(ref _unpacking, Stopwatch.GetTimestamp() - started);
            Volatile.Write(ref _levels, levels);
            return levels;
        }
    }

    /// <summary>The box average of every two-by-two of texels; a side already down to one stays one.</summary>
    private static GamePicture Halved(GamePicture above)
    {
        int wide = Math.Max(1, above.Width / 2);
        int high = Math.Max(1, above.Height / 2);
        var rgba = new byte[wide * high * 4];
        byte[] from = above.Rgba;

        for (var y = 0; y < high; y++)
        {
            int y0 = Math.Min(y * 2, above.Height - 1);
            int y1 = Math.Min((y * 2) + 1, above.Height - 1);

            for (var x = 0; x < wide; x++)
            {
                int x0 = Math.Min(x * 2, above.Width - 1);
                int x1 = Math.Min((x * 2) + 1, above.Width - 1);

                int a = ((y0 * above.Width) + x0) * 4;
                int b = ((y0 * above.Width) + x1) * 4;
                int c = ((y1 * above.Width) + x0) * 4;
                int d = ((y1 * above.Width) + x1) * 4;
                int into = ((y * wide) + x) * 4;

                for (var part = 0; part < 4; part++)
                {
                    rgba[into + part] = (byte)((from[a + part] + from[b + part] + from[c + part] + from[d + part] + 2) >> 2);
                }
            }
        }

        return new GamePicture(wide, high, rgba);
    }
}
