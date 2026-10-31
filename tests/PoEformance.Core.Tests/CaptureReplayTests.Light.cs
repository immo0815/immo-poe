using PoEformance.Features;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>The parts that hold a stage against the game's picture, read back from the numbers a capture kept.</summary>
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

    /// <summary>Each roof section's hidden share fitted again from roof-fade.bin - roof-fade-replay.txt.</summary>
    [Fact]
    public void THEROOFSectionsFadeIsJudgedAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, CaptureRoofFade.Numbers)))
        {
            return;
        }

        string replay = CaptureRoofFade.Replay(folder);
        File.WriteAllText(Path.Combine(folder, CaptureRoofFade.ReplayFile), replay);
        Assert.StartsWith("roof fade replay: ", replay, StringComparison.Ordinal);
    }
}
