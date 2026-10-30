using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's shadow-casters part: what throws the sun's shadow on the ground of the stage shown - every caster by its kind, its material,
/// the blend word its graphs spell and how much of its texture is clear - drawn as the picture casts it and with the shadow-only shapes cut by
/// their texture's alpha, and held against the game's picture where the two differ.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (v0.1.368), with The Red Vale's picture: the tree crowns threw lozenge and square shadows the game does not.
/// The lead, read from the files: the woods' templates carry canopy cards whose materials are shadow-only (JungleCanopyc and canopytestc on
/// ForceAlphaTestWithShadowOnly.fxgraph, black_shadowonlyc on OpaqueShadowOnly), and a blend word naming "shadowonly" is cast whole here, its
/// texture unread. Whether the engine cuts such a card by its alpha is in no file read - so this part MEASURES it: the shadows a cut would take
/// away, and the game's brightness there beside its brightness in the open and in a shadow both readings cast. Nought says the game casts as
/// the picture does, one that it casts nothing there.
///
/// The ground is the receiver: a texel of the sun's map is shadow on the ground where the nearest caster lies a few texels before the ground
/// along the light (three texels, CHOSEN). The squares are found as the ground shadow's pieces per caster material, each piece's fill of the
/// tightest box round it - a piece filling 0.8 of it or more is square-like (CHOSEN).
/// </remarks>
public static class CaptureShadowCasters
{
    /// <summary>The sun's map of the ground by what shadows it.</summary>
    public const string MapPicture = "shadow-casters-map.png";

    /// <summary>The game's picture with the shadows a cut would take away marked.</summary>
    public const string OnGamePicture = "shadow-casters-game.png";

    /// <summary>The ground's map by kind, for the replay - see <see cref="BinaryFormat"/>.</summary>
    public const string Numbers = "shadow-casters.bin";

    /// <summary>The replay's report.</summary>
    public const string ReplayFile = "shadow-casters-replay.txt";

    /// <summary>How the numbers are laid out.</summary>
    public const string BinaryFormat =
        "little-endian: \"PSCM\", int version 1, int side, float units a texel, then side x side bytes, rows from the map's least V: 0 no ground, "
        + "1 ground in the sun, 2 shadowed by a solid shape, 3 by a cut-out shape, 4 by a shadow-only shape kept when cut, 5 by a shadow-only shape a cut takes away";

    /// <summary>The kinds of a ground texel.</summary>
    private const byte NoGround = 0, Sunlit = 1, BySolid = 2, ByCutout = 3, ByShadowOnly = 4, Lost = 5;

    /// <summary>How many texels before the ground a caster must lie to shadow it - against the map's own rounding.</summary>
    private const float Before = 3f;

    /// <summary>The least texels a piece of shadow needs to be judged square or not.</summary>
    private const int FewestTexels = 16;

    /// <summary>The fill of its box from which a piece is square-like - CHOSEN.</summary>
    private const double SquareFill = 0.8;

    /// <summary>The graphs that spell a shadow-only or alpha-tested blend, written whole every time - which word each holds settles the blend rule.</summary>
    private static readonly string[] BlendGraphs =
    [
        "Metadata/Effects/Graphs/General/ForceAlphaTestWithShadowOnly.fxgraph",
        "Metadata/Effects/Graphs/General/ForceAlphaTestShadowOnly.fxgraph",
        "Metadata/Effects/Graphs/General/ForceOpaqueShadowOnly.fxgraph",
        "Metadata/Effects/Graphs/General/ForceAlphaTestWithShadow.fxgraph",
        "Metadata/Effects/Graphs/General/ForceAlphaTest.fxgraph",
        "Metadata/Effects/Graphs/General/ForceOpaqueNoShadow.fxgraph",
        "Metadata/Materials/Environment/SecondPass/ForceAlphaTestWithShadowOnly.mat",
    ];

