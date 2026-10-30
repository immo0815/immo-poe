namespace PoEformance.Game.Files;

/// <summary>One emitter of a trail effect: its lines, each a key and what follows it, in the file's order.</summary>
public sealed record TrailEmitter(IReadOnlyList<KeyValuePair<string, string>> Values)
{
    /// <summary>The last value written under <paramref name="key"/> - the reference keeps the emitter as a map, so the last wins - or null.</summary>
    public string? this[string key]
    {
        get
        {
            for (int one = Values.Count - 1; one >= 0; one--)
            {
                if (string.Equals(Values[one].Key, key, StringComparison.Ordinal))
                {
                    return Values[one].Value;
                }
            }

            return null;
        }
    }
}

/// <summary>
/// A trail effect (<c>.trl</c>): counted emitters of key and value lines, and a JSON payload after them.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>trl</c> grammar (<c>file_parsers/trl/parser.rs</c>): lines trimmed and the blank ones dropped
/// (no comments); THE COUNT OF EMITTERS COMES FIRST, before an optional <c>version</c> line - the other way round
/// from a <c>.pet</c>; per emitter a line <c>{</c>, lines of an unquoted key, spaces and the rest, and a line
/// <c>}</c>; and whatever is left, joined with nothing between the lines, is one JSON value. The reference keeps an
/// emitter as a map; this keeps its lines in order, duplicates and all, and <see cref="TrailEmitter"/>'s indexer
/// answers as the map would.
/// </remarks>
/// <param name="Version">Off the version line, or null where the file has none.</param>
/// <param name="Emitters">The emitters, as many as counted or as read before a stop.</param>
/// <param name="Payload">The JSON after the emitters, or null where nothing follows them.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record TrailEffects(int? Version, IReadOnlyList<TrailEmitter> Emitters, string? Payload, string Why)
{
    /// <summary>What a trail effect's file name ends with.</summary>
    public const string Extension = ".trl";

    /// <summary>Nothing read.</summary>
    public static TrailEffects None { get; } = new(null, [], null, "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static TrailEffects Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static TrailEffects Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        FileLines lines = FileLines.Of(text, comment: null);
        var count = new LineWords(lines.Count > 0 ? lines[0] : default);
        if (!count.UInt(out uint emitterCount) || !count.End)
        {
            return None with { Why = $"{lines.Said(0)} is not the count of emitters" };
        }

        int at = 1;
        int? version = lines.Version(ref at);
        var emitters = new List<TrailEmitter>((int)Math.Min(emitterCount, (uint)lines.Count));
        for (uint one = 0; one < emitterCount; one++)
        {
            if (at >= lines.Count || !lines[at].SequenceEqual("{"))
            {
                return new TrailEffects(version, emitters, null, $"emitter {one}: {lines.Said(at)} is not its opening brace");
            }

            at++;
            var values = new List<KeyValuePair<string, string>>();
            for (; at < lines.Count && !lines[at].SequenceEqual("}"); at++)
            {
                var words = new LineWords(lines[at]);
                if (!words.Word(out string key) || !words.Space())
                {
                    return new TrailEffects(version, emitters, null, $"emitter {one}: {lines.Said(at)} is not a key and a value");
                }

                values.Add(new KeyValuePair<string, string>(key, words.Rest().ToString()));
            }

            if (at >= lines.Count)
            {
                return new TrailEffects(version, emitters, null, $"emitter {one}: no closing brace");
            }

            emitters.Add(new TrailEmitter(values));
            at++;
        }

        if (at >= lines.Count)
        {
            return new TrailEffects(version, emitters, null, string.Empty);
        }

        string payload = lines.Joined(at);
        return new TrailEffects(version, emitters, payload, FileLines.IsJson(payload) ? string.Empty : $"{lines.Said(at)} on is not one JSON value");
    }
}
