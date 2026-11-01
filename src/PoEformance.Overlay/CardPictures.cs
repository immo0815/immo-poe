using PoEformance.Gpu;
using Vortice.Direct3D11;

namespace PoEformance.Overlay;

/// <summary>
/// The graphics card's drawing for every book's model pane: one ModelGpu on the overlay's own device, made when first asked, and how it reads textures.
/// </summary>
/// <remarks>
/// MADE ON THE RENDER THREAD, THE FIRST TIME A PANE DRAWS - the books are attached before the
/// overlay has a device, and the device's context belongs to that thread. A device the drawing cannot
/// use (shaders that will not compile, say) is asked once: the reason is kept and every pane draws on
/// the processor as before, saying why on its card button.
///
/// THE CARD ALWAYS DRAWS where it can (asked for from the live client, v0.1.231: the switch to the
/// processor is gone, and so is <c>modelCard</c>). The card button's one switch is the anisotropic
/// read (<see cref="Sharp"/>), for every book like the flat light, kept as <c>modelCardSharp</c>.
/// </remarks>
public sealed class CardPictures : IDisposable
{
    /// <summary>
    /// The folder beside the tool that compiled shaders are kept in - the materials' (ProgramShaders) and the fixed ones (ModelGpu.Warm);
    /// deleting it costs only a recompile.
    /// </summary>
    public const string ShaderFolder = "shader-cache";

    private readonly Func<(ID3D11Device? Device, ID3D11DeviceContext? Context)> _device;
    private readonly Func<ID3D11ShaderResourceView, IntPtr> _show;
    private readonly Func<IntPtr, bool> _hide;
    private ModelGpu? _gpu;
    private bool _asked;

    /// <param name="device">The overlay's device and its immediate context, asked on the render thread.</param>
    /// <param name="show">Hands ImGui a picture the card drew, for a handle to draw it by.</param>
    /// <param name="hide">Takes one back without releasing it.</param>
    public CardPictures(
        Func<(ID3D11Device? Device, ID3D11DeviceContext? Context)> device,
        Func<ID3D11ShaderResourceView, IntPtr> show,
        Func<IntPtr, bool> hide)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(hide);
        _device = device;
        _show = show;
        _hide = hide;

        // THE FIXED SHADERS COMPILED BEHIND THE FRAME FROM THE START, so the first pane drawn on the card does not wait 6 s for them - see ModelGpu.Warm.
        ModelGpu.Warm(Path.Combine(AppContext.BaseDirectory, ShaderFolder));
    }

    /// <summary>Whether the card reads textures anisotropically - what the settings keep as <c>modelCardSharp</c>. See ModelScene.Sharp.</summary>
    /// <remarks>
    /// ONE SWITCH FOR EVERY BOOK, like the flat light, and for the same reason: it is the same
    /// question in each pane. ON TO START, because it is what the game does.
    /// </remarks>
    public bool Sharp { get; set; } = true;

    /// <summary>Told when a pane's button turns the anisotropic read on or off.</summary>
    public Action<bool>? SharpChanged { get; set; }

    /// <summary>Why there is no drawing on the card, or empty while there is one or it has not been asked for.</summary>
    public string Why { get; private set; } = string.Empty;

    /// <summary>The drawing, made the first time it is asked for - render thread only.</summary>
    public ModelGpu? Gpu
    {
        get
        {
            if (!_asked)
            {
                (ID3D11Device? device, ID3D11DeviceContext? context) = _device();
                if (device is null)
                {
                    Why = "the overlay has no graphics device yet";
                    return null;
                }

                _asked = true;
                try
                {
                    _gpu = ModelGpu.Of(device, context, Path.Combine(AppContext.BaseDirectory, ShaderFolder), out string why);
                    Why = why;
                }
                catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
                {
                    // A MISSING SHADER COMPILER OR A DEVICE THAT REFUSES - the panes draw on the processor
                    // as before, and say why.
                    _gpu = null;
                    Why = $"the card's drawing would not start: {exception.Message}";
                }
            }

            return _gpu;
        }
    }

    /// <summary>
    /// Stops drawing on the card after it failed - a device lost, say - so every pane goes back to the processor and says why.
    /// </summary>
    public void Fail(string why)
    {
        _asked = true;
        Why = why;
        _gpu?.Dispose();
        _gpu = null;
    }

    /// <summary>
    /// Lets the drawing go after the overlay's graphics device was made anew, and asks again on the new one the next time a pane draws.
    /// </summary>
    /// <remarks>
    /// A FAILURE IS FORGOTTEN TOO: a pane whose drawing failed because the old device was lost goes back to
    /// the processor through <see cref="Fail"/>, and the new device is the reason to try the card again. The
    /// panes' own targets were made on the old device and are dropped by each pane - see MonsterPortrait.DeviceLost.
    /// </remarks>
    public void Reset()
    {
        _gpu?.Dispose();
        _gpu = null;
        _asked = false;
        Why = string.Empty;
    }

    /// <summary>Hands ImGui a target's picture - see <see cref="ModelTarget.Shown"/>.</summary>
    public IntPtr Show(ModelTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _show(target.Shown);
    }

    /// <summary>Takes a target's picture back from ImGui and lets the target go.</summary>
    public void Forget(ModelTarget? target, IntPtr shown)
    {
        if (shown != IntPtr.Zero)
        {
            _hide(shown);
        }

        target?.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _gpu?.Dispose();
        _gpu = null;
    }
}
