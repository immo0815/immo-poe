using System.IO.Compression;
using PoEformance.Core.Schema;
using PoEformance.Features;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// Where an area's light comes from - its WorldAreas row names an Environments row, which names the .env - and the zip a capture is sent in.
/// </summary>
public class AreaEnvironmentTests
{
    /// <summary>
    /// The snapshot's area between two areas is a DEFAULT AreaInfo, which skips the parameter's default: its environment came out null and
    /// the light panel's length read ended the tool (v0.1.452, leaving Plantation for the hideout with the Monster Book open).
    /// </summary>
    [Fact]
    public void ADEFAULTAreaHasAnEmptyEnvironmentNotANullOne()
    {
        Assert.Equal(string.Empty, default(AreaInfo).Environment);
        Assert.Equal(string.Empty, new AreaInfo("G1_2", "The Riverbank", 1, false, false).Environment);
        Assert.Equal("Metadata/Environment/a.env", (default(AreaInfo) with { Environment = "Metadata/Environment/a.env" }).Environment);
        Assert.Equal(new AreaInfo("x", "y", 1, false, false, "e"), new AreaInfo("x", "y", 1, false, false, "e"));
    }

    private const ulong WorldData = 0x10_0000;
    private const ulong Details = 0x20_0000;
    private const ulong Row = 0x30_0000;
    private const ulong EnvironmentRow = 0x40_00B3; // rows of a 0x75-byte table sit at odd addresses, as the live one did
    private const ulong IdText = 0x50_0000;
    private const ulong FileText = 0x60_0000;
    private const ulong OtherText = 0x70_0000;

    /// <summary>A string in a page of its own, as a table's string block is - the reader asks for a whole window and halves it only where memory ends.</summary>
    private static byte[] Text(string text)
    {
        var bytes = new byte[0x1000];
        System.Text.Encoding.Unicode.GetBytes(text, bytes);
        return bytes;
    }

    private static (FakeMemoryReader Fake, OffsetSchema Schema) Area()
    {
        OffsetSchema schema = RealSessionTests.LiveSchema();
        StructDef row = schema.Structs["WorldAreaDat"];
        var fake = new FakeMemoryReader()
            .Place(WorldData + (ulong)schema.Structs["WorldData"].OffsetOf("WorldAreaDetailsPtr"), Details)
            .Place(Details + (ulong)schema.Structs["WorldAreaDetails"].OffsetOf("RowPtr"), Row)
            .Place(Row + (ulong)row.OffsetOf("IdPtr"), IdText)
            .Place(IdText, Text("MapSeepage"))
            .Place(Row + (ulong)row.OffsetOf("NamePtr"), 0UL)
            .Place(Row + (ulong)row.OffsetOf("Act"), 10)
            .Place(Row + (ulong)row.OffsetOf("IsTown"), (byte)0)
            .Place(Row + (ulong)row.OffsetOf("EnvironmentRef"), EnvironmentRow)
            .Place(EnvironmentRow + (ulong)schema.Structs["EnvironmentsDat"].OffsetOf("BaseEnvFilePtr"), FileText)
            .Place(FileText, Text("Metadata/EnvironmentSettings/Maps/Seepage.env"))
            .Place(OtherText, Text("Metadata/EnvironmentSettings/other.env"));
        return (fake, schema);
    }

    /// <summary>The area's row leads to its .env, and the row's environment is read once - a second frame on the same row asks nothing.</summary>
    [Fact]
    public void ANAREASRowNamesItsEnvironmentReadOncePerRow()
    {
        (FakeMemoryReader fake, OffsetSchema schema) = Area();
        var areas = new WorldAreaReader(fake, schema);

        AreaInfo first = areas.Read(WorldData);
        Assert.Equal("MapSeepage", first.Id);
        Assert.Equal("Metadata/EnvironmentSettings/Maps/Seepage.env", first.Environment);

        fake.Place(EnvironmentRow + (ulong)schema.Structs["EnvironmentsDat"].OffsetOf("BaseEnvFilePtr"), OtherText);
        Assert.Equal("Metadata/EnvironmentSettings/Maps/Seepage.env", areas.Read(WorldData).Environment);
    }

    /// <summary>A row whose environment does not resolve still reads as the area, with no environment.</summary>
    [Fact]
    public void ANAREAWithoutAnEnvironmentIsStillTheArea()
    {
        (FakeMemoryReader fake, OffsetSchema schema) = Area();
        fake.Place(Row + (ulong)schema.Structs["WorldAreaDat"].OffsetOf("EnvironmentRef"), 0UL);

        AreaInfo area = new WorldAreaReader(fake, schema).Read(WorldData);
        Assert.Equal("MapSeepage", area.Id);
        Assert.Equal(string.Empty, area.Environment);
    }

