using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// The engine's cloth arithmetic, function by function as it stands in the game's own code - each held against the game's code run on the
/// same inputs (ClothEngineTests, over tests/fixtures/cloth-engine).
/// </summary>
/// <remarks>
/// READ FROM THE PROGRAM (v0.1.421; the build of 2026-10-27, program-code.bin of the capture 2026-11-01 11:23) and RUN: every function here
/// was executed under a CPU emulator over that image on random inputs, and its outputs are the fixtures - so a formula here is the game's to
/// float precision unless its summary says otherwise. A matrix is the engine's 4x3 in a Matrix4x4: rows one to three the turn (row
/// vectors, as Vector3.Transform reads them), the fourth the place; "a · b" is <see cref="Matrix4x4.Multiply(Matrix4x4, Matrix4x4)"/>
/// (a first) - the engine's compose (module+0x13A4C00) is exactly that.
/// </remarks>
public static class ClothMath
{
    /// <summary>The distance a contact is made within, outside the shape (the world step's context, module+0x139E940: 5).</summary>
    public const float ContactMargin = 5f;

    /// <summary>The solver's iterations a step (module+0x13A0990).</summary>
    public const int Iterations = 10;

    /// <summary>A bend constraint is solved every third iteration, three times a step (module+0x13A0990).</summary>
    public const int BendPasses = 3;

    /// <summary>
    /// A distance constraint's correction (module+0x139C6E0): both ends moved along the line between them, the move added to each one's
    /// displacement as well - so the step's motion keeps it. Shorter than its rest it is left alone until shorter by more than
    /// <paramref name="contraction"/> of the rest; a negative <paramref name="stiffness"/> corrects whole, a stiffness of nought or more by
    /// stiffness · C · dt² / iterations, held to the error.
    /// </summary>
    public static void Distance(
        ref Vector3 xa, ref Vector3 da, bool kinematicA, ref Vector3 xb, ref Vector3 db, bool kinematicB,
        float rest, float stiffness, float contraction, float dt, int iterations)
    {
        Vector3 along = xb - xa;
        float length = MathF.Sqrt(along.LengthSquared());

        // AS WRITTEN: no guard on a length of nought - the engine divides by it.
        Vector3 unit = along / length;
        float error = length - rest;
        if (error < 0f)
        {
            error = MathF.Min((rest * contraction) + error, 0f);
        }

        float magnitude = MathF.Abs(error);
        float correction = stiffness >= 0f
            ? Math.Clamp(stiffness * error * dt * dt / iterations, -magnitude, magnitude)
            : (error > 0f ? 1f : -1f) * magnitude;

        float wa = kinematicA ? 0f : 1f, wb = kinematicB ? 0f : 1f;
        float weight = wb + wa + 0.0001f;
        if (!kinematicA)
        {
            float inverse = MathF.Abs(weight) > 1e-8f ? 1f / weight : 0f;
            Vector3 move = unit * correction * wa * inverse;
            xa += move;
            da += move;
        }

        if (!kinematicB)
        {
            float inverse = MathF.Abs(weight) > 1e-8f ? 1f / -weight : 0f;
            Vector3 move = unit * correction * wb * inverse;
            xb += move;
            db += move;
        }
    }

    /// <summary>
    /// The fold about the edge <paramref name="a"/>-<paramref name="b"/> between the wings <paramref name="c"/> and <paramref name="d"/>
    /// (module+0x139C9E0): nought where the two triangles lie flat, the angle between their normals - folds past a right angle held at one -
    /// signed by the way it folds, less <paramref name="rest"/>, and nothing within <paramref name="allowed"/> of it.
    /// </summary>
    public static float Fold(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float allowed, float rest)
    {
        Vector3 edge = b - a;
        Vector3 one = Unit(Vector3.Cross(edge, c - a));
        Vector3 two = Unit(Vector3.Cross(d - a, edge));
        float cosine = Math.Clamp(MathF.Max(Vector3.Dot(one, two), 0f), -1f, 1f);
        float angle = MathF.Acos(cosine);
        if (Vector3.Dot(Vector3.Cross(one, two), Unit(edge)) <= 0f)
        {
            angle = -angle;
        }

        float off = angle - rest;
        return MathF.Abs(off) < allowed ? 0f : off - (off > 0f ? allowed : -allowed);
    }

