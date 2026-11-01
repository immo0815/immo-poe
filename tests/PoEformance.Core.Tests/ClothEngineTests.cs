using System.Numerics;
using System.Text.Json;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The cloth's arithmetic held against the game's own: tests/fixtures/cloth-engine holds the outputs of the engine's functions, run under a
/// CPU emulator over the program image of the 2026-11-01 11:23 capture (the build of 2026-10-27) on random inputs - numbers only, never the
/// program's bytes. Each function of ClothMath must give the same.
/// </summary>
public sealed class ClothEngineTests
{
    private static IEnumerable<JsonElement> Cases(string name)
    {
        foreach (string line in File.ReadLines(ComponentSweepTests.Fixture(Path.Combine("cloth-engine", name + ".jsonl"))))
        {
            if (line.Length > 0)
            {
                yield return JsonDocument.Parse(line).RootElement;
            }
        }
    }

    private static float[] F(JsonElement c, string key) => [.. c.GetProperty(key).EnumerateArray().Select(one => one.GetSingle())];

    private static Vector3 V(float[] a, int index) => new(a[index * 3], a[(index * 3) + 1], a[(index * 3) + 2]);

    private static bool[] B(JsonElement c, string key) => [.. c.GetProperty(key).EnumerateArray().Select(one => one.GetBoolean())];

    private static void Near(Vector3 want, Vector3 got, float tolerance, string what)
        => Assert.True(Vector3.Distance(want, got) <= tolerance * MathF.Max(1f, want.Length()), $"{what}: wanted {want}, got {got}");

    /// <summary>THE DISTANCE CONSTRAINT is the engine's to float precision (module+0x139C6E0).</summary>
    [Fact]
    public void THEDISTANCEConstraintIsTheEngines()
    {
        var count = 0;
        foreach (JsonElement c in Cases("distance"))
        {
            float[] x = F(c, "x"), d = F(c, "d"), x2 = F(c, "x2"), d2 = F(c, "d2");
            bool[] kin = B(c, "kin");
            Vector3 xa = V(x, 0), xb = V(x, 1), da = V(d, 0), db = V(d, 1);
            ClothMath.Distance(ref xa, ref da, kin[0], ref xb, ref db, kin[1], c.GetProperty("rest").GetSingle(), c.GetProperty("stiffness").GetSingle(),
                c.GetProperty("contraction").GetSingle(), c.GetProperty("dt").GetSingle(), ClothMath.Iterations);
            Near(V(x2, 0), xa, 1e-5f, $"case {count} a");
            Near(V(x2, 1), xb, 1e-5f, $"case {count} b");
            Near(V(d2, 0), da, 1e-5f, $"case {count} da");
            Near(V(d2, 1), db, 1e-5f, $"case {count} db");
            count++;
        }

        Assert.True(count >= 50);
    }

    /// <summary>
    /// THE FOLD is the engine's (module+0x139C9E0), and the bend constraint moves the corners as the engine's: within 3% of the move in 95 of
    /// a hundred folds and within 30% in every one. The rest is the engine's own length of the gradient it measures the fold's change 0.001
    /// along (ClothMath.Bend), which is not read: where that measure crosses a kink of the fold - its dead zone, its right angle - the two
    /// take a different step (MEASURED on these fixtures: 77 of 80 within 3%, the worst 27% of a move of 0.007).
    /// </summary>
    [Fact]
    public void THEBENDConstraintIsTheEngines()
    {
        int count = 0, close = 0;
        foreach (JsonElement c in Cases("bend"))
        {
            float[] x = F(c, "x"), d = F(c, "d"), x2 = F(c, "x2");
            bool[] kin = B(c, "kin");
            float rest = c.GetProperty("rest").GetSingle(), allowed = c.GetProperty("allowed").GetSingle();
            Vector3[] corners = [V(x, 0), V(x, 1), V(x, 2), V(x, 3)];
            Assert.Equal(c.GetProperty("angle").GetSingle(), ClothMath.Fold(corners[0], corners[1], corners[2], corners[3], allowed, rest), 4);
            Vector3[] moves = [V(d, 0), V(d, 1), V(d, 2), V(d, 3)];
            ClothMath.Bend(corners, moves, kin, c.GetProperty("stiffness").GetSingle(), rest, allowed, c.GetProperty("dt").GetSingle(), ClothMath.BendPasses);
            float engine = 0f, apart = 0f;
            for (var k = 0; k < 4; k++)
            {
                engine = MathF.Max(engine, Vector3.Distance(V(x2, k), V(x, k)));
                apart = MathF.Max(apart, Vector3.Distance(V(x2, k), corners[k]));
            }

            if (apart <= (0.03f * engine) + 2e-4f)
            {
                close++;
            }

            Assert.True(apart <= (0.3f * engine) + 2e-4f, $"case {count}: the engine moved {engine}, the tool {apart} apart from it");
            count++;
        }

        Assert.True(count >= 70);
        Assert.True(close >= count * 0.95, $"{close} of {count} folds within 3% of the engine's move");
    }

