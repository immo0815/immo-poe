using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PoEformance.Game.Files;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D11.Shader;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PoEformance.Gpu;

/// <summary>
/// The engine's GPU particles on the card: each emitter's update compute shader dispatched over its pool, then its render passes drawn, one
/// instance a particle, over the picture after its translucent shapes - the shaders the engine's own graphs and sources link to
/// (ParticleLinker), compiled by FXC behind the frame.
/// </summary>
/// <remarks>
/// THE CARD'S ALONE (DECIDED from the live client): the processor's picture leaves them out, and the line under it says so.
/// WHAT THE SHADERS ARE HANDED is bound by NAME, read off each one's reflection, never by a slot the tool assumes: the update's pool
/// (particles_data_write), its order (particles_index_data_write), the culling buffers, the emitter record, the bones and the spline LUT; a
/// render pass's the same read-only, its textures by the globals the linker declared (<c>res_…</c>), the engine's samplers by name, and every
/// other resource a neutral one (a white 1x1 picture, a black cube, an empty list) - the engine's lights, shadows and environment are not
/// handed over, so a lit particle is lit by its own emission alone (said under the picture). The cbuffer $Globals is filled by name too.
/// THE VALUES NO FILE HOLDS are the decided readings (2026-11-01, from the live client): instance_count the pool, batch_size one,
/// frame_delta_time (dt, 0, 1, 0), frame_index the pane's; the scene's matrices the picture's own clip (scene_view_projection_transform),
/// so a particle tests the picture's reversed depth as everything else does, read-only.
/// THE COLOUR the engine's pass writes is linear and unexposed; the tool's own entry after it (ParticleLinker.Wrapped) exposes it, grades it
/// and encodes it as every mixed layer of the picture is, then lays it on premultiplied: AlphaBlend by its alpha, Additive its light times
/// its alpha added - the engine's blend states are in no file read (CHOSEN, said).
/// </remarks>
public sealed partial class ModelGpu
{
    /// <summary>The cbuffer and the grade the finishing entry reads, on registers no linked pass takes - b12 and t127.</summary>
    private const int FinishBuffer = 12, FinishGrade = 127;

    /// <summary>The finishing entry every pass's pixel shader is compiled from.</summary>
    private const string Finished = "PixelFinished";

    private readonly Dictionary<ParticleProgram, ParticleCard> _particleCards = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, (ID3D11Buffer Buffer, ID3D11ShaderResourceView View)> _emptyStructured = [];
    private ID3D11Buffer? _particleFinish;
    private string _particlesFailed = string.Empty;

    /// <summary>The step the particles' drawing is at, named where the card refuses one - an HRESULT alone says nothing of which call it was.</summary>
    private string _particleStep = string.Empty;
    private ID3D11SamplerState?[]? _engineSamplers;
    private ID3D11ShaderResourceView? _white, _blackCube, _zeroUints, _zeroTyped, _zeroFloats;
    private ID3D11Texture2D? _whiteTexture, _cubeTexture, _uintTexture;
    private ID3D11Buffer? _typedBuffer, _floatBuffer;
    private ID3D11BlendState? _particleMixes, _particleAdds;
    private ID3D11RasterizerState? _particleRaster;
    private ID3D11DepthStencilState? _particleTests;

    /// <summary>What the last picture's particles came to - how many emitters were drawn, which still compile, why any was not - or empty for none.</summary>
    public string ParticlesSaid { get; private set; } = string.Empty;

    private ParticleCard Card(ParticleProgram program)
    {
        if (!_particleCards.TryGetValue(program, out ParticleCard? card))
        {
            card = new ParticleCard(program, _cache);
            _particleCards[program] = card;
        }

        card.Used = _draws;
        return card;
    }

    /// <summary>
    /// Every emitter of the frame stepped and drawn over the picture in the target - after its translucent shapes, before the fog: each one's
    /// update dispatched over its pool, then its passes drawn testing the depth and writing none.
    /// </summary>
    private void Particled(ID3D11DeviceContext context, ModelTarget target, ParticleFrame frame, in ParticleView view, SceneLight? light)
    {
        // THE PICTURE'S OWN BINDINGS HELD ACROSS THE PARTICLES AND PUT BACK (v0.1.451): their passes bind and then unbind every resource,
        // sampler and constant buffer their shaders name - t0..t12 among them, where the light keeps its points, reach, cube, grade and
        // shadow map (ModelGpu.Light) for the passes after, the screen-space fog's grading among them (a review of v0.1.450's capture).
        var pixelViews = new ID3D11ShaderResourceView[HeldViews];
        var vertexViews = new ID3D11ShaderResourceView[HeldViews];
        var samplers = new ID3D11SamplerState[HeldSamplers];
        var pixelBuffers = new ID3D11Buffer[HeldBuffers];
        var vertexBuffers = new ID3D11Buffer[HeldBuffers];
        context.PSGetShaderResources(0, HeldViews, pixelViews);
        context.VSGetShaderResources(0, HeldViews, vertexViews);
        context.PSGetSamplers(0, HeldSamplers, samplers);
        context.PSGetConstantBuffers(0, HeldBuffers, pixelBuffers);
        context.VSGetConstantBuffers(0, HeldBuffers, vertexBuffers);
        try
        {
            Particles(context, target, frame, view, light);
        }
        finally
        {
            context.PSSetShaderResources(0, HeldViews, pixelViews);
            context.VSSetShaderResources(0, HeldViews, vertexViews);
            context.PSSetSamplers(0, HeldSamplers, samplers);
            context.PSSetConstantBuffers(0, HeldBuffers, pixelBuffers);
            context.VSSetConstantBuffers(0, HeldBuffers, vertexBuffers);
            foreach (IDisposable? one in pixelViews.Concat<IDisposable?>(vertexViews).Concat(samplers).Concat(pixelBuffers).Concat(vertexBuffers))
            {
                one?.Dispose();
            }
        }
    }

    /// <summary>The resource slots of a stage held across the particles: all of them (D3D11_COMMONSHADER_INPUT_RESOURCE_SLOT_COUNT).</summary>
    private const int HeldViews = 128;

    /// <summary>The sampler slots held across the particles (D3D11_COMMONSHADER_SAMPLER_SLOT_COUNT).</summary>
    private const int HeldSamplers = 16;

    /// <summary>The constant buffer slots held across the particles (D3D11_COMMONSHADER_CONSTANT_BUFFER_API_SLOT_COUNT).</summary>
    private const int HeldBuffers = 14;

    /// <summary>The emitters stepped and drawn - <see cref="Particled"/>'s body, its bindings held around it.</summary>
    private void Particles(ID3D11DeviceContext context, ModelTarget target, ParticleFrame frame, in ParticleView view, SceneLight? light)
    {
        var drawn = 0;
        var compiling = 0;
        var failed = new List<string>();
        foreach (ParticleEmitterNow emitter in frame.Emitters)
        {
            ParticleCard card = Card(emitter.Program);
            if (!card.Landing())
            {
                compiling++;
                continue;
            }

            if (!card.Made(_device, out string why))
            {
                failed.Add($"{emitter.Program.Name}: {why}");
                continue;
            }

            _particleStep = "the neutral resources and the particles' states";
            Neutrals();
            _particleStep = emitter.Program.Name + "'s update";
            Stepped(context, card, emitter, frame, view);
            _particleStep = emitter.Program.Name + "'s passes";
            drawn += Drew(context, target, card, emitter, frame, view, light);
            card.LastDrawn = emitter;
        }

        _particleLast = (target, frame, view, light);
        ParticlesSaid = string.Create(
            CultureInfo.InvariantCulture,
            $"particles: {drawn} passes of {frame.Emitters.Count} emitters drawn on the card{(compiling > 0 ? $", {compiling} still compiling" : string.Empty)}{(failed.Count > 0 ? "; " + string.Join("; ", failed) : string.Empty)}");
    }

