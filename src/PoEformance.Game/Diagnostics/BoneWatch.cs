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
/// ITS FIRST WATCH MISSED THE STAFF'S OWN BONES (v0.1.403's capture, 03:27): its 48 blocks held the body's bones, the robe's swinging in step
/// every 3.2 s and the staff's placement four times over (PiecePlacement), but no array of the staff's 19 bones. So from v0.1.404 each piece's
/// own skeleton - and those of the pieces it hangs off itself - is first looked for in the whole of memory by its rest (<see cref="PoseHunt"/>)
/// and watched beside the blocks with the player's place; each simulated bone's swing on its parent is set out by <see cref="PoseSwing"/>.
///
/// THE 04:49 CAPTURE FOUND ONLY COPIES OF THE REST (v0.1.404's, 2026-11-01): 21 arrays in the parents' and the model's frames, not one bone of
/// which moved while his hand did. So from v0.1.407 every piece whose rig the engine simulates a bone of is also looked for in the WORLD,
/// round its placement's World matrix (<see cref="PoseWorld"/>), in the same sweep - arrays of its bones there and single places where a
/// simulated bone may be kept - and both are watched with the World they are taken back through. The sweep leaves out the memory the game
/// cannot write and the driver's write-combined heaps, which held that sweep for 1,150 of its threads' seconds and ended it on its budget.
///
/// WHAT THE PERSON DOES, as the capture's window asks in a banner with a tone (v0.1.407, DECIDED from the live client: "in that window
/// finding anything is hard"): stand still while the search runs, then - at the high tone and the green banner - walk until the low tone;
/// from v0.1.411 a few steps and then a stand, as the 08:00 capture walked to the end and the swing after a stop shows its period and how
/// it settles.
/// </remarks>
public sealed class BoneWatch
{
    /// <summary>How long it watches - CHOSEN: long enough for a few steps and the swing to settle after them.</summary>
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

    /// <summary>The bytes read before bone nought's x and after the last bone of a skeleton found - for a turn kept beside the translations.</summary>
    public const int SkeletonMargin = 64;

    /// <summary>Most bytes the skeletons found are read as, together, each sweep - CHOSEN: forty arrays of a 91-bone rig would be a quarter of a megabyte a sweep.</summary>
    public const int MostSkeletonBytes = 96 * 1024;

    /// <summary>The bytes read before a candidate's x in the world, and after it - for what the engine keeps beside it (a last place, a speed).</summary>
    public const int CandidateMargin = 32;

    /// <summary>Most bytes the candidates in the world are read as, together, each sweep - CHOSEN: some five hundred places a sweep.</summary>
    public const int MostCandidateBytes = 48 * 1024;

    /// <summary>Most blocks read as often as they change between the sweeps - CHOSEN.</summary>
    public const int MostFast = 24;

    /// <summary>
    /// Most windows of places near one piece read as often as they change - those holding a place taken for a simulated bone that moved
    /// between the search's two reads, the nearest its bone's length first. CHOSEN.
    /// </summary>
    public const int MostFastWindows = 4;

    /// <summary>Most reads kept of a block read as often as it changes - CHOSEN: some ten a frame for twelve seconds.</summary>
    public const int MostFastReads = 8000;

    /// <summary>Where it stands, for the banner the capture's window shows the person at the game.</summary>
    public enum Stage
    {
        /// <summary>Not begun.</summary>
        Starting,

        /// <summary>Reading the player and searching memory: the person is to stand still.</summary>
        Searching,

        /// <summary>Watching: the person is to walk a few steps, then stop and stand still.</summary>
        Watching,

        /// <summary>Finished, or stopped.</summary>
        Done,
    }

    private readonly IMemoryReader _reader;
    private readonly OffsetSchema _schema;
    private readonly double _seconds;
    private readonly FloatHuntProgress _hunting = new();
    private volatile bool _stop;
    private volatile bool _hunted;
    private volatile Stage _stage;
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

    /// <summary>For the capture's banner: where it stands - see <see cref="Stage"/>.</summary>
    public Stage Now => _stage;

    /// <summary>For the capture's banner: the seconds left of the watch while it watches, its whole length before.</summary>
    public double Left => _began == 0 ? _seconds : Math.Max(0, _seconds - Stopwatch.GetElapsedTime(_began).TotalSeconds);

