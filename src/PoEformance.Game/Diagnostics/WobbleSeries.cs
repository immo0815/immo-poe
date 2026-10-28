using System.Globalization;
using System.Text;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// The wobble watch read back from its file (<c>wobble.txt</c>, WobbleProbe): each wobble point's WobblePoint over time, its rows of
/// a turn worked out - the offline reader of the capture's wobble part.
/// </summary>
/// <remarks>
/// THE TURN AT +0x68 IS A READING, not a measurement: the one read before this part existed (v0.1.289's capture) held three rows of
/// four floats there, (1, 0, -0.0005, 0), (0, 1, -0.0013, 0), (0.0005, 0.0013, 1, 0) - a rotation by 0.078 degrees and nothing
/// moved. Read as such here, its angle and axis written for every row, so the file shows whether it stays a rotation over time.
/// </remarks>
public static class WobbleSeries
{
    /// <summary>Where the three rows of the turn start, four floats apart.</summary>
    public const int Turn = 0x68;

    /// <summary>One wobble point's WobblePoint over time: the first read whole, and each row's time and changed fields.</summary>
    public sealed record Point(string Name, byte[] First, IReadOnlyList<int> Fields, IReadOnlyList<(double At, float[] Values)> Rows)
    {
        /// <summary>The block at one row: the first read with the row's changed fields over it.</summary>
        public byte[] At(int row)
        {
            byte[] bytes = (byte[])First.Clone();
            for (var field = 0; field < Fields.Count; field++)
            {
                if (Fields[field] + 4 <= bytes.Length)
                {
                    BitConverter.TryWriteBytes(bytes.AsSpan(Fields[field]), Rows[row].Values[field]);
                }
            }

            return bytes;
        }
    }

