using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The particle shader's building blocks held against hand-written sources and graphs - no engine source is committed (DECIDED from the live
/// client, 2026-11-01): the .ffx reader, the engine's graph composition as read from its code, the linker's ordering rules, the spline bake and
/// the prelude's finding. The real sources are linked offline from a capture (CaptureReplayTests' particles replay).
/// </summary>
public class ParticleLinkerTests
{
    /// <summary>A small source in the engine's grammar: a fragment with a semantic, a node uniform and a body; declarations; extension points; a group.</summary>
    private const string Source = """
        // A comment before anything.
        DECLARATIONS helpers
        	include basics
        	uniform pipeline cs rw_structBuffer<ParticleData> particles_data_write
        	#if defined(GPU_PARTICLE_DRAW_CALL)
        	uniform object uint gpu_particle_emitter
        	#endif
        {{
        	float Twice(float x) { return x * 2.0f; }
        }}

        DECLARATIONS basics
        	#define BASICS_READY 1
        {{
        	static bool g_thread_enabled;
        }}

        FRAGMENT ReadId
        	cost: cheap
        	compute
        	engineonly
        	macro GROUP_SIZE_X:64
        	include helpers
        	in uint3 dispatch_index : SV_DispatchThreadID
        	out uint particle_id : PARTICLE_ID
        {{
        	particle_id = dispatch_index.x;
        }}

        FRAGMENT UsesId
        	cost: free
        	in uint particle_id : PARTICLE_ID
        	out float output
        {{
        	output = float(particle_id);
        }}

        FRAGMENT AddConst
        	cost: cheap
        	in float a
        	uniform float add_const "b" "0" "2" "0.5" : custom_range
        	out float output
        {{
        	output = a + add_const;
        }}

        FRAGMENT Spline5
        	cost: expensive
        	uniform spline5 constant_track5 "value" : custom_range
        	out spline5 output
        {{
        	output = constant_track5;
        }}

        EXTENSIONPOINT ParticlesUpdate::Size
        	group ParticlesUpdate
        	compute
        	defaultvisible
        	defaultstage ParticlesUpdate
        	type float
        	generate: {{}}

        EXTENSIONPOINT ParticlesUpdate::Color
        	group ParticlesUpdate
        	compute
        	type float4
        	generate: {{ value = float4(1.0f, 1.0f, 1.0f, 1.0f); }}

        GROUP ForEach
        	cost: ok
        	unroll: FOUR
        	loop float3 position
        	prologue out uint loop_count : G_LoopCount
        	prologue: {{
        		loop_count = 4;
        	}}
        	epilogue: {{
        	}}
        """;

    [Fact]
    public void THEFFXReaderKeepsEveryPartOfABlock()
    {
        var source = new ShaderSource();
        Assert.Equal(9, source.Add("shaders/test.ffx", Source));

        ShaderBlock read = source.Fragments["ReadId"];
        Assert.Contains("compute", read.Flags);
        Assert.Contains("cost:cheap", read.Flags);
        Assert.Equal(["helpers"], read.Includes);
        Assert.Equal(["GROUP_SIZE_X:64"], read.Macros);
        Assert.Equal(new ShaderPort("in", "uint3", "dispatch_index", "SV_DispatchThreadID", string.Empty), read.Ports[0]);
        Assert.Equal("PARTICLE_ID", read.Ports[1].Semantic);
        Assert.Equal("particle_id = dispatch_index.x;", read.Body.Trim());

        ShaderUniform constant = Assert.Single(source.Fragments["AddConst"].Uniforms);
        Assert.Equal((string.Empty, "float", "add_const", "custom_range"), (constant.Scope, constant.Type, constant.Name, constant.Semantic));
        Assert.Equal(["b", "0", "2", "0.5"], constant.Quoted);

        ShaderBlock helpers = source.Declarations["helpers"];
        Assert.Equal(("pipeline", "cs", "rw_structBuffer<ParticleData>"), (helpers.Uniforms[0].Scope, helpers.Uniforms[0].Stages, helpers.Uniforms[0].Type));
        Assert.Equal("#if defined(GPU_PARTICLE_DRAW_CALL)", helpers.Uniforms[1].Condition);
        Assert.Contains("Twice", helpers.Body, StringComparison.Ordinal);
        Assert.Contains("#define BASICS_READY 1", source.Declarations["basics"].Header);

        ShaderBlock color = source.Extensions["ParticlesUpdate::Color"];
        Assert.Equal(("float4", "ParticlesUpdate"), (color.Value("type"), color.Value("group")));
        Assert.Equal("value = float4(1.0f, 1.0f, 1.0f, 1.0f);", color.Bodies["generate"]);
        Assert.Equal(string.Empty, source.Extensions["ParticlesUpdate::Size"].Bodies["generate"]);

        ShaderBlock group = source.Groups["ForEach"];
        Assert.Equal("FOUR", group.Value("unroll"));
        Assert.Equal("loop_count = 4;", group.Bodies["prologue"].Trim());
        Assert.Contains(group.Ports, port => port.Direction == "prologue out" && port.Semantic == "G_LoopCount");

        Assert.Same(color, source.Node("InputParticlesUpdate::Color"));
        Assert.Same(read, source.Node("ReadId"));
    }

