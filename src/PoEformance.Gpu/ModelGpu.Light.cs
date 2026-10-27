using System.Numerics;
using System.Runtime.InteropServices;
using PoEformance.Game.Files;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PoEformance.Gpu;

/// <summary>
/// The game's light on the graphics card - SceneLight as MeshPicture draws it.
/// </summary>
/// <remarks>
/// THE SUN'S SHADOW MAP IS DRAWN ON THE CARD, on the processor's own texels: ShadowMap.Framed lays the
/// map out from the same vertices, the casters are the same shapes (solid, cut-out through their
/// texture, and shadow-only), and each texel keeps its nearest depth through the blend's minimum
/// into a float target - the compare-and-swap ShadowMap.Build keeps it by, without the threads. Kept
/// while the mesh stands still and the sun and the map's side hold, as the canvas keeps its own.
///
/// WHAT A PIXEL READS IS HANDED OVER AS THE PROCESSOR HOLDS IT: the point lights and the grid they are
/// looked up by, the cube's texels, the grade's table, and the sRGB and gamma tables - each once, by
/// reference, and let go as the meshes are when unused.
/// </remarks>
public sealed partial class ModelGpu
{
    private readonly CardBuffer _tables;
    private readonly Dictionary<SceneLight, LightBuffers> _lit = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, CardBuffer> _read = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Writes the light's numbers, draws the sun's shadow map where it casts, and binds what the lit pixel shader reads.
    /// </summary>
    /// <param name="light">The light.</param>
    /// <param name="buffers">The mesh on the card, already bound for drawing.</param>
    /// <param name="plan">Its runs.</param>
    /// <param name="view">The camera's turn - the way into the picture is its third column.</param>
    /// <param name="placed">The vertices the picture is drawn from, the pose's where it moves.</param>
    /// <param name="pose">Which those are: nought the mesh's own, a pose's key, -1 a pose nothing is known of - a map is kept for the first two.</param>
    /// <param name="from">The first vertex the pose moves - <see cref="ModelScene.PoseFrom"/> - or nought.</param>
    private void Lit(SceneLight light, MeshBuffers buffers, Plan plan, Matrix4x4 view, Vector3[] placed, long pose, int from)
    {
        ID3D11DeviceContext context = _context;
        ShadowTarget? map = null;
        ShadowFrame frame = default;
        var drawn = false;
        int split = -1;
        if (light.SunShadows && light.SunColour != Vector3.Zero)
        {
            if (pose >= 0 && buffers.ShadowFor is { } was && was.Direction == light.SunDirection && was.Side == light.ShadowSide
                && was.Pose == pose && ReferenceEquals(was.Plan, plan) && buffers.Shadow is not null)
            {
                map = buffers.Shadow;
                frame = was.Frame;
            }
            else if (Framed(buffers, placed, light, from, plan.Sways && _swayed is not null, out split) is { } framed)
            {
                frame = framed;
                if (buffers.Shadow is not { } kept || kept.Side != framed.Side)
                {
                    buffers.Shadow?.Dispose();
                    buffers.Shadow = new ShadowTarget(_device, framed.Side);
                }

                map = buffers.Shadow;
                drawn = true;
                buffers.ShadowFor = pose >= 0 ? (light.SunDirection, light.ShadowSide, framed, pose, plan) : null;
            }
            else
            {
                buffers.ShadowFor = null;
            }
        }

        // THE VIEW'S THIRD COLUMN IS THE WAY INTO THE PICTURE in model space - MeshPicture.Drawing's _toEye.
        var into = new Vector3(view.M13, view.M23, view.M33);
        Vector3 toEye = into.LengthSquared() > 0f ? -Vector3.Normalize(into) : -Vector3.UnitZ;
        SceneLight.LightCells cells = light.Cells;
        ColourGrade? grade = light.Grade;
        int ambient = light.Ambient == SceneAmbient.Cube && light.Cube is not null ? 2
            : light.Ambient is SceneAmbient.Flat or SceneAmbient.Cube ? 1
            : 0;

        // THE WATER'S REFLECTION reads the specular cube where it is asked for and the cube ambient is on - SceneLight.Reflected.
        CubeMap? mirror = ambient == 2 && light.WaterReflection ? light.Mirror : null;
        Written(_scene, new SceneConstants
        {
            CubeTurn = light.CubeTurn,
            SunColour = new Vector4(light.SunColour, light.SunColour != Vector3.Zero ? 1f : 0f),
            SunTravels = new Vector4(light.SunDirection, map is null ? 0f : 1f),
            ToEye = new Vector4(toEye, light.WaterReflection && light.WaterScene ? 1f : 0f),
            Surround = new Vector4(light.FlatAmbient, light.CubeBrightness, light.DirectLightEnvRatio, light.GiEnvOcclusion),
            Finish = new Vector4(light.Exposure, light.WaterGlints ? 1f : 0f, light.WaterReflection ? 1f : 0f, mirror?.Size ?? 0),
            CellLeast = new Vector4(cells.Least, cells.Size),
            ShadowU = new Vector4(frame.U, frame.Least.X),
            ShadowV = new Vector4(frame.V, frame.Least.Y),
            ShadowW = new Vector4(frame.W, frame.Nearest),
            ShadowSize = map is null ? default : new Vector4(frame.PerUnit, 1f / frame.PerUnit, frame.Side, 0f),
            Ambient = new Int4(ambient, grade is null ? 0 : 1, ambient == 2 ? light.Cube!.Size : 0, light.PointCount),
            Cells = new Int4(cells.X, cells.Y, cells.Z, (cells.X * cells.Y * cells.Z) + 1),
            Graded = grade is null ? default : new Int4(grade.Width, grade.Height, grade.Depth, 0),
        });
        context.VSSetConstantBuffer(2, _scene);
        context.PSSetConstantBuffer(2, _scene);

        // TAKEN OFF BEFORE IT IS DRAWN INTO - a texture bound for reading is unbound as a target by the
        // runtime, with a warning, and the other way round.
        context.PSSetShaderResource(6, null!);
        if (drawn && split > 0)
        {
            Staged(map!, buffers, plan, frame, split);
        }
        else if (drawn)
        {
            Cast(map!, plan, plan.Casts, clear: true);
        }

        LightBuffers? points = light.PointCount > 0 ? Points(light) : null;
        context.PSSetShaderResource(2, points?.Points.View!);
        context.PSSetShaderResource(3, points?.Reach.View!);
        context.PSSetShaderResource(4, ambient == 2 ? Read(light.Cube!, light.Cube!.Texels).View : null!);
        context.PSSetShaderResource(ModelShaders.MirrorSlot, mirror is null ? null! : Read(mirror, mirror.Texels).View);
        context.PSSetShaderResource(5, grade is null ? null! : Read(grade, MemoryMarshal.Cast<Vector3, float>(grade.Texels)).View);
        context.PSSetShaderResource(6, map?.View!);
    }

