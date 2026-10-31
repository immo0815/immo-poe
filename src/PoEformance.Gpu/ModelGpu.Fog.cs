using System.Numerics;
using System.Runtime.InteropServices;
using PoEformance.Game.Files;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace PoEformance.Gpu;

/// <summary>
/// The air on the card: the clouds' shadow on every surface the sun lights (SceneLight.Clouds, ModelShaders' Clouded) and the screen-space
/// fog - the weather attachment's pass (SceneLight.Fog, a screen program) drawn over the whole picture once the rest is.
/// </summary>
/// <remarks>
/// THE CARD'S ALONE (DECIDED from the live client): the processor's picture leaves it out, and the line under it says so. Drawn after the
/// translucent shapes and before the wetness casts - the engine's own order for its DownScaledAlphaBlend passes is in no file read, so CHOSEN -
/// over the solid depth the picture left, copied for the program to read (ModelTarget.Behind): a card below feature level 10.1 cannot copy its
/// depth, and draws the picture without the fog.
/// </remarks>
public sealed partial class ModelGpu
{
    private ID3D11Buffer? _air;

    /// <summary>Why the last picture with a fog to lay was drawn without it - its shader would not compile, or this card cannot copy its depth - or empty.</summary>
    public string FogWhy { get; private set; } = string.Empty;

    /// <summary>The fog a light lays over its picture, where this card can draw it - its program with its textures in hand - else null.</summary>
    private ShadeProgram? Fogs(SceneLight? light)
        => light?.Fog is { Ready: true } fog && FeatureLevel >= FeatureLevel.Level_10_1 ? fog.Program : null;

    /// <summary>
    /// Whether the fog's shader is in, or why not - pending while it compiles; a fog that will never compile draws nothing and says nothing more
    /// here than the picture does without it.
    /// </summary>
    private bool FogReady(SceneLight? light, out string why)
    {
        why = string.Empty;
        if (Fogs(light) is not { } program)
        {
            FogWhy = light?.Fog is { Ready: true } && FeatureLevel < FeatureLevel.Level_10_1
                ? "this card cannot copy its depth for the fog to read (feature level 10.0)"
                : string.Empty;
            return true;
        }

        foreach (Mipmaps? sheet in program.Sheets)
        {
            if (sheet is not null && (sheet.Width > _largest || sheet.Height > _largest || Waiting(sheet)))
            {
                why = "the screen-space fog's texture is still being unpacked or is too large for this card";
                return false;
            }
        }

        if (_programs.Of(program, ProgramPass.Screen, out bool pending, out string failed) is null)
        {
            if (pending)
            {
                why = "the screen-space fog's shader is still compiling for the card";
                return false;
            }

            // A FOG THAT WILL NEVER COMPILE leaves the picture as it is, and says so under it.
            FogWhy = failed;
            return true;
        }

        FogWhy = string.Empty;
        return true;
    }

    /// <summary>
    /// The air's constants for the whole picture (ModelShaders' cbuffer Air, b5), written and bound before anything is drawn: the clouds every
    /// surface the sun lights reads, and the fog's pass's values.
    /// </summary>
    /// <param name="context">The immediate context.</param>
    /// <param name="light">The picture's light.</param>
    /// <param name="player">Where the player stands in the picture's frame - the camera's focus on a stage (CHOSEN, DECIDED from the live client).</param>
    /// <param name="clouded">Whether the clouds are drawn - <see cref="Clouds"/>.</param>
    private void Aired(ID3D11DeviceContext context, SceneLight light, Vector3 player, bool clouded)
    {
        _air ??= _device.CreateBuffer(Marshal.SizeOf<AirConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        Vector2 origin = light.AreaOrigin;
        ScreenFog? fog = light.Fog;
        AreaClouds? clouds = clouded ? light.Clouds : null;
        (Vector2 along, float speed) = clouds?.Drift ?? (Vector2.UnitX, 0f);
        Written(_air, new AirConstants
        {
            Fog = fog?.Colour ?? Vector4.Zero,
            Shape = new Vector4(fog?.Shape ?? Vector2.Zero, 0f, 0f),
            Player = new Vector4(player.X + origin.X, player.Y + origin.Y, player.Z, 0f),
            Light = new Vector4(fog?.Environment ?? Vector3.Zero, 0f),
            Origin = new Vector4(origin, 0f, 0f),
            Clouds = clouds is null ? Vector4.Zero : new Vector4(clouds.Scale, clouds.Intensity, clouds.Midpoint, clouds.Sharpness),
            Drift = clouds is null ? Vector4.Zero : new Vector4(along, speed, clouds.FadeRadius),
            Fades = clouds is null ? Vector4.Zero : new Vector4(clouds.PreFade, clouds.PostFade, 1f, 0f),
        });

        context.PSSetConstantBuffer(5, _air);
    }

    /// <summary>
    /// Whether a light's clouds are drawn: it carries them and their noise is in (ShadeProgram.Distortion, which UseCaustics hands over as the
    /// install opens - AreaClouds.NoisePath).
    /// </summary>
    private static bool Clouds(SceneLight? light) => light?.Clouds is not null && ShadeProgram.Distortion is not null;

    /// <summary>
    /// The fog over the picture in the target: the solid depth copied for it, its program run on every pixel by one triangle over the whole
    /// target, laid over the picture as a mixed layer is - over the air's constants <see cref="Aired"/> bound.
    /// </summary>
    /// <param name="context">The immediate context.</param>
    /// <param name="target">The picture's target, its picture drawn.</param>
    /// <param name="program">The fog's program - <see cref="Fogs"/>.</param>
    private void Fogged(ID3D11DeviceContext context, ModelTarget target, ShadeProgram program)
    {
        if (_programs.Of(program, ProgramPass.Screen, out _, out _) is not { } shader)
        {
            return;
        }

        target.Kept(context);
        context.OMSetRenderTargets(target.AccumulatedTarget, null);
        context.RSSetViewport(new Viewport(0f, 0f, target.Size, target.Size, 0f, 1f));
        context.OMSetBlendState(_mixes);
        context.OMSetDepthStencilState(_ignores);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_whole);
        context.PSSetShader(shader);
        context.PSSetShaderResource(ModelShaders.BehindSlot, target.Behind);
        IReadOnlyList<Mipmaps?> sheets = program.Sheets;
        for (var sheet = 0; sheet < sheets.Count; sheet++)
        {
            context.PSSetShaderResource(ModelShaders.FirstSheet + sheet, sheets[sheet] is { } one ? Skin(one).View : null!);
        }

        _sheetsBound = Math.Max(_sheetsBound, sheets.Count);
        context.Draw(3, 0);
    }

    /// <summary>The air's constants - ModelShaders' cbuffer Air, field for field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AirConstants
    {
        public Vector4 Fog;
        public Vector4 Shape;
        public Vector4 Player;
        public Vector4 Light;
        public Vector4 Origin;
        public Vector4 Clouds;
        public Vector4 Drift;
        public Vector4 Fades;
    }
}
