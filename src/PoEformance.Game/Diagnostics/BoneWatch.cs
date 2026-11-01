using System.Diagnostics;
using System.Globalization;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.Entities;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// Watches the bones of the pieces the game draws the player with, read from memory over seconds while he moves - for the capture's bone
/// watch part: the blocks round each piece's object that change, kept as they change (BoneWatchSeries), and what they say of bones.
/// </summary>
/// <remarks>
/// WHY IT EXISTS (v0.1.401, DECIDED from the live client: measure the swing before it is simulated). The Exile's Staff's lantern hangs on
/// phys_tri_lamp_jntBnd and dangles and swings in the game; its .ao names the engine's simulation's parameters (bend_stiffness 800,
/// enclosure_radius and enclosure_angle 5, animation_velocity_factor and animation_force_factor 2, the viscosities nought) and no file holds
/// the simulation. The same physics moves skirts, shawls, feathers and helmet dangles. What the engine makes of it each frame lies in memory
/// somewhere round the piece's object: the composition (Render.Pieces) holds each piece's object, and from each its pointers are followed
/// <see cref="Depth"/> hops; the blocks that change in the first <see cref="FindSeconds"/> are watched for the rest, each read kept.
///
/// WHAT THE PERSON DOES: press the key, then walk, stop and turn a few times until the window says it is done - a swing wants a start and a
/// stop to show its period and how it settles.
/// </remarks>
public sealed class BoneWatch
{
    /// <summary>How long it watches - CHOSEN: long enough to walk, stop and turn twice.</summary>
    public const double Seconds = 12;

    /// <summary>The first part of the watch, which finds the blocks that change.</summary>
    public const double FindSeconds = 1;

    /// <summary>How much of each block is read - a run of twelve 4x4 matrices and more.</summary>
    public const int BlockBytes = 0x400;

    /// <summary>How many hops from a piece's object its pointers are followed.</summary>
    public const int Depth = 3;

    /// <summary>Most blocks the walk reads - CHOSEN, a guard against a pointer into the whole world.</summary>
    public const int MostBlocks = 4000;

    /// <summary>Most blocks watched after the finding, those with the most changing turn matrices first - CHOSEN.</summary>
    public const int MostKept = 48;

    /// <summary>How often a watched block is read - a thirtieth of a second.</summary>
    public const double Every = 1.0 / 30;

    private readonly IMemoryReader _reader;
    private readonly OffsetSchema _schema;
    private readonly double _seconds;
    private volatile bool _stop;
    private long _began;

