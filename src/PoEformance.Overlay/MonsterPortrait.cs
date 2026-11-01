using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using ImGuiNET;
using PoEformance.Features;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;
using PoEformance.Game.World;
using PoEformance.Gpu;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PoEformance.Overlay;

/// <summary>
/// A monster's model, drawn in a pane, turned with the mouse and played through its animations.
/// </summary>
/// <remarks>
/// THREE THREADS' WORTH OF RULES IN ONE SMALL CLASS, which is why it is its own class rather than
/// another two hundred lines of the book's window:
///
///   - WALKING THE CHAIN IS SLOW. Five files out of the bundles and a texture to decode measured
///     around a tenth of a second, which is six frames. It happens on a task.
///   - UPLOADING A TEXTURE IS NOT. It is a call into the renderer and belongs on the thread that
///     draws, so the task hands over PIXELS and the draw picks them up.
///   - AND A HANDLE HAS TO BE GIVEN BACK. Every monster somebody clicks on would otherwise leave
///     a texture behind - the book has 2792 rows, and browsing is what it is for.
///
/// NOTHING HERE THROWS ON THE DRAW THREAD. An exception escaping a frame ends the session, and
/// every input to this is a file out of a game's bundles - see <see cref="MonsterModels"/>, which
/// answers with a reason rather than an exception for the same reason.
///
/// THE PICTURE IS REDRAWN WHEN SOMETHING CHANGES, not per frame: a new monster, a turn, a tilt or
/// a different size. Holding still costs one textured quad. PLAYING, ORBITING AND DRAGGING ARE
/// THE EXCEPTIONS, and they are paid for alike: at the rung the pane asks for, up to the cap in
/// <see cref="PictureLadder"/>, drawn in bands on every core. It used to be one rung under the
/// cap as well, and on a 1200 px pane that was a monster drawn at 512 and stretched - the blur
/// the live client reported once the floor's own staircase was gone. Stop it and the next frame
/// is drawn at whatever size the pane is, cap or no cap. THE CARD IS NEVER CAPPED - see
/// <see cref="Wanted"/>: the cap prices the processor's frame, and the card's costs nothing it
/// would notice at the top rung. How many pictures a second that comes to is shown in the pane's
/// own row, or in the picture's corner where there is no row (<see cref="RedrawRate"/>), because
/// it is the number that says whether the cap is the right one for this machine.
///
/// THE KEYFRAMES ARE UNPACKED ON A TASK, ONE ANIMATION AT A TIME. A bundled rig holds twelve
/// megabytes of them; the animation being played is a few tens of kilobytes of that, and Oodle
/// on the draw thread is a call this class has no measurement for and does not make.
///
/// THE FLOOR IS DRAWN BY THE OVERLAY, NOT INTO THE PICTURE. It used to be, and a picture drawn
/// one rung down and stretched over the pane stretched the floor's one-pixel lines into steps -
/// "so unglaublich pixelig", from the live client. <see cref="ModelFloor"/> works the lines out
/// from the very camera the picture was drawn with, in shares of the side, and <see cref="Strokes"/>
/// writes them into the draw list at the screen's own resolution, anti-aliased, on every frame.
/// Under the picture while the eye is above the floor, and over it while the eye is below - the
/// model is all on one side of the plane, so that order is right at every pixel but a weapon
/// hanging under the feet.
///
/// NOTHING IS SAID IN A TOOLTIP. There was one, with the gestures and the reasons in it, and it
/// was reported as being in the way: it comes up exactly where the pointer is, which while the
/// camera is being placed is over the model. Everything it said is in short lines UNDER the
/// picture now, which are read without pointing at anything and never cover the model; the two
/// widgets that sit over the picture - the orbit button and the animation's progress - are at
/// its top edge, where a model's head seldom reaches, and the button is drawn AFTER the picture
/// so that ImGui's overlap rule gives it the pointer.
/// </remarks>
public sealed partial class MonsterPortrait
{
    /// <summary>How far the mouse drags to turn the model once around.</summary>
    public const float Sweep = 360f;

    /// <summary>How much one notch of the wheel changes the zoom.</summary>
    /// <remarks>
    /// MULTIPLIED AND NOT ADDED, because zoom is a ratio: a fixed step crawls when pulled back and
    /// leaps when pushed in. Notches compound, so the whole range is about eight of them.
    /// </remarks>
    public const float Notch = 1.18f;

    /// <summary>How long the orbit takes to carry the camera once around the model, in seconds.</summary>
    /// <remarks>
    /// ASKED FOR AS "constant and rather slow", with no number: this is the number, and it is
    /// what a turntable in a product shot does. Constant in time rather than per frame, so it is
    /// the same lap on a 60 Hz and a 144 Hz monitor, and clamped per frame like the animation
    /// is so that a stalled frame does not spin the model round.
    /// </remarks>
    public const float Orbit = 30f;

    /// <summary>The animation chosen when a monster arrives, where its rig has one by that name.</summary>
    /// <remarks>On 1328 of the install's 1628 rigs, which is more than any other name by far.</remarks>
    public const string Idle = "idle_01";

    /// <summary>What an animation plays at when its header says nothing usable.</summary>
    private const float UsualRate = 30f;

    /// <summary>The id ImGui knows the picture by, which is what makes it something to grab.</summary>
    private const string Grip = "##monster-model";

    /// <summary>The buttons down the picture's right edge, top to bottom, as indices into the arrays below.</summary>
    /// <remarks>
    /// THE CARD'S, THE LIGHT'S, THE ORBIT'S, THE PROBE'S AND THE CLOCK'S, in that order because that
    /// is the order asked for. The orbit button was there first and alone; the others were words -
    /// three beside the rate in the opposite corner, Pause at the end of the animation row - and
    /// three words in a row over a picture read as a caption rather than as controls. As art they
    /// are the orbit's size and hang in one column with it. The card's and the light's are skipped
    /// where they have nothing to switch - no card, no specular colour - and the clock's where
    /// nothing plays; the rest close up. THE CAMERA'S THREE come after (v0.1.324, asked for from the live client with the free camera):
    /// the view fitted again, the game's own angle and distance about the focus, and perspective or parallel. THE FOCUS MARKER'S last
    /// (v0.1.380, asked for from the live client): the camera's focus stands for the player wherever the engine reads the player's place
    /// - the fog's disperse radius, the clouds' fade - and the marker shows where that is.
    /// </remarks>
    private const int CardSide = 0;
    private const int LightSide = 1;
    private const int OrbitSide = 2;
    private const int ProbeSide = 3;
    private const int ClockSide = 4;
    private const int ResetSide = 5;
    private const int GameSide = 6;
    private const int PerspectiveSide = 7;
    private const int FocusSide = 8;
    private const int Sides = 9;

    /// <summary>The ids ImGui knows the edge buttons by.</summary>
    private static readonly string[] SideIds =
    [
        "##monster-card", "##monster-light", "##monster-orbit", "##monster-probe", "##monster-clock", "##monster-reset", "##monster-game",
        "##monster-perspective", "##monster-focus",
    ];

    /// <summary>The art on the edge buttons, by the end of each manifest resource name - the project file pins them.</summary>
    private static readonly string[] SideArt =
    [
        "3dMV_cpugpu.png", "3dMV_light.png", "3dMV_orbit.png", "3dMV_probe.png", "3dMV_clock.png", "3dMV_reset.png", "3dMV_game.png",
        "3dMV_perspective.png", "3dMV_focus.png",
    ];

    /// <summary>How many panes have been made, so each one's keys are its own.</summary>
    private static int s_panes;

    /// <summary>The progress bar's width as a share of the picture's side.</summary>
    private const float ProgressShare = 0.4f;

    /// <summary>The most the progress bar is allowed to be, in font sizes: a wide pane does not need a wide bar.</summary>
    private const float ProgressSpan = 14f;

    /// <summary>How big the edge buttons' art is, in frame heights.</summary>
    /// <remarks>
    /// THREE, and it got there in two steps from the live client. At one it could not be made out
    /// at all - a drawing of a cube inside a broken circle has detail that nineteen pixels cannot
    /// hold, and over a dark backdrop what is left reads as a smudge rather than as a control. At
    /// two it was legible and still asked to grow by half again, which is this. In frame heights
    /// rather than pixels, so it follows the text size the way every other control does. The three
    /// that joined it are drawn from the same sheet at the same size.
    /// </remarks>
    private const float OrbitFaces = 3f;

    /// <summary>The line under the picture that says what the mouse does.</summary>
    private const string Gestures = "drag turns · wheel zooms at the pointer · double-click resets";

    /// <summary>The same where the picture has a sun to move.</summary>
    private const string SunGestures = "drag turns · shift + drag moves the sun · wheel zooms at the pointer · double-click resets";

    /// <summary>The sun's mark on the picture: its colour, and its disc's radius in font sizes.</summary>
    private static readonly Vector4 SunInk = new(1f, 0.82f, 0.3f, 1f);

    private const float SunDisc = 0.4f;

    /// <summary>The same line while the orbit runs, when a drag does nothing.</summary>
    private const string Orbiting = "orbiting · the orbit button at the right edge stops it · no dragging until then";

    /// <summary>The line that says what the floor's squares are.</summary>
    private const string FloorSaid = "floor: 250 units to a tile, ten squares each · x red · y green";

    /// <summary>The line under a picture showing a view other than the lit picture - what it shows, and what it leaves out.</summary>
    private static string ViewSaid(PictureView view) => "view: " + SceneLight.ViewNames[(int)view] + view switch
    {
        PictureView.LightingOnly => " - the game light on a grey of 0.25 in place of every albedo (the engine's debug_lighting_only)",
        _ => " - the material's channel as the engine's debug view writes it, neither exposed nor graded; the water's layer left out (CHOSEN),"
            + " the normal the one the picture is lit with, turned to the eye",
    };

    /// <summary>What the greying slider is, said where somebody asks it.</summary>
    /// <remarks>
    /// The number and its evidence, because it is the one control here whose right value is not
    /// obvious and the game itself is not consistent about it. See <see cref="PictureGrey"/>.
    /// </remarks>
    private const string GreySaid =
        "how much of the colour's mean the grey keeps.\n"
        + "the game's own Inactive icons run 0.47 to 1.14 of it;\n"
        + "0.78 is the median over its 27 boss pairs.";

    /// <summary>What the outline is, said where somebody asks it.</summary>
    /// <remarks>The measurement, because the number is otherwise somebody's taste. See PictureOutline.</remarks>
    private const string RimSaid =
        "the black rim the game's own icons have.\n"
        + "measured over its 27 boss icons: the outermost\n"
        + "pixel is black, the second half way out of it,\n"
        + "and the art starts at the third. zero draws none.";

    /// <summary>What the brightness row is, said where somebody asks it.</summary>
    private const string LightSaid =
        "brings the model to the brightness the game paints its icons at.\n"
        + "measured on the interior of its 27 boss icons: median 55,\n"
        + "against 24 on the first model exported here. a curve, so\n"
        + "black stays black and a highlight is not clipped away.\n"
        + "the number beside it is what came out.";

    /// <summary>What the export button does, said where somebody asks it.</summary>
    private const string SaveSaid =
        "writes this pose as icon art: colour and greyed,\n"
        + "cut out on transparency, at 64 px and at 1024.";

    /// <summary>What the three fields under the previews are for.</summary>
    /// <remarks>
    /// The point of the whole row, in four lines: a picture on disk points at nothing, and
    /// these are the three facts that make it a marker. See <see cref="BossIcons.Remember"/>.
    /// </remarks>
    private const string EntrySaid =
        "what this picture is OF, written to data/boss-icons.json\n"
        + "by the same click that writes the files.\n"
        + "area: the map id, several separated by commas -\n"
        + "one boss is often the boss of two maps.\n"
        + "tile: the arena's own path, where it is known.\n"
        + "name: what the game calls it. it becomes the marker's label.";

    /// <summary>How many texture file names the cost line prints before it stops.</summary>
    /// <remarks>Two is what a monster of parts usually has; more than three is a line nobody reads.</remarks>
    private const int Named = 3;

    /// <summary>Longest each of the three fields may be. A path is the long one.</summary>
    private const uint AreasLength = 512;

    /// <inheritdoc cref="AreasLength"/>
    private const uint TileLength = 256;

    /// <inheritdoc cref="AreasLength"/>
    private const uint NameLength = 128;

    /// <summary>The side the export is drawn at, before the small pair is made from it.</summary>
    /// <remarks>
    /// FIXED, AND NOT THE PANE'S SIZE, so that what comes out does not depend on how big the
    /// window happened to be - two icons made a week apart have to match. Sixteen times a sheet
    /// cell each way, which is enough to downscale from without the filter inventing anything.
    /// </remarks>
    public const int Shot = 1024;

    /// <summary>What the pane is painted before anything else: Blender's viewport grey, 0x3D3D3D.</summary>
    /// <remarks>
    /// THE COLOURS ARE BLENDER'S DEFAULT THEME, read out of its userdef_default_theme.c rather than
    /// eyeballed off a screenshot, because Blender's floor is what the live client asked for by
    /// name. Its 3D viewport background is 0x3D3D3D; its grid is 0x545454 at half alpha and its
    /// emphasised grid the same at full, which <see cref="ModelFloor.Faint"/> carries; and an
    /// axis is the grid colour blended half way to the axis colour - 0xFF3352 for x, 0x8BDC00 for
    /// y - and shaded down by ten, which is its make_axis_color in resources.cc. Packed the way
    /// ImGui packs a colour: alpha in the top byte, red in the bottom one.
    /// </remarks>
    private const uint Backdrop = 0xFF3D3D3D;

    /// <summary>The two greys of the transparency checkerboard, and how big one square is.</summary>
    /// <remarks>
    /// Mid greys rather than the white-and-light-grey a paint program uses: this sits in a dark
    /// interface next to a dark model, and a white backdrop here is a lamp in the corner of the
    /// eye. Both are far enough from Blender's 0x3D3D3D that nobody can mistake which backdrop
    /// they are looking at.
    /// </remarks>
    private const uint CheckerLight = 0xFF7A7A7A;

    /// <inheritdoc cref="CheckerLight"/>
    private const uint CheckerDark = 0xFF5A5A5A;

    /// <inheritdoc cref="CheckerLight"/>
    private const float Checker = 16f;

    /// <summary>A ceiling on the squares drawn, so a huge pane cannot turn into a wall of rectangles.</summary>
    private const int CheckerMost = 4096;

    /// <summary>
    /// How much bigger the second row of previews is drawn than a sheet cell.
    /// </summary>
    /// <remarks>
    /// THREE, WHICH IS TWO DECISIONS. The small pair is what the icon will really look like and
    /// is the one being judged; this one is for seeing WHY it looks like that - which shape lost
    /// its edge, where the weapon went. Bigger than four would make the window wider than the
    /// model pane it sits beside, and a preview that has to be moved out of the way to see the
    /// model is no longer a comparison.
    /// </remarks>
    private const int Closer = 3;

    /// <summary>The hairline round an exported cell, so its edge can be told from the checkerboard.</summary>
    /// <remarks>
    /// A CELL IS JUDGED BY ITS EDGES as much as by what is in it - whether the model is cut off,
    /// whether it is lost in the middle of too much space - and without a frame the picture and
    /// the board it stands on run together at exactly the places that matter.
    /// </remarks>
    private const uint Edge = 0xFF9A9A9A;

    /// <inheritdoc cref="Backdrop"/>
    private const uint Grid = 0x00545454;

    /// <inheritdoc cref="Backdrop"/>
    private const uint AxisX = 0x0049399F;

    /// <inheritdoc cref="Backdrop"/>
    private const uint AxisY = 0x00208E65;

    /// <summary>
    /// How the picture is handed to the renderer, which is CONTIGUOUS and that is not a preference.
    /// </summary>
    /// <remarks>
    /// THE RENDERER UPLOADS A TEXTURE BY TAKING THE IMAGE'S SINGLE PIXEL SPAN, and ImageSharp's
    /// default allocator splits anything over its four-megabyte pool block across several buffers,
    /// for which that span does not exist. Measured on every rung of the ladder: up to 1024 px,
    /// which is exactly four megabytes, the picture arrives whole; at 1536 and 2048 it arrives
    /// split; with this configuration all seven arrive whole. <see cref="IconCache"/> and
    /// <see cref="TerrainLayer"/> learned the same thing the same way.
    ///
    /// HOW IT SHOWED UP HERE IS WORTH WRITING DOWN, because it hid for half an hour at a time. An
    /// animation was drawn one rung down, which under the cap of the day was 512 px and one
    /// megabyte, so playing never split. Pausing went back up to the rung that covers the pane,
    /// and on a pane wider than 1024 px that is a split image: the upload threw, the picture was
    /// dropped, and with it the controls that could have started the animation again - the viewer
    /// was gone until the app was restarted. The guard test that pins this rule had filtered its
    /// files on the spelling "_upload(", and this class writes "_upload!(". Playing draws at the
    /// cap now, and a cap raised past 1024 splits while playing too; the configuration covers it.
    ///
    /// A configuration of its own rather than the global default, and without TIFF, for the
    /// reasons IconCache gives.
    /// </remarks>
    private static readonly Configuration Contiguous = Contiguously();

    private readonly PictureLadder _sizes;
    private readonly Func<string, byte[]?>? _install;
    private readonly Func<ReadOnlyMemory<byte>, int, byte[]?>? _unpack;
    private readonly Func<string, Image<Rgba32>, bool, IntPtr>? _upload;
    private readonly Action<string>? _release;

    /// <summary>How many pictures a second the pane is drawing, for its own row.</summary>
    private readonly RedrawRate _rate = new();

    private string _shown = string.Empty;
    private string _wanted = string.Empty;
    private Task<MonsterModel>? _loading;

    /// <summary>How far the running load has got - see <see cref="Building"/>.</summary>
    private ModelProgress? _progress;
    private MonsterModel _model = MonsterModel.None;

    private IntPtr _texture;
    private string _key = string.Empty;
    private int _keys;

    /// <summary>Where this pane's pictures are drawn on the card, and ImGui's handle for it - kept while the size holds. See <see cref="OnCard"/>.</summary>
    private ModelTarget? _cardTarget;
    private IntPtr _cardShown;

    /// <summary>Whether the picture shown was drawn on the card, and why the last one was not.</summary>
    private bool _onCard;
    private string _cardWhy = string.Empty;

    /// <summary>The card's anisotropic read as the last picture was drawn - a press on the card button redraws.</summary>
    private bool _drawnSharp;

    /// <summary>
    /// Whether the last picture went to the processor because the card said no, and how many material shaders had landed then - one landing since redraws, in case it was this picture's.
    /// </summary>
    private bool _cardWaiting;
    private long _cardLanded;

    /// <summary>
    /// What every key this pane hands the renderer starts with.
    /// </summary>
    /// <remarks>
    /// ONE PER PANE, because the renderer caches by key and there is more than one pane: the
    /// entity browser's strip, the monster book and the item book each own one, and each counted
    /// its keys from zero. Two panes holding "poeformance.monster.3" at once get one texture
    /// between them - AddOrGetImagePointer hands the second the first's picture - and the first
    /// to let go of it frees it under the other.
    /// </remarks>
    private readonly string _prefix = $"poeformance.monster.{Interlocked.Increment(ref s_panes)}.";

    /// <summary>The keys the renderer holds the edge buttons' art under, by side. One upload each per pane.</summary>
    private readonly string[] _sideKeys;

    private MeshPicture.Canvas? _canvas;

    /// <summary>The floor's lines for this frame, in a list kept so that no frame allocates one.</summary>
    private readonly List<ModelFloor.Line> _floor = [];

    /// <summary>The lines under the picture this frame, kept for the same reason.</summary>
    private readonly List<string> _status = [];

    /// <summary>A tile in the shown model's own units - see <see cref="ModelFloor.TileOn"/>.</summary>
    private float _tile = ModelFloor.Tile;

    /// <summary>Where the picture is looked at from - see CameraPose - fitted to each model as it lands (see <see cref="Framed"/>).</summary>
    private CameraPose _view;

    /// <summary>
    /// The pose the picture on screen was drawn from: the floor and the sun's mark are drawn from it, not from the one being moved
    /// towards. Default - no pose at all - until the first picture, and again whenever the next must be drawn whatever moved.
    /// </summary>
    private CameraPose _drawnView;

    /// <summary>The mesh the pose was fitted to, the stage it stood on and whether it was a monster on one - see <see cref="Framed"/>.</summary>
    private SkinnedMesh? _viewFor;
    private string _viewStage = string.Empty;
    private bool _viewStaged;

    /// <summary>Whether the picture is drawn without perspective - the edge button's switch, kept from model to model.</summary>
    private bool _parallel;

    /// <summary>Whether the focus marker is drawn - the edge button's switch, kept from model to model (see <see cref="FocusMark"/>).</summary>
    private bool _focusShown;

    /// <summary>Whether the pointer was over the picture last frame - the keys fly the camera only then (see <see cref="Flown"/>).</summary>
    private bool _hovered;

    /// <summary>Whether the keys flew the camera this frame - a moving picture, drawn at the capped rung (see <see cref="Wanted(float)"/>).</summary>
    private bool _flying;
    private int _drawnSize;
    private float _drawnFrame = float.NaN;
    private int _drawnAnimation = -1;
    private bool _drawnPosed;

    /// <summary>
    /// The game's clock as the picture runs it, in seconds - what a shade program's <c>Time</c> reads.
    /// </summary>
    /// <remarks>
    /// MOVED ON LIKE AN ANIMATION'S FRAME, by the time the last frame took and only while playing, so
    /// Pause stops a scrolling texture as it stops a walk. See <see cref="Ticking"/>.
    /// </remarks>
    private float _clock;
    private float _drawnClock = float.NaN;

    /// <summary>The picture the clock last asked for, while it is being drawn - see <see cref="ClockBehind"/>.</summary>
    private Task<ClockPicture>? _clockDrawing;

    /// <summary>The clock's own canvas: a drawing behind the frame never shares the frame's. It keeps its still part - see MeshPicture.Again.</summary>
    private MeshPicture.Canvas? _clockCanvas;

    /// <summary>What the clock's canvas was last asked to draw with, the time aside.</summary>
    private ClockKey? _clockKey;

    /// <summary>
    /// How many pictures the frame has drawn. A clock drawing asked for before the latest is not shown -
    /// it would put back a picture from before a turn, a zoom or another model.
    /// </summary>
    private int _drawing;

    /// <summary>The list <see cref="_timed"/> was worked out for.</summary>
    private IReadOnlyList<ShadeProgram?>? _timedOf;
    private bool _timed;

    /// <summary>The greying the shown picture was drawn with - 0 for none. See <see cref="Greyed"/>.</summary>
    private float _drawnGrey = float.NaN;

    /// <summary>
    /// The two pictures the last export made, as the renderer knows them, and their keys.
    /// </summary>
    /// <remarks>
    /// THE FILES' OWN PIXELS, not a second rendering of them: what is uploaded here is the
    /// same 64-square buffer that is written to disk, so what is on screen is what is in the
    /// file down to the last edge pixel. A preview drawn any other way could look right while
    /// the file was wrong, which is the one thing a preview must not do.
    /// </remarks>
    private IntPtr _shotColour;
    private IntPtr _shotGrey;
    private string _shotColourKey = string.Empty;
    private string _shotGreyKey = string.Empty;

    /// <summary>Which monster the pair is of, for the window's title.</summary>
    private string _shotStem = string.Empty;

    /// <summary>
    /// The export's own pictures, kept so the outline can be changed without drawing again.
    /// </summary>
    /// <remarks>
    /// THE SOURCE IS THE RAW RENDER - no lift, no greying, no rim - because all three are
    /// settings somebody changes while LOOKING at the result, and redrawing a thousand-square
    /// model for a slider would be forty milliseconds a tick of something the pose did not
    /// change. Both halves are derived from this one buffer by <see cref="Remake"/>, so they
    /// cannot drift apart, and what is made is kept beside it so the button that writes the
    /// files and the pictures on screen cannot come from different pixels.
    /// </remarks>
    private byte[] _shotSource = [];
    private byte[] _shotColourFull = [];
    private byte[] _shotGreyFull = [];
    private byte[] _shotColourCell = [];
    private byte[] _shotGreyCell = [];
    private int _shotWide;
    private int _shotTall;
    private string _shotFolder = string.Empty;

    /// <summary>Whether the files on disk are what the window is showing.</summary>
    /// <remarks>
    /// The outline can be changed after an export, so the two can differ - and a preview that
    /// silently disagreed with the file would be worse than no preview. False puts a line under
    /// the pictures saying so, which is what the write button is for.
    /// </remarks>
    private bool _shotWritten;

    /// <summary>What the lift actually achieved, for the line that says so. See PictureLight.</summary>
    /// <remarks>
    /// REPORTED RATHER THAN ASSUMED, because the exponent is searched on luminance and applied
    /// per channel, and those are not the same operation on a saturated colour - so what comes
    /// out lands near what was asked for rather than on it.
    /// </remarks>
    private float _shotLit;

    /// <summary>Whether the window showing them is up. Opened by an export, closed by its cross.</summary>
    private bool _shotsOpen;

    /// <summary>
    /// What the last export wrote, for the line under the picture. Empty until one happens.
    /// </summary>
    /// <remarks>
    /// VOLATILE because the encoding runs on a task and this is how it reports back - see
    /// <see cref="Save"/>. A reference assignment is atomic either way; what this buys is that
    /// the draw thread is guaranteed to SEE it rather than reading a cached empty string for
    /// as long as the loop keeps it in a register.
    /// </remarks>
    private volatile string _exported = string.Empty;

    /// <summary>
    /// The three facts the picture needs to become a marker, and which export they were filled for.
    /// </summary>
    /// <remarks>
    /// PREFILLED ONCE PER EXPORT AND THEN LEFT ALONE, which is the whole of the rule. Everything
    /// here can be worked out except the part that cannot: the area is the one the player is
    /// standing in, the tile is an arena seen in that area, the name is the monster table's own
    /// - and every one of those is a good guess that is sometimes wrong, so it is put in the
    /// field rather than used. Re-prefilling per frame would type over somebody's correction;
    /// prefilling only when the field is empty would leave the last boss's area behind on the
    /// next one. The stem says which export they belong to, and it is the only trigger.
    /// </remarks>
    private string _entryAreas = string.Empty;
    private string _entryTile = string.Empty;
    private string _entryName = string.Empty;
    private string _entryFor = string.Empty;

    /// <summary>What the last write to the curated file said, or empty when none happened.</summary>
    private string _remembered = string.Empty;

    /// <summary>The monster's own name, as the table has it. The name field's prefill.</summary>
    private string _called = string.Empty;

    /// <summary>Whether what is loaded was read WITH the monster's attachments. See Wanted.</summary>
    private bool _dressed = true;

    /// <summary>The row the model was loaded for, kept for the file dump - it needs the .ao list.</summary>
    private MonsterVariety? _variety;

    /// <summary>What the last file dump wrote, or why it could not. Cleared with the model.</summary>
    private string _dumped = string.Empty;

    private SkeletonPose? _pose;
    private AnimationTracks? _tracks;
    private Task<AnimationTracks?>? _loadingTracks;
    private int _loadingAnimation = -1;
    private int _chosen = -1;
    private float _frame;
    private bool _playing = true;
    private string _stillWhy = string.Empty;

    /// <summary>The line about the stage the monster stands on, or why there is none - see MonsterModel.StageSaid.</summary>
    private string _stage = string.Empty;

    /// <summary>A room stage's own lines, or empty - see MonsterModel.StageMove.</summary>
    private string _stageMove = string.Empty;

    /// <summary>Whether the model wears the engine's water anywhere - see ShadeProgram.Water - so the picture says what water it is drawn with.</summary>
    private bool _wearsWater;

    /// <summary>Whether the model carries the engine's beach waves' sheet - see ShadeProgram.Beaches.</summary>
    private bool _wearsBeach;

    /// <summary>The water line under the picture and the water it was written for - rewritten only when that changes.</summary>
    private string _waterSaid = string.Empty;

    private WaterLook _waterSaidFor;

    /// <summary>Whether the line was made with the field waves drawn - the card's, with the light's crash switch on.</summary>
    private bool _waterSaidCrash;
    private bool _waterSaidGround;

    /// <summary>Whether the line was made with the card's wetness pass drawn - the light's wetness switch on.</summary>
    private bool _waterSaidWet;

    /// <summary>Whether the line was made with the card's refraction drawn - the light's refraction switch on.</summary>
    private bool _waterSaidRefract;

    /// <summary>Whether the line was made with the caustics on, and over which of their states (ShadeProgram.CausticsSaid, by reference).</summary>
    private bool _waterSaidCausticsOn;
    private string? _waterSaidCausticsBy;
    private bool _waterSaidLit;
    private Vector3[] _posed = [];
    private Vector3[] _posedNormals = [];

    /// <summary>
    /// What <see cref="_posed"/> was last posed for - the model, its keyframes and the frame - and the key the card is told it by (ModelScene.PoseKey), new for every pose.
    /// </summary>
    /// <remarks>
    /// A PAUSED MONSTER WAS POSED AGAIN ON EVERY REDRAW - a drag, an orbit, a light's slider - and the
    /// card sent the whole stage and cast its shadow again each time, for a pose that had not moved.
    /// </remarks>
    private (MonsterModel Model, AnimationTracks Tracks, float Frame)? _posedFor;

    private long _poseKey;

    /// <summary>The keys handed out, one counter for every pane, so two panes' poses never share one.</summary>
    private static long _poseKeys;

    /// <summary>The monster's own vertices posed, where he stands on a stage - written over his slice of the above. See <see cref="Posed"/>.</summary>
    private Vector3[] _actorPosed = [];
    private Vector3[] _actorNormals = [];

