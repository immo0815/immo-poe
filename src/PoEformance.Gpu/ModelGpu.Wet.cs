using System.Numerics;
using System.Runtime.InteropServices;
using PoEformance.Game.Files;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace PoEformance.Gpu;

/// <summary>
/// The engine's wetness pass on the card: the water and the beach waves cast their wetness onto the screen, and what reads it is darker where wet.
/// </summary>
/// <remarks>
/// WHAT THE ENGINE DOES (common.ffx's <c>wetness_tex</c>, its debug views "Wetness (Caster)" and "Wetness (Receiver)"): a material whose
/// blend mode is <c>Wetness</c> writes its alpha into the target - the water's by how deep it covers what lies behind (WaterComputeWetness),
/// the beach waves' by BreakingOceanWavesWetness.fxgraph - and a receiver's graph reads it back at its pixel (ReadPixelWetness;
/// ShadeProgram.ReadsWetness). Port's quay is one: the crest that climbs its face leaves it dark, which its own CrashingFoamSurface
/// cannot do (ShadeProgram.ReadsWetness's remarks).
///
/// CAST AFTER THE PICTURE, READ THE NEXT FRAME, so the pass costs no second walk of the solid part: the casters need the solid depth
/// the receivers are drawn into, and a pass that cast first would have to lay that depth down twice. The receivers read what the last
/// frame cast, where the picture has not moved since; a moved picture reads dry and starts again.
///
/// DRYING OVER <see cref="WetFade"/> SECONDS, CHOSEN: the engine keeps its target from frame to frame or not, which no shader says, and
/// the live client's video of the quay (v0.1.331) shows its face wet for about a second after a crest fell back. So last frame's
/// wetness, less the clock's step over that, is where this frame's casters start - the larger of the two kept (the blend's MAX).
/// </remarks>
public sealed partial class ModelGpu
{
    /// <summary>How long the wetness takes to dry, in seconds of the clock - CHOSEN, see the class's remarks.</summary>
    public const float WetFade = 1f;

    private ID3D11PixelShader? _wetWater;
    private ID3D11PixelShader? _wetDried;
    private ID3D11BlendState? _wetMost;
    private ID3D11DepthStencilState? _wetTests;
    private ID3D11Buffer? _wetDry;
    private bool _wetTried;

    /// <summary>Why the wetness pass could not be made, or empty.</summary>
    private string _wetWhy = string.Empty;

    /// <summary>What the last frame cast into: its target, its plan, its camera and its clock - the receivers read it only where all four stand.</summary>
    private (ModelTarget Target, Plan Plan, Matrix4x4 Clip, float Clock)? _wetLast;

    /// <summary>Why the card's wetness pass is not drawn, or empty - for the line under the picture.</summary>
    public string WetWhy => _wetWhy;

    /// <summary>Whether a run casts wetness: the water's, and the beach's where its program has a wetness to cast.</summary>
    private static bool Casts(Run run, Plan plan) => Sea(run, plan) || (Beach(run, plan) && plan.Programs[run.Shade].Wetness is not null);

    /// <summary>Whether the plan held both something reading the wetness and something casting it - asked once a plan, a room having thousands of runs.</summary>
    private (Plan Plan, bool Both)? _wetPlan;

    /// <summary>Whether a picture draws the wetness pass: the game light's switch on, something reading it, and something casting it.</summary>
    private bool Wets(Plan plan, SceneLight? light)
    {
        if (light is not { WaterWetness: true })
        {
            return false;
        }

        if (_wetPlan is not { } held || !ReferenceEquals(held.Plan, plan))
        {
            held = (plan, Array.Exists(plan.Programs, one => one.ReadsWetness) && Array.Exists(plan.Runs, one => Casts(one, plan)));
            _wetPlan = held;
        }

        return held.Both;
    }

    /// <summary>Whether the last frame cast into this very target, plan and camera, at a clock no more than a drying behind.</summary>
    private bool Still(ModelTarget target, Plan plan, in Matrix4x4 clip, float clock)
        => _wetLast is { } last && ReferenceEquals(last.Target, target) && ReferenceEquals(last.Plan, plan) && last.Clip == clip
            && clock >= last.Clock && clock - last.Clock <= WetFade;

    /// <summary>The wetness the receivers read this frame: the last frame's where the picture stands still, else none - they read dry.</summary>
    private ID3D11ShaderResourceView? WetRead(ModelTarget target, Plan plan, in Matrix4x4 clip, float clock)
        => Still(target, plan, clip, clock) ? target.WetHeld : null;

