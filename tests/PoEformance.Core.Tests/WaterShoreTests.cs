using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>The water shore part's search: the files naming the water's blocking or the ocean's markup, the types reaching them, and its file read back.</summary>
public class WaterShoreTests
{
    /// <summary>An install of a few files, as the game writes them: scripts UTF-16 with their mark, a tile and a table binary.</summary>
    private static readonly Dictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Metadata/Terrain/Objects/Blocker.ot"] = Wide("version 2\nextends \"Metadata/Parent\"\n\nBaseEvents\n{\n\ton_construction_complete = \"SetWaterBlocking( true );\"\n}\n"),
        ["Metadata/Terrain/Objects/Boat.ot"] = Wide("version 2\nextends \"Metadata/Terrain/Objects/Blocker\"\n"),
        ["Metadata/Terrain/Objects/Rock.ot"] = Wide("version 2\nextends \"nothing\"\n"),
        ["Metadata/Terrain/Objects/River.otc"] = Wide("version 2\nClientEvents\n{\n\ton_construction_complete = \"SetWaterFlow( 1, 0 );\"\n}\n"),
        ["Metadata/Terrain/Island/island_ocean_deepwater.gt"] = Wide("IslandOceanDeepWater\n1 1 0 0 1\n"),

        // A BINARY TILE naming the deep water's ground type in its string table, at an odd place.
        ["Metadata/Terrain/Island/Ocean_01.tdt"] = [0x01, 0x00, 0x07, .. Encoding.Unicode.GetBytes("Metadata/Terrain/Island/island_ocean_deepwater.gt"), 0x00, 0x00, 0xFF],
        ["Data/WaterTypes.datc64"] = [0x05, 0, 0, 0, 0xBB, 0xBB, 0xBB, 0xBB, .. Encoding.Unicode.GetBytes("ShoreDeep"), 0, 0],
    };

    private static byte[] Wide(string text) => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)];

    private static byte[]? Read(string path) => Files.GetValueOrDefault(path);

    [Fact]
    public void THEFILESNamingTheWatersBlockingOrTheOceansMarkupAreFound()
    {
        string[] files = [.. Files.Keys.Where(path => !path.StartsWith("Data/", StringComparison.Ordinal)), "Metadata/Terrain/Objects/Missing.ot"];
        string text = CaptureWaterShore.Write(
            Read, files, ["Data/WaterTypes.datc64"], ["Metadata/Terrain/Objects/Boat", "Metadata/Terrain/Objects/Rock.ot", "Metadata/Terrain/Island/island_ocean_deepwater.gt", "Art/x.dds"], null, null);
        Assert.StartsWith("water shore: 4 of 7 files and 1 of 1 data tables name", text, StringComparison.Ordinal);

        // THE BOAT EXTENDS THE BLOCKER, and the rock reaches nothing.
        Assert.Contains("1 of the area's 2 loaded object types reach one", text, StringComparison.Ordinal);
        Assert.Contains("Metadata/Terrain/Objects/Boat  ->  Metadata/Terrain/Objects/Blocker.ot", text, StringComparison.Ordinal);

        // THE AREA'S GROUND TYPE WHOLE, and the table's string round the word.
        Assert.Contains("  | 1 1 0 0 1", text, StringComparison.Ordinal);
        Assert.Contains("Data/WaterTypes.datc64  |  @0x8 utf-16: ShoreDeep", text, StringComparison.Ordinal);

        IReadOnlyList<CaptureWaterShore.Call> calls = CaptureWaterShore.Calls(text);
        Assert.Equal(
            [
                new CaptureWaterShore.Call("Metadata/Terrain/Island/island_ocean_deepwater.gt", "IslandOceanDeepWater"),
                new CaptureWaterShore.Call("Metadata/Terrain/Island/Ocean_01.tdt", "@0x3 utf-16: Metadata/Terrain/Island/island_ocean_deepwater.gt"),
                new CaptureWaterShore.Call("Metadata/Terrain/Objects/Blocker.ot", "on_construction_complete = \"SetWaterBlocking( true );\""),
                new CaptureWaterShore.Call("Metadata/Terrain/Objects/River.otc", "on_construction_complete = \"SetWaterFlow( 1, 0 );\""),
            ],
            calls);

        string report = CaptureWaterShore.Report(calls);
        Assert.StartsWith("water shore replay: 4 lines in 4 files", report, StringComparison.Ordinal);
        Assert.Contains("=== SetWaterBlocking: 1 lines", report, StringComparison.Ordinal);
        Assert.Contains("=== deepwater: 2 lines", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The spray's chain as the install has it: a graph firing on the crashing waves' trigger, the update material naming it, the GPU
    /// particle naming that material, the animated object carrying the particle - and a script nothing names.
    /// </summary>
    private static readonly Dictionary<string, byte[]> Spray = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Metadata/Effects/Graphs/Water/CrashingTrigger.fxgraph"] = Encoding.UTF8.GetBytes("{\n  \"nodes\": [\n    { \"type\": \"CrashingSprayTrigger\" }\n  ]\n}\n"),
        ["Metadata/Effects/Water/mat/FoamSpray_U.mat"] = Encoding.UTF8.GetBytes("{\"graphinstances\":[{\"parent\":\"Metadata/Effects/Graphs/Water/CrashingTrigger.fxgraph\"}]}"),
        ["Metadata/Effects/Water/fx/Splashes.pet"] = Encoding.UTF8.GetBytes("{\n  \"update_material\": \"Metadata/Effects/Water/mat/FoamSpray_U.mat\"\n}\n"),
        ["Metadata/Effects/Water/WaterSplash.ao"] = Wide("version 2\nAnimatedObject\n{\n\tparticles = \"Metadata/Effects/Water/fx/Splashes.pet\"\n}\n"),
        ["Metadata/Terrain/Objects/Rock.ot"] = Wide("version 2\nextends \"nothing\"\n"),
    };

    [Fact]
    public void THESPRAYSChainIsFollowedFromItsGraphToWhatCarriesIt()
    {
        string text = CaptureWaterShore.Write(path => Spray.GetValueOrDefault(path), [.. Spray.Keys], null, null, null, null);
        Assert.Contains("1 spray trigger graphs and 1 / 1 / 1 files hop by hop naming them", text, StringComparison.Ordinal);
        Assert.Contains("##### Metadata/Effects/Graphs/Water/CrashingTrigger.fxgraph", text, StringComparison.Ordinal);
        Assert.Contains("Metadata/Effects/Water/fx/Splashes.pet  ->  \"update_material\": \"Metadata/Effects/Water/mat/FoamSpray_U.mat\"", text, StringComparison.Ordinal);
        Assert.Contains("Metadata/Effects/Water/WaterSplash.ao  ->  particles = \"Metadata/Effects/Water/fx/Splashes.pet\"", text, StringComparison.Ordinal);

        // THE CALLS STAY THE SHORE'S LIST: the graph's line among them, the chain's - found by name alone - not; the material and the
        // object are never searched for the words.
        Assert.Equal(
            [new CaptureWaterShore.Call("Metadata/Effects/Graphs/Water/CrashingTrigger.fxgraph", "{ \"type\": \"CrashingSprayTrigger\" }")],
            CaptureWaterShore.Calls(text));
    }

    [Fact]
    public void ASTOPPEDSearchSaysSo()
    {
        var search = new CaptureWaterShore.Search();
        search.Stop();
        string text = CaptureWaterShore.Write(Read, [.. Files.Keys], null, null, null, null, search);
        Assert.Contains("STOPPED after 0 of ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NOFILESSaySo()
    {
        string text = CaptureWaterShore.Write(Read, null, null, null, null, null);
        Assert.StartsWith("water shore: 0 of 0 files and 0 of 0 data tables", text, StringComparison.Ordinal);
        Assert.Contains("(no files: the install walk found none", text, StringComparison.Ordinal);
        Assert.Empty(CaptureWaterShore.Calls(text));
    }
}
