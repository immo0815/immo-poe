using System.Globalization;

namespace PoEformance.Game.Files;

/// <summary>
/// The simulation an .ao's ClientAnimationController names, every key at its value or the engine's default - as the engine reads it.
/// </summary>
/// <remarks>
/// READ FROM THE ENGINE'S OWN CODE (v0.1.420; the build of 2026-10-27, program-code.txt of the capture 2026-11-01 11:23). The keys are
/// parsed by two methods of the controller definition's vtable: the floats by module+0x285BF80, each key compared whole and stored to the
/// definition at its offset, and the bools by module+0x285C2C0. <c>viscosity_multiplier</c> is the same store as
/// <c>normal_viscosity_multiplier</c> - an older spelling. The defaults are what the definition's constructor (module+0x285A720) writes
/// before any key is read, so a set that leaves a key out keeps them:
///
///     +0x78 linear_stiffness              -1      +0x98 enclosure_radius_additive     0
///     +0x7C linear_allowed_contraction    0.5     +0x9C animation_velocity_factor     0
///     +0x80 bend_stiffness                10      +0xA0 animation_force_factor        0
///     +0x84 bend_allowed_angle            1       +0xA4 animation_velocity_multiplier 1
///     +0x88 normal_viscosity_multiplier   0.2     +0xA8 animation_max_velocity        1000
///     +0x8C tangent_viscosity_multiplier  0.02    +0xAC gravity                       980
///     +0x90 enclosure_radius              0       +0xB0 use_shock_propagation         true
///     +0x94 enclosure_angle               0.5     +0xB1 enable_collisions             true
///                                                 +0xB2 enable_heightmap_collisions   false
///                                                 +0xB3 recreate_on_scale             true
///
/// WHAT EACH DOES, read from the code that uses them (the physics object's step, module+0x1330160 and what it calls): gravity is a force
/// (0, 0, gravity) on every particle each step - down, the game's up being minus z; the two viscosities make a particle's velocity relative
/// to the wind decay as exp(-viscosity * 30 * dt), across its surface's normal and along it (module+0x13AB700); where either animation factor
/// is above 1e-7 the velocity relaxes to a target - the way to its animated place times the velocity factor plus the animated place's own
/// velocity times the multiplier, held to the max velocity - as exp(-force factor * dt) (module+0x13ABBC0); and every particle is kept
/// within enclosure_radius times its depth from the pinned ones, plus the additive, plus its animated link to its parent times
/// enclosure_angle, of its animated place (module+0x13A8440). The two stiffnesses and their allowed contraction and angle go to the
/// physics world's constraints.
/// </remarks>
public sealed record ClothSettings(
    float LinearStiffness,
    float LinearAllowedContraction,
    float BendStiffness,
    float BendAllowedAngle,
    float NormalViscosity,
    float TangentViscosity,
    float EnclosureRadius,
    float EnclosureAngle,
    float EnclosureRadiusAdditive,
    float AnimationVelocityFactor,
    float AnimationForceFactor,
    float AnimationVelocityMultiplier,
    float AnimationMaxVelocity,
    float Gravity,
    bool ShockPropagation,
    bool Collisions,
    bool HeightmapCollisions,
    bool RecreateOnScale)
{
    /// <summary>The engine's defaults - the definition's constructor, module+0x285A720.</summary>
    public static ClothSettings Defaults { get; } = new(
        -1f, 0.5f, 10f, 1f, 0.2f, 0.02f, 0f, 0.5f, 0f, 0f, 0f, 1f, 1000f, 980f, true, true, false, true);

    /// <summary>The factor the step multiplies both viscosities by before the decay (module+0x1330160).</summary>
    public const float ViscosityScale = 30f;

    /// <summary>
    /// The settings a ClientAnimationController's keys make: each key the engine reads at its value, the rest at <see cref="Defaults"/>.
    /// A value that does not read as a number or a bool keeps the default - the engine's parser is not read for that case.
    /// </summary>
    /// <param name="keys">The block's keys and values, nearest file first.</param>
    /// <param name="unknown">The keys the engine's parsers do not name, besides the rig's, a socket's and the attachment's.</param>
    public static ClothSettings Of(IReadOnlyDictionary<string, string> keys, out IReadOnlyList<string> unknown)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var left = new List<string>();
        ClothSettings settings = Defaults;
        foreach ((string key, string value) in keys)
        {
            float number = float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : float.NaN;
            bool? flag = value.Trim().ToUpperInvariant() switch
            {
                "TRUE" or "1" => true,
                "FALSE" or "0" => false,
                _ => null,
            };

            settings = key switch
            {
                "linear_stiffness" when float.IsFinite(number) => settings with { LinearStiffness = number },
                "linear_allowed_contraction" when float.IsFinite(number) => settings with { LinearAllowedContraction = number },
                "bend_stiffness" when float.IsFinite(number) => settings with { BendStiffness = number },
                "bend_allowed_angle" when float.IsFinite(number) => settings with { BendAllowedAngle = number },
                "normal_viscosity_multiplier" or "viscosity_multiplier" when float.IsFinite(number) => settings with { NormalViscosity = number },
                "tangent_viscosity_multiplier" when float.IsFinite(number) => settings with { TangentViscosity = number },
                "enclosure_radius" when float.IsFinite(number) => settings with { EnclosureRadius = number },
                "enclosure_angle" when float.IsFinite(number) => settings with { EnclosureAngle = number },
                "enclosure_radius_additive" when float.IsFinite(number) => settings with { EnclosureRadiusAdditive = number },
                "animation_velocity_factor" when float.IsFinite(number) => settings with { AnimationVelocityFactor = number },
                "animation_force_factor" when float.IsFinite(number) => settings with { AnimationForceFactor = number },
                "animation_velocity_multiplier" when float.IsFinite(number) => settings with { AnimationVelocityMultiplier = number },
                "animation_max_velocity" when float.IsFinite(number) => settings with { AnimationMaxVelocity = number },
                "gravity" when float.IsFinite(number) => settings with { Gravity = number },
                "use_shock_propagation" when flag is { } on => settings with { ShockPropagation = on },
                "enable_collisions" when flag is { } on => settings with { Collisions = on },
                "enable_heightmap_collisions" when flag is { } on => settings with { HeightmapCollisions = on },
                "recreate_on_scale" when flag is { } on => settings with { RecreateOnScale = on },
                _ => settings,
            };

            if (!Known.Contains(key))
            {
                left.Add(key);
            }
        }

        unknown = left;
        return settings;
    }

    /// <summary>The keys the engine's two parsers name.</summary>
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "linear_stiffness", "linear_allowed_contraction", "bend_stiffness", "bend_allowed_angle", "normal_viscosity_multiplier",
        "viscosity_multiplier", "tangent_viscosity_multiplier", "enclosure_radius", "enclosure_angle", "enclosure_radius_additive",
        "animation_velocity_factor", "animation_force_factor", "animation_velocity_multiplier", "animation_max_velocity", "gravity",
        "use_shock_propagation", "enable_collisions", "enable_heightmap_collisions", "recreate_on_scale",
    };

    /// <summary>
    /// The block of floats the engine's physics object keeps these as, from +0xD8 (its constructor, module+0x13A7A60): normal and tangent
    /// viscosity, enclosure radius, angle and additive, animation force factor, velocity factor, velocity multiplier, max velocity, a one
    /// (a scale the force and velocity factors are multiplied by), gravity.
    /// </summary>
    public float[] ObjectBlock() =>
    [
        NormalViscosity, TangentViscosity, EnclosureRadius, EnclosureAngle, EnclosureRadiusAdditive, AnimationForceFactor,
        AnimationVelocityFactor, AnimationVelocityMultiplier, AnimationMaxVelocity, 1f, Gravity,
    ];

    /// <summary>The settings in one line, each key by its name - defaults marked with a star.</summary>
    public string Say()
    {
        string One(string name, float value, float fallback)
            => string.Create(CultureInfo.InvariantCulture, $"{name} {value:G6}{(value.Equals(fallback) ? "*" : string.Empty)}");
        string Flag(string name, bool value, bool fallback) => $"{name} {(value ? "true" : "false")}{(value == fallback ? "*" : string.Empty)}";
        ClothSettings d = Defaults;
        return string.Join(", ",
            One("linear_stiffness", LinearStiffness, d.LinearStiffness),
            One("linear_allowed_contraction", LinearAllowedContraction, d.LinearAllowedContraction),
            One("bend_stiffness", BendStiffness, d.BendStiffness),
            One("bend_allowed_angle", BendAllowedAngle, d.BendAllowedAngle),
            One("normal_viscosity", NormalViscosity, d.NormalViscosity),
            One("tangent_viscosity", TangentViscosity, d.TangentViscosity),
            One("enclosure_radius", EnclosureRadius, d.EnclosureRadius),
            One("enclosure_angle", EnclosureAngle, d.EnclosureAngle),
            One("enclosure_radius_additive", EnclosureRadiusAdditive, d.EnclosureRadiusAdditive),
            One("animation_velocity_factor", AnimationVelocityFactor, d.AnimationVelocityFactor),
            One("animation_force_factor", AnimationForceFactor, d.AnimationForceFactor),
            One("animation_velocity_multiplier", AnimationVelocityMultiplier, d.AnimationVelocityMultiplier),
            One("animation_max_velocity", AnimationMaxVelocity, d.AnimationMaxVelocity),
            One("gravity", Gravity, d.Gravity),
            Flag("use_shock_propagation", ShockPropagation, d.ShockPropagation),
            Flag("enable_collisions", Collisions, d.Collisions),
            Flag("enable_heightmap_collisions", HeightmapCollisions, d.HeightmapCollisions),
            Flag("recreate_on_scale", RecreateOnScale, d.RecreateOnScale));
    }
}
