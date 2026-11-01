using PoEformance.Features;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The bone watch read again from a capture: bone-watch-replay.txt, what this build's reading makes of the reads bone-watch.bin keeps - so a
/// change to how the runs of turn matrices are found or set out is held against a real watch without the game.
/// </summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THEBONEWatchIsReadAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, BoneWatchSeries.File)))
        {
            return;
        }

        BoneWatchSeries? series = BoneWatchSeries.Read(File.ReadAllBytes(CaptureParts.Find(folder, BoneWatchSeries.File)));
        Assert.NotNull(series);
        File.WriteAllLines(Path.Combine(folder, "bone-watch-replay.txt"), series.Describe());
    }
}
