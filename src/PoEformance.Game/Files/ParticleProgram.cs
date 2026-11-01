using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>How a particle render pass is laid over the picture - its graphs' <c>overriden_blend_mode</c>.</summary>
public enum ParticleBlend
{
    /// <summary><c>AlphaBlend</c> (ForceAlpha.fxgraph): mixed by its alpha.</summary>
    Alpha,

    /// <summary><c>Additive</c> (ForceAdditive.fxgraph): its light added.</summary>
    Additive,

    /// <summary><c>Shimmer</c> (Shimmer.fxgraph): a distortion of the picture behind it - not drawn yet, said.</summary>
    Shimmer,

    /// <summary>Any mode not read: not drawn, said.</summary>
    Unknown,
}

/// <summary>One linked particle shader with every include expanded: its HLSL, its entry point, and the curves its spline LUT rows hold.</summary>
/// <param name="Hlsl">The whole source - ParticleLinker's, ParticleLinker.Expanded over the install's includes.</param>
/// <param name="Entry">UpdateParticles, VertexMain or PixelMain.</param>
/// <param name="Tracks">The spline LUT's rows, in the order the shader numbers them (LinkedShader.Tracks).</param>
public sealed record ParticleShaderText(string Hlsl, string Entry, IReadOnlyList<CurveKey[]> Tracks);

/// <summary>One render pass of an emitter, linked: its two shaders, how it is laid over the picture, and the textures it names.</summary>
/// <param name="Vertex">The vertex shader.</param>
/// <param name="Pixel">The pixel shader.</param>
/// <param name="Blend">How it is laid over the picture.</param>
/// <param name="Textures">Each texture the pass's graphs name, by the global the linker declared it under (<c>res_…</c>), and its picture - null where it did not read.</param>
/// <param name="Material">The render material, for the account.</param>
public sealed record ParticlePassProgram(
    ParticleShaderText Vertex,
    ParticleShaderText Pixel,
    ParticleBlend Blend,
    IReadOnlyList<(string Name, string Path, Mipmaps? Texture)> Textures,
    string Material);

/// <summary>
/// The mesh every particle of an emitter is drawn as, one instance a particle: the camera-facing quad, or the emitter's particle_mesh.
/// </summary>
/// <param name="Positions">Each vertex's place.</param>
/// <param name="Coordinates">Each vertex's TEXCOORD0.</param>
/// <param name="Normals">Each vertex's NORMAL, w nought.</param>
/// <param name="Tangents">Each vertex's TANGENT, w its handedness.</param>
/// <param name="Indices">Three a triangle.</param>
/// <param name="Source">The .fmt it came from, or what stands in.</param>
public sealed record ParticleMeshData(Vector3[] Positions, Vector2[] Coordinates, Vector4[] Normals, Vector4[] Tangents, uint[] Indices, string Source)
{
    /// <summary>
    /// The quad an emitter with no particle_mesh is drawn as - CHOSEN: corners at plus and minus one, its coordinates nought to one, facing +z;
    /// the engine's own is in no file read (the program's image-space quads are a screen pass's), to be measured.
    /// </summary>
    public static ParticleMeshData Quad { get; } = new(
        [new(-1f, -1f, 0f), new(1f, -1f, 0f), new(1f, 1f, 0f), new(-1f, 1f, 0f)],
        [new(0f, 1f), new(1f, 1f), new(1f, 0f), new(0f, 0f)],
        [new(0f, 0f, 1f, 0f), new(0f, 0f, 1f, 0f), new(0f, 0f, 1f, 0f), new(0f, 0f, 1f, 0f)],
        [new(1f, 0f, 0f, 1f), new(1f, 0f, 0f, 1f), new(1f, 0f, 0f, 1f), new(1f, 0f, 0f, 1f)],
        [0, 1, 2, 0, 2, 3],
        "the quad (CHOSEN: corners at plus and minus one)");
}

/// <summary>
/// One GPU emitter of a particle effect, linked and ready for the card: its update and render passes, its mesh, and the values its dispatch is
/// handed (GpuParticleObjectUniforms, MEASURED v0.1.437) - everything but what changes a frame.
/// </summary>
/// <param name="Name">The effect and the emitter's place in it, for the account.</param>
/// <param name="Update">The update compute shader.</param>
/// <param name="Passes">The render passes, in the .pet's order.</param>
/// <param name="Mesh">What each particle is drawn as.</param>
/// <param name="Count">The pool - gpu_particle_count: a count drawn between the file's least and most, times the group size.</param>
/// <param name="GroupSize">group_size.</param>
/// <param name="Seed">gpu_particle_seed, a fraction.</param>
/// <param name="Burst">gpu_particle_burst - emit_burst over a hundred.</param>
/// <param name="Duration">gpu_particles_duration - particle_duration_min and _max.</param>
/// <param name="Culling">gpu_particle_culling_priority - Gameplay 0, Cosmetic 1, Important 100.</param>
/// <param name="EmitterDuration">The record's emitter_duration - emitter_duration_min..max drawn.</param>
/// <param name="Infinite">Whether the emitter runs on for good (is_continuous) - IS_INFINITE.</param>
/// <param name="LockFlags">The record's lock_flags, from the .pet's lock keys.</param>
/// <param name="Unresolved">What the link left out or stood in for, every shader's.</param>
public sealed record ParticleProgram(
    string Name,
    ParticleShaderText Update,
    IReadOnlyList<ParticlePassProgram> Passes,
    ParticleMeshData Mesh,
    int Count,
    int GroupSize,
    float Seed,
    float Burst,
    Vector2 Duration,
    uint Culling,
    float EmitterDuration,
    bool Infinite,
    uint LockFlags,
    IReadOnlyList<string> Unresolved);

/// <summary>One emitter as it stands this frame: its program, its frame now and the frame before, its clock, and its bones in its frame.</summary>
/// <param name="Program">What it is.</param>
/// <param name="World">Its frame in the picture - the record's translation and rotation.</param>
/// <param name="LastWorld">Its frame the frame before.</param>
/// <param name="Time">Its clock now, seconds since it was made.</param>
/// <param name="LastTime">Its clock the frame before.</param>
/// <param name="Bones">Its bone group in its own frame, w the distance along it (CarriedEffects.At).</param>
/// <param name="LastBones">The same the frame before.</param>
/// <param name="Fresh">Whether it was made this frame - IS_NEW, its particles started over.</param>
public sealed record ParticleEmitterNow(
    ParticleProgram Program,
    Matrix4x4 World,
    Matrix4x4 LastWorld,
    float Time,
    float LastTime,
    Vector4[] Bones,
    Vector4[] LastBones,
    bool Fresh);

/// <summary>Every emitter a picture draws this frame, the frame's step and its number - frame_delta_time and frame_index.</summary>
/// <param name="Emitters">The emitters.</param>
/// <param name="Delta">Seconds since the frame before - nought where the clock stands.</param>
/// <param name="Index">The pane's frame number.</param>
public sealed record ParticleFrame(IReadOnlyList<ParticleEmitterNow> Emitters, float Delta, uint Index);