    /// <summary>For the capture's window: how far it is, nought to one - the search for the skeletons its first half where one runs.</summary>
    public float Fraction
    {
        get
        {
            float watched = _began == 0 ? 0f : (float)Math.Clamp(Stopwatch.GetElapsedTime(_began).TotalSeconds / _seconds, 0, 1);
            if (!_hunted)
            {
                return watched;
            }

            float searched = _began != 0 ? 1f : _hunting.Budget > 0 ? (float)Math.Clamp((double)_hunting.Bytes / _hunting.Budget, 0, 1) : 0f;
            return (0.5f * searched) + (0.5f * watched);
        }
    }

    /// <summary>Stops the searching and the watching early - what they found and read until then is written.</summary>
    public void Stop()
    {
        _stop = true;
        _hunting.Stop();
    }

    /// <summary>Watches, and says what it saw; the reads go to <paramref name="bin"/> where one is given.</summary>
    /// <param name="gameStatesStatic">The game-states static, the chain's root.</param>
    /// <param name="bin">Where the reads are written (BoneWatchSeries.File beside the part's text), or null.</param>
    /// <param name="rigsOf">
    /// Each piece's rigs from the install - its own and those of the pieces it hangs off itself - for finding their poses by their rest
    /// (<see cref="PoseHunt"/>); null leaves the search out.
    /// </param>
    /// <param name="regions">The game's memory regions, for the search; null leaves it out.</param>
    public IReadOnlyList<string> Run(ulong gameStatesStatic, string? bin, Func<string, IReadOnlyList<PoseRig>>? rigsOf = null, IMemoryRegions? regions = null)
    {
        _stage = Stage.Searching;
        try
        {
            return Watch(gameStatesStatic, bin, rigsOf, regions);
        }
        finally
        {
            _stage = Stage.Done;
        }
    }

    private IReadOnlyList<string> Watch(ulong gameStatesStatic, string? bin, Func<string, IReadOnlyList<PoseRig>>? rigsOf, IMemoryRegions? regions)
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

        // THE SKELETONS BY THEIR REST AND IN THE WORLD, first: the search reads the whole of memory while he stands still.
        Found found = Skeletons(player, pieces, rigsOf, regions, head);
        List<BoneWatchSeries.Skeleton> skeletons = found.Skeletons;

        // THE WALK: every block a root's pointers reach, Depth hops deep, read once.
        Line = "following the pieces' pointers";
        List<BoneWatchSeries.Block> walked = Walk(roots);

        // HIS PLACE, read with every sweep: the swing is the hand's motion's, and the table sets his speed beside it. The skeletons, the
        // pieces' places and the candidates in the world first, in the order their records name them.
        var always = new List<BoneWatchSeries.Block>(found.Blocks);
        ulong render = components.GetValueOrDefault("Render");
        if (render != 0)
        {
            always.Add(new BoneWatchSeries.Block("his place (Render.CurrentWorldPosition)", render + (ulong)_schema.Structs["Render"].OffsetOf("CurrentWorldPosition"), 12, []));
        }

        // THE FINDING: every block read over the first second, those that change kept - the most changing turns first. The skeletons and
        // his place are watched from the start - and from here he is to move.
        _began = Stopwatch.GetTimestamp();
        _stage = Stage.Watching;
        byte[] buffer = new byte[Math.Max(BlockBytes, always.Count == 0 ? 0 : always.Max(block => block.Size))];
        int finds = 0, sweeps = 0;
        double next = 0;
        while (!_stop && Stopwatch.GetElapsedTime(_began).TotalSeconds < Math.Min(FindSeconds, _seconds))
        {
            double now = Stopwatch.GetElapsedTime(_began).TotalSeconds;
            foreach (BoneWatchSeries.Block block in walked)
            {
                Read(block, now, buffer, firstAndLast: true);
            }

            if (now >= next)
            {
                foreach (BoneWatchSeries.Block block in always)
                {
                    Read(block, Stopwatch.GetElapsedTime(_began).TotalSeconds, buffer);
                }

                sweeps++;
                next = now + Every;
            }

            finds++;
            Line = string.Create(CultureInfo.InvariantCulture, $"finding what changes: {walked.Count} blocks read {finds} times - walk a few steps now, then stop");
            Thread.Sleep(10);
        }

        List<BoneWatchSeries.Block> kept = Kept(walked);
        int changing = walked.Count(Changes);
        List<BoneWatchSeries.Block> watching = [.. kept, .. always];

