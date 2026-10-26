using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>The water shore part's search: the object scripts naming the water's blocking, the types reaching them, and its file read back.</summary>
public class WaterShoreTests
{
    /// <summary>An install of three scripts, as the game writes them: UTF-16 with its mark.</summary>
    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Metadata/Terrain/Objects/Blocker.ot"] = "version 2\nextends \"Metadata/Parent\"\n\nBaseEvents\n{\n\ton_construction_complete = \"SetWaterBlocking( true );\"\n}\n",
        ["Metadata/Terrain/Objects/Boat.ot"] = "version 2\nextends \"Metadata/Terrain/Objects/Blocker\"\n",
        ["Metadata/Terrain/Objects/Rock.ot"] = "version 2\nextends \"nothing\"\n",
        ["Metadata/Terrain/Objects/River.otc"] = "version 2\nClientEvents\n{\n\ton_construction_complete = \"SetWaterFlow( 1, 0 );\"\n}\n",
    };

    private static byte[]? Read(string path) =>
        Files.TryGetValue(path, out string? text) ? [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)] : null;

    [Fact]
    public void THESCRIPTSNamingTheWatersBlockingAreFoundWithTheTypesReachingThem()
    {
        string text = CaptureWaterShore.Write(Read, [.. Files.Keys, "Metadata/Terrain/Objects/Missing.ot"], ["Metadata/Terrain/Objects/Boat", "Metadata/Terrain/Objects/Rock.ot", "Art/x.dds"], null, null);
        Assert.StartsWith("water shore: 2 of 5 object scripts name the water's blocking or flow", text, StringComparison.Ordinal);

        // THE BOAT EXTENDS THE BLOCKER, and the rock reaches nothing.
        Assert.Contains("1 of the area's 2 loaded object types reach one", text, StringComparison.Ordinal);
        Assert.Contains("Metadata/Terrain/Objects/Boat  ->  Metadata/Terrain/Objects/Blocker.ot", text, StringComparison.Ordinal);

        IReadOnlyList<CaptureWaterShore.Call> calls = CaptureWaterShore.Calls(text);
        Assert.Equal(
            [
                new CaptureWaterShore.Call("Metadata/Terrain/Objects/Blocker.ot", "on_construction_complete = \"SetWaterBlocking( true );\""),
                new CaptureWaterShore.Call("Metadata/Terrain/Objects/River.otc", "on_construction_complete = \"SetWaterFlow( 1, 0 );\""),
            ],
            calls);

        string report = CaptureWaterShore.Report(calls);
        Assert.StartsWith("water shore replay: 2 lines in 2 scripts", report, StringComparison.Ordinal);
        Assert.Contains("=== SetWaterBlocking: 1 lines", report, StringComparison.Ordinal);
    }

    [Fact]
    public void NOSCRIPTSSaySo()
    {
        string text = CaptureWaterShore.Write(Read, null, null, null, null);
        Assert.StartsWith("water shore: 0 of 0 object scripts", text, StringComparison.Ordinal);
        Assert.Contains("(no scripts: the install walk found none", text, StringComparison.Ordinal);
        Assert.Empty(CaptureWaterShore.Calls(text));
    }
}
