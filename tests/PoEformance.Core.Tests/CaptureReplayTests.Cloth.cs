using PoEformance.Features;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The cloth watch read again from a capture: cloth-watch-replay.txt, what this build's cloth makes of the game's own reads cloth-watch.bin
/// keeps - every step taken again from the game's state onto its targets, and each object's setup against the tool's cloth of its piece - so a
/// change to the tool's cloth is held against the game's own steps without the game.
/// </summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THECLOTHWatchIsSteppedAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, ClothWatchSeries.File)))
        {
            return;
        }

        ClothWatchSeries? series = ClothWatchSeries.Read(File.ReadAllBytes(CaptureParts.Find(folder, ClothWatchSeries.File)));
        Assert.NotNull(series);
        File.WriteAllLines(Path.Combine(folder, "cloth-watch-replay.txt"), series.Report());
    }
}
