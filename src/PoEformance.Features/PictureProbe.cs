using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// What a probed pixel of a model's picture was made of, and how its translucent layers came out, as lines a person can read.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. The mud over seepage's offices came out as a slab that hid what the game shows,
/// and after the contact fade was written from the game's own shader sources it looked exactly the
/// same - with nothing to say whether the fade ran and found the ground too far below, ran and found
/// no ground at all, or never ran. A picture cannot answer that about its own pixels. These lines
/// can: every surface under the pixel with its material, where it came from and how far apart they
/// lie, and for every mixed layer how much of it had something solid behind it and what alpha it got.
///
/// THE WORDS ARE WORKED OUT HERE AND NOT IN THE PANE, so a test can read them without a window.
/// </remarks>
public static class PictureProbe
{
    /// <summary>Most materials named in the layers' lines; the rest are counted.</summary>
    public const int MostLayers = 8;

    /// <summary>The lines for one probed pixel, nearest surface first.</summary>
    /// <param name="probe">A probe a drawing has been through.</param>
    /// <param name="model">The model the picture is of.</param>
    /// <param name="shades">The programs the picture was drawn with, one per shape, or null.</param>
    public static IReadOnlyList<string> Lines(PixelProbe probe, MonsterModel model, IReadOnlyList<ShadeProgram?>? shades)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(model);
        if (!probe.Drawn)
        {
            return [Say($"probe at {probe.X}, {probe.Y}: not drawn yet")];
        }

        if (probe.Fragments.Count == 0)
        {
            return [Say($"probe at {probe.X}, {probe.Y}: nothing of the model reaches this pixel")];
        }

        // NEAREST FIRST, which is how a person reads a stack of surfaces from above; the drawing's own
        // order breaks a tie, so a surface drawn twice at one depth keeps its order.
        ProbeFragment[] sorted = [.. probe.Fragments.Select((one, at) => (one, at)).OrderBy(pair => pair.one.Depth).ThenBy(pair => pair.at).Select(pair => pair.one)];
        var lines = new List<string>(sorted.Length + 1)
        {
            Say($"probe at {probe.X}, {probe.Y}: {sorted.Length} fragment{(sorted.Length == 1 ? string.Empty : "s")}, nearest first"),
        };

        foreach (ProbeFragment fragment in sorted)
        {
            int shape = ShapeOf(model.Mesh, fragment.Triangle);
            var line = new StringBuilder(160);
            line.Append("  depth ").Append(Number(fragment.Depth))
                .Append(" · ").Append(Outcome(fragment, probe.Final));

            if (fragment.Seen is Seen.Mixed or Seen.Added or Seen.Clear or Seen.Discarded)
            {
                line.Append(" · ").Append(fragment.Behind < float.MaxValue
                    ? Number(fragment.Behind - fragment.Depth) + " in front of the solid behind it"
                    : "nothing solid behind it");
            }

            line.Append(" · ").Append(Described(model, shades, shape));
            lines.Add(line.ToString());
        }

        foreach (ProbeWater water in probe.Waters)
        {
            lines.Add(Watered(water));
        }

        foreach (ProbeSwell swell in probe.Swells)
        {
            lines.Add(Swelled(swell));
        }

        foreach (ProbeField field in probe.Fields)
        {
            lines.Add(Fielded(field));
        }

        foreach (ProbeGround ground in probe.Grounds)
        {
            lines.Add(Grounded(ground));
        }

        foreach (ProbeBeach beach in probe.Beaches)
        {
            lines.Add(Beached(beach));
        }

        foreach (ProbeSun sun in probe.Suns)
        {
            lines.Add(Sunned(sun, model, shades));
        }

