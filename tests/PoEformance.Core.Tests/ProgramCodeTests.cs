using System.Buffers.Binary;
using System.Text;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The program code part's reading of an image (ProgramCode.Read) held against a small image made here: its sections and function
/// table off its own header, a key found in its data, the lea of its address found in its code within its function, and a pointer to it
/// found in its data.
/// </summary>
public sealed class ProgramCodeTests
{
    private const ulong Base = 0x140000000;

    [Fact]
    public void AKEYIsFoundWithTheCodeAndTheDataThatReadIt()
    {
        byte[] image = Image();
        List<string> lines = ProgramCode.Read(image, Base);

        Assert.Contains(lines, line => line.StartsWith("image: PE header at 0x80", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("functions: 1 in the exception directory at module+0x3000", StringComparison.Ordinal));
        Assert.Contains("--- key \"bend_stiffness\": 1 string(s)", lines);
        Assert.Contains("  module+0x2100 \"bend_stiffness\": read by 1 place(s) of code and 1 of data", lines);
        Assert.Contains(lines, line => line.StartsWith("    code module+0x1013 in module+0x1000..0x1020:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("    data module+0x2200:", StringComparison.Ordinal) && line.Contains("[0000000140002100]", StringComparison.Ordinal));
    }

    [Fact]
    public void AWORDIsFoundInAWideStringAndAKeyIsNotFoundInsideAnother()
    {
        byte[] image = Image();
        Encoding.Unicode.GetBytes("ClothSimulation\0").CopyTo(image, 0x2300);
        Encoding.ASCII.GetBytes("my_gravity_well\0").CopyTo(image, 0x2400);
        List<ProgramCode.Section> sections = ProgramCode.Sections(image, out _);
        List<ProgramCode.Found> found = ProgramCode.Strings(image, sections);

        Assert.Contains(found, one => one is { Rva: 0x2300, Text: "ClothSimulation", Wide: true });
        Assert.DoesNotContain(found, one => one.Text == "my_gravity_well");
        Assert.Contains(found, one => one is { Rva: 0x2100, Text: "bend_stiffness", Wide: false });
    }

    /// <summary>
    /// An image of two sections - .text running at 0x1000 with one function to 0x1020 loading the key's address, .rdata at 0x2000 holding
    /// the key, a pointer to it and the function table (.pdata) at 0x3000 with its unwind information.
    /// </summary>
    private static byte[] Image()
    {
        var image = new byte[0x4000];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C), 0x80);
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(image, 0x80);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x84), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x86), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x88), 0x6543_2100);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x94), 0xF0);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x98), 0x20B);

        // THE EXCEPTION DIRECTORY, the fourth data directory, 112 bytes into the optional header.
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x98 + 112 + 24), 0x3000);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x98 + 112 + 28), 12);

        Section(image, 0x98 + 0xF0, ".text", 0x1000, 0x1000, 0x60000020);
        Section(image, 0x98 + 0xF0 + 40, ".rdata", 0x2000, 0x2000, 0x40000040);

        // lea rax, [rip + disp32] at 0x1010, its displacement at 0x1013, ending at 0x1017.
        image[0x1010] = 0x48;
        image[0x1011] = 0x8D;
        image[0x1012] = 0x05;
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x1013), 0x2100 - 0x1017);

        Encoding.ASCII.GetBytes("bend_stiffness\0").CopyTo(image, 0x2100);
        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(0x2200), Base + 0x2100);

        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3000), 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3004), 0x1020);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3008), 0x3100);
        image[0x3100] = 0x01;
        return image;
    }

    private static void Section(byte[] image, int at, string name, int rva, int size, uint flags)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(image, at);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(at + 8), size);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(at + 12), rva);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at + 36), flags);
    }
}
