using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's catalogue: every part once, under a listed heading, writing its own file; what the ticked ones ask of the recording; and the unticked keys kept with the settings.
/// </summary>
public class CapturePartsTests
{
    /// <summary>Keys and files are unique, every part sits under a heading the page lists, and the recording's own parts are where the page puts them last.</summary>
    [Fact]
    public void THECATALOGUEListsEveryPartOnceUnderAListedHeading()
    {
        Assert.Equal(CaptureParts.All.Count, CaptureParts.All.Select(part => part.Key).Distinct(StringComparer.Ordinal).Count());
        string[] files = [.. CaptureParts.All.Where(part => part.File.Length > 0).Select(part => part.File)];
        Assert.Equal(files.Length, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(CaptureParts.All, part => Assert.Contains(part.Group, CaptureParts.Groups));
        Assert.Equal(CaptureParts.All.Count, CaptureParts.Groups.Sum(group => CaptureParts.In(group).Count()));

        Assert.Equal(CaptureReport.GameShot, CaptureParts.All.Single(part => part.Key == CaptureParts.GamePicture).File);
        Assert.Equal(CaptureReport.MemoryFile, CaptureParts.All.Single(part => part.Key == CaptureParts.Memory).File);
        Assert.Equal(string.Empty, CaptureParts.All.Single(part => part.Key == CaptureParts.RawSweep).File);
        Assert.Equal("memory.rec: ", CaptureParts.All.Single(part => part.Key == CaptureParts.Memory).Hover[..12]);
        Assert.Equal("Memory", CaptureParts.Groups[^1]);
    }

    /// <summary>What the pass reads is the union over the ticked parts, and nothing at all once the recording itself is off.</summary>
    [Fact]
    public void THERECORDINGReadsWhatTheTickedPartsAsk()
    {
        var none = new HashSet<string>(StringComparer.Ordinal);
        Assert.Equal(CaptureReads.World | CaptureReads.Loaded | CaptureReads.Doodads | CaptureReads.Sweep | CaptureReads.Monsters | CaptureReads.Landscape, CaptureParts.Reads(none));

        var doodadsOnly = new HashSet<string>(
            CaptureParts.All.Select(part => part.Key).Where(key => key is not (CaptureParts.Doodads or CaptureParts.RoomsPlaced or CaptureParts.Memory)),
            StringComparer.Ordinal);
        Assert.Equal(CaptureReads.Doodads, CaptureParts.Reads(doodadsOnly));
        Assert.True(CaptureParts.IsOn(CaptureParts.Doodads, doodadsOnly));
        Assert.False(CaptureParts.IsOn(CaptureParts.Entities, doodadsOnly));

        Assert.Equal(CaptureReads.Loaded | CaptureReads.Doodads, CaptureParts.Reads(new HashSet<string>([CaptureParts.Entities, CaptureParts.RawSweep, CaptureParts.Monsters, CaptureParts.Landscape], StringComparer.Ordinal)));

        // The monsters bring the world read they list from, and live only inside the recording.
        Assert.True(CaptureMemory.ReadsWorld(CaptureReads.Monsters));
        Assert.True(CaptureParts.NeedsRecording(CaptureParts.Monsters));
        Assert.True(CaptureParts.NeedsRecording(CaptureParts.RawSweep));

        // So does the terrain's second grid, with the world read whose ground it is held against.
        Assert.True(CaptureMemory.ReadsWorld(CaptureReads.Landscape));
        Assert.True(CaptureParts.NeedsRecording(CaptureParts.Landscape));
        Assert.False(CaptureParts.NeedsRecording(CaptureParts.Entities));
        Assert.Equal(CaptureReads.None, CaptureParts.Reads(new HashSet<string>([CaptureParts.Memory], StringComparer.Ordinal)));

        Assert.True(CaptureMemory.ReadsWorld(CaptureReads.Sweep));
        Assert.False(CaptureMemory.ReadsWorld(CaptureReads.Doodads | CaptureReads.Loaded));
        Assert.Equal("the loaded files, both entity maps with every named entity's model", CaptureMemory.Said(CaptureReads.Loaded | CaptureReads.Doodads));
    }

    /// <summary>The unticked keys come back from the settings file; a key the catalogue no longer has is dropped; an untouched file switches nothing off.</summary>
    [Fact]
    public void THEUNTICKEDPartsAreKeptWithTheSettings()
    {
        Assert.Equal(
            [CaptureParts.Entities, CaptureParts.RawSweep],
            CaptureParts.Kept(new HashSet<string>(["gone-part", CaptureParts.RawSweep, CaptureParts.Entities], StringComparer.Ordinal)));

        string path = Path.Combine(Path.GetTempPath(), $"capture-off-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(OverlaySettingsStore.Save(OverlaySettings.Default with { CaptureOff = [CaptureParts.RawSweep, CaptureParts.GamePicture] }, path));
            Assert.Equal([CaptureParts.RawSweep, CaptureParts.GamePicture], OverlaySettingsStore.Load(path).CaptureOffOrEmpty);
            Assert.Contains("captureOff", File.ReadAllText(path), StringComparison.Ordinal);

            File.WriteAllText(path, """{"minLootRarity":2}""");
            Assert.Empty(OverlaySettingsStore.Load(path).CaptureOffOrEmpty);
            Assert.Empty(OverlaySettings.Default.CaptureOffOrEmpty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A part that runs for hours starts off: an untouched settings file leaves it off, ticking it is kept apart as on (captureOn), and
    /// neither list carries what the other keeps - so the parts on by default still keep only their unticked keys.
    /// </summary>
    [Fact]
    public void APARTOffByDefaultStaysOffUntilTicked()
    {
        Assert.True(CaptureParts.All.Single(part => part.Key == CaptureParts.StageSweep).OffByDefault);
        Assert.Equal([CaptureParts.AoSimulations, CaptureParts.StageSweep], CaptureParts.All.Where(part => part.OffByDefault).Select(part => part.Key));

        // UNTOUCHED: the sweep and the .ao survey off, nothing else.
        IReadOnlySet<string> untouched = CaptureParts.OffOf([], []);
        Assert.True(untouched.SetEquals([CaptureParts.AoSimulations, CaptureParts.StageSweep]), string.Join(", ", untouched));
        Assert.Empty(CaptureParts.Kept(untouched));
        Assert.Empty(CaptureParts.KeptOn(untouched));

        // TICKED, with another part unticked: each list keeps its own.
        IReadOnlySet<string> ticked = CaptureParts.OffOf([CaptureParts.RawSweep], [CaptureParts.StageSweep, "gone-part"]);
        Assert.True(ticked.SetEquals([CaptureParts.RawSweep, CaptureParts.AoSimulations]), string.Join(", ", ticked));
        Assert.Equal([CaptureParts.RawSweep], CaptureParts.Kept(ticked));
        Assert.Equal([CaptureParts.StageSweep], CaptureParts.KeptOn(ticked));

        string path = Path.Combine(Path.GetTempPath(), $"capture-on-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(OverlaySettingsStore.Save(OverlaySettings.Default with { CaptureOn = [CaptureParts.StageSweep] }, path));
            Assert.Equal([CaptureParts.StageSweep], OverlaySettingsStore.Load(path).CaptureOnOrEmpty);
            Assert.Contains("captureOn", File.ReadAllText(path), StringComparison.Ordinal);

            File.WriteAllText(path, """{"minLootRarity":2}""");
            Assert.Empty(OverlaySettingsStore.Load(path).CaptureOnOrEmpty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Every part's file goes to its heading's folder - the rooms' two headings to one, the memory pass's index beside the
    /// recording - and capture.txt stays at the top.
    /// </summary>
    [Fact]
    public void EACHPartsFileGoesToItsHeadingsFolder()
    {
        Assert.Equal("pictures", CaptureParts.FolderOf(CaptureReport.GameShot));
        Assert.Equal("entities", CaptureParts.FolderOf(CaptureReport.EntitiesFile));
        Assert.Equal("entities", CaptureParts.FolderOf(CaptureReport.MonstersFile));
        Assert.Equal("area", CaptureParts.FolderOf(CaptureReport.GroundFile));
        Assert.Equal("rooms", CaptureParts.FolderOf(CaptureReport.TileIdentitiesFile));
        Assert.Equal("rooms", CaptureParts.FolderOf(CaptureReport.DoodadsFile));
        Assert.Equal("rooms", CaptureParts.FolderOf(CaptureReport.LandscapeFile));
        Assert.Equal("windows", CaptureParts.FolderOf(CaptureReport.PanesFile));
        Assert.Equal("mem", CaptureParts.FolderOf(CaptureReport.MemoryFile));
        Assert.Equal("mem", CaptureParts.FolderOf(CaptureMemory.IndexFile));
        Assert.Equal(string.Empty, CaptureParts.FolderOf(CaptureReport.SummaryFile));
        Assert.All(CaptureParts.All.Where(part => part.File.Length > 0), part => Assert.Contains(CaptureParts.FolderOf(part.File), CaptureParts.Folders));
    }

    /// <summary>A capture's file is found at the top, as older captures and the fixtures have it, or in its heading's folder.</summary>
    [Fact]
    public void ACAPTURESFileIsFoundAtTheTopOrInItsFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"capture-find-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string written = CaptureParts.PathOf(root, CaptureReport.GroundFile);
            File.WriteAllText(written, "ground");
            File.WriteAllText(Path.Combine(root, CaptureReport.MastersFile), "masters");

            Assert.Equal(Path.Combine(root, "area", CaptureReport.GroundFile), written);
            Assert.Equal(written, CaptureParts.Find(root, CaptureReport.GroundFile));
            Assert.Equal(Path.Combine(root, CaptureReport.MastersFile), CaptureParts.Find(root, CaptureReport.MastersFile));
            Assert.Equal(Path.Combine(root, CaptureReport.WaterFile), CaptureParts.Find(root, CaptureReport.WaterFile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
