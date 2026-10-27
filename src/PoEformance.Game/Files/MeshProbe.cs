using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>What became of one triangle's fragment at a probed pixel - see <see cref="PixelProbe"/>.</summary>
public enum Seen : byte
{
    /// <summary>Written, colour and depth: the nearest solid surface so far.</summary>
    Solid,

    /// <summary>Behind what the depth buffer already held, so not drawn at all.</summary>
    Under,

    /// <summary>A translucent shape that had already blended into this pixel - see MeshPicture.Canvas.Stamps.</summary>
    Again,

    /// <summary>Cut out: its alpha was under the cut-out line.</summary>
    Cut,

    /// <summary>Dropped by its own shade program - a contact fade over a surface that lies above it.</summary>
    Discarded,

    /// <summary>Translucent with no alpha left: blended as nothing.</summary>
    Clear,

    /// <summary>Mixed over what was behind it, by <see cref="ProbeFragment.Cover"/>.</summary>
    Mixed,

    /// <summary>Added to what was behind it.</summary>
    Added,

    /// <summary>The lifted water behind a nearer crossing of its own surface - see MeshPicture.Drawing's Surface.</summary>
    Folded,
}

/// <summary>One triangle's fragment at a probed pixel.</summary>
/// <param name="Triangle">The triangle, by its place in the mesh.</param>
/// <param name="Seen">What became of it.</param>
/// <param name="Depth">Its own depth along the view, in the model's units - nearer is less.</param>
/// <param name="Behind">What the depth buffer held when it arrived: the nearest solid surface drawn before it, or <see cref="float.MaxValue"/> where none was.</param>
/// <param name="Cover">How much of it covered what was behind, for a translucent one; nought otherwise.</param>
public readonly record struct ProbeFragment(int Triangle, Seen Seen, float Depth, float Behind, float Cover);

/// <summary>
/// What a water surface lit the game's way did at a probed pixel: where it stood, its waves and what its reflection of the picture met.
/// </summary>
/// <remarks>
/// WRITTEN WHEN SAND-COLOURED PATCHES SHOWED IN THE WATER BETWEEN THE SWELL'S CRESTS (asked for from the live client, v0.1.234), which
/// a picture cannot tell apart: the sea floor seen where a lifted trough drops below it (the water then dropped, a fragment
/// <see cref="Seen.Discarded"/>), or the shore reflected by the screen ray off a wave's face. This says the second's half: the
/// waves' normal, the view turned about it, and where the ray met the solid picture and what colour it took from there.
/// </remarks>
/// <param name="Depth">The water fragment's depth along the view.</param>
/// <param name="Place">Where it stands, lifted as drawn.</param>
/// <param name="Normal">The mesh's own normal there, the water's surface the ray is kept above.</param>
/// <param name="Waves">The waves' normal the light and the ray take.</param>
/// <param name="Reflected">The view turned about the waves' normal and kept above the surface; zero where the ray was not cast.</param>
/// <param name="Step">The step the ray met a surface at, -1 for none (the environment alone), -2 where it was not cast.</param>
/// <param name="HitX">Where it met it, the picture's column.</param>
/// <param name="HitY">Where it met it, the picture's row.</param>
/// <param name="HitDepth">The solid depth there.</param>
/// <param name="HitColour">The solid picture's colour there, as stored.</param>
/// <param name="Environment">The environment's share of the reflection.</param>
/// <param name="Added">The light the reflection added, linear.</param>
/// <param name="Sun">How much of the sun the shadow map lets through to it, nought in shadow - asked for when the waves seemed cut off
/// along a straight line beside Port's ship (from the live client, v0.1.236): a shadow takes the sun's glints with it.</param>
/// <param name="Light">Every part of its light where it was lit as the engine's water pass lights it (SceneLight.ShadeWater) - asked for
/// when the water looked nearer the game under the flat ambient than under the cube (from the live client, v0.1.295); default where not.</param>
public readonly record struct ProbeWater(
    float Depth, Vector3 Place, Vector3 Normal, Vector3 Waves, Vector3 Reflected, int Step, int HitX, int HitY, float HitDepth,
    Vector3 HitColour, float Environment, Vector3 Added, float Sun, WaterLight Light = default);

