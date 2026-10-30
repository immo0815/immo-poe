using PoEformance.Features;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>The light beyond the sun - the clouds, the screen-space fog and shadow, the roof sections and the player's light - read back from a capture.</summary>
public partial class CaptureReplayTests
{
    /// <summary>light-extras.txt read back, each section's records counted and named - light-extras-replay.txt.</summary>
    [Fact]
    public void THELIGHTExtrasAreReadBackFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || CaptureParts.Find(folder, CaptureReport.LightExtrasFile) is not { } path || !File.Exists(path))
        {
            return;
        }

        string replay = CaptureLightExtras.Replay(File.ReadAllText(path));
        File.WriteAllText(Path.Combine(folder, CaptureLightExtras.ReplayFile), replay);
        Assert.StartsWith("light extras replay: ", replay, StringComparison.Ordinal);
    }
}