    /// <param name="reader">The game's memory.</param>
    /// <param name="schema">The offsets.</param>
    /// <param name="seconds">How long it watches - <see cref="Seconds"/> but in a test.</param>
    public BoneWatch(IMemoryReader reader, OffsetSchema schema, double seconds = Seconds)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        _reader = reader;
        _schema = schema;
        _seconds = seconds;
    }

    /// <summary>For the capture's window: what it is doing.</summary>
    public string Line { get; private set; } = "not started";

    /// <summary>For the capture's window: how far it is, nought to one.</summary>
    public float Fraction => _began == 0 ? 0f : (float)Math.Clamp(Stopwatch.GetElapsedTime(_began).TotalSeconds / _seconds, 0, 1);

    /// <summary>Stops the watching early - what it read until then is written.</summary>
    public void Stop() => _stop = true;

    /// <summary>Watches, and says what it saw; the reads go to <paramref name="bin"/> where one is given.</summary>
    /// <param name="gameStatesStatic">The game-states static, the chain's root.</param>
    /// <param name="bin">Where the reads are written (BoneWatchSeries.File beside the part's text), or null.</param>
    public IReadOnlyList<string> Run(ulong gameStatesStatic, string? bin)
    {
        GameChainAddresses chain = GameChain.Resolve(_reader, _schema, gameStatesStatic);
        var entities = new EntityReader(_reader, _schema);
        if (chain.PlayerEntity == 0 || entities.ReadIdentity(chain.PlayerEntity) is not { } player)
        {
            return ["bone watch: no player read - not in an area, or the chain did not resolve"];
        }

        IReadOnlyDictionary<string, ulong> components = entities.ReadComponents(player.Address, player.Details);
        ulong composition = PlayerPieces.CompositionOf(_reader, _schema, components.GetValueOrDefault("Render"));
        IReadOnlyList<PlayerPieces.Piece> pieces = PlayerPieces.Slots(PlayerPieces.Read(_reader, _schema, composition));
        var roots = new List<(string Name, ulong Address)>();
        foreach (PlayerPieces.Piece piece in pieces)
        {
            roots.Add((string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(piece.Path)} (composition +0x{piece.At:X})"), piece.Object));
        }

        // HIS OWN ANIMATED AND RENDER COMPONENTS BESIDE THEM: his hand's bone and his place, for the swing to be set against.
        foreach (string name in (ReadOnlySpan<string>)["Render", "Animated"])
        {
            if (components.GetValueOrDefault(name) is var at and not 0)
            {
                roots.Add((name, at));
            }
        }

        var head = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"the player {player.Path}: the composition 0x{composition:X}, {pieces.Count} pieces in its slots"),
        };
        foreach (PlayerPieces.Piece piece in pieces)
        {
            head.Add(string.Create(CultureInfo.InvariantCulture, $"  piece\t+0x{piece.At:X}\t0x{piece.Object:X}\t{piece.Path}"));
        }

        if (roots.Count == 0)
        {
            return [.. head.Prepend("bone watch: nothing to watch - no piece read and no component of his")];
        }

        // THE WALK: every block a root's pointers reach, Depth hops deep, read once.
        Line = "following the pieces' pointers";
        List<BoneWatchSeries.Block> walked = Walk(roots);

        // THE FINDING: every block read over the first second, those that change kept - the most changing turns first.
        _began = Stopwatch.GetTimestamp();
        byte[] buffer = new byte[BlockBytes];
        int finds = 0;
        while (!_stop && Stopwatch.GetElapsedTime(_began).TotalSeconds < Math.Min(FindSeconds, _seconds))
        {
            double now = Stopwatch.GetElapsedTime(_began).TotalSeconds;
            foreach (BoneWatchSeries.Block block in walked)
            {
                Read(block, now, buffer, firstAndLast: true);
            }

            finds++;
            Line = string.Create(CultureInfo.InvariantCulture, $"finding what changes: {walked.Count} blocks read {finds} times - walk, stop and turn now");
            Thread.Sleep(10);
        }

        List<BoneWatchSeries.Block> kept = Kept(walked);
        int changing = walked.Count(Changes);

        // THE WATCH: the kept blocks every thirtieth of a second until the time is up.
        double next = 0;
        int sweeps = 0;
        while (!_stop)
        {
            double now = Stopwatch.GetElapsedTime(_began).TotalSeconds;
            if (now >= _seconds)
            {
                break;
            }

            if (now >= next)
            {
                foreach (BoneWatchSeries.Block block in kept)
                {
                    Read(block, now, buffer);
                }

                sweeps++;
                next = now + Every;
                Line = string.Create(CultureInfo.InvariantCulture, $"{now:0.0} of {_seconds:0} s - {kept.Count} blocks read {sweeps} times - walk, stop and turn");
            }

            Thread.Sleep(1);
        }

        double watched = Stopwatch.GetElapsedTime(_began).TotalSeconds;
        head.Insert(0, string.Create(
            CultureInfo.InvariantCulture,
            $"bone watch: {pieces.Count} pieces and his own components, {walked.Count} blocks walked {Depth} hops deep, {changing} changing in the first {FindSeconds:0.#} s, {kept.Count} watched for {watched:0.0} s{(_stop ? " (stopped)" : string.Empty)}, {sweeps} sweeps"));
        head.Insert(1, string.Create(CultureInfo.InvariantCulture, $"each block {BlockBytes} bytes; a block is kept where it changed in the finding, the most changing turn matrices first, at most {MostKept}; its reads are in {BoneWatchSeries.File} beside this file"));
        var series = new BoneWatchSeries(kept, head);
        if (bin is not null)
        {
            File.WriteAllBytes(bin, series.ToBytes());
        }

        Line = "done";
        return series.Describe();
    }

    /// <summary>Every block the roots' pointers reach, <see cref="Depth"/> hops deep, each named by its way - read once to choose.</summary>
    private List<BoneWatchSeries.Block> Walk(List<(string Name, ulong Address)> roots)
    {
        var blocks = new List<BoneWatchSeries.Block>();
        var seen = new HashSet<ulong>();
        var queue = new Queue<(string Way, ulong At, int Depth)>();
        foreach ((string name, ulong address) in roots)
        {
            if (seen.Add(address))
            {
                queue.Enqueue((name, address, 0));
            }
        }

        ulong moduleEnd = _reader.ModuleBase + _reader.ModuleSize;
        byte[] bytes = new byte[BlockBytes];
        while (queue.Count > 0 && blocks.Count < MostBlocks)
        {
            (string way, ulong at, int depth) = queue.Dequeue();
            if (!_reader.TryRead(at, bytes))
            {
                continue;
            }

            blocks.Add(new BoneWatchSeries.Block(way, at, BlockBytes, []));
            if (depth >= Depth)
            {
                continue;
            }

            for (var offset = 0; offset + 8 <= BlockBytes; offset += 8)
            {
                ulong pointer = BitConverter.ToUInt64(bytes, offset);

                // NOT THE PROGRAM'S OWN: a vtable or a static says nothing that moves.
                if (MemoryReaderExtensions.IsPlausiblePointer(pointer) && (pointer < _reader.ModuleBase || pointer >= moduleEnd) && seen.Add(pointer))
                {
                    queue.Enqueue((string.Create(CultureInfo.InvariantCulture, $"{way} +0x{offset:X} ->"), pointer, depth + 1));
                }
            }
        }

        return blocks;
    }

    /// <summary>The blocks to watch: those that changed in the finding, the most changing turn matrices first, then the most changing floats.</summary>
    private static List<BoneWatchSeries.Block> Kept(List<BoneWatchSeries.Block> walked)
    {
        var ranked = new List<(BoneWatchSeries.Block Block, int Turns, int Floats)>();
        foreach (BoneWatchSeries.Block block in walked)
        {
            if (!Changes(block))
            {
                continue;
            }

            int turns = BoneWatchSeries.Runs(block).Sum(run => run.Count);
            int floats = 0;
            for (var at = 0; at + 4 <= block.Size; at += 4)
            {
                uint first = BitConverter.ToUInt32(block.Reads[0].Bytes, at);
                floats += block.Reads.Any(read => BitConverter.ToUInt32(read.Bytes, at) != first) ? 1 : 0;
            }

            ranked.Add((block, turns, floats));
        }

        // THE FINDING'S READS ARE KEPT: they are the first second of the watch.
        return [.. ranked.OrderByDescending(one => one.Turns).ThenByDescending(one => one.Floats).Take(MostKept).Select(one => one.Block)];
    }

    /// <summary>Whether a block's reads differ anywhere.</summary>
    private static bool Changes(BoneWatchSeries.Block block)
        => block.Reads.Count > 1 && block.Reads.Skip(1).Any(read => !read.Bytes.AsSpan().SequenceEqual(block.Reads[0].Bytes));

    /// <summary>
    /// Reads a block once more and keeps the read - in the finding only its first and its latest (<paramref name="firstAndLast"/>): four
    /// thousand blocks read sixty times would be a quarter of a gigabyte, and the finding asks only whether a block changed.
    /// </summary>
    private void Read(BoneWatchSeries.Block block, double now, byte[] buffer, bool firstAndLast = false)
    {
        Span<byte> span = buffer.AsSpan(0, block.Size);
        if (!_reader.TryRead(block.Address, span))
        {
            return;
        }

        if (firstAndLast && block.Reads.Count >= 2)
        {
            // THE LATEST REPLACES THE ONE BEFORE unless that one already differs from the first - a change once seen is kept.
            if (block.Reads[1].Bytes.AsSpan().SequenceEqual(block.Reads[0].Bytes))
            {
                span.CopyTo(block.Reads[1].Bytes);
                block.Reads[1] = (now, block.Reads[1].Bytes);
            }

            return;
        }

        block.Reads.Add((now, span.ToArray()));
    }
}
