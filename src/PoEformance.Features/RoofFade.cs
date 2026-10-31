using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// Which of a model's roof sections are hidden round a place on its ground - the pane's reading of the game's roof fade: every roof section
/// of a tile piece whose box across the ground reaches into a region round the place, from <see cref="Toward"/> towards the eye to
/// <see cref="Behind"/> away from it and <see cref="Aside"/> to either side, left out of the picture whole (MaterialBlend.Hidden).
/// </summary>
/// <remarks>
/// THE GAME HIDES WHAT STANDS BETWEEN ITS CAMERA AND THE PLAYER, NOT THE TILE PIECE HE STANDS ON (v0.1.393). Three captures of The Red Vale held
/// one tree against the game - curve5's trunk and crown, sections of the very piece the player stood on in all three: at 09:12 and 13:46 the
/// player stood with the tree 460 to 600 units towards the camera and 130 to 260 aside, and the game hid trunk and crown; at 13:36 he stood
/// beside the trunk, 47 to 176 units behind him and 10 to 139 aside, and the game showed it (said from the live client: "the player stands
/// right at a tree, which does not cover him in the game"). v0.1.387's reading - the piece under the player fades whole - hid it. Another
/// trunk 388 to 516 towards the camera but 372 to 501 aside stayed shown. The engine's own rule is the program's (ClientTileMap::
/// CalculateRoofFadeRanges); environments name its extents area.roof_fade_circle_up, _dn and _lr (up the screen, down it and to the
/// sides - sanctum_floor1.env sets 361.2 up and 400 down, the prison's 500 down, Expedition's subareas -10 up), and The Red Vale's file sets
/// none, so the engine's defaults stand there, read in no file.
///
/// THE EXTENTS ARE CHOSEN within what the captures allow (at least 467 towards the camera, 134 to under 372 aside, -126 to under 47 behind; roof-fade.txt
/// works the allowance out again from every capture, and its replay over several at once): 600, 250 and nought. The test is the one the
/// captures measure: the box's four corners across the ground laid on the camera's two ways from the place - into the picture and to its
/// side - and the two spreads meeting the region's. The ways are the VIEWER'S camera's (DECIDED from the live client): what stands between
/// the viewer's eye and its focus fades, so turning the camera turns the region with it; at the game's angle it is the game's.
///
/// CHOSEN beside them, each said under the picture: the place is the camera's focus, standing for the player as it does for the fog and the
/// clouds; a hidden section's shadow goes with it, as the engine's shadow pass would read the object's own roof_fade too (roof-fade.txt
/// measures whether the game keeps it; drawn as a shadow caster alone instead, a tree's cut-out leaves cast square shadows); it is hidden
/// at once, with no fading in or out over time (the engine dissolves a section through a speckle of world-space noise as its value falls,
/// roof_fade.fxgraph, over a time RoofFadeBlend names - unread); a roof section of no tile piece - an object's own, whose .ao may set a
/// roof_fade_cylinder or box of its own - is left drawn.
/// </remarks>
public sealed class RoofFade
{
    /// <summary>How far towards the eye from the place, across the ground, a section may reach into the region - CHOSEN, see the remarks.</summary>
    public const float Toward = 600f;

    /// <summary>How far to either side of the place - CHOSEN.</summary>
    public const float Aside = 250f;

    /// <summary>How far behind the place, away from the eye - CHOSEN.</summary>
    public const float Behind = 0f;

    /// <summary>What a laid tile piece's source puts between its file and its least corner (LaidRoomModels).</summary>
    private const string AtTile = " at tile ";

    private readonly int[] _shapes;
    private readonly Vector2[] _least;
    private readonly Vector2[] _most;
    private readonly string[] _sources;

    private RoofFade(int[] shapes, Vector2[] least, Vector2[] most, string[] sources, int loose)
    {
        (_shapes, _least, _most, _sources) = (shapes, least, most, sources);
        Loose = loose;
    }

