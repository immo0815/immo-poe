using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The ground's own scatter, strewn by the tool over a painted ground: the grass, ferns, fallen leaves and strewn weapons each ground
/// material's doodad layers (<c>.dlp</c>) carry, and the master's doodad list (<c>.ddt</c>) - what the client strews itself, where no
/// file says where.
/// </summary>
/// <remarks>
/// THE TOOL STREWS IT ITSELF, with a fixed seed (DECIDED from the live client, 2026-10-30): a scene built away from its area could read
/// nothing from memory either, and the client keeps none of it as an entity - v0.1.360's capture of The Red Vale held a handful of the
/// list's rusted weapons among 1,243 entities, each one a room's own line, and none of the layers' meshes.
///
/// WHAT THE FILES SAY, MEASURED on that capture (ground-scatter.txt): a layer's points lie on a grid of thirty-seconds of the unit square
/// and no two of its meshes share a cell - one pattern dealt out among the meshes (RVL_SwordScatter01 49 of 49, RVL_SwordScatter02 588
/// of 588); and in RVL_SwordScatter01 every mesh carries round(4 x its number) points, all nine of them. The number is a ninth's
/// multiple throughout (23/9, 16/9, 11/9, 7/9, 2/9), and RVL_SwordScatter02's is 10 for nine meshes of 39 to 94 points - a cap - and
/// 88/9 for its tenth of 39. JungleFoliageA_Redvale's points lie off the grid and owe its numbers nothing.
///
/// THE READING, DECIDED from the live client (2026-10-30): the number is a mesh's count a tile, and the points a pattern over two
/// tiles by two - 500 units, four tiles, the four the points hold; the ground's colour repeats every 500 units too (PBRGroundBN halves
/// the engine's world times 0.004, MEASURED on Port, v0.1.313). So a layer's mesh with points lays them every 500 units of the area's
/// own coordinates (<see cref="Pattern"/>) and one without strews its number a tile at random. A group's layers are each strewn at their
/// material's share of the group's weights - the painted ground staying its heaviest material, as GroundPainter draws it - and a sub-tile
/// of several grounds strews each ground's by its share of the sub-tile's four corners, bilinear (DECIDED: "all in proportion to their
/// weight"). The master's doodad list is strewn ONE A TILE (CHOSEN from the live client: it names no density), from the group named as
/// the ground's group is, else "Default".
///
/// CHOSEN beside them, and said under the picture: the seed (<see cref="Seed"/>), hashed with the area's own coordinates, so the same
/// spot strews alike however the block round it is laid - a list doodad that is a set takes its member by the same draw (see
/// MemberOf); a turn spread over the layer's MaxRotation, half of it either way of the mesh's own, a whole turn where it sets none; a
/// size between its RandomScale's two, shrunk towards MinEdgeScale where the ground carrying the layer gives way to another; each stood
/// upright on the ground's top under it - the least z of every template's ground over the place. The list's doodads at their files'
/// own size and a whole turn, a weight written "All" counted as one. A layer not AllowOnBlocking strews nothing on ground nobody walks
/// where the area's walkable grid is known - a built room's is not, and strews over all of it - and the list's doodads keep off it too.
/// A point on the pattern's edge, a coordinate of one (RVL_SwordScatter01 holds five), lies where the next square's nought does and is
/// laid there; a point the file writes at both is laid twice.
/// NOT READ: the number on the list's first line - 1 in The Red Vale, 0.25 in Marrow, the Titan Grotto and Traitor's Passage, "0.2 5" in
/// Port - which poe_data_tools calls a scale with a question mark; taken as a size it would shrink Port's doodads to a fifth.
/// NOT DRAWN: AllowWaving's sway, the AudioType and a layer's own Seed; a group's extra choices.
/// </remarks>
internal sealed class GroundScatter
{
    /// <summary>The side of a layer's pattern, in world units: two tiles - see the remarks.</summary>
    public const float Pattern = 2f * TileModels.Side;

    /// <summary>The master's doodad list's count a tile - CHOSEN, see the remarks.</summary>
    public const float ListPerTile = 1f;

    /// <summary>The fixed seed every choice is hashed from - CHOSEN, see the remarks.</summary>
    public const ulong Seed = 0x5EED_0F_6A0D_5CA7UL;

