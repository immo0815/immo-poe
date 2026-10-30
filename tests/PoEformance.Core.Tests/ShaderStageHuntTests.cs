using System.Buffers.Binary;
using System.Text;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's shader stages part: the engine's stage table found in the program's image as MSVC std::strings, and the install's
/// graphs' links across stages - see ShaderStageHunt.
/// </summary>
public sealed class ShaderStageHuntTests
{
    private const ulong Base = 0x1400000000;
    private const ulong Heap = 0x30000000;
    private const int TableAt = 0x800;

    /// <summary>
    /// The table as the 2026-10-09 find saw its pixel side: names up to fifteen letters inline, longer ones on the heap - and the vertex
    /// side before it, which nobody has seen.
    /// </summary>
    private static readonly string[] Table =
    [
        "VertexInit", "LocalTransform", "Animation", "WorldTransform_Init", "WorldTransform", "VertexOutput_Final",
        "VertexToPixel", "PixelInit", "UVSetup", "Texturing_Init", "Texturing", "PreLighting",
    ];

    [Fact]
    public void THETABLEIsFoundInTheImageInItsOrderAndTheGraphsLinksAreHeldAgainstIt()
    {
        string text = string.Join("\n", ShaderStageHunt.Run(Reader(), Graphs().GetValueOrDefault, [.. Graphs().Keys, "Metadata/Missing.fxgraph"], new ShaderStageHunt.Running()));

        Assert.StartsWith("shader stages: the table at module+0x800, 12 entries, holds 5 of the 5 stages the graphs use; 3 graphs read, 1 links from a vertex stage to a pixel stage;", text, StringComparison.Ordinal);

        // THE RUN WHOLE AND IN ITS ORDER, inline and heap alike - the literal pool and the lone string are no run.
        for (var at = 0; at < Table.Length; at++)
        {
            Assert.Contains($"  entry\t1\t{at}\tmodule+0x{TableAt + (0x20 * at):X}\t{Table[at]}\t{(Table[at].Length > 15 ? "heap" : "inline")}\t", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("run 2:", text, StringComparison.Ordinal);
        Assert.Contains("image: module 0x1400000000, 0x2000 bytes, 0x0 of them unreadable; 11 std::strings held inline, 2 on the heap (0 more whose text did not read); 1 runs holding stages", text, StringComparison.Ordinal);

        // THE COMPILE'S ORDER against it, and the table's stages it does not run.
        Assert.Contains("  compile\tVertexInit\ttable 0", text, StringComparison.Ordinal);
        Assert.Contains("  compile\tUVSetup_Calc\tnot in the table", text, StringComparison.Ordinal);
        Assert.Contains("the compile's stages run in the table's order", text, StringComparison.Ordinal);
        Assert.Contains("the table's entries the compile does not run: LocalTransform, Animation, WorldTransform_Init, WorldTransform, VertexOutput_Final, VertexToPixel, PixelInit", text, StringComparison.Ordinal);

        // THE LINKS: forwards, and face_camera's against the names' order - which the table puts backwards.
        Assert.Contains("  pair\tVertexInit\tTexturing\tgraphs 1\tforwards\te.g. Metadata/Effects/Graphs/General/AddMask.fxgraph", text, StringComparison.Ordinal);
        Assert.Contains("  pair\tWorldTransform\tWorldTransform_Init\tgraphs 1\tbackwards\te.g. Metadata/EngineGraphs/face_camera.fxgraph", text, StringComparison.Ordinal);
        Assert.Contains("stage pairs: 2 - 1 forwards, 1 backwards, 0 with a stage the table does not hold", text, StringComparison.Ordinal);

        // FROM THE VERTEX SHADER TO THE PIXEL SHADER: the light rays' mask, through the stage-less nodes between.
        Assert.Contains("  cross\tInputVertexUV@VertexInit\tAlbedoColor@Texturing\tgraphs 1\tMetadata/Effects/Graphs/General/AddMask.fxgraph", text, StringComparison.Ordinal);
        Assert.DoesNotContain("  cross\tInputVertexPosition", text, StringComparison.Ordinal);

        // THE COORDINATES' PATH: the engine's own from the mesh to the pixel side, and the mask's pass-through.
        Assert.Contains("  coords\tVertexUV@VertexInit\tVertexLocalUV\tvia VertexLocalUV\tgraphs 1\tMetadata/EngineGraphs/fixed.fxgraph", text, StringComparison.Ordinal);
        Assert.Contains("  coords\tUV@PixelInit\tFromVertexUV\tvia FromVertexUV\tgraphs 1\tMetadata/EngineGraphs/fixed.fxgraph", text, StringComparison.Ordinal);
        Assert.Contains("  coords\tVertexUV@VertexInit\tInputVertexUV@VertexInit\tvia InputVertexUV\tgraphs 1", text, StringComparison.Ordinal);

        Assert.Contains("graphs: 3 of the walk's 5 read (2 of them the engine's own, under Metadata/EngineGraphs/), 1 not in the install, 1 not a graph", text, StringComparison.Ordinal);
        Assert.Contains("  stage\tVertexInit\tgraphs 3\tnodes 4\ttable 0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void THEREPLAYHoldsTheReportsPairsAgainstItsTableAgain()
    {
        string text = string.Join("\n", ShaderStageHunt.Run(Reader(), Graphs().GetValueOrDefault, [.. Graphs().Keys], new ShaderStageHunt.Running()));
        string replay = ShaderStageHunt.Replay(text);

        Assert.StartsWith("shader stages replay: a table of 12 entries, 2 stage pairs", replay, StringComparison.Ordinal);
        Assert.Contains("  3\tWorldTransform_Init", replay, StringComparison.Ordinal);
        Assert.Contains("  backwards\tWorldTransform\tWorldTransform_Init\tgraphs 1", replay, StringComparison.Ordinal);
        Assert.Contains("stage pairs: 1 forwards, 1 backwards, 0 with a stage the table does not hold", replay, StringComparison.Ordinal);
        Assert.Contains("the compile's stages run in the table's order", replay, StringComparison.Ordinal);
        Assert.StartsWith("shader stages replay: the report holds no table and no stage pair", ShaderStageHunt.Replay("nothing"), StringComparison.Ordinal);
    }

    [Fact]
    public void WITHNOTableTheVertexSideIsTakenByName()
    {
        var reader = new FakeMemoryReader { ModuleBase = Base, ModuleSize = 0x2000 }.Place(Base, new byte[0x2000]);
        string text = string.Join("\n", ShaderStageHunt.Run(reader, Graphs().GetValueOrDefault, [.. Graphs().Keys], new ShaderStageHunt.Running()));

        Assert.StartsWith("shader stages: no table found; 3 graphs read, 1 links from a vertex stage to a pixel stage;", text, StringComparison.Ordinal);
        Assert.Contains("no run of std::strings in the image holds two stages the graphs or the compile name", text, StringComparison.Ordinal);
        Assert.Contains("=== from a vertex stage to a pixel stage: the vertex side by name, the table not found", text, StringComparison.Ordinal);
        Assert.Contains("  pair\tVertexInit\tTexturing\tgraphs 1\tunplaced", text, StringComparison.Ordinal);

        string stopped = string.Join("\n", ShaderStageHunt.Run(new FakeMemoryReader(), null, null, new ShaderStageHunt.Running()));
        Assert.StartsWith("shader stages: no table found; 0 graphs read", stopped, StringComparison.Ordinal);
        Assert.Contains("graphs: none read - no install", stopped, StringComparison.Ordinal);
        Assert.Contains("image: not read - the program's module has no base or no size", stopped, StringComparison.Ordinal);
    }

    /// <summary>An image holding the table, a literal pool of the same names as a decoy, and a lone string; two names on the heap.</summary>
    private static FakeMemoryReader Reader()
    {
        var image = new byte[0x2000];
        Encoding.ASCII.GetBytes("Texturing\0UVSetup\0PixelInit\0VertexInit\0").CopyTo(image, 0x100);
        var reader = new FakeMemoryReader { ModuleBase = Base, ModuleSize = (uint)image.Length };
        ulong heap = Heap;
        for (var at = 0; at < Table.Length; at++)
        {
            String(image, TableAt + (0x20 * at), Table[at], reader, ref heap);
        }

        String(image, 0x1800, "Unrelated", reader, ref heap);
        return reader.Place(Base, image);
    }

    /// <summary>An MSVC std::string at <paramref name="at"/>: inline up to fifteen letters, else on the heap at a capacity of 0x1F.</summary>
    private static void String(byte[] image, int at, string text, FakeMemoryReader reader, ref ulong heap)
    {
        if (text.Length <= 15)
        {
            Encoding.ASCII.GetBytes(text).CopyTo(image, at);
            BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(at + 0x18), 15);
        }
        else
        {
            BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(at), heap);
            BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(at + 0x18), 0x1F);
            reader.PlaceUtf8(heap, text);
            heap += 0x40;
        }

        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(at + 0x10), (ulong)text.Length);
    }