    /// <summary>THE CONTACT is the engine's (module+0x139E520).</summary>
    [Fact]
    public void THECONTACTIsTheEngines()
    {
        foreach (JsonElement c in Cases("contact"))
        {
            Vector3 x = V(F(c, "x"), 0), d = V(F(c, "d"), 0);
            ClothMath.Contact(ref x, ref d, V(F(c, "point"), 0), V(F(c, "normal"), 0), V(F(c, "motion"), 0));
            Near(V(F(c, "x2"), 0), x, 1e-5f, "x");
            Near(V(F(c, "d2"), 0), d, 1e-5f, "d");
        }
    }

    /// <summary>THE CAPSULE is the engine's round cone (module+0x13A5E90), and the ellipsoid its five rounds (module+0x13A65E0).</summary>
    [Fact]
    public void THESHAPESAreTheEngines()
    {
        foreach (JsonElement c in Cases("capsule"))
        {
            (Vector3 point, Vector3 normal) = ClothMath.RoundCone(V(F(c, "a"), 0), c.GetProperty("ra").GetSingle(), V(F(c, "b"), 0), c.GetProperty("rb").GetSingle(), V(F(c, "p"), 0));
            Near(V(F(c, "point"), 0), point, 1e-3f, "capsule point");
            Near(V(F(c, "normal"), 0), normal, 1e-3f, "capsule normal");
        }

        foreach (JsonElement c in Cases("ellipsoid"))
        {
            (Vector3 point, Vector3 normal) = ClothMath.Ellipsoid(V(F(c, "radii"), 0), V(F(c, "p"), 0));
            Vector3 want = V(F(c, "point"), 0);
            if (want.Length() > 1000f)
            {
                Assert.True(point.Length() > 1000f, "a missed line stays missed");
                continue;
            }

            Near(want, point, 1e-4f, "ellipsoid point");
            Near(V(F(c, "normal"), 0), normal, 1e-4f, "ellipsoid normal");
        }
    }

    /// <summary>THE FRAMES are the engine's: a particle's, an edge's and a triangle's (module+0x13AC9F0, 0x13AD2E0, 0x13ADA70).</summary>
    [Fact]
    public void THEFRAMESAreTheEngines()
    {
        foreach (JsonElement c in Cases("frames"))
        {
            float[] x = F(c, "x");
            Vector3[] p = [.. Enumerable.Range(0, x.Length / 3).Select(one => V(x, one))];
            var triangles = c.GetProperty("triangles").EnumerateArray().Select(one => one.EnumerateArray().Take(3).Select(v => (int)v.GetDouble()).ToArray()).ToList();
            float[] areas = [.. c.GetProperty("triangles").EnumerateArray().Select(one => one.EnumerateArray().ElementAt(6).GetSingle())];
            var rig = new ClothRig(p, [.. p.Select(_ => new Int4(-1, -1, -1, -1))], [.. p.Select(_ => Vector4.Zero)], [.. triangles.SelectMany(one => one)], [], [], ClothSettings.Defaults, "test");
            var vertex = c.GetProperty("vertex").EnumerateArray().ToList();
            for (var one = 0; one < p.Length; one++)
            {
                Same(vertex[one], ClothMath.VertexFrame(p, rig, one), $"vertex {one}");
            }

            foreach (JsonElement edge in c.GetProperty("edges").EnumerateArray())
            {
                int[] e = [.. edge.GetProperty("edge").EnumerateArray().Select(v => v.GetInt32())];
                Same(edge.GetProperty("frame"), ClothMath.EdgeFrame(p[e[0]], p[e[1]], p[e[2]], e[3] >= 0 ? p[e[3]] : null, edge.GetProperty("t").GetSingle()), "edge");
            }

            foreach (JsonElement tri in c.GetProperty("tri").EnumerateArray())
            {
                int[] t = triangles[tri.GetProperty("triangle").GetInt32()];
                Same(tri.GetProperty("frame"), ClothMath.TriangleFrame(p[t[0]], p[t[1]], p[t[2]], areas[tri.GetProperty("triangle").GetInt32()]), "triangle");
            }
        }
    }

