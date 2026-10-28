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

    [Fact]
    public void THESHOREFieldsNumbersAreReadAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = ShoreFieldReplay(folder);
        File.WriteAllText(Path.Combine(folder, "shore-field-replay.txt"), replay);
        Assert.StartsWith("shore fields:", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each shore field the capture wrote as numbers (<c>shore-field.bin</c>, v0.1.333) read back: its frame, how many cells of each kind,
    /// and each way in's spread - what a video of the game read into the room's frame is set beside.
    /// </summary>
    internal static string ShoreFieldReplay(string folder)
    {
        string[] files = [.. Directory.GetFiles(folder, "*-field.bin", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        var said = new StringBuilder("shore fields: ").Append(files.Length).AppendLine(" written as numbers");
        foreach (string file in files)
        {
            said.AppendLine().Append(Path.GetFileName(file)).Append(": ");
            if (CapturePanes.ReadField(File.ReadAllBytes(file)) is not { } field)
            {
                said.AppendLine("not a field's numbers");
                continue;
            }

            said.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{field.Width} x {field.Height} cells of {field.Cell:0.#} from ({field.Left:0}, {field.Top:0}), the water at {field.Level:0.#}"));
            foreach ((string name, byte bit) in (ReadOnlySpan<(string, byte)>)[("land", ShoreField.LandBit), ("ground", ShoreField.GroundBit),
                ("standing", ShoreField.StandingBit), ("wall", ShoreField.WallBit), ("shore", ShoreField.ShoreBit), ("in the way round", ShoreField.BlockedBit),
                ("ground the way past the walls counts", ShoreField.SteersBit)])
            {
                said.Append("  ").Append(name).Append(' ').Append(field.Kinds.Count(kind => (kind & bit) != 0)).AppendLine(" cells");
            }

            foreach ((string name, float[] values) in (ReadOnlySpan<(string, float[])>)[("shore", field.Shore), ("way in by the ground", field.ByGround),
                ("way in round what stands", field.Round), ("way in past the walls", field.PastWalls), ("ground's shore", field.GroundShore)])
            {
                said.Append(System.Globalization.CultureInfo.InvariantCulture, $"  {name} {values.Min():0} to {values.Max():0}").AppendLine();
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