    /// <summary>How many shadow maps were drawn as a kept stage and the monster cast over it - see <see cref="Staged"/>; for the tests.</summary>
    internal long ShadowsStaged { get; private set; }

    /// <summary>
    /// The frame the sun's map is drawn in - from every vertex, or where a pose moves only a monster's, from the stage's bounds kept and his scanned - and the triangle the stage's casters end at where the monster stands inside the stage's frame, else -1.
    /// </summary>
    /// <param name="swaying">Whether runs sway: their casters are then cast again every frame over the stage's kept - a room alone kept whole but for them.</param>
    private static ShadowFrame? Framed(MeshBuffers buffers, Vector3[] placed, SceneLight light, int from, bool swaying, out int split)
    {
        split = from > 0 && from < placed.Length ? buffers.Split(from) : -1;
        if (split <= 0 && swaying)
        {
            // NO MONSTER BEFORE THE SWAY: every vertex is the stage's, its bounds kept - a ship sways a few units inside them.
            split = buffers.Triangles;
            from = placed.Length;
        }

        if (split <= 0)
        {
            return ShadowMap.Framed(placed, light.SunDirection, light.ShadowSide);
        }

        ShadowBounds stage = buffers.StageBounds(from, light.SunDirection);
        ShadowBounds whole = from < placed.Length ? stage.With(ShadowMap.Bounded(placed.AsSpan(from), light.SunDirection)) : stage;
        if (whole != stage)
        {
            split = -1;
        }

        return ShadowMap.Framed(whole, light.SunDirection, light.ShadowSide);
    }

