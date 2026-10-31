using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// What a screen pass hands every pixel of a screen program (ShadeProgram.CompileScreen) - the engine's pass uniforms and the pixel's own place,
/// in the picture's model space.
/// </summary>
/// <param name="Camera">CameraPos: where the eye stands - scene_camera_position.</param>
/// <param name="Surface">UnprojectPixelDepth: the solid surface behind the pixel, its depth put back into the scene.</param>
/// <param name="Pixel">SV_Position's x and y: the pixel's place on the screen, a pixel's middle at its half - what ReadBayerNoise reads.</param>
/// <param name="FogColour">SSFColor: the pass's ssf_color - the area's <c>screenspace_fog.ssf_color</c> and <c>ssf_color_alpha</c>.</param>
/// <param name="FogShape">SSFDisperseRadiusFeathering: <c>ssf_disperse_radius</c> and <c>ssf_feathering</c>.</param>
/// <param name="Player">PlayerTrailingGhost: where the player stands - on a stage, the camera's focus (CHOSEN, see the node).</param>
/// <param name="Direct">GetIntegralOmniLight's direct light on the processor: the sun's and the room's lights, unshadowed - see ShadeProgram.Omni.</param>
/// <param name="Environment">GetTotalEnvLight: the two cubes' light along (0, 1, 0) at their last level, times cube_brightness.x.</param>
public readonly record struct ScreenPixel(
    Vector3 Camera, Vector3 Surface, Vector2 Pixel, Vector4 FogColour, Vector2 FogShape, Vector3 Player, Vector3 Direct, Vector3 Environment);

/// <summary>
/// A screen program: a material whose graphs write the pixel's own colour (OutPixelColor into PixelOutput) over a mesh laid across the whole
/// screen - the engine's screen-space fog, the weather attachment an area's .env names as <c>area.player_environment_ao</c>.
/// </summary>
/// <remarks>
/// THE RED VALE'S FOG IS A MATERIAL (v0.1.380's capture, light-extras.txt section 7): its .env names
/// <c>Metadata/Effects/weather_attachments/act1/Red_Vale/Red_Vale.ao</c>, a FixedMesh (<c>screenSpaceWeatherEffect.fmt</c>, two triangles)
/// drawn through one render pass, <c>Red_Vale_ao_pass0.mat</c>: <c>ScreenspaceQuad.fxgraph</c>, which lays the mesh over the screen
/// (VertexProjectedPosition from the mesh's own place, <c>xy * 2 - 1</c> at a depth of 1e-6), and <c>ScreenspaceFogFlat.fxgraph</c> under the
/// blend mode DownScaledAlphaBlend, writing no depth, whose OutPixelColor hands the pixel <c>(res.rgb / max(res.a, Epsilon), res.a)</c> - the
/// fog's light and how much of what lies behind it hides. Compiled here from its own files, the material's parameters over the graph's.
///
/// THE LOOP IS A GROUP (ScreenspaceFogFlatRaymarchGroup, utilitynodes.ffx): a node of the graph whose inside is the nodes carrying its
/// <c>group_id</c>, run once per layer between its prologue and its epilogue - HLSL of the engine's own, ported line for line in
/// <see cref="Unit.Raymarched"/>. UNROLLED AT THE COMPILE: the layers' count is the material's constant (5), so each pass of the loop is its
/// steps, the inside compiled again with that pass's values; a count that is not a constant is refused, and named.
///
/// ON THE CARD ALONE (DECIDED from the live client): the processor runs a screen program only to be held against (<see cref="RunScreen"/>),
/// its light handed in whole.
/// </remarks>
public sealed partial class ShadeProgram
{
    /// <summary>The fog's loop - the one group the compile knows, see <see cref="Unit.Raymarched"/>.</summary>
    internal const string RaymarchGroup = "ScreenspaceFogFlatRaymarchGroup";

    /// <summary>Which pass value an <see cref="Op.Pass"/> step reads - ScreenPixel's members.</summary>
    internal const int PassCamera = 0;

    /// <inheritdoc cref="PassCamera"/>
    internal const int PassSurface = 1;

    /// <inheritdoc cref="PassCamera"/>
    internal const int PassPixel = 2;

    /// <inheritdoc cref="PassCamera"/>
    internal const int PassFogColour = 3;

