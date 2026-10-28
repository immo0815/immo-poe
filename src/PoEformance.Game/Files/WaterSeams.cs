using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// Where a room's water shapes meet one another, and whether their edges carry each other's corners - see <see cref="Of"/>.
/// </summary>
/// <remarks>
/// WRITTEN WHEN THIN BLACK LINES RAN THROUGH PORT'S WATER, straight and cornered, moving with the waves (said from the live client with
/// a picture, v0.1.336). Water drawn lifted moves every vertex by the waves at its own place, so two shapes that meet along an edge stay
/// joined only where each corner of one is a corner of the other. A corner of one lying in the middle of the other's edge - a
/// T-junction - is lifted while the edge beside it is not, and the gap shows what lies beneath: Port's sea floor is the ocean's black.
/// Port's harbour lays 48 WaterPlane3x3 doodads edge to edge, 750 apart, each of 2,842 triangles; whether their edges carry each other's
/// corners is what this counts.
/// </remarks>
/// <param name="Edges">The water shapes' own edges that border nothing of their own shape.</param>
/// <param name="Matched">Of those, the ones whose both ends are another water shape's corners and nothing of it lies between.</param>
/// <param name="Junctions">The ones with another water shape's corner strictly between their ends - a crack once lifted.</param>
/// <param name="Corners">The corners of other shapes lying on such edges, between their ends.</param>
/// <param name="Outer">The ones along no other water shape, touching one at a corner at most - the water's own border.</param>
/// <param name="Samples">Up to eight places where a corner lies on another shape's edge.</param>
public readonly record struct WaterSeams(int Edges, int Matched, int Junctions, int Corners, int Outer, IReadOnlyList<Vector3> Samples)
{
    /// <summary>How close a corner must lie to an edge, in world units, to count as on it.</summary>
    private const float Near = 0.05f;

    /// <summary>The cells the corners are filed in, world units.</summary>
    private const float Cell = 64f;

    /// <summary>The seams of the marked shapes of a mesh, or none where fewer than two are marked.</summary>
    /// <param name="mesh">The room as drawn.</param>
    /// <param name="water">Per shape, whether it is water.</param>
    public static WaterSeams Of(SkinnedMesh mesh, IReadOnlyList<bool> water)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(water);

        // EVERY WATER CORNER, filed by its cell, with the shape it belongs to.
        var corners = new Dictionary<(int, int), List<(Vector3 At, int Shape)>>();
        var seen = new HashSet<(int Vertex, int Shape)>();
        var shapes = 0;
        for (var shape = 0; shape < mesh.Shapes.Count && shape < water.Count; shape++)
        {
            if (!water[shape])
            {
                continue;
            }

            shapes++;
            MeshShape one = mesh.Shapes[shape];
            for (int at = one.From; at < one.From + one.Count && at < mesh.Indices.Length; at++)
            {
                int vertex = mesh.Indices[at];
                if (seen.Add((vertex, shape)))
                {
                    Vector3 place = mesh.Positions[vertex];
                    (int, int) key = Key(place);
                    if (!corners.TryGetValue(key, out List<(Vector3 At, int Shape)>? list))
                    {
                        corners[key] = list = [];
                    }

                    list.Add((place, shape));
                }
            }
        }

        if (shapes < 2)
        {
            return new WaterSeams(0, 0, 0, 0, 0, []);
        }

        int edges = 0, matched = 0, junctions = 0, onEdges = 0, outer = 0;
        var samples = new List<Vector3>();
        for (var shape = 0; shape < mesh.Shapes.Count && shape < water.Count; shape++)
        {
            if (!water[shape])
            {
                continue;
            }

            foreach ((int a, int b) in Border(mesh, mesh.Shapes[shape]))
            {
                edges++;
                Vector3 p = mesh.Positions[a], q = mesh.Positions[b];
                int inside = 0, ends = 0;
                foreach ((Vector3 at, int owner) in Along(corners, p, q))
                {
                    if (owner == shape)
                    {
                        continue;
                    }

                    float t = Placed(p, q, at, out float off);
                    if (off > Near || t < -Near || t > Vector2.Distance(Flat(p), Flat(q)) + Near)
                    {
                        continue;
                    }

                    if (t < Near || t > Vector2.Distance(Flat(p), Flat(q)) - Near)
                    {
                        ends++;
                        continue;
                    }

                    inside++;
                    if (samples.Count < 8)
                    {
                        samples.Add(at);
                    }
                }

                if (inside > 0)
                {
                    junctions++;
                    onEdges += inside;
                }
                else if (ends >= 2)
                {
                    matched++;
                }
                else
                {
                    outer++;
                }
            }
        }

        return new WaterSeams(edges, matched, junctions, onEdges, outer, samples);
    }

    /// <summary>A shape's edges that no other triangle of it shares - its border, by its own corners' indices.</summary>
    private static List<(int A, int B)> Border(SkinnedMesh mesh, MeshShape shape)
    {
        var uses = new Dictionary<(int, int), int>();
        int end = Math.Min(shape.From + shape.Count, mesh.Indices.Length);
        for (int at = shape.From; at + 2 < end; at += 3)
        {
            for (var side = 0; side < 3; side++)
            {
                int a = mesh.Indices[at + side], b = mesh.Indices[at + ((side + 1) % 3)];
                (int, int) key = a < b ? (a, b) : (b, a);
                uses[key] = uses.GetValueOrDefault(key) + 1;
            }
        }

        var border = new List<(int A, int B)>();
        foreach (KeyValuePair<(int, int), int> one in uses)
        {
            if (one.Value == 1)
            {
                border.Add(one.Key);
            }
        }

        return border;
    }

    /// <summary>The corners filed in every cell an edge's box touches.</summary>
    private static IEnumerable<(Vector3 At, int Shape)> Along(Dictionary<(int, int), List<(Vector3 At, int Shape)>> corners, Vector3 p, Vector3 q)
    {
        (int x0, int y0) = Key(Vector3.Min(p, q) - new Vector3(Near));
        (int x1, int y1) = Key(Vector3.Max(p, q) + new Vector3(Near));
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (corners.TryGetValue((x, y), out List<(Vector3 At, int Shape)>? list))
                {
                    foreach ((Vector3 At, int Shape) one in list)
                    {
                        yield return one;
                    }
                }
            }
        }
    }

    /// <summary>How far along an edge, seen from above, a place lies, and how far off it.</summary>
    private static float Placed(Vector3 p, Vector3 q, Vector3 at, out float off)
    {
        Vector2 from = Flat(p), way = Flat(q) - from, to = Flat(at) - from;
        float length = way.Length();
        if (length < 1e-6f)
        {
            off = to.Length();
            return 0f;
        }

        Vector2 along = way / length;
        float t = Vector2.Dot(to, along);
        off = MathF.Abs((to.X * along.Y) - (to.Y * along.X));
        return t;
    }

    private static Vector2 Flat(Vector3 place) => new(place.X, place.Y);

    private static (int, int) Key(Vector3 place) => ((int)MathF.Floor(place.X / Cell), (int)MathF.Floor(place.Y / Cell));
}
