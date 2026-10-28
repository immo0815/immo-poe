using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's render debug part: the debug views first, followed through what they name, what the compile makes of each, the
/// sources naming a node it does not know, and the program's strings round its table of views - read back offline as the replay does.
/// </summary>
public sealed class CaptureRenderDebugTests
{
    private const string Unknown = "DebugWalkabilityTint";

    private static readonly Dictionary<string, string> Install = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Metadata/EngineGraphs/debug_albedo.fxgraph"] = """
            {"version":3,"textures":[{"filename":"Art/Textures/Debug/ramp.dds"}],
             "include":"Metadata/EngineGraphs/debug_base.fxgraph",
             "nodes":[{"type":"ConstantFloat3","index":0,"parameters":[{"value":[1.0,0.0,1.0]}]},
                      {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
             "links":[{"src":{"type":"ConstantFloat3","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}]}
            """,
        ["Metadata/EngineGraphs/debug_base.fxgraph"] = """
            {"version":3,"nodes":[{"type":"ConstantFloat3","index":0,"parameters":[{"value":[0.5,0.5,0.5]}]},
                                  {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
             "links":[{"src":{"type":"ConstantFloat3","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}]}
            """,
        ["Metadata/EngineGraphs/debug_walkability.fxgraph"] = $$$"""
            {"version":3,"nodes":[{"type":"{{{Unknown}}}","index":0},{"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
             "links":[{"src":{"type":"{{{Unknown}}}","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}]}
            """,
        ["Metadata/EngineGraphs/grid_visualization_line.fxgraph"] = """
            {"version":3,"nodes":[{"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],"links":[]}
            """,
        ["Shaders/Renderer/Nodes/Debug.ffx"] = "// the debug pass\n=== not a heading, marked\n" + Unknown + "\n",
        ["Shaders/Renderer/Nodes/Other.ffx"] = "FRAGMENT " + Unknown + " { }\n",
        ["Shaders/Plain.hlsl"] = "float4 main() { return 0; }\n",
    };

    [Fact]
    public void THEDEBUGViewsAreWrittenWholeWithWhatTheCompileMakesOfThemAndReadBackOffline()
    {
        Assert.True(ShadeProgram.Writes("AlbedoColor") && ShadeProgram.Knows("ConstantFloat3"));
        Assert.False(ShadeProgram.Knows(Unknown) || ShadeProgram.Writes(Unknown));

        string program = Path.Combine(Path.GetTempPath(), $"poef-render-debug-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(program, Program());
        try
        {
            string text = CaptureRenderDebug.Write(
                Read,
                [program],
                ["metadata/enginegraphs/debug_albedo.fxgraph", "metadata/enginegraphs/debug_walkability.fxgraph", "metadata/enginegraphs/grid_visualization_line.fxgraph"],
                ["Shaders/Plain.hlsl", "Shaders/Renderer/Nodes/Other.ffx", "Shaders/Renderer/Nodes/Debug.ffx"]).ReplaceLineEndings("\n");

            // THE BASE IS FOUND THROUGH THE ALBEDO VIEW, which names it; the views known node for node are the two without the unknown one.
            Assert.StartsWith(
                "render debug: 3 debug views and 1 other graphs written whole, 2 of 3 debug views known to the compile node for node, 1 node types the views use unknown (1 in all); 3 engine graphs the program names, ",
                text, StringComparison.Ordinal);
            Assert.Contains("##### Metadata/EngineGraphs/debug_base.fxgraph  (named by Metadata/EngineGraphs/debug_albedo.fxgraph)", text, StringComparison.Ordinal);
            Assert.True(text.IndexOf("##### Metadata/EngineGraphs/debug_albedo", StringComparison.Ordinal) < text.IndexOf("##### metadata/enginegraphs/grid_visualization_line", StringComparison.OrdinalIgnoreCase));
            Assert.Contains($"  {Unknown}: 1 debug views, 1 graphs - debug_walkability.fxgraph", text, StringComparison.Ordinal);
            Assert.Contains("  Art/Textures/Debug/ramp.dds  (Metadata/EngineGraphs/debug_albedo.fxgraph): ", text, StringComparison.Ordinal);

            // THE PROGRAM'S LABELS between the paths are printed as they stand.
            Assert.Contains("  file:0x2B  Walkability\n", text, StringComparison.Ordinal);
            Assert.Contains("  file:0x37  Metadata/EngineGraphs/debug_walkability.fxgraph  (UTF-16)", text, StringComparison.Ordinal);

            // THE DEBUG SOURCE WHOLE and the one naming the unknown node; the plain one not; a source's line never taken for a heading.
            Assert.Contains("##### Shaders/Renderer/Nodes/Debug.ffx  ((a debug source), " + Unknown + ")", text, StringComparison.Ordinal);
            Assert.Contains("  | === not a heading, marked", text, StringComparison.Ordinal);
            Assert.Contains("##### Shaders/Renderer/Nodes/Other.ffx  (" + Unknown + ")", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Plain.hlsl", text, StringComparison.Ordinal);

            // READ BACK OFFLINE, the replay's compile says the same.
            string replay = CaptureReplayTests.RenderDebugReplay(text.Split('\n'));
            Assert.StartsWith("render debug replay: 4 graphs read back, 2 of 3 debug views known to the compile node for node, 1 node types the views use unknown (1 in all)", replay, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(program);
        }
    }

    private static byte[]? Read(string path) => Install.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null;

    /// <summary>
    /// A program holding three engine graphs' paths with a label beside one - no PE header, so addresses are file offsets. One path is
    /// UTF-16 at an odd offset, as the game's program holds all of them (v0.1.317's capture, whose ASCII search found none).
    /// </summary>
    private static byte[] Program()
    {
        var bytes = new List<byte>();
        void Put(string text)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(text));
            bytes.Add(0);
        }

        void Wide(string text)
        {
            bytes.AddRange(Encoding.Unicode.GetBytes(text));
            bytes.AddRange([0, 0]);
        }

        Put("Metadata/EngineGraphs/debug_albedo.fxgraph");
        Put("Walkability");
        Wide("Metadata/EngineGraphs/debug_walkability.fxgraph");
        Put("Metadata/EngineGraphs/grid_visualization_line.fxgraph");
        return [.. bytes];
    }
}
