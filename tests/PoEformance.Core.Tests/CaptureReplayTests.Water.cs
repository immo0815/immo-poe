using System.Text;
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

    [Fact]
    public void THEWATERSPassTexturesAreMeasuredAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = WaterPassReplay(folder);
        File.WriteAllText(Path.Combine(folder, "water-pass-replay.txt"), replay);
        Assert.StartsWith("water pass textures:", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pass textures' candidates written whole beside water-textures.txt (<c>water-pass-N-name.dds</c>), measured again as
    /// WaterPassTextures measures them - a caustics' candidate by its name, the rest as the distortion.
    /// </summary>
    internal static string WaterPassReplay(string folder)
    {
        string[] files = [.. Directory.GetFiles(folder, "water-pass-*.dds", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        var said = new StringBuilder("water pass textures: ").Append(files.Length).AppendLine(" candidates written whole");
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            bool distortion = !name.Contains("caustic", StringComparison.OrdinalIgnoreCase);
            said.AppendLine().Append(distortion ? "distortion? " : "caustics? ").AppendLine(name);
            if (GameArt.Decode(File.ReadAllBytes(file)) is { } picture)
            {
                foreach (string line in WaterPassTextures.Lines(picture, distortion))
                {
                    said.AppendLine(line);
                }
            }
            else
            {
                said.AppendLine("  not decoded");
            }
        }

        return said.ToString();
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
