namespace PoEformance.Game.Files;

/// <summary>
/// Which of the engine's own graphs (<c>Metadata/EngineGraphs/</c>) a GPU emitter's shaders are built from, put before its update and render
/// materials - READ FROM THE ENGINE'S CODE.
/// </summary>
/// <remarks>
/// WHERE IT WAS READ (v0.1.429, DECIDED from the live client to measure before building, 2026-11-01): the emitter's shader composer,
/// module+0x135D340..0x1360D71 of the build of 2026-10-27 (program-code.bin of the capture 2026-11-01 16-13-49, Cemetery of the Eternals),
/// pushes each graph's handle onto a list per shader in the order below, and hands the list to the graph compiler (module+0xBD1820). The
/// fields it tests are the .pet's keys - schema <c>GpuParticleEmitterDef</c>: the reader (module+0x28E5000..0x28EC000) stores every key
/// 0x3B0 below its field, so lock_to_screen's local +0x83E is the composer's +0x48E (update_lock_screen2), emit_once's +0x84A its +0x49A
/// (emit_once.fxgraph), group_size's +0x744 its +0x394.
///
/// THE UPDATE SHADER: update_base; update_init_sort where the emitter sorts; update_persistent where is_persistent; update_dynamic_culling;
/// one lifetime - lifetime_pps for count_type PPS or EPS (the enum Fixed 0, Random 1, PPS 2, Expression 3, EPS 4, module+0x35B4E70),
/// lifetime_distributed where particles_distribution is written, else lifetime_default; seed; emit_once where emit_once and the count is
/// not per second; integrate; update_vertex_group (and update_mesh_set under it) where the mesh's vertex groups are used; spawn; one lock -
/// update_lock_screen2 for lock_to_screen, else update_lock_mode2 for any translation, movement or along lock (or a rotation lock - the
/// words +0x458 and +0x45C taken for lock_rotation's and lock_rotation_to_bone's by their order, a reading), else update_lock_scale2 for a
/// scale lock (the six words +0x460..+0x474, the lock_scale keys by their order, a reading); update_group_N for a group size rounded up to a power of two from 2 to 64; and the update material LAST.
///
/// THE SORT: a stateful emitter (it has an update material) whose mesh's vertex groups are not used sorts - render_sort in each pass,
/// update_init_sort in the update, and a sort shader of its own (update_persistent where persistent, then sort.fxgraph).
///
/// EACH RENDER PASS: base.fxgraph (Metadata/EngineGraphs/base.fxgraph); render_persistent where persistent; render_default without a
/// mesh, render_vertex_group (and render_mesh_set where mesh_set) with one whose vertex groups are used, else render_mesh - the stateless
/// variants where there is no update material; render_sort where it sorts; face_&lt;face_lock in lower case&gt; unless None (the face enum's
/// 38 names at module+0x35B4F70 index the composer's jump table at module+0x1360BEC one for one); render_transform where the face takes it
/// (<see cref="TakesTransform"/>: one of <see cref="Transformed"/>, or below 4); render_flip_normal without a mesh; render_group_N; the
/// pass's render material LAST.
///
/// THE FACE'S render_transform, READ AGAIN (v0.1.450): the composer switches on the face less 4 over 34 values (module+0x135F1DA: add eax, -4;
/// cmp eax, 0x21; ja the default), the byte table at module+0x1360C88 taking each to case 0 or case 1 of a two-entry jump table at
/// module+0x1360C80. Case 1 and the default - the faces below 4, None, Camera, Camera_Z and Camera_Fixed - push render_transform.fxgraph
/// (module+0x135F1FB, the wide string at module+0x319B650) and fall into case 0 (module+0x135F26C), which pushes render_flip_normal.fxgraph
/// where there is no mesh (module+0x319B1D0). So the table's ones TAKE render_transform: v0.1.429 read it the other way round, and the
/// embers' face_velocity_camera, which writes the vertex's world place itself, was transformed a second time - every quad at its particle
/// plus its scale times its particle again (MEASURED v0.1.449, the capture of 2026-11-02 01:02: 108 of 108 vertices streamed out of view,
/// particle 0's z -184.55 x (1 + its scale 0.5827) = -292.09 against -292.10 streamed). The faces the table holds nought for are exactly
/// the 14 whose graph writes VertexPosition itself (the capture's face graphs, every one held: Camera_Velocity, Camera_Emitter,
/// Camera_Along, Velocity_Camera, Emitter_Camera, EmitterShape_Camera, Along_Camera and the seven Old_ ones naming Camera); every other face
/// writes only the particle's rotation for render_transform to turn the mesh by.
///
/// NOT READ: what a mesh adds to a pass's list between the sort and the face (module+0x13C0490, by a number the mesh or the pass carries) -
/// a pass with a mesh says so in <see cref="ParticleShaders.Unread"/>; which local of the composer is "has a mesh" (+0x2C88, the emitter's
/// mesh by its place in the code, a reading) and "the pass's own mesh" (+0x308, a reading).
/// </remarks>
public static class ParticleComposition
{
    /// <summary>The engine's particle graphs' folder.</summary>
    public const string Folder = "Metadata/EngineGraphs/GpuParticles/";

