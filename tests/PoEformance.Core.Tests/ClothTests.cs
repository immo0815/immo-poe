using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using PoEformance.Game.Diagnostics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The cloth's building blocks held against what the engine's code and poe_data_tools' parser say: the .smd tail read in each version's
/// order, the ClientAnimationController keys at the engine's defaults, and a physics object's vectors recognised by their records' sizes.
/// </summary>
public sealed class ClothTests
{
    [Fact]
    public void AVERSIONThreeTailReadsInPoeDataToolsOrder()
    {
        byte[] tail = Tail(3);
        SimulationMesh mesh = SimulationMesh.Read(tail, 0, 4);

        Assert.Equal(string.Empty, mesh.Why);
        Assert.Equal(3, mesh.TailVersion);
        Assert.Equal(2, mesh.Vertices.Count);
        Assert.Equal(new Vector3(1, 2, 3), mesh.Vertices[0].Position);
        Assert.Equal(new Int4(4, 5, 0, 0), mesh.Vertices[0].Bones);
        Assert.Equal(new Vector4(0.75f, 0.25f, 0, 0), mesh.Vertices[0].Weights);
        Assert.Equal([0, 1, 1], mesh.Triangles);
        Assert.Equal([1], mesh.Pinned);
        Assert.Equal("hip", mesh.Spheres.Single().Name);
        Assert.Equal(5f, mesh.Spheres.Single().Radius);
        Assert.Equal((0, 0), mesh.Connections.Single());
        Assert.Equal("chest", mesh.Ellipsoids.Single().Name);
    }

    [Fact]
    public void AVERSIONFourTailPutsTheVerticesFirst()
    {
        SimulationMesh mesh = SimulationMesh.Read(Tail(4), 0, 4);

        Assert.Equal(string.Empty, mesh.Why);
        Assert.Equal(2, mesh.Vertices.Count);
        Assert.Equal(new Vector3(6, 7, 8), mesh.Vertices[1].Position);
        Assert.Equal([1], mesh.Pinned);
        Assert.Equal("hip", mesh.Spheres.Single().Name);
    }

    [Fact]
    public void AFILEWithNoTailHasNoCloth()
    {
        SimulationMesh mesh = SimulationMesh.Read(new byte[8], 8, 4);

        Assert.False(mesh.Simulates);
        Assert.Equal(string.Empty, mesh.Why);
    }

    [Fact]
    public void THEKEYSKeepTheEnginesDefaultsAndViscosityMultiplierIsTheNormalOne()
    {
        ClothSettings robe = ClothSettings.Of(
            new Dictionary<string, string>
            {
                ["animation_force_factor"] = "5",
                ["viscosity_multiplier"] = "0.12",
                ["use_shock_propagation"] = "false",
                ["skirt_weight"] = "3",
            },
            out IReadOnlyList<string> unknown);

        Assert.Equal(5f, robe.AnimationForceFactor);
        Assert.Equal(0.12f, robe.NormalViscosity);
        Assert.Equal(0.02f, robe.TangentViscosity);
        Assert.Equal(980f, robe.Gravity);
        Assert.Equal(1000f, robe.AnimationMaxVelocity);
        Assert.Equal(-1f, robe.LinearStiffness);
        Assert.False(robe.ShockPropagation);
        Assert.True(robe.Collisions);
        Assert.Equal(["skirt_weight"], unknown);
        Assert.Equal([0.12f, 0.02f, 0f, 0.5f, 0f, 5f, 0f, 1f, 1000f, 1f, 980f], robe.ObjectBlock());
    }

    [Fact]
    public void APHYSICSObjectsVectorsHoldTogether()
    {
        byte[] bytes = new byte[ClothLive.ObjectBytes];
        Vector(bytes, 0x00, 0x1000, 3, 0x58);
        Vector(bytes, 0x18, 0x5000, 2, 0x28);
        Vector(bytes, 0x48, 0x6000, 1, 0x1C);
        Vector(bytes, 0x60, 0x7000, 3, 0x10);
        Assert.True(ClothLive.Holds(bytes));

        // A PARTICLE VECTOR OF A LENGTH NO WHOLE NUMBER OF PARTICLES MAKES is no physics object.
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 0x1000 + 0x50);
        Assert.False(ClothLive.Holds(bytes));
    }

    private static void Vector(byte[] bytes, int at, ulong begin, int count, int record)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), begin);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + 8), begin + (ulong)(count * record));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + 16), begin + (ulong)(count * record));
    }

    /// <summary>A tail of two vertices, one triangle, one pinned vertex, a sphere, a capsule and an ellipsoid, in a version's order.</summary>
    private static byte[] Tail(int version)
    {
        var parts = new List<byte[]>();
        void U32(uint value) => parts.Add(BitConverter.GetBytes(value));
        void F32(float value) => parts.Add(BitConverter.GetBytes(value));
        void Name(string text)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(text);
            U32((uint)bytes.Length);
            parts.Add(bytes);
        }

        void Vertices()
        {
            foreach ((Vector3 at, uint bone) in new[] { (new Vector3(1, 2, 3), 4u), (new Vector3(6, 7, 8), 9u) })
            {
                F32(at.X);
                F32(at.Y);
                F32(at.Z);
                U32(bone);
                U32(bone + 1);
                U32(0);
                U32(0);
                F32(0.75f);
                F32(0.25f);
                F32(0);
                F32(0);
            }
        }

        void Triangles()
        {
            U32(0);
            U32(1);
            U32(1);
        }

        void Ellipsoids()
        {
            for (var one = 0; one < 15; one++)
            {
                F32(one);
            }

            U32(7);
            Name("chest");
        }

        void Spheres()
        {
            F32(0);
            F32(0);
            F32(-10);
            F32(5);
            U32(2);
            Name("hip");
        }

        void Capsules()
        {
            U32(0);
            U32(0);
        }

        U32((uint)version);
        U32(1);  // ellipsoids
        U32(1);  // spheres
        U32(1);  // connections
        U32(0);  // t3s
        U32(2);  // vertices
        U32(3);  // triangle indices
        U32(1);  // pinned
        if (version == 3)
        {
            Ellipsoids();
            Spheres();
            Capsules();
            Vertices();
            Triangles();
            U32(1);
        }
        else
        {
            Vertices();
            Triangles();
            Ellipsoids();
            Spheres();
            Capsules();
            U32(1);
        }

        return [.. parts.SelectMany(one => one)];
    }
}
