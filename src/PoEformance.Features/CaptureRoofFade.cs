using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// Which roof sections the game hides round the player, measured on the game's own picture - beside where each section and its tile piece
/// stand to the player and to the camera, the ground under the player, and the roof fade's words the area's files set.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (2026-10-31, the third of the four, "measure first"): the shader side is settled - a section wears the
/// engine's roof_fade.fxgraph, an AlphaTest of the object uniform <c>roof_fade</c> against world-space noise, so it dissolves through a
/// speckle as the value falls from one to nought - but the value is the program's (ClientTileMap::CalculateRoofFadeRanges, RoofFadeMode,
/// the cylinders, boxes and circles some files set; light-extras.txt section 10). Before a rule is given, this part measures it.
///
/// THE STAGE IS DRAWN THREE TIMES through the game's camera (CaptureAtGame): as the light parts draw it, with every roof section; with the
/// sections drawn as shadow casters alone, each casting as it does in the first (a cut-out through its texture, MaterialBlend.ShadowCut) -
/// what lies behind them, their shadows kept; and with the sections neither drawn nor cast (MaterialBlend.Hidden) - what lies behind
/// them, their shadows gone too. Where the game hides a section its picture shows what lies behind it.
///
/// THE GAME'S PATTERN DECIDES, NOT ITS BRIGHTNESS (v0.1.386). v0.1.385 fitted the game's light at a section's pixels as a mix of the two
/// drawings, which read the light's own mismatch - the clouds, the fog, the lamp the pane does not draw as the game does - as a share: The
/// Red Vale's capture of 09:12 has the two trees in front of the player hidden in the game's picture and the two beside it shown, and the
/// fit called three of the four mostly hidden. Now, over each section's telling pixels (the drawings with and without it a fifth of the
/// brighter apart, CHOSEN), the logarithm of the game's luminance is correlated with each drawing's - a scale and an offset of the light
/// leave a correlation as it is - and the section is HIDDEN where the better drawing without it goes with the game by a tenth more than the
/// one with it (CHOSEN), SHOWN the other way round, unclear between. That capture, read again: with 0.00 against without 0.50 and 0.20
/// against 0.62 (hidden), 0.26 against 0.07 and 0.68 against 0.17 (shown). Beside it the share: the game's centred logarithm fitted by the
/// two drawings', the one without's part of the sum.
///
/// THE HIDDEN SECTIONS' SHADOWS are measured where they fall: the stage drawn twice more without the sections the verdicts call hidden,
/// their shadows kept and gone, and the game's pattern held against both over the pixels those two tell apart and a band round them (inside
/// a shadow the two differ by a scale alone; its edge is the step that tells them apart) - whether the engine keeps a hidden roof's shadow,
/// which a section's own pixels cannot say (its shadow seldom falls behind it).
///
/// WHERE EACH STANDS: its box on the ground in the camera's own ways from the player - up the screen and across it; how far across and how
/// high over him; how near the line from the game's eye to him passes it, and where along that line; its pixels on the screen from his; its
/// depth against his. And its TILE PIECE - a tile file's laid footprint in the area, read from the area's tiles at the corner its source
/// names: the area's master may set RoofFadeMode "Tile", "BoundingBox", "Island" or "Global" and RoofFadeDistanceUpscreen and Downscreen
/// (light-extras section 10 of the 09:12 capture: 68 masters), a ground type HideRoof (36), a room no_roof_fade and force_roof_fade zones
/// (49), while The Red Vale's master and environment set none - the program's own rule then. In that capture the two hidden trees are
/// pieces of curve5 (9 by 6 tiles from tile 21, 30), the tile the player stands on (23, 33) among them, and the two shown ones of a piece he
/// does not stand on. So each section says whether its piece is his or touches his, the pieces carrying roof sections are listed with their
/// sections' verdicts, and the reading "the player's piece fades whole" is held against every verdict.
/// </remarks>
public static class CaptureRoofFade
{
    /// <summary>The numbers the report is worked out from, beside it.</summary>
    public const string Numbers = "roof-fade.bin";

    /// <summary>The game's picture with each section's pixels tinted by which drawing they go with, beside it.</summary>
    public const string Tinted = "roof-fade.png";

    /// <summary>The offline reader's file, beside the capture.</summary>
    public const string ReplayFile = "roof-fade-replay.txt";

    /// <summary>How far apart the two drawings' luminance must stand at a pixel, over the brighter, for it to tell them apart - CHOSEN.</summary>
    internal const float Telling = 0.2f;

    /// <summary>The fewest telling pixels a verdict is given over.</summary>
    internal const int FewestTelling = 12;

    /// <summary>How far one drawing's correlation with the game must stand over the other's for a verdict - CHOSEN.</summary>
    internal const float Margin = 0.1f;

    /// <summary>
    /// Added to a luminance before its logarithm - CHOSEN: about three steps of the game's darkest 8-bit levels, so their rounding does not
    /// rule the dark pixels (the 09:12 capture's verdicts alike from 1e-4 to 1e-2).
    /// </summary>
    internal const float LogFloor = 1e-3f;

    /// <summary>How many gathered pixels round the hidden sections' shadows the shadow's measure takes in - CHOSEN, see Shades.</summary>
    private const int Band = 6;

    /// <summary>How many points along the line from the eye to the player its nearness to a section's box is sought at.</summary>
    private const int LineSteps = 256;

    /// <summary>The most extends hops an object's words are followed through.</summary>
    private const int MostHops = 4;

    /// <summary>The most tiles a piece's footprint is sought across from its corner - past the widest tile file read so far.</summary>
    private const int PieceReach = 64;

    /// <summary>What a laid tile piece's source puts between its file and its least corner (LaidRoomModels).</summary>
    private const string AtTile = " at tile ";

    private const int Magic = 0x44465250; // "PRFD"

    private const int Version = 2;

    /// <summary>One gathered pixel of a roof section: the game's light and the three drawings', linear, where it stands and how deep.</summary>
    /// <param name="Shape">The section's shape.</param>
    /// <param name="Drawn">The drawn pixel.</param>
    /// <param name="Game">The game's light there.</param>
    /// <param name="With">The drawing with the sections.</param>
    /// <param name="Kept">The drawing without them, their shadows kept.</param>
    /// <param name="Gone">The drawing without them, their shadows gone.</param>
    /// <param name="Place">Its place, the stage's space.</param>
    /// <param name="Depth">Its depth along the view.</param>
    public readonly record struct Sample(int Shape, int Drawn, Vector3 Game, Vector3 With, Vector3 Kept, Vector3 Gone, Vector3 Place, float Depth);

    /// <summary>One gathered pixel where the hidden sections' shadows fall: the game's light, and the stage without those sections, their shadows kept and gone.</summary>
    /// <param name="Drawn">The drawn pixel.</param>
    /// <param name="Game">The game's light there.</param>
    /// <param name="Kept">The drawing without the hidden sections, their shadows kept.</param>
    /// <param name="Gone">Without them, their shadows gone.</param>
    public readonly record struct Shade(int Drawn, Vector3 Game, Vector3 Kept, Vector3 Gone);

    /// <summary>A tile piece: a tile file laid in the area, its footprint from its least tile to its most, both in.</summary>
    /// <param name="File">The tile file.</param>
    /// <param name="MinX">Its least tile across - the corner its source names.</param>
    /// <param name="MinY">Its least tile down.</param>
    /// <param name="MaxX">Its most tile across, below the least where only the corner is known.</param>
    /// <param name="MaxY">Its most tile down.</param>
    public sealed record Piece(string File, int MinX, int MinY, int MaxX, int MaxY)
    {
        /// <summary>Whether its footprint is known, not its corner alone.</summary>
        public bool Known => MaxX >= MinX && MaxY >= MinY;

        /// <summary>Whether a tile lies in its footprint.</summary>
        public bool Holds(int x, int y) => Known && x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