    /// <summary>
    /// The sun's map of a monster posed on a stage: the stage's own casters drawn once into a map of their own and kept, copied in, and the monster's cast over it.
    /// </summary>
    /// <remarks>
    /// THE SAME MAP TO THE BIT as every caster drawn at once: each texel keeps the least depth drawn
    /// into it whatever the order, and the monster standing inside the stage's frame leaves the frame
    /// the stage's alone (<see cref="Framed(MeshBuffers, Vector3[], SceneLight, int, out int)"/>). A
    /// playing animation recast a room of two million triangles every frame for a monster's worth of moved ones.
    /// </remarks>
    private void Staged(ShadowTarget map, MeshBuffers buffers, Plan plan, ShadowFrame frame, int split)
    {
        (Run[] stage, Run[] moving) = buffers.Parted(plan, split);
        if (buffers.StageShadow is not { } kept || kept.Side != frame.Side
            || buffers.StageShadowFor is not { } was || !ReferenceEquals(was.Plan, plan) || was.Frame != frame || was.Split != split)
        {
            if (buffers.StageShadow is not { } had || had.Side != frame.Side)
            {
                buffers.StageShadow?.Dispose();
                buffers.StageShadow = new ShadowTarget(_device, frame.Side);
            }

            Cast(buffers.StageShadow, plan, stage, clear: true);
            buffers.StageShadowFor = (plan, frame, split);
        }

        _context.CopyResource(map.Texture, buffers.StageShadow.Texture);
        Cast(map, plan, moving, clear: false);
        ShadowsStaged++;
    }

    /// <summary>The sun's shadow map drawn: these casters along the light, each texel keeping its nearest depth - over a cleared map or over what it holds.</summary>
    private void Cast(ShadowTarget map, Plan plan, Run[] casts, bool clear)
    {
        ID3D11DeviceContext context = _context;
        if (clear)
        {
            context.ClearRenderTargetView(map.Target, new Color4(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));
        }

        context.OMSetRenderTargets(map.Target, null);
        context.RSSetViewport(new Viewport(0f, 0f, map.Side, map.Side, 0f, 1f));
        context.OMSetBlendState(_least);
        context.OMSetDepthStencilState(_ignores);
        context.VSSetShader(_casting);
        context.PSSetShader(_away);
        foreach (Run run in casts)
        {
            // ONLY A CUT-OUT ONE READS ITS TEXTURE - ShadowMap.Build cuts nothing else.
            Mipmaps? worn = run.Blend == MaterialBlend.Cutout ? plan.Palette[run.Wears] : null;
            ID3D11ShaderResourceView? view = worn is null ? null : Skin(worn).View;
            context.PSSetShaderResource(0, view!);
            Part(new Vector4(view is null ? 0f : 1f, view is null ? -1f : MeshPicture.CutoutAlpha, 0f, 0f), run.Sway);
            context.DrawIndexed(run.Count * 3, run.First * 3, 0);
        }

        // TAKEN OFF AS A TARGET BEFORE IT IS BOUND FOR READING: a resource still bound for output is
        // set to a shader slot as NULL by the runtime, and a NULL map reads nought everywhere - every
        // pixel in shadow, on every frame the map was drawn on. The first WARP run caught it on the
        // coarse map; the full one passed only because it was drawn twice, the second from the kept map.
        context.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
    }

