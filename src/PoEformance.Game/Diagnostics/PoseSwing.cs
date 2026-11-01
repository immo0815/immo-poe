using System.Numerics;

namespace PoEformance.Game.Diagnostics;

/// <summary>Where a pose array's records keep each bone's turn, as found from the reads.</summary>
public enum TurnKind
{
    /// <summary>No turn found beside the translations - the array is read for places alone.</summary>
    None,

    /// <summary>A 4x4 by rows, the translation its last row: the turn's three rows sixteen bytes apart, 48 bytes before x.</summary>
    RowsBefore48,

    /// <summary>A 3x4 or a 4x4 whose translation is its last column, by rows of four: the turn's rows sixteen bytes apart, 12 bytes before x.</summary>
    RowsBefore12,

    /// <summary>A 3x3 packed, then the translation: the turn's rows twelve bytes apart, 36 bytes before x.</summary>
    PackedBefore36,

    /// <summary>A quaternion, then the translation: 16 bytes before x.</summary>
    QuaternionBefore16,

    /// <summary>The translation, then a quaternion: 12 bytes after x.</summary>
    QuaternionAfter12,

    /// <summary>The translation padded to four floats, then a quaternion: 16 bytes after x.</summary>
    QuaternionAfter16,
}

/// <summary>How the turns of a pose array are read: where, whether written the other way round, and how well that agrees with the rest.</summary>
/// <param name="Kind">Where a record keeps its turn.</param>
/// <param name="Flip">Whether the turn read is the inverse of the bone's - a matrix by columns, a quaternion conjugated.</param>
/// <param name="WFirst">Whether a quaternion's w comes before its x, y and z.</param>
/// <param name="Share">The share of the bones' reads that held a turn there.</param>
/// <param name="Agreement">The median angle, in degrees, between the fixed bones' turns at the first read and their rest - nought for a rigid piece at rest.</param>
public readonly record struct TurnRead(TurnKind Kind, bool Flip, bool WFirst, double Share, double Agreement);

/// <summary>A simulated bone's swing over the watch, measured on its parent.</summary>
/// <param name="Bone">The bone, by name.</param>
/// <param name="Parent">Its parent, by name.</param>
/// <param name="By">What the swing is measured by: its place on its parent ("place") or its turn on it ("turn").</param>
/// <param name="Signed">Its swing in degrees at each read, along the axis it swings about most.</param>
/// <param name="Axis">That axis, in the parent's frame (the model's where no turn was read).</param>
/// <param name="Angle">Its angle from its rest at each read, in degrees.</param>
/// <param name="RestLength">How far it rests from its parent.</param>
/// <param name="LeastRatio">Its least distance from its parent over the watch, over its rest's.</param>
/// <param name="MostRatio">Its most, likewise.</param>
/// <param name="Period">Its period by the autocorrelation (WobbleProbe.Period), nought where none stands out.</param>
/// <param name="PeriodByPeaks">Its period by the times between the swing's turning points of one kind, nought where there are too few.</param>
/// <param name="Extrema">Its turning points: when, how far, and whether a high.</param>
/// <param name="Decrement">The median logarithm of one swing's height over the next of its phase - nought where there are none.</param>
/// <param name="DampingRatio">The damping ratio that decrement makes, δ / sqrt(4π² + δ²).</param>
/// <param name="Pairs">How many pairs of swings the decrement is the median of.</param>
public sealed record BoneSwing(
    string Bone,
    string Parent,
    string By,
    float[] Signed,
    Vector3 Axis,
    float[] Angle,
    float RestLength,
    float LeastRatio,
    float MostRatio,
    double Period,
    double PeriodByPeaks,
    IReadOnlyList<(double At, float Value, bool High)> Extrema,
    double Decrement,
    double DampingRatio,
    int Pairs);

