using System.Text.Json;

namespace PoEformance.Game.Files;

/// <summary>One render pass of a GPU emitter: the material it is drawn with, how it faces, and whether it may be left out.</summary>
/// <param name="Material">render_material's path.</param>
/// <param name="FaceLock">face_lock as written (<c>Velocity_Camera</c>, <c>None</c>).</param>
/// <param name="Optional">optional - the engine may leave the pass out.</param>
/// <param name="OverwriteMesh">overwrite_mesh.</param>
/// <param name="VertexGroups">The pass's own vertex_groups.</param>
public sealed record ParticlePass(string Material, string FaceLock, bool Optional, bool OverwriteMesh, bool VertexGroups);

/// <summary>
/// One <c>gpu_emitters</c> entry of a <c>.pet</c>, with the keys the engine's shader composer reads (see <see cref="ParticleComposition"/>) and
/// the rest kept as written.
/// </summary>
/// <param name="UpdateMaterial">update_material's path.</param>
/// <param name="Mesh">particle_mesh's path, or empty for camera-facing quads.</param>
/// <param name="CountType">count_type as written - Fixed, Random, PPS, Expression, EPS; empty where unset.</param>
/// <param name="Distributed">Whether particles_distribution is written (the reader sets its flag only then, and not for PPS or EPS).</param>
/// <param name="GroupSize">group_size, one where unset.</param>
/// <param name="Passes">render_passes in order.</param>
/// <param name="Flags">Every boolean key as written.</param>
/// <param name="Words">Every string key as written (the lock_scale and lock_rotation modes among them).</param>
/// <param name="Numbers">Every number key as written.</param>
public sealed record ParticleEmitter(
    string UpdateMaterial,
    string Mesh,
    string CountType,
    bool Distributed,
    int GroupSize,
    IReadOnlyList<ParticlePass> Passes,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyDictionary<string, string> Words,
    IReadOnlyDictionary<string, double> Numbers)
{
    /// <summary>A boolean key, false where unset - the reader's local block starts nought (module+0x28E7775 and its kin store nought for an absent key).</summary>
    public bool Flag(string key) => Flags.TryGetValue(key, out bool value) && value;

    /// <summary>A string key, empty where unset.</summary>
    public string Word(string key) => Words.TryGetValue(key, out string? value) ? value : string.Empty;

    /// <summary>A number key, or <paramref name="otherwise"/> where unset.</summary>
    public double Number(string key, double otherwise = 0) => Numbers.TryGetValue(key, out double value) ? value : otherwise;
}

/// <summary>
/// A particle effect file (<c>.pet</c>): its version line, then JSON whose <c>gpu_emitters</c> list the engine's GPU emitters.
/// </summary>
/// <remarks>
/// The JSON as particles.txt wrote lamp.pet whole (2026-11-01 16-07-31): <c>version 5</c>, a line <c>0</c>, then one object. Only the GPU
/// emitters are read; a CPU emitter list, where a file has one, is not part of the engine's GPU particle system.
/// </remarks>
public static class ParticleEffect
{
    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>The file's GPU emitters in order; empty for anything that is not such a file.</summary>
    public static IReadOnlyList<ParticleEmitter> Read(byte[]? content)
        => content is { Length: > 0 } ? Read(StatDescriptionFiles.Decode(content)) : [];

    /// <summary>The GPU emitters of a <c>.pet</c>'s text.</summary>
    public static IReadOnlyList<ParticleEmitter> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int open = text.IndexOf('{', StringComparison.Ordinal);
        if (open < 0)
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text[open..], Lenient);
            if (!document.RootElement.TryGetProperty("gpu_emitters", out JsonElement listed) || listed.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var emitters = new List<ParticleEmitter>();
            foreach (JsonElement emitter in listed.EnumerateArray())
            {
                if (emitter.ValueKind == JsonValueKind.Object)
                {
                    emitters.Add(Emitter(emitter));
                }
            }

            return emitters;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ParticleEmitter Emitter(JsonElement emitter)
    {
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        var words = new Dictionary<string, string>(StringComparer.Ordinal);
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        var passes = new List<ParticlePass>();
        foreach (JsonProperty property in emitter.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.True or JsonValueKind.False:
                    flags[property.Name] = property.Value.ValueKind == JsonValueKind.True;
                    break;
                case JsonValueKind.String:
                    words[property.Name] = property.Value.GetString() ?? string.Empty;
                    break;
                case JsonValueKind.Number:
                    numbers[property.Name] = property.Value.GetDouble();
                    break;
                case JsonValueKind.Array when property.Name == "render_passes":
                    foreach (JsonElement pass in property.Value.EnumerateArray())
                    {
                        if (pass.ValueKind == JsonValueKind.Object)
                        {
                            passes.Add(new ParticlePass(Text(pass, "render_material"), Text(pass, "face_lock"), Bool(pass, "optional"), Bool(pass, "overwrite_mesh"), Bool(pass, "vertex_groups")));
                        }
                    }

                    break;
            }
        }

        string count = words.GetValueOrDefault("count_type") ?? string.Empty;
        bool distributed = emitter.TryGetProperty("particles_distribution", out _) && !IsPerSecond(count);
        int group = numbers.TryGetValue("group_size", out double size) ? (int)size : 1;
        return new ParticleEmitter(
            words.GetValueOrDefault("update_material") ?? string.Empty,
            words.GetValueOrDefault("particle_mesh") ?? string.Empty,
            count,
            distributed,
            group,
            passes,
            flags,
            words,
            numbers);
    }

    /// <summary>Whether a count type is spawned by the second (PPS, EPS - the enum's 2 and 4, which the composer tests as <c>(x - 2) &amp; ~2 == 0</c>).</summary>
    public static bool IsPerSecond(string countType) => countType is "PPS" or "EPS";

    private static string Text(JsonElement owner, string name)
        => owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static bool Bool(JsonElement owner, string name)
        => owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
