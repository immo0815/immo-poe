namespace PoEformance.Game.Files;

/// <summary>One emitter of a particle effect: its type, its material, and its body as written.</summary>
/// <param name="Type">The unquoted word after the brace (the reference's <c>emitter_type</c>).</param>
/// <param name="Material">The quoted <c>.mat</c>, or null where the line is <c>""</c>.</param>
/// <param name="Body">The lines up to the closing brace, trimmed and joined by <c>\n</c> - the reference's <c>key_values</c>, kept unread.</param>
public sealed record ParticleEmitter(string Type, string? Material, string Body);

/// <summary>
/// A particle effect (<c>.pet</c>): counted emitters, each a type, a material and a body, and - for a GPU particle -
/// a JSON payload after them.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>pet</c> grammar (<c>file_parsers/pet/parser.rs</c>): lines trimmed and the blank ones dropped
/// (no comments); an optional <c>version</c> line; a count of emitters; per emitter a line <c>{</c>, a type, a
/// quoted <c>.mat</c> or <c>""</c>, any lines and a line <c>}</c>; and whatever is left, joined with nothing
/// between the lines, is one JSON value. The water textures part found a GPU particle (Act 4's
/// <c>foamCrash_01.pet</c>) naming an <c>update_material</c> and a <c>render_material</c> in that payload; it is
/// kept as text here, a valid value (<see cref="FileLines.IsJson"/>) and nothing parsed further, so a reader takes
/// <see cref="System.Text.Json.JsonDocument"/> to it only when it asks.
/// </remarks>
/// <param name="Version">Off the version line, or null where the file has none.</param>
/// <param name="Emitters">The emitters, as many as counted or as read before a stop.</param>
/// <param name="Payload">The JSON after the emitters, or null where nothing follows them.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record ParticleEmitters(int? Version, IReadOnlyList<ParticleEmitter> Emitters, string? Payload, string Why)
{
    /// <summary>What a particle effect's file name ends with.</summary>
    public const string Extension = ".pet";

    /// <summary>Nothing read.</summary>
    public static ParticleEmitters None { get; } = new(null, [], null, "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static ParticleEmitters Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static ParticleEmitters Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        FileLines lines = FileLines.Of(text, comment: null);
        int at = 0;
        int? version = lines.Version(ref at);

        var count = new LineWords(at < lines.Count ? lines[at] : default);
        if (!count.UInt(out uint emitterCount) || !count.End)
        {
            return new ParticleEmitters(version, [], null, $"{lines.Said(at)} is not the count of emitters");
        }

        at++;
        var emitters = new List<ParticleEmitter>((int)Math.Min(emitterCount, (uint)lines.Count));
        for (uint one = 0; one < emitterCount; one++)
        {
            if (at >= lines.Count || !lines[at].SequenceEqual("{"))
            {
                return new ParticleEmitters(version, emitters, null, $"emitter {one}: {lines.Said(at)} is not its opening brace");
            }

            var type = new LineWords(at + 1 < lines.Count ? lines[at + 1] : default);
            if (!type.Word(out string emitterType) || !type.End)
            {
                return new ParticleEmitters(version, emitters, null, $"emitter {one}: {lines.Said(at + 1)} is not its type");
            }

            var quoted = new LineWords(at + 2 < lines.Count ? lines[at + 2] : default);
            if (!quoted.Quoted(out ReadOnlySpan<char> material) || !quoted.End
                || (!material.IsEmpty && !material.EndsWith(".mat", StringComparison.Ordinal)))
            {
                return new ParticleEmitters(version, emitters, null, $"emitter {one}: {lines.Said(at + 2)} is not a quoted .mat or \"\"");
            }

            int body = at + 3;
            int close = body;
            while (close < lines.Count && !lines[close].SequenceEqual("}"))
            {
                close++;
            }

            if (close >= lines.Count)
            {
                return new ParticleEmitters(version, emitters, null, $"emitter {one}: no closing brace after {lines.Said(body - 1)}");
            }

            var joined = new System.Text.StringBuilder();
            for (int line = body; line < close; line++)
            {
                _ = (line > body ? joined.Append('\n') : joined).Append(lines[line]);
            }

            emitters.Add(new ParticleEmitter(emitterType, material.IsEmpty ? null : LineWords.Forward(material), joined.ToString()));
            at = close + 1;
        }

        if (at >= lines.Count)
        {
            return new ParticleEmitters(version, emitters, null, string.Empty);
        }

        string payload = lines.Joined(at);
        return new ParticleEmitters(version, emitters, payload, FileLines.IsJson(payload) ? string.Empty : $"{lines.Said(at)} on is not one JSON value");
    }
}
