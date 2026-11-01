using System.Globalization;
using System.Text;

namespace PoEformance.Game.Files;

/// <summary>One graph of a shader's list, with the parameters its material sets on it.</summary>
/// <param name="Path">The graph's path.</param>
/// <param name="Graph">The graph read.</param>
/// <param name="Custom">The material's parameters by the custom names the graph's nodes expose; empty for an engine graph.</param>
public sealed record LinkedGraph(string Path, ShaderGraph Graph, IReadOnlyDictionary<string, ShaderValue[]> Custom);

/// <summary>A shader linked from the engine's fragments, and everything the link could not settle.</summary>
/// <param name="Hlsl">The HLSL, whole: the engine's DX11 prelude, the macros, the declarations, the entry point.</param>
/// <param name="Entry">The entry point's name.</param>
/// <param name="Unresolved">What the link left out or stood in for, each once - a node type no source defines, an input no link feeds, an engine type it cannot fill.</param>
/// <param name="Resources">The textures and samplers the nodes name, each a global of the HLSL the drawing must bind: its name, then what fills it.</param>
/// <param name="Nodes">How many nodes were emitted.</param>
/// <param name="Fragments">How many distinct fragments their code came from.</param>
/// <param name="Declarations">How many declarations were included.</param>
/// <param name="Tracks">
/// The curves the shader's splineGrayscale and splineColour uniforms name, by their index in the engine's <c>spline_lut</c> buffer - a curve's
/// keys as its node or material writes them; the drawing bakes each into the buffer's row of that index (color_spline_data's SplineLUTSample
/// reads <c>LUT_WIDTH</c> values a row, two halves to a word).
/// </param>
public sealed record LinkedShader(
    string Hlsl,
    string Entry,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<(string Name, string Filled)> Resources,
    int Nodes,
    int Fragments,
    int Declarations,
    IReadOnlyList<CurveKey[]>? Tracks = null);

/// <summary>
/// Links the engine's GPU particle update shader from its graphs as the engine's graph compiler does: every node's FRAGMENT taken word for word
/// from the install's <c>.ffx</c> sources, extension points (<c>ParticlesUpdate::Position</c>) as variables read and written at their stages in
/// the order of the engine's stage table, and graphs in the composer's order within a stage (see <see cref="ParticleComposition"/>).
/// </summary>
/// <remarks>
/// WHY A LINKER (DECIDED from the live client, 2026-11-01, over porting node by node into ShadeProgram): the particle graphs use 721 node types,
/// 462 of them ones ShadeProgram does not evaluate, and 719 are FRAGMENTs of the install's own sources. Linking them is generic for every
/// effect, the engine's arithmetic to the letter.
///
/// THE RULES, each where it comes from:
/// - STAGES IN THE TABLE'S ORDER, GRAPHS IN THE LIST'S ORDER WITHIN ONE, AND WITHIN ONE GRAPH AT ONE STAGE EVERY READ SEES THE VALUE BEFORE THAT
///   GRAPH WROTE - the rule ShadeProgram's colour has kept since the stage table was read (its remarks), here for the particles' 21 stages
///   (<see cref="Stages"/>, MEASURED: shader-stages.txt of 2026-11-01 16-10-02, entries 49 to 69).
/// - A NODE WITHOUT A STAGE RUNS WHERE ITS FIRST CONSUMER NEEDS IT, after what it reads; a fragment flagged <c>sideeffects</c> that nothing
///   consumes runs where the Connection chain it sits in is written (the nocode extension point ParticlesUpdate::Connection orders them),
///   else at the end. A port bound to a SEMANTIC (<c>in uint particle_id : PARTICLE_ID</c>) and fed by no link reads what the fragment that
///   writes that semantic wrote - ReadParticleUpdateId's <c>out uint particle_id : PARTICLE_ID</c> - so its writers run first.
/// - A NODE'S OWN UNIFORMS ARE ITS PARAMETERS BY POSITION, the material's list under the node's custom name laid over the node's own, an entry
///   neither fills the uniform's declared default (ShadeProgram's Parameter, the same rule).
/// - <c>&lt;Group&gt;</c> IS THE GRAPH EDITOR'S FOLDING, wiring alone: a slot is what the link INTO it carries, read by every link out of it
///   (Better Emitter Phase in BoneSphereEmitter.fxgraph: in_FrameFrac in, out_EmitterPhase out).
///
/// CHOSEN, said in <see cref="LinkedShader.Unresolved"/> where it stands in: UNIQUE_NODE_ID as a hash of the graph's path and the node's key
/// (the engine's compiler hands it to each node function as `in uint UNIQUE_NODE_ID`, module+0x3193F18, its value not read); COMPUTE beside
/// the generator's own COMPUTE_SHADER (the program keeps a table of compute passes' macros, COMPUTE and COMPUTE_PHYSIC among them,
/// module+0x459B070 - which the particle update takes is not read); a Spline5 parameter baked from its keys as straight lines between them
/// (<see cref="Spline"/>; the engine inlines it through LoadSpline5 of five float4s, module+0x31946A0, baked by its C++).
///
/// THE ENGINE'S STRUCTS ARE ITS OWN: ParticleData, ParticleEmitterData, BoneData, spline5 and LUT_WIDTH (128) stand in
/// Shaders/CommonDeclarations.inc, which the DX11 prelude includes (v0.1.429's capture) - the drawing fills its buffers in that layout.
/// </remarks>
public static class ParticleLinker
{
    /// <summary>The particle update's stages in the engine's order - MEASURED, the client's stage table entries 49 to 69 (schema, at the stage table's note).</summary>
    public static readonly IReadOnlyList<string> Stages =
    [
        "ParticlesSetup", "ParticlesLock", "ParticlesLifetimeInit", "ParticlesLifetime", "ParticlesLifetimeCalc",
        "ParticlesEmitInit", "ParticlesEmit", "ParticlesEmitCalc", "ParticlesUpdateInit", "ParticlesUpdate", "ParticlesUpdateCalc",
        "ParticlesPhysicInit", "ParticlesPhysic", "ParticlesPhysicCalc", "ParticlesIntegrateInit", "ParticlesCollision",
        "ParticlesIntegrate", "ParticlesPostIntegrate", "ParticlesUnlock", "ParticlesWrite", "ParticlesWriteFinal",
    ];

    /// <summary>The update shader's entry point.</summary>
    public const string UpdateEntry = "UpdateParticles";

    /// <summary>A render pass's vertex shader's entry point.</summary>
    public const string VertexEntry = "VertexMain";

    /// <summary>A render pass's pixel shader's entry point.</summary>
    public const string PixelEntry = "PixelMain";

    /// <summary>
    /// The material pipeline's vertex stages in the engine's order - MEASURED, the client's stage table entries 0 to 16 (shader-stages.txt of
    /// 2026-11-01 16-10-02; ShadeProgram.Stages names the same as far as PreLighting_Final).
    /// </summary>
    public static readonly IReadOnlyList<string> VertexStages =
    [
        "VertexInit", "Animation", "LocalTransform", "LocalTransform_Calc", "LocalTransform_Final",
        "WorldTransform_Init", "WorldTransform", "WorldTransform_Calc", "WorldTransform_Final",
        "ProjectionTransform_Init", "ProjectionTransform", "ProjectionTransform_Calc", "ProjectionTransform_Final",
        "VertexOutput", "VertexOutput_Calc", "VertexOutput_Final", "VertexOutput_Pass",
    ];

    /// <summary>The material pipeline's pixel stages in the engine's order - MEASURED, the stage table's entries 17 to 47.</summary>
    public static readonly IReadOnlyList<string> PixelStages =
    [
        "VertexToPixel", "PixelInit", "UVSetup", "UVSetup_Calc", "UVSetup_Final",
        "Texturing_Init", "Texturing", "Texturing_Calc", "Texturing_Final", "PreLighting", "PreLighting_Calc", "PreLighting_Final",
        "AlphaClip", "Lighting", "Lighting_Calc", "Lighting_Final", "CustomLighting", "CustomLighting_Calc", "CustomLighting_Final",
        "LightingEnd", "LightingEnd_Calc", "LightingEnd_Final", "PostLighting", "PostLighting_Calc", "PostLighting_Final",
        "FogStage", "FogStage_Calc", "FogStage_Final", "PixelOutput", "PixelOutput_Calc", "PixelOutput_Final",
    ];

    /// <summary>Which shader a link writes.</summary>
    public enum Side
    {
        /// <summary>The particle update's compute shader.</summary>
        Update,

        /// <summary>A render pass's vertex shader.</summary>
        Vertex,

        /// <summary>A render pass's pixel shader.</summary>
        Pixel,
    }