        return lines;
    }

    /// <summary>Two grounds mixed at the pixel: the mask's green or the corners' share, each ground's colour and what the mix made of them - see <see cref="ProbeGround"/>.</summary>
    public static string Grounded(ProbeGround ground)
    {
        Vector3 mixed = Vector3.Lerp(ground.First, ground.Second, ground.Share);
        string weight = ground.Cornered
            ? "the sub-tile's corners' share, bilinear (CHOSEN for a shape across the mask's atlas)"
            : "the mask's green";
        string heights = ground.ByHeights
            ? Say($" · weighed by their heights {ground.FirstHeight:F3} and {ground.SecondHeight:F3} as the engine's height blending weighs them, the second ground's share then {ground.Share:F3}")
            : string.Empty;
        return Say($"  ground mix at depth {Number(ground.Depth)} · {weight} {ground.Green:F3}, the second ground's share")
            + heights
            + Say($" · the first ground {ground.First.X:F3}, {ground.First.Y:F3}, {ground.First.Z:F3}, the second {ground.Second.X:F3}, {ground.Second.Y:F3}, {ground.Second.Z:F3}")
            + Say($" · mixed {mixed.X:F3}, {mixed.Y:F3}, {mixed.Z:F3} linear, before the light{(ground.ByHeights ? string.Empty : " · the engine's height blending not drawn")}");
    }

    /// <summary>
    /// The beach waves at the pixel: where the sheet stood, its waves' lift, the shore's distance, and the graph's mix - see <see cref="ProbeBeach"/>.
    /// </summary>
    public static string Beached(ProbeBeach beach)
    {
        float through = MathF.Exp(-beach.Path * beach.Thickness * ShadeProgram.BeachAbsorption);
        string path = beach.Path < 1e30f ? Number(beach.Path) + " units of path to the solid behind" : "nothing solid behind";
        return Say($"  beach at depth {Number(beach.Depth)} · at ({Number(beach.Place.X)}, {Number(beach.Place.Y)}, {Number(beach.Place.Z)}) as drawn")
            + Say($" · its waves move it {Number(beach.Lift)} along z (up is minus z)")
            + Say($" · the shore {beach.Shore:F3} colour units ({Number(beach.Shore * ShoreField.Reach)}, {(beach.Shore >= 0f ? "out at sea" : "in on land")})")
            + Say($" · foam {Fraction(beach.Foam)}, the film {Fraction(beach.Thickness)} thick over {path}, letting through {Fraction(through)}")
            + Say($" · cover {Fraction(beach.Cover)} · colour {beach.Colour.X:F3}, {beach.Colour.Y:F3}, {beach.Colour.Z:F3} linear");
    }

    /// <summary>
    /// The swell's foam at the pixel, term by term - the shore field's cell and distances, the tide, the cross-section read at it, and
    /// what the foam came to - see <see cref="ProbeSwell"/>.
    /// </summary>
    public static string Swelled(ProbeSwell swell)
    {
        string depth = swell.Depth < float.MaxValue ? Number(swell.Depth) + " units of water to the solid behind" : "nothing solid behind";
        return Say($"  swell's foam at ({Number(swell.Place.X)}, {Number(swell.Place.Y)}) · the shore field's cell {swell.CellX}, {swell.CellY}, {swell.Cell}")
            + Say($" · the shore {swell.ShoreDist:F3} colour units ({Number(swell.ShoreDist * ShoreField.Reach)}), the way in {swell.OceanDist:F3} ({Number(swell.OceanDist * ShoreField.Reach)})")
            + Say($" · the tide's phase {swell.Tide:F3}{(swell.Leading ? ", on the leading edge" : string.Empty)}, the cross-section {swell.Profile.X:F3}, {swell.Profile.Y:F3}, trail {swell.Profile.Z:F3}, front {swell.Profile.W:F3}")
            + Say($" · {depth} · the trail taken {swell.Trail:F3}, the front {swell.Front:F3}, the shallows {swell.Shallow:F3} (their foam term {swell.ShallowFoam:F3})")
            + Say($" · covering {Fraction(swell.Cover)}");
    }

    /// <summary>
    /// The field waves at the pixel, term by term - where the card's lifted water meets the view, the shore's distances, the noise and the
    /// break phase, the crest, and every term of the foam with what it came to - see <see cref="ProbeField"/>.
    /// </summary>
    public static string Fielded(ProbeField field)
    {
        string state = field.On ? "drawn on the card" : "off, the card drawing none - what they would be";
        string met = float.IsNaN(field.Off)
            ? Say($" · the view runs along the water, so at the processor's place ({Number(field.Drawn.X)}, {Number(field.Drawn.Y)}, {Number(field.Drawn.Z)})")
            : Say($" · at ({Number(field.Place.X)}, {Number(field.Place.Y)}, {Number(field.Place.Z)}), where the card's lifted water meets the view (missed by {field.Off:F2}; the processor's water, unlifted by them, at ({Number(field.Drawn.X)}, {Number(field.Drawn.Y)}, {Number(field.Drawn.Z)}))");
        string laid = field.Overlaid
            ? Say($" · its two nodes overlapping, each layer dissolved apart (by {field.Layers.X:F3} and {field.Layers.Y:F3}) and laid one over the other")
            : " · its two layers blended and dissolved by that profile";
        FieldFoamProfile profile = field.Profile;
        return Say($"  field waves, {state}{met}")
            + Say($" · the shore {Number(field.ShoreDist)} units off the room's shore field (CHOSEN; rising {field.ShoreSlope.X:F2}, {field.ShoreSlope.Y:F2}), the way in {Number(field.OceanDist)} (the waves running {field.Way.X:F2}, {field.Way.Y:F2})")
            + Say($" · the ground's shore alone {Number(field.GroundDist)}, what stands in the sea no shore - the break taken off {(field.ByGround ? "it (the light panel's \"break by ground\")" : "the shore's")}")
            + Say($" · the noise {field.Noise:F3}, so the break's centre {field.Centre:F3} (over a half lowers the break) · 1 - {(field.ByGround ? "ground" : "shore")} x 0.0002 = {field.Raw:F3}, the break phase {field.BreakPhase:F3}")
            + Say($" · the wave's coordinate {field.WaveCoord:F3}, the foam's place in its wave {field.Interp:F3} (nought its front) · the crest moves the water {Number(field.Lift)} along z (up is minus z; the height offset {Number(field.HeightOffset)} in it)")
            + Say($" · the foam's profile there: the break lets {profile.BreakFoam:F3} form, spike {profile.Spike:F3} + tail {profile.Tail:F3} + constant {profile.Constant:F3}, the wave's own {profile.Wave:F3} once lerped by the break phase, the near-shore term {profile.NearShore:F3}, so {profile.Intensity:F3}, sharpness {profile.Sharpness:F3}")
            + laid
            + Say($" · covering {Fraction(field.Cover)}, colour {field.Colour.X:F3}, {field.Colour.Y:F3}, {field.Colour.Z:F3} linear, read at level {field.Level:F1}");
    }

    /// <summary>How much of the sun reached a fragment, and what cast the shadow where it did not - see <see cref="ProbeSun"/>.</summary>
    public static string Sunned(ProbeSun sun, MonsterModel model, IReadOnlyList<ShadeProgram?>? shades)
    {
        ArgumentNullException.ThrowIfNull(model);
        string head = Say($"  sun on shape {ShapeOf(model.Mesh, sun.Triangle)} at depth {Number(sun.Depth)}, ({Number(sun.Place.X)}, {Number(sun.Place.Y)}, {Number(sun.Place.Z)})");
        if (sun.Sun >= 1f)
        {
            return head + " · in the sun";
        }

        string shaded = Say($" · in the sun's shadow, {Fraction(sun.Sun)} of it reaching");
        return sun.Caster < 0
            ? head + shaded + " · nothing casts on the way to the sun: the map's texel, not a shape"
            : head + shaded + Say($" · cast by {Described(model, shades, ShapeOf(model.Mesh, sun.Caster))}, {Number(sun.CasterAway)} towards the sun");
    }

    /// <summary>
    /// A water surface lit the game's way at the pixel: where it stood, how far its waves' normal leans from its own, and what its
    /// reflection of the picture met - see <see cref="ProbeWater"/>.
    /// </summary>
    public static string Watered(ProbeWater water)
    {
        float lean = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(water.Normal), water.Waves), -1f, 1f)) * (180f / MathF.PI);
        var line = new StringBuilder(240);
        line.Append("  water at depth ").Append(Number(water.Depth))
            .Append(Say($" · at ({Number(water.Place.X)}, {Number(water.Place.Y)}, {Number(water.Place.Z)}) as drawn"))
            .Append(Say($" · its waves lean {lean:F1} degrees from its surface"))
            .Append(water.Sun < 1f ? Say($" · in the sun's shadow, {Fraction(water.Sun)} of it reaching") : " · in the sun");
        string said = water.Step switch
        {
            -2 => " · its reflection of the picture not cast (the light's water switches, or no solid part kept)",
            -1 => " · its reflection met nothing: the environment alone, the ray " + Way(water.Reflected),
            _ => Say($" · its reflection met the solid picture at step {water.Step}, pixel {water.HitX}, {water.HitY}, depth {Number(water.HitDepth)} ({Number(water.HitDepth - water.Depth)} past the water), colour {(int)water.HitColour.X}, {(int)water.HitColour.Y}, {(int)water.HitColour.Z}; the environment's share {Fraction(water.Environment)}, the light it added {water.Added.X:F3}, {water.Added.Y:F3}, {water.Added.Z:F3}; the ray ") + Way(water.Reflected),
        };
        line.Append(said);

        // EVERY PART OF THE ENGINE'S WATER LIGHT, linear before the exposure and the grade - where the colour came from.
        WaterLight lit = water.Light;
        if (lit != default)
        {
            line.Append(Say($" · lit as the engine's water pass: the medium {lit.Subsurface.X:F4}, {lit.Subsurface.Y:F4}, {lit.Subsurface.Z:F4} (scattered by the waves' height, exponent {lit.Exponent:F3})"))
                .Append(Say($", the foam {lit.Foam.X:F3}, {lit.Foam.Y:F3}, {lit.Foam.Z:F3}; the light they give off {lit.Absorbed.X:F4}, {lit.Absorbed.Y:F4}, {lit.Absorbed.Z:F4}, the ambient's {lit.Ambient.X:F4}, {lit.Ambient.Y:F4}, {lit.Ambient.Z:F4}"))
                .Append(Say($"; the specular kept {lit.Specular.X:F4}, {lit.Specular.Y:F4}, {lit.Specular.Z:F4}; reflectivity {lit.Reflectivity:F4}, refracted {lit.Refracted:F4}"))
                .Append(Say($"; through the medium {Fraction(lit.Through)}, the shore's ratio {Fraction(lit.Terrain)}, hidden by foam {Fraction(lit.Hidden)}, covering {Fraction(lit.Cover)}"));
        }

        return line.ToString();
    }

    /// <summary>
    /// The lines for how every translucent material came out over the whole picture, the most-drawn first.
    /// </summary>
    /// <param name="tally">What a drawing counted.</param>
    /// <param name="model">The model the picture is of.</param>
    /// <param name="shades">The programs the picture was drawn with, one per shape, or null.</param>
    public static IReadOnlyList<string> Layers(LayerTally tally, MonsterModel model, IReadOnlyList<ShadeProgram?>? shades)
    {
        ArgumentNullException.ThrowIfNull(tally);
        ArgumentNullException.ThrowIfNull(model);

        // BY MATERIAL, NOT BY SHAPE: a room's mud is sixty shapes of one material, and the question
        // is about the material.
        var byMaterial = new Dictionary<string, Summed>(StringComparer.OrdinalIgnoreCase);
        for (var shape = 0; shape < tally.Shapes; shape++)
        {
            LayerCount count = tally.Of(shape);
            if (count.Fragments == 0)
            {
                continue;
            }

            string material = shape < model.ShapeMaterials.Count && model.ShapeMaterials[shape].Length > 0
                ? MaterialFile.Bare(model.ShapeMaterials[shape])
                : "(no material)";
            if (!byMaterial.TryGetValue(material, out Summed? summed))
            {
                summed = new Summed(material);
                byMaterial[material] = summed;
            }

            summed.Add(count, shades is not null && shape < shades.Count && shades[shape] is not null);
        }

        if (byMaterial.Count == 0)
        {
            return ["layers: nothing translucent reached the picture"];
        }

        Summed[] ranked = [.. byMaterial.Values.OrderByDescending(one => one.Fragments).ThenBy(one => one.Material, StringComparer.Ordinal)];
        var lines = new List<string>(Math.Min(ranked.Length, MostLayers) + 2)
        {
            "layers: how each translucent material came out over this picture - pixels it reached in front of the solid there",
        };

        foreach (Summed one in ranked.Take(MostLayers))
        {
            lines.Add(one.Said());
        }

        if (ranked.Length > MostLayers)
        {
            lines.Add(Say($"  and {ranked.Length - MostLayers} more"));
        }

        return lines;
    }

    /// <summary>The shape a triangle belongs to, or -1.</summary>
    /// <remarks>A scan rather than a search: asked a few times per probe, over a few thousand shapes.</remarks>
    public static int ShapeOf(SkinnedMesh mesh, int triangle)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        int index = triangle * 3;
        for (var shape = 0; shape < mesh.Shapes.Count; shape++)
        {
            MeshShape one = mesh.Shapes[shape];
            if (index >= one.From && index < one.From + one.Count)
            {
                return shape;
            }
        }

        return -1;
    }

    private static string Outcome(ProbeFragment fragment, float final) => fragment.Seen switch
    {
        Seen.Solid => fragment.Depth <= final ? "shown" : "drawn, then covered by a nearer surface",
        Seen.Under => "hidden under the surface drawn before it",
        Seen.Again => "its shape had already blended here",
        Seen.Cut => "cut out",
        Seen.Discarded => "dropped by its program",
        Seen.Clear => "mixed with alpha 0",
        Seen.Mixed => "mixed, alpha " + Fraction(fragment.Cover),
        Seen.Added => "added, alpha " + Fraction(fragment.Cover),
        Seen.Folded => "behind a nearer crossing of the lifted water, which is drawn alone",
        _ => fragment.Seen.ToString(),
    };

    /// <summary>A shape's material, how it is blended, what draws it, and where it came from.</summary>
    private static string Described(MonsterModel model, IReadOnlyList<ShadeProgram?>? shades, int shape)
    {
        if (shape < 0)
        {
            return "no shape";
        }

        string material = shape < model.ShapeMaterials.Count && model.ShapeMaterials[shape].Length > 0
            ? GroundBlend.Short(MaterialFile.Bare(model.ShapeMaterials[shape]))
            : "no material";
        string mode = shape < model.Modes.Count && model.Modes[shape].Length > 0 ? model.Modes[shape] : "-";
        string blend = shape < model.Blends.Count ? model.Blends[shape].ToString().ToLowerInvariant() : "opaque";
        string drawn = shades is not null && shape < shades.Count && shades[shape] is not null
            ? "program"
            : shape < model.Skins.Count && model.Skins[shape] is not null ? "texture" : "plain";
        string from = shape < model.ShapeSources.Count && model.ShapeSources[shape].Length > 0
            ? " · from " + model.ShapeSources[shape]
            : string.Empty;
        return Say($"{material} ({mode} -> {blend}, {drawn}) · shape {shape}{from}");
    }

    private static string Tail(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    private static string Number(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Way(Vector3 way) => Say($"({way.X:F3}, {way.Y:F3}, {way.Z:F3})");

    private static string Fraction(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Say(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>One material's counts added up over its shapes.</summary>
    private sealed class Summed(string material)
    {
        private readonly int[] _cover = new int[LayerTally.CoverBins];
        private readonly int[] _gaps = new int[LayerTally.GapBins];
        private int _behind;
        private int _discarded;
        private bool _program;
        private bool _texture;

        public string Material { get; } = material;

        public long Fragments { get; private set; }

        public void Add(LayerCount count, bool program)
        {
            Fragments += count.Fragments;
            _behind += count.Behind;
            _discarded += count.Discarded;
            for (var bin = 0; bin < _cover.Length; bin++)
            {
                _cover[bin] += count.Cover[bin];
            }

            for (var bin = 0; bin < _gaps.Length; bin++)
            {
                _gaps[bin] += count.Gaps[bin];
            }

            _program |= program;
            _texture |= !program;
        }

        public string Said()
        {
            var line = new StringBuilder(220);
            line.Append("  ").Append(Tail(Material))
                .Append(" · ").Append(_program && _texture ? "program and texture" : _program ? "program" : "texture alpha")
                .Append(" · ").Append(Fragments.ToString("N0", CultureInfo.InvariantCulture)).Append(" px")
                .Append(" · solid behind ").Append(Share(_behind, Fragments));

            if (_behind > 0)
            {
                line.Append(" · gap");
                IReadOnlyList<float> edges = LayerTally.GapEdges;
                for (var bin = 0; bin < _gaps.Length; bin++)
                {
                    string label = bin == 0
                        ? "<" + Edge(edges[0])
                        : bin < edges.Count ? Edge(edges[bin - 1]) + "-" + Edge(edges[bin]) : Edge(edges[^1]) + "+";
                    line.Append(bin == 0 ? " " : ", ").Append(label).Append(' ').Append(Share(_gaps[bin], _behind));
                }
            }

            line.Append(" · alpha 0 ").Append(Share(_cover[0], Fragments))
                .Append(", <¼ ").Append(Share(_cover[1], Fragments))
                .Append(", <½ ").Append(Share(_cover[2], Fragments))
                .Append(", <¾ ").Append(Share(_cover[3], Fragments))
                .Append(", <1 ").Append(Share(_cover[4], Fragments))
                .Append(", 1 ").Append(Share(_cover[5], Fragments))
                .Append(" · dropped ").Append(Share(_discarded, Fragments));
            return line.ToString();
        }

        private static string Edge(float value) => value.ToString("0", CultureInfo.InvariantCulture);

        // BY HAND RATHER THAN "0%": the invariant culture writes a percentage with a space before the sign.
        private static string Share(long part, long whole)
            => whole <= 0 ? "-" : Math.Round(100.0 * part / whole).ToString("0", CultureInfo.InvariantCulture) + "%";
    }
}