    /// <inheritdoc cref="PassCamera"/>
    internal const int PassFogShape = 4;

    /// <inheritdoc cref="PassCamera"/>
    internal const int PassPlayer = 5;

    /// <summary>The most layers the loop is unrolled to - the count's own range in the graph (ConstantInt "Layers count", 1 to 100).</summary>
    private const int MostLayers = 100;

    /// <summary>The pixel the thread is running a screen program for - set by <see cref="RunScreen"/>, read by the pass's steps.</summary>
    [ThreadStatic]
    private static ScreenPixel s_screen;

    /// <summary>Whether the program is a screen program - its colour the pixel's own, drawn over the whole picture (<see cref="CompileScreen"/>).</summary>
    public bool Screen { get; private set; }

    /// <summary>
    /// Compiles the pixel's own colour a material's graphs write - OutPixelColor's <c>pixel_color</c> into PixelOutput - for a material whose
    /// vertex side lays its mesh over the screen; or says why it cannot.
    /// </summary>
    /// <param name="chain">The material's graph instances in its own order, each with its graph.</param>
    /// <remarks>
    /// THE LAST GRAPH WRITING IT STANDS, as a later graph's write of a channel stands over an earlier one's. Nothing a mesh's surface holds is
    /// read: a screen pass has no surface, so a node reading a channel (InputUV and its kin) is refused before any graph wrote it.
    /// </remarks>
    public static ShadeCompile CompileScreen(IReadOnlyList<(ShaderInstance Instance, ShaderGraph Graph)> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var skipped = new List<string>();

        // THE VERTEX SIDE LAYS THE MESH OVER THE SCREEN where a graph writes its projected place itself (ScreenspaceQuad.fxgraph).
        bool onScreen = chain.Any(one =>
        {
            var lookup = new Lookup(one.Graph);
            return one.Graph.Nodes.Any(node => node.Type == "VertexProjectedPosition" && lookup.Fed(node));
        });

        for (int link = chain.Count - 1; link >= 0; link--)
        {
            (ShaderInstance instance, ShaderGraph graph) = chain[link];
            if (PixelOutput(graph) is not { } output)
            {
                continue;
            }

            string named = Named(instance.Parent);
            if (!onScreen)
            {
                skipped.Add($"OutPixelColor in {named}, whose material does not lay itself over the screen");
                return new ShadeCompile(null, skipped);
            }

            var build = new Builder();
            var state = new Dictionary<string, Held>(StringComparer.Ordinal);
            var lost = new Dictionary<string, string>(StringComparer.Ordinal);
            var unit = new Unit(build, new Lookup(graph), instance, state, lost, default, _ => state) { ScreenPass = true };
            unit.Start();
            if (unit.Port(output, "pixel_color") is not { } colour || !build.Fits)
            {
                skipped.Add($"{(!build.Fits ? "too many steps" : unit.Why)} in {named}'s pixel output");
                return new ShadeCompile(null, skipped);
            }

            ShadeProgram? program = Finish(build, colour, -1, -1, [instance.Parent], hasAlpha: true, plainable: false);
            if (program is null)
            {
                skipped.Add($"more than {MostRegisters} registers in {named}'s pixel output");
                return new ShadeCompile(null, skipped);
            }

            program.Screen = true;
            return new ShadeCompile(program, skipped);
        }

        skipped.Add("no graph writes the pixel's own colour (OutPixelColor into PixelOutput)");
        return new ShadeCompile(null, skipped);
    }

    /// <summary>The OutPixelColor whose <c>connection</c> feeds a ConnectionPixel at PixelOutput - the pixel's own colour written - or null.</summary>
    private static ShaderNode? PixelOutput(ShaderGraph graph)
    {
        foreach (ShaderLink link in graph.Links)
        {
            if (link.Source.Type == "OutPixelColor" && link.Source.Variable == "connection"
                && link.Target.Type == "ConnectionPixel" && link.Target.Stage == "PixelOutput")
            {
                return graph.Nodes.FirstOrDefault(node => node.Type == "OutPixelColor" && node.Index == link.Source.Index);
            }
        }

        return null;
    }