    /// <summary>
    /// Which pose <see cref="_posed"/> holds, by its key - the processor's arrays are posed only when something reads them (see <see cref="CpuPosed"/>); -1 none.
    /// </summary>
    /// <remarks>
    /// THE CARD SKINS THE PICTURE ITSELF (CardSkin, v0.1.325): every frame of a playing animation skinned the monster on the
    /// processor - tens of thousands of vertices on every core - and sent them to the card, which then drew them. Now the
    /// processor poses the vertices only for what reads them: its own picture (the probe, the grey, a shader still compiling),
    /// the pick under the pointer, and the export.
    /// </remarks>
    private long _cpuPosedKey = -1;

    /// <summary>The monster's bones' boxes of the vertices they move, for the sun's map of a pose the card skins - made the first time a shadow asks.</summary>
    private PoseHull? _hull;
    private bool _hullMade;

    /// <summary>The hull's bounds along the sun, and the pose and sun they were worked out for.</summary>
    private (long Key, Vector3 Direction, ShadowBounds Bounds)? _hullFor;

    /// <summary>
    /// The bones' matrices the card skins by - SkeletonPose.Skin alone, conjugated by his place on a stage - and the pose they hold.
    /// </summary>
    private Matrix4x4[] _cardBones = [];
    private long _cardBonesKey = -1;
    private CardSkin? _cardSkin;

    /// <summary>The lowest point of the pose last worked out (<see cref="Lowest()"/>), the pose it was for and when.</summary>
    private float _lowest;
    private long _lowestKey = -1;
    private double _lowestAt = double.NegativeInfinity;

    /// <summary>
    /// How often, in seconds, a playing animation's lowest point is worked out again - CHOSEN: five times a second, the notice it feeds
    /// (Planted) being about a monster standing well under the floor, not a frame's worth of a foot.
    /// </summary>
    private const double LowestEvery = 0.2;

    /// <summary>
    /// The model's textures being decoded behind its load for the processor's picture, and what stops it - see <see cref="UnpackBehind"/>.
    /// </summary>
    private Task? _unpacking;
    private CancellationTokenSource? _unpackStop;

    /// <summary>The textures <see cref="_unpacking"/> decodes, the model's every one.</summary>
    private Mipmaps[] _unpackOf = [];

    /// <summary>When the unpacking began, as Stopwatch counts - for the capture's line about it.</summary>
    private long _unpackBegan;

    /// <summary>Whether the processor's picture is waiting for the unpacking - it is drawn the frame the unpacking ends.</summary>
    private bool _unpackWaiting;

    /// <summary>Whether the capture's line about the unpacking has been written for the model shown.</summary>
    private bool _unpackSaid;

    /// <summary>Whether the model's textures are all decoded for the processor - or there are none to decode.</summary>
    private bool UnpackDone => _unpacking is null || _unpacking.IsCompleted;

    /// <summary>The lines that change when a model or its keyframes land, built then and not per frame.</summary>
    private string _cost = string.Empty;
    private string _paint = string.Empty;
    private string _still = string.Empty;
    private string _shaders = string.Empty;
    private string _blend = string.Empty;
    private string _graphs = string.Empty;
    private string _count = string.Empty;

    /// <summary>The line for a model that stands in a pit, and the whole unit it was last built for.</summary>
    /// <remarks>
    /// REBUILT ONLY WHEN THE UNIT CHANGES. The lowest point is found on every redraw - one pass
    /// over the posed vertices, which is nothing beside drawing them - but a line is a string,
    /// and a walk that bobs its feet by a fraction of a unit a frame would otherwise allocate one
    /// per frame for a number that reads the same.
    /// </remarks>
    private string _planted = string.Empty;
    private int _plantedAt = int.MinValue;

    /// <summary>The combo's width, and the font size it was measured at.</summary>
    /// <remarks>
    /// AS WIDE AS THE LONGEST NAME IN IT AND NO WIDER, by request; measured once per model rather
    /// than per frame, because a rig can carry two hundred animations and the width does not
    /// change until the names or the font do.
    /// </remarks>
    private float _comboWide;
    private float _comboFont;

    /// <summary>Whether the camera is being carried round the model on its own.</summary>
    private bool _orbiting;

    /// <summary>Each edge button's art, the size it was uploaded at, and whether asking for it failed - by side.</summary>
    /// <remarks>
    /// UPLOADED AT THE SIZE IT IS DRAWN, not at the 350 px it ships at: a button's worth of
    /// pixels sampled one to one is crisp, and the same art shrunk by the renderer's bilinear
    /// filter from twelve times the size is a smear. The size follows the frame height, so a
    /// change of font scale re-uploads it once. Asked for once: a resource that is not there is
    /// a build mistake, and not one to go looking for sixty times a second.
    /// </remarks>
    private readonly IntPtr[] _side = new IntPtr[Sides];
    private readonly int[] _sidePixels = new int[Sides];
    private readonly bool[] _sideMissing = new bool[Sides];

    /// <summary>Whether the model was being dragged last frame, which is what caps the rung.</summary>
    /// <remarks>
    /// LAST FRAME AND NOT THIS ONE, because the size has to be settled before the button that
    /// reports the drag has been submitted. Being a frame behind costs one frame drawn at the
    /// wrong rung at each end of a drag, which nobody can see; asking in the right order would
    /// mean drawing the picture before knowing how big to draw it.
    /// </remarks>
    private bool _held;

    /// <param name="install">How to read a file out of the game, or null where there is none.</param>
    /// <param name="upload">Hands pixels to the renderer and gives back a handle.</param>
    /// <param name="release">Gives a handle back.</param>
    /// <param name="most">The biggest the model may be drawn, each way. See <see cref="PictureLadder"/>.</param>
    /// <param name="unpack">
    /// The install's Oodle, for keyframes kept in a bundle. Null leaves rigs from version 8 up
    /// standing still; the older ones keep their frames loose and play regardless.
    /// </param>
    public MonsterPortrait(
        Func<string, byte[]?>? install,
        Func<string, Image<Rgba32>, bool, IntPtr>? upload,
        Action<string>? release,
        int most = PictureLadder.Usual,
        Func<ReadOnlyMemory<byte>, int, byte[]?>? unpack = null)
    {
        _install = install;
        _upload = upload;
        _release = release;
        _unpack = unpack;
        _sizes = new PictureLadder(most);
        _sideKeys = [_prefix + "card", _prefix + "light", _prefix + "orbit", _prefix + "probe", _prefix + "clock", _prefix + "reset", _prefix + "game", _prefix + "perspective"];
    }

    /// <summary>
    /// How a model is loaded, where it is not a monster's or an item's .ao walk.
    /// </summary>
    /// <remarks>
    /// NULL FOR THE BOOKS THAT DRAW AN .ao, which is what the pane was built for. The tile book
    /// draws a terrain tile instead - a .tdt, not an .ao - and everything after the load is the
    /// same: the camera, the floor, the turning, the export. Handed the install, the variety the
    /// caller drew with, the key it drew under and whether attachments are wanted; run on the
    /// thread pool like the walk it replaces, with where to say how far it has got - see ModelProgress.
    /// </remarks>
    public Func<Func<string, byte[]?>, MonsterVariety, string, bool, ModelProgress, MonsterModel>? Load { get; set; }

    /// <summary>
    /// Whether the line under the picture names the shader graphs the model's materials use.
    /// </summary>
    /// <remarks>
    /// ON IN THE EFFECT BOOK, where it is the evidence for how a material blends - see
    /// MaterialFile.Parents - and off elsewhere, where it would be one more line nobody asked for.
    /// </remarks>
    public bool ShowShaders { get; set; }

    /// <summary>
    /// Whether materials that ask to blend are drawn translucent - mixed or added - rather than solid.
    /// </summary>
    /// <remarks>
    /// ON IN THE EFFECT BOOK AND THE TILE BOOK. What a blend mode's word means is a rule on words, not
    /// a measurement - see MaterialBlends - and the monster book exports boss icons from what this
    /// pane draws: a guess that turned part of a boss see-through would end up in the map's art.
    /// The effect book is where it was asked for, and where a wrong guess costs a look and nothing more.
    ///
    /// THE TILE BOOK WAS OFF, and that is why the contact fade changed nothing there: a ground layer's
    /// fade runs in the translucent pass, against the solid depth under it, and with this off every
    /// mixed shape was drawn solid. The probe said so in one line - "nothing translucent reached the
    /// picture" - over a room whose mud is sixty mixed shapes.
    /// </remarks>
    public bool Translucent { get; set; }

    /// <summary>
    /// Whether cut-out and shadow-only materials are honoured where <see cref="Translucent"/> is off.
    /// </summary>
    /// <remarks>
    /// NEITHER IS TRANSLUCENCY, and the tile book needs both: a shadow caster such as
    /// black_shadowonlyc.mat is never seen in the game, and drawn solid it laid black slabs across
    /// the ship in Port's boss room. Without this the book drew every shape solid, because the blend
    /// list reached the picture only where translucency was on - which is why the shadow-only fix
    /// did nothing there at first. Mixed and added shapes stay solid, as <see cref="Translucent"/> says.
    /// </remarks>
    public bool Masked { get; set; }

    /// <summary>
    /// The blends handed to the picture: <see cref="Unfaded"/>'s, with the roof sections round the camera's focus left out whole where the
    /// light panel's "roof fade" is on (RoofFade - the sections reaching into the region between the viewer's camera and its focus,
    /// the extents CHOSEN within what the captures allow).
    /// </summary>
    /// <remarks>
    /// A NEW LIST ONLY WHEN THE SECTIONS HIDDEN CHANGE: both drawings compare the list they were drawn with by reference, so the picture is
    /// drawn again when a section crosses the region's edge, and not on every step of a drag. The sections are asked again only when the
    /// focus or the turn moved - a few hundred boxes laid on two ways, into a list kept for the purpose.
    /// </remarks>
    private IReadOnlyList<MaterialBlend>? Blends()
    {
        IReadOnlyList<MaterialBlend>? own = Unfaded();
        if (SceneRoofFade?.Invoke() != true || Roofs() is not { } roofs || !_view.Valid)
        {
            _roofsShown = false;
            return own;
        }

        if (!_roofsShown || !ReferenceEquals(_fadedOf, own) || _roofsAt != _view.Focus || _roofsTurn != _view.Turn)
        {
            int count = roofs.Hide(_view.Focus, RoofFade.AheadOf(_view.Turn), _roofsAsked);
            (_roofsAt, _roofsTurn) = (_view.Focus, _view.Turn);
            if (!_roofsShown || !ReferenceEquals(_fadedOf, own) || !_roofsAsked.AsSpan().SequenceEqual(_roofsHidden))
            {
                (_roofsHidden, _roofsAsked) = (_roofsAsked, _roofsHidden);
                _roofsShown = true;
                _roofsChanged++;
                _fadedOf = own;
                _faded = count == 0 ? own : roofs.Faded(own, _model.Mesh.Shapes.Count, _roofsHidden);
            }
        }

        return _faded;
    }

    /// <summary>The roof sections of tile pieces of the model shown, worked out once a model - see RoofFade - or null where it has none.</summary>
    private RoofFade? Roofs()
    {
        if (!ReferenceEquals(_roofsOf, _model))
        {
            _roofsOf = _model;
            _roofs = RoofFade.Of(_model);
            _roofsHidden = new bool[_roofs?.Sections ?? 0];
            _roofsAsked = new bool[_roofsHidden.Length];
            _roofsShown = false;
            _fadedOf = null;
            _faded = null;
        }

        return _roofs;
    }

    private MonsterModel? _roofsOf;
    private RoofFade? _roofs;
    private RoofFade? _roofSaidOf;
    private int _roofSaidFor = -1;
    private string _roofSaid = string.Empty;
    private IReadOnlyList<MaterialBlend>? _fadedOf;
    private IReadOnlyList<MaterialBlend>? _faded;

    /// <summary>The sections the picture is drawn with hidden, one flag a section, and the list the next ask fills - swapped when they differ.</summary>
    private bool[] _roofsHidden = [];
    private bool[] _roofsAsked = [];

    /// <summary>Whether <see cref="_roofsHidden"/> holds the hidden sections of the blends last handed on, and where they were asked from.</summary>
    private bool _roofsShown;
    private Vector3 _roofsAt;
    private float _roofsTurn;

    /// <summary>How many times the hidden sections changed - what the line under the picture is kept by.</summary>
    private int _roofsChanged;

    /// <summary>
    /// Whether the roof sections round the camera's focus are hidden - the light panel's "roof fade", asked whether the game's light is on or
    /// not, like the dust; null for never. See RoofFade.
    /// </summary>
    public Func<bool>? SceneRoofFade { get; set; }

    /// <summary>
    /// Whether a piece's lamp swings - the light panel's "swing", asked every pose whether the game's light is on or not; null for always.
    /// See ClothSwinger and <see cref="Swung"/>.
    /// </summary>
    public Func<bool>? SceneSwing { get; set; }

    /// <summary>The model's swings stepped with its clock, or null where it swings nothing - made with the pose (<see cref="Rigged"/>).</summary>
    private ClothSwinger? _swinger;

    /// <summary>Whether the last pose was swung, so a change of the switch takes the pose again.</summary>
    private bool _drawnSwing = true;

    /// <summary>Whether the lamps swing now - the panel's switch, on where none is wired.</summary>
    private bool Swinging() => SceneSwing?.Invoke() ?? true;

    /// <summary>
    /// The simulated bones set into the pose just taken: the cloths stepped once by the time the clock ran on, as the engine steps them, each
    /// bone then where its cloth holds it (ClothSwinger.Swing). Nothing where the model simulates nothing.
    /// </summary>
    /// <remarks>
    /// RIGHT AFTER EVERY TAKE, before anything reads the pose's skin matrices - the card's upload, the processor's vertices, the hull, the
    /// carried lights - all of which key on the pose (<see cref="_poseKey"/>).
    /// </remarks>
    private void Swung()
    {
        if (_swinger is not { } swinger || _pose is not { } pose || _tracks is not { Ready: true } tracks
            || _chosen < 0 || _chosen >= _model.Rig.Animations.Count)
        {
            return;
        }

        float rate = _model.Rig.Animations[_chosen].Rate;
        bool on = Swinging();
        swinger.Swing(pose, tracks, _frame, rate > 0f ? rate : UsualRate, tracks.Frames, on);
        _drawnSwing = on;
    }

    /// <summary>
    /// The blends handed to the picture before the roof fade: all of them, the solid-or-absent ones only, or none - and what a capture's pane
    /// is drawn with, every roof section in it (CaptureRoofFade measures what the game hides against them).
    /// </summary>
    private IReadOnlyList<MaterialBlend>? Unfaded()
    {
        IReadOnlyList<MaterialBlend> all = _model.Blends;
        if (Translucent)
        {
            return all;
        }

        // A STAGE IS DRAWN THE TILE BOOK'S WAY whatever this pane's switches say - every blend as its
        // material spells it, or its ground layers are slabs of mud and its shadow casters black
        // slabs - and the monster on it this pane's way, so he is drawn as he is drawn alone.
        int stage = _model.Actor is { } actor ? _model.Mesh.Shapes.Count - actor.Mesh.Shapes.Count : 0;
        if (!Masked && stage == 0)
        {
            return null;
        }

        // Worked out once per list: the picture asks on every redraw, and a drag is many of those.
        if (!ReferenceEquals(_maskedOf, all))
        {
            _maskedOf = all;
            var kept = new MaterialBlend[all.Count];
            for (var one = 0; one < kept.Length; one++)
            {
                kept[one] = one < stage || (Masked && all[one] is MaterialBlend.Cutout or MaterialBlend.ShadowOnly)
                    ? all[one]
                    : MaterialBlend.Opaque;
            }

            _masked = kept;
        }

        return _masked;
    }

    private IReadOnlyList<MaterialBlend>? _maskedOf;
    private IReadOnlyList<MaterialBlend>? _masked;

    /// <summary>The blends the last picture was drawn with, compared by reference - a turn of the translucency switches redraws.</summary>
    private IReadOnlyList<MaterialBlend>? _drawnBlends;

    /// <summary>
    /// Whether a material's colour is worked out from its shader graphs where they say more than a texture.
    /// </summary>
    /// <remarks>
    /// ON IN THE TILE, ITEM AND EFFECT BOOKS, off in the monster book. Not because it is a guess -
    /// ShadeProgram evaluates only the nodes whose meaning is plain and names the rest - but because
    /// the monster book exports boss icons from what this pane draws, and those are already in the
    /// map's art: a change to how a boss is coloured should be a choice made for that book, not a
    /// side effect of a terrain fix. A pane with its own Load reads this to decide what to load.
    /// </remarks>
    public bool Shaded { get; set; }

    /// <summary>
    /// Whether a specular colour is lit flat - laid onto the albedo - rather than the game's way, by its GGX lobe; see MonsterModel.GlossShades.
    /// </summary>
    /// <remarks>
    /// THE GAME'S WAY BY DEFAULT, flat where that is too slow: the glossy programs read one more
    /// texture and every pixel of them works out a lobe. Changed by the button beside the rate,
    /// which tells <see cref="FlatLightChanged"/> so every book follows and the settings keep it.
    /// </remarks>
    public bool FlatLight { get; set; }

    /// <summary>Told when the light button is pressed, with the new choice.</summary>
    public Action<bool>? FlatLightChanged { get; set; }

    /// <summary>The programs the picture is drawn with: none unshaded, else the flat or the glossy list - see <see cref="FlatLight"/>.</summary>
    /// <remarks>
    /// A STAGE IS ALWAYS DRAWN FROM ITS GRAPHS, as the tile book draws it: a staged model's lists carry
    /// the stage's programs and, where this pane is unshaded, nothing for the monster's shapes - so he
    /// is drawn from his texture as he is alone, and the ground under him is the tile book's ground.
    /// </remarks>
    private IReadOnlyList<ShadeProgram?>? ShadesOf(MonsterModel model) => ShadesOf(model, SceneLit?.Invoke(model));

    /// <summary>The programs the picture is drawn with under a light - the pane's own, or the one a capture measures with (PaneReport).</summary>
    private IReadOnlyList<ShadeProgram?>? ShadesOf(MonsterModel model, SceneLight? light)
    {
        IReadOnlyList<ShadeProgram?>? shades = !Shaded && !model.Staged ? null : FlatLight || model.GlossShades.Count == 0 ? model.Shades : model.GlossShades;
        return shades is not null && light is { SprayOccluded: true } ? Occluded(shades) : shades;
    }

    /// <summary>The list <see cref="Occluded"/> last swapped, and what it came to - the same list while neither changes, so nothing redraws.</summary>
    private (IReadOnlyList<ShadeProgram?> Of, IReadOnlyList<ShadeProgram?> Swapped, int Count)? _occluded;

    /// <summary>The line saying how many shapes' spray the switch swapped, and the count it was made for.</summary>
    private string _occludedSaid = string.Empty;

    private int _occludedSaidFor = -1;

    /// <summary>The line under a card's picture the screen-space fog is laid over - see SceneLight.Fog and ModelGpu's Fogged.</summary>
    private const string FogDrawn = "screen-space fog: the area's weather attachment's pass (ScreenspaceFogFlat.fxgraph and its group, compiled from its"
        + " own files) laid over the picture - CHOSEN: the camera's focus stands for the player, drawn at the picture's own size, exposed and"
        + " graded as a mixed layer, none where nothing solid lies behind, its sun's shadow sampled a texel apart and clouded as the ground's;"
        + " the card's alone";

    /// <summary>The line under a card's picture the clouds' shadow lies on - see SceneLight.Clouds and ModelShaders' Clouded.</summary>
    private const string CloudsDrawn = "clouds: shadows.ffx's GetCloudsIntensity on the sun's light, on every surface and through the fog, as the area's"
        + " .env sets them - MEASURED in The Red Vale: muddle_double.dds's alpha as the engine's noise (no file names it; held against the"
        + " game's picture it is the best candidate in two captures, clearly in the second) and clouds_velocity along (cos a, sin a) of the"
        + " .env's angle; CHOSEN: the camera's focus as the player, the picture's clock as the engine's time; the card's alone";

    /// <summary>
    /// A list with every program that has one swapped for its twin whose spray reads the stone's own occlusion (ShadeProgram.Occluded) -
    /// the light panel's "spray from occlusion"; the list itself where none has one. Both drawings take it.
    /// </summary>
    private IReadOnlyList<ShadeProgram?> Occluded(IReadOnlyList<ShadeProgram?> shades)
    {
        if (_occluded is { } held && ReferenceEquals(held.Of, shades))
        {
            return held.Swapped;
        }

        ShadeProgram?[]? swapped = null;
        var count = 0;
        for (var at = 0; at < shades.Count; at++)
        {
            if (shades[at]?.Occluded is { } twin)
            {
                swapped ??= [.. shades];
                swapped[at] = twin;
                count++;
            }
        }

        IReadOnlyList<ShadeProgram?> result = swapped ?? shades;
        _occluded = (shades, result, count);
        return result;
    }

    /// <summary>The list the last picture was drawn with, compared by reference - a press of the light button redraws.</summary>
    private IReadOnlyList<ShadeProgram?>? _drawnShades;

    /// <summary>
    /// The game's light a model is drawn under, or null for the picture's own - see SceneLightPanel. Null leaves every picture as it was.
    /// </summary>
    /// <remarks>
    /// ASKED EVERY FRAME AND COMPARED BY REFERENCE: the panel hands back the same light while nothing
    /// changed, and a new one the moment a switch, a reading or the environment does - which redraws.
    /// </remarks>
    public Func<MonsterModel, SceneLight?>? SceneLit { get; set; }

    /// <summary>The model the pane is showing, or MonsterModel.None.</summary>
    public MonsterModel Showing => _model;

    /// <summary>
    /// The sun shift + drag moves, and whose mark the picture shows while it is set by hand - see SceneLightPanel. Null leaves the drag turning the model only.
    /// </summary>
    public IMovableSun? Sun { get; set; }

    /// <summary>Whether the drag in hand is moving the sun - it stays so until the button is let go, shift or not.</summary>
    private bool _sunHeld;

    /// <summary>The light the last picture was drawn under, compared by reference.</summary>
    private SceneLight? _drawnLight;

    /// <summary>The light for the model in the pane, or null - with the lights the model carries on its bones where its pose puts them (Carrying).</summary>
    private SceneLight? Lit() => SceneLit?.Invoke(_model) is { } light ? Carrying(light) : null;

    // THE CARRIED LIGHTS' LAST LAYING: the light they were laid into, the model and the pose they stood at, and the light that made.
    private SceneLight? _carriedFrom;
    private MonsterModel? _carriedModel;
    private long _carriedPose = -1;
    private bool _carriedPosed;
    private float _carriedClock = float.NaN;
    private SceneLight? _carriedLight;
    private PointLight[] _carriedAt = [];

    /// <summary>
    /// The light with the lights the model carries on its bones standing where the pose the pane holds puts them - the same object while the
    /// light, the model and the pose stand, so the picture is redrawn only when one moved (the redraw compares lights by reference).
    /// </summary>
    private SceneLight Carrying(SceneLight light)
    {
        bool posed = _tracks is { Ready: true } && _pose is not null;

        // A FLICKERING LIGHT IS LAID AGAIN AS THE CLOCK RUNS, the pose standing or not (LightStyle) - a light with no style as before.
        bool flickers = light.Flickers && light.Carries && CarriedLights.Flicker((_model.Actor ?? _model).Carried);
        if (_carriedLight is not null && ReferenceEquals(_carriedFrom, light) && ReferenceEquals(_carriedModel, _model)
            && _carriedPosed == posed && (!posed || _carriedPose == _poseKey) && (!flickers || _carriedClock == _clock))
        {
            return _carriedLight;
        }

        _carriedLight = Carried(light, posed);
        _carriedFrom = light;
        _carriedModel = _model;
        _carriedPose = _poseKey;
        _carriedPosed = posed;
        _carriedClock = _clock;
        return _carriedLight;
    }

    /// <summary>
    /// A light with the model's carried lights laid in where the pose puts their bones (CarriedLights.At) - a staged monster's through his place
    /// on the stage, as the card skins him - or the light as it is where he carries none or the panel's switch is off.
    /// </summary>
    private SceneLight Carried(SceneLight light, bool posed)
    {
        MonsterModel carrier = _model.Actor ?? _model;
        if (carrier.Carried.Count == 0 || !light.Carries)
        {
            return light;
        }

        if (_carriedAt.Length < carrier.Carried.Count)
        {
            _carriedAt = new PointLight[carrier.Carried.Count];
        }

        int count = CarriedLights.At(
            carrier.Carried, posed ? _pose!.Skin : [], _model.Actor is null ? Matrix4x4.Identity : _model.ActorPlace, _carriedAt,
            light.Flickers ? _clock : double.NaN);
        return light.Carrying(_carriedAt.AsSpan(0, count));
    }

    /// <summary>
    /// The area's dust colour a material's DustColor reads, or null for the assumed one - asked whether the game's light is on or not.
    /// </summary>
    /// <remarks>
    /// NOT PART OF THE LIGHT: the dust is a colour the graphs lay over a material's own, so a picture
    /// under the lamp wears it as one under the game's light does. See ShadeProgram.Dust.
    /// </remarks>
    public Func<Vector3>? SceneDust { get; set; }

    private Vector3 Dusted() => SceneDust?.Invoke() ?? EnvironmentSettings.AssumedDust;

    private Vector3 _drawnDust = EnvironmentSettings.AssumedDust;

    /// <summary>
    /// The area's open water the water program reads, or null for the engine's default - asked whether the game's light is on or not, like the dust.
    /// </summary>
    /// <remarks>See ShadeProgram.Water: an Ocean material is drawn as the area's water, whose file sets it or leaves the engine's default.</remarks>
    public Func<WaterLook>? SceneWater { get; set; }

    /// <summary>
    /// The river's flow map of the area instance a model was laid in, or null - asked with the model's <see cref="MonsterModel.AreaStamp"/>, so a
    /// picture kept from an area since left is handed nothing (RiverFlowLive, v0.1.357).
    /// </summary>
    public Func<int, RiverFlowMap?>? SceneFlow { get; set; }

    /// <summary>
    /// The area's water, over the room's own shore where the model is a room that has one, its way in the light's - see ShoreField.Way - and
    /// the area's river flow where the model is laid as the area laid it.
    /// </summary>
    private WaterLook Watered() => Watered(Lit());

    /// <summary>The water under a light - the pane's own, or the one a capture measures with (PaneReport): its way in is the light's.</summary>
    private WaterLook Watered(SceneLight? light)
        => (SceneWater?.Invoke() ?? WaterLook.EngineDefault) with
        {
            Shore = light is { } lit ? _model.Shore?.Way(lit.WaterWayRound, lit.WaterPastWalls) : _model.Shore,
            Flow = _model.AreaOrigin is { } origin && _model.AreaStamp is { } stamp && SceneFlow?.Invoke(stamp) is { } map ? new RiverFlow(map, origin) : null,
        };

    private WaterLook _drawnWater = WaterLook.EngineDefault;

    /// <summary>Which way in the waves run down, as the line under the picture says it - the light panel's two switches (ShoreField.Way).</summary>
    private static string WayIn(ShoreField? shore) => shore switch
    {
        { PassesWalls: true } => "past the walls, a quay's or a pier's own ground in no wave's way (the light panel's \"past walls\")",
        { RoundsWhatStands: true } => "round whatever crosses the water - the ship's hull among it (the light panel's \"way round\")",
        _ => "round the ground alone, the waves running on under what stands in the water",
    };

    /// <summary>
    /// Which tilesets place which tile, for a tile's dump to find the areas that use it - see ModelDump.OfTile.
    /// </summary>
    public TilesetCatalog? Tilesets { get; set; }

    /// <summary>The install's shader sources, for a tile's dump to search - see ModelDump.OfTile.</summary>
    public Func<IReadOnlyList<string>>? Shaders { get; set; }

    /// <summary>The files the current area loaded, for a room's dump to find the environments it is lit by - see ModelDump.Lighting.</summary>
    public Func<IReadOnlyList<string>>? Loaded { get; set; }

    /// <summary>Every material the install has, for the graph survey - see GraphSurvey. Null leaves its button out.</summary>
    public Func<IReadOnlyList<string>>? Materials { get; set; }

    /// <summary>Every environment the install has, for the env survey - see EnvSurvey. Null leaves its button out.</summary>
    public Func<IReadOnlyList<string>>? Environments { get; set; }

    /// <summary>Whether a graph survey is running, so a second press does not start another.</summary>
    private int _surveying;

    /// <summary>
    /// The colour a shape with no texture is drawn in, or default for the pale warm grey every book began with.
    /// </summary>
    /// <remarks>
    /// THE TILE BOOK'S GROUND IS THE CASE: it is unpainted on purpose, and in the warm grey it read as
    /// the brightest thing in the picture - a slab louder than the props standing on it. A darker
    /// grey says "nothing to see here" in the one place that is true.
    /// </remarks>
    public Vector3 Ink { get; set; }

    /// <summary>Whether a picture could be drawn at all - an install and a renderer.</summary>
    public bool Possible => _install is not null && _upload is not null;

    /// <summary>Where exported icon art is written.</summary>
    /// <remarks>
    /// Beside the executable, next to logs/ and data/, because these are files somebody is
    /// about to go and pick up - a folder they can find without being told where it is.
    /// </remarks>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "exports");

    /// <summary>
    /// The greying the picture is drawn with: the factor, or 0 while it is switched off.
    /// </summary>
    /// <remarks>
    /// ONE NUMBER FOR BOTH QUESTIONS, so the redraw check has one thing to compare. Zero can
    /// never collide with a real factor - <see cref="PictureGrey.Weakest"/> is well above it.
    /// </remarks>
    private float Greyed => Grey ? Math.Clamp(GreyFactor, PictureGrey.Weakest, PictureGrey.Strongest) : 0f;

    /// <summary>The biggest the model will be drawn, each way.</summary>
    public int Most => _sizes.Most;