        /// <summary>Whether another is this piece: its file laid from the same corner.</summary>
        public bool Same(Piece? other) => other is not null && MinX == other.MinX && MinY == other.MinY && string.Equals(File, other.File, StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether another piece's footprint meets this one's, edge or corner, without being it.</summary>
        public bool Touches(Piece? other)
            => Known && other is { Known: true } && !Same(other)
                && MinX <= other.MaxX + 1 && other.MinX <= MaxX + 1 && MinY <= other.MaxY + 1 && other.MinY <= MaxY + 1;

        /// <summary>The piece in words: its file's name, its corner and its size in tiles.</summary>
        public string Said
            => Known
                ? string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(File)} at tile {MinX}, {MinY}, {MaxX - MinX + 1} x {MaxY - MinY + 1}")
                : string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(File)} at tile {MinX}, {MinY}");
    }

    /// <summary>A section of the stage, as the numbers keep it.</summary>
    /// <param name="Shape">Its shape.</param>
    /// <param name="Material">The material it wears.</param>
    /// <param name="Source">What it came from - an .ao, a tile's file and the piece's corner.</param>
    /// <param name="Least">Its box's least corner, the stage's space.</param>
    /// <param name="Most">Its box's most.</param>
    /// <param name="Piece">The tile piece it belongs to, or null where it is none.</param>
    public sealed record Section(int Shape, string Material, string Source, Vector3 Least, Vector3 Most, Piece? Piece = null);

    /// <summary>What the numbers hold beside the samples: the drawing's size, the stage's corner, the player, the game's eye and its ways.</summary>
    /// <param name="Width">The drawn width.</param>
    /// <param name="Height">The drawn height.</param>
    /// <param name="Factor">The screen's pixels a drawn one stands for, each way.</param>
    /// <param name="Origin">The stage's corner in the area.</param>
    /// <param name="Player">The player in the area, NaN where none was read.</param>
    /// <param name="Eye">The game's eye.</param>
    /// <param name="Forward">The way it looks.</param>
    /// <param name="Right">The way the screen's x grows - nought in numbers of version 1, then worked out from the forward way.</param>
    /// <param name="PlayerPixel">The player's drawn pixel.</param>
    /// <param name="Sections">Every roof section of the stage.</param>
    public sealed record Header(
        int Width, int Height, int Factor, Vector2 Origin, Vector3 Player, Vector3 Eye, Vector3 Forward, Vector3 Right, Vector2 PlayerPixel,
        IReadOnlyList<Section> Sections)
    {
        /// <summary>The tile piece the player stands on, or null where it is not known.</summary>
        public Piece? PlayerPiece { get; init; }

        /// <summary>The ground types at the corners of the player's tile, each marked where its file says HideRoof - or why none were read.</summary>
        public string PlayerGround { get; init; } = string.Empty;

        /// <summary>The sections the verdicts called hidden, which the shadow drawings left out.</summary>
        public IReadOnlyList<int> Removed { get; init; } = [];
    }

    /// <summary>The game's pattern held against the three drawings over a section's telling pixels - see the remarks.</summary>
    /// <param name="Count">The telling pixels.</param>
    /// <param name="With">The correlation of the game's log luminance with the drawing's with the section.</param>
    /// <param name="Kept">With the drawing's without it, its shadow kept.</param>
    /// <param name="Gone">With the drawing's without it, its shadow gone.</param>
    /// <param name="Share">How much of the game's centred log luminance the drawing without it (its shadow kept) stands for, fitted with the one with it.</param>
    public readonly record struct Judged(int Count, float With, float Kept, float Gone, float Share)
    {
        /// <summary>The better of the two correlations without the section.</summary>
        public float Without => float.IsNaN(Gone) || Kept >= Gone ? Kept : Gone;

        /// <summary>"hidden", "shown", "unclear", or "-" over too few telling pixels.</summary>
        public string Verdict
            => Count < FewestTelling || float.IsNaN(With) || float.IsNaN(Without) ? "-"
                : Without - With >= Margin ? "hidden" : With - Without >= Margin ? "shown" : "unclear";
    }

    /// <summary>The game's pattern over the hidden sections' shadows: its correlation with the drawing keeping them and the one dropping them.</summary>
    /// <param name="Count">The pixels the two drawings tell apart.</param>
    /// <param name="Kept">The correlation with the drawing that keeps their shadows.</param>
    /// <param name="Gone">With the one that drops them.</param>
    /// <param name="Share">How much of the game's centred log luminance the dropping one stands for, fitted with the keeping one.</param>
    public readonly record struct Shadowed(int Count, float Kept, float Gone, float Share)
    {
        /// <summary>"kept", "dropped", "unclear", or "-" over too few pixels.</summary>
        public string Verdict
            => Count < FewestTelling || float.IsNaN(Kept) || float.IsNaN(Gone) ? "-"
                : Gone - Kept >= Margin ? "dropped" : Kept - Gone >= Margin ? "kept" : "unclear";
    }

    /// <summary>
    /// The part: every roof section the chosen stage draws, measured against the game's picture - its text; the tinted picture and the numbers
    /// handed back.
    /// </summary>
    /// <param name="read">The install's files - the materials' .mat, the objects' .ao, the master, the environment and the ground types.</param>
    /// <param name="scene">The stage drawn through the game's camera with the game's picture (CaptureAtGame.Draw).</param>
    /// <param name="environment">The area's environment, or empty.</param>
    /// <param name="tiles">The area's laid tiles, or null - the sections' pieces and the player's.</param>
    /// <param name="ground">The area's ground types at the tile corners, or null - the ground under the player.</param>
    /// <param name="search">Where it says how far it is, or null.</param>
    /// <param name="pictures">The tinted picture, or none.</param>
    /// <param name="numbers">The numbers, or none.</param>
    public static string Write(
        Func<string, byte[]?>? read, CaptureAtGame scene, string environment, TerrainTiles? tiles, TerrainGroundTypes? ground, CaptureAtGame.Search? search,
        out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers)
    {
        ArgumentNullException.ThrowIfNull(scene);
        pictures = [];
        numbers = [];
        MonsterModel model = scene.Pane.Model;
        SkinnedMesh mesh = model.Mesh;
        var roofs = new CaptureLightCompare.RoofSections(read);
        var roof = new bool[mesh.Shapes.Count];
        var sections = new List<Section>();
        Dictionary<string, int>? ids = tiles is null ? null : IdsOf(tiles);
        for (var shape = 0; shape < roof.Length; shape++)
        {
            string name = CaptureLightCompare.MaterialName(model, shape, roofs);
            if (!name.StartsWith(CaptureLightCompare.RoofWord, StringComparison.Ordinal) || mesh.Shapes[shape].Count == 0)
            {
                continue;
            }

            roof[shape] = true;
            (Vector3 least, Vector3 most) = BoxOf(mesh, shape);
            string source = shape < model.ShapeSources.Count ? model.ShapeSources[shape] : string.Empty;
            sections.Add(new Section(shape, name[CaptureLightCompare.RoofWord.Length..], source, least, most, PieceOf(source, tiles, ids)));
        }

        var text = new StringBuilder();
        if (sections.Count == 0)
        {
            text.Append("roof fade: no roof sections on the stage - ").AppendLine(roofs.Said());
            text.AppendLine(string.Join(Environment.NewLine, scene.Said));
            return text.ToString();
        }

        // THE TWO DRAWINGS WITHOUT THE SECTIONS: drawn as shadow casters alone, and left out of the mesh.
        search?.Begin(2);
        search?.Step("drawing the stage again without its roof sections, their shadows kept");
        MaterialBlend[] given = new MaterialBlend[mesh.Shapes.Count];
        for (var shape = 0; shape < given.Length; shape++)
        {
            given[shape] = scene.Pane.Blends is { } blends && shape < blends.Count ? blends[shape] : MaterialBlend.Opaque;
        }

        byte[] kept = scene.Redrawn(mesh, Without(given, roof, keepShadows: true));
        search?.Step("drawing the stage again without its roof sections, their shadows gone");
        int[] shapeOf = scene.ShapeOf;
        byte[] gone = scene.Redrawn(mesh, Without(given, roof, keepShadows: false));

        // EVERY GATHERED PIXEL OF A SECTION the game's picture shows plainly: not masked, not at its ceiling, something behind it on the stage.
        var samples = new List<Sample>();
        var behindNothing = 0;
        PixelGather gather = scene.Gather;
        for (var at = 0; at < gather.Triangle.Length; at++)
        {
            int triangle = gather.Triangle[at];
            int drawn = triangle < 0 ? -1 : scene.DrawnAt(at);
            if (drawn < 0 || triangle >= shapeOf.Length || shapeOf[triangle] < 0 || !roof[shapeOf[triangle]] || Hidden(scene, drawn))
            {
                continue;
            }

            Vector3 game = scene.GameAt(drawn);
            if (float.IsNaN(game.X))
            {
                continue;
            }

            if (kept[(drawn * 4) + 3] == 0 || gone[(drawn * 4) + 3] == 0)
            {
                behindNothing++;
                continue;
            }

            samples.Add(new Sample(
                shapeOf[triangle], drawn, game, LinearOf(scene.Picture, drawn), LinearOf(kept, drawn), LinearOf(gone, drawn), gather.Place[at], gather.Depth[at]));
        }

        // THE SECTIONS CALLED HIDDEN, their shadows held against the game where they fall.
        ILookup<int, Sample> byShape = samples.ToLookup(one => one.Shape);
        int[] removed = [.. sections.Select(one => one.Shape).Where(shape => Judge(byShape[shape]).Verdict == "hidden")];
        List<Shade> shades = removed.Length > 0 ? Shades(scene, mesh, given, removed, search) : [];

        GameCamera camera = scene.Camera;
        Vector3 player = scene.PlayerWorld;
        Vector2 playerPixel = float.IsNaN(player.X) ? new Vector2(float.NaN) : camera.Pixel(player) / scene.Factor;
        (int tileX, int tileY) = TileOf(player);
        var header = new Header(
            scene.DrawnWidth, scene.DrawnHeight, scene.Factor, scene.Origin, player, camera.Eye, camera.Forward, camera.Right, playerPixel, sections)
        {
            PlayerPiece = tiles is null || float.IsNaN(player.X) ? null : PieceAt(tiles, tileX, tileY),
            PlayerGround = float.IsNaN(player.X) ? "no player read" : GroundUnder(ground, read, tileX, tileY),
            Removed = removed,
        };
        numbers = Write(header, samples, shades);

        text.Append(Report(header, samples, shades, "roof fade"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{behindNothing:N0} section pixels left out: nothing of the stage behind them, the game's picture there showing what lies past it"));
        text.AppendLine(roofs.Said());
        text.AppendLine(tiles is null
            ? "the area's tiles: not read - no section's piece and not the player's"
            : string.Create(CultureInfo.InvariantCulture, $"the area's tiles: {tiles.Width} x {tiles.Height}, each section's piece read from the corner its source names"));

        // EACH SECTION'S OBJECT'S WORDS, then the area's.
        text.AppendLine().AppendLine("=== the sections' objects' roof fade words (an object's .ao and what it extends, any case; a tile piece's are its master's, its ground's and its room's - no .tdt, .tgt or .tmd of the install names one, light-extras section 10)");
        int ofPieces = 0;
        foreach (string source in sections.Select(one => one.Source).Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            if (source.Contains(AtTile, StringComparison.Ordinal))
            {
                ofPieces++;
                continue;
            }

            text.Append("  ").Append(source).Append(": ").AppendLine(read is null ? "no install to read" : Words(read, source));
        }

        text.Append(CultureInfo.InvariantCulture, $"  {ofPieces} tile pieces: their words are the area's, below").AppendLine();
        text.AppendLine().AppendLine("=== the area's roof fade words");
        text.Append("  master ").Append(model.MasterFile.Length > 0 ? model.MasterFile : "(none named by the stage)").Append(": ")
            .AppendLine(read is null || model.MasterFile.Length == 0 ? "not read" : Words(read, model.MasterFile));
        text.Append("  environment ").Append(environment.Length > 0 ? environment : "(none)").Append(": ")
            .AppendLine(read is null || environment.Length == 0 ? "not read" : Words(read, environment));
        text.Append("  the ground under the player: ").AppendLine(header.PlayerGround);

        text.AppendLine().AppendLine(string.Join(Environment.NewLine, scene.Said));
        text.AppendLine(Tinted + ": the game's picture dimmed; each section's telling pixel tinted red where the game's pattern there stands nearer the drawing without the section, green where nearer the one with it (each logarithm less its section's mean, over its spread); grey a section pixel that tells nothing; where the hidden sections' shadows fall, blue nearer the drawing keeping them, yellow nearer the one dropping them");
        text.AppendLine(Format);
        pictures = [(Tinted, Tint(scene, header, samples, shades))];
        return text.ToString();
    }

    /// <summary>What the numbers file holds, for the report's last line.</summary>
    private const string Format = Numbers + ": little-endian: \"PRFD\", int version 2, int drawn width, int drawn height, int factor, floats the stage's corner x y, the player in the world x y z (NaN none), the game's eye x y z, its forward x y z, its right x y z, the player's drawn pixel x y; the player's piece; a ushort-length UTF-8 string, the ground under him; int sections, each: int shape, two strings (material, source), floats its box's least x y z and most x y z (the stage's space), its piece; int samples, each: int shape, int drawn pixel, floats the game's linear rgb, the drawing with the sections, without them their shadows kept, without them their shadows gone, its place x y z, its depth; int sections called hidden, each its shape; int shadow pixels, each: int drawn pixel, floats the game's linear rgb, the stage without the hidden sections their shadows kept, and gone. A piece: a string, its file (empty: none), ints its least tile x y and most x y (the most below the least: its corner alone). Version 1 lacks the right, the pieces, the ground, the hidden list and the shadow pixels";

    /// <summary>The report over the numbers - the part's and the replay's alike.</summary>
    internal static string Report(Header header, IReadOnlyList<Sample> samples, IReadOnlyList<Shade> shades, string title)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(shades);
        var text = new StringBuilder();
        ILookup<int, Sample> byShape = samples.ToLookup(one => one.Shape);
        var rows = header.Sections
            .Select(section => (Section: section, Judged: Judge(byShape[section.Shape]), Pixels: byShape[section.Shape].Count()))
            .ToList();
        int telling = rows.Sum(one => one.Judged.Count);
        int Counted(string verdict) => rows.Count(one => one.Pixels > 0 && one.Judged.Verdict == verdict);
        text.Append(CultureInfo.InvariantCulture,
            $"{title}: {header.Sections.Count} roof sections on the stage, {rows.Count(one => one.Pixels > 0)} on the screen, {samples.Count:N0} of their pixels compared, {telling:N0} telling the drawings with and without them apart; {Counted("hidden")} hidden by the game, {Counted("shown")} shown, {Counted("unclear")} unclear, {Counted("-")} too few telling pixels").AppendLine();
        text.AppendLine(Player(header));
        text.AppendLine(Shadows(header, shades));
        text.AppendLine();
        text.Append(Rules(header, rows));

        // THE SECTIONS: on the screen first, the most pixels first; then the rest, the nearest first.
        text.AppendLine().AppendLine("=== each section: the game's log luminance correlated with the drawing with it and the two without it over its telling pixels, the verdict (hidden where the better without leads by " + Margin.ToString("0.0#", CultureInfo.InvariantCulture) + ", shown where the with does), the share without of a fit of the game by both; where it stands to the player");
        text.AppendLine("  shape\tmaterial\tpixels\ttelling\tverdict\tr with\tr without, shadow kept\tr without, shadow gone\tshare without\tpiece\tup the screen from the player\tacross the screen\tacross to the player\tover the player\teye-player line passes\tat\tscreen x from the player\tscreen y\tnearest pixel\tdepth less the player's\tbox in the area\tsource");
        (Vector2 up, Vector2 right) = Ways(header);
        foreach ((Section section, Judged judged, int pixels) in rows
            .OrderByDescending(one => one.Pixels > 0)
            .ThenByDescending(one => one.Pixels)
            .ThenBy(one => Across(header, one.Section)))
        {
            Sample[] own = [.. byShape[section.Shape]];
            text.Append(CultureInfo.InvariantCulture, $"  {section.Shape}\t{section.Material}\t{pixels:N0}\t{judged.Count:N0}\t{judged.Verdict}\t");
            text.Append(Number(judged.With, "0.00")).Append('\t').Append(Number(judged.Kept, "0.00")).Append('\t').Append(Number(judged.Gone, "0.00")).Append('\t')
                .Append(judged.Count < FewestTelling ? "-" : Number(judged.Share, "0.00")).Append('\t');
            text.Append(PieceWord(header, section.Piece)).Append('\t');
            text.Append(Where(header, section, own, up, right)).Append('\t').AppendLine(section.Source);
        }

        text.Append(Pieces(header, rows, up, right));
        return text.ToString();
    }

    /// <summary>The player's line: where he stands, how the game's eye looks at him, his tile, its piece and the ground under him.</summary>
    private static string Player(Header header)
    {
        if (float.IsNaN(header.Player.X))
        {
            return "the player: none read - the sections' places to him are not measured";
        }

        Vector3 local = header.Player - new Vector3(header.Origin, 0f);
        float depth = Vector3.Dot(header.Player - header.Eye, header.Forward);
        (int tileX, int tileY) = TileOf(header.Player);
        (Vector2 up, Vector2 right) = Ways(header);
        string worked = header.Right == Vector3.Zero ? " (worked out from the forward way: numbers of version 1)" : string.Empty;
        string piece = header.PlayerPiece?.Said ?? "not known";
        string ground = header.PlayerGround.Length > 0 ? header.PlayerGround : "not in these numbers";
        return string.Create(CultureInfo.InvariantCulture,
            $"the player at ({header.Player.X:0.#}, {header.Player.Y:0.#}, {header.Player.Z:0.#}) in the area, ({local.X:0.#}, {local.Y:0.#}, {local.Z:0.#}) on the stage, at drawn pixel ({header.PlayerPixel.X:0.#}, {header.PlayerPixel.Y:0.#}) of {header.Width} x {header.Height} (1/{header.Factor} of the screen), {depth:0.#} units along the view from the game's eye at ({header.Eye.X:0.#}, {header.Eye.Y:0.#}, {header.Eye.Z:0.#}); up the screen across the ground ({up.X:0.###}, {up.Y:0.###}), right ({right.X:0.###}, {right.Y:0.###}){worked}; on tile {tileX}, {tileY}, of the piece {piece}; the ground there: {ground}");
    }

    /// <summary>The line on the hidden sections' shadows.</summary>
    private static string Shadows(Header header, IReadOnlyList<Shade> shades)
    {
        if (header.Removed.Count == 0)
        {
            return "the hidden sections' shadows: no section called hidden, or numbers of version 1 - not measured";
        }

        Shadowed shadowed = Shadow(shades);
        string said = shadowed.Verdict switch
        {
            "kept" => "the game KEEPS a hidden section's shadow",
            "dropped" => "the game DROPS a hidden section's shadow",
            "unclear" => "unclear",
            _ => "too few pixels",
        };
        string share = shadowed.Count < FewestTelling ? "-" : Number(shadowed.Share, "0.00");
        return string.Create(CultureInfo.InvariantCulture,
            $"the hidden sections' shadows ({string.Join(", ", header.Removed)}): {shadowed.Count:N0} pixels where the stage without them tells their shadows kept from gone, and a band of {Band} gathered pixels round them (the step at a shadow's edge, which a scale inside it cannot show); the game's log luminance goes with the shadows kept r {Number(shadowed.Kept, "0.00")}, gone r {Number(shadowed.Gone, "0.00")}, share gone {share} - {said}");
    }

    /// <summary>The readings held against every verdict: the player's piece fading whole, and with the pieces touching it.</summary>
    private static string Rules(Header header, IReadOnlyList<(Section Section, Judged Judged, int Pixels)> rows)
    {
        var text = new StringBuilder();
        text.AppendLine("=== readings held against the verdicts (hidden or shown, sections of a tile piece)");
        var verdicted = rows.Where(one => one.Judged.Verdict is "hidden" or "shown").ToList();
        int loose = verdicted.Count(one => one.Section.Piece is null);
        if (header.PlayerPiece is not { } mine)
        {
            text.AppendLine("  the player's piece: not known (no tiles read, or numbers of version 1) - no reading held");
            return text.ToString();
        }

        void Held(string reading, Func<Piece, bool> hides)
        {
            var held = verdicted.Where(one => one.Section.Piece is not null).ToList();
            var against = held.Where(one => (one.Judged.Verdict == "hidden") != hides(one.Section.Piece!)).ToList();
            text.Append(CultureInfo.InvariantCulture, $"  {reading}: {held.Count - against.Count} of {held.Count} verdicts agree");
            if (against.Count > 0)
            {
                text.Append(" - against it: ").Append(string.Join(", ", against.Select(one => string.Create(CultureInfo.InvariantCulture, $"{one.Section.Shape} {one.Judged.Verdict} ({one.Section.Piece!.Said})"))));
            }

            text.AppendLine();
        }

        Held("the piece the player stands on fades whole, no other (a reading of RoofFadeMode \"Tile\")", piece => piece.Same(mine));
        Held("his piece and every piece touching it fade (a reading of an island of pieces)", piece => piece.Same(mine) || piece.Touches(mine));
        if (loose > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {loose} verdicts on sections of no tile piece left out").AppendLine();
        }

        return text.ToString();
    }

    /// <summary>The pieces carrying roof sections: each one's footprint, where it stands to the player, and its sections' verdicts.</summary>
    private static string Pieces(Header header, IReadOnlyList<(Section Section, Judged Judged, int Pixels)> rows, Vector2 up, Vector2 right)
    {
        var text = new StringBuilder();
        var pieces = rows.Where(one => one.Section.Piece is not null)
            .GroupBy(one => (File: one.Section.Piece!.File.ToUpperInvariant(), one.Section.Piece.MinX, one.Section.Piece.MinY))
            .Select(group => (Piece: group.First().Section.Piece!, Rows: group.ToList()))
            .OrderByDescending(one => one.Rows.Sum(row => row.Pixels) > 0)
            .ThenBy(one => one.Rows.Min(row => Across(header, row.Section)))
            .ToList();
        text.AppendLine().AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"=== the tile pieces carrying roof sections: {pieces.Count} - each one's footprint, whether it is the player's or touches his, where it stands on the ground from him (tiles of {TileModels.Side:0} units), its sections on the screen by verdict and those off it"));
        text.AppendLine("  piece\tto the player's\tup the screen from the player\tacross the screen\thidden\tshown\tunclear\ttoo few\toff the screen\tfile");
        foreach ((Piece piece, List<(Section Section, Judged Judged, int Pixels)> own) in pieces)
        {
            string upward = "-", across = "-";
            if (piece.Known && !float.IsNaN(header.Player.X))
            {
                var least = new Vector2(piece.MinX, piece.MinY) * TileModels.Side;
                var most = new Vector2(piece.MaxX + 1, piece.MaxY + 1) * TileModels.Side;
                var at = new Vector2(header.Player.X, header.Player.Y);
                upward = Range(Spread(up, least, most, at));
                across = Range(Spread(right, least, most, at));
            }

            int Of(string verdict) => own.Count(one => one.Pixels > 0 && one.Judged.Verdict == verdict);
            text.Append(CultureInfo.InvariantCulture,
                $"  {piece.Said}\t{PieceWord(header, piece)}\t{upward}\t{across}\t{Of("hidden")}\t{Of("shown")}\t{Of("unclear")}\t{Of("-")}\t{own.Count(one => one.Pixels == 0)}\t{piece.File}").AppendLine();
        }

        return text.ToString();
    }

    /// <summary>How a piece stands to the player's: "his", "touches his", "other", "?" where his is not known, "-" no piece.</summary>
    private static string PieceWord(Header header, Piece? piece)
        => piece is null ? "-" : header.PlayerPiece is not { } mine ? "?" : piece.Same(mine) ? "his" : piece.Touches(mine) ? "touches his" : "other";

    /// <summary>Where a section stands to the player and the game's eye - the report's columns.</summary>
    private static string Where(Header header, Section section, IReadOnlyList<Sample> own, Vector2 up, Vector2 right)
    {
        Vector3 origin = new(header.Origin, 0f);
        Vector3 least = section.Least + origin, most = section.Most + origin;
        string box = string.Create(CultureInfo.InvariantCulture, $"({least.X:0}, {least.Y:0}, {least.Z:0}) to ({most.X:0}, {most.Y:0}, {most.Z:0})");
        if (float.IsNaN(header.Player.X))
        {
            return "-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t" + box;
        }

        Vector3 player = header.Player;
        var at = new Vector2(player.X, player.Y);
        string upward = Range(Spread(up, new Vector2(least.X, least.Y), new Vector2(most.X, most.Y), at));
        string across = Range(Spread(right, new Vector2(least.X, least.Y), new Vector2(most.X, most.Y), at));

        // UP IS MINUS Z: how high over the player's place the box reaches, its bottom to its top.
        string over = string.Create(CultureInfo.InvariantCulture, $"{player.Z - most.Z:0} to {player.Z - least.Z:0}");
        float flat = Across(header, section);

        // THE LINE FROM THE EYE TO THE PLAYER: its nearest pass by the box, and where along it.
        float nearest = float.MaxValue, along = 0f;
        for (var step = 0; step <= LineSteps; step++)
        {
            float share = step / (float)LineSteps;
            Vector3 point = Vector3.Lerp(header.Eye, player, share);
            float distance = Vector3.Distance(point, Vector3.Clamp(point, least, most));
            if (distance < nearest)
            {
                (nearest, along) = (distance, share);
            }
        }

        // ITS PIXELS ON THE SCREEN from the player's: their spread across and down, the nearest, and the nearest depth.
        float screen = float.NaN, nearestDepth = float.NaN;
        float leastX = float.MaxValue, mostX = float.MinValue, leastY = float.MaxValue, mostY = float.MinValue;
        foreach (Sample one in own)
        {
            float x = (one.Drawn % header.Width) + 0.5f - header.PlayerPixel.X, y = (one.Drawn / header.Width) + 0.5f - header.PlayerPixel.Y;
            (leastX, mostX, leastY, mostY) = (MathF.Min(leastX, x), MathF.Max(mostX, x), MathF.Min(leastY, y), MathF.Max(mostY, y));
            float distance = MathF.Sqrt((x * x) + (y * y));
            screen = float.IsNaN(screen) ? distance : MathF.Min(screen, distance);
            nearestDepth = float.IsNaN(nearestDepth) ? one.Depth : MathF.Min(nearestDepth, one.Depth);
        }

        float playerDepth = Vector3.Dot(player - header.Eye, header.Forward);
        bool seen = own.Count > 0 && !float.IsNaN(screen);
        return string.Create(CultureInfo.InvariantCulture,
            $"{upward}\t{across}\t{flat:0}\t{over}\t{nearest:0}\t{along:0.00}\t{(seen ? Range((leastX, mostX)) : "-")}\t{(seen ? Range((leastY, mostY)) : "-")}\t{(seen ? screen.ToString("0", CultureInfo.InvariantCulture) : "-")}\t{(seen ? (nearestDepth - playerDepth).ToString("0", CultureInfo.InvariantCulture) : "-")}\t{box}");
    }

    /// <summary>How far across the ground the player stands from a section's box - nought inside it, NaN with no player.</summary>
    private static float Across(Header header, Section section)
    {
        if (float.IsNaN(header.Player.X))
        {
            return float.NaN;
        }

        var at = new Vector2(header.Player.X, header.Player.Y);
        Vector2 least = new Vector2(section.Least.X, section.Least.Y) + header.Origin, most = new Vector2(section.Most.X, section.Most.Y) + header.Origin;
        return Vector2.Distance(at, Vector2.Clamp(at, least, most));
    }

    /// <summary>The camera's two ways across the ground: up the screen - its forward way laid flat - and right, the screen's x laid flat.</summary>
    /// <remarks>
    /// NUMBERS OF VERSION 1 KEEP NO RIGHT WAY, so it is worked out from up as the game's left-handed frame turns it, (up.y, -up.x): its camera
    /// looks along +x +y with +x -y to the right (CLAUDE.md), and the 09:12 capture's sections across to the left of the player stand left of
    /// him on the screen.
    /// </remarks>
    internal static (Vector2 Up, Vector2 Right) Ways(Header header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var up = new Vector2(header.Forward.X, header.Forward.Y);
        up = up.LengthSquared() > 1e-12f ? Vector2.Normalize(up) : Vector2.UnitY;
        var right = new Vector2(header.Right.X, header.Right.Y);
        right = right.LengthSquared() > 1e-12f ? Vector2.Normalize(right) : new Vector2(up.Y, -up.X);
        return (up, right);
    }

    /// <summary>The least and most a box's four corners on the ground stand along a way from a point.</summary>
    private static (float Least, float Most) Spread(Vector2 way, Vector2 least, Vector2 most, Vector2 from)
    {
        float a = Vector2.Dot(least - from, way), b = Vector2.Dot(new Vector2(most.X, least.Y) - from, way);
        float c = Vector2.Dot(new Vector2(least.X, most.Y) - from, way), d = Vector2.Dot(most - from, way);
        return (MathF.Min(MathF.Min(a, b), MathF.Min(c, d)), MathF.Max(MathF.Max(a, b), MathF.Max(c, d)));
    }

    private static string Range((float Least, float Most) range)
        => string.Create(CultureInfo.InvariantCulture, $"{range.Least:0} to {range.Most:0}");

    private static string Number(float value, string format) => float.IsNaN(value) ? "-" : value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>The game's pattern against the three drawings over a section's pixels where the drawings with and without it tell apart.</summary>
    internal static Judged Judge(IEnumerable<Sample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var values = new List<Quad>();
        foreach (Sample one in samples)
        {
            float with = CaptureAtGame.Luminance(one.With), without = CaptureAtGame.Luminance(one.Kept);
            if (Tells(with, without))
            {
                values.Add(new Quad(Log(CaptureAtGame.Luminance(one.Game)), Log(with), Log(without), Log(CaptureAtGame.Luminance(one.Gone))));
            }
        }

        (float shown, float kept, float gone, float share) = Correlated(values);
        return new Judged(values.Count, shown, kept, gone, share);
    }

    /// <summary>The game's pattern against the drawings keeping and dropping the hidden sections' shadows.</summary>
    internal static Shadowed Shadow(IEnumerable<Shade> shades)
    {
        ArgumentNullException.ThrowIfNull(shades);
        var values = new List<Quad>();
        foreach (Shade one in shades)
        {
            double gone = Log(CaptureAtGame.Luminance(one.Gone));
            values.Add(new Quad(Log(CaptureAtGame.Luminance(one.Game)), Log(CaptureAtGame.Luminance(one.Kept)), gone, gone));
        }

        (float kept, float dropped, _, float share) = Correlated(values);
        return new Shadowed(values.Count, kept, dropped, share);
    }

    /// <summary>Whether two luminances stand a fifth of the brighter apart.</summary>
    private static bool Tells(float one, float other)
    {
        float brighter = MathF.Max(one, other);
        return brighter > 0f && MathF.Abs(one - other) >= Telling * brighter;
    }

    private static double Log(float luminance) => Math.Log(Math.Max(0f, luminance) + LogFloor);

    /// <summary>The game's value at a pixel and three drawings'.</summary>
    private readonly record struct Quad(double Game, double First, double Second, double Third);

    /// <summary>
    /// The correlation of the game's values with each drawing's, and the second drawing's share of the game fitted by the first two - each
    /// less its mean, in two passes. A drawing that stands still beside a game that does not explains none of it: nought, not a gap.
    /// </summary>
    private static (float First, float Second, float Third, float Share) Correlated(List<Quad> values)
    {
        int n = values.Count;
        if (n < 3)
        {
            return (float.NaN, float.NaN, float.NaN, float.NaN);
        }

        double mg = 0, ma = 0, mb = 0, mc = 0;
        foreach (Quad one in values)
        {
            (mg, ma, mb, mc) = (mg + one.Game, ma + one.First, mb + one.Second, mc + one.Third);
        }

        (mg, ma, mb, mc) = (mg / n, ma / n, mb / n, mc / n);
        double gg = 0, aa = 0, bb = 0, cc = 0, ga = 0, gb = 0, gc = 0, ab = 0;
        foreach (Quad one in values)
        {
            double g = one.Game - mg, a = one.First - ma, b = one.Second - mb, c = one.Third - mc;
            gg += g * g;
            aa += a * a;
            bb += b * b;
            cc += c * c;
            ga += g * a;
            gb += g * b;
            gc += g * c;
            ab += a * b;
        }

        double still = 1e-12 * n;
        if (gg <= still)
        {
            return (float.NaN, float.NaN, float.NaN, float.NaN);
        }

        float R(double together, double own) => own <= still ? 0f : (float)Math.Clamp(together / Math.Sqrt(gg * own), -1d, 1d);
        float share;
        if (aa <= still && bb <= still)
        {
            share = float.NaN;
        }
        else if (aa <= still || bb <= still)
        {
            share = aa <= still ? 1f : 0f;
        }
        else
        {
            double det = (aa * bb) - (ab * ab);
            double first = det > 1e-9 * aa * bb ? ((ga * bb) - (gb * ab)) / det : double.NaN;
            double second = det > 1e-9 * aa * bb ? ((gb * aa) - (ga * ab)) / det : double.NaN;
            share = Math.Abs(first + second) > 1e-12 ? (float)(second / (first + second)) : float.NaN;
        }

        return (R(ga, aa), R(gb, bb), R(gc, cc), share);
    }

    /// <summary>
    /// The stage drawn twice more without the hidden sections - their shadows kept, and gone - and every gathered pixel those two tell apart
    /// with a band of <see cref="Band"/> round them, where the game's picture shows plainly and no hidden section covers the drawing with them.
    /// </summary>
    /// <remarks>
    /// THE BAND, because inside a shadow the two drawings differ by a scale alone, which a correlation does not see: the step at the shadow's
    /// edge is what tells a kept shadow from a dropped one.
    /// </remarks>
    private static List<Shade> Shades(CaptureAtGame scene, SkinnedMesh mesh, MaterialBlend[] given, int[] removed, CaptureAtGame.Search? search)
    {
        var hidden = new bool[given.Length];
        foreach (int shape in removed)
        {
            hidden[shape] = true;
        }

        search?.Begin(2);
        search?.Step("drawing the stage without the sections the game hides, their shadows kept");
        byte[] kept = scene.Redrawn(mesh, Without(given, hidden, keepShadows: true));
        search?.Step("drawing the stage without the sections the game hides, their shadows gone");
        int[] shapeOf = scene.ShapeOf;
        byte[] gone = scene.Redrawn(mesh, Without(given, hidden, keepShadows: false));

        PixelGather gather = scene.Gather;
        int entries = gather.Triangle.Length;
        var usable = new bool[entries];
        var differs = new bool[entries];
        for (var at = 0; at < entries; at++)
        {
            int drawn = scene.DrawnAt(at);
            int triangle = gather.Triangle[at];
            if (drawn < 0 || (triangle >= 0 && triangle < shapeOf.Length && shapeOf[triangle] >= 0 && hidden[shapeOf[triangle]]) || Hidden(scene, drawn)
                || kept[(drawn * 4) + 3] == 0 || gone[(drawn * 4) + 3] == 0 || float.IsNaN(scene.GameAt(drawn).X))
            {
                continue;
            }

            usable[at] = true;
            differs[at] = Tells(CaptureAtGame.Luminance(LinearOf(kept, drawn)), CaptureAtGame.Luminance(LinearOf(gone, drawn)));
        }

        bool[] near = Dilated(differs, gather.Width, entries / Math.Max(1, gather.Width), Band);
        var shades = new List<Shade>();
        for (var at = 0; at < entries; at++)
        {
            if (usable[at] && near[at])
            {
                int drawn = scene.DrawnAt(at);
                shades.Add(new Shade(drawn, scene.GameAt(drawn), LinearOf(kept, drawn), LinearOf(gone, drawn)));
            }
        }

        return shades;
    }

    /// <summary>A mark grown by a reach each way, a square round every marked entry - two running counts, along the rows and down the columns.</summary>
    private static bool[] Dilated(bool[] marked, int width, int height, int reach)
    {
        var across = new bool[marked.Length];
        for (var y = 0; y < height; y++)
        {
            int row = y * width, count = 0;
            for (var x = -reach; x < width + reach; x++)
            {
                if (x + reach < width && marked[row + x + reach])
                {
                    count++;
                }

                if (x - reach - 1 >= 0 && marked[row + x - reach - 1])
                {
                    count--;
                }

                if (x >= 0 && x < width)
                {
                    across[row + x] = count > 0;
                }
            }
        }

        var grown = new bool[marked.Length];
        for (var x = 0; x < width; x++)
        {
            var count = 0;
            for (var y = -reach; y < height + reach; y++)
            {
                if (y + reach < height && across[((y + reach) * width) + x])
                {
                    count++;
                }

                if (y - reach - 1 >= 0 && across[((y - reach - 1) * width) + x])
                {
                    count--;
                }

                if (y >= 0 && y < height)
                {
                    grown[(y * width) + x] = count > 0;
                }
            }
        }

        return grown;
    }

    /// <summary>Whether the game's picture is no measure at a drawn pixel: masked, or at its ceiling.</summary>
    private static bool Hidden(CaptureAtGame scene, int drawn) => scene.Masked[drawn] || (scene.Clipped.Length > drawn && scene.Clipped[drawn]);

    /// <summary>
    /// The blends with the marked shapes not drawn: casting as they cast in the drawing with them - a cut-out through its texture
    /// (MaterialBlend.ShadowCut), a solid one whole, a mixed or added one not at all - or neither drawn nor cast (MaterialBlend.Hidden).
    /// </summary>
    /// <remarks>
    /// ON THE WHOLE MESH, NOT A MESH KEPT TO FEWER TRIANGLES (a review of v0.1.386-389): a kept mesh stands the staged monster in his bind
    /// pose (CaptureAtGame.Posed takes the pane's posed vertices for the pane's own mesh alone) and frames the sun's map over fewer
    /// vertices, so his limbs' shadows and the map's texels differed between the drawings for reasons no roof made. And not
    /// ShadowOnly for a cut-out section, which casts every triangle whole - a tree's leaves as square cards the game never cast.
    /// </remarks>
    private static MaterialBlend[] Without(MaterialBlend[] given, bool[] marked, bool keepShadows)
    {
        var blends = new MaterialBlend[given.Length];
        for (var shape = 0; shape < blends.Length; shape++)
        {
            blends[shape] = !marked[shape] ? given[shape]
                : !keepShadows ? MaterialBlend.Hidden
                : given[shape] switch
                {
                    MaterialBlend.Cutout => MaterialBlend.ShadowCut,
                    MaterialBlend.Alpha or MaterialBlend.Additive or MaterialBlend.Hidden => MaterialBlend.Hidden,
                    MaterialBlend.ShadowCut => MaterialBlend.ShadowCut,
                    _ => MaterialBlend.ShadowOnly,
                };
        }

        return blends;
    }

    /// <summary>The tile a place in the area stands on.</summary>
    private static (int X, int Y) TileOf(Vector3 place)
        => float.IsNaN(place.X) ? (-1, -1) : ((int)MathF.Floor(place.X / TileModels.Side), (int)MathF.Floor(place.Y / TileModels.Side));

    /// <summary>The tile files' ids by path, any case.</summary>
    private static Dictionary<string, int> IdsOf(TerrainTiles tiles)
    {
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var id = 0; id < tiles.Paths.Count; id++)
        {
            ids.TryAdd(tiles.Paths[id], id);
        }

        return ids;
    }

    /// <summary>
    /// The piece a section's source names - <c>file at tile x, y</c>, the piece's least corner (LaidRoomModels) - its footprint read from the
    /// area's tiles where they are known, its corner alone where not; null for a source that names no piece.
    /// </summary>
    internal static Piece? PieceOf(string source, TerrainTiles? tiles, IReadOnlyDictionary<string, int>? ids)
    {
        ArgumentNullException.ThrowIfNull(source);
        int at = source.LastIndexOf(AtTile, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        string file = source[..at];
        string[] corner = source[(at + AtTile.Length)..].Split(',', StringSplitOptions.TrimEntries);
        if (corner.Length < 2 || !int.TryParse(corner[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(corner[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
        {
            return null;
        }

        return tiles is not null && (ids ?? IdsOf(tiles)).TryGetValue(file, out int id) ? Footprint(tiles, id, x, y, file) : new Piece(file, x, y, x - 1, y - 1);
    }

    /// <summary>The piece of the tile at a column and row, its footprint whole - or null where the tile names no file.</summary>
    internal static Piece? PieceAt(TerrainTiles tiles, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        int id = tiles.IdAt(x, y);
        if (id < 0)
        {
            return null;
        }

        (int subX, int subY) = tiles.SubAt(x, y);
        return Footprint(tiles, id, x - subX, y - subY, id < tiles.Paths.Count ? tiles.Paths[id] : string.Empty);
    }

    /// <summary>
    /// A piece's footprint: every tile of its file within reach of its corner naming its place from that corner - TerrainTiles.Pieces' own
    /// rule, a tile of a piece naming its place in the laid footprint whichever way it was laid. The corner alone where none does.
    /// </summary>
    private static Piece Footprint(TerrainTiles tiles, int id, int cornerX, int cornerY, string file)
    {
        int leastX = int.MaxValue, leastY = int.MaxValue, mostX = int.MinValue, mostY = int.MinValue;
        int toX = Math.Min(tiles.Width, cornerX + PieceReach), toY = Math.Min(tiles.Height, cornerY + PieceReach);
        for (int y = Math.Max(0, cornerY); y < toY; y++)
        {
            for (int x = Math.Max(0, cornerX); x < toX; x++)
            {
                if (tiles.IdAt(x, y) != id)
                {
                    continue;
                }

                (int subX, int subY) = tiles.SubAt(x, y);
                if (x - subX == cornerX && y - subY == cornerY)
                {
                    (leastX, leastY, mostX, mostY) = (Math.Min(leastX, x), Math.Min(leastY, y), Math.Max(mostX, x), Math.Max(mostY, y));
                }
            }
        }

        return leastX > mostX ? new Piece(file, cornerX, cornerY, cornerX - 1, cornerY - 1) : new Piece(file, leastX, leastY, mostX, mostY);
    }

    /// <summary>The ground types at a tile's four corners, each marked where its file says HideRoof, or why none were read.</summary>
    private static string GroundUnder(TerrainGroundTypes? ground, Func<string, byte[]?>? read, int x, int y)
    {
        if (ground is null)
        {
            return "the area's ground types were not read";
        }

        var said = new List<string>();
        foreach (int type in new[] { ground.At(x, y), ground.At(x + 1, y), ground.At(x, y + 1), ground.At(x + 1, y + 1) }.Distinct())
        {
            if (!ground.Names(type))
            {
                said.Add(type < 0 ? "outside the corners" : "(no type named)");
                continue;
            }

            string file = ground.Types[type];
            string? content = read?.Invoke(file) is { Length: > 0 } bytes ? StatDescriptionFiles.Decode(bytes) : null;
            said.Add(file + (content is null ? " (not read)" : content.Contains("HideRoof", StringComparison.OrdinalIgnoreCase) ? " (HideRoof)" : string.Empty));
        }

        return string.Join(", ", said) + (ground.Trusted ? string.Empty : " - the ground types' read is not trusted");
    }

    /// <summary>
    /// The game's picture dimmed; each section's telling pixel tinted by which drawing it goes with - red without, green with - its
    /// logarithm less its section's mean, over its spread; grey a section pixel that tells nothing; the hidden sections' shadows blue where
    /// nearer the drawing keeping them, yellow where nearer the one dropping them.
    /// </summary>
    private static GamePicture Tint(CaptureAtGame scene, Header header, IReadOnlyList<Sample> samples, IReadOnlyList<Shade> shades)
    {
        int width = header.Width, height = header.Height;
        var rgba = new byte[width * height * 4];
        for (var at = 0; at < width * height; at++)
        {
            Vector3 game = scene.GameAt(at);
            Vector3 shown = float.IsNaN(game.X) ? Vector3.Zero : game * 0.4f;
            rgba[at * 4] = CaptureAtGame.Encoded(shown.X);
            rgba[(at * 4) + 1] = CaptureAtGame.Encoded(shown.Y);
            rgba[(at * 4) + 2] = CaptureAtGame.Encoded(shown.Z);
            rgba[(at * 4) + 3] = 255;
        }

        foreach (IGrouping<int, Sample> section in samples.GroupBy(one => one.Shape))
        {
            Sample[] telling = [.. section.Where(one => Tells(CaptureAtGame.Luminance(one.With), CaptureAtGame.Luminance(one.Kept)))];
            Standard game = Standard.Of(telling, one => one.Game), with = Standard.Of(telling, one => one.With), without = Standard.Of(telling, one => one.Kept);
            var told = new HashSet<int>(telling.Length >= FewestTelling ? telling.Select(one => one.Drawn) : []);
            foreach (Sample one in section)
            {
                int to = one.Drawn * 4;
                if (!told.Contains(one.Drawn))
                {
                    (rgba[to], rgba[to + 1], rgba[to + 2]) = ((byte)128, (byte)128, (byte)128);
                    continue;
                }

                float g = game.Of(one.Game);
                bool behind = MathF.Abs(g - without.Of(one.Kept)) < MathF.Abs(g - with.Of(one.With));
                (rgba[to], rgba[to + 1], rgba[to + 2]) = behind ? ((byte)235, (byte)50, (byte)40) : ((byte)40, (byte)220, (byte)60);
            }
        }

        if (shades.Count >= FewestTelling)
        {
            Standard game = Standard.Of(shades, one => one.Game), keeping = Standard.Of(shades, one => one.Kept), dropping = Standard.Of(shades, one => one.Gone);
            foreach (Shade one in shades)
            {
                float g = game.Of(one.Game);
                bool kept = MathF.Abs(g - keeping.Of(one.Kept)) <= MathF.Abs(g - dropping.Of(one.Gone));
                int to = one.Drawn * 4;
                (rgba[to], rgba[to + 1], rgba[to + 2]) = kept ? ((byte)40, (byte)120, (byte)255) : ((byte)255, (byte)210, (byte)40);
            }
        }

        return new GamePicture(width, height, rgba);
    }

    /// <summary>A logarithm of luminance less its mean over a set, over its spread.</summary>
    private readonly record struct Standard(double Mean, double Spread)
    {
        public static Standard Of<T>(IReadOnlyCollection<T> set, Func<T, Vector3> colour)
        {
            if (set.Count == 0)
            {
                return new Standard(0, 1);
            }

            double sum = 0, squares = 0;
            foreach (T one in set)
            {
                double value = Log(CaptureAtGame.Luminance(colour(one)));
                sum += value;
                squares += value * value;
            }

            double mean = sum / set.Count;
            return new Standard(mean, Math.Sqrt(Math.Max(1e-12, (squares / set.Count) - (mean * mean))));
        }

        public float Of(Vector3 colour) => (float)((Log(CaptureAtGame.Luminance(colour)) - Mean) / Spread);
    }

    /// <summary>A file's lines naming a roof fade word, followed through what it extends - or that none does.</summary>
    private static string Words(Func<string, byte[]?> read, string path)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? next = path;
        for (var hop = 0; hop < MostHops && next is not null && seen.Add(next); hop++)
        {
            byte[]? bytes = read(next);
            if (bytes is not { Length: > 0 })
            {
                lines.Add(hop == 0 ? "not in the install" : next + " not in the install");
                break;
            }

            string content = StatDescriptionFiles.Decode(bytes);
            string? extends = null;
            foreach (string raw in content.Split('\n'))
            {
                string line = raw.Trim();
                if (CaptureLightExtras.RoofFadeWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    lines.Add((hop == 0 ? string.Empty : next + ": ") + line);
                }

                if (extends is null && line.StartsWith("extends", StringComparison.Ordinal) && line.IndexOf('"', StringComparison.Ordinal) is var open and >= 0
                    && line.IndexOf('"', open + 1) is var close and > 0)
                {
                    string parent = line[(open + 1)..close];
                    extends = parent.Length == 0 || parent.Contains("nothing", StringComparison.OrdinalIgnoreCase) ? null
                        : Path.HasExtension(parent) ? parent : parent + Path.GetExtension(path);
                }
            }

            next = extends;
        }

        return lines.Count == 0 ? "names no roof fade word" : string.Join(" | ", lines);
    }

    /// <summary>A shape's box over the vertices its triangles use, the stage's space.</summary>
    private static (Vector3 Least, Vector3 Most) BoxOf(SkinnedMesh mesh, int shape)
    {
        MeshShape part = mesh.Shapes[shape];
        var least = new Vector3(float.MaxValue);
        var most = new Vector3(float.MinValue);
        for (int at = part.From; at < part.From + part.Count && at < mesh.Indices.Length; at++)
        {
            Vector3 place = mesh.Positions[mesh.Indices[at]];
            least = Vector3.Min(least, place);
            most = Vector3.Max(most, place);
        }

        return (least, most);
    }

    private static Vector3 LinearOf(byte[] rgba, int drawn)
    {
        float[] linear = CaptureAtGame.Linear;
        int at = drawn * 4;
        return new Vector3(linear[rgba[at]], linear[rgba[at + 1]], linear[rgba[at + 2]]);
    }

    /// <summary>The numbers, as <see cref="Format"/> says.</summary>
    internal static byte[] Write(Header header, IReadOnlyList<Sample> samples, IReadOnlyList<Shade> shades)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(shades);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(header.Width);
            writer.Write(header.Height);
            writer.Write(header.Factor);
            Floats(writer, header.Origin.X, header.Origin.Y, header.Player.X, header.Player.Y, header.Player.Z, header.Eye.X, header.Eye.Y, header.Eye.Z,
                header.Forward.X, header.Forward.Y, header.Forward.Z, header.Right.X, header.Right.Y, header.Right.Z, header.PlayerPixel.X, header.PlayerPixel.Y);
            Write(writer, header.PlayerPiece);
            Text(writer, header.PlayerGround);
            writer.Write(header.Sections.Count);
            foreach (Section section in header.Sections)
            {
                writer.Write(section.Shape);
                Text(writer, section.Material);
                Text(writer, section.Source);
                Floats(writer, section.Least.X, section.Least.Y, section.Least.Z, section.Most.X, section.Most.Y, section.Most.Z);
                Write(writer, section.Piece);
            }

            writer.Write(samples.Count);
            foreach (Sample one in samples)
            {
                writer.Write(one.Shape);
                writer.Write(one.Drawn);
                Floats(writer, one.Game.X, one.Game.Y, one.Game.Z, one.With.X, one.With.Y, one.With.Z, one.Kept.X, one.Kept.Y, one.Kept.Z,
                    one.Gone.X, one.Gone.Y, one.Gone.Z, one.Place.X, one.Place.Y, one.Place.Z, one.Depth);
            }

            writer.Write(header.Removed.Count);
            foreach (int shape in header.Removed)
            {
                writer.Write(shape);
            }

            writer.Write(shades.Count);
            foreach (Shade one in shades)
            {
                writer.Write(one.Drawn);
                Floats(writer, one.Game.X, one.Game.Y, one.Game.Z, one.Kept.X, one.Kept.Y, one.Kept.Z, one.Gone.X, one.Gone.Y, one.Gone.Z);
            }
        }

        return stream.ToArray();
    }

    /// <summary>The numbers read back - see <see cref="Format"/>; version 1's too, its pieces from its sources' corners.</summary>
    internal static (Header Header, List<Sample> Samples, List<Shade> Shades) Read(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        int version = reader.ReadInt32() == Magic ? reader.ReadInt32() : -1;
        if (version is not (1 or Version))
        {
            throw new InvalidDataException("not roof-fade numbers of version 1 or " + Version);
        }

        int width = reader.ReadInt32(), height = reader.ReadInt32(), factor = reader.ReadInt32();
        var origin = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        Vector3 player = Vector(reader), eye = Vector(reader), forward = Vector(reader);
        Vector3 right = version >= 2 ? Vector(reader) : Vector3.Zero;
        var pixel = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        Piece? playerPiece = version >= 2 ? ReadPiece(reader) : null;
        string ground = version >= 2 ? Text(reader) : string.Empty;
        int count = reader.ReadInt32();
        var sections = new List<Section>(count);
        for (var at = 0; at < count; at++)
        {
            int shape = reader.ReadInt32();
            string material = Text(reader), source = Text(reader);
            Vector3 least = Vector(reader), most = Vector(reader);
            sections.Add(new Section(shape, material, source, least, most, version >= 2 ? ReadPiece(reader) : PieceOf(source, null, null)));
        }

        int many = reader.ReadInt32();
        var samples = new List<Sample>(many);
        for (var at = 0; at < many; at++)
        {
            int shape = reader.ReadInt32(), drawn = reader.ReadInt32();
            samples.Add(new Sample(shape, drawn, Vector(reader), Vector(reader), Vector(reader), Vector(reader), Vector(reader), reader.ReadSingle()));
        }

        var removed = new List<int>();
        var shades = new List<Shade>();
        if (version >= 2)
        {
            int hidden = reader.ReadInt32();
            for (var at = 0; at < hidden; at++)
            {
                removed.Add(reader.ReadInt32());
            }

            int dark = reader.ReadInt32();
            for (var at = 0; at < dark; at++)
            {
                int drawn = reader.ReadInt32();
                shades.Add(new Shade(drawn, Vector(reader), Vector(reader), Vector(reader)));
            }
        }

        var header = new Header(width, height, factor, origin, player, eye, forward, right, pixel, sections)
        {
            PlayerPiece = playerPiece,
            PlayerGround = ground,
            Removed = removed,
        };
        return (header, samples, shades);
    }

    /// <summary>The offline reader: the numbers beside a capture judged again.</summary>
    public static string Replay(string folder)
    {
        string path = CaptureParts.Find(folder, Numbers);
        if (!File.Exists(path))
        {
            return "roof fade replay: no " + Numbers + " in " + folder;
        }

        (Header header, List<Sample> samples, List<Shade> shades) = Read(File.ReadAllBytes(path));
        return Report(header, samples, shades, "roof fade replay");
    }

    private static void Write(BinaryWriter writer, Piece? piece)
    {
        Text(writer, piece?.File ?? string.Empty);
        writer.Write(piece?.MinX ?? 0);
        writer.Write(piece?.MinY ?? 0);
        writer.Write(piece?.MaxX ?? -1);
        writer.Write(piece?.MaxY ?? -1);
    }

    private static Piece? ReadPiece(BinaryReader reader)
    {
        string file = Text(reader);
        int minX = reader.ReadInt32(), minY = reader.ReadInt32(), maxX = reader.ReadInt32(), maxY = reader.ReadInt32();
        return file.Length == 0 ? null : new Piece(file, minX, minY, maxX, maxY);
    }

    private static void Floats(BinaryWriter writer, params ReadOnlySpan<float> values)
    {
        foreach (float value in values)
        {
            writer.Write(value);
        }
    }

    private static void Text(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int length = Math.Min(bytes.Length, ushort.MaxValue);
        writer.Write((ushort)length);
        writer.Write(bytes, 0, length);
    }

    private static string Text(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));

    private static Vector3 Vector(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
