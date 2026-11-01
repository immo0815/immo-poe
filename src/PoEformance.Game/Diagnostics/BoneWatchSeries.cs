using System.Globalization;
using System.Numerics;
using System.Text;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// What the bone watch read (BoneWatch): blocks of memory read again and again over seconds - and what they say of bones: the runs of turn
/// matrices in them, each matrix's turn against the first of its run over the watch. Written to <see cref="File"/> beside the part's text and
/// read back from it offline, so the reading is held against the very reads again.
/// </summary>
/// <remarks>
/// WHAT IT LOOKS FOR (v0.1.401, asked for from the live client: the Exile's Staff's lantern dangles and swings in the game, and the engine's
/// simulation of its physics bone, phys_tri_lamp_jntBnd, is in no file - the .ao names only its parameters). A rigid weapon's own bones keep
/// their turns against its root whatever the hand does; a simulated bone does not. So every run of turn matrices in the watched memory - a
/// skeleton's bones, side by side at one stride - is set out matrix by matrix against the run's first: a bone that turns on it while the
/// rest hold still is the simulated one, and its angle over time is the swing to be measured: how far, how fast it settles, its period.
/// A turn is three rows of three floats, each of one length (a uniform scale), at right angles to one another; a row is the first three
/// floats of four (a 4x4 or 3x4 matrix written by rows) or three floats packed (a 3x3).
/// </remarks>
public sealed class BoneWatchSeries
{
    /// <summary>The file the reads are kept in beside the part's text.</summary>
    public const string File = "bone-watch.bin";

    /// <summary>The first bytes of the file, its version in the last: 2 from v0.1.404, whose skeletons and the player's place follow the blocks.</summary>
    private static readonly byte[] Magic = "POEFBONES2"u8.ToArray();

    /// <summary>The first bytes of a file of v0.1.401 to v0.1.403 - the blocks alone.</summary>
    private static readonly byte[] MagicOne = "POEFBONES1"u8.ToArray();

    /// <summary>The most columns of swings a skeleton's table writes.</summary>
    private const int MostSwingColumns = 8;

    /// <summary>How near a row's length must be to the others' and their products to nought, against the scale - CHOSEN.</summary>
    private const float Slack = 2e-3f;

    /// <summary>The share of a matrix's reads that must be a turn for it to count - CHOSEN: a torn read now and then is no reason to drop it.</summary>
    private const double TurnShare = 0.9;

    /// <summary>A run's least count of matrices - CHOSEN: one alone is no skeleton.</summary>
    private const int LeastRun = 2;

    /// <summary>Most matrices of one run written one by one - an 87-bone body is written whole.</summary>
    private const int MostWritten = 160;

    /// <summary>Most rows of a series table - one every <see cref="SeriesStep"/> s over a watch.</summary>
    private const double SeriesStep = 1.0 / 30;

    /// <summary>One block read again and again: its name (the way to it), address, size and every read with its time.</summary>
    public sealed record Block(string Name, ulong Address, int Size, List<(double At, byte[] Bytes)> Reads);

    /// <summary>A skeleton's pose array found by its rest (PoseHunt) and watched as a block: whose rig, how it lies, where in the block bone nought's x is.</summary>
    /// <param name="Block">The block it was read as, by its place among the blocks.</param>
    /// <param name="Rig">The rig.</param>
    /// <param name="Frame">Whose frame its translations are in.</param>
    /// <param name="Layout">How a translation lies in its record.</param>
    /// <param name="Stride">The bytes from one bone's record to the next.</param>
    /// <param name="Offset">Where bone nought's x lies in the block - the bytes read before it, for a turn kept there.</param>
    /// <param name="Matched">How many fixed bones stood at their rest when it was found.</param>
    /// <param name="Fixed">How many bones hold their rest in its frame.</param>
    /// <param name="Votes">How many bones' places found in the sweep agreed on it.</param>
    public sealed record Skeleton(int Block, PoseRig Rig, PoseFrame Frame, PoseLayout Layout, int Stride, int Offset, int Matched, int Fixed, int Votes);

    /// <summary>The blocks, in the order they were watched.</summary>
    public IReadOnlyList<Block> Blocks { get; }