    /// <summary>The light rays' mask graph, the engine's coordinates, face_camera's link against the names' order, and a file that is no graph.</summary>
    private static Dictionary<string, byte[]> Graphs() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Metadata/Effects/Graphs/General/AddMask.fxgraph"] = Graph(
            [("InputVertexUV", "VertexInit"), ("VertexUV", "VertexInit"), ("MuddleTex", ""), ("SampleTexture", ""), ("InputAlbedoColor", "Texturing"),
             ("Multiply", ""), ("AlbedoColor", "Texturing")],
            [(0, 1), (0, 2), (2, 3), (3, 5), (4, 5), (5, 6)]),
        ["Metadata/EngineGraphs/fixed.fxgraph"] = Graph(
            [("VertexLocalUV", ""), ("VertexUV", "VertexInit"), ("FromVertexUV", ""), ("UV", "PixelInit")],
            [(0, 1), (2, 3)]),
        ["Metadata/EngineGraphs/face_camera.fxgraph"] = Graph(
            [("InputVertexPosition", "WorldTransform"), ("VertexPosition", "WorldTransform_Init"), ("InputVertexColor", "VertexInit")],
            [(0, 1)]),
        ["Metadata/Broken.fxgraph"] = Encoding.UTF8.GetBytes("not a graph"),
    };

    /// <summary>A graph's JSON: each node index nought, its stage where it has one; each link from node to node.</summary>
    private static byte[] Graph((string Type, string Stage)[] nodes, (int From, int To)[] links)
    {
        string Node((string Type, string Stage) node) => node.Stage.Length > 0
            ? $"{{\"type\":\"{node.Type}\",\"index\":0,\"stage\":\"{node.Stage}\"}}"
            : $"{{\"type\":\"{node.Type}\",\"index\":0}}";
        string End((string Type, string Stage) node, string variable) => node.Stage.Length > 0
            ? $"{{\"type\":\"{node.Type}\",\"index\":0,\"stage\":\"{node.Stage}\",\"variable\":\"{variable}\"}}"
            : $"{{\"type\":\"{node.Type}\",\"index\":0,\"variable\":\"{variable}\"}}";
        string json = "{\"version\":3,\"nodes\":[" + string.Join(",", nodes.Select(Node)) + "],\"links\":["
            + string.Join(",", links.Select(link => $"{{\"src\":{End(nodes[link.From], "output")},\"dst\":{End(nodes[link.To], "input")}}}")) + "]}";
        return Encoding.UTF8.GetBytes(json);
    }
}
