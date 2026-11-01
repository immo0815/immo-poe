using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>A triangle of a cloth: its corners and, as the engine keeps them, its three edges' rest lengths and its rest area.</summary>
public readonly record struct ClothTriangle(int A, int B, int C, float AB, float BC, float CA, float Area)
{
    /// <summary>Its corner <paramref name="slot"/>, nought to two.</summary>
    public int this[int slot] => slot switch { 0 => A, 1 => B, _ => C };

    /// <summary>The slot of <paramref name="particle"/>, the first that holds it.</summary>
    public int SlotOf(int particle) => particle == A ? 0 : particle == B ? 1 : 2;

    /// <summary>The rest length of edge <paramref name="slot"/>: from that corner to the next.</summary>
    public float Edge(int slot) => slot switch { 0 => AB, 1 => BC, _ => CA };
}

/// <summary>An edge of a cloth (module+0x13AB010): its two ends, the lower first, and the far corner of each triangle on it, -1 for none.</summary>
public readonly record struct ClothEdge(int A, int B, int C, int D);

/// <summary>A particle a "skinned" bone matches its shape to: its weight on the bone and where it rested.</summary>
public readonly record struct ClothMember(int Particle, float Weight, Vector3 Rest);

/// <summary>How a simulated bone follows its cloth - the four kinds the engine's bone records carry (module+0x13AA090).</summary>
public enum ClothBoneKind
{
    /// <summary>The frame of its nearest particle (module+0x13AC9F0).</summary>
    Particle,

    /// <summary>A frame on an edge it lies along (module+0x13AD2E0).</summary>
    Edge,

    /// <summary>The frame of its nearest triangle, a name with "_tri_" in it (module+0x13ADA70).</summary>
    Triangle,

    /// <summary>Shape matching over the particles weighted to it, a name with "skinned" in it.</summary>
    Skinned,
}

/// <summary>A simulated bone and how it follows its cloth: its frame at the bind and the bone's matrix from that frame.</summary>
/// <param name="Bone">The bone as its world numbers it (the model's rig).</param>
/// <param name="Name">Its name, for the lines.</param>
/// <param name="Kind">How it follows.</param>
/// <param name="Offset">The bone's matrix at the bind through its frame's inverse at the bind - the bone is <c>Offset · frame</c> after.</param>
/// <param name="Particle">The particle of a <see cref="ClothBoneKind.Particle"/> bone.</param>
/// <param name="Along">The edge of an <see cref="ClothBoneKind.Edge"/> bone.</param>
/// <param name="T">Where along the edge, from its first end.</param>
/// <param name="Triangle">The triangle of a <see cref="ClothBoneKind.Triangle"/> bone.</param>
/// <param name="Members">The particles of a <see cref="ClothBoneKind.Skinned"/> bone.</param>
/// <param name="InverseRest">Its shape matching's Aqq⁻¹ - the identity plus the members' weighted rest spread, inverted.</param>
/// <param name="RestCentre">Its members' weighted rest centre.</param>
/// <param name="Bind">The bone's matrix at the bind in the model's space - its vertices rest there, so its skin is Bind⁻¹ · the bone.</param>
public sealed record ClothBoneLink(
    int Bone,
    string Name,
    ClothBoneKind Kind,
    Matrix4x4 Offset,
    int Particle,
    ClothEdge Along,
    float T,
    int Triangle,
    IReadOnlyList<ClothMember> Members,
    Matrix4x4 InverseRest,
    Vector3 RestCentre,
    Matrix4x4 Bind = default);

/// <summary>
/// A cloth as the engine builds it from a mesh's simulation mesh (the ClientAnimationController's setup, module+0x2863C00, and the physics
/// object's, module+0x13A7A60): a particle a vertex, its triangles, its edges in the engine's order with a distance constraint each and a bend
/// constraint on each with two triangles, every particle's depth from the pinned ones and its nearest pinned one, and its simulated bones.
/// </summary>
public sealed class ClothRig
{
    /// <summary>The weight below which a vertex is no member of a "skinned" bone (the setup's 0.001).</summary>
    public const float LeastMemberWeight = 0.001f;