    /// <summary>Whether the floor the model stands on is drawn. On, because it is what makes a turn legible.</summary>
    public bool Ground { get; set; } = true;

    /// <summary>
    /// Whether the pieces the monster hangs off itself are read and drawn with it. On.
    /// </summary>
    /// <remarks>
    /// ON, BECAUSE THE MONSTER IS WRONG WITHOUT THEM. Doryani in the game wears a skirt, a belt
    /// and a necklace; the pane drew him bare-legged for as long as it read the body alone, and
    /// an icon made from that is an icon of somebody else.
    ///
    /// A SWITCH AT ALL, BECAUSE THEY ARE NOT FREE. His nine pieces bring their own meshes, rigs
    /// and sheets and the belt hangs three more under itself - two to three times the reads of
    /// the body, paid on every click in a list somebody scrolls. See MonsterModels.Dressing.
    /// </remarks>
    public bool Parts { get; set; } = true;

    /// <summary>
    /// The terrain tile the monster is stood on, as a <see cref="TileKey"/> string, or empty for the monster alone - see StagedModels.
    /// </summary>
    /// <remarks>
    /// PART OF WHAT IS ASKED FOR, like <see cref="Parts"/>: the stage is read from the install and joined
    /// onto the monster at load, so a change of stage goes back for both. Only a book that loads its
    /// model the usual way takes one; a book with a <see cref="Load"/> of its own draws what that says.
    /// </remarks>
    public string Stage { get; set; } = string.Empty;

    /// <summary>The stage the running load was asked for, so a change of it is a new load.</summary>
    private string _stagedOn = string.Empty;

    /// <summary>
    /// Called when one of the capture settings moved, so the choice is written down.
    /// </summary>
    /// <remarks>
    /// The same arrangement the place markers make: holding a value and announcing that it
    /// moved are separate jobs, and a control that does only the first is indistinguishable
    /// from one that does neither once the tool is restarted.
    /// </remarks>
    public Action? Changed { get; set; }

    /// <summary>
    /// The curated table an exported picture is written into, or null while nothing is loaded.
    /// </summary>
    /// <remarks>
    /// THE HALF OF AN EXPORT THAT IS NOT A PICTURE. Four PNGs in a folder are not a feature -
    /// something has to know that they belong to the arena in Grimhaven and to the one in
    /// Epitaph, and that what stands in both is called Saphira. Those three facts are only ever
    /// all in one person's head while they are looking at the model, so they are asked for here
    /// and written in the same click. See <see cref="BossIcons.Remember"/>.
    /// </remarks>
    public BossIcons? Icons { get; set; }

    /// <summary>
    /// Called once an export's files are all on disk, from the task that wrote them.
    /// </summary>
    /// <remarks>
    /// What puts the new picture on the map in the same session: the icon sheet lays the
    /// exports folder into its texture, and this is how it learns there is something new in
    /// it - see IconCache.SheetChanged, which is safe to call from the writing task.
    /// </remarks>
    public Action? Written { get; set; }

    /// <summary>The area the player is in, for the area field's prefill. Empty when unknown.</summary>
    public Func<string>? AreaNow { get; set; }

    /// <summary>
    /// The arena tiles known for an area, for the tile field's prefill.
    /// </summary>
    /// <remarks>
    /// A FUNCTION RATHER THAN A LIST, because the two sources it draws on answer at different
    /// times: the area on screen has its arenas in the terrain this frame, while a map played an
    /// hour ago has only what was written to logs/boss-arenas.tsv. Which of the two applies
    /// depends on what somebody typed in the area field, so it is asked rather than handed over.
    /// </remarks>
    public Func<string, IReadOnlyList<string>>? ArenasIn { get; set; }

    /// <summary>
    /// The current area's grid, for a stage that is a room laid in it - see StagedModels.LaidRoom. Null, or answering null, leaves such a stage out with its reason.
    /// </summary>
    /// <remarks>Asked on the load's task when the stage is a room, which is why it is a function and not the grid: the grid is the frame's, and the load starts from one frame and lands in another.</remarks>
    public Func<TerrainGrid?>? Terrain { get; set; }

    /// <summary>
    /// Whether the model is drawn greyed, the way the game's own Inactive map icons are.
    /// </summary>
    /// <remarks>
    /// WHAT IT IS FOR: a boss with no minimap icon of its own gets one made from its model, and
    /// the game's landmarks come in pairs - the colour one while the thing is alive and a grey
    /// one once it is not. This is the second of the pair, done to the same rule the game's art
    /// follows (see <see cref="PictureGrey"/>), so the two halves of a made icon match the
    /// hundreds the game shipped.
    ///
    /// ON THE PICTURE AND NOT ON THE SKIN, deliberately. Greying the texture once when a model
    /// loads would be free per frame and would do nothing at all for the monsters drawn in plain
    /// ink - <see cref="MonsterModel.Painted"/> is false for a good many - and it would leave
    /// what is exported able to differ from what is on screen. One pass over the pixels of a
    /// picture that was going to be uploaded anyway is the cost, and only while this is on.
    /// </remarks>
    public bool Grey { get; set; }

    /// <summary>
    /// The graphics card's drawing, shared by every book, or null to draw on the processor alone - see <see cref="CardPictures"/>.
    /// </summary>
    public CardPictures? Card { get; set; }

    /// <summary>How much of the colour's mean the grey keeps. See <see cref="PictureGrey.Measured"/>.</summary>
    public float GreyFactor { get; set; } = PictureGrey.Measured;

    /// <summary>
    /// Which side of the model the black rim the game's icons have is drawn on.
    /// </summary>
    /// <remarks>
    /// OUTWARDS by default, which is the side a rendered model can afford - see
    /// <see cref="PictureOutline"/>. Both are offered because they lose different things, and
    /// the switch is in the preview window rather than out here: which one is right depends on
    /// what the icon looks like at 64 pixels, so it belongs where that is on screen.
    /// </remarks>
    public OutlineSide Outline { get; set; } = OutlineSide.Outward;

    /// <summary>How wide that rim is, in CELL pixels. Zero draws none. See the same type.</summary>
    public float OutlineWidth { get; set; } = PictureOutline.Measured;

    /// <summary>
    /// Whether an exported icon is brought to the brightness the game's own icons are painted at.
    /// </summary>
    /// <remarks>
    /// ON, because a model is lit for standing in a dungeon and an icon is painted to be read
    /// at 64 pixels: measured against the art it has to sit beside, the first export of this
    /// came out at half the brightness of the game's boss icons. See <see cref="PictureLight"/>,
    /// which also explains why it is a curve rather than a multiply.
    /// </remarks>
    public bool Light { get; set; } = true;

    /// <summary>What brightness to bring it to. The game's own median - see the same type.</summary>
    public float LightTarget { get; set; } = PictureLight.Measured;

    /// <summary>What is painted behind the model.</summary>
    public ModelBackdrop Behind { get; set; } = ModelBackdrop.Viewport;

    /// <summary>
    /// The flat colour behind the model, when that is what is behind it.
    /// </summary>
    /// <remarks>
    /// Magenta, because a flat backdrop is for keying out by hand and the useful colour for
    /// that is the one no monster is made of. Nothing here depends on it - the picture's own
    /// background is already transparent and the export writes that - so this is a convenience
    /// for a screenshot rather than part of the pipeline.
    /// </remarks>
    public uint FlatBehind { get; set; } = 0xFFFF00FF;

    /// <summary>Why there is no picture, for the line that says so. Empty while there is one.</summary>
    public string Why { get; private set; } = string.Empty;

    /// <summary>Whether an animation is running. Stays as set across monsters, like the zoom does not.</summary>
    public bool Playing => _playing;

    /// <summary>
    /// The book this pane belongs to - "monster", "item", "tile", "effect" - which names its window and keeps the four apart.
    /// </summary>
    public string Book { get; init; } = "monster";

    /// <summary>Whether the model's own window is open - see <see cref="DrawWindow"/>.</summary>
    public bool WindowOpen { get; set; } = true;

    /// <summary>Told when the window is opened or closed, with the new state, for a book that keeps it.</summary>
    public Action<bool>? WindowChanged { get; set; }

    /// <summary>
    /// What the book's chosen row is, every frame it is chosen: starts its load when it changes, and opens the window then.
    /// </summary>
    /// <remarks>
    /// THE MODEL HAS A WINDOW OF ITS OWN, asked for from the live client: the picture shared its pane
    /// with every switch the book has, and what was left for it was a postage stamp. So the book says
    /// what to show and draws its own switches (<see cref="DrawExport"/>, <see cref="DrawFiles"/>), and
    /// the window draws the picture and what acts on the picture itself.
    ///
    /// A NEW CHOICE OPENS THE WINDOW AGAIN, closed or not - choosing a row is asking to see it. Closed,
    /// nothing is drawn, so a book browsed with the window shut costs no rendering; the model is still
    /// read, because the dump and the export work from it.
    /// </remarks>
    /// <param name="one">The monster to show, or null for none.</param>
    /// <param name="path">Its path, which is what tells one monster from another.</param>
    /// <param name="title">What the window's title calls it.</param>
    public void Show(MonsterVariety? one, string path, string title)
    {
        if (!Possible)
        {
            return;
        }

        if (!string.Equals(path, _asked, StringComparison.Ordinal))
        {
            _asked = path;
            if (path.Length > 0 && !WindowOpen)
            {
                WindowOpen = true;
                WindowChanged?.Invoke(true);
            }
        }

        _title = title ?? string.Empty;
        Wanted(one, path);
        Adopted();
        _agentSeen = _agentTicket;
    }

    /// <summary>The path last handed to <see cref="Show"/>, so only a new choice opens the window.</summary>
    private string _asked = string.Empty;

    /// <summary>What the window's title calls the model.</summary>
    private string _title = string.Empty;

    /// <summary>Whether the lines under the picture are unfolded - see <see cref="Status"/>.</summary>
    private bool _infoOpen;

