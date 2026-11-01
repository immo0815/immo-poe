using System.Numerics;
using PoEformance.Game.Files;
using PoEformance.Gpu;

namespace PoEformance.Gpu.Tests;

/// <summary>
/// The engine's GPU particles on the card (ModelGpu.Particles), held against a program written by hand under the names the engine's linked
/// shaders bind by: the update dispatched over the pool writes each particle's place, the render pass draws the mesh an instance a particle
/// through the picture's own clip, and the tool's finishing lays its colour on - in front of the box, not behind it.
/// </summary>
public sealed class ParticleGpuTests
{
    private const string Update = """
        struct Particle { float4 pos; };
        RWStructuredBuffer<Particle> particles_data_write;
        uint instance_count;
        [numthreads(64, 1, 1)]
        void UpdateParticles(uint3 id : SV_DispatchThreadID)
        {
            if (id.x >= instance_count) { return; }
            Particle one;
            one.pos = float4(-0.7f + (1.4f * id.x / max(instance_count - 1, 1)), 2.0f, 0.0f, 1.0f);
            particles_data_write[id.x] = one;
        }
        """;

    private const string Vertex = """
        struct Particle { float4 pos; };
        StructuredBuffer<Particle> particles_data_read;
        row_major float4x4 scene_view_projection_transform;
        struct Interstage { float4 position : SV_Position; };
        struct VertexIn { float3 position : POSITION; float2 texcoord0 : TEXCOORD0; };
        Interstage VertexMain(VertexIn vin, uint sv_InstanceID : SV_InstanceID)
        {
            Interstage vout = (Interstage)0;
            float3 at = particles_data_read[sv_InstanceID].pos.xyz + (float3(vin.position.x, 0.0f, vin.position.y) * 0.08f);
            vout.position = mul(float4(at, 1.0f), scene_view_projection_transform);
            return vout;
        }
        """;

    private const string Pixel = """
        struct Interstage { float4 position : SV_Position; };
        struct PixelOut { float4 target_PIXEL_RETURN_SEMANTIC_COLOR : SV_Target0; };
        PixelOut PixelMain(Interstage pin)
        {
            PixelOut pout = (PixelOut)0;
            pout.target_PIXEL_RETURN_SEMANTIC_COLOR = float4(1.0f, 0.0f, 0.0f, 1.0f);
            return pout;
        }
        """;

    [GpuFact]
    public void THEUPDATEPlacesThePoolAndThePassDrawsAnInstanceAParticleInFrontOfTheBox()
    {
        using ModelGpu? gpu = ModelGpu.Warp(out string why);
        Assert.True(gpu is not null, why);
        var program = new ParticleProgram(
            "test.pet[0]",
            new ParticleShaderText(Update, "UpdateParticles", []),
            [new ParticlePassProgram(new ParticleShaderText(Vertex, "VertexMain", []), new ParticleShaderText(Pixel, "PixelMain", []), ParticleBlend.Alpha, [], "test.mat")],
            ParticleMeshData.Quad,
            8,
            1,
            0.5f,
            0f,
            Vector2.One,
            1,
            1f,
            true,
            0,
            []);
        Vector4[] bones = [new(0f, 0f, 0f, 0f)];
        var frame = new ParticleFrame([new ParticleEmitterNow(program, Matrix4x4.Identity, Matrix4x4.Identity, 0.1f, 0f, bones, bones, true)], 0.1f, 1);
        var scene = new ModelScene(Box(), Particles: frame);
        using ModelTarget target = gpu.Target(ModelGpuTests.Side);

        // ITS SHADERS COMPILE BEHIND THE FRAME: the picture is drawn without them until they land.
        byte[] picture = [];
        for (long until = Environment.TickCount64 + 120_000; Environment.TickCount64 < until;)
        {
            Assert.True(gpu.Draw(target, scene, out why), why);
            if (gpu.ParticlesSaid.StartsWith("particles: 1 passes", StringComparison.Ordinal))
            {
                picture = gpu.Read(target);
                break;
            }

            Thread.Sleep(50);
        }

        Assert.True(picture.Length > 0, gpu.ParticlesSaid);
        int red = 0;
        for (var at = 0; at < picture.Length; at += 4)
        {
            red += picture[at] > 200 && picture[at + 1] < 40 && picture[at + 2] < 40 ? 1 : 0;
        }

        // EIGHT SQUARES IN A ROW across the box's face, each a few pixels across at this size.
        Assert.True(red > 8 * 4, $"{red} red pixels; {gpu.ParticlesSaid}");

        // THE CARD'S POOL READ BACK AS THE CAPTURE WRITES IT (model-panes.txt): each member by the shader's own layout, the update's eight
        // particles from -0.7 to 0.7 along x at a height of 2.
        string state = string.Join("\n", gpu.ParticleState());
        Assert.Contains("pool: 8 particles of 16 bytes", state, StringComparison.Ordinal);
        Assert.Contains("8 not all nought", state, StringComparison.Ordinal);
        Assert.Contains("pos (float4, +0): -0.7..0.7", state, StringComparison.Ordinal);
        Assert.Contains("; 2..2 mean 2", state, StringComparison.Ordinal);
    }

    private static SkinnedMesh Box()
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        Vector3[] faces = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        foreach (Vector3 face in faces)
        {
            Vector3 across = MathF.Abs(face.X) > 0.5f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 down = Vector3.Cross(face, across);
            int at = positions.Count;
            positions.AddRange([face - across - down, face + across - down, face + across + down, face - across + down]);
            normals.AddRange([face, face, face, face]);
            indices.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
        }

        return SkinnedMesh.Of([.. positions], [.. normals], [.. indices], new Vector3(-1f), new Vector3(1f));
    }
}