    /// <summary>One emitter's update: its record, bones and globals written, its compute shader run over the pool.</summary>
    private void Stepped(ID3D11DeviceContext context, ParticleCard card, ParticleEmitterNow emitter, ParticleFrame frame, in ParticleView view)
    {
        ParticleProgram program = emitter.Program;
        card.LastRecord = Record(emitter, card.Fresh);
        context.UpdateSubresource(card.LastRecord, card.Emitter!);
        card.Steps++;
        Vector4[] bones = card.BonesHeld;
        int count = Math.Min(emitter.Bones.Length, bones.Length / 2);
        for (var at = 0; at < count; at++)
        {
            bones[at * 2] = emitter.Bones[at];
            bones[(at * 2) + 1] = at < emitter.LastBones.Length ? emitter.LastBones[at] : emitter.Bones[at];
        }

        context.UpdateSubresource(bones, card.Bones!);
        Shader update = card.Update!;
        Globals(context, update, program, emitter, frame, view);
        context.CSSetShader(update.Compute);
        context.CSSetConstantBuffer(update.GlobalsSlot, update.Constants);
        foreach (Binding bind in update.Bindings)
        {
            if (bind.Kind is ShaderInputType.ConstantBuffer or ShaderInputType.TextureBuffer)
            {
                // A cbuffer's register is b, not t: handed a neutral view it would take the place of a real one at the same number.
                continue;
            }

            if (bind.Unordered)
            {
                context.CSSetUnorderedAccessView(bind.Slot, card.Unordered(bind.Name)!);
            }
            else if (bind.Kind == ShaderInputType.Sampler)
            {
                context.CSSetSampler(bind.Slot, Sampler(bind.Name));
            }
            else
            {
                context.CSSetShaderResource(bind.Slot, card.Read(bind.Name, update) ?? Neutral(bind));
            }
        }

        int threads = Math.Max(1, update.Threads);
        context.Dispatch((program.Count + threads - 1) / threads, 1, 1);

        foreach (Binding bind in update.Bindings)
        {
            if (bind.Unordered)
            {
                context.CSSetUnorderedAccessView(bind.Slot, null!);
            }
            else if (bind.Kind is not (ShaderInputType.Sampler or ShaderInputType.ConstantBuffer or ShaderInputType.TextureBuffer))
            {
                context.CSSetShaderResource(bind.Slot, null!);
            }
        }

        context.CSSetShader(null);
        card.Fresh = false;
    }

    /// <summary>
    /// One emitter's render passes drawn over the picture, an instance a particle; how many were drawn. With <paramref name="into"/> they are
    /// drawn into that target instead, unblended - and against the picture's depth only where <paramref name="tested"/> - for the capture's
    /// account of what a pass puts on the screen (ParticleState); <paramref name="only"/> draws that pass alone.
    /// </summary>
    private int Drew(
        ID3D11DeviceContext context, ModelTarget target, ParticleCard card, ParticleEmitterNow emitter, ParticleFrame frame, in ParticleView view, SceneLight? light,
        ID3D11RenderTargetView? into = null, bool tested = true, int only = -1, ID3D11PixelShader? standIn = null, ID3D11GeometryShader? streamed = null)
    {
        ParticleProgram program = emitter.Program;
        context.OMSetRenderTargets(into ?? target.AccumulatedTarget, tested ? target.Depth : null);
        context.RSSetViewport(new Viewport(0f, 0f, target.Size, target.Size, 0f, 1f));
        context.RSSetState(_particleRaster);
        context.OMSetDepthStencilState(_particleTests);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetIndexBuffer(card.MeshIndices, Format.R32_UInt, 0);
        Finish(context, light);
        var drawn = 0;
        for (var p = 0; p < card.Passes.Count; p++)
        {
            if (only >= 0 && p != only)
            {
                continue;
            }

            PassCard pass = card.Passes[p];
            ParticlePassProgram written = program.Passes[p];
            context.OMSetBlendState(into is not null ? null : written.Blend == ParticleBlend.Additive ? _particleAdds : _particleMixes);
            context.IASetInputLayout(pass.Layout);
            card.MeshStreams(context, pass);
            Globals(context, pass.Vertex, program, emitter, frame, view);
            Globals(context, pass.Pixel, program, emitter, frame, view);
            context.VSSetShader(pass.Vertex.VertexShader);
            context.VSSetConstantBuffer(pass.Vertex.GlobalsSlot, pass.Vertex.Constants);
            Bound(context, card, pass.Vertex, written, pixel: false);
            context.PSSetShader(standIn ?? pass.Pixel.PixelShader);
            context.PSSetConstantBuffer(pass.Pixel.GlobalsSlot, pass.Pixel.Constants);
            context.PSSetConstantBuffer(FinishBuffer, _particleFinish);
            Bound(context, card, pass.Pixel, written, pixel: true);
            if (streamed is not null)
            {
                context.GSSetShader(streamed);
            }

            context.DrawIndexedInstanced(program.Mesh.Indices.Length, program.Count, 0, 0, 0);
            if (streamed is not null)
            {
                context.GSSetShader(null!);
            }

            Unbound(context, pass);
            drawn++;
        }

        context.VSSetShader(_placed);
        return drawn;
    }

    /// <summary>A shader's resources bound by name - the emitter's buffers, its pass's textures, the engine's samplers, a neutral one for the rest.</summary>
    private void Bound(ID3D11DeviceContext context, ParticleCard card, Shader shader, ParticlePassProgram pass, bool pixel)
    {
        foreach (Binding bind in shader.Bindings)
        {
            if (bind.Kind == ShaderInputType.Sampler)
            {
                if (pixel)
                {
                    context.PSSetSampler(bind.Slot, Sampler(bind.Name));
                }
                else
                {
                    context.VSSetSampler(bind.Slot, Sampler(bind.Name));
                }

                continue;
            }

            if (bind.Kind is ShaderInputType.ConstantBuffer or ShaderInputType.TextureBuffer)
            {
                continue;
            }

            // THE TOOL'S OWN FINISHING GRADE (PoefGrade at t127) IS FINISH'S: v0.1.450 bound a neutral of zeros over it here, which graded
            // every colour of a lit picture to nought - the flame drew exactly nothing (v0.1.449's capture), the embers would have drawn black.
            if (pixel && bind.Slot == FinishGrade)
            {
                continue;
            }

            ID3D11ShaderResourceView view = card.Read(bind.Name, shader) ?? Texture(pass, bind.Name) ?? Neutral(bind);
            if (pixel)
            {
                context.PSSetShaderResource(bind.Slot, view);
            }
            else
            {
                context.VSSetShaderResource(bind.Slot, view);
            }
        }
    }

