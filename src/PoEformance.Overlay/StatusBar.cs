using System.Numerics;
using System.Runtime.Versioning;
using ImGuiNET;
using PoEformance.Features;

namespace PoEformance.Overlay;

/// <summary>
/// The one line that says whether the tool is working, painted into the main window's title bar.
/// </summary>
/// <remarks>
/// THE READOUT WAS A PAGE, AND A PAGE IS SOMEWHERE YOU HAVE TO BE. "Is it reading the game" and
/// "which area does it think I am in" are questions asked WHILE doing something else - while
/// configuring markers, while reading the dissector, while wondering why a flask did not fire -
/// and the answer lived on a tab you had to leave what you were doing to go and look at. So
/// either the tool sat on its status page and the other twelve were out of reach, or it sat on
/// a tool page and a blank overlay was ambiguous again.
///
/// A strip in the title bar is on screen on every page, costs no line at all, and answers the four
/// or five questions that are worth answering constantly. The status PAGE keeps everything
/// else - the belt, the panels, the projection probe, the whole debugging pass - because those
/// are read deliberately, once, while working something out.
///
/// AND IT IS WHAT LETS THE WINDOW STOP RESIZING ITSELF. The readout being a page is why this
/// window had two sizes and switched between them: the readout auto-sized to its dozen short
/// lines and every other page was forced to 940x620, which meant that leaving the readout
/// OVERWROTE whatever size the window had been dragged to. Somebody who sized the dissector to
/// fit beside their game got that size thrown away every time they glanced at the readout and
/// came back. With the live part up here the window has one size, and that size is the user's -
/// see <c>ToolTabs.Render</c>.
///
/// CHIPS RATHER THAN A SENTENCE. Each fact is a short coloured word in a fixed order, so the
/// strip is read by SHAPE after the first day - the eye goes to the third position for the
/// entity count rather than reading a line of prose to find it. A fact that does not fit the
/// window is dropped rather than wrapped, which keeps it inside the title bar at every size;
/// they are drawn most-important-first, so what survives a narrow window is what matters.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class StatusBar
{
    /// <summary>Whether a chip has been drawn on this strip yet, so the first gets no divider.</summary>
    /// <remarks>
    /// Static because the strip is drawn once per frame on the render thread, in one place,
    /// between <see cref="InTitle"/> and <see cref="End"/> - the same thread-and-moment argument
    /// the font stack is built on. Nothing outside the render thread may touch this.
    /// </remarks>
    private static bool _started;

    /// <summary>Where the next chip starts, in screen space.</summary>
    private static float _x;

    /// <summary>Where the strip must stop: the version, or the title bar's icons.</summary>
    private static float _right;

    /// <summary>The title bar's top edge and height, for centring each chip in it.</summary>
    private static float _top;

    private static float _bar;

    /// <summary>Whether a strip is open, so a chip outside one paints nothing.</summary>
    private static bool _open;

    /// <summary>
    /// Opens the strip in the current window's TITLE BAR, between its name and <paramref name="right"/>.
    /// </summary>
    /// <remarks>
    /// IN THE TITLE BAR, asked for from the live client (v0.1.270): a line of its own under the
    /// title cost a row of every page for facts that fit beside the name. PAINTED, not submitted:
    /// an item in the title bar widens the window's measured content, which is the loop
    /// <see cref="WindowChrome.TitleButtons"/> explains - text that is only drawn cannot feed it.
    /// The name's width is measured rather than assumed, so a renamed window still starts its
    /// strip after its own title, past ImGui's collapse arrow (one font size and an inner gap).
    /// Call right after the title buttons, in the face the title was set in - and on a collapsed
    /// window too, where the title bar is all there is to see.
    /// </remarks>
    /// <param name="title">The window's own name as ImGui draws it.</param>
    /// <param name="right">The screen x the strip must not cross.</param>
    public static void InTitle(string title, float right)
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        Vector2 corner = ImGui.GetWindowPos();
        float size = ImGui.GetFontSize();

        _started = false;
        _open = true;
        _top = corner.Y;
        _bar = ImGui.GetFrameHeight();
        _right = right;
        _x = corner.X + style.FramePadding.X + size + style.ItemInnerSpacing.X
             + ImGui.CalcTextSize(title).X + (size * 0.8f);

        // Clipped to the bar, so nothing a chip paints can spill into the page below it. NOT
        // intersected with the clip in force: that is the window's content area, which starts
        // under the title bar, and intersecting with it clipped the whole strip away (v0.1.270).
        ImGui.PushClipRect(new Vector2(corner.X, _top), new Vector2(right, _top + _bar), false);
    }

    /// <summary>Closes the strip.</summary>
    public static void End()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        ImGui.PopClipRect();
    }

    /// <summary>One fact, in its own colour, after a divider.</summary>
    /// <remarks>
    /// DROPPED RATHER THAN SQUEEZED when the bar is too short for it: the facts are drawn
    /// most-important-first, so a narrow window keeps the state and the area and loses the
    /// frame timing. Dropping also stops every later chip, so the strip never shows a gap
    /// with a less important fact beyond it. The measurement includes the divider, because a
    /// chip that fits only by losing its divider would run into its neighbour.
    /// </remarks>
    /// <param name="text">The fact. Already formatted - this draws it, it does not decide it.</param>
    /// <param name="ink">What it is drawn in. The colour IS the state for most of these.</param>
    public static void Chip(string text, Vector4 ink)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!_open || text.Length == 0)
        {
            return;
        }

        float divider = _started ? ImGui.GetFontSize() * 1.6f : 0f;
        float wide = ImGui.CalcTextSize(text).X;
        if (_x + divider + wide > _right)
        {
            // Nothing after this one either: see the remarks.
            _right = float.MinValue;
            return;
        }

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        float y = _top + ((_bar - ImGui.GetTextLineHeight()) * 0.5f);

        if (_started)
        {
            float bar = ImGui.CalcTextSize("|").X;
            draw.AddText(new Vector2(_x + ((divider - bar) * 0.5f), y), ImGui.GetColorU32(OverlayInk.Edge), "|");
        }

        // AddText draws the string as it is - no printf - so area names with percent signs in
        // them need nothing done to them.
        draw.AddText(new Vector2(_x + divider, y), ImGui.GetColorU32(ink), text);
        _x += divider + wide;
        _started = true;
    }

    /// <summary>A fact in the quiet ink, for the ones that are never good or bad news.</summary>
    public static void Chip(string text) => Chip(text, OverlayInk.Quiet);

    /// <summary>
    /// A measured figure, in the mono face so it stops twitching as it counts.
    /// </summary>
    /// <remarks>
    /// THE WHOLE REASON THE MONO FACE EXISTS IN THIS TOOL, and this strip is where it earns its
    /// keep hardest: these numbers change every frame, they are on screen the entire time, and
    /// in a proportional face a count going from 999 to 1000 is WIDER - so every chip to its
    /// right shifts sideways, several times a second, forever. Fixed-width digits are what stop
    /// the strip moving while it is being watched.
    /// </remarks>
    public static void Figure(string text, Vector4 ink)
    {
        OverlayFonts.PushMono();
        try
        {
            Chip(text, ink);
        }
        finally
        {
            OverlayFonts.PopMono();
        }
    }

    /// <summary>The same, in the ink that means "this tool worked it out".</summary>
    public static void Figure(string text) => Figure(text, OverlayInk.Measured);
}
