using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

namespace PoEformance.Gpu;

/// <summary>
/// Compiles a linked particle shader (ParticleLinker) the way the tool's drawing would - FXC through D3DCompiler, cs_5_0 for Direct3D 11 - and
/// says what came of it, for the capture's particles part to write beside the shader.
/// </summary>
/// <remarks>
/// WHY HERE (v0.1.430): the shaders are linked offline from a capture and compiled there with DXC (SM 6), which is not what the tool draws with;
/// only a Windows build can ask FXC, and the capture key runs on one. The includes are expanded before (ParticleLinker.Expanded), so no
/// include handler is handed over.
/// </remarks>
public static class ParticleCompile
{
    /// <summary>The compute profile the tool's Direct3D 11 card is asked for.</summary>
    public const string Profile = "cs_5_0";

    /// <summary>
    /// The profile a linked shader's entry point is compiled for - the update's cs_5_0, a render pass's vs_5_0 and ps_5_0 (v0.1.440): the
    /// shader model the tool's own drawing compiles for.
    /// </summary>
    public static string ProfileOf(string entry) => entry switch
    {
        "VertexMain" => "vs_5_0",
        "PixelMain" => "ps_5_0",
        _ => Profile,
    };

    /// <summary>Compiles a shader with every include already expanded; a line saying the bytecode's size, or why it would not compile.</summary>
    public static string Fxc(string hlsl, string entry)
    {
        ArgumentNullException.ThrowIfNull(hlsl);
        ArgumentNullException.ThrowIfNull(entry);
        if (!OperatingSystem.IsWindows())
        {
            return "not compiled: FXC runs on Windows alone";
        }

        try
        {
            string profile = ProfileOf(entry);
            Result result = Compiler.Compile(hlsl, null!, null!, entry, "particle.hlsl", profile, ShaderFlags.IeeeStrictness | ShaderFlags.OptimizationLevel3, out Blob blob, out Blob errors);
            try
            {
                string said = errors is null ? string.Empty : Marshal.PtrToStringAnsi(errors.BufferPointer) ?? string.Empty;
                return result.Failure || blob is null
                    ? $"would not compile ({profile}): {(said.Length > 0 ? said : result.ToString())}"
                    : $"compiled ({profile}): {blob.BufferSize:N0} bytes{(said.Length > 0 ? "; warnings: " + said.Replace('\n', ' ') : string.Empty)}";
            }
            finally
            {
                blob?.Dispose();
                errors?.Dispose();
            }
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            return $"would not compile ({ProfileOf(entry)}): {exception.Message}";
        }
    }
}