    /// <summary>
    /// A screen program's <c>pixel_color</c> at one pixel: the light it lays over what lies behind in xyz, linear, and how much of that it hides in w.
    /// </summary>
    /// <param name="registers">Scratch, <see cref="Registers"/> long.</param>
    /// <param name="time">The game's clock - see <see cref="Clock"/>.</param>
    /// <param name="pixel">What the pass hands the pixel.</param>
    /// <remarks>
    /// THE PROCESSOR DRAWS NO SCREEN PROGRAM - it is held against the card's here, every texture read at its top level but those the graph
    /// reads at a level of its own, and the light its <paramref name="pixel"/>'s (<see cref="Omni"/>).
    /// </remarks>
    public Vector4 RunScreen(Span<Vector4> registers, float time, in ScreenPixel pixel)
    {
        s_screen = pixel;
        registers[Clock] = new Vector4(time);
        registers[Dropped] = Vector4.Zero;
        Constants(registers);
        Span<float> levels = stackalloc float[Math.Max(1, _sampleTexture.Length)];
        levels.Clear();
        Run(registers, levels, default, -1);
        return registers[_result];
    }

    /// <summary>The pass's value an <see cref="Op.Pass"/> step names: a place with a w of nought, a colour whole, a pair in x and y.</summary>
    private static Vector4 Passed(int which) => which switch
    {
        PassCamera => new Vector4(s_screen.Camera, 0f),
        PassSurface => new Vector4(s_screen.Surface, 0f),
        PassPixel => new Vector4(s_screen.Pixel, 0f, 0f),
        PassFogColour => s_screen.FogColour,
        PassFogShape => new Vector4(s_screen.FogShape, 0f, 0f),
        PassPlayer => new Vector4(s_screen.Player, 0f),
        _ => Vector4.Zero,
    };

    /// <summary>
    /// GetBayerNoise16 of a float2 (math_utils' bayer_matrices): the pixel's place in its four by four block, read through the matrix and
    /// centred in its sixteenth.
    /// </summary>
    /// <remarks>
    /// <c>uint(frac(x / 4) * 4 - 0.5 + (frac(y / 4) * 4 - 0.5) * 4 + 0.5)</c>, clamped to 0..15, then <c>(bayer_matrix16[i] + 0.5) / 16</c> over
    /// <c>{0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5}</c>. A conversion of a negative number to uint gives nought, as the card's does.
    /// </remarks>
    public static float Bayer(Vector2 pixel)
    {
        ReadOnlySpan<int> matrix = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        float x = pixel.X / 4f, y = pixel.Y / 4f;
        float index = ((x - MathF.Floor(x)) * 4f) - 0.5f + (((y - MathF.Floor(y)) * 4f) - 0.5f) * 4f + 0.5f;
        uint at = index > 0f ? (uint)index : 0u;
        return (matrix[(int)Math.Min(at, 15u)] + 0.5f) / 16f;
    }

    /// <summary>
    /// SampleFlowingTexMip2 (flowing_textures2), line for line: two nodes half a period apart, each read where its hash and the flow put it, mixed
    /// by their weights, and their spread from the texture's mean scaled back up by PreserveVariance.
    /// </summary>
    /// <param name="texture">The texture.</param>
    /// <param name="uv">The coordinates, in x and y.</param>
    /// <param name="flow">The flow, in x and y.</param>
    /// <param name="packed">The phase, the seed, the level and the phase's offset.</param>
    /// <remarks>
    /// <c>node_uv = hash33(float3(seeds[i], seed, 0)).xy + uv + flow_vec * (phases[i] * 2 - 1 + phase_offset)</c>, each read at the level the
    /// graph hands it, the mean at <c>(0.5, 0.5)</c> at level 10 - the texture's last, which a card clamps it to.
    /// </remarks>
    private Vector4 Flowed(int texture, Vector4 uv, Vector4 flow, Vector4 packed)
    {
        float phase = packed.X, seed = packed.Y, level = packed.Z, offset = packed.W;
        (Vector2 phases, Vector2 seeds, Vector2 weights) = LinearInterpNodes(phase);
        var colour = Vector4.Zero;
        float moment = 0f;
        for (var node = 0; node < 2; node++)
        {
            float local = node == 0 ? phases.X : phases.Y, weight = node == 0 ? weights.X : weights.Y;
            Vector3 hash = Hash33(new Vector3(node == 0 ? seeds.X : seeds.Y, seed, 0f));
            float along = (local * 2f) - 1f + offset;
            var at = new Vector2(hash.X + uv.X + (flow.X * along), hash.Y + uv.Y + (flow.Y * along));
            moment += weight * weight;
            colour += Read(texture, at, level) * weight;
        }

        Vector4 mean = Read(texture, new Vector2(0.5f, 0.5f), 10f);
        return ((colour - mean) / MathF.Sqrt(moment)) + mean;
    }