/// <summary>
/// How much of the sun reached a fragment the game light shaded at a probed pixel, and - where it was shaded - what cast the shadow.
/// </summary>
/// <remarks>
/// WRITTEN WHEN DARK PATCHES WITH STRAIGHT, TOOTHED EDGES LAY ON PORT'S SAND behind the beach waves' foam (v0.1.242's capture): a shadow,
/// the beach's film over a deep gap, or the lifted sea over the sand look alike in a picture. The shadow map keeps depths only, so the
/// caster is found by casting the one ray from the fragment towards the sun through every triangle that casts - once, on a click.
/// </remarks>
/// <param name="Triangle">The fragment's triangle.</param>
/// <param name="Depth">Its depth along the view.</param>
/// <param name="Place">Where it stands, model space, as drawn.</param>
/// <param name="Normal">The normal it was lit with, turned to the eye.</param>
/// <param name="Sun">How much of the sun the shadow map let through, nought in shadow.</param>
/// <param name="Caster">The nearest triangle that casts on the way to the sun, -1 for none or not looked for.</param>
/// <param name="CasterAway">How far towards the sun it lies.</param>
public readonly record struct ProbeSun(int Triangle, float Depth, Vector3 Place, Vector3 Normal, float Sun, int Caster = -1, float CasterAway = 0f);

/// <summary>
/// What a tile's two grounds mixed per pixel made of a probed pixel: the mask's green there and each ground's colour before the mix.
/// </summary>
/// <remarks>
/// ASKED FOR BEFORE A CAPTURE OF PORT'S BEACH (v0.1.244, "takes some getting used to", said from the live client): mixed per pixel as the engine's
/// GroundGenerateWeights_1a1b mixes them, the beach came out dark all over, which reads as the ocean's black taking most of each
/// pixel where the mask's green is high - and the engine's height blending, not drawn, would hand most of it back to the cobblestone.
/// Whether that is what happened is the green and the two colours at the pixel, which this says.
/// </remarks>
/// <param name="Triangle">The ground's triangle.</param>
/// <param name="Depth">Its depth along the view.</param>
/// <param name="Green">The mask's green at the pixel - the second ground's share.</param>
/// <param name="First">The first ground's colour there, linear.</param>
/// <param name="Second">The second ground's colour there, linear.</param>
public readonly record struct ProbeGround(int Triangle, float Depth, float Green, Vector3 First, Vector3 Second);

/// <summary>
/// What the beach waves made of a probed pixel: where the sheet stood, its waves' lift there, the shore's distances and the graph's mix.
/// </summary>
/// <remarks>
/// THE BEACH'S HALF OF THE PIXEL (asked for by v0.1.242's capture, whose probe could not say whether a dark patch on the sand was the
/// beach's film): the foam's share, the film's thickness and the path the view crosses under it to the solid behind - from which the
/// film lets <c>exp(-path * thickness * absorption)</c> through (ShadeProgram.Beaching) - and the cover they came to.
/// </remarks>
/// <param name="Triangle">The beach's triangle.</param>
/// <param name="Depth">Its depth along the view.</param>
/// <param name="Place">Where it stands, model space, moved by its waves as drawn.</param>
/// <param name="Lift">How far its waves move the sheet there along z - BreakingWavesVertex's, the area's height offset in it; up is minus z.</param>
/// <param name="Shore">The shore field's distance there, colour units - positive at sea, negative on land.</param>
/// <param name="Foam">The foam's share of the cover, the graph's <c>0.7 * a</c>.</param>
/// <param name="Thickness">The film's thickness under the foam.</param>
/// <param name="Path">How far the view crosses from the sheet to the solid behind it.</param>
/// <param name="Cover">What the foam and the film covered together.</param>
/// <param name="Colour">The colour mixed, linear, before the light.</param>
public readonly record struct ProbeBeach(
    int Triangle, float Depth, Vector3 Place, float Lift, float Shore, float Foam, float Thickness, float Path, float Cover, Vector3 Colour);

