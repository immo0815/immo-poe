namespace PoEformance.Game.Files;

/// <summary>
/// What a light's <c>style</c> line does to its colour over time - the engine's flicker, measured off the game's own light list.
/// </summary>
/// <remarks>
/// MEASURED (2026-11-01 08:31, The Red Vale, v0.1.412's bone watch): the Exile's Staff's lamp - WandererStaffHeld.ao, <c>style = 1</c> - stands
/// in the renderer's light list (schema <c>RenderPointLight</c>) as its .ao colour times a factor that runs through 25 keys at ten a second,
/// each to the next in a straight line, over and over: the light read every tick (10 ms) for 10.8 s, three periods of 2.5 s key for key
/// alike, the keys 0.1000 s apart found with 0.001 of the factor left over, and the three channels in the .ao's own proportions throughout.
/// The keys are 0.72, 0.80, 0.84 and one, to a thousandth. Style 0, or no style line, is the colour as written; any other style is not
/// measured, and stands as written too - <see cref="Measured"/> says which.
/// WHERE THE LOOP STANDS in the game is in no file and was not read; a picture starts it at its brightest key at its clock's nought (CHOSEN).
/// </remarks>
public static class LightStyle
{
    /// <summary>The keys of style 1, from its brightest, <see cref="KeysPerSecond"/> a second - MEASURED, see the class remarks.</summary>
    private static readonly float[] One =
    [
        1.00f, 0.84f, 0.80f, 0.72f, 0.84f, 0.72f, 0.72f, 0.80f, 0.72f, 0.72f, 0.84f, 0.72f, 0.72f,
        0.80f, 0.72f, 0.84f, 0.72f, 0.72f, 0.80f, 0.72f, 0.72f, 0.80f, 0.72f, 0.72f, 0.84f,
    ];

    /// <summary>How many keys a second a style runs through - MEASURED for style 1.</summary>
    public const float KeysPerSecond = 10f;

    /// <summary>How long style 1 takes to come round again, in seconds.</summary>
    public static float Period => One.Length / KeysPerSecond;

    /// <summary>True where the style's flicker was measured - none (0) and style 1; any other draws its colour as written.</summary>
    public static bool Measured(int style) => style is 0 or 1;

    /// <summary>True where the style changes the colour over time as measured - style 1 alone.</summary>
    public static bool Flickers(int style) => style == 1;

    /// <summary>
    /// The factor on a light's colour <paramref name="seconds"/> into its loop: style 1's keys joined by straight lines, one for any other.
    /// </summary>
    public static float Factor(int style, double seconds)
    {
        if (style != 1 || !double.IsFinite(seconds))
        {
            return 1f;
        }

        double at = seconds * KeysPerSecond;
        double whole = Math.Floor(at);
        int key = (int)(((long)whole % One.Length + One.Length) % One.Length);
        float along = (float)(at - whole);
        float from = One[key];
        return from + ((One[(key + 1) % One.Length] - from) * along);
    }
}