    /// <summary>The stub a strewn line carries, for the probe and the files' account.</summary>
    public const string Stub = "ground scatter";

    /// <summary>Positions are hashed in sixteenths of a unit.</summary>
    private const float Quantum = 16f;

    /// <summary>A ground triangle narrower than this across, seen from above, is a wall's and stands nothing on it.</summary>
    private const float Flat = 1e-3f;

    private readonly Func<string, byte[]?> _read;
    private readonly GroundPainter _painter;
    private readonly GroundDoodadList _list;
    private readonly string _listPath;
    private readonly Dictionary<string, IReadOnlyList<(int Weight, string Ao)>> _sets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Layer?> _layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, IReadOnlyList<(Layer Layer, float Share)>> _recipes = [];
    private readonly Dictionary<string, Cell[]> _cells = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, string> _unread = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(RoomDoodad One, Matrix4x4 Place)> _lines = [];
    private int _strewnCells;
    private int _blocked;
    private int _bare;
    private int _listed;
    private bool _walkKnown;

    private GroundScatter(Func<string, byte[]?> read, GroundPainter painter, GroundDoodadList list, string listPath)
    {
        _read = read;
        _painter = painter;
        _list = list;
        _listPath = listPath;
    }

    /// <summary>What was strewn, each line where it stands in the room's frame, in the order strewn.</summary>
    public IReadOnlyList<(RoomDoodad One, Matrix4x4 Place)> Lines => _lines;

    /// <summary>The scatter for a painter's MaterialsList and the master's doodad list, where the master names one.</summary>
    /// <param name="read">The install.</param>
    /// <param name="master">The master the painter was made for - its <c>Doodads</c> key names the list.</param>
    /// <param name="painter">The ground's painter, whose MaterialsList names the layers.</param>
    public static GroundScatter For(Func<string, byte[]?> read, string master, GroundPainter painter)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(painter);
        string clean = master.Replace('\\', '/').Trim();
        string named = clean.Length > 0 ? MasterFile.Read(read(clean)).Values.GetValueOrDefault("Doodads", string.Empty) : string.Empty;
        if (named.Length > 0)
        {
            foreach (string candidate in MasterFile.Candidates(clean, named))
            {
                GroundDoodadList list = GroundDoodadList.Read(read(candidate));
                if (list.Groups.Count > 0)
                {
                    return new GroundScatter(read, painter, list, candidate);
                }
            }
        }

