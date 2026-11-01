using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PoEformance.Features;

/// <summary>
/// The agent link's key: what a request through the tunnel must carry for the tool to answer it.
/// </summary>
/// <remarks>
/// MADE BY THE TOOL, NOT TYPED IN. Thirty-two random bytes, so nobody has to invent one and none can be
/// guessed; the player copies it once from the overlay into the cloud environment's settings (the variable
/// the bridge reads, <see cref="EnvironmentVariable"/>), where every later session finds it.
///
/// KEPT AS THE GITHUB TOKEN IS (<see cref="GitHubTokenStore"/>): sealed with DPAPI for this Windows user,
/// because config/ is plain JSON that travels in backups and zips. Unlike the token it can be shown again -
/// the overlay's "copy key" button - since a new cloud environment needs it as much as the first did, and
/// "renew" makes a new one when an old one may have gone where it should not.
/// </remarks>
public static class AgentKeyStore
{
    /// <summary>The variable the cloud side's bridge reads the key from.</summary>
    public const string EnvironmentVariable = "POEF_AGENT_KEY";

    /// <summary>Where the sealed key is kept.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "config", "agent-link.json");

    /// <summary>A new key: a prefix that says what it is, then 32 random bytes in URL-safe base64.</summary>
    public static string Make()
        => "poef_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// A short name for a key that says nothing about it: the first eight hex digits of its SHA-256,
    /// published beside the tunnel's address so the bridge can tell a key that does not match from a
    /// tunnel that is down.
    /// </summary>
    public static string Fingerprint(string key)
        => key.Length == 0 ? string.Empty : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];

    /// <summary>The saved key, or a new one saved first when there is none that opens.</summary>
    public static string LoadOrMake(Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect, string? path = null)
    {
        string key = Load(unprotect, path);
        if (key.Length > 0)
        {
            return key;
        }

        key = Make();
        return Save(key, protect, path) ? key : string.Empty;
    }

    /// <summary>The saved key, or empty when there is none or it does not open (copied from another user).</summary>
    public static string Load(Func<byte[], byte[]> unprotect, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(unprotect);
        (string sealedKey, _) = Read(path);
        if (sealedKey.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(unprotect(Convert.FromBase64String(sealedKey))).Trim();
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return string.Empty;
        }
    }

    /// <summary>Whether the link is switched on - off until the player switches it on once (the About page, "agent link").</summary>
    public static bool IsOn(string? path = null) => Read(path).On;

    /// <summary>Switches the link on or off, keeping the key; false where the file could not be written.</summary>
    public static bool SetOn(bool on, string? path = null) => Write(Read(path).SealedKey, on, path);

    /// <summary>Seals and saves a key, keeping whether the link is on, and says whether it worked.</summary>
    public static bool Save(string key, Func<byte[], byte[]> protect, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(protect);
        try
        {
            return Write(Convert.ToBase64String(protect(Encoding.UTF8.GetBytes(key))), Read(path).On, path);
        }
        catch (Exception exception) when (exception is CryptographicException or NotSupportedException)
        {
            return false;
        }
    }

    private static (string SealedKey, bool On) Read(string? path)
    {
        try
        {
            string file = path ?? DefaultPath;
            if (!File.Exists(file))
            {
                return (string.Empty, false);
            }

            using JsonDocument stored = JsonDocument.Parse(File.ReadAllBytes(file));
            JsonElement root = stored.RootElement;
            string sealedKey = root.TryGetProperty("key", out JsonElement key) && key.ValueKind == JsonValueKind.String ? key.GetString()! : string.Empty;
            bool on = root.TryGetProperty("on", out JsonElement switched) && switched.ValueKind == JsonValueKind.True;
            return (sealedKey, on);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return (string.Empty, false);
        }
    }

    private static bool Write(string sealedKey, bool on, string? path)
    {
        string file = path ?? DefaultPath;
        try
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("key", sealedKey);
                writer.WriteBoolean("on", on);
                writer.WriteEndObject();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temporary = file + ".writing";
            File.WriteAllBytes(temporary, buffer.ToArray());
            File.Move(temporary, file, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>The key in memory, read once and renewed on demand - a request through the tunnel compares against this, not the disk.</summary>
public sealed class AgentKeyHolder
{
    private readonly Func<byte[], byte[]> _protect;
    private readonly string? _path;
    private string _key;

    public AgentKeyHolder(Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(protect);
        _protect = protect;
        _path = path;
        _key = AgentKeyStore.LoadOrMake(protect, unprotect, path);
    }

    /// <summary>The key, empty only where none could be made or saved.</summary>
    public string Key => Volatile.Read(ref _key);

    /// <summary>Makes, saves and holds a new key - the old one stops working at once; false where it could not be saved.</summary>
    public bool Renew()
    {
        string made = AgentKeyStore.Make();
        if (!AgentKeyStore.Save(made, _protect, _path))
        {
            return false;
        }

        Volatile.Write(ref _key, made);
        return true;
    }
}
