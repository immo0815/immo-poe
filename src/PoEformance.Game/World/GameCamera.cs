using System.Numerics;

namespace PoEformance.Game.World;

/// <summary>
/// The game's camera as an eye and a frame: the world-to-clip matrix (WorldData + 0x1A0, see <see cref="WorldToScreen"/>) taken apart.
/// </summary>
/// <param name="Eye">Where the eye stands, world units.</param>
/// <param name="Right">The way the picture's x grows, unit length.</param>
/// <param name="Down">The way the picture's y grows - down the screen - unit length.</param>
/// <param name="Forward">The way the eye looks, unit length: a point's depth is along it.</param>
/// <param name="FocalX">Pixels across per unit across at a unit's depth.</param>
/// <param name="FocalY">Pixels down per unit down at a unit's depth.</param>
/// <param name="Principal">The pixel the eye's line lands on - the middle of the screen unless the frustum is off centre.</param>
/// <param name="Near">The depth under which the game draws nothing, world units.</param>
/// <param name="Far">The depth past which it draws nothing.</param>
/// <param name="Width">The screen the matrix projects onto, pixels.</param>
/// <param name="Height">Its height.</param>
/// <param name="Skew">How much of the right way the picture's y takes, as the matrix's rows have it - nought for the game's, whose rows stand square.</param>
/// <remarks>
/// WORKED OUT FROM THE ROWS AS <see cref="WorldToScreen.Clip"/> READS THEM: clip x, y and w are each a dot of the point with a column of the
/// flat array plus its last entry, so the eye is where all three are nought, the forward way is w's row (w is the depth in world units -
/// its row's length one), and the picture's right and down are x's and y's rows less their share along it. Their lengths are the focal
/// lengths; what they keep along the forward way moves the principal point off the middle (a side panel open makes the frustum lopsided -
/// the schema's FrustumPlanes). A pixel is then <c>Principal + (FocalX (R.d), FocalY (D.d)) / (F.d)</c> with <c>d</c> the point less the
/// eye - exactly <see cref="WorldToScreen.Project"/>, held to it by the tests.
///
/// THE FRAME IS LEFT-HANDED as the game's world is: <c>Right x Down = -Forward</c>, the improper frame CameraPose.Turning builds from its
/// two angles with x turned over. Taken from the matrix as it stands, it needs no turning over of its own.
/// </remarks>
public sealed record GameCamera(
    Vector3 Eye, Vector3 Right, Vector3 Down, Vector3 Forward, double FocalX, double FocalY, Vector2 Principal, float Near, float Far,
    int Width, int Height, double Skew)
{
    /// <summary>The camera the matrix projects with onto a screen this size, or null where it is not a camera - too few numbers, or rows that do not span.</summary>
    /// <param name="matrix">The 16 floats, as WorldToScreen reads them.</param>
    /// <param name="width">The screen's width, pixels.</param>
    /// <param name="height">Its height.</param>
    public static GameCamera? Of(ReadOnlySpan<float> matrix, int width, int height)
    {
        if (matrix.Length < 16 || width <= 0 || height <= 0)
        {
            return null;
        }

        // IN DOUBLE THROUGHOUT: the eye comes out of a three by three solve whose rows are thousands apart in size.
        var ax = new Vector3D(matrix[0], matrix[4], matrix[8]);
        var ay = new Vector3D(matrix[1], matrix[5], matrix[9]);
        var az = new Vector3D(matrix[2], matrix[6], matrix[10]);
        var aw = new Vector3D(matrix[3], matrix[7], matrix[11]);
        double bx = matrix[12], by = matrix[13], bz = matrix[14], bw = matrix[15];

        double k = aw.Length;
        if (!(k > 1e-12))
        {
            return null;
        }

        // THE EYE: where x, y and w are all nought.
        if (Solve(ax, ay, aw, -bx, -by, -bw) is not { } eye)
        {
            return null;
        }

        Vector3D forward = aw / k;
        double alongX = ax.Dot(forward);
        Vector3D acrossX = ax - (forward * alongX);
        double sx = acrossX.Length;
        double alongY = ay.Dot(forward);
        Vector3D acrossY = ay - (forward * alongY);
        if (!(sx > 1e-12))
        {
            return null;
        }

        Vector3D right = acrossX / sx;
        double skew = acrossY.Dot(right);
        Vector3D up = acrossY - (right * skew);
        double sy = up.Length;
        if (!(sy > 1e-12))
        {
            return null;
        }

        up /= sy;

        // NEAR AND FAR FROM z's ROW: the clip depth z / w = A + B / w, nought at the near plane and one at the far (either way round: they are sorted).
        double a = az.Dot(aw) / aw.Dot(aw);
        double b = bz - (a * bw);
        // w IS k TIMES THE DEPTH along the forward way, k the row's length (1.0001 in the fixtures).
        double near = Math.Abs(a) > 1e-12 ? -b / a / k : 0d;
        double far = Math.Abs(1d - a) > 1e-12 ? b / (1d - a) / k : double.PositiveInfinity;
        if (near > far)
        {
            (near, far) = (far, near);
        }

        return new GameCamera(
            eye.ToVector3(),
            right.ToVector3(),
            (-up).ToVector3(),
            forward.ToVector3(),
            width * sx / (2d * k),
            height * sy / (2d * k),
            new Vector2((float)(width * 0.5 * (1d + (alongX / k))), (float)(height * 0.5 * (1d - (alongY / k)))),
            (float)Math.Max(0d, near),
            (float)far,
            width,
            height,
            skew / sy);
    }

    /// <summary>The model-to-view turn for a model whose space is the world less <paramref name="origin"/> across, the eye at the view's origin.</summary>
    /// <remarks>Row vectors, as System.Numerics multiplies them: a point's view x is along <see cref="Right"/>, y along <see cref="Down"/> and z, its depth, along <see cref="Forward"/>.</remarks>
    public Matrix4x4 View(Vector2 origin)
    {
        var basis = new Matrix4x4(
            Right.X, Down.X, Forward.X, 0f,
            Right.Y, Down.Y, Forward.Y, 0f,
            Right.Z, Down.Z, Forward.Z, 0f,
            0f, 0f, 0f, 1f);
        return Matrix4x4.CreateTranslation(-EyeIn(origin)) * basis;
    }

    /// <summary>The eye in a model whose space is the world less <paramref name="origin"/> across, its z the world's.</summary>
    public Vector3 EyeIn(Vector2 origin) => Eye - new Vector3(origin, 0f);

    /// <summary>Where a world point lands on the screen, pixels - <see cref="WorldToScreen.Project"/>'s answer, worked out the camera's way; NaN behind the eye.</summary>
    public Vector2 Pixel(Vector3 world)
    {
        Vector3 d = world - Eye;
        float depth = Vector3.Dot(Forward, d);
        if (!(depth > 1e-6f))
        {
            return new Vector2(float.NaN);
        }

        float across = Vector3.Dot(Right, d), down = Vector3.Dot(Down, d);
        return new Vector2(
            Principal.X + (float)(FocalX * across / depth),
            Principal.Y + (float)(FocalY * (down - (Skew * across)) / depth));
    }

    /// <summary>The way from the eye through the middle of a pixel, unit length, world units.</summary>
    public Vector3 Ray(float x, float y)
    {
        Vector3 way = Forward + (Right * (float)((x - Principal.X) / FocalX)) + (Down * (float)((y - Principal.Y) / FocalY));
        return Vector3.Normalize(way);
    }

    /// <summary>Says the camera in a line: the eye, the way it looks, the focal lengths, the principal point and the planes.</summary>
    public string Said()
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"eye ({Eye.X:0.##}, {Eye.Y:0.##}, {Eye.Z:0.##}), looking ({Forward.X:0.#####}, {Forward.Y:0.#####}, {Forward.Z:0.#####}), right ({Right.X:0.#####}, {Right.Y:0.#####}, {Right.Z:0.#####}), focal {FocalX:0.###} x {FocalY:0.###} px ({2d * Math.Atan(Height / (2d * FocalY)) * 180d / Math.PI:0.###} degrees top to bottom), principal ({Principal.X:0.##}, {Principal.Y:0.##}) on {Width} x {Height}, skew {Skew:0.######}, near {Near:0.#} far {Far:0.#}");

    private static Vector3D? Solve(Vector3D r0, Vector3D r1, Vector3D r2, double c0, double c1, double c2)
    {
        double det = r0.Dot(r1.Cross(r2));
        if (!(Math.Abs(det) > 1e-18))
        {
            return null;
        }

        // CRAMER'S RULE over the rows: x = (c0 (r1 x r2) + c1 (r2 x r0) + c2 (r0 x r1)) / det.
        return ((r1.Cross(r2) * c0) + (r2.Cross(r0) * c1) + (r0.Cross(r1) * c2)) / det;
    }

    /// <summary>Three doubles - the solve's precision.</summary>
    private readonly record struct Vector3D(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt(Dot(this));

        public double Dot(Vector3D other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

        public Vector3D Cross(Vector3D other) => new((Y * other.Z) - (Z * other.Y), (Z * other.X) - (X * other.Z), (X * other.Y) - (Y * other.X));

        public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);

        public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vector3D operator -(Vector3D a) => new(-a.X, -a.Y, -a.Z);

        public static Vector3D operator *(Vector3D a, double s) => new(a.X * s, a.Y * s, a.Z * s);

        public static Vector3D operator /(Vector3D a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    }
}
