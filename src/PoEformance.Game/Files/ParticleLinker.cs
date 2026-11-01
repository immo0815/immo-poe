using System.Globalization;
using System.Text;

namespace PoEformance.Game.Files;

/// <summary>One graph of a shader's list, with the parameters its material sets on it.</summary>
/// <param name="Path">The graph's path.</param>
/// <param name="Graph">The graph read.</param>
/// <param name="Custom">The material's parameters by the custom names the graph's nodes expose; empty for an engine graph.</param>
public sealed record LinkedGraph(string Path, ShaderGraph Graph, IReadOnlyDictionary<string, ShaderValue[]> Custom);

/// <summary>A shader linked from the engine's fragments, and everything the link could not settle.</summary>
/// <param name="Hlsl">The HLSL, whole: the macros, the engine's structs as the sources use them, the declarations, the entry point.</param>
/// <param name="Entry">The entry point's name.</param>
/// <param name="Unresolved">What the link left out or stood in for, each once - a node type no source defines, an input no link feeds, an engine type it cannot fill.</param>
/// <param name="Resources">The textures and samplers the nodes name, each a global of the HLSL the drawing must bind: its name, then what fills it.</param>
/// <param name="Nodes">How many nodes were emitted.</param>
/// <param name="Fragments">How many distinct fragments their code came from.</param>
/// <param name="Declarations">How many declarations were included.</param>
public sealed record LinkedShader(string Hlsl, string Entry, IReadOnlyList<string> Unresolved, IReadOnlyList<(string Name, string Filled)> Resources, int Nodes, int Fragments, int Declarations);

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
/// (the engine's value is set by its graph compiler and read in no file); COMPUTE_SHADER and COMPUTE both defined for the update shader (the
/// sources test both); the engine's structs (ParticleData, ParticleEmitterData, BoneData, spline5) written from the fields the sources use,
/// in an order of this tool's own (they live in its own buffers); a Spline5 parameter baked from its keys as straight lines between them
/// (<see cref="Spline"/>; the engine's baking is its C++'s).
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

    /// <summary>
    /// The engine's structs no source defines, written from the fields the sources use (WriteParticleData and ReadParticleData for ParticleData;
    /// every <c>particles_emitter_data[...]</c> and <c>emitter_bones_data[...]</c> read for the others; SampleSpline5's for spline5) - each
    /// field's type from what is done with it (UnpackHalf2 takes a uint, UnpackHalf4 a uint2). The ORDER is this tool's: the buffers are its own.
    /// </summary>
    public const string Prelude = """
        struct ParticleData
        {
            float3 pos;
            float phase;
            float4 rotation;
            float3 scale;
            float custom_data;
            uint2 angular_velocity;
            uint2 color;
            uint2 velocity;
            uint2 uniform_scale;
            uint2 along_direction;
            uint emitter_uid;
            uint size_mass;
            uint spawn_uvs;
            uint emitter_phase;
            uint bone_frac;
            uint kinematic_velocity;
        };

        struct ParticleEmitterData
        {
            float3 translation;
            float time;
            float4 rotation;
            float3 scale;
            float prev_time;
            float3 last_translation;
            float emitter_duration;
            float4 last_rotation;
            float3 last_scale;
            uint particles_start;
            float3 inverse_scale;
            uint bones;
            float3 last_inverse_scale;
            uint culling_tag;
            uint2 custom_data_a;
            uint2 custom_data_b;
            uint lock_flags;
            uint speed_factor;
            uint dead_time;
            uint culling_aggression;
        };

        struct BoneData
        {
            float4 pos;
            float4 prev_pos;
        };

        struct spline5
        {
            float4 segment_polynom[4];
            float4 time_variance;
        };
        """;

    /// <summary>Links the update compute shader of an emitter from its graphs, in the composer's order.</summary>
    /// <param name="source">The engine's shader sources.</param>
    /// <param name="graphs">The graphs, the update material's own last - <see cref="ParticleComposition"/>'s Update list read.</param>
    /// <param name="prelude">The engine's DX11 prelude (<see cref="ShaderPrelude"/>), put first; null or empty where none was read, said.</param>
    public static LinkedShader Update(ShaderSource source, IReadOnlyList<LinkedGraph> graphs, string? prelude = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(graphs);
        return new Link(source, graphs, prelude ?? string.Empty).Run();
    }

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
    private sealed class Link(ShaderSource source, IReadOnlyList<LinkedGraph> graphs, string prelude)
    {
        private readonly List<Node> _nodes = [];
        private readonly Dictionary<(int Graph, string Key), Node> _byKey = [];
        private readonly Dictionary<(int Graph, string Key, string Variable), List<ShaderLink>> _into = [];
        private readonly StringBuilder _main = new();
        private readonly SortedSet<string> _unresolved = new(StringComparer.Ordinal);
        private readonly List<(string Name, string Filled)> _resources = [];
        private readonly Dictionary<string, ShaderBlock> _extensions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _semantics = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Node>> _semanticWriters = new(StringComparer.Ordinal);
        private readonly List<ShaderBlock> _fragments = [];
        private readonly HashSet<string> _fragmentNames = new(StringComparer.Ordinal);
        private int _emitted;

        public LinkedShader Run()
        {
            Gather();
            Order();
            return Write();
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
                }
            }

            foreach (Node node in _nodes)
            {
                if (node.Kind == NodeKind.Fragment && node.Block is { } fragment)
                {
                    foreach (ShaderPort port in fragment.Ports.Where(port => port.Direction is "out" or "inout" && port.Semantic.Length > 0 && !port.Semantic.StartsWith("SV_", StringComparison.Ordinal)))
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
            foreach ((Node node, int stage) in anchors.Where(one => one.Stage < 0))
            {
                _unresolved.Add($"{node.Source.Type} at stage \"{node.Source.Stage}\" in {graphs[node.Graph].Path}: no stage of the update shader");
            }

            foreach ((Node node, _) in anchors
                .Where(one => one.Stage >= 0)
                .OrderBy(one => one.Stage)
                .ThenBy(one => one.Node.Graph)
                .ThenBy(one => one.Node.Kind == NodeKind.Read ? 0 : 1)
                .ThenBy(one => one.Node.Order))
            {
                Emit(node);
            }

            foreach (Node node in _nodes.Where(node => node.Kind == NodeKind.Fragment && node.Block!.Has("sideeffects") && !node.Done))
            {
                Emit(node);
            }

            foreach (Node node in _nodes.Where(node => node.Kind == NodeKind.Unknown))
            {
                _unresolved.Add($"node type {node.Source.Type} ({graphs[node.Graph].Path}): no source defines it");
            }
        }

        private static int StageOf(Node node)
        {
            for (var at = 0; at < Stages.Count; at++)
            {
                if (string.Equals(Stages[at], node.Source.Stage, StringComparison.Ordinal))
                {
                    return at;
                }
            }

            return -1;
        }

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
            if (block.Bodies.ContainsKey("read"))
            {
                _unresolved.Add($"extension point {block.Name} is read through its own code (read:), which the update shader does not bind");
            }

            string type = block.Value("type");
            _main.Append(CultureInfo.InvariantCulture, $"    {type} {node.Name}_output = {Extension(block)}; // {node.Source.Type} @{node.Source.Stage}, {Path.GetFileName(graphs[node.Graph].Path)}").Append('\n');
            _emitted++;
        }

        private void EmitWrite(Node node)
        {
            ShaderBlock block = node.Block!;
            string type = block.Value("type");
            string? value = Input(node, "input", type);
            string variable = Extension(block);
            if (value is not null)
            {
                _main.Append(CultureInfo.InvariantCulture, $"    {variable} = {value}; // {node.Source.Type} @{node.Source.Stage}, {Path.GetFileName(graphs[node.Graph].Path)}").Append('\n');
                _emitted++;
            }
        }

        /// <summary>A fragment's code as a block: its inputs and uniforms as locals, its body word for word, its outputs copied out.</summary>
        private void EmitFragment(Node node)
        {
            ShaderBlock fragment = node.Block!;
            if (!fragment.Has("compute") && (fragment.Has("pixel") || fragment.Has("vertex")))
            {
                _unresolved.Add($"fragment {fragment.Name} is a {(fragment.Has("pixel") ? "pixel" : "vertex")} shader's, used in the update ({Path.GetFileName(graphs[node.Graph].Path)})");
            }

            // WHAT IT READS FIRST.
            var inputs = new List<(ShaderPort Port, string? Value)>();
            foreach (ShaderPort port in fragment.Ports)
            {
                if (port.Direction is "in" or "inout")
                {
                    string? value = Input(node, port.Name, port.Type);
                    if (value is null && port.Semantic.Length > 0 && !port.Semantic.StartsWith("SV_", StringComparison.Ordinal))
                    {
                        foreach (Node writer in _semanticWriters.GetValueOrDefault(port.Semantic) ?? [])
                        {
                            if (writer != node)
                            {
                                Emit(writer);
                            }
                        }
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
                code.Append(CultureInfo.InvariantCulture, $"    {port.Type} {node.Name}_{port.Name} = ({port.Type})0;").Append('\n');
            }

            code.Append("    {\n");
            code.Append(CultureInfo.InvariantCulture, $"#define UNIQUE_NODE_ID {NodeId(graphs[node.Graph].Path, node.Key)}u").Append('\n');
            foreach ((ShaderPort port, string? value) in inputs)
            {
                code.Append(Guarded(port.Condition, $"        {port.Type} {port.Name} = {value ?? Unfed(node, port)};"));
            }

            foreach (ShaderPort port in fragment.Ports.Where(port => port.Direction == "out"))
            {
                code.Append(Guarded(port.Condition, $"        {port.Type} {port.Name} = ({port.Type})0;"));
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
                        defined.Add(uniform.Name);
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
                string copy = $"        {node.Name}_{port.Name} = {port.Name};";
                if (port.Semantic.Length > 0 && !port.Semantic.StartsWith("SV_", StringComparison.Ordinal))
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
                    Emit(from);
                    return $"{from.Name}_{(from.Kind == NodeKind.Read ? "output" : end.Variable)}{swizzle}";
            }
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

                case "texture" or "texture3d" or "textureCube" or "sampler":
                {
                    string global = $"res_{node.Name}_{uniform.Name}";
                    _resources.Add((global, uniform.Type == "sampler" ? "sampler (its name is a string the graph reader drops)" : said.Path.Length > 0 ? said.Path : "no texture named"));
                    return $"#define {uniform.Name} {global}";
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

        /// <summary>A node uniform's declared default: its fourth quoted word (label, least, most, default), split on spaces.</summary>
        private static float[] Defaults(ShaderUniform uniform)
            => uniform.Quoted.Count >= 4
                ? [.. uniform.Quoted[3].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f)]
                : [];

        /// <summary>The whole shader.</summary>
        private LinkedShader Write()
        {
            var hlsl = new StringBuilder();
            hlsl.Append("// The engine's GPU particle update, linked from its graphs by PoEformance's ParticleLinker.\n");
            foreach (string graph in graphs.Select(one => one.Path))
            {
                hlsl.Append("//   ").Append(graph).Append('\n');
            }

            // THE ENGINE'S PRELUDE FIRST, as it compiles every shader.
            if (prelude.Length > 0)
            {
                hlsl.Append(prelude.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd()).Append('\n');
            }
            else
            {
                _unresolved.Add("no prelude: the engine's DX11 text (ShaderPrelude) was not read, so its macros and sampler and struct declarations are missing");
            }

            // MACROS: the update shader's, then every fragment's.
            var macros = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["COMPUTE_SHADER"] = string.Empty,
                ["COMPUTE"] = string.Empty,
            };
            foreach (ShaderBlock fragment in _fragments)
            {
                foreach (string macro in fragment.Macros)
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

            hlsl.Append("#ifndef GROUP_SIZE_X\n#define GROUP_SIZE_X 64\n#endif\n#ifndef GROUP_SIZE_Y\n#define GROUP_SIZE_Y 1\n#endif\n#ifndef GROUP_SIZE_Z\n#define GROUP_SIZE_Z 1\n#endif\n");
            hlsl.Append(Prelude.Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\n');

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
                    if (uniform.Stages.Length > 0 && !uniform.Stages.Split('|').Contains("cs", StringComparer.Ordinal))
                    {
                        continue;
                    }

                    if (named.Contains(uniform.Name) && uniforms.Add(uniform.Name))
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

            hlsl.Append("\n[numthreads(GROUP_SIZE_X, GROUP_SIZE_Y, GROUP_SIZE_Z)]\n");
            hlsl.Append(CultureInfo.InvariantCulture, $"void {UpdateEntry}(uint3 sv_DispatchThreadID : SV_DispatchThreadID, uint3 sv_GroupThreadID : SV_GroupThreadID, uint3 sv_GroupID : SV_GroupID, uint sv_GroupIndex : SV_GroupIndex)\n{{\n");
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

            hlsl.Append(_main).Append("}\n");
            return new LinkedShader(hlsl.ToString(), UpdateEntry, [.. _unresolved], _resources, _emitted, _fragments.Count, declared.Count);
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
        private static string Guarded(string condition, string line)
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