    /// <summary>The graph every render pass starts with.</summary>
    public const string Base = "Metadata/EngineGraphs/base.fxgraph";

    /// <summary>
    /// The faces from 4 on whose pass takes render_transform.fxgraph: the composer's byte table at module+0x1360C88 holds one for them (faces
    /// below 4 take it as the switch's default - <see cref="TakesTransform"/>).
    /// </summary>
    public static readonly IReadOnlySet<string> Transformed = new HashSet<string>(StringComparer.Ordinal)
    {
        "Velocity", "Velocity_Fixed", "Emitter", "Emitter_Fixed", "EmitterShape", "EmitterShape_Fixed", "Along", "Along_Fixed",
        "X", "X_Fixed", "Y", "Y_Fixed", "Z", "Z_Fixed", "Z_Camera", "Z_Velocity", "Z_Emitter", "Z_Along", "Old_X", "Old_Y",
    };

    /// <summary>Every face_lock name the engine knows, in its enum's order (module+0x35B4F70) - None is nought.</summary>
    public static readonly IReadOnlyList<string> Faces =
    [
        "None", "Camera", "Camera_Z", "Camera_Fixed", "Camera_Velocity", "Camera_Emitter", "Camera_Along", "Velocity", "Velocity_Fixed",
        "Velocity_Camera", "Emitter", "Emitter_Fixed", "Emitter_Camera", "EmitterShape", "EmitterShape_Fixed", "EmitterShape_Camera",
        "Along", "Along_Fixed", "Along_Camera", "X", "X_Fixed", "Y", "Y_Fixed", "Z", "Z_Fixed", "Z_Camera", "Z_Velocity", "Z_Emitter",
        "Z_Along", "Old_X", "Old_Y", "Old_Along_Camera", "Old_Camera_Along", "Old_Camera_Emitter", "Old_Camera_Velocity",
        "Old_Emitter_Camera", "Old_EmitterShape_Camera", "Old_Velocity_Camera",
    ];

    /// <summary>
    /// Whether a pass of this face takes render_transform.fxgraph: a face the composer's switch does not reach (below 4: None, Camera, Camera_Z,
    /// Camera_Fixed - or a name the enum lacks, whose value would be past its end) by its default, one from 4 on where the byte table holds one.
    /// </summary>
    public static bool TakesTransform(string face)
    {
        int at = -1;
        for (var one = 0; one < Faces.Count; one++)
        {
            if (string.Equals(Faces[one], face, StringComparison.Ordinal))
            {
                at = one;
                break;
            }
        }

        return at < 4 || Transformed.Contains(face);
    }

    /// <summary>The graphs an emitter's shaders are built from, the materials last.</summary>
    /// <param name="emitter">The emitter as its .pet writes it.</param>
    /// <param name="meshUsesVertexGroups">
    /// Whether the emitter's mesh has vertex groups the engine uses - the composer's own test of the mesh (module+0x135D7CB: vertex_groups or
    /// mesh_set, and the mesh's counts). False without a mesh.
    /// </param>
    public static ParticleShaders Of(ParticleEmitter emitter, bool meshUsesVertexGroups = false)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        bool stateful = emitter.UpdateMaterial.Length > 0;
        bool hasMesh = emitter.Mesh.Length > 0;
        bool vertexGroups = hasMesh && meshUsesVertexGroups && (emitter.Flag("vertex_groups") || emitter.Flag("mesh_set"));
        bool sorts = stateful && !vertexGroups;
        bool persistent = emitter.Flag("is_persistent");
        bool perSecond = ParticleEffect.IsPerSecond(emitter.CountType);
        string group = Group(emitter.GroupSize);
        var unread = new List<string>();

        var update = new List<string> { Folder + "update_base.fxgraph" };
        if (sorts)
        {
            update.Add(Folder + "update_init_sort.fxgraph");
        }

        if (persistent)
        {
            update.Add(Folder + "update_persistent.fxgraph");
        }

        update.Add(Folder + "update_dynamic_culling.fxgraph");
        update.Add(Folder + (perSecond ? "lifetime_pps.fxgraph" : emitter.Distributed ? "lifetime_distributed.fxgraph" : "lifetime_default.fxgraph"));
        update.Add(Folder + "seed.fxgraph");
        if (emitter.Flag("emit_once") && !perSecond)
        {
            update.Add(Folder + "emit_once.fxgraph");
        }

