using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>The engine's field waves on the card - see <see cref="Op.Field"/> and ShadeHlsl.FieldLibrary.</summary>
/// <remarks>
/// THE BREAKERS EVERY FIVE SECONDS (v0.1.295, asked for from the live client with four in-game videos of Port): the game's sea
/// rolls in white-crested fronts against the quay, the pier's end and through the channel by the ship, while the tool drew none.
/// MEASURED off the videos, the brightness of every patch of water over time: fronts 4.9 s apart at the quay's corner, 5.1 s on the
/// open sea, 4.9 s at the pier's end - the engine's field waves (water.ffx's <c>field_waves</c>: <c>field_wave_length</c> 1,000,
/// <c>field_wave_speed</c> 200, so 5 s), not its beach waves (2.5 s) nor its flood waves (25 s). They are CrashingWavesVertex and
/// CrashingWavesFragment over a mesh the engine lays itself, read nowhere, and their textures are bound by no graph the program names.
///
/// PORTED AS WRITTEN (ShadeHlsl.FieldLibrary, line for line): GetFieldWaveGeom - the wave's coordinate down the distance out to sea
/// (ShoreField's way in), its advected and stationary noise, the break phase off the shore's distance (FieldRampNorm), the crest off
/// GetGerstnerProfile times 0.3 of a wave - and GetFieldFoamSurface: GetWaveInterpNodes' eight nodes a wave, two scrolling foam
/// layers with their turbulence, dissolved by GetFieldFoamProfile, blended or overlaid as the nodes overlap.
///
/// CHOSEN, and said under the picture: they are laid on the room's own water - its vertices lifted by the crest and the area's height
/// offset as CrashingWavesVertex lifts its own mesh (the shape's mean lies under nought - a fifth of a wave's height near a shore - and
/// Port's offset of -15 lifts it back), its pixels turned by the crest's slope and covered by the foam as the swell's foam covers
/// (InitWaterSurfaceData's way) - not on a mesh of their own; their wave shape is
/// <c>gerstner_wave_profile.dds</c> (four floats, read red and blue as GetGerstnerProfile reads them - the profile the program names
/// that neither the swell nor the beach takes); their noise the beach's (flow_muddle), and the scrolling layers' turbulence that same
/// noise; their foam the beach graph's (SeaFoam colour and normal). The processor draws none (DECIDED from the live client: "the
/// card alone is fine"), and the light panel's water row switches them (SceneLight.WaterCrash).
///
/// THE PROBE WORKS THEM OUT ON THE PROCESSOR (<see cref="FieldProbe"/>, v0.1.322, asked for from the live client): Port's crests in the
/// channel by the ship came out white from the hull on, where the game's stay a dark ridge until near the quay, and which term makes
/// them white - the shore's distance off the CHOSEN field, the noise moving the break, the near-shore term - a picture cannot say.
/// The same functions as the card's, line for line, at the place the card's lifted water meets the view; still nothing drawn.
/// </remarks>
public sealed partial class ShadeProgram
{
    /// <summary>The field step's output: the foam's colour and how much it covers.</summary>
    internal const int FieldColour = 0;

    /// <summary>The field step's output: the waves' normal turned by the crest's slope, their height kept.</summary>
    internal const int FieldNormal = 1;

    /// <summary>The wave shape the field waves read - GetGerstnerProfile's <c>gerstner_profile_tex</c>, CHOSEN (see the remarks).</summary>
    public const string FieldProfilePath = "Art/2DArt/Lookup/gerstner_wave_profile.dds";

    /// <summary>The foam's colour and height - the beach graph's <c>foam_color_tex</c>, sRGB.</summary>
    public const string FieldFoamColourPath = "Art/Textures/Environment/Global/BreakingWaves/SeaFoam_colour_Uncompressed.dds";

    /// <summary>The foam's normal, ao and roughness - the beach graph's <c>foam_normal_tex</c>.</summary>
    public const string FieldFoamNormalPath = "Art/Textures/Environment/Global/BreakingWaves/SeaFoam_normal_Uncompressed.dds";

    private const string FieldProfileStandIn = "the field waves stand flat - the engine's wave shape not read";

    /// <summary>The field wave shape read: flat until <see cref="Use"/> hands over the engine's.</summary>
    private static WaterSheet s_fieldProfile = new(2, 2, 4, new float[16], false, FieldProfileStandIn);

    /// <summary>The foam's colour: white at half height until the engine's is handed over (<see cref="UseFieldFoam"/>).</summary>
    private static Mipmaps s_fieldFoamColour = Mipmaps.Of(new GamePicture(1, 1, [255, 255, 255, 128]))!;

    /// <summary>The foam's normal, ao and roughness: flat, lit and half rough until the engine's is handed over.</summary>
    private static Mipmaps s_fieldFoamNormal = Mipmaps.Of(new GamePicture(1, 1, [128, 128, 255, 128]))!;

    private static string s_fieldFoamSaid = "the field waves' foam white - the beach graph's foam not read yet";

    /// <summary>
    /// The field waves' wave shape, row by row, four floats a texel - read red (the crest's sideways offset) and blue (its height): the
    /// engine's once handed over, nought until then, which stands the waves flat.
    /// </summary>
    public static WaterSheet FieldProfile => Volatile.Read(ref s_fieldProfile);

    /// <summary>The field waves' foam colour and height, with its levels - read sRGB.</summary>
    public static Mipmaps FieldFoamColour => Volatile.Read(ref s_fieldFoamColour);

    /// <summary>The field waves' foam normal, ao and roughness, with its levels.</summary>
    public static Mipmaps FieldFoamNormal => Volatile.Read(ref s_fieldFoamNormal);

    /// <summary>What the field waves are drawn over - the wave shape and the foam, or which stands in and why.</summary>
    public static string FieldSaid => FieldProfile.Said + "; " + Volatile.Read(ref s_fieldFoamSaid);

    /// <summary>The field waves' wave shape taken where read - see <see cref="Use"/>.</summary>
    private static void UseField(WaterLookups lookups) =>
        Volatile.Write(ref s_fieldProfile, lookups.FieldProfile is { Channels: 4, Width: > 0, Height: > 0 } profile
            ? profile
            : FieldProfile with { Said = FieldProfileStandIn });

    /// <summary>
    /// Hands the field waves the beach graph's foam - decoded off the drawing thread, after the install opens; the card makes its
    /// textures again when it sees them change. A null keeps the stand-in, saying why.
    /// </summary>
    public static void UseFieldFoam(Mipmaps? colour, Mipmaps? normal, string why)
    {
        if (colour is null || normal is null)
        {
            Volatile.Write(ref s_fieldFoamSaid, "the field waves' foam white - " + why);
            return;
        }

        Volatile.Write(ref s_fieldFoamColour, colour);
        Volatile.Write(ref s_fieldFoamNormal, normal);
        Volatile.Write(ref s_fieldFoamSaid, "their foam the beach graph's (SeaFoam colour and normal)");
    }
}