    /// <summary>The player's level is one byte: what sits in the next three no longer turns level 96 into 16777312.</summary>
    [Fact]
    public void THEPLAYERSLevelIsOneByte()
    {
        StructDef player = RealSessionTests.LiveSchema().Structs["Player"];
        Assert.Equal(FieldType.U8, player.Field("Level")!.Type);
        Assert.Equal(0x204, player.OffsetOf("Level"));
    }

    /// <summary>A capture's folder is packed beside it under its own name, a heading's folder a folder in the zip and capture.txt at the top first, the pictures and the recording stored as they are, the text compressed.</summary>
    [Fact]
    public void ACAPTUREIsPackedBesideItsFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"capture-pack-{Guid.NewGuid():N}");
        string folder = Path.Combine(root, "2026-10-08 15-58-09 Seepage");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(CaptureParts.PathOf(folder, "game.png"), new byte[4096]);
            File.WriteAllBytes(CaptureParts.PathOf(folder, "memory.rec"), new byte[4096]);
            File.WriteAllText(CaptureParts.PathOf(folder, "loaded-files.txt"), string.Concat(Enumerable.Repeat("Metadata/Terrain/Maps/Seepage/a.tdt\n", 200)));
            File.WriteAllText(CaptureParts.PathOf(folder, "capture.txt"), "capture  the summary\n");

            (IReadOnlyList<string> files, List<string> leftOut) = CaptureReport.Pack(folder);

            string zip = Assert.Single(files);
            Assert.Equal(folder + ".zip", zip);
            Assert.False(File.Exists(zip + ".001"));
            Assert.Empty(leftOut);
            using ZipArchive archive = ZipFile.OpenRead(zip);
            Assert.Equal(["capture.txt", "area/loaded-files.txt", "mem/memory.rec", "pictures/game.png"], archive.Entries.Select(one => one.FullName));
            Assert.Equal(4096, archive.GetEntry("pictures/game.png")!.CompressedLength);
            Assert.Equal(4096, archive.GetEntry("mem/memory.rec")!.CompressedLength);
            ZipArchiveEntry text = archive.GetEntry("area/loaded-files.txt")!;
            Assert.True(text.CompressedLength * 10 < text.Length, $"{text.CompressedLength} of {text.Length}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A capture bigger than a piece is packed straight into pieces - every one full but the last, none past the most - and the
    /// pieces joined in order are the zip, every file in it as it was.
    /// </summary>
    [Fact]
    public void ACAPTUREBiggerThanAPieceIsCutIntoPiecesThatJoinIntoTheZip()
    {
        string root = Path.Combine(Path.GetTempPath(), $"capture-pieces-{Guid.NewGuid():N}");
        string folder = Path.Combine(root, "2026-10-13 06-07-07 The Excavation");
        Directory.CreateDirectory(folder);
        try
        {
            var random = new Random(7);
            byte[] picture = new byte[25_000], recording = new byte[12_345];
            random.NextBytes(picture);
            random.NextBytes(recording);
            File.WriteAllBytes(Path.Combine(folder, "game.png"), picture);
            File.WriteAllBytes(Path.Combine(folder, "memory.rec"), recording);
            File.WriteAllText(Path.Combine(folder, "capture.txt"), "capture  the summary\n");

            (IReadOnlyList<string> files, List<string> leftOut) = CaptureReport.Pack(folder, most: 10_000);

            Assert.Empty(leftOut);
            Assert.Equal(4, files.Count);
            Assert.Equal([.. Enumerable.Range(1, 4).Select(piece => $"{folder}.zip.{piece:000}")], files);
            Assert.False(File.Exists(folder + ".zip"));
            Assert.All(files.SkipLast(1), piece => Assert.Equal(10_000, new FileInfo(piece).Length));
            Assert.InRange(new FileInfo(files[^1]).Length, 1, 10_000);

            using var joined = new MemoryStream();
            foreach (string piece in files)
            {
                joined.Write(File.ReadAllBytes(piece));
            }

            joined.Position = 0;
            using var archive = new ZipArchive(joined, ZipArchiveMode.Read);
            Assert.Equal(["capture.txt", "game.png", "memory.rec"], archive.Entries.Select(one => one.FullName));
            Assert.Equal(picture, Content(archive.GetEntry("game.png")!));
            Assert.Equal(recording, Content(archive.GetEntry("memory.rec")!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Content(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