    /// <summary>
    /// The engine's vertex layout as its graph compiler turns it into the <c>VERTEX_HAS_*</c>, <c>VERTEX_OFFSET_*</c> and <c>VERTEX_SIZE</c> macros
    /// the sources read a mesh's vertex by (gpu_particles_emitter_mesh) - READ from the program (the build of 2026-10-27): the generator at
    /// module+0x12DFC80 walks the eleven 12-byte entries at module+0x3183430 in the order the list at module+0x3183400 gives (0 to 10), each
    /// a flag bit, a format, a semantic (the names at module+0x3194D60) with its number appended where not nought, and a size in bytes.
    /// </summary>
    public static readonly IReadOnlyList<(string Semantic, uint Bit, int Size)> VertexLayout =
    [
        ("POSITION", 0x200, 12), ("NORMAL", 0x10, 4), ("TANGENT", 0x20, 4), ("TEXCOORD", 0x8, 4), ("TEXCOORD1", 0x80, 8),
        ("TEXCOORD2", 0x100, 8), ("BLENDINDICES", 0x4, 4), ("BLENDWEIGHT", 0x4, 4), ("COLOR3", 0x1, 4), ("COLOR", 0x2, 4), ("COLOR4", 0x40, 4),
    ];

    /// <summary>
    /// The vertex layout's macros as the generator writes them: <c>VERTEX_HAS_X(F) (((F) &amp; bit) != 0)</c> (<c>(true)</c> for a bit of nought),
    /// <c>VERTEX_OFFSET_X(F)</c> the one before's offset plus its size where present (<c>(0)</c> for the first), and <c>VERTEX_SIZE(F)</c> the
    /// last's offset plus its size.
    /// </summary>
    public static string VertexMacros()
    {
        var text = new StringBuilder();
        (string Semantic, uint Bit, int Size)? previous = null;
        foreach ((string semantic, uint bit, int size) in VertexLayout)
        {
            text.Append(CultureInfo.InvariantCulture, $"#define VERTEX_HAS_{semantic}(F) ({(bit != 0 ? $"((F) & {bit}) != 0" : "true")})\n");
            text.Append(CultureInfo.InvariantCulture, $"#define VERTEX_OFFSET_{semantic}(F) ({(previous is { } before ? $"(VERTEX_HAS_{before.Semantic}(F) ? {before.Size} : 0) + VERTEX_OFFSET_{before.Semantic}(F)" : "0")})\n");
            previous = (semantic, bit, size);
        }

        (string last, _, int lastSize) = VertexLayout[^1];
        text.Append(CultureInfo.InvariantCulture, $"#define VERTEX_SIZE(F) ((VERTEX_HAS_{last}(F) ? {lastSize} : 0) + VERTEX_OFFSET_{last}(F))\n");
        return text.ToString();
    }

    /// <summary>Links the update compute shader of an emitter from its graphs, in the composer's order.</summary>
    /// <param name="source">The engine's shader sources.</param>
    /// <param name="graphs">The graphs, the update material's own last - <see cref="ParticleComposition"/>'s Update list read.</param>
    /// <param name="prelude">The engine's DX11 prelude (<see cref="ShaderPrelude"/>), put first; null or empty where none was read, said.</param>
    public static LinkedShader Update(ShaderSource source, IReadOnlyList<LinkedGraph> graphs, string? prelude = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(graphs);
        return new Link(source, graphs, prelude ?? string.Empty, Side.Update, [], []).Run();
    }

    /// <summary>
    /// Links a render pass's vertex and pixel shaders from its graphs, in the composer's order (base.fxgraph first, the render material last),
    /// the pixel shader first: the values it reads of the vertex side cross as interpolants the vertex shader then writes, and both share one
    /// struct of what passes between them.
    /// </summary>
    /// <remarks>
    /// AS THE ENGINE'S GENERATOR WRITES IT, READ FROM ITS TEMPLATES (program-code.bin, module+0x3193388 to +0x3195030 of the build of
    /// 2026-10-27): a node's machine semantics (POSITION, TEXCOORDn, COLORn, INSTANCEID, the SV_ ones, PIXEL_RETURN_SEMANTIC_COLOR) are the
    /// shader's own inputs and outputs, collected into a struct (ShaderOut, `shader_out_data.`); a value a pixel stage reads of a vertex
    /// stage crosses as a generated semantic (`AUTO_S_%s_%s`); every other semantic is a variable the fragments share (semantics_data,
    /// surface_data). CHOSEN, said in each shader's unresolved list: a node of no stage and no side runs in the shader that reads it, a
    /// vertex value it needs crossing interpolated - the engine's placement of such a node is its generator's, not read; POSITION and VPOS
    /// are SV_Position, a crossing of an integer or a boolean is not interpolated.
    /// </remarks>
    /// <param name="source">The engine's shader sources.</param>
    /// <param name="graphs">The pass's graphs - <see cref="ParticleComposition"/>'s pass list read.</param>
    /// <param name="prelude">The engine's DX11 prelude, put first.</param>
    /// <param name="macros">The pass's own macros (the colour pass's OUTPUT_COLOR, a lit material's HAS_LIGHTING), before every fragment's.</param>
    public static (LinkedShader Vertex, LinkedShader Pixel) Render(ShaderSource source, IReadOnlyList<LinkedGraph> graphs, string? prelude, IReadOnlyCollection<string> macros)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(graphs);
        ArgumentNullException.ThrowIfNull(macros);
        var pixelLink = new Link(source, graphs, prelude ?? string.Empty, Side.Pixel, macros, []);
        pixelLink.Prepare();
        var vertexLink = new Link(source, graphs, prelude ?? string.Empty, Side.Vertex, macros, pixelLink.Crossings);
        vertexLink.Prepare();
        LinkedShader pixel = pixelLink.Write(vertexLink.FragmentMacros);
        LinkedShader vertex = vertexLink.Write(pixelLink.FragmentMacros);

        // ONE STRUCT BETWEEN THEM: every machine semantic the vertex shader writes or the pixel shader reads, each inside the lines its port
        // stood inside (VertexOutput's TEXCOORD5 under VERTEX_OUTPUT_TBN), and every crossing.
        var members = new SortedDictionary<string, (string Type, string Semantic, bool Flat, string Condition)>(StringComparer.Ordinal);
        foreach ((string semantic, (string type, string condition)) in vertexLink.Outputs.Concat(pixelLink.Inputs))
        {
            (string member, string bound) = Member(semantic);
            members.TryAdd(member, (Stored(type), bound, Flat(type), condition));
        }

        foreach (Crossing crossing in pixelLink.Crossings)
        {
            members.TryAdd(crossing.Name, (Stored(crossing.Type), crossing.Name, Flat(crossing.Type), string.Empty));
        }

        var shared = new StringBuilder("struct Interstage\n{\n");
        foreach ((string member, (string type, string semantic, bool flat, string condition)) in members)
        {
            shared.Append(Link.Guarded(condition, $"    {(flat && semantic != "SV_Position" ? "nointerpolation " : string.Empty)}{type} {member} : {semantic};"));
        }