        // THE LIVE ARRAYS AS OFTEN AS THEY CHANGE: between the sweeps the arrays that moved between the search's two reads and the pieces'
        // Worlds are read again and again, a read kept only where it differs from the one before - the 07:26 capture's staff array held the
        // staff in one read of three, every 0.23 s at thirty sweeps a second, too coarse for a swing of half a second (2026-11-01,
        // v0.1.410). The sweeps leave them to this; while there are any the loop does not sleep.
        HashSet<BoneWatchSeries.Block> fast = [.. found.Fast];
        List<BoneWatchSeries.Block> swept = [.. watching.Where(block => !fast.Contains(block))];
        int fastReads = 0;

        // THE WATCH: the kept blocks, the skeletons and his place every thirtieth of a second until the time is up.
        while (!_stop)
        {
            double now = Stopwatch.GetElapsedTime(_began).TotalSeconds;
            if (now >= _seconds)
            {
                break;
            }

            if (now >= next)
            {
                foreach (BoneWatchSeries.Block block in swept)
                {
                    Read(block, now, buffer);
                }

                sweeps++;
                next = now + Every;
                Line = string.Create(CultureInfo.InvariantCulture, $"{now:0.0} of {_seconds:0} s - {kept.Count} blocks, {skeletons.Count} skeletons and {found.Candidates.Count} places in the world read {sweeps} times, {fast.Count} live ones {fastReads} times as they changed - walk a few steps, then stop and stand still");
            }

            if (fast.Count == 0)
            {
                Thread.Sleep(1);
                continue;
            }

            foreach (BoneWatchSeries.Block block in found.Fast)
            {
                fastReads += Changed(block, Stopwatch.GetElapsedTime(_began).TotalSeconds, buffer) ? 1 : 0;
            }

            Thread.Yield();
        }

        double watched = Stopwatch.GetElapsedTime(_began).TotalSeconds;
        head.Insert(0, string.Create(
            CultureInfo.InvariantCulture,
            $"bone watch: {pieces.Count} pieces and his own components, {walked.Count} blocks walked {Depth} hops deep, {changing} changing in the first {FindSeconds:0.#} s, {kept.Count} watched, {skeletons.Count} skeletons found by their rest or in the world and {found.Candidates.Count} places near the pieces in the world, for {watched:0.0} s{(_stop ? " (stopped)" : string.Empty)}, {sweeps} sweeps"));
        head.Insert(1, string.Create(CultureInfo.InvariantCulture, $"each block {BlockBytes} bytes; a block is kept where it changed in the finding, the most changing turn matrices first, at most {MostKept}; its reads are in {BoneWatchSeries.File} beside this file"));
        head.Insert(2, string.Create(CultureInfo.InvariantCulture, $"read as often as they changed between the sweeps: {fast.Count} blocks - the arrays that moved between the search's two reads and the pieces' Worlds, at most {MostFast} - {fastReads} reads kept, at most {MostFastReads} a block"));
        var series = new BoneWatchSeries(
            watching,
            head,
            [.. skeletons.Select(one => one with { Block = kept.Count + one.Block })],
            render != 0 ? watching.Count - 1 : -1,
            [.. found.Places.Select(one => one with { Block = kept.Count + one.Block })],
            [.. found.Candidates.Select(one => one with { Block = kept.Count + one.Block })]);
        if (bin is not null)
        {
            File.WriteAllBytes(bin, series.ToBytes());
        }