        update.Add(Folder + "integrate.fxgraph");
        if (vertexGroups)
        {
            update.Add(Folder + "update_vertex_group.fxgraph");
            if (emitter.Flag("mesh_set"))
            {
                update.Add(Folder + "update_mesh_set.fxgraph");
            }
        }

        update.Add(Folder + "spawn.fxgraph");
        if (emitter.Flag("lock_to_screen"))
        {
            update.Add(Folder + "update_lock_screen2.fxgraph");
        }
        else if (emitter.Flag("lock_translation") || emitter.Flag("lock_translation_to_bone") || emitter.Flag("lock_movement")
                 || emitter.Flag("lock_movement_to_bone") || emitter.Flag("lock_along") || emitter.Flag("lock_along_to_bone")
                 || Enabled(emitter.Word("lock_rotation")) || Enabled(emitter.Word("lock_rotation_to_bone")))
        {
            update.Add(Folder + "update_lock_mode2.fxgraph");
        }
        else if (new[] { "lock_scale_x", "lock_scale_y", "lock_scale_z", "lock_scale_x_to_bone", "lock_scale_y_to_bone", "lock_scale_z_to_bone" }
                 .Any(key => Enabled(emitter.Word(key))))
        {
            update.Add(Folder + "update_lock_scale2.fxgraph");
        }

        if (group.Length > 0)
        {
            update.Add(Folder + "update_" + group);
        }

        if (stateful)
        {
            update.Add(emitter.UpdateMaterial);
        }

        var sort = new List<string>();
        if (sorts)
        {
            if (persistent)
            {
                sort.Add(Folder + "update_persistent.fxgraph");
            }

            sort.Add(Folder + "sort.fxgraph");
        }

        var passes = new List<IReadOnlyList<string>>();
        foreach (ParticlePass pass in emitter.Passes)
        {
            var render = new List<string> { Base };
            if (persistent)
            {
                render.Add(Folder + "render_persistent.fxgraph");
            }

            bool noMesh = !hasMesh;
            string body = !stateful
                ? noMesh ? "render_stateless.fxgraph" : vertexGroups ? "render_vertex_group_stateless.fxgraph" : "render_mesh_stateless.fxgraph"
                : noMesh ? "render_default.fxgraph" : vertexGroups ? "render_vertex_group.fxgraph" : "render_mesh.fxgraph";
            render.Add(Folder + body);
            if (vertexGroups && emitter.Flag("mesh_set"))
            {
                render.Add(Folder + "render_mesh_set.fxgraph");
            }

            if (sorts)
            {
                render.Add(Folder + "render_sort.fxgraph");
            }

            if (!noMesh)
            {
                unread.Add($"the graphs a mesh adds to a pass (module+0x13C0490) - {emitter.Mesh} for {pass.Material}");
            }

            string face = pass.FaceLock.Length > 0 ? pass.FaceLock : "None";
            if (!Faces.Contains(face, StringComparer.Ordinal))
            {
                unread.Add($"face_lock \"{face}\" is no name of the engine's enum");
            }
            else if (face != "None")
            {
                render.Add(Folder + "face_" + face.ToLowerInvariant() + ".fxgraph");
            }

            if (TakesTransform(face))
            {
                render.Add(Folder + "render_transform.fxgraph");
            }

            if (noMesh)
            {
                render.Add(Folder + "render_flip_normal.fxgraph");
            }

            if (group.Length > 0)
            {
                render.Add(Folder + "render_" + group);
            }

            render.Add(pass.Material);
            passes.Add(render);
        }

        if (emitter.GroupSize > 64)
        {
            unread.Add($"group_size {emitter.GroupSize}: the engine says \"[PARTICLES] Invalid Group Size\"");
        }

        return new ParticleShaders(update, sort, passes, unread);
    }

    /// <summary><c>group_N.fxgraph</c> for a group size rounded up to a power of two from 2 to 64 (the composer's bit smear and jump table), else empty.</summary>
    public static string Group(int size)
    {
        if (size <= 1 || size > 64)
        {
            return string.Empty;
        }

        uint rounded = System.Numerics.BitOperations.RoundUpToPowerOf2((uint)size);
        return $"group_{rounded}.fxgraph";
    }

    /// <summary>Whether a lock mode word is set - anything but empty and Disabled.</summary>
    private static bool Enabled(string mode) => mode.Length > 0 && mode != "Disabled";
}

/// <summary>The graphs an emitter's shaders are built from, in the engine's order, the materials last.</summary>
/// <param name="Update">The update compute shader's graphs.</param>
/// <param name="Sort">The sort shader's graphs, empty where the emitter does not sort.</param>
/// <param name="Passes">Each render pass's graphs.</param>
/// <param name="Unread">What the composer adds that the code reading did not reach.</param>
public sealed record ParticleShaders(IReadOnlyList<string> Update, IReadOnlyList<string> Sort, IReadOnlyList<IReadOnlyList<string>> Passes, IReadOnlyList<string> Unread);