    /// <summary>
    /// The casters' wetness into this frame's target - last frame's less what dried since, then the water's and the beach's - once the
    /// picture is drawn, its solid depth (with the beach's) in the depth buffer.
    /// </summary>
    private void Wet(ID3D11DeviceContext context, ModelTarget target, Plan plan, in Matrix4x4 clip, float clock, bool lifting)
    {
        if (!WetMade(out string why))
        {
            _wetWhy = why;
            return;
        }

        _wetWhy = string.Empty;
        bool still = Still(target, plan, clip, clock);
        float dried = still ? (clock - _wetLast!.Value.Clock) / WetFade : 0f;
        target.Wetted(context);

        // LAST FRAME'S, LESS WHAT DRIED - or dry where the picture starts again.
        Written(_wetDry!, new Vector4(dried, still ? 0f : 1f, 0f, 0f));
        context.OMSetRenderTargets(target.WetWriting, null);
        context.OMSetBlendState(_covers);
        context.OMSetDepthStencilState(_ignores);
        context.IASetInputLayout(null);
        context.VSSetShader(_whole);
        context.PSSetShader(_wetDried);
        context.PSSetConstantBuffer(4, _wetDry);
        context.PSSetShaderResource(0, target.WetHeld!);
        context.Draw(3, 0);
        context.PSSetShaderResource(0, null!);

        // THE CASTERS OVER IT, the larger kept, against the depth the picture left - the water measuring itself to the solid behind
        // through its copy, the beach tested where it shows as the engine's CustomDepthTest discards it where it does not.
        target.Kept(context);
        context.PSSetShaderResource(ModelShaders.BehindSlot, target.Behind);
        context.OMSetRenderTargets(target.WetWriting, target.Depth);
        context.OMSetBlendState(_wetMost);
        context.IASetInputLayout(_layout);
        for (var at = 0; at < plan.Runs.Length; at++)
        {
            Run run = plan.Runs[at];
            if (Sea(run, plan))
            {
                context.VSSetShader(lifting ? _placedWaves : _placed);
                context.PSSetShader(_wetWater);
                context.OMSetDepthStencilState(_ignores);
                Part(new Vector4(0f, -1f, 0f, 0f), run.Sway);
                context.DrawIndexed(run.Count * 3, run.First * 3, 0);
            }
            else if (Beach(run, plan) && plan.Programs[run.Shade].Wetness is { } wetness
                && _programs.Of(wetness, ProgramPass.Wet, out _, out _) is { } cast)
            {
                // A CASTER STILL COMPILING CASTS NOTHING YET; the pane redraws as it lands.
                context.VSSetShader(_placedBeach);
                context.PSSetShader(cast);
                context.OMSetDepthStencilState(_wetTests);
                Drawn(run, plan);
            }
        }

        target.WetSwapped();
        _wetLast = (target, plan, clip, clock);
    }

    /// <summary>The pass's shaders and states, made the first time a picture draws it - or false and why not.</summary>
    private bool WetMade(out string why)
    {
        if (_wetTried)
        {
            why = _wetWhy;
            return _wetWater is not null;
        }

        _wetTried = true;
        using Blob? water = Compiled.Code(ModelShaders.Wetting, "WaterWet", "ps_4_0", out why);
        if (water is null)
        {
            _wetWhy = why;
            return false;
        }

        using Blob? dried = Compiled.Code(ModelShaders.Drying, "Dried", "ps_4_0", out why);
        if (dried is null)
        {
            _wetWhy = why;
            return false;
        }

        _wetWater = _device.CreatePixelShader(water);
        _wetDried = _device.CreatePixelShader(dried);
        var most = new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One);
        most.RenderTarget[0].BlendOperation = BlendOperation.Max;
        most.RenderTarget[0].BlendOperationAlpha = BlendOperation.Max;
        _wetMost = _device.CreateBlendState(most);

        // TESTED, NOT WRITTEN: nearer or level with the depth the picture left, which for the beach is its own nearest crossing.
        _wetTests = _device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.GreaterEqual));
        _wetDry = _device.CreateBuffer(Marshal.SizeOf<Vector4>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        why = string.Empty;
        return true;
    }

    private void DisposeWet()
    {
        _wetWater?.Dispose();
        _wetDried?.Dispose();
        _wetMost?.Dispose();
        _wetTests?.Dispose();
        _wetDry?.Dispose();
    }
}