    /// <summary>What the watch said of itself - the player's pieces, what was walked - written at the top of the file's reading.</summary>
    public IReadOnlyList<string> Head { get; }

    /// <summary>The skeletons found by their rest, each read as one of the blocks - none in a file of v0.1.401 to v0.1.403.</summary>
    public IReadOnlyList<Skeleton> Skeletons { get; }

    /// <summary>The block holding the player's place (Render.CurrentWorldPosition, x, y and z), -1 for none.</summary>
    public int Player { get; }

    /// <param name="blocks">The blocks watched.</param>
    /// <param name="head">What the watch said of itself.</param>
    /// <param name="skeletons">The skeletons found by their rest among the blocks.</param>
    /// <param name="player">The block of the player's place, -1 for none.</param>
    public BoneWatchSeries(IReadOnlyList<Block> blocks, IReadOnlyList<string> head, IReadOnlyList<Skeleton>? skeletons = null, int player = -1)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(head);
        Blocks = blocks;
        Head = head;
        Skeletons = skeletons ?? [];
        Player = player >= 0 && player < blocks.Count ? player : -1;
    }

    /// <summary>
    /// The reads as bytes: the magic, the head's lines, then each block's name, address, size and reads; then the player's block and each
    /// skeleton - its block, its rig whole (piece, file, each bone's name, parent and both rests) and how it lies.
    /// </summary>
    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using (var write = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            write.Write(Magic);
            write.Write(Head.Count);
            foreach (string line in Head)
            {
                write.Write(line);
            }

            write.Write(Blocks.Count);
            foreach (Block block in Blocks)
            {
                write.Write(block.Name);
                write.Write(block.Address);
                write.Write(block.Size);
                write.Write(block.Reads.Count);
                foreach ((double at, byte[] bytes) in block.Reads)
                {
                    write.Write(at);
                    write.Write(bytes, 0, block.Size);
                }
            }

            write.Write(Player);
            write.Write(Skeletons.Count);
            foreach (Skeleton skeleton in Skeletons)
            {
                write.Write(skeleton.Block);
                PoseRig rig = skeleton.Rig;
                write.Write(rig.Piece);
                write.Write(rig.Path);
                write.Write(rig.Count);
                for (var bone = 0; bone < rig.Count; bone++)
                {
                    write.Write(rig.Names[bone]);
                    write.Write(rig.Parents[bone]);
                    Matrix(write, rig.Local[bone]);
                    Matrix(write, rig.Model[bone]);
                }

                write.Write((int)skeleton.Frame);
                write.Write((int)skeleton.Layout);
                write.Write(skeleton.Stride);
                write.Write(skeleton.Offset);
                write.Write(skeleton.Matched);
                write.Write(skeleton.Fixed);
                write.Write(skeleton.Votes);
            }
        }

        return stream.ToArray();
    }

    /// <summary>The reads back from <see cref="ToBytes"/>'s bytes - or a file of v0.1.401 to v0.1.403's, its blocks alone - or null where they are not such a file.</summary>
    public static BoneWatchSeries? Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        bool two = bytes.Length >= Magic.Length && bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic);
        if (!two && !(bytes.Length >= MagicOne.Length && bytes.AsSpan(0, MagicOne.Length).SequenceEqual(MagicOne)))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, Magic.Length, bytes.Length - Magic.Length);
            using var read = new BinaryReader(stream, Encoding.UTF8);
            int lines = read.ReadInt32();
            var head = new List<string>(Math.Clamp(lines, 0, 4096));
            for (var line = 0; line < lines; line++)
            {
                head.Add(read.ReadString());
            }

            int count = read.ReadInt32();
            var blocks = new List<Block>(Math.Clamp(count, 0, 4096));
            for (var one = 0; one < count; one++)
            {
                string name = read.ReadString();
                ulong address = read.ReadUInt64();
                int size = read.ReadInt32();
                int reads = read.ReadInt32();
                if (size is <= 0 or > 1 << 20 || reads < 0)
                {
                    return null;
                }

                var taken = new List<(double, byte[])>(reads);
                for (var at = 0; at < reads; at++)
                {
                    taken.Add((read.ReadDouble(), read.ReadBytes(size)));
                }

                blocks.Add(new Block(name, address, size, taken));
            }

            if (!two)
            {
                return new BoneWatchSeries(blocks, head);
            }

            int player = read.ReadInt32();
            int skeletons = read.ReadInt32();
            var found = new List<Skeleton>(Math.Clamp(skeletons, 0, 256));
            for (var one = 0; one < skeletons; one++)
            {
                int block = read.ReadInt32();
                string piece = read.ReadString(), path = read.ReadString();
                int bones = read.ReadInt32();
                if (bones is < 0 or > 1024 || block < 0 || block >= blocks.Count)
                {
                    return null;
                }

                var names = new string[bones];
                var parents = new int[bones];
                var local = new Matrix4x4[bones];
                var model = new Matrix4x4[bones];
                for (var bone = 0; bone < bones; bone++)
                {
                    names[bone] = read.ReadString();
                    parents[bone] = read.ReadInt32();
                    local[bone] = Matrix(read);
                    model[bone] = Matrix(read);
                }

                var rig = new PoseRig(piece, path, names, parents, local, model);
                found.Add(new Skeleton(
                    block, rig, (PoseFrame)read.ReadInt32(), (PoseLayout)read.ReadInt32(), read.ReadInt32(), read.ReadInt32(), read.ReadInt32(), read.ReadInt32(), read.ReadInt32()));
            }

            return new BoneWatchSeries(blocks, head, found, player);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    private static void Matrix(BinaryWriter write, Matrix4x4 matrix)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                write.Write(matrix[row, column]);
            }
        }
    }

    private static Matrix4x4 Matrix(BinaryReader read)
    {
        var matrix = default(Matrix4x4);
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                matrix[row, column] = read.ReadSingle();
            }
        }

        return matrix;
    }

    /// <summary>A run of turn matrices in one block: where it starts, its stride, how many, and how each row of a matrix is laid.</summary>
    /// <param name="Start">The first matrix's offset in the block.</param>
    /// <param name="Stride">The bytes from one matrix to the next.</param>
    /// <param name="Count">How many matrices.</param>
    /// <param name="Row">The bytes from one row of a matrix to the next - 16 for a 4x4 or 3x4 by rows, 12 for a packed 3x3.</param>
    public readonly record struct Run(int Start, int Stride, int Count, int Row);

    /// <summary>
    /// The runs of turn matrices a block holds over its reads - each matrix a turn in <see cref="TurnShare"/> of them - the longest first,
    /// no two overlapping.
    /// </summary>
    public static IReadOnlyList<Run> Runs(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (block.Reads.Count == 0)
        {
            return [];
        }

        var runs = new List<Run>();
        var taken = new bool[block.Size];
        foreach (int row in (ReadOnlySpan<int>)[16, 12])
        {
            int span = (2 * row) + 12;

            // WHICH OFFSETS HOLD A TURN, by the share of reads that do.
            var turn = new bool[block.Size];
            for (var at = 0; at + span <= block.Size; at += 4)
            {
                int turns = 0;
                foreach ((_, byte[] bytes) in block.Reads)
                {
                    turns += IsTurn(bytes, at, row) ? 1 : 0;
                }

                turn[at] = turns >= TurnShare * block.Reads.Count;
            }

            // RUNS AT EVERY STRIDE a matrix may take, the longest kept: a skeleton's bones lie side by side, a matrix after a matrix.
            foreach (int stride in (ReadOnlySpan<int>)[64, 48, 36, 80, 96, 112, 128])
            {
                if (stride < span)
                {
                    continue;
                }

                for (var start = 0; start + span <= block.Size; start += 4)
                {
                    if (!turn[start] || taken[start])
                    {
                        continue;
                    }

                    int count = 0;
                    while (start + (count * stride) + span <= block.Size && turn[start + (count * stride)] && !taken[start + (count * stride)])
                    {
                        count++;
                    }

                    if (count < LeastRun)
                    {
                        continue;
                    }

                    runs.Add(new Run(start, stride, count, row));
                    for (var one = 0; one < count; one++)
                    {
                        for (var at = start + (one * stride); at < start + (one * stride) + span && at < block.Size; at++)
                        {
                            taken[at] = true;
                        }
                    }
                }
            }
        }

        return [.. runs.OrderByDescending(one => one.Count)];
    }

    /// <summary>Whether the bytes at <paramref name="at"/> hold a turn: three rows of three floats, one length each, at right angles, not mirrored.</summary>
    public static bool IsTurn(byte[] bytes, int at, int row)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (at < 0 || at + (2 * row) + 12 > bytes.Length)
        {
            return false;
        }

        Vector3 x = Row(bytes, at), y = Row(bytes, at + row), z = Row(bytes, at + (2 * row));
        float lx = x.Length(), ly = y.Length(), lz = z.Length();
        if (!float.IsFinite(lx + ly + lz) || lx is < 1e-2f or > 1e2f)
        {
            return false;
        }

        float slack = Slack * lx;
        return MathF.Abs(ly - lx) <= slack && MathF.Abs(lz - lx) <= slack
            && MathF.Abs(Vector3.Dot(x, y)) <= slack * lx && MathF.Abs(Vector3.Dot(y, z)) <= slack * lx && MathF.Abs(Vector3.Dot(x, z)) <= slack * lx
            && Vector3.Dot(Vector3.Cross(x, y), z) > 0f;
    }

    /// <summary>The turn at <paramref name="at"/> as a unit quaternion, its rows made of one length.</summary>
    public static Quaternion TurnAt(byte[] bytes, int at, int row)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Vector3 x = Vector3.Normalize(Row(bytes, at)), y = Vector3.Normalize(Row(bytes, at + row)), z = Vector3.Normalize(Row(bytes, at + (2 * row)));
        var matrix = new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
    }

    /// <summary>The angle between two turns, in degrees.</summary>
    /// <remarks>
    /// FROM THE TURN BETWEEN THEM, its vector part over its scalar: twice the arc cosine of their dot product reads a hundredth of a degree
    /// of nothing out of a float's last bit near one - a bone fixed on its root seemed to turn on it by 0.07 degrees.
    /// </remarks>
    public static float Degrees(Quaternion a, Quaternion b)
    {
        Quaternion between = Quaternion.Concatenate(Quaternion.Inverse(a), b);
        double vector = Math.Sqrt(((double)between.X * between.X) + ((double)between.Y * between.Y) + ((double)between.Z * between.Z));
        return (float)(2.0 * Math.Atan2(vector, Math.Abs((double)between.W)) * 180.0 / Math.PI);
    }

    /// <summary>
    /// What the reads say: for each block, its runs of turn matrices; for each run, every matrix's turn against the run's first matrix over
    /// the watch - least, most, its period - the matrices that turn on the first written as a table over time; and the block's other changing
    /// floats counted.
    /// </summary>
    public IReadOnlyList<string> Describe()
    {
        var lines = new List<string>(Head);
        SkeletonLines(lines);
        lines.Add(string.Empty);
        lines.Add("=== the runs of turn matrices in the watched blocks - a skeleton's bones side by side - each matrix against the run's first: how far it turns on it over the watch and how often; a held weapon's own bones hold still on its root, a simulated bone (phys_) does not");
        int found = 0;
        HashSet<int> skeletal = [.. Skeletons.Select(one => one.Block)];
        for (var index = 0; index < Blocks.Count; index++)
        {
            Block block = Blocks[index];
            if (skeletal.Contains(index) || index == Player)
            {
                continue;
            }

            IReadOnlyList<Run> runs = Runs(block);
            if (runs.Count == 0)
            {
                continue;
            }

            found += runs.Count;
            double span = block.Reads.Count > 1 ? block.Reads[^1].At - block.Reads[0].At : 0;
            lines.Add(string.Empty);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"--- {block.Name} 0x{block.Address:X} ({block.Size} bytes, {block.Reads.Count} reads over {span:0.0} s): {runs.Count} runs"));
            foreach (Run run in runs)
            {
                RunLines(block, run, lines);
            }
        }

        if (found == 0)
        {
            lines.Add("(no run of turn matrices in any watched block)");
        }

        return lines;
    }

    /// <summary>
    /// The skeletons found by their rest: each array's rig and how it lies, where its records keep their turns, which bones moved over the
    /// watch, and each simulated bone's swing on its parent - its turning points and how fast it settles - with a table of the swings over
    /// time beside the player's speed.
    /// </summary>
    private void SkeletonLines(List<string> lines)
    {
        lines.Add(string.Empty);
        lines.Add("=== the pieces' own skeletons, found in the whole of memory by their rest (PoseHunt) and watched with the rest: each array's rig, frame, layout and stride; where its records keep their turns and how near the fixed bones' turns lie to their rest; which bones moved over the watch; and each simulated (phys_) bone's swing on its parent - by its place on it (a pendulum's way) and by its turn on it - with its period, its turning points and the decrement between them (δ = ln of one swing's height - a turning point to the next - over the height a period on; damping ratio δ / sqrt(4π² + δ²))");
        if (Skeletons.Count == 0)
        {
            lines.Add("(none found, or none looked for)");
            return;
        }

        (double At, Vector3 Place)[] player = Player >= 0
            ? [.. Blocks[Player].Reads.Where(one => one.Bytes.Length >= 12).Select(one => (one.At, Row(one.Bytes, 0)))]
            : [];
        for (var index = 0; index < Skeletons.Count; index++)
        {
            Skeleton skeleton = Skeletons[index];
            Block block = Blocks[skeleton.Block];
            PoseRig rig = skeleton.Rig;
            double span = block.Reads.Count > 1 ? block.Reads[^1].At - block.Reads[0].At : 0;
            lines.Add(string.Empty);
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"--- skeleton {index + 1}: {Path.GetFileName(rig.Piece)}'s rig ({rig.Path}, {rig.Count} bones, {rig.SimulatedCount} simulated) - {skeleton.Frame} frame, {(skeleton.Layout == PoseLayout.Packed ? "x, y and z side by side" : "x, y and z 16 bytes apart")}, {skeleton.Stride} bytes a bone, bone 0's x at 0x{block.Address + (ulong)skeleton.Offset:X}: {skeleton.Matched} of {skeleton.Fixed} fixed bones at their rest when found, {skeleton.Votes} bones' places agreed on it; {block.Reads.Count} reads over {span:0.0} s"));
            if (block.Reads.Count == 0)
            {
                lines.Add("  (no read held)");
                continue;
            }

            TurnRead how = PoseSwing.Turns(block, skeleton);
            lines.Add(how.Kind == TurnKind.None
                ? "  turns: none found beside the translations - measured by places alone"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"  turns: {how.Kind}{(how.Flip ? ", read the other way round (by columns, or conjugated)" : string.Empty)}{(how.WFirst ? ", w first" : string.Empty)} - held in {how.Share:P0} of the bones' reads; the fixed bones' turns {how.Agreement:0.##} degrees from their rest at the first read (median)"));
            MovedLines(block, skeleton, how, lines);

            IReadOnlyList<BoneSwing> swings = PoseSwing.Swings(block, skeleton, how);
            foreach (BoneSwing swing in swings)
            {
                SwingLines(swing, lines);
            }

            SwingTable(block, swings, player, lines);
        }
    }

    /// <summary>Which bones moved over the watch: each whose translation or turn left its first read's, by how much at most.</summary>
    private static void MovedLines(Block block, Skeleton skeleton, TurnRead how, List<string> lines)
    {
        PoseRig rig = skeleton.Rig;
        byte[] first = block.Reads[0].Bytes;
        var moved = new List<string>();
        for (var bone = 0; bone < rig.Count; bone++)
        {
            int x = skeleton.Offset + (bone * skeleton.Stride);
            Vector3 from = PoseHunt.At(first, x, skeleton.Layout);
            Quaternion? turned = PoseSwing.Turn(first, x, how);
            float far = 0, turn = 0;
            foreach ((_, byte[] bytes) in block.Reads)
            {
                far = MathF.Max(far, Vector3.Distance(PoseHunt.At(bytes, x, skeleton.Layout), from));
                if (turned is { } was && PoseSwing.Turn(bytes, x, how) is { } now)
                {
                    turn = MathF.Max(turn, Degrees(was, now));
                }
            }

            if (far > 0.01f || turn > 0.1f)
            {
                moved.Add(string.Create(CultureInfo.InvariantCulture, $"{rig.Names[bone]} ({far:0.###} units, {turn:0.##} degrees)"));
            }
        }

        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"  moved over the watch: {moved.Count} of {rig.Count} bones{(moved.Count == 0 ? " - a copy of the rest, or a pose that held still" : ": " + string.Join(", ", moved.Take(40)) + (moved.Count > 40 ? $" and {moved.Count - 40} more" : string.Empty))}"));
    }

    /// <summary>One simulated bone's swing in words: how far, about which axis, how often, how it settles - and its turning points.</summary>
    private static void SwingLines(BoneSwing swing, List<string> lines)
    {
        float least = swing.Signed.Length > 0 ? swing.Signed.Min() : 0, most = swing.Signed.Length > 0 ? swing.Signed.Max() : 0;
        string rest = swing.By == "place"
            ? string.Create(CultureInfo.InvariantCulture, $"rests {swing.RestLength:0.###} from it, its distance {swing.LeastRatio:0.####}..{swing.MostRatio:0.####} of that; ")
            : string.Empty;
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"  {swing.Bone} on {swing.Parent} by its {swing.By}: {rest}swings {least:0.##}..{most:0.##} degrees about ({swing.Axis.X:0.###}, {swing.Axis.Y:0.###}, {swing.Axis.Z:0.###}), {swing.Angle.DefaultIfEmpty().Max():0.##} from its rest at most; period {Seconds(swing.Period)} by the autocorrelation, {Seconds(swing.PeriodByPeaks)} by its turning points; {swing.Extrema.Count} turning points; decrement {(swing.Pairs > 0 ? string.Create(CultureInfo.InvariantCulture, $"{swing.Decrement:0.###} (damping ratio {swing.DampingRatio:0.####}) over {swing.Pairs} pairs") : "- (no swing followed a period on by a smaller)")}"));
        if (swing.Extrema.Count > 0)
        {
            lines.Add("    turning points: " + string.Join(", ", swing.Extrema.Take(48).Select(one => string.Create(CultureInfo.InvariantCulture, $"{one.At:0.00} s {one.Value:+0.##;-0.##;0}"))));
        }
    }

    /// <summary>The swings over time, a row every thirtieth of a second, beside the player's speed on the ground.</summary>
    private static void SwingTable(Block block, IReadOnlyList<BoneSwing> swings, (double At, Vector3 Place)[] player, List<string> lines)
    {
        if (swings.Count == 0)
        {
            lines.Add("  (no simulated bone with a parent in this rig)");
            return;
        }

        BoneSwing[] shown = [.. swings.Take(MostSwingColumns)];
        lines.Add(string.Join('\t', shown.Select(one => $"{one.Bone} {one.By}").Prepend("his speed").Prepend("  t")));
        double next = block.Reads[0].At;
        for (var read = 0; read < block.Reads.Count; read++)
        {
            double at = block.Reads[read].At;
            if (at < next)
            {
                continue;
            }

            next = at + SeriesStep;
            string speed = Speed(player, at) is { } fast ? fast.ToString("0", CultureInfo.InvariantCulture) : "-";
            lines.Add(string.Join(
                '\t',
                shown.Select(one => one.Signed[read].ToString("0.###", CultureInfo.InvariantCulture))
                    .Prepend(speed)
                    .Prepend("  " + (at - block.Reads[0].At).ToString("0.000", CultureInfo.InvariantCulture))));
        }
    }

    /// <summary>The player's speed on the ground at a time, in units a second over the tenth of a second before it - null where his place was not read then.</summary>
    public static double? Speed((double At, Vector3 Place)[] player, double at)
    {
        ArgumentNullException.ThrowIfNull(player);
        int now = -1;
        for (var read = 0; read < player.Length && player[read].At <= at + 1e-6; read++)
        {
            now = read;
        }

        if (now < 0)
        {
            return null;
        }

        int before = now;
        while (before > 0 && player[now].At - player[before].At < 0.1)
        {
            before--;
        }

        double span = player[now].At - player[before].At;
        if (span <= 0)
        {
            return 0;
        }

        Vector2 moved = new Vector2(player[now].Place.X, player[now].Place.Y) - new Vector2(player[before].Place.X, player[before].Place.Y);
        return moved.Length() / span;
    }

    private static string Seconds(double period) => period > 0 ? period.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "-";

    /// <summary>One run's lines: each matrix's turn on the first, and the ones that turn as a table over time.</summary>
    private static void RunLines(Block block, Run run, List<string> lines)
    {
        int reads = block.Reads.Count;
        double[] times = [.. block.Reads.Select(one => one.At)];
        (float[][] against, float[][] own, bool byRows) = Against(block, run);
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  run at +0x{run.Start:X}: {run.Count} matrices, {run.Stride} bytes apart, rows {run.Row} apart - turned on the first {(byRows ? "as rows (child times the first's inverse)" : "as columns (the first's inverse times child)")}, the order under which the run moves least"));
        lines.Add("  matrix\toffset\tits own turn least..most\ton the first least..most\tits swing on the first's axis, least..most\tthe swing's period\tposition at the first read");
        var moving = new List<int>();
        var swings = new float[run.Count][];
        for (var one = 0; one < run.Count && one < MostWritten; one++)
        {
            int at = run.Start + (one * run.Stride);
            float most = against[one].Max();
            (float[] signed, Vector3 axis) = most > 0.5f ? Swing(block, run, one, byRows) : ([], Vector3.Zero);
            swings[one] = signed;
            double period = signed.Length > 0 ? WobbleProbe.Period(times, signed) : 0;
            Vector3 place = run.Row == 16 && at + 60 <= block.Size ? Row(block.Reads[0].Bytes, at + 48) : default;
            string swing = signed.Length > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{signed.Min():0.##}..{signed.Max():0.##} about ({axis.X:0.###}, {axis.Y:0.###}, {axis.Z:0.###})")
                : "-";
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  {one}\t+0x{at:X}\t{own[one].Min():0.##}..{own[one].Max():0.##}\t{against[one].Min():0.##}..{most:0.##}\t{swing}\t{(period > 0 ? period.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "-")}\t({place.X:0.##}, {place.Y:0.##}, {place.Z:0.##})"));
            if (one > 0 && most > 0.5f)
            {
                moving.Add(one);
            }
        }

        if (run.Count > MostWritten)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  ({run.Count - MostWritten} more not written)"));
        }

        if (moving.Count == 0 || moving.Count > 12)
        {
            lines.Add(moving.Count == 0 ? "  no matrix turns on the first by half a degree" : string.Create(CultureInfo.InvariantCulture, $"  {moving.Count} matrices turn on the first by half a degree or more - a skeleton moving as a whole, no table"));
            return;
        }

        // THE ONES THAT TURN ON THE FIRST, over time - a simulated bone's swing along its axis, signed, read every thirtieth of a second.
        lines.Add(string.Join('\t', moving.Select(one => string.Create(CultureInfo.InvariantCulture, $"m{one}")).Prepend("  t")));
        double next = times.Length > 0 ? times[0] : 0;
        for (var read = 0; read < reads; read++)
        {
            if (times[read] < next)
            {
                continue;
            }

            next = times[read] + SeriesStep;
            lines.Add(string.Join(
                '\t',
                moving.Select(one => swings[one][read].ToString("0.###", CultureInfo.InvariantCulture)).Prepend("  " + (times[read] - times[0]).ToString("0.000", CultureInfo.InvariantCulture))));
        }
    }

    /// <summary>
    /// Each matrix of a run against the run's first over the reads, in degrees from the first read - in whichever order of the two the whole
    /// run moves least (<paramref name="block"/>'s matrices written by rows or by columns) - and each matrix's own turn from its first read.
    /// </summary>
    /// <remarks>
    /// BOTH ORDERS, BECAUSE THE LAYOUT IS NOT KNOWN: written by rows a child's world turn is its local turn times its parent's, so the local
    /// one is the child's times the parent's inverse; written by columns the product turns round. Taken the wrong way round, a bone fixed on
    /// its root reads as turning whenever the root turns - the local turn conjugated by it. A rigid weapon's own bones hold still on its root
    /// in the right order, so the order that moves the whole run least is the layout's.
    /// </remarks>
    public static (float[][] Against, float[][] Own, bool ByRows) Against(Block block, Run run)
    {
        ArgumentNullException.ThrowIfNull(block);
        int reads = block.Reads.Count;
        var rows = new float[run.Count][];
        var columns = new float[run.Count][];
        var own = new float[run.Count][];
        var firsts = new Quaternion[run.Count];
        var rowFirsts = new Quaternion[run.Count];
        var columnFirsts = new Quaternion[run.Count];
        Quaternion rootFirst = reads == 0 ? Quaternion.Identity : TurnAt(block.Reads[0].Bytes, run.Start, run.Row);
        for (var one = 0; one < run.Count; one++)
        {
            rows[one] = new float[reads];
            columns[one] = new float[reads];
            own[one] = new float[reads];
            if (reads > 0)
            {
                firsts[one] = TurnAt(block.Reads[0].Bytes, run.Start + (one * run.Stride), run.Row);
                rowFirsts[one] = Quaternion.Concatenate(firsts[one], Quaternion.Inverse(rootFirst));
                columnFirsts[one] = Quaternion.Concatenate(Quaternion.Inverse(rootFirst), firsts[one]);
            }
        }

        double rowSum = 0, columnSum = 0;
        for (var read = 0; read < reads; read++)
        {
            byte[] bytes = block.Reads[read].Bytes;
            Quaternion root = TurnAt(bytes, run.Start, run.Row), back = Quaternion.Inverse(root);
            for (var one = 0; one < run.Count; one++)
            {
                Quaternion turn = TurnAt(bytes, run.Start + (one * run.Stride), run.Row);
                rows[one][read] = Degrees(Quaternion.Concatenate(turn, back), rowFirsts[one]);
                columns[one][read] = Degrees(Quaternion.Concatenate(back, turn), columnFirsts[one]);
                own[one][read] = Degrees(turn, firsts[one]);
                rowSum += rows[one][read];
                columnSum += columns[one][read];
            }
        }

        return rowSum <= columnSum ? (rows, own, true) : (columns, own, false);
    }

    /// <summary>
    /// One matrix's swing on the run's first, signed: its turn on the first from the first read as a turn vector (the axis times the angle, in
    /// degrees), set along the axis it turns about most over the watch - and that axis. A swing's angle alone passes nought twice a period.
    /// </summary>
    public static (float[] Signed, Vector3 Axis) Swing(Block block, Run run, int matrix, bool byRows)
    {
        ArgumentNullException.ThrowIfNull(block);
        int reads = block.Reads.Count;
        var vectors = new Vector3[reads];
        if (reads == 0)
        {
            return ([], Vector3.Zero);
        }

        Quaternion Relative(byte[] bytes)
        {
            Quaternion root = TurnAt(bytes, run.Start, run.Row), turn = TurnAt(bytes, run.Start + (matrix * run.Stride), run.Row);
            return byRows ? Quaternion.Concatenate(turn, Quaternion.Inverse(root)) : Quaternion.Concatenate(Quaternion.Inverse(root), turn);
        }

        Quaternion first = Relative(block.Reads[0].Bytes);
        for (var read = 0; read < reads; read++)
        {
            Quaternion between = Quaternion.Concatenate(Quaternion.Inverse(first), Relative(block.Reads[read].Bytes));
            if (between.W < 0f)
            {
                between = Quaternion.Negate(between);
            }

            var axis = new Vector3(between.X, between.Y, between.Z);
            float length = axis.Length();
            float degrees = (float)(2.0 * Math.Atan2(length, between.W) * 180.0 / Math.PI);
            vectors[read] = length > 1e-9f ? axis / length * degrees : Vector3.Zero;
        }

        // THE AXIS IT TURNS ABOUT MOST: the spread's leading direction.
        return PoseSwing.Signed(vectors);
    }

    private static Vector3 Row(byte[] bytes, int at)
        => new(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4), BitConverter.ToSingle(bytes, at + 8));
}