        return new GroundScatter(read, painter, GroundDoodadList.None, string.Empty);
    }

    /// <summary>
    /// Strews one laid piece's ground: every sub-tile of it, each by the grounds its corners name.
    /// </summary>
    /// <param name="tile">The tile's <c>.tdt</c>.</param>
    /// <param name="ground">Its ground block, as the painter was handed it.</param>
    /// <param name="pieces">Which sub-tile each run of the block's triangles is - or none, the tile's four corners standing for all.</param>
    /// <param name="width">The tile's sub-tiles across.</param>
    /// <param name="height">Its sub-tiles down.</param>
    /// <param name="place">Where the piece is laid in the room's frame.</param>
    /// <param name="offset">The room's frame's place in the area: what turns a frame point into the area's own coordinates.</param>
    /// <param name="blocked">Whether a frame point is ground nobody walks - or null where the walkable grid is not known.</param>
    public void Strew(
        string tile, SkinnedMesh ground, IReadOnlyList<GroundPiece> pieces, int width, int height, Matrix4x4 place, Vector2 offset,
        Func<Vector2, bool>? blocked)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(pieces);
        _walkKnown |= blocked is not null;
        if (!ground.Ready || !Matrix4x4.Invert(place, out Matrix4x4 back))
        {
            return;
        }

        if (!_cells.TryGetValue(tile, out Cell[]? cells))
        {
            cells = CellsOf(tile, ground, pieces, width, height);
            _cells[tile] = cells;
        }

        Span<int> orders = stackalloc int[4];
        Span<float> shares = stackalloc float[4];
        var layers = new Dictionary<Layer, (int Count, int[] Orders, float[] Shares)>();
        foreach (Cell cell in cells)
        {
            // THE CELL IN THE ROOM'S FRAME: a sub-tile laid by turns and mirrors stays a square on the tiles' grid.
            (Vector2 least, Vector2 most) = Boxed(cell, place);
            int distinct = Distinct(cell.Orders, orders);
            if (distinct == 0)
            {
                continue;
            }

            // EACH LAYER ONCE A CELL, with every ground of it carrying the layer and the share it carries it at - so a layer two of its
            // grounds strew is one draw at each place, never two meshes on one spot.
            layers.Clear();
            for (var at = 0; at < distinct; at++)
            {
                foreach ((Layer layer, float share) in RecipeOf(orders[at]))
                {
                    if (!layers.TryGetValue(layer, out (int Count, int[] Orders, float[] Shares) carried))
                    {
                        carried = (0, new int[4], new float[4]);
                    }

                    carried.Orders[carried.Count] = orders[at];
                    carried.Shares[carried.Count] = share;
                    layers[layer] = (carried.Count + 1, carried.Orders, carried.Shares);
                }
            }

            int before = _lines.Count;
            foreach ((Layer layer, (int count, int[] carriedOrders, float[] carriedShares)) in layers)
            {
                StrewLayer(layer, cell, least, most, back, place, offset, blocked, carriedOrders.AsSpan(0, count), carriedShares.AsSpan(0, count), shares);
            }

            if (_list.Groups.Count > 0)
            {
                StrewList(cell, orders[..distinct], least, most, back, place, offset, blocked, shares);
            }

            _strewnCells += _lines.Count > before ? 1 : 0;
        }
    }

    /// <summary>What was strewn and on what it rests - for the line under the picture.</summary>
    public string Said()
    {
        var parts = new List<string>();
        foreach (Layer layer in _layers.Values.OfType<Layer>().Where(one => one.Laid > 0).OrderByDescending(one => one.Laid))
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{layer.Laid} of {Path.GetFileNameWithoutExtension(layer.Path)}"));
        }

        if (_listed > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{_listed} from {Path.GetFileName(_listPath)}"));
        }

        string unread = _unread.Count == 0 ? string.Empty
            : "; not read: " + string.Join(", ", _unread.Select(one => one.Key + " (" + one.Value + ")"));
        string left = (_blocked > 0 ? string.Create(CultureInfo.InvariantCulture, $"; {_blocked} kept off ground nobody walks") : string.Empty)
            + (_bare > 0 ? string.Create(CultureInfo.InvariantCulture, $"; {_bare} over no ground") : string.Empty)
            + (_walkKnown ? string.Empty : "; the walkable grid not known here, so strewn over ground nobody walks too");
        return string.Create(CultureInfo.InvariantCulture,
            $"ground scatter: {_lines.Count} strewn over {_strewnCells} sub-tiles{(parts.Count > 0 ? " - " + string.Join(", ", parts) : string.Empty)}{left}{unread}")
            + string.Create(CultureInfo.InvariantCulture,
                $" - a layer's points as a pattern every {Pattern:0} units, its number a tile where it has none, each at its material's share of the group's weights and its ground's share of the corners (DECIDED from the live client), the doodad list {ListPerTile:0} a tile at its files' own size (CHOSEN; its first line's number not read); turn, size, edge and seed CHOSEN; not drawn: waving");
    }

    /// <summary>One layer's meshes over one cell, each place kept by the shares of the grounds carrying the layer there.</summary>
    private void StrewLayer(
        Layer layer, Cell cell, Vector2 least, Vector2 most, Matrix4x4 back, Matrix4x4 place, Vector2 offset, Func<Vector2, bool>? blocked,
        ReadOnlySpan<int> carriers, ReadOnlySpan<float> carried, Span<float> shares)
    {
        for (var entry = 0; entry < layer.File.Entries.Count; entry++)
        {
            DoodadLayerEntry one = layer.File.Entries[entry];
            if (one.Points.Count > 0)
            {
                // THE PATTERN every Pattern units of the area's own coordinates, its squares over the cell - and one more each side, so a
                // point on a square's edge (a coordinate of one) is laid in the cell holding it, where the next square's nought lies.
                Vector2 low = least + offset, high = most + offset;
                int fromX = (int)MathF.Floor(low.X / Pattern) - 1, toX = (int)MathF.Floor(high.X / Pattern) + 1;
                int fromY = (int)MathF.Floor(low.Y / Pattern) - 1, toY = (int)MathF.Floor(high.Y / Pattern) + 1;
                for (int squareY = fromY; squareY <= toY; squareY++)
                {
                    for (int squareX = fromX; squareX <= toX; squareX++)
                    {
                        foreach ((float a, float b) in one.Points)
                        {
                            var area = new Vector2((squareX + a) * Pattern, (squareY + b) * Pattern);
                            Vector2 at = area - offset;
                            if (at.X < least.X || at.X >= most.X || at.Y < least.Y || at.Y >= most.Y)
                            {
                                continue;
                            }

                            Place(layer, one.Mesh, cell, at, Hash(area, layer.Id, entry, 0), back, place, blocked, carriers, carried, shares);
                        }
                    }
                }

                continue;
            }

            // ITS NUMBER A TILE where it has no points: the whole part always, the rest by a draw - the cell a tile square.
            Vector2 corner = least + offset;
            ulong drawn = Hash(corner, layer.Id, entry, -1);
            int count = (int)MathF.Floor(one.Number) + (Unit(drawn, 0) < one.Number - MathF.Floor(one.Number) ? 1 : 0);
            for (var candidate = 0; candidate < count; candidate++)
            {
                ulong hash = Hash(corner, layer.Id, entry, candidate + 1);
                var at = new Vector2(least.X + (Unit(hash, 1) * (most.X - least.X)), least.Y + (Unit(hash, 2) * (most.Y - least.Y)));
                Place(layer, one.Mesh, cell, at, hash, back, place, blocked, carriers, carried, shares);
            }
        }
    }

    /// <summary>One place of a layer's mesh, kept or not by its draw, stood on the ground and added.</summary>
    private void Place(
        Layer layer, string mesh, Cell cell, Vector2 at, ulong hash, Matrix4x4 back, Matrix4x4 place, Func<Vector2, bool>? blocked,
        ReadOnlySpan<int> carriers, ReadOnlySpan<float> carried, Span<float> shares)
    {
        Vector3 inBlock = Vector3.Transform(new Vector3(at, 0f), back);
        Shares(cell, inBlock, shares);
        float chance = 0f, ground = 0f;
        for (var one = 0; one < carriers.Length; one++)
        {
            float share = ShareOf(cell, carriers[one], shares);
            chance += carried[one] * share;
            ground += share;
        }

        if (Unit(hash, 3) >= chance)
        {
            return;
        }

        if (!layer.File.AllowOnBlocking && blocked?.Invoke(at) == true)
        {
            _blocked++;
            return;
        }

        if (HeightAt(cell, inBlock.X, inBlock.Y) is not { } z)
        {
            _bare++;
            return;
        }

        float size = float.Lerp(layer.File.ScaleMin, layer.File.ScaleMax, Unit(hash, 4)) * float.Lerp(layer.MinEdge, 1f, Math.Clamp(ground, 0f, 1f));
        float turn = (Unit(hash, 5) - 0.5f) * layer.Turn;
        Add(mesh, new Vector3(inBlock.X, inBlock.Y, z), size, turn, place);
        layer.Laid++;
    }

    /// <summary>The master's doodad list over one cell: its count a tile, each from the group its ground at that place names.</summary>
    private void StrewList(
        Cell cell, ReadOnlySpan<int> orders, Vector2 least, Vector2 most, Matrix4x4 back, Matrix4x4 place, Vector2 offset,
        Func<Vector2, bool>? blocked, Span<float> shares)
    {
        Vector2 corner = least + offset;
        ulong drawn = Hash(corner, ListId, 0, -1);
        int count = (int)MathF.Floor(ListPerTile) + (Unit(drawn, 0) < ListPerTile - MathF.Floor(ListPerTile) ? 1 : 0);
        for (var candidate = 0; candidate < count; candidate++)
        {
            ulong hash = Hash(corner, ListId, 0, candidate + 1);
            var at = new Vector2(least.X + (Unit(hash, 1) * (most.X - least.X)), least.Y + (Unit(hash, 2) * (most.Y - least.Y)));
            Vector3 inBlock = Vector3.Transform(new Vector3(at, 0f), back);
            Shares(cell, inBlock, shares);

            // THE GROUND AT THE PLACE, by its share there; its group's name names the list's group, "Default" where none is named so.
            float pick = Unit(hash, 3), sum = 0f;
            int order = -1;
            foreach (int one in orders)
            {
                sum += ShareOf(cell, one, shares);
                if (pick < sum)
                {
                    order = one;
                    break;
                }
            }

            if (order < 0 || ListGroup(_painter.GroupAt(order).Name) is not { Doodads.Count: > 0 } group)
            {
                continue;
            }

            float total = 0f;
            foreach (GroundDoodad doodad in group.Doodads)
            {
                total += Math.Max(doodad.Weight ?? 1f, 0f);
            }

            float choice = Unit(hash, 4) * total;
            GroundDoodad? chosen = null;
            foreach (GroundDoodad doodad in group.Doodads)
            {
                choice -= Math.Max(doodad.Weight ?? 1f, 0f);
                if (choice < 0f)
                {
                    chosen = doodad;
                    break;
                }
            }

            if (chosen is null || chosen.Ao.Length == 0)
            {
                continue;
            }

            if (blocked?.Invoke(at) == true)
            {
                _blocked++;
                continue;
            }

            if (HeightAt(cell, inBlock.X, inBlock.Y) is not { } z)
            {
                _bare++;
                continue;
            }

            Add(MemberOf(chosen.Ao, hash), new Vector3(inBlock.X, inBlock.Y, z), 1f, (Unit(hash, 5) - 0.5f) * MathF.Tau, place);
            _listed++;
        }
    }

    /// <summary>
    /// The member a list doodad that is a set draws, by the place's own draw - itself where its file is no set; a member that is a set again
    /// is chosen from in turn, a few deep.
    /// </summary>
    /// <remarks>
    /// THE SCATTER'S DRAW, NOT THE ROOM'S: RoomModels.Lay picks a set's member by a hash of where the line stands in the room's frame,
    /// rounded to hundredths, so the same spot under a block laid from another corner - its frame 250 units off, its sums a float's last
    /// digit off - could draw another member. The draw here is keyed by the cell's corner in the area and the candidate, exactly; the
    /// member it names is no set, so Lay draws it as it is. A file is asked whether it is a set where its name ends in "set", as Lay asks.
    /// </remarks>
    private string MemberOf(string ao, ulong hash)
    {
        for (var deep = 0; deep < 4; deep++)
        {
            if (!Path.GetFileNameWithoutExtension(ao).EndsWith("set", StringComparison.OrdinalIgnoreCase))
            {
                return ao;
            }

            if (!_sets.TryGetValue(ao, out IReadOnlyList<(int Weight, string Ao)>? members))
            {
                members = RoomModels.Members(AnimatedObject.Read(_read(ao.Replace('\\', '/').Trim())));
                _sets[ao] = members;
            }

            long total = 0;
            foreach ((int weight, _) in members)
            {
                total += Math.Max(weight, 0);
            }

            if (total <= 0)
            {
                return ao;
            }

            // THE LAST MEMBER WHERE A FLOAT'S ROUNDING CARRIES THE DRAW PAST THE SUM.
            double pick = Unit(hash, 6 + deep) * (double)total;
            string next = members[^1].Ao;
            foreach ((int weight, string member) in members)
            {
                pick -= Math.Max(weight, 0);
                if (pick < 0)
                {
                    next = member;
                    break;
                }
            }

            ao = next;
        }

        return ao;
    }

    /// <summary>The list's group a ground's group's name names - "Default" where none is named so, and null where there is none.</summary>
    private GroundDoodadGroup? ListGroup(string name)
    {
        GroundDoodadGroup? fallback = null;
        foreach (GroundDoodadGroup group in _list.Groups)
        {
            if (name.Length > 0 && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return group;
            }

            if (string.Equals(group.Name, "Default", StringComparison.OrdinalIgnoreCase))
            {
                fallback ??= group;
            }
        }

        return fallback;
    }

    /// <summary>One strewn line: the mesh at its size and turn, stood at a place of the block and laid with the piece.</summary>
    private void Add(string mesh, Vector3 inBlock, float size, float turn, Matrix4x4 place)
    {
        Vector3 at = Vector3.Transform(inBlock, place);
        Matrix4x4 laid = Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationZ(turn) * Matrix4x4.CreateTranslation(at);
        _lines.Add((new RoomDoodad(0, 0, turn, size, mesh, Stub), laid));
    }

    /// <summary>A group's layers, each with the share of the group's weights its materials carry it at - worked out once a group.</summary>
    private IReadOnlyList<(Layer Layer, float Share)> RecipeOf(int order)
    {
        if (_recipes.TryGetValue(order, out IReadOnlyList<(Layer Layer, float Share)>? known))
        {
            return known;
        }

        GroundGroup group = _painter.GroupAt(order);
        float total = 0f;
        for (var choice = 0; choice < group.Choices.Count; choice++)
        {
            total += WeightOf(group, choice);
        }

        var shares = new Dictionary<Layer, float>();
        var ordered = new List<Layer>();
        for (var choice = 0; choice < group.Choices.Count && total > 0f; choice++)
        {
            foreach (string path in group.Choices[choice].Layers)
            {
                if (LayerOf(path) is not { } layer)
                {
                    continue;
                }

                if (!shares.ContainsKey(layer))
                {
                    ordered.Add(layer);
                }

                shares[layer] = shares.GetValueOrDefault(layer) + (WeightOf(group, choice) / total);
            }
        }

        IReadOnlyList<(Layer Layer, float Share)> recipe = [.. ordered.Select(layer => (layer, shares[layer]))];
        _recipes[order] = recipe;
        return recipe;
    }

    /// <summary>A choice's weight - one each where the group writes none, a single choice.</summary>
    private static float WeightOf(GroundGroup group, int choice)
        => group.Weights.Count == group.Choices.Count ? Math.Max(group.Weights[choice], 0) : 1f;

    /// <summary>A layer read once, or null where it does not read - said under the picture.</summary>
    private Layer? LayerOf(string path)
    {
        string clean = path.Replace('\\', '/').Trim();
        if (_layers.TryGetValue(clean, out Layer? known))
        {
            return known;
        }

        DoodadLayer file = DoodadLayer.Read(_read(clean));
        Layer? layer = null;
        if (file.Entries.Count > 0)
        {
            float edge = 1f;
            foreach ((string key, string value) in file.Headers)
            {
                if (string.Equals(key, "MinEdgeScale", StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float read))
                {
                    edge = Math.Clamp(read, 0f, 1f);
                }
            }

            layer = new Layer(clean, file, Id(clean), edge, file.MaxRotation is { } most ? most * (MathF.PI / 180f) : MathF.Tau);
        }
        else
        {
            _unread[clean] = file.Why.Length > 0 ? file.Why : "no mesh";
        }

        _layers[clean] = layer;
        return layer;
    }

    /// <summary>A tile's cells - each sub-tile with its corners' groups and its ground's triangles - worked out once a tile.</summary>
    private Cell[] CellsOf(string tile, SkinnedMesh ground, IReadOnlyList<GroundPiece> pieces, int width, int height)
    {
        (TileDefinition definition, _) = TileModels.Defined(_read, tile);
        if (!definition.Ready)
        {
            return [];
        }

        int triangles = ground.Indices.Length / 3;
        bool gridded = definition.GroundGrid.Count > 0 && pieces.Count > 0 && pieces.Sum(one => one.Triangles) == triangles;
        var cells = new List<Cell>();
        if (gridded)
        {
            // ONE CELL A SUB-TILE, whatever number of templates lay ground on it: a tile of several templates (Landmark_feature_01's
            // three) runs one GroundPiece per template at the same sub-tile, and each would strew it again; joined, the place stands on
            // the top of them all.
            var first = 0;
            var runs = new Dictionary<(int X, int Y), List<float>>();
            var order = new List<(int X, int Y)>();
            foreach (GroundPiece piece in pieces)
            {
                var least = new Vector2((piece.X - 1) * TileModels.Side, (1 - piece.Y) * TileModels.Side);
                if (!runs.TryGetValue((piece.X, piece.Y), out List<float>? run))
                {
                    run = [];
                    runs[(piece.X, piece.Y)] = run;
                    order.Add((piece.X, piece.Y));
                }

                run.AddRange(Triangles(ground, first, piece.Triangles, least));
                first += piece.Triangles;
            }

            foreach ((int x, int y) in order)
            {
                IReadOnlyList<string> corners = definition.CornersOf(x, y);
                var least = new Vector2((x - 1) * TileModels.Side, (1 - y) * TileModels.Side);
                if (corners.Count == 4)
                {
                    cells.Add(new Cell(least, least, new Vector2(TileModels.Side), OrdersOf(corners), [.. runs[(x, y)]], Bilinear: true));
                }
            }

            return [.. cells];
        }

        // NO GRID: THE TILE'S FOUR CORNERS STAND FOR EVERY SUB-TILE, as the painter draws it - each ground at its count of the four
        // throughout, its corners being the tile's (down-left first) and not each sub-tile's.
        if (definition.Grounds.Count != 4)
        {
            return [];
        }

        int[] orders = OrdersOf(definition.Grounds);
        var whole = new Vector2(0f, (1 - height) * TileModels.Side);
        for (var y = 1; y <= height; y++)
        {
            for (var x = 1; x <= width; x++)
            {
                var least = new Vector2((x - 1) * TileModels.Side, (1 - y) * TileModels.Side);
                cells.Add(new Cell(least, whole, new Vector2(width, height) * TileModels.Side, orders, Triangles(ground, 0, triangles, least), Bilinear: false));
            }
        }

        return [.. cells];
    }

    /// <summary>Each corner's group by its place in the MaterialsList, -1 where it names none.</summary>
    private int[] OrdersOf(IReadOnlyList<string> corners)
    {
        var orders = new int[4];
        for (var corner = 0; corner < 4; corner++)
        {
            orders[corner] = _painter.GroupOrder(corners[corner]);
        }

        return orders;
    }

    /// <summary>The run's triangles that reach over a cell, as nine floats each - its three corners - for the height under a place.</summary>
    private static float[] Triangles(SkinnedMesh ground, int first, int count, Vector2 least)
    {
        var kept = new List<float>(count * 9);
        Vector2 most = least + new Vector2(TileModels.Side);
        for (int triangle = first; triangle < first + count; triangle++)
        {
            Vector3 a = ground.Positions[ground.Indices[triangle * 3]];
            Vector3 b = ground.Positions[ground.Indices[(triangle * 3) + 1]];
            Vector3 c = ground.Positions[ground.Indices[(triangle * 3) + 2]];
            if (MathF.Max(a.X, MathF.Max(b.X, c.X)) < least.X || MathF.Min(a.X, MathF.Min(b.X, c.X)) > most.X
                || MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) < least.Y || MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) > most.Y)
            {
                continue;
            }

            kept.AddRange([a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z]);
        }

        return [.. kept];
    }

    /// <summary>
    /// The ground's top under a place of the block - the least z of the triangles over it, up being minus - or null where none is.
    /// </summary>
    private static float? HeightAt(Cell cell, float x, float y)
    {
        float[] corners = cell.Triangles;
        float? top = null;
        for (var at = 0; at < corners.Length; at += 9)
        {
            float ax = corners[at], ay = corners[at + 1], bx = corners[at + 3], by = corners[at + 4], cx = corners[at + 6], cy = corners[at + 7];
            float area = ((bx - ax) * (cy - ay)) - ((cx - ax) * (by - ay));
            if (MathF.Abs(area) < Flat)
            {
                continue;
            }

            float u = (((bx - x) * (cy - y)) - ((cx - x) * (by - y))) / area;
            float v = (((cx - x) * (ay - y)) - ((ax - x) * (cy - y))) / area;
            float w = 1f - u - v;
            if (u < -1e-4f || v < -1e-4f || w < -1e-4f)
            {
                continue;
            }

            float z = (u * corners[at + 2]) + (v * corners[at + 5]) + (w * corners[at + 8]);
            top = top is { } known ? MathF.Min(known, z) : z;
        }

        return top;
    }

    /// <summary>Each corner's weight at a place of the block: bilinear across the cell's frame, or a quarter each over a tile carrying no grid.</summary>
    private static void Shares(Cell cell, Vector3 inBlock, Span<float> shares)
    {
        if (!cell.Bilinear)
        {
            shares.Fill(0.25f);
            return;
        }

        float s = Math.Clamp((inBlock.X - cell.Frame.X) / cell.FrameSize.X, 0f, 1f);
        float t = Math.Clamp((inBlock.Y - cell.Frame.Y) / cell.FrameSize.Y, 0f, 1f);
        shares[0] = (1f - s) * (1f - t);
        shares[1] = s * (1f - t);
        shares[2] = (1f - s) * t;
        shares[3] = s * t;
    }

    /// <summary>A group's share at a place: the weights of the corners naming it.</summary>
    private static float ShareOf(Cell cell, int order, ReadOnlySpan<float> shares)
    {
        float share = 0f;
        for (var corner = 0; corner < 4; corner++)
        {
            share += cell.Orders[corner] == order ? shares[corner] : 0f;
        }

        return share;
    }

    /// <summary>The distinct groups a cell's corners name, in corner order; how many.</summary>
    private static int Distinct(int[] orders, Span<int> into)
    {
        var count = 0;
        foreach (int order in orders)
        {
            if (order >= 0 && into[..count].IndexOf(order) < 0)
            {
                into[count++] = order;
            }
        }

        return count;
    }

    /// <summary>A cell's square in the room's frame - laid by turns and mirrors, a square still.</summary>
    private static (Vector2 Least, Vector2 Most) Boxed(Cell cell, Matrix4x4 place)
    {
        Vector2 least = new(float.MaxValue), most = new(float.MinValue);
        for (var corner = 0; corner < 4; corner++)
        {
            var at = new Vector3(cell.Least.X + ((corner & 1) * TileModels.Side), cell.Least.Y + ((corner >> 1) * TileModels.Side), 0f);
            Vector3 laid = Vector3.Transform(at, place);
            least = Vector2.Min(least, new Vector2(laid.X, laid.Y));
            most = Vector2.Max(most, new Vector2(laid.X, laid.Y));
        }

        return (least, most);
    }

    /// <summary>What the list's draws are keyed by, apart from every layer's.</summary>
    private const int ListId = unchecked((int)0x6C697374);

    /// <summary>A path's number for the hash - FNV-1a over its letters, case aside.</summary>
    private static int Id(string path)
    {
        uint hash = 2166136261;
        foreach (char letter in path)
        {
            hash = (hash ^ char.ToLowerInvariant(letter)) * 16777619;
        }

        return unchecked((int)hash);
    }

    /// <summary>The draw for one place: the seed, the place in sixteenths of a unit, the layer, the mesh and the candidate.</summary>
    private static ulong Hash(Vector2 area, int layer, int entry, int candidate)
    {
        ulong hash = Step(Seed ^ (ulong)(long)MathF.Round(area.X * Quantum));
        hash = Step(hash ^ (ulong)(long)MathF.Round(area.Y * Quantum));
        hash = Step(hash ^ (uint)layer);
        hash = Step(hash ^ (uint)entry);
        return Step(hash ^ (uint)candidate);
    }

    /// <summary>A number from nought to under one, one of several a draw gives.</summary>
    internal static float Unit(ulong hash, int lane) => (Step(hash + (ulong)lane) >> 40) * (1f / (1 << 24));

    /// <summary>SplitMix64's step.</summary>
    private static ulong Step(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A layer as strewn: its file, its number for the hash, its edge size and turn, and how many it laid.</summary>
    private sealed class Layer(string path, DoodadLayer file, int id, float minEdge, float turn)
    {
        public string Path { get; } = path;

        public DoodadLayer File { get; } = file;

        public int Id { get; } = id;

        public float MinEdge { get; } = minEdge;

        public float Turn { get; } = turn;

        public int Laid { get; set; }
    }

    /// <summary>
    /// One sub-tile as strewn: its square in the block, the frame its corners' weights run across, each corner's group and its ground's triangles.
    /// </summary>
    private sealed record Cell(Vector2 Least, Vector2 Frame, Vector2 FrameSize, int[] Orders, float[] Triangles, bool Bilinear);
}
