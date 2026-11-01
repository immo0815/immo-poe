using System.Text;
using PoEformance.Features;
using PoEformance.Game.Entities;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's particles part (v0.1.404): a staff the monster wears names its lamp's particle effect, whose emitter is read out, whose
/// update material's graph and the engine's particle graph are written whole with what the compile makes of them, and whose nodes are found
/// where the sources define them - a fragment, the extension point an Input node reads, the declarations they include through every
/// include - those sources whole; read back offline as the replay does.
/// </summary>
public sealed class CaptureParticlesTests
{
    private const string Pet =
        "version 5\n0\n{\n    \"gpu_emitters\": [\n        {\n            \"particle_duration_min\": 0.7,\n            \"particle_duration_max\": 1.3,\n"
        + "            \"count_type\": \"Random\",\n            \"is_continuous\": true,\n            \"lock_translation\": true,\n            \"lock_rotation\": \"Disabled\",\n"
        + "            \"group_size\": 1,\n            \"particle_mesh\": \"\",\n            \"update_material\": \"staff/mat/shimmer_U.mat\",\n"
        + "            \"render_passes\": [ { \"render_material\": \"staff/mat/shimmer_R.mat\", \"face_lock\": \"Camera_Fixed\", \"optional\": false } ],\n"
        + "            \"interval\": { \"min_active\": 1.0, \"max_active\": 1.0 },\n            \"emit_burst\": 0.0,\n"
        + "            \"particles_count_min\": 5,\n            \"particles_count_max\": 7\n        }\n    ]\n}\n";

    private const string Update = """
        {"version":3,"nodes":[{"type":"ReadParticleEmitterData","index":0,"stage":"ParticlesEmit"},
                              {"type":"InputParticlesUpdate::Position","index":0,"stage":"ParticlesUpdate"},
                              {"type":"SpinTheLantern","index":0,"stage":"ParticlesUpdate"}],
         "links":[]}
        """;

    private const string Engine = """
        {"version":3,"nodes":[{"type":"ReadParticleEmitterData","index":0,"stage":"ParticlesSetup"}],"links":[]}
        """;

    private const string Particles =
        "DECLARATIONS gpu_particles_emitter_data\n\tinclude gpu_particles_emitter\n{{\n\t#define PARTICLE_EMITTER_FLAG_IS_ALIVE 0x1\n}}\n\n"
        + "FRAGMENT ReadParticleEmitterData\n\tinclude gpu_particles_emitter_data\n\tout float3 position\n{{\n\tposition = float3(0, 0, 0);\n}}\n\n"
        + "EXTENSIONPOINT ParticlesUpdate::Position\n\tgroup ParticlesUpdate\n\ttype float3\n";

    private const string InputOutput = "DECLARATIONS gpu_particles_emitter\n\tuniform object uint gpu_particle_emitter\n{{\n}}\n";

    [Fact]
    public void ALANTERNSParticlesAreGatheredWithEverySourceTheirNodesNeed()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Encoding.UTF8.GetBytes(
            "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t}\n}\n");
        install.Files["staff/StaffHeld.ao"] = Encoding.UTF8.GetBytes(
            "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t}\n"
            + "\tParticleEffects\n\t{\n\t\tcontinuous_effect = \"lamp staff/fx/lamp.pet\"\n\t}\n}\n");
        install.Files["staff/fx/lamp.pet"] = Encoding.UTF8.GetBytes(Pet);
        install.Files["staff/mat/shimmer_U.mat"] = Encoding.UTF8.GetBytes("{\"graphinstances\":[{\"parent\":\"Metadata/Effects/Graphs/GpuParticles/Update/Spin.fxgraph\"}]}");
        install.Files["staff/mat/shimmer_R.mat"] = Encoding.UTF8.GetBytes("{\"graphinstances\":[]}");
        install.Files["Metadata/Effects/Graphs/GpuParticles/Update/Spin.fxgraph"] = Encoding.UTF8.GetBytes(Update);
        install.Files["Metadata/EngineGraphs/GpuParticles/integrate.fxgraph"] = Encoding.UTF8.GetBytes(Engine);
        install.Files["shaders/renderer/nodes/gpuparticles.ffx"] = Encoding.UTF8.GetBytes(Particles);
        install.Files["shaders/renderer/nodes/inputoutput.ffx"] = Encoding.UTF8.GetBytes(InputOutput);

