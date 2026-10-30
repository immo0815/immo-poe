using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// Paints a laid tile's ground block from the area's MaterialsList, sub-tile by sub-tile: one ground in its material, two grounds
/// mixed by the template's mask - or says why a sub-tile stays plain.
/// </summary>
/// <remarks>
/// WHAT IS MEASURED AND WHAT IS NOT, from Port's captures (v0.1.195 to v0.1.200):
///
///     the materials       the master's MaterialsList keys a group by the ground type's name (the .gt's first line); a
///                         tile's corners name the grounds. MEASURED: Port's harbour is cobblestone where its corners'
///                         IslandTownUnwalkableFill names G_IHLT_CobblestoneDirt01
///     whose corners       each SUB-TILE's own, from the definition's ground grid (TileDefinition.GroundGrid, v0.1.248) -
///                         MEASURED: Port's harbour names two grounds at its corners and four in its grid; the tile's four
///                         corners stand for every sub-tile only where a definition carries no grid
///     two grounds         the engine weighs them by its mask's green read at the ground block's own coordinates
///                         (ground.ffx, GetUserMask; GroundGenerateWeights_1a1b: lerp(first, second, green)). MEASURED:
///                         read so at its vertices, green follows height on the harbour (r 0.78, the sea floor all 1),
///                         the ocean-edge walls (r 1.00) and the coasts - the block's coordinates were made to split it
///     which is first      DECIDED FROM ONE AREA: in all three of Port's two-ground families the ground at green nought
///                         is the one the MaterialsList lists first (IslandTownUnwalkableFill, then IslandOcean, then
///                         TropicalFill). Neither the names' order nor the terrain's own list fits all three
///     the coordinates     the engine's CreateProjectedCoords: world.xy times ground_scalemove_uv.xy, a pass uniform no
///                         file holds; every environment of every capture carries "ground_scale": 0.004 in the block
///                         beside dust_color, the uniform's neighbour - taken as it, likely and not measured
///     which choice        a group of several materials is drawn by weight per tile in the game; the heaviest is taken
///
/// MIXED PER PIXEL, as the engine mixes them (v0.1.244): one shape wearing both grounds, the mask's green read at the block's
/// own coordinates and the two materials run and mixed by it (ShadeProgram.Grounds, MonsterModels.Paints.Grounds) - the
/// mesh keeps the block's coordinates and the program works the materials' out of the place. Before, a triangle wore the
/// second ground where its three vertices' green averaged a half or more, and Port's harbour showed the ocean's black in
/// patches with straight, toothed edges on its beach (said from the live client, v0.1.242's probe on one: blackc.mat under
/// a thin sea and a thin beach film). That split stays where the two cannot be run as one program, and the line under the
/// picture says why. THE ENGINE'S HEIGHT BLENDING (GROUND_HEIGHT_BLENDING, which Port's ground graph PBRGroundBN.fxgraph turns on -
/// the weights sharpened by the materials' heights, blackc.mat's nought) was left out, DECIDED from the live client (2026-10-12),
/// and is drawn from v0.1.311 behind a switch (RoomKey.GroundHeights), asked for from the live client: v0.1.309's capture of Port's
/// quay showed cobblestones standing out of the sand where neither the mask's squares nor the corners' share put them
/// (ShadeProgram.Grounds). Three or four grounds (the engine's red atlas, placed by transforms no file
/// holds) are drawn as the two most of their corners name (CHOSEN, see SetOf); the capture's picture of the ground on the
/// screen still marks them as the engine's own (SubTilesOf).
///
/// THE SHAPES ACROSS THE MASK'S ATLAS ARE MIXED BY THEIR CORNERS (v0.1.310), CHOSEN from the live client: a shape whose block
/// coordinates reach a cell of the atlas (64 texels) or more across the mask, in a sub-tile of two grounds, reads the atlas's cells
/// as squares - Port's checkerboard of the ocean's black and the dry sand by the ship's stern (v0.1.309's probe: the mask's green
/// 0.004 beside 1.000), its slopes and the pier's edge - while the game draws no squares there (measured at The Excavation,
/// v0.1.256, and Port's quay, v0.1.262) and what it reads is not measured. Such a shape is mixed instead by the second ground's
/// share of its sub-tile's four corners in the grid, bilinear across the sub-tile (CaptureGroundCompare.Share, the candidate the
/// capture holds against the picture), worked out per vertex and carried in the coordinates' first channel
/// (ShadeProgram.GroundsByCorners) - its vertices copied, so a vertex it shares with a shape on the mask keeps the block's
/// coordinates there. Every other shape stays on the mask, and a switch (RoomKey.MaskCells) keeps them all on it, the squares and
/// all, to hold the two against the game. A sub-tile whose corners are the tile's four, its definition carrying no grid, stays on
/// the mask.
/// </remarks>
internal sealed class GroundPainter
{
    /// <summary>The ground materials' tiling: world units to texture coordinates - see the remarks.</summary>
    public const float GroundScale = 0.004f;

    /// <summary>Green at or over this, averaged over a triangle's vertices, is the second ground's.</summary>
    private const float Half = 0.5f;

    /// <summary>The ground masks' atlas cell, in texels: newmask.dds is four by eight cells of 64 (v0.1.199) - a shape reaching one across is mixed by its corners.</summary>
    internal const int AtlasCell = 64;

    private readonly Func<string, byte[]?> _read;
    private readonly GroundMaterials _list;
    private readonly MonsterModels.Paints _paints;
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GamePicture?> _masks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Plan> _plans = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _plain = new(StringComparer.Ordinal);

    /// <summary>The tiles each reason for a plain ground was met on, each with its template and mask, a few a reason - for the stage sweep.</summary>
    private readonly Dictionary<string, List<string>> _plainSubjects = new(StringComparer.Ordinal);

    /// <summary>The ground type file (<c>.gt</c>) of each ground the MaterialsList does not name, by its name - for the stage sweep.</summary>
    private readonly Dictionary<string, string> _unlistedFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many tiles a reason for a plain ground keeps for the stage sweep.</summary>
    private const int SubjectsKept = 4;
    private readonly Dictionary<string, int> _split = new(StringComparer.Ordinal);
    private int _one;
    private int _two;
    private int _mixed;
    private int _gridded;
    private int _unnamed;
    private int _bare;
    private int _unlisted;
    private int _alikeSubTiles;
    private readonly List<string> _unlistedNames = [];
    private int _reduced;

    /// <summary>The MaterialsList's group with no name, by its place in the list, or -1 - see SetOf.</summary>
    private readonly int _unnamedGroup;

    /// <summary>Each group's first group alike, by place in the list - itself where none before it is; see SetOf.</summary>
    private readonly int[] _alike;
    private int _cornered;

    /// <summary>Whether the shapes across the mask's atlas stay on its cells - the switch, see the remarks.</summary>
    private readonly bool _maskCells;

    /// <summary>Whether two grounds are weighed by their heights where their graphs turn that on - the switch, see the remarks.</summary>
    private readonly bool _heights;
    private int _byCorners;

    /// <summary>Sub-tiles of two grounds whose template names no mask, mixed by their corners - see <see cref="Plan.Maskless"/>.</summary>
    private int _maskless;
    private int _cornerShapes;
    private int _keptOnCells;

