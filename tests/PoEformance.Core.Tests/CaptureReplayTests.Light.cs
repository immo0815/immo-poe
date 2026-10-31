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

    /// <summary>The files the player look part wrote whole read again, each one's lines a piece attaches by - player-look-replay.txt.</summary>
    [Fact]
    public void THEPLAYERLooksFilesAreReadAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder || !Directory.EnumerateDirectories(folder, CapturePlayerLook.Folder, SearchOption.AllDirectories).Any())
        {
            return;
        }

        string replay = CapturePlayerLook.Replay(folder);
        File.WriteAllText(Path.Combine(folder, CapturePlayerLook.ReplayFile), replay);
        Assert.StartsWith("player look replay: ", replay, StringComparison.Ordinal);
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

    /// <summary>
    /// The roof fade region's extents the verdicts of several captures allow together - POEF_CAPTURES, or POEF_CAPTURE alone - written as
    /// roof-fade-extents-replay.txt beside the first: each capture's verdicts narrow how far towards the camera, aside and behind the game hides.
    /// </summary>
    [Fact]
    public void THEROOFRegionsExtentsAreBoundedOverCaptures()
    {
        string[] folders = [.. Folders().Where(one => File.Exists(CaptureParts.Find(one, CaptureRoofFade.Numbers)))];
        if (folders.Length == 0)
        {
            return;
        }

        string extents = CaptureRoofFade.ExtentsOver(folders);
        File.WriteAllText(Path.Combine(folders[0], CaptureRoofFade.ExtentsFile), extents);
        Assert.StartsWith("roof fade extents over ", extents, StringComparison.Ordinal);
    }
}