    /// <summary>The part's report and what goes beside it.</summary>
    /// <param name="read">How to get a file out of the install, or null.</param>
    /// <param name="pane">The pane whose stage is looked at.</param>
    /// <param name="scene">The stage drawn through the game's camera, or null where it could not be - then nothing is held against the game.</param>
    /// <param name="search">Where it says how far it is, or null.</param>
    /// <param name="pictures">The pictures to write beside it.</param>
    /// <param name="numbers">The ground's map by kind, <see cref="Numbers"/>.</param>
    public static string Write(
        Func<string, byte[]?>? read, CapturePane pane, CaptureAtGame? scene, CaptureAtGame.Search? search,
        out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers)
    {
        ArgumentNullException.ThrowIfNull(pane);
        pictures = [];
        numbers = [];
        if (pane.Light is not { } light)
        {
            return "not written: the pane has no light panel to say where the sun is";
        }

        MonsterModel model = pane.Model;
        SkinnedMesh mesh = model.Mesh;
        Vector3[] positions = pane.Positions is { } posed && posed.Length == mesh.Positions.Length ? posed : mesh.Positions;
        int[] shapeOf = CaptureAtGame.ShapesOf(mesh);
        int shapes = mesh.Shapes.Count;
        IReadOnlyList<MaterialBlend> blends = pane.Blends ?? [.. Enumerable.Repeat(MaterialBlend.Opaque, shapes)];
        var ground = new bool[shapes];
        for (var shape = 0; shape < shapes; shape++)
        {
            ground[shape] = shape < model.ShapeSources.Count && model.ShapeSources[shape].StartsWith(RoomModels.GroundSource, StringComparison.Ordinal);
        }

        search?.Begin(3);
        const int side = ShadowMap.Usual;
        search?.Step("the sun's map as the picture casts it");
        ShadowOwners? drawn = MeshPicture.Casters(mesh, positions, light.SunDirection, side, model.Skin, model.Skins, blends, pane.Shades);
        search?.Step("the sun's map with the shadow-only shapes cut by their alpha");
        ShadowOwners? cut = MeshPicture.Casters(mesh, positions, light.SunDirection, side, model.Skin, model.Skins, blends, pane.Shades, MeshPicture.CastRule.ShadowOnlyCut);
        search?.Step("the ground alone");
        ShadowOwners? floor = MeshPicture.Casters(
            mesh, positions, light.SunDirection, side, model.Skin, model.Skins, blends, pane.Shades,
            only: one => one < shapeOf.Length && shapeOf[one] >= 0 && ground[shapeOf[one]]);
        if (drawn is null || cut is null)
        {
            return "not written: the stage casts nothing along the sun";
        }

        var text = new StringBuilder();
        var kinds = new byte[side * side];
        var shadowed = new int[shapes];
        var kept = new int[shapes];
        int groundTexels = 0, sunlit = 0;
        float texel = drawn.Texel;
        if (floor is not null)
        {
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    float under = floor.DepthAt(x, y);
                    if (!float.IsFinite(under))
                    {
                        continue;
                    }

                    groundTexels++;
                    int at = (y * side) + x;
                    int caster = Caster(drawn, x, y, under, texel, shapeOf, ground);
                    int casterCut = Caster(cut, x, y, under, texel, shapeOf, ground);
                    if (caster < 0)
                    {
                        kinds[at] = Sunlit;
                        sunlit++;
                        continue;
                    }

                    int shape = shapeOf[caster];
                    shadowed[shape]++;
                    if (casterCut >= 0)
                    {
                        kept[shapeOf[casterCut]]++;
                    }

                    MaterialBlend blend = shape < blends.Count ? blends[shape] : MaterialBlend.Opaque;
                    kinds[at] = blend switch
                    {
                        MaterialBlend.ShadowOnly => casterCut < 0 ? Lost : ByShadowOnly,
                        MaterialBlend.Cutout => ByCutout,
                        _ => BySolid,
                    };
                }
            }
        }

        int lost = kinds.Count(one => one == Lost);
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"shadow casters: {(floor is null ? "no ground shape in the stage - nothing to measure the shadows on" : $"{groundTexels:N0} texels of ground under the sun, {groundTexels - sunlit:N0} in shadow; {lost:N0} of them are shadow-only shapes' that a cut by their texture's alpha takes away ({(double)lost / Math.Max(1, groundTexels - sunlit):0.###} of the shadow)")}"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"the sun's map: {side} texels across, {texel:0.###} units a texel, the light travelling ({light.SunDirection.X:0.####}, {light.SunDirection.Y:0.####}, {light.SunDirection.Z:0.####}); a ground texel is shadowed where its nearest caster lies {Before} texels before it along the light (CHOSEN); the game's own cascades are finer near the eye, so the edges' sharpness is not compared, only where the shadows fall"));

        // THE GAME'S VERDICT where its picture was taken through its own camera.
        text.AppendLine().AppendLine("=== the game's picture where a cut would take the shadow away");
        List<(string File, GamePicture Picture)> drawnPictures = [(MapPicture, Map(kinds, side))];
        if (scene is null || scene.Game.Length == 0)
        {
            text.AppendLine("  not measured: no game picture through the game's camera (see light-against-game.txt for why)");
        }
        else
        {
            text.Append(Verdict(scene, drawn, cut, shapeOf, ground, out GamePicture overlay));
            drawnPictures.Add((OnGamePicture, overlay));
        }

        // EVERY CASTER BY ITS MATERIAL, the most shadow first.
        text.AppendLine().AppendLine("=== what casts the ground's shadow, by material - the most first");
        text.AppendLine("  blend       word (the graphs' last)            texels  kept when cut  shapes  triangles  clear texels  material");
        var groups = new Dictionary<string, (MaterialBlend Blend, string Word, int Texels, int Kept, List<int> Shapes)>(StringComparer.OrdinalIgnoreCase);
        for (var shape = 0; shape < shapes; shape++)
        {
            if (shadowed[shape] == 0 && !(shape < blends.Count && blends[shape] == MaterialBlend.ShadowOnly))
            {
                continue;
            }

            string material = shape < model.ShapeMaterials.Count ? MaterialFile.Bare(model.ShapeMaterials[shape]) : "(no material)";
            MaterialBlend blend = shape < blends.Count ? blends[shape] : MaterialBlend.Opaque;
            string word = shape < model.Modes.Count ? model.Modes[shape] : string.Empty;
            string key = material + "|" + blend + "|" + word;
            (MaterialBlend Blend, string Word, int Texels, int Kept, List<int> Shapes) held = groups.TryGetValue(key, out var had) ? had : (blend, word, 0, 0, []);
            held.Shapes.Add(shape);
            groups[key] = (blend, word, held.Texels + shadowed[shape], held.Kept + kept[shape], held.Shapes);
        }

        var written = new List<string>();
        foreach ((string key, (MaterialBlend blend, string word, int texels, int keptTexels, List<int> those)) in groups.OrderByDescending(one => one.Value.Texels).Take(40))
        {
            string material = key[..key.IndexOf('|', StringComparison.Ordinal)];
            int triangles = those.Sum(shape => mesh.Shapes[shape].Count / 3);
            string clear = Clear(model, those);
            text.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {blend,-11} {Shorten(word, 32),-32} {texels,9:N0} {keptTexels,14:N0} {those.Count,7} {triangles,10:N0}  {clear,12}  {material}"));
            if (blend is MaterialBlend.ShadowOnly or MaterialBlend.Cutout && written.Count < 16 && !written.Contains(material, StringComparer.OrdinalIgnoreCase))
            {
                written.Add(material);
            }
        }

        // THE PIECES OF SHADOW, per caster material: how square each is.
        text.AppendLine().AppendLine(Pieces(drawn, floor, kinds, side, shapeOf, model, ground));

        // THE FILES THAT SETTLE THE RULE: each blend graph whole with the word it spells, and the shadow-only and cut-out materials that cast.
        text.AppendLine().AppendLine("=== the blend graphs, each with the word its graph spells (MaterialFile.GraphBlend) - what MaterialBlends.Of reads");
        if (read is null)
        {
            text.AppendLine("  not read: no install");
        }
        else
        {
            foreach (string graph in BlendGraphs)
            {
                byte[]? bytes = read(graph);
                text.AppendLine().Append("##### ").Append(graph).Append(": ").AppendLine(bytes is { Length: > 0 }
                    ? $"word \"{MaterialFile.GraphBlend(bytes)}\", taken as {MaterialBlends.Of(MaterialFile.GraphBlend(bytes))}"
                    : "not in the install");
                if (bytes is { Length: > 0 })
                {
                    text.AppendLine(StatDescriptionFiles.Decode(bytes).TrimEnd());
                }
            }

            text.AppendLine().AppendLine("=== the shadow-only and cut-out materials that cast, whole");
            var paints = new MonsterModels.Paints();
            var graphs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string material in written)
            {
                text.AppendLine().Append("##### ").AppendLine(material);
                CaptureMaterials.Material(read, material, paints, text, graphs, seen);
            }

            CaptureMaterials.Graphs(read, graphs, "a caster's", text);
        }

        // AND WHAT A DOODAD'S OBJECT ASKS THAT NOTHING HERE DOES: a second render pass (AnimatedRender's RenderPasses).
        text.AppendLine().AppendLine("=== doodad objects asking for a render pass of their own (AnimatedRender's RenderPasses) - not drawn or cast here");
        int passes = 0;
        foreach (DoodadFile file in model.DoodadFiles)
        {
            if (read?.Invoke(file.Ao) is not { Length: > 0 } bytes)
            {
                continue;
            }

            string content = StatDescriptionFiles.Decode(bytes);
            int at = content.IndexOf("RenderPasses", StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            passes++;
            int end = content.IndexOf('\n', at);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {file.Ao} ({file.Lines} lines, {file.Laid} laid): {content[at..(end < 0 ? content.Length : end)].Trim()}"));
        }

        text.AppendLine(passes == 0 ? "  none among the doodad files" : string.Create(CultureInfo.InvariantCulture, $"  {passes} files"));

        text.AppendLine().AppendLine("=== the numbers beside it");
        text.AppendLine(Numbers + ": " + BinaryFormat);
        text.AppendLine(MapPicture + ": the sun's map, half size - black no ground, grey in the sun, blue a solid shape's shadow, green a cut-out's, red a shadow-only shape's kept when cut, magenta one a cut takes away");
        text.AppendLine(OnGamePicture + ": the game's picture where the pane's ground lies - magenta where a cut would take the pane's shadow away, blue where both readings shadow it");
        numbers = Bytes(kinds, side, texel);
        pictures = drawnPictures;
        return text.ToString();
    }

    /// <summary>The replay: the ground's map read back, its pieces counted again by kind.</summary>
    /// <param name="folder">The capture's folder, unpacked.</param>
    public static string Replay(string folder)
    {
        string path = CaptureParts.Find(folder, Numbers);
        if (!File.Exists(path))
        {
            return "shadow casters replay: no " + Numbers + " in " + folder;
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 16 || !bytes.AsSpan(0, 4).SequenceEqual("PSCM"u8))
        {
            return "shadow casters replay: " + Numbers + " is not the shadow casters' numbers";
        }

        int side = BitConverter.ToInt32(bytes, 8);
        float texel = BitConverter.ToSingle(bytes, 12);
        byte[] kinds = bytes[16..];
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"shadow casters replay: {side} texels across, {texel:0.###} units a texel"));
        foreach ((byte kind, string name) in new[] { (BySolid, "a solid shape's"), (ByCutout, "a cut-out's"), (ByShadowOnly, "a shadow-only shape's kept when cut"), (Lost, "a shadow-only shape's a cut takes away") })
        {
            List<(int Count, double Fill, double Aspect, Vector2 Middle)> pieces = Components(kinds, side, at => kinds[at] == kind);
            int square = pieces.Count(one => one.Fill >= SquareFill);
            text.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {name}: {kinds.Count(one => one == kind):N0} texels in {pieces.Count:N0} pieces of {FewestTexels}+ texels, {square:N0} square-like (fill {SquareFill}+), median fill {(pieces.Count > 0 ? pieces.OrderBy(one => one.Fill).ElementAt(pieces.Count / 2).Fill : double.NaN):0.###}"));
        }

        return text.ToString();
    }

    /// <summary>The caster that shadows a ground texel - nearest the light, not ground, a few texels before the ground - or -1.</summary>
    private static int Caster(ShadowOwners map, int x, int y, float under, float texel, int[] shapeOf, bool[] ground)
    {
        int owner = map.OwnerAt(x, y);
        if (owner < 0 || owner >= shapeOf.Length || shapeOf[owner] < 0 || ground[shapeOf[owner]])
        {
            return -1;
        }

        return map.DepthAt(x, y) < under - (Before * texel) ? owner : -1;
    }

    /// <summary>The share of a group's texels a cut by its alpha drops, over the textures its shapes wear - its skins' top level.</summary>
    private static string Clear(MonsterModel model, List<int> shapes)
    {
        long clear = 0, all = 0;
        var done = new HashSet<Mipmaps>();
        foreach (int shape in shapes)
        {
            Mipmaps? skin = shape < model.Skins.Count ? model.Skins[shape] : model.Skin;
            if (skin is null || !done.Add(skin))
            {
                continue;
            }

            GamePicture top = skin.Top;
            byte[] rgba = top.Rgba;
            for (var at = 3; at < rgba.Length; at += 4)
            {
                clear += rgba[at] < 128 ? 1 : 0;
            }

            all += rgba.Length / 4;
        }

        return all == 0 ? "no texture" : ((double)clear / all).ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The game's brightness where a cut would take the pane's shadow away, beside its brightness where both readings leave the ground lit and
    /// where both shadow it - per receiving material, the game's over the pane's albedo so the ground's own colour drops out.
    /// </summary>
    private static string Verdict(CaptureAtGame scene, ShadowOwners drawn, ShadowOwners cut, int[] shapeOf, bool[] ground, out GamePicture overlay)
    {
        PixelGather gather = scene.Gather;
        int w = gather.Width, h = (scene.DrawnHeight + gather.Step - 1) / gather.Step;
        var rgba = new byte[w * h * 4];
        var groups = new Dictionary<int, (List<double> Lit, List<double> Shade, List<double> Lost)>();
        for (var at = 0; at < w * h; at++)
        {
            int drawnAt = scene.DrawnAt(at);
            Vector3 game = drawnAt >= 0 ? scene.GameAt(drawnAt) : new Vector3(float.NaN);
            int to = at * 4;
            rgba[to + 3] = 255;
            if (!float.IsFinite(game.X))
            {
                continue;
            }

            rgba[to] = CaptureAtGame.Encoded(game.X * 0.5f);
            rgba[to + 1] = CaptureAtGame.Encoded(game.Y * 0.5f);
            rgba[to + 2] = CaptureAtGame.Encoded(game.Z * 0.5f);
            int triangle = gather.Triangle[at];
            if (triangle < 0 || triangle >= shapeOf.Length || shapeOf[triangle] < 0 || !ground[shapeOf[triangle]] || gather.Water[at]
                || scene.Layer[drawnAt] >= 0 || scene.Masked[drawnAt])
            {
                continue;
            }

            Vector3 place = gather.Place[at], normal = gather.Normal[at];
            float asDrawn = drawn.Lit(place, normal), asCut = cut.Lit(place, normal);
            double albedo = Math.Max(CaptureAtGame.Luminance(gather.Terms[at].Albedo), 1e-4);
            double light = CaptureAtGame.Luminance(game) / albedo;
            int shape = shapeOf[triangle];
            (List<double> Lit, List<double> Shade, List<double> Lost) group = groups.TryGetValue(shape, out var had) ? had : ([], [], []);
            groups[shape] = group;
            if (asDrawn >= 0.9f && asCut >= 0.9f)
            {
                group.Lit.Add(light);
            }
            else if (asDrawn <= 0.1f && asCut <= 0.1f)
            {
                group.Shade.Add(light);
                rgba[to + 2] = (byte)Math.Min(255, rgba[to + 2] + 90);
            }
            else if (asDrawn <= 0.1f && asCut >= 0.9f)
            {
                group.Lost.Add(light);
                rgba[to] = (byte)Math.Min(255, rgba[to] + 120);
                rgba[to + 2] = (byte)Math.Min(255, rgba[to + 2] + 120);
            }
        }

        overlay = new GamePicture(w, h, rgba);
        var text = new StringBuilder();
        text.AppendLine("  where t is (lost - shade) / (lit - shade) of the game's light over the pane's albedo, medians: nought the game shadows as the picture casts, one it casts nothing there");
        int lit = 0, shade = 0, lostCount = 0;
        double weighted = 0;
        var rows = new List<string>();
        foreach ((int shape, (List<double> Lit, List<double> Shade, List<double> Lost) group) in groups.OrderByDescending(one => one.Value.Lost.Count))
        {
            lit += group.Lit.Count;
            shade += group.Shade.Count;
            lostCount += group.Lost.Count;
            if (group.Lost.Count < 30 || group.Lit.Count < 30 || group.Shade.Count < 30)
            {
                continue;
            }

            double l = Median(group.Lit), s = Median(group.Shade), x = Median(group.Lost);
            double t = l > s ? (x - s) / (l - s) : double.NaN;
            if (double.IsFinite(t))
            {
                weighted += t * group.Lost.Count;
            }

            string material = shape < scene.Pane.Model.ShapeMaterials.Count ? Path.GetFileNameWithoutExtension(MaterialFile.Bare(scene.Pane.Model.ShapeMaterials[shape])) : "?";
            rows.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  {Shorten(material, 40),-40} lit {group.Lit.Count,7:N0} at {l,7:0.###}  shade {group.Shade.Count,7:N0} at {s,7:0.###}  lost {group.Lost.Count,7:N0} at {x,7:0.###}  t {t:0.###}"));
        }

        int judged = groups.Values.Where(one => one.Lost.Count >= 30 && one.Lit.Count >= 30 && one.Shade.Count >= 30).Sum(one => one.Lost.Count);
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  ground pixels: {lit:N0} lit in both readings, {shade:N0} shadowed in both, {lostCount:N0} shadowed only as drawn; t over the {judged:N0} judged {(judged > 0 ? (weighted / judged).ToString("0.###", CultureInfo.InvariantCulture) : "-")}"));
        foreach (string row in rows.Take(20))
        {
            text.AppendLine(row);
        }

        return text.ToString();
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>The ground shadow's pieces per caster material, and how square each is.</summary>
    private static string Pieces(ShadowOwners drawn, ShadowOwners? floor, byte[] kinds, int side, int[] shapeOf, MonsterModel model, bool[] ground)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"=== the ground shadow's pieces by caster material - {FewestTexels}+ texels, square-like where a piece fills {SquareFill}+ of the tightest box round it (CHOSEN)"));
        var material = new int[side * side];
        var names = new List<string>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                int at = (y * side) + x;
                material[at] = -1;
                if (kinds[at] < BySolid)
                {
                    continue;
                }

                int owner = drawn.OwnerAt(x, y);
                int shape = owner >= 0 && owner < shapeOf.Length ? shapeOf[owner] : -1;
                if (shape < 0 || ground[shape])
                {
                    continue;
                }

                string name = shape < model.ShapeMaterials.Count ? MaterialFile.Bare(model.ShapeMaterials[shape]) : "?";
                if (!index.TryGetValue(name, out int id))
                {
                    id = names.Count;
                    names.Add(name);
                    index[name] = id;
                }

                material[at] = id;
            }
        }

        var rows = new List<(string Name, int Pieces, int Square, double Median, List<(int Count, double Fill, double Aspect, Vector2 Middle)> All)>();
        for (var id = 0; id < names.Count; id++)
        {
            int own = id;
            List<(int Count, double Fill, double Aspect, Vector2 Middle)> pieces = Components(kinds, side, at => material[at] == own);
            if (pieces.Count == 0)
            {
                continue;
            }

            rows.Add((names[id], pieces.Count, pieces.Count(one => one.Fill >= SquareFill), pieces.OrderBy(one => one.Fill).ElementAt(pieces.Count / 2).Fill, pieces));
        }

        Vector2 origin = model.AreaOrigin ?? Vector2.Zero;
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  material                                             pieces  square-like  median fill   the three biggest: texels, fill, aspect, where on the ground ({(model.AreaOrigin is null ? "the stage's space" : "the area's")})"));
        foreach ((string name, int count, int square, double median, List<(int Count, double Fill, double Aspect, Vector2 Middle)> all) in rows.OrderByDescending(one => one.Square).ThenByDescending(one => one.Pieces).Take(25))
        {
            string biggest = string.Join("; ", all.OrderByDescending(one => one.Count).Take(3).Select(one =>
            {
                // ON THE GROUND: the map's place of its middle, along the light as deep as the ground lies there.
                ShadowFrame frame = drawn.Frame;
                Vector3 place = (frame.U * ((one.Middle.X / frame.PerUnit) + frame.Least.X)) + (frame.V * ((one.Middle.Y / frame.PerUnit) + frame.Least.Y));
                float deep = floor?.DepthAt(Math.Clamp((int)one.Middle.X, 0, side - 1), Math.Clamp((int)one.Middle.Y, 0, side - 1)) ?? float.NaN;
                place += frame.W * (frame.Nearest + (float.IsFinite(deep) ? deep : 0f));
                return string.Create(CultureInfo.InvariantCulture, $"{one.Count} {one.Fill:0.##} {one.Aspect:0.#} ({place.X + origin.X:0}, {place.Y + origin.Y:0})");
            }));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {Shorten(name, 52),-52} {count,6:N0} {square,12:N0} {median,12:0.###}   {biggest}"));
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>The 4-connected pieces of the texels a test takes, each with its count, the fill of its tightest box, the box's aspect and its middle.</summary>
    private static List<(int Count, double Fill, double Aspect, Vector2 Middle)> Components(byte[] kinds, int side, Func<int, bool> takes)
    {
        var seen = new bool[side * side];
        var pieces = new List<(int Count, double Fill, double Aspect, Vector2 Middle)>();
        var stack = new Stack<int>();
        var members = new List<int>();
        for (var start = 0; start < kinds.Length; start++)
        {
            if (seen[start] || !takes(start))
            {
                continue;
            }

            members.Clear();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int at = stack.Pop();
                members.Add(at);
                int x = at % side, y = at / side;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            if (members.Count >= FewestTexels)
            {
                pieces.Add(Shape(members, side));
            }
        }

        return pieces;

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= side || y >= side)
            {
                return;
            }

            int at = (y * side) + x;
            if (!seen[at] && takes(at))
            {
                seen[at] = true;
                stack.Push(at);
            }
        }
    }

    /// <summary>A piece's count, the fill of the tightest box round it, that box's aspect and the piece's middle.</summary>
    /// <remarks>
    /// THE TIGHTEST BOX OVER EIGHTEEN TURNS, five degrees apart - not the principal axes': a square's spread is the same every way, so its
    /// axes fall anywhere, and a square boxed at 45 degrees fills half its box (the first test's own square came out 0.55). A square or a
    /// lozenge fills its tightest box whole, a disc or an ellipse 0.79 of it.
    /// </remarks>
    private static (int Count, double Fill, double Aspect, Vector2 Middle) Shape(List<int> members, int side)
    {
        double mx = 0, my = 0;
        foreach (int at in members)
        {
            mx += (at % side) + 0.5;
            my += (at / side) + 0.5;
        }

        mx /= members.Count;
        my /= members.Count;
        double bestArea = double.MaxValue, bestAspect = 1;
        for (var turn = 0; turn < 18; turn++)
        {
            double angle = turn * 5d * Math.PI / 180d;
            double c = Math.Cos(angle), s = Math.Sin(angle);
            double leastU = double.MaxValue, mostU = double.MinValue, leastV = double.MaxValue, mostV = double.MinValue;
            foreach (int at in members)
            {
                double dx = (at % side) + 0.5 - mx, dy = (at / side) + 0.5 - my;
                double u = (dx * c) + (dy * s), v = (-dx * s) + (dy * c);
                leastU = Math.Min(leastU, u);
                mostU = Math.Max(mostU, u);
                leastV = Math.Min(leastV, v);
                mostV = Math.Max(mostV, v);
            }

            // A TEXEL'S OWN WIDTH round the middles' extent, so a piece one texel wide is a box one texel wide.
            double a = mostU - leastU + 1d, b = mostV - leastV + 1d;
            if (a * b < bestArea)
            {
                bestArea = a * b;
                bestAspect = Math.Max(a, b) / Math.Min(a, b);
            }
        }

        return (members.Count, Math.Min(1d, members.Count / bestArea), bestAspect, new Vector2((float)mx, (float)my));
    }

    /// <summary>The map by kind, half size, each kept texel's most telling kind of its four.</summary>
    private static GamePicture Map(byte[] kinds, int side)
    {
        int half = side / 2;
        var rgba = new byte[half * half * 4];
        for (var y = 0; y < half; y++)
        {
            for (var x = 0; x < half; x++)
            {
                int at = (2 * y * side) + (2 * x);
                byte kind = Math.Max(Math.Max(kinds[at], kinds[at + 1]), Math.Max(kinds[at + side], kinds[at + side + 1]));
                (byte r, byte g, byte b) = kind switch
                {
                    Sunlit => ((byte)170, (byte)170, (byte)160),
                    BySolid => ((byte)40, (byte)60, (byte)140),
                    ByCutout => ((byte)40, (byte)150, (byte)60),
                    ByShadowOnly => ((byte)200, (byte)40, (byte)40),
                    Lost => ((byte)240, (byte)40, (byte)240),
                    _ => ((byte)0, (byte)0, (byte)0),
                };
                int to = ((y * half) + x) * 4;
                rgba[to] = r;
                rgba[to + 1] = g;
                rgba[to + 2] = b;
                rgba[to + 3] = 255;
            }
        }

        return new GamePicture(half, half, rgba);
    }

    private static byte[] Bytes(byte[] kinds, int side, float texel)
    {
        var bytes = new byte[16 + kinds.Length];
        "PSCM"u8.CopyTo(bytes);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), 1);
        BitConverter.TryWriteBytes(bytes.AsSpan(8), side);
        BitConverter.TryWriteBytes(bytes.AsSpan(12), texel);
        kinds.CopyTo(bytes, 16);
        return bytes;
    }

    private static string Shorten(string text, int most) => text.Length <= most ? text : "..." + text[^(most - 3)..];
}
