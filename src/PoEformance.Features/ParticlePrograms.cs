using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The engine's GPU particles a model carries, made ready for the card: each emitter of each carried effect composed as the engine composes it
/// (ParticleComposition), its update and render passes linked from the install's own sources (ParticleLinker), the includes expanded, its
/// textures and mesh read, and the values its dispatch is handed set (ParticleProgram).
/// </summary>
/// <remarks>
/// THE CAPTURE'S LINK AT A MODEL'S LOAD (v0.1.441, DECIDED from the live client: the engine's particle system built generically, update and
/// render in one go, on the card alone): what particles.txt has linked and FXC compiled since v0.1.429 - the lamp's three updates and every
/// render pass compile at cs_5_0, vs_5_0 and ps_5_0 (the capture of 23:05) - now linked where the Monster Book builds the monster.
/// THE VALUES NO FILE HOLDS, as decided (said in each program's account): the count drawn between particles_count_min and _max and the
/// seed by a stable hash of the effect and the emitter - the engine draws them at random, not read; emitter_duration likewise between its min
/// and max. MEASURED (GpuParticleObjectUniforms, v0.1.437): the count is that draw times group_size, the burst emit_burst over a hundred,
/// the duration particle_duration_min and _max, the culling priority Gameplay 0, Cosmetic 1, Important 100.
/// </remarks>
public static class ParticlePrograms
{
    /// <summary>The sources read once per install - every .ffx the startup walk found, parsed (ShaderSource) - by the list's identity.</summary>
    private static readonly ConditionalWeakTable<IReadOnlyList<string>, ShaderSource> Sources = new();

    /// <summary>
    /// Every emitter of every effect a model carries, linked for the card; and the account: per emitter what was made and what was left
    /// out, or why nothing was.
    /// </summary>
    /// <param name="read">The install's files by path.</param>
    /// <param name="shaders">Every shader source the install's walk found (only the .ffx are read).</param>
    /// <param name="prelude">The engine's DX11 prelude (ShaderPrelude), or null.</param>
    /// <param name="effects">The effects the model carries.</param>
    public static (IReadOnlyList<(CarriedEffect Effect, ParticleProgram Program)> Programs, IReadOnlyList<string> Said) Of(
        Func<string, byte[]?> read, IReadOnlyList<string> shaders, string? prelude, IReadOnlyList<CarriedEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(effects);
        var said = new List<string>();
        var made = new List<(CarriedEffect, ParticleProgram)>();
        if (effects.Count == 0)
        {
            return (made, said);
        }

        if (prelude is null)
        {
            said.Add("no particles: the engine's DX11 prelude was not read from the game's program");
            return (made, said);
        }

        ShaderSource source = Sources.GetValue(shaders, list => Parse(read, list));
        if (source.Fragments.Count == 0)
        {
            said.Add("no particles: the install's walk found no shader sources");
            return (made, said);
        }

        var texts = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? Text(string path) => texts.GetOrAdd(Clean(path), clean => read(clean) is { Length: > 0 } bytes ? StatDescriptionFiles.Decode(bytes) : null);
        foreach (CarriedEffect effect in effects)
        {
            IReadOnlyList<ParticleEmitter> emitters = Text(effect.Effect) is { } pet ? ParticleEffect.Read(pet) : [];
            if (emitters.Count == 0)
            {
                said.Add($"{Short(effect.Effect)}: no GPU emitter read");
                continue;
            }

            for (var e = 0; e < emitters.Count; e++)
            {
                (ParticleProgram? program, string line) = Linked(read, Text, source, prelude, effect.Effect, e, emitters[e]);
                said.Add(line);
                if (program is not null)
                {
                    made.Add((effect, program));
                }
            }
        }

        return (made, said);
    }