    /// <summary>
    /// GetIntegralOmniLight on the processor: <c>direct + environment * ambient.a + ambient.rgb</c>, the direct and the environment's light the
    /// pixel's (<see cref="ScreenPixel"/>).
    /// </summary>
    /// <remarks>
    /// THE ENGINE'S, READ (lighting.ffx): GetIntegralTotalLightIntensity - each dynamic light at the layer's first point, the shadowed ones
    /// through IntegrateShadowMap's seven samples from the first point to the last, the sun's also through GetCloudsIntensity - plus
    /// GetTotalPointLightsIntensity's lights of the pixel's tile, plus GetTotalEnvLight times the ambient's alpha, plus its colour. The card
    /// works out the direct light itself (ModelShaders' ShadeOmni); the processor, which draws no screen program, is handed it.
    /// </remarks>
    private static Vector3 Omni(Vector4 ambient)
        => s_screen.Direct + (s_screen.Environment * ambient.W) + new Vector3(ambient.X, ambient.Y, ambient.Z);

    /// <summary>The engine's hash3u3fStable: three uints through PCG's rounds (hash33UintPcg), each as HashUToF makes it a fraction.</summary>
    /// <remarks>hash3i3fStable is it over <c>uint3(int3)</c>, the ints' own bits; hash33Stable over <c>asuint</c> of three floats.</remarks>
    public static Vector3 HashStable(uint x, uint y, uint z)
    {
        unchecked
        {
            x = (x * 1664525u) + 1013904223u;
            y = (y * 1664525u) + 1013904223u;
            z = (z * 1664525u) + 1013904223u;
            x += y * z;
            y += z * x;
            z += x * y;
            x ^= x >> 16;
            y ^= y >> 16;
            z ^= z >> 16;
            x += y * z;
            y += z * x;
            z += x * y;
        }

        return new Vector3(HashUnit(x), HashUnit(y), HashUnit(z));
    }

    /// <summary>The unit's part for the screen-space fog's nodes and its loop.</summary>
    private sealed partial class Unit
    {
        /// <summary>The pass of a group's loop being compiled, or null outside every loop.</summary>
        private GroupPass? _inside;

        /// <summary>Whether a node inside a group is being compiled within a pass of that group's loop.</summary>
        private bool Inside(ShaderNode node) => _inside is { } pass && string.Equals(node.Group, pass.Key, StringComparison.Ordinal);

        /// <summary>
        /// Whether a node's <c>group_id</c> is a loop's - a group node's type and index (<c>ScreenspaceFogFlatRaymarchGroup_0</c>) - rather than
        /// one of the boxes the graph editor draws round nodes for order alone (<c>&lt;Group&gt;_3</c> - TWEAK_AlbedoTint, AGT_DesertDust,
        /// PBRDielectricBN and the material blends among the fixtures), whose nodes run as any other.
        /// </summary>
        private static bool Looped(string group) => group.StartsWith(RaymarchGroup + "_", StringComparison.Ordinal);

        /// <summary>
        /// One output of a group: inside a pass of its loop, the pass's own values; outside it, the loop's result once it has run.
        /// </summary>
        private int? Grouped(ShaderNode group, string variable)
        {
            if (_inside is { } pass && ReferenceEquals(pass.Group, group))
            {
                return pass.Values.TryGetValue(variable, out int held)
                    ? held
                    : Fail($"{group.Type}'s {variable}, which its loop does not hand the nodes inside it");
            }

            if (variable != "res_color")
            {
                return Fail($"{group.Type}'s {variable}, read outside its loop");
            }

            if (_pairs.TryGetValue((group, variable), out int known))
            {
                return known;
            }

            if (!_open.Add(group))
            {
                return Fail($"{group.Type} feeding itself");
            }

            int? said = Raymarched(group);
            _open.Remove(group);
            if (said is { } register)
            {
                _pairs[(group, variable)] = register;
            }

            return said;
        }

