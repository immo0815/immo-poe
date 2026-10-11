using System.Numerics;
using System.Runtime.Versioning;
using ImGuiNET;
using PoEformance.Features;
using PoEformance.Game.Ui;
using PoEformance.Game.World;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PoEformance.Overlay;

/// <summary>
/// Draws on the game's map the ground a projectile crosses and a body cannot - a chasm, an abyss, the foot of a wall.
/// </summary>
/// <remarks>
/// THE DIFFERENCE BETWEEN THE TERRAIN'S TWO GRIDS, as a translucent sheet under the layout's line and floor: the walkable
/// grid says where a body goes and the layout draws it; GridLandscapeData beside it says where a projectile goes
/// (measured, see the schema), and this draws what the second adds to the first. With both on, the map reads in three:
/// floor, ground only a shot crosses, and - left clear - walls, which stop both.
///
/// BUILT THE WAY THE LAYOUT IS (TerrainLayer): one flat quad per piece of the map, the heights baked into the texture,
/// the picture kept at several sizes so a strip a few cells wide is not skipped into dashes on a zoomed-out map, and the
/// colour a tint, so the colour and the opacity change nothing in the texture. What it bakes is TerrainOutline.Shots -
/// see there for why the ground is drawn at the height of the nearest walkable ground rather than at its own.
///
/// No retreat as the map is walked, unlike the floor: the game draws nothing of this, so there is nothing for it to
/// make way for.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ShotLayer : IDisposable
{
    /// <summary>The key the overlay's texture cache stores this under, with the level appended.</summary>
    private const string TextureKey = "poeformance.shots";

    /// <summary>Largest texture edge of the finest level - the layout's, for the layout's reasons.</summary>
    private const int MaxTextureEdge = 2048;

    /// <summary>How many sizes of the picture are kept, the finest included - the layout's, for the layout's reasons.</summary>
    private const int Levels = 4;

    /// <summary>One uploaded size of the picture, and how many grid cells it spans, padding included - see TerrainLayer.</summary>
    private readonly record struct Level(IntPtr Texture, int Width, int Height, float CoverX, float CoverY);

    private readonly Func<string, Image<Rgba32>, bool, IntPtr> _upload;
    private readonly Action<string> _release;

    private TerrainGrid? _built;
    private Level[] _levels = [];
    private int _step = 1;
    private long _cells;

    // What the last draw measured, for the readout - see TerrainLayer.Describe.
    private float _texelPixels;
    private int _level;
    private bool _onLargeMap;

    // Set once the layer has given up, so a failure is reported once rather than every frame.
    private string? _failure;

    /// <param name="upload">Hands an image to the renderer and returns its handle.</param>
    /// <param name="release">Drops a previously uploaded image.</param>
    public ShotLayer(Func<string, Image<Rgba32>, bool, IntPtr> upload, Action<string> release)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(release);
        _upload = upload;
        _release = release;
    }

    /// <summary>
    /// The sheet's colour with its opacity for alpha, ABGR as ImGui packs it. Zero alpha draws nothing.
    /// </summary>
    /// <remarks>A tint and nothing more - the texture is white - so dragging either control rebuilds nothing.</remarks>
    public uint Fill { get; set; } = OverlaySettings.Default.ShotsPacked;

    /// <summary>
    /// How far from the walkable ground the sheet reaches, in world units; nought for all of it (TerrainOutline.Shots).
    /// </summary>
    /// <remarks>Unlike the colour this is pixels, not a tint: a change rebuilds the sheet, once, on the next frame drawn.</remarks>
    public int Reach
    {
        get => _reach;
        set
        {
            int clamped = Math.Clamp(value, 0, OverlaySettings.MostShotsReach);
            if (clamped != _reach)
            {
                _reach = clamped;
                _built = null;
                _failure = null;
            }
        }
    }

    private int _reach = OverlaySettings.Default.ShotsReach;

    /// <summary>Draws the sheet onto a map, if the terrain has loaded with its projectiles' grid.</summary>
    /// <param name="player">The player's world position - the map projects everything relative to it.</param>
    public void Draw(ImDrawListPtr draw, MapView map, TerrainGrid grid, Vector3 player)
    {
        ArgumentNullException.ThrowIfNull(grid);

        // At zero opacity nothing is built either: the one pass over every cell is spared too.
        if (_failure is not null || (Fill >> 24) == 0)
        {
            return;
        }

        if (!ReferenceEquals(_built, grid))
        {
            // On the render thread, where an escaping exception ends the process - see TerrainLayer.Draw.
            try
            {
                Build(grid);
            }
            catch (Exception exception)
            {
                _failure = exception.Message;
                _built = grid;
                ReleaseTextures();
                Console.Error.WriteLine($"projectile layer disabled: {exception.Message}");
            }
        }

        if (_levels.Length == 0)
        {
            return;
        }

        _texelPixels = map.PixelsPerCellEdge * _step;
        _level = TerrainPicture.LevelFor(_texelPixels, _levels.Length);
        _onLargeMap = map.IsLargeMap;
        Level level = _levels[_level];

        // One pass per uncovered piece of the map, as the layout draws - see TerrainLayer.Draw.
        foreach (ScreenRect piece in map.Uncovered)
        {
            draw.PushClipRect(piece.TopLeft, piece.BottomRight, intersect_with_current_clip_rect: true);
            TerrainLayer.DrawQuad(draw, map, grid, player, level.Texture, level.CoverX, level.CoverY, 0f, Fill);
            draw.PopClipRect();
        }
    }

    /// <summary>Turns the two grids into the sheet's textures, once per area.</summary>
    private void Build(TerrainGrid grid)
    {
        _built = grid;
        ReleaseTextures();
        _cells = 0;

        // No second grid, nothing to draw - and no pass over the cells to find that out.
        if (!grid.HasShotGrid)
        {
            return;
        }

        // World units to cells, rounded up: a reach that rounded to nought would turn the limit off.
        int reachCells = _reach == 0 ? 0 : Math.Max(1, (int)MathF.Ceiling(_reach / MapView.WorldToGrid));
        ShotPlan plan = TerrainOutline.Shots(grid, MaxTextureEdge, isoHeightShift: true, reachCells);
        _cells = plan.Cells;
        _step = plan.Step;
        if (plan.Cells == 0)
        {
            return;
        }

        TerrainPicture picture = TerrainPicture.Sheet(plan.Coverage, plan.Width, plan.Height);

        // CONTIGUOUS, for the reason given in TerrainLayer.Build: the renderer uploads from one pixel span.
        Configuration configuration = Configuration.Default.Clone();
        configuration.PreferContiguousImageBuffers = true;

        var levels = new List<Level>(Levels);
        int cellsPerTexel = plan.Step;
        for (int level = 0; level < Levels; level++)
        {
            using var image = Image.LoadPixelData<Rgba32>(configuration, picture.Pixels, picture.Width, picture.Height);

            // The key in a local, not inline: a test reads every upload call out of the source by regex.
            string key = KeyOf(level);
            IntPtr texture = _upload(key, image, false);
            levels.Add(new Level(
                texture, picture.Width, picture.Height,
                picture.Width * cellsPerTexel, picture.Height * cellsPerTexel));

            if (level + 1 == Levels || (picture.Width <= 1 && picture.Height <= 1))
            {
                break;
            }

            picture = picture.Halved();
            cellsPerTexel *= 2;
        }

        _levels = [.. levels];
    }

    private static string KeyOf(int level) => $"{TextureKey}.{level}";

    /// <summary>Drops every level's texture, whether or not it was uploaded - see TerrainLayer.ReleaseTextures.</summary>
    private void ReleaseTextures()
    {
        for (int level = 0; level < Levels; level++)
        {
            _release(KeyOf(level));
        }

        _levels = [];
    }

    /// <summary>What the sheet holds, or why it holds nothing - for the readouts.</summary>
    /// <remarks>
    /// "No projectiles' grid" and "nothing a projectile crosses that a body cannot" both draw nothing, and only one of them
    /// is the area: the grid's own note says which.
    /// </remarks>
    public string Describe(TerrainGrid? grid)
    {
        if (_failure is not null)
        {
            return $"failed: {_failure}";
        }

        if (grid is null)
        {
            return "loading";
        }

        if (!grid.HasShotGrid)
        {
            return grid.ShotNote.Length > 0 ? $"no projectiles' grid: {grid.ShotNote}" : "no projectiles' grid";
        }

        if (!ReferenceEquals(_built, grid))
        {
            return "not drawn yet";
        }

        if (_levels.Length == 0)
        {
            return _cells == 0 ? "nothing a projectile crosses that a body cannot" : "none";
        }

        Level finest = _levels[0];
        string reach = _reach == 0 ? "no reach limit" : $"within {_reach} of the ground";
        string sizes = $"{_cells:N0} cells, {reach}, {finest.Width}x{finest.Height} in {_levels.Length} levels";
        return _texelPixels > 0f
            ? $"{sizes}, {_texelPixels:F2} px/texel on the {(_onLargeMap ? "large map" : "minimap")} -> level {_level}"
            : sizes;
    }

    public void Dispose()
    {
        ReleaseTextures();
        _built = null;
    }
}