    /// <summary>How many roof sections belong to a tile piece - the ones the region may hide, in the order <see cref="Hide"/> fills.</summary>
    public int Sections => _shapes.Length;

    /// <summary>How many roof sections belong to no tile piece - an object's own, left drawn.</summary>
    public int Loose { get; }

    /// <summary>The model's roof sections of tile pieces, each one's box across the ground worked out - or null where the model has no roof section.</summary>
    public static RoofFade? Of(MonsterModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<bool> roofs = model.RoofSections;
        IReadOnlyList<string> sources = model.ShapeSources;
        SkinnedMesh mesh = model.Mesh;
        if (roofs.Count == 0 || !model.Ready)
        {
            return null;
        }

        var shapes = new List<int>();
        var least = new List<Vector2>();
        var most = new List<Vector2>();
        var named = new List<string>();
        var loose = 0;
        for (var shape = 0; shape < roofs.Count && shape < mesh.Shapes.Count; shape++)
        {
            if (!roofs[shape])
            {
                continue;
            }

            string source = shape < sources.Count ? sources[shape] : string.Empty;
            if (!source.Contains(AtTile, StringComparison.Ordinal))
            {
                loose++;
                continue;
            }

            if (Across(mesh, shape) is { } box)
            {
                shapes.Add(shape);
                least.Add(box.Least);
                most.Add(box.Most);
                named.Add(source);
            }
        }

        return shapes.Count == 0 && loose == 0 ? null : new RoofFade([.. shapes], [.. least], [.. most], [.. named], loose);
    }

    /// <summary>The way into the picture across the ground for a camera's turn - CameraPose's way in, (sin t cos l, -cos t cos l, sin l), laid flat.</summary>
    /// <remarks>Defined at every tilt, straight down included, where the way in has no part across the ground but the picture still has a top.</remarks>
    public static Vector2 AheadOf(float turn) => new(MathF.Sin(turn), -MathF.Cos(turn));

    /// <summary>Whether a box's spreads into the picture and to its side, from the place, meet the region's - the test the pane and roof-fade.txt share.</summary>
    /// <param name="ahead">The box's least and most along the way into the picture: minus is towards the eye.</param>
    /// <param name="aside">Its least and most along the picture's side.</param>
    public static bool Reaches((float Least, float Most) ahead, (float Least, float Most) aside)
        => ahead.Most >= -Toward && ahead.Least <= Behind && aside.Most >= -Aside && aside.Least <= Aside;

    /// <summary>The least and most a box's four corners across the ground stand along a way from a point.</summary>
    public static (float Least, float Most) Spread(Vector2 way, Vector2 least, Vector2 most, Vector2 from)
    {
        // A BOX'S SPREAD ALONG A WAY is its middle's place along it, give or take its half sides laid on it.
        Vector2 middle = ((least + most) * 0.5f) - from, half = (most - least) * 0.5f;
        float at = Vector2.Dot(middle, way), reach = (half.X * MathF.Abs(way.X)) + (half.Y * MathF.Abs(way.Y));
        return (at - reach, at + reach);
    }

    /// <summary>
    /// Which sections reach into the region round a place, the eye looking along <paramref name="ahead"/> - one flag a section, in
    /// <see cref="Sections"/>' order - and how many.
    /// </summary>
    /// <param name="place">The place - the camera's focus, standing for the player.</param>
    /// <param name="ahead">The way into the picture across the ground, of length one (<see cref="AheadOf"/>).</param>
    /// <param name="into">A flag a section, at least <see cref="Sections"/> long.</param>
    public int Hide(Vector3 place, Vector2 ahead, Span<bool> into)
    {
        var at = new Vector2(place.X, place.Y);
        var side = new Vector2(ahead.Y, -ahead.X);
        var hidden = 0;
        for (var one = 0; one < _shapes.Length; one++)
        {
            bool reaches = Reaches(Spread(ahead, _least[one], _most[one], at), Spread(side, _least[one], _most[one], at));
            into[one] = reaches;
            hidden += reaches ? 1 : 0;
        }

        return hidden;
    }