    private GroundPainter(Func<string, byte[]?> read, GroundMaterials list, string from, MonsterModels.Paints paints, bool maskCells, bool heights)
    {
        _read = read;
        _list = list;
        _paints = paints;
        _maskCells = maskCells;
        _heights = heights;
        From = from;
        _unnamedGroup = -1;
        for (var at = 0; at < list.Groups.Count && _unnamedGroup < 0; at++)
        {
            _unnamedGroup = list.Groups[at].Name.Length == 0 ? at : -1;
        }

        // EACH GROUP'S FIRST ALIKE, itself where none before it is - see SetOf.
        _alike = new int[list.Groups.Count];
        for (var at = 0; at < _alike.Length; at++)
        {
            _alike[at] = at;
            for (var before = 0; before < at; before++)
            {
                if (Alike(list.Groups[before], list.Groups[at]))
                {
                    _alike[at] = _alike[before];
                    break;
                }
            }
        }
    }

    /// <summary>The MaterialsList painted from.</summary>
    public string From { get; }

    /// <summary>
    /// The painter for a master's MaterialsList, or null and why there is none.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="master">The area's master (<c>.tsi</c>).</param>
    /// <param name="paints">The room's paint cache, so the ground's textures are read once with everything else's.</param>
    /// <param name="maskCells">Whether the shapes across the mask's atlas stay on its cells rather than mixed by their corners - see the remarks.</param>
    /// <param name="heights">Whether two grounds are weighed by their heights where their graphs turn that on - see the remarks.</param>
    public static (GroundPainter? Painter, string Why) For(
        Func<string, byte[]?> read, string master, MonsterModels.Paints paints, bool maskCells = false, bool heights = false)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(paints);
        if (master.Length == 0)
        {
            return (null, "no master to name a MaterialsList");
        }

        string named = MasterFile.Read(read(Clean(master))).Values.GetValueOrDefault("MaterialsList", string.Empty);
        foreach (string candidate in MasterFile.Candidates(Clean(master), named))
        {
            GroundMaterials list = GroundMaterials.Read(read(candidate));
            if (list.Groups.Count > 0)
            {
                return (new GroundPainter(read, list, candidate, paints, maskCells, heights), string.Empty);
            }
        }