        /// <summary>
        /// ScreenspaceFogFlatRaymarchGroup (utilitynodes.ffx), its prologue, its inner prologue and epilogue and its epilogue line for line, the
        /// loop unrolled: the ray from the camera to the surface behind the pixel cut into the fog's layers between the two heights, each layer's
        /// colour the inside's for that pass, and the layers laid over one another from the nearest out.
        /// </summary>
        /// <remarks>
        /// <para>The prologue: <c>world_ray_delta = end - start</c>; <c>inv_delta_z = abs(delta.z) &gt; 1e-7 ? 1 / delta.z : 1e7</c>;
        /// <c>height_scale_start = (-height_range.x - start.z) * inv_delta_z</c> (the bottom - up is minus z), <c>height_scale_end</c> the same of
        /// <c>height_range.y</c> (the top); <c>layers_count = max(1, layers_count)</c>; <c>global_phase = vertical_phase * layers_count + 0.5</c>,
        /// <c>start_layer_index = -int(floor(global_phase))</c>, <c>layer_phase = frac(global_phase)</c>; <c>base_layer_path = abs(height_range.y -
        /// height_range.x) / layers_count</c>; <c>res_radiance = (0, 0, 0, 1)</c>.</para>
        /// <para>Each pass: <c>layer_seed = loop_index + start_layer_index</c>; <c>layer_ratio = (loop_index + layer_phase) / layers_count</c>; the
        /// layer from <c>saturate(lerp(height_scale_start, height_scale_end, layer_ratio))</c> along the ray to the same a layer lower;
        /// <c>layer_length_ratio = length(start - end) / base_layer_path</c>. Then the inside's <c>layer_color</c>, faded in at the first pass by
        /// <c>saturate(2 * layer_phase)</c> and out at the last by <c>saturate(2 * (1 - layer_phase))</c>; <c>T = exp(-a * fade * ratio)</c>;
        /// <c>res.rgb = res.rgb * T + rgb * (1 - T)</c>, <c>res.a *= T</c>.</para>
        /// <para>The epilogue: <c>res_color = (res.rgb, 1 - res.a)</c>.</para>
        /// <para>A HEIGHT RANGE OF NOTHING divides the layers' length by nought, which the engine's division makes infinite and this compile's
        /// (Op.Divide, the Divide node's own) nought - the one place the two part.</para>
        /// </remarks>
        private int? Raymarched(ShaderNode group)
        {
            if (Port(group, "world_ray_start") is not { } start || Port(group, "world_ray_end") is not { } end
                || Port(group, "layers_count") is not { } counted || Port(group, "vertical_phase") is not { } rising
                || Port(group, "height_range") is not { } range)
            {
                return null;
            }

            if (!Build.ConstantOf(counted, out Vector4 said))
            {
                return Fail($"{group.Type} with a layers_count that is not a constant");
            }

            // AN INT: the constant's own number truncated, as the conversion to the group's int is.
            int count = Math.Max(1, (int)said.X);
            if (count > MostLayers)
            {
                return Fail($"{group.Type} with {count} layers, past the {MostLayers} its count is declared to");
            }

            // THE SLOTS, handed in from outside once and read inside every pass.
            var handed = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string slot in group.Slots ?? [])
            {
                if (Port(group, slot) is not { } value)
                {
                    return null;
                }

                handed[slot] = value;
            }

            // THE PROLOGUE.
            int delta = Emit(Op.Subtract, end, start);
            int deltaZ = OneOf(delta, 2);
            int inverse = Emit(Op.If, Emit(Op.Abs, deltaZ), Number(1e-7f), Emit(Op.Divide, Number(1f), deltaZ), Number(1e7f), Number(1e7f));
            int startZ = OneOf(start, 2);
            int bottom = OneOf(range, 0);
            int top = OneOf(range, 1);
            int scaleStart = Emit(Op.Multiply, Emit(Op.Subtract, Emit(Op.Negate, bottom), startZ), inverse);
            int scaleEnd = Emit(Op.Multiply, Emit(Op.Subtract, Emit(Op.Negate, top), startZ), inverse);
            int global = Emit(Op.MultiplyAdd, OneOf(rising, 0), Number(count), Number(0.5f));
            int first = Emit(Op.Negate, Emit(Op.Floor, global));
            int phase = Emit(Op.Frac, global);
            float inv = 1f / count;
            int path = Emit(Op.Multiply, Emit(Op.Abs, Emit(Op.Subtract, top, bottom)), Number(inv));
            int radiance = Build.Constant(new Vector4(0f, 0f, 0f, 1f));
            int noAlpha = Build.Constant(new Vector4(1f, 1f, 1f, 0f));