    /// <summary>THE SKINNED BONE's shape matching is the engine's (module+0x13AA090, its fourth kind).</summary>
    [Fact]
    public void THESKINNEDBoneIsTheEngines()
    {
        foreach (JsonElement c in Cases("skinned"))
        {
            float[] x = F(c, "x"), rest = F(c, "rest"), w = F(c, "weights"), inv = F(c, "inverse"), centre = F(c, "centroid"), off = F(c, "offset");
            Vector3[] p = [.. Enumerable.Range(0, x.Length / 3).Select(one => V(x, one))];
            var members = Enumerable.Range(0, p.Length).Select(one => new ClothMember(one, w[one], V(rest, one))).ToList();
            var link = new ClothBoneLink(0, "phys_skinned", ClothBoneKind.Skinned, Matrix(off), -1, default, 0f, -1, members,
                new Matrix4x4(inv[0], inv[1], inv[2], 0f, inv[3], inv[4], inv[5], 0f, inv[6], inv[7], inv[8], 0f, 0f, 0f, 0f, 1f), V(centre, 0));
            Same(c.GetProperty("bone"), ClothMath.Skinned(p, link), "skinned bone");
        }
    }

    /// <summary>THE DRAG, THE PULL AND THE ENCLOSURE are the engine's (module+0x13AB700, 0x13ABBC0, 0x13A8440), run through a world's own step pieces.</summary>
    [Fact]
    public void THEFORCESAreTheEngines()
    {
        foreach (JsonElement c in Cases("drag"))
        {
            float[] x = F(c, "x"), d = F(c, "d"), d2 = F(c, "d2");
            bool[] kin = B(c, "kin");
            Vector3[] p = [.. Enumerable.Range(0, x.Length / 3).Select(one => V(x, one))];
            var triangles = c.GetProperty("triangles").EnumerateArray().SelectMany(one => one.EnumerateArray().Select(v => v.GetInt32())).ToList();
            var rig = new ClothRig(p, [.. p.Select(_ => new Int4(-1, -1, -1, -1))], [.. p.Select(_ => Vector4.Zero)], triangles,
                [.. Enumerable.Range(0, p.Length).Where(one => kin[one])], [], ClothSettings.Defaults, "test");
            float dt = c.GetProperty("dt").GetSingle();
            float across = MathF.Exp(-(c.GetProperty("normal").GetSingle() * ClothSettings.ViscosityScale * dt));
            float along = MathF.Exp(-(c.GetProperty("tangent").GetSingle() * ClothSettings.ViscosityScale * dt));
            for (var one = 0; one < p.Length; one++)
            {
                Vector3 n = ClothMath.VertexFrame(p, rig, one) is var frame ? new Vector3(frame.M21, frame.M22, frame.M23) : default;
                Vector3 moved = V(d, one);
                Vector3 drag = kin[one] ? moved : n == Vector3.Zero ? Vector3.Zero : (moved * along) + (n * (Vector3.Dot(n, moved) * (across - along)));
                Near(V(d2, one), drag, 1e-4f, $"drag {one}");
            }
        }
    }

    private static Matrix4x4 Matrix(float[] m) => new(m[0], m[1], m[2], 0f, m[3], m[4], m[5], 0f, m[6], m[7], m[8], 0f, m[9], m[10], m[11], 1f);

    private static void Same(JsonElement want, Matrix4x4 got, string what)
    {
        float[] m = [.. want.EnumerateArray().Select(one => one.GetSingle())];
        Matrix4x4 w = Matrix(m);
        float[] a = [w.M11, w.M12, w.M13, w.M21, w.M22, w.M23, w.M31, w.M32, w.M33, w.M41, w.M42, w.M43];
        float[] b = [got.M11, got.M12, got.M13, got.M21, got.M22, got.M23, got.M31, got.M32, got.M33, got.M41, got.M42, got.M43];
        float scale = MathF.Max(1f, a.Max(MathF.Abs));
        for (var one = 0; one < a.Length; one++)
        {
            Assert.True(MathF.Abs(a[one] - b[one]) <= 2e-4f * scale, $"{what}: element {one} wanted {a[one]}, got {b[one]}");
        }
    }
}
