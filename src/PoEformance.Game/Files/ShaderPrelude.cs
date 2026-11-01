using System.Text;

namespace PoEformance.Game.Files;

/// <summary>
/// The text the engine puts before every shader it compiles for Direct3D 11: its macros for the API (<c>#define DX11</c>,
/// <c>GROUPSHARED groupshared</c>, <c>STRUCTURED_BUFFER_DECL</c>, the atomics) and the two includes that declare its samplers and common
/// structs (<c>Shaders/CommonDeclarations.inc</c>, <c>Shaders/SamplerDeclarations.inc</c>).
/// </summary>
/// <remarks>
/// FOUND (v0.1.429) by linking the lamp's update shader and compiling it: <c>GROUPSHARED</c>, <c>SamplerLinearWrap</c>, <c>VERTEX_HAS_POSITION</c>,
/// <c>SceneInfo</c> and their kin stand in no <c>.ffx</c>. The program holds five such texts, one an API - Vulkan, DX11, two more and DX12 -
/// each a NUL-terminated string in its read-only data (module+0x2EF6747 to +0x2EFD230 in the build of 2026-10-27), the DX11 one beginning
/// <c>#define CONCAT_HELPER</c> and holding <c>#define DX11</c>. The exe on disk carries the same bytes, so it is read from there - the engine's
/// text, not a copy of it in this repository. The tool draws with Direct3D 11, so the DX11 text is the one taken.
/// </remarks>
public static class ShaderPrelude
{
    /// <summary>The line marking the Direct3D 11 text among the five.</summary>
    public const string Marker = "#define DX11\n";

    /// <summary>A line every such text holds.</summary>
    private const string Shared = "#define GROUPSHARED";

    /// <summary>The DX11 text in a program's bytes, or null where it holds none.</summary>
    public static string? Find(ReadOnlySpan<byte> program)
    {
        byte[] marker = Encoding.ASCII.GetBytes(Marker);
        byte[] shared = Encoding.ASCII.GetBytes(Shared);
        var from = 0;
        while (from < program.Length)
        {
            int at = program[from..].IndexOf(marker);
            if (at < 0)
            {
                return null;
            }

            at += from;
            int start = at;
            while (start > 0 && program[start - 1] != 0)
            {
                start--;
            }

            int end = program[at..].IndexOf((byte)0);
            end = end < 0 ? program.Length : at + end;
            ReadOnlySpan<byte> text = program[start..end];
            int first = text.IndexOf("#define"u8);
            if (first > 0)
            {
                text = text[first..];
            }

            if (text.IndexOf(shared) >= 0)
            {
                return Encoding.ASCII.GetString(text);
            }

            from = end + 1;
        }

        return null;
    }

    /// <summary>The DX11 text of the first program file that holds one, with the file it came from; null where none does.</summary>
    public static (string Text, string File)? Read(IEnumerable<string> programs)
    {
        ArgumentNullException.ThrowIfNull(programs);
        foreach (string program in programs)
        {
            try
            {
                if (Find(File.ReadAllBytes(program)) is { } text)
                {
                    return (text, program);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