    /// <summary>
    /// What casts a shadow, in as few draws as the mesh's order allows: every solid and shadow-only run, one after another, as one draw, and each cut-out run that wears a texture on its own.
    /// </summary>
    /// <remarks>
    /// MERGED BECAUSE THE ORDER DOES NOT MATTER - the map keeps the nearest depth whichever is drawn
    /// first - and a solid caster reads no texture, so a room's thousands of runs are a few draws.
    /// </remarks>
    private static Run[] Casting(List<Run> runs, Mipmaps?[] palette, List<ShadeProgram> programs)
    {
        var casts = new List<Run>();
        foreach (Run run in runs)
        {
            // NOR A CUT-OUT SHAPE ITS PROGRAM CUTS AWAY AT EVERY PIXEL - MeshPicture's Casts, ShadeProgram.FixedAlpha.
            if (run.Blend is MaterialBlend.Alpha or MaterialBlend.Additive
                || (run.Blend == MaterialBlend.Cutout && run.Shade >= 0 && programs[run.Shade].CutAway(MeshPicture.CutoutAlpha)))
            {
                continue;
            }

            if (run.Blend == MaterialBlend.Cutout && palette[run.Wears] is not null)
            {
                casts.Add(run);
                continue;
            }

            // NOT ACROSS A SWAY: a group's casters are drawn with its own matrix.
            if (casts.Count > 0 && casts[^1] is { Blend: MaterialBlend.Opaque } last && last.First + last.Count == run.First && last.Sway == run.Sway)
            {
                casts[^1] = last with { Count = last.Count + run.Count };
            }
            else
            {
                casts.Add(new Run(run.First, run.Count, 0, MaterialBlend.Opaque, Sway: run.Sway));
            }
        }

        return [.. casts];
    }

    /// <summary>The blend that keeps the smaller of what is there and what is drawn - the map's nearest depth.</summary>
    /// <summary>A blend that writes no colour - the lifted water's walk for its nearest crossing, depth alone (ModelGpu's Surfaced).</summary>
    private static BlendDescription Unseen()
    {
        BlendDescription unseen = BlendDescription.Opaque;
        unseen.RenderTarget[0].RenderTargetWriteMask = ColorWriteEnable.None;
        return unseen;
    }