/// <summary>
/// What the swell's foam (ShadeProgram.Foam) was made of at a probed pixel of water: the shore field there, the tide's phase, the
/// cross-section read at it, and every term between them and the cover.
/// </summary>
/// <remarks>
/// WRITTEN WHEN PORT'S CHANNEL SHOWED FOAM IN RECTANGLES WITH STRAIGHT EDGES with the field waves off (said from the live client,
/// v0.1.301; their cause asked after again at v0.1.308). The swell's foam is the one foam drawn there then, and every term of it - the
/// shore's distance, the way in, the depth the view crosses, the shallows - may change along a straight line: a wall's edge, a sunken
/// stone's, a water plane's. Worked out by the same function that draws it, at the pixel's own place, clock and depth.
/// </remarks>
/// <param name="Place">The water's place, x and y, as the program reads it.</param>
/// <param name="Depth">How much water the view crosses to the solid behind (<c>gBufferDelta</c>); <see cref="float.MaxValue"/> where nothing is behind.</param>
/// <param name="ShoreDist">The shore field's distance there, colour units.</param>
/// <param name="OceanDist">The way in there, colour units.</param>
/// <param name="Tide">The tide's phase there, nought to one.</param>
/// <param name="Profile">The cross-section read at the phase: the swell's height, its slope, the trail's strength (z) and the front's phase (w).</param>
/// <param name="Leading">Whether the place is on the swell's leading edge, where the front's phase counts.</param>
/// <param name="Trail">The trail's strength the foam takes: the cross-section's green times the depth to 25 units.</param>
/// <param name="Front">The front's phase the foam takes, the shallows worked in.</param>
/// <param name="Shallow">The shallows' share.</param>
/// <param name="ShallowFoam">The shallows' foam term, <c>-(shoreDist - 0.18) * 10</c>.</param>
/// <param name="Cover">What the foam came to: how much it covers.</param>
/// <param name="CellX">The shore field's cell, its column.</param>
/// <param name="CellY">Its row.</param>
/// <param name="Cell">What the cell is: water, land, a wall, or something crossing the water's surface there.</param>
public readonly record struct ProbeSwell(
    Vector2 Place, float Depth, float ShoreDist, float OceanDist, float Tide, Vector4 Profile, bool Leading, float Trail, float Front,
    float Shallow, float ShallowFoam, float Cover, int CellX, int CellY, string Cell);

/// <summary>
/// Every fragment one pixel of a drawing was made of, in the order the drawing met them.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. A layer of mud drawn over everything and a layer of mud that fades as the game
/// fades it look different only where the ground under it is - and a picture says nothing about
/// what is under its own pixels. The fragments say it all: which surface won, what lay beneath it,
/// how far beneath, and what alpha a mixed layer came out with. The same question the interface
/// browser answers for the game's UI with F8, asked of a picture.
///
/// RECORDED BY THE DRAWING ITSELF, at the points where it decides, not by a second walk over the
/// triangles that would have to agree with the first. One pixel lies in one band and a band is drawn
/// by one thread, so the list needs no lock. A drawing that keeps its still part and draws the
/// clock's part again (MeshPicture.Again) probes nothing: set this on the canvas and draw it whole.
/// </remarks>
public sealed class PixelProbe
{
    private readonly List<ProbeFragment> _fragments = [];
    private readonly List<ProbeWater> _waters = [];
    private readonly List<ProbeSun> _suns = [];
    private readonly List<ProbeBeach> _beaches = [];
    private readonly List<ProbeGround> _grounds = [];
    private readonly List<ProbeSwell> _swells = [];

