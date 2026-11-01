using System.Text.Json;

namespace PoEformance.Features;

/// <summary>
/// Where the agent link's bridge finds the tunnel: one small file on a branch of the tool's own repository.
/// </summary>
/// <remarks>
/// A quick tunnel's name is new on every start (<see cref="AgentTunnel"/>), and the bridge in the cloud container
/// has exactly one way to the player's machine that needs no setting of its own: git, which it already uses for
/// the repository. So the tool writes the current name to <see cref="Branch"/> as a single parentless commit
/// (<see cref="GitHubWriter.PlaceAlone"/>, nothing accumulates) with the saved GitHub token - the boss-icon
/// upload's, which may write the repository - and the bridge fetches that branch whenever it has no working
/// address. The file holds no secret: the address alone answers nothing without the key, and the key's
/// fingerprint (<see cref="AgentKeyStore.Fingerprint"/>) only lets the bridge tell a wrong key from a dead tunnel.
/// </remarks>
public static class AgentBeacon
{
    /// <summary>The branch the address is published on. No workflow watches it.</summary>
    public const string Branch = "agent-link";

    /// <summary>The file on it.</summary>
    public const string FileName = "agent-link.json";

    /// <summary>The file's bytes for one address.</summary>
    public static byte[] Content(string url, string fingerprint, string version, string commit, DateTimeOffset since)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("url", url);
            writer.WriteString("since", since.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("version", version);
            writer.WriteString("commit", commit);
            writer.WriteString("key", fingerprint);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>Publishes an address with the given token.</summary>
    public static async Task<UploadOutcome> Publish(
        string token, string url, string fingerprint, string version, string commit, CancellationToken cancelling = default)
    {
        if (token.Length == 0)
        {
            return new UploadOutcome(false, 0,
                "no GitHub token saved - the address cannot be published (Status page, the GitHub token)");
        }

        using var writer = new GitHubWriter(token);
        return await writer.PlaceAlone(
            Branch, FileName, Content(url, fingerprint, version, commit, DateTimeOffset.UtcNow),
            $"Agent link at {url} ({version})", cancelling).ConfigureAwait(false);
    }
}
