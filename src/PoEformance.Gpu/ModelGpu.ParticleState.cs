using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using PoEformance.Game.Files;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PoEformance.Gpu;

/// <summary>
/// What the card holds of the particles it runs, read back for the capture's model-panes part: every emitter's record and update globals as
/// last written, and its pool, order and culling buffers as the update left them - each pool member by the engine's own ParticleData layout.
/// </summary>
/// <remarks>
/// BUILT BEFORE THE FIRST LIVE LOOK AT THE LAMP (CLAUDE.md, "a question the capture cannot answer is a new part, built BEFORE the question is
/// asked"): where the picture shows no flame, a picture alone cannot tell an update that spawned nothing from particles placed out of view or a
/// pass that drew them transparent - the pool's numbers can. PRINT BOTH HALVES: what was handed to the card beside what it made of it.
/// </remarks>
public sealed partial class ModelGpu
{
    /// <summary>Particles of each pool written out member by member - the rest summed per member.</summary>
    private const int MostStateParticles = 4;

    /// <summary>
    /// Every emitter the card holds, read back - its record and update globals as last written, its pool member by member with each member's
    /// range over the pool, its order and culling buffers' first words. Run on the render thread; empty where the card runs no particles.
    /// </summary>
    public IReadOnlyList<string> ParticleState()
    {
        var lines = new List<string>();
        if (_particleCards.Count == 0)
        {
            return lines;
        }

        lines.Add(ParticlesSaid.Length > 0 ? ParticlesSaid : "particles: none drawn in the last picture");
        foreach (ParticleCard card in _particleCards.Values)
        {
            try
            {
                card.State(_device, _context, lines);
                Screened(card, lines);
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
            {
                lines.Add("  the card's buffers would not read back: " + exception.Message);
            }
        }

        return lines;
    }

    /// <summary>The last picture's particles were drawn into, with what - kept so the capture can draw each pass again (Screened).</summary>
    private (ModelTarget Target, ParticleFrame Frame, ParticleView View, SceneLight? Light)? _particleLast;

    /// <summary>
    /// What each of a card's passes puts on the screen: drawn again, alone and unblended, into a target of its own the picture's size - once over
    /// nothing and once against the picture's depth - and read back: the pixels it covered, their box, the largest colour and alpha and the mean
    /// alpha over them. Placed out of view, hidden behind the model and drawn transparent are told apart here; the simulation is not stepped.
    /// </summary>
    /// <remarks>
    /// v0.1.448'S LIVE CAPTURE SAID "nothing" for both of the lamp's passes, over nothing too, while their pools held live particles at the lantern -
    /// and "nothing" cannot tell geometry that never lands from a pixel shader that writes nought. So three readings stand beside it (v0.1.449): the
    /// card's own pipeline statistics over the draw (what reached the clipper, what passed it, how often the pixel shader ran); every vertex the
    /// engine's vertex shader made, streamed out as it left the shader (hidden at render_default's cull value, behind the eye, out of view or in
    /// it); and the pass drawn once more with a stand-in pixel shader writing one colour, which covers whatever the geometry covers.
    /// v0.1.449'S CAPTURE told the two apart: the embers' geometry never landed, the flame's covered 2,890 pixels with the stand-in and
    /// nothing with its own pixel shader - which "nothing" cannot split into a nought alpha, a nought colour or the tool's finishing. So the
    /// pass's own pixel shader is drawn once more for each of <see cref="RawReadouts"/> (v0.1.450), returning what it computed in place of
    /// the tool's finishing, into a target cleared to <see cref="Unwritten"/> so a nought it writes is told from a pixel it never reached; and
    /// the finishing's own constants are written beside them.
    /// </remarks>
    private void Screened(ParticleCard card, List<string> lines)
    {
        if (_particleLast is not { } last || card.LastDrawn is not { } emitter || card.Passes.Count == 0)
        {
            return;
        }

        (ModelTarget target, ParticleFrame frame, ParticleView view, SceneLight? light) = last;
        int size = target.Size;
        using ID3D11Texture2D texture = _device.CreateTexture2D(new Texture2DDescription(Format.R32G32B32A32_Float, size, size, arraySize: 1, mipLevels: 1, BindFlags.RenderTarget));
        using ID3D11RenderTargetView into = _device.CreateRenderTargetView(texture);
        using ID3D11Texture2D staging = _device.CreateTexture2D(new Texture2DDescription(
            Format.R32G32B32A32_Float, size, size, arraySize: 1, mipLevels: 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        using ID3D11Query statistics = _device.CreateQuery(QueryType.PipelineStatistics);
        ID3D11PixelShader? standIn = StandIn(out string standInWhy);
        var row = new float[size * 4];
        ColourGrade? grade = light?.Grade;
        lines.Add(Say($"  the tool's finishing (PoefParticleFinish): exposure {light?.Exposure ?? 1f:0.####}, ")
            + (grade is null ? "no grade" : Say($"graded through {grade.Width} x {grade.Height} x {grade.Depth} texels")));

        // THE RAW READOUTS AND THE TRACE COMPILED ON EVERY CORE AT ONCE: an engine pixel shader runs to 17,000 lines, and the shader cache
        // keeps them.
        (byte[]? Code, string Why)[,] raw = card.RawCodes(RawReadouts.Select(one => one.Body).ToArray());
        (byte[]? Code, string Why, string[] Names)[][] traces = card.TraceCodes();
        for (var p = 0; p < card.Passes.Count; p++)
        {
            foreach (bool tested in (ReadOnlySpan<bool>)[false, true])
            {
                _context.ClearRenderTargetView(into, new Color4(0f, 0f, 0f, 0f));
                Neutrals();
                if (!tested)
                {
                    _context.Begin(statistics);
                }

                Drew(_context, target, card, emitter, frame, view, light, into, tested, p);
                if (!tested)
                {
                    _context.End(statistics);
                }

                lines.Add(Say($"  pass {p} on the screen {(tested ? "against the picture's depth" : "over nothing")}: ") + Covered(texture, staging, row, size));
                if (!tested)
                {
                    lines.Add(Say($"  pass {p} through the card's pipeline over nothing: ") + Pipeline(statistics));
                }
            }

            lines.Add(Say($"  pass {p} as its vertex shader left it: ") + Streamed(card, emitter, frame, view, light, target, into, p));
            if (standIn is null)
            {
                lines.Add(Say($"  pass {p} with a stand-in pixel shader: not drawn - ") + standInWhy);
                continue;
            }

            _context.ClearRenderTargetView(into, new Color4(0f, 0f, 0f, 0f));
            Neutrals();
            Drew(_context, target, card, emitter, frame, view, light, into, tested: false, p, standIn);
            lines.Add(Say($"  pass {p} with a stand-in pixel shader writing one colour, over nothing: ") + Covered(texture, staging, row, size));
            for (var r = 0; r < RawReadouts.Length; r++)
            {
                string said = Say($"  pass {p}, its pixel shader returning {RawReadouts[r].Said}, over nothing: ");
                (byte[]? code, string why) = p < raw.GetLength(0) ? raw[p, r] : (null, "the program holds no such pass");
                if (code is null)
                {
                    lines.Add(said + "not drawn - " + why);
                    continue;
                }

                using ID3D11PixelShader shader = _device.CreatePixelShader(code);
                _context.ClearRenderTargetView(into, new Color4(Unwritten, Unwritten, Unwritten, Unwritten));
                Neutrals();
                Drew(_context, target, card, emitter, frame, view, light, into, tested: false, p, shader);
                lines.Add(said + Channels(texture, staging, row, size));
            }

            // THE TRACE (v0.1.451): every write of the surface's albedo and emissive in the pass's pixel shader, four a draw, in the order
            // the shader makes them (ParticleLinker.Traced) - each over the pixels written, -1 where the write was never reached.
            foreach ((byte[]? code, string why, string[] names) in p < traces.Length ? traces[p] : [])
            {
                string said = Say($"  pass {p} traced, over nothing: ");
                if (code is null)
                {
                    lines.Add(said + string.Join(", ", names) + " - not drawn: " + why);
                    continue;
                }

                using ID3D11PixelShader shader = _device.CreatePixelShader(code);
                _context.ClearRenderTargetView(into, new Color4(Unwritten, Unwritten, Unwritten, Unwritten));
                Neutrals();
                Drew(_context, target, card, emitter, frame, view, light, into, tested: false, p, shader);
                lines.Add(said + Traces(texture, staging, row, size, names));
            }
        }
    }

    /// <summary>
    /// What a traced draw wrote: over the pixels written, each of its four channels' least, most and mean under the name of the write it holds
    /// (<see cref="ParticleLinker.Traced"/>).
    /// </summary>
    private string Traces(ID3D11Texture2D texture, ID3D11Texture2D staging, float[] row, int size, string[] names)
    {
        _context.CopyResource(staging, texture);
        MappedSubresource mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        long written = 0;
        var low = new float[] { float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue };
        var high = new float[] { float.MinValue, float.MinValue, float.MinValue, float.MinValue };
        var sum = new double[4];
        var odd = new long[4];
        try
        {
            for (var y = 0; y < size; y++)
            {
                Marshal.Copy(mapped.DataPointer + (y * mapped.RowPitch), row, 0, row.Length);
                for (var x = 0; x < size; x++)
                {
                    int at = x * 4;
                    if (row[at] == Unwritten && row[at + 1] == Unwritten && row[at + 2] == Unwritten && row[at + 3] == Unwritten)
                    {
                        continue;
                    }

                    written++;
                    for (var c = 0; c < 4; c++)
                    {
                        float value = row[at + c];
                        if (!float.IsFinite(value))
                        {
                            odd[c]++;
                            continue;
                        }

                        low[c] = Math.Min(low[c], value);
                        high[c] = Math.Max(high[c], value);
                        sum[c] += value;
                    }
                }
            }
        }
        finally
        {
            _context.Unmap(staging, 0);
        }

        if (written == 0)
        {
            return string.Join(", ", names) + " - no pixel written";
        }

        var said = new List<string>(4);
        for (var c = 0; c < names.Length && c < 4; c++)
        {
            long finite = written - odd[c];
            said.Add(finite == 0
                ? Say($"{names[c]} not finite")
                : Say($"{names[c]} {low[c]:0.####}..{high[c]:0.####} mean {sum[c] / finite:0.####}") + (odd[c] > 0 ? Say($" ({odd[c]} not finite)") : string.Empty));
        }

        return Say($"{written} pixels: ") + string.Join("; ", said);
    }

    /// <summary>
    /// What a pass's own pixel shader is made to return in place of the tool's finishing, each its entry's statements after the engine's
    /// PixelMain ran (ParticleLinker.Wrapped: <c>colour</c> its colour output): the colour as it hands it, its surface's albedo, and its
    /// surface's emissive beside the light's alpha - the engine's static surface and emission data as PixelMain left them.
    /// </summary>
    private static readonly (string Said, string Body)[] RawReadouts =
    [
        ("the engine's colour as PixelMain hands it, unfinished", "    return colour;"),
        ("its surface's albedo (rgb, a)", "    return sem_surface_data.material.albedo_color;"),
        ("its surface's emissive (rgb) and the light's alpha (a)", "    return float4(sem_surface_data.material.emissive_color, sem_emission_data.pixel_color.a);"),
    ];

    /// <summary>What a raw readout's target is cleared to: a value no shader writes, so a nought written is told from a pixel never reached.</summary>
    private const float Unwritten = -1e30f;

    /// <summary>
    /// What a raw readout wrote: the pixels written (not <see cref="Unwritten"/>), how many with an alpha above nought and how many with a nought
    /// colour, each channel's least, most and mean over them, and how many were not finite.
    /// </summary>
    private string Channels(ID3D11Texture2D texture, ID3D11Texture2D staging, float[] row, int size)
    {
        _context.CopyResource(staging, texture);
        MappedSubresource mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        long written = 0, odd = 0, lit = 0, black = 0;
        var low = new float[] { float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue };
        var high = new float[] { float.MinValue, float.MinValue, float.MinValue, float.MinValue };
        var sum = new double[4];
        try
        {
            for (var y = 0; y < size; y++)
            {
                Marshal.Copy(mapped.DataPointer + (y * mapped.RowPitch), row, 0, row.Length);
                for (var x = 0; x < size; x++)
                {
                    int at = x * 4;
                    if (row[at] == Unwritten && row[at + 1] == Unwritten && row[at + 2] == Unwritten && row[at + 3] == Unwritten)
                    {
                        continue;
                    }

                    written++;
                    if (!float.IsFinite(row[at]) || !float.IsFinite(row[at + 1]) || !float.IsFinite(row[at + 2]) || !float.IsFinite(row[at + 3]))
                    {
                        odd++;
                        continue;
                    }

                    for (var c = 0; c < 4; c++)
                    {
                        low[c] = Math.Min(low[c], row[at + c]);
                        high[c] = Math.Max(high[c], row[at + c]);
                        sum[c] += row[at + c];
                    }

                    lit += row[at + 3] > 0f ? 1 : 0;
                    black += row[at] == 0f && row[at + 1] == 0f && row[at + 2] == 0f ? 1 : 0;
                }
            }
        }
        finally
        {
            _context.Unmap(staging, 0);
        }

        if (written == 0)
        {
            return "no pixel written";
        }

        long finite = written - odd;
        string channels = finite == 0
            ? string.Empty
            : string.Concat(Enumerable.Range(0, 4).Select(c => Say($"; {"rgba"[c]} {low[c]:0.####}..{high[c]:0.####} mean {sum[c] / finite:0.####}")));
        return Say($"{written} pixels written, {lit} with alpha above nought, {black} with rgb nought") + channels + (odd > 0 ? Say($", {odd} not finite") : string.Empty);
    }

    /// <summary>What a target holds after a pass drew into it alone: the pixels not nought, their box, the largest colour and alpha, the mean alpha.</summary>
    private string Covered(ID3D11Texture2D texture, ID3D11Texture2D staging, float[] row, int size)
    {
        _context.CopyResource(staging, texture);
        MappedSubresource mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        long covered = 0, opaque = 0, odd = 0;
        int left = size, top = size, right = -1, bottom = -1;
        float red = 0f, green = 0f, blue = 0f, alpha = 0f;
        double alphas = 0;
        try
        {
            for (var y = 0; y < size; y++)
            {
                Marshal.Copy(mapped.DataPointer + (y * mapped.RowPitch), row, 0, row.Length);
                for (var x = 0; x < size; x++)
                {
                    float r = row[x * 4], g = row[(x * 4) + 1], b = row[(x * 4) + 2], a = row[(x * 4) + 3];
                    if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b) || !float.IsFinite(a))
                    {
                        odd++;
                        continue;
                    }

                    if (r == 0f && g == 0f && b == 0f && a == 0f)
                    {
                        continue;
                    }

                    covered++;
                    opaque += a > 0f ? 1 : 0;
                    alphas += a;
                    (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
                    (red, green, blue, alpha) = (Math.Max(red, r), Math.Max(green, g), Math.Max(blue, b), Math.Max(alpha, a));
                }
            }
        }
        finally
        {
            _context.Unmap(staging, 0);
        }

        string box = covered == 0 ? "nothing" : Say($"{covered} pixels in ({left}, {top})..({right}, {bottom}) of {size}, {opaque} with alpha, mean alpha {alphas / covered:0.###}, most {red:0.###} {green:0.###} {blue:0.###} {alpha:0.###}");
        return box + (odd > 0 ? Say($", {odd} not finite") : string.Empty);
    }

    /// <summary>A pipeline statistics query's numbers once the card has them (two seconds at most).</summary>
    private string Pipeline(ID3D11Query statistics)
    {
        QueryDataPipelineStatistics data;
        for (long until = Environment.TickCount64 + 2000; !_context.GetData(statistics, AsyncGetDataFlags.None, out data);)
        {
            if (Environment.TickCount64 > until)
            {
                return "the card gave no statistics within two seconds";
            }

            Thread.Yield();
        }

        return Say($"{data.IAVertices} vertices and {data.IAPrimitives} triangles in, {data.VSInvocations} vertex shader runs, {data.CInvocations} triangles to the clipper, {data.CPrimitives} past it, {data.PSInvocations} pixel shader runs");
    }

    /// <summary>The most vertices a pass streams out for the capture (16 bytes each).</summary>
    private const int MostStreamedVertices = 1 << 16;

    /// <summary>The vertices a pass streams out that are written whole.</summary>
    private const int StreamedSaid = 6;

    /// <summary>
    /// Every vertex a pass's vertex shader made, as it left the shader: its SV_Position streamed out through a geometry shader made from the vertex
    /// shader itself (the card's own pass-through), nothing rasterized. Counted as render_default's cull value (0, 0, -1, 1), behind the eye (w at
    /// most nought), not finite, in view (inside the clip volume) or out of it, with the ranges of the rest after the divide and the first few whole.
    /// </summary>
    private unsafe string Streamed(
        ParticleCard card, ParticleEmitterNow emitter, ParticleFrame frame, in ParticleView view, SceneLight? light, ModelTarget target, ID3D11RenderTargetView into, int p)
    {
        PassCard pass = card.Passes[p];
        ParticleProgram program = emitter.Program;
        int count = (int)Math.Min((long)program.Mesh.Indices.Length * program.Count, MostStreamedVertices);
        if (count <= 0)
        {
            return "no vertices to stream";
        }

        byte[] code = pass.Vertex.Code;
        SODeclarationEntry[] entries = [new SODeclarationEntry { Stream = 0, SemanticName = "SV_Position", SemanticIndex = 0, StartComponent = 0, ComponentCount = 4, OutputSlot = 0 }];
        ID3D11GeometryShader geometry;
        fixed (byte* at = code)
        {
            // NO RASTERIZED STREAM (D3D11_SO_NO_RASTERIZED_STREAM): only the stream-out is wanted here.
            geometry = _device.CreateGeometryShaderWithStreamOutput((IntPtr)at, new PointerSize(code.Length), entries, entries.Length, [16], 1, -1, null!);
        }

        using (geometry)
        using (ID3D11Buffer buffer = _device.CreateBuffer(new BufferDescription(count * 16, BindFlags.StreamOutput)))
        using (ID3D11Buffer back = _device.CreateBuffer(new BufferDescription(count * 16, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read)))
        using (ID3D11Query written = _device.CreateQuery(QueryType.SOStatistics))
        {
            Neutrals();
            _context.SOSetTargets([buffer], [0]);
            _context.Begin(written);
            Drew(_context, target, card, emitter, frame, view, light, into, tested: false, p, streamed: geometry);
            _context.End(written);
            _context.SOSetTargets(0, [], []);
            _context.CopyResource(back, buffer);
            QueryDataSOStatistics streamed;
            for (long until = Environment.TickCount64 + 2000; !_context.GetData(written, AsyncGetDataFlags.None, out streamed);)
            {
                if (Environment.TickCount64 > until)
                {
                    return "the card gave no stream-out statistics within two seconds";
                }

                Thread.Yield();
            }

            int vertices = (int)Math.Min((long)streamed.NumPrimitivesWritten * 3, count);
            var positions = new Vector4[vertices];
            MappedSubresource mapped = _context.Map(back, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                new ReadOnlySpan<Vector4>((void*)mapped.DataPointer, vertices).CopyTo(positions);
            }
            finally
            {
                _context.Unmap(back, 0);
            }

            return Say($"{streamed.NumPrimitivesWritten} triangles streamed of {streamed.PrimitivesStorageNeeded} made; ") + Positions(positions);
        }
    }

    /// <summary>Streamed clip positions counted by what becomes of them, with the ranges after the divide and the first few whole.</summary>
    private static string Positions(Vector4[] positions)
    {
        long culled = 0, behind = 0, odd = 0, inside = 0, outside = 0;
        var low = new Vector4(float.MaxValue);
        var high = new Vector4(float.MinValue);
        var hidden = new Vector4(0f, 0f, -1f, 1f);
        foreach (Vector4 at in positions)
        {
            if (!float.IsFinite(at.X) || !float.IsFinite(at.Y) || !float.IsFinite(at.Z) || !float.IsFinite(at.W))
            {
                odd++;
                continue;
            }

            if (at == hidden)
            {
                culled++;
                continue;
            }

            if (at.W <= 0f)
            {
                behind++;
                continue;
            }

            var divided = new Vector4(at.X / at.W, at.Y / at.W, at.Z / at.W, at.W);
            low = Vector4.Min(low, divided);
            high = Vector4.Max(high, divided);
            bool seen = MathF.Abs(divided.X) <= 1f && MathF.Abs(divided.Y) <= 1f && divided.Z >= 0f && divided.Z <= 1f;
            inside += seen ? 1 : 0;
            outside += seen ? 0 : 1;
        }

        string ranges = inside + outside == 0
            ? string.Empty
            : Say($", after the divide x {low.X:0.###}..{high.X:0.###} y {low.Y:0.###}..{high.Y:0.###} z {low.Z:0.####}..{high.Z:0.####}, w {low.W:0.###}..{high.W:0.###}");
        string first = string.Join(" ", positions.Take(StreamedSaid).Select(at => Say($"({at.X:0.###}, {at.Y:0.###}, {at.Z:0.####}, {at.W:0.###})")));
        return Say($"{positions.Length} vertices: {culled} at the cull value (0, 0, -1, 1), {behind} behind the eye, {odd} not finite, {inside} with the whole of them in view, {outside} out of it")
            + ranges + (first.Length > 0 ? "; first " + first : string.Empty);
    }

    /// <summary>
    /// A pixel shader writing one colour and reading nothing - an empty input signature, so it links behind any vertex shader - made once: drawn
    /// in place of a pass's own, it covers whatever the pass's geometry covers.
    /// </summary>
    private ID3D11PixelShader? StandIn(out string why)
    {
        why = string.Empty;
        if (_particleStandIn is not null)
        {
            return _particleStandIn;
        }

        const string Source = "float4 StandIn() : SV_Target0 { return float4(1.0f, 0.0f, 1.0f, 1.0f); }";
        Result result = Compiler.Compile(Source, "StandIn", "stand-in.hlsl", "ps_5_0", out Blob blob, out Blob errors);
        using (blob)
        using (errors)
        {
            if (result.Failure || blob is null)
            {
                why = "it did not compile: " + (errors?.AsString() ?? result.ToString());
                return null;
            }

            _particleStandIn = _device.CreatePixelShader(blob.AsBytes());
            return _particleStandIn;
        }
    }

    /// <summary>The capture's stand-in pixel shader, made at its first use (<see cref="StandIn"/>).</summary>
    private ID3D11PixelShader? _particleStandIn;

    private static string Say(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>One member of the pool's element: its name, where it starts, its scalar kind and how many of them.</summary>
    private readonly record struct PoolMember(string Name, int Offset, ShaderVariableType Kind, int Components);

    private sealed partial class ParticleCard
    {
        /// <summary>The record its last step handed the card.</summary>
        public EmitterRecord LastRecord { get; set; }

        /// <summary>How many steps it has run.</summary>
        public long Steps { get; set; }

        /// <summary>The emitter as it was last drawn - what the capture draws its passes again with.</summary>
        public ParticleEmitterNow? LastDrawn { get; set; }

        /// <summary>
        /// Each drawn pass's pixel shader wrapped to return each body's value in place of the tool's finishing (ParticleLinker.Wrapped, entry
        /// <see cref="RawEntry"/>), compiled on every core at once and kept in the shader cache: [pass, body] - its bytecode, or why none.
        /// </summary>
        public (byte[]? Code, string Why)[,] RawCodes(IReadOnlyList<string> bodies)
        {
            int passes = Math.Min(Passes.Count, _program.Passes.Count);
            var codes = new (byte[]? Code, string Why)[passes, bodies.Count];
            Parallel.For(0, passes * bodies.Count, at =>
            {
                int p = at / bodies.Count, b = at % bodies.Count;
                string? source = ParticleLinker.Wrapped(_program.Passes[p].Pixel.Hlsl, RawEntry, string.Empty, bodies[b]);
                if (source is null)
                {
                    codes[p, b] = (null, "it holds no PixelMain");
                    return;
                }

                byte[]? code = ParticleBytes(source, RawEntry, "ps_5_0", _cache, out string why);
                codes[p, b] = (code, why.Length > 0 ? why : "it would not compile");
            });
            return codes;
        }

        /// <summary>The raw readouts' entry point.</summary>
        private const string RawEntry = "PixelRaw";

        /// <summary>
        /// Each drawn pass's pixel shader traced (ParticleLinker.Traced) and wrapped to return four of its traced writes a draw, compiled on
        /// every core at once and kept in the shader cache: per pass, per group of four - its bytecode or why none, and the writes' names.
        /// </summary>
        public (byte[]? Code, string Why, string[] Names)[][] TraceCodes()
        {
            int passes = Math.Min(Passes.Count, _program.Passes.Count);
            var all = new (byte[]? Code, string Why, string[] Names)[passes][];
            var jobs = new List<(int Pass, int Group, string Source, string[] Names)>();
            for (var p = 0; p < passes; p++)
            {
                (string traced, IReadOnlyList<string> points) = ParticleLinker.Traced(_program.Passes[p].Pixel.Hlsl);
                int groups = (points.Count + 3) / 4;
                all[p] = new (byte[]? Code, string Why, string[] Names)[groups];
                for (var g = 0; g < groups; g++)
                {
                    string[] names = points.Skip(g * 4).Take(4).ToArray();
                    string channels = string.Join(", ", Enumerable.Range(0, 4).Select(c => g * 4 + c < points.Count
                        ? "PoefTrace[" + (g * 4 + c).ToString(CultureInfo.InvariantCulture) + "]"
                        : "-2.0f"));
                    string? source = ParticleLinker.Wrapped(traced, RawEntry, string.Empty, "    return float4(" + channels + ");");
                    if (source is null)
                    {
                        all[p][g] = (null, "it holds no PixelMain", names);
                        continue;
                    }

                    jobs.Add((p, g, source, names));
                }
            }

            Parallel.ForEach(jobs, job =>
            {
                byte[]? code = ParticleBytes(job.Source, RawEntry, "ps_5_0", _cache, out string why);
                all[job.Pass][job.Group] = (code, why.Length > 0 ? why : "it would not compile", job.Names);
            });
            return all;
        }

        /// <summary>Its account, read back: see <see cref="ParticleState"/>.</summary>
        public void State(ID3D11Device device, ID3D11DeviceContext context, List<string> lines)
        {
            ParticleProgram program = _program;
            lines.Add(Say($"emitter {program.Name}: pool {program.Count}, group {program.GroupSize}, {program.Passes.Count} passes ({string.Join(", ", program.Passes.Select(one => one.Blend))}), mesh {program.Mesh.Source}, {Steps} steps run"));
            if (!Landing())
            {
                lines.Add("  still compiling");
                return;
            }

            if (!_made)
            {
                lines.Add("  not made: " + (_why.Length > 0 ? _why : "not asked yet"));
                return;
            }

            EmitterRecord record = LastRecord;
            lines.Add(Say($"  record: translation {At(record.Translation)}, last {At(record.LastTranslation)}, rotation {At(record.Rotation)}, time {record.Time:0.####} (prev {record.PrevTime:0.####}), duration {record.EmitterDuration:0.###}, bones {record.Bones >> 8} + {record.Bones & 0xFF}, lock flags 0x{record.LockFlags:X}, culling tag 0x{record.CullingTag:X8} (flags 0x{record.CullingTag >> 14:X}, condition {(record.CullingTag >> 11) & 7}, group {((record.CullingTag >> 5) & 63) + 1}), start {record.ParticlesStart}"));
            if (Update is { } update)
            {
                lines.Add("  update globals: " + Globals(update));
            }

            for (var p = 0; p < Passes.Count; p++)
            {
                lines.Add(Say($"  pass {p} vertex globals: ") + Globals(Passes[p].Vertex));
            }

            List<PoolMember> layout = Update?.PoolLayout is { Count: > 0 } own ? own : Passes.Select(one => one.Vertex.PoolLayout).FirstOrDefault(one => one.Count > 0) ?? [];
            if (_pool is not null)
            {
                byte[] pool = Back(device, context, _pool);
                int stride = program.Count > 0 ? pool.Length / program.Count : pool.Length;
                Pool(lines, pool, stride, program.Count, layout);
            }

            if (_index is not null)
            {
                lines.Add("  order (particles_index_data): " + Words(Back(device, context, _index), 32));
            }

            if (_meta is not null)
            {
                lines.Add("  culling meta: " + Words(Back(device, context, _meta), 16));
            }
        }

        /// <summary>
        /// The pool written out: each member's range over every particle - floats as floats, a uint also as the two halves it packs - and the
        /// first particles member by member; the words alone where the reflection described no element.
        /// </summary>
        private static void Pool(List<string> lines, byte[] pool, int stride, int count, List<PoolMember> layout)
        {
            lines.Add(Say($"  pool: {count} particles of {stride} bytes, {pool.Length} read back; {Living(pool, stride, count)} not all nought"));
            if (layout.Count == 0)
            {
                for (var at = 0; at < Math.Min(count, MostStateParticles); at++)
                {
                    lines.Add(Say($"    particle {at}: ") + Words(pool.AsSpan(at * stride, stride).ToArray(), stride / 4));
                }

                return;
            }

            foreach (PoolMember member in layout)
            {
                lines.Add("    " + member.Name + " (" + member.Kind.ToString().ToLowerInvariant() + (member.Components > 1 ? member.Components.ToString(CultureInfo.InvariantCulture) : string.Empty)
                    + Say($", +{member.Offset}): ") + Range(pool, stride, count, member));
            }

            for (var at = 0; at < Math.Min(count, MostStateParticles); at++)
            {
                var one = new List<string>(layout.Count);
                foreach (PoolMember member in layout)
                {
                    var values = new string[member.Components];
                    for (var c = 0; c < member.Components; c++)
                    {
                        values[c] = Value(pool, (at * stride) + member.Offset + (c * 4), member.Kind);
                    }

                    one.Add(member.Name + " " + string.Join(" ", values));
                }

                lines.Add(Say($"    particle {at}: ") + string.Join(", ", one));
            }
        }

        /// <summary>How many particles hold any word that is not nought.</summary>
        private static int Living(byte[] pool, int stride, int count)
        {
            var living = 0;
            for (var at = 0; at < count && ((at + 1) * stride) <= pool.Length; at++)
            {
                if (pool.AsSpan(at * stride, stride).IndexOfAnyExcept((byte)0) >= 0)
                {
                    living++;
                }
            }

            return living;
        }

        /// <summary>A member's range over the pool, component by component.</summary>
        private static string Range(byte[] pool, int stride, int count, PoolMember member)
        {
            var parts = new string[member.Components];
            for (var c = 0; c < member.Components; c++)
            {
                double least = double.MaxValue, most = double.MinValue, sum = 0;
                float lowLeast = float.MaxValue, lowMost = float.MinValue, highLeast = float.MaxValue, highMost = float.MinValue;
                var read = 0;
                var nought = 0;
                for (var at = 0; at < count; at++)
                {
                    int offset = (at * stride) + member.Offset + (c * 4);
                    if (offset + 4 > pool.Length)
                    {
                        break;
                    }

                    uint word = BitConverter.ToUInt32(pool, offset);
                    nought += word == 0 ? 1 : 0;
                    double value = member.Kind switch
                    {
                        ShaderVariableType.Float => BitConverter.UInt32BitsToSingle(word),
                        ShaderVariableType.Int => (int)word,
                        _ => word,
                    };
                    if (double.IsFinite(value))
                    {
                        least = Math.Min(least, value);
                        most = Math.Max(most, value);
                        sum += value;
                        read++;
                    }

                    if (member.Kind != ShaderVariableType.Float)
                    {
                        float low = (float)BitConverter.UInt16BitsToHalf((ushort)word), high = (float)BitConverter.UInt16BitsToHalf((ushort)(word >> 16));
                        if (float.IsFinite(low))
                        {
                            lowLeast = Math.Min(lowLeast, low);
                            lowMost = Math.Max(lowMost, low);
                        }

                        if (float.IsFinite(high))
                        {
                            highLeast = Math.Min(highLeast, high);
                            highMost = Math.Max(highMost, high);
                        }
                    }
                }

                string halves = member.Kind == ShaderVariableType.Float || lowLeast > lowMost
                    ? string.Empty
                    : Say($" (halves {lowLeast:0.####}..{lowMost:0.####} | {(highLeast > highMost ? "-" : Say($"{highLeast:0.####}..{highMost:0.####}"))})");
                parts[c] = read == 0 ? "none finite" : Say($"{least:0.####}..{most:0.####} mean {sum / read:0.####}, {nought} nought") + halves;
            }

            return string.Join("; ", parts);
        }

        /// <summary>One word as its kind reads it - a uint also as its two halves.</summary>
        private static string Value(byte[] pool, int offset, ShaderVariableType kind)
        {
            if (offset + 4 > pool.Length)
            {
                return "-";
            }

            uint word = BitConverter.ToUInt32(pool, offset);
            return kind switch
            {
                ShaderVariableType.Float => Say($"{BitConverter.UInt32BitsToSingle(word):0.####}"),
                ShaderVariableType.Int => Say($"{(int)word}"),
                _ => Say($"{word}[{(float)BitConverter.UInt16BitsToHalf((ushort)word):0.###}|{(float)BitConverter.UInt16BitsToHalf((ushort)(word >> 16)):0.###}]"),
            };
        }

        /// <summary>A shader's $Globals as last written, every variable by name in the order it lies: its words as floats, a word that is no plain float also as a uint.</summary>
        private static string Globals(Shader shader)
        {
            byte[] bytes = shader.GlobalsHeld;
            var said = new List<string>(shader.Globals.Count);
            foreach ((string name, (int offset, int size)) in shader.Globals.OrderBy(one => one.Value.Offset))
            {
                int words = Math.Min(size / 4, 16);
                var values = new string[words];
                for (var w = 0; w < words; w++)
                {
                    int at = offset + (w * 4);
                    if (at + 4 > bytes.Length)
                    {
                        values[w] = "-";
                        continue;
                    }

                    uint word = BitConverter.ToUInt32(bytes, at);
                    float value = BitConverter.UInt32BitsToSingle(word);
                    values[w] = word != 0 && (float.IsSubnormal(value) || !float.IsFinite(value)) ? Say($"u{word}") : Say($"{value:0.####}");
                }

                said.Add(name + " " + string.Join(" ", values));
            }

            return said.Count == 0 ? "none" : string.Join(", ", said);
        }

        /// <summary>A buffer's first words.</summary>
        private static string Words(byte[] bytes, int most)
        {
            int words = Math.Min(bytes.Length / 4, most);
            var said = new string[words];
            for (var w = 0; w < words; w++)
            {
                said[w] = BitConverter.ToUInt32(bytes, w * 4).ToString(CultureInfo.InvariantCulture);
            }

            int nonzero = 0;
            for (var w = 0; w + 4 <= bytes.Length; w += 4)
            {
                nonzero += BitConverter.ToUInt32(bytes, w) != 0 ? 1 : 0;
            }

            return Say($"{bytes.Length / 4} words, {nonzero} not nought; first {string.Join(" ", said)}");
        }

        /// <summary>A buffer copied to the processor through a staging buffer of its size (the copy waits for the card).</summary>
        private static byte[] Back(ID3D11Device device, ID3D11DeviceContext context, ID3D11Buffer buffer)
        {
            int size = buffer.Description.ByteWidth;
            using ID3D11Buffer staging = device.CreateBuffer(new BufferDescription(size, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            context.CopyResource(staging, buffer);
            MappedSubresource mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var bytes = new byte[size];
                Marshal.Copy(mapped.DataPointer, bytes, 0, size);
                return bytes;
            }
            finally
            {
                context.Unmap(staging, 0);
            }
        }

        private static string At(Vector3 point) => Say($"({point.X:0.###}, {point.Y:0.###}, {point.Z:0.###})");

        private static string At(Vector4 point) => Say($"({point.X:0.###}, {point.Y:0.###}, {point.Z:0.###}, {point.W:0.###})");

        private static string Say(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }
}