/// <summary>
/// What a skeleton's pose array watched over seconds says of its simulated bones: where its records keep the turns, and each phys_ bone's
/// swing on its parent - how far, about which axis, how often, and how fast it settles.
/// </summary>
/// <remarks>
/// MEASURED ON ITS PARENT, BECAUSE THE SIMULATION MOVES IT THERE: a lantern swinging on its staff swings on the bone it hangs from, whatever
/// the hand does. Two measures are given where both can be had - the bone's place on its parent (a pendulum's way, from its rest's) and its
/// turn on it - since which of the two the engine simulates is what the watch is to tell. A bone resting at its parent's place has no way,
/// and is measured by its turn alone.
/// </remarks>
public static class PoseSwing
{
    /// <summary>The least size of a turning point counted, in degrees - CHOSEN: below it a float's noise turns.</summary>
    public const float LeastTurningPoint = 0.25f;

    /// <summary>The least share of the reads whose records hold a turn for a place to be taken as the turns'. CHOSEN.</summary>
    public const double LeastShare = 0.9;

    /// <summary>How near one a quaternion's length must be. CHOSEN.</summary>
    private const float UnitSlack = 2e-3f;

    /// <summary>Where a record keeps its turn, relative to its x, and how far its rows lie apart - nought for a quaternion.</summary>
    public static (int Offset, int Row) Place(TurnKind kind) => kind switch
    {
        TurnKind.RowsBefore48 => (-48, 16),
        TurnKind.RowsBefore12 => (-12, 16),
        TurnKind.PackedBefore36 => (-36, 12),
        TurnKind.QuaternionBefore16 => (-16, 0),
        TurnKind.QuaternionAfter12 => (12, 0),
        TurnKind.QuaternionAfter16 => (16, 0),
        _ => (0, 0),
    };

    /// <summary>The bytes a turn of this kind spans.</summary>
    public static int Bytes(TurnKind kind) => Place(kind).Row is > 0 and var row ? (2 * row) + 12 : 16;

    /// <summary>
    /// Where the array's records keep their turns: of the places a record may keep one - those its layout allows - the first whose reads
    /// hold a turn for <see cref="LeastShare"/> of the bones at the first, middle and last reads; then which way round, by the fixed bones'
    /// agreement with their rest.
    /// </summary>
    public static TurnRead Turns(BoneWatchSeries.Block block, BoneWatchSeries.Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(skeleton);
        if (block.Reads.Count == 0)
        {
            return default;
        }

        ReadOnlySpan<TurnKind> kinds = skeleton.Layout == PoseLayout.Packed
            ? [TurnKind.RowsBefore48, TurnKind.QuaternionBefore16, TurnKind.QuaternionAfter12, TurnKind.QuaternionAfter16, TurnKind.PackedBefore36]
            : [TurnKind.RowsBefore12];
        int[] sample = [.. new[] { 0, block.Reads.Count / 2, block.Reads.Count - 1 }.Distinct()];
        foreach (TurnKind kind in kinds)
        {
            int held = 0, asked = 0;
            foreach (int read in sample)
            {
                byte[] bytes = block.Reads[read].Bytes;
                for (var bone = 0; bone < skeleton.Rig.Count; bone++)
                {
                    asked++;
                    held += Holds(bytes, skeleton.Offset + (bone * skeleton.Stride), kind) ? 1 : 0;
                }
            }

            double share = asked == 0 ? 0 : (double)held / asked;
            if (share < LeastShare)
            {
                continue;
            }

            // WHICH WAY ROUND: the variant under which the fixed bones' turns lie nearest their rest - written by columns a matrix read by
            // rows is the inverse, and a quaternion may put its w first.
            TurnRead best = default;
            double least = double.MaxValue;
            foreach (bool flip in (ReadOnlySpan<bool>)[false, true])
            {
                foreach (bool wFirst in Place(kind).Row > 0 ? (ReadOnlySpan<bool>)[false] : [false, true])
                {
                    var how = new TurnRead(kind, flip, wFirst, share, 0);
                    double agreement = Agreement(block.Reads[0].Bytes, skeleton, how);
                    if (agreement < least)
                    {
                        least = agreement;
                        best = how with { Agreement = agreement };
                    }
                }
            }

            return best;
        }

        return new TurnRead(TurnKind.None, false, false, 0, 0);
    }