    /// <summary>The blends with the flagged sections left out whole (MaterialBlend.Hidden) - every shape's own where none is flagged.</summary>
    /// <param name="blends">Each shape's blend, or null for all solid.</param>
    /// <param name="shapes">How many shapes the model has.</param>
    /// <param name="hidden">A flag a section, as <see cref="Hide"/> filled it.</param>
    public IReadOnlyList<MaterialBlend>? Faded(IReadOnlyList<MaterialBlend>? blends, int shapes, ReadOnlySpan<bool> hidden)
    {
        if (!hidden.Contains(true))
        {
            return blends;
        }

        var faded = new MaterialBlend[shapes];
        for (var shape = 0; shape < shapes; shape++)
        {
            faded[shape] = blends is not null && shape < blends.Count ? blends[shape] : MaterialBlend.Opaque;
        }

        for (var one = 0; one < _shapes.Length && one < hidden.Length; one++)
        {
            if (hidden[one] && _shapes[one] < shapes)
            {
                faded[_shapes[one]] = MaterialBlend.Hidden;
            }
        }

        return faded;
    }

    /// <summary>The line under the picture: how many sections are hidden round the place and of which pieces, and what the reading takes as given.</summary>
    /// <param name="hidden">A flag a section, as <see cref="Hide"/> filled it.</param>
    public string Said(ReadOnlySpan<bool> hidden)
    {
        var count = 0;
        var pieces = new List<string>();
        for (var one = 0; one < _shapes.Length && one < hidden.Length; one++)
        {
            if (!hidden[one])
            {
                continue;
            }

            count++;
            string piece = Short(_sources[one]);
            if (!pieces.Contains(piece, StringComparer.Ordinal))
            {
                pieces.Add(piece);
            }
        }

        string what = count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"none of the {_shapes.Length} roof sections of tile pieces reaches into the region round the focus - none hidden")
            : string.Create(CultureInfo.InvariantCulture, $"{count} of the {_shapes.Length} roof sections of tile pieces hidden round the focus ({string.Join(", ", pieces)})");
        string loose = Loose > 0 ? string.Create(CultureInfo.InvariantCulture, $"; {Loose} roof sections of objects, not tiles, drawn") : string.Empty;
        return "roof fade: " + what + loose
            + string.Create(CultureInfo.InvariantCulture,
                $" - a section is hidden where its box across the ground reaches into {Toward:0} units towards the viewer's camera from the focus, {Aside:0} to either side and {Behind:0} behind it, the camera's own way (DECIDED from the live client); the extents CHOSEN within what three captures of The Red Vale allow (at least 467 towards the camera, 134 to under 372 aside, under 47 behind - roof-fade.txt narrows them with every capture), the focus as the player, its shadow gone with it, hidden at once");
    }

    /// <summary>A piece's source said short: its file's name and its corner.</summary>
    private static string Short(string source)
    {
        int at = source.LastIndexOf(AtTile, StringComparison.Ordinal);
        return at < 0 ? source : Path.GetFileNameWithoutExtension(source[..at]) + source[at..];
    }

    /// <summary>A shape's box across the ground over the vertices its triangles use, or null where it has none.</summary>
    private static (Vector2 Least, Vector2 Most)? Across(SkinnedMesh mesh, int shape)
    {
        MeshShape part = mesh.Shapes[shape];
        var least = new Vector2(float.MaxValue);
        var most = new Vector2(float.MinValue);
        int end = Math.Min(part.From + part.Count, mesh.Indices.Length);
        for (int at = part.From; at < end; at++)
        {
            Vector3 place = mesh.Positions[mesh.Indices[at]];
            least = Vector2.Min(least, new Vector2(place.X, place.Y));
            most = Vector2.Max(most, new Vector2(place.X, place.Y));
        }

        return least.X <= most.X ? (least, most) : null;
    }
}
