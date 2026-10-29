using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The stage sweep's text: its rows read back as they were written, the causes ranked by the rooms they reach whichever reads them, its
/// reasons kept to their kind, and a room that does not build counted rather than thrown.
/// </summary>
public class CaptureStageSweepTests
{
    private static readonly CaptureStageSweep.Row[] Sample =
    [
        new("Metadata/Terrain/A/master.tsi", "Metadata/Terrain/A/Rooms/one.arm", true, 1.25, 1000, new Dictionary<string, long>
        {
            ["white: Mystery in A.fxgraph"] = 40,
            ["ground: left plain - no group for the ground"] = 6,
            ["geometry: doodad not drawn - no mesh (a set, _set.ao)"] = 3,
        }),
        new("Metadata/Terrain/A/master.tsi", "Metadata/Terrain/A/Rooms/two.arm", true, 0.5, 500, new Dictionary<string, long>
        {
            ["white: Mystery in B.fxgraph"] = 10,
            ["lost: alpha: Mystery in C.fxgraph"] = 500,
        }),
        new("Metadata/Terrain/B/master.tsi", "Metadata/Terrain/B/Rooms/one.arm", false, 0.01, 0, new Dictionary<string, long>
        {
            ["room: did not build - no tiles"] = 1,
        }),
    ];

    /// <summary>The rows come back from the text whole - master, room, built, seconds, triangles and every cause with its weight.</summary>
    [Fact]
    public void THEROWSAreReadBackAsTheyWereWritten()
    {
        IReadOnlyList<CaptureStageSweep.Row> back = CaptureStageSweep.RowsOf("stage sweep: header line\n" + CaptureStageSweep.Ranked(Sample) + CaptureStageSweep.Rows(Sample));

        Assert.Equal(Sample.Length, back.Count);
        foreach (CaptureStageSweep.Row row in Sample)
        {
            CaptureStageSweep.Row read = Assert.Single(back, one => one.Master == row.Master && one.Room == row.Room);
            Assert.Equal((row.Built, row.Seconds, row.Triangles), (read.Built, read.Seconds, read.Triangles));
            Assert.Equal(row.Causes.OrderBy(one => one.Key, StringComparer.Ordinal), read.Causes.OrderBy(one => one.Key, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// A cause is ranked under its group by the rooms it reaches; the compile's reasons are ranked again by node alone, one node over
    /// many graphs counting once a room; and the offline reader ranks what the sweep ranked.
    /// </summary>
    [Fact]
    public void THECAUSESRankByTheRoomsTheyReach()
    {
        string ranked = CaptureStageSweep.Ranked(Sample);

        Assert.StartsWith("rooms 3, built 2, triangles drawn 1,500, of them white 50 (3.33%); rooms with a white surface 2, with ground left plain 1, with a doodad not drawn 1", ranked, StringComparison.Ordinal);
        Assert.Contains("=== rooms that did not build - 1 causes", ranked, StringComparison.Ordinal);
        Assert.Contains("  1\t1\t1\troom: did not build - no tiles\tone (B)\n", ranked, StringComparison.Ordinal);
        Assert.Contains("  1\t1\t40\twhite: Mystery in A.fxgraph\tone (A)\n", ranked, StringComparison.Ordinal);
        Assert.Contains("  1\t1\t500\tlost: alpha: Mystery in C.fxgraph\ttwo (A)\n", ranked, StringComparison.Ordinal);

        // BY NODE: Mystery in three graphs over two rooms - two rooms, every weight summed.
        Assert.Contains("=== the compile's reasons by node alone - 1 nodes, by the rooms they reach\n  rooms\tweight\tnode\n  2\t550\tMystery\n", ranked, StringComparison.Ordinal);

        string replay = CaptureStageSweep.Report("stage sweep: header\n" + ranked + CaptureStageSweep.Rows(Sample));
        Assert.Equal("stage sweep replay: 3 rows read back\n" + ranked, replay);
    }

    /// <summary>A reason's paths are kept to their kind and its counts to a hash, so one reason over many rooms ranks as one.</summary>
    [Fact]
    public void AREASONSPathsAndCountsAreKeptToTheirKind()
    {
        Assert.Equal(
            "Unknown in *.fxgraph: # of # textures from *.dds",
            CaptureStageSweep.Normal("Unknown in Metadata/Effects/Graphs/foo_bar.fxgraph: 3 of 12 textures from Art/Textures/x/y_z.dds"));
        Assert.Equal("a shape of *.mat", CaptureStageSweep.Normal("a shape of Art/Textures/Thing.mat:2"));
    }

    /// <summary>A room whose files are not there is a row that says it did not build, and a sweep with no room set says how many masters named none.</summary>
    [Fact]
    public void AROOMThatDoesNotBuildIsCountedNotThrown()
    {
        CaptureStageSweep.Row row = CaptureStageSweep.Built(_ => null, new MonsterModels.Paints { Lean = true }, "Metadata/Terrain/A/master.tsi", "Metadata/Terrain/A/Rooms/one.arm");
        Assert.False(row.Built);
        Assert.StartsWith("room: did not build - ", Assert.Single(row.Causes).Key, StringComparison.Ordinal);

        var search = new CaptureStageSweep.Search();
        string text = CaptureStageSweep.Write(_ => null, ["Metadata/Terrain/A/master.tsi"], search);
        Assert.StartsWith("stage sweep: 0 of 0 rooms built under 0 masters in ", text, StringComparison.Ordinal);
        Assert.Contains("1 masters in the install, 1 naming no room", text, StringComparison.Ordinal);
        Assert.Equal((0, 0), (search.Done, search.Of));
    }
}
