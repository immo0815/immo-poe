using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoEformance.Features;

/// <summary>The features the tool starts with, kept beside the executable.</summary>
/// <remarks>
/// These were command-line flags, and a first start with none of them had nothing to show.
/// The overlay is not one of them: it always starts, because its status page is where these
/// are switched. A command-line flag adds its feature for that start only and never removes
/// one, so turning a feature off is always done on the status page.
/// </remarks>
public sealed record LaunchSettings(
    [property: JsonPropertyName("config")] bool Config = false,
    [property: JsonPropertyName("autoFlask")] bool AutoFlask = false,
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