            for (var at = 0; at < count; at++)
            {
                // THE INNER PROLOGUE.
                Forget(group.GroupKey);
                int seed = Emit(Op.Add, first, Number(at));
                int ratio = Emit(Op.Multiply, Emit(Op.Add, phase, Number(at)), Number(inv));
                int ratioEnd = Emit(Op.Subtract, ratio, Number(inv));
                int startScale = Emit(Op.Saturate, Emit(Op.Lerp, scaleStart, scaleEnd, ratio));
                int endScale = Emit(Op.Saturate, Emit(Op.Lerp, scaleStart, scaleEnd, ratioEnd));
                int worldStart = Emit(Op.MultiplyAdd, delta, startScale, start);
                int worldEnd = Emit(Op.MultiplyAdd, delta, endScale, start);
                int lengthRatio = Emit(Op.Divide, Build.Emit(Op.Length, Emit(Op.Subtract, worldStart, worldEnd), extra: 3), path);

                // THE INSIDE, with this pass's values.
                var values = new Dictionary<string, int>(handed, StringComparer.Ordinal)
                {
                    ["layer_world_start"] = worldStart,
                    ["layer_world_end"] = worldEnd,
                    ["layer_ratio"] = ratio,
                    ["layer_seed"] = seed,
                };
                _inside = new GroupPass(group, group.GroupKey, values);
                int? layer = Port(group, "layer_color");
                _inside = null;
                if (layer is not { } colour)
                {
                    Forget(group.GroupKey);
                    return null;
                }

                // THE INNER EPILOGUE.
                int fade = Number(1f);
                if (at == 0)
                {
                    fade = Emit(Op.Saturate, Emit(Op.Multiply, Number(2f), phase));
                }

                if (at == count - 1)
                {
                    fade = Emit(Op.Multiply, fade, Emit(Op.Saturate, Emit(Op.Multiply, Number(2f), Emit(Op.OneMinus, phase))));
                }

                int throughput = Emit(Op.Exp, Emit(Op.Multiply, Emit(Op.Multiply, Emit(Op.Negate, OneOf(colour, 3)), fade), lengthRatio));
                int lit = Emit(Op.Multiply, Emit(Op.Multiply, colour, Emit(Op.OneMinus, throughput)), noAlpha);
                radiance = Emit(Op.MultiplyAdd, radiance, throughput, lit);
            }

            Forget(group.GroupKey);

            // THE EPILOGUE.
            return Emit(Op.MultiplyAdd, radiance, Build.Constant(new Vector4(1f, 1f, 1f, -1f)), Build.Constant(new Vector4(0f, 0f, 0f, 1f)));
        }

        /// <summary>
        /// Whether the unit compiles a screen pass (<see cref="CompileScreen"/>), whose pixel the pass's values are handed for - the only
        /// compile the nodes reading them (<see cref="Op.Pass"/>, the omni light) are taken in.
        /// </summary>
        /// <remarks>
        /// REFUSED ON A MESH'S SURFACE, as they were before they were known: a material drawn on a mesh is handed no pass values, and a
        /// camera or a player at nought would draw it otherwise than the game, without a word.
        /// </remarks>
        public bool ScreenPass { get; init; }

        /// <summary>The screen-space fog's own nodes - each the fragment the engine compiles it from (utilitynodes.ffx and its includes).</summary>
        private int? Screened(ShaderNode node)
            => !ScreenPass && node.Type is not ("Hash3i3fStable" or "SampleFlowTextureLod2")
                ? Fail($"{node.Type} on a mesh's surface, whose pass hands it nothing")
                : ScreenNode(node);