    [Fact]
    public void THELAMPSEmittersTakeTheEnginesGraphsInItsOrder()
    {
        IReadOnlyList<ParticleEmitter> emitters = ParticleEffect.Read("""
            version 5
            0
            { "gpu_emitters": [
              { "count_type": "Random", "group_size": 1, "particle_mesh": "", "update_material": "u0.mat", "emit_once": false,
                "render_passes": [ { "render_material": "r0.mat", "face_lock": "Velocity_Camera", "optional": false } ] },
              { "count_type": "Random", "group_size": 8, "particle_mesh": "ribbon.fmt", "update_material": "u1.mat",
                "render_passes": [ { "render_material": "r1.mat", "face_lock": "None" }, { "render_material": "r2.mat", "face_lock": "None", "optional": true } ] },
              { "count_type": "Random", "group_size": 1, "update_material": "u2.mat", "lock_translation": true, "lock_translation_to_bone": true,
                "render_passes": [ { "render_material": "r3.mat", "face_lock": "Camera_Fixed" } ] },
              { "count_type": "PPS", "group_size": 3, "update_material": "u3.mat", "emit_once": true, "particles_distribution": {}, "is_persistent": true,
                "render_passes": [ { "render_material": "r4.mat", "face_lock": "Z" } ] }
            ] }
            """);
        Assert.Equal(4, emitters.Count);
        const string F = ParticleComposition.Folder;

        ParticleShaders ember = ParticleComposition.Of(emitters[0]);
        Assert.Equal(
            [F + "update_base.fxgraph", F + "update_init_sort.fxgraph", F + "update_dynamic_culling.fxgraph", F + "lifetime_default.fxgraph",
             F + "seed.fxgraph", F + "integrate.fxgraph", F + "spawn.fxgraph", "u0.mat"],
            ember.Update);
        Assert.Equal([F + "sort.fxgraph"], ember.Sort);
        Assert.Equal(
            [ParticleComposition.Base, F + "render_default.fxgraph", F + "render_sort.fxgraph", F + "face_velocity_camera.fxgraph",
             F + "render_flip_normal.fxgraph", "r0.mat"],
            ember.Passes[0]);
        Assert.Empty(ember.Unread);

        ParticleShaders flame = ParticleComposition.Of(emitters[1]);
        Assert.Equal(F + "update_group_8.fxgraph", flame.Update[^2]);
        Assert.Equal(
            [ParticleComposition.Base, F + "render_mesh.fxgraph", F + "render_sort.fxgraph", F + "render_transform.fxgraph", F + "render_group_8.fxgraph", "r2.mat"],
            flame.Passes[1]);
        Assert.Equal(2, flame.Unread.Count);

        Assert.Equal(F + "update_lock_mode2.fxgraph", ParticleComposition.Of(emitters[2]).Update[^2]);
        Assert.Contains(F + "face_camera_fixed.fxgraph", ParticleComposition.Of(emitters[2]).Passes[0]);
        Assert.Contains(F + "render_transform.fxgraph", ParticleComposition.Of(emitters[2]).Passes[0]);

        // PER SECOND: lifetime_pps, no emit_once, the distribution's flag never set; persistent in update, sort and render; group 3 rounds to 4;
        // Z takes render_transform (its graph writes only the particle's rotation).
        ParticleShaders pps = ParticleComposition.Of(emitters[3]);
        Assert.False(emitters[3].Distributed);
        Assert.Contains(F + "lifetime_pps.fxgraph", pps.Update);
        Assert.DoesNotContain(F + "emit_once.fxgraph", pps.Update);
        Assert.Contains(F + "update_persistent.fxgraph", pps.Update);
        Assert.Equal([F + "update_persistent.fxgraph", F + "sort.fxgraph"], pps.Sort);
        Assert.Equal(F + "update_group_4.fxgraph", pps.Update[^2]);
        Assert.Contains(F + "render_persistent.fxgraph", pps.Passes[0]);
        Assert.Contains(F + "render_transform.fxgraph", pps.Passes[0]);
    }