        return (null, named.Length == 0 ? $"the master names no MaterialsList: {master}" : $"the MaterialsList did not read: {named}");
    }

    /// <summary>
    /// The master beside a room - <c>master.tsi</c> in the folder above its <c>Rooms/</c> - or empty where there is none.
    /// </summary>
    /// <remarks>
    /// WHERE 456 OF RePoE's 458 MAP GRAPHS KEEP THEIRS (see AreaRoomSet), looked for by name and read to be sure; a room
    /// whose master is elsewhere or named otherwise is drawn with its ground plain, and the line under it says so.
    /// </remarks>
    public static string MasterBeside(Func<string, byte[]?> read, string room)
    {
        ArgumentNullException.ThrowIfNull(read);
        string path = Clean(room);
        int rooms = path.IndexOf("/Rooms/", StringComparison.OrdinalIgnoreCase);
        if (rooms < 0)
        {
            return string.Empty;
        }

        string master = path[..(rooms + 1)] + "master.tsi";
        return read(master) is { Length: > 0 } ? master : string.Empty;
    }

    /// <summary>
    /// The tile's ground painted for one piece with its four corners standing for every sub-tile - see the other overload.
    /// </summary>
    public MonsterModel? Paint(string tile, SkinnedMesh ground, Matrix4x4 place, string source) => Paint(tile, ground, [], place, source);

    /// <summary>
    /// The tile's ground painted for one piece, or null where all of it stays plain - counted sub-tile by sub-tile for <see cref="Said"/>.
    /// </summary>
    /// <param name="tile">The tile's <c>.tdt</c>.</param>
    /// <param name="ground">Its ground block, as the tile's walk joined it - its coordinates the mask's.</param>
    /// <param name="pieces">Which sub-tile each run of the block's triangles is, in the block's order - or none, and the tile's four corners stand for all.</param>
    /// <param name="place">Where the piece is laid, which the materials' coordinates are worked out from.</param>
    /// <param name="source">What the piece is called, for the probe.</param>
    public MonsterModel? Paint(string tile, SkinnedMesh ground, IReadOnlyList<GroundPiece> pieces, Matrix4x4 place, string source)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(pieces);
        if (!_plans.TryGetValue(tile, out Plan? plan))
        {
            plan = Planned(tile, ground, pieces);
            _plans[tile] = plan;
        }

        _one += plan.One;
        _mixed += plan.Mixed;
        _two += plan.Split;
        _gridded += plan.Gridded ? 1 : 0;
        _unnamed += plan.Unnamed;
        _bare += plan.Bare;
        _unlisted += plan.Unlisted;
        _alikeSubTiles += plan.Alike;
        foreach (string name in plan.UnlistedNames)
        {
            if (!_unlistedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _unlistedNames.Add(name);
            }
        }

        _reduced += plan.Reduced;
        _cornered += plan.Gridded ? 0 : 1;
        _byCorners += plan.ByCorners;
        _maskless += plan.Maskless;
        _cornerShapes += plan.CornerShapes;
        _keptOnCells += plan.KeptOnCells;
        foreach ((string why, int count) in plan.Plain)
        {
            _plain[why] = _plain.GetValueOrDefault(why) + count;
            List<string> subjects = _plainSubjects.TryGetValue(why, out List<string>? known) ? known : _plainSubjects[why] = [];
            string subject = plan.Subject.Length > 0 ? plan.Subject : tile;
            if (subjects.Count < SubjectsKept && !subjects.Contains(subject, StringComparer.OrdinalIgnoreCase))
            {
                subjects.Add(subject);
            }
        }

        foreach ((string why, int count) in plan.Splits)
        {
            _split[why] = _split.GetValueOrDefault(why) + count;
        }

        if (plan.Named.Count == 0)
        {
            return null;
        }

        // THE ENGINE'S COORDINATES, from where the piece lies - so a pattern runs on across the pieces as the game's does. A sub-tile
        // of two grounds mixed keeps the block's own, for the mask, and its program works these out of the place (ShadeProgram.Grounds).
        // A sub-tile's vertices are its own - the block joins the sub-tiles' meshes whole - so each takes its sub-tile's kind. The copies
        // a shape mixed by its corners draws carry the second ground's share there instead.
        int vertices = ground.Positions.Length;
        var coordinates = new Vector2[vertices + plan.Extra.Length];
        for (var copy = 0; copy < plan.Extra.Length; copy++)
        {
            coordinates[vertices + copy] = new Vector2(plan.Shares[copy], 0f);
        }

        for (var vertex = 0; vertex < vertices; vertex++)
        {
            if (plan.Own[vertex])
            {
                coordinates[vertex] = ground.Coordinates[vertex];
                continue;
            }

            Vector3 laid = Vector3.Transform(ground.Positions[vertex], place);
            coordinates[vertex] = new Vector2(laid.X, laid.Y) * GroundScale;
        }

        SkinnedMesh painted = SkinnedMesh.Of(
            plan.Positions ?? ground.Positions, plan.Normals ?? ground.Normals, plan.Indices, ground.Least, ground.Most, coordinates, plan.Shapes, ground.Facts);
        return MonsterModels.Worn(_read, painted, plan.Named, paintTheRest: false, source, "a tile's ground is static", _paints);
    }

    /// <summary>What was painted and what stayed plain, counted - for the room's account (RoomAccount).</summary>
    internal (int Painted, IReadOnlyDictionary<string, int> Plain, int Unlisted, IReadOnlyList<string> UnlistedNames) Counted()
        => (_one + _mixed + _two, new Dictionary<string, int>(_plain, StringComparer.Ordinal), _unlisted, [.. _unlistedNames]);

    /// <summary>The ground type file of each ground the MaterialsList does not name, by its name - see <see cref="Counted"/>.</summary>
    internal IReadOnlyDictionary<string, string> UnlistedFiles() => new Dictionary<string, string>(_unlistedFiles, StringComparer.OrdinalIgnoreCase);

    /// <summary>The tiles each reason for a plain ground was met on, with their templates and masks - see <see cref="Counted"/>.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> PlainSubjects()
        => _plainSubjects.ToDictionary(one => one.Key, one => (IReadOnlyList<string>)[.. one.Value], StringComparer.Ordinal);

    /// <summary>What was painted and what stayed plain, and on what it rests - for the line under the picture.</summary>
    public string Said()
    {
        string plain = _plain.Count == 0 ? "none"
            : string.Join(", ", _plain.OrderByDescending(one => one.Value).Select(one => string.Create(CultureInfo.InvariantCulture, $"{one.Value} {one.Key}")));
        string split = _two == 0 ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $", {_two} of two split by triangle where the green averages a half - mixing them could not be: ")
                + string.Join(", ", _split.OrderByDescending(one => one.Value).Select(one => string.Create(CultureInfo.InvariantCulture, $"{one.Value} {one.Key}")));
        string reduced = _reduced == 0 ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $", {_reduced} of three or four grounds drawn as the two most of their corners name, ties to the list's first - CHOSEN, a stand-in for the engine's own mask placed by transforms no file holds");
        string bare = _bare == 0 ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"; {_bare} of a tile carrying no grid with an own corner unnamed, taken as that group too - DECIDED from the live client");
        string unlisted = _unlisted == 0 ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"; {_unlisted} naming a ground the MaterialsList does not list ({string.Join(", ", _unlistedNames)}), taken as the group with no name - CHOSEN until a capture measures it (ground-on-screen's choices)");
        string alike = _alikeSubTiles == 0 ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" ({_alikeSubTiles} of them of two grounds whose groups offer the same materials at the same weights - a material mixed with itself)");
        string heights = _heights
            ? "weighed by the grounds' heights as the engine's GROUND_HEIGHT_BLENDING weighs them (GroundInterpolate), where a ground's graphs turn it on - by the switch"
            : "the engine's height blending left out by the switch";
        string corners = _maskCells
            ? string.Create(CultureInfo.InvariantCulture, $", {_keptOnCells} of them with shapes across the mask's atlas kept on its cells by the switch, squares and all")
            : _byCorners == 0 && _keptOnCells == 0 ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $", of them {_byCorners} with shapes across the mask's atlas ({_cornerShapes} shapes) mixed instead by the second ground's share of their sub-tile's corners, bilinear - CHOSEN, a stand-in: the game draws no squares there and what it reads is not measured{(_keptOnCells > 0 ? $"; {_keptOnCells} more left on the mask, their corners' mix not running" : string.Empty)}");
        string maskless = _maskless == 0 ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $", {_maskless} of two grounds whose template names no mask mixed by the second ground's share of their sub-tile's corners, bilinear - CHOSEN from the live client, the same stand-in: the engine mixes them by its own mask, placed by transforms no file holds");
        return string.Create(CultureInfo.InvariantCulture,
            $"ground: painted from {From}, each sub-tile from its own corners in its definition's ground grid ({_gridded} pieces; {_cornered} from the tile's four corners, their definition carrying no grid; {_unnamed} sub-tiles with a corner the grid leaves unnamed, taken as the MaterialsList's group with no name - the game holds its null slot there, and that it draws this group is DECIDED{bare}{unlisted}) - {_one} sub-tiles of one ground{alike}, {_mixed} of two mixed per pixel by the template mask's green at the block's own coordinates as the engine mixes them (GroundGenerateWeights_1a1b; the MaterialsList's first ground at green nought, as Port's three families agree - one area; {heights}){corners}{maskless}{reduced}{split}, plain: {plain}; the materials' coordinates the world times {GroundScale} (the environments' ground_scale, likely and not measured), a group's heaviest choice where the game draws by weight");
    }

    /// <summary>How a tile's ground is painted, worked out once whatever number of pieces lay it - and what it comes to, sub-tile by sub-tile.</summary>
    private sealed record Plan(int[] Indices, IReadOnlyList<MeshShape> Shapes, IReadOnlyList<(string Shape, string Material)> Named, bool[] Own)
    {
        /// <summary>Sub-tiles of one ground.</summary>
        public int One { get; init; }

        /// <summary>Sub-tiles of two grounds mixed per pixel - see the class's remarks.</summary>
        public int Mixed { get; init; }

        /// <summary>Sub-tiles of two grounds split by triangle, the two being unable to run as one program.</summary>
        public int Split { get; init; }

        /// <summary>
        /// Sub-tiles of two grounds whose template names no mask, mixed by the second ground's share of their corners - CHOSEN from the
        /// live client (v0.1.350): the stage sweep left them plain in 5,329 rooms (v0.1.348). The engine mixes them by its own mask,
        /// read through per-tile transforms no file holds (GroundGenerateWeights_2a); the corners' share is the stand-in the shapes
        /// across the mask's atlas take already (v0.1.310). A tile with no ground grid has no sub-tile corners to mix by, and stays plain.
        /// </summary>
        public int Maskless { get; init; }

        /// <summary>Whether the sub-tiles took their own corners from the definition's ground grid.</summary>
        public bool Gridded { get; init; }

        /// <summary>Sub-tiles with a corner the grid leaves unnamed, which takes the MaterialsList's group with no name.</summary>
        public int Unnamed { get; init; }

        /// <summary>Sub-tiles of three or four grounds drawn as the two most of their corners name - see SetOf.</summary>
        public int Reduced { get; init; }

        /// <summary>Sub-tiles of a tile with no grid, an own corner of which is unnamed - the group with no name; see SetOf.</summary>
        public int Bare { get; init; }

        /// <summary>Sub-tiles naming a ground the MaterialsList does not - the group with no name; see SetOf.</summary>
        public int Unlisted { get; init; }

        /// <summary>The grounds those name, by name.</summary>
        public IReadOnlyList<string> UnlistedNames { get; init; } = [];

        /// <summary>Sub-tiles of two grounds whose groups are alike, drawn as one - see SetOf.</summary>
        public int Alike { get; init; }

        /// <summary>Sub-tiles left plain, by why.</summary>
        public IReadOnlyDictionary<string, int> Plain { get; init; } = new Dictionary<string, int>();

        /// <summary>The tile, its template and the mask the template names - what the stage sweep names a plain ground by; empty where the definition did not read.</summary>
        public string Subject { get; init; } = string.Empty;

        /// <summary>Sub-tiles split by triangle, by why the two could not be mixed.</summary>
        public IReadOnlyDictionary<string, int> Splits { get; init; } = new Dictionary<string, int>();

        /// <summary>The block's vertex each copy past its own is of, for the shapes mixed by their corners - see the class's remarks.</summary>
        public int[] Extra { get; init; } = [];

        /// <summary>The second ground's share at each copy, its sub-tile's corners' bilinear.</summary>
        public float[] Shares { get; init; } = [];

        /// <summary>The block's positions with the copies after them, or null where there are none - the same for every piece laid.</summary>
        public Vector3[]? Positions { get; init; }

        /// <summary>The block's normals likewise.</summary>
        public Vector3[]? Normals { get; init; }

        /// <summary>Sub-tiles of two grounds with shapes across the mask's atlas mixed by their corners.</summary>
        public int ByCorners { get; init; }

        /// <summary>The shapes those are, each counted once a sub-tile.</summary>
        public int CornerShapes { get; init; }

        /// <summary>Sub-tiles of two grounds with shapes across the mask's atlas left on its cells - by the switch, or the corners' mix not running.</summary>
        public int KeptOnCells { get; init; }

        public static Plan Plained(string why, int subTiles) => new([], [], [], []) { Plain = new Dictionary<string, int> { [why] = subTiles } };
    }

    /// <summary>The triangles of the block whose sub-tiles name one set of grounds.</summary>
    private sealed class Run(List<(int Order, GroundGroup Group)> grounds, string why)
    {
        public List<(int Order, GroundGroup Group)> Grounds { get; } = grounds;

        public string Why { get; } = why;

        public List<int> Triangles { get; } = [];

        /// <summary>Each sub-tile of the run: its place in the tile's list of sub-tiles and its run of the block's triangles.</summary>
        public List<(int SubTile, int First, int Count)> Pieces { get; } = [];

        public int SubTiles { get; set; }
    }

    /// <summary>
    /// A tile's ground worked out sub-tile by sub-tile: each sub-tile's grounds from its own corners in the definition's ground grid
    /// (TileDefinition.GroundGrid) - the tile's four corners where it has none - and the sub-tiles naming one set painted alike.
    /// </summary>
    /// <remarks>
    /// THE GRID, NOT THE TILE'S CORNERS (v0.1.248). Port's harbour names two grounds at its corners and four in its grid: its land
    /// stands on PortTownTrim and the tropical fill, and its ground shapes there carry coordinates laid across the mask a third a
    /// sub-tile - read through newmask.dds, an atlas of transitions, they came out as a checkerboard of black and cobblestone
    /// (v0.1.247's capture, ground-shapes-12-Port_Boss_01.png). A sub-tile whose corners name one ground is drawn in it, its mask
    /// unread, as the engine's GroundGenerateWeights_1a draws one; two are mixed as before, in the list's order.
    /// </remarks>
    private Plan Planned(string tile, SkinnedMesh ground, IReadOnlyList<GroundPiece> pieces)
    {
        (TileDefinition definition, string why) = TileModels.Defined(_read, tile);
        if (!definition.Ready)
        {
            return Plan.Plained("whose definition did not read: " + why, Math.Max(1, pieces.Count));
        }

        // EACH SUB-TILE'S OWN CORNERS where the block's pieces add up to it and the definition has its grid; else one run, the tile's corners.
        int triangles = ground.Indices.Length / 3;
        bool gridded = definition.GroundGrid.Count > 0 && pieces.Count > 0 && pieces.Sum(one => one.Triangles) == triangles;
        List<(int Triangles, IReadOnlyList<string> Corners)> subTiles = gridded
            ? [.. pieces.Select(one => (one.Triangles, definition.CornersOf(one.X, one.Y)))]
            : [(triangles, definition.Grounds)];

        var runs = new Dictionary<string, Run>(StringComparer.Ordinal);
        var order = new List<Run>();
        var at = 0;
        int unnamedCorners = 0, bareCorners = 0, unlistedSubTiles = 0, alikeSubTiles = 0, reducedSubTiles = 0, subTile = -1;
        var unlistedNames = new List<string>();
        foreach ((int count, IReadOnlyList<string> corners) in subTiles)
        {
            subTile++;
            GroundSet set = SetOf(corners, twoOfMore: true, alike: true);
            (string key, List<(int Order, GroundGroup Group)> grounds, string setWhy, bool reduced) = (set.Key, set.Grounds, set.Why, set.Reduced);
            bool unnamed = corners.Any(corner => corner.Length == 0);
            unnamedCorners += gridded && unnamed ? 1 : 0;
            bareCorners += !gridded && unnamed ? 1 : 0;
            unlistedSubTiles += set.Unlisted.Count > 0 ? 1 : 0;
            alikeSubTiles += set.Merged ? 1 : 0;
            foreach (string name in set.Unlisted)
            {
                if (!unlistedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    unlistedNames.Add(name);
                }
            }

            reducedSubTiles += reduced ? 1 : 0;
            if (!runs.TryGetValue(key, out Run? run))
            {
                run = new Run(grounds, setWhy);
                runs[key] = run;
                order.Add(run);
            }

            run.SubTiles++;
            run.Pieces.Add((subTile, at, count));
            for (var one = 0; one < count; one++)
            {
                run.Triangles.Add(at + one);
            }

            at += count;
        }

        string mask = definition.Templates.Count > 0 ? TileTemplate.Read(_read(Clean(definition.Templates[0]))).GroundMask : string.Empty;
        var indices = new List<int>(ground.Indices.Length);
        var shapes = new List<MeshShape>();
        var named = new List<(string Shape, string Material)>();
        var own = new bool[ground.Positions.Length];
        var plain = new Dictionary<string, int>(StringComparer.Ordinal);
        var splits = new Dictionary<string, int>(StringComparer.Ordinal);
        int oneGround = 0, mixedGrounds = 0, splitGrounds = 0, byCorners = 0, cornerShapes = 0, keptOnCells = 0, maskless = 0;
        var extra = new List<int>();
        var shares = new List<float>();
        var copies = new Dictionary<int, int>();
        int[]? shapeOf = null;

        void Shape(IEnumerable<int> triangleList, string? material, bool copied = false, Dictionary<int, int>? copyMap = null)
        {
            int from = indices.Count;
            Dictionary<int, int> copiedAs = copyMap ?? copies;
            foreach (int triangle in triangleList)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    int vertex = ground.Indices[(triangle * 3) + corner];
                    indices.Add(copied ? copiedAs[vertex] : vertex);
                }
            }

            string name = shapes.Count.ToString(CultureInfo.InvariantCulture);
            shapes.Add(new MeshShape(name, from, indices.Count - from));
            if (material is not null)
            {
                named.Add((name, material));
            }
        }

        void Plain(Run run, string reason)
        {
            plain[reason] = plain.GetValueOrDefault(reason) + run.SubTiles;
            Shape(run.Triangles, null);
        }

        foreach (Run run in order)
        {
            if (run.Why.Length > 0)
            {
                Plain(run, run.Why);
                continue;
            }

            string first = Heaviest(run.Grounds[0].Group);
            if (run.Grounds.Count == 1)
            {
                oneGround += run.SubTiles;
                Shape(run.Triangles, first);
                continue;
            }

            string second = Heaviest(run.Grounds[1].Group);
            if (mask.Length == 0 || MaskOf(mask) is not { } picture)
            {
                // NO MASK: MIXED BY THE SUB-TILES' CORNERS where the grid names them - see Plan.Maskless.
                if (mask.Length == 0 && gridded && _paints.GroundsByCorners(_read, first, second, GroundScale, _heights, out _) is { } byTheCorners)
                {
                    // ITS OWN COPIES: a vertex this run shares with a run of another pair of grounds is that run's at another share.
                    var all = new List<int>(run.Triangles.Count);
                    var runCopies = new Dictionary<int, int>();
                    foreach ((int piece, int first0, int count) in run.Pieces)
                    {
                        int from = all.Count;
                        for (int triangle = first0; triangle < first0 + count; triangle++)
                        {
                            all.Add(triangle);
                        }

                        Copied(ground, all, from, all.Count, pieces[piece], Seconds(subTiles[piece].Corners, run.Grounds[1].Order), extra, shares, runCopies);
                    }

                    maskless += run.SubTiles;
                    Shape(all, byTheCorners, copied: true, copyMap: runCopies);
                    continue;
                }

                Plain(run, mask.Length == 0 ? "of two grounds whose template names no mask, its tile carrying no ground grid to mix them by" : "of two grounds whose template's mask does not read");
                continue;
            }

            if (!ground.Coordinated)
            {
                Plain(run, "of two grounds whose block carries no coordinates into its mask");
                continue;
            }

            // MIXED PER PIXEL as the engine mixes them, where the two grounds' programs run as one - see the remarks.
            if (_paints.Grounds(_read, first, second, Clean(mask), GroundScale, _heights, out string unmixed) is { } mixed)
            {
                mixedGrounds += run.SubTiles;

                // THE SHAPES ACROSS THE MASK'S ATLAS BY THEIR CORNERS, each sub-tile's own - see the class's remarks.
                List<int> onCells = run.Triangles;
                var wide = new List<int>();
                string? byCornersName = null;
                if (gridded)
                {
                    shapeOf ??= ShapesOf(ground, triangles);
                    var narrow = new List<int>(run.Triangles.Count);
                    int wideShapes = 0, wideSubTiles = 0;
                    var firstOfWide = new List<(int SubTile, int From)>();
                    foreach ((int piece, int first0, int count) in run.Pieces)
                    {
                        int from = wide.Count;
                        int found = Across(ground, shapeOf, first0, count, picture, narrow, wide);
                        wideShapes += found;
                        wideSubTiles += found > 0 ? 1 : 0;
                        if (wide.Count > from)
                        {
                            firstOfWide.Add((piece, from));
                        }
                    }

                    if (wide.Count > 0 && !_maskCells && _paints.GroundsByCorners(_read, first, second, GroundScale, _heights, out _) is { } cornered)
                    {
                        onCells = narrow;
                        byCorners += wideSubTiles;
                        cornerShapes += wideShapes;
                        for (var one = 0; one < firstOfWide.Count; one++)
                        {
                            (int piece, int from) = firstOfWide[one];
                            int to = one + 1 < firstOfWide.Count ? firstOfWide[one + 1].From : wide.Count;
                            Copied(ground, wide, from, to, pieces[piece], Seconds(subTiles[piece].Corners, run.Grounds[1].Order), extra, shares, copies);
                        }

                        byCornersName = cornered;
                    }
                    else
                    {
                        keptOnCells += wideSubTiles;
                    }
                }

                foreach (int triangle in onCells)
                {
                    own[ground.Indices[triangle * 3]] = own[ground.Indices[(triangle * 3) + 1]] = own[ground.Indices[(triangle * 3) + 2]] = true;
                }

                if (onCells.Count > 0)
                {
                    Shape(onCells, mixed);
                }

                if (byCornersName is not null)
                {
                    Shape(wide, byCornersName, copied: true);
                }

                continue;
            }

            // ELSE EACH TRIANGLE TO THE GROUND ITS VERTICES' GREEN AVERAGES TO, the first's then the second's.
            splitGrounds += run.SubTiles;
            splits[unmixed] = splits.GetValueOrDefault(unmixed) + run.SubTiles;
            var firsts = new List<int>(run.Triangles.Count);
            var seconds = new List<int>(run.Triangles.Count / 2);
            foreach (int triangle in run.Triangles)
            {
                float mean = 0f;
                for (var corner = 0; corner < 3; corner++)
                {
                    Vector2 uv = ground.Coordinates[ground.Indices[(triangle * 3) + corner]];
                    mean += Sample(picture, 1, uv.X, uv.Y) / 3f;
                }

                (mean >= Half ? seconds : firsts).Add(triangle);
            }

            Shape(firsts, first);
            Shape(seconds, second);
        }

        // THE COPIES' PLACES ONCE, whatever number of pieces lay the tile: they lie in the block's own frame.
        Vector3[]? positions = null, normals = null;
        if (extra.Count > 0)
        {
            int vertices = ground.Positions.Length;
            positions = new Vector3[vertices + extra.Count];
            normals = new Vector3[positions.Length];
            ground.Positions.CopyTo(positions, 0);
            ground.Normals.CopyTo(normals, 0);
            for (var copy = 0; copy < extra.Count; copy++)
            {
                positions[vertices + copy] = ground.Positions[extra[copy]];
                normals[vertices + copy] = ground.Normals[extra[copy]];
            }
        }

        return new Plan([.. indices], shapes, named, own)
        {
            Positions = positions,
            Normals = normals,
            One = oneGround,
            Mixed = mixedGrounds,
            Split = splitGrounds,
            Gridded = gridded,
            Unnamed = unnamedCorners,
            Bare = bareCorners,
            Unlisted = unlistedSubTiles,
            UnlistedNames = unlistedNames,
            Alike = alikeSubTiles,
            Reduced = reducedSubTiles,
            Plain = plain,
            Subject = $"{tile} template {(definition.Templates.Count > 0 ? Clean(definition.Templates[0]) : "none")} mask {(mask.Length > 0 ? Clean(mask) : "none")}",
            Splits = splits,
            Extra = [.. extra],
            Shares = [.. shares],
            ByCorners = byCorners,
            Maskless = maskless,
            CornerShapes = cornerShapes,
            KeptOnCells = keptOnCells,
        };
    }

    /// <summary>The block's shape each triangle is of, by its place in the block's shapes, or -1 for none.</summary>
    private static int[] ShapesOf(SkinnedMesh ground, int triangles)
    {
        var shapeOf = new int[triangles];
        Array.Fill(shapeOf, -1);
        for (var index = 0; index < ground.Shapes.Count; index++)
        {
            MeshShape shape = ground.Shapes[index];
            int to = Math.Min(triangles, (shape.From + shape.Count) / 3);
            for (int triangle = Math.Max(0, shape.From / 3); triangle < to; triangle++)
            {
                shapeOf[triangle] = index;
            }
        }

        return shapeOf;
    }

    /// <summary>
    /// One sub-tile's triangles, shape by shape, into those whose shape's block coordinates reach a cell of the mask's atlas or more
    /// across - <paramref name="wide"/> - and the rest; how many shapes were wide.
    /// </summary>
    /// <remarks>
    /// THE CAPTURE'S RULE (CaptureGroundScreen, "across the mask's atlas"): the coordinates' reach over the shape's vertices in the
    /// sub-tile, in the mask's texels, a cell or more along either axis.
    /// </remarks>
    private static int Across(SkinnedMesh ground, int[] shapeOf, int first, int count, GamePicture mask, List<int> narrow, List<int> wide)
    {
        int found = 0, end = first + count;
        for (int start = first; start < end;)
        {
            int shape = shapeOf[start], stop = start + 1;
            while (stop < end && shapeOf[stop] == shape)
            {
                stop++;
            }

            float leastU = float.MaxValue, mostU = float.MinValue, leastV = float.MaxValue, mostV = float.MinValue;
            for (int index = start * 3; index < stop * 3; index++)
            {
                Vector2 uv = ground.Coordinates[ground.Indices[index]];
                leastU = MathF.Min(leastU, uv.X);
                mostU = MathF.Max(mostU, uv.X);
                leastV = MathF.Min(leastV, uv.Y);
                mostV = MathF.Max(mostV, uv.Y);
            }

            bool across = (mostU - leastU) * mask.Width >= AtlasCell || (mostV - leastV) * mask.Height >= AtlasCell;
            found += across ? 1 : 0;
            for (int triangle = start; triangle < stop; triangle++)
            {
                (across ? wide : narrow).Add(triangle);
            }

            start = stop;
        }

        return found;
    }

    /// <summary>Which of a sub-tile's four corners, in the grid's order, are the second ground's - one, or nought for the first's or any other.</summary>
    private float[] Seconds(IReadOnlyList<string> corners, int second)
    {
        var seconds = new float[4];
        for (var corner = 0; corner < seconds.Length && corner < corners.Count; corner++)
        {
            int order = OrderOf(corners[corner], out _);
            seconds[corner] = order >= 0 && _alike[order] == second ? 1f : 0f;
        }

        return seconds;
    }

    /// <summary>
    /// A copy of every vertex of one sub-tile's wide triangles, each with the second ground's share at it - the corners' bilinear at
    /// its place in the sub-tile, the frame TileDefinition.CornersOf numbers them in.
    /// </summary>
    private static void Copied(
        SkinnedMesh ground, List<int> wide, int from, int to, GroundPiece piece, float[] seconds, List<int> extra, List<float> shares, Dictionary<int, int> copies)
    {
        // The sub-tile's own corner in the block: column x - 1, row 1 - y, a tile each way (as CaptureGroundCompare lays it).
        float left = (piece.X - 1) * TileModels.Side, bottom = (1 - piece.Y) * TileModels.Side;
        int count = ground.Positions.Length;
        for (int at = from; at < to; at++)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                int vertex = ground.Indices[(wide[at] * 3) + corner];
                if (copies.ContainsKey(vertex))
                {
                    continue;
                }

                Vector3 place = ground.Positions[vertex];
                float s = Math.Clamp((place.X - left) / TileModels.Side, 0f, 1f), t = Math.Clamp((place.Y - bottom) / TileModels.Side, 0f, 1f);
                copies[vertex] = count + extra.Count;
                extra.Add(vertex);
                shares.Add(CaptureGroundCompare.Share(seconds, s, t));
            }
        }
    }

    /// <summary>
    /// The grounds a sub-tile's corners name, by name in the MaterialsList's order - the order the engine's weights follow (see the
    /// remarks) - with a key the sub-tiles naming the same set share; or why it stays plain.
    /// </summary>
    /// <remarks>
    /// A CORNER THE GRID LEAVES UNNAMED takes the MaterialsList's group with no name. MEASURED (v0.1.248's capture,
    /// ground-grid-replay.txt): where Port's harbour grid names no ground, along the pier, the game holds the null slot of its
    /// ground-type list at the same corners, and draws the pier cobblestone; the list's one group without a name is all cobblestone.
    /// DECIDED, not measured: that the game draws that group for its null slot. A TILE'S OWN FOUR CORNERS, standing in where there is
    /// no grid, take it too from v0.1.344 (DECIDED from the live client, 2026-10-29): before, an unnamed one named nothing, and 18 of
    /// Crimson Shores' boss room's sub-tiles - its arena's stromatolite tiles, which carry no grid - stayed white.
    ///
    /// A GROUND THE LIST DOES NOT NAME takes the group with no name as well - CHOSEN from the live client (2026-10-29) until a capture
    /// measures it (CaptureGroundChoices runs every material of the list there). Crimson Shores lays StromatoliteDeep and
    /// StromatoliteShallow, the sea floor along its shore, and its materials.mtd names neither: 106 of the boss room's sub-tiles stayed
    /// white, and the shallow water over them looked white too.
    ///
    /// GROUPS ALIKE ARE ONE GROUND, where <paramref name="alike"/>: two groups offering the same materials at the same weights are drawn
    /// as the first of them, the mix of a material with itself being that material whatever the mask (Crimson Shores' list offers the
    /// same three sands for seven of its eight groups, and its arena's tiles carry no mask to mix them by).
    ///
    /// THREE OR FOUR GROUNDS, where <paramref name="twoOfMore"/>, are drawn as the two most of the corners name - ties to the one the
    /// MaterialsList lists first - mixed as two are. CHOSEN from the live client (2026-10-13), a stand-in: the engine mixes them with
    /// its own mask's red as well as the template's green (GroundGenerateWeights_2a1b, 3a1b, 2a2b), read through two per-tile
    /// transforms no file holds, and left plain they drew as white squares along Port's quay - c14r12 under the tarpaulin crate
    /// (unnamed, IslandOcean, unnamed, TropicalFill) among them, where the game draws the quay's top as its neighbours'.
    /// </remarks>
    private GroundSet SetOf(IReadOnlyList<string> corners, bool twoOfMore = false, bool alike = false)
    {
        var grounds = new List<(int Order, GroundGroup Group)>(4);
        Span<int> named = stackalloc int[4];
        List<string>? unlisted = null;
        Span<int> raw = stackalloc int[4];
        var raws = 0;
        foreach (string corner in corners)
        {
            int order = OrderOf(corner, out bool listed);
            if (!listed)
            {
                unlisted ??= [];
                string name = NameOf(corner);
                if (!unlisted.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    unlisted.Add(name);
                }

                if (corner.Length > 0)
                {
                    _unlistedFiles.TryAdd(name, corner);
                }
            }

            if (order < 0)
            {
                if (corner.Length == 0)
                {
                    continue;
                }

                return Plained("with a ground the MaterialsList does not list");
            }

            if (raws < raw.Length && raw[..raws].IndexOf(order) < 0)
            {
                raw[raws++] = order;
            }

            order = alike ? _alike[order] : order;

            int at = grounds.FindIndex(one => one.Order == order);
            if (at < 0)
            {
                at = grounds.Count;
                grounds.Add((order, _list.Groups[order]));
            }

            if (at < named.Length)
            {
                named[at]++;
            }
        }

        var reduced = false;
        if (grounds.Count > 2 && twoOfMore && grounds.Count <= named.Length)
        {
            // THE TWO MOST NAMED, ties to the list's first: picked by count, then laid back in the list's order.
            var ranked = new List<(int Order, GroundGroup Group, int Named)>(grounds.Count);
            for (var at = 0; at < grounds.Count; at++)
            {
                ranked.Add((grounds[at].Order, grounds[at].Group, named[at]));
            }

            ranked.Sort((one, other) => one.Named != other.Named ? other.Named.CompareTo(one.Named) : one.Order.CompareTo(other.Order));
            grounds = [(ranked[0].Order, ranked[0].Group), (ranked[1].Order, ranked[1].Group)];
            reduced = true;
        }

        grounds.Sort((one, other) => one.Order.CompareTo(other.Order));
        IReadOnlyList<string> unnamed = unlisted ?? (IReadOnlyList<string>)[];
        return grounds.Count switch
        {
            0 => Plained("whose corners name no ground"),
            > 2 => Plained("of three or four grounds (the engine's atlas, placed by transforms no file holds)"),
            _ => new GroundSet(string.Join(",", grounds.Select(one => one.Order.ToString(CultureInfo.InvariantCulture))), grounds, string.Empty, reduced)
            {
                Unlisted = unnamed,
                Merged = grounds.Count == 1 && raws > 1,
            },
        };

        GroundSet Plained(string why) => new("plain: " + why, [], why, false) { Unlisted = unlisted ?? (IReadOnlyList<string>)[] };
    }

    /// <summary>
    /// A corner's group in the MaterialsList, by its place there, or -1 - an unnamed corner, or one naming a ground the list does not
    /// name (<paramref name="listed"/> false), the group with no name (see <see cref="SetOf"/>).
    /// </summary>
    private int OrderOf(string corner, out bool listed)
    {
        listed = true;
        if (corner.Length == 0)
        {
            return _unnamedGroup;
        }

        string name = NameOf(corner);
        for (var at = 0; at < _list.Groups.Count; at++)
        {
            if (string.Equals(_list.Groups[at].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return at;
            }
        }

        listed = false;
        return _unnamedGroup;
    }

    /// <summary>A corner's group by its place in the MaterialsList, as the painter takes it (see <see cref="OrderOf"/>) - for the ground's scatter.</summary>
    internal int GroupOrder(string corner) => OrderOf(corner, out _);

    /// <summary>The MaterialsList's group at a place in it - see <see cref="GroupOrder"/>.</summary>
    internal GroundGroup GroupAt(int order) => _list.Groups[order];

    /// <summary>A sub-tile's grounds as SetOf works them out - see there.</summary>
    /// <param name="Key">What the sub-tiles naming the same set share.</param>
    /// <param name="Grounds">The groups, by their place in the list, in its order.</param>
    /// <param name="Why">Why the sub-tile stays plain, or empty.</param>
    /// <param name="Reduced">Whether three or four grounds were taken as their two most named.</param>
    private sealed record GroundSet(string Key, List<(int Order, GroundGroup Group)> Grounds, string Why, bool Reduced)
    {
        /// <summary>The grounds its corners name that the list does not, by name - taken as the group with no name.</summary>
        public IReadOnlyList<string> Unlisted { get; init; } = [];

        /// <summary>Whether two grounds came to one, their groups alike.</summary>
        public bool Merged { get; init; }
    }

    /// <summary>Whether two groups offer the same materials at the same weights - and nothing else apart - so that either draws as the other.</summary>
    private static bool Alike(GroundGroup one, GroundGroup other)
        => one.Choices.Count == other.Choices.Count
            && one.Choices.Zip(other.Choices).All(pair => string.Equals(Clean(pair.First.Material), Clean(pair.Second.Material), StringComparison.OrdinalIgnoreCase))
            && one.Weights.SequenceEqual(other.Weights)
            && one.Trailing == other.Trailing
            && one.ExtraNumber == other.ExtraNumber
            && one.ExtraFlag == other.ExtraFlag
            && one.Extras.Count == other.Extras.Count
            && one.Extras.Zip(other.Extras).All(pair => string.Equals(Clean(pair.First.Material), Clean(pair.Second.Material), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Each sub-tile's corners and the grounds they come to, as <see cref="Paint"/> works them out - for the capture's picture of the
    /// ground on the screen (CaptureGroundScreen) - and the template's mask with the picture it decodes to.
    /// </summary>
    /// <param name="tile">The tile's <c>.tdt</c>.</param>
    /// <param name="pieces">Which sub-tile each run of the block's triangles is, in the block's order.</param>
    /// <param name="triangles">The block's triangles, which the pieces must add up to for the grid to be read - as Paint asks.</param>
    internal (IReadOnlyList<SubTileGrounds> SubTiles, string Mask, GamePicture? Picture) SubTilesOf(string tile, IReadOnlyList<GroundPiece> pieces, int triangles)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        (TileDefinition definition, string why) = TileModels.Defined(_read, tile);
        if (!definition.Ready)
        {
            return ([.. pieces.Select(_ => new SubTileGrounds([], [], "whose definition did not read: " + why))], string.Empty, null);
        }

        bool gridded = definition.GroundGrid.Count > 0 && pieces.Count > 0 && pieces.Sum(one => one.Triangles) == triangles;
        var subTiles = new SubTileGrounds[pieces.Count];
        for (var at = 0; at < pieces.Count; at++)
        {
            IReadOnlyList<string> corners = gridded ? definition.CornersOf(pieces[at].X, pieces[at].Y) : definition.Grounds;
            GroundSet set = SetOf(corners);
            List<(int Order, GroundGroup Group)> grounds = set.Grounds;
            bool[] seconds = grounds.Count == 2 && corners.Count == 4 ? [.. corners.Select(corner => OrderOf(corner, out _) == grounds[1].Order)] : [];
            subTiles[at] = new SubTileGrounds(corners, [.. grounds.Select(one => one.Group.Name)], set.Why)
            {
                Unlisted = set.Unlisted,
                Everything = set.Unlisted.Count > 0 ? Everything : [],
                Gridded = gridded,
                Seconds = seconds,
                Materials = [.. grounds.Select(one => Heaviest(one.Group))],
                Choices = [.. grounds.Select(one => ChoicesOf(one.Group))],
            };
        }

        string mask = definition.Templates.Count > 0 ? TileTemplate.Read(_read(Clean(definition.Templates[0]))).GroundMask : string.Empty;
        return (subTiles, mask, mask.Length > 0 ? MaskOf(mask) : null);
    }

    /// <summary>Whether the MaterialsList has a group for a ground type (<c>.gt</c>), by the name its file spells.</summary>
    internal bool Lists(string groundType)
    {
        string name = NameOf(groundType);
        return name.Length > 0 && _list.Groups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every material the MaterialsList offers, each once in the list's order - what a ground it does not name may be drawn with.</summary>
    private IReadOnlyList<(string Material, int Weight)> Everything => _everything ??=
        [.. _list.Groups.SelectMany(group => group.Choices).Select(choice => choice.Material).Distinct(StringComparer.OrdinalIgnoreCase).Select(material => (material, 0))];

    private IReadOnlyList<(string Material, int Weight)>? _everything;

    /// <summary>A group's choices with their weights, in file order - nought for a lone choice, whose weight the file omits.</summary>
    private static IReadOnlyList<(string Material, int Weight)> ChoicesOf(GroundGroup group)
        => [.. group.Choices.Select((choice, at) => (choice.Material, at < group.Weights.Count ? group.Weights[at] : 0))];

    /// <summary>A group's heaviest choice - its only one where it has one.</summary>
    private static string Heaviest(GroundGroup group)
    {
        int best = 0;
        for (var at = 1; at < group.Choices.Count && at < group.Weights.Count; at++)
        {
            best = group.Weights[at] > group.Weights[best] ? at : best;
        }

        return group.Choices.Count > 0 ? group.Choices[best].Material : string.Empty;
    }

    private string NameOf(string ground)
    {
        if (!_names.TryGetValue(ground, out string? name))
        {
            name = GroundType.Name(_read(Clean(ground)));
            _names[ground] = name;
        }

        return name;
    }

    private GamePicture? MaskOf(string path)
    {
        if (!_masks.TryGetValue(path, out GamePicture? picture))
        {
            picture = GameArt.Decode(GameArt.ReadRaw(_read, Clean(path))) is { Ready: true } decoded ? decoded : null;
            _masks[path] = picture;
        }

        return picture;
    }

    /// <summary>One channel at a texture coordinate, nought to one, as a linear sampler that wraps reads it.</summary>
    internal static float Sample(GamePicture picture, int channel, float u, float v)
    {
        float x = (u * picture.Width) - 0.5f, y = (v * picture.Height) - 0.5f;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        float At(int column, int row)
        {
            int wrappedX = ((column % picture.Width) + picture.Width) % picture.Width;
            int wrappedY = ((row % picture.Height) + picture.Height) % picture.Height;
            return picture.Rgba[(((wrappedY * picture.Width) + wrappedX) * 4) + channel] / 255f;
        }

        float top = (At(x0, y0) * (1 - fx)) + (At(x0 + 1, y0) * fx);
        float bottom = (At(x0, y0 + 1) * (1 - fx)) + (At(x0 + 1, y0 + 1) * fx);
        return (top * (1 - fy)) + (bottom * fy);
    }

    private static string Clean(string path) => path.Replace('\\', '/').Trim();
}

/// <summary>One sub-tile's ground as GroundPainter works it out - see GroundPainter.SubTilesOf.</summary>
/// <param name="Corners">Its four corners' ground types (<c>.gt</c>), empty where unnamed - from the definition's grid, or the tile's own four.</param>
/// <param name="Grounds">The MaterialsList groups they come to, by name in the list's order - empty for the group with none.</param>
/// <param name="Why">Why it stays plain, or empty.</param>
internal sealed record SubTileGrounds(IReadOnlyList<string> Corners, IReadOnlyList<string> Grounds, string Why)
{
    /// <summary>Whether the corners are the sub-tile's own from the definition's grid, rather than the tile's four standing in for it.</summary>
    public bool Gridded { get; init; }

    /// <summary>For a sub-tile of two grounds, which of its four corners - in <see cref="Corners"/>' order - are the second ground's; empty else.</summary>
    public IReadOnlyList<bool> Seconds { get; init; } = [];

    /// <summary>The material each of <see cref="Grounds"/> is painted with - its group's heaviest choice, as Paint takes it.</summary>
    public IReadOnlyList<string> Materials { get; init; } = [];

    /// <summary>Every material each of <see cref="Grounds"/>' groups offers, with its weight - what the game draws one from (CaptureGroundChoices).</summary>
    public IReadOnlyList<IReadOnlyList<(string Material, int Weight)>> Choices { get; init; } = [];

    /// <summary>The grounds its corners name that the MaterialsList does not, by name - painted as the group with no name, CHOSEN; see GroundPainter.SetOf.</summary>
    public IReadOnlyList<string> Unlisted { get; init; } = [];

    /// <summary>Where <see cref="Unlisted"/> names any, every material the list offers, weighed nought - what CaptureGroundChoices holds there; else empty.</summary>
    public IReadOnlyList<(string Material, int Weight)> Everything { get; init; } = [];
}

/// <summary>
/// The name a tile's ground of two grounds wears as its material when the two are mixed per pixel - see GroundPainter and
/// MonsterModels.Paints.Grounds - and how a picture's lines say it.
/// </summary>
/// <remarks>
/// A NAME, NOT A FILE: every list a shape's material travels through - the pile, the join, the stage, the probe, the capture - carries
/// it as it carries a material's path, and the paint cache that made the program answers for it.
/// </remarks>
internal static class GroundBlend
{
    private const string With = " mixed with ";

    private const string By = " by the green of ";

    private const string ByCorners = " by its sub-tiles' corners";

    private const string ByHeights = ", weighed by their heights";

    /// <summary>The name for two grounds mixed by a mask - weighed by their heights where <paramref name="heights"/>.</summary>
    public static string Name(string first, string second, string mask, bool heights = false)
        => first + With + second + By + mask + (heights ? ByHeights : string.Empty);

    /// <summary>The name for two grounds mixed by the second's share of the sub-tile's corners - see GroundPainter - weighed by their heights where <paramref name="heights"/>.</summary>
    public static string Cornered(string first, string second, bool heights = false)
        => first + With + second + ByCorners + (heights ? ByHeights : string.Empty);

    /// <summary>Whether a name says its two grounds are weighed by their heights where their graphs turn that on (ShadeProgram.Grounds).</summary>
    public static bool Heighted(string material) => material.EndsWith(ByHeights, StringComparison.Ordinal);

    /// <summary>The two grounds and the mask a name says - the mask empty where they are mixed by the corners - or false where it is a material's own path.</summary>
    public static bool Parts(string material, out string first, out string second, out string mask)
    {
        first = second = mask = string.Empty;
        if (Heighted(material))
        {
            material = material[..^ByHeights.Length];
        }

        int with = material.IndexOf(With, StringComparison.Ordinal);
        if (with <= 0)
        {
            return false;
        }

        if (material.EndsWith(ByCorners, StringComparison.Ordinal) && material.Length - ByCorners.Length > with + With.Length)
        {
            first = material[..with];
            second = material[(with + With.Length)..^ByCorners.Length];
            return true;
        }

        int by = material.LastIndexOf(By, StringComparison.Ordinal);
        if (by <= with)
        {
            return false;
        }

        first = material[..with];
        second = material[(with + With.Length)..by];
        mask = material[(by + By.Length)..];
        return true;
    }

    /// <summary>The name as a picture's lines say it - the files' own names.</summary>
    public static string Short(string material)
        => Parts(material, out string first, out string second, out string mask)
            ? (mask.Length > 0 ? $"{Tail(first)} + {Tail(second)} by {Tail(mask)}'s green" : $"{Tail(first)} + {Tail(second)} by the sub-tiles' corners")
                + (Heighted(material) ? ByHeights : string.Empty)
            : Tail(material);

    private static string Tail(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
}
