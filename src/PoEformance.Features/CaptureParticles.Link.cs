using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

public static partial class CaptureParticles
{
    /// <summary>
    /// Section 6: every emitter's shaders as the engine composes them (ParticleComposition, read from its code) and its update compute shader
    /// linked from those graphs and the sources (ParticleLinker) - what the link left out or stood in for, the resources it names, and the
    /// HLSL whole under <c>##### linked/...hlsl</c>, so the replay and a compiler read it back. Shared by the part and its offline reader.
    /// </summary>
    /// <param name="text">A file's text by path - the install's at the press, the capture's whole files offline; null where it has none.</param>
    /// <param name="source">The shader sources read.</param>
    /// <param name="effects">The .pet files to link, in order.</param>
    /// <param name="body">Where the section goes.</param>
    /// <param name="prelude">The engine's DX11 prelude (ShaderPrelude), or null.</param>
    /// <returns>A line saying how many emitters were linked and with how much left unresolved.</returns>
    public static string Link(Func<string, string?> text, ShaderSource source, IEnumerable<string> effects, StringBuilder body, string? prelude)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(body);
        body.AppendLine().AppendLine("=== 6. each emitter's shaders as the engine composes them - the engine's own graphs before the materials, READ from its code (ParticleComposition) - and its update compute shader linked from those graphs and the sources (ParticleLinker): what the link left out or stood in for, the resources it names, then the HLSL whole");
        body.Append(prelude is { Length: > 0 } ? $"prelude: the engine's DX11 text, {prelude.Length:N0} characters ({PreludePath} below)" : "prelude: none read").AppendLine();
        body.Append(CultureInfo.InvariantCulture, $"sources: {source.Fragments.Count} fragments, {source.Declarations.Count} declarations, {source.Extensions.Count} extension points, {source.Groups.Count} groups{(source.Twice.Count > 0 ? $"; defined twice, the first kept: {string.Join(", ", source.Twice.Take(12))}" : string.Empty)}").AppendLine();

        var linked = new List<(string Path, string Text, string Why)>();
        int emitters = 0, unresolved = 0;
        foreach (string effect in effects)
        {
            IReadOnlyList<ParticleEmitter> read = text(effect) is { } pet ? ParticleEffect.Read(pet) : [];
            for (var e = 0; e < read.Count; e++)
            {
                ParticleEmitter emitter = read[e];
                ParticleShaders shaders = ParticleComposition.Of(emitter);
                body.AppendLine().Append(CultureInfo.InvariantCulture, $"--- {effect} gpu_emitters[{e}]: count {(emitter.CountType.Length > 0 ? emitter.CountType : "-")}, group {emitter.GroupSize}, mesh {(emitter.Mesh.Length > 0 ? emitter.Mesh : "none")}, {emitter.Passes.Count} passes").AppendLine();
                body.Append("  update\t").AppendLine(string.Join(" -> ", shaders.Update.Select(Short)));
                if (shaders.Sort.Count > 0)
                {
                    body.Append("  sort\t").AppendLine(string.Join(" -> ", shaders.Sort.Select(Short)));
                }

                for (var p = 0; p < shaders.Passes.Count; p++)
                {
                    body.Append(CultureInfo.InvariantCulture, $"  pass {p} ({emitter.Passes[p].FaceLock}{(emitter.Passes[p].Optional ? ", optional" : string.Empty)})\t").AppendLine(string.Join(" -> ", shaders.Passes[p].Select(Short)));
                }

                foreach (string open in shaders.Unread)
                {
                    body.Append("  NOT READ\t").AppendLine(open);
                }

                // THE UPDATE'S GRAPHS: an engine graph as it is, a material's instances each with what the material sets on it.
                var graphs = new List<LinkedGraph>();
                foreach (string path in shaders.Update)
                {
                    if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    {
                        string? material = text(path);
                        IReadOnlyList<ShaderInstance> instances = material is null ? [] : ShaderGraph.Instances(Encoding.UTF8.GetBytes(material));
                        if (instances.Count == 0)
                        {
                            body.Append("  missing\t").Append(path).AppendLine(" (the material, or its graph instances)");
                        }

                        foreach (ShaderInstance instance in instances)
                        {
                            Add(instance.Parent, instance.Custom);
                        }
                    }
                    else
                    {
                        Add(path, new Dictionary<string, ShaderValue[]>(StringComparer.Ordinal));
                    }
                }

                void Add(string path, IReadOnlyDictionary<string, ShaderValue[]> custom)
                {
                    ShaderGraph graph = text(path) is { } json ? ShaderGraph.Read(Encoding.UTF8.GetBytes(json)) : ShaderGraph.None;
                    if (!graph.Ready)
                    {
                        body.Append("  missing\t").AppendLine(path);
                        return;
                    }

                    graphs.Add(new LinkedGraph(path, graph, custom));
                }

                LinkedShader shader = ParticleLinker.Update(source, graphs, prelude);
                emitters++;
                unresolved += shader.Unresolved.Count;
                body.Append(CultureInfo.InvariantCulture, $"  linked\t{graphs.Count} graphs, {shader.Nodes} nodes from {shader.Fragments} fragments, {shader.Declarations} declarations, {shader.Hlsl.Length:N0} characters of HLSL, {shader.Unresolved.Count} unresolved, {shader.Resources.Count} resources").AppendLine();
                foreach (string open in shader.Unresolved)
                {
                    body.Append("  unresolved\t").AppendLine(open);
                }

                foreach ((string name, string filled) in shader.Resources)
                {
                    body.Append("  resource\t").Append(name).Append('\t').AppendLine(filled);
                }

                linked.Add(($"linked/{Path.GetFileNameWithoutExtension(effect)}-{e}.update.hlsl", shader.Hlsl, $"{effect} gpu_emitters[{e}]'s update, entry {shader.Entry}"));
            }
        }

        body.AppendLine();
        CaptureLightExtras.Wholes(linked, LinkedChars, LinkedChars, "linked shaders", body);
        return string.Create(CultureInfo.InvariantCulture, $"{emitters} emitters' updates linked, {unresolved} unresolved in all");
    }

    /// <summary>The name the engine's DX11 prelude is written whole under, as the offline reader finds it.</summary>
    public const string PreludePath = "program/shader-prelude-dx11.hlsl";

    /// <summary>The characters the linked shaders may take whole.</summary>
    private const int LinkedChars = 8 * 1024 * 1024;

    /// <summary>A graph's path short: its name, or the folder's last part and its name for an engine graph.</summary>
    private static string Short(string path)
        => path.StartsWith(ParticleComposition.Folder, StringComparison.OrdinalIgnoreCase) ? path[ParticleComposition.Folder.Length..] : path;
}