    /// <summary>
    /// The model's own window: the picture, its animation row, the buttons on it, and its lines folded under it. Call every frame the book is drawn.
    /// </summary>
    /// <remarks>
    /// BEGUN FROM INSIDE THE BOOK'S DRAW, which ImGui allows - a Begin inside another window's makes a
    /// window of its own, the way the export preview already is one. So the window is there while its
    /// book is, and goes with it when another tab is chosen. Where it sits and how big it is are
    /// ImGui's to remember, by the id after the ###, as for every other window here.
    /// </remarks>
    public void DrawWindow()
    {
        // THE EXPORT'S PREVIEW IS A WINDOW OF ITS OWN TOO, and stays up whether or not this one is.
        Shots();
        if (!Possible || !WindowOpen || _wanted.Length == 0)
        {
            return;
        }

        // AT THE SCREEN'S RIGHT THE FIRST TIME, beside the tools window rather than over it; after
        // that wherever it was left.
        Vector2 screen = ImGui.GetIO().DisplaySize;
        ImGui.SetNextWindowPos(new Vector2(MathF.Max(0f, screen.X - 680f), 60f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(640f, 720f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(240f, 240f), new Vector2(float.MaxValue, float.MaxValue));
        bool open = true;
        string name = _title.Length > 0 ? _title : "model";
        if (_agentUnfold)
        {
            // THE AGENT LINK ASKED FOR THE PICTURE: a collapsed window draws none.
            ImGui.SetNextWindowCollapsed(false);
            _agentUnfold = false;
        }

        bool expanded = ImGui.Begin($"{name} - {Book} model###{Book}-model-window", ref open, ImGuiWindowFlags.NoFocusOnAppearing);
        if (expanded)
        {
            Vector2 room = ImGui.GetContentRegionAvail();
            Picture(room.X, room.Y);
        }

        ImGui.End();
        if (!open)
        {
            WindowOpen = false;
            WindowChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// The book's switch for the window - and what the model is doing while it builds or why there is none, which the book shows beside it.
    /// </summary>
    public void DrawOpener()
    {
        if (!DrawWindowSwitch())
        {
            return;
        }

        ImGui.SameLine();
        DrawState();
    }

    /// <summary>The window's switch alone. False, with the reason said, where there is no install to read a model from.</summary>
    public bool DrawWindowSwitch()
    {
        if (!Possible)
        {
            ImGui.TextDisabled("No install to read the model from.");
            return false;
        }

        bool open = WindowOpen;
        if (ImGui.Checkbox($"model window##{Book}-model-window", ref open))
        {
            WindowOpen = open;
            WindowChanged?.Invoke(open);
        }

        OverlayLayout.Hint("The model in a window of its own: drag to turn it, the wheel zooms, a double click puts it back."
            + " Choosing another row opens it again.");
        return true;
    }

    /// <summary>
    /// What the model is doing, in a few words - text only, for a line of information beside a name.
    /// </summary>
    /// <remarks>
    /// SHORT: the whole account is the window's "info". A reason can run to a paragraph, so it is cut
    /// and given whole on hover.
    /// </remarks>
    public void DrawState()
    {
        if (!Possible)
        {
            return;
        }

        if (_loading is { IsCompleted: false })
        {
            ImGui.TextDisabled(_progress is { } progress ? ImGuiText.Escape(progress.Said()) : "building the model…");
        }
        else if (!_model.Ready && Why.Length > 0)
        {
            ImGui.TextDisabled(ImGuiText.Escape(Why.Length > ShortWhy ? "no model: " + Why[..ShortWhy] + "…" : "no model: " + Why));
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(ImGuiText.Escape(Why));
            }
        }
        else if (_model.Ready)
        {
            ImGui.TextDisabled(string.Create(
                CultureInfo.InvariantCulture,
                $"ready · {ByteCount.Said(_model.Bytes)} in {_model.Files} file{(_model.Files == 1 ? string.Empty : "s")}"));
        }
        else
        {
            ImGui.TextDisabled("no model");
        }
    }

    /// <summary>Whether the files button has written something and says where on a line of its own - a bar under a list makes room for it.</summary>
    public bool HasFilesLine => _dumped.Length > 0;

    /// <summary>How much of a reason the state shows before cutting it - see <see cref="DrawState"/>.</summary>
    private const int ShortWhy = 60;

    /// <summary>Takes a finished load, whether or not the window is open to draw it - the book's dump and export work from it.</summary>
    private void Adopted()
    {
        if (_loading is not { IsCompleted: true } done)
        {
            return;
        }

        _model = done.IsCompletedSuccessfully
            ? done.Result

            // A FAULTED TASK IS NOT A CRASH HERE. MonsterModels answers with a reason rather
            // than throwing, so reaching this means something underneath it did - and the
            // window says so instead of the session ending on the next frame.
            : MonsterModel.None with { Why = Said(done.Exception) };

        Why = _model.Why;
        _loading = null;
        _shown = string.Empty;
        _drawnView = default;
        _built = _progress is { } progress ? [.. progress.Report()] : [];
        _pictureDue = _model.Ready;
        _cardDue = _model.Ready;
        Rigged();
        UnpackBehind();
        ParticlesBehind();
        Said();
    }

    /// <summary>Where the time of the model shown went, for the capture - see ModelProgress.Report and <see cref="Pictured"/>.</summary>
    private List<string> _built = [];

    /// <summary>The pane's last stage, kept for the next monster set on it - see StageKeep.</summary>
    private readonly StageKeep _stageKeep = new();

    /// <summary>When the model shown was asked for, as Stopwatch counts.</summary>
    private long _chosenAt;

    /// <summary>Whether the model landed and its first picture is still to be drawn - see <see cref="Pictured"/>.</summary>
    private bool _pictureDue;

    /// <summary>Whether the model landed and the card has not drawn it yet - see <see cref="Pictured"/>.</summary>
    private bool _cardDue;

    /// <summary>
    /// The first picture of a model that landed: how long it took to draw - its upload to the card with it - and how long from the
    /// model being asked for until it was shown, which is the wait the person at the pane has.
    /// </summary>
    private void Pictured(long began)
    {
        // THE CARD'S FIRST PICTURE TOO, where the first was the processor's: until every material's shader has compiled for the card
        // the processor draws, and how long that takes is what compiling the shaders on every worker is for.
        if (_cardDue && _onCard)
        {
            _cardDue = false;
            if (!_pictureDue)
            {
                _built.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"first picture on the card: shown {(double)(Stopwatch.GetTimestamp() - _chosenAt) / Stopwatch.Frequency:0.000} s after the model was asked for, its shaders compiled"));
            }
        }

        if (!_pictureDue)
        {
            return;
        }

        _pictureDue = false;
        long now = Stopwatch.GetTimestamp();
        _built.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"first picture: drawn on the {(_onCard ? "card" : "processor")} in {(double)(now - began) / Stopwatch.Frequency:0.000} s, shown {(double)(now - _chosenAt) / Stopwatch.Frequency:0.000} s after the model was asked for{(_onCard || _cardWhy.Length == 0 ? string.Empty : " - not on the card: " + _cardWhy)}"));
    }

    /// <summary>
    /// The picture and what acts on it, fitted to the room it is given.
    /// </summary>
    /// <param name="wide">How wide the room is.</param>
    /// <param name="tall">How tall it is. The picture is square and fits inside both, with its row and its lines.</param>
    private void Picture(float wide, float tall)
    {
        Turned();
        Flown();

        // THE SIZE IS SETTLED BEFORE THE PICTURE IS TAKEN, because what it is drawn at follows
        // what it will be shown at - and that is the pane less what its row and its lines take,
        // so that nothing under the picture is pushed off the bottom and into a scrollbar. The
        // lines are known before they are drawn, so their height is exact, wrapping included.
        // TO THE RENDERER'S OWN LIMIT AND NOT TO THE CAP. The cap says what MOVING may cost; it
        // said nothing about how big the picture may be, and clamping the shown size to it was a
        // picture that stopped growing with its pane however far the boundary was dragged.
        Lines();
        float side = Math.Clamp(MathF.Min(wide, tall - Chrome(wide)), 64f, MeshPicture.Widest);
        Finished(side);

        // THE CONTROLS COME BEFORE THE QUESTION OF WHETHER THERE IS A PICTURE, so that a picture
        // which would not upload - it happened, at the rung Pause returns to - leaves the button
        // that plays it again, under the cap, and not only the reason. They draw nothing while
        // there is no skeleton to control, which is every frame the model is still loading.
        Controls(side);

        if (_texture == IntPtr.Zero)
        {
            if (_loading is { IsCompleted: false })
            {
                Building(wide);
            }
            else
            {
                ImGui.TextDisabled(Why.Length > 0 ? ImGuiText.Escape(Why) : "no model");
            }

            if (_cost.Length > 0)
            {
                ImGui.TextDisabled(_cost);
            }

            return;
        }

        // A BUTTON WITH THE PICTURE PAINTED INTO IT, AND NOT ImGui.Image. An image is an item with
        // NO ID, and ImGui only hands the hover to an item that has one - imgui.cpp's ItemHoverable
        // reads "if (id != 0) SetHoveredID(id)". Two bugs came out of that one fact, and both were
        // reported from the live client:
        //
        //   - IsItemActive compares g.ActiveId against the item's id, so after an image it is
        //     NEVER true and the drag below was unreachable code that read as though it worked.
        //   - With the hover left unclaimed, the sections drawn AFTER this took the click instead.
        //     OverlayLayout.Subsection passes SpanAvailWidth, so a header's hit box runs the whole
        //     width of the pane and straight across the picture: trying to turn the monster
        //     collapsed "Type" instead. A claimed id is also what fixes that, through the guard
        //     "if (g.HoveredId != 0 && g.HoveredId != id) return false" the headers then meet.
        //
        // ALLOWED TO BE OVERLAPPED, because two widgets sit on top of it. ImGui's rule for that
        // (ItemHoverable, "AllowOverlap mode requires previous frame HoveredId to be null or to
        // match") is that the LATER item wins the pointer, a frame late - so the button drawn
        // after the picture takes the click that would otherwise have started a drag.
        ImGui.SetNextItemAllowOverlap();

        // BOTH BUTTONS, so a right drag holds the picture as a left one does: it pans.
        ImGui.InvisibleButton(Grip, new Vector2(side, side), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        Vector2 below = ImGui.GetCursorScreenPos();

        // THE FLOOR GOES UNDER THE PICTURE OR OVER IT BY WHICH SIDE OF IT THE EYE IS ON - see
        // ModelFloor.Under. From the camera the picture was DRAWN with, not the one being dragged
        // towards: the drag and the wheel below move the camera after this frame's picture was
        // taken, and a floor a frame ahead of its model would slide under the feet on every turn.
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 corner = ImGui.GetItemRectMin();
        Paint(draw, corner, ImGui.GetItemRectMax());
        MeshPicture.Camera camera = MeshPicture.Camera.Of(_model.Mesh, _drawnView);
        bool under = ModelFloor.Under(camera);
        if (under)
        {
            Floor(draw, corner, side, camera);
        }

        draw.AddImage(_texture, corner, ImGui.GetItemRectMax());
        if (!under)
        {
            Floor(draw, corner, side, camera);
        }

        // HELD RATHER THAN HOVERED, so the model keeps turning when the drag runs off the edge of
        // it - which it does constantly, because the picture is small and a full turn is 360 px.
        // NOT WHILE ORBITING, by request: the orbit is stopped by its button and by nothing else,
        // so a hand on the picture does nothing until it is.
        // THE LEFT BUTTON TURNS ABOUT THE FOCUS, the right one pans across the picture (asked for from the live client, 2026-10-28):
        // a turn after zooming in on something turns about that something, not about the room's middle - see CameraPose.
        bool held = ImGui.IsItemActive() && !_orbiting;
        if (held && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && _view.Valid)
        {
            Vector2 moved = ImGui.GetIO().MouseDelta;
            if (Sun is { } sun && (_sunHeld || ImGui.GetIO().KeyShift))
            {
                _sunHeld = true;
                MoveSun(sun, moved, camera);
            }
            else
            {
                _view = _view.Orbited(-moved.X / Sweep * MathF.Tau, moved.Y / Sweep * MathF.Tau);
            }
        }
        else if (held && ImGui.IsMouseDragging(ImGuiMouseButton.Right) && _view.Valid && side > 0f)
        {
            _view = _view.Panned(ImGui.GetIO().MouseDelta / side);
        }

        // LET GO, the sun stays where it was put and its shadows are drawn sharp again.
        if (!held && _sunHeld)
        {
            _sunHeld = false;
            if (Sun?.Free is { } put)
            {
                Sun.Shine(put, moving: false);
            }
        }

        _held = held;
        SunMark(draw, corner, side, camera);
        FocusMark(draw, corner, side, camera);

        // A CLICK, NOT A DRAG, PROBES THE PIXEL UNDER IT while probing - see PictureProbe. As a share
        // of the picture, because the next drawing may be at another rung than this one.
        if (_probing && ImGui.IsItemDeactivated() && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
            && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left) == Vector2.Zero && side > 0f)
        {
            _probeAt = (ImGui.GetMousePos() - corner) / side;
            _probeAsked = true;
        }

        // EVERYTHING THAT ASKS ABOUT "THE ITEM" ASKS BEFORE THE NEXT ITEM IS SUBMITTED, because
        // ImGui answers for the last item and that would then be the orbit button: the wheel's
        // owner is claimed on the picture, not on a button in its corner.
        _hovered = ImGui.IsItemHovered();
        if (_hovered)
        {
            Wheel(side);

            // A DOUBLE CLICK LOOKS AT THE POINT UNDER IT - the nearest surface its ray meets - and turns about it from then on.
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && _view.Valid && side > 0f
                && Picked((ImGui.GetMousePos() - corner) / side) is { } point)
            {
                _view = _view.LookingAt(point);
            }
        }

        Overlays(corner, side);

        // THE RATE ON THE PICTURE where there is no animation row to carry it - a tile, a room, an
        // item, a monster without a rig. Those are exactly the pictures the doodad cap and the size
        // ladder are set for, and the row only exists where there is something to play.
        if (_pose is null || !_model.Moves)
        {
            Rate(draw, corner);
        }

        ImGui.SetCursorScreenPos(below);
        Status();
        Probed();
    }

    /// <summary>The redraw rate as the pane shows it, made again only when the number changes - it is asked every frame.</summary>
    private string RateText()
    {
        int now = (int)Math.Round(_rate.At(ImGui.GetTime()));
        bool card = Card is not null && _onCard;
        if (now != _rateShown || card != _rateCard || _rateText.Length == 0)
        {
            _rateShown = now;
            _rateCard = card;

            // WHICH OF THE TWO DREW THE PICTURE SHOWN, beside how often - a glance says both.
            _rateText = now.ToString(CultureInfo.InvariantCulture) + " fps" + (Card is null ? string.Empty : card ? " card" : " cpu");
        }

        return _rateText;
    }

    private int _rateShown = -1;
    private bool _rateCard;
    private string _rateText = string.Empty;

    /// <summary>
    /// The redraw rate in the picture's top left corner, over a shadow so it reads on any backdrop.
    /// </summary>
    /// <remarks>
    /// The top right holds the orbit button and the middle the animation's progress, so the left
    /// corner is the one free. Drawn, not laid out, so it takes no line from the pane.
    /// </remarks>
    private void Rate(ImDrawListPtr draw, Vector2 corner)
    {
        float inset = ImGui.GetStyle().ItemSpacing.X;
        string rate = RateText();
        var at = new Vector2(corner.X + inset, corner.Y + inset);
        draw.AddText(at + Vector2.One, RateShadow, rate);
        draw.AddText(at, ImGui.GetColorU32(ImGuiCol.TextDisabled), rate);
    }

    /// <summary>The rate's shadow: black, mostly opaque.</summary>
    private const uint RateShadow = 0xC0000000;

    /// <summary>
    /// What the load is doing while it runs: the step, how far into it, for how long - and a bar for the step.
    /// </summary>
    /// <remarks>
    /// "READING THE MODEL" SAID ONE THING FOR SECONDS, and a room takes seconds: whether it was still
    /// working or had quietly failed was a guess, and "reading" was not even what it was doing for most
    /// of them - it is building. The seconds move every frame whatever the step, so a stalled build and
    /// a slow one look different; the bar follows the step the build is on - see ModelProgress.
    /// </remarks>
    private void Building(float wide)
    {
        ModelProgress? progress = _progress;
        ImGui.TextDisabled(progress is null ? "building the model…" : ImGuiText.Escape(progress.Said()));
        if (progress is { Total: > 0 })
        {
            ImGui.ProgressBar(progress.Fraction, new Vector2(Math.Min(wide, ImGui.GetContentRegionAvail().X), 0f), string.Empty);
        }
    }

    /// <summary>
    /// The probe switched: while it is on, a click on the picture lists what that pixel is made of and how the translucent layers came out over that drawing.
    /// </summary>
    /// <remarks>
    /// THE CLICK'S DRAWING IS THE PROCESSOR'S, the one that records fragments and counts layers; every other is the card's
    /// where it can draw (see OnCard), so the lines stand for the last click while the picture moves on.
    ///
    /// A DIAGNOSIS ASKED FOR, NOT A FEATURE LEFT RUNNING: off, a drawing does not look at either. See
    /// PictureProbe for what it answers, and MeshPicture's PixelProbe for how it is recorded.
    /// </remarks>
    private void ProbeToggled()
    {
        _probing = !_probing;
        _probeAt = null;
        _probeLines = [];
        _probedShapes = [];
        _probeCopied = false;
        _layerLines = [];

        // DRAWN AGAIN AT ONCE, so the layers' lines are there without waiting for a turn.
        _probeAsked = _probing;
    }

    /// <summary>The probe's lines under the picture - see <see cref="ProbeToggled"/>; the capture key writes them, see <see cref="ProbeReport"/>.</summary>
    /// <remarks>
    /// IN A BOX OF ITS OWN HEIGHT, scrolled inside it: the picture is sized round it once, when the probe is switched on
    /// (see Chrome), and does not jump with every click as one fragment or eight come back.
    /// </remarks>
    private void Probed()
    {
        if (!_probing)
        {
            return;
        }

        if (ImGui.BeginChild("##monster-probe", new Vector2(0f, ProbeHeight()), ImGuiChildFlags.Borders))
        {
            if (!HasProbe)
            {
                ImGuiText.Wrapped(OverlayInk.Quiet, "probe: click the picture to see what that pixel is made of - its lines go to the clipboard too");
            }
            else
            {
                if (_probeCopied)
                {
                    ImGuiText.Wrapped(OverlayInk.Quiet, "copied to the clipboard - paste it anywhere");
                }

                foreach (string line in _probeLines)
                {
                    ImGuiText.Wrapped(OverlayInk.Quiet, ImGuiText.Escape(line));
                }

                foreach (string line in _layerLines)
                {
                    ImGuiText.Wrapped(OverlayInk.Quiet, ImGuiText.Escape(line));
                }
            }
        }

        ImGui.EndChild();
    }

    /// <summary>How many lines the probe's box shows before it scrolls.</summary>
    private const int ProbeRows = 8;

    /// <summary>The probe's box: its rows and its padding.</summary>
    private static float ProbeHeight() => (ImGui.GetTextLineHeightWithSpacing() * ProbeRows) + (ImGui.GetStyle().WindowPadding.Y * 2f);

    /// <summary>The probe's lines as they stand - the pixel's surfaces, then the layers' counts - for the capture key; null while the probe is off, has not been clicked, or is about another model.</summary>
    public string? ProbeReport() => HasProbe ? string.Join('\n', _probeLines.Concat(_layerLines)) : null;

    /// <summary>
    /// The model shown and the lines under it, for the capture key's model-panes part - null where nothing is shown. See CapturePanes.
    /// </summary>
    /// <remarks>The lines as the info fold has them, the escape ImGui needs for a percent sign taken off again.</remarks>
    public CapturePane? PaneReport()
    {
        if (!_model.Ready)
        {
            return null;
        }

        // THE LIGHT A CAPTURE HOLDS THE STAGE AGAINST THE GAME UNDER - the panel's, built whether or not the pane draws under it now - and the
        // pose the pane stands at, copied: the parts that draw it again through the game's camera draw what the pane shows (CaptureAtGame).
        (SceneLight? light, IReadOnlyList<string> said) = SceneMeasured?.Invoke(_model) ?? (null, []);

        // WITH THE LIGHTS HE CARRIES where the pose the pane stands at puts them - laid afresh, the pane's own laying left as it is.
        light = light is null ? null : Carried(light, _tracks is { Ready: true } && _pose is not null);
        Vector3[]? positions = null, normals = null;
        if (_tracks is { Ready: true } && _pose is not null)
        {
            CpuPosed();
            if (_posed.Length == _model.Mesh.Positions.Length)
            {
                positions = [.. _posed];
                normals = [.. _posedNormals];
            }
        }

        // THE PROGRAMS AND THE WATER FOLLOW THE LIGHT HANDED OVER, not the one the pane draws under: with "game light" unticked the pane's
        // lists know nothing of the light's spray twins or way in, and the capture draws as the measured light says.
        SceneLight? under = light ?? Lit();
        return new CapturePane(Book, [.. _status.Select(line => line.Replace("%%", "%", StringComparison.Ordinal))], _model)
        {
            Built = [.. _built],
            Probed = HasProbe ? _probedShapes : [],
            Shades = ShadesOf(_model, under),
            View = _drawnView.Valid ? _drawnView : null,
            Variety = _variety,
            Path = _wanted,
            Unpack = _unpack,
            Light = light,
            LightOn = Lit() is not null,
            LightSaid = said,
            Dust = Dusted(),
            Water = Watered(under),
            Clock = _clock,
            Ink = Ink,
            Blends = Unfaded(),
            Positions = positions,
            Normals = normals,
            Particles = _onCard ? Card?.Gpu?.ParticleState() ?? [] : [],
        };
    }

    /// <summary>
    /// The light a capture holds the pane's stage against the game under, and what the light panel says of it - see SceneLightPanel.Measured; null
    /// where the book has no panel.
    /// </summary>
    public Func<MonsterModel, (SceneLight? Light, IReadOnlyList<string> Said)>? SceneMeasured { get; set; }

    /// <summary>The shapes under the last probe click, nearest first - see <see cref="PaneReport"/>.</summary>
    private IReadOnlyList<int> _probedShapes = [];

    /// <summary>Whether the probe has lines about the model shown - the frame's own question, asked without building the text.</summary>
    private bool HasProbe => _probing && ReferenceEquals(_probedModel, _model) && _probeLines.Count + _layerLines.Count > 0;

    /// <summary>Whether the probe is on - see <see cref="ProbeToggled"/>.</summary>
    private bool _probing;

    /// <summary>Where the last click probed, as a share of the picture from its top left, or null.</summary>
    private Vector2? _probeAt;

    /// <summary>Whether the next frame draws the picture whole for the probe, whether or not anything moved.</summary>
    private bool _probeAsked;

    /// <summary>What the drawings count the translucent layers into, while probing. One for the pane, reused.</summary>
    private readonly LayerTally _tally = new();

    /// <summary>The model the probe's lines are about, so a new one does not show the last one's.</summary>
    private MonsterModel? _probedModel;

    private IReadOnlyList<string> _probeLines = [];
    private IReadOnlyList<string> _layerLines = [];

    /// <summary>The room the rate is given in the animation row, three digits wide, so it does not shuffle as the number changes.</summary>
    private const string RateRoom = "999 fps card";

    /// <summary>
    /// Carries the camera on one step of its lap, where the orbit is running.
    /// </summary>
    /// <remarks>
    /// BEFORE THIS FRAME'S PICTURE IS TAKEN, unlike the drag, which moves it after: a drag is
    /// read off the picture, so it cannot come first, and being a frame behind a hand is
    /// invisible. The orbit has no such reason, and a picture drawn for the frame before would
    /// have the floor - worked out from the camera the picture was drawn with - trailing the
    /// turn by a frame too, for no gain.
    /// </remarks>
    private void Turned()
    {
        if (!_orbiting)
        {
            return;
        }

        // KEPT WITHIN ONE LAP by CameraPose.Orbited, so an orbit left running for an evening does not walk the angle out to where a
        // float can no longer tell one frame from the next.
        if (_view.Valid)
        {
            _view = _view.Orbited(-Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.25f) / Orbit * MathF.Tau, 0f);
        }
    }

    /// <summary>
    /// Draws the model as a turning picture and nothing else.
    /// </summary>
    /// <remarks>
    /// THE SAME MODEL PANE WITH EVERYTHING AROUND IT TAKEN OFF, for somewhere the picture is a
    /// FACT ABOUT THE ROW rather than the thing being worked on - the entity browser's page for
    /// a monster, where the question is "what am I looking at" and the answer is worth a
    /// hundred and eighty pixels. No animation picker, no export, no greying slider, and no
    /// drag or wheel: a pane that turns by itself and costs the reader no decisions.
    ///
    /// THE ORBIT IS ON AND HAS NO BUTTON, which is the whole difference from <see cref="Draw"/>.
    /// There is nothing here to stop it for - a still picture of a monster from one angle is
    /// what a sprite would have been - and a control that only ever gets pressed by accident is
    /// worse than none.
    ///
    /// NO FLOOR EITHER. <see cref="ModelFloor"/> earns its lines on a pane somebody is placing a
    /// camera in, where they say which way is which; at this size they are a grid over a thumbnail.
    ///
    /// ITS OWN INSTANCE, NOT THE BOOK'S. Two panes sharing one of these would each reload the
    /// model on every switch between them - six to sixteen megabytes of bundle reads to show a
    /// picture that was on screen a moment ago - so the composition root builds a second, with a
    /// <see cref="PictureLadder"/> capped small because this one is never drawn big.
    /// </remarks>
    /// <param name="one">The monster to show, or null for none.</param>
    /// <param name="path">Its path, which is what tells one monster from another.</param>
    /// <param name="wide">How wide the strip is.</param>
    /// <param name="tall">How tall it is. The picture is square and fits inside both.</param>
    public void Circling(MonsterVariety? one, string path, float wide, float tall)
    {
        if (!Possible)
        {
            return;
        }

        Wanted(one, path);
        _orbiting = true;
        Turned();

        float side = Math.Clamp(MathF.Min(wide, tall), Least, MeshPicture.Widest);
        Finished(side);

        if (_texture == IntPtr.Zero)
        {
            // SHORTER THAN THE BOOK'S, and deliberately so: this pane is a line of a page about
            // something else, and a model that will not read must not push the components down
            // the screen with a paragraph. The Monster Book is where the reason is spelled out.
            ImGui.TextDisabled(_loading is { IsCompleted: false } ? "building the model…" : "no model");
            return;
        }

        // A DUMMY AND NOT AN INVISIBLE BUTTON, because nothing here takes the pointer: the
        // picture is not draggable, and claiming an id would only take the click away from
        // whatever section header runs across it.
        ImGui.Dummy(new Vector2(side, side));

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 corner = ImGui.GetItemRectMin();
        Paint(draw, corner, ImGui.GetItemRectMax());
        draw.AddImage(_texture, corner, ImGui.GetItemRectMax());

        // THE SAME RATE AS THE BOOK'S PANE, since this one turns all the time and is where a slow
        // machine shows first.
        Rate(draw, corner);
    }

    /// <summary>The smallest a turning picture is drawn. Below this a monster is a smudge.</summary>
    private const float Least = 48f;

    /// <summary>
    /// The row above the picture: which animation, of how many, with the pane's own frame rate at the right.
    /// </summary>
    /// <remarks>
    /// ONLY WHERE THERE IS SOMETHING TO PLAY. A monster with no skeleton, or one whose keyframes
    /// this machine cannot unpack, gets no controls at all and the reason in a line under the
    /// picture - a combo box listing nothing would look like the tool had broken rather than the
    /// file lacking.
    ///
    /// THE LAYOUT IS THE ONE ASKED FOR, item by item: a label, a combo only as wide as its longest
    /// name, the count after it, all at the left; and at the right edge of the picture the rate.
    /// The rate's width is reserved for three digits so it does not shuffle as the number changes,
    /// and it never runs back over the left-hand three on a narrow pane - it goes past the
    /// picture's edge instead, where it can still be read. Pause sat after the rate once; it is
    /// the clock button in the column down the picture's edge now, with the other switches.
    /// </remarks>
    private void Controls(float side)
    {
        if (_pose is null || !_model.Moves)
        {
            return;
        }

        IReadOnlyList<SkeletonAnimation> moves = _model.Rig.Animations;
        string current = _chosen >= 0 && _chosen < moves.Count ? moves[_chosen].Name : string.Empty;

        ImGuiStylePtr style = ImGui.GetStyle();
        Vector2 row = ImGui.GetCursorScreenPos();

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("animation");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ComboWidth(moves, style));
        if (ImGui.BeginCombo("##monster-animation", ImGuiText.Escape(current)))
        {
            for (var one = 0; one < moves.Count; one++)
            {
                bool picked = one == _chosen;
                if (ImGui.Selectable(ImGuiText.Escape(moves[one].Name) + "##" + one, picked))
                {
                    Choose(one);
                }

                // Opens scrolled to the one that is playing rather than to the top, which on a
                // rig with two hundred animations is the difference between a glance and a hunt.
                if (picked)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(_count);

        float reserved = ImGui.CalcTextSize(RateRoom).X;
        ImGui.SameLine();
        float left = MathF.Max(ImGui.GetCursorScreenPos().X, row.X + side - reserved);

        string rate = RateText();
        ImGui.SetCursorScreenPos(new Vector2(left + reserved - ImGui.CalcTextSize(rate).X, row.Y));
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(rate);
    }

    /// <summary>The combo's width: its longest name, the frame's padding and the arrow.</summary>
    private float ComboWidth(IReadOnlyList<SkeletonAnimation> moves, ImGuiStylePtr style)
    {
        float font = ImGui.GetFontSize();
        if (_comboFont != font)
        {
            var longest = 0f;
            foreach (SkeletonAnimation move in moves)
            {
                longest = MathF.Max(longest, ImGui.CalcTextSize(move.Name).X);
            }

            // The arrow is a square of the frame's height - BeginCombo's own arrow_size.
            _comboWide = longest + (style.FramePadding.X * 2f) + ImGui.GetFrameHeight();
            _comboFont = font;
        }

        return _comboWide;
    }

    /// <summary>
    /// Moves the sun by a drag: up raises it, across turns it round so that its mark follows the hand on the picture.
    /// </summary>
    /// <remarks>
    /// WHICH WAY ROUND FOLLOWS THE HAND depends on how the picture is turned, so it is measured rather
    /// than reasoned: the mark is put through the camera a little each way round, and the drag goes the
    /// way that moves it the same way as the pointer.
    /// </remarks>
    private void MoveSun(IMovableSun sun, Vector2 moved, MeshPicture.Camera camera)
    {
        Vector3 travels = sun.Free ?? _drawnLight?.SunDirection ?? SceneLight.SunToward(0f, MathF.PI / 4f);
        (float round, float height) = SceneLight.SunStands(travels);
        height = Math.Clamp(height - (moved.Y / Sweep * MathF.PI), 0.02f, MathF.PI / 2f);
        if (moved.X != 0f && camera.Ready)
        {
            // BY THE WAY ITSELF ACROSS THE VIEW, not by a point put through the camera: in perspective such a point may stand behind
            // the eye, while the view's x of the way moves as the mark does on either view.
            const float step = 0.01f;
            float ahead = Vector3.TransformNormal(-SceneLight.SunToward(round + step, height), camera.View).X;
            float behind = Vector3.TransformNormal(-SceneLight.SunToward(round - step, height), camera.View).X;
            round += (ahead >= behind ? 1f : -1f) * moved.X / Sweep * MathF.Tau;
        }

        sun.Shine(SceneLight.SunToward(round, height), moving: true);
    }

    /// <summary>
    /// The hand-set sun's mark: a disc where the sun stands beyond the model, rays round it, and a line to the model's middle - faint where it stands behind the model.
    /// </summary>
    private void SunMark(ImDrawListPtr draw, Vector2 corner, float side, MeshPicture.Camera camera)
    {
        if (Sun?.Free is not { } travels || !camera.Ready || side <= 0f)
        {
            return;
        }

        (Vector3 centre, float reach) = Reach();
        Vector3 middle = camera.Place(centre);
        float beyond = reach * 1.15f;
        if (camera.Perspective)
        {
            // IN PERSPECTIVE THE MIDDLE MAY BE BEHIND THE EYE - the mark then stands alone - and a sun on the eye's side is put no
            // nearer than half way to it, where it still lands on the picture.
            if (!camera.InFront(middle))
            {
                return;
            }

            float towards = -Vector3.TransformNormal(-travels, camera.View).Z;
            if (towards > 0f)
            {
                beyond = MathF.Min(beyond, (middle.Z - camera.Near) * 0.5f / towards);
            }
        }

        Vector3 at = camera.Place(centre - (travels * beyond));
        if (!float.IsFinite(at.X) || !float.IsFinite(at.Y))
        {
            return;
        }

        float inset = ImGui.GetFontSize();
        var from = corner + (new Vector2(middle.X, middle.Y) * side);
        var to = corner + new Vector2(Math.Clamp(at.X * side, inset, side - inset), Math.Clamp(at.Y * side, inset, side - inset));
        bool behind = at.Z > middle.Z;
        Vector4 ink = behind ? SunInk with { W = 0.45f } : SunInk;
        uint colour = ImGui.GetColorU32(ink);
        draw.AddLine(from, to, ImGui.GetColorU32(ink with { W = ink.W * 0.5f }), 1.5f);
        float radius = ImGui.GetFontSize() * SunDisc;
        draw.AddCircleFilled(to, radius, colour);
        for (var ray = 0; ray < 8; ray++)
        {
            float angle = ray * MathF.PI / 4f;
            var way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            draw.AddLine(to + (way * radius * 1.4f), to + (way * radius * 2.1f), colour, 1.5f);
        }
    }

    /// <summary>
    /// The focus marker: three short arms through the point the camera turns about, along x (red), y (green) and up (blue), and a dot
    /// where they cross - drawn through the picture's own camera, so it moves with the focus and stands in the scene's perspective.
    /// </summary>
    /// <remarks>
    /// ASKED FOR FROM THE LIVE CLIENT (v0.1.380): on a stage the camera's focus stands for the player wherever the engine reads the player's
    /// place, and the marker shows where that is. DRAWN OVER THE PICTURE rather than into it (CHOSEN), so a roof or a tree crown does not
    /// hide it; its arms a fortieth of the camera's distance (CHOSEN) - 54 units at the game's view, a few at a barrel - so it stays small at
    /// every zoom.
    /// </remarks>
    private void FocusMark(ImDrawListPtr draw, Vector2 corner, float side, MeshPicture.Camera camera)
    {
        CameraPose pose = _drawnView.Valid ? _drawnView : _view;
        if (!_focusShown || !pose.Valid || !camera.Ready || side <= 0f)
        {
            return;
        }

        Vector3 centre = camera.Place(pose.Focus);
        if (camera.Perspective && !camera.InFront(centre))
        {
            return;
        }

        float reach = MathF.Max(0.5f, pose.Distance * FocusReach);
        var at = corner + (new Vector2(centre.X, centre.Y) * side);
        ReadOnlySpan<(Vector3 Way, Vector4 Ink)> arms =
        [
            (Vector3.UnitX, new Vector4(1f, 0.35f, 0.3f, 1f)),
            (Vector3.UnitY, new Vector4(0.4f, 1f, 0.4f, 1f)),
            (-Vector3.UnitZ, new Vector4(0.45f, 0.65f, 1f, 1f)),
        ];
        foreach ((Vector3 way, Vector4 ink) in arms)
        {
            Vector3 one = camera.Place(pose.Focus + (way * reach)), other = camera.Place(pose.Focus - (way * reach));
            if (camera.Perspective && (!camera.InFront(one) || !camera.InFront(other)))
            {
                continue;
            }

            draw.AddLine(corner + (new Vector2(other.X, other.Y) * side), corner + (new Vector2(one.X, one.Y) * side), ImGui.GetColorU32(ink), 2f);
        }

        float radius = ImGui.GetFontSize() * 0.22f;
        draw.AddCircleFilled(at, radius + 1.5f, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.8f)));
        draw.AddCircleFilled(at, radius, ImGui.GetColorU32(OverlayInk.Accent));
    }

    /// <summary>How long the focus marker's arms are, as a share of the camera's distance - see <see cref="FocusMark"/>.</summary>
    private const float FocusReach = 0.025f;

    /// <summary>The model's middle and how far its box reaches from it - where the sun's mark is set beyond.</summary>
    private (Vector3 Centre, float Reach) Reach()
    {
        Vector3 least = _model.Mesh.Least, most = _model.Mesh.Most;
        return ((least + most) * 0.5f, MathF.Max(1f, Vector3.Distance(least, most) * 0.5f));
    }

    /// <summary>
    /// The two widgets over the picture's top edge: the orbit button at the right, and the
    /// animation's progress in the middle, both the button's height.
    /// </summary>
    /// <remarks>
    /// SUBMITTED AFTER THE PICTURE, which is what gives the button the pointer over it - see the
    /// overlap remark in <see cref="Draw"/>. The bar is not an item anybody can press, so it
    /// claims nothing, and a drag that starts on it turns the model as if it were not there.
    ///
    /// THE BAR'S TEXT IS DRAWN BY HAND IN ITS MIDDLE. ImGui's own overlay rides along beside the
    /// end of the fill (ProgressBar: "fill_br.x + style.ItemSpacing.x"), which for a counter
    /// that runs through every frame of a walk is a number sliding across the bar; in the
    /// middle it holds still and reads as a counter.
    /// </remarks>
    private void Overlays(Vector2 corner, float side)
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        float inset = style.ItemSpacing.X;
        float top = corner.Y + inset;

        // THE TWO ARE SIZED APART AND SHARE ONLY THE LINE THEY HANG FROM. The button had to grow
        // to be recognisable at all - three frame heights by now - while the counter did not, and
        // a bar as tall as the button would be a slab across the top of the picture. So the bar
        // keeps the height it had, a frame and a button's padding, and only the top edges line up.
        float counter = ImGui.GetFrameHeight() + (style.FramePadding.Y * 2f);

        // THE COLUMN DOWN THE RIGHT EDGE, each button right-aligned on its own width so that the
        // words, where the art is missing, line up on the edge rather than on the left. The widest
        // is what the progress bar keeps clear of.
        float art = MathF.Round(ImGui.GetFrameHeight() * OrbitFaces);
        float widest = 0f;
        float hang = top;
        for (int which = 0; which < Sides; which++)
        {
            if (!Hung(which))
            {
                continue;
            }

            IntPtr icon = SideIcon(which, (int)art);
            bool drawn = icon != IntPtr.Zero;
            float wide = (drawn ? art : ImGui.CalcTextSize(SideWord(which)).X) + (style.FramePadding.X * 2f);
            widest = MathF.Max(widest, wide);
            ImGui.SetCursorScreenPos(new Vector2(corner.X + side - wide - inset, hang));

            if (drawn ? Pictured(which, icon, art) : Worded(which, wide, counter))
            {
                Pressed(which);
            }

            if (ImGui.IsItemHovered())
            {
                Tipped(which);
            }

            hang += (drawn ? art : counter) + style.ItemSpacing.Y;
        }

        if (_tracks is not { Ready: true } tracks || !(tracks.Frames > 0f))
        {
            return;
        }

        // Kept clear of the column at both ends - the bar is centred, so the room it may take is
        // twice the gap to the column. On any pane worth reading this is nowhere near binding.
        float span = MathF.Min(side * ProgressShare, ImGui.GetFontSize() * ProgressSpan);
        span = MathF.Min(span, side - (2f * (widest + (2f * inset))));
        if (!(span > 1f))
        {
            return;
        }

        var at = new Vector2(corner.X + ((side - span) * 0.5f), top);
        var size = new Vector2(span, counter);
        ImGui.SetCursorScreenPos(at);
        ImGui.ProgressBar(_frame / tracks.Frames, size, string.Empty);

        string said = $"{(int)_frame} / {(int)MathF.Ceiling(tracks.Frames)}";
        ImGui.GetWindowDrawList().AddText(
            at + ((size - ImGui.CalcTextSize(said)) * 0.5f), ImGui.GetColorU32(ImGuiCol.Text), said);
    }

    /// <summary>Whether an edge button has anything to switch this frame: the card's needs a card, the light's a specular colour, the clock's something that plays.</summary>
    private bool Hung(int which)
        => which switch
        {
            CardSide => Card is not null,
            LightSide => HasShine(),
            ClockSide => HasClock() || (_pose is not null && _model.Moves),
            _ => true,
        };

    /// <summary>
    /// An edge button itself: the art alone, with no frame of its own under it.
    /// </summary>
    /// <remarks>
    /// NOTHING LIT WHILE NOBODY IS POINTING AT IT, which is what was asked for: the button sat in
    /// a warm box over the picture, and a box is what the row of controls above wears, not
    /// something that belongs over a model. ImageButtonEx always calls RenderFrame, so the only
    /// way to be rid of the box is to hand it nothing to draw - the button's own colour goes
    /// transparent and the border width goes to zero. The HOVER AND THE PRESS STILL SHOW: those
    /// reach for ImGuiCol_ButtonHovered and ImGuiCol_ButtonActive, which are left alone, so the
    /// control still answers the pointer and only rests invisible.
    ///
    /// AND A SWITCH THROWN IS SHOWN BY TINTING THE ART rather than by a colour behind it. A
    /// background was the first attempt and it is the wrong mechanism here: the art is an OPAQUE
    /// black tile with a white drawing on it, so anything painted behind reaches only the four
    /// rounded corners. A tint multiplies instead, which leaves the black tile black and turns the
    /// drawing itself the accent - the one part of the picture anybody is looking at. See
    /// <see cref="Lit"/> for which state is the tinted one.
    /// </remarks>
    private bool Pictured(int which, IntPtr icon, float art)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        try
        {
            return ImGui.ImageButton(
                SideIds[which],
                icon,
                new Vector2(art),
                Vector2.Zero,
                Vector2.One,
                Vector4.Zero,
                Lit(which) ? OverlayInk.Accent : Vector4.One);
        }
        finally
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }
    }

    /// <summary>Whether an edge button's art is tinted: the switch is away from how the pane starts.</summary>
    /// <remarks>
    /// AWAY FROM THE START, NOT "ON", because the four do not share a sense of on. The orbit tinted
    /// while it runs and the probe while it listens read alike; a card tinted while it draws, which
    /// it does from the first frame, would be the one button lit on a pane nobody has touched. So
    /// the tint says "you changed this": the flat light, the processor, the running orbit, the probe,
    /// the paused clock.
    /// </remarks>
    private bool Lit(int which)
        => which switch
        {
            CardSide => Card is { Sharp: false },
            LightSide => FlatLight,
            OrbitSide => _orbiting,
            ClockSide => !_playing,
            ResetSide or GameSide => false,
            PerspectiveSide => _parallel,
            FocusSide => _focusShown,
            _ => _probing,
        };

    /// <summary>The same button where the art could not be loaded, which keeps its frame - a bare word is not a button.</summary>
    private bool Worded(int which, float wide, float tall)
        => ImGui.Button(SideWord(which) + SideIds[which], new Vector2(wide, tall));

    /// <summary>What an edge button says where it has no art: its state as a word, as the corner buttons said it.</summary>
    private string SideWord(int which)
        => which switch
        {
            CardSide => Card is { Sharp: true } ? "card: sharp" : "card: trilinear",
            LightSide => FlatLight ? "flat light" : "game light",
            OrbitSide => _orbiting ? "stop" : "orbit",
            ClockSide => _playing ? "Pause" : "Play",
            ResetSide => "reset view",
            GameSide => "game view",
            PerspectiveSide => _parallel ? "parallel" : "perspective",
            FocusSide => _focusShown ? "focus: shown" : "focus",
            _ => _probing ? "probe: on" : "probe",
        };

    /// <summary>What a press on an edge button does.</summary>
    private void Pressed(int which)
    {
        switch (which)
        {
            case CardSide when Card is { } card:
                card.Sharp = !card.Sharp;
                card.SharpChanged?.Invoke(card.Sharp);
                break;
            case LightSide:
                FlatLight = !FlatLight;
                FlatLightChanged?.Invoke(FlatLight);
                break;
            case OrbitSide:
                _orbiting = !_orbiting;
                break;
            case ProbeSide:
                ProbeToggled();
                break;
            case ClockSide:
                _playing = !_playing;
                break;
            case ResetSide when _model.Ready:
                _view = Started(_model.Mesh, reset: true);
                break;
            case GameSide when _view.Valid:
                // A STAGED MONSTER IS LOOKED AT AS THE GAME'S CAMERA LOOKS AT THE PLAYER - at the floor by his feet, GameAhead off them
                // (said from the live client: about the point looked at, he stood a few metres off where the game's camera has him).
                _view = _model.Standing is { } standing
                    ? _view.AsTheGameAt(new Vector3(standing.At, standing.Floor))
                    : _view.AsTheGame();
                break;
            case PerspectiveSide:
                _parallel = !_parallel;
                _view = _view with { Parallel = _parallel };
                break;
            case FocusSide:
                _focusShown = !_focusShown;
                break;
            default:
                break;
        }
    }

    /// <summary>The tip over an edge button: what the picture it is on will do, and the state the button is in.</summary>
    private void Tipped(int which)
    {
        switch (which)
        {
            case CardSide when Card is { } card:
                CardTip(card);
                break;
            case LightSide:
                ImGui.SetTooltip((FlatLight ? "Flat light on. " : "Game light on. ")
                    + "How a specular colour is lit - a metal's albedo is black, and its colour is all specular.\n"
                    + "Game light: the game's GGX lobe for the lamp and its environment term, under an environment as bright as the picture's ambient."
                    + " Reads the gloss texture and works out a lobe per pixel.\n"
                    + "Flat light: what the specular colour has past a dielectric's 0.04 is laid onto the albedo and shaded with it. Cheaper.");
                break;
            case OrbitSide:
                ImGui.SetTooltip(_orbiting
                    ? "Orbiting: the camera is carried round the model. Press to stop."
                    : "Orbit: carries the camera round the model until pressed again.");
                break;
            case ProbeSide:
                ImGui.SetTooltip((_probing ? "Debug probe on. " : "Debug probe. ")
                    + "Click the picture to list every surface under that pixel, nearest first: material, blend,\n"
                    + "what drew it, where it came from, and for a mixed layer how far in front of the solid behind it lies and its alpha.\n"
                    + "The click's drawing also counts how each translucent material came out over the whole picture.\n"
                    + "That one drawing is the processor's; the rest stay on the card. The capture key writes the lines to model-probe.txt.");
                break;
            case ClockSide:
                ImGui.SetTooltip(_playing
                    ? "Playing: the animation runs, or the picture runs with the game's clock. Press to pause."
                    : "Paused: the picture holds still at the pane's own size. Press to play.");
                break;
            case ResetSide:
                ImGui.SetTooltip("Reset view: the whole model in the picture again - a room or a stage at the game's angle, a monster from the front.\n"
                    + CameraHelp);
                break;
            case GameSide:
                ImGui.SetTooltip("Game view: the game's own camera - 48.7 degrees down along +x +y, 2,165 units off,\n"
                    + "30 degrees from the picture's top to its bottom (measured off the game's camera matrix).\n"
                    + "A monster on a stage is looked at as the game's camera looks at the player: at the floor by his feet,\n"
                    + "38 units back along x and y towards the eye (measured over 20 captures); anything else about the point looked at.\n" + CameraHelp);
                break;
            case PerspectiveSide:
                ImGui.SetTooltip((_parallel ? "Parallel: no perspective, as every picture was drawn before. " : "Perspective, as the game sees it. ")
                    + "Press to switch.\n" + CameraHelp);
                break;
            case FocusSide:
                ImGui.SetTooltip((_focusShown ? "Focus marker shown. " : "Focus marker. ")
                    + "A small mark where the camera's focus stands - the point it turns about - moving with it.\n"
                    + "On a stage there is no player, and the focus stands for him wherever the engine reads the player's place\n"
                    + "(the screen-space fog's disperse radius, the clouds' fade): in the game the camera looks at him. Press to show or hide.\n"
                    + CameraHelp);
                break;
            default:
                break;
        }
    }

    /// <summary>How the camera is moved - the lines under each camera button's tip.</summary>
    private const string CameraHelp =
        "Left drag: turn about the point looked at. Right drag: pan. Wheel: towards the point under the pointer.\n"
        + "Double click: look at the point under it, and turn about it from then on. Shift + left drag: the sun.\n"
        + "W/S: along the view, A/D: sideways, Q/E: down and up, Shift: four times as fast - while the pointer is over the picture.";

    /// <summary>
    /// Fits the camera to the model shown, when it changed - kept where the same stage is shown under another monster.
    /// </summary>
    /// <remarks>
    /// A STAGE KEPT ACROSS MONSTERS KEEPS ITS CAMERA: the room is the same room, and a view flown to the quay should not jump back to
    /// the whole harbour because another boss was picked. Anything else starts from its own fit (see <see cref="Started"/>). The area
    /// round you taken again in the same block is the same stage too, only the monster moved to where the player stood then
    /// (AreaBlock.Unstood) - its tiles are the ones kept.
    /// </remarks>
    private void Framed()
    {
        SkinnedMesh mesh = _model.Mesh;
        if (ReferenceEquals(mesh, _viewFor) || !mesh.Ready)
        {
            return;
        }

        bool kept = _view.Valid && _viewStaged && _model.Staged
            && string.Equals(AreaBlock.Unstood(_viewStage), AreaBlock.Unstood(_stagedOn), StringComparison.Ordinal);
        _viewFor = mesh;
        _viewStaged = _model.Staged;
        _viewStage = _stagedOn;
        if (!kept)
        {
            _view = Started(mesh, reset: false);
        }
    }

    /// <summary>
    /// Where a model starts: a room, a tile or a stage at the game's angle, a monster or an item turned as the last one was - or from
    /// the front, on a reset - each at the distance that fills the picture with its box.
    /// </summary>
    /// <remarks>ASSUMED FROM THE LIVE CLIENT'S ANSWERS and not objected to (2026-10-28): rooms and stages at the game's angle, a monster as before.</remarks>
    private CameraPose Started(SkinnedMesh mesh, bool reset)
    {
        bool scene = _model.Staged || _model.Kind is ModelKind.Room or ModelKind.Tile;
        if (scene)
        {
            return CameraPose.Fitted(mesh, CameraPose.GameTurn, CameraPose.GameTilt, _parallel);
        }

        bool turned = !reset && _view.Valid;
        return CameraPose.Fitted(mesh, turned ? _view.Turn : 0f, turned ? _view.Tilt : 0f, _parallel);
    }

    /// <summary>
    /// Flies the camera by the keys held while the pointer is over the picture - before this frame's picture is taken, as the orbit turns it.
    /// </summary>
    /// <remarks>
    /// ONLY OVER THE PICTURE AND NOT WHILE TEXT IS TYPED, so a W typed into a search box flies nothing. The pointer's place is last
    /// frame's - the picture is drawn after this - which a hand on a key never notices.
    /// </remarks>
    private void Flown()
    {
        _flying = false;
        if (!_hovered || !_view.Valid || ImGui.GetIO().WantTextInput)
        {
            return;
        }

        static float Held(ImGuiKey plus, ImGuiKey minus) => (ImGui.IsKeyDown(plus) ? 1f : 0f) - (ImGui.IsKeyDown(minus) ? 1f : 0f);
        Vector3 by = _view.Flight(
            Held(ImGuiKey.W, ImGuiKey.S), Held(ImGuiKey.D, ImGuiKey.A), Held(ImGuiKey.E, ImGuiKey.Q), ImGui.GetIO().DeltaTime, ImGui.GetIO().KeyShift);
        if (by != Vector3.Zero)
        {
            _view = _view.Flown(by);
            _flying = true;
        }
    }

    /// <summary>The nearest surface under a point of the picture, in the model's space - where its ray first meets the mesh as drawn - or null where it meets nothing.</summary>
    /// <param name="pointer">The point as a share of the picture's side from its top left corner.</param>
    private Vector3? Picked(Vector2 pointer)
    {
        if (!_view.Valid || !_model.Ready || !float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y))
        {
            return null;
        }

        (Vector3 from, Vector3 way) = _view.Ray(pointer);

        // THE POSE AS DRAWN, brought onto the processor for the pick alone where the card skinned it - see CpuPosed.
        bool posed = _drawnPosed && _pose is not null && _posed.Length == _model.Mesh.Positions.Length;
        if (posed)
        {
            CpuPosed();
        }

        Vector3[] positions = posed ? _posed : _model.Mesh.Positions;
        IReadOnlyList<MaterialBlend>? blends = Blends();
        IReadOnlyList<ShadeProgram?>? shades = ShadesOf(_model);
        bool Shown(int shape)
        {
            MaterialBlend blend = blends is not null && shape < blends.Count ? blends[shape] : MaterialBlend.Opaque;
            return blend is not (MaterialBlend.ShadowOnly or MaterialBlend.Hidden or MaterialBlend.ShadowCut)
                && !(blend == MaterialBlend.Cutout && shades is not null && shape < shades.Count && shades[shape] is { } cutting && cutting.CutAway(cutting.Cut));
        }

        return MeshPicture.Pick(_model.Mesh, positions, from, way, out float away, shown: Shown) ? from + (way * away) : null;
    }

    /// <summary>An edge button's art, uploaded at the size it is drawn, or nothing where the resource is not there.</summary>
    private IntPtr SideIcon(int which, int pixels)
    {
        if (_side[which] != IntPtr.Zero && _sidePixels[which] == pixels)
        {
            return _side[which];
        }

        if (_sideMissing[which] || pixels <= 0)
        {
            return IntPtr.Zero;
        }

        if (_side[which] != IntPtr.Zero)
        {
            _release?.Invoke(_sideKeys[which]);
            _side[which] = IntPtr.Zero;
        }

        try
        {
            Assembly assembly = typeof(MonsterPortrait).Assembly;
            string wanted = SideArt[which];
            string? resource = Array.Find(
                assembly.GetManifestResourceNames(),
                candidate => candidate.EndsWith(wanted, StringComparison.OrdinalIgnoreCase));
            using Stream? stream = resource is null ? null : assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                _sideMissing[which] = true;
                return IntPtr.Zero;
            }

            using Image<Rgba32> image =
                Image.Load<Rgba32>(new DecoderOptions { Configuration = Contiguous }, stream);
            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(pixels, pixels),
                Mode = ResizeMode.Max,
            }));

            // The renderer's precondition, asked rather than discovered - see IconCache.Upload.
            if (!image.DangerousTryGetSinglePixelMemory(out _))
            {
                _sideMissing[which] = true;
                return IntPtr.Zero;
            }

            _side[which] = _upload!(_sideKeys[which], image, IconCache.Srgb);
            _sidePixels[which] = pixels;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            // A picture that shipped broken is a build mistake, not something to end a session
            // over; the button falls back to its word.
            _sideMissing[which] = true;
        }

        return _side[which];
    }

    /// <summary>Gathers this frame's lines under the picture, from strings built when something landed.</summary>
    /// <remarks>
    /// THE GESTURE AND FLOOR LINES ARE COUNTED FROM THE MOMENT A MODEL IS ASKED FOR, not from the
    /// moment its picture arrives: the picture's size is fitted around the lines, and lines that
    /// appeared with the picture would shrink it on the frame it landed, one rung down and
    /// drawn twice.
    /// </remarks>
    private void Lines()
    {
        _status.Clear();
        if (_cost.Length > 0)
        {
            _status.Add(_cost);
        }

        if (!_model.Ready && _loading is null)
        {
            return;
        }

        _status.Add(_orbiting ? Orbiting : Sun is not null ? SunGestures : Gestures);
        if (Ground && !_model.Staged)
        {
            _status.Add(FloorSaid);
        }

        if (_stage.Length > 0)
        {
            _status.Add(_stage);
        }

        if (_stageMove.Length > 0)
        {
            _status.Add(_stageMove);
        }

        // THE VIEW SAYS WHAT IT SHOWS, so a channel's picture is never taken for a lit one - see PictureView.
        if (_drawnLight is { View: not PictureView.Picture } viewed)
        {
            _status.Add(ViewSaid(viewed.View));
        }

        if (_wearsWater)
        {
            WaterLook water = Watered();
            bool crashing = _onCard && _drawnLight is { WaterCrash: true };
            bool byGround = crashing && _drawnLight is { WaterBreakByGround: true };
            bool wetting = _onCard && _drawnLight is { WaterWetness: true };
            bool refracting = _onCard && _drawnLight is { WaterRefraction: true };
            bool causticsOn = refracting && _drawnLight is { WaterCaustics: true };
            string causticsBy = ShadeProgram.CausticsSaid;

            // THE ENGINE'S WATER PASS LIGHTS THE GLOSSY TWIN under the game light alone - SceneLight.ShadeWater.
            bool engineLit = _drawnLight is not null && !FlatLight;
            if (_waterSaid.Length == 0 || water != _waterSaidFor || crashing != _waterSaidCrash || byGround != _waterSaidGround || engineLit != _waterSaidLit
                || wetting != _waterSaidWet || refracting != _waterSaidRefract || causticsOn != _waterSaidCausticsOn
                || !ReferenceEquals(causticsBy, _waterSaidCausticsBy))
            {
                _waterSaidFor = water;
                _waterSaidCrash = crashing;
                _waterSaidGround = byGround;
                _waterSaidWet = wetting;
                _waterSaidRefract = refracting;
                _waterSaidCausticsOn = causticsOn;
                _waterSaidCausticsBy = causticsBy;
                string caustics = !causticsOn
                    ? "not drawn (the light panel's caustics, with refraction, the card's alone)"
                    : ShadeProgram.Caustics is null || ShadeProgram.Distortion is null
                        ? causticsBy
                        : causticsBy + " (GetCausticsBias as written, times " + water.CausticsMult.ToString("0.###", CultureInfo.InvariantCulture)
                          + " - the area's caustics_mult or the engine's measured 1; the ground water ratio nought, the light multiplied after its"
                          + " grade, the pattern's phase the picture's frame's; the card's alone)";
                _waterSaidLit = engineLit;
                _waterSaid = ImGuiText.Escape(water.Line() + " (its colour's pattern a moving noise; its waves the engine's ocean ripples, three octaves"
                    + " rolling in the wind - " + ShadeProgram.Ripples.Said + "; the wind's way, height, speed and"
                    + " directionness the engine's former constants where the file sets none, a reading until measured, its way turned by the light"
                    + " panel's wind; the swell rolling in to the shore the engine's formula over a shore made here from the room's ground and what"
                    + " stands in the sea (the shapes whose material sprays: the rocks and the pier's stones), a wall - spraying stone too - rising out of water deeper"
                    + " than " + ShoreField.BeachDeep.ToString("0", CultureInfo.InvariantCulture) + " no shore, CHOSEN until the engine's is read,"
                    + " down a way in from the open sea " + WayIn(water.Shore) + " - CHOSEN until the engine's"
                    + " OceanMarkupField is read - " + ShadeProgram.Swells.Said + "; its foam the engine's trail foam (GetFoamColor3) from the"
                    + " cross-section's green and alpha, none where the stand-in stands, its leading edge's textured layers and the orbit's"
                    + " offsets not drawn; what stands in it sprayed and wetted as its own material says (CrashingFoamSurface: a crashing wave"
                    + " every five seconds running up from the water's line, over the same CHOSEN shore); their glints and reflection under the game light, by the light"
                    + " panel's water switches - the scene reflected by the engine's screen ray, its dither a 4 by 4 Bayer, a reading - and the surface"
                    + " lifted by them times its lift - which the engine does per pixel only - and, lifted, drawn as one surface whose"
                    + " nearest crossing of each pixel alone shows, DECIDED"
                    + (engineLit
                        ? "; lit as the engine's water pass lights it (BuildWaterRefraction as written: its colour light let through, not an"
                          + " albedo - every light wrapped round it, the cube on it, lifted greener on a crest and darkened in a trough by the"
                          + " waves' height and the area's scattering - what lies behind seen through the medium and one less the surface's"
                          + " reflectivity, the reflection thinned by nothing but the shore's ratio; "
                          + (refracting
                              ? "what lies behind bent into the water as the engine bends it (GetRefractedOpaqueSurface as written: the area's"
                                + " refraction index " + water.RefractionIndex.ToString("0.###", CultureInfo.InvariantCulture) + " thinned to one at"
                                + " the shore, three steps over the solid depth, red and green split towards where the ray started, the murkiness"
                                + " over the bent ray's length; the card's alone); its caustics " + caustics + "; not drawn:"
                              : "not drawn: the refraction's bend and its caustics (the light panel's refraction and caustics, the card's alone),")
                          + " the terrain water colour and the tidal state's own ratio to the shore)"
                        : string.Empty)
                    + (water.HeightOffset != 0f ? "; its height offset not applied - it moves the beach and flood waves alone)" : ")")
                    + (_wearsBeach
                        ? "; the beach waves the engine's (BreakingWavesVertex and BreakingWavesFragment as written, BreakingOceanWaves.fxgraph's mix):"
                          + " crests rolling in and breaking at the waterline, their foam running up the sand - over "
                          + ShadeProgram.BeachProfile.Said + ", " + ShadeProgram.BeachNoise.Said + "; on a sheet over the ground from "
                          + ShoreField.BeachDeep.ToString(CultureInfo.InvariantCulture) + " under the water to " + ShoreField.BeachHigh.ToString(CultureInfo.InvariantCulture) + " above it, the rocks standing"
                          + " out of it, at the engine's own height (" + water.HeightOffset.ToString("0.##", CultureInfo.InvariantCulture) + " the area's offset), the sea measuring its depth to"
                          + " it - the sheet, the shore's distances (its travel half a wave less, so the waves break at the waterline) and the film's"
                          + " colour (the area's water) CHOSEN; its refraction, caustics, wet sand and spray not drawn"
                        : string.Empty)
                    + (crashing
                        ? "; the field waves the engine's (CrashingWavesVertex and CrashingWavesFragment as written): breakers every five seconds"
                          + " rolling in down the way in and breaking as they near the shore - over " + ShadeProgram.FieldSaid + "; laid on the room's"
                          + " own water, lifted by the crest and the area's offset, the foam over it as the swell's - CHOSEN; the card's alone"
                          + (byGround
                              ? "; their break phase off the shore's distance to the ground alone, the rocks and the pier's stones no shore for it"
                                + " (the light panel's \"break by ground\", CHOSEN)"
                              : string.Empty)
                        : string.Empty)
                    + (wetting
                        ? "; the engine's wetness pass (WaterComputeWetness and BreakingOceanWavesWetness.fxgraph cast, ReadPixelWetness read):"
                          + " what the still water covers and the beach waves wash darkened where its graph reads it - the quay, the rocks, the crates,"
                          + " the ship - drying over " + ModelGpu.WetFade.ToString("0.#", CultureInfo.InvariantCulture) + " s, CHOSEN; the gloss"
                          + " and normal its graph also turns not drawn; the card's alone"
                        : string.Empty));
            }

            _status.Add(_waterSaid);
        }

        // THE SPRAY'S TWINS ARE SAID while the light panel swaps them in - see Occluded.
        if (_drawnLight is { SprayOccluded: true } && _occluded is { } occluded)
        {
            int swapped = occluded.Count;
            if (swapped != _occludedSaidFor)
            {
                _occludedSaidFor = swapped;
                _occludedSaid = swapped == 0
                    ? "spray from occlusion (the light panel's): no shape here carries a spray whose occlusion a graph before it set to one"
                    : "spray from occlusion (the light panel's, CHOSEN): " + swapped.ToString(CultureInfo.InvariantCulture)
                      + " shapes' spray reads the stone's own occlusion, which a graph before it set to one (Port's quay: TRO_MossBlend) - off"
                      + " draws the files as compiled";
            }

            _status.Add(_occludedSaid);
        }

        // THE SCREEN-SPACE FOG, the card's alone (SceneLight.Fog): whether this picture shows it, or why there is none.
        if (_drawnLight?.Fog is { } fog)
        {
            _status.Add(!fog.Ready ? fog.Said[0]
                : !_onCard ? "screen-space fog: not drawn - the processor's picture leaves it out (the card's alone)"
                : Card?.Gpu?.FogWhy is { Length: > 0 } failed ? "screen-space fog: not drawn - " + failed
                : FogDrawn);
        }

        // AND THE CLOUDS, the card's alone too (SceneLight.Clouds), once their noise is in.
        if (_drawnLight?.Clouds is not null)
        {
            _status.Add(!_onCard ? "clouds: not drawn - the processor's picture leaves them out (the card's alone)"
                : ShadeProgram.Distortion is null ? "clouds: not drawn yet - their noise, muddle_double.dds, is not in: " + ShadeProgram.CausticsSaid
                : CloudsDrawn);
        }

        // THE ENGINE'S GPU PARTICLES, the card's alone (MonsterPortrait.Particles): what was linked, what the card drew, the readings it takes.
        if (ParticlesLine() is { Length: > 0 } particlesLine)
        {
            _status.Add(particlesLine);
        }

        // THE ROOF FADE, both drawings' (RoofFade): which roof sections are hidden round the focus, and what the reading takes as given.
        if (SceneRoofFade?.Invoke() == true && Roofs() is { } roofs && _roofsShown)
        {
            // MADE AGAIN ONLY WHEN THE SECTIONS HIDDEN CHANGE - their flags are swapped in then and only then (Blends).
            if (!ReferenceEquals(_roofSaidOf, roofs) || _roofSaidFor != _roofsChanged)
            {
                (_roofSaidOf, _roofSaidFor, _roofSaid) = (roofs, _roofsChanged, roofs.Said(_roofsHidden));
            }

            _status.Add(_roofSaid);
        }

        // WHERE THE FILES WENT, and it stays up until the next export rather than flashing: the
        // whole point of the button is that somebody then goes and finds what it wrote.
        if (_exported.Length > 0)
        {
            _status.Add(_exported);
        }

        if (_paint.Length > 0)
        {
            _status.Add(_paint);
        }

        if (_still.Length > 0)
        {
            _status.Add(_still);
        }

        if (_shaders.Length > 0)
        {
            _status.Add(_shaders);
        }

        if (_blend.Length > 0)
        {
            _status.Add(_blend);
        }

        if (_graphs.Length > 0)
        {
            _status.Add(_graphs);
        }

        if (_planted.Length > 0)
        {
            _status.Add(_planted);
        }
    }

    /// <summary>What the row and the lines take off the pane's height, wrapping at its width included.</summary>
    private float Chrome(float wide)
    {
        float chrome = _pose is not null && _model.Moves ? ImGui.GetFrameHeightWithSpacing() : 0f;

        // THE PROBE'S BOX TOO, while probing - see Probed. Left out, its lines went under a picture sized to the pane's
        // height, out of the window: a click in the Monster Book's stage "did nothing" while the capture had its answer.
        if (_probing)
        {
            chrome += ProbeHeight() + ImGui.GetStyle().ItemSpacing.Y;
        }

        if (_status.Count == 0)
        {
            return chrome;
        }

        // THE LINES' HEADER, and the lines themselves only while they are unfolded - see Status.
        chrome += ImGui.GetFrameHeightWithSpacing();
        if (!_infoOpen)
        {
            return chrome;
        }

        float spacing = ImGui.GetStyle().ItemSpacing.Y;
        foreach (string line in _status)
        {
            // The same font and the same wrap width TextWrapped will use, so the height is the
            // one it will come out at rather than an estimate of it.
            chrome += ImGui.CalcTextSize(line, wide).Y + spacing;
        }

        return chrome;
    }

    /// <summary>
    /// The two pictures the last export made, at the size a sheet cell is.
    /// </summary>
    /// <remarks>
    /// WHY IT IS HERE AT ALL: an exported icon is judged at 64 pixels and nowhere else. A pose
    /// that reads beautifully across a 1200 px pane can be a smudge in a cell - a raised weapon
    /// becomes two dark pixels, a head turned away loses the face - and the only way to know is
    /// to look at the cell. Shown side by side so the pair can be judged together, which is how
    /// they will be seen: the game draws one of them while its boss lives and the other after.
    ///
    /// ONE TO ONE, NOT SCALED UP, because a smoothed-up preview is a different picture from the
    /// file. Hovering gives the same texture at four times the size for a closer look, which
    /// costs nothing and changes nothing about what was written.
    ///
    /// ON A CHECKERBOARD, because what is being judged includes the cut-out: an edge that kept
    /// a dark fringe, or a leg that vanished into the background, is invisible against grey.
    /// </remarks>
    private void Shots()
    {
        if (!_shotsOpen || (_shotColour == IntPtr.Zero && _shotGrey == IntPtr.Zero))
        {
            return;
        }

        // A WINDOW OF ITS OWN RATHER THAN A ROW IN THE PANE, which is the same call the icon
        // picker makes. Two reasons, and the second is the one that decided it: a row under
        // the picture takes 64 px off the model for as long as it is there - the picture is
        // sized to what is left of the pane - and a window can be dragged next to the game's
        // own minimap, which is the comparison somebody is actually making.
        //
        // The title carries the monster, because a window left open while another monster is
        // posed would otherwise be two pictures with nothing saying what they are of.
        bool open = _shotsOpen;
        bool expanded = ImGui.Begin(
            $"{(_shotStem.Length > 0 ? _shotStem : "exported")} icon###monster-shots",
            ref open,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing);

        if (expanded)
        {
            Pair(IconSheet.Tile, "at a sheet cell's size, one pixel to one");
            ImGui.Dummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y));
            Pair(IconSheet.Tile * Closer, $"{Closer} times that, to look at");

            ImGui.Separator();
            Rim();

            ImGui.Separator();
            Entry();
            PathLink.Line(OverlayInk.Quiet, _exported);
            if (_remembered.Length > 0)
            {
                ImGuiText.Wrapped(OverlayInk.Quiet, _remembered);
            }
        }

        ImGui.End();
        _shotsOpen = open;
    }

    /// <summary>
    /// The three fields that turn a picture into a marker, and the button that writes both.
    /// </summary>
    /// <remarks>
    /// IN THE PREVIEW WINDOW, WITH THE PICTURE, because this is where the answers are. Somebody
    /// posing a boss has its area on screen, its name in the book beside them and its arena
    /// under their feet; an hour later, looking at a folder of PNGs, they have none of the
    /// three. The window that shows what was made is therefore also the window that asks what
    /// it is of, and one click settles both.
    ///
    /// EVERY FIELD IS OPTIONAL, and the button still writes the pictures when they are all
    /// empty - which is what it did before this row existed. An export of a monster nobody has
    /// worked out the arena for yet is still worth having on disk.
    ///
    /// THE FAMILY IS NOT A FIELD. It is the export's own stem, which is what the files are
    /// named after, so the entry and the pictures cannot be written under different names -
    /// the one mistake in this that would be invisible until a marker failed to appear.
    /// </remarks>
    private void Entry()
    {
        Fill();

        float wide = (IconSheet.Tile * Closer * 2f) + ImGui.GetStyle().ItemSpacing.X;
        float label = ImGui.CalcTextSize("area").X + (ImGui.GetStyle().ItemSpacing.X * 2f);

        Field("area", "##monster-entry-areas", "map ids, comma separated", ref _entryAreas, AreasLength, wide - label);
        Field("tile", "##monster-entry-tile", "the arena tile's path", ref _entryTile, TileLength, wide - label);
        Field("name", "##monster-entry-name", "what the game calls it", ref _entryName, NameLength, wide - label);

        if (ImGui.Button("write##monster-write"))
        {
            Write();
            Remember();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(EntrySaid);
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGuiText.Wrapped(
            OverlayInk.Quiet,
            _shotWritten ? $"as {_shotStem}Active / {_shotStem}Inactive" : "not written yet");
    }

    /// <summary>One labelled field, laid out so the three line up.</summary>
    private static void Field(string caption, string id, string hint, ref string value, uint most, float wide)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(caption);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Max(wide, 120f));
        ImGui.InputTextWithHint(id, hint, ref value, most);
    }

    /// <summary>
    /// Fills the three fields, once per export, with what can be worked out.
    /// </summary>
    /// <remarks>
    /// AS MUCH AS POSSIBLE, by request, and every one of them is a guess somebody may have to
    /// correct: the boss of the area the player is standing in is usually the boss they are
    /// posing, and sometimes they are posing next season's. So the guesses go in the fields,
    /// where they can be read and changed, and not into the file.
    /// </remarks>
    private void Fill()
    {
        if (string.Equals(_entryFor, _shotStem, StringComparison.Ordinal))
        {
            return;
        }

        _entryFor = _shotStem;
        _entryName = _called;
        _entryAreas = AreaNow?.Invoke() ?? string.Empty;
        _entryTile = string.Empty;
        _remembered = string.Empty;

        if (_entryAreas.Length > 0 && ArenasIn?.Invoke(_entryAreas) is { Count: > 0 } arenas)
        {
            // The first, because an area with two arenas in it is rare and the field is right
            // there: a wrong tile that is visible beats a right one that had to be looked up.
            _entryTile = arenas[0];
        }
    }

    /// <summary>Writes the entry beside the pictures, and says what happened.</summary>
    /// <remarks>
    /// AFTER THE FILES AND NOT INSTEAD OF THEM: the pictures are written whatever the fields
    /// say, so a full field is never the thing standing between somebody and their export.
    /// </remarks>
    private void Remember()
    {
        if (Icons is not { } icons)
        {
            return;
        }

        string[] areas = _entryAreas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (areas.Length == 0 && _entryTile.Trim().Length == 0)
        {
            _remembered = string.Empty;
            return;
        }

        _ = icons.Remember(areas, _entryTile, _shotStem, _entryName, out string said);
        _remembered = said;
    }

    /// <summary>
    /// The black rim: how wide, which side, and the button that writes the result.
    /// </summary>
    /// <remarks>
    /// IN THIS WINDOW AND NOT IN THE PANE, which is where it was asked for and is also where it
    /// belongs: whether an outline helps is a question about what the icon looks like at 64
    /// pixels, and the answer is two inches away from the control. Nothing is re-rendered when
    /// it moves - the model's own pictures are kept - so the pair above changes as fast as the
    /// slider is let go of.
    ///
    /// ON RELEASE RATHER THAN PER TICK, for the reason <see cref="Remake"/> gives: two distance
    /// transforms and two downscales over a megapixel is a tenth of a second, which is once
    /// worth paying and sixty times a second is not. The same rule the keep-out boxes use for
    /// their drag.
    ///
    /// AND THE FILES DO NOT FOLLOW ON THEIR OWN. Writing four PNGs on every change would
    /// hammer the disk while somebody is deciding; instead the row below says whether what is
    /// on screen has been written, and its button settles it. See <see cref="Entry"/>.
    /// </remarks>
    private void Rim()
    {
        float wide = ImGui.CalcTextSize("0.00").X * 4f;

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("outline");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(wide);
        float width = OutlineWidth;
        if (ImGui.SliderFloat("##monster-outline-width", ref width, 0f, PictureOutline.Widest, "%.1f px"))
        {
            OutlineWidth = width;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Remake();
            Changed?.Invoke();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(RimSaid);
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(wide * 1.6f);
        int side = (int)Outline;
        if (ImGui.Combo("##monster-outline-side", ref side, "none\0outward\0inward\0"))
        {
            Outline = (OutlineSide)side;
            Remake();
            Changed?.Invoke();
        }

        Lift(wide);
    }

    /// <summary>
    /// The brightness row: whether the icon is brought to the game's own, and to what.
    /// </summary>
    /// <remarks>
    /// BESIDE THE RIM AND NOT IN THE PANE, for the same reason: this changes what the icon
    /// looks like at 64 pixels beside the game's, and both pictures that answer that question
    /// are directly above it. It also reports what it ACHIEVED rather than only what was
    /// asked - the exponent is solved on luminance and applied per channel, so the result
    /// lands near the target rather than on it, and a number nobody can check is not a
    /// measurement.
    /// </remarks>
    private void Lift(float wide)
    {
        bool lit = Light;
        if (ImGui.Checkbox("light##monster-light", ref lit))
        {
            Light = lit;
            Remake();
            Changed?.Invoke();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(LightSaid);
        }

        if (!Light)
        {
            return;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(wide);
        float target = LightTarget;
        if (ImGui.SliderFloat(
            "##monster-light-target", ref target, PictureLight.Dimmest, PictureLight.Brightest, "%.0f"))
        {
            LightTarget = target;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Remake();
            Changed?.Invoke();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(LightSaid);
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled($"= {_shotLit:F0}");
    }

    /// <summary>The two halves side by side at one size, with a line saying what the size is.</summary>
    private void Pair(float side, string said)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        float gap = ImGui.GetStyle().ItemSpacing.X;
        Vector2 at = ImGui.GetCursorScreenPos();

        Preview(draw, at, side, _shotColour, "the colour half - the game's Active icon");
        Preview(draw, at + new Vector2(side + gap, 0f), side, _shotGrey, "the grey half - its Inactive icon");

        // The pictures are drawn straight into the draw list, so the layout is told how much
        // room they took or everything after them is written over the top of them.
        ImGui.Dummy(new Vector2((side * 2f) + gap, side));
        ImGuiText.Wrapped(OverlayInk.Quiet, said);
    }

    /// <summary>One preview: the checkerboard, the picture, and a closer look on hover.</summary>
    private static void Preview(ImDrawListPtr draw, Vector2 at, float side, IntPtr texture, string said)
    {
        Vector2 far = at + new Vector2(side, side);
        Checkered(draw, at, far);

        if (texture == IntPtr.Zero)
        {
            return;
        }

        draw.AddImage(texture, at, far);
        draw.AddRect(at, far, Edge);

        // WHICH HALF THIS IS, and nothing else. The window already shows the pair three times
        // over, so a tooltip carrying a bigger copy of the picture would be the third one -
        // what is missing at a glance is which of the two is which, and that is a sentence.
        if (ImGui.IsMouseHoveringRect(at, far))
        {
            ImGui.SetTooltip(said);
        }
    }

    /// <summary>The lines under the picture, in the quiet ink the rest of the pane's asides use.</summary>
    private void Status()
    {
        if (_status.Count == 0)
        {
            return;
        }

        // FOLDED BY DEFAULT, so the picture has the window: the lines are what to read when a picture
        // looks wrong, not while turning one that looks right. The first of them is in the header.
        ImGui.SetNextItemOpen(_infoOpen, ImGuiCond.Always);
        _infoOpen = ImGui.CollapsingHeader($"info: {_status[0]}###monster-info");
        if (!_infoOpen)
        {
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, OverlayInk.Quiet);
        // A LINE NAMING A FILE THE PANE WROTE - the export - makes the file a link. See PathLink.
        foreach (string line in _status)
        {
            PathLink.Line(line);
        }

        ImGui.PopStyleColor();
    }

    /// <summary>Rebuilds the lines that follow a model and its keyframes: once when either lands, not per frame.</summary>
    /// <remarks>
    /// THE COST WAS ASKED FOR FROM THE LIVE CLIENT after the first half hour of watching
    /// animations. A bundled rig holds up to twelve megabytes of keyframes and the book has 2792
    /// rows, so "what did that click just read" is a fair question, and the walk already knew
    /// the answer. THE REASONS ARE HERE BECAUSE A PICTURE CANNOT CARRY THEM: an unpainted model
    /// is drawn in a pale warm grey that reads as bare skin, and a model that holds still looks
    /// the same whether it has no skeleton, a skeleton this machine cannot unpack, or is simply
    /// paused - so both answers are worked out where the files are read and said here rather
    /// than thrown away.
    /// </remarks>
    private void Said()
    {
        if (_model.Files == 0)
        {
            _cost = string.Empty;
        }
        else
        {
            string said = $"read {ByteCount.Said(_model.Bytes)} in {_model.Files} file{(_model.Files == 1 ? string.Empty : "s")}";

            // HOW MANY SHEETS THE MONSTER IS PAINTED FROM, whenever it is painted at all.
            // This was shown only for more than one, on the reasoning that one is the
            // ordinary case and says nothing - and it says the one thing that was asked from
            // the live client: whether the part-by-part painting is running and found a
            // single sheet, or is not running at all. A line that appears only in the
            // interesting case cannot answer that, because its absence is also what the
            // version without it looks like.
            if (_model.Materials.Count > 0)
            {
                int sheets = _model.Materials.Count;
                said += $" · {_model.Mesh.Shapes.Count} shapes from {sheets} texture{(sheets == 1 ? string.Empty : "s")}";

                // WHICH SHEETS, BY NAME, because a WRONG texture and a MISSING one look the
                // same on screen: a mask or an occlusion map drawn as colour is a monster in
                // greyscale, which reads as "the texture is gone" - and the file name settles
                // it in a word. Reported on the Vessel of Kulemak, who is painted from two
                // sheets and looks unpainted.
                if (_model.Textures.Count > 0)
                {
                    said += ": " + string.Join(
                        ", ", _model.Textures.Take(Named).Select(one => one[(one.LastIndexOf('/') + 1)..]));
                }

                // AND WHETHER THE CHOICE WAS A GUESS. Where no slot carries Albedo, Colour or
                // Color, the rule is "the first texture that is not a normal map" - right on
                // the materials that have been looked at, and still a guess. It has been
                // marked as one in the comments since it was written; this is the first time
                // it says so where somebody looking at the picture can see it.
                if (_model.Guessed)
                {
                    said += " · no slot said which map is the colour one";
                }

                // AND WHERE FEWER SHEETS THAN SHAPES CAME BACK, how many were NAMED. One
                // texture over thirty-five shapes is either a monster painted from one atlas,
                // which is ordinary, or a monster whose per-shape materials were not matched -
                // and the picture is identical either way. The counts are the only thing that
                // tells them apart, so they are printed exactly where the question arises.
                if (_model.Kind == ModelKind.Tile)
                {
                    // A TILE'S UNNAMED SHAPES ARE ITS GROUND, left plain on purpose - TileModels -
                    // so the count says how much of the picture that is, not how a .sm matched.
                    int ground = _model.Mesh.Shapes.Count - _model.NamedInAo;
                    said += ground > 0 ? $" · {ground} ground shapes left plain" : " · no ground drawn";
                    if (_model.Walls > 0)
                    {
                        said += $" · {_model.Walls} black wall shape{(_model.Walls == 1 ? string.Empty : "s")} left out";
                    }

                    // DRAWN AS A TILESET, how much of what is on screen is the tileset's and not the tile's.
                    if (_model.Tileset.Length > 0)
                    {
                        said += $" · drawn as {_model.Tileset}: {_model.Swapped} of the tile's materials swapped";
                    }

                    if (_model.Laid.Length > 0)
                    {
                        said += $" · laid as in this area: {_model.Laid}";
                    }
                }
                else if (_model.Mesh.Shapes.Count > _model.Materials.Count)
                {
                    said += $" · named: {_model.NamedInAo} in the .ao, {_model.NamedInMesh} in the .sm";
                }

                // AND WHETHER THE MANIFEST'S NUMBERS WERE WHAT JOINED THEM. The number after
                // each material path is unidentified in every reader of this format there is;
                // this one uses it only when the numbers add up to exactly the shape count, and
                // says when they did - so a boss that comes out right is evidence for the
                // reading rather than just a boss that came out right.
                if (_model.Runs)
                {
                    said += " · the .sm's counts spread over the shapes";
                }

                // AND WHETHER THE BODY ITSELF IS SEVERAL FILES. A SkinMesh block may name one
                // manifest or seven, and while only the first was read Zar Wali the Bone Tyrant
                // drew as two floating arms - which looks exactly like a monster that IS two
                // arms. The count is the difference.
                if (_model.Sections > 1)
                {
                    said += $" · body in {_model.Sections} sections";
                }

                // AND WHAT IT IS WEARING. A monster with no attachments, one whose pieces were
                // switched off, and one whose pieces all failed to read are three different
                // things that draw identically - so the count says which.
                if (_model.Kind == ModelKind.Tile)
                {
                    said += $" · {_model.Parts} sub-tile{(_model.Parts == 1 ? string.Empty : "s")}";
                }
                else if (_model.Parts > 0)
                {
                    said += $" · wearing {_model.Parts} part{(_model.Parts == 1 ? string.Empty : "s")}";
                }
                else if (!Parts)
                {
                    said += " · parts off";
                }
            }

            if (_tracks is { Ready: true } tracks)
            {
                said += $" · keyframes {ByteCount.Said(tracks.Bytes)}";
            }

            _cost = said;
        }

        _paint = _model.Paint.Length > 0 ? "plain ink: " + ImGuiText.Escape(_model.Paint) : string.Empty;

        string still = _model.Move.Length > 0 ? _model.Move : _stillWhy;
        _still = still.Length > 0 ? "still: " + ImGuiText.Escape(still) : string.Empty;

        _stage = _model.StageSaid.Length > 0 ? ImGuiText.Escape(_model.StageSaid) : string.Empty;
        _stageMove = _model.StageMove.Length > 0 ? "the stage's own lines: " + ImGuiText.Escape(_model.StageMove) : string.Empty;
        _wearsWater = _model.Shades.Any(one => one is not null && one.Graphs.Contains(ShadeProgram.WaterGraph));
        _wearsBeach = _model.Shades.Any(one => one is { Beaches: true });

        _shaders = ShowShaders && _model.Shaders.Count > 0
            ? "shaders: " + ImGuiText.Escape(string.Join(", ", _model.Shaders.Select(Tail)))
            : string.Empty;

        // THE RAW MODE AND WHAT IT IS TAKEN TO MEAN, side by side, and called a guess - the rule
        // reading the word is chosen, not measured. See MaterialBlends.
        string[] modes = [.. _model.Modes
            .Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        _blend = ShowShaders && modes.Length > 0
            ? "blend (guessed from the word): " + ImGuiText.Escape(string.Join(", ",
                modes.Select(one => $"{one} -> {MaterialBlends.Of(one).ToString().ToLowerInvariant()}")))
            : string.Empty;

        // WHICH MATERIALS ARE DRAWN FROM THEIR GRAPHS, AND WHAT WAS LEFT OUT - see ShadeProgram. The
        // second half is the one that matters: a colour this could not evaluate is drawn as before,
        // and the node that stopped it is named here rather than guessed at.
        _graphs = Shaded && (_model.ShadedBy > 0 || _model.Unshaded.Count > 0)
            ? ImGuiText.Escape(
                $"graphs: {_model.ShadedBy} material{(_model.ShadedBy == 1 ? string.Empty : "s")} coloured by their shader graphs"
                + (_model.Unshaded.Count > 0
                    ? " · not evaluated: " + string.Join(", ", _model.Unshaded.Take(Named))
                        + (_model.Unshaded.Count > Named ? $" and {_model.Unshaded.Count - Named} more" : string.Empty)
                    : string.Empty)

                // AND WHICH OF THEM RUN WITH THE CLOCK, by name - the picture moves for these, and they
                // are what to compare with the game. See ShadeProgram.UsesTime.
                + (_model.Clocked.Count > 0
                    ? $" · {_model.Clocked.Count} running with the clock: " + string.Join(", ", _model.Clocked.Take(Named).Select(Tail))
                        + (_model.Clocked.Count > Named ? $" and {_model.Clocked.Count - Named} more" : string.Empty)
                    : string.Empty))
            : string.Empty;

        static string Tail(string path) => path[(path.LastIndexOf('/') + 1)..];
    }

    /// <summary>Starts on a different animation, from its first frame.</summary>
    private void Choose(int which)
    {
        if (which == _chosen || _pose is null)
        {
            return;
        }

        _chosen = which;
        _frame = 0f;
        _tracks = null;
        _stillWhy = string.Empty;
        Start(which);
        Said();
    }

    /// <summary>Sets the keyframes of one animation loading, or says why they cannot be.</summary>
    private void Start(int which)
    {
        IReadOnlyList<SkeletonAnimation> moves = _model.Rig.Animations;
        if (which < 0 || which >= moves.Count)
        {
            return;
        }

        // A BUNDLED RIG WITH NOTHING TO UNPACK IT is the one case decided here rather than by the
        // task: the model cannot know whether this machine has Oodle, and the task would only
        // come back with "the keyframes did not unpack", which is true and says less.
        AnimationSkeleton rig = _model.Rig;
        if (!rig.Loose && _unpack is null)
        {
            _stillWhy = "this rig keeps its keyframes in a bundle, and there is no Oodle here to unpack them";
            return;
        }

        SkeletonAnimation move = moves[which];
        Func<ReadOnlyMemory<byte>, int, byte[]?> unpack = _unpack ?? ((_, _) => null);
        _loadingAnimation = which;
        // HIS AND EVERY RIDER'S of the same name, in step - see AnimationSkeleton.Played.
        _loadingTracks = Task.Run(() => (AnimationTracks?)rig.Played(move, unpack));
    }

    /// <summary>
    /// Zooms on the wheel, and takes the wheel off the pane underneath while doing it.
    /// </summary>
    /// <remarks>
    /// THE PANE WOULD OTHERWISE SCROLL AWAY UNDER THE MODEL. ImGui gives the wheel to the hovered
    /// WINDOW, not to the hovered item, so without this a notch both zooms and scrolls the detail
    /// pane - and the picture the wheel was aimed at slides off the top.
    ///
    /// SetItemKeyOwner IS WHY THE BUTTON HAD TO BE A BUTTON: it reads g.LastItemData.ID and does
    /// nothing at all for an id of zero, which is what ImGui.Image left behind. Ownership lands in
    /// OwnerCurr immediately but UpdateMouseWheel has already run for this frame inside NewFrame,
    /// so it bites from the NEXT one - claimed on every hovered frame, that only leaves a notch
    /// unclaimed if it arrives on the very first frame the cursor is over the picture.
    /// </remarks>
    private void Wheel(float side)
    {
        ImGui.SetItemKeyOwner(ImGuiKey.MouseWheelY);

        float notches = ImGui.GetIO().MouseWheel;
        if (notches == 0f)
        {
            return;
        }

        if (!_view.Valid)
        {
            return;
        }

        // TOWARDS WHAT IS UNDER THE POINTER, the nearest surface its ray meets, or where it meets the focus's plane over nothing - the
        // camera stretched about that point, which stays where it is on the picture (CameraPose.Zoomed). No cap but the eye's own:
        // asked for from the live client, close enough for a barrel to fill the picture. The pointer as a share of the picture from
        // its top left corner; the picture is the last item, so its rectangle is the one ImGui hands back here.
        Vector2 pointer = (ImGui.GetMousePos() - ImGui.GetItemRectMin()) / MathF.Max(side, 1f);
        Vector3 about = Picked(pointer) ?? _view.OnFocusPlane(pointer);
        _view = _view.Zoomed(about, MathF.Pow(Notch, -notches), CameraPose.Farthest * MathF.Max(1f, CameraPose.Reach(_model.Mesh)));
    }

    /// <summary>Gives back the textures. Called when the window goes, and when the install changes.</summary>
    public void Forget()
    {
        if (_key.Length > 0)
        {
            _release?.Invoke(_key);
        }

        for (int which = 0; which < Sides; which++)
        {
            if (_side[which] != IntPtr.Zero)
            {
                _release?.Invoke(_sideKeys[which]);
            }

            _side[which] = IntPtr.Zero;
            _sidePixels[which] = 0;
            _sideMissing[which] = false;
        }

        ForgetShots();
        _texture = IntPtr.Zero;
        _key = string.Empty;
        _orbiting = false;
        _shown = string.Empty;
        _model = MonsterModel.None;
        _drawnView = default;
        Rest();

        // THE UNPACKING STOPPED with the model it was for.
        UnpackBehind();
    }

    /// <summary>
    /// Lets go of every handle after the overlay's graphics device was made anew, the model kept - see EntityOverlay.DeviceRebuilt.
    /// </summary>
    /// <remarks>
    /// THE HANDLES ARE ALREADY GONE: the renderer forgot every texture with the old device, so nothing
    /// here is released through it - giving a key back that it no longer holds does nothing. The picture
    /// is drawn again on the next frame (the drawn size no longer matches), the side buttons upload their
    /// icons again as they are drawn, and the export's preview pair is handed over again from the cells
    /// it was made of. The card's target belongs to the old device and goes with it.
    /// </remarks>
    public void DeviceLost()
    {
        _texture = IntPtr.Zero;
        _key = string.Empty;
        for (int which = 0; which < Sides; which++)
        {
            _side[which] = IntPtr.Zero;
            _sidePixels[which] = 0;
        }

        _cardTarget?.Dispose();
        _cardTarget = null;
        _cardShown = IntPtr.Zero;
        _onCard = false;
        _drawnSize = 0;

        if (_shotColourKey.Length > 0 && _shotColourCell.Length > 0 && _shotGreyCell.Length > 0)
        {
            Shown(_shotColourCell, _shotGreyCell);
        }
        else
        {
            DropShots();
        }
    }

    /// <summary>Drops the skeleton and whatever animation was loaded or playing on it.</summary>
    private void Rest()
    {
        _pose = null;
        _swinger = null;
        _tracks = null;
        _loadingTracks = null;
        _loadingAnimation = -1;
        _chosen = -1;
        _frame = 0f;
        _stillWhy = string.Empty;
        _cost = string.Empty;
        _paint = string.Empty;
        _still = string.Empty;
        _stage = string.Empty;
        _shaders = string.Empty;
        _blend = string.Empty;
        _graphs = string.Empty;
        _count = string.Empty;
        _planted = string.Empty;
        _plantedAt = int.MinValue;
        _comboFont = 0f;
        _drawnFrame = float.NaN;
        _drawnAnimation = -1;
    }

    /// <summary>Starts a load when the monster changed, and only then.</summary>
    private void Wanted(MonsterVariety? one, string path)
    {
        // THE SWITCH IS PART OF WHAT WAS ASKED FOR, not a way of drawing what is already loaded:
        // the pieces are read from the install, so turning them on has to go back for them. The
        // model stays on screen while it reloads, which is what makes the toggle feel like one.
        if (string.Equals(path, _wanted, StringComparison.Ordinal) && _dressed == Parts
            && string.Equals(_stagedOn, Stage, StringComparison.Ordinal))
        {
            return;
        }

        _wanted = path;
        _stagedOn = Stage;
        _model = MonsterModel.None;
        Why = string.Empty;

        // THE LAST MODEL'S UNPACKING STOPPED, so the load has the cores.
        UnpackBehind();

        // Kept rather than asked for again at export time: the row is right here now, and the
        // export happens from a window that no longer has the table. See Fill.
        _called = one?.Name ?? string.Empty;
        _variety = one;
        _dumped = string.Empty;

        // The last monster's exported cells go with it. Left up they would sit under a
        // different model saying "this is what you made", which is the kind of wrong that gets
        // a file overwritten.
        ForgetShots();
        _exported = string.Empty;
        Rest();


        // The game draws this variety's mesh scaled by its own multiplier, and the floor's tiles
        // have to shrink in the mesh's units by the same amount to stay the game's tiles.
        _tile = ModelFloor.TileOn(one?.ModelSize ?? 0);

        if (one is null || path.Length == 0)
        {
            Drop();
            _loading = null;
            return;
        }

        // A load already running for the previous monster is left to finish and then ignored -
        // cancelling a handful of bundle reads costs more code than letting them land.
        bool wearing = Parts;
        _dressed = wearing;
        bool shaded = Shaded;
        string stage = Stage;
        Func<TerrainGrid?>? terrain = Terrain;
        Func<Func<string, byte[]?>, MonsterVariety, string, bool, ModelProgress, MonsterModel>? load = Load;
        var progress = new ModelProgress();
        _progress = progress;
        _chosenAt = Stopwatch.GetTimestamp();
        _built = [];
        Func<string, byte[]?> read = progress.Timing(_install!);

        // THE STAGE KEPT WHILE IT IS ASKED FOR, and let go of the moment none is - see StageKeep.
        StageKeep keep = _stageKeep;
        if (stage.Length == 0)
        {
            keep.Forget();
        }

        _loading = load is null
            ? Task.Run(() => StagedModels.Of(read, one, stage, wearing, shaded, progress, terrain, keep))
            : Task.Run(() => load(read, one, path, wearing, progress));
    }

    /// <summary>Takes a finished load, and re-renders when anything it depends on moved.</summary>
    /// <param name="side">How wide the picture will be shown, which decides what it is drawn at.</param>
    private void Finished(float side)
    {
        Adopted();
        ParticlesLanded();
        if (!_model.Ready)
        {
            Drop();
            return;
        }

        Framed();
        Landed();
        Advance();
        UnpackSaid();
        bool ticking = Ticking;
        if (ticking)
        {
            _clock += Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.25f);
        }

        // CAPPED WHILE IT IS BEING DRAGGED, ORBITED OR PLAYED ON THE PROCESSOR, and the pane's own
        // rung the moment it stops - or while the card draws, see Wanted. The work grows with the
        // AREA, and it is while moving that a dropped frame is felt, so the cap is somebody's answer
        // to what a moving frame may cost the processor; the bands on every core are what let the
        // usual cap sit at the pane's own size.
        int size = Wanted(side);
        bool posed = PoseReady;

        bool moved = !string.Equals(_shown, _wanted, StringComparison.Ordinal)
            || _drawnView != _view
            || _drawnSize != size
            || _drawnPosed != posed

            // The greying is done to the PIXELS, so a change to it is a change to the picture
            // and has to redraw one - without this the switch and the slider do nothing at all
            // until something else moves, which reads as neither working.
            || _drawnGrey != Greyed
            || !ReferenceEquals(_drawnShades, ShadesOf(_model))
            || !ReferenceEquals(_drawnBlends, Blends())
            || !ReferenceEquals(_drawnLight, Lit())
            || _drawnDust != Dusted()
            || _drawnWater != Watered()
            || (posed && (_drawnFrame != _frame || _drawnAnimation != _chosen))
            || (posed && _swinger is not null && _drawnSwing != Swinging())
            || _drawnSharp != (Card?.Sharp ?? false)
            || (_cardWaiting && Card is { } card && card.Gpu is { } gpu && gpu.Landed != _cardLanded)
            || (_unpackWaiting && UnpackDone)
            || _probeAsked;

        if (moved)
        {
            Render(size, posed);
            return;
        }

        ClockLanded(size);

        // A PROGRAM THAT READS THE CLOCK changes the picture as the clock runs - see Ticking. Behind
        // the frame where nothing else moved, and on it where a pose is held: the pose's buffers
        // are the frame's, and a drawing on a task must not read them while the frame writes them.
        if (ticking && _drawnClock != _clock)
        {
            // ON THE CARD THE TICK IS A FRAME'S DRAW like any other, and cheaper than the processor's
            // kept still part - see ClockBehind for why the processor's goes behind the frame.
            if (posed || _onCard)
            {
                Render(size, posed);
            }
            else
            {
                ClockBehind(size);
            }
        }
    }

    /// <summary>Whether the picture is drawn in a pose: the chosen animation's keyframes are in and the skeleton holds together.</summary>
    private bool PoseReady => _tracks is { Ready: true } && _pose is not null;

    /// <summary>Builds the pose for a model that just arrived, and starts its first animation.</summary>
    private void Rigged()
    {
        Rest();
        if (!_model.Moves)
        {
            return;
        }

        _pose = SkeletonPose.Of(_model.Rig);

        // HIS CLOTHS STAND ON THEIR TARGETS AT HIS FIRST POSE, a swinger of their own for every model (ClothSwinger).
        IReadOnlyList<ClothSwing> swings = (_model.Actor ?? _model).Swings;
        _swinger = swings.Count > 0 ? new ClothSwinger(swings, (_model.Actor ?? _model).Colliders) : null;
        _posedFor = null;
        _cpuPosedKey = -1;
        _cardBonesKey = -1;
        _cardSkin = null;
        _hull = null;
        _hullMade = false;
        _hullFor = null;
        _lowestKey = -1;
        if (_pose is null)
        {
            _stillWhy = "the skeleton's bone tree does not hold together";
            return;
        }

        int count = _model.Mesh.Positions.Length;
        if (_posed.Length != count)
        {
            _posed = new Vector3[count];
            _posedNormals = new Vector3[count];
        }

        // ON A STAGE THE POSE MOVES THE MONSTER'S SLICE ALONE, so the rest of the arrays hold the stage
        // as the join laid it - filled once here, and never written by a frame. See Posed.
        if (_model.Actor is { } actor)
        {
            _model.Mesh.Positions.CopyTo(_posed, 0);
            _model.Mesh.Normals.CopyTo(_posedNormals, 0);
            int own = actor.Mesh.Positions.Length;
            if (_actorPosed.Length != own)
            {
                _actorPosed = new Vector3[own];
                _actorNormals = new Vector3[own];
            }
        }

        // THE BARE NUMBER IN BRACKETS, because the label before the combo already says what is
        // being counted: "animation [idle_01] 32 animations" says the word twice and reads as
        // two separate facts rather than as one.
        IReadOnlyList<SkeletonAnimation> moves = _model.Rig.Animations;
        _count = $"({moves.Count})";

        // THE MODEL'S OWN OPENING FIRST - the player's stance's idle (MonsterModel.Opening) - then a monster's idle_01, then the first.
        var first = 0;
        string opening = _model.Opening.Length > 0 ? _model.Opening : Idle;
        for (var one = 0; one < moves.Count; one++)
        {
            if (string.Equals(moves[one].Name, opening, StringComparison.Ordinal))
            {
                first = one;
                break;
            }
        }

        Choose(first);
    }

    /// <summary>Takes a finished keyframe load, for the animation still chosen.</summary>
    private void Landed()
    {
        if (_loadingTracks is not { IsCompleted: true } done)
        {
            return;
        }

        _loadingTracks = null;
        if (_loadingAnimation != _chosen)
        {
            return;
        }

        AnimationTracks? tracks = done.IsCompletedSuccessfully ? done.Result : null;
        if (tracks is { Ready: true })
        {
            _tracks = tracks;
            _stillWhy = string.Empty;
        }
        else
        {
            _tracks = null;
            _stillWhy = tracks is null
                ? done.IsCompletedSuccessfully ? "the keyframes did not unpack" : Said(done.Exception)
                : tracks.Why.Length > 0 ? tracks.Why : "the keyframes did not read as tracks";
        }

        Said();
    }

    /// <summary>Moves the animation on by however long the last frame took.</summary>
    /// <remarks>
    /// IN FRAMES OF THE ANIMATION, not of the screen: the keys are timed in the file's own frames
    /// at the rate its header gives, so a 30fps walk plays at the same speed on a 60Hz and a 144Hz
    /// monitor. It wraps rather than stops, because every one of these is a loop or a one-shot
    /// that looks fine looped, and the file carries no flag that says which.
    /// </remarks>
    private void Advance()
    {
        if (!_playing || _tracks is not { Ready: true } tracks || tracks.Frames <= 0f
            || _chosen < 0 || _chosen >= _model.Rig.Animations.Count)
        {
            return;
        }

        float rate = _model.Rig.Animations[_chosen].Rate;
        if (rate <= 0f)
        {
            rate = UsualRate;
        }

        float seconds = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.25f);
        _frame += seconds * rate;
        if (_frame > tracks.Frames)
        {
            _frame %= tracks.Frames;
        }

        // THE SWING RUNS ON THE SAME CLOCK, stepped where the pose is next taken (Swung).
        _swinger?.Due(seconds);
    }

    /// <summary>What the model is drawn at: the rung that covers the pane, capped while it moves on the processor.</summary>
    /// <remarks>
    /// THE CAP IS THE PROCESSOR'S. It is somebody's answer to what a moving frame may cost in
    /// processor time - PictureLadder's measurements are of the rasteriser's bands - and the card
    /// pays nothing it would notice for the top rung: a 2048 square target is a frame's draw at a
    /// hundred a second, where the processor's was a quarter of one. Capped on the card, a 3440 px
    /// monitor's pane played every animation at 1024 and stretched it, which the live client saw as
    /// textures that went soft the closer the camera came. So where the card drew the picture
    /// shown, the next one is the pane's own rung whether it moves or not. The card's word is the
    /// LAST picture's (<see cref="_onCard"/>), since who draws is settled only in the drawing: a
    /// picture the card hands back to the processor - a material's shader still compiling - costs
    /// one uncapped frame there, and the next is capped again.
    /// </remarks>
    private int Wanted(float side)
    {
        bool moving = _held || _orbiting || _flying || (_playing && _tracks is { Ready: true }) || Ticking;
        return moving && !_onCard ? _sizes.Moving(side) : _sizes.For(side);
    }

    /// <summary>
    /// Whether the picture runs with the clock: a shade program of the model reads <c>Time</c>, and it is playing.
    /// </summary>
    /// <remarks>
    /// AN ANIMATION OF ITS OWN, and drawn at the capped rung like one while it runs - but behind
    /// the frame, not on it, where nothing else moved: see <see cref="ClockBehind"/>.
    /// </remarks>
    private bool Ticking => _playing && HasClock();

    /// <summary>
    /// Each wobble core's sway at the clock - RoomSway.At - into an array kept for the model, or null where nothing sways.
    /// </summary>
    /// <remarks>
    /// THE GAME STARTS EACH CORE AT A RANDOM PLACE IN ITS LOOP (<c>randomise_start_position = true</c> in its .ao); here every core
    /// starts at nought, so two pictures of one room sway alike.
    /// </remarks>
    private Matrix4x4[]? Swayed()
    {
        IReadOnlyList<RoomSway> sways = _model.Sways;
        if (sways.Count == 0)
        {
            return null;
        }

        if (_swayed is null || _swayed.Length != sways.Count)
        {
            _swayed = new Matrix4x4[sways.Count];
        }

        for (var one = 0; one < sways.Count; one++)
        {
            _swayed[one] = sways[one].At(_clock);
        }

        return _swayed;
    }

    private Matrix4x4[]? _swayed;

    /// <summary>Whether a shade program the picture is drawn with reads <c>Time</c> - or a ship sways in it. Worked out once per list, since it is asked every frame.</summary>
    private bool HasClock()
    {
        // A SWAY RUNS WITH THE CLOCK, on the card - the processor's picture stands still, and says so. So does the screen-space fog,
        // which drifts and rises with it (SceneLight.Fog: ScreenspaceFogFlat reads Time), and the clouds' shadow, which drifts along their
        // velocity (SceneLight.Clouds), both drawn by the card alone.
        bool drifting = _drawnLight?.Clouds is { } clouds && clouds.Drift.Speed > 0f && ShadeProgram.Distortion is not null;
        if ((_model.Sways.Count > 0 || _drawnLight?.Fog is { Ready: true, Program.UsesTime: true } || drifting || Particled) && _onCard)
        {
            return true;
        }

        IReadOnlyList<ShadeProgram?>? shades = ShadesOf(_model);
        if (shades is null)
        {
            return false;
        }

        if (!ReferenceEquals(_timedOf, shades))
        {
            _timedOf = shades;
            _timed = shades.Any(one => one is { UsesTime: true });
        }

        return _timed;
    }

    /// <summary>Whether the model has a specular colour to light at all - the light button is shown only then. Worked out once per model.</summary>
    private bool HasShine()
    {
        if (!ReferenceEquals(_shineOf, _model))
        {
            _shineOf = _model;
            _shine = _model.Shades.Any(one => one is { HasSpecular: true }) || !ReferenceEquals(_model.GlossShades, _model.Shades);
        }

        return (Shaded || _model.Staged) && _shine;
    }

    /// <summary>The model <see cref="_shine"/> was worked out for.</summary>
    private MonsterModel? _shineOf;
    private bool _shine;

    /// <summary>
    /// Takes a picture the clock asked for once it has been drawn, and shows it - unless the frame
    /// drew since it was asked for, or the model or the size moved on.
    /// </summary>
    private void ClockLanded(int size)
    {
        if (_clockDrawing is not { IsCompleted: true } done)
        {
            return;
        }

        // LET GO FIRST, stale or not: its canvas is free again only once nothing waits on it.
        _clockDrawing = null;
        ClockPicture drawn = done.Result;
        if (drawn.Drawing != _drawing || !ReferenceEquals(drawn.Model, _model) || drawn.Size != size)
        {
            return;
        }

        if (drawn.Failed.Length > 0)
        {
            Why = drawn.Failed;
            Drop();
            return;
        }

        _rate.Redrawn(ImGui.GetTime());
        try
        {
            Upload(drawn.Picture);
            _agentCpu = drawn.Picture;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            Why = $"the model would not draw: {exception.Message}";
            Drop();
        }
    }

    /// <summary>
    /// Draws the picture at the clock's new time on a task, into a canvas of its own, while the
    /// frame goes on showing the last one.
    /// </summary>
    /// <remarks>
    /// THE FRAME DOES NOT WAIT FOR THE CLOCK. A tile as large as meta_tile.tdt takes longer to draw
    /// than a frame lasts, and drawn on the frame every tick it held the whole overlay to its own
    /// pace. Behind the frame it costs the picture its rate and nothing else: the overlay stays at
    /// its own, and the picture moves on as fast as the tile can be drawn.
    ///
    /// AND ONLY THE CLOCK'S PART, where nothing but the time moved since the clock's canvas was last
    /// drawn whole: breach_boss_01 still came out at two pictures a second behind the frame, because
    /// every tick drew all of its thousand shapes for the sake of two. The canvas keeps the rest -
    /// see MeshPicture.Again - so a tick costs what the clock's own triangles cost.
    ///
    /// ONE AT A TIME, because the canvas is the drawing's until its picture has been uploaded - the
    /// next is asked for on the first frame after that. Everything the drawing reads is taken here,
    /// on the frame: the model and its programs are never changed once loaded, and the blends are
    /// a list made once per model.
    /// </remarks>
    private void ClockBehind(int size)
    {
        // NOT WHILE THE TEXTURES ARE UNPACKING: the task would decode them itself beside the unpacking, on the same cores.
        if (_clockDrawing is not null || !UnpackDone)
        {
            return;
        }

        if (_clockCanvas is not { } canvas || canvas.Size != size)
        {
            canvas = new MeshPicture.Canvas(size) { Keeps = true };
            _clockCanvas = canvas;
        }

        canvas.Time = _clock;
        canvas.Dust = _drawnDust;
        canvas.Water = _drawnWater;
        _drawnClock = _clock;
        MonsterModel model = _model;
        CameraPose view = _view;
        canvas.Pose = view;
        Vector3 ink = Ink;
        float grey = Grey ? GreyFactor : float.NaN;
        IReadOnlyList<MaterialBlend>? blends = Blends();
        IReadOnlyList<ShadeProgram?>? shades = ShadesOf(model);
        SceneLight? light = Lit();
        canvas.Light = light;
        int drawing = _drawing;

        // AGAIN, NOT WHOLE, where nothing but the time moved since the canvas was last drawn whole:
        // only the triangles that read the clock are drawn - see MeshPicture.Again. Grey is laid on
        // after, so it is not part of what has to match.
        var key = new ClockKey(model, view, ink, size, blends, shades, light);
        bool again = key == _clockKey;
        _clockKey = key;
        _clockDrawing = Task.Run(() =>
        {
            try
            {
                GamePicture drawn = again && MeshPicture.Again(canvas, out GamePicture redrawn)
                    ? redrawn
                    : MeshPicture.Of(model.Mesh, canvas, ink: ink, skin: model.Skin, skins: model.Skins, blends: blends, shades: shades);
                if (!drawn.Ready)
                {
                    return new ClockPicture(drawn, model, size, drawing, "the model drew nothing");
                }

                if (!float.IsNaN(grey))
                {
                    PictureGrey.Apply(drawn.Rgba.AsSpan(0, drawn.Width * drawn.Height * 4), grey);
                }

                return new ClockPicture(drawn, model, size, drawing, string.Empty);
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
            {
                return new ClockPicture(default, model, size, drawing, $"the model would not draw: {exception.Message}");
            }
        });
    }

    /// <summary>Everything a picture on the clock's canvas was drawn with but the time - see <see cref="ClockBehind"/>.</summary>
    /// <remarks>The lists compare by reference: the model's are made once, and the blends once per model.</remarks>
    private sealed record ClockKey(
        MonsterModel Model, CameraPose View, Vector3 Ink, int Size,
        IReadOnlyList<MaterialBlend>? Blends, IReadOnlyList<ShadeProgram?>? Shades, SceneLight? Light);

    /// <summary>A picture the clock asked for, and what it was asked for with - see <see cref="ClockLanded"/>.</summary>
    /// <param name="Picture">The pixels, on the clock's own canvas.</param>
    /// <param name="Model">The model it is of.</param>
    /// <param name="Size">The rung it was drawn at.</param>
    /// <param name="Drawing">Which of the frame's drawings it followed - see <see cref="_drawing"/>.</param>
    /// <param name="Failed">Why there is no picture, or empty.</param>
    private sealed record ClockPicture(GamePicture Picture, MonsterModel Model, int Size, int Drawing, string Failed);

    /// <summary>The buffers for one size, kept until the size changes.</summary>
    /// <remarks>
    /// REMADE ONLY ON A RUNG CHANGE. The pair is 4.5 MB at the default cap and allocating it per
    /// frame is what the canvas exists to avoid - see <see cref="MeshPicture.Canvas"/>. Rungs are
    /// crossed when a pane is resized past one and when a drag starts or ends, which is rare
    /// enough that the two collections it costs do not show.
    /// </remarks>
    private MeshPicture.Canvas Canvas(int size)
    {
        if (_canvas is not { } had || had.Size != size)
        {
            had = new MeshPicture.Canvas(size);
            _canvas = had;
        }

        return had;
    }

    /// <summary>Draws the mesh - posed, where an animation is loaded - and hands the pixels to the renderer.</summary>
    private void Render(int size, bool posed)
    {
        // WHAT IS BEING DRAWN IS REMEMBERED WHETHER OR NOT IT CAN BE SHOWN. A picture that fails
        // to upload is dropped and its reason shown, and the next frame would otherwise find
        // nothing recorded, draw the same picture again and fail again - 25 ms a frame at the top
        // rung, for as long as the reason stands. Recorded first, a failure holds still until
        // something moves, and the check in Finished then tries again on its own.
        _shown = _wanted;
        _agentDrawn = _agentTicket;
        _drawnView = _view;
        _drawnSize = size;
        _drawnPosed = posed;
        _drawnFrame = _frame;
        _drawnAnimation = _chosen;
        _drawnGrey = Greyed;
        _drawnClock = _clock;
        _drawnShades = ShadesOf(_model);
        _drawnBlends = Blends();
        _drawnLight = Lit();
        _drawnDust = Dusted();
        _drawnWater = Watered();
        _drawnSharp = Card?.Sharp ?? false;
        _unpackWaiting = false;
        _drawing++;
        long began = Stopwatch.GetTimestamp();

        try
        {
            float lowest;
            bool moving = posed && _pose is not null && _tracks is not null;
            if (moving)
            {
                // THE SKELETON ALONE IS POSED HERE: the card skins the vertices itself, and the processor's arrays are posed
                // where the processor draws (CpuPosed).
                if (_posedFor is not { } was || !ReferenceEquals(was.Model, _model) || !ReferenceEquals(was.Tracks, _tracks) || was.Frame != _frame
                    || (_swinger is not null && _drawnSwing != Swinging()))
                {
                    _pose!.Take(_tracks!, _frame);
                    Swung();
                    _posedFor = (_model, _tracks!, _frame);
                    _poseKey = Interlocked.Increment(ref _poseKeys);
                }

                // ON A STAGE THE LOWEST POINT IS NOT ASKED FOR (Planted), and finding it is a scan of the whole stage.
                lowest = _model.Staged ? 0f : Lowest();
            }
            else
            {
                lowest = _model.Mesh.Most.Z;
            }

            // THE LIGHTS HE CARRIES STAND WHERE THIS POSE PUTS THEIR BONES: the light asked again now the pose is taken, or the lantern
            // would trail its staff by a frame (Carrying keeps the object while the pose stands).
            _drawnLight = Lit();

            // ON THE CARD WHERE IT CAN DRAW THE SAME PICTURE, on the processor where it cannot.
            if (OnCard(size, moving))
            {
                _rate.Redrawn(ImGui.GetTime());
                Planted(lowest);
                return;
            }

            // THE PROCESSOR'S PICTURE WAITS FOR ITS TEXTURES rather than decoding them on this thread - seconds of a stage's BC7s; the
            // frame the unpacking behind the load ends draws it (see the redraw test).
            if (!UnpackDone)
            {
                _unpackWaiting = true;
                Drop();
                Why = string.Create(
                    CultureInfo.InvariantCulture,
                    $"unpacking textures for the processor's picture - {Array.FindAll(_unpackOf, one => one.Unpacked).Length} of {_unpackOf.Length}");
                return;
            }

            // The canvas lends its pixels rather than giving them, and LoadPixelData below copies
            // them into the image straight away - so nothing here outlives the next redraw.
            MeshPicture.Canvas canvas = Probing(Clocked(Canvas(size)), size);
            canvas.Light = _drawnLight;
            canvas.Pose = _view;

            // THE CARD'S SHADOW FRAME, the hull's, so the processor's picture stays the card's reference.
            canvas.Hull = moving ? HullOn(_drawnLight) : null;
            if (moving)
            {
                CpuPosed();
            }

            GamePicture drawn = moving
                ? MeshPicture.Of(
                    _model.Mesh, canvas, _posed, _posedNormals, ink: Ink,
                    skin: _model.Skin, skins: _model.Skins, blends: Blends(), shades: ShadesOf(_model))
                : MeshPicture.Of(
                    _model.Mesh, canvas, ink: Ink, skin: _model.Skin, skins: _model.Skins, blends: Blends(), shades: ShadesOf(_model));

            Probed(canvas);

            _rate.Redrawn(ImGui.GetTime());
            Planted(lowest);

            if (!drawn.Ready)
            {
                Why = "the model drew nothing";
                Drop();
                return;
            }

            // IN PLACE, on the canvas's own pixels, which is safe because the next draw into it
            // starts by clearing it - the picture is never greyed twice.
            if (Grey)
            {
                PictureGrey.Apply(drawn.Rgba.AsSpan(0, drawn.Width * drawn.Height * 4), GreyFactor);
            }

            Upload(drawn);
            _agentCpu = drawn;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            // The draw thread is the one place an exception costs the whole session, and every
            // input here came out of a game's files.
            Why = $"the model would not draw: {exception.Message}";
            Drop();
        }
        finally
        {
            Pictured(began);
        }
    }

    /// <summary>
    /// Draws the picture on the graphics card where it is switched on and can draw the same picture - false leaves it to the processor.
    /// </summary>
    /// <remarks>
    /// THE PROCESSOR KEEPS what reads its own pixels - the probe, and the grey laid on them - and a
    /// picture whose material shaders are still compiling for the card. Each says so on the card
    /// button rather than drawing something else. A card that fails mid-draw is not asked again;
    /// see <see cref="CardPictures.Fail"/>.
    /// </remarks>
    private bool OnCard(int size, bool moving)
    {
        _onCard = false;
        if (Card is not { } card)
        {
            return false;
        }

        // THE PROBE'S OWN DRAWING ALONE ON THE PROCESSOR - the click's, or the switch's for the layers' counts - and every
        // other on the card: drawn on the processor while the probe stood on, a room with running water came out at five
        // frames a second and held every core, and a capture pressed then took ages (said from the live client, v0.1.237).
        if ((_probing && _probeAsked) || Grey)
        {
            _cardWhy = Grey ? "the grey is laid on the processor's pixels" : "the probe reads the processor's drawing";
            return false;
        }

        if (card.Gpu is not { } gpu)
        {
            _cardWhy = card.Why;
            return false;
        }

        var scene = new ModelScene(
            _model.Mesh, 0f, 0f, Ink, _model.Skin, 1f, default, _model.Skins, Blends(),
            null, null, _drawnLight, ShadesOf(_model), _clock, _drawnDust, card.Sharp, _drawnWater,
            Sways: _model.ShapeSways.Count > 0 ? _model.ShapeSways : null, Swayed: Swayed(), Pose: _view,
            Skinned: moving ? CardSkinned() : null, Particles: ParticlesNow());
        if (!gpu.Can(scene, out string why))
        {
            // A MATERIAL'S SHADER STILL COMPILING is the usual no: the picture is the processor's until
            // it lands, and the frame it lands on draws it again - see the redraw test in Draw.
            _cardWhy = why;
            _cardWaiting = true;
            _cardLanded = gpu.Landed;
            return false;
        }

        try
        {
            if (_cardTarget is not { } target || target.Size != size)
            {
                card.Forget(_cardTarget, _cardShown);
                _cardShown = IntPtr.Zero;
                _cardTarget = target = gpu.Target(size);
                _cardShown = card.Show(target);
            }

            if (!gpu.Draw(target, scene, out why))
            {
                _cardWhy = why;
                _cardWaiting = true;
                _cardLanded = gpu.Landed;
                return false;
            }
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            // NOTHING OF THE CARD'S LEFT FOR IMGUI TO DRAW: its picture is taken back before the
            // device that made it is let go.
            if (_texture == _cardShown)
            {
                _texture = IntPtr.Zero;
            }

            card.Forget(_cardTarget, _cardShown);
            _cardTarget = null;
            _cardShown = IntPtr.Zero;
            card.Fail($"the card failed to draw: {exception.Message}");
            _cardWhy = card.Why;
            return false;
        }

        Drop();
        _texture = _cardShown;
        _onCard = true;
        _cardWaiting = false;
        _cardWhy = string.Empty;
        Why = string.Empty;
        return true;
    }

    /// <summary>
    /// The card button's tip: how the card reads textures, and why the processor drew this picture where it did.
    /// </summary>
    /// <remarks>
    /// THE CARD DRAWS, with no switch to the processor (asked for from the live client, v0.1.231): the processor
    /// draws only what the card cannot draw the same - the probe's picture, the grey, a material whose shader is
    /// still compiling, or no graphics device at all - and the tip says which.
    /// </remarks>
    private void CardTip(CardPictures card)
        => ImGui.SetTooltip(
            "Pictures are drawn on the graphics card, and on the processor only where the card cannot draw the same picture."
            + (!_onCard && _cardWhy.Length > 0 ? "\nThis one on the processor: " + _cardWhy : string.Empty)
            + "\nThe rate in the corner says which drew the picture shown."
            + (card.Sharp
                ? "\nAnisotropic texture read ON - a surface seen at a slant stays sharp along it, as in the game. Press to read trilinearly."
                : "\nAnisotropic texture read OFF - textures read trilinearly. Press to read them as the game does.")
            + " One switch for every book.");

    /// <summary>Hands a drawn picture to the renderer in place of the one shown.</summary>
    private void Upload(GamePicture drawn)
    {
        using var image = Image.LoadPixelData<Rgba32>(Contiguous, drawn.Rgba, drawn.Width, drawn.Height);

        // A NEW KEY EACH TIME, because the renderer caches by key and the pixels change on
        // every turn - reusing one hands back the picture from the first frame forever.
        Drop();
        _key = _prefix + _keys++.ToString(CultureInfo.InvariantCulture);
        _texture = _upload!(_key, image, false);
        Why = string.Empty;
    }

    /// <summary>Says so, in the lines, when the model as drawn has its lowest point well under the floor.</summary>
    /// <remarks>
    /// THE COLOSSUS FROM THE LIVE CLIENT, whose feet the reader puts five tiles under the floor
    /// and is right to - see <see cref="ModelFloor.Planted"/>. Asked for as a notice and NOT as
    /// a tooltip, and put where the other notices are.
    /// </remarks>
    private void Planted(float lowest)
    {
        // On a stage the lowest point is the stage's, and the floor the notice is about is not drawn.
        if (_model.Staged)
        {
            _planted = string.Empty;
            _plantedAt = int.MinValue;
            return;
        }

        int whole = (int)MathF.Round(lowest);
        if (whole == _plantedAt)
        {
            return;
        }

        _plantedAt = whole;
        _planted = ModelFloor.Planted(lowest, _model.Mesh.Most.Z - _model.Mesh.Least.Z);
    }

    /// <summary>
    /// Moves the mesh into the pose the skeleton holds: the whole of it, or on a stage the monster's own mesh, written placed over his slice of the joined one.
    /// </summary>
    /// <remarks>
    /// THE STAGE IS NEVER POSED. Its vertices sit in the arrays from <see cref="Rigged"/> on, and a frame
    /// writes only the monster's - skinned in his own space, then put where the join put him, a scale
    /// and a move, so the normals turn with him and are made unit length again. A monster's worth of
    /// vertices a frame, beside a picture that rasterises the whole stage.
    /// </remarks>
    private void Posed()
    {
        if (_model.Actor is not { } actor)
        {
            _pose!.Move(_model.Mesh, _posed, _posedNormals);
            return;
        }

        _pose!.Move(actor.Mesh, _actorPosed, _actorNormals);
        Matrix4x4 place = _model.ActorPlace;
        int first = _model.ActorFirst;
        int count = Math.Min(_actorPosed.Length, _posed.Length - first);
        Vector3[] own = _actorPosed, ownNormals = _actorNormals, posed = _posed, posedNormals = _posedNormals;
        void Placed(int from, int to)
        {
            for (int one = from; one < to; one++)
            {
                posed[first + one] = Vector3.Transform(own[one], place);
                Vector3 turned = Vector3.TransformNormal(ownNormals[one], place);
                float length = turned.Length();
                posedNormals[first + one] = length > 1e-6f ? turned / length : ownNormals[one];
            }
        }

        // ON EVERY CORE, as the skinning before it - each vertex is its own.
        int pieces = Math.Min(Environment.ProcessorCount, count / 4096);
        if (pieces < 2)
        {
            Placed(0, count);
            return;
        }

        Parallel.For(0, pieces, piece => Placed((int)((long)count * piece / pieces), (int)((long)count * (piece + 1) / pieces)));
    }

    /// <summary>
    /// Starts decoding every texture of the model just shown for the processor, behind its load - the one before stopped between textures.
    /// </summary>
    /// <remarks>
    /// AFTER THE LOAD, NOT IN IT (DECIDED from the live client, v0.1.326): the card draws a stage from the file's blocks at once, and
    /// the processor - the probe, the grey, a shader still compiling, no card - decodes them here on every core but the one the frame
    /// is drawn on. A processor's picture asked for meanwhile waits for it (Render); one already decoded costs nothing.
    /// </remarks>
    private void UnpackBehind()
    {
        _unpackStop?.Cancel();
        _unpackStop = null;
        _unpacking = null;
        _unpackSaid = false;
        _unpackOf = TexturesOf(_model);
        if (Array.TrueForAll(_unpackOf, one => one.Unpacked))
        {
            return;
        }

        var stop = new CancellationTokenSource();
        Mipmaps[] textures = _unpackOf;
        int threads = Math.Max(1, Environment.ProcessorCount - 1);
        _unpackStop = stop;
        _unpackBegan = Stopwatch.GetTimestamp();
        _unpacking = Task.Run(() => Mipmaps.Unpack(textures, threads, stop.Token), CancellationToken.None);
    }

    /// <summary>Every texture a model's pictures read - its own, its shapes', and each program's sheets with its twins' - once each.</summary>
    private static Mipmaps[] TexturesOf(MonsterModel model)
    {
        var textures = new HashSet<Mipmaps>(ReferenceEqualityComparer.Instance);
        var programs = new HashSet<ShadeProgram>(ReferenceEqualityComparer.Instance);
        void Program(ShadeProgram? program)
        {
            if (program is null || !programs.Add(program))
            {
                return;
            }

            foreach (Mipmaps? sheet in program.Sheets)
            {
                if (sheet is not null)
                {
                    textures.Add(sheet);
                }
            }

            Program(program.Glossy);
            Program(program.Heighted);
        }

        if (model.Skin is { } skin)
        {
            textures.Add(skin);
        }

        foreach (Mipmaps? one in model.Skins)
        {
            if (one is not null)
            {
                textures.Add(one);
            }
        }

        foreach (ShadeProgram? one in model.Shades)
        {
            Program(one);
        }

        foreach (ShadeProgram? one in model.GlossShades)
        {
            Program(one);
        }

        return [.. textures];
    }

    /// <summary>The capture's line about the unpacking, once it has ended for the model shown - how long it took and what by format.</summary>
    private void UnpackSaid()
    {
        if (_unpackSaid || _unpacking is not { IsCompleted: true })
        {
            return;
        }

        _unpackSaid = true;
        int done = 0, of = 0;
        var formats = new SortedDictionary<string, (int Count, long Ticks)>(StringComparer.Ordinal);
        foreach (Mipmaps one in _unpackOf)
        {
            if (one.Blocks is not { } blocks)
            {
                continue;
            }

            of++;
            if (!one.Unpacked)
            {
                continue;
            }

            done++;
            (int count, long ticks) = formats.GetValueOrDefault(blocks.Format.ToString());
            formats[blocks.Format.ToString()] = (count + 1, ticks + one.UnpackTicks);
        }

        double seconds = (double)(Stopwatch.GetTimestamp() - _unpackBegan) / Stopwatch.Frequency;
        string each = string.Join(", ", formats.Select(one => string.Create(
            CultureInfo.InvariantCulture, $"{one.Key} {one.Value.Count} in {(double)one.Value.Ticks / Stopwatch.Frequency:0.000} s")));
        _built.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"textures unpacked for the processor behind the build: {done} of {of} kept as blocks, {seconds:0.000} s wall on {Math.Max(1, Environment.ProcessorCount - 1)} threads - {each} (summed over threads; a texture an earlier model unpacked counts its time again)"));
    }

    /// <summary>The processor's arrays brought to the pose the skeleton holds, where they hold another - for whatever reads the posed vertices on the processor.</summary>
    private void CpuPosed()
    {
        if (_pose is null || _cpuPosedKey == _poseKey)
        {
            return;
        }

        Posed();
        _cpuPosedKey = _poseKey;
    }

    /// <summary>
    /// The pose for the card to skin: the bones' matrices in the picture's frame, the weighted mesh and where its vertices start, and the hull for the sun's map.
    /// </summary>
    /// <remarks>
    /// ON A STAGE THE BONES ARE CONJUGATED by his place (SkeletonPose.SkinAt), since the stage's mesh holds his vertices where the join
    /// put them, and the card skins those. Worked out once a pose, a hundred-odd matrices.
    /// </remarks>
    private CardSkin? CardSkinned()
    {
        if (_pose is not { } pose)
        {
            return null;
        }

        IReadOnlyList<Matrix4x4> skin = pose.Skin;
        if (_cardBonesKey != _poseKey)
        {
            if (_cardBones.Length != skin.Count)
            {
                _cardBones = new Matrix4x4[skin.Count];
            }

            pose.SkinAt(_model.Actor is null ? Matrix4x4.Identity : _model.ActorPlace, _cardBones);
            _cardBonesKey = _poseKey;
        }

        ShadowBounds? hull = HullOn(_drawnLight)?.Bounds;
        if (_cardSkin is not { } had || had.Key != _poseKey || had.Hull != hull || !ReferenceEquals(had.Matrices, _cardBones))
        {
            _cardSkin = new CardSkin(
                _model.Actor?.Mesh ?? _model.Mesh, _model.Actor is null ? 0 : _model.ActorFirst, _cardBones, skin.Count, _poseKey, hull);
        }

        return _cardSkin;
    }

    /// <summary>
    /// Where the pose lies along the sun - its first vertex and PoseHull's bounds - for the sun's map of either picture; null where the light casts no shadow.
    /// </summary>
    /// <remarks>Worked out once a pose and a sun: the bones' boxes' corners, a few hundred points.</remarks>
    private (int From, ShadowBounds Bounds)? HullOn(SceneLight? light)
    {
        if (_pose is not { } pose || light is not { SunShadows: true } || light.SunColour == Vector3.Zero)
        {
            return null;
        }

        if (!_hullMade)
        {
            _hull = PoseHull.Of(pose, _model.Actor?.Mesh ?? _model.Mesh);
            _hullMade = true;
        }

        if (_hull is not { } hull)
        {
            return null;
        }

        if (_hullFor is not { } had || had.Key != _poseKey || had.Direction != light.SunDirection)
        {
            had = (_poseKey, light.SunDirection, hull.Bounds(pose, light.SunDirection, _model.Actor is null ? Matrix4x4.Identity : _model.ActorPlace));
            _hullFor = had;
        }

        return (_model.Actor is null ? 0 : _model.ActorFirst, had.Bounds);
    }

    /// <summary>
    /// The lowest point of the pose the skeleton holds, for the notice about a monster planted under the floor: worked out again at most
    /// <see cref="LowestEvery"/> while playing, at once when paused or scrubbed.
    /// </summary>
    /// <remarks>
    /// FROM THE PROCESSOR'S ARRAYS where they hold the pose already, else SkeletonPose.Lowest - the z alone of what Move would put
    /// where, with nothing written; the card holds the posed vertices and nothing reads them back.
    /// </remarks>
    private float Lowest()
    {
        double now = ImGui.GetTime();
        if (_lowestKey != _poseKey && (_lowestKey < 0 || !_playing || now - _lowestAt >= LowestEvery))
        {
            _lowest = _cpuPosedKey == _poseKey ? Lowest(_posed) : _pose!.Lowest(_model.Mesh);
            _lowestKey = _poseKey;
            _lowestAt = now;
        }

        return _lowest;
    }

    /// <summary>The largest z among the points, which is the lowest: the model's up is -z.</summary>
    private static float Lowest(ReadOnlySpan<Vector3> places)
    {
        float lowest = float.NegativeInfinity;
        foreach (Vector3 place in places)
        {
            lowest = MathF.Max(lowest, place.Z);
        }

        return lowest;
    }

    /// <summary>
    /// The row that turns a model into icon art: the floor, the greying, the backdrop, the file - drawn by the book.
    /// </summary>
    /// <remarks>
    /// WHAT THIS ROW IS FOR, in one line: a boss the game draws no map icon for still has a
    /// model, so the icon is made from the model - pose it, pause it, press the button, and the
    /// Active and Inactive halves are on disk, cut out, at the size a sheet cell is.
    ///
    /// IN THE BOOK AND NOT THE MODEL'S WINDOW, which keeps the picture and what acts on it alone:
    /// these change what is written or how it is framed, and the window is for looking.
    ///
    /// THE SLIDER HIDES WHILE THE GREYING IS OFF. It is the one control here that says nothing
    /// until it is in use, and a pane can be narrow.
    /// </remarks>
    public void DrawExport()
    {
        if (!_model.Ready)
        {
            ImGui.TextDisabled("no model to export yet");
            return;
        }

        float wide = ImGui.CalcTextSize("0.00").X * 4f;

        bool ground = Ground;
        if (ImGui.Checkbox("floor##monster-floor", ref ground))
        {
            Ground = ground;
            Changed?.Invoke();
        }

        ImGui.SameLine();
        bool parts = Parts;
        if (ImGui.Checkbox("parts##monster-parts", ref parts))
        {
            Parts = parts;
            Changed?.Invoke();
        }

        OverlayLayout.Hint(
            "The skirt, belt, weapons and danglers a monster hangs off itself - each its own file."
            + " Off reads the body alone, which is two to three times cheaper.");

        ImGui.SameLine();
        bool grey = Grey;
        if (ImGui.Checkbox("grey##monster-grey", ref grey))
        {
            Grey = grey;
            Changed?.Invoke();
        }

        if (Grey)
        {
            // THE FACTOR IS SHARED with the exported Inactive half, so moving it here changes
            // what a written icon will look like - and since the export is derived from the
            // raw render, the pictures in the preview window follow without the model being
            // drawn again. The switch above is not shared: the export always writes both
            // halves, whether or not the pane is showing the grey one.
            ImGui.SameLine();
            ImGui.SetNextItemWidth(wide);
            float factor = GreyFactor;
            if (ImGui.SliderFloat(
                "##monster-grey-factor", ref factor, PictureGrey.Weakest, PictureGrey.Strongest, "%.2f"))
            {
                GreyFactor = factor;
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                Remake();
                Changed?.Invoke();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(GreySaid);
            }
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(wide * 1.6f);
        int behind = (int)Behind;
        if (ImGui.Combo("##monster-behind", ref behind, "viewport\0flat\0checker\0"))
        {
            Behind = (ModelBackdrop)behind;
            Changed?.Invoke();
        }

        if (Behind == ModelBackdrop.Flat)
        {
            // NOT WRITTEN DOWN, unlike the three beside it, and the difference is what each is
            // for: the keying colour only matters while a screenshot is being taken, and the
            // export needs no key at all. Magenta is where it starts every session, which is
            // the right answer for the one job it has.
            ImGui.SameLine();
            Vector4 flat = ImGui.ColorConvertU32ToFloat4(FlatBehind);
            if (ImGui.ColorEdit4(
                "##monster-flat", ref flat, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha))
            {
                FlatBehind = ImGui.ColorConvertFloat4ToU32(flat);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("PNG##monster-export"))
        {
            Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(SaveSaid);
        }
    }

    /// <summary>
    /// The buttons that write a model's files out as text, and the install-wide surveys - in the book, beside its other switches.
    /// </summary>
    /// <remarks>
    /// A TILE OR ROOM THAT DID NOT LOAD CAN STILL BE DUMPED - its own file and the reason - since "the
    /// room places no doodads" is exactly the claim somebody wants to check against the file. Only the
    /// tile book's portrait has a tileset catalogue, which is what marks it here.
    /// </remarks>
    public void DrawFiles()
    {
        bool failed = Tilesets is not null && _loading is not { IsCompleted: false } && _wanted.Length > 0 && !_model.Ready;
        bool any = false;
        if (_model.Ready || failed)
        {
            if (ImGui.Button("files##monster-dump"))
            {
                Dump();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(DumpSaid);
            }

            any = true;
        }

        if (Materials is not null)
        {
            if (any)
            {
                ImGui.SameLine();
            }

            any = true;
            if (ImGui.Button("graphs##monster-survey"))
            {
                Survey();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Reads every material in the install and writes which graph nodes keep the most of them"
                    + " from being coloured by their graphs, ranked, and how often a material's first and last blend modes"
                    + " draw it differently - " + GraphSurvey.File + " beside the dumps, with "
                    + GraphSurvey.ExamplesFile + ": a whole graph for each thing a graph reads from the vertex side. Takes a while.");
            }
        }

        if (Environments is not null)
        {
            if (any)
            {
                ImGui.SameLine();
            }

            any = true;
            if (ImGui.Button("envs##monster-envs"))
            {
                SurveyEnvironments();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Reads every .env in the install - what lights an area - and writes every key they use, in how many"
                    + " files, with its range and some values, and a few whole files that light their area with a sun - "
                    + EnvSurvey.File + " beside the dumps.");
            }
        }

        if (!any)
        {
            ImGui.TextDisabled("nothing to write until a model is read");
        }

        if (_dumped.Length > 0)
        {
            PathLink.Line(_dumped);
        }
    }

    /// <summary>What the files button is for.</summary>
    private const string DumpSaid =
        "Writes the .ao chain and the mesh manifest out as text, beside the exported pictures.\n"
        + "For questions about the format itself, which a count under the picture cannot answer.";

    /// <summary>
    /// Writes the files behind this model out as text, for reading a format rather than a model.
    /// </summary>
    /// <remarks>
    /// A FILE AND NOT A PANEL, because what this is for is being pasted somewhere. The .ao chain
    /// of a boss runs to a few kilobytes and the question it answers - what does the game
    /// actually write here - is one nobody can settle from a screenshot of a wrapped text widget.
    /// </remarks>
    private void Dump()
    {
        string at = Path.Combine(Folder, Stem(_wanted) + ".files.txt");
        if (_model.Kind is not (ModelKind.Tile or ModelKind.Room) && (_model.Ready || Tilesets is null))
        {
            Write(at, () => ModelDump.Of(_install, _variety, _wanted, _model));
            return;
        }

        // A ROOM TAKES THE TILE'S DUMP, not the monster's: it is materials, graphs and textures
        // over a joined mesh, plus each doodad's own mesh file. The monster dump walks an .ao
        // chain a room has not got, and wrote "names no .ao file" for one.

        // A TILE'S DUMP READS EVERY TILESET in the install to find the ones using it - hundreds of
        // small files - so it is written off the frame, and says so while it is. The model and the
        // key are taken now, so a click on another tile meanwhile cannot change what is written.
        // A TILE'S KEY CARRIES WHAT IS LEFT OUT after the mark - see TileBookWindow.Load.
        MonsterModel model = _model;
        string tile = _wanted.Split('|')[0];
        Func<string, byte[]?>? install = _install;
        TilesetCatalog? catalog = Tilesets;
        IReadOnlyList<string> shaders = Shaders?.Invoke() ?? [];
        IReadOnlyList<string> loaded = Loaded?.Invoke() ?? [];
        _dumped = "writing " + at + " - reading every tileset, this takes a moment";

        // AND EVERY SHADER SOURCE BESIDE IT, whole - see ModelDump.ShaderSources. One file for all
        // tiles, so it is written again with each dump rather than once per tile.
        string beside = shaders.Count > 0 ? Path.Combine(Folder, ModelDump.ShaderFile) : string.Empty;
        _ = Task.Run(() =>
        {
            if (beside.Length > 0)
            {
                Write(beside, () => ModelDump.ShaderSources(install, shaders));
            }

            Write(at, () => ModelDump.OfTile(install, tile, model, catalog?.Wait(), shaders, loaded),
                beside.Length > 0 ? $" - and the {shaders.Count} shader sources beside it, in {ModelDump.ShaderFile}" : string.Empty);
        });
    }

    /// <summary>
    /// Writes the graph survey beside the dumps, off the frame, and says how far it has got while it runs.
    /// </summary>
    private void Survey()
    {
        IReadOnlyList<string> materials = Materials?.Invoke() ?? [];
        if (materials.Count == 0)
        {
            _dumped = "no materials listed yet - the install's list is read shortly after start-up";
            return;
        }

        if (Interlocked.Exchange(ref _surveying, 1) == 1)
        {
            return;
        }

        string at = Path.Combine(Folder, GraphSurvey.File);
        Func<string, byte[]?>? install = _install;
        _dumped = "surveying the graphs of " + materials.Count + " materials";
        _ = Task.Run(() =>
        {
            // A SURVEY THAT DIED WOULD OTHERWISE LEAVE "surveying..." UP FOREVER - a faulted task
            // nobody observes - so whatever stops it is said, with its type, in the same line.
            try
            {
                // THE EXAMPLES ARE GATHERED IN THE SAME PASS and written beside the survey before it,
                // so the line under the button links the survey, which is what is read first.
                string examples = Path.Combine(Folder, GraphSurvey.ExamplesFile);
                Write(
                    at,
                    () =>
                    {
                        var gathered = new System.Text.StringBuilder();
                        string survey = GraphSurvey.Of(install, materials, step => _dumped = "surveying graphs: " + step, gathered);
                        File.WriteAllText(examples, gathered.ToString());
                        return survey;
                    },
                    " - and " + GraphSurvey.ExamplesFile + " beside it");
            }
            catch (Exception fault)
            {
                _dumped = $"the graph survey failed - {fault.GetType().Name}: {fault.Message}";
            }
            finally
            {
                Interlocked.Exchange(ref _surveying, 0);
            }
        });
    }

    /// <summary>
    /// Writes the env survey beside the dumps, off the frame, and says how far it has got while it runs.
    /// </summary>
    private void SurveyEnvironments()
    {
        IReadOnlyList<string> environments = Environments?.Invoke() ?? [];
        if (environments.Count == 0)
        {
            _dumped = "no environments listed yet - the install's list is read shortly after start-up";
            return;
        }

        if (Interlocked.Exchange(ref _surveying, 1) == 1)
        {
            return;
        }

        string at = Path.Combine(Folder, EnvSurvey.File);
        Func<string, byte[]?>? install = _install;
        _dumped = "surveying " + environments.Count + " environments";
        _ = Task.Run(() =>
        {
            try
            {
                Write(at, () => EnvSurvey.Of(install, environments, step => _dumped = "surveying " + step));
            }
            catch (Exception fault)
            {
                _dumped = $"the env survey failed - {fault.GetType().Name}: {fault.Message}";
            }
            finally
            {
                Interlocked.Exchange(ref _surveying, 0);
            }
        });
    }

    /// <summary>Writes a dump and says where, or why not, under the buttons.</summary>
    private void Write(string at, Func<string> text, string more = "")
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(at, text());
            _dumped = "wrote " + at + more;
        }
        catch (Exception fault) when (fault is IOException or UnauthorizedAccessException)
        {
            _dumped = "could not write the dump: " + fault.Message;
        }
    }

    /// <summary>
    /// Writes the pose on screen as icon art: colour and greyed, cut out, small and full size.
    /// </summary>
    /// <remarks>
    /// DRAWN AGAIN AT ITS OWN SIZE rather than taken from the pane, and that is what makes the
    /// result independent of how big somebody happened to have the window. <see cref="Shot"/>
    /// square every time, from the same camera and the same frame, so two icons made a week
    /// apart match.
    ///
    /// NOTHING IS KEYED OUT, because nothing has to be: the renderer clears its buffer to zero
    /// and draws the model into it, so the background is already alpha 0 and the floor and the
    /// widgets were never in the picture - they are drawn by the overlay around it. A screenshot
    /// of the pane would need all three undone by hand, and would still carry the edge pixels
    /// where the model blended into whatever was behind it.
    ///
    /// BOTH HALVES FROM ONE DRAW. The greying is a pass over the pixels, so the colour one is
    /// copied off first and the grey is made from the same buffer - one render, two pictures,
    /// and they cannot disagree about the pose.
    ///
    /// THE SMALL PAIR IS DOWNSCALED FROM THE BIG ONE with a proper filter and premultiplied
    /// alpha. Drawing straight at 64 would alias every edge, and resizing without premultiplying
    /// pulls the transparent background's black into the outline as a dark fringe - which is
    /// exactly the fault that makes a hand-keyed icon look cheap next to the game's own.
    /// </remarks>
    private void Save()
    {
        if (!_model.Ready)
        {
            _exported = "there is no model to write";
            return;
        }

        // DRAWN ON THE PROCESSOR, ON THIS THREAD: its textures are decoded first, behind the load, rather than here.
        if (!UnpackDone)
        {
            _exported = "the textures are still unpacking for the processor - try again in a moment";
            return;
        }

        try
        {
            _shotFolder = Folder;
            Directory.CreateDirectory(_shotFolder);

            var canvas = new MeshPicture.Canvas(Shot);
            GamePicture shot = Taken(canvas);
            if (!shot.Ready)
            {
                _exported = "the model drew nothing to write";
                return;
            }

            // THE RENDER IS KEPT RAW, so the window can lift it, grey it, put a rim on it and
            // change its mind about any of the three without the model being drawn again.
            int bytes = shot.Width * shot.Height * 4;
            _shotSource = shot.Rgba.AsSpan(0, bytes).ToArray();
            _shotWide = shot.Width;
            _shotTall = shot.Height;
            _shotStem = Stem(_wanted);
            _shotsOpen = true;

            Remake();
            Write();
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            // The draw thread is the one place an exception costs the whole session, and this
            // one touches a disk somebody else's software may have a lock on.
            _exported = $"could not make the pictures: {exception.Message}";
        }
    }

    /// <summary>
    /// Builds the four pictures from the kept renders, and shows the small pair.
    /// </summary>
    /// <remarks>
    /// THE OUTLINE IS DRAWN AT THE RENDER'S OWN SIZE and the cell is made from the result,
    /// which is what antialiases it: sixteen picture pixels to a cell pixel means the
    /// downscale does the smoothing, and the width asked for is in CELL pixels because that is
    /// the size the thing is judged at. Drawing it on the cell instead would give a hard edge
    /// that no amount of care afterwards can soften.
    ///
    /// GREY FIRST, OUTLINE SECOND, and it is the order the measurement asks for: the game's
    /// Inactive icons carry the same black rim as their Active pair, so the rim must not be
    /// greyed. Black survives a multiply anyway; the order is what keeps that true if the
    /// greying ever stops being one.
    ///
    /// Run when an export happens and whenever the outline is changed in the window - not per
    /// frame, and not while a slider is being dragged: two distance transforms and two Lanczos
    /// downscales over a megapixel each is a tenth of a second, which is fine once and awful
    /// sixty times a second.
    /// </remarks>
    private void Remake()
    {
        if (_shotSource.Length == 0 || _shotWide <= 0 || _shotTall <= 0)
        {
            return;
        }

        // THE ORDER IS THE ARGUMENT. The lift comes first, because the brightness it aims for
        // was measured on the game's art BEFORE its black rim was counted - and because a
        // curve applied after the rim would lift the rim too. The greying comes second, so
        // the Inactive half is the greyed version of the SAME picture the Active one shows
        // rather than of a dimmer one. The rim comes last and is black in both, which is what
        // the game's own pairs do.
        _shotColourFull = (byte[])_shotSource.Clone();
        PictureLight.Apply(
            _shotColourFull,
            Light ? PictureLight.GammaFor(_shotColourFull, LightTarget) : PictureLight.Untouched);
        _shotLit = PictureLight.MeanOf(_shotColourFull);

        _shotGreyFull = (byte[])_shotColourFull.Clone();
        PictureGrey.Apply(_shotGreyFull, GreyFactor);

        float radius = OutlineWidth * _shotWide / IconSheet.Tile;
        PictureOutline.Apply(_shotColourFull, _shotWide, _shotTall, radius, Outline);
        PictureOutline.Apply(_shotGreyFull, _shotWide, _shotTall, radius, Outline);

        _shotColourCell = Cell(_shotColourFull, _shotWide, _shotTall);
        _shotGreyCell = Cell(_shotGreyFull, _shotWide, _shotTall);

        Shown(_shotColourCell, _shotGreyCell);
        _shotWritten = false;
    }

    /// <summary>
    /// Writes what the window is showing: the small pair and the big one, colour and greyed.
    /// </summary>
    /// <remarks>
    /// THE DRAW IS NOT HERE, which is the split that matters. The pictures were made from the
    /// pose that was on screen; encoding four PNGs - two of them a megapixel - is a tenth of a
    /// second of somebody's frame for no reason, so it happens on a task. The arrays are the
    /// ones the window is showing rather than copies of them, and nothing writes to them
    /// except <see cref="Remake"/>, which replaces them wholesale.
    /// </remarks>
    private void Write()
    {
        if (_shotColourCell.Length == 0)
        {
            return;
        }

        string folder = _shotFolder;
        string stem = _shotStem;
        byte[] colourCell = _shotColourCell;
        byte[] greyCell = _shotGreyCell;
        byte[] colourFull = _shotColourFull;
        byte[] greyFull = _shotGreyFull;
        int wide = _shotWide;
        int tall = _shotTall;

        _exported = $"writing {stem}Active and {stem}Inactive…";
        _shotWritten = true;

        _ = Task.Run(() =>
        {
            try
            {
                Png(Path.Combine(folder, $"{stem}Active.png"), colourCell, IconSheet.Tile, IconSheet.Tile);
                Png(Path.Combine(folder, $"{stem}Inactive.png"), greyCell, IconSheet.Tile, IconSheet.Tile);
                Png(Path.Combine(folder, $"{stem}Active-{wide}.png"), colourFull, wide, tall);
                Png(Path.Combine(folder, $"{stem}Inactive-{wide}.png"), greyFull, wide, tall);

                _exported = $"wrote 4 files as {stem}Active/Inactive in {folder}";
                Written?.Invoke();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or NotSupportedException or ImageFormatException)
            {
                _exported = $"could not write the pictures: {exception.Message}";
            }
        });
    }

    /// <summary>Draws the model as it stands, at the export's own size.</summary>
    private GamePicture Taken(MeshPicture.Canvas canvas)
    {
        canvas.Light = Lit();
        canvas.Pose = _view;
        canvas.Hull = null;
        if (_tracks is { Ready: true } && _pose is not null)
        {
            _pose.Take(_tracks, _frame);
            Swung();

            // THE SKELETON NOW HOLDS THIS POSE, whatever the frame's said - see _posedFor - and the arrays are brought to it.
            _posedFor = (_model, _tracks, _frame);
            _poseKey = Interlocked.Increment(ref _poseKeys);
            CpuPosed();
            canvas.Hull = HullOn(canvas.Light);
            return MeshPicture.Of(
                _model.Mesh, Clocked(canvas), _posed, _posedNormals, ink: Ink,
                skin: _model.Skin, skins: _model.Skins, blends: Blends(), shades: ShadesOf(_model));
        }

        return MeshPicture.Of(_model.Mesh, Clocked(canvas), ink: Ink, skin: _model.Skin, skins: _model.Skins, blends: Blends(), shades: ShadesOf(_model));
    }

    /// <summary>The canvas with the clock the picture is at - the export takes the instant on screen.</summary>
    private MeshPicture.Canvas Clocked(MeshPicture.Canvas canvas)
    {
        canvas.Time = _clock;
        canvas.Dust = _drawnDust;
        canvas.Water = _drawnWater;
        return canvas;
    }

    /// <summary>The canvas with the probe's pixel and the layers' tally set where the probe is on, and neither where not.</summary>
    /// <remarks>
    /// THE ASK IS LET GO OF HERE, BEFORE THE DRAWING, so a picture that fails to draw is not asked
    /// for again on every frame - the same reason Render records what it draws before drawing it.
    /// </remarks>
    private MeshPicture.Canvas Probing(MeshPicture.Canvas canvas, int size)
    {
        canvas.Tally = _probing ? _tally : null;
        canvas.Probe = _probing && _probeAsked && _probeAt is { } at
            ? new PixelProbe((int)MathF.Floor(at.X * size), (int)MathF.Floor(at.Y * size))
            : null;
        _probeAsked = false;
        return canvas;
    }

    /// <summary>Turns what a probing drawing recorded into the lines under the picture, and lets go of the probe.</summary>
    private void Probed(MeshPicture.Canvas canvas)
    {
        if (!_probing)
        {
            return;
        }

        IReadOnlyList<ShadeProgram?>? shades = ShadesOf(_model);
        if (!ReferenceEquals(_probedModel, _model))
        {
            _probedModel = _model;
            _probeLines = [];
            _probedShapes = [];
            _probeCopied = false;
        }

        bool clicked = false;
        if (canvas.Probe is { Drawn: true } probe)
        {
            _probeLines = PictureProbe.Lines(probe, _model, shades);

            // THE SHAPES UNDER IT TOO, nearest first, for the capture: their materials are written whole (CapturePanes) - and after
            // them what cast a shadow on them (ProbeSun), so a shadow-only caster's material is in the capture as well.
            _probedShapes =
            [
                .. probe.Fragments.OrderBy(one => one.Depth).Select(one => one.Triangle)
                    .Concat(probe.Suns.Where(one => one.Caster >= 0).Select(one => one.Caster))
                    .Select(one => PictureProbe.ShapeOf(_model.Mesh, one)).Where(shape => shape >= 0).Distinct(),
            ];
            clicked = true;
        }

        _layerLines = PictureProbe.Layers(_tally, _model, shades);
        canvas.Probe = null;

        // EACH CLICK'S LINES INTO THE CLIPBOARD as well, asked for from the live client: one click and they are ready to paste,
        // without a capture. Only on a click - a redraw for a turn or the clock recounts the layers and copies nothing.
        if (clicked)
        {
            ImGui.SetClipboardText(string.Join('\n', _probeLines.Concat(_layerLines)));
            _probeCopied = true;
        }
    }

    /// <summary>Whether the last click's lines went into the clipboard - said at the top of the probe's box.</summary>
    private bool _probeCopied;

    /// <summary>
    /// One picture shrunk to a sheet cell, as pixels.
    /// </summary>
    /// <remarks>
    /// DOWNSCALED RATHER THAN DRAWN SMALL, with a proper filter: a model rasterised straight
    /// at 64 aliases every edge it has, while sixteen times that shrunk down is the same
    /// antialiasing the game's own art has.
    ///
    /// PREMULTIPLIED FIRST, which is the part that is easy to leave out and impossible to miss
    /// afterwards. The background is transparent BLACK, and a filter that averages colour
    /// without weighting by alpha pulls that black into every edge - a dark fringe all round
    /// the model, which is exactly what makes a hand-made icon look cheap beside a real one.
    /// </remarks>
    private static byte[] Cell(byte[] rgba, int wide, int tall)
    {
        using Image<Rgba32> full = Image.LoadPixelData<Rgba32>(Contiguous, rgba, wide, tall);
        using Image<Rgba32> cell = full.Clone(picture => picture.Resize(new ResizeOptions
        {
            Size = new Size(IconSheet.Tile, IconSheet.Tile),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Lanczos3,
            PremultiplyAlpha = true,
        }));

        var pixels = new byte[IconSheet.Tile * IconSheet.Tile * 4];
        cell.CopyPixelDataTo(pixels);
        return pixels;
    }

    /// <summary>Writes pixels to a PNG, alpha and all.</summary>
    private static void Png(string path, byte[] rgba, int wide, int tall)
    {
        using Image<Rgba32> picture = Image.LoadPixelData<Rgba32>(Contiguous, rgba, wide, tall);
        picture.SaveAsPng(path);
    }

    /// <summary>
    /// Hands the two finished cells to the renderer, so the pane can show what was written.
    /// </summary>
    /// <remarks>
    /// A NEW KEY EACH TIME, for the reason the model's own upload gives: the renderer caches by
    /// key, and reusing one would show the first export's pictures for the rest of the session.
    /// The previous pair is given back first, or an evening of posing leaks a texture a click.
    /// </remarks>
    private void Shown(byte[] colour, byte[] grey)
    {
        if (_upload is null)
        {
            return;
        }

        DropShots();

        using Image<Rgba32> shownColour =
            Image.LoadPixelData<Rgba32>(Contiguous, colour, IconSheet.Tile, IconSheet.Tile);
        using Image<Rgba32> shownGrey =
            Image.LoadPixelData<Rgba32>(Contiguous, grey, IconSheet.Tile, IconSheet.Tile);

        _shotColourKey = _prefix + "shot." + _keys++.ToString(CultureInfo.InvariantCulture);
        _shotGreyKey = _prefix + "shot." + _keys++.ToString(CultureInfo.InvariantCulture);
        _shotColour = _upload(_shotColourKey, shownColour, false);
        _shotGrey = _upload(_shotGreyKey, shownGrey, false);
    }

    /// <summary>
    /// Gives the preview pair's TEXTURES back to the renderer, and nothing else.
    /// </summary>
    /// <remarks>
    /// TEXTURES ONLY, and the boundary is load-bearing. This runs twice for two different
    /// reasons - before a new pair is uploaded, and when the pane is done with the old one -
    /// and it once did the second job in both places: an export released its own handles,
    /// emptied the pictures it had just made and closed the window, so the button rendered a
    /// model, showed nothing and wrote nothing. Everything that is not a handle now lives in
    /// <see cref="ForgetShots"/>, which only the second caller reaches.
    /// </remarks>
    private void DropShots()
    {
        if (_shotColourKey.Length > 0)
        {
            _release?.Invoke(_shotColourKey);
        }

        if (_shotGreyKey.Length > 0)
        {
            _release?.Invoke(_shotGreyKey);
        }

        _shotColour = IntPtr.Zero;
        _shotGrey = IntPtr.Zero;
        _shotColourKey = string.Empty;
        _shotGreyKey = string.Empty;
    }

    /// <summary>Drops the pair and everything that was kept to make it again.</summary>
    /// <remarks>
    /// For when the pane is finished with a monster. Six of these arrays are a megapixel of
    /// RGBA each, which is worth holding while they are on screen and worth nothing at all
    /// once they are not - and a window left open over the NEXT monster would be two pictures
    /// with nothing saying what they are of.
    /// </remarks>
    private void ForgetShots()
    {
        DropShots();

        _shotStem = string.Empty;
        _shotsOpen = false;
        _shotWritten = false;

        // The three fields go with the monster they were filled in for. Left standing they
        // would offer the last boss's area over the next one's picture, which is the one way
        // this could write a confident, wrong entry.
        _entryFor = string.Empty;
        _entryAreas = string.Empty;
        _entryTile = string.Empty;
        _entryName = string.Empty;
        _remembered = string.Empty;
        _shotSource = [];
        _shotColourFull = [];
        _shotGreyFull = [];
        _shotColourCell = [];
        _shotGreyCell = [];
        _shotLit = 0f;
    }

    /// <summary>
    /// What to call the files: the monster's own file name, with nothing in it a path dislikes.
    /// </summary>
    /// <remarks>
    /// The LAST part of the metadata path, because that is what the monster is called in the
    /// game's own files - BloodKnightBossMAP2 - and it is what somebody renaming these to match
    /// a sheet cell will recognise. A trailing underscore goes: several of the boss varieties
    /// carry one and it would end up in the middle of "…_Active".
    /// </remarks>
    private static string Stem(string path)
    {
        // A BOOK'S KEY MAY CARRY WORDS after the mark - a tile's ground choice, the tileset it is
        // drawn as, whose path would otherwise name every dump "mastertsi" - so the stem is the part before.
        int mark = path.IndexOf('|', StringComparison.Ordinal);
        return BossIcons.FamilyOfPath(mark >= 0 ? path[..mark] : path) is { Length: > 0 } named ? named : "monster";
    }

    /// <summary>
    /// Paints what is behind the model.
    /// </summary>
    /// <remarks>
    /// THREE, AND EACH ANSWERS A DIFFERENT QUESTION. Blender's viewport grey is what the pane
    /// was built to look like and what a model reads best against. A flat colour is for a
    /// screenshot somebody will key by hand. And the checkerboard answers "what will actually
    /// be in the file" - the picture's background is transparent and always was, so this is the
    /// only one of the three that shows the truth about it.
    ///
    /// THE CHECKERBOARD DRAWS ONLY ITS DARK SQUARES, over one filled rectangle of the light
    /// one. Half the squares, and on a 1200 px pane that is the difference between twenty-eight
    /// hundred rectangles a frame and fourteen hundred, for a picture that looks the same.
    /// </remarks>
    private void Paint(ImDrawListPtr draw, Vector2 corner, Vector2 far)
    {
        if (Behind != ModelBackdrop.Checker)
        {
            draw.AddRectFilled(corner, far, Behind == ModelBackdrop.Flat ? FlatBehind : Backdrop);
            return;
        }

        Checkered(draw, corner, far);
    }

    /// <summary>
    /// The transparency checkerboard, behind the pane's picture or behind a preview.
    /// </summary>
    /// <remarks>
    /// ONLY THE DARK SQUARES ARE DRAWN, over one filled rectangle of the light one. Half the
    /// squares, and on a 1200 px pane that is the difference between twenty-eight hundred
    /// rectangles a frame and fourteen hundred, for a picture that looks the same.
    /// </remarks>
    private static void Checkered(ImDrawListPtr draw, Vector2 corner, Vector2 far)
    {
        draw.AddRectFilled(corner, far, CheckerLight);

        var squares = 0;
        for (float y = corner.Y; y < far.Y && squares < CheckerMost; y += Checker)
        {
            for (float x = corner.X; x < far.X && squares < CheckerMost; x += Checker)
            {
                // Every other square, offset row by row, which is what makes it a checkerboard
                // rather than stripes.
                if ((int)((x - corner.X) / Checker) % 2 != (int)((y - corner.Y) / Checker) % 2)
                {
                    continue;
                }

                draw.AddRectFilled(
                    new Vector2(x, y),
                    new Vector2(MathF.Min(x + Checker, far.X), MathF.Min(y + Checker, far.Y)),
                    CheckerDark);
                squares++;
            }
        }
    }

    private void Floor(ImDrawListPtr draw, Vector2 corner, float side, in MeshPicture.Camera camera)
    {
        // A STAGE IS ITS OWN FLOOR: the squares would be drawn at the tile's lowest point, under its
        // ground, where they say nothing about where the monster stands.
        if (!Ground || _model.Staged)
        {
            return;
        }

        ModelFloor.Of(_floor, camera, side, _tile);
        Strokes(draw, corner, side, _floor);
    }

    /// <summary>
    /// Writes the floor's pieces into the draw list, each fading from one end to the other.
    /// </summary>
    /// <remarks>
    /// STRAIGHT INTO THE VERTEX BUFFER, and not one AddLine per piece. A line of ImGui's is one
    /// colour from end to end, and the frame's fade is not - so the fade has to be carried by the
    /// vertices, which AddLine does not take. And there are a few thousand pieces on a frame, each
    /// of which through the wrapper is a call across the interop boundary per vertex; one reserve
    /// per piece and a pointer walk is the same picture for a fraction of the frame.
    ///
    /// THE PROFILE IS THE ONE IMGUI DREW THIN LINES WITH before it baked them into its atlas: a
    /// solid centre and a one-pixel feather to nothing on either side, on the white pixel of the
    /// font atlas so that the current texture serves. That is a two-pixel tent holding one pixel's
    /// worth of ink, which is what an anti-aliased line of width one is.
    /// </remarks>
    private static unsafe void Strokes(ImDrawListPtr draw, Vector2 corner, float side, List<ModelFloor.Line> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        Vector2 white = ImGui.GetFontTexUvWhitePixel();
        ImDrawList* list = draw.NativePtr;
        float feather = list->_FringeScale;

        foreach (ModelFloor.Line line in lines)
        {
            Vector2 a = corner + (line.From * side);
            Vector2 b = corner + (line.To * side);
            Vector2 run = b - a;
            float length = run.Length();
            if (!(length > 1e-3f))
            {
                continue;
            }

            Vector2 across = new Vector2(-run.Y, run.X) * (feather / length);
            uint ink = line.Stroke switch
            {
                ModelFloor.Stroke.AxisX => AxisX,
                ModelFloor.Stroke.AxisY => AxisY,
                _ => Grid,
            };

            uint fromInk = ink | ((uint)Math.Clamp(line.FromAlpha * 255f, 0f, 255f) << 24);
            uint toInk = ink | ((uint)Math.Clamp(line.ToAlpha * 255f, 0f, 255f) << 24);

            // Six vertices - the feather's two edges and the centre at each end - and four
            // triangles between them. Reserved per piece, so ImGui's own check for running past
            // sixteen-bit indices runs per piece too.
            draw.PrimReserve(12, 6);
            ImDrawVert* vertex = list->_VtxWritePtr;
            ushort* index = list->_IdxWritePtr;
            uint at = list->_VtxCurrentIdx;

            vertex[0] = new ImDrawVert { pos = a - across, uv = white, col = ink };
            vertex[1] = new ImDrawVert { pos = a, uv = white, col = fromInk };
            vertex[2] = new ImDrawVert { pos = a + across, uv = white, col = ink };
            vertex[3] = new ImDrawVert { pos = b - across, uv = white, col = ink };
            vertex[4] = new ImDrawVert { pos = b, uv = white, col = toInk };
            vertex[5] = new ImDrawVert { pos = b + across, uv = white, col = ink };

            index[0] = (ushort)at;
            index[1] = (ushort)(at + 1);
            index[2] = (ushort)(at + 4);
            index[3] = (ushort)at;
            index[4] = (ushort)(at + 4);
            index[5] = (ushort)(at + 3);
            index[6] = (ushort)(at + 1);
            index[7] = (ushort)(at + 2);
            index[8] = (ushort)(at + 5);
            index[9] = (ushort)(at + 1);
            index[10] = (ushort)(at + 5);
            index[11] = (ushort)(at + 4);

            list->_VtxWritePtr = vertex + 6;
            list->_IdxWritePtr = index + 12;
            list->_VtxCurrentIdx = at + 6;
        }
    }

    private void Drop()
    {
        if (_key.Length > 0)
        {
            _release?.Invoke(_key);
        }

        _texture = IntPtr.Zero;
        _key = string.Empty;
    }

    private static string Said(AggregateException? fault)
        => fault?.InnerException?.Message ?? fault?.Message ?? "the model could not be read";

    private static Configuration Contiguously()
    {
        Configuration configuration = IconCache.WithoutTiff();
        configuration.PreferContiguousImageBuffers = true;
        return configuration;
    }
}

/// <summary>What is painted behind the model in the pane.</summary>
/// <remarks>
/// THREE, AND THE THIRD IS THE HONEST ONE. The picture itself has a transparent background and
/// always did - the renderer clears its buffer to zero - so the viewport grey and a flat colour
/// are both something the overlay paints UNDER it, and only the checkerboard shows what a file
/// written from it will actually contain.
/// </remarks>
public enum ModelBackdrop
{
    /// <summary>Blender's viewport grey, which the pane was built around.</summary>
    Viewport,

    /// <summary>One flat colour, for a screenshot somebody will key out by hand.</summary>
    Flat,

    /// <summary>The transparency checkerboard: what is really behind the model, which is nothing.</summary>
    Checker,
}
