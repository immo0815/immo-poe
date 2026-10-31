using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// Which of a model's roof sections are hidden round a place on its ground - the pane's reading of the game's roof fade: every roof section
/// of the tile piece the place stands on, left out of the picture whole (MaterialBlend.Hidden).
/// </summary>
/// <remarks>
/// THE RULE IS CHOSEN UNTIL THE CAPTURES SETTLE IT (v0.1.387; asked for from the live client, "build everything as far as possible", the
/// roof fade having been DECIDED "measured first" - roof-fade.txt measures it). The engine's own rule is the program's
/// (ClientTileMap::CalculateRoofFadeRanges, RoofFadeMode), and The Red Vale's master sets no mode. Its one measured capture (09:12) fits
/// the reading taken here - the piece the player stands on fades whole, RoofFadeMode "Tile"'s reading: both trees the game hid were
/// sections of curve5, the piece under the player, and both it showed belonged to a piece he did not stand on - and it fits a reach down
/// the screen as well; roof-fade.txt holds both readings against every capture.
///
/// CHOSEN beside it, each said under the picture: the place is the camera's focus, standing for the player as it does for the fog and the
/// clouds; a hidden section's shadow goes with it, as the engine's shadow pass would read the object's own roof_fade too (roof-fade.txt
/// measures whether the game keeps it; drawn as a shadow caster alone instead, a tree's cut-out leaves cast square shadows); it is hidden
/// at once, with no fading in or out over time (the engine dissolves a section through a speckle of world-space noise as its value falls, roof_fade.fxgraph, over a time
/// RoofFadeBlend names - unread); a roof section of no tile piece - an object's own - is left drawn. A piece's footprint is its ground's box
/// across the ground (its shapes named "ground of" and the piece), its own shapes' box where it lays no ground.
/// </remarks>
public sealed class RoofFade
{
    /// <summary>What a laid tile piece's source puts between its file and its least corner (LaidRoomModels).</summary>
    private const string AtTile = " at tile ";

    private readonly Piece[] _pieces;

    private RoofFade(Piece[] pieces, int loose)
    {
        _pieces = pieces;
        Loose = loose;
        Sections = pieces.Sum(one => one.Shapes.Length);
    }

    /// <summary>A tile piece carrying roof sections: its source, its footprint across the ground (the model's space) and its sections.</summary>
    private sealed record Piece(string Source, Vector2 Least, Vector2 Most, int[] Shapes);

    /// <summary>How many tile pieces carry roof sections.</summary>
    public int Pieces => _pieces.Length;

    /// <summary>How many roof sections belong to a tile piece.</summary>
    public int Sections { get; }

    /// <summary>How many roof sections belong to no tile piece - an object's own, left drawn.</summary>
    public int Loose { get; }

    /// <summary>The model's roof sections by tile piece, each piece's footprint worked out - or null where the model has no roof section.</summary>
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

        var shapesOf = new Dictionary<string, List<int>>(StringComparer.Ordinal);
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

            if (!shapesOf.TryGetValue(source, out List<int>? shapes))
            {
                shapesOf[source] = shapes = [];
            }

            shapes.Add(shape);
        }

        if (shapesOf.Count == 0 && loose == 0)
        {
            return null;
        }

        // THE FOOTPRINTS, in one walk over the shapes the pieces name: their ground, and their own shapes where they lay none.
        var grounds = new Dictionary<string, (Vector2 Least, Vector2 Most)>(StringComparer.Ordinal);
        var owns = new Dictionary<string, (Vector2 Least, Vector2 Most)>(StringComparer.Ordinal);
        for (var shape = 0; shape < mesh.Shapes.Count && shape < sources.Count; shape++)
        {
            string source = sources[shape];
            bool ground = source.StartsWith(RoomModels.GroundSource, StringComparison.Ordinal);
            string piece = ground ? source[RoomModels.GroundSource.Length..] : source;
            if (!shapesOf.ContainsKey(piece) || Across(mesh, shape) is not { } box)
            {
                continue;
            }

            Dictionary<string, (Vector2 Least, Vector2 Most)> into = ground ? grounds : owns;
            into[piece] = into.TryGetValue(piece, out (Vector2 Least, Vector2 Most) had)
                ? (Vector2.Min(had.Least, box.Least), Vector2.Max(had.Most, box.Most))
                : box;
        }

        var pieces = new List<Piece>(shapesOf.Count);
        foreach ((string source, List<int> shapes) in shapesOf)
        {
            if (grounds.TryGetValue(source, out (Vector2 Least, Vector2 Most) footprint) || owns.TryGetValue(source, out footprint))
            {
                pieces.Add(new Piece(source, footprint.Least, footprint.Most, [.. shapes]));
            }
        }

        return new RoofFade([.. pieces.OrderBy(one => one.Source, StringComparer.Ordinal)], loose);
    }

    /// <summary>The pieces whose footprint holds a place across the ground, by their index - in order, none where none does.</summary>
    public int[] Holding(Vector3 place)
    {
        var holding = new List<int>();
        for (var piece = 0; piece < _pieces.Length; piece++)
        {
            Piece one = _pieces[piece];
            if (place.X >= one.Least.X && place.X <= one.Most.X && place.Y >= one.Least.Y && place.Y <= one.Most.Y)
            {
                holding.Add(piece);
            }
        }

        return [.. holding];
    }

    /// <summary>The roof sections of the pieces given, by their index.</summary>
    public IEnumerable<int> SectionsOf(IReadOnlyList<int> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        foreach (int piece in pieces)
        {
            foreach (int shape in _pieces[piece].Shapes)
            {
                yield return shape;
            }
        }
    }

    /// <summary>The blends with the given pieces' roof sections left out whole (MaterialBlend.Hidden) - every shape's own where none is hidden.</summary>
    /// <param name="blends">Each shape's blend, or null for all solid.</param>
    /// <param name="shapes">How many shapes the model has.</param>
    /// <param name="pieces">The pieces whose sections are hidden, by their index.</param>
    public IReadOnlyList<MaterialBlend>? Faded(IReadOnlyList<MaterialBlend>? blends, int shapes, IReadOnlyList<int> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        if (pieces.Count == 0)
        {
            return blends;
        }

        var faded = new MaterialBlend[shapes];
        for (var shape = 0; shape < shapes; shape++)
        {
            faded[shape] = blends is not null && shape < blends.Count ? blends[shape] : MaterialBlend.Opaque;
        }

        foreach (int shape in SectionsOf(pieces))
        {
            if (shape < shapes)
            {
                faded[shape] = MaterialBlend.Hidden;
            }
        }

        return faded;
    }

    /// <summary>The line under the picture: which pieces' sections are hidden round the place, and what the reading takes as given.</summary>
    public string Said(IReadOnlyList<int> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        string hidden = pieces.Count == 0
            ? "the focus stands on none of the " + _pieces.Length.ToString(CultureInfo.InvariantCulture) + " tile pieces carrying roof sections - none hidden"
            : string.Create(CultureInfo.InvariantCulture,
                $"{SectionsOf(pieces).Count()} roof sections of {string.Join(", ", pieces.Select(one => Short(_pieces[one].Source)))} hidden, the piece under the focus");
        string loose = Loose > 0 ? string.Create(CultureInfo.InvariantCulture, $"; {Loose} roof sections of objects, not tiles, drawn") : string.Empty;
        return "roof fade: " + hidden + loose
            + " - CHOSEN until roof-fade.txt settles it: the piece the player stands on fades whole (RoofFadeMode \"Tile\"'s reading, one capture of"
            + " The Red Vale fits it), the camera's focus as the player, its shadow gone with it, hidden at once";
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