    /// <param name="x">The pixel's column, from the left.</param>
    /// <param name="y">The pixel's row, from the top.</param>
    public PixelProbe(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>The pixel's column, from the left.</summary>
    public int X { get; }

    /// <summary>The pixel's row, from the top.</summary>
    public int Y { get; }

    /// <summary>Every fragment the pixel met, in the drawing's order: the solid pass's, then the translucent pass's.</summary>
    public IReadOnlyList<ProbeFragment> Fragments => _fragments;

    /// <summary>The depth the pixel was left with: the solid surface shown, or <see cref="float.MaxValue"/> where none was.</summary>
    public float Final { get; internal set; } = float.MaxValue;

    /// <summary>Whether a drawing has been through it since it was set.</summary>
    public bool Drawn { get; internal set; }

    /// <summary>Every water surface lit the game's way at the pixel, in the drawing's order - see <see cref="ProbeWater"/>.</summary>
    public IReadOnlyList<ProbeWater> Waters => _waters;

    /// <summary>Every fragment the game light shaded at the pixel, with the sun it let through and what shaded it - see <see cref="ProbeSun"/>.</summary>
    public IReadOnlyList<ProbeSun> Suns => _suns;

    /// <summary>Every beach fragment mixed at the pixel - see <see cref="ProbeBeach"/>.</summary>
    public IReadOnlyList<ProbeBeach> Beaches => _beaches;

    internal void Add(ProbeFragment fragment) => _fragments.Add(fragment);

    internal void Add(ProbeWater water) => _waters.Add(water);

    internal void Add(ProbeSun sun) => _suns.Add(sun);

    /// <summary>Every fragment of two grounds mixed at the pixel - see <see cref="ProbeGround"/>.</summary>
    public IReadOnlyList<ProbeGround> Grounds => _grounds;

    internal void Add(ProbeBeach beach) => _beaches.Add(beach);

    internal void Add(ProbeGround ground) => _grounds.Add(ground);

    /// <summary>Every water fragment's swell foam worked out at the pixel - see <see cref="ProbeSwell"/>.</summary>
    public IReadOnlyList<ProbeSwell> Swells => _swells;

    internal void Add(ProbeSwell swell) => _swells.Add(swell);

    /// <summary>Names what cast the shadow on one of <see cref="Suns"/>.</summary>
    internal void Cast(int sun, int caster, float away) => _suns[sun] = _suns[sun] with { Caster = caster, CasterAway = away };

    internal void Clear()
    {
        _fragments.Clear();
        _waters.Clear();
        _suns.Clear();
        _beaches.Clear();
        _grounds.Clear();
        _swells.Clear();
        Final = float.MaxValue;
        Drawn = false;
    }
}

/// <summary>
/// How every translucent shape of a drawing came out, pixel by pixel: how many it reached, how many
/// had a solid surface behind and how far behind, and what alpha it was mixed with.
/// </summary>
/// <remarks>
/// THE OTHER HALF OF <see cref="PixelProbe"/>. One pixel says what happened there; this says
/// whether it happened everywhere. A ground layer that should fade where the ground meets it and
/// shows at full alpha on nine pixels in ten is a number here before it is an impression.
///
/// IN BINS, NOT IN SAMPLES, because a room's mud reaches hundreds of thousands of pixels and a
/// median would have to keep them all. Counted per thread and added up once the bands are done,
/// so the drawing's threads never wait on each other for it.
/// </remarks>
public sealed class LayerTally
{
    /// <summary>Slots per shape: fragments, with a solid behind, discarded, then the cover bins and the gap bins.</summary>
    internal const int Slots = 3 + CoverBins + GapBins;

    /// <summary>How the alpha is binned: nought, under a quarter, a half, three quarters, nearly whole, whole.</summary>
    public const int CoverBins = 6;