    [Fact]
    public void ANYFACEButThoseWritingTheVertexsPlaceTakesRenderTransformAsTheComposersByteTableSays()
    {
        // THE BYTES AT module+0x1360C88 (the build of 2026-10-27), faces 4 to 37 in the enum's order: one takes render_transform, nought does
        // not; the faces below 4 take it as the switch's default.
        byte[] table = [0, 0, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(38, ParticleComposition.Faces.Count);
        for (var face = 0; face < ParticleComposition.Faces.Count; face++)
        {
            bool takes = face < 4 || table[face - 4] == 1;
            Assert.True(takes == ParticleComposition.TakesTransform(ParticleComposition.Faces[face]), ParticleComposition.Faces[face]);
        }

        // THE NOUGHTS ARE THE FACES WHOSE GRAPH WRITES VertexPosition ITSELF (every face graph of the capture of 2026-11-01 23:05).
        Assert.False(ParticleComposition.TakesTransform("Velocity_Camera"));
        Assert.False(ParticleComposition.TakesTransform("Old_EmitterShape_Camera"));
        Assert.True(ParticleComposition.TakesTransform("Camera"));
        Assert.True(ParticleComposition.TakesTransform("Z_Camera"));
        Assert.True(ParticleComposition.TakesTransform("no face of the enum"));
    }

    [Fact]
    public void THELINKERRunsStagesInOrderAndReadsBeforeAGraphsOwnWrite()
    {
        ShaderSource source = ShaderSource.Of([("shaders/test.ffx", Source)]);

        // Graph 0 writes Size at ParticlesUpdate from AddConst(ReadId -> UsesId). Graph 1 reads Size at ParticlesSetup (earlier: it sees the
        // default) and at ParticlesUpdate (the same stage, a later graph: it sees graph 0's write), and writes Size at ParticlesUpdate from
        // its own read there - which must still see graph 0's value, not its own.
        ShaderGraph first = Graph("""
            { "nodes": [
                { "type": "UsesId", "index": 0 },
                { "type": "AddConst", "index": 0, "parameters": [ { "value": 1.5 } ], "custom_parameter": "Offset" },
                { "type": "ParticlesUpdate::Size", "index": 0, "stage": "ParticlesUpdate" },
                { "type": "ReadId", "index": 0 } ],
              "links": [
                { "src": { "type": "UsesId", "index": 0, "variable": "output" }, "dst": { "type": "AddConst", "index": 0, "variable": "a" } },
                { "src": { "type": "AddConst", "index": 0, "variable": "output" }, "dst": { "type": "ParticlesUpdate::Size", "index": 0, "stage": "ParticlesUpdate", "variable": "input" } } ] }
            """);
        ShaderGraph second = Graph("""
            { "nodes": [
                { "type": "InputParticlesUpdate::Size", "index": 0, "stage": "ParticlesSetup" },
                { "type": "InputParticlesUpdate::Size", "index": 1, "stage": "ParticlesUpdate" },
                { "type": "ParticlesUpdate::Size", "index": 0, "stage": "ParticlesUpdate" } ],
              "links": [
                { "src": { "type": "InputParticlesUpdate::Size", "index": 1, "stage": "ParticlesUpdate", "variable": "output" }, "dst": { "type": "ParticlesUpdate::Size", "index": 0, "stage": "ParticlesUpdate", "variable": "input" } } ] }
            """);
        var custom = new Dictionary<string, ShaderValue[]>(StringComparer.Ordinal) { ["Offset"] = [new ShaderValue(string.Empty, [2.25f], null)] };
        LinkedShader linked = ParticleLinker.Update(source, [new LinkedGraph("a.fxgraph", first, custom), new LinkedGraph("b.fxgraph", second, new Dictionary<string, ShaderValue[]>())], "#define DX11");
        string hlsl = linked.Hlsl;

        int setupRead = hlsl.IndexOf("InputParticlesUpdate::Size @ParticlesSetup", StringComparison.Ordinal);
        int firstWrite = hlsl.IndexOf("ParticlesUpdate::Size @ParticlesUpdate, a.fxgraph", StringComparison.Ordinal);
        int updateRead = hlsl.IndexOf("InputParticlesUpdate::Size @ParticlesUpdate, b.fxgraph", StringComparison.Ordinal);
        int secondWrite = hlsl.IndexOf("ParticlesUpdate::Size @ParticlesUpdate, b.fxgraph", StringComparison.Ordinal);
        Assert.True(setupRead >= 0 && setupRead < firstWrite && firstWrite < updateRead && updateRead < secondWrite, hlsl);

        // THE SEMANTIC'S WRITER RUNS BEFORE ITS READER, the material's parameter over the node's, the declarations before the entry point.
        Assert.True(hlsl.IndexOf("// ReadId #0", StringComparison.Ordinal) < hlsl.IndexOf("// UsesId #0", StringComparison.Ordinal));
        Assert.Contains("float add_const = float(2.25f);", hlsl, StringComparison.Ordinal);
        Assert.Contains("sem_PARTICLE_ID = particle_id;", hlsl, StringComparison.Ordinal);
        Assert.Contains("uint3 dispatch_index = sv_DispatchThreadID;", hlsl, StringComparison.Ordinal);
        Assert.Contains("#define GROUP_SIZE_X 64", hlsl, StringComparison.Ordinal);
        Assert.StartsWith("// The engine's GPU particle update", hlsl, StringComparison.Ordinal);
        // THE MACROS BEFORE THE PRELUDE (CommonDeclarations.inc declares the particle structs under GPU_PARTICLE_DRAW_CALL), the generator's
        // group size and vertex layout among them.
        Assert.True(hlsl.IndexOf("#define COMPUTE_SHADER", StringComparison.Ordinal) < hlsl.IndexOf("#define DX11", StringComparison.Ordinal));
        Assert.Contains("#define GROUP_SIZE (GROUP_SIZE_X*GROUP_SIZE_Y*GROUP_SIZE_Z)", hlsl, StringComparison.Ordinal);
        Assert.Contains("#define VERTEX_OFFSET_NORMAL(F) ((VERTEX_HAS_POSITION(F) ? 12 : 0) + VERTEX_OFFSET_POSITION(F))", hlsl, StringComparison.Ordinal);
        Assert.True(hlsl.IndexOf("DECLARATIONS basics", StringComparison.Ordinal) < hlsl.IndexOf("DECLARATIONS helpers", StringComparison.Ordinal));
        Assert.True(hlsl.IndexOf("float Twice", StringComparison.Ordinal) < hlsl.IndexOf("void UpdateParticles", StringComparison.Ordinal));

        // ONLY THE UNIFORMS THE CODE NAMES: particles_data_write is named nowhere; gpu_particle_emitter neither.
        Assert.DoesNotContain("particles_data_write;", hlsl, StringComparison.Ordinal);
        Assert.Empty(linked.Unresolved);
    }

    [Fact]
    public void ACURVEOfTheGraphEditorIsGivenARowOfTheSplineLut()
    {
        const string curves = """
            FRAGMENT SplineGrayscale
            	cost: expensive
            	uniform splineGrayscale constant_trackG "value"
            	out splineGrayscale output
            {{
            	output = constant_trackG;
            }}

            EXTENSIONPOINT ParticlesUpdate::Track
            	group ParticlesUpdate
            	compute
            	type int
            	generate: {{}}
            """;
        ShaderGraph graph = Graph("""
            { "nodes": [
                { "type": "SplineGrayscale", "index": 0, "parameters": [ { "A": { "points": [ { "time": 0.0, "value": 1.0, "type": "Linear" }, { "time": 1.0, "value": 0.0, "type": "Linear" } ] } } ] },
                { "type": "ParticlesUpdate::Track", "index": 0, "stage": "ParticlesUpdate" } ],
              "links": [
                { "src": { "type": "SplineGrayscale", "index": 0, "variable": "output" }, "dst": { "type": "ParticlesUpdate::Track", "index": 0, "stage": "ParticlesUpdate", "variable": "input" } } ] }
            """);
        LinkedShader linked = ParticleLinker.Update(ShaderSource.Of([("curves.ffx", curves)]), [new LinkedGraph("c.fxgraph", graph, new Dictionary<string, ShaderValue[]>())], "#define DX11");

        CurveKey[] row = Assert.Single(linked.Tracks!);
        Assert.Equal([new CurveKey(0f, 1f, "Linear"), new CurveKey(1f, 0f, "Linear")], row);
        Assert.Contains("int constant_trackG = 0;", linked.Hlsl, StringComparison.Ordinal);
    }

    [Fact]
    public void ASPLINEOfStraightKeysIsBakedAsTheirLines()
    {
        (float[] polynoms, float[] tv) = ParticleLinker.Spline([new CurveKey(0f, 2f, "Linear"), new CurveKey(0.5f, 4f, "Linear"), new CurveKey(1f, 0f, "Linear")], 0.2f);

        // SampleSpline5 as the engine's source writes it: the segment by the breakpoints, then dot(polynom, (t³, t², t, 1)).
        float Sample(float t)
        {
            int i = 3;
            if (t < tv[2]) { i = 2; }
            if (t < tv[1]) { i = 1; }
            if (t < tv[0]) { i = 0; }
            return (polynoms[i * 4] * t * t * t) + (polynoms[(i * 4) + 1] * t * t) + (polynoms[(i * 4) + 2] * t) + polynoms[(i * 4) + 3];
        }

        Assert.Equal(2f, Sample(0f), 5);
        Assert.Equal(3f, Sample(0.25f), 5);
        Assert.Equal(4f, Sample(0.5f), 5);
        Assert.Equal(2f, Sample(0.75f), 5);
        Assert.Equal(0f, Sample(1f), 5);
        Assert.Equal(0.2f, tv[3]);

        (float[] flat, float[] flatTv) = ParticleLinker.Spline([new CurveKey(0f, 0.6f, "Linear"), new CurveKey(1f, 0.6f, "Linear")], 0f);
        Assert.Equal(0.6f, flat[3]);
        Assert.True(flatTv[0] > 1f);
    }

    [Fact]
    public void THEPRELUDEIsTheDx11TextAmongThePrograms()
    {
        byte[] program = [.. Encoding.ASCII.GetBytes("junk\0#define VULKAN\n#define GROUPSHARED groupshared\n\0\u0001\u0002"),
            .. Encoding.ASCII.GetBytes("#define CONCAT_HELPER( x, y ) x##y\n#define DX11\n#define GROUPSHARED groupshared\n#include \"Shaders/CommonDeclarations.inc\"\0tail")];
        string? found = ShaderPrelude.Find(program);
        Assert.NotNull(found);
        Assert.StartsWith("#define CONCAT_HELPER", found, StringComparison.Ordinal);
        Assert.Contains(ShaderPrelude.Marker, found, StringComparison.Ordinal);
        Assert.EndsWith("CommonDeclarations.inc\"", found, StringComparison.Ordinal);
        Assert.Null(ShaderPrelude.Find(Encoding.ASCII.GetBytes("#define DX11\n no groupshared here\0")));
    }

    /// <summary>
    /// A render pass in the engine's grammar: a vertex-only mesh read, base.fxgraph's kind of outputs - a macro alone, a position and instance
    /// written under it, a colour written into a shared struct at its stage - a node of no side and a pixel output, none of which links on.
    /// </summary>
    private const string RenderSource = """
        DECLARATIONS tints
        {{
        	struct TintData { float4 tint; };
        }}

        FRAGMENT MeshPosition
        	cost: free
        	vertex
        	in float3 pos : POSITION
        	out float3 output
        {{
        	output = pos * 2.0f;
        }}

        FRAGMENT OutputVertexPosition
        	cost: free
        	vertex
        	macro VERTEX_OUTPUT_POSITION
        {{
        }}

        FRAGMENT VertexOutputPosition
        	cost: free
        	vertex
        	in float3 position
        #ifdef VERTEX_OUTPUT_POSITION
        	out float4 clip : POSITION
        #endif
        	out uint instance_id : INSTANCEID
        {{
        #ifdef VERTEX_OUTPUT_POSITION
        	clip = float4(position, 1.0f);
        #endif
        	instance_id = 7;
        }}

        FRAGMENT OutputTint
        	cost: free
        	vertex
        	include tints
        	in float4 input
        	inout TintData tintData : tint_data
        {{
        	tintData.tint = input;
        }}

        FRAGMENT Shade
        	cost: free
        	in float3 a
        	in uint instance : INSTANCEID
        	out float4 output
        {{
        	output = float4(a, float(instance));
        }}

        FRAGMENT OutputColor
        	cost: free
        	pixel
        	in float4 color
        	in float4 screen : SV_POSITION
        	uniform sampler colour_sampler
        	out float4 result : PIXEL_RETURN_SEMANTIC_COLOR
        {{
        	result = color * screen.w;
        }}

        EXTENSIONPOINT Tint
        	group Vertex
        	vertex
        	type float4
        	generate: {{ value = float4(1.0f, 1.0f, 1.0f, 1.0f); }}
        """;

    [Fact]
    public void ARENDERPassSplitsIntoTheTwoShadersAndRunsTheOutputsNothingReads()
    {
        ShaderGraph graph = Graph("""
            { "nodes": [
                { "type": "MeshPosition", "index": 0 },
                { "type": "OutputVertexPosition", "index": 0 },
                { "type": "VertexOutputPosition", "index": 0 },
                { "type": "InputTint", "index": 0, "stage": "VertexOutput_Final" },
                { "type": "OutputTint", "index": 0 },
                { "type": "InputTint", "index": 1, "stage": "VertexOutput_Pass" },
                { "type": "Tint", "index": 0, "stage": "VertexOutput_Pass" },
                { "type": "Shade", "index": 0 },
                { "type": "OutputColor", "index": 0 } ],
              "links": [
                { "src": { "type": "MeshPosition", "index": 0, "variable": "output" }, "dst": { "type": "VertexOutputPosition", "index": 0, "variable": "position" } },
                { "src": { "type": "InputTint", "index": 0, "stage": "VertexOutput_Final", "variable": "output" }, "dst": { "type": "OutputTint", "index": 0, "variable": "input" } },
                { "src": { "type": "InputTint", "index": 1, "stage": "VertexOutput_Pass", "variable": "output" }, "dst": { "type": "Tint", "index": 0, "stage": "VertexOutput_Pass", "variable": "input" } },
                { "src": { "type": "MeshPosition", "index": 0, "variable": "output" }, "dst": { "type": "Shade", "index": 0, "variable": "a" } },
                { "src": { "type": "Shade", "index": 0, "variable": "output" }, "dst": { "type": "OutputColor", "index": 0, "variable": "color" } } ] }
            """);
        (LinkedShader vertex, LinkedShader pixel) = ParticleLinker.Render(ShaderSource.Of([("render.ffx", RenderSource)]),
            [new LinkedGraph("r.fxgraph", graph, new Dictionary<string, ShaderValue[]>())], "#define DX11", ["OUTPUT_COLOR"]);
        string vs = vertex.Hlsl, ps = pixel.Hlsl;

        // THE OUTPUTS NOTHING READS RUN: the macro alone defines VERTEX_OUTPUT_POSITION in both shaders, before the prelude, so the position
        // is written; the instance crosses to the pixel shader that reads it.
        foreach (string hlsl in new[] { vs, ps })
        {
            Assert.True(hlsl.IndexOf("#define VERTEX_OUTPUT_POSITION", StringComparison.Ordinal) is >= 0 and var at && at < hlsl.IndexOf("#define DX11", StringComparison.Ordinal), hlsl);
            Assert.Contains("float4 position : SV_Position;", hlsl, StringComparison.Ordinal);
            Assert.Contains("nointerpolation uint instanceid : INSTANCEID;", hlsl, StringComparison.Ordinal);
        }

        Assert.Contains("#define VERTEX_SHADER", vs, StringComparison.Ordinal);
        Assert.Contains("#define PIXEL_SHADER", ps, StringComparison.Ordinal);
        Assert.Contains("Interstage VertexMain(VertexIn vin", vs, StringComparison.Ordinal);
        Assert.Contains("PixelOut PixelMain(Interstage pin", ps, StringComparison.Ordinal);
        Assert.Contains("float3 pos = ((float3)(vin.position));", vs, StringComparison.Ordinal);
        Assert.Contains("vout.position = (float4)(clip);", vs, StringComparison.Ordinal);
        Assert.Contains("vout.instanceid = (uint)(instance_id);", vs, StringComparison.Ordinal);
        Assert.Contains("uint instance = ((uint)(pin.instanceid));", ps, StringComparison.Ordinal);

        // A LEAF RUNS AT THE LATEST STAGE OF WHAT IT READS: after its read at VertexOutput_Final, before the write at VertexOutput_Pass.
        int read = vs.IndexOf("InputTint @VertexOutput_Final", StringComparison.Ordinal);
        int leaf = vs.IndexOf("// OutputTint #0", StringComparison.Ordinal);
        int write = vs.IndexOf("Tint @VertexOutput_Pass, r.fxgraph", StringComparison.Ordinal);
        Assert.True(read >= 0 && read < leaf && leaf < write, vs);

        // EACH NODE IN ITS SHADER: the vertex-only mesh read crosses to the pixel shader's node of no side, interpolated.
        Assert.Matches(@"vout\.(AUTO_S_n0_\d+_output) = \(float3\)\(n0_\d+_output\); // to the pixel shader", vs);
        Assert.Matches(@"float3 a = \(\(\(float3\)pin\.AUTO_S_n0_\d+_output\)\);", ps);
        Assert.DoesNotContain("// Shade #0", vs, StringComparison.Ordinal);
        Assert.DoesNotContain("// OutputColor #0", vs, StringComparison.Ordinal);
        Assert.DoesNotContain("// MeshPosition #0", ps, StringComparison.Ordinal);
        Assert.Contains("float4 screen = ((float4)pin.position);", ps, StringComparison.Ordinal);
        Assert.Contains("float4 target_PIXEL_RETURN_SEMANTIC_COLOR : SV_Target0;", ps, StringComparison.Ordinal);
        Assert.Contains("pout.target_PIXEL_RETURN_SEMANTIC_COLOR = (float4)(result);", ps, StringComparison.Ordinal);
        Assert.Empty(vertex.Unresolved);

        // A NODE'S SAMPLER IS THE ENGINE'S OWN, not a register of its own: SamplerDeclarations.inc takes s0 to s15 already.
        Assert.Contains($"#define colour_sampler {ParticleLinker.NodeSampler}", ps, StringComparison.Ordinal);
        Assert.Equal([$"a node's sampler no graph names: the engine's {ParticleLinker.NodeSampler} (CHOSEN)"], pixel.Unresolved);
        Assert.DoesNotContain("SamplerState res_", ps, StringComparison.Ordinal);
    }

    [Fact]
    public void AMACHINESemanticIsTheShadersOwnInputOrOutputAndASharedOneIsNot()
    {
        Assert.True(ParticleLinker.Machine("TEXCOORD5"));
        Assert.True(ParticleLinker.Machine("SV_InstanceID"));
        Assert.True(ParticleLinker.Machine("PIXEL_RETURN_SEMANTIC_COLOR"));
        Assert.False(ParticleLinker.Machine("semantics_data"));
        Assert.False(ParticleLinker.Machine("PARTICLE_ID"));
        Assert.False(ParticleLinker.Machine("G_InstanceID"));
    }

    private static ShaderGraph Graph(string json) => ShaderGraph.Read(Encoding.UTF8.GetBytes(json));
}