    /// <summary>
    /// A bend constraint's correction (module+0x139CDF0): the four corners moved along the fold's gradient (each corner's own, nought for a
    /// kinematic one) by a Newton step - the fold's change along that way measured 0.001 along it, times a thousand - soft as a distance is
    /// (stiffness · step · dt² / passes, held to the whole step) or whole where the stiffness is negative, then halved up to ten times while
    /// the step would fold past nought or fold further; the move added to each corner's displacement too.
    /// </summary>
    /// <remarks>
    /// THE GRADIENT'S LENGTH IS THE TOOL'S: the engine's way along the four corners is the fold's exact gradient (Bridson et al.'s, MEASURED:
    /// parallel to the engine's corner by corner over 200 random folds) times a factor of its own per fold that is not read; that factor only
    /// sets how far 0.001 along it the engine measures the change, so the tool takes the gradient itself.
    /// </remarks>
    public static void Bend(Span<Vector3> x, Span<Vector3> d, ReadOnlySpan<bool> kinematic, float stiffness, float rest, float allowed, float dt, int passes)
    {
        float before = Fold(x[0], x[1], x[2], x[3], allowed, rest);
        Span<Vector3> way = stackalloc Vector3[4];
        Gradient(x[0], x[1], x[2], x[3], way);
        for (var corner = 0; corner < 4; corner++)
        {
            if (kinematic[corner])
            {
                way[corner] = Vector3.Zero;
            }
        }

        Span<Vector3> trial = stackalloc Vector3[4];
        for (var corner = 0; corner < 4; corner++)
        {
            trial[corner] = (way[corner] * 0.001f) + x[corner];
        }

        float change = (Fold(trial[0], trial[1], trial[2], trial[3], allowed, rest) - before) * 1000f;
        float newton = MathF.Abs(change) > 0.0001f ? before / change : 0f;
        float whole = -newton;
        float step = stiffness >= 0f
            ? Math.Clamp(-1f / passes * dt * dt * (newton * stiffness), -MathF.Abs(whole), MathF.Abs(whole))
            : whole;

        float reached = 0f, stride = 1f;
        for (var halving = 0; halving < 10; halving++)
        {
            float at = reached + stride;
            if (at > 1.001f)
            {
                break;
            }

            float scale = at * step;
            for (var corner = 0; corner < 4; corner++)
            {
                trial[corner] = (scale * way[corner]) + x[corner];
            }

            float after = Fold(trial[0], trial[1], trial[2], trial[3], allowed, rest);
            if (after * before >= -0.0001f && MathF.Abs(after) <= MathF.Abs(before))
            {
                reached = at;
            }
            else
            {
                stride *= 0.5f;
            }
        }

        float taken = step * reached;
        for (var corner = 0; corner < 4; corner++)
        {
            if (!kinematic[corner])
            {
                Vector3 move = way[corner] * taken;
                x[corner] += move;
                d[corner] += move;
            }
        }
    }

    /// <summary>
    /// The gradient of <see cref="Fold"/> about a-b with wings c and d: Bridson et al.'s (2003) bending modes, turned the other way - MEASURED
    /// against the engine's own fold's numerical gradient, the same way round on every random fold tried.
    /// </summary>
    private static void Gradient(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Span<Vector3> into)
    {
        Vector3 edge = b - a;
        float length = edge.Length();
        Vector3 n1 = Vector3.Cross(c - a, c - b), n2 = Vector3.Cross(d - b, d - a);
        float s1 = n1.LengthSquared(), s2 = n2.LengthSquared();
        if (length < 1e-12f || s1 < 1e-24f || s2 < 1e-24f)
        {
            into.Clear();
            return;
        }

        Vector3 u1 = n1 / s1, u2 = n2 / s2;
        into[2] = -length * u1;
        into[3] = -length * u2;
        into[0] = -((Vector3.Dot(c - b, edge) / length * u1) + (Vector3.Dot(d - b, edge) / length * u2));
        into[1] = (Vector3.Dot(c - a, edge) / length * u1) + (Vector3.Dot(d - a, edge) / length * u2);
    }

