using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Gpu.Tests;

/// <summary>
/// A particle update linked from hand-written fragments (no engine source is committed) compiled through FXC as the capture's particles part
/// compiles the lamp's: cs_5_0, every include expanded first.
/// </summary>
public class ParticleCompileTests
{
    private const string Source = """
        DECLARATIONS basics
        {{
        	static bool g_thread_enabled;
        	float Twice(float x) { return x * 2.0f; }
        }}

        FRAGMENT ReadId
        	cost: cheap
        	compute
        	macro GROUP_SIZE_X:64
        	include basics
        	in uint3 dispatch_index : SV_DispatchThreadID
        	out uint particle_id : PARTICLE_ID
        {{
        	g_thread_enabled = true;
        	particle_id = dispatch_index.x;
        }}

        FRAGMENT Halved
        	cost: free
        	include basics
        	in uint particle_id : PARTICLE_ID
        	out float output
        {{
        	output = Twice(float(particle_id)) * 0.25f;
        }}

        EXTENSIONPOINT ParticlesUpdate::Size
        	group ParticlesUpdate
        	compute
        	type float
        	generate: {{}}
        """;

    [GpuFact]
    public void ALINKEDParticleUpdateCompilesThroughFxc()
    {
        ShaderGraph graph = ShaderGraph.Read(Encoding.UTF8.GetBytes("""
            { "nodes": [ { "type": "Halved", "index": 0 }, { "type": "ReadId", "index": 0 }, { "type": "ParticlesUpdate::Size", "index": 0, "stage": "ParticlesUpdate" } ],
              "links": [ { "src": { "type": "Halved", "index": 0, "variable": "output" }, "dst": { "type": "ParticlesUpdate::Size", "index": 0, "stage": "ParticlesUpdate", "variable": "input" } } ] }
            """));
        LinkedShader linked = ParticleLinker.Update(
            ShaderSource.Of([("test.ffx", Source)]),
            [new LinkedGraph("t.fxgraph", graph, new Dictionary<string, ShaderValue[]>())],
            "#define DX11\n#include \"Shaders/Test.inc\"");
        string expanded = ParticleLinker.Expanded(linked.Hlsl, path => path == "Shaders/Test.inc" ? "#define TEST_INC 1" : null, out IReadOnlyList<string> missing);

        Assert.Empty(missing);
        Assert.Contains("#define TEST_INC 1", expanded, StringComparison.Ordinal);
        string said = ParticleCompile.Fxc(expanded, linked.Entry);
        Assert.StartsWith("compiled (cs_5_0)", said, StringComparison.Ordinal);
    }
}
