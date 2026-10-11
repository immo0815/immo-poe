using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's water part: every environment's water keys counted with their values, and the water pass's sources whole.
/// </summary>
public sealed class CaptureWaterTests
{
    private const string Port = "Metadata/EnvironmentSettings/Maps/Port.env";
    private const string Lake = "Metadata/EnvironmentSettings/Maps/Lake.env";
    private const string Dry = "Metadata/EnvironmentSettings/Maps/Dry.env";
    private const string Broken = "Metadata/EnvironmentSettings/Maps/Broken.env";
    private const string Common = "shaders/renderer/common.ffx";
    private const string Water = "shaders/renderer/water.ffx";
    private const string Other = "shaders/renderer/other.ffx";

    [Fact]
    public void EVERYEnvironmentsWaterKeysAreCountedAndTheWaterSourcesWrittenWhole()
    {
        string text = CaptureWater.Write(Install, [Port, Lake, Dry, Broken, "missing.env"], [Common, Water, Other]).ReplaceLineEndings("\n");

        Assert.StartsWith("2 of 5 environments carry a water section, 3 keys; 2 of 3 shader sources draw the water\n", text, StringComparison.Ordinal);
        Assert.Contains("2 of 5 environments carry a water section, 3 keys, 1 did not read as JSON", text, StringComparison.Ordinal);

        // BOTH FILES' VALUES, the first with the file that wrote it; a key only one writes counted once.
        Assert.Contains("  water.height_offset  in 2 files: -15.0 (" + Port + ")  |  0 (" + Lake + ")", text, StringComparison.Ordinal);
        Assert.Contains("  water.colour  in 1 files: [0.1, 0.2, 0.3] (" + Lake + ")", text, StringComparison.Ordinal);
        Assert.Contains("  water.flow_intensity  in 1 files: 1.5 (" + Port + ")", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dust_color", text, StringComparison.Ordinal);

        Assert.Contains("##### " + Common + "\n", text, StringComparison.Ordinal);
        Assert.Contains("  " + Water + "  (WaterProps, InitWaterProps, BuildWaterRefraction)", text, StringComparison.Ordinal);
        Assert.Contains("void InitWaterProps(out WaterProps props)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("##### " + Other, text, StringComparison.Ordinal);
    }

    [Fact]
    public void BEFORETheInstallWalkHasRunItSaysSo()
    {
        string text = CaptureWater.Write(Install, [], null);

        Assert.StartsWith("no environments to read; no shader sources to search", text, StringComparison.Ordinal);
    }

    private static byte[]? Install(string path) => path switch
    {
        Port => CaptureGroundTests.Text("{\"area\": {\"dust_color\": [0.3, 0.3, 0.2]}, \"water\": {\"flow_intensity\": 1.5, \"height_offset\": -15.0}}"),
        Lake => CaptureGroundTests.Text("{\"water\": {\"height_offset\": 0, \"colour\": [0.1, 0.2, 0.3]},}"),
        Dry => CaptureGroundTests.Text("{\"area\": {\"wind_intensity\": 60.0}}"),
        Broken => CaptureGroundTests.Text("{\"water\": "),
        Common => CaptureGroundTests.Text("#define PASS_TYPE_WATER 4\nuniform pass float4 water_color_open\n"),
        Water => CaptureGroundTests.Text("void InitWaterProps(out WaterProps props)\n{\n}\nfloat4 BuildWaterRefraction() { return 0; }\n"),
        Other => CaptureGroundTests.Text("float4 Unrelated() { return 0; }\n"),
        _ => null,
    };
}