    /// <summary>
    /// A contact's correction (module+0x139E520): where the particle lies behind the surface, half the depth out along the normal, then its
    /// motion across the normal relative to the surface's own taken away, held to a quarter of the depth - the move added to its
    /// displacement too.
    /// </summary>
    public static void Contact(ref Vector3 x, ref Vector3 d, Vector3 point, Vector3 normal, Vector3 motion)
    {
        float depth = Vector3.Dot(point - x, normal);
        if (depth <= 0f)
        {
            return;
        }

        Vector3 push = normal * depth;
        Vector3 half = push * 0.5f;
        x += half;
        d += half;
        float cone = push.LengthSquared() * 0.25f;
        Vector3 relative = d - motion;
        Vector3 across = (normal * Vector3.Dot(normal, relative)) - relative;
        float acrossSquared = across.LengthSquared();
        if (acrossSquared > cone * 0.25f)
        {
            across *= MathF.Sqrt(cone) * 0.5f / (MathF.Sqrt(acrossSquared) + 1e-5f);
        }

        x += across;
        d += across;
    }

    /// <summary>
    /// The nearest point of a capsule between two spheres of their own radii to <paramref name="p"/> and the surface's normal there
    /// (module+0x13A5E90): the round cone the two spheres sweep - MEASURED against the engine's to 1e-5 - or the larger sphere where it
    /// holds the other.
    /// </summary>
    public static (Vector3 Point, Vector3 Normal) RoundCone(Vector3 a, float ra, Vector3 b, float rb, Vector3 p)
    {
        Vector3 axis = b - a;
        float length = axis.Length();
        float slope = length > 1e-8f ? (rb - ra) / length : 0f;
        float t;
        if (MathF.Abs(slope) >= 1f || length <= 1e-8f)
        {
            t = rb > ra ? 1f : 0f;
        }
        else
        {
            Vector3 unit = axis / length;
            Vector3 from = p - a;
            float along = Vector3.Dot(from, unit);
            float off = (from - (unit * along)).Length();
            float z = along + (slope * off / MathF.Sqrt(1f - (slope * slope)));
            t = Math.Clamp(z / length, 0f, 1f);
        }

        Vector3 centre = a + (axis * t);
        float radius = ra + ((rb - ra) * t);
        Vector3 normal = Unit(p - centre);
        return (centre + (normal * radius), normal);
    }

    /// <summary>
    /// The engine's nearest point of an ellipsoid of <paramref name="radii"/> about its own frame to <paramref name="p"/> in that frame, and
    /// its normal (module+0x13A65E0): five times along the gradient from <paramref name="p"/> to where the line meets the surface, the
    /// gradient taken anew where it met; where a line misses it, the point a hundred thousand units inside along the way it had - no contact.
    /// </summary>
    public static (Vector3 Point, Vector3 Normal) Ellipsoid(Vector3 radii, Vector3 p)
    {
        Vector3 inverse = new(1f / radii.X, 1f / radii.Y, 1f / radii.Z);
        Vector3 squared = inverse * inverse;
        Vector3 way = 2f * squared * p;
        Vector3 at = p;
        for (var round = 0; round < 5; round++)
        {
            Vector3 scaledWay = way * inverse, scaledP = p * inverse;
            float qa = scaledWay.LengthSquared();
            float qb = 2f * Vector3.Dot(scaledWay, scaledP);
            float qc = scaledP.LengthSquared() - 1f;
            float discriminant = (qb * qb) - (qa * qc * 4f);
            if (discriminant <= 0f)
            {
                Vector3 missed = Unit(way);
                return (at - (missed * 100000f), missed);
            }

            float s = (MathF.Sqrt(discriminant) - qb) * (0.5f / qa);
            at = p + (way * s);
            way = 2f * squared * at;
        }

        return (at, Unit(way));
    }

