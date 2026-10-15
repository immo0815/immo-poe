using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoEformance.Features;

/// <summary>What the tool decides at start and cannot change afterwards, kept beside the executable.</summary>
/// <remarks>
/// Only debug is one: the diagnostics are built into the overlay as it starts. The other
/// features are live switches on the status page - auto-flask is a checkbox that acts at once,
/// and the settings window is a button that opens it. A command-line flag adds debug for that
/// start only and never removes it.
/// </remarks>
public sealed record LaunchSettings(
    [property: JsonPropertyName("debug")] bool Debug = false)
{
    public static LaunchSettings Default { get; } = new();
}

/// <summary>Loads and saves <see cref="LaunchSettings"/> next to the executable.</summary>
/// <remarks>
/// Beside the executable, like every other settings file here: the tool is a portable directory
/// and its state should travel with it.
/// </remarks>
public static class LaunchSettingsStore
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "config", "launch.json");

    /// <summary>Reads the settings, falling back to all-off on any problem.</summary>
    /// <remarks>
    /// A missing file is the first start, and a corrupt one must not stop the tool from starting
    /// either. Everything here is off by default, so the failure mode is a plain start.
    /// </remarks>
    public static LaunchSettings Load(string? path = null)
    {
        string file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file))
            {
                return LaunchSettings.Default;
            }

            using FileStream stream = File.OpenRead(file);
            LaunchSettings? loaded = JsonSerializer.Deserialize(stream, LaunchJsonContext.Default.LaunchSettings);
            return loaded ?? LaunchSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return LaunchSettings.Default;
        }
    }

    /// <summary>Writes the settings, returning false when it could not.</summary>
    public static bool Save(LaunchSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string file = path ?? DefaultPath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using FileStream stream = File.Create(file);
            JsonSerializer.Serialize(stream, settings, LaunchJsonContext.Default.LaunchSettings);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

[JsonSerializable(typeof(LaunchSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public sealed partial class LaunchJsonContext : JsonSerializerContext;