        Line = "done";
        return series.Describe();
    }

    /// <summary>What the search found, as blocks to watch: the skeletons, the pieces' places in the world and the candidates there, each naming its block.</summary>
    private sealed record Found(
        List<BoneWatchSeries.Block> Blocks, List<BoneWatchSeries.Skeleton> Skeletons, List<BoneWatchSeries.Place> Places, List<BoneWatchSeries.Candidate> Candidates)
    {
        /// <summary>The blocks read as often as they change between the sweeps: the arrays that moved between the search's two reads and the pieces' Worlds.</summary>
        public List<BoneWatchSeries.Block> Fast { get; } = [];
    }

    /// <summary>
    /// The skeletons of his body and every piece he is drawn with - and of the pieces those hang off themselves - found in the whole of
    /// memory by their rest (<see cref="PoseHunt"/>), and the pieces whose rigs the engine simulates a bone of in the world
    /// (<see cref="PoseWorld"/>), as blocks to watch; what the search said goes into the head.
    /// </summary>
    private Found Skeletons(
        EntityIdentity player, IReadOnlyList<PlayerPieces.Piece> pieces, Func<string, IReadOnlyList<PoseRig>>? rigsOf, IMemoryRegions? regions, List<string> head)
    {
        var found = new Found([], [], [], []);
        head.Add(string.Empty);
        if (rigsOf is null || regions is null)
        {
            head.Add("the skeletons by their rest: not looked for - " + (rigsOf is null ? "no install to read the pieces' rigs from" : "the game's memory regions are not listed"));
            return found;
        }

        // HIS BODY'S OWN RIG FIRST (its attachments are what the pieces cover), then every piece's and what each hangs off itself.
        var rigs = new List<PoseRig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Take(IReadOnlyList<PoseRig> taken, bool firstOnly)
        {
            foreach (PoseRig rig in taken)
            {
                if (seen.Add(rig.Path))
                {
                    rigs.Add(rig);
                }

                if (firstOnly)
                {
                    break;
                }
            }
        }

        Take(rigsOf(player.Path), firstOnly: true);
        foreach (PlayerPieces.Piece piece in pieces)
        {
            Take(rigsOf(piece.Path), firstOnly: false);
        }

        head.Add(string.Create(CultureInfo.InvariantCulture, $"the skeletons by their rest (PoseHunt): {rigs.Count} rigs - his body's and every piece's, and those of the pieces each hangs off itself"));
        for (var at = 0; at < rigs.Count; at++)
        {
            PoseRig rig = rigs[at];
            head.Add(string.Create(CultureInfo.InvariantCulture, $"  rig\t{at}\t{rig.Piece}\t{rig.Path}\t{rig.Count} bones\t{rig.SimulatedCount} simulated"));
        }

        if (rigs.Count == 0)
        {
            return found;
        }

        List<PiecePlace> places = Places(pieces, rigs, head);
        _hunted = true;
        Line = "looking for each piece's skeleton by its rest and in the world, in the memory the game writes - stand still until the banner turns green";
        PoseHuntResult result = PoseHunt.Find(_reader, regions, rigs, pieces.Count > 0 ? pieces[0].Object : player.Address, _hunting, places);
        head.AddRange(result.Lines.Select(line => "  " + line));
        head.Add("  array\trig\tframe\tlayout\tstride\tbone 0's x\tfixed bones at rest\tbones agreeing\tsimulated bones from their rest\tbetween the search's two reads");
        foreach (PoseArray array in result.Arrays)
        {
            head.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  array\t{array.Rig}\t{array.Frame}\t{array.Layout}\t{array.Stride}\t0x{array.First:X}\t{array.Matched} of {array.Fixed}\t{array.Votes}\t{array.Stray:0.###}\t{(array.Moving ? "moved" : "still")}"));
        }

        // EACH PIECE'S PLACE IN THE WORLD watched as a block of its own, once a world array or a candidate names it: what they are taken
        // back through at every sweep.
        var placeBlocks = new Dictionary<int, int>();
        int PlaceOf(int place)
        {
            if (!placeBlocks.TryGetValue(place, out int index))
            {
                PiecePlace one = result.Places[place];
                found.Blocks.Add(new BoneWatchSeries.Block(
                    string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(rigs[one.Rig].Piece)}'s World (PiecePlacement.World)"), one.WorldAt, 64, []));
                found.Places.Add(new BoneWatchSeries.Place(found.Blocks.Count - 1, 0, rigs[one.Rig]));
                if (found.Fast.Count < MostFast)
                {
                    found.Fast.Add(found.Blocks[^1]);
                }

                placeBlocks[place] = index = found.Places.Count - 1;
            }

            return index;
        }

        int bytes = 0, past = 0;
        foreach (PoseArray array in result.Arrays)
        {
            PoseRig rig = rigs[array.Rig];
            int length = ((rig.Count - 1) * array.Stride) + PoseHunt.Span(array.Layout);

            // A MARGIN THAT DOES NOT READ is left out - an array at the very edge of its pages - and its turns then only after its x.
            int margin = _reader.TryRead(array.First - SkeletonMargin, new byte[length + (2 * SkeletonMargin)]) ? SkeletonMargin : 0;
            int size = length + (2 * margin);
            if (bytes + size > MostSkeletonBytes)
            {
                past++;
                continue;
            }

            bytes += size;
            int place = array.Frame == PoseFrame.World && array.Place >= 0 ? PlaceOf(array.Place) : -1;
            found.Blocks.Add(new BoneWatchSeries.Block(
                string.Create(CultureInfo.InvariantCulture, $"skeleton {found.Skeletons.Count + 1}: {Path.GetFileName(rig.Piece)} {array.Frame} {array.Layout} {array.Stride}"),
                array.First - (ulong)margin,
                size,
                []));
            found.Skeletons.Add(new BoneWatchSeries.Skeleton(found.Blocks.Count - 1, rig, array.Frame, array.Layout, array.Stride, margin, array.Matched, array.Fixed, array.Votes) { Place = place });
            if (array.Moving && found.Fast.Count < MostFast)
            {
                found.Fast.Add(found.Blocks[^1]);
            }
        }

        if (past > 0)
        {
            head.Add(string.Create(CultureInfo.InvariantCulture, $"  {past} more arrays found, past the {MostSkeletonBytes / 1024} KB read a sweep: not watched"));
        }

        Candidates(result, rigs, PlaceOf, found, head);
        return found;
    }

    /// <summary>
    /// The places of the pieces whose own rig the engine simulates a bone of: each piece's placement (PieceObject.Placement) and its World
    /// matrix there (PiecePlacement.World), read once before the sweep - a piece whose World is not one left out, said why.
    /// </summary>
    private List<PiecePlace> Places(IReadOnlyList<PlayerPieces.Piece> pieces, List<PoseRig> rigs, List<string> head)
    {
        var places = new List<PiecePlace>();
        if (!_schema.Structs.TryGetValue("PieceObject", out StructDef? objectFields) || objectFields.Field("Placement") is not { } placement
            || !_schema.Structs.TryGetValue("PiecePlacement", out StructDef? placementFields) || placementFields.Field("World") is not { } world)
        {
            head.Add("the pieces in the world: not looked for - the schema names no PieceObject.Placement or PiecePlacement.World");
            return places;
        }

        byte[] bytes = new byte[64];
        foreach (PlayerPieces.Piece piece in pieces)
        {
            int rig = rigs.FindIndex(one => string.Equals(one.Piece, piece.Path, StringComparison.OrdinalIgnoreCase));
            if (rig < 0 || rigs[rig].SimulatedCount == 0)
            {
                continue;
            }

            ulong at = _reader.ReadPointer(piece.Object + (ulong)placement.Offset);
            ulong worldAt = at + (ulong)world.Offset;
            string name = Path.GetFileName(piece.Path);
            if (at == 0 || !_reader.TryRead(worldAt, bytes) || PoseWorld.Read(bytes, 0) is var matrix && !PoseWorld.IsWorld(matrix))
            {
                head.Add(string.Create(CultureInfo.InvariantCulture, $"  the pieces in the world: {name}'s placement 0x{at:X} holds no World at +0x{world.Offset:X} - not looked for there"));
                continue;
            }

            places.Add(new PiecePlace(rig, worldAt, matrix));
        }

        head.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"the pieces in the world (PoseWorld): {places.Count} pieces whose own rig the engine simulates a bone of, each looked for round its placement's World"));
        return places;
    }

    /// <summary>
    /// The candidates in the world as blocks to watch: each with <see cref="CandidateMargin"/> bytes either side, those whose windows meet
    /// read as one block - the simulated bones' first, the nearest their bone's length first, until <see cref="MostCandidateBytes"/>.
    /// </summary>
    private void Candidates(PoseHuntResult result, List<PoseRig> rigs, Func<int, int> placeOf, Found found, List<string> head)
    {
        if (result.Candidates.Count == 0)
        {
            return;
        }

        // THE WINDOWS, joined where they meet: a world array of a rig no fixed bone of which agreed still lies among its candidates. Those
        // that moved between the search's two reads first (PoseHunt.Changing), then the simulated bones', the nearest their length first.
        WorldCandidate[] chosen = [.. result.Candidates
            .OrderByDescending(one => one.Moving)
            .ThenByDescending(one => one.Parent >= 0)
            .ThenBy(one => one.Parent >= 0 ? MathF.Abs(one.Ratio - 1f) : one.Ratio)];
        var windows = new List<(ulong From, ulong To, List<WorldCandidate> Inside)>();
        int bytes = 0, past = 0;
        foreach (WorldCandidate candidate in chosen)
        {
            ulong from = candidate.At - CandidateMargin, to = candidate.At + (ulong)PoseHunt.Span(candidate.Layout) + CandidateMargin;
            int joined = windows.FindIndex(window => from <= window.To && to >= window.From);
            int grows = joined < 0 ? (int)(to - from) : (int)(Math.Max(to, windows[joined].To) - Math.Min(from, windows[joined].From) - (windows[joined].To - windows[joined].From));
            if (bytes + grows > MostCandidateBytes)
            {
                past++;
                continue;
            }

            bytes += grows;
            if (joined < 0)
            {
                windows.Add((from, to, [candidate]));
            }
            else
            {
                (ulong was, ulong end, List<WorldCandidate> inside) = windows[joined];
                inside.Add(candidate);
                windows[joined] = (Math.Min(was, from), Math.Max(end, to), inside);
            }
        }

        // EVERY PIECE'S PLACE MADE A BLOCK FIRST: made inside the loop below it would come after a window, and the window's candidates
        // after it would take the place's block for their own - v0.1.407 numbered two of the 06:00 capture's 776 so (2026-11-01).
        var placeIndex = new Dictionary<int, int>();
        foreach (WorldCandidate candidate in chosen)
        {
            if (!placeIndex.ContainsKey(candidate.Place))
            {
                placeIndex[candidate.Place] = placeOf(candidate.Place);
            }
        }

        byte[] probe = new byte[(int)windows.Select(window => window.To - window.From).DefaultIfEmpty(0UL).Max()];
        var swinging = new List<(BoneWatchSeries.Block Block, int Place, float Off)>();
        foreach ((ulong from, ulong to, List<WorldCandidate> inside) in windows.OrderBy(window => window.From))
        {
            int size = (int)(to - from);
            if (!_reader.TryRead(from, probe.AsSpan(0, size)))
            {
                past += inside.Count;
                continue;
            }

            found.Blocks.Add(new BoneWatchSeries.Block(
                string.Create(CultureInfo.InvariantCulture, $"places in the world near {string.Join(", ", inside.Select(one => Path.GetFileName(rigs[result.Places[one.Place].Rig].Piece)).Distinct())}: {inside.Count}"),
                from,
                size,
                []));
            int window = found.Blocks.Count - 1;
            foreach (WorldCandidate candidate in inside)
            {
                found.Candidates.Add(new BoneWatchSeries.Candidate(
                    window, (int)(candidate.At - from), candidate.Layout, placeIndex[candidate.Place], candidate.Bone, candidate.Parent));
            }

            foreach (WorldCandidate candidate in inside.Where(one => one.Moving && one.Parent >= 0))
            {
                swinging.Add((found.Blocks[window], candidate.Place, MathF.Abs(candidate.Ratio - 1f)));
            }
        }

        // THE SWINGING PLACES AS OFTEN AS THEY CHANGE too: the 08:00 capture's lamp, read thirty times a second against its staff's World
        // read every frame, came at a random point of each frame - too coarse a clock for its swing (2026-11-01, v0.1.411). Each piece's
        // windows holding a place taken for a simulated bone that moved between the search's two reads, the nearest its length first.
        foreach (IGrouping<int, (BoneWatchSeries.Block Block, int Place, float Off)> piece in swinging.GroupBy(one => one.Place))
        {
            foreach (BoneWatchSeries.Block block in piece.OrderBy(one => one.Off).Select(one => one.Block).Distinct().Take(MostFastWindows))
            {
                if (found.Fast.Count < MostFast && !found.Fast.Contains(block))
                {
                    found.Fast.Add(block);
                }
            }
        }

        head.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"  the places in the world watched: {found.Candidates.Count} in {windows.Count} windows of {CandidateMargin} bytes either side{(past > 0 ? $", {past} more past the {MostCandidateBytes / 1024} KB read a sweep or no longer read" : string.Empty)}"));
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

    /// <summary>
    /// Reads a block once more and keeps the read only where it differs from the block's last - whether it was kept; none past
    /// <see cref="MostFastReads"/>.
    /// </summary>
    private bool Changed(BoneWatchSeries.Block block, double now, byte[] buffer)
    {
        Span<byte> span = buffer.AsSpan(0, block.Size);
        if (block.Reads.Count >= MostFastReads || !_reader.TryRead(block.Address, span)
            || (block.Reads.Count > 0 && span.SequenceEqual(block.Reads[^1].Bytes)))
        {
            return false;
        }

        block.Reads.Add((now, span.ToArray()));
        return true;
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
