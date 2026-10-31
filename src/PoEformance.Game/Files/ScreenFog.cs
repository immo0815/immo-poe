using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace PoEformance.Game.Files;

/// <summary>
/// The screen-space fog an area's .env puts on the player: its weather attachment's render pass compiled as a screen program
/// (ShadeProgram.CompileScreen) with its textures in hand, the .env's fog colour and shape, and the environment's light its layers take.
/// </summary>
/// <remarks>
/// THE ATTACHMENT'S OWN FILES, hop by hop as the game names them (v0.1.380's capture, light-extras.txt section 7): the .env's
/// <c>area.player_environment_ao</c> is an object whose AnimatedRender lists its render passes (<c>RenderPasses</c>, a JSON payload: each pass
/// <c>is_main</c>, a <c>type</c> - "Append" - and a material <c>filename</c>), and the pass's material is the fog. The object's FixedMesh
/// (<c>screenSpaceWeatherEffect.fmt</c>) is drawn by the pass over the whole screen; its own material (<c>SSTexturec.mat</c>) is not read here,
/// and the line under the picture says so.
///
/// DRAWN ON THE CARD ALONE (DECIDED from the live client) - see ModelGpu's fog pass.
/// </remarks>
public sealed class ScreenFog
{
    private ScreenFog(string attachment, ShadeProgram? program, Vector4 colour, Vector2 shape, Vector3 environment, IReadOnlyList<string> said)
    {
        Attachment = attachment;
        Program = program;
        Colour = colour;
        Shape = shape;
        Environment = environment;
        Said = said;
    }

    /// <summary>The attachment's path, or empty.</summary>
    public string Attachment { get; }

    /// <summary>The pass's screen program with its textures in hand, or null where there is none to draw.</summary>
    public ShadeProgram? Program { get; }

    /// <summary>The pass's ssf_color - EnvironmentSettings.FogColour, nought where the .env sets none.</summary>
    public Vector4 Colour { get; }

    /// <summary>The pass's ssf_disperse_radius and ssf_feathering - EnvironmentSettings.FogShape, nought where the .env sets none.</summary>
    public Vector2 Shape { get; }

    /// <summary>
    /// GetTotalEnvLight (lighting.ffx): the diffuse and the specular cube read along (0, 1, 0) at level 10 - each file's last, a card clamping the
    /// level to it - times cube_brightness.x, the .env's env_brightness.
    /// </summary>
    /// <remarks>
    /// THE CUBES' OWN WAY, NOT TURNED: the fragment hands SAMPLE_TEXCUBELOD the direction (0, 1, 0) as it stands, where a surface's light is
    /// read along its normal turned by env_map_rotation. The GI's share (<c>cube_brightness.w</c>, defined under GI) is not added - the cube's light
    /// is drawn without it everywhere in this tool.
    /// </remarks>
    public Vector3 Environment { get; }

    /// <summary>What was read and what came of it, a line each - for the line under the picture and a capture.</summary>
    public IReadOnlyList<string> Said { get; }

    /// <summary>Whether there is a fog to draw.</summary>
    public bool Ready => Program is { Bound: true };

    /// <summary>No fog, and why.</summary>
    public static ScreenFog None(string why) => new(string.Empty, null, default, default, default, [why]);

