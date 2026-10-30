using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>Where a lit picture's ambient light comes from - see <see cref="SceneLight.Ambient"/>.</summary>
public enum SceneAmbient
{
    /// <summary>None: only the lights.</summary>
    None,

    /// <summary>The picture's own flat ambient, the same in every direction.</summary>
    Flat,

    /// <summary>The environment's diffuse cube, the game's way.</summary>
    Cube,
}

/// <summary>
/// What a picture lit the game's way shows - the lit picture, or one of the material's channels as the engine's own debug views show it.
/// </summary>
/// <remarks>
/// THE ENGINE'S DEBUG VIEWS, ported from their graphs (Metadata/EngineGraphs/debug_*.fxgraph and renderer/nodes/debug.ffx, whole in a capture's
/// render-debug.txt, v0.1.317) and named as the game's program lists them (v0.1.318's capture, the strings round the table of views): each hands
/// DebugOutputColor a colour in place of the lit one - the albedo as it is, the specular colour, the gloss and one less it through RGBToSRGB
/// (<c>pow(saturate(x), 2.2)</c>), the normal through TbnToRGB (<c>(normalize(n) + 1) / 2</c>) and RGBToSRGB, or as it is - while
/// debug_lighting_only lights a grey of 0.25 in place of the albedo. In the game they are compiled for profiling builds alone
/// (DebugOverlayIntensity is nought without PROFILING), so a player never sees them; here they are what the picture shows.
/// </remarks>
public enum PictureView
{
    /// <summary>"Final Color": the lit picture.</summary>
    Picture,

    /// <summary>"Albedo" (debug_albedo): the albedo, linear.</summary>
    Albedo,

    /// <summary>"Specular" (debug_specular): the specular colour, linear.</summary>
    Specular,

    /// <summary>"Gloss" (debug_gloss): <c>pow(saturate(gloss), 2.2)</c>.</summary>
    Gloss,

    /// <summary>"Roughness" (debug_roughness): <c>pow(1 - saturate(gloss), 2.2)</c>.</summary>
    Roughness,

    /// <summary>"World Normal (Fit)" (debug_normal): <c>pow((normalize(n) + 1) / 2, 2.2)</c> - black (-1, -1, -1), white (1, 1, 1).</summary>
    NormalFit,

    /// <summary>"World Normal (True)" (debug_normal2): <c>pow(saturate(n), 2.2)</c> - the normal as it is, its negative parts black.</summary>
    NormalTrue,

    /// <summary>"Lighting Only" (debug_lighting_only): the picture lit as it is, a grey of 0.25 in place of every albedo, no normal maps.</summary>
    LightingOnly,
}

/// <summary>
/// How a picture is lit the game's way: an area's sun, the player's light, a room's point lights, the environment cube and the exposure - each switchable.
/// </summary>
/// <remarks>
/// THE ARITHMETIC IS THE GAME'S SHADERS', line for line where they have it (renderer/lighting.ffx,
/// postprocessuber.hlsl; NEW_LIGHT is 1 in PoE2):
/// <list type="bullet">
/// <item>A POINT LIGHT - ComputePointLightParamsNew: the cutoff is <c>radius * sqrt(sum|colour| * 0.2 / 0.1)</c>,
/// the radius multiplier <c>lerp(1/sqrt(0.02), 100, saturate(a))</c>, and the light at a distance d
/// <c>colour * min(10, 0.2 m^2 * saturate(1 - (d/cutoff)^4)^2 / (d m / radius + 1)^2)</c>.</item>
/// <item>THE SUN - ComputeDirectionalLightParams: its colour, from a fixed direction.</item>
/// <item>EACH ADDS <c>saturate(N.L) * light * albedo</c> (ComputeDiffuse) and its GGX lobe where the
/// material is glossy (ComputeLight), and every light's intensity is summed into <c>total</c>.</item>
/// <item>THE CUBE - AddDiffuseEnvironmentLight: <c>cube(N turned by env_map_rotation) * (cube_brightness.x
/// + total * direct_light_env_ratio) * albedo * (1 - gi_env_occlusion)</c>.</item>
/// <item>THE EXPOSURE - ApplyToneMapping with GGG_POE_1 off: the colour times <c>max(1, exposure)</c>.</item>
/// </list>
///
/// WHAT THE SHADERS DO NOT SAY is turned into vectors on the processor, and those were candidate
/// readings until the game's memory answered them: how the .env's phi and theta make the sun's
/// direction (<see cref="GameSun"/>) and how hor_angle makes env_map_rotation (<see cref="CubeTurnFrom"/>)
/// were both found by LightHunt. How vert_angle tips the cube (<see cref="CubeReading"/>) and which
/// number a room light's light_position_data.a is (<see cref="PointShape"/>) are candidates still, and
/// the player light's radius and height are in no file read so far.
///
/// WHERE GI OWNS THE AMBIENT (gi_env_occlusion one, Seepage among them) the game's own global
/// illumination stands in for the cube, and that is not something this draws: the flat ambient is
/// what is left, and the line under the picture says so.
///
/// IN MODEL SPACE: the lights, the vertices and the normals are all in the picture's model space,
/// which for a room is the room's own - the area's, for a room drawn as the area laid it.
/// </remarks>
public sealed class SceneLight
{
    /// <summary>The cutoff radius's colour threshold and zero-intensity multiplier - GetPointCutoffRadius.</summary>
    private const float ColourThreshold = 0.1f;

    /// <inheritdoc cref="ColourThreshold"/>
    private const float ZeroIntensity = 0.2f;

    /// <summary>The cells of the light grid each way, at most.</summary>
    private const int MostCells = 32;

    private readonly Point[] _lights;
    private readonly int[] _cellStarts;
    private readonly int[] _cellEntries;
    private readonly Vector3 _gridLeast;
    private readonly float _cellSize;
    private readonly int _cellsX, _cellsY, _cellsZ;

    /// <param name="points">The room's point lights, in model space - <see cref="PointLight"/>.</param>
    /// <param name="shape">Which number stands for each one's light_position_data.a.</param>
    /// <param name="player">The player's light where it is drawn, else null.</param>
    public SceneLight(IReadOnlyList<PointLight> points, PointShape shape, PlayerLamp? player)
    {
        ArgumentNullException.ThrowIfNull(points);
        var lights = new List<Point>(points.Count + 1);
        foreach (PointLight one in points)
        {
            float a = shape switch
            {
                PointShape.One => 1f,
                PointShape.Penumbra => one.PenumbraDist,
                _ => 0f,
            };

            if (Of(one.Position, one.Colour, one.Radius, a) is { } light)
            {
                lights.Add(light);
            }
        }

        // THE PLAYER'S LIGHT IS A POINT LIGHT TOO, with a of nought: the shader's own comment on the
        // radius multiplier - "1.0f / sqrt(0.02f) -- params of player light for backwards compatibility".
        if (player is { } lamp && Of(lamp.Position, lamp.Colour, lamp.Radius, 0f) is { } own)
        {
            lights.Add(own);
        }

        _lights = [.. lights];
        PointCount = _lights.Length;

        // A GRID OVER THE LIGHTS' REACH, so a pixel asks only the lights whose cutoff reaches its
        // cell - a room with a hundred torches costs each pixel the two or three near it.
        if (_lights.Length == 0)
        {
            _cellStarts = [0, 0];
            _cellEntries = [];
            _cellsX = _cellsY = _cellsZ = 1;
            _cellSize = 1f;
            return;
        }

        Vector3 least = new(float.MaxValue), most = new(float.MinValue);
        float widest = 0f;
        foreach (Point one in _lights)
        {
            least = Vector3.Min(least, one.Position - new Vector3(one.Cutoff));
            most = Vector3.Max(most, one.Position + new Vector3(one.Cutoff));
            widest = MathF.Max(widest, one.Cutoff);
        }

        Vector3 span = most - least;
        float cell = MathF.Max(MathF.Max(span.X, MathF.Max(span.Y, span.Z)) / MostCells, MathF.Max(widest * 0.5f, 1f));
        _gridLeast = least;
        _cellSize = cell;
        _cellsX = Math.Clamp((int)MathF.Ceiling(span.X / cell), 1, MostCells);
        _cellsY = Math.Clamp((int)MathF.Ceiling(span.Y / cell), 1, MostCells);
        _cellsZ = Math.Clamp((int)MathF.Ceiling(span.Z / cell), 1, MostCells);
        int cells = _cellsX * _cellsY * _cellsZ;
        var counts = new int[cells + 1];
        for (var pass = 0; pass < 2; pass++)
        {
            int[]? cursors = pass == 1 ? (int[])counts.Clone() : null;
            int[] entries = pass == 1 ? new int[counts[cells]] : [];
            for (var at = 0; at < _lights.Length; at++)
            {
                Point one = _lights[at];
                (int x0, int y0, int z0) = Cell(one.Position - new Vector3(one.Cutoff));
                (int x1, int y1, int z1) = Cell(one.Position + new Vector3(one.Cutoff));
                for (int z = z0; z <= z1; z++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int index = (((z * _cellsY) + y) * _cellsX) + x;
                            if (pass == 0)
                            {
                                counts[index + 1]++;
                            }
                            else
                            {
                                entries[cursors![index]++] = at;
                            }
                        }
                    }
                }
            }

