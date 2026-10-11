using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>The engine's water, as a program - see <see cref="Water"/>.</summary>
public sealed partial class ShadeProgram
{
    /// <summary>What the water program is named in a picture's lines.</summary>
    public const string WaterGraph = "the engine's water pass, drawn as the area's open water (WaterLook)";

    /// <summary>
    /// The shader type the engine draws in its own water pass rather than through the material's graphs.
    /// </summary>
    /// <remarks>
    /// <c>nvidian_Ocean.mat</c> - Port's harbour's 31 WaterPlane3x3 doodads - says <c>"shadertype":"Ocean"</c>
    /// beside <c>"splashywater":true</c>, and its graphs write no colour of their own the picture can draw:
    /// the colour is the pass's (common.ffx: PASS_TYPE_WATER, water_color_open, water_color_terrain).
    /// </remarks>
    public const string OceanShader = "Ocean";

    /// <summary>
    /// How fast the water thins out towards the floor behind it, a unit of water at a time: the engine's water pass's own number.
    /// </summary>
    /// <remarks>
    /// internalonly.ffx's WaterComputeRefraction hands water.ffx's BuildWaterRefraction <c>0.03</c> as its
    /// <c>water_to_terrain_mult</c>, and that makes <c>water_to_terrain_ratio = saturate(1 - gap * 0.03 * lerp(1, 30, ground_water_ratio))</c>,
    /// the gap the distance along the view from the surface to the solid behind (DepthDistance's). The density the medium
    /// absorbs with is multiplied by <c>1 - water_to_terrain_ratio</c>, and a fragment whose ratio reaches one is discarded:
    /// the water fades to nothing within 33 units of the floor. The ratio's other term is nought on water drawn this way:
    /// GetFlowingSurface's planar state (GetFlowingWaterPlanarStateWorld) leaves both its ground_water_ratio and its own
    /// water_to_terrain_ratio at nought, so the shore's thirtyfold is not taken.
    /// </remarks>
    public const float ShoreFade = 0.03f;

    /// <summary>
    /// Open water as a medium: the area's colour, covering by how much water the view crosses before the solid surface behind it, its pattern moving with the flow.
    /// </summary>
    /// <remarks>
    /// THE ENGINE'S MEDIUM, cut down to what the picture can say. water.ffx's GetRefractedColor lights the
    /// medium with <c>water_color_open.rgb</c> and lets through <c>exp(-distance * density)</c> of what lies
    /// behind, the density being <c>water_color_open.a</c>; here the distance is the depth from the water's
    /// surface to the solid one behind it along the view (DepthDistance's, see <see cref="Op.Depth"/>), the
    /// colour and density the area's (<see cref="WaterColour"/>), lit as any colour is, and what gets
    /// through is the picture behind it showing through the mix. Where nothing solid lies behind, the
    /// water covers whole. Near the floor the density thins as the engine thins it (<see cref="ShoreFade"/>),
    /// so a shore's edge fades out rather than standing as a band of colour.
    ///
    /// THE PASS'S OWN FORMULA. The water pass (internalonly.ffx's WaterComputeSurface and WaterComputeRefraction,
    /// water.ffx's BuildWaterRefraction and GetRefractedWaterLight) mixes <c>opaque * r + absorbed * (1 - r)</c>,
    /// <c>r = exp(-travel * density)</c>; what it adds beyond this program is a Fresnel reflection weighted by the
    /// area's reflectiveness, the sun's highlight, the environment's cube and screen-space reflections, the
    /// refraction (index 1.33) bending the ray it measures the travel along, foam, and the waves' normals.
    ///
    /// NOT THE ENGINE'S WAVES. Its surface is a sum of waves and flow maps (WaterComputeSurface,
    /// GetFlowingSurface) that no picture of a room carries. What stands for them is a pattern of value
    /// noise on the surface, a cell every 120 units, its brightness from 0.85 to 1.15 of the colour,
    /// drifting at the area's flow - numbers chosen for a moving surface that reads as water, not
    /// measured, and still where an area's flow is nought.
    /// </remarks>
    public static ShadeProgram Water()
    {
        var build = new Builder();

        // HOW MUCH WATER THE VIEW CROSSES: the solid surface behind less the water's own depth, never less than nought.
        int crossed = build.Emit(Op.Max, build.Emit(Op.Depth, Position), build.Constant(Vector4.Zero));

        // THE SHORE: the density thinned within 33 units of the floor, 1 - water_to_terrain_ratio.
        int shore = build.Emit(Op.Saturate, build.Emit(Op.Multiply, crossed, build.Constant(new Vector4(ShoreFade))));

        // WHAT GETS THROUGH, exp(-distance * density), and the cover is the rest.
        int density = build.Emit(Op.Multiply, build.Emit(Op.Swizzle, WaterColour, extra: AllFrom(3)), shore);
        int travelled = build.Emit(Op.Negate, build.Emit(Op.Multiply, crossed, density));
        int through = build.Emit(Op.Power, build.Constant(new Vector4(MathF.E)), travelled);
        int cover = build.Emit(Op.OneMinus, through);

        // THE PATTERN, drifting at the flow: x and y off the surface, z the time - all of it still at a flow of nought.
        int flow = build.Emit(Op.Swizzle, WaterFlow, extra: AllFrom(0));
        int rate = build.Emit(Op.Multiply, flow, build.Constant(new Vector4(0.15f, 0.1f, 0.2f, 0f)));
        int surface = build.Emit(Op.Multiply, Position, build.Constant(new Vector4(1f / 120f, 1f / 120f, 0f, 0f)));
        int spot = build.Emit(Op.MultiplyAdd, Clock, rate, surface);
        int pattern = build.Emit(Op.Noise, spot, extra: 0);
        int shimmer = build.Emit(Op.MultiplyAdd, pattern, build.Constant(new Vector4(0.3f)), build.Constant(new Vector4(0.85f)));

        // THE COLOUR, its w the cover.
        int colour = build.Emit(Op.Multiply, WaterColour, shimmer);
        build.Into(colour, cover, Only(3, 0));
        return Finish(build, colour, -1, -1, [WaterGraph], hasAlpha: true, plainable: false)
            ?? throw new InvalidOperationException("the water program does not fit its registers");
    }

    /// <summary>A swizzle putting one component of its source in all four.</summary>
    private static int AllFrom(int component) => (0xF << 8) | component | (component << 2) | (component << 4) | (component << 6);

    /// <summary>A swizzle writing one component from one of its source's.</summary>
    private static int Only(int into, int from) => (1 << (8 + into)) | (from << (into * 2));
}