    /// <summary>One emitter linked: its update and passes, its mesh and textures, its values - or null and why not.</summary>
    private static (ParticleProgram? Program, string Said) Linked(
        Func<string, byte[]?> read, Func<string, string?> text, ShaderSource source, string prelude, string effect, int index, ParticleEmitter emitter)
    {
        string name = $"{Short(effect)}[{index}]";
        ParticleShaders shaders = ParticleComposition.Of(emitter);
        var log = new StringBuilder();
        List<LinkedGraph> updateGraphs = CaptureParticles.Graphs(text, shaders.Update, log);
        LinkedShader update = ParticleLinker.Update(source, updateGraphs, prelude);
        var unresolved = new List<string>(update.Unresolved.Select(one => "update: " + one));
        string updateText = ParticleLinker.Expanded(update.Hlsl, text, out IReadOnlyList<string> absent);
        unresolved.AddRange(absent.Select(one => "update: include not found " + one));

        var passes = new List<ParticlePassProgram>(shaders.Passes.Count);
        var skipped = new List<string>();
        for (var p = 0; p < shaders.Passes.Count; p++)
        {
            List<LinkedGraph> graphs = CaptureParticles.Graphs(text, shaders.Passes[p], log);
            ParticleBlend blend = BlendOf(graphs);
            if (blend is ParticleBlend.Shimmer or ParticleBlend.Unknown)
            {
                // A SHIMMER DISTORTS THE PICTURE BEHIND IT (Shimmer.fxgraph's own blend mode), a pass the card does not draw yet - said.
                skipped.Add($"pass {p} ({(blend == ParticleBlend.Shimmer ? "Shimmer, a distortion of the picture behind it" : "a blend mode not read: " + string.Join(", ", graphs.Select(one => one.Graph.BlendMode).Where(mode => mode.Length > 0)))}) not drawn");
                continue;
            }

            (LinkedShader vertex, LinkedShader pixel) = ParticleLinker.Render(source, graphs, prelude, CaptureParticles.PassMacros(graphs));
            unresolved.AddRange(vertex.Unresolved.Select(one => $"pass {p} vertex: {one}"));
            unresolved.AddRange(pixel.Unresolved.Select(one => $"pass {p} pixel: {one}"));
            var textures = new List<(string, string, Mipmaps?)>();
            foreach ((string global, string path) in vertex.Resources.Concat(pixel.Resources).DistinctBy(one => one.Name))
            {
                textures.Add((global, path, path.Contains('/', StringComparison.Ordinal) ? Texture(read, path) : null));
            }

            string material = emitter.Passes.Count > p ? emitter.Passes[p].Material : string.Empty;
            passes.Add(new ParticlePassProgram(
                new ParticleShaderText(ParticleLinker.Expanded(vertex.Hlsl, text, out _), vertex.Entry, vertex.Tracks ?? []),
                new ParticleShaderText(ParticleLinker.Expanded(pixel.Hlsl, text, out _), pixel.Entry, pixel.Tracks ?? []),
                blend,
                textures,
                material));
        }

        unresolved.AddRange(skipped);
        if (passes.Count == 0)
        {
            return (null, $"{name}: no pass the card draws - {string.Join("; ", skipped)}");
        }

        // THE VALUES NO FILE HOLDS, drawn by a stable hash within the file's ranges (DECIDED from the live client).
        uint hash = Hash(effect + "#" + index.ToString(CultureInfo.InvariantCulture));
        float Fraction(int salt) => (Mix(hash + (uint)salt) & 0xFFFFFF) / (float)0x1000000;
        int least = (int)emitter.Number("particles_count_min", 1), most = Math.Max(least, (int)emitter.Number("particles_count_max", least));
        int drawn = least + (int)MathF.Floor(Fraction(1) * (most - least + 1));
        int group = Math.Max(1, emitter.GroupSize);
        float emitterLeast = (float)emitter.Number("emitter_duration_min", 1), emitterMost = (float)emitter.Number("emitter_duration_max", emitterLeast);
        ParticleMeshData mesh = MeshOf(read, emitter.Mesh, out string meshSaid);
        if (meshSaid.Length > 0)
        {
            unresolved.Add(meshSaid);
        }

        var program = new ParticleProgram(
            name,
            new ParticleShaderText(updateText, update.Entry, update.Tracks ?? []),
            passes,
            mesh,
            Math.Max(1, drawn) * group,
            group,
            Fraction(2),
            (float)emitter.Number("emit_burst") / 100f,
            new Vector2((float)emitter.Number("particle_duration_min", 1), (float)emitter.Number("particle_duration_max", 1)),
            emitter.Word("culling_prio") switch { "Gameplay" => 0u, "Important" => 100u, _ => 1u },
            emitterLeast + (Fraction(3) * (emitterMost - emitterLeast)),
            emitter.Flag("is_continuous"),
            LocksOf(emitter, unresolved),
            unresolved);
        return (program, string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: {program.Count} particles ({drawn} drawn of {least}..{most} x group {group}), {passes.Count} of {shaders.Passes.Count} passes, mesh {mesh.Source}, {unresolved.Count} left out or stood in"));
    }

    /// <summary>A pass's blend mode: the last of its graphs to override it (the material's after the engine's), Alpha where none does.</summary>
    private static ParticleBlend BlendOf(IReadOnlyList<LinkedGraph> graphs)
    {
        string mode = graphs.Select(one => one.Graph.BlendMode).LastOrDefault(one => one.Length > 0) ?? string.Empty;
        return mode switch
        {
            "" or "AlphaBlend" => ParticleBlend.Alpha,
            "Additive" => ParticleBlend.Additive,
            "Shimmer" => ParticleBlend.Shimmer,
            _ => ParticleBlend.Unknown,
        };
    }

    /// <summary>
    /// The record's lock_flags from the .pet's lock keys - the booleans as written (gpuparticles.ffx's PARTICLE_LOCK_FLAG_*); a mode word other
    /// than Disabled taken as its plain flag, with _EMIT where it names Emit (CHOSEN, said: the lamp's modes are all Disabled).
    /// </summary>
    private static uint LocksOf(ParticleEmitter emitter, List<string> said)
    {
        uint flags = 0;
        void Flag(string key, uint flag)
        {
            if (emitter.Flag(key))
            {
                flags |= flag;
            }
        }

        void Mode(string key, uint plain, uint emit)
        {
            string word = emitter.Word(key);
            if (word.Length > 0 && word != "Disabled")
            {
                flags |= word.Contains("Emit", StringComparison.OrdinalIgnoreCase) ? emit : plain;
                said.Add($"lock {key} \"{word}\" taken as {(word.Contains("Emit", StringComparison.OrdinalIgnoreCase) ? "its _EMIT flag" : "its flag")} (CHOSEN)");
            }
        }

        Flag("lock_translation", 0x00000001);
        Flag("lock_movement", 0x00008000);
        Flag("lock_translation_to_bone", 0x00010000);
        Flag("lock_movement_to_bone", 0x00080000);
        Flag("lock_rotation_bone_fixed", 0x00100000);
        Flag("lock_along", 0x00200000);
        Flag("lock_along_to_bone", 0x00400000);
        Mode("lock_rotation", 0x00000002, 0x00000004);
        Mode("lock_scale_x", 0x00000008, 0x00000010);
        Mode("lock_scale_y", 0x00000020, 0x00000040);
        Mode("lock_scale_z", 0x00000080, 0x00000100);
        Mode("lock_scale_x_to_bone", 0x00000200, 0x00000400);
        Mode("lock_scale_y_to_bone", 0x00000800, 0x00001000);
        Mode("lock_scale_z_to_bone", 0x00002000, 0x00004000);
        Mode("lock_rotation_to_bone", 0x00020000, 0x00040000);
        return flags;
    }

    /// <summary>The emitter's particle_mesh as the card draws it, or the quad where it names none or it does not read.</summary>
    private static ParticleMeshData MeshOf(Func<string, byte[]?> read, string path, out string said)
    {
        said = string.Empty;
        if (path.Length == 0)
        {
            said = "mesh: " + ParticleMeshData.Quad.Source;
            return ParticleMeshData.Quad;
        }

        FixedMesh fixedMesh = FixedMesh.Read(read(Clean(path)));
        SkinnedMesh mesh = fixedMesh.Mesh;
        if (!fixedMesh.Ready || mesh.Positions.Length == 0)
        {
            said = $"mesh {path} did not read ({fixedMesh.Why}): the quad stands in";
            return ParticleMeshData.Quad;
        }

        int count = mesh.Positions.Length;
        var coordinates = new Vector2[count];
        var normals = new Vector4[count];
        var tangents = new Vector4[count];
        for (var at = 0; at < count; at++)
        {
            coordinates[at] = at < mesh.Coordinates.Length ? mesh.Coordinates[at] : Vector2.Zero;
            Vector3 normal = at < mesh.Normals.Length ? mesh.Normals[at] : Vector3.UnitZ;
            normals[at] = new Vector4(normal, 0f);
            Vector3 across = MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            tangents[at] = new Vector4(Vector3.Normalize(across - (Vector3.Dot(across, normal) * normal)), 1f);
        }

        said = $"mesh {Short(path)}: normals as read, w nought; tangents made across them (CHOSEN: the file's own not read)";
        return new ParticleMeshData([.. mesh.Positions], coordinates, normals, tangents, [.. mesh.Indices.Select(one => (uint)one)], Short(path));
    }

    /// <summary>A texture with its levels - the file's own as its blocks where it carries every one, else decoded with levels built.</summary>
    private static Mipmaps? Texture(Func<string, byte[]?> read, string path)
    {
        byte[]? raw = GameArt.ReadRaw(read, Clean(path));
        return raw is null ? null : Mipmaps.Kept(BlockTexture.Of(raw)) ?? Mipmaps.Of(GameArt.Decode(raw));
    }

    private static ShaderSource Parse(Func<string, byte[]?> read, IReadOnlyList<string> shaders)
    {
        var texts = new ConcurrentBag<(string, string)>();
        Parallel.ForEach(shaders.Select(Clean).Where(path => path.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase), path =>
        {
            if (read(path) is { Length: > 0 } bytes)
            {
                texts.Add((path, StatDescriptionFiles.Decode(bytes)));
            }
        });

        return ShaderSource.Of([.. texts.OrderBy(one => one.Item1, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>FNV-1a over the text's UTF-16 units - the draw's seed.</summary>
    private static uint Hash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash;
    }

    /// <summary>A 32-bit integer mixed (Murmur3's finaliser) - one draw a salt.</summary>
    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x85EBCA6B;
        value ^= value >> 13;
        value *= 0xC2B2AE35;
        value ^= value >> 16;
        return value;
    }

    private static string Clean(string path) => path.Replace('\\', '/').Trim();

    private static string Short(string path) => path[(path.Replace('\\', '/').LastIndexOf('/') + 1)..];
}
