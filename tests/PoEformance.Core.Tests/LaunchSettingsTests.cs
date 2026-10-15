using PoEformance.Features;

namespace PoEformance.Core.Tests;

public sealed class LaunchSettingsTests
{
    [Fact]
    public void A_missing_file_is_the_first_start_with_everything_off()
    {
        string path = Path.Combine(Path.GetTempPath(), $"poeformance-launch-{Guid.NewGuid():N}.json");

        LaunchSettings loaded = LaunchSettingsStore.Load(path);

        Assert.Equal(LaunchSettings.Default, loaded);
        Assert.False(loaded.Debug);
    }

    [Fact]
    public void Saved_switches_come_back_as_they_were_saved()
    {
        string path = Path.Combine(Path.GetTempPath(), $"poeformance-launch-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new LaunchSettings(Debug: true);

            Assert.True(LaunchSettingsStore.Save(settings, path));
            Assert.Equal(settings, LaunchSettingsStore.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_corrupt_file_starts_the_tool_with_everything_off_rather_than_failing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"poeformance-launch-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ this is not json");

            Assert.Equal(LaunchSettings.Default, LaunchSettingsStore.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