    private void Unbound(ID3D11DeviceContext context, PassCard pass)
    {
        foreach (Binding bind in pass.Vertex.Bindings.Where(one => one.Kind is not (ShaderInputType.Sampler or ShaderInputType.ConstantBuffer or ShaderInputType.TextureBuffer)))
        {
            context.VSSetShaderResource(bind.Slot, null!);
        }

        // THE FINISHING GRADE STAYS for the emitter's next pass - Finish binds it once a Drew.
        foreach (Binding bind in pass.Pixel.Bindings.Where(one => one.Kind is not (ShaderInputType.Sampler or ShaderInputType.ConstantBuffer or ShaderInputType.TextureBuffer) && one.Slot != FinishGrade))
        {
            context.PSSetShaderResource(bind.Slot, null!);
        }
    }

    /// <summary>A pass's texture by the global the linker declared it under, uploaded on first use; null where it names none of that name.</summary>
    private ID3D11ShaderResourceView? Texture(ParticlePassProgram pass, string name)
    {
        foreach ((string global, string _, Mipmaps? texture) in pass.Textures)
        {
            if (global == name)
            {
                return texture is not null && texture.Width <= _largest && texture.Height <= _largest ? Skin(texture).View : _white;
            }
        }

        return null;
    }

    /// <summary>The finishing entry's constants: the light's exposure and grade, and its grade's texels at t127 - as a mixed layer of the picture is finished.</summary>
    private void Finish(ID3D11DeviceContext context, SceneLight? light)
    {
        _particleFinish ??= _device.CreateBuffer(Marshal.SizeOf<FinishConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        ColourGrade? grade = light?.Grade;
        Written(_particleFinish, new FinishConstants
        {
            Exposure = new Vector4(light?.Exposure ?? 1f, grade is null ? 0f : 1f, 0f, 0f),
            Graded = new Int4(grade?.Width ?? 0, grade?.Height ?? 0, grade?.Depth ?? 0, 0),
        });
        context.PSSetShaderResource(FinishGrade, grade is null ? null! : Read(grade, MemoryMarshal.Cast<Vector3, float>(grade.Texels)).View);
    }

    /// <summary>
    /// A shader's $Globals filled by name: the decided readings, the emitter's values (GpuParticleObjectUniforms) and the picture's camera; every
    /// other global nought.
    /// </summary>
    private void Globals(ID3D11DeviceContext context, Shader shader, ParticleProgram program, ParticleEmitterNow emitter, ParticleFrame frame, in ParticleView view)
    {
        if (shader.Constants is null)
        {
            return;
        }

        byte[] bytes = shader.GlobalsHeld;
        Array.Clear(bytes);
        Dictionary<string, (int Offset, int Size)> globals = shader.Globals;
        void Put<T>(string name, T value)
            where T : unmanaged
        {
            if (globals.TryGetValue(name, out (int Offset, int Size) at) && at.Offset + Marshal.SizeOf<T>() <= bytes.Length)
            {
                MemoryMarshal.Write(bytes.AsSpan(at.Offset), in value);
            }
        }

        Put("instance_count", (uint)program.Count);
        Put("batch_size", 1u);
        Put("gpu_particle_emitter", 0u);
        Put("gpu_particle_emitter_id", (uint)(RuntimeHelpers.GetHashCode(program) & 0x7FFFFFFF));
        Put("gpu_particle_pass_count", (uint)program.Passes.Count);
        Put("gpu_particle_culling_priority", program.Culling);
        Put("gpu_particle_count", (uint)program.Count);
        Put("gpu_particle_seed", program.Seed);
        Put("gpu_particle_burst", program.Burst);
        Put("gpu_particles_duration", program.Duration);
        Put("gpu_particle_dyn_culling", 0u);
        Put("frame_delta_time", new Vector4(frame.Delta, 0f, 1f, 0f));
        Put("frame_index", frame.Index);
        Put("time", emitter.Time);
        Put("world_transform", Matrix4x4.Identity);
        Put("prev_world_transform", Matrix4x4.Identity);
        Put("scene_view_projection_transform", view.Clip);
        Put("last_view_projection_transform", view.Clip);
        Put("scene_view_projection_transform_nopost", view.Clip);
        Put("scene_view_projection_transform_inv", view.Unclip);
        Put("scene_view_transform", view.View);
        Put("scene_view_transform_inv", view.ViewInverse);
        Put("scene_projection_transform", view.Onto);
        Put("scene_projection_transform_inv", view.OntoInverse);
        Put("scene_camera_position", view.Eye);
        Put("scene_camera_right_vector", view.Right);
        Put("scene_camera_up_vector", view.Up);
        Put("scene_camera_forward_vector", view.Forward);
        Put("frame_resolution", new Vector4(view.Size, view.Size, 1f / view.Size, 1f / view.Size));
        Put("frame_to_dynamic_scale", Vector4.One);
        Put("pass_downscale", 1);
        Put("particles_culling_data", new Int4(1, 1, 1, 1));
        Written(shader.Constants, bytes);
    }

    private unsafe void Written(ID3D11Buffer buffer, byte[] bytes)
    {
        MappedSubresource mapped = _context.Map(buffer, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Marshal.Copy(bytes, 0, mapped.DataPointer, bytes.Length);
        _context.Unmap(buffer, 0);
    }

    /// <summary>
    /// The emitter's record as the engine lays it (CommonDeclarations.inc's ParticleEmitterData, MEASURED field by field in v0.1.433-436):
    /// unscaled, its frame's turn and place now and the frame before, its clock, the decided flags - alive, infinite where it runs on, visible,
    /// new on its first step - its group size and lock condition 'always' (7, measured) in culling_tag, its bones from nought.
    /// </summary>
    private static EmitterRecord Record(ParticleEmitterNow emitter, bool fresh)
    {
        ParticleProgram program = emitter.Program;
        (Quaternion turn, Vector3 place) = Pose(emitter.World);
        (Quaternion lastTurn, Vector3 lastPlace) = Pose(emitter.LastWorld);
        uint flags = 0x1u | 0x20u | (program.Infinite ? 0x2u : 0u) | (fresh || emitter.Fresh ? 0x8u : 0u);
        const int CullBits = 5, GroupBits = 6, ConditionBits = 3;
        uint culling = (flags << (CullBits + GroupBits + ConditionBits)) | (7u << (CullBits + GroupBits)) | ((uint)Math.Clamp(program.GroupSize - 1, 0, 63) << CullBits);
        return new EmitterRecord
        {
            Scale = Vector3.One,
            EmitterDuration = program.EmitterDuration,
            LastScale = Vector3.One,
            DeadTime = Half2(0f, 0f),
            InverseScale = Vector3.One,
            LockFlags = program.LockFlags,
            LastInverseScale = Vector3.One,
            SpeedFactor = Half2(1f, 1f),
            Rotation = new Vector4(turn.X, turn.Y, turn.Z, turn.W),
            LastRotation = new Vector4(lastTurn.X, lastTurn.Y, lastTurn.Z, lastTurn.W),
            Translation = place,
            Time = emitter.Time,
            LastTranslation = lastPlace,
            PrevTime = emitter.LastTime,
            CullingAggression = 0,
            Bones = (uint)Math.Min(emitter.Bones.Length, 255),
            CullingTag = culling,
            ParticlesStart = 0,
        };
    }

    private static (Quaternion Turn, Vector3 Place) Pose(Matrix4x4 world)
    {
        if (Matrix4x4.Decompose(world, out _, out Quaternion turn, out Vector3 place))
        {
            return (Quaternion.Normalize(turn), place);
        }

        return (Quaternion.Identity, world.Translation);
    }

    private static uint Half2(float x, float y)
        => BitConverter.HalfToUInt16Bits((Half)x) | ((uint)BitConverter.HalfToUInt16Bits((Half)y) << 16);

    /// <summary>The engine's sampler by its name (SamplerDeclarations.inc's comments say each one's filter and addressing), linear and wrapping where the name says nothing.</summary>
    private ID3D11SamplerState Sampler(string name)
    {
        _engineSamplers ??= new ID3D11SamplerState?[6];
        (int kind, Filter filter, TextureAddressMode u, TextureAddressMode v, ComparisonFunction compare) = name switch
        {
            _ when name.Contains("Depth", StringComparison.Ordinal) => (5, Filter.ComparisonMinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp, ComparisonFunction.LessEqual),
            _ when name.Contains("PointClamp", StringComparison.Ordinal) => (1, Filter.MinMagMipPoint, TextureAddressMode.Clamp, TextureAddressMode.Clamp, ComparisonFunction.Never),
            _ when name.Contains("PointWrap", StringComparison.Ordinal) => (2, Filter.MinMagMipPoint, TextureAddressMode.Wrap, TextureAddressMode.Wrap, ComparisonFunction.Never),
            _ when name.Contains("Clamp", StringComparison.Ordinal) => (3, Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp, ComparisonFunction.Never),
            _ when name.Contains("Border", StringComparison.Ordinal) => (4, Filter.MinMagMipLinear, TextureAddressMode.Border, TextureAddressMode.Border, ComparisonFunction.Never),
            _ => (0, Filter.MinMagMipLinear, TextureAddressMode.Wrap, TextureAddressMode.Wrap, ComparisonFunction.Never),
        };

        return _engineSamplers[kind] ??= _device.CreateSamplerState(new SamplerDescription(filter, u, v, u, 0f, 1, compare, 0f, float.MaxValue));
    }

    /// <summary>A resource the tool hands no value of its own: a white picture, a black cube, an empty list of the stride the shader reads.</summary>
    private ID3D11ShaderResourceView Neutral(Binding bind)
    {
        Neutrals();
        if (bind.Kind is ShaderInputType.Structured or ShaderInputType.ByteAddress)
        {
            int stride = bind.Kind == ShaderInputType.ByteAddress ? 4 : Math.Max(4, bind.Stride);
            if (!_emptyStructured.TryGetValue(stride, out (ID3D11Buffer Buffer, ID3D11ShaderResourceView View) held))
            {
                bool raw = bind.Kind == ShaderInputType.ByteAddress;
                ID3D11Buffer buffer = _device.CreateBuffer(new BufferDescription(
                    stride * 16, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None,
                    raw ? ResourceOptionFlags.BufferAllowRawViews : ResourceOptionFlags.BufferStructured, raw ? 0 : stride));
                ID3D11ShaderResourceView view = _device.CreateShaderResourceView(buffer, raw
                    ? new ShaderResourceViewDescription(buffer, Format.R32_Typeless, 0, 16, BufferExtendedShaderResourceViewFlags.Raw)
                    : new ShaderResourceViewDescription(buffer, Format.Unknown, 0, 16, BufferExtendedShaderResourceViewFlags.None));
                held = (buffer, view);
                _emptyStructured[stride] = held;
            }

            return held.View;
        }

        return bind.Dimension switch
        {
            ShaderResourceViewDimension.TextureCube => _blackCube!,
            ShaderResourceViewDimension.Buffer => bind.Unsigned ? _zeroTyped! : _zeroFloats!,
            _ when bind.Unsigned => _zeroUints!,
            _ => _white!,
        };
    }

    /// <summary>The neutral resources and the particles' states, made once.</summary>
    private void Neutrals()
    {
        if (_white is not null)
        {
            return;
        }

        _whiteTexture = _device.CreateTexture2D(new Texture2DDescription(Format.R32G32B32A32_Float, 1, 1, 1, 1, BindFlags.ShaderResource, ResourceUsage.Immutable),
            [new SubresourceData(Pinned([1f, 1f, 1f, 1f]), 16)]);
        _white = _device.CreateShaderResourceView(_whiteTexture);
        var black = new SubresourceData(Pinned([0f, 0f, 0f, 0f]), 16);
        // Named: Vortice's order is the array's size, then the levels - six levels of a 1x1 picture, a cube of one face, the card refuses.
        _cubeTexture = _device.CreateTexture2D(new Texture2DDescription(Format.R32G32B32A32_Float, 1, 1, arraySize: 6, mipLevels: 1, BindFlags.ShaderResource, ResourceUsage.Immutable,
            CpuAccessFlags.None, 1, 0, ResourceOptionFlags.TextureCube), [black, black, black, black, black, black]);
        _blackCube = _device.CreateShaderResourceView(_cubeTexture);
        _uintTexture = _device.CreateTexture2D(new Texture2DDescription(Format.R32G32B32A32_UInt, 1, 1, 1, 1, BindFlags.ShaderResource, ResourceUsage.Immutable),
            [new SubresourceData(Pinned([0f, 0f, 0f, 0f]), 16)]);
        _zeroUints = _device.CreateShaderResourceView(_uintTexture);
        _typedBuffer = _device.CreateBuffer(new uint[16], BindFlags.ShaderResource, ResourceUsage.Immutable);
        _zeroTyped = _device.CreateShaderResourceView(_typedBuffer, new ShaderResourceViewDescription
        {
            Format = Format.R32_UInt,
            ViewDimension = ShaderResourceViewDimension.Buffer,
            Buffer = new BufferShaderResourceView { FirstElement = 0, NumElements = 16 },
        });

        // A FLOAT BUFFER'S NEUTRAL IS A FLOAT VIEW - a uint view bound to a Buffer<float> is a return-type mismatch the debug layer reports.
        _floatBuffer = _device.CreateBuffer(new float[16], BindFlags.ShaderResource, ResourceUsage.Immutable);
        _zeroFloats = _device.CreateShaderResourceView(_floatBuffer, new ShaderResourceViewDescription
        {
            Format = Format.R32_Float,
            ViewDimension = ShaderResourceViewDimension.Buffer,
            Buffer = new BufferShaderResourceView { FirstElement = 0, NumElements = 16 },
        });
        _particleMixes = _device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
        _particleAdds = _device.CreateBlendState(new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One));
        _particleRaster = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthClipEnable = true });
        _particleTests = _device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.Greater));
    }

    /// <summary>Four floats pinned for an upload - kept alive for the process, a handful of bytes once.</summary>
    private static nint Pinned(float[] values) => GCHandle.Alloc(values, GCHandleType.Pinned).AddrOfPinnedObject();

    /// <summary>Lets go of every particle emitter's buffers and shaders, and the neutral resources.</summary>
    private void DisposeParticles()
    {
        foreach (ParticleCard card in _particleCards.Values)
        {
            card.Dispose();
        }

        _particleCards.Clear();
        foreach ((ID3D11Buffer buffer, ID3D11ShaderResourceView view) in _emptyStructured.Values)
        {
            view.Dispose();
            buffer.Dispose();
        }

        _emptyStructured.Clear();
        _particleFinish?.Dispose();
        foreach (ID3D11SamplerState? one in _engineSamplers ?? [])
        {
            one?.Dispose();
        }

        _white?.Dispose();
        _blackCube?.Dispose();
        _zeroUints?.Dispose();
        _zeroTyped?.Dispose();
        _whiteTexture?.Dispose();
        _cubeTexture?.Dispose();
        _uintTexture?.Dispose();
        _typedBuffer?.Dispose();
        _zeroFloats?.Dispose();
        _floatBuffer?.Dispose();
        _particleMixes?.Dispose();
        _particleAdds?.Dispose();
        _particleRaster?.Dispose();
        _particleTests?.Dispose();
        _particleStandIn?.Dispose();
    }

    /// <summary>Lets go of the emitters not drawn for <see cref="Kept"/> draws.</summary>
    private void ForgetParticles()
    {
        foreach (KeyValuePair<ParticleProgram, ParticleCard> one in _particleCards.Where(one => _draws - one.Value.Used > Kept).ToArray())
        {
            one.Value.Dispose();
            _particleCards.Remove(one.Key);
        }
    }

    /// <summary>
    /// The tool's finishing of a pass's colour (ParticleLinker.Wrapped's body): exposed, graded and encoded as a mixed layer of the picture
    /// (ModelShaders' Graded3 and Srgb, here over the light's grade at t127), then premultiplied - by its alpha for AlphaBlend, its light times its
    /// alpha added for Additive.
    /// </summary>
    /// <remarks>
    /// ADDITIVE LIGHT IS SCALED BY ITS ALPHA BEFORE IT IS FINISHED (v0.1.451): the engine adds a pass's light times its alpha to its linear
    /// picture and tone-maps the sum, while v0.1.450 encoded the light first and scaled the code - at the flame's alphas of a quarter or so, an
    /// HDR light of 2 came out 0.26 where the light it adds encodes to 0.75 (the capture of 2026-11-02 01:38, the grade off: the flame's pass at
    /// most 0.148 over 8,158 pixels, too dim to see).
    /// </remarks>
    internal static string FinishHlsl(ParticleBlend blend) =>
        "cbuffer PoefParticleFinish : register(b" + FinishBuffer.ToString(CultureInfo.InvariantCulture) + """
        )
        {
            float4 PoefExposure;  // x: the exposure; y: one where graded
            int4 PoefGraded;      // xyz: the grade's texels each way
        };

        Buffer<float> PoefGrade : register(t
        """ + FinishGrade.ToString(CultureInfo.InvariantCulture) + """
        );

        float PoefEncoded(float value)
        {
            return value > 0.0f ? (value >= 1.0f ? 1.0f : pow(value, 1.0f / 2.2f)) : 0.0f;
        }

        float3 PoefTexel(int at)
        {
            return float3(PoefGrade.Load(at * 3), PoefGrade.Load((at * 3) + 1), PoefGrade.Load((at * 3) + 2));
        }

        float3 PoefGradeRead(float u, float v, float w)
        {
            int3 size = PoefGraded.xyz;
            float x = clamp((u * size.x) - 0.5f, 0.0f, size.x - 1.0f);
            float y = clamp((v * size.y) - 0.5f, 0.0f, size.y - 1.0f);
            float z = clamp((w * size.z) - 0.5f, 0.0f, size.z - 1.0f);
            int x0 = (int)x;
            int y0 = (int)y;
            int z0 = (int)z;
            float fx = x - x0;
            float fy = y - y0;
            float fz = z - z0;
            int stepX = x0 < size.x - 1 ? 1 : 0;
            int stepY = y0 < size.y - 1 ? size.x : 0;
            int stepZ = z0 < size.z - 1 ? size.x * size.y : 0;
            int a = (((z0 * size.y) + y0) * size.x) + x0;
            float3 c00 = lerp(PoefTexel(a), PoefTexel(a + stepX), fx);
            float3 c10 = lerp(PoefTexel(a + stepY), PoefTexel(a + stepY + stepX), fx);
            int b = a + stepZ;
            float3 c01 = lerp(PoefTexel(b), PoefTexel(b + stepX), fx);
            float3 c11 = lerp(PoefTexel(b + stepY), PoefTexel(b + stepY + stepX), fx);
            return lerp(lerp(c00, c10, fy), lerp(c01, c11, fy), fz);
        }

        float3 PoefGraded3(float3 colour)
        {
            float most = max(max(colour.r, colour.g), max(colour.b, 1.0f));
            float share = 1.0f / most;
            return PoefGradeRead(PoefEncoded(colour.r * share), PoefEncoded(colour.g * share), PoefEncoded(colour.b * share)) * most;
        }

        float PoefSrgbOf(float value)
        {
            float x = saturate(value);
            return x <= 0.0031308f ? x * 12.92f : (1.055f * pow(x, 1.0f / 2.4f)) - 0.055f;
        }

        float3 PoefFinished(float3 colour)
        {
            float3 exposed = max(colour, 0.0f) * PoefExposure.x;
            float3 graded = PoefExposure.y > 0.5f ? PoefGraded3(exposed) : exposed;
            return float3(PoefSrgbOf(graded.r), PoefSrgbOf(graded.g), PoefSrgbOf(graded.b));
        }
        """;

    /// <summary>The finishing entry's statements for a blend: the colour finished and laid on premultiplied.</summary>
    internal static string FinishBody(ParticleBlend blend) => blend == ParticleBlend.Additive
        ? """
              float alpha = saturate(colour.a);
              float3 added = PoefFinished(colour.rgb * alpha);
              return float4(added, 0.0f);
          """
        : """
              float alpha = saturate(colour.a);
              if (!(alpha > 0.0f))
              {
                  discard;
              }

              return float4(PoefFinished(colour.rgb) * alpha, alpha);
          """;

    /// <summary>The picture's camera as the particles' globals read it - ModelGpu.Draw's matrices, the eye and its ways in the picture's frame.</summary>
    internal readonly record struct ParticleView(
        Matrix4x4 Clip, Matrix4x4 Unclip, Matrix4x4 View, Matrix4x4 ViewInverse, Matrix4x4 Onto, Matrix4x4 OntoInverse,
        Vector3 Eye, Vector3 Right, Vector3 Up, Vector3 Forward, float Size);

    /// <summary>The finishing entry's constants - its cbuffer, field for field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FinishConstants
    {
        public Vector4 Exposure;
        public Int4 Graded;
    }

    /// <summary>CommonDeclarations.inc's ParticleEmitterData, 160 bytes, field for field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EmitterRecord
    {
        public Vector3 Scale;
        public float EmitterDuration;
        public Vector3 LastScale;
        public uint DeadTime;
        public Vector3 InverseScale;
        public uint LockFlags;
        public Vector3 LastInverseScale;
        public uint SpeedFactor;
        public Vector4 Rotation;
        public Vector4 LastRotation;
        public Vector3 Translation;
        public float Time;
        public Vector3 LastTranslation;
        public float PrevTime;
        public uint CullingAggression;
        public uint Bones;
        public uint CullingTag;
        public uint ParticlesStart;
        public ulong CustomA;
        public ulong CustomB;
    }

    /// <summary>One resource a shader reads, off its reflection.</summary>
    private sealed record Binding(string Name, ShaderInputType Kind, int Slot, int Stride, ShaderResourceViewDimension Dimension, bool Unsigned)
    {
        public bool Unordered => Kind is ShaderInputType.UnorderedAccessViewRWStructured or ShaderInputType.UnorderedAccessViewRWByteAddress
            or ShaderInputType.UnorderedAccessViewRWTyped or ShaderInputType.UnorderedAccessViewAppendStructured
            or ShaderInputType.UnorderedAccessViewConsumeStructured or ShaderInputType.UnorderedAccessViewRWStructuredWithCounter;
    }

    /// <summary>One compiled shader with what its reflection said: its resources, its $Globals by name, its threads.</summary>
    private sealed class Shader : IDisposable
    {
        public ID3D11ComputeShader? Compute { get; init; }

        public ID3D11VertexShader? VertexShader { get; init; }

        public ID3D11PixelShader? PixelShader { get; init; }

        public byte[] Code { get; init; } = [];

        public List<Binding> Bindings { get; } = [];

        public Dictionary<string, (int Offset, int Size)> Globals { get; } = new(StringComparer.Ordinal);

        public ID3D11Buffer? Constants { get; set; }

        /// <summary>The register $Globals took - b0 unless the engine's declarations put a cbuffer of their own before it.</summary>
        public int GlobalsSlot { get; set; }

        public byte[] GlobalsHeld { get; set; } = [];

        public int Threads { get; set; } = 64;

        public IReadOnlyList<CurveKey[]> Tracks { get; init; } = [];

        public ID3D11Buffer? Lut { get; set; }

        public ID3D11ShaderResourceView? LutView { get; set; }

        /// <summary>The particle pool's element as this shader's reflection lays it out (the engine's ParticleData) - empty where it reads no pool.</summary>
        public List<PoolMember> PoolLayout { get; } = [];

        /// <summary>Its reflection read: every resource, $Globals' variables, the compute shader's group size; its constants and LUT made.</summary>
        public void Reflect(ID3D11Device device)
        {
            using ID3D11ShaderReflection reflection = Compiler.Reflect<ID3D11ShaderReflection>(Code);
            ShaderDescription description = reflection.Description;
            for (var at = 0; at < description.BoundResources; at++)
            {
                InputBindingDescription bind = reflection.GetResourceBindingDescription(at);
                Bindings.Add(new Binding(bind.Name, bind.Type, bind.BindPoint, bind.NumSamples, bind.Dimension, bind.ReturnType is ResourceReturnType.UInt or ResourceReturnType.SInt));
                if (bind.Type == ShaderInputType.ConstantBuffer && bind.Name == "$Globals")
                {
                    ID3D11ShaderReflectionConstantBuffer globals = reflection.GetConstantBufferByName("$Globals");
                    ConstantBufferDescription buffer = globals.Description;
                    for (var v = 0; v < buffer.VariableCount; v++)
                    {
                        ShaderVariableDescription variable = globals.GetVariableByIndex(v).Description;
                        Globals[variable.Name] = (variable.StartOffset, variable.Size);
                    }

                    GlobalsSlot = bind.BindPoint;
                    GlobalsHeld = new byte[(buffer.Size + 15) & ~15];
                    Constants = device.CreateBuffer(GlobalsHeld.Length, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
                }
            }

            if (Bindings.FirstOrDefault(one => one.Name is "particles_data_write" or "particles_data_read") is { } pool)
            {
                PoolLayout.AddRange(Members(reflection, pool.Name));
            }

            if (Compute is not null)
            {
                reflection.GetThreadGroupSize(out int x, out int y, out int z);
                Threads = Math.Max(1, x * Math.Max(1, y) * Math.Max(1, z));
            }

            // THE SPLINE LUT'S ROWS, this shader's own numbering (LinkedShader.Tracks): each curve baked as ShadeProgram's CurveLut,
            // packed two halves a word as SplineLUTSamplePoint unpacks them (UnpackHalf2: the even x low, the odd high).
            int rows = Math.Max(1, Tracks.Count);
            var words = new uint[rows * (ShadeProgram.CurveWidth / 2)];
            for (var row = 0; row < Tracks.Count; row++)
            {
                Vector4[] lut = ShadeProgram.CurveLut(new Dictionary<string, CurveKey[]>(StringComparer.Ordinal) { ["A"] = Tracks[row] }, "A");
                for (var x = 0; x < ShadeProgram.CurveWidth; x += 2)
                {
                    words[(row * (ShadeProgram.CurveWidth / 2)) + (x / 2)] = Half2(lut[x].X, lut[x + 1].X);
                }
            }

            // One element a row (the engine's structBuffer<LUTData>, read as spline_lut[track].value[x / 2]). Named: Vortice's array overload takes
            // the size before the stride, and a stride passed sixth is a structured buffer of stride nought, which the card refuses (E_INVALIDARG).
            int stride = ShadeProgram.CurveWidth * 2;
            Lut = device.CreateBuffer(words, BindFlags.ShaderResource, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, sizeInBytes: 0, structureByteStride: stride);
            LutView = device.CreateShaderResourceView(Lut, new ShaderResourceViewDescription(Lut, Format.Unknown, 0, rows, BufferExtendedShaderResourceViewFlags.None));
        }

        /// <summary>
        /// A structured buffer's element, member by member, from the reflection's own description of it (its "$Element") - or nothing where the
        /// reflection describes none.
        /// </summary>
        private static List<PoolMember> Members(ID3D11ShaderReflection reflection, string name)
        {
            var members = new List<PoolMember>();
            try
            {
                ID3D11ShaderReflectionType element = reflection.GetConstantBufferByName(name).GetVariableByIndex(0).VariableType;
                for (var m = 0; m < element.Description.MemberCount; m++)
                {
                    ShaderTypeDescription one = element.GetMemberTypeByIndex(m).Description;
                    members.Add(new PoolMember(element.GetMemberTypeName(m), one.Offset, one.Type, Math.Max(1, one.RowCount) * Math.Max(1, one.ColumnCount)));
                }
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
            {
                // A REFLECTION THAT DESCRIBES NO ELEMENT LEAVES THE POOL READ AS WORDS.
                members.Clear();
            }

            return members;
        }

        public void Dispose()
        {
            Compute?.Dispose();
            VertexShader?.Dispose();
            PixelShader?.Dispose();
            Constants?.Dispose();
            LutView?.Dispose();
            Lut?.Dispose();
        }
    }

    /// <summary>One render pass on the card: its two shaders and the input layout its mesh is read through.</summary>
    private sealed class PassCard(Shader vertex, Shader pixel, ID3D11InputLayout layout, string[] streams) : IDisposable
    {
        public Shader Vertex { get; } = vertex;

        public Shader Pixel { get; } = pixel;

        public ID3D11InputLayout Layout { get; } = layout;

        /// <summary>Which of the mesh's streams each input slot reads - POSITION, TEXCOORD, NORMAL, TANGENT.</summary>
        public string[] Streams { get; } = streams;

        public void Dispose()
        {
            Vertex.Dispose();
            Pixel.Dispose();
            Layout.Dispose();
        }
    }

    /// <summary>One emitter's shaders and buffers on the card - compiled behind the frame, made on the render thread once they land.</summary>
    private sealed partial class ParticleCard : IDisposable
    {
        private readonly ParticleProgram _program;
        private readonly Task<Compiled> _compiling;
        private ID3D11Buffer? _pool, _index, _meta, _tiles, _positions, _coordinates, _normals, _tangents;
        private ID3D11UnorderedAccessView? _poolWrite, _indexWrite, _metaWrite, _tilesWrite;
        private ID3D11ShaderResourceView? _poolRead, _indexRead, _emitterRead, _bonesRead;
        private bool _made;
        private string _why = string.Empty;

        public ParticleCard(ParticleProgram program, string? cache)
        {
            _program = program;
            _cache = cache;
            _compiling = Task.Run(() => Compile(program, cache));
        }

        /// <summary>Where its compiled shaders are kept (shader-cache), or null.</summary>
        private readonly string? _cache;

        public long Used { get; set; }

        /// <summary>Whether its first step is still to come - IS_NEW sent, its pool started over.</summary>
        public bool Fresh { get; set; } = true;

        public Shader? Update { get; private set; }

        public List<PassCard> Passes { get; } = [];

        public ID3D11Buffer? Emitter { get; private set; }

        public ID3D11Buffer? Bones { get; private set; }

        public Vector4[] BonesHeld { get; private set; } = [];

        public ID3D11Buffer? MeshIndices { get; private set; }

        /// <summary>Whether its shaders have landed, made or failed.</summary>
        public bool Landing() => _compiling.IsCompleted;

        /// <summary>Its shaders and buffers made, on the render thread, once - or false and why not.</summary>
        public bool Made(ID3D11Device device, out string why)
        {
            if (_made || _why.Length > 0)
            {
                why = _why;
                return _made;
            }

            Compiled code = _compiling.Result;
            if (code.Why.Length > 0)
            {
                why = _why = code.Why;
                return false;
            }

            // The step under way, named in the refusal: an HRESULT alone (E_INVALIDARG) says nothing of which of a dozen calls it was.
            string step = "the update shader";
            try
            {
                Update = new Shader { Compute = device.CreateComputeShader(code.Update!), Code = code.Update!, Tracks = _program.Update.Tracks };
                step = "the update's constants or spline LUT";
                Update.Reflect(device);
                for (var p = 0; p < _program.Passes.Count; p++)
                {
                    step = $"pass {p}'s vertex shader, its constants or spline LUT";
                    var vertex = new Shader { VertexShader = device.CreateVertexShader(code.Vertex[p]!), Code = code.Vertex[p]!, Tracks = _program.Passes[p].Vertex.Tracks };
                    vertex.Reflect(device);
                    step = $"pass {p}'s pixel shader, its constants or spline LUT";
                    var pixel = new Shader { PixelShader = device.CreatePixelShader(code.Pixel[p]!), Code = code.Pixel[p]!, Tracks = _program.Passes[p].Pixel.Tracks };
                    pixel.Reflect(device);
                    step = $"pass {p}'s input layout";
                    (InputElementDescription[] elements, string[] streams) = Layout(vertex.Code);
                    Passes.Add(new PassCard(vertex, pixel, device.CreateInputLayout(elements, vertex.Code), streams));
                }

                int stride = Update.Bindings.FirstOrDefault(one => one.Name == "particles_data_write") is { Stride: > 0 } pool ? pool.Stride : 112;
                int count = Math.Max(1, _program.Count);
                step = $"the particle pool ({count} of {stride} bytes)";
                (_pool, _poolWrite, _poolRead) = Structured(device, stride, count, zeroed: true);
                step = "the particle index, culling meta or tiles";
                (_index, _indexWrite, _indexRead) = Raw(device, count);
                (_meta, _metaWrite, _) = Raw(device, 1024);
                (_tiles, _tilesWrite, _) = Raw(device, 1024);
                step = "the emitter record or its bones";
                Emitter = device.CreateBuffer(new BufferDescription(Marshal.SizeOf<EmitterRecord>(), BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, Marshal.SizeOf<EmitterRecord>()));
                _emitterRead = device.CreateShaderResourceView(Emitter, new ShaderResourceViewDescription(Emitter, Format.Unknown, 0, 1, BufferExtendedShaderResourceViewFlags.None));
                BonesHeld = new Vector4[2 * 64];
                Bones = device.CreateBuffer(new BufferDescription(BonesHeld.Length * 16, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 32));
                _bonesRead = device.CreateShaderResourceView(Bones, new ShaderResourceViewDescription(Bones, Format.Unknown, 0, BonesHeld.Length / 2, BufferExtendedShaderResourceViewFlags.None));
                step = "the mesh's buffers";
                ParticleMeshData mesh = _program.Mesh;
                _positions = device.CreateBuffer(mesh.Positions, BindFlags.VertexBuffer, ResourceUsage.Immutable);
                _coordinates = device.CreateBuffer(mesh.Coordinates, BindFlags.VertexBuffer, ResourceUsage.Immutable);
                _normals = device.CreateBuffer(mesh.Normals, BindFlags.VertexBuffer, ResourceUsage.Immutable);
                _tangents = device.CreateBuffer(mesh.Tangents, BindFlags.VertexBuffer, ResourceUsage.Immutable);
                MeshIndices = device.CreateBuffer(mesh.Indices, BindFlags.IndexBuffer, ResourceUsage.Immutable);
                _made = true;
                why = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
            {
                why = _why = $"the card refused {step}: {exception.Message}";
                return false;
            }
        }

        /// <summary>The update's writable buffer of a name, or null.</summary>
        public ID3D11UnorderedAccessView? Unordered(string name) => name switch
        {
            "particles_data_write" => _poolWrite,
            "particles_index_data_write" => _indexWrite,
            "particles_culling_meta_write" => _metaWrite,
            "particles_culling_tiles_write" => _tilesWrite,
            _ => null,
        };

        /// <summary>A readable buffer of a name - the emitter's own - or null.</summary>
        public ID3D11ShaderResourceView? Read(string name, Shader shader) => name switch
        {
            "particles_data_read" or "particles_data" => _poolRead,
            "particles_index_data_read" or "particles_index_data" => _indexRead,
            "particles_emitter_data" => _emitterRead,
            "emitter_bones_data" => _bonesRead,
            "spline_lut" => shader.LutView,
            _ => null,
        };

        /// <summary>The mesh's streams in the slots a pass's layout reads them from.</summary>
        public void MeshStreams(ID3D11DeviceContext context, PassCard pass)
        {
            for (var slot = 0; slot < pass.Streams.Length; slot++)
            {
                (ID3D11Buffer? buffer, int stride) = pass.Streams[slot] switch
                {
                    "POSITION" => (_positions, 12),
                    "TEXCOORD" => (_coordinates, 8),
                    "NORMAL" => (_normals, 16),
                    _ => (_tangents, 16),
                };
                context.IASetVertexBuffer(slot, buffer!, stride, 0);
            }
        }

        /// <summary>
        /// The input layout a vertex shader reads the mesh through: each input of its signature one stream of the mesh - POSITION three floats,
        /// TEXCOORD two, NORMAL and TANGENT four (the engine's packed normal read as floats, CHOSEN).
        /// </summary>
        private static (InputElementDescription[] Elements, string[] Streams) Layout(byte[] code)
        {
            using ID3D11ShaderReflection reflection = Compiler.Reflect<ID3D11ShaderReflection>(code);
            var elements = new List<InputElementDescription>();
            var streams = new List<string>();
            for (var at = 0; at < reflection.Description.InputParameters; at++)
            {
                ShaderParameterDescription input = reflection.GetInputParameterDescription(at);
                if (input.SemanticName.StartsWith("SV_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string stream = input.SemanticName.ToUpperInvariant() switch
                {
                    "POSITION" => "POSITION",
                    "TEXCOORD" => "TEXCOORD",
                    "NORMAL" => "NORMAL",
                    _ => "TANGENT",
                };
                Format format = stream switch
                {
                    "POSITION" => Format.R32G32B32_Float,
                    "TEXCOORD" => Format.R32G32_Float,
                    _ => Format.R32G32B32A32_Float,
                };
                elements.Add(new InputElementDescription(input.SemanticName, input.SemanticIndex, format, 0, streams.Count, InputClassification.PerVertexData, 0));
                streams.Add(stream);
            }

            return ([.. elements], [.. streams]);
        }

        private static (ID3D11Buffer, ID3D11UnorderedAccessView, ID3D11ShaderResourceView) Structured(ID3D11Device device, int stride, int count, bool zeroed)
        {
            ID3D11Buffer buffer = zeroed
                ? device.CreateBuffer(new byte[stride * count], BindFlags.ShaderResource | BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, sizeInBytes: 0, structureByteStride: stride)
                : device.CreateBuffer(new BufferDescription(stride * count, BindFlags.ShaderResource | BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, stride));
            return (buffer,
                device.CreateUnorderedAccessView(buffer, new UnorderedAccessViewDescription(buffer, Format.Unknown, 0, count, BufferUnorderedAccessViewFlags.None)),
                device.CreateShaderResourceView(buffer, new ShaderResourceViewDescription(buffer, Format.Unknown, 0, count, BufferExtendedShaderResourceViewFlags.None)));
        }

        private static (ID3D11Buffer, ID3D11UnorderedAccessView, ID3D11ShaderResourceView) Raw(ID3D11Device device, int words)
        {
            ID3D11Buffer buffer = device.CreateBuffer(new uint[words], BindFlags.ShaderResource | BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferAllowRawViews, sizeInBytes: 0, structureByteStride: 0);
            return (buffer,
                device.CreateUnorderedAccessView(buffer, new UnorderedAccessViewDescription(buffer, Format.R32_Typeless, 0, words, BufferUnorderedAccessViewFlags.Raw)),
                device.CreateShaderResourceView(buffer, new ShaderResourceViewDescription(buffer, Format.R32_Typeless, 0, words, BufferExtendedShaderResourceViewFlags.Raw)));
        }

        /// <summary>Every shader of the program compiled by FXC, kept on disk by what was compiled; or why one would not compile.</summary>
        private static Compiled Compile(ParticleProgram program, string? cache)
        {
            byte[]? update = ParticleBytes(program.Update.Hlsl, program.Update.Entry, "cs_5_0", cache, out string why);
            if (update is null)
            {
                return new Compiled(null, [], [], $"its update would not compile: {why}");
            }

            var vertex = new byte[]?[program.Passes.Count];
            var pixel = new byte[]?[program.Passes.Count];
            for (var p = 0; p < program.Passes.Count; p++)
            {
                ParticlePassProgram pass = program.Passes[p];
                vertex[p] = ParticleBytes(pass.Vertex.Hlsl, pass.Vertex.Entry, "vs_5_0", cache, out why);
                if (vertex[p] is null)
                {
                    return new Compiled(null, [], [], $"pass {p}'s vertex shader would not compile: {why}");
                }

                string? finished = ParticleLinker.Wrapped(pass.Pixel.Hlsl, Finished, FinishHlsl(pass.Blend), FinishBody(pass.Blend));
                pixel[p] = finished is null ? null : ParticleBytes(finished, Finished, "ps_5_0", cache, out why);
                if (pixel[p] is null)
                {
                    return new Compiled(null, [], [], $"pass {p}'s pixel shader would not compile: {(finished is null ? "it holds no PixelMain" : why)}");
                }
            }

            return new Compiled(update, vertex, pixel, string.Empty);
        }

        /// <summary>One shader's bytecode: kept on disk by its source, entry and profile hashed (particle-*.cso), else compiled and kept.</summary>
        private static byte[]? ParticleBytes(string source, string entry, string profile, string? cache, out string why)
        {
            string? file = cache is null
                ? null
                : Path.Combine(cache, "particle-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}\n{entry}\n{profile}"))) + ".cso");
            try
            {
                if (file is not null && File.Exists(file))
                {
                    why = string.Empty;
                    return File.ReadAllBytes(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A FILE THAT WILL NOT READ IS COMPILED AGAIN.
            }

            Result result = Compiler.Compile(source, null!, null!, entry, "particle.hlsl", profile, ShaderFlags.IeeeStrictness | ShaderFlags.OptimizationLevel3, out Blob blob, out Blob errors);
            try
            {
                string said = errors is null ? string.Empty : Marshal.PtrToStringAnsi(errors.BufferPointer) ?? string.Empty;
                if (result.Failure || blob is null)
                {
                    why = said.Length > 0 ? said.Trim() : result.ToString();
                    return null;
                }

                byte[] bytes = new byte[blob.BufferSize];
                Marshal.Copy(blob.BufferPointer, bytes, 0, bytes.Length);
                if (file is not null)
                {
                    try
                    {
                        Directory.CreateDirectory(cache!);
                        string aside = file + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".part";
                        File.WriteAllBytes(aside, bytes);
                        File.Move(aside, file, overwrite: true);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // NOT KEPT, AND STILL DRAWN.
                    }
                }

                why = string.Empty;
                return bytes;
            }
            finally
            {
                blob?.Dispose();
                errors?.Dispose();
            }
        }

        public void Dispose()
        {
            Update?.Dispose();
            foreach (PassCard pass in Passes)
            {
                pass.Dispose();
            }

            foreach (IDisposable? one in new IDisposable?[] { _poolWrite, _indexWrite, _metaWrite, _tilesWrite, _poolRead, _indexRead, _emitterRead, _bonesRead,
                         _pool, _index, _meta, _tiles, Emitter, Bones, _positions, _coordinates, _normals, _tangents, MeshIndices })
            {
                one?.Dispose();
            }
        }

        /// <summary>What compiling came to: every shader's bytecode, or why one would not compile.</summary>
        private sealed record Compiled(byte[]? Update, byte[]?[] Vertex, byte[]?[] Pixel, string Why);
    }
}
