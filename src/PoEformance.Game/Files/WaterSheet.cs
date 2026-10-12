namespace PoEformance.Game.Files;

/// <summary>
/// A lookup the water reads - the ripple texture or the swell's cross-section: its texels, row by row, and what it is.
/// </summary>
/// <param name="Width">Texels across.</param>
/// <param name="Height">Texels down.</param>
/// <param name="Channels">Floats a texel.</param>
/// <param name="Texels">Row by row, <paramref name="Channels"/> floats a texel - what the card is handed as it is.</param>
/// <param name="Engine">Whether it is the engine's own, read from the install, rather than this tool's stand-in.</param>
/// <param name="Said">What it is, for the line under a picture: the engine's file, or the stand-in and why the engine's is not used.</param>
public sealed record WaterSheet(int Width, int Height, int Channels, float[] Texels, bool Engine, string Said);