        private int? ScreenNode(ShaderNode node) => node.Type switch
        {
            // cam_pos = GetCameraPosition(): scene_camera_position, the pass's.
            "CameraPos" => Build.Emit(Op.Pass, -1, extra: PassCamera),

            // ReadNonlinearDepth at the pixel, Unproject through the inverse of the view and projection: the pass's own working out.
            "UnprojectPixelDepth" => Build.Emit(Op.Pass, -1, extra: PassSurface),

            // GetBayerNoise16(SV_Position.xy).
            "ReadBayerNoise" => Build.Emit(Op.Bayer, Build.Emit(Op.Pass, -1, extra: PassPixel)),

            // color = ssf_color; the radius and the feathering ssf_disperse_radius_feathering's x and y - pass uniforms the area's .env fills.
            "SSFColor" => Build.Emit(Op.Pass, -1, extra: PassFogColour),
            "SSFDisperseRadiusFeathering" => Build.Emit(Op.Pass, -1, extra: PassFogShape),

            // DEFINED IN NO SOURCE READ (light-extras.txt section 8): the player's place, which its name and its one use - the distance the
            // fog disperses round - say. CHOSEN: on a stage the camera's focus stands for the player (DECIDED from the live client).
            "PlayerTrailingGhost" => Build.Emit(Op.Pass, -1, extra: PassPlayer),

            // A DYNAMIC PARAMETER THE ENGINE'S C++ SETS (dynamic float4 WindVector), read nowhere: nought, CHOSEN. ScreenspaceFogFlat multiplies
            // it by "Environment wind Influence", which the graph leaves at nought and the Red Vale's material does not set.
            "WindVector" => Build.Constant(Vector4.Zero),

            "Hash3i3fStable" => Single(node) is { } seeded ? Build.Emit(Op.HashInt, seeded) : null,
            "SampleFlowTextureLod2" => Flowing(node),
            "GetIntegralOmniLight" => Port(node, "ray_point0") is { } near && Port(node, "ray_point1") is { } far
                && Port(node, "ray_hash") is { } hash && Port(node, "ambient_light") is { } ambient
                ? Build.Emit(Op.Omni, near, far, hash, ambient)
                : null,
            _ => Fail(node.Type),
        };

        /// <summary>SampleFlowTextureLod2: its texture, coordinates and flow, the phase, seed, level and offset in one register - see ShadeProgram.Flowed.</summary>
        private int? Flowing(ShaderNode node)
        {
            if (Handed(node, "tex") is not { } sheet || Port(node, "uv") is not { } uv || Port(node, "flow_vec") is not { } flow
                || Port(node, "phase") is not { } phase || Port(node, "seed") is not { } seed || Port(node, "lod") is not { } level
                || Port(node, "phase_offset") is not { } offset)
            {
                return null;
            }

            int packed = Build.Register();
            Build.Steps.Add(new Step(Op.Clear, packed, -1, -1, -1, -1, -1, 0));
            Build.Into(packed, phase, Only(0, 0));
            Build.Into(packed, seed, Only(1, 0));
            Build.Into(packed, level, Only(2, 0));
            Build.Into(packed, offset, Only(3, 0));
            return Build.Emit(Op.Flow, uv, flow, packed, extra: sheet);
        }

        /// <summary>Forgets what the nodes inside a group compiled to, so the next pass of its loop compiles them again with its own values.</summary>
        private void Forget(string group)
        {
            foreach (ShaderNode gone in _done.Keys.Where(one => one.Group == group).ToList())
            {
                _done.Remove(gone);
            }

            foreach ((ShaderNode Node, string Output) gone in _pairs.Keys.Where(one => one.Node.Group == group).ToList())
            {
                _pairs.Remove(gone);
            }
        }

        private int Emit(Op op, int a, int b = -1, int c = -1, int d = -1, int e = -1) => Build.Emit(op, a, b, c, d, e);

        /// <summary>A number in all four components.</summary>
        private int Number(float value) => Build.Constant(new Vector4(value));

        /// <summary>One component of a register spread across all four - a float of a vector.</summary>
        private int OneOf(int register, int part)
            => Build.Emit(Op.Swizzle, register, extra: part | (part << 2) | (part << 4) | (part << 6) | (0b1111 << 8));

        /// <summary>One pass of a group's loop: the group, its key as its inside names it, and the values the pass hands the inside.</summary>
        private sealed record GroupPass(ShaderNode Group, string Key, Dictionary<string, int> Values);
    }
}