    /// <summary>A fog of a screen program already in hand - its textures bound - with the pass's values: what a test draws with textures of its own.</summary>
    /// <param name="program">The screen program, bound (ShadeProgram.With).</param>
    /// <param name="colour">The pass's ssf_color.</param>
    /// <param name="shape">The pass's ssf_disperse_radius and ssf_feathering.</param>
    /// <param name="environment">GetTotalEnvLight.</param>
    public static ScreenFog Made(ShadeProgram program, Vector4 colour, Vector2 shape, Vector3 environment)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new ScreenFog(string.Empty, program, colour, shape, environment, ["screen-space fog: a program in hand"]);
    }

    /// <summary>The fog an environment names, read from the install, or none and why. Never throws.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="environment">The area's environment.</param>
    public static ScreenFog Of(Func<string, byte[]?> read, EnvironmentSettings environment)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(environment);

        // ON THE DRAWING THREAD, over the game's own files: whatever they do to a reader is a fog not drawn, said, never the frame lost.
        try
        {
            return Read(read, environment);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            return None("screen-space fog: its files would not read - " + exception.Message);
        }
    }

    private static ScreenFog Read(Func<string, byte[]?> read, EnvironmentSettings environment)
    {
        string attachment = environment.PlayerEnvironmentAo.Replace('\\', '/').Trim();
        if (attachment.Length == 0)
        {
            return None("screen-space fog: the area's .env names no player_environment_ao");
        }

        var said = new List<string>();
        IReadOnlyList<string> passes = Passes(read, attachment, said);
        if (passes.Count == 0)
        {
            said.Insert(0, $"screen-space fog: {Named(attachment)} lists no render pass");
            return new ScreenFog(attachment, null, default, default, default, said);
        }

        // THE FIRST PASS THAT IS A SCREEN PROGRAM, the main one first as the file orders it.
        ShadeProgram? program = null;
        string pass = string.Empty;
        foreach (string material in passes)
        {
            IReadOnlyList<ShaderInstance> instances = ShaderGraph.Instances(read(material));
            if (instances.Count == 0)
            {
                said.Add($"pass {Named(material)}: no graph instances read");
                continue;
            }

            var chain = new List<(ShaderInstance Instance, ShaderGraph Graph)>(instances.Count);
            foreach (ShaderInstance instance in instances)
            {
                chain.Add((instance, ShaderGraph.Read(read(instance.Parent.Replace('\\', '/').Trim()))));
            }

            ShadeCompile compiled = ShadeProgram.CompileScreen(chain);
            if (compiled.Program is { } screen)
            {
                program = screen;
                pass = material;
                break;
            }

            said.Add($"pass {Named(material)}: {string.Join("; ", compiled.Skipped)}");
        }

        if (program is null)
        {
            said.Insert(0, $"screen-space fog: no pass of {Named(attachment)} compiled as a screen program");
            return new ScreenFog(attachment, null, default, default, default, said);
        }

        // ITS TEXTURES WITH THEIR LEVELS: the file's own blocks where it holds every level, else decoded and halved.
        var sheets = new Mipmaps?[program.Textures.Count];
        for (var one = 0; one < sheets.Length; one++)
        {
            byte[]? bytes = GameArt.ReadRaw(read, program.Textures[one].Path);
            sheets[one] = bytes is { Length: > 0 } ? Mipmaps.Kept(BlockTexture.Of(bytes)) ?? Mipmaps.Of(GameArt.Decode(bytes)) : null;
            if (sheets[one] is null)
            {
                said.Insert(0, $"screen-space fog: {Named(pass)}'s texture {program.Textures[one].Path} did not read");
                return new ScreenFog(attachment, null, default, default, default, said);
            }
        }

        Vector3 light = (Last(read, environment.DiffuseCube) + Last(read, environment.SpecularCube)) * (environment.EnvBrightness ?? 1f);
        Vector4 colour = environment.FogColour ?? Vector4.Zero;
        Vector2 shape = environment.FogShape ?? Vector2.Zero;
        said.Insert(0, string.Create(
            CultureInfo.InvariantCulture,
            $"screen-space fog: {Named(attachment)}'s pass {Named(pass)} ({string.Join(", ", program.Graphs.Select(Named))}), compiled whole from its files, on the card alone"));
        said.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"its .env: ssf_color {colour.X:0.#####} {colour.Y:0.#####} {colour.Z:0.#####} alpha {colour.W:0.#####}{(environment.FogColour is null ? " (none set, nought)" : string.Empty)}, disperse radius {shape.X:0.##}, feathering {shape.Y:0.#####}{(environment.FogShape is null ? " (none set, nought)" : string.Empty)}; the environment's light (0, 1, 0) at the cubes' last level {light.X:0.####} {light.Y:0.####} {light.Z:0.####}"));
        return new ScreenFog(attachment, program.With(sheets), colour, shape, light, said);
    }

    /// <summary>
    /// The materials of an object's render passes, the main one first: AnimatedRender's <c>RenderPasses</c> in the file or the first file it
    /// extends that has one.
    /// </summary>
    private static List<string> Passes(Func<string, byte[]?> read, string path, List<string> said)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(path);
        while (queue.Count > 0 && seen.Count < 16)
        {
            string file = queue.Dequeue();
            if (!seen.Add(file))
            {
                continue;
            }

            AnimatedObject held = AnimatedObject.Read(read(file));
            if (!held.Ready && !Path.HasExtension(file))
            {
                held = AnimatedObject.Read(read(file + AnimatedObject.Suffix));
            }

            foreach (AoStruct render in held.Named("AnimatedRender"))
            {
                foreach (AoEntry entry in render.Entries)
                {
                    if (string.Equals(entry.Key, "RenderPasses", StringComparison.OrdinalIgnoreCase) && Listed(entry.Value, said) is { Count: > 0 } passes)
                    {
                        return passes;
                    }
                }
            }

            foreach (string parent in held.Extends)
            {
                if (!string.Equals(parent, "nothing", StringComparison.OrdinalIgnoreCase))
                {
                    queue.Enqueue(parent.Replace('\\', '/').Trim());
                }
            }
        }

        return [];
    }

    /// <summary>A RenderPasses payload's materials, the main pass first: <c>{"passes": [{"is_main": true, "type": "Append", "filename": ...}]}</c>.</summary>
    private static List<string> Listed(string payload, List<string> said)
    {
        var main = new List<string>();
        var rest = new List<string>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("passes", out JsonElement passes) || passes.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            foreach (JsonElement pass in passes.EnumerateArray())
            {
                if (pass.ValueKind != JsonValueKind.Object || !pass.TryGetProperty("filename", out JsonElement file) || file.ValueKind != JsonValueKind.String
                    || file.GetString() is not { Length: > 0 } name)
                {
                    continue;
                }

                bool isMain = pass.TryGetProperty("is_main", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
                (isMain ? main : rest).Add(name.Replace('\\', '/').Trim());
            }
        }
        catch (JsonException fault)
        {
            said.Add("RenderPasses not JSON: " + fault.Message);
            return [];
        }

        return [.. main, .. rest];
    }

    /// <summary>A cube's last level read along (0, 1, 0), its own way - nought where the .env names none or it does not read.</summary>
    private static Vector3 Last(Func<string, byte[]?> read, string cube)
        => cube.Length > 0 && CubeMap.Read(GameArt.ReadRaw(read, cube), out _, largest: 1) is { } last ? last.Sample(new Vector3(0f, 1f, 0f)) : Vector3.Zero;

    private static string Named(string path) => Path.GetFileName(path);
}
