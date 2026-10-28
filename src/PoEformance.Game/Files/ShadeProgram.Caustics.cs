namespace PoEformance.Game.Files;

/// <summary>The caustics the card lays over what the water bends (SceneLight.WaterCaustics): the engine's two pass textures for them.</summary>
/// <remarks>
/// WATER.FFX'S GetCausticsBias READS TWO TEXTURES THE ENGINE BINDS ITSELF, named by no file: <c>caustics_tex_sampler</c>, its green
/// as <c>pow(g, 1 / 4.2) * 2 - 1</c> at the bent ray's end times 0.002, and <c>distortion_sampler</c>, its red and green as
/// <c>(rg * 2 - 1) * 0.4</c> at the end times 0.001 drifting with the clock, which moves the first. SETTLED BY THE FILES (the water
/// textures part's WaterPassTextures, measured again in Crimson Shores' capture of v0.1.341): the caustics are
/// <see cref="CausticsPath"/>, which the game's program names and the source's own comment over the caustics calls
/// "water_caustics_2.dds" - one greyscale picture in every channel, bright lines over a darker floor, tiling (its wrap's step 1.1
/// times one inside; the install's <c>General/Water/water_caustics_2_bc1.dds</c> holds the same picture); the distortion is
/// <see cref="DistortionPath"/>, which the program names too and the only candidate both centred on nought and tiling (its mean vector
/// (0.005, -0.029), its wrap's step 1.6). Until both are handed over the card draws no caustics, and the line under the picture says why.
/// </remarks>
public sealed partial class ShadeProgram
{
    /// <summary>The caustics' picture - water.ffx's <c>caustics_tex_sampler</c>, as the remarks settle it.</summary>
    public const string CausticsPath = "Art/particles/environment_effects/waterfalls/twilight_strand/water_caustics_2.dds";

    /// <summary>The caustics' distortion - water.ffx's <c>distortion_sampler</c>, as the remarks settle it.</summary>
    public const string DistortionPath = "Art/particles/distortion/muddle_double.dds";

    private static Mipmaps? s_caustics;

    private static Mipmaps? s_distortion;

    private static string s_causticsSaid = "the caustics' textures not read yet";

    /// <summary>The caustics' picture with its levels, or null until read.</summary>
    public static Mipmaps? Caustics => Volatile.Read(ref s_caustics);

    /// <summary>The caustics' distortion with its levels, or null until read.</summary>
    public static Mipmaps? Distortion => Volatile.Read(ref s_distortion);

    /// <summary>What the caustics are drawn over, or why they are not.</summary>
    public static string CausticsSaid => Volatile.Read(ref s_causticsSaid);

    /// <summary>
    /// Hands the caustics their two textures - decoded off the drawing thread, after the install opens; the card draws them once both are in.
    /// A null for either keeps them undrawn, saying why.
    /// </summary>
    public static void UseCaustics(Mipmaps? caustics, Mipmaps? distortion, string why)
    {
        if (caustics is null || distortion is null)
        {
            Volatile.Write(ref s_causticsSaid, "the caustics not drawn - " + why);
            return;
        }

        Volatile.Write(ref s_distortion, distortion);
        Volatile.Write(ref s_caustics, caustics);
        Volatile.Write(ref s_causticsSaid, "the caustics over water_caustics_2.dds, moved by muddle_double.dds");
    }
}