    private static BlendDescription Least()
    {
        var least = new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One);
        least.RenderTarget[0].BlendOperation = BlendOperation.Min;
        least.RenderTarget[0].BlendOperationAlpha = BlendOperation.Min;
        return least;
    }

    /// <summary>
    /// ShadeProgram's sRGB tables, ColourGrade's gamma table and GlossLight's environment, one after another - where ModelShaders.Common's table offsets say.
    /// </summary>
    private static CardBuffer Tables(ID3D11Device device)
    {
        ReadOnlySpan<float> linear = ShadeProgram.LinearTable, srgb = ShadeProgram.SrgbTable, gamma = ColourGrade.Encoding;
        ReadOnlySpan<float> bias = GlossLight.BiasTable, scale = GlossLight.ScaleTable;
        var all = new float[linear.Length + srgb.Length + gamma.Length + bias.Length + scale.Length];
        Span<float> rest = all;
        linear.CopyTo(rest);
        rest = rest[linear.Length..];
        srgb.CopyTo(rest);
        rest = rest[srgb.Length..];
        gamma.CopyTo(rest);
        rest = rest[gamma.Length..];
        bias.CopyTo(rest);
        scale.CopyTo(rest[bias.Length..]);
        return CardBuffer.Of<float>(device, all, Format.R32_Float, all.Length);
    }

    /// <summary>A light's point lights and their grid on the card, uploaded the first time it is drawn.</summary>
    private LightBuffers Points(SceneLight light)
    {
        if (!_lit.TryGetValue(light, out LightBuffers? buffers))
        {
            ReadOnlySpan<SceneLight.Point> points = light.Points;
            var packed = new Vector4[points.Length * 3];
            for (var at = 0; at < points.Length; at++)
            {
                SceneLight.Point one = points[at];
                packed[at * 3] = new Vector4(one.Position, one.Cutoff);
                packed[(at * 3) + 1] = new Vector4(one.Colour, one.Core);
                packed[(at * 3) + 2] = new Vector4(one.Zero, 0f, 0f, 0f);
            }

            ReadOnlySpan<int> starts = light.CellStarts, entries = light.CellEntries;
            var reach = new int[starts.Length + Math.Max(1, entries.Length)];
            starts.CopyTo(reach);
            entries.CopyTo(reach.AsSpan(starts.Length));
            buffers = new LightBuffers(
                CardBuffer.Of<Vector4>(_device, packed, Format.R32G32B32A32_Float, packed.Length),
                CardBuffer.Of<int>(_device, reach, Format.R32_UInt, reach.Length));
            _lit[light] = buffers;
        }

        buffers.Used = _draws;
        return buffers;
    }

    /// <summary>A cube's or a grade's texels on the card, uploaded the first time they are read.</summary>
    private CardBuffer Read(object owner, ReadOnlySpan<float> texels)
    {
        if (!_read.TryGetValue(owner, out CardBuffer? buffer))
        {
            buffer = CardBuffer.Of(_device, texels, Format.R32_Float, texels.Length);
            _read[owner] = buffer;
        }

        buffer.Used = _draws;
        return buffer;
    }

    /// <summary>Lets go of the lights, cubes and grades that have not been drawn for <see cref="Kept"/> draws.</summary>
    private void ForgetLight()
    {
        foreach (KeyValuePair<SceneLight, LightBuffers> one in _lit.Where(one => _draws - one.Value.Used > Kept).ToArray())
        {
            one.Value.Dispose();
            _lit.Remove(one.Key);
        }

        foreach (KeyValuePair<object, CardBuffer> one in _read.Where(one => _draws - one.Value.Used > Kept).ToArray())
        {
            one.Value.Dispose();
            _read.Remove(one.Key);
        }
    }

    private void DisposeLight()
    {
        foreach (LightBuffers one in _lit.Values)
        {
            one.Dispose();
        }

        foreach (CardBuffer one in _read.Values)
        {
            one.Dispose();
        }

        _lit.Clear();
        _read.Clear();
        _tables.Dispose();
    }

    /// <summary>The light's constants - ModelShaders' Scene, field for field, sixteen-byte rows.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SceneConstants
    {
        public Matrix4x4 CubeTurn;
        public Vector4 SunColour;
        public Vector4 SunTravels;
        public Vector4 ToEye;
        public Vector4 Surround;
        public Vector4 Finish;
        public Vector4 CellLeast;
        public Vector4 ShadowU;
        public Vector4 ShadowV;
        public Vector4 ShadowW;
        public Vector4 ShadowSize;
        public Int4 Ambient;
        public Int4 Cells;
        public Int4 Graded;
    }

    /// <summary>An HLSL int4.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Int4(int X, int Y, int Z, int W);

    /// <summary>A light's point lights - three rows each - and its grid's starts and entries, one after the other.</summary>
    private sealed class LightBuffers(CardBuffer points, CardBuffer reach) : IDisposable
    {
        public CardBuffer Points { get; } = points;

        public CardBuffer Reach { get; } = reach;

        public long Used { get; set; }

        public void Dispose()
        {
            Reach.Dispose();
            Points.Dispose();
        }
    }

    /// <summary>A buffer a shader reads element by element, and the view it reads through.</summary>
    private sealed class CardBuffer : IDisposable
    {
        private readonly ID3D11Buffer _buffer;

        private CardBuffer(ID3D11Buffer buffer, ID3D11ShaderResourceView view)
        {
            _buffer = buffer;
            View = view;
        }

        public ID3D11ShaderResourceView View { get; }

        public long Used { get; set; }

        public static CardBuffer Of<T>(ID3D11Device device, ReadOnlySpan<T> data, Format format, int elements)
            where T : unmanaged
        {
            ID3D11Buffer buffer = device.CreateBuffer(data, BindFlags.ShaderResource, ResourceUsage.Immutable);
            var described = new ShaderResourceViewDescription
            {
                Format = format,
                ViewDimension = ShaderResourceViewDimension.Buffer,
                Buffer = new BufferShaderResourceView { FirstElement = 0, NumElements = elements },
            };
            return new CardBuffer(buffer, device.CreateShaderResourceView(buffer, described));
        }

        public void Dispose()
        {
            View.Dispose();
            _buffer.Dispose();
        }
    }

    /// <summary>The sun's shadow map on the card: one float a texel, the nearest depth.</summary>
    private sealed class ShadowTarget : IDisposable
    {
        public ShadowTarget(ID3D11Device device, int side)
        {
            Side = side;
            Texture = device.CreateTexture2D(new Texture2DDescription(
                Format.R32_Float, side, side, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
            Target = device.CreateRenderTargetView(Texture);
            View = device.CreateShaderResourceView(Texture);
        }

        public int Side { get; }

        public ID3D11Texture2D Texture { get; }

        public ID3D11RenderTargetView Target { get; }

        public ID3D11ShaderResourceView View { get; }

        public void Dispose()
        {
            View.Dispose();
            Target.Dispose();
            Texture.Dispose();
        }
    }
}