    /// <summary>Whether the bytes for a bone hold a turn of this kind.</summary>
    private static bool Holds(byte[] bytes, int x, TurnKind kind)
    {
        (int offset, int row) = Place(kind);
        int at = x + offset;
        if (at < 0 || at + Bytes(kind) > bytes.Length)
        {
            return false;
        }

        if (row > 0)
        {
            return BoneWatchSeries.IsTurn(bytes, at, row);
        }

        var q = new Vector4(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4), BitConverter.ToSingle(bytes, at + 8), BitConverter.ToSingle(bytes, at + 12));
        float length = q.Length();
        return float.IsFinite(length) && MathF.Abs(length - 1f) <= UnitSlack;
    }

    /// <summary>A bone's turn as the array holds it, read the way <paramref name="how"/> says - null where there is none.</summary>
    public static Quaternion? Turn(byte[] bytes, int x, TurnRead how)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (how.Kind == TurnKind.None || !Holds(bytes, x, how.Kind))
        {
            return null;
        }

        (int offset, int row) = Place(how.Kind);
        int at = x + offset;
        Quaternion turn;
        if (row > 0)
        {
            turn = BoneWatchSeries.TurnAt(bytes, at, row);
        }
        else
        {
            float a = BitConverter.ToSingle(bytes, at), b = BitConverter.ToSingle(bytes, at + 4), c = BitConverter.ToSingle(bytes, at + 8), d = BitConverter.ToSingle(bytes, at + 12);
            turn = Quaternion.Normalize(how.WFirst ? new Quaternion(b, c, d, a) : new Quaternion(a, b, c, d));
        }

        return how.Flip ? Quaternion.Conjugate(turn) : turn;
    }

    /// <summary>A bone's turn at rest in a frame.</summary>
    public static Quaternion RestTurn(PoseRig rig, PoseFrame frame, int bone)
    {
        ArgumentNullException.ThrowIfNull(rig);
        Matrix4x4 rest = frame == PoseFrame.Local ? rig.Local[bone] : rig.Model[bone];
        return Matrix4x4.Decompose(rest, out _, out Quaternion turn, out _) ? Quaternion.Normalize(turn) : Quaternion.Identity;
    }

    /// <summary>The median angle between the fixed bones' turns as read and at their rest.</summary>
    private static double Agreement(byte[] bytes, BoneWatchSeries.Skeleton skeleton, TurnRead how)
    {
        var angles = new List<double>();
        for (var bone = 0; bone < skeleton.Rig.Count; bone++)
        {
            if (PoseHunt.Holds(skeleton.Rig, skeleton.Frame, bone) && Turn(bytes, skeleton.Offset + (bone * skeleton.Stride), how) is { } turn)
            {
                angles.Add(BoneWatchSeries.Degrees(turn, RestTurn(skeleton.Rig, skeleton.Frame, bone)));
            }
        }

        if (angles.Count == 0)
        {
            return double.MaxValue;
        }

        angles.Sort();
        return angles[angles.Count / 2];
    }

    /// <summary>
    /// Each simulated bone's swing on its parent over the watch - by its place on it where it rests away from it, else by its turn on it.
    /// </summary>
    public static IReadOnlyList<BoneSwing> Swings(BoneWatchSeries.Block block, BoneWatchSeries.Skeleton skeleton, TurnRead how)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(skeleton);
        PoseRig rig = skeleton.Rig;
        var swings = new List<BoneSwing>();
        int reads = block.Reads.Count;
        if (reads == 0)
        {
            return swings;
        }

        double[] times = [.. block.Reads.Select(one => one.At)];
        for (var bone = 0; bone < rig.Count; bone++)
        {
            int parent = rig.Parents[bone];
            if (!rig.Simulated(bone) || parent < 0)
            {
                continue;
            }

            if (Way(block, skeleton, how, bone, parent) is { } way)
            {
                swings.Add(Measure(rig.Names[bone], rig.Names[parent], "place", times, way.Vectors, way.Length, way.Ratios));
            }

            if (how.Kind != TurnKind.None && Turned(block, skeleton, how, bone, parent) is { } turned)
            {
                swings.Add(Measure(rig.Names[bone], rig.Names[parent], "turn", times, turned, 0f, null));
            }
        }

        return swings;
    }

    /// <summary>
    /// A bone's way from its parent at each read, as a turn vector from its rest's way (the axis times the angle, in degrees), with its
    /// length over the rest's - null where it rests at its parent's place.
    /// </summary>
    private static (Vector3[] Vectors, float Length, float[] Ratios)? Way(BoneWatchSeries.Block block, BoneWatchSeries.Skeleton skeleton, TurnRead how, int bone, int parent)
    {
        PoseRig rig = skeleton.Rig;
        bool local = skeleton.Frame == PoseFrame.Local;
        Vector3 rest = local
            ? rig.Local[bone].Translation
            : rig.Model[bone].Translation - rig.Model[parent].Translation;
        bool turns = !local && how.Kind != TurnKind.None;
        if (turns)
        {
            rest = Vector3.Transform(rest, Quaternion.Inverse(RestTurn(rig, PoseFrame.Model, parent)));
        }

        float length = rest.Length();
        if (length < 1e-3f)
        {
            return null;
        }

        var vectors = new Vector3[block.Reads.Count];
        var ratios = new float[block.Reads.Count];
        int x = skeleton.Offset + (bone * skeleton.Stride), xp = skeleton.Offset + (parent * skeleton.Stride);
        for (var read = 0; read < block.Reads.Count; read++)
        {
            byte[] bytes = block.Reads[read].Bytes;
            Vector3 way = PoseHunt.At(bytes, x, skeleton.Layout);
            if (!local)
            {
                way -= PoseHunt.At(bytes, xp, skeleton.Layout);
                if (turns && Turn(bytes, xp, how) is { } over)
                {
                    way = Vector3.Transform(way, Quaternion.Inverse(over));
                }
            }

            ratios[read] = way.Length() / length;
            vectors[read] = Between(rest, way);
        }

        return (vectors, length, ratios);
    }

    /// <summary>A bone's turn on its parent at each read against its rest's, as a turn vector - null where a read holds no turn.</summary>
    private static Vector3[]? Turned(BoneWatchSeries.Block block, BoneWatchSeries.Skeleton skeleton, TurnRead how, int bone, int parent)
    {
        PoseRig rig = skeleton.Rig;
        bool local = skeleton.Frame == PoseFrame.Local;
        Quaternion rest = RestTurn(rig, PoseFrame.Local, bone);
        var vectors = new Vector3[block.Reads.Count];
        int x = skeleton.Offset + (bone * skeleton.Stride), xp = skeleton.Offset + (parent * skeleton.Stride);
        for (var read = 0; read < block.Reads.Count; read++)
        {
            byte[] bytes = block.Reads[read].Bytes;
            if (Turn(bytes, x, how) is not { } turn)
            {
                return null;
            }

            if (!local)
            {
                if (Turn(bytes, xp, how) is not { } over)
                {
                    return null;
                }

                // ROW VECTORS: a child's model turn is its local one followed by its parent's.
                turn = Quaternion.Concatenate(turn, Quaternion.Inverse(over));
            }

            Quaternion between = Quaternion.Concatenate(Quaternion.Inverse(rest), turn);
            if (between.W < 0f)
            {
                between = Quaternion.Negate(between);
            }

            var axis = new Vector3(between.X, between.Y, between.Z);
            float size = axis.Length();
            float degrees = (float)(2.0 * Math.Atan2(size, between.W) * 180.0 / Math.PI);
            vectors[read] = size > 1e-9f ? axis / size * degrees : Vector3.Zero;
        }

        return vectors;
    }

    /// <summary>The turn from one way to another as a vector: the axis times the angle in degrees.</summary>
    public static Vector3 Between(Vector3 from, Vector3 to)
    {
        Vector3 axis = Vector3.Cross(from, to);
        float size = axis.Length();
        float angle = MathF.Atan2(size, Vector3.Dot(from, to)) * 180f / MathF.PI;
        return size > 1e-12f ? axis / size * angle : Vector3.Zero;
    }

    /// <summary>A swing measured: signed along its main axis, its period both ways, its turning points and its decrement.</summary>
    /// <param name="bone">The bone, by name.</param>
    /// <param name="parent">What it swings on, by name.</param>
    /// <param name="by">What it is measured by - "place", "turn", or a place in the world.</param>
    /// <param name="times">Each read's time.</param>
    /// <param name="vectors">Its turn from its rest at each read, the axis times the angle in degrees.</param>
    /// <param name="rest">How far it rests from what it swings on - nought where measured by its turn.</param>
    /// <param name="ratios">Its distance at each read over its rest's, or null.</param>
    public static BoneSwing Measure(string bone, string parent, string by, double[] times, Vector3[] vectors, float rest, float[]? ratios)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(vectors);
        (float[] signed, Vector3 axis) = Signed(vectors);
        float[] angle = [.. vectors.Select(one => one.Length())];
        IReadOnlyList<(double At, float Value, bool High)> extrema = Extrema(times, signed, LeastTurningPoint);
        (double decrement, double ratio, int pairs) = Damping(extrema);
        return new BoneSwing(
            bone, parent, by, signed, axis, angle, rest,
            ratios is { Length: > 0 } ? ratios.Min() : 1f, ratios is { Length: > 0 } ? ratios.Max() : 1f,
            WobbleProbe.Period(times, signed), PeriodByPeaks(extrema), extrema, decrement, ratio, pairs);
    }

    /// <summary>
    /// Turn vectors set along the axis they spread along most - the spread's leading direction, by a few steps of the power method from the
    /// widest of x, y and z - and that axis.
    /// </summary>
    public static (float[] Signed, Vector3 Axis) Signed(Vector3[] vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        if (vectors.Length == 0)
        {
            return ([], Vector3.Zero);
        }

        Vector3 mean = Vector3.Zero;
        foreach (Vector3 one in vectors)
        {
            mean += one;
        }

        mean /= vectors.Length;
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (Vector3 one in vectors)
        {
            Vector3 d = one - mean;
            (xx, xy, xz, yy, yz, zz) = (xx + (d.X * d.X), xy + (d.X * d.Y), xz + (d.X * d.Z), yy + (d.Y * d.Y), yz + (d.Y * d.Z), zz + (d.Z * d.Z));
        }

        Vector3 lead = xx >= yy && xx >= zz ? Vector3.UnitX : yy >= zz ? Vector3.UnitY : Vector3.UnitZ;
        for (var step = 0; step < 24; step++)
        {
            var next = new Vector3((xx * lead.X) + (xy * lead.Y) + (xz * lead.Z), (xy * lead.X) + (yy * lead.Y) + (yz * lead.Z), (xz * lead.X) + (yz * lead.Y) + (zz * lead.Z));
            float size = next.Length();
            if (size <= 1e-12f)
            {
                break;
            }

            lead = next / size;
        }

        var signed = new float[vectors.Length];
        for (var at = 0; at < vectors.Length; at++)
        {
            signed[at] = Vector3.Dot(vectors[at], lead);
        }

        return (signed, lead);
    }

    /// <summary>
    /// A series' turning points: each read higher (or lower) than every read within a tenth of a second either side and at least
    /// <paramref name="least"/> from the series' mean - one a run of equal reads - and whether it is a high or a low.
    /// </summary>
    public static IReadOnlyList<(double At, float Value, bool High)> Extrema(double[] times, float[] values, float least)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(values);
        var found = new List<(double At, float Value, bool High)>();
        if (values.Length < 3)
        {
            return found;
        }

        const double Reach = 0.1;
        float mean = values.Average();
        int from = 0, to = 0;
        for (var at = 0; at < values.Length; at++)
        {
            while (times[from] < times[at] - Reach)
            {
                from++;
            }

            while (to + 1 < values.Length && times[to + 1] <= times[at] + Reach)
            {
                to++;
            }

            float value = values[at];
            if (MathF.Abs(value - mean) < least || at == 0 || at == values.Length - 1)
            {
                continue;
            }

            bool high = true, low = true;
            for (int other = from; other <= to && (high || low); other++)
            {
                if (other == at)
                {
                    continue;
                }

                // ONE A RUN OF EQUAL READS: the first of them stands, the rest yield to it.
                high &= values[other] < value || (values[other] == value && other > at);
                low &= values[other] > value || (values[other] == value && other > at);
            }

            if ((high && value > mean) || (low && value < mean))
            {
                found.Add((times[at], value, high && value > mean));
            }
        }

        return found;
    }

    /// <summary>
    /// How fast a swing settles: the median logarithm of one swing's height over the swing's a period on - a swing's height the distance from
    /// a turning point to the next of the other kind, which no offset of the swing's middle changes - and the damping ratio it makes, from the
    /// pairs whose second is the smaller and starts within two and a half seconds.
    /// </summary>
    /// <remarks>
    /// HEIGHTS, NOT TURNING POINTS FROM A MEAN: a lantern settling a few degrees from its rest - the hand held still at a slant, the walk
    /// pushing it - swings about that place, and a turning point's distance from the mean of a decaying series is not its distance from the
    /// middle it swings about. For x = m + A e^(-σt) cos(ωt) one height over the next of its phase is e^(σT) whatever m is.
    /// </remarks>
    public static (double Decrement, double Ratio, int Pairs) Damping(IReadOnlyList<(double At, float Value, bool High)> extrema)
    {
        ArgumentNullException.ThrowIfNull(extrema);
        var heights = new List<(double At, double Height)>();
        for (var at = 0; at + 1 < extrema.Count; at++)
        {
            if (extrema[at].High != extrema[at + 1].High)
            {
                heights.Add((extrema[at].At, Math.Abs(extrema[at + 1].Value - extrema[at].Value)));
            }
            else
            {
                heights.Add((extrema[at].At, double.NaN));
            }
        }

        var decrements = new List<double>();
        for (var at = 0; at + 2 < heights.Count; at++)
        {
            (double when, double first) = heights[at];
            (double then, double second) = heights[at + 2];
            if (double.IsNaN(first) || double.IsNaN(second) || then - when > 2.5 || second <= 1e-6 || second >= first)
            {
                continue;
            }

            decrements.Add(Math.Log(first / second));
        }

        if (decrements.Count == 0)
        {
            return (0, 0, 0);
        }

        decrements.Sort();
        double median = decrements[decrements.Count / 2];
        return (median, median / Math.Sqrt((4 * Math.PI * Math.PI) + (median * median)), decrements.Count);
    }

    /// <summary>The period by the turning points: the median time from one to the next of its kind - nought where there are too few.</summary>
    public static double PeriodByPeaks(IReadOnlyList<(double At, float Value, bool High)> extrema)
    {
        ArgumentNullException.ThrowIfNull(extrema);
        var gaps = new List<double>();
        for (var at = 0; at < extrema.Count; at++)
        {
            for (int next = at + 1; next < extrema.Count; next++)
            {
                if (extrema[next].High == extrema[at].High)
                {
                    gaps.Add(extrema[next].At - extrema[at].At);
                    break;
                }
            }
        }

        if (gaps.Count == 0)
        {
            return 0;
        }

        gaps.Sort();
        return gaps[gaps.Count / 2];
    }
}