    /// <summary>How the gap to the solid behind is binned, in the model's units: under 1, 3, 10, 30, 100, and past it.</summary>
    public const int GapBins = 6;

    private const int FragmentsAt = 0;
    private const int BehindAt = 1;
    private const int DiscardedAt = 2;
    private const int CoverAt = 3;
    private const int GapAt = CoverAt + CoverBins;

    private int[] _counts = [];

    /// <summary>How many shapes the last drawing counted.</summary>
    public int Shapes { get; private set; }

    /// <summary>The upper edge of each gap bin but the last.</summary>
    public static IReadOnlyList<float> GapEdges { get; } = [1f, 3f, 10f, 30f, 100f];

    /// <summary>What one shape came to in the last drawing.</summary>
    public LayerCount Of(int shape)
    {
        if ((uint)shape >= (uint)Shapes)
        {
            return default;
        }

        ReadOnlySpan<int> at = _counts.AsSpan(shape * Slots, Slots);
        return new LayerCount(
            at[FragmentsAt],
            at[BehindAt],
            at[DiscardedAt],
            at.Slice(CoverAt, CoverBins).ToArray(),
            at.Slice(GapAt, GapBins).ToArray());
    }

    /// <summary>Room for this many shapes, every count nought.</summary>
    internal void Reset(int shapes)
    {
        Shapes = Math.Max(0, shapes);
        if (_counts.Length != Shapes * Slots)
        {
            _counts = new int[Shapes * Slots];
        }
        else
        {
            Array.Clear(_counts);
        }
    }

    /// <summary>One thread's counts added in.</summary>
    internal void Add(int[] local)
    {
        lock (_counts)
        {
            for (var one = 0; one < local.Length && one < _counts.Length; one++)
            {
                _counts[one] += local[one];
            }
        }
    }

    /// <summary>Counts one translucent fragment into a thread's own counts.</summary>
    /// <param name="local">The thread's counts, <see cref="Slots"/> per shape.</param>
    /// <param name="shape">The shape it belongs to.</param>
    /// <param name="cover">The alpha it was mixed with, or negative infinity where its program discarded it.</param>
    /// <param name="depth">Its own depth.</param>
    /// <param name="behind">The depth of the solid surface behind it, or <see cref="float.MaxValue"/>.</param>
    internal static void Count(int[] local, int shape, float cover, float depth, float behind)
    {
        int at = shape * Slots;
        if ((uint)(at + Slots) > (uint)local.Length)
        {
            return;
        }

        local[at + FragmentsAt]++;
        if (behind < float.MaxValue)
        {
            local[at + BehindAt]++;
            float gap = behind - depth;
            int bin = gap < 1f ? 0 : gap < 3f ? 1 : gap < 10f ? 2 : gap < 30f ? 3 : gap < 100f ? 4 : 5;
            local[at + GapAt + bin]++;
        }

        if (float.IsNegativeInfinity(cover))
        {
            local[at + DiscardedAt]++;
            return;
        }

        int part = !(cover > 0f) ? 0 : cover < 0.25f ? 1 : cover < 0.5f ? 2 : cover < 0.75f ? 3 : cover < 0.99f ? 4 : 5;
        local[at + CoverAt + part]++;
    }
}

/// <summary>What one translucent shape came to - see <see cref="LayerTally"/>.</summary>
/// <param name="Fragments">Pixels it reached in front of the solid surface there.</param>
/// <param name="Behind">Of those, how many had a solid surface behind them at all.</param>
/// <param name="Discarded">How many its own program dropped.</param>
/// <param name="Cover">The rest by alpha - see <see cref="LayerTally.CoverBins"/>.</param>
/// <param name="Gaps">Those with a solid behind, by how far behind - see <see cref="LayerTally.GapEdges"/>.</param>
public readonly record struct LayerCount(int Fragments, int Behind, int Discarded, IReadOnlyList<int> Cover, IReadOnlyList<int> Gaps);