            if (pass == 0)
            {
                for (var at = 0; at < cells; at++)
                {
                    counts[at + 1] += counts[at];
                }
            }
            else
            {
                _cellEntries = entries;
            }
        }

        _cellStarts = counts;
        _cellEntries ??= [];
    }

    /// <summary>What stands for a room light's light_position_data.a, which no file names - a candidate reading.</summary>
    public enum PointShape
    {
        /// <summary>Nought: the player light's own value, a soft core.</summary>
        Zero,

        /// <summary>One: the sharpest core the shader allows.</summary>
        One,

        /// <summary>The light's penumbra_dist line, clamped as the shader clamps it.</summary>
        Penumbra,
    }

    /// <summary>How the .env's phi and theta make the sun's direction - candidate readings, see the class remarks.</summary>
    public enum SunReading
    {
        /// <summary>Theta from straight up, phi round it from x; the vector points at the sun.</summary>
        PolarTheta,

        /// <summary>Theta as the height above the ground, phi round it from x; the vector points at the sun.</summary>
        ElevationTheta,

        /// <summary>Phi from straight up, theta round it from x; the vector points at the sun.</summary>
        PolarPhi,

        /// <summary>Phi as the height above the ground, theta round it from x; the vector points at the sun.</summary>
        ElevationPhi,

        /// <summary>As <see cref="PolarTheta"/>, the vector the way the light travels.</summary>
        PolarThetaFalling,

        /// <summary>As <see cref="ElevationTheta"/>, the vector the way the light travels.</summary>
        ElevationThetaFalling,

        /// <summary>As <see cref="PolarPhi"/>, the vector the way the light travels.</summary>
        PolarPhiFalling,

        /// <summary>As <see cref="ElevationPhi"/>, the vector the way the light travels.</summary>
        ElevationPhiFalling,

        /// <summary>Phi from straight down - the game's plus z - theta round it from x; the vector points at the sun.</summary>
        /// <remarks>Appended, so the readings before it keep the numbers earlier captures name them by.</remarks>
        PolarPhiDown,

        /// <summary>As <see cref="PolarPhiDown"/>, the vector the way the light travels.</summary>
        PolarPhiDownFalling,
    }

    /// <summary>The reading whose vector is this one's negative - the same angles read the other way round.</summary>
    public static SunReading Twin(SunReading reading) => reading switch
    {
        SunReading.PolarPhiDown => SunReading.PolarPhiDownFalling,
        SunReading.PolarPhiDownFalling => SunReading.PolarPhiDown,
        < SunReading.PolarThetaFalling => reading + 4,
        _ => reading - 4,
    };

    /// <summary>How vert_angle tips env_map_rotation - candidate readings; the turn by hor_angle is the game's own, see <see cref="CubeTurnFrom"/>.</summary>
    public enum CubeReading
    {
        /// <summary>The turn alone, vert_angle left out - what the game holds where vert_angle is nought.</summary>
        Round,

        /// <summary>Turned, then tipped about the cube's x by vert_angle.</summary>
        RoundThenTipX,

        /// <summary>Tipped about the cube's x by vert_angle, then turned.</summary>
        TipXThenRound,

        /// <summary>Turned, then tipped about the cube's z by vert_angle.</summary>
        RoundThenTipZ,

        /// <summary>Tipped about the cube's z by vert_angle, then turned.</summary>
        TipZThenRound,
    }

    /// <summary>
    /// How the area's ground runs on the game's screen: where one unit along x and one along y land, as right and up.
    /// </summary>
    /// <remarks>
    /// FOR SAYING A DIRECTION THE WAY A PLAYER SEES IT - "the shadows fall to the lower right" can be
    /// held against the game at a glance, where a vector cannot. The live camera's matrix gives it
    /// exactly (EntityOverlay projects the player and a step from it); without a game the map's own
    /// transform stands in (<see cref="Map"/>), which turns the ground the same way and tilts it by
    /// the map's angle rather than the camera's.
    /// </remarks>
    /// <param name="X">Where a step along the area's x lands on the screen: right, up.</param>
    /// <param name="Y">Where a step along y lands.</param>
    public readonly record struct GroundOnScreen(Vector2 X, Vector2 Y)
    {
        /// <summary>
        /// The in-game map's transform - MapView.Project: a step (dx, dy) lands at ((dx - dy) cos, -(dx + dy) sin) with y down, so x runs up-right and y up-left.
        /// </summary>
        public static GroundOnScreen Map { get; } = new(
            new Vector2(MathF.Cos((float)Ui.MapView.CameraAngle), MathF.Sin((float)Ui.MapView.CameraAngle)),
            new Vector2(-MathF.Cos((float)Ui.MapView.CameraAngle), MathF.Sin((float)Ui.MapView.CameraAngle)));

        /// <summary>Whether the two steps span the screen - a projection that flattened the ground does not.</summary>
        public bool Ready => MathF.Abs(Cross) > 1e-6f;

        private float Cross => (X.X * Y.Y) - (X.Y * Y.X);

        /// <summary>The way a direction runs on the screen, right and up, unit length - nought for one straight up or down.</summary>
        public Vector2 Way(Vector3 direction)
        {
            Vector2 way = (direction.X * X) + (direction.Y * Y);
            return way.LengthSquared() > 1e-12f ? Vector2.Normalize(way) : Vector2.Zero;
        }

        /// <summary>The ground direction that runs the given way on the screen, unit length - the inverse of <see cref="Way"/> on the ground.</summary>
        public Vector2 Ground(Vector2 screen)
        {
            float cross = Cross;
            if (MathF.Abs(cross) <= 1e-6f)
            {
                return Vector2.UnitX;
            }

            var ground = new Vector2(((screen.X * Y.Y) - (screen.Y * Y.X)) / cross, ((X.X * screen.Y) - (X.Y * screen.X)) / cross);
            return ground.LengthSquared() > 1e-12f ? Vector2.Normalize(ground) : Vector2.UnitX;
        }

        /// <summary>A screen way as a bearing: nought up, ninety right, clockwise - in degrees, nought to 360.</summary>
        public static float Bearing(Vector2 way)
        {
            float degrees = MathF.Atan2(way.X, way.Y) * 180f / MathF.PI;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        /// <summary>The screen way of a bearing - the inverse of <see cref="Bearing"/>.</summary>
        public static Vector2 OfBearing(float degrees)
        {
            float radians = degrees * MathF.PI / 180f;
            return new Vector2(MathF.Sin(radians), MathF.Cos(radians));
        }
    }

    /// <summary>The player's light: where, what colour (its colour times its intensity), how far.</summary>
    public readonly record struct PlayerLamp(Vector3 Position, Vector3 Colour, float Radius);

    /// <summary>How many point lights are drawn, the player's included.</summary>
    public int PointCount { get; }

    /// <summary>The point lights as <see cref="Shade"/> works with them, the player's included - for a drawing that lights elsewhere, the graphics card's.</summary>
    public ReadOnlySpan<Point> Points => _lights;

    /// <summary>The grid <see cref="Shade"/> looks the point lights up by: where it starts, how wide a cell is, and how many cells each way.</summary>
    public LightCells Cells => new(_gridLeast, _cellSize, _cellsX, _cellsY, _cellsZ);

    /// <summary>Where each cell's lights start in <see cref="CellEntries"/>, one more than there are cells - x fastest, then y, then z.</summary>
    public ReadOnlySpan<int> CellStarts => _cellStarts;

    /// <summary>Every cell's lights, as indices into <see cref="Points"/>, each cell's in the order <see cref="Shade"/> adds them.</summary>
    public ReadOnlySpan<int> CellEntries => _cellEntries;

    /// <summary>The sun's light, its colour times its multiplier - zero for none.</summary>
    public Vector3 SunColour { get; init; }

    /// <summary>The way the sun's light travels, unit length, in model space.</summary>
    public Vector3 SunDirection { get; init; } = new(0f, 0f, 1f);

    /// <summary>Whether the sun casts shadows - <see cref="ShadowMap"/>.</summary>
    public bool SunShadows { get; init; }

    /// <summary>Where the ambient comes from.</summary>
    public SceneAmbient Ambient { get; init; } = SceneAmbient.Flat;

    /// <summary>The flat ambient's light, linear - the picture's usual one unless the caller says otherwise.</summary>
    public float FlatAmbient { get; init; } = UsualFlatAmbient;

    /// <summary>The picture's own ambient as light: 0.22 on the sRGB value, which is what an unlit picture shades with.</summary>
    public static float UsualFlatAmbient { get; } = MathF.Pow((0.22f + 0.055f) / 1.055f, 2.4f);

    /// <summary>The diffuse cube, where <see cref="Ambient"/> is <see cref="SceneAmbient.Cube"/>.</summary>
    public CubeMap? Cube { get; init; }

    /// <summary>
    /// The environment's specular cube, read along a reflection - the water's (see <see cref="WaterReflection"/>); the diffuse cube stands in where it is null.
    /// </summary>
    public CubeMap? Mirror { get; init; }

    /// <summary>
    /// Whether the water's waves glint: every light's GGX lobe worked out with the normal its program hands over (ShadeProgram.HasWaves).
    /// </summary>
    public bool WaterGlints { get; init; }

    /// <summary>
    /// Whether the water's waves reflect the environment: <see cref="Mirror"/> read along the view turned about the waves' normal, as the surround is lit, and weighed by the split sum's Fresnel.
    /// </summary>
    /// <remarks>
    /// THE ENGINE'S WATER SPECULAR IS ITS OWN SURFACE'S: InitWaterSurfaceData sets the reflected direction
    /// from the waves' normal, the specular colour to the area's reflectiveness and the glossiness to
    /// 0.99, and ComputeEnvironmentLighting reads the environment that way. Every other glossy material
    /// here still takes the surround's average (see <see cref="Shade(Vector3, Vector3, Vector3, Vector3, float, bool, Vector3, float)"/>).
    /// </remarks>
    public bool WaterReflection { get; init; }

    /// <summary>
    /// Whether the waves' reflection takes in the picture itself where its ray meets it - the engine's screen-space reflection, see <see cref="ScreenReflection"/> - and not only the environment's cube. Only with <see cref="WaterReflection"/>.
    /// </summary>
    public bool WaterScene { get; init; }

    /// <summary>
    /// Whether the water bends what lies behind it - the engine's refraction, the card's alone (ModelShaders' Refraction), on in the light panel by
    /// default (DECIDED from the live client); off, what lies behind is seen straight through as before.
    /// </summary>
    /// <remarks>
    /// AS WRITTEN (water.ffx's GetRefractedOpaqueSurface, GetRefractedDir, GetRaytracedLightAdaptiveStepping, ComputeDispersedColor and
    /// GetRefractedWaterLight): the view bent into the water by the area's refraction index (<see cref="WaterLook.RefractionIndex"/>) thinned to
    /// one by the shore's <c>water_to_terrain_ratio</c>; marched <see cref="RefractionSteps"/> times over the solid depth, each step taking the
    /// solid surface where a point along the bent ray lands on the picture and going on as far along the ray as it lies; the picture's solid
    /// part read where the march ended, green and red shifted towards where it started (<see cref="WaterDispersion"/>); and what gets through
    /// worked out over the bent ray's own length, <c>exp(-travel * density * (1 - ratio))</c>, rather than over the straight view's depth. The
    /// colour behind is mixed in as the layer mixed it, in the picture's own numbers; its caustics by <see cref="WaterCaustics"/>. The
    /// processor's picture draws the water as before, and the line under it says so.
    /// </remarks>
    public bool WaterRefraction { get; init; }

    /// <summary>
    /// Whether the light the water bends is patterned by the engine's caustics - the card's alone, with <see cref="WaterRefraction"/>; on in the
    /// light panel by default (DECIDED from the live client once their strength was measured).
    /// </summary>
    /// <remarks>
    /// AS WRITTEN (water.ffx's GetCausticsBias and GetCausticsIntensity, in GetRefractedOpaqueSurface): the light where the bent ray ended
    /// times <c>1 + bias * caustics_mult * (1 - ratio)</c>, the bias the caustics' picture (ShadeProgram.Caustics) read at the end's place
    /// times 0.002, moved by the distortion read at times 0.001 drifting with the clock, as <c>pow(g, 1 / 4.2) * 2 - 1</c>, times
    /// <c>saturate(depth * 0.02 - 0.3) * 0.7</c> over the depth the ray fell. The area's <see cref="WaterLook.CausticsMult"/> - Port's the
    /// engine's 1, MEASURED. READINGS, said under the picture: the ground water ratio stands at nought, as everywhere else it is not taken,
    /// so the deep water's intensity and frequency are drawn throughout; the multiplication is made on the solid part's light as the picture
    /// holds it, after its exposure and grade, where the engine's is before; and the pattern's phase follows the picture's frame, a room's or
    /// a stage's, not the area's world.
    /// </remarks>
    public bool WaterCaustics { get; init; }

    /// <summary>
    /// How far the water's surface is lifted by its waves, as a share of the height the engine's octaves give it - nought for a flat surface.
    /// </summary>
    /// <remarks>
    /// NOT THE ENGINE'S, and said so under the picture: its ocean planes are flat meshes, and the waves
    /// raise the surface only per pixel (GetTotalWaveSurfaceNumerical meets the view with the plane lifted by
    /// the height there); only the beach, crashing and flood waves move their vertices (BreakingWavesVertex and
    /// its kin). The game's camera never looks along the water, the book's does, so here a water shape's
    /// vertices are lifted by <see cref="ShadeProgram.Ocean"/> times this - one being the engine's own
    /// height - on a mesh fine enough to carry them (SkinnedMesh.Refined).
    /// </remarks>
    public float WaterLift { get; init; }

    /// <summary>
    /// Whether the engine's field waves are drawn on the water - its breakers every five seconds (ShadeProgram.Field); the card's alone.
    /// </summary>
    /// <remarks>
    /// THEIR CREST LIFTS THE WATER AT THE ENGINE'S OWN HEIGHT, whatever <see cref="WaterLift"/> says: CrashingWavesVertex moves its
    /// vertices, as the beach waves' do. The processor's picture draws none, and the line under it says so.
    /// </remarks>
    public bool WaterCrash { get; init; }

    /// <summary>
    /// Whether the waves' way in runs round what stands in the water - the hull, posts, rocks - as v0.1.295 to v0.1.306 drew it, rather
    /// than by the ground alone (ShoreField.Way).
    /// </summary>
    public bool WaterWayRound { get; init; }

    /// <summary>
    /// Whether the waves' way in runs past the walls - a quay's or a pier's own ground in no wave's way, as Port's pier stands in the game's
    /// (ShoreField.Way, a video measured from the live client, v0.1.332). CHOSEN, a switch to hold against the game; it wins over
    /// <see cref="WaterWayRound"/>.
    /// </summary>
    public bool WaterPastWalls { get; init; }

    /// <summary>
    /// Whether the field waves' break phase takes the shore's distance of the ground alone - what stands in the sea, the rocks and the
    /// pier's stones, no shore for it (ShoreField.GroundTexels) - rather than the shore's the swell takes. CHOSEN, a switch to hold against
    /// the game; the card's alone, as the field waves are.
    /// </summary>
    public bool WaterBreakByGround { get; init; }

    /// <summary>
    /// Whether the engine's wetness pass is drawn: the water and the beach waves cast their wetness onto the screen and what reads it -
    /// Port's quay, rocks, crates and ship (ShadeProgram.ReadsWetness) - is darkened where wet, drying over a second after the water
    /// left (CHOSEN). The card's alone, as the field waves are.
    /// </summary>
    public bool WaterWetness { get; init; }

    /// <summary>
    /// Whether a material's spray reads the stone's own occlusion where a graph before it set the occlusion to one (ShadeProgram.Occluded:
    /// Port's quay, whose moss blend does) - CHOSEN, a switch held against the game and on in the light panel by default from v0.1.340,
    /// DECIDED from the live client once its video matched the game's runoff; both drawings follow it, the picture's programs swapped
    /// for their twins.
    /// </summary>
    public bool SprayOccluded { get; init; }

    /// <summary>env_map_rotation: what a normal is put through before the cube is read - the world's axes as the cube's unless the environment turns it, see <see cref="CubeTurnFrom"/>.</summary>
    public Matrix4x4 CubeTurn { get; init; } = CubeSwap;

    /// <summary>cube_brightness.x - env_brightness.</summary>
    public float CubeBrightness { get; init; } = 1f;

    /// <summary>direct_light_env_ratio: how much the cube is brightened by the light that falls here.</summary>
    public float DirectLightEnvRatio { get; init; }

    /// <summary>gi_env_occlusion: the share of the cube's light the game's GI replaces.</summary>
    public float GiEnvOcclusion { get; init; }

    /// <summary>The colour is multiplied by this before it is shown - max(1, camera.exposure); one for none.</summary>
    public float Exposure { get; init; } = 1f;

    /// <summary>The environment's colour grade, applied after the exposure - see <see cref="ColourGrade"/>; null for none.</summary>
    public ColourGrade? Grade { get; init; }

    /// <summary>What the picture shows - lit, or a material channel as the engine's debug views show it; see <see cref="PictureView"/>.</summary>
    public PictureView View { get; init; }

    /// <summary>Whether <see cref="View"/> shows a channel in place of the lit colour - every view but the picture and the lighting alone.</summary>
    public bool Channelled => View is not (PictureView.Picture or PictureView.LightingOnly);

    /// <summary>The grey debug_lighting_only lights in place of an albedo (an additive material's 0.01 is not told apart here).</summary>
    public const float LightingGrey = 0.25f;

    /// <summary>
    /// How many texels the sun's shadow map is across - <see cref="ShadowMap.Usual"/>, or <see cref="ShadowMap.Coarse"/> while the sun is being moved.
    /// </summary>
    public int ShadowSide { get; init; } = ShadowMap.Usual;

    /// <summary>
    /// The reading the game uses: phi measured from straight down - the game's plus z - theta its bearing round from x, and the vector they make points at the sun.
    /// </summary>
    /// <remarks>
    /// THE LIGHT IS THE SHADOW BOX'S THIRD AXIS (v0.1.368, MEASURED). Necropolis's memory (cap1's
    /// light-hunt, phi 2.07694 theta 2.70526) holds three pairs of planes sixteen bytes apart, each pair a
    /// normal and its negative with a distance, and the eight corners they bound just before them: A =
    /// (0.42262, 0.90631, 0), B = (-0.43939, 0.20489, 0.87462) and C = (0.79268, -0.36963, 0.48481), C = A x B.
    /// C is <c>-(sin phi cos theta, sin phi sin theta, cos phi)</c> to five digits; B is its derivative by
    /// phi and A is level - a light's look-at frame with up the world's. The game's pictures say which is
    /// the light, every one C: the shadows' bearing in four areas through each capture's own camera (The
    /// Red Vale's trunks at 239 degrees against C's 233 and B's 53, Port's skeleton at 293 against 289 and
    /// 109, Crimson Shores' rocks, Necropolis's statue on the wall beside it), the length of Port's
    /// skeleton's shadow (about 320 pixels, C 317 to 323, B 167), and the sun's glint on the water.
    ///
    /// SO THE SUN STANDS phi - 90 DEGREES HIGH: The Red Vale 30, Port 35, Crimson Shores 38, Necropolis 29,
    /// Azmerian Ranges 44. Until v0.1.367 the picture drew B (<see cref="SunReading.ElevationPhi"/>), found
    /// first in Azmerian Ranges and taken for the light because it was the only one of the eight readings
    /// there - C was none of them - and its sign settled by the sun standing above the ground. B threw the
    /// shadows the opposite way and stood the sun at 180 - phi: 60 degrees in The Red Vale, its shadows a
    /// third of the game's length and its sunlight on flat ground 1.73 times the game's. It went unseen in
    /// Port's built stage, which the game's instances laid turned 180 degrees, so the reversal cancelled -
    /// though not in Port's laid stage, whose shadows fell the other way too.
    /// </remarks>
    public const SunReading GameSun = SunReading.PolarPhiDown;

    /// <summary>The way a sun with these angles shines, as the reading takes them - unit length, the way the light travels.</summary>
    public static Vector3 SunFrom(float phi, float theta, SunReading reading)
    {
        // THE GAME'S UP IS MINUS Z, so "up" in a reading is -z; x and y are the area's.
        (float round, float tilt, bool polar) = reading switch
        {
            SunReading.PolarTheta or SunReading.PolarThetaFalling => (phi, theta, true),
            SunReading.ElevationTheta or SunReading.ElevationThetaFalling => (phi, theta, false),
            SunReading.PolarPhi or SunReading.PolarPhiFalling or SunReading.PolarPhiDown or SunReading.PolarPhiDownFalling => (theta, phi, true),
            _ => (theta, phi, false),
        };

        float across = polar ? MathF.Sin(tilt) : MathF.Cos(tilt);
        float up = polar ? MathF.Cos(tilt) : MathF.Sin(tilt);
        bool fromDown = reading is SunReading.PolarPhiDown or SunReading.PolarPhiDownFalling;
        var pointing = new Vector3(across * MathF.Cos(round), across * MathF.Sin(round), fromDown ? up : -up);
        bool falling = reading is SunReading.PolarThetaFalling or SunReading.ElevationThetaFalling
            or SunReading.PolarPhiFalling or SunReading.ElevationPhiFalling or SunReading.PolarPhiDownFalling;

        // POINTING AT THE SUN, the light travels the other way.
        Vector3 travels = falling ? pointing : -pointing;
        return travels.LengthSquared() > 0f ? Vector3.Normalize(travels) : new Vector3(0f, 0f, 1f);
    }

    /// <summary>The way the light travels from a sun standing round the area's x axis by <paramref name="round"/> and above the ground by <paramref name="height"/>, both in radians.</summary>
    /// <remarks>Up is minus z, the game's own - so a sun above the ground shines with plus z.</remarks>
    public static Vector3 SunToward(float round, float height)
    {
        float across = MathF.Cos(height);
        return Vector3.Normalize(new Vector3(-across * MathF.Cos(round), -across * MathF.Sin(round), MathF.Sin(height)));
    }

    /// <summary>Where a sun stands for the way its light travels: round the area's x axis and above the ground, in radians - the inverse of <see cref="SunToward"/>.</summary>
    public static (float Round, float Height) SunStands(Vector3 travels)
    {
        float height = MathF.Asin(Math.Clamp(travels.Z, -1f, 1f));
        float round = travels.X == 0f && travels.Y == 0f ? 0f : MathF.Atan2(-travels.Y, -travels.X);
        return (round, height);
    }

    /// <summary>
    /// The world's axes as the cube's: x stays, the world's y is the cube's z, and the world's up - minus z - is the cube's up, plus y.
    /// </summary>
    public static Matrix4x4 CubeSwap { get; } = new(
        1f, 0f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, -1f, 0f, 0f,
        0f, 0f, 0f, 1f);

    /// <summary>
    /// env_map_rotation: the world's axes swapped to the cube's, turned about the cube's up by minus hor_angle, and tipped by vert_angle as the reading takes it.
    /// </summary>
    /// <remarks>
    /// FOUND IN THE GAME'S MEMORY (LightHunt, AzmerianRanges, hor_angle 0.19198, vert_angle absent):
    /// none of the first candidates - turns about the world's own axes - was there, as written or
    /// transposed, but among every arrangement of the same turn one stood out, 37 copies, nearly all at
    /// offset 0x80 of a 256-byte block - the stride Direct3D 12 lays constant buffers out in. Its rows
    /// are (cos h, 0, sin h), (-sin h, 0, cos h), (0, -1, 0): <see cref="CubeSwap"/> times a turn about
    /// y by minus h. Read as the shaders read it - mul(float4(dir, 0), env_map_rotation), the row vector
    /// System.Numerics multiplies too - the world's up becomes the cube's plus y and the ground turns
    /// about it by hor_angle, which is the only reading of it that makes "hor" horizontal. So the cube
    /// is authored y-up, and before this its sky was read off its plus and minus z faces.
    ///
    /// HOW vert_angle TIPS IT was not seen - AzmerianRanges has none - so <see cref="CubeReading"/> offers
    /// the tips about the cube's x and z either side of the turn until a hunt where it is set says.
    /// </remarks>
    public static Matrix4x4 CubeTurnFrom(float horizontal, float vertical, CubeReading reading)
    {
        Matrix4x4 round = Matrix4x4.CreateRotationY(-horizontal);
        return CubeSwap * (reading switch
        {
            CubeReading.RoundThenTipX => round * Matrix4x4.CreateRotationX(vertical),
            CubeReading.TipXThenRound => Matrix4x4.CreateRotationX(vertical) * round,
            CubeReading.RoundThenTipZ => round * Matrix4x4.CreateRotationZ(vertical),
            CubeReading.TipZThenRound => Matrix4x4.CreateRotationZ(vertical) * round,
            _ => round,
        });
    }

    /// <summary>
    /// One pixel lit: its albedo under every light, the ambient, the specular light of a glossy material, exposed - linear.
    /// </summary>
    /// <param name="albedo">The surface's colour, linear.</param>
    /// <param name="place">Where the pixel is, model space.</param>
    /// <param name="normal">Its normal, unit length, turned to face the eye.</param>
    /// <param name="toEye">The way to the eye, unit length.</param>
    /// <param name="sunShadow">How much of the sun reaches it, nought to one.</param>
    /// <param name="glossy">Whether the material's specular light is worked out - its program has a gloss.</param>
    /// <param name="specular">The material's specular colour, linear.</param>
    /// <param name="gloss">Its gloss.</param>
    public Vector3 Shade(Vector3 albedo, Vector3 place, Vector3 normal, Vector3 toEye, float sunShadow, bool glossy, Vector3 specular, float gloss)
        => Shade(albedo, place, normal, toEye, sunShadow, glossy, specular, gloss, default);

    /// <summary>
    /// One pixel lit, its specular light worked out with a normal of its program's own where it hands one - the water's waves.
    /// </summary>
    /// <param name="albedo">The surface's colour, linear.</param>
    /// <param name="place">Where the pixel is, model space.</param>
    /// <param name="normal">Its normal, unit length, turned to face the eye - what the diffuse light and the surround are worked out with.</param>
    /// <param name="toEye">The way to the eye, unit length.</param>
    /// <param name="sunShadow">How much of the sun reaches it, nought to one.</param>
    /// <param name="glossy">Whether the material's specular light is worked out - its program has a gloss.</param>
    /// <param name="specular">The material's specular colour, linear.</param>
    /// <param name="gloss">Its gloss.</param>
    /// <param name="waves">The normal its program hands the specular light, unit length, or nought for none - then the light is <see cref="WaterGlints"/>' and <see cref="WaterReflection"/>'s to give.</param>
    /// <param name="environment">The environment's share of the waves' reflection - one, less where the picture itself is reflected (<see cref="ScreenReflection.EnvironmentShare"/>).</param>
    public Vector3 Shade(
        Vector3 albedo, Vector3 place, Vector3 normal, Vector3 toEye, float sunShadow, bool glossy, Vector3 specular, float gloss, Vector3 waves,
        float environment = 1f)
    {
        // A CHANNEL IN PLACE OF THE LIGHT, neither exposed nor graded: the engine's debug views write it straight to the pixel's colour.
        if (Channelled)
        {
            return Channel(View, albedo, normal, specular, gloss);
        }

        if (View == PictureView.LightingOnly)
        {
            albedo = new Vector3(LightingGrey);
        }

        Vector3 diffuse = Vector3.Zero;
        Vector3 shine = Vector3.Zero;
        Vector3 total = Vector3.Zero;
        float facing = MathF.Max(Vector3.Dot(normal, toEye), 1e-4f);

        // THE WAVES' OWN SPECULAR: the lobes with their normal where the glints are asked for, none of the
        // mesh's normal's, and the environment along their reflection where that is.
        bool waved = waves != Vector3.Zero;
        bool glints = waved && glossy && WaterGlints;
        float wavesFacing = waved ? MathF.Max(Vector3.Dot(waves, toEye), 1e-4f) : facing;
        bool lobes = glossy && !waved;

        if (SunColour != Vector3.Zero && sunShadow > 0f)
        {
            Vector3 light = SunColour * sunShadow;
            Vector3 towards = -SunDirection;
            Lit(light, towards, normal, toEye, facing, lobes, specular, gloss, ref diffuse, ref shine);
            if (glints)
            {
                Glinted(light, towards, waves, toEye, wavesFacing, specular, gloss, ref shine);
            }

            total += light;
        }

        if (_lights.Length > 0)
        {
            int cell = CellIndex(place);
            if (cell >= 0)
            {
                for (int at = _cellStarts[cell], upto = _cellStarts[cell + 1]; at < upto; at++)
                {
                    ref readonly Point one = ref _lights[_cellEntries[at]];
                    Vector3 offset = one.Position - place;
                    float squared = offset.LengthSquared();
                    if (squared >= one.Cutoff * one.Cutoff)
                    {
                        continue;
                    }

                    float distance = MathF.Sqrt(squared);
                    float ratio = distance / one.Cutoff;
                    float fade = MathF.Max(0f, 1f - (ratio * ratio * ratio * ratio));
                    float divisor = (distance / one.Core) + 1f;
                    float attenuation = MathF.Min(10f, one.Zero * fade * fade / (divisor * divisor));
                    if (!(attenuation > 0f))
                    {
                        continue;
                    }

                    Vector3 light = one.Colour * attenuation;
                    Vector3 towards = distance > 1e-4f ? offset / distance : normal;
                    Lit(light, towards, normal, toEye, facing, lobes, specular, gloss, ref diffuse, ref shine);
                    if (glints)
                    {
                        Glinted(light, towards, waves, toEye, wavesFacing, specular, gloss, ref shine);
                    }

                    total += light;
                }
            }
        }

        Vector3 colour = diffuse * albedo;
        Vector3 surround = Vector3.Zero;
        switch (Ambient)
        {
            case SceneAmbient.Cube when Cube is not null:
                // THE SHARE GI OWNS IS THE FLAT AMBIENT - the game's GI is not drawn; see the remarks.
                surround = (Cube.Sample(Vector3.TransformNormal(normal, CubeTurn))
                    * (new Vector3(CubeBrightness) + (total * DirectLightEnvRatio)) * (1f - GiEnvOcclusion))
                    + new Vector3(FlatAmbient * GiEnvOcclusion);
                break;
            case SceneAmbient.Flat:
            case SceneAmbient.Cube:
                surround = new Vector3(FlatAmbient);
                break;
        }

        colour += surround * albedo;

        // THE ENVIRONMENT'S SPECULAR, as the unlit picture works it out - a uniform environment as
        // bright as the ambient - since the specular cube is not read yet.
        if (lobes)
        {
            GlossLight.Environment(facing, gloss, out float bias, out float scale);
            float level = (surround.X + surround.Y + surround.Z) / 3f;
            shine += new Vector3(level * bias) + (level * scale * specular);
        }

        // THE WAVES' REFLECTION: the specular cube along the view turned about their normal, lit as the
        // surround is, weighed by the same split sum.
        if (waved && glossy && WaterReflection)
        {
            GlossLight.Environment(wavesFacing, gloss, out float bias, out float scale);
            Vector3 reflected = (2f * Vector3.Dot(waves, toEye) * waves) - toEye;
            Vector3 seen = Reflected(reflected, total) * environment;
            shine += (seen * bias) + (seen * scale * specular);
        }

        Vector3 exposed = (colour + shine) * Exposure;
        return Grade is null ? exposed : Grade.Apply(exposed);
    }

    /// <summary>The views as the game's program names them, in <see cref="PictureView"/>'s order - the light panel's list.</summary>
    public static IReadOnlyList<string> ViewNames { get; } =
        ["Final Color", "Albedo", "Specular", "Gloss", "Roughness", "World Normal (Fit)", "World Normal (True)", "Lighting Only"];

    /// <summary>
    /// A channel as the engine's debug view writes it to the pixel - linear, neither exposed nor graded; nought for the picture and the lighting.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <param name="albedo">The surface's colour, linear.</param>
    /// <param name="normal">The normal the picture is lit with, unit length.</param>
    /// <param name="specular">The material's specular colour, linear.</param>
    /// <param name="gloss">Its gloss.</param>
    public static Vector3 Channel(PictureView view, Vector3 albedo, Vector3 normal, Vector3 specular, float gloss) => view switch
    {
        PictureView.Albedo => albedo,
        PictureView.Specular => specular,
        PictureView.Gloss => new Vector3(ToLinear(Math.Clamp(gloss, 0f, 1f))),
        PictureView.Roughness => new Vector3(ToLinear(1f - Math.Clamp(gloss, 0f, 1f))),
        PictureView.NormalFit => ToLinear((normal + Vector3.One) * 0.5f),
        PictureView.NormalTrue => ToLinear(normal),
        _ => Vector3.Zero,
    };

    /// <summary>RGBToSRGB, which despite its name takes a gamma of 2.2 OFF: <c>pow(saturate(x), 2.2)</c> - so the shown sRGB value is about x.</summary>
    private static float ToLinear(float value) => MathF.Pow(Math.Clamp(value, 0f, 1f), 2.2f);

    /// <inheritdoc cref="ToLinear(float)"/>
    private static Vector3 ToLinear(Vector3 value) => new(ToLinear(value.X), ToLinear(value.Y), ToLinear(value.Z));

    /// <summary>The medium's scattering colour the engine pulls the water's colour towards on a crest - GetWaterMediumColor's <c>mult_color</c>.</summary>
    public static readonly Vector3 WaterScatterColour = new(0.3f, 0.9f, 0.5f);

    /// <summary>How the water's own colour takes the sun - ComputeDiffuse's subsurface wrap: InitWaterSurfaceData's <c>subsurface_translucency</c>.</summary>
    public const float WaterTranslucency = 3f;

    /// <summary>The most a channel of the water's specular light reaches - ClampSpecularLight's <c>max_val</c>.</summary>
    public const float WaterSpecularCap = 10f;

    /// <summary>
    /// How far the refraction splits the colours - GetRefractedOpaqueSurface's <c>water_dispersion</c>, a constant there: ComputeDispersedColor reads
    /// green one shift and red two from where the bent ray ends towards where it started, a shift this share of the way.
    /// </summary>
    public const float WaterDispersion = 0.2f;

    /// <summary>The most one dispersion shift reaches, a share of the picture - ComputeDispersedColor's <c>min(l, 0.003)</c>.</summary>
    public const float WaterDispersionMost = 0.003f;

    /// <summary>
    /// How many steps the refraction's ray is marched - GetRaytracedLightAdaptiveStepping's <c>3 + DETAIL_LEVEL / 2</c>, DETAIL_LEVEL being nought
    /// (ocean_data's <c>#define</c>).
    /// </summary>
    public const int RefractionSteps = 3;

    /// <summary>
    /// One pixel of open water lit as the engine's water pass lights it - BuildWaterRefraction - as a layer: its colour, linear, exposed
    /// and graded, and how much it covers what lies behind; a cover of nought or less drops the pixel.
    /// </summary>
    /// <param name="water">The parts the program handed over - see ShadeProgram.WaterOut.</param>
    /// <param name="medium">The area's water colour, linear (<c>pow(open_water_color, 2.2)</c>), times the colour's pattern.</param>
    /// <param name="scattering">The area's <c>water_subsurface_scattering</c>.</param>
    /// <param name="place">Where the pixel is, model space.</param>
    /// <param name="waves">The waves' normal there, unit length.</param>
    /// <param name="toEye">The way to the eye, unit length.</param>
    /// <param name="sunShadow">How much of the sun reaches it.</param>
    /// <param name="specular">The surface's specular colour - the area's reflectiveness lerped to the foam's 0.04.</param>
    /// <param name="gloss">Its gloss - the engine's 0.99 lerped to nought by the foam.</param>
    /// <param name="environment">The environment's share of the reflection - less where the picture itself is reflected.</param>
    /// <param name="mirrored">The picture's own reflection, already lit, exposed and graded - ScreenReflection.</param>
    /// <param name="cover">How much of what lies behind the layer covers.</param>
    /// <param name="terms">Every part worked out on the way, for the probe.</param>
    /// <remarks>
    /// AS WRITTEN (water.ffx's GetWaterMediumColor, InitWaterSurfaceData, ApplyWaterSurfaceLighting, BuildWaterRefraction and
    /// GetRefractedWaterLight; lighting.ffx's ComputeDiffuse, ComputeLight and AddDiffuseEnvironmentLight), asked for from the live
    /// client after the water looked nearer the game under the flat ambient than under the cube (v0.1.295):
    /// <list type="bullet">
    /// <item>THE WATER'S ALBEDO IS BLACK; its colour is light it lets through. The medium's diffuse colour is nought and its colour
    /// is the subsurface colour, <c>pow(colour / (0.3, 0.9, 0.5), 1 / (1 + max(-0.9, 250 * height * 5e-5 * scattering))) * (0.3, 0.9, 0.5)</c> -
    /// a crest's colour lifted towards that green, a trough's darkened - less the foam's share; the foam is the albedo.</item>
    /// <item>EVERY LIGHT ADDS <c>saturate(N.L)</c> of itself on the foam and <c>saturate((N.L + 3) / 4)</c> on the medium (front scattering
    /// one), N the waves' normal - not the mesh's - for all of it; the sun through its shadow.</item>
    /// <item>THE AMBIENT is the diffuse cube along the waves' normal times the cube's brightness on the foam and the medium together, less
    /// the GI's share; the GI's own light falls on the albedo alone (<c>indirect_color.rgb * albedo</c>), so the flat ambient stands in for it
    /// on the foam and on nothing else.</item>
    /// <item>THE SPECULAR - the lights' lobes and the environment along the waves' reflection - is capped at ten a channel and thinned by
    /// <c>1 - ratio^3</c> towards the floor, the shore's <c>water_to_terrain_ratio</c>; nothing else weakens it, the water's depth least of all.</item>
    /// <item>WHAT LIES BEHIND AND THE MEDIUM'S LIGHT ARE MIXED by what gets through, then taken times <c>1 - reflectivity</c>,
    /// <c>reflectivity = lerp(Fresnel(N.V), 1, specular) * (1 - ratio)</c>, the ratio lifting it back to one; the foam hides both by
    /// <c>1 - (1 - foam)^3</c>.</item>
    /// </list>
    /// So the pixel is <c>specular + absorbed * k + behind * d</c>; as a layer over what lies behind its cover is <c>1 - d</c> and its
    /// colour the rest over the cover - exposed and graded there, as every layer is before it is mixed.
    /// NOT DRAWN HERE: the refraction's bend and its caustics (the card's alone, <see cref="WaterRefraction"/> and <see cref="WaterCaustics"/>), the terrain water colour (the medium's colour over ground under water), the
    /// tidal state's own water_to_terrain_ratio and the ground water ratio's thirtyfold - both of them read off the shore field, whose
    /// scale is CHOSEN.
    /// </remarks>
    public Vector3 ShadeWater(
        ShadeProgram.WaterOut water, Vector3 medium, float scattering, Vector3 place, Vector3 waves, Vector3 toEye, float sunShadow,
        Vector3 specular, float gloss, float environment, Vector3 mirrored, out float cover, out WaterLight terms)
    {
        float terrain = Math.Clamp(water.Terrain, 0f, 1f);
        if (terrain > 0.999999f)
        {
            cover = 0f;
            terms = default;
            return Vector3.Zero;
        }

        Vector3 normal = waves.LengthSquared() > 1e-12f ? Vector3.Normalize(waves) : toEye;
        if (Vector3.Dot(normal, toEye) < 0f)
        {
            normal = -normal;
        }

        // THE MEDIUM: black albedo, its colour scattered by the waves' height - GetWaterMediumColor.
        float share = Math.Clamp(water.Share, 0f, 1f);
        float exponent = 1f / (1f + MathF.Max(-0.9f, 250f * water.Height * 5e-5f * scattering));
        Vector3 scatter = WaterScatterColour;
        Vector3 subsurface = new Vector3(
            MathF.Pow(MathF.Max(medium.X, 0f) / scatter.X, exponent),
            MathF.Pow(MathF.Max(medium.Y, 0f) / scatter.Y, exponent),
            MathF.Pow(MathF.Max(medium.Z, 0f) / scatter.Z, exponent)) * scatter * (1f - share);
        Vector3 albedo = Vector3.Max(water.Foam, Vector3.Zero);

        float facing = MathF.Max(Vector3.Dot(normal, toEye), 1e-4f);
        bool glints = WaterGlints;
        Vector3 diffuse = Vector3.Zero, through = Vector3.Zero, shine = Vector3.Zero, total = Vector3.Zero;
        if (SunColour != Vector3.Zero && sunShadow > 0f)
        {
            Vector3 light = SunColour * sunShadow;
            Vector3 towards = -SunDirection;
            WaterLit(light, towards, normal, ref diffuse, ref through);
            if (glints)
            {
                Glinted(light, towards, normal, toEye, facing, specular, gloss, ref shine);
            }

            total += light;
        }

        if (_lights.Length > 0)
        {
            int cell = CellIndex(place);
            if (cell >= 0)
            {
                for (int at = _cellStarts[cell], upto = _cellStarts[cell + 1]; at < upto; at++)
                {
                    ref readonly Point one = ref _lights[_cellEntries[at]];
                    Vector3 offset = one.Position - place;
                    float squared = offset.LengthSquared();
                    if (squared >= one.Cutoff * one.Cutoff)
                    {
                        continue;
                    }

                    float distance = MathF.Sqrt(squared);
                    float ratio = distance / one.Cutoff;
                    float fade = MathF.Max(0f, 1f - (ratio * ratio * ratio * ratio));
                    float divisor = (distance / one.Core) + 1f;
                    float attenuation = MathF.Min(10f, one.Zero * fade * fade / (divisor * divisor));
                    if (!(attenuation > 0f))
                    {
                        continue;
                    }

                    Vector3 light = one.Colour * attenuation;
                    Vector3 towards = distance > 1e-4f ? offset / distance : normal;
                    WaterLit(light, towards, normal, ref diffuse, ref through);
                    if (glints)
                    {
                        Glinted(light, towards, normal, toEye, facing, specular, gloss, ref shine);
                    }

                    total += light;
                }
            }
        }

        // THE AMBIENT on the foam and the medium alike, the GI's stand-in on the foam alone.
        Vector3 ambient = Ambient switch
        {
            SceneAmbient.Cube when Cube is not null =>
                (Cube.Sample(Vector3.TransformNormal(normal, CubeTurn)) * (new Vector3(CubeBrightness) + (total * DirectLightEnvRatio))
                    * (1f - GiEnvOcclusion) * (albedo + subsurface))
                + (albedo * (FlatAmbient * GiEnvOcclusion)),
            SceneAmbient.Flat or SceneAmbient.Cube => (albedo + subsurface) * FlatAmbient,
            _ => Vector3.Zero,
        };

        // THE ENVIRONMENT ALONG THE WAVES' REFLECTION - AddSpecularEnvironmentLight: the specular cube times the cube's brightness.
        if (WaterReflection)
        {
            GlossLight.Environment(facing, gloss, out float bias, out float scale);
            Vector3 reflected = (2f * Vector3.Dot(normal, toEye) * normal) - toEye;
            Vector3 seen = WaterSky(reflected, total) * environment;
            shine += (seen * bias) + (seen * scale * specular);
        }

        float brightest = MathF.Max(shine.X, MathF.Max(shine.Y, shine.Z));
        if (brightest > WaterSpecularCap)
        {
            shine *= WaterSpecularCap / brightest;
        }

        float fresnel = GlossLight.Fresnel(Math.Clamp(Vector3.Dot(normal, toEye), 0f, 1f));
        float reflectivity = (fresnel + (specular.X * (1f - fresnel))) * (1f - terrain);
        float refracted = (1f - reflectivity) + (reflectivity * terrain);
        float kept = 1f - (terrain * terrain * terrain);
        Vector3 absorbed = (diffuse * albedo) + ambient + (through * subsurface);
        float clear = 1f - share;
        float hidden = 1f - (clear * clear * clear);
        float passing = Math.Clamp(water.Through, 0f, 1f);
        float mixed = ((1f - hidden) * refracted * (1f - passing)) + hidden;
        float behind = (1f - hidden) * refracted * passing;
        cover = 1f - behind;
        terms = new WaterLight(
            subsurface, exponent, albedo, absorbed, ambient, shine * kept, reflectivity, refracted, passing, terrain, hidden, cover);
        if (!(cover > 1e-6f))
        {
            cover = 0f;
            return Vector3.Zero;
        }

        Vector3 layer = ((shine * kept) + (absorbed * mixed)) * (Exposure / cover);
        Vector3 graded = Grade is null ? layer : Grade.Apply(layer);
        return graded + (mirrored * (kept / cover));
    }

    /// <summary>One light on the water: its diffuse share on the foam, <c>saturate(N.L)</c>, and its share on the medium, <c>saturate((N.L + 3) / 4)</c> - ComputeDiffuse.</summary>
    private static void WaterLit(Vector3 light, Vector3 towards, Vector3 normal, ref Vector3 diffuse, ref Vector3 through)
    {
        float lit = Vector3.Dot(normal, towards);
        diffuse += light * Math.Clamp(lit, 0f, 1f);
        through += light * Math.Clamp((lit + WaterTranslucency) / (1f + WaterTranslucency), 0f, 1f);
    }

    /// <summary>
    /// The environment's light along one way on the water - AddSpecularEnvironmentLight's: the specular cube times <c>cube_brightness.x + total * direct_light_env_ratio</c>,
    /// none of it the GI's; the flat ambient where the ambient is flat.
    /// </summary>
    private Vector3 WaterSky(Vector3 direction, Vector3 total) => Ambient switch
    {
        SceneAmbient.Cube when Cube is not null =>
            (Mirror ?? Cube).Sample(Vector3.TransformNormal(direction, CubeTurn)) * (new Vector3(CubeBrightness) + (total * DirectLightEnvRatio)),
        SceneAmbient.Flat or SceneAmbient.Cube => new Vector3(FlatAmbient),
        _ => Vector3.Zero,
    };

    /// <summary>The environment's light coming from one way, as the surround is lit: the specular cube where there is one, the diffuse cube where not, the flat ambient or nothing as the ambient is.</summary>
    private Vector3 Reflected(Vector3 direction, Vector3 total)
    {
        switch (Ambient)
        {
            case SceneAmbient.Cube when Cube is not null:
                return ((Mirror ?? Cube).Sample(Vector3.TransformNormal(direction, CubeTurn))
                    * (new Vector3(CubeBrightness) + (total * DirectLightEnvRatio)) * (1f - GiEnvOcclusion))
                    + new Vector3(FlatAmbient * GiEnvOcclusion);
            case SceneAmbient.Flat:
            case SceneAmbient.Cube:
                return new Vector3(FlatAmbient);
            default:
                return Vector3.Zero;
        }
    }

    /// <summary>One light's GGX lobe on the waves - <see cref="Lit"/>'s specular share, with their normal.</summary>
    private static void Glinted(Vector3 light, Vector3 towards, Vector3 waves, Vector3 toEye, float facing, Vector3 specular, float gloss, ref Vector3 shine)
    {
        if (Vector3.Dot(waves, towards) <= 0f)
        {
            return;
        }

        Vector3 half = Vector3.Normalize(toEye + towards);
        float lobe = GlossLight.Lobe(waves, facing, towards, half, gloss);
        float fresnel = GlossLight.Fresnel(Math.Clamp(Vector3.Dot(toEye, half), 0f, 1f));
        shine += light * lobe * (new Vector3(fresnel) + (specular * (1f - fresnel)));
    }

    /// <summary>One light's diffuse and specular share at one pixel - ComputeDiffuse and the GGX lobe of ComputeLight.</summary>
    private static void Lit(
        Vector3 light, Vector3 towards, Vector3 normal, Vector3 toEye, float facing, bool glossy, Vector3 specular, float gloss,
        ref Vector3 diffuse, ref Vector3 shine)
    {
        float lit = Vector3.Dot(normal, towards);
        if (lit <= 0f)
        {
            return;
        }

        diffuse += light * MathF.Min(lit, 1f);
        if (glossy)
        {
            Vector3 half = Vector3.Normalize(toEye + towards);
            float lobe = GlossLight.Lobe(normal, facing, towards, half, gloss);
            float fresnel = GlossLight.Fresnel(Math.Clamp(Vector3.Dot(toEye, half), 0f, 1f));
            shine += light * lobe * (new Vector3(fresnel) + (specular * (1f - fresnel)));
        }
    }

    /// <summary>A light's precomputed numbers - GetPointCutoffRadius and ComputePointLightParamsNew's constants - or null for one that adds nothing.</summary>
    private static Point? Of(Vector3 position, Vector3 colour, float radius, float a)
    {
        float intensity = MathF.Abs(colour.X) + MathF.Abs(colour.Y) + MathF.Abs(colour.Z);
        if (!(radius > 0f) || !(intensity > 0f))
        {
            return null;
        }

        float cutoff = radius * MathF.Sqrt(intensity * ZeroIntensity / ColourThreshold);
        float multiplier = float.Lerp(1f / MathF.Sqrt(0.02f), 100f, Math.Clamp(a, 0f, 1f));
        return new Point(position, colour, cutoff, radius / multiplier, 10f * 0.02f * multiplier * multiplier);
    }

    private int CellIndex(Vector3 place)
    {
        Vector3 at = (place - _gridLeast) / _cellSize;
        if (at.X < 0f || at.Y < 0f || at.Z < 0f)
        {
            return -1;
        }

        int x = (int)at.X, y = (int)at.Y, z = (int)at.Z;
        if (x >= _cellsX || y >= _cellsY || z >= _cellsZ)
        {
            return -1;
        }

        return (((z * _cellsY) + y) * _cellsX) + x;
    }

    private (int X, int Y, int Z) Cell(Vector3 place)
    {
        Vector3 at = (place - _gridLeast) / _cellSize;
        return (
            Math.Clamp((int)at.X, 0, _cellsX - 1),
            Math.Clamp((int)at.Y, 0, _cellsY - 1),
            Math.Clamp((int)at.Z, 0, _cellsZ - 1));
    }

    /// <summary>A point light ready to shade with: its cutoff, its core radius (median over multiplier), and its zero intensity.</summary>
    /// <param name="Position">Where it is, model space.</param>
    /// <param name="Colour">Its colour times its intensity.</param>
    /// <param name="Cutoff">How far it reaches - GetPointCutoffRadius.</param>
    /// <param name="Core">Its radius over the radius multiplier - ComputePointLightParamsNew's divisor.</param>
    /// <param name="Zero">Its intensity at nought distance, before the fade - 0.2 m², and the ten it is capped at.</param>
    public readonly record struct Point(Vector3 Position, Vector3 Colour, float Cutoff, float Core, float Zero);

    /// <summary>The light grid's shape - see <see cref="Cells"/>.</summary>
    /// <param name="Least">Its corner nearest minus infinity, model space.</param>
    /// <param name="Size">A cell's side.</param>
    /// <param name="X">How many cells along x.</param>
    /// <param name="Y">How many along y.</param>
    /// <param name="Z">How many along z.</param>
    public readonly record struct LightCells(Vector3 Least, float Size, int X, int Y, int Z);
}

