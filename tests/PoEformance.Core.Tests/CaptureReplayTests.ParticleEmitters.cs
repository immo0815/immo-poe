using PoEformance.Features;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The particle emitters' records read again from a capture: particle-emitters-replay.txt, every record line of particle-emitters.txt decoded
/// with this build's decoder (ParticleEmitterHunt.Replay), so a corrected reading of a field is held against the game's own bytes offline.
/// </summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THEPARTICLEEmittersRecordsAreDecodedAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !File.Exists(CaptureParts.Find(folder, CaptureReport.ParticleEmittersFile)))
        {
            return;
        }

        string region = CaptureParts.Find(folder, ParticleEmitterHunt.RegionFile);
        List<string> lines = ParticleEmitterHunt.Replay(
            File.ReadLines(CaptureParts.Find(folder, CaptureReport.ParticleEmittersFile)), File.Exists(region) ? File.ReadAllBytes(region) : null);
        File.WriteAllLines(Path.Combine(folder, "particle-emitters-replay.txt"), lines);
    }
}
