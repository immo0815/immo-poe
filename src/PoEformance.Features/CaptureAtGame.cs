using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.Ui;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// A book's stage drawn through the game's own camera onto the game's own picture, pixel on pixel - what the light-against-the-game and the
/// shadow-casters parts hold against the game (CaptureLightCompare, CaptureShadowCasters).
/// </summary>
/// <remarks>
/// WHY: the light was compared by hand, crops of two pictures from two cameras, so how much lay in the sun and how bright the sun was could not
/// be told apart - and "the shade is 2.6 times too bright" rested on that. Drawn through the game's camera (GameCamera from the matrix at
/// WorldData + 0x1A0, MeshPicture.Camera.Through) over a stage laid as the area laid it (its space the world less its corner,
/// MonsterModel.AreaOrigin), every pixel of the pane's picture is the same piece of the world as the game's pixel under it, and each one carries
/// what it was made of (PixelGather): its triangle, its place, its light term by term.
///
/// ON THE PROCESSOR, at a whole fraction of the game's size: the canvas is square and at most 2048 a side, so a 3440 x 1440 screen is drawn at
/// half (1720 x 720), each drawn pixel the middle of a two by two block of the game's - and the game's picture is averaged over that block in
/// linear light. The longer side sets the fraction, a screen taller than wide by its height. The processor draws no field waves, refraction,
/// caustics or wetness - water is told apart and left out of the light's comparison.
/// </remarks>
public sealed class CaptureAtGame
{
    /// <summary>Every how many drawn pixels each way one is gathered: a 1720 x 720 drawing gathers 860 x 360.</summary>
    private const int GatherStep = 2;

    private CaptureAtGame(CapturePane pane, GameCamera camera, Vector2 origin, int factor, int drawnWidth, int drawnHeight, int side)
    {
        Pane = pane;
        Camera = camera;
        Origin = origin;
        Factor = factor;
        DrawnWidth = drawnWidth;
        DrawnHeight = drawnHeight;
        Side = side;
    }

    /// <summary>How far the parts that draw through the game's camera have got - for the capture's window.</summary>
    public sealed class Search
    {
        private long _done;
        private long _of;
        private volatile bool _stopped;
        private volatile string _now = string.Empty;

        /// <summary>Steps done.</summary>
        public long Done => Interlocked.Read(ref _done);

        /// <summary>Steps to do.</summary>
        public long Of => Interlocked.Read(ref _of);

        /// <summary>What it is doing.</summary>
        public string Now => _now;

        /// <summary>Whether it was told to stop.</summary>
        public bool Stopped => _stopped;

        /// <summary>Stops it before its next step.</summary>
        public void Stop() => _stopped = true;

        internal void Begin(long of) => Interlocked.Add(ref _of, of);

        internal void Step(string now)
        {
            _now = now;
            Interlocked.Increment(ref _done);
        }
    }

    /// <summary>The pane drawn.</summary>
    public CapturePane Pane { get; }

    /// <summary>The game's camera at the press, taken apart.</summary>
    public GameCamera Camera { get; }

    /// <summary>The stage's corner in the area: its space is the world less this across.</summary>
    public Vector2 Origin { get; }

    /// <summary>How many of the game's pixels each way one drawn pixel stands for.</summary>
    public int Factor { get; }

    /// <summary>The drawn picture's width, pixels.</summary>
    public int DrawnWidth { get; }

    /// <summary>Its height.</summary>
    public int DrawnHeight { get; }

    /// <summary>The square canvas's side it was drawn on.</summary>
    public int Side { get; }

    /// <summary>The pane's picture through the game's camera, RGBA, <see cref="DrawnWidth"/> by <see cref="DrawnHeight"/>; alpha nought where it drew nothing.</summary>
    public byte[] Picture { get; private set; } = [];

    /// <summary>The drawing's depth per drawn pixel, float.MaxValue where nothing.</summary>
    public float[] Depth { get; private set; } = [];

