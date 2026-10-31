namespace PoEformance.Game.Files;

/// <summary>How a material's pixels are put over what is already drawn.</summary>
public enum MaterialBlend : byte
{
    /// <summary>Covers what is behind it - every material the game does not say otherwise about.</summary>
    Opaque,

    /// <summary>
    /// Mixed with what is behind it: by its shade program's alpha where its graphs set one - a ground layer fading by the depth behind it - and by its texture's own alpha where they do not.
    /// </summary>
    Alpha,

    /// <summary>Added to what is behind it - glow, fire, lightning.</summary>
    Additive,

    /// <summary>
    /// Solid where the texture's alpha is high enough and absent where it is not - leaves, hair, chains.
    /// </summary>
    /// <remarks>
    /// NOT TRANSLUCENT: a pixel is either the material's, written to depth like any opaque one, or
    /// nothing at all. Drawn in the opaque pass for that reason.
    /// </remarks>
    Cutout,

    /// <summary>
    /// Not drawn at all: the shape only casts a shadow.
    /// </summary>
    /// <remarks>
    /// black_shadowonlyc.mat is an instance of ForceOpaqueShadowOnly.fxgraph, beside its sibling
    /// ForceOpaqueNoShadow.fxgraph whose mode reads "OpaqueNoShadow". A shadow caster has no colour
    /// of its own to show, so the picture leaves it out rather than painting it black.
    /// </remarks>
    ShadowOnly,

    /// <summary>
    /// Neither drawn nor casting a shadow: a shape the picture leaves out whole - a roof section the game hides round the player (RoofFade).
    /// </summary>
    /// <remarks>
    /// NO MATERIAL IS READ AS IT: a model's own blends never hold it, only the pane's list with the roof fade laid on it. Not ShadowOnly,
    /// which casts every triangle whole - a tree's cut-out leaves hidden so cast square shadows.
    /// </remarks>
    Hidden,

    /// <summary>
    /// Not drawn, and casting its shadow through its texture as a cut-out does - a cut-out shape drawn as a shadow caster alone.
    /// </summary>
    /// <remarks>
    /// NO MATERIAL IS READ AS IT EITHER: the roof fade's capture part (CaptureRoofFade) draws the stage with its roof sections' shadows kept
    /// and the sections themselves gone, and a tree's leaves drawn ShadowOnly cast square cards where the game's cast leaves.
    /// </remarks>
    ShadowCut,
}

/// <summary>
/// What a material's blend mode, as the game spells it, is taken to mean.
/// </summary>
/// <remarks>
/// WHERE THE SPELLING COMES FROM IS ESTABLISHED; WHAT IT SAYS IS NOT. zao's <c>libpoe/poe/format/mat.cpp</c>
/// reads <c>overriden_blend_mode</c> - a string - off a material's <c>defaultgraph</c> and off any
/// <c>.fxgraph</c>, and nothing in any reference lists its values. So the reading here is a RULE ON
/// WORDS, chosen in so many words by the person this tool is for and labelled as a guess wherever it
/// shows: a value naming "add" is additive, one naming an alpha test is cut out, one naming alpha,
/// blend or transparency is mixed, anything else is opaque. The raw value is always kept beside it, so the first real effect says whether the
/// rule holds - and one that does not is a line in this method rather than a new idea somewhere else.
/// </remarks>
public static class MaterialBlends
{
    /// <summary>The blend a mode's name is taken to mean. Empty or unknown is opaque.</summary>
    public static MaterialBlend Of(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return MaterialBlend.Opaque;
        }

        // A SHADOW ONLY IS ASKED BEFORE EVERYTHING, since it says the shape is not drawn whatever else
        // its name carries.
        if (mode.Contains("shadowonly", StringComparison.OrdinalIgnoreCase))
        {
            return MaterialBlend.ShadowOnly;
        }

        // WATER IS MIXED, whatever its graph's mode: the engine draws an Ocean material in its own water
        // pass over the scene, a medium the solid surface behind shows through - and the graph under
        // nvidian_Ocean.mat says BackgroundGBufferSurfaces, which this rule would take for opaque. The
        // word is MonsterModels.Paints's, given a material whose shadertype is Ocean.
        // THE BEACH'S SHEET IS MIXED TOO, drawn over the solid part by its own pass (ShadeProgram.Beaches) - its graph says
        // BackgroundGBufferSurfaces as well. The word is RoomModels.Piled's.
        if (mode.Contains("water", StringComparison.OrdinalIgnoreCase) || mode.Contains("beach", StringComparison.OrdinalIgnoreCase))
        {
            return MaterialBlend.Alpha;
        }

        // ADDITIVE IS ASKED FIRST, because the game could well spell it "AdditiveBlend" - and that
        // carries "blend" too, which the mixed rule would otherwise take.
        if (mode.Contains("add", StringComparison.OrdinalIgnoreCase))
        {
            return MaterialBlend.Additive;
        }

        // AN ALPHA TEST IS ASKED BEFORE ALPHA, because it carries that word and means the opposite of
        // mixing. The first real value seen was "AlphaTestWithShadow", on a material whose graph is
        // ForceAlphaTestWithShadow.fxgraph - a test that keeps or drops a pixel, and casts a shadow,
        // which nothing see-through does.
        if (mode.Contains("alphatest", StringComparison.OrdinalIgnoreCase))
        {
            return MaterialBlend.Cutout;
        }

        return mode.Contains("alpha", StringComparison.OrdinalIgnoreCase)
            || mode.Contains("blend", StringComparison.OrdinalIgnoreCase)
            || mode.Contains("transp", StringComparison.OrdinalIgnoreCase)
            ? MaterialBlend.Alpha
            : MaterialBlend.Opaque;
    }
}