        MonsterModel model = MonsterModels.OfFiles(
            install.Read, ["body.ao"], true, null, [new WornPiece("R_wrist_jntBnd", "staff/StaffHeld.ao") { Held = true }]);
        var pane = new CapturePane("monster", [], model);
        string text = CaptureParticles.Write(
            install.Read,
            [pane],
            ["metadata/enginegraphs/gpuparticles/integrate.fxgraph", "metadata/enginegraphs/debug_albedo.fxgraph"],
            ["shaders/renderer/nodes/gpuparticles.ffx", "shaders/renderer/nodes/inputoutput.ffx"]).ReplaceLineEndings("\n");

        Assert.StartsWith("particles: 1 effect files from 1 panes, 1 emitters; 2 graphs (1 the engine's particle graphs), ", text, StringComparison.Ordinal);

        // THE WALK reached the lamp from the staff, and its materials and graph from the lamp.
        Assert.Contains("  file\tstaff/fx/lamp.pet\t", text, StringComparison.Ordinal);
        Assert.Contains("\tstaff/StaffHeld.ao\ttext", text, StringComparison.Ordinal);
        Assert.Contains("  file\tMetadata/Effects/Graphs/GpuParticles/Update/Spin.fxgraph\t", text, StringComparison.Ordinal);

        // THE EMITTER read out.
        Assert.Contains(
            "  staff/fx/lamp.pet gpu_emitters[0]: pool 5..7 (Random), life 0.7..1.3 s, continuous true, burst 0.0, once -, chance -, per second -, emitter -..- s, interval [min_active 1.0 max_active 1.0], group 1, mesh \"\", update staff/mat/shimmer_U.mat, render [staff/mat/shimmer_R.mat facing Camera_Fixed], locks [lock_translation true]",
            text,
            StringComparison.Ordinal);

        // EVERY NODE WHERE IT IS DEFINED, an Input node by its extension point; the unknown one by none.
        Assert.Contains("  node\tReadParticleEmitterData\t2 uses\t", text, StringComparison.Ordinal);
        Assert.Contains("FRAGMENT ReadParticleEmitterData in shaders/renderer/nodes/gpuparticles.ffx", text, StringComparison.Ordinal);
        Assert.Contains("  node\tInputParticlesUpdate::Position\t1 uses\t", text, StringComparison.Ordinal);
        Assert.Contains("EXTENSIONPOINT ParticlesUpdate::Position in shaders/renderer/nodes/gpuparticles.ffx", text, StringComparison.Ordinal);
        Assert.Contains("  node\tSpinTheLantern\t1 uses\tNOT KNOWN\tdefined by no source", text, StringComparison.Ordinal);

        // THE DECLARATIONS THROUGH EVERY INCLUDE, and both sources whole.
        Assert.Contains("  include\tgpu_particles_emitter_data\tDECLARATIONS in shaders/renderer/nodes/gpuparticles.ffx\tincluded by ReadParticleEmitterData", text, StringComparison.Ordinal);
        Assert.Contains("  include\tgpu_particles_emitter\tDECLARATIONS in shaders/renderer/nodes/inputoutput.ffx\tincluded by gpu_particles_emitter_data", text, StringComparison.Ordinal);
        Assert.Contains("##### shaders/renderer/nodes/gpuparticles.ffx  (defines 3: ", text, StringComparison.Ordinal);
        Assert.Contains("##### shaders/renderer/nodes/inputoutput.ffx  (defines 1: gpu_particles_emitter)", text, StringComparison.Ordinal);
        Assert.Contains("  | \tposition = float3(0, 0, 0);", text, StringComparison.Ordinal);

        // THE GRAPHS AND THE EFFECT WHOLE, read back offline as the replay reads them.
        Assert.Contains("##### staff/fx/lamp.pet  (hop 1, named by staff/StaffHeld.ao)", text, StringComparison.Ordinal);
        string replay = CaptureReplayTests.ParticlesReplay(text.Split('\n'));
        Assert.StartsWith("particles replay: 2 graphs read back, ", replay, StringComparison.Ordinal);
        Assert.Contains("SpinTheLantern", replay, StringComparison.Ordinal);
    }

    /// <summary>With no pane showing a monster and no engine graph, the part says so.</summary>
    [Fact]
    public void WITHNothingToGatherThePartSaysSo()
    {
        string text = CaptureParticles.Write(_ => null, [], [], []).ReplaceLineEndings("\n");
        Assert.StartsWith("particles: 0 effect files from 0 panes, 0 emitters; 0 graphs (0 the engine's particle graphs), ", text, StringComparison.Ordinal);
        Assert.Contains("  (no effect named by any pane's monster or part)", text, StringComparison.Ordinal);
        Assert.Contains("(no shader sources: the install walk found none, or it had not run when the key went down)", text, StringComparison.Ordinal);
    }
}