    /// <summary>
    /// A cloth of particles resting at <paramref name="rest"/>, each a target its <paramref name="bones"/> and <paramref name="weights"/>
    /// carry, from <paramref name="triangles"/> (three a triangle), <paramref name="pinned"/> kinematic, and its simulated bones linked.
    /// </summary>
    /// <param name="rest">Each particle's rest in the model's space.</param>
    /// <param name="bones">Each particle's four bones, in the model's rig, -1 for none.</param>
    /// <param name="weights">Their weights.</param>
    /// <param name="triangles">The simulation mesh's triangles.</param>
    /// <param name="pinned">The particles that follow the animation.</param>
    /// <param name="simulated">The simulated bones: their number in the model's rig, name, matrix at the bind in the model's space, and the
    /// particle weights on them by the mesh's own numbering for a "skinned" one (the four of each vertex that name it).</param>
    /// <param name="settings">The ClientAnimationController's simulation.</param>
    /// <param name="source">What it came from, for the lines.</param>
    public ClothRig(
        IReadOnlyList<Vector3> rest,
        IReadOnlyList<Int4> bones,
        IReadOnlyList<Vector4> weights,
        IReadOnlyList<int> triangles,
        IReadOnlyList<int> pinned,
        IReadOnlyList<(int Bone, string Name, Matrix4x4 Bind, IReadOnlyList<(int Particle, float Weight)> Weighted)> simulated,
        ClothSettings settings,
        string source)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(simulated);
        Count = rest.Count;
        Rest = [.. rest];
        Bones = [.. bones];
        Weights = [.. weights];
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Source = source ?? string.Empty;
        Pinned = new bool[Count];
        foreach (int one in pinned)
        {
            if (one >= 0 && one < Count)
            {
                Pinned[one] = true;
            }
        }

        Triangles = Triangulated(Rest, triangles);
        (IncidentStart, IncidentCount, Incident) = Incidence(Count, Triangles);
        Edges = Edged(Count, Triangles, IncidentStart, IncidentCount, Incident);
        Bends = [.. Edges.Where(edge => edge.C >= 0 && edge.D >= 0)];
        RestFolds = [.. Bends.Select(edge => ClothMath.Fold(Rest[edge.A], Rest[edge.B], Rest[edge.C], Rest[edge.D], 0f, 0f))];
        (Depth, Parent, Shocks) = Finalised(Rest, Pinned, Triangles, IncidentStart, IncidentCount, Incident);
        var links = new List<ClothBoneLink>();
        var said = new List<string>();
        foreach ((int bone, string name, Matrix4x4 bind, IReadOnlyList<(int Particle, float Weight)> weighted) in simulated)
        {
            if (Linked(bone, name, bind, weighted) is { } link)
            {
                links.Add(link);
            }
            else
            {
                said.Add(name);
            }
        }