        shared.Append("};\n");
        return (vertex with { Hlsl = vertex.Hlsl.Replace(InterstagePlace, shared.ToString(), StringComparison.Ordinal) },
            pixel with { Hlsl = pixel.Hlsl.Replace(InterstagePlace, shared.ToString(), StringComparison.Ordinal) });
    }

    /// <summary>A value a pixel stage reads of a vertex stage: the node and output it comes from, its type, and the interpolant carrying it.</summary>
    public sealed record Crossing(int Graph, string Key, string Variable, string Type, string Name);

    /// <summary>Where the struct between the two shaders goes, until both are linked.</summary>
    private const string InterstagePlace = "/*INTERSTAGE*/";

    /// <summary>
    /// Whether a semantic is the shader's own input or output rather than a variable the fragments share - the generator's list of machine
    /// semantics as its templates and the prelude name them.
    /// </summary>
    public static bool Machine(string semantic)
    {
        if (semantic.StartsWith("SV_", StringComparison.OrdinalIgnoreCase) || semantic.StartsWith("PIXEL_RETURN_SEMANTIC", StringComparison.Ordinal))
        {
            return true;
        }

        string bare = semantic.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return bare is "POSITION" or "POSITIONT" or "NORMAL" or "TANGENT" or "BINORMAL" or "BLENDINDICES" or "BLENDWEIGHT" or "COLOR"
            or "TEXCOORD" or "PSIZE" or "DEPTH" or "VPOS" or "VFACE" or "INSTANCEID";
    }

    /// <summary>A machine semantic's member of the struct between the shaders, and the semantic it is bound to there.</summary>
    private static (string Member, string Semantic) Member(string semantic)
        => semantic.ToUpperInvariant() switch
        {
            "POSITION" or "SV_POSITION" or "VPOS" or "POSITIONT" => ("position", "SV_Position"),
            var upper when upper.StartsWith("PIXEL_RETURN_SEMANTIC", StringComparison.Ordinal) => ("target_" + Link.Safe(semantic), "SV_Target" + Target(upper)),
            var upper => (upper.ToLowerInvariant(), upper),
        };

    /// <summary>
    /// Which render target a pixel return semantic writes: the plain one 0, the numbered ones their number, the named ones the colour 0 and
    /// then the motion vector, the GI's light and its normals 1 to 3. The generator writes <c>#define PIXEL_RETURN_SEMANTIC_COLOR </c> and its
    /// kin with a number it chooses per pass (its templates, module+0x3194220 to +0x3194300), not read - CHOSEN in that order; the colour pass
    /// defines none of OUTPUT_MVEC and the GI's macros, so only the colour is written.
    /// </summary>
    private static int Target(string semantic)
        => semantic switch
        {
            "PIXEL_RETURN_SEMANTIC_MVEC" => 1,
            "PIXEL_RETURN_SEMANTIC_GI_LIGHT" => 2,
            "PIXEL_RETURN_SEMANTIC_GI_NORMALS" => 3,
            _ => semantic.Length > 0 && char.IsAsciiDigit(semantic[^1]) ? semantic[^1] - '0' : 0,
        };

    /// <summary>A type as the struct between the shaders can carry it - a boolean as an unsigned integer.</summary>
    private static string Stored(string type) => type.StartsWith("bool", StringComparison.Ordinal) ? "uint" + type[4..] : type;

    /// <summary>Whether a type is passed between the shaders as it is, not interpolated.</summary>
    private static bool Flat(string type)
        => type.StartsWith("uint", StringComparison.Ordinal) || type.StartsWith("int", StringComparison.Ordinal) || type.StartsWith("bool", StringComparison.Ordinal);

    /// <summary>
    /// A Spline5 parameter as the <c>spline5</c> struct SampleSpline5 reads: up to four segments between its keys, each a cubic in time
    /// (<c>dot(segment_polynom[i], float4(t³, t², t, 1))</c>), the segment chosen by the breakpoints in <c>time_variance.xyz</c> and the random
    /// spread in its w. CHOSEN: each segment the straight line between its two keys (the lamp's keys are all <c>"type": "Linear"</c>), a time before
    /// the first key or after the last carrying its segment's line on; breakpoints past one where there are fewer than five keys.
    /// </summary>
    public static (float[] Polynoms, float[] TimeVariance) Spline(IReadOnlyList<CurveKey> keys, float variance)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var polynoms = new float[16];
        float[] breaks = [2f, 2f, 2f];
        if (keys.Count == 0)
        {
            return (polynoms, [breaks[0], breaks[1], breaks[2], variance]);
        }

        if (keys.Count == 1)
        {
            polynoms[3] = polynoms[7] = polynoms[11] = polynoms[15] = keys[0].Value;
            return (polynoms, [breaks[0], breaks[1], breaks[2], variance]);
        }

        int segments = Math.Min(keys.Count - 1, 4);
        for (var s = 0; s < 4; s++)
        {
            int from = Math.Min(s, segments - 1);
            CurveKey a = keys[from];
            CurveKey b = keys[from + 1];
            float span = b.Time - a.Time;
            float slope = Math.Abs(span) > 1e-12f ? (b.Value - a.Value) / span : 0f;
            polynoms[(s * 4) + 2] = slope;
            polynoms[(s * 4) + 3] = a.Value - (slope * a.Time);
        }

        for (var s = 1; s < segments; s++)
        {
            breaks[s - 1] = keys[s].Time;
        }

        return (polynoms, [breaks[0], breaks[1], breaks[2], variance]);
    }

    /// <summary>
    /// A shader with every <c>#include "path"</c> line replaced by the file's text, through the files' own includes, each where it is named -
    /// what a compiler's include handler hands it, done before the compile so one string is all it needs (the prelude names
    /// Shaders/CommonDeclarations.inc and Shaders/SamplerDeclarations.inc, the sources Shaders/Include/HalfUtility.hlsl and Quaternions.hlsl).
    /// </summary>
    /// <param name="hlsl">The shader.</param>
    /// <param name="include">A file's text by the path its include line names; null where there is none, the line then kept and said.</param>
    /// <param name="missing">The paths no text was found for.</param>
    public static string Expanded(string hlsl, Func<string, string?> include, out IReadOnlyList<string> missing)
    {
        ArgumentNullException.ThrowIfNull(hlsl);
        ArgumentNullException.ThrowIfNull(include);
        var absent = new List<string>();
        string text = Expand(hlsl, include, absent, 0);
        missing = absent;
        return text;
    }

    private static string Expand(string text, Func<string, string?> include, List<string> missing, int depth)
    {
        if (!text.Contains("#include", StringComparison.Ordinal) || depth > 16)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            int open = trimmed.IndexOf('"', StringComparison.Ordinal);
            int close = open < 0 ? -1 : trimmed.IndexOf('"', open + 1);
            if (trimmed.StartsWith("#include", StringComparison.Ordinal) && close > open)
            {
                string path = trimmed[(open + 1)..close];
                if (include(path) is { } file)
                {
                    result.Append(Expand(file.Replace("\r\n", "\n", StringComparison.Ordinal), include, missing, depth + 1)).Append('\n');
                    continue;
                }

                if (!missing.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    missing.Add(path);
                }
            }

            result.Append(line).Append('\n');
        }

        return result.ToString();
    }

    /// <summary>A float as HLSL writes it, to the bit.</summary>
    internal static string Number(float value)
    {
        if (!float.IsFinite(value))
        {
            return "0.0f";
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal) ? text + "f" : text + ".0f";
    }

    /// <summary>One run of the link over one shader's graphs.</summary>
    private sealed class Link(ShaderSource source, IReadOnlyList<LinkedGraph> graphs, string prelude, Side side, IReadOnlyCollection<string> passMacros, IReadOnlyList<Crossing> crossed)
    {
        /// <summary>The values this pixel shader reads of the vertex side - what the vertex shader is to write.</summary>
        public List<Crossing> Crossings { get; } = [];

        /// <summary>The machine semantics this shader reads (a vertex shader's from the mesh, a pixel shader's from the vertex shader), by semantic.</summary>
        public SortedDictionary<string, (string Type, string Condition)> Inputs { get; } = new(StringComparer.Ordinal);

        /// <summary>The machine semantics this shader writes, by semantic.</summary>
        public SortedDictionary<string, (string Type, string Condition)> Outputs { get; } = new(StringComparer.Ordinal);

        /// <summary>The macros this shader's fragments declare - the other shader of the pass is written with them too.</summary>
        public IEnumerable<string> FragmentMacros => _fragments.SelectMany(fragment => fragment.Macros);

        /// <summary>The system values this shader reads (SV_InstanceID, SV_IsFrontFace...) - its entry point's own parameters.</summary>
        private readonly SortedDictionary<string, string> _system = new(StringComparer.Ordinal);

        /// <summary>This shader's stages in the engine's order.</summary>
        private readonly IReadOnlyList<string> _stages = side switch
        {
            Side.Vertex => VertexStages,
            Side.Pixel => PixelStages,
            _ => Stages,
        };

        private readonly List<Node> _nodes = [];
        private readonly Dictionary<(int Graph, string Key), Node> _byKey = [];
        private readonly Dictionary<(int Graph, string Key, string Variable), List<ShaderLink>> _into = [];
        private readonly Dictionary<(int Graph, string Key), List<ShaderLink>> _intoNode = [];
        private readonly HashSet<(int Graph, string Key)> _read = [];
        private readonly StringBuilder _main = new();
        private readonly SortedSet<string> _unresolved = new(StringComparer.Ordinal);
        private readonly List<(string Name, string Filled)> _resources = [];
        private readonly List<CurveKey[]> _tracks = [];
        private readonly Dictionary<string, ShaderBlock> _extensions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _semantics = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Node>> _semanticWriters = new(StringComparer.Ordinal);
        private readonly List<ShaderBlock> _fragments = [];
        private readonly HashSet<string> _fragmentNames = new(StringComparer.Ordinal);
        private int _emitted;

        public LinkedShader Run()
        {
            Prepare();
            return Write([]);
        }

        /// <summary>The nodes gathered and ordered, the code emitted - all but the shader's text.</summary>
        public void Prepare()
        {
            Gather();
            Order();
        }

        /// <summary>Every node of every graph, classified, and every link by its target.</summary>
        private void Gather()
        {
            for (var g = 0; g < graphs.Count; g++)
            {
                LinkedGraph graph = graphs[g];
                foreach (ShaderNode node in graph.Graph.Nodes)
                {
                    string key = ShaderGraph.Key(node.Type, node.Index, node.Stage);
                    var built = new Node(g, node, key, Classify(node), _nodes.Count);
                    if (_byKey.TryAdd((g, key), built))
                    {
                        _nodes.Add(built);
                    }
                }

                foreach (ShaderLink link in graph.Graph.Links)
                {
                    string target = ShaderGraph.Key(link.Target.Type, link.Target.Index, link.Target.Stage);
                    (int, string, string) at = (g, target, link.Target.Variable);
                    if (!_into.TryGetValue(at, out List<ShaderLink>? list))
                    {
                        _into[at] = list = [];
                    }

                    list.Add(link);
                    if (!_intoNode.TryGetValue((g, target), out List<ShaderLink>? whole))
                    {
                        _intoNode[(g, target)] = whole = [];
                    }

                    whole.Add(link);
                    _read.Add((g, ShaderGraph.Key(link.Source.Type, link.Source.Index, link.Source.Stage)));
                }
            }

            foreach (Node node in _nodes)
            {
                if (node.Kind == NodeKind.Fragment && node.Block is { } fragment)
                {
                    // A SEMANTIC'S WRITERS ARE ITS PURE OUTPUTS: an inout port reads it first (FromVertexUV's semantics_data), and counting it a
                    // writer orders every reader after every other reader - cycles in base.fxgraph's pixel side.
                    foreach (ShaderPort port in fragment.Ports.Where(port => port.Direction == "out" && port.Semantic.Length > 0 && !port.Semantic.StartsWith("SV_", StringComparison.Ordinal)))
                    {
                        if (!_semanticWriters.TryGetValue(port.Semantic, out List<Node>? writers))
                        {
                            _semanticWriters[port.Semantic] = writers = [];
                        }

                        writers.Add(node);
                    }
                }
            }
        }

        private (NodeKind Kind, ShaderBlock? Block) Classify(ShaderNode node)
        {
            if (node.Type == "<Group>")
            {
                return (NodeKind.Group, null);
            }

            if (source.Fragments.TryGetValue(node.Type, out ShaderBlock? fragment))
            {
                return (NodeKind.Fragment, fragment);
            }

            if (source.Extensions.TryGetValue(node.Type, out ShaderBlock? written))
            {
                return (NodeKind.Write, written);
            }

            return node.Type.StartsWith("Input", StringComparison.Ordinal) && source.Extensions.TryGetValue(node.Type[5..], out ShaderBlock? read)
                ? (NodeKind.Read, read)
                : (NodeKind.Unknown, null);
        }

        /// <summary>The anchors in the stage order, each pulling in what it needs; the side effects nothing pulled in after.</summary>
        private void Order()
        {
            var anchors = _nodes
                .Where(node => node.Kind is NodeKind.Read or NodeKind.Write)
                .Select(node => (Node: node, Stage: StageOf(node)))
                .ToList();
            foreach ((Node node, int stage) in anchors.Where(one => one.Stage < 0 && SideOfStage(one.Node.Source.Stage) is null))
            {
                _unresolved.Add($"{node.Source.Type} at stage \"{node.Source.Stage}\" in {graphs[node.Graph].Path}: no stage of the {Said(side)}");
            }

            // A RENDER SHADER'S OUTPUTS ARE THE NODES OF ITS SIDE NOTHING READS (v0.1.440): base.fxgraph's VertexOutputPosition writes POSITION
            // and INSTANCEID, render_default's OutputVertexColor the vertex colour into semantics_data, and OutputVertexPosition,
            // OutputVertexTBN and OutputVertexUV are a macro alone (VERTEX_OUTPUT_POSITION...) that every output's port stands under - none
            // links on, so nothing pulled them in and the vertex shader wrote no position. Each runs at the latest stage of what it reads,
            // after that stage's reads and before its writes (CHOSEN: the generator's placement is not read); one reading nothing runs first.
            // The update's such nodes are its side effects alone, run as before.
            var ordered = anchors.Where(one => one.Stage >= 0).Select(one => (one.Node, one.Stage, Kind: one.Node.Kind == NodeKind.Read ? 0 : 2)).ToList();
            if (side != Side.Update)
            {
                var latest = new Dictionary<Node, int>();
                ordered.AddRange(_nodes
                    .Where(node => node.Kind == NodeKind.Fragment && SideOf(node) == side && !_read.Contains((node.Graph, node.Key)))
                    .Select(node => (node, Latest(node, latest), 1)));
            }

            foreach ((Node node, _, _) in ordered
                .OrderBy(one => one.Stage)
                .ThenBy(one => one.Node.Graph)
                .ThenBy(one => one.Kind)
                .ThenBy(one => one.Node.Order))
            {
                Emit(node);
            }

            foreach (Node node in _nodes.Where(node => node.Kind == NodeKind.Fragment && node.Block!.Has("sideeffects") && !node.Done && Allowed(node)))
            {
                Emit(node);
            }

            // A VERTEX SHADER WRITES WHAT THE PIXEL SHADER READS OF IT: every crossing's value into its interpolant.
            foreach (Crossing crossing in crossed)
            {
                if (_byKey.TryGetValue((crossing.Graph, crossing.Key), out Node? from))
                {
                    Emit(from);
                    string value = $"{from.Name}_{(from.Kind == NodeKind.Read ? "output" : crossing.Variable)}";
                    _main.Append(CultureInfo.InvariantCulture, $"    vout.{crossing.Name} = ({Stored(crossing.Type)})({value}); // to the pixel shader").Append('\n');
                }
            }

            foreach (Node node in _nodes.Where(node => node.Kind == NodeKind.Unknown))
            {
                _unresolved.Add($"node type {node.Source.Type} ({graphs[node.Graph].Path}): no source defines it");
            }
        }

        /// <summary>Whether a node is the pixel shader's or reads, through every node between, something that is.</summary>
        private bool ReadsPixel(Node node, HashSet<Node> seen)
        {
            if (SideOf(node) is { } own)
            {
                return own == Side.Pixel;
            }

            if (!seen.Add(node))
            {
                return false;
            }

            foreach (ShaderLink link in _intoNode.GetValueOrDefault((node.Graph, node.Key)) ?? [])
            {
                if (_byKey.TryGetValue((node.Graph, ShaderGraph.Key(link.Source.Type, link.Source.Index, link.Source.Stage)), out Node? from) && ReadsPixel(from, seen))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The latest of this shader's stages among the extension points a node reads, through every node between; -1 for none.</summary>
        private int Latest(Node node, Dictionary<Node, int> known)
        {
            if (known.TryGetValue(node, out int had))
            {
                return had;
            }

            known[node] = -1; // a cycle reads as nothing; Emit says so
            int latest = -1;
            foreach (ShaderLink link in _intoNode.GetValueOrDefault((node.Graph, node.Key)) ?? [])
            {
                if (_byKey.TryGetValue((node.Graph, ShaderGraph.Key(link.Source.Type, link.Source.Index, link.Source.Stage)), out Node? from))
                {
                    latest = Math.Max(latest, from.Kind is NodeKind.Read or NodeKind.Write ? StageOf(from) : Latest(from, known));
                }
            }

            known[node] = latest;
            return latest;
        }

        private int StageOf(Node node)
        {
            for (var at = 0; at < _stages.Count; at++)
            {
                if (string.Equals(_stages[at], node.Source.Stage, StringComparison.Ordinal))
                {
                    return at;
                }
            }

            return -1;
        }

        /// <summary>Which shader a stage belongs to; null for a stage of none.</summary>
        private static Side? SideOfStage(string stage)
            => VertexStages.Contains(stage, StringComparer.Ordinal) ? Side.Vertex
                : PixelStages.Contains(stage, StringComparer.Ordinal) ? Side.Pixel
                : Stages.Contains(stage, StringComparer.Ordinal) ? Side.Update
                : null;

        /// <summary>
        /// Which shader a node must run in: an extension point's by its stage, a fragment flagged for one shader alone by its flag; null for
        /// one that runs wherever it is read.
        /// </summary>
        private static Side? SideOf(Node node)
        {
            if (node.Kind is NodeKind.Read or NodeKind.Write)
            {
                return SideOfStage(node.Source.Stage);
            }

            if (node.Kind != NodeKind.Fragment || node.Block is not { } fragment)
            {
                return null;
            }

            bool compute = fragment.Has("compute"), vertex = fragment.Has("vertex"), pixel = fragment.Has("pixel");
            return (compute, vertex, pixel) switch
            {
                (true, false, false) => Side.Update,
                (false, true, false) => Side.Vertex,
                (false, false, true) => Side.Pixel,
                _ => null,
            };
        }

        /// <summary>Whether a node may run in this shader.</summary>
        private bool Allowed(Node node) => SideOf(node) is not { } only || only == side;

        private static string Said(Side kind) => kind switch
        {
            Side.Vertex => "vertex shader",
            Side.Pixel => "pixel shader",
            _ => "update shader",
        };

        /// <summary>Writes a node's code after everything it reads; nothing for a node already written or being written (a cycle, said).</summary>
        private void Emit(Node node)
        {
            if (node.Done)
            {
                return;
            }

            if (node.Busy)
            {
                _unresolved.Add($"a cycle through {node.Source.Type} ({graphs[node.Graph].Path})");
                return;
            }

            node.Busy = true;
            switch (node.Kind)
            {
                case NodeKind.Read:
                    EmitRead(node);
                    break;
                case NodeKind.Write:
                    EmitWrite(node);
                    break;
                case NodeKind.Fragment:
                    EmitFragment(node);
                    break;
                default:
                    break;
            }

            node.Busy = false;
            node.Done = true;
        }

        private string Extension(ShaderBlock block)
        {
            _extensions.TryAdd(block.Name, block);
            return "ext_" + Safe(block.Name);
        }

        private void EmitRead(Node node)
        {
            ShaderBlock block = node.Block!;
            string type = block.Value("type");
            if (Coded(block, "read") is { } read)
            {
                // AN EXTENSION POINT KEPT IN A SHARED STRUCT (AlbedoColor in surface_data) is read through its own code over that struct -
                // its writers pulled in first, outside the block.
                string bound = Bound(block, node);
                _main.Append(CultureInfo.InvariantCulture, $"    {type} {node.Name}_output = ({type})0; // {node.Source.Type} @{node.Source.Stage}, {Path.GetFileName(graphs[node.Graph].Path)}, its read code").Append('\n');
                _main.Append("    {\n").Append(bound).Append(CultureInfo.InvariantCulture, $"        {type} value = ({type})0;\n{read}\n        {node.Name}_output = value;\n    }}\n");
                _emitted++;
                return;
            }

            _main.Append(CultureInfo.InvariantCulture, $"    {type} {node.Name}_output = {Extension(block)}; // {node.Source.Type} @{node.Source.Stage}, {Path.GetFileName(graphs[node.Graph].Path)}").Append('\n');
            _emitted++;
        }

        private void EmitWrite(Node node)
        {
            ShaderBlock block = node.Block!;
            string type = block.Value("type");
            string? value = Input(node, "input", type);
            if (value is null)
            {
                return;
            }

            if (Coded(block, "write") is { } write)
            {
                string bound = Bound(block, node);
                _main.Append(CultureInfo.InvariantCulture, $"    {{ // {node.Source.Type} @{node.Source.Stage}, {Path.GetFileName(graphs[node.Graph].Path)}, its write code").Append('\n');
                _main.Append(bound).Append(CultureInfo.InvariantCulture, $"        {type} value = {value};\n{write}\n");
                foreach (ShaderPort port in block.Ports.Where(port => port.Direction is "out" or "inout" && port.Semantic.Length > 0))
                {
                    _main.Append(CultureInfo.InvariantCulture, $"        {Semantic(port)} = {port.Name};").Append('\n');
                }

                _main.Append("    }\n");
                _emitted++;
                return;
            }

            _main.Append(CultureInfo.InvariantCulture, $"    {Extension(block)} = {value}; // {node.Source.Type} @{node.Source.Stage}, {Path.GetFileName(graphs[node.Graph].Path)}").Append('\n');
            _emitted++;
        }

        /// <summary>An extension point's own read or write code where it has one that says something, else null.</summary>
        private static string? Coded(ShaderBlock block, string name)
            => block.Bodies.TryGetValue(name, out string? code) && code.Trim().Length > 0 && block.Ports.Count > 0 ? code.Trim() : null;

        /// <summary>An extension point's ports as locals over the shared variables their semantics name, their writers run first.</summary>
        private string Bound(ShaderBlock block, Node node)
        {
            var bound = new StringBuilder();
            foreach (ShaderPort port in block.Ports.Where(port => port.Semantic.Length > 0))
            {
                if (port.Direction is "in" or "inout")
                {
                    PullWriters(port.Semantic, node);
                }

                bound.Append(Guarded(port.Condition, $"        {port.Type} {port.Name} = {Semantic(port)};"));
            }

            return bound.ToString();
        }

        /// <summary>The fragments writing a shared semantic run before a node reading it - those of this shader alone.</summary>
        private void PullWriters(string semantic, Node reader)
        {
            if (semantic.StartsWith("SV_", StringComparison.Ordinal) || (side != Side.Update && Machine(semantic)))
            {
                return;
            }

            foreach (Node writer in _semanticWriters.GetValueOrDefault(semantic) ?? [])
            {
                // A WRITER OF NO SIDE THAT HANGS ON THE PIXEL SHADER is not the vertex shader's: render_default's SetRandomSeed #1 writes
                // RANDOM_SEED from the particle the pixel side read (ReadParticleData #1 after SetParticlePixelId) - a vertex shader cannot run it.
                if (writer != reader && Allowed(writer) && !(side == Side.Vertex && ReadsPixel(writer, [])))
                {
                    Emit(writer);
                }
            }
        }

        /// <summary>A fragment's code as a block: its inputs and uniforms as locals, its body word for word, its outputs copied out.</summary>
        private void EmitFragment(Node node)
        {
            ShaderBlock fragment = node.Block!;
            if (side == Side.Update && !fragment.Has("compute") && (fragment.Has("pixel") || fragment.Has("vertex")))
            {
                _unresolved.Add($"fragment {fragment.Name} is a {(fragment.Has("pixel") ? "pixel" : "vertex")} shader's, used in the update ({Path.GetFileName(graphs[node.Graph].Path)})");
            }
            else if (side != Side.Update && !Allowed(node))
            {
                _unresolved.Add($"fragment {fragment.Name} is not the {Said(side)}'s, used in it ({Path.GetFileName(graphs[node.Graph].Path)})");
            }

            // WHAT IT READS FIRST.
            var inputs = new List<(ShaderPort Port, string? Value)>();
            foreach (ShaderPort port in fragment.Ports)
            {
                if (port.Direction is "in" or "inout")
                {
                    string? value = Input(node, port.Name, port.Type);
                    if (value is null && port.Semantic.Length > 0 && !port.Semantic.StartsWith("SV_", StringComparison.Ordinal) && !(side != Side.Update && Machine(port.Semantic)))
                    {
                        PullWriters(port.Semantic, node);
                    }

                    inputs.Add((port, value));
                }
            }

            if (_fragmentNames.Add(fragment.Name))
            {
                _fragments.Add(fragment);
            }

            var code = new StringBuilder();
            code.Append(CultureInfo.InvariantCulture, $"    // {fragment.Name} #{node.Source.Index}{(node.Source.Stage.Length > 0 ? " @" + node.Source.Stage : string.Empty)}, {Path.GetFileName(graphs[node.Graph].Path)}").Append('\n');
            foreach (ShaderPort port in fragment.Ports.Where(port => port.Direction is "out" or "inout"))
            {
                code.Append(Declare(port.Type, $"{node.Name}_{port.Name}", null, "    ")).Append('\n');
            }

            code.Append("    {\n");
            code.Append(CultureInfo.InvariantCulture, $"#define UNIQUE_NODE_ID {NodeId(graphs[node.Graph].Path, node.Key)}u").Append('\n');
            foreach ((ShaderPort port, string? value) in inputs)
            {
                code.Append(Guarded(port.Condition, Declare(port.Type, port.Name, value ?? Unfed(node, port), "        ")));
            }

            foreach (ShaderPort port in fragment.Ports.Where(port => port.Direction == "out"))
            {
                code.Append(Guarded(port.Condition, Declare(port.Type, port.Name, null, "        ")));
            }

            var defined = new List<string>();
            for (var u = 0; u < fragment.Uniforms.Count; u++)
            {
                ShaderUniform uniform = fragment.Uniforms[u];
                if (uniform.Scope.Length == 0)
                {
                    string local = UniformLocal(node, uniform, u);
                    if (local.StartsWith("#define", StringComparison.Ordinal))
                    {
                        defined.AddRange(local.Split('\n').Select(line => line.Split(' ')[1]));
                        code.Append(local).Append('\n');
                    }
                    else
                    {
                        code.Append(Guarded(uniform.Condition, $"        {local}"));
                    }
                }
            }

            code.Append(fragment.Body.TrimEnd()).Append('\n');
            foreach (ShaderPort port in fragment.Ports.Where(port => port.Direction is "out" or "inout"))
            {
                string copy = $"        {node.Name}_{port.Name} = {port.Name};{(Resource(port.Type) is not null ? $" {node.Name}_{port.Name}_index = {port.Name}_index;" : string.Empty)}";
                if (side != Side.Update && port.Semantic.Length > 0 && Machine(port.Semantic))
                {
                    Outputs.TryAdd(port.Semantic, (port.Type, port.Condition));
                    copy += $" {(side == Side.Vertex ? "vout" : "pout")}.{Member(port.Semantic).Member} = ({Stored(port.Type)})({port.Name});";
                }
                else if (port.Semantic.Length > 0 && !port.Semantic.StartsWith("SV_", StringComparison.Ordinal))
                {
                    copy += $" {Semantic(port)} = {port.Name};";
                }

                code.Append(Guarded(port.Condition, copy));
            }

            foreach (string name in defined)
            {
                code.Append("#undef ").Append(name).Append('\n');
            }

            code.Append("#undef UNIQUE_NODE_ID\n    }\n");
            _main.Append(code);
            _emitted++;
        }

        /// <summary>A port no link feeds: its semantic's value, the shader's own input for an SV_ one, else nought (said).</summary>
        private string Unfed(Node node, ShaderPort port)
        {
            if (side != Side.Update && Machine(port.Semantic))
            {
                return MachineInput(port);
            }

            if (port.Semantic.StartsWith("SV_", StringComparison.Ordinal))
            {
                return "sv_" + port.Semantic[3..];
            }

            if (port.Semantic.Length > 0)
            {
                return Semantic(port);
            }

            if (port.Name is not ("in_connection" or "connection"))
            {
                _unresolved.Add($"{node.Source.Type}#{node.Source.Index}.{port.Name} is fed by no link: nought ({Path.GetFileName(graphs[node.Graph].Path)})");
            }

            return $"({port.Type})0";
        }

        /// <summary>
        /// A machine semantic read: a vertex shader's from the mesh (its vertex struct) or the system (its entry's parameters), a pixel shader's
        /// from the struct the vertex shader wrote or the system - SV_POSITION being that struct's place, as POSITION is the vertex side's.
        /// </summary>
        private string MachineInput(ShaderPort port)
        {
            string upper = port.Semantic.ToUpperInvariant();
            if (side == Side.Pixel && upper is "SV_POSITION" or "VPOS")
            {
                Inputs.TryAdd("SV_POSITION", ("float4", string.Empty));
                return $"(({port.Type})pin.position)";
            }

            if (upper.StartsWith("SV_", StringComparison.Ordinal))
            {
                _system[port.Semantic] = port.Type;
                return "sv_" + port.Semantic[3..];
            }

            if (upper == "VFACE")
            {
                _system["SV_IsFrontFace"] = "bool";
                return "(sv_IsFrontFace ? 1.0f : -1.0f)";
            }

            Inputs.TryAdd(port.Semantic, (port.Type, port.Condition));
            return $"(({port.Type})(vin.{Member(port.Semantic).Member}))".Replace("vin.", side == Side.Pixel ? "pin." : "vin.", StringComparison.Ordinal);
        }

        private string Semantic(ShaderPort port)
        {
            string name = "sem_" + Safe(port.Semantic);
            _semantics.TryAdd(name, port.Type);
            return name;
        }

        /// <summary>The value a node's input port is fed: the links into it, each component the link writes; null where none.</summary>
        private string? Input(Node node, string variable, string type)
        {
            if (!_into.TryGetValue((node.Graph, node.Key, variable), out List<ShaderLink>? links) || links.Count == 0)
            {
                return null;
            }

            if (links.Count == 1 && links[0].Target.Swizzle.Length == 0)
            {
                return Source(node.Graph, links[0].Source, 0);
            }

            // COMPONENTS: a temporary of the port's type, nought, with each link written into the components it names.
            var composed = new List<string>();
            foreach (ShaderLink link in links)
            {
                string? from = Source(node.Graph, link.Source, 0);
                if (from is null)
                {
                    continue;
                }

                composed.Add($"{(link.Target.Swizzle.Length == 0 ? "xyzw"[..Width(type)] : link.Target.Swizzle)}={from}");
            }

            string temp = $"{node.Name}_{Safe(variable)}_in";
            _main.Append(CultureInfo.InvariantCulture, $"    {type} {temp} = ({type})0;").Append('\n');
            foreach (string one in composed)
            {
                int eq = one.IndexOf('=', StringComparison.Ordinal);
                _main.Append(CultureInfo.InvariantCulture, $"    {temp}.{one[..eq]} = {one[(eq + 1)..]};").Append('\n');
            }

            return temp;
        }

        /// <summary>A link's source as an expression, written first where it is not yet; through a group's slots to what feeds them.</summary>
        private string? Source(int graph, ShaderEnd end, int depth)
        {
            string key = ShaderGraph.Key(end.Type, end.Index, end.Stage);
            if (!_byKey.TryGetValue((graph, key), out Node? from))
            {
                _unresolved.Add($"a link from {end.Type}#{end.Index}, no node of {Path.GetFileName(graphs[graph].Path)}");
                return null;
            }

            string swizzle = end.Swizzle.Length > 0 ? "." + end.Swizzle : string.Empty;
            switch (from.Kind)
            {
                case NodeKind.Group:
                    if (depth > 16 || !_into.TryGetValue((graph, key, end.Variable), out List<ShaderLink>? feeding) || feeding.Count == 0)
                    {
                        _unresolved.Add($"group slot {end.Variable} of {Path.GetFileName(graphs[graph].Path)} is fed by nothing");
                        return null;
                    }

                    string? fed = Source(graph, feeding[0].Source, depth + 1);
                    return fed is null ? null : swizzle.Length > 0 ? $"({fed}){swizzle}" : fed;
                case NodeKind.Unknown:
                    return null;
                default:
                    if (side == Side.Pixel && SideOf(from) == Side.Vertex)
                    {
                        return Cross(graph, key, from, end.Variable) is { } crossing ? $"((({crossing.Type})pin.{crossing.Name})){swizzle}" : null;
                    }

                    if (side == Side.Vertex && SideOf(from) == Side.Pixel)
                    {
                        _unresolved.Add($"{from.Source.Type} ({Path.GetFileName(graphs[graph].Path)}) is the pixel shader's, read by the vertex shader: nought");
                        return null;
                    }

                    Emit(from);
                    if (from.Kind == NodeKind.Fragment && from.Block is { } fragment
                        && !fragment.Ports.Any(port => port.Name == end.Variable && port.Direction is "out" or "inout")
                        && end.Variable.Contains("connection", StringComparison.Ordinal))
                    {
                        // EVERY NODE HAS A CONNECTION OUTPUT, ITS PLACE IN THE ORDER: base.fxgraph links VertexOutput's and PixelInput's
                        // `connection` though neither fragment declares one - the generator's own; nought, the node run before.
                        return "0.0f";
                    }

                    return $"{from.Name}_{(from.Kind == NodeKind.Read ? "output" : end.Variable)}{swizzle}";
            }
        }

        /// <summary>
        /// A local of a port's type: a value initialised to what it is fed or to nought; a texture or a sampler as the HLSL resource it is,
        /// with the <c>_index</c> beside a texture the engine's source builder generates for its bindless textures ("in and out is generated in
        /// shadersource builder", InputTexture) - nought, the tool drawing with Direct3D 11's bound textures.
        /// </summary>
        private static string Declare(string type, string name, string? value, string indent)
        {
            if (Resource(type) is not { } resource)
            {
                return $"{indent}{type} {name} = {value ?? $"({type})0"};";
            }

            string declared = value is null ? $"{indent}{resource} {name};" : $"{indent}{resource} {name} = {value};";
            if (resource == "SamplerState")
            {
                return declared;
            }

            string index = value is not null && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? value + "_index" : "0u";
            return $"{declared} uint {name}_index = {index};";
        }

        /// <summary>The HLSL resource a port's engine type is, or null for a value.</summary>
        private static string? Resource(string type) => type switch
        {
            "texture" => "Texture2D",
            "texture3d" => "Texture3D",
            "textureCube" => "TextureCube",
            "sampler" => "SamplerState",
            _ => null,
        };

        /// <summary>The crossing carrying a vertex node's output to this pixel shader, made the first time it is read.</summary>
        private Crossing? Cross(int graph, string key, Node from, string variable)
        {
            string type = from.Kind == NodeKind.Read
                ? from.Block!.Value("type")
                : from.Block?.Ports.FirstOrDefault(port => port.Name == variable && port.Direction is "out" or "inout")?.Type ?? string.Empty;
            if (type.Length == 0)
            {
                _unresolved.Add($"{from.Source.Type}.{variable} ({Path.GetFileName(graphs[graph].Path)}) crosses to the pixel shader with no type known: nought");
                return null;
            }

            string name = $"AUTO_S_{from.Name}_{Safe(from.Kind == NodeKind.Read ? "output" : variable)}";
            if (Crossings.Find(one => one.Name == name) is { } had)
            {
                return had;
            }

            var crossing = new Crossing(graph, key, from.Kind == NodeKind.Read ? "output" : variable, type, name);
            Crossings.Add(crossing);
            return crossing;
        }

        /// <summary>A node's own uniform as a local holding its parameter - the material's over the node's, else the declared default.</summary>
        private string UniformLocal(Node node, ShaderUniform uniform, int position)
        {
            ShaderValue own = position < node.Source.Parameters.Length ? node.Source.Parameters[position] : ShaderValue.Empty;
            ShaderValue said = node.Source.Custom.Length > 0
                && graphs[node.Graph].Custom.TryGetValue(node.Source.Custom, out ShaderValue[]? set)
                && position < set.Length
                    ? own.Under(set[position])
                    : own;
            switch (uniform.Type)
            {
                case "spline5":
                {
                    CurveKey[] keys = said.Curves?.GetValueOrDefault(string.Empty) ?? [];
                    if (keys.Length == 0)
                    {
                        _unresolved.Add($"{node.Source.Type} ({Path.GetFileName(graphs[node.Graph].Path)}) has no keys: a flat nought");
                    }

                    if (keys.Any(key => key.Kind is { Length: > 0 } kind && kind != "Linear"))
                    {
                        _unresolved.Add($"a Spline5 key of kind {keys.First(key => key.Kind != "Linear").Kind}: baked as a straight line (CHOSEN)");
                    }

                    (float[] polynoms, float[] tv) = Spline(keys, said.Variance ?? 0f);
                    string p(int s) => $"float4({Number(polynoms[s * 4])}, {Number(polynoms[(s * 4) + 1])}, {Number(polynoms[(s * 4) + 2])}, {Number(polynoms[(s * 4) + 3])})";
                    return $"spline5 {uniform.Name}; {uniform.Name}.segment_polynom[0] = {p(0)}; {uniform.Name}.segment_polynom[1] = {p(1)}; {uniform.Name}.segment_polynom[2] = {p(2)}; {uniform.Name}.segment_polynom[3] = {p(3)}; {uniform.Name}.time_variance = float4({Number(tv[0])}, {Number(tv[1])}, {Number(tv[2])}, {Number(tv[3])});";
                }

                // A CURVE OF THE GRAPH EDITOR IS A ROW OF THE ENGINE'S SPLINE LUT: color_spline_data reads splineGrayscale as one int (the row,
                // nought or more; under nought reads nought) and splineColour as an int4 of rows, R G B A. Each curve gets a row of its own.
                case "splineGrayscale":
                    return $"int {uniform.Name} = {Track(said, "A", node)};";
                case "splineColour":
                    return $"int4 {uniform.Name} = int4({Track(said, "R", node)}, {Track(said, "G", node)}, {Track(said, "B", node)}, {Track(said, "A", node)});";
                case "splineVariance":
                    // Which channels a SplineVariance parameter writes its two curves under is not read - no graph captured so far uses one.
                    _unresolved.Add($"{node.Source.Type} ({Path.GetFileName(graphs[node.Graph].Path)}): a splineVariance's channels are not read, both rows -1 (nought)");
                    return $"int2 {uniform.Name} = int2(-1, -1);";
                case "texture" or "texture3d" or "textureCube" or "sampler":
                {
                    string global = $"res_{node.Name}_{uniform.Name}";
                    _resources.Add((global, uniform.Type == "sampler" ? "sampler (its name is a string the graph reader drops)" : said.Path.Length > 0 ? said.Path : "no texture named"));
                    return uniform.Type == "sampler" ? $"#define {uniform.Name} {global}" : $"#define {uniform.Name} {global}\n#define {uniform.Name}_index 0u";
                }

                default:
                {
                    int width = Width(uniform.Type);
                    if (width == 0)
                    {
                        _unresolved.Add($"a node uniform of type {uniform.Type} ({node.Source.Type}): not filled");
                        return $"{uniform.Type} {uniform.Name} = ({uniform.Type})0;";
                    }

                    float[] numbers = said.Numbers.Length > 0 ? said.Numbers : Defaults(uniform);
                    var values = new string[width];
                    for (var i = 0; i < width; i++)
                    {
                        float v = i < numbers.Length ? numbers[i] : 0f;
                        values[i] = Base(uniform.Type) switch
                        {
                            "int" => ((int)v).ToString(CultureInfo.InvariantCulture),
                            "uint" => ((uint)Math.Max(0, v)).ToString(CultureInfo.InvariantCulture) + "u",
                            "bool" => v != 0 ? "true" : "false",
                            _ => Number(v),
                        };
                    }

                    return $"{uniform.Type} {uniform.Name} = {uniform.Type}({string.Join(", ", values)});";
                }
            }
        }

        /// <summary>The spline LUT row a curve's channel is given - a new row a channel the parameter writes, -1 (read as nought) where it writes none.</summary>
        private int Track(ShaderValue said, string channel, Node node)
        {
            if (said.Curves?.GetValueOrDefault(channel) is not { Length: > 0 } keys)
            {
                _unresolved.Add($"{node.Source.Type} ({Path.GetFileName(graphs[node.Graph].Path)}) writes no curve for {channel}: read as nought");
                return -1;
            }

            _tracks.Add(keys);
            return _tracks.Count - 1;
        }

        /// <summary>A node uniform's declared default: its fourth quoted word (label, least, most, default), split on spaces.</summary>
        private static float[] Defaults(ShaderUniform uniform)
            => uniform.Quoted.Count >= 4
                ? [.. uniform.Quoted[3].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f)]
                : [];

        /// <summary>The whole shader.</summary>
        /// <summary>The shader's text, with the other shader of the pass's fragment macros beside its own (<paramref name="shared"/>).</summary>
        public LinkedShader Write(IEnumerable<string> shared)
        {
            var hlsl = new StringBuilder();
            hlsl.Append(CultureInfo.InvariantCulture, $"// The engine's GPU particle {(side == Side.Update ? "update" : side == Side.Vertex ? "render pass's vertex shader" : "render pass's pixel shader")}, linked from its graphs by PoEformance's ParticleLinker.\n");
            foreach (string graph in graphs.Select(one => one.Path))
            {
                hlsl.Append("//   ").Append(graph).Append('\n');
            }

            // MACROS: the update shader's, then every fragment's.
            var macros = new Dictionary<string, string>(StringComparer.Ordinal);
            switch (side)
            {
                case Side.Vertex:
                    macros["VERTEX_SHADER"] = string.Empty;
                    break;
                case Side.Pixel:
                    macros["PIXEL_SHADER"] = string.Empty;
                    break;
                default:
                    macros["COMPUTE_SHADER"] = string.Empty;
                    macros["COMPUTE"] = string.Empty;
                    break;
            }

            foreach (string macro in passMacros)
            {
                int colon = macro.IndexOf(':', StringComparison.Ordinal);
                macros[colon < 0 ? macro : macro[..colon]] = colon < 0 ? string.Empty : macro[(colon + 1)..];
            }

            // EVERY FRAGMENT'S, THE OTHER SHADER'S OF THE PASS TOO: the generator compiles one program of a pass's graphs, so a macro a vertex
            // fragment declares (OutputVertexPosition's VERTEX_OUTPUT_POSITION) stands in the pixel shader, which reads world_position by it.
            foreach (IReadOnlyList<string> macroList in _fragments.Select(fragment => fragment.Macros).Append([.. shared]))
            {
                foreach (string macro in macroList)
                {
                    int colon = macro.IndexOf(':', StringComparison.Ordinal);
                    string name = colon < 0 ? macro : macro[..colon];
                    string value = colon < 0 ? string.Empty : macro[(colon + 1)..];
                    if (macros.TryGetValue(name, out string? had) && had != value)
                    {
                        _unresolved.Add($"macro {name} is {had} and {value} in two fragments: the first kept");
                        continue;
                    }

                    macros[name] = value;
                }
            }

            foreach ((string name, string value) in macros)
            {
                hlsl.Append("#define ").Append(name).Append(value.Length > 0 ? " " + value : string.Empty).Append('\n');
            }

            // THE GENERATOR'S OWN LINES (module+0x3194328 to +0x3194460, written by module+0x12E0300's preamble): the group size's defaults and
            // their product, then the vertex layout's macros.
            hlsl.Append("#ifndef GROUP_SIZE_X\n\t#define GROUP_SIZE_X 64\n#endif\n#ifndef GROUP_SIZE_Y\n\t#define GROUP_SIZE_Y  1\n#endif\n#ifndef GROUP_SIZE_Z\n\t#define GROUP_SIZE_Z  1\n#endif\n");
            hlsl.Append("#define GROUP_SIZE (GROUP_SIZE_X*GROUP_SIZE_Y*GROUP_SIZE_Z)\n");
            hlsl.Append(VertexMacros());

            // THE CURVES' TYPES AS THE GENERATOR DECLARES THEM ("int %s", "int2 %s", "int4 %s", module+0x3194AA8 to +0x3194AC0): a row of the
            // spline LUT, two for a variance, four for a colour.
            hlsl.Append("typedef int splineGrayscale;\ntypedef int2 splineVariance;\ntypedef int4 splineColour;\n");

            // THE INSTANCES' CONSTANTS: the generator writes a cbuffer of InstanceData[] followed by padding0, padding1, instance_count and
            // batch_size (module+0x31935F8 to +0x3193728). This tool dispatches one emitter at a time, so the two the sources read stand as
            // globals - CHOSEN, the instance array's per-object values the drawing's own constants.
            hlsl.Append("uint instance_count;\nuint batch_size;\n");

            // THE ENGINE'S PRELUDE AFTER THE MACROS: CommonDeclarations.inc, which it includes, declares the particle structs only under
            // GPU_PARTICLE_DRAW_CALL, a fragment's macro.
            if (prelude.Length > 0)
            {
                hlsl.Append(prelude.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd()).Append('\n');
            }
            else
            {
                _unresolved.Add("no prelude: the engine's DX11 text (ShaderPrelude) was not read, so its macros and sampler and struct declarations are missing");
            }

            // DECLARATIONS: every one the fragments include, through every include, an include before what includes it.
            var declared = new List<ShaderBlock>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShaderBlock fragment in _fragments)
            {
                foreach (string include in fragment.Includes)
                {
                    Declare(include, declared, seen);
                }
            }

            foreach (ShaderBlock block in _extensions.Values)
            {
                foreach (string include in block.Includes)
                {
                    Declare(include, declared, seen);
                }
            }

            // ONLY THE UNIFORMS THE CODE NAMES: the engine's global declarations hold every pass's buffers (scene_infos of SceneInfo, the
            // lights' and the spline LUT's), whose structs are its C++'s; a uniform no emitted line names is left out.
            var named = new HashSet<string>(StringComparer.Ordinal);
            Identifiers(_main.ToString(), named);
            foreach (ShaderBlock block in declared)
            {
                Identifiers(block.Body, named);
            }

            var uniforms = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShaderBlock block in declared)
            {
                hlsl.Append("// DECLARATIONS ").Append(block.Name).Append(" (").Append(block.Source).Append(")\n");
                foreach (string line in block.Header.Where(line => line.StartsWith("#define", StringComparison.Ordinal)))
                {
                    hlsl.Append(line).Append('\n');
                }

                foreach (ShaderUniform uniform in block.Uniforms)
                {
                    if (uniform.Stages.Length > 0 && !uniform.Stages.Split('|').Contains(side switch { Side.Vertex => "vs", Side.Pixel => "ps", _ => "cs" }, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    // A RENDER PASS DECLARES EVERY UNIFORM OF A PLAIN TYPE too: its sources' .hlsl includes read some (common.ffx's sh_atlas_size
                    // by SHSampledAtlas.hlsl), which no identifier here names; an unused one costs nothing.
                    if ((named.Contains(uniform.Name) || (side != Side.Update && Plain(uniform.Type))) && uniforms.Add(uniform.Name))
                    {
                        hlsl.Append(Guarded(uniform.Condition, GlobalUniform(uniform)));
                    }
                }

                hlsl.Append(block.Body.TrimEnd()).Append('\n');
            }

            foreach ((string name, string _) in _resources)
            {
                hlsl.Append(name.Contains("sampler", StringComparison.OrdinalIgnoreCase) ? "SamplerState " : "Texture2D ").Append(name).Append(";\n");
            }

            foreach ((string name, string type) in _semantics)
            {
                hlsl.Append("static ").Append(type).Append(' ').Append(name).Append(";\n");
            }

            string entry;
            if (side == Side.Update)
            {
                entry = UpdateEntry;
                hlsl.Append("\n[numthreads(GROUP_SIZE_X, GROUP_SIZE_Y, GROUP_SIZE_Z)]\n");
                hlsl.Append(CultureInfo.InvariantCulture, $"void {UpdateEntry}(uint3 sv_DispatchThreadID : SV_DispatchThreadID, uint3 sv_GroupThreadID : SV_GroupThreadID, uint3 sv_GroupID : SV_GroupID, uint sv_GroupIndex : SV_GroupIndex)\n{{\n");
            }
            else
            {
                // THE STRUCT BETWEEN THE SHADERS, written once both are linked (Render), and this shader's own: the mesh's vertex for the
                // vertex shader, the targets for the pixel shader.
                hlsl.Append('\n').Append(InterstagePlace).Append('\n');
                string own = side == Side.Vertex ? "VertexIn" : "PixelOut";
                hlsl.Append("struct ").Append(own).Append("\n{\n");
                var written = new HashSet<string>(StringComparer.Ordinal);
                foreach ((string semantic, (string type, string condition)) in side == Side.Vertex ? Inputs : Outputs)
                {
                    (string member, string bound) = Member(semantic);
                    if (written.Add(member))
                    {
                        hlsl.Append(Guarded(condition, $"    {Stored(type)} {member} : {(side == Side.Vertex ? semantic : bound)};"));
                    }
                }

                if (written.Count == 0)
                {
                    hlsl.Append(side == Side.Vertex ? "    uint unused : SV_VertexID;\n" : "    float4 target_none : SV_Target0;\n");
                }

                hlsl.Append("};\n");
                IEnumerable<string> system = _system.Select(one => $"{one.Value} sv_{one.Key[3..]} : {one.Key}");
                entry = side == Side.Vertex ? VertexEntry : PixelEntry;
                string input = side == Side.Vertex ? "VertexIn vin" : "Interstage pin";
                hlsl.Append(CultureInfo.InvariantCulture, $"\n{(side == Side.Vertex ? "Interstage" : "PixelOut")} {entry}({string.Join(", ", system.Prepend(input))})\n{{\n");
                hlsl.Append(side == Side.Vertex ? "    Interstage vout = (Interstage)0;\n" : "    PixelOut pout = (PixelOut)0;\n");
            }

            foreach (ShaderBlock block in _extensions.Values)
            {
                string type = block.Value("type");
                string variable = "ext_" + Safe(block.Name);
                string generate = block.Bodies.GetValueOrDefault("generate") ?? string.Empty;
                hlsl.Append(CultureInfo.InvariantCulture, $"    {type} {variable} = ({type})0;");
                if (generate.Trim().Length > 0)
                {
                    hlsl.Append(CultureInfo.InvariantCulture, $" {{ {type} value = ({type})0; {generate.Trim()} {variable} = value; }}");
                }

                hlsl.Append('\n');
            }

            hlsl.Append(_main);
            if (side != Side.Update)
            {
                hlsl.Append(side == Side.Vertex ? "    return vout;\n" : "    return pout;\n");
            }

            hlsl.Append("}\n");
            return new LinkedShader(hlsl.ToString(), entry, [.. _unresolved], _resources, _emitted, _fragments.Count, declared.Count, _tracks);
        }

        private void Declare(string name, List<ShaderBlock> declared, HashSet<string> seen)
        {
            if (!seen.Add(name))
            {
                return;
            }

            if (!source.Declarations.TryGetValue(name, out ShaderBlock? block))
            {
                _unresolved.Add($"DECLARATIONS {name}: no source defines it");
                return;
            }

            foreach (string include in block.Includes)
            {
                Declare(include, declared, seen);
            }

            declared.Add(block);
        }

        /// <summary>A declarations' uniform as a global of the compute shader: a resource as its HLSL type, a value as a global (FXC's $Globals).</summary>
        private string GlobalUniform(ShaderUniform uniform)
        {
            string type = uniform.Type;
            string hlsl = type switch
            {
                _ when type.StartsWith("rw_structBuffer<", StringComparison.Ordinal) => "RWStructuredBuffer" + type["rw_structBuffer".Length..],
                _ when type.StartsWith("structBuffer<", StringComparison.Ordinal) => "StructuredBuffer" + type["structBuffer".Length..],
                "rw_byteBuffer" => "RWByteAddressBuffer",
                "byteBuffer" => "ByteAddressBuffer",
                "texture" => "Texture2D",
                "texture3d" => "Texture3D",
                "textureCube" => "TextureCube",
                "texture2darray" => "Texture2DArray",
                "sampler" => "SamplerState",
                _ when type.StartsWith("texture<", StringComparison.Ordinal) => "Texture2D" + type["texture".Length..],
                _ when type.StartsWith("texelBuffer<", StringComparison.Ordinal) => "Buffer" + type["texelBuffer".Length..],
                _ when type.StartsWith("rw_texelBuffer<", StringComparison.Ordinal) => "RWBuffer" + type["rw_texelBuffer".Length..],
                _ when type.StartsWith("rw_texture<", StringComparison.Ordinal) => "RWTexture2D" + type["rw_texture".Length..],
                _ => type,
            };
            if (hlsl == type && Width(type) == 0 && !type.Contains("x", StringComparison.Ordinal))
            {
                _unresolved.Add($"uniform {uniform.Name} of type {type}: written as is");
            }

            return $"{hlsl} {uniform.Name};";
        }

        /// <summary>Whether a uniform's type is one HLSL knows without the engine's own structs: a scalar, a vector, a matrix, a texture or a sampler.</summary>
        private static bool Plain(string type)
            => Width(type) > 0 || Resource(type) is not null || type is "texture2darray" || (type.Length == 8 && type.StartsWith("float", StringComparison.Ordinal) && type[6] == 'x');

        /// <summary>Every identifier a piece of code names.</summary>
        private static void Identifiers(string code, HashSet<string> into)
        {
            var at = 0;
            while (at < code.Length)
            {
                char c = code[at];
                if (char.IsAsciiLetter(c) || c == '_')
                {
                    int start = at;
                    while (at < code.Length && (char.IsAsciiLetterOrDigit(code[at]) || code[at] == '_'))
                    {
                        at++;
                    }

                    into.Add(code[start..at]);
                }
                else
                {
                    at++;
                }
            }
        }

        /// <summary>A line inside the preprocessor lines it stood inside where written.</summary>
        internal static string Guarded(string condition, string line)
        {
            if (condition.Length == 0)
            {
                return line + "\n";
            }

            int opened = condition.Split('\n').Count(one => one.StartsWith("#if", StringComparison.Ordinal));
            return condition + "\n" + line + "\n" + string.Concat(Enumerable.Repeat("#endif\n", opened));
        }

        /// <summary>How many components a scalar or vector type has; nought for anything else.</summary>
        private static int Width(string type)
        {
            string bare = Base(type);
            if (bare.Length == 0)
            {
                return 0;
            }

            string rest = type[bare.Length..];
            return rest.Length == 0 ? 1 : rest.Length == 1 && rest[0] is >= '2' and <= '4' ? rest[0] - '0' : 0;
        }

        private static string Base(string type)
        {
            foreach (string bare in new[] { "float", "half", "uint", "int", "bool" })
            {
                if (type.StartsWith(bare, StringComparison.Ordinal))
                {
                    return bare;
                }
            }

            return string.Empty;
        }

        /// <summary>UNIQUE_NODE_ID: FNV-1a of the graph's path and the node's key - CHOSEN, the engine's own is its compiler's.</summary>
        private static uint NodeId(string path, string key)
        {
            uint hash = 2166136261;
            foreach (char c in path.ToLowerInvariant() + "|" + key)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }

        internal static string Safe(string name)
        {
            var safe = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                safe.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
            }

            return safe.ToString();
        }

        private enum NodeKind
        {
            Unknown,
            Fragment,
            Read,
            Write,
            Group,
        }

        private sealed class Node(int graph, ShaderNode source, string key, (NodeKind Kind, ShaderBlock? Block) classified, int order)
        {
            public int Graph { get; } = graph;

            public ShaderNode Source { get; } = source;

            public string Key { get; } = key;

            public NodeKind Kind { get; } = classified.Kind;

            public ShaderBlock? Block { get; } = classified.Block;

            public int Order { get; } = order;

            public string Name { get; } = string.Create(CultureInfo.InvariantCulture, $"n{graph}_{order}");

            public bool Busy { get; set; }

            public bool Done { get; set; }
        }
    }
}
