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

    /// <summary>
    /// A cause names the files it was met on, the ones met in the most rooms first; and the first causes' files are written beside the
    /// part at their install paths with what they name - an object's meshes, a material's graphs - kept to the install's own files.
    /// </summary>
    [Fact]
    public void ACAUSENamesItsFilesAndTheirBytesTravelInTheCapture()
    {
        var subjects = new CaptureStageSweep.Subjects();
        subjects.Add(new() { ["white: Mystery in A.fxgraph"] = new(StringComparer.OrdinalIgnoreCase) { ["Art/a.mat"] = 30 } });
        subjects.Add(new() { ["white: Mystery in A.fxgraph"] = new(StringComparer.OrdinalIgnoreCase) { ["Art/a.mat"] = 10, ["Art/b.mat"] = 99 } });
        subjects.Add(new()
        {
            ["geometry: doodad not drawn - no SkinMesh or FixedMesh anywhere in the .ao files or what they extend (a set, _set.ao)"] =
                new(StringComparer.OrdinalIgnoreCase) { ["Metadata/Rock_set.ao"] = 4 },
        });

        string ranked = CaptureStageSweep.Ranked(Sample, subjects);
        Assert.Contains("white: Mystery in A.fxgraph\tone (A)\n  \t\t\t  of Art/a.mat - 2 rooms, weight 40\n  \t\t\t  of Art/b.mat - 1 rooms, weight 99\n", ranked, StringComparison.Ordinal);

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Art/a.mat"] = System.Text.Encoding.UTF8.GetBytes("""{"graphinstances":[{"parent":"Metadata/Graphs/Mystery.fxgraph"}]}"""),
            ["Metadata/Graphs/Mystery.fxgraph"] = [1, 2, 3],
            ["Metadata/Rock_set.ao"] = [.. System.Text.Encoding.Unicode.GetPreamble(), .. System.Text.Encoding.Unicode.GetBytes("version 2\nAnimatedSet { \"Metadata/Rock_01.ao\" }\n")],
            ["Metadata/Rock_01.ao"] = System.Text.Encoding.Unicode.GetBytes("FixedMesh { \"Art/Rock_01.fmt\" }"),
            ["Art/Rock_01.fmt"] = [9, 9],
        };
        CaptureStageSweep.Row[] rows =
        [
            new("m.tsi", "r.arm", true, 0, 10, new Dictionary<string, long>
            {
                ["white: Mystery in A.fxgraph"] = 40,
                ["geometry: doodad not drawn - no SkinMesh or FixedMesh anywhere in the .ao files or what they extend (a set, _set.ao)"] = 4,
            }),
        ];

        string folder = Path.Combine(Path.GetTempPath(), $"stage-sweep-{Guid.NewGuid():N}");
        string into = Path.Combine(folder, CaptureStageSweep.SampleFolder);
        try
        {
            string said = CaptureStageSweep.Samples(files.GetValueOrDefault, rows, subjects, into);
            Assert.StartsWith("\n=== the subjects' files, under stage-sweep/ at their install paths - 5 written", said, StringComparison.Ordinal);
            Assert.Contains("not in the install\tArt/b.mat", said, StringComparison.Ordinal);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(into, "Metadata", "Graphs", "Mystery.fxgraph")));
            Assert.Equal([9, 9], File.ReadAllBytes(Path.Combine(into, "Art", "Rock_01.fmt")));

            // READ AGAIN OFFLINE: the objects and the material from the files the capture carries.
            string again = CaptureStageSweep.Recheck(folder);
            Assert.StartsWith("stage sweep files read again: 0 of 2 objects built, ", again, StringComparison.Ordinal);
            Assert.Contains("  Metadata/Rock_01.ao\tnot built: ", again, StringComparison.Ordinal);
            Assert.Contains("  Art/a.mat\t", again, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>A file names the install paths in its bytes, as UTF-16 and as single bytes; an install path stays under the folder it is written to.</summary>
    [Fact]
    public void AFILENamesItsPathsAndAPathStaysUnderItsFolder()
    {
        byte[] wide = System.Text.Encoding.Unicode.GetBytes("TileMeshRoot \"Art/Models/Terrain/Slope\" GroundMask \"Art/Textures/mask.dds\"");
        Assert.Equal(["Art/Textures/mask.dds"], CaptureStageSweep.Named(wide, "Art/x.tgt"));
        byte[] narrow = [.. System.Text.Encoding.ASCII.GetBytes("xx Art/Models/a.smd\0Art/Models/b.mat:2\0"), 0xFF];
        Assert.Equal(["Art/Models/a.smd", "Art/Models/b.mat"], CaptureStageSweep.Named(narrow, "Art/Models/self.sm"));

        Assert.Equal(Path.Combine("Art", "x.ao"), CaptureStageSweep.Safe("Art/../x.ao"));
        Assert.Equal(Path.Combine("Art", "a_b.mat"), CaptureStageSweep.Safe("/Art/a:b.mat"));
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