        Links = links;
        Unlinked = said;
    }

    private ClothRig(
        int count, ClothTriangle[] triangles, ClothEdge[] edges, ClothEdge[] bends, float[] restFolds, bool[] pinned, int[] depth, int[] parent,
        (int Parent, int Child)[] shocks, ClothSettings settings, string source)
    {
        Count = count;
        Rest = new Vector3[count];
        Bones = new Int4[count];
        Array.Fill(Bones, new Int4(-1, -1, -1, -1));
        Weights = new Vector4[count];
        Pinned = pinned;
        Triangles = triangles;
        (IncidentStart, IncidentCount, Incident) = Incidence(count, triangles);
        Edges = edges;
        Bends = bends;
        RestFolds = restFolds;
        Depth = depth;
        Parent = parent;
        Shocks = shocks;
        Links = [];
        Unlinked = [];
        Settings = settings;
        Source = source;
    }

    /// <summary>
    /// A cloth as the game's own physics object holds it (ClothWatch): its triangles with their rest lengths and areas, the distance
    /// constraints in the order the world solves them, the bends with their rest folds, the pinned particles, every particle's depth and
    /// parent and the shock links - no rest of its own and no bone linked: it is stepped on targets handed to it (ClothWorld.Seed, Retarget).
    /// </summary>
    /// <param name="count">How many particles.</param>
    /// <param name="triangles">Its triangles.</param>
    /// <param name="edges">Its distance constraints' ends, in the solver's order (their wings unread: -1).</param>
    /// <param name="bends">Its bend constraints' four particles.</param>
    /// <param name="restFolds">Each bend's rest fold.</param>
    /// <param name="pinned">Which particles follow the animation.</param>
    /// <param name="depth">Each particle's depth from the pinned ones.</param>
    /// <param name="parent">Each particle's parent.</param>
    /// <param name="shocks">The shock links, parent and child.</param>
    /// <param name="settings">The simulation.</param>
    /// <param name="source">What it came from, for the lines.</param>
    public static ClothRig Recorded(
        int count, IReadOnlyList<ClothTriangle> triangles, IReadOnlyList<ClothEdge> edges, IReadOnlyList<ClothEdge> bends, IReadOnlyList<float> restFolds,
        IReadOnlyList<bool> pinned, IReadOnlyList<int> depth, IReadOnlyList<int> parent, IReadOnlyList<(int Parent, int Child)> shocks,
        ClothSettings settings, string source)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(bends);
        ArgumentNullException.ThrowIfNull(restFolds);
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(shocks);
        ArgumentNullException.ThrowIfNull(settings);
        if (count < 0 || pinned.Count != count || depth.Count != count || parent.Count != count || restFolds.Count != bends.Count)
        {
            throw new ArgumentException("a recorded cloth's particles, depths, parents and folds must agree in number");
        }

        static bool Inside(int particle, int count) => (uint)particle < (uint)count;
        if (triangles.Any(t => !Inside(t.A, count) || !Inside(t.B, count) || !Inside(t.C, count))
            || edges.Any(e => !Inside(e.A, count) || !Inside(e.B, count))
            || bends.Any(e => !Inside(e.A, count) || !Inside(e.B, count) || !Inside(e.C, count) || !Inside(e.D, count))
            || shocks.Any(s => !Inside(s.Parent, count) || !Inside(s.Child, count)))
        {
            throw new ArgumentException("a recorded cloth names a particle it does not have");
        }

        return new ClothRig(
            count, [.. triangles], [.. edges], [.. bends], [.. restFolds], [.. pinned], [.. depth], [.. parent], [.. shocks], settings, source ?? string.Empty);
    }

    /// <summary>How many particles.</summary>
    public int Count { get; }

    /// <summary>Each particle's rest in the model's space.</summary>
    public Vector3[] Rest { get; }

    /// <summary>Each particle's four bones in the model's rig.</summary>
    public Int4[] Bones { get; }

    /// <summary>Their weights.</summary>
    public Vector4[] Weights { get; }

    /// <summary>Which particles follow the animation (kinematic).</summary>
    public bool[] Pinned { get; }

    /// <summary>The triangles, in the file's order.</summary>
    public ClothTriangle[] Triangles { get; }

    /// <summary>Where each particle's triangles start in <see cref="Incident"/>.</summary>
    public int[] IncidentStart { get; }

    /// <summary>How many triangles each particle is a corner of.</summary>
    public int[] IncidentCount { get; }

    /// <summary>Each particle's triangles, in the file's order, one particle after another.</summary>
    public int[] Incident { get; }

    /// <summary>The edges in the engine's order - a distance constraint each, solved in this order.</summary>
    public ClothEdge[] Edges { get; }

    /// <summary>The edges with a triangle on either side - a bend constraint each.</summary>
    public ClothEdge[] Bends { get; }

    /// <summary>Each bend's fold at rest.</summary>
    public float[] RestFolds { get; }

    /// <summary>Each particle's hops from a pinned one over the triangles' edges, -1 where none reaches it.</summary>
    public int[] Depth { get; }

    /// <summary>Each particle's nearest pinned particle at rest, -1 where none is pinned.</summary>
    public int[] Parent { get; }

    /// <summary>The links the breadth-first walk from the pinned ones crossed: parent, child - the shock propagation's.</summary>
    public (int Parent, int Child)[] Shocks { get; }

    /// <summary>The simulated bones and how each follows.</summary>
    public IReadOnlyList<ClothBoneLink> Links { get; }

    /// <summary>The simulated bones no frame could be made for, by name.</summary>
    public IReadOnlyList<string> Unlinked { get; }

    /// <summary>The simulation.</summary>
    public ClothSettings Settings { get; }

    /// <summary>What it came from.</summary>
    public string Source { get; }

    private static ClothTriangle[] Triangulated(Vector3[] rest, IReadOnlyList<int> corners)
    {
        var triangles = new List<ClothTriangle>(corners.Count / 3);
        for (var at = 0; at + 2 < corners.Count; at += 3)
        {
            int a = corners[at], b = corners[at + 1], c = corners[at + 2];
            if ((uint)a >= (uint)rest.Length || (uint)b >= (uint)rest.Length || (uint)c >= (uint)rest.Length)
            {
                continue;
            }

            Vector3 p0 = rest[a], p1 = rest[b], p2 = rest[c];
            float area = Vector3.Cross(p2 - p0, p1 - p0).Length() * 0.5f;
            triangles.Add(new ClothTriangle(a, b, c, Vector3.Distance(p0, p1), Vector3.Distance(p1, p2), Vector3.Distance(p2, p0), area));
        }

        return [.. triangles];
    }

    /// <summary>Each particle's triangles, as the engine lists them (module+0x13AAB10): in the triangles' order.</summary>
    private static (int[] Start, int[] Count, int[] Incident) Incidence(int particles, ClothTriangle[] triangles)
    {
        var count = new int[particles];
        foreach (ClothTriangle triangle in triangles)
        {
            count[triangle.A]++;
            count[triangle.B]++;
            count[triangle.C]++;
        }

        var start = new int[particles];
        for (int one = 1; one < particles; one++)
        {
            start[one] = start[one - 1] + count[one - 1];
        }

        var incident = new int[triangles.Length * 3];
        var filled = new int[particles];
        for (var index = 0; index < triangles.Length; index++)
        {
            for (var slot = 0; slot < 3; slot++)
            {
                int particle = triangles[index][slot];
                incident[start[particle] + filled[particle]++] = index;
            }
        }

        return (start, count, incident);
    }

    /// <summary>
    /// The edges in the engine's order (module+0x13AB010): particle by particle, its triangles in order, their corners in order, each edge
    /// the first time it is met - the far corners of the first two of the particle's triangles holding both ends.
    /// </summary>
    private static ClothEdge[] Edged(int particles, ClothTriangle[] triangles, int[] start, int[] count, int[] incident)
    {
        var met = new HashSet<long>();
        var edges = new List<ClothEdge>();
        for (var particle = 0; particle < particles; particle++)
        {
            for (var one = 0; one < count[particle]; one++)
            {
                ClothTriangle triangle = triangles[incident[start[particle] + one]];
                for (var slot = 0; slot < 3; slot++)
                {
                    int other = triangle[slot];
                    if (other == particle || !met.Add(((long)Math.Min(particle, other) << 32) | (uint)Math.Max(particle, other)))
                    {
                        continue;
                    }

                    Span<int> far = [-1, -1];
                    int found = 0;
                    for (var near = 0; near < count[particle] && found < 2; near++)
                    {
                        ClothTriangle round = triangles[incident[start[particle] + near]];
                        for (var corner = 0; corner < 3; corner++)
                        {
                            if (round[corner] != other)
                            {
                                continue;
                            }

                            int next = round[(corner + 1) % 3];
                            far[found++] = next == particle ? round[(corner + 2) % 3] : next;
                            break;
                        }
                    }

                    if (found is 1 or 2)
                    {
                        edges.Add(new ClothEdge(particle, other, far[0], far[1]));
                    }
                }
            }
        }

        return [.. edges];
    }

    /// <summary>
    /// Depth, parent and the shock links as the engine finalises them (module+0x13ABFB0): each particle's parent its nearest pinned one at
    /// rest, the depth the hops of a breadth-first walk from the pinned ones over its triangles' edges, the walk's every new step a link.
    /// </summary>
    private static (int[] Depth, int[] Parent, (int, int)[] Shocks) Finalised(
        Vector3[] rest, bool[] pinned, ClothTriangle[] triangles, int[] start, int[] count, int[] incident)
    {
        int n = rest.Length;
        var depth = new int[n];
        var parent = new int[n];
        var seen = new bool[n];
        Array.Fill(depth, -1);
        Array.Fill(parent, -1);
        var queue = new List<int>();
        var roots = new List<int>();
        for (var one = 0; one < n; one++)
        {
            if (pinned[one])
            {
                queue.Add(one);
                seen[one] = true;
                depth[one] = 0;
                roots.Add(one);
            }
        }

        for (var one = 0; one < n; one++)
        {
            foreach (int root in roots)
            {
                if (parent[one] == -1 || Vector3.DistanceSquared(rest[one], rest[parent[one]]) > Vector3.DistanceSquared(rest[one], rest[root]))
                {
                    parent[one] = root;
                }
            }
        }

        var shocks = new List<(int, int)>();
        for (var head = 0; head < queue.Count; head++)
        {
            int from = queue[head];
            for (var one = 0; one < count[from]; one++)
            {
                ClothTriangle triangle = triangles[incident[start[from] + one]];
                int slot = triangle.SlotOf(from);
                foreach (int next in (ReadOnlySpan<int>)[triangle[(slot + 1) % 3], triangle[(slot + 2) % 3]])
                {
                    if (seen[next])
                    {
                        continue;
                    }

                    seen[next] = true;
                    depth[next] = depth[from] + 1;
                    queue.Add(next);
                    shocks.Add((from, next));
                }
            }
        }

        return (depth, parent, [.. shocks]);
    }

    /// <summary>A simulated bone linked to its cloth as the setup links it (module+0x2863C00): by its name's kind, against the rest.</summary>
    private ClothBoneLink? Linked(int bone, string name, Matrix4x4 bind, IReadOnlyList<(int Particle, float Weight)> weighted)
    {
        Vector3 at = bind.Translation;
        if (name.Contains("_tri_", StringComparison.Ordinal))
        {
            if (Triangles.Length == 0)
            {
                return null;
            }

            // THE NEAREST TRIANGLE to the bone (module+0x13A9A10, the first of the least distance).
            int best = 0;
            float nearest = float.MaxValue;
            for (var index = 0; index < Triangles.Length; index++)
            {
                ClothTriangle t = Triangles[index];
                float gap = Vector3.Distance(Closest(Rest[t.A], Rest[t.B], Rest[t.C], at), at);
                if (index == 0 || gap < nearest)
                {
                    best = index;
                    nearest = gap;
                }
            }

            ClothTriangle near = Triangles[best];
            Matrix4x4 frame = ClothMath.TriangleFrame(Rest[near.A], Rest[near.B], Rest[near.C], near.Area);
            return Matrix4x4.Invert(frame, out Matrix4x4 inverse)
                ? new ClothBoneLink(bone, name, ClothBoneKind.Triangle, bind * inverse, -1, default, 0f, best, [], Matrix4x4.Identity, Vector3.Zero, bind)
                : null;
        }

        if (name.Contains("skinned", StringComparison.Ordinal))
        {
            var members = new List<ClothMember>();
            foreach ((int particle, float weight) in weighted)
            {
                if (weight >= LeastMemberWeight && (uint)particle < (uint)Count)
                {
                    members.Add(new ClothMember(particle, weight, Rest[particle]));
                }
            }

            if (members.Count == 0)
            {
                return null;
            }

            float total = 1e-5f;
            Vector3 sum = Vector3.Zero;
            foreach (ClothMember member in members)
            {
                sum += member.Rest * member.Weight;
                total += member.Weight;
            }

            Vector3 centre = sum * (1f / total);
            Matrix4x4 aqq = Matrix4x4.Identity;
            foreach (ClothMember member in members)
            {
                Vector3 q = member.Rest - centre;
                float w = member.Weight * (1f / total);
                aqq.M11 += q.X * q.X * w;
                aqq.M12 += q.Y * q.X * w;
                aqq.M13 += q.Z * q.X * w;
                aqq.M21 += q.Y * q.X * w;
                aqq.M22 += q.Y * q.Y * w;
                aqq.M23 += q.Z * q.Y * w;
                aqq.M31 += q.Z * q.X * w;
                aqq.M32 += q.Z * q.Y * w;
                aqq.M33 += q.Z * q.Z * w;
            }

            return Matrix4x4.Invert(aqq, out Matrix4x4 inverseRest)
                ? new ClothBoneLink(bone, name, ClothBoneKind.Skinned, bind * Matrix4x4.CreateTranslation(-centre), -1, default, 0f, -1, members, inverseRest, centre, bind)
                : null;
        }

        // A PLAIN BONE ON AN EDGE it lies along - within a unit of its line, a tenth to nine tenths along it (module+0x13A8E90) - else on its
        // nearest particle.
        foreach (ClothEdge edge in Edges)
        {
            Vector3 a = Rest[edge.A], b = Rest[edge.B];
            Vector3 way = b - a;
            float squared = way.LengthSquared();
            if (squared < 1f)
            {
                continue;
            }

            Vector3 unit = ClothMath.Unit(way);
            if (Vector3.Cross(unit, at - b).LengthSquared() > 1f)
            {
                continue;
            }

            float t = Vector3.Dot(at - a, way) / squared;
            if (t > 0.9f || t < 0.1f)
            {
                continue;
            }

            Matrix4x4 frame = ClothMath.EdgeFrame(a, b, Rest[edge.C], edge.D >= 0 ? Rest[edge.D] : null, t);
            return new ClothBoneLink(bone, name, ClothBoneKind.Edge, bind * RigidInverse(frame), -1, edge, t, -1, [], Matrix4x4.Identity, Vector3.Zero, bind);
        }

        if (Count == 0)
        {
            return null;
        }

        int closest = 0;
        float least = float.MaxValue;
        for (var particle = 0; particle < Count; particle++)
        {
            float gap = Vector3.Distance(at, Rest[particle]);
            if (particle == 0 || gap < least)
            {
                closest = particle;
                least = gap;
            }
        }

        Matrix4x4 own = ClothMath.VertexFrame(Rest, this, closest);
        return new ClothBoneLink(bone, name, ClothBoneKind.Particle, bind * RigidInverse(own), closest, default, 0f, -1, [], Matrix4x4.Identity, Vector3.Zero, bind);
    }

    /// <summary>The inverse of a frame the engine takes as rigid (module+0x13A48E0): its turn transposed.</summary>
    public static Matrix4x4 RigidInverse(Matrix4x4 frame)
    {
        Matrix4x4 turn = frame with { M41 = 0f, M42 = 0f, M43 = 0f };
        Matrix4x4 back = Matrix4x4.Transpose(turn);
        Vector3 place = -Vector3.TransformNormal(frame.Translation, back);
        return back with { M14 = 0f, M24 = 0f, M34 = 0f, M41 = place.X, M42 = place.Y, M43 = place.Z, M44 = 1f };
    }

    /// <summary>The point of a triangle nearest <paramref name="p"/> (Ericson's, Real-Time Collision Detection 5.1.5).</summary>
    public static Vector3 Closest(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return a;
        }

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return b;
        }

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            return a + (ab * (d1 / (d1 - d3)));
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return c;
        }

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            return a + (ac * (d2 / (d2 - d6)));
        }

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            return b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
        }

        float denominator = 1f / (va + vb + vc);
        return a + (ab * (vb * denominator)) + (ac * (vc * denominator));
    }
}