    /// <summary>Every wobble point the file holds a table for.</summary>
    public static IReadOnlyList<Point> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Split('\n');
        var firsts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var points = new List<Point>();
        string? owner = null;
        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.StartsWith("=== wobble point ", StringComparison.Ordinal))
            {
                owner = line.Split(' ')[3];
            }
            else if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                owner = null;
            }

            // THE FIRST READ: the hex rows under "-- WobblePoint", two quadwords a row.
            if (owner is not null && line.TrimStart().StartsWith("-- " + WobbleProbe.Component + " ", StringComparison.Ordinal))
            {
                byte[] bytes = new byte[WobbleProbe.PointBytes];
                for (index++; index < lines.Length && lines[index].StartsWith("    +", StringComparison.Ordinal); index++)
                {
                    string[] parts = lines[index].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    int row = int.Parse(parts[0][1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    for (var quad = 0; quad < 2 && row + (quad * 8) + 8 <= bytes.Length; quad++)
                    {
                        BitConverter.TryWriteBytes(bytes.AsSpan(row + (quad * 8)), ulong.Parse(parts[1 + quad], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    }
                }

                firsts[owner] = bytes;
                index--;
                continue;
            }

            // THE TABLE: a heading naming the point, the columns, then a row a time.
            const string Heading = "=== " + WobbleProbe.Component + " of ";
            if (!line.StartsWith(Heading, StringComparison.Ordinal) || index + 1 >= lines.Length)
            {
                continue;
            }

            string name = line[Heading.Length..].Split(' ')[0];
            int[] fields = [.. lines[index + 1].TrimEnd('\r').Split('\t').Skip(1).Where(one => one.Length > 3)
                .Select(one => int.Parse(one[3..], NumberStyles.HexNumber, CultureInfo.InvariantCulture))];
            var rows = new List<(double, float[])>();
            for (index += 2; index < lines.Length; index++)
            {
                string[] cells = lines[index].TrimEnd('\r').Split('\t');
                if (cells.Length != fields.Length + 1 || !double.TryParse(cells[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double at))
                {
                    break;
                }

                rows.Add((at, [.. cells.Skip(1).Select(cell => float.Parse(cell, NumberStyles.Float, CultureInfo.InvariantCulture))]));
            }

            index--;
            points.Add(new Point(name, firsts.GetValueOrDefault(name) ?? new byte[WobbleProbe.PointBytes], fields, rows));
        }

        return points;
    }

    /// <summary>A row of the turn: its angle in degrees and its axis, read as a rotation's three rows at <see cref="Turn"/>.</summary>
    public static (double Degrees, double X, double Y, double Z) TurnOf(byte[] block)
    {
        ArgumentNullException.ThrowIfNull(block);
        float M(int row, int column) => BitConverter.ToSingle(block, Turn + (row * 16) + (column * 4));
        double trace = M(0, 0) + M(1, 1) + M(2, 2);

        // THE ANGLE AND AXIS FROM THE SKEW PART, twice the sine: the trace's cosine is lost to a float's precision at a tenth of a
        // degree, where the skew still holds three figures.
        double x = M(2, 1) - M(1, 2), y = M(0, 2) - M(2, 0), z = M(1, 0) - M(0, 1);
        double length = Math.Sqrt((x * x) + (y * y) + (z * z));
        double angle = Math.Atan2(length / 2, (trace - 1) / 2);
        return length > 0 ? (angle * 180 / Math.PI, x / length, y / length, z / length) : (0, 0, 0, 0);
    }

    /// <summary>What the file says, worked out: each point's changed fields with their range and period, and its turn over time.</summary>
    public static string Report(IReadOnlyList<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture, $"wobble replay: {points.Count} wobble points").AppendLine();
        foreach (Point point in points)
        {
            double[] times = [.. point.Rows.Select(row => row.At)];
            said.AppendLine().Append(CultureInfo.InvariantCulture, $"=== {point.Name}: {point.Rows.Count} rows over {(times.Length > 0 ? times[^1] - times[0] : 0):0.00} s").AppendLine();
            for (var field = 0; field < point.Fields.Count; field++)
            {
                float[] values = [.. point.Rows.Select(row => row.Values[field])];
                double period = WobbleProbe.Period(times, values);
                said.Append(CultureInfo.InvariantCulture, $"  +0x{point.Fields[field]:X}: {values.Min():G6} .. {values.Max():G6}, period {(period > 0 ? period.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "none")}").AppendLine();
            }

            float[] angles = new float[point.Rows.Count];
            said.AppendLine("  the turn at +0x68 read as a rotation - t, degrees, axis:");
            for (var row = 0; row < point.Rows.Count; row++)
            {
                (double degrees, double x, double y, double z) = TurnOf(point.At(row));
                angles[row] = (float)degrees;
                if (row % 5 == 0)
                {
                    said.Append(CultureInfo.InvariantCulture, $"    {point.Rows[row].At:0.00}\t{degrees:0.0000}\t{x:0.000} {y:0.000} {z:0.000}").AppendLine();
                }
            }

            if (angles.Length > 0)
            {
                double period = WobbleProbe.Period(times, angles);
                said.Append(CultureInfo.InvariantCulture, $"  the turn's angle {angles.Min():0.0000} .. {angles.Max():0.0000} degrees, period {(period > 0 ? period.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "none")}").AppendLine();
            }

            if (FixedPoint(point) is { } fixedPoint)
            {
                said.Append(CultureInfo.InvariantCulture, $"  the point the turn holds still: {fixedPoint.Point.X:0.##} {fixedPoint.Point.Y:0.##} {fixedPoint.Point.Z:0.##}, the translation left over {fixedPoint.Residual:0.###} at most, of {fixedPoint.Largest:0.###}").AppendLine();
            }
        }

        return said.ToString();
    }

    /// <summary>
    /// The point every row's matrix leaves where it is, fitted over the rows - p (I - R) = t for row vectors - with the most it misses
    /// any row's translation by, and the largest translation: a pure turn about one point leaves next to nothing over.
    /// </summary>
    public static (System.Numerics.Vector3 Point, double Residual, double Largest)? FixedPoint(Point point)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (point.Rows.Count < 3)
        {
            return null;
        }

        // THE NORMAL EQUATIONS over every row: each row's three equations are A p = t with A = (I - R) transposed.
        double[,] normal = new double[3, 3];
        double[] right = new double[3];
        var rows = new List<(double[,] A, double[] T)>(point.Rows.Count);
        for (var row = 0; row < point.Rows.Count; row++)
        {
            byte[] block = point.At(row);
            float M(int r, int c) => BitConverter.ToSingle(block, Turn + (r * 16) + (c * 4));
            var a = new double[3, 3];
            for (var r = 0; r < 3; r++)
            {
                for (var c = 0; c < 3; c++)
                {
                    a[r, c] = (r == c ? 1 : 0) - M(c, r);
                }
            }

            double[] t = [M(3, 0), M(3, 1), M(3, 2)];
            rows.Add((a, t));
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        normal[i, j] += a[k, i] * a[k, j];
                    }
                }

                for (var k = 0; k < 3; k++)
                {
                    right[i] += a[k, i] * t[k];
                }
            }
        }

        if (Solve(normal, right) is not { } p)
        {
            return null;
        }

        double residual = 0, largest = 0;
        foreach ((double[,] a, double[] t) in rows)
        {
            for (var k = 0; k < 3; k++)
            {
                double fitted = (a[k, 0] * p[0]) + (a[k, 1] * p[1]) + (a[k, 2] * p[2]);
                residual = Math.Max(residual, Math.Abs(fitted - t[k]));
                largest = Math.Max(largest, Math.Abs(t[k]));
            }
        }

        return (new System.Numerics.Vector3((float)p[0], (float)p[1], (float)p[2]), residual, largest);
    }

    /// <summary>A three by three system by Cramer's rule, or null where it is singular.</summary>
    private static double[]? Solve(double[,] m, double[] b)
    {
        double Det(double[,] x) =>
            (x[0, 0] * ((x[1, 1] * x[2, 2]) - (x[1, 2] * x[2, 1])))
            - (x[0, 1] * ((x[1, 0] * x[2, 2]) - (x[1, 2] * x[2, 0])))
            + (x[0, 2] * ((x[1, 0] * x[2, 1]) - (x[1, 1] * x[2, 0])));
        double det = Det(m);
        if (Math.Abs(det) < 1e-30)
        {
            return null;
        }

        var solved = new double[3];
        for (var column = 0; column < 3; column++)
        {
            var swapped = (double[,])m.Clone();
            for (var row = 0; row < 3; row++)
            {
                swapped[row, column] = b[row];
            }

            solved[column] = Det(swapped) / det;
        }

        return solved;
    }
}
