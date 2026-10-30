using PoEformance.Features;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>The two parts that hold a stage against the game's picture, read back from the numbers a capture kept.</summary>
public partial class CaptureReplayTests
{
    /// <summary>The light against the game worked out again from light-against-game.bin - light-against-game-replay.txt.</summary>
    [Fact]
    public void THELIGHTAgainstTheGameIsWorkedOutAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, CaptureLightCompare.Numbers)))
        {
            return;
        }

        string replay = CaptureLightCompare.Replay(folder);
        File.WriteAllText(Path.Combine(folder, CaptureLightCompare.ReplayFile), replay);
        Assert.StartsWith("light against the game replay: ", replay, StringComparison.Ordinal);
    }

    /// <summary>The ground's shadows by kind counted again from shadow-casters.bin - shadow-casters-replay.txt.</summary>
    [Fact]
    public void THESHADOWCastersAreCountedAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, CaptureShadowCasters.Numbers)))
        {
            return;
        }

        string replay = CaptureShadowCasters.Replay(folder);
        File.WriteAllText(Path.Combine(folder, CaptureShadowCasters.ReplayFile), replay);
        Assert.StartsWith("shadow casters replay: ", replay, StringComparison.Ordinal);
    }
}
