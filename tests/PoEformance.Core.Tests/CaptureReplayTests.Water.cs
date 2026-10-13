using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>The water's lookups read again from a capture: water-lookups-replay.txt, what WaterLookups takes of the files written whole beside water-textures.txt.</summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THEWATERLOOKUPSAreReadAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = WaterLookupsReplay(folder);
        File.WriteAllText(Path.Combine(folder, "water-lookups-replay.txt"), replay);
        Assert.StartsWith("water lookups:", replay, StringComparison.Ordinal);
    }

    /// <summary>WaterLookups over the capture's <c>water-lookup-name.dds</c> files, as it reads them out of the install.</summary>
    internal static string WaterLookupsReplay(string folder)
    {
        byte[]? Read(string path)
        {
            string file = CaptureParts.Find(folder, new WaterLookupFile(path, []).File);
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        }

        WaterLookups lookups = WaterLookups.Read(Read);
        return "water lookups: " + lookups;
    }
}