    /// <summary>The translucent shape blended into each drawn pixel last, or -1.</summary>
    public int[] Layer { get; private set; } = [];

    /// <summary>What every gathered pixel was made of - every <see cref="GatherStep"/>-th drawn pixel each way.</summary>
    public PixelGather Gather { get; private set; } = new(1);

    /// <summary>Each triangle's shape in the model's mesh.</summary>
    public int[] ShapeOf { get; private set; } = [];

    /// <summary>The game's picture averaged over each drawn pixel's block, linear red green blue; empty where there was no picture.</summary>
    public float[] Game { get; private set; } = [];

    /// <summary>The drawn pixels left out of every comparison: the game's interface, the player, monsters and effects.</summary>
    public bool[] Masked { get; private set; } = [];

    /// <summary>The player across in the world at the press, NaN where there was none.</summary>
    public Vector2 PlayerAcross { get; private set; } = new(float.NaN);

    /// <summary>The lines saying what was drawn, from where and what was left out.</summary>
    public IReadOnlyList<string> Said { get; private set; } = [];

    /// <summary>
    /// The pane a part holds against the game: a stage laid as this area instance laid it, with the light panel's light - or null, with why.
    /// </summary>
    public static CapturePane? Chosen(IReadOnlyList<CapturePane>? panes, WorldSnapshot shot, out string why)
    {
        ArgumentNullException.ThrowIfNull(shot);
        int? stamp = shot.Terrain is { } grid ? RoomKey.Stamp(grid) : null;
        CapturePane? best = null;
        var reasons = new List<string>();
        foreach (CapturePane pane in panes ?? [])
        {
            MonsterModel model = pane.Model;
            if (model.AreaOrigin is null)
            {
                reasons.Add($"the {pane.Book} book's stage is not laid in the area (a room as its file has it, or a built one)");
                continue;
            }

            if (stamp is null || model.AreaStamp != stamp)
            {
                reasons.Add($"the {pane.Book} book's stage was laid in another area instance");
                continue;
            }

            if (pane.Light is null)
            {
                reasons.Add($"the {pane.Book} book has no light panel to light it");
                continue;
            }

            // THE ONE DRAWN UNDER THE GAME'S LIGHT FIRST, where both are laid.
            if (best is null || (pane.LightOn && !best.LightOn))
            {
                best = pane;
            }
        }

        why = best is not null ? string.Empty
            : reasons.Count > 0 ? string.Join("; ", reasons)
            : "no book shows anything - open the Tile or Monster Book on \"the area round you\" or a room laid as the area laid it";
        return best;
    }

    /// <summary>
    /// The pane's stage drawn through the game's camera on a screen this size, the game's picture beside it - or null, with why.
    /// </summary>
    /// <param name="pane">The pane, from <see cref="Chosen"/>.</param>
    /// <param name="shot">The world as read when the game's picture was taken - its camera, player, entities and interface.</param>
    /// <param name="bgr">The game's picture, blue green red, top row first - or null where none was taken.</param>
    /// <param name="width">The screen's width, pixels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="search">Where it says how far it is, or null.</param>
    /// <param name="why">Why nothing was drawn, or empty.</param>
    public static CaptureAtGame? Draw(CapturePane pane, WorldSnapshot shot, byte[]? bgr, int width, int height, Search? search, out string why)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(shot);
        why = string.Empty;
        if (pane.Model.AreaOrigin is not { } origin || pane.Light is not { } light)
        {
            why = "the pane's stage is not laid in the area, or it has no light";
            return null;
        }

        if (GameCamera.Of(shot.Matrix, width, height) is not { } camera)
        {
            why = "the game's camera matrix does not take apart - no camera read";
            return null;
        }