    /// <summary>
    /// A particle's frame (module+0x13AC9F0): its normal the sum of its triangles' normals weighted by their angle at it, its tangent the
    /// way to the next corner of its first triangle across that normal; rows normal × tangent, normal, tangent, its place. A particle of no
    /// triangle has no turn - three rows of nought.
    /// </summary>
    public static Matrix4x4 VertexFrame(ReadOnlySpan<Vector3> x, ClothRig rig, int particle)
    {
        ArgumentNullException.ThrowIfNull(rig);
        int first = rig.IncidentStart[particle], count = rig.IncidentCount[particle];
        Vector3 at = x[particle];
        if (count == 0)
        {
            return Rows(Vector3.Zero, Vector3.Zero, Vector3.Zero, at);
        }

        Vector3 sum = Vector3.Zero;
        Vector3 next = Vector3.Zero;
        for (var one = 0; one < count; one++)
        {
            ClothTriangle triangle = rig.Triangles[rig.Incident[first + one]];
            int slot = triangle.SlotOf(particle);
            Vector3 p1 = x[triangle[(slot + 1) % 3]], p2 = x[triangle[(slot + 2) % 3]];
            Vector3 normal = Unit(Vector3.Cross(x[triangle.B] - x[triangle.A], x[triangle.C] - x[triangle.A]));
            float cosine = Math.Clamp(Vector3.Dot(Unit(p1 - at), Unit(p2 - at)), -1f, 1f);
            sum += normal * MathF.Acos(cosine);
            if (one == 0)
            {
                next = p1 - at;
            }
        }

        Vector3 n = Unit(sum);
        Vector3 tangent = Unit(next - (n * Vector3.Dot(n, next)));
        return Rows(Vector3.Cross(n, tangent), n, tangent, at);
    }

    /// <summary>
    /// A frame on an edge at <paramref name="t"/> from a to b (module+0x13AD2E0): along the edge first, then across it the sum of the
    /// two wings' unit ways crossed with it (one wing where the edge has one), then the third.
    /// </summary>
    public static Matrix4x4 EdgeFrame(Vector3 a, Vector3 b, Vector3 c, Vector3? d, float t)
    {
        Vector3 along = Unit(b - a);
        Vector3 across = Vector3.Cross(Unit(c - a), along);
        if (d is { } other)
        {
            across += Vector3.Cross(along, Unit(other - a));
        }

        Vector3 normal = Unit(across - (along * Vector3.Dot(along, across)));
        return Rows(along, normal, Vector3.Cross(normal, along), a + ((b - a) * t));
    }

    /// <summary>
    /// A triangle's frame (module+0x13ADA70): its two edges from the first corner as rows, its unit normal times the square root of its
    /// area over its rest area, the first corner's place - an affine frame that shears and stretches with the triangle.
    /// </summary>
    public static Matrix4x4 TriangleFrame(Vector3 p0, Vector3 p1, Vector3 p2, float restArea)
    {
        Vector3 e1 = p1 - p0, e2 = p2 - p0;
        Vector3 cross = Vector3.Cross(e1, e2);
        float stretch = MathF.Sqrt(cross.Length() * 0.5f / restArea);
        return Rows(e1, e2, Unit(cross) * stretch, p0);
    }

