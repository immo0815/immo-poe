using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The .ao simulations part read back from a capture: ao-simulations-replay.txt, its sets ranked again from its numbers
/// (ao-simulations.tsv beside the part) - so a change to the ranking is held against the whole install's files without the game.
/// </summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THEAOSimulationsAreRankedAgainFromTheirNumbers()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, CaptureReport.AoSimulationsFile)))
        {
            return;
        }

        string data = Path.Combine(Path.GetDirectoryName(CaptureParts.Find(folder, CaptureReport.AoSimulationsFile)) ?? folder, CaptureAoSimulations.DataFile);
        if (!File.Exists(data))
        {
            return;
        }

        List<string> replay = CaptureAoSimulations.Replay(File.ReadAllText(data));
        File.WriteAllLines(Path.Combine(folder, "ao-simulations-replay.txt"), replay);
        Assert.EndsWith($"worked out again from {CaptureAoSimulations.DataFile}", replay[0], StringComparison.Ordinal);
    }
}