        // THE LONGER SIDE SETS THE FRACTION: a screen taller than wide (1440 x 2560) drawn whole by its width alone would want more rows
        // than the canvas holds.
        int factor = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / (double)MeshPicture.Widest));
        int drawnWidth = (width + factor - 1) / factor, drawnHeight = (height + factor - 1) / factor;
        int side = Math.Max(drawnWidth, drawnHeight);
        var scene = new CaptureAtGame(pane, camera, origin, factor, drawnWidth, drawnHeight, side);
        search?.Begin(2);

        MonsterModel model = pane.Model;
        SkinnedMesh mesh = model.Mesh;
        Vector3[] positions = pane.Positions is { } posed && posed.Length == mesh.Positions.Length ? posed : mesh.Positions;
        Vector3[] normals = pane.Normals is { } turned && turned.Length == mesh.Normals.Length && ReferenceEquals(positions, pane.Positions) ? turned : mesh.Normals;
        var gather = new PixelGather(side, GatherStep);
        var canvas = new MeshPicture.Canvas(side)
        {
            Lens = MeshPicture.Camera.Through(camera, origin, side, default, 1f / factor),
            Rows = drawnHeight,
            Light = light,
            Time = pane.Clock,
            Dust = pane.Dust,
            Water = pane.Water,
            Gather = gather,
        };

        search?.Step("drawing the stage through the game's camera");
        long began = Environment.TickCount64;
        GamePicture picture = MeshPicture.Of(
            mesh, canvas, positions, normals, ink: pane.Ink, skin: model.Skin, skins: model.Skins, blends: pane.Blends, shades: pane.Shades);
        long drawnIn = Environment.TickCount64 - began;

        scene.Picture = new byte[drawnWidth * drawnHeight * 4];
        scene.Depth = new float[drawnWidth * drawnHeight];
        scene.Layer = new int[drawnWidth * drawnHeight];
        ReadOnlySpan<float> depths = canvas.Depths;
        ReadOnlySpan<int> layers = canvas.Layers;
        for (var y = 0; y < drawnHeight; y++)
        {
            Array.Copy(picture.Rgba, y * side * 4, scene.Picture, y * drawnWidth * 4, drawnWidth * 4);
            depths.Slice(y * side, drawnWidth).CopyTo(scene.Depth.AsSpan(y * drawnWidth, drawnWidth));
            if (layers.Length >= (y + 1) * side)
            {
                layers.Slice(y * side, drawnWidth).CopyTo(scene.Layer.AsSpan(y * drawnWidth, drawnWidth));
            }
            else
            {
                scene.Layer.AsSpan(y * drawnWidth, drawnWidth).Fill(-1);
            }
        }

        scene.Gather = gather;
        scene.ShapeOf = ShapesOf(mesh);
        search?.Step("averaging the game's picture");
        scene.Game = bgr is { } bytes && bytes.Length >= width * height * 3 ? Averaged(bytes, width, height, factor, drawnWidth, drawnHeight) : [];
        (scene.Masked, string masked) = Masks(shot, camera, width, height, factor, drawnWidth, drawnHeight);

        Vector3 player = shot.Player is { } one ? new Vector3(one.WorldX, one.WorldY, one.WorldZ) : new Vector3(float.NaN);
        scene.PlayerAcross = new Vector2(player.X, player.Y);
        Vector2 playerPixel = float.IsNaN(player.X) ? new Vector2(float.NaN) : camera.Pixel(player);
        ScreenPoint projected = float.IsNaN(player.X) ? default : WorldToScreen.Project(shot.Matrix, player.X, player.Y, player.Z, width, height);
        var said = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"the {pane.Book} book's stage ({model.Mesh_}), its corner at ({origin.X:0.#}, {origin.Y:0.#}) in the area, drawn on the processor through the game's camera at 1/{factor} of {width} x {height}: {drawnWidth} x {drawnHeight} on a canvas {side} square, in {drawnIn} ms"),
            "camera: " + camera.Said(),
            string.Create(CultureInfo.InvariantCulture, $"camera: focal lengths {camera.FocalX:0.###} and {camera.FocalY:0.###} px, the lens takes their mean; a point is off by their difference times its distance from the principal point"),
            float.IsNaN(player.X)
                ? "camera: no player to hold the two projections against"
                : string.Create(CultureInfo.InvariantCulture, $"camera: the player at ({player.X:0.#}, {player.Y:0.#}, {player.Z:0.#}) lands at ({playerPixel.X:0.##}, {playerPixel.Y:0.##}) taken apart, ({projected.X:0.##}, {projected.Y:0.##}) by WorldToScreen"),
            pane.LightOn ? "light: the light panel's, the pane drawn under it" : "light: the light panel's as it would be with \"game light\" ticked - the pane itself draws under its own lamp",
            masked,
            "drawn on the processor: no field waves, refraction, caustics or wetness - water is told apart and left out of the light's comparison",
        };
        said.AddRange(pane.LightSaid.Select(line => "light panel: " + line));
        scene.Said = said;
        return scene;
    }

    /// <summary>Each triangle's shape in a mesh, -1 for one in none.</summary>
    internal static int[] ShapesOf(SkinnedMesh mesh)
    {
        var shapeOf = new int[mesh.Indices.Length / 3];
        Array.Fill(shapeOf, -1);
        for (var shape = 0; shape < mesh.Shapes.Count; shape++)
        {
            MeshShape part = mesh.Shapes[shape];
            int from = Math.Clamp(part.From / 3, 0, shapeOf.Length);
            int upto = Math.Clamp((part.From + part.Count) / 3, from, shapeOf.Length);
            Array.Fill(shapeOf, shape, from, upto - from);
        }

        return shapeOf;
    }

    /// <summary>The drawn pixel a gathered entry stands on.</summary>
    internal int DrawnAt(int gathered)
    {
        int x = gathered % Gather.Width * Gather.Step, y = gathered / Gather.Width * Gather.Step;
        return x < DrawnWidth && y < DrawnHeight ? (y * DrawnWidth) + x : -1;
    }

    /// <summary>A linear colour the game's picture holds at a drawn pixel, or NaN where there is none.</summary>
    internal Vector3 GameAt(int drawn)
        => Game.Length >= (drawn + 1) * 3 ? new Vector3(Game[drawn * 3], Game[(drawn * 3) + 1], Game[(drawn * 3) + 2]) : new Vector3(float.NaN);

    /// <summary>
    /// The game's picture averaged over each drawn pixel's block, in linear light - its bytes decoded by the piecewise sRGB curve (CHOSEN: the
    /// engine's ApplyOETF is unread; the tool encodes its own pictures so).
    /// </summary>
    private static float[] Averaged(byte[] bgr, int width, int height, int factor, int drawnWidth, int drawnHeight)
    {
        float[] linear = Linear;
        var game = new float[drawnWidth * drawnHeight * 3];
        Parallel.For(0, drawnHeight, y =>
        {
            for (var x = 0; x < drawnWidth; x++)
            {
                float r = 0f, g = 0f, b = 0f;
                var count = 0;
                for (int dy = 0; dy < factor && (y * factor) + dy < height; dy++)
                {
                    int row = ((y * factor) + dy) * width;
                    for (int dx = 0; dx < factor && (x * factor) + dx < width; dx++)
                    {
                        int at = (row + (x * factor) + dx) * 3;
                        b += linear[bgr[at]];
                        g += linear[bgr[at + 1]];
                        r += linear[bgr[at + 2]];
                        count++;
                    }
                }

                int to = ((y * drawnWidth) + x) * 3;
                game[to] = r / count;
                game[to + 1] = g / count;
                game[to + 2] = b / count;
            }
        });

        return game;
    }

    /// <summary>
    /// The drawn pixels the game's picture shows something else at: its interface (the HUD's parts as the game names them) and everything that
    /// moves - the player, monsters, NPCs, effects, items and projectiles, each a disc round where it stands as tall as its bounds.
    /// </summary>
    private static (bool[] Masked, string Said) Masks(WorldSnapshot shot, GameCamera camera, int width, int height, int factor, int drawnWidth, int drawnHeight)
    {
        var masked = new bool[drawnWidth * drawnHeight];
        var hud = 0;
        foreach (InterfacePart part in shot.InterfaceParts)
        {
            ScreenRect where = part.Where;
            if (!(where.Right > where.Left) || !(where.Bottom > where.Top))
            {
                continue;
            }

            hud++;
            int left = Math.Clamp((int)(where.Left / factor), 0, drawnWidth), right = Math.Clamp((int)MathF.Ceiling(where.Right / factor), 0, drawnWidth);
            int top = Math.Clamp((int)(where.Top / factor), 0, drawnHeight), bottom = Math.Clamp((int)MathF.Ceiling(where.Bottom / factor), 0, drawnHeight);
            for (int y = top; y < bottom; y++)
            {
                masked.AsSpan((y * drawnWidth) + left, right - left).Fill(true);
            }
        }

        var moving = 0;
        IEnumerable<WorldEntity> entities = shot.Player is { } player ? shot.Listed.Append(player) : shot.Listed;
        foreach (WorldEntity entity in entities)
        {
            if (entity.Kind is not (EntityKind.Player or EntityKind.Monster or EntityKind.Npc or EntityKind.Effect or EntityKind.WorldItem or EntityKind.Projectile))
            {
                continue;
            }

            var foot = new Vector3(entity.WorldX, entity.WorldY, entity.TerrainHeight != 0f ? entity.TerrainHeight : entity.WorldZ);
            float tall = MathF.Max(150f, MathF.Abs(entity.ModelBoundsZ) * 2f);
            Vector2 low = camera.Pixel(foot), high = camera.Pixel(foot - new Vector3(0f, 0f, tall));
            if (!float.IsFinite(low.X) || !float.IsFinite(high.X))
            {
                continue;
            }

            Vector2 middle = (low + high) * 0.5f / factor;
            float radius = MathF.Max(24f, Vector2.Distance(low, high) * 0.6f) / factor;
            if (middle.X < -radius || middle.Y < -radius || middle.X > drawnWidth + radius || middle.Y > drawnHeight + radius)
            {
                continue;
            }

            moving++;
            int top = Math.Max(0, (int)(middle.Y - radius)), bottom = Math.Min(drawnHeight - 1, (int)(middle.Y + radius));
            for (int y = top; y <= bottom; y++)
            {
                float dy = y + 0.5f - middle.Y;
                float across = MathF.Sqrt(MathF.Max(0f, (radius * radius) - (dy * dy)));
                int left = Math.Max(0, (int)(middle.X - across)), right = Math.Min(drawnWidth - 1, (int)(middle.X + across));
                for (int x = left; x <= right; x++)
                {
                    masked[(y * drawnWidth) + x] = true;
                }
            }
        }

        int count = masked.Count(one => one);
        return (masked, string.Create(
            CultureInfo.InvariantCulture,
            $"left out: {count:N0} of {masked.Length:N0} drawn pixels - {hud} parts of the game's interface (as read, taken as the game's own client pixels) and {moving} things that move (a disc as tall as each one's bounds, at least 150 units)"));
    }

    /// <summary>Linear light from an sRGB byte, by the piecewise curve.</summary>
    internal static float[] Linear { get; } = BuildLinear();

    private static float[] BuildLinear()
    {
        var table = new float[256];
        for (var at = 0; at < 256; at++)
        {
            float value = at / 255f;
            table[at] = value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        }

        return table;
    }

    /// <summary>An sRGB byte from linear light, by the piecewise curve.</summary>
    internal static byte Encoded(float linear)
    {
        float value = Math.Clamp(linear, 0f, 1f);
        float encoded = value <= 0.0031308f ? value * 12.92f : (1.055f * MathF.Pow(value, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)((encoded * 255f) + 0.5f), 0, 255);
    }

    /// <summary>Rec. 709's luminance of a linear colour.</summary>
    internal static float Luminance(Vector3 colour) => (0.2126f * colour.X) + (0.7152f * colour.Y) + (0.0722f * colour.Z);

    /// <summary>
    /// How the pane's picture lines up with the game's: the correlation of their edges - each picture's luminance gradient - at every shift up to
    /// a few drawn pixels, the best and the one at nought. One shift best everywhere says the camera is off; nought best says it is the game's.
    /// </summary>
    internal string LinedUp()
    {
        if (Game.Length == 0)
        {
            return "lined up: no game picture to hold it against";
        }

        int w = DrawnWidth, h = DrawnHeight;
        var pane = new float[w * h];
        var game = new float[w * h];
        var drawn = new bool[w * h];
        for (var at = 0; at < w * h; at++)
        {
            drawn[at] = Picture[(at * 4) + 3] != 0 && !Masked[at];
            pane[at] = Luminance(new Vector3(Linear[Picture[at * 4]], Linear[Picture[(at * 4) + 1]], Linear[Picture[(at * 4) + 2]]));
            game[at] = Luminance(GameAt(at));
        }

        float[] paneEdges = Edges(pane, w, h), gameEdges = Edges(game, w, h);
        const int reach = 6;
        var best = (X: 0, Y: 0, R: double.NegativeInfinity);
        double atNought = double.NaN;
        var line = new StringBuilder();
        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                double r = Correlation(paneEdges, gameEdges, drawn, w, h, dx, dy);
                if (dx == 0 && dy == 0)
                {
                    atNought = r;
                }

                if (r > best.R)
                {
                    best = (dx, dy, r);
                }
            }
        }

        // NO VERDICT WITHOUT A MEASUREMENT: a stage hardly showing through the game's camera (laid elsewhere in the instance, or under the
        // interface) leaves no shift with pixels enough, and nought would read as agreement.
        if (!double.IsFinite(best.R))
        {
            int shown = drawn.Count(one => one);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"lined up: not measured - {shown:N0} drawn pixels outside the left-out ones, too few or too even to line up (the stage hardly shows through the game's camera)");
        }

        line.Append(CultureInfo.InvariantCulture, $"lined up: the edges' correlation at no shift {atNought:0.###}, best {best.R:0.###} at ({best.X}, {best.Y}) drawn pixels ({best.X * Factor}, {best.Y * Factor} of the game's)");
        line.Append(best.X == 0 && best.Y == 0 ? " - the game's camera and the stage's placing agree" : Math.Abs(best.X) <= 1 && Math.Abs(best.Y) <= 1 ? " - within a drawn pixel" : " - OFF: the statistics above are of pixels a shift apart");
        return line.ToString();
    }

    private static float[] Edges(float[] value, int w, int h)
    {
        var edges = new float[w * h];
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                int at = (y * w) + x;
                float gx = value[at + 1] - value[at - 1], gy = value[at + w] - value[at - w];
                edges[at] = MathF.Sqrt((gx * gx) + (gy * gy)) / (0.02f + value[at]);
            }
        }

        return edges;
    }

    private static double Correlation(float[] pane, float[] game, bool[] drawn, int w, int h, int dx, int dy)
    {
        double sp = 0, sg = 0, spp = 0, sgg = 0, spg = 0;
        long n = 0;
        for (var y = 8; y < h - 8; y += 2)
        {
            for (var x = 8; x < w - 8; x += 2)
            {
                int at = (y * w) + x, from = ((y + dy) * w) + x + dx;
                if (!drawn[at] || !drawn[from])
                {
                    continue;
                }

                double p = pane[at], g = game[from];
                if (!double.IsFinite(g))
                {
                    continue;
                }

                sp += p;
                sg += g;
                spp += p * p;
                sgg += g * g;
                spg += p * g;
                n++;
            }
        }

        if (n < 100)
        {
            return double.NaN;
        }

        double cov = (spg / n) - (sp / n * (sg / n));
        double vp = (spp / n) - (sp / n * (sp / n)), vg = (sgg / n) - (sg / n * (sg / n));
        return vp > 0 && vg > 0 ? cov / Math.Sqrt(vp * vg) : double.NaN;
    }
}
