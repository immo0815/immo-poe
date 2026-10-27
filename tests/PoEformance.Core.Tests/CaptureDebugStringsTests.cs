using System.Buffers.Binary;
using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's debug strings part: the program's strings found ASCII and UTF-16 at either alignment with their section and address,
/// the tables' and the paths' beside them, grouped, and read back and sorted by shape offline.
/// </summary>
public sealed class CaptureDebugStringsTests
{
    private const string Monster = "Metadata/Monsters/DebugMonster";
    private const string Path0 = "Art/Textures/__TestTextures/DEBUG/DBG_Blue.mat";

    [Fact]
    public void THEPROGRAMSStringsAreFoundWithTheirSectionsAndTheTablesAndPathsBesideThem()
    {
        string program = Path.Combine(Path.GetTempPath(), $"poef-debug-program-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(program, Program());
        try
        {
            string text = CaptureDebugStrings.Write(Install, [program], ["Data/A.datc64", "Data/B.datc64", "Data/C.datc64"], [Path0]).ReplaceLineEndings("\n");

            Assert.StartsWith(
                $"debug strings: 6 strings in the program, 1 in 2 of 3 data tables and 1 install paths hold one of {CaptureDebugStrings.Words.Count} ",
                text, StringComparison.Ordinal);
            Assert.Contains("image base 0x140000000", text, StringComparison.Ordinal);

            IReadOnlyList<CaptureDebugStrings.Entry> entries = CaptureDebugStrings.Read(text);
            Assert.Equal(8, entries.Count);

            // ASCII TWICE IS ONE STRING TWICE, at its first place; UTF-16 at an odd offset found; the three-letter word alone left out.
            CaptureDebugStrings.Entry draw = Assert.Single(entries, entry => entry.Text == "DebugDrawNavMesh");
            Assert.Equal((CaptureDebugStrings.ProgramKind, "debug", false, ".rdata:0x2000", 2), (draw.Kind, draw.Group, draw.Wide, draw.Where, draw.Times));
            CaptureDebugStrings.Entry wire = Assert.Single(entries, entry => entry.Text == "r_wireframe");
            Assert.Equal(("console, visualisation", true, ".rdata:0x2101"), (wire.Group, wire.Wide, wire.Where));
            Assert.DoesNotContain(entries, entry => entry.Text == "dbg");

            // A STRING GOES UNDER THE FIRST GROUP of its words, all of them beside it.
            CaptureDebugStrings.Entry command = Assert.Single(entries, entry => entry.Text == "/toggle_fps");
            Assert.Equal("console, visualisation", command.Group);
            Assert.Equal(["toggle", "fps"], command.Holds);
            Assert.Equal("asserts, logging", Assert.Single(entries, entry => entry.Text == "assert failed: %s").Group);
            Assert.StartsWith(".text*:0x", Assert.Single(entries, entry => entry.Text == "xxdbgxx").Where, StringComparison.Ordinal);

            CaptureDebugStrings.Entry table = Assert.Single(entries, entry => entry.Kind == CaptureDebugStrings.TableKind);
            Assert.Equal((Monster, true, "Data/A.datc64", 2, 2), (table.Text, table.Wide, table.Where, table.Times, table.Places));
            Assert.Equal(Path0, Assert.Single(entries, entry => entry.Kind == CaptureDebugStrings.PathKind).Text);

            // THE OFFLINE READER sorts by shape, the type names demangled and the executable section's chance spelling apart.
            string report = CaptureDebugStrings.Report(entries).ReplaceLineEndings("\n");
            Assert.StartsWith("debug strings replay: 8 strings, 1 of them in the program's executable sections", report, StringComparison.Ordinal);
            Assert.Contains("=== program strings, debug, C++ type names: 1\n  Render::DebugRenderer\n", report, StringComparison.Ordinal);
            Assert.Contains("=== program strings, console, visualisation, chat commands: 1\n  /toggle_fps\n", report, StringComparison.Ordinal);
            Assert.DoesNotContain("xxdbgxx", report, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(program);
        }
    }

    [Fact]
    public void AWORDAcrossAChunksEdgeIsFoundOnce()
    {
        // The ASCII search widens a megabyte at a time; a word straddling the edge belongs to the chunk it starts in.
        var bytes = new byte[(1 << 20) + 64];
        byte[] word = Encoding.ASCII.GetBytes("ShowDebugGrid");
        word.CopyTo(bytes, (1 << 20) - 7);
        var found = new Dictionary<(string, bool), CaptureDebugStrings.Tally>();
        CaptureDebugStrings.Narrow(bytes, found, "edge");

        (string text, bool wide) = Assert.Single(found.Keys);
        Assert.Equal(("ShowDebugGrid", false), (text, wide));
        Assert.Equal((1 << 20) - 7, found[(text, wide)].First);
    }

    [Theory]
    [InlineData(".?AVDebugRenderer@Render@@", "Render::DebugRenderer")]
    [InlineData(".?AUDebugLine@@", "DebugLine")]
    [InlineData(".?AW4DebugMode@Game@@", "Game::DebugMode")]
    [InlineData(".?AV?$DebugList@H@@", ".?AV?$DebugList@H@@")]
    public void ATYPEDescriptorIsDemangled(string text, string expected) => Assert.Equal(expected, CaptureDebugStrings.Demangled(text));

    /// <summary>Two tables naming the debug monster in UTF-16, one naming nothing.</summary>
    private static byte[]? Install(string path) => path switch
    {
        "Data/A.datc64" or "Data/B.datc64" => [0xBB, 0xBB, .. Encoding.Unicode.GetBytes(Monster), 0, 0],
        "Data/C.datc64" => Encoding.Unicode.GetBytes("Metadata/Monsters/Rhoa"),
        _ => null,
    };

    /// <summary>A program of two sections, code at 0x400 and read-only data at 0x600, as a PE header names them.</summary>
    private static byte[] Program()
    {
        var bytes = new byte[0xA00];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C), 0x40);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(0x40));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x46), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x54), 0xF0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x58), 0x20B);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x58 + 24), 0x140000000);
        Section(bytes, 0x58 + 0xF0, ".text", 0x1000, 0x200, 0x400, 0x60000020);
        Section(bytes, 0x58 + 0xF0 + 40, ".rdata", 0x2000, 0x400, 0x600, 0x40000040);

        Ascii(bytes, 0x480, "xxdbgxx");
        Ascii(bytes, 0x600, "DebugDrawNavMesh");
        Ascii(bytes, 0x620, ".?AVDebugRenderer@Render@@");
        Ascii(bytes, 0x660, "/toggle_fps");
        Ascii(bytes, 0x680, "assert failed: %s");
        Ascii(bytes, 0x6A0, "DebugDrawNavMesh");
        Ascii(bytes, 0x6C0, "dbg");
        Encoding.Unicode.GetBytes("r_wireframe").CopyTo(bytes, 0x701);
        return bytes;
    }

    private static void Section(byte[] bytes, int at, string name, uint virtualAt, uint size, uint rawAt, uint flags)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, at);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 8), size);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 12), virtualAt);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 16), size);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 20), rawAt);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 36), flags);
    }

    private static void Ascii(byte[] bytes, int at, string text) => Encoding.ASCII.GetBytes(text).CopyTo(bytes, at);
}