    /// <summary>
    /// A "skinned" bone's frame by shape matching (module+0x13AA090, its fourth kind): the weighted centroid of its particles, the linear map
    /// that best takes their rest about the rest centroid onto their places about it (Apq · Aqq⁻¹) as its rows' columns; then the bone its
    /// offset through that frame, its third row made the unit cross of its first two, signed as the offset's turn is.
    /// </summary>
    public static Matrix4x4 Skinned(ReadOnlySpan<Vector3> x, ClothBoneLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        float total = 0f;
        Vector3 sum = Vector3.Zero;
        foreach (ClothMember member in link.Members)
        {
            sum += x[member.Particle] * member.Weight;
            total += member.Weight;
        }

        Vector3 centre = sum;
        if (MathF.Abs(total) > 1e-7f)
        {
            centre *= 1f / total;
        }

        float share = 1f / total;
        Matrix4x4 apq = default;
        foreach (ClothMember member in link.Members)
        {
            Vector3 p = x[member.Particle] - centre, q = member.Rest - link.RestCentre;
            float w = share * member.Weight;
            apq.M11 += p.X * q.X * w;
            apq.M12 += p.X * q.Y * w;
            apq.M13 += p.X * q.Z * w;
            apq.M21 += q.X * p.Y * w;
            apq.M22 += q.Y * p.Y * w;
            apq.M23 += q.Z * p.Y * w;
            apq.M31 += q.X * p.Z * w;
            apq.M32 += q.Y * p.Z * w;
            apq.M33 += q.Z * p.Z * w;
        }

        Matrix4x4 m = Multiply3(apq, link.InverseRest);
        var frame = new Matrix4x4(m.M11, m.M21, m.M31, 0f, m.M12, m.M22, m.M32, 0f, m.M13, m.M23, m.M33, 0f, centre.X, centre.Y, centre.Z, 1f);
        Matrix4x4 bone = Matrix4x4.Multiply(link.Offset, frame);
        Vector3 r0 = new(bone.M11, bone.M12, bone.M13), r1 = new(bone.M21, bone.M22, bone.M23);
        Matrix4x4 o = link.Offset;
        float det = (o.M11 * ((o.M22 * o.M33) - (o.M23 * o.M32))) + (o.M12 * ((o.M23 * o.M31) - (o.M21 * o.M33))) + (o.M13 * ((o.M21 * o.M32) - (o.M22 * o.M31)));
        Vector3 r2 = Unit(Vector3.Cross(r0, r1)) * (det > 0f ? 1f : -1f);
        bone.M31 = r2.X;
        bone.M32 = r2.Y;
        bone.M33 = r2.Z;
        return bone;
    }

    /// <summary>The 3x3 part of a times b, row-major (module+0x13AE3C0).</summary>
    public static Matrix4x4 Multiply3(Matrix4x4 a, Matrix4x4 b) => new(
        (a.M11 * b.M11) + (a.M12 * b.M21) + (a.M13 * b.M31), (a.M11 * b.M12) + (a.M12 * b.M22) + (a.M13 * b.M32), (a.M11 * b.M13) + (a.M12 * b.M23) + (a.M13 * b.M33), 0f,
        (a.M21 * b.M11) + (a.M22 * b.M21) + (a.M23 * b.M31), (a.M21 * b.M12) + (a.M22 * b.M22) + (a.M23 * b.M32), (a.M21 * b.M13) + (a.M22 * b.M23) + (a.M23 * b.M33), 0f,
        (a.M31 * b.M11) + (a.M32 * b.M21) + (a.M33 * b.M31), (a.M31 * b.M12) + (a.M32 * b.M22) + (a.M33 * b.M32), (a.M31 * b.M13) + (a.M32 * b.M23) + (a.M33 * b.M33), 0f,
        0f, 0f, 0f, 1f);

    /// <summary>A frame of three rows and a place.</summary>
    public static Matrix4x4 Rows(Vector3 r0, Vector3 r1, Vector3 r2, Vector3 at)
        => new(r0.X, r0.Y, r0.Z, 0f, r1.X, r1.Y, r1.Z, 0f, r2.X, r2.Y, r2.Z, 0f, at.X, at.Y, at.Z, 1f);

    /// <summary>A unit vector its way, nought where it is under 1e-8 long - the engine's normalize throughout.</summary>
    public static Vector3 Unit(Vector3 v)
    {
        float length = MathF.Sqrt(v.LengthSquared());
        return length > 1e-8f ? v * (1f / length) : Vector3.Zero;
    }
}