/// <summary>
/// What SceneLight.ShadeWater worked out at one pixel of water, part by part - for the probe, which then says where the water's colour came from.
/// </summary>
/// <param name="Subsurface">The medium's colour as light lets it through: scattered by the waves' height, less the foam's share - linear.</param>
/// <param name="Exponent">The scattering's exponent, <c>1 / (1 + max(-0.9, 250 * height * 5e-5 * scattering))</c> - under one on a crest.</param>
/// <param name="Foam">The foam's albedo times its cover, linear.</param>
/// <param name="Absorbed">The light the medium and the foam give off: the lights on both, the ambient and the GI's stand-in - <c>absorb_light</c>.</param>
/// <param name="Ambient">The ambient's share of it.</param>
/// <param name="Specular">The specular light kept - the lights' lobes and the environment's reflection, times <c>1 - ratio^3</c>.</param>
/// <param name="Reflectivity">How much the surface reflects, <c>lerp(Fresnel(N.V), 1, specular) * (1 - ratio)</c>.</param>
/// <param name="Refracted">How much of what lies under the surface is seen - <c>lerp(1 - reflectivity, 1, ratio)</c>.</param>
/// <param name="Through">How much of what lies behind gets through the medium.</param>
/// <param name="Terrain">The shore's <c>water_to_terrain_ratio</c>.</param>
/// <param name="Hidden">How much the foam hides of what lies under it, <c>1 - (1 - foam)^3</c>.</param>
/// <param name="Cover">How much of what lies behind the water covers as a layer.</param>
public readonly record struct WaterLight(
    Vector3 Subsurface, float Exponent, Vector3 Foam, Vector3 Absorbed, Vector3 Ambient, Vector3 Specular, float Reflectivity, float Refracted,
    float Through, float Terrain, float Hidden, float Cover);
