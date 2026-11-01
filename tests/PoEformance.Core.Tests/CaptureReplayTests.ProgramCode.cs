using System.Globalization;
using PoEformance.Features;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The program code part read back from a capture: program-code-replay.txt, the image beside the part (program-code.bin) searched again
/// for the bone simulation's keys and words and what reads them - so a change to the search is held against the game's own program
/// without the game.
/// </summary>
public partial class CaptureReplayTests
{
    [Fact]
    public void THEPROGRAMCodeIsSearchedAgainFromItsImage()
    {
        if (Folder is not { Length: > 0 } folder || CaptureParts.Find(folder, CaptureReport.ProgramCodeFile) is not { } report || !File.Exists(report))
        {
            return;
        }

        string image = Path.Combine(Path.GetDirectoryName(report) ?? folder, ProgramCode.ImageFile);
        if (!File.Exists(image))
        {
            return;
        }

        // THE MODULE'S BASE, as the report's first line says it: what a pointer in the image's data holds.
        string first = File.ReadLines(report).First();
        int at = first.IndexOf("module 0x", StringComparison.Ordinal);
        Assert.True(at >= 0, first);
        string hex = new([.. first[(at + "module 0x".Length)..].TakeWhile(Uri.IsHexDigit)]);
        ulong moduleBase = ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        List<string> lines = ProgramCode.Read(File.ReadAllBytes(image), moduleBase);
        File.WriteAllLines(Path.Combine(folder, "program-code-replay.txt"), lines);
        Assert.Contains(lines, line => line.StartsWith("--- key \"bend_stiffness\"", StringComparison.Ordinal));
    }
}
