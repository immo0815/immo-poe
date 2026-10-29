namespace PoEformance.Features;

/// <summary>
/// What a capture's memory pass reads once more inside the recording, as the ticked parts ask for it - see <see cref="CaptureParts"/>.
/// </summary>
/// <remarks>
/// THE PASS IS CUT TO THE QUESTION. One recording used to read everything the tool knows how to read,
/// and most of it was for nobody: a question about a room's doodads needs the entity maps and
/// nothing of the raw sweep, and a question about a light needs the sweep and nothing of the maps.
/// Each part says what the pass must read for it, and the pass reads the union - the recording is
/// then the overlay's own reads for a few seconds, which it always is, plus exactly what was asked.
/// </remarks>
[Flags]
public enum CaptureReads
{
    /// <summary>Nothing beyond the overlay's own reads.</summary>
    None = 0,

    /// <summary>A fresh world read with every switch on: the terrain, every entity the game lists, the player, the camera.</summary>
    World = 1,

    /// <summary>The area's loaded-file list.</summary>
    Loaded = 2,

    /// <summary>Both entity maps walked whole, with every named entity's path, position and model - see SleepingDoodads.</summary>
    Doodads = 4,

    /// <summary>The raw sweep: the chain's roots whole, one hop beyond them, and every component of the entities around the player - see CaptureSweep.</summary>
    Sweep = 8,

    /// <summary>Every monster's mods off its ObjectMagicProperties, with the world read that lists the monsters - see CaptureMonsters.</summary>
    Monsters = 16,

    /// <summary>The terrain's two cell grids whole, with the world read whose ground types they are held against - see CaptureLandscape.</summary>
    Landscape = 32,
}

/// <summary>One thing a capture can write, as the catalogue lists it - see <see cref="CaptureParts"/>.</summary>
/// <param name="Key">Its id, which the settings keep when it is unticked - stable across rewordings.</param>
/// <param name="Group">The heading it sits under on the Keys page.</param>
/// <param name="Label">What its box says.</param>
/// <param name="File">The file it writes in the capture's folder, or empty where it only shapes the recording.</param>
/// <param name="Hint">What it writes and what it is for, as the box's hover.</param>
/// <param name="Reads">What the memory pass reads for it, where the recording is on.</param>
public sealed record CapturePart(string Key, string Group, string Label, string File, string Hint, CaptureReads Reads = CaptureReads.None)
{
    /// <summary>
    /// Whether the part is written only once somebody ticks it - for a part that runs for hours, which every press of the key would
    /// otherwise wait for (the stage sweep). The rest are on by default; see <see cref="CaptureParts"/>' remarks.
    /// </summary>
    public bool OffByDefault { get; init; }

    /// <summary>The box's hover: the file it writes, then the hint - made once, since a page draws it every frame.</summary>
    public string Hover { get; } = File.Length > 0 ? File + ": " + Hint : Hint;

    /// <summary>The box's ImGui label, its id after the label - made once for the same reason.</summary>
    public string Box { get; } = Label + "##capture-" + Key;
}

/// <summary>
/// Everything the capture key can write, as one catalogue: what each part is, where it goes, and what it asks of the memory recording.
/// </summary>
/// <remarks>
/// ONE PLACE FOR EVERY DIAGNOSIS, instead of a copy button beside every line that somebody once had
/// to send. The Tile Book had five, the routes page one, the light fold one, every model pane one,
/// the dissector two, the atlas log one - each put one text on the clipboard, each had to be found,
/// and a question about a room took three of them and a screenshot. The capture key already wrote a
/// folder with the pictures and the rooms around the player; now every one of those texts is a part
/// of that folder, and the key writes the parts that are ticked. A new diagnosis is a new part here,
/// a writer for its file, and nothing on any window.
///
/// ALL ON BY DEFAULT, and the settings keep the keys that are OFF (OverlaySettings.CaptureOff): a
/// part added in a later build is then written without anybody ticking it, and a settings file that
/// never touched the boxes gains no key. What a part costs when nothing is there to write is one
/// line in capture.txt saying so. BUT ONE THAT RUNS FOR HOURS is off until ticked (CapturePart.OffByDefault,
/// DECIDED from the live client for the stage sweep, 2026-10-29), and the settings keep those switched ON
/// apart (OverlaySettings.CaptureOn) - so it too stays as it was left, and a later one starts off.
///
/// THE RECORDING FOLLOWS THE TICKS - see <see cref="CaptureReads"/>. A part that reads memory says
/// what it needs, <see cref="Reads"/> takes the union over the ticked ones, and the pass inside
/// the recording reads that and no more.
/// </remarks>
public static class CaptureParts
{
    /// <summary>The game alone, the overlay hidden for the shot.</summary>
    public const string GamePicture = "game-picture";

    /// <summary>The game with the overlay over it, as the key went down.</summary>
    public const string OverlayPicture = "overlay-picture";

    /// <summary>Every entity the overlay read this frame.</summary>
    public const string Entities = "entities";

    /// <summary>Every monster with what the game put on it - mods, buffs, the targetable byte - and the marked places beside them.</summary>
    public const string Monsters = "monsters";

    /// <summary>The files the area loaded.</summary>
    public const string LoadedFiles = "loaded-files";

    /// <summary>The area's environment and the rooms' around the player.</summary>
    public const string Environments = "environments";

    /// <summary>Every room file the area loaded, whole.</summary>
    public const string RoomFiles = "room-files";

    /// <summary>The rooms arranged around the player, with their lights and files.</summary>
    public const string RoomsNear = "rooms-near";

    /// <summary>Every room arranged by its ground and tiles.</summary>
    public const string RoomsArranged = "rooms-arranged";

    /// <summary>The Tile Book's picked row against the rooms on the map, and where it parts with the area.</summary>
    public const string RoomPick = "room-pick";

    /// <summary>Every tile file the area laid, as a room's slot asks about it.</summary>
    public const string TileIdentities = "tile-identities";

    /// <summary>Every room of the area's set, each kind of its slots against the tiles the area's tilesets offer.</summary>
    public const string SlotCandidates = "slot-candidates";

    /// <summary>The area's tile lists whole.</summary>
    public const string TilesetLists = "tileset-lists";

    /// <summary>Every tile file the lists name or the area laid, its definition followed hop by hop, and its doodad list (.tmd).</summary>
    public const string TileFiles = "tile-files";

    /// <summary>The area's masters whole, and every file they name.</summary>
    public const string Masters = "masters";

    /// <summary>What the laid tiles' ground is made of: its blocks, its materials, and the shader sources naming its coordinates.</summary>
    public const string Ground = "ground";

    /// <summary>The laid tiles' sub-tiles of two grounds over the game's picture, with their ground shapes that run across the mask's atlas.</summary>
    public const string GroundScreen = "ground-screen";

    /// <summary>What the water is drawn from: every environment's water keys, and the shader sources of the water pass.</summary>
    public const string Water = "water";

    /// <summary>Where the engine's ripple texture may be: the textures whose paths name the water, the likeliest decoded, and the program's strings naming one.</summary>
    public const string WaterTextures = "water-textures";

    /// <summary>The survey of the area's entity maps for the rooms' doodads, every sighting listed.</summary>
    public const string Doodads = "doodads";

    /// <summary>Every room placed by its doodads.</summary>
    public const string RoomsPlaced = "rooms-placed";

    /// <summary>The terrain's second cell grid against the walkable one and the ground under it.</summary>
    public const string Landscape = "landscape";

    /// <summary>The active routes.</summary>
    public const string Routes = "routes";

    /// <summary>The light hunt's report.</summary>
    public const string SceneLight = "scene-light";

    /// <summary>The model panes' probe lines.</summary>
    public const string ModelProbe = "model-probe";

    /// <summary>The model panes' lines under their pictures, and every doodad file of a room shown with what became of it.</summary>
    public const string ModelPanes = "model-panes";

    /// <summary>The Monster Book's monster: where the stage stands him, his parts as hung, his lowest point in every animation, his files whole, and the game's entity of him.</summary>
    public const string MonsterFiles = "monster-files";

    /// <summary>The Memory Dissector's places, route and last search.</summary>
    public const string Dissector = "dissector";

    /// <summary>The atlas check's report.</summary>
    public const string AtlasLog = "atlas-log";

    /// <summary>The overlay's own log, this session's and the one before: its window, its graphics device and the game's window.</summary>
    public const string OverlayLog = "overlay-log";

    /// <summary>The light hunt run at the press: the sun, the sky's turn, the dust and the water as the game's memory holds them.</summary>
    public const string LightHunt = "light-hunt";

    /// <summary>The wobble points and the doodads by them, watched for seconds from the press: what sways a ship.</summary>
    public const string Wobble = "wobble";

    /// <summary>The install's files of the engine's wobble: the object types, every path naming one, the sources and the program's strings.</summary>
    public const string WobbleFiles = "wobble-files";

    /// <summary>What the engine's shore field is made from: every object script naming the water's blocking or flow, the sources and strings.</summary>
    public const string WaterShore = "water-shore";

    /// <summary>Every string the game's program, data tables and paths hold naming a debug, cheat, test, console or logging function.</summary>
    public const string DebugStrings = "debug-strings";

    /// <summary>The engine's own debug views: its graphs whole, what the compile makes of them, their sources and the program's table of them.</summary>
    public const string RenderDebug = "render-debug";

    /// <summary>The terrain and what it points to searched at the press for the grid the engine's ocean markup is made from.</summary>
    public const string OceanMarkup = "ocean-markup";

    /// <summary>Every room of every room set in the install built as a Monster Book stage, and what fails counted by cause - off until ticked.</summary>
    public const string StageSweep = "stage-sweep";

    /// <summary>The memory recording itself.</summary>
    public const string Memory = "memory";

    /// <summary>The raw sweep inside the recording.</summary>
    public const string RawSweep = "raw-sweep";

    /// <summary>The loaded-file table walked for its count field - once the Loaded Files page's "find the count field" button.</summary>
    public const string CountField = "count-field";

    /// <summary>What the area's room files name - once the Loaded Files page's "look inside the rooms" button.</summary>
    public const string RoomsInside = "rooms-inside";

    /// <summary>The area's room files and the type files they declare, decoded whole - once the Loaded Files page's "write the rooms out" button.</summary>
    public const string RoomsExported = "rooms-exported";

    /// <summary>Whatever in memory refers to one of the area's room files - once the Loaded Files page's "hunt the placements" button.</summary>
    public const string RoomPlacements = "room-placements";

    /// <summary>What the runecraft panel read, row by row - once the Runecraft page's "what the panel read" block.</summary>
    public const string RunecraftPanel = "runecraft-panel";

    /// <summary>
    /// Whether a part is a piece of work started at the press, off the drawing thread, and waited for when the files are written - see CaptureKey.OfferWork.
    /// </summary>
    /// <remarks>
    /// THE LOADED FILES PAGE'S FOUR BUTTONS, moved here (S8 of the labels review): diagnoses on a player page, each answering in
    /// lines under the buttons that nobody playing reads. They read the file table and the bundles, which takes seconds, so they
    /// cannot be asked for on the frame the key goes down as a window's text is.
    /// </remarks>
    public static bool RunsAtPress(string key) => key is CountField or RoomsInside or RoomsExported or RoomPlacements or Wobble or OceanMarkup;

    /// <summary>
    /// A heading as the Keys page writes it. The headings themselves stay as they are: they are what <see cref="FolderOfGroup"/> keys
    /// the folders by, and only their wording on the page was changed.
    /// </summary>
    public static string GroupLabel(string group) => group switch
    {
        "Pictures" => "pictures",
        "Entities" => "entities",
        "Area files" => "area",
        "Rooms by ground and tiles" => "rooms by tiles",
        "Rooms by doodads" => "rooms by doodads",
        "Windows" => "windows",
        "Memory" => "memory",
        _ => group,
    };

    /// <summary>The headings, in the order the Keys page shows them.</summary>
    public static IReadOnlyList<string> Groups { get; } =
        ["Pictures", "Entities", "Area files", "Rooms by ground and tiles", "Rooms by doodads", "Windows", "Memory"];

    /// <summary>
    /// The folders a capture's files are sorted into, in its folder and its zip alike - one a heading, the rooms' two headings
    /// sharing one; capture.txt and the replays stay at the top (asked for from the live client, 2026-10-13).
    /// </summary>
    public static IReadOnlyList<string> Folders { get; } = ["pictures", "entities", "area", "rooms", "windows", "mem"];

    /// <summary>
    /// Each file a part writes by its folder, the memory pass's index beside the recording - see <see cref="FolderOf"/>. Made on
    /// first use: a field made from <see cref="All"/> above it in the file would be made before <see cref="All"/> is.
    /// </summary>
    private static Dictionary<string, string>? _fileFolders;

    private static Dictionary<string, string> FileFolders => _fileFolders ??= Filed();

    /// <summary>A heading's folder, or empty for none.</summary>
    public static string FolderOfGroup(string group) => group switch
    {
        "Pictures" => "pictures",
        "Entities" => "entities",
        "Area files" => "area",
        "Rooms by ground and tiles" or "Rooms by doodads" => "rooms",
        "Windows" => "windows",
        "Memory" => "mem",
        _ => string.Empty,
    };

    /// <summary>
    /// The folder a file of a capture goes to: its part's, by the file the part writes - empty for capture.txt and anything no
    /// part names, which stay at the top. The pictures and dumps a part writes beside its file go where its file goes.
    /// </summary>
    public static string FolderOf(string file) => FileFolders.GetValueOrDefault(file, string.Empty);

    /// <summary>Where a file of a capture is written, its folder made where it is not yet there.</summary>
    /// <param name="capture">The capture's folder.</param>
    /// <param name="file">The file, by its name.</param>
    public static string PathOf(string capture, string file)
    {
        string folder = FolderOf(file);
        if (folder.Length == 0)
        {
            return Path.Combine(capture, file);
        }

        string directory = Path.Combine(capture, folder);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, file);
    }

    /// <summary>
    /// Where a file of an unpacked capture is - at the top, as every capture before v0.1.260 and every fixture has it, or in one of
    /// <see cref="Folders"/> - or where it would be at the top, where it is in neither: the offline readers' one way in.
    /// </summary>
    /// <param name="capture">The capture's folder.</param>
    /// <param name="file">The file, by its name.</param>
    public static string Find(string capture, string file)
    {
        string top = Path.Combine(capture, file);
        if (File.Exists(top))
        {
            return top;
        }

        foreach (string folder in Folders)
        {
            string inside = Path.Combine(capture, folder, file);
            if (File.Exists(inside))
            {
                return inside;
            }
        }

        return top;
    }

    private static Dictionary<string, string> Filed()
    {
        var filed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [CaptureMemory.IndexFile] = "mem" };
        foreach (CapturePart part in All)
        {
            if (part.File.Length > 0)
            {
                filed.TryAdd(part.File, FolderOfGroup(part.Group));
            }
        }

        return filed;
    }

    /// <summary>Every part, grouped as <see cref="Groups"/> orders them.</summary>
    public static IReadOnlyList<CapturePart> All { get; } =
    [
        new(GamePicture, "Pictures", "game only", CaptureReport.GameShot,
            "The game as it was, the overlay hidden for the shot - what the game drew, with nothing of the tool over it."),
        new(OverlayPicture, "Pictures", "game + overlay", CaptureReport.OverlayShot,
            "The game with the overlay over it, as the key went down - what you were looking at."),

        new(Entities, "Entities", "entities", CaptureReport.EntitiesFile,
            "Every entity the overlay read this frame, nearest first: where it stands, what it is, and where in memory it was. "
            + "With the recording on, the world is read once more inside it with every switch on - visuals, effects, buffs, aim, "
            + "actions, as many entities as the game lists, and the terrain.",
            CaptureReads.World),
        new(Monsters, "Entities", "monster mods & buffs", CaptureReport.MonstersFile,
            "Inside the recording: every monster the game lists, nearest first, with its mods by the game's own ids, its buffs "
            + "with their clocks, its life and its targetable byte - and every marked place standing on it, an essence prison "
            + "being one. Two captures, before and after an essence is clicked, say what the game puts on an imprisoned "
            + "monster and takes off a freed one. Needs the recording.",
            CaptureReads.Monsters),

        new(LoadedFiles, "Area files", "area loaded files", CaptureReport.LoadedFile,
            "Every file the area loaded, as the game lists them - which rooms, tiles, models and environments the area is built from. "
            + "With the recording on, the list is read once more inside it.",
            CaptureReads.Loaded),
        new(Environments, "Area files", "environments", CaptureReport.EnvironmentsFile,
            "The area's own environment and those the rooms around you name, whole - the light, the fog, the sky."),
        new(RoomFiles, "Area files", "room files", CaptureReport.RoomFilesFile,
            "Every room file (.arm) the area loaded, whole: the doodad lines and the slots each room is placed by. With "
            + "doodads.txt it is the whole of what the placing works from, so a placing can be run again without the game."),

        new(RoomsNear, "Rooms by ground and tiles", "nearby rooms", CaptureReport.RoomsFile,
            "Every room arranged within three tiles of yours, its doodads' lights and the pieces attached to them, and the file "
            + "of each room you stand in - so a lamp in the picture can be found in a file."),
        new(RoomsArranged, "Rooms by ground and tiles", "arranged rooms", CaptureReport.ArrangedFile,
            "Where the ground-and-tile search arranged every room of the area, what disagrees there, and how many of its tiles "
            + "can be walked - the Tile Book's \"rooms:\" line and its hover, as the map draws them without an entity read."),
        new(RoomPick, "Rooms by ground and tiles", "tile book pick", CaptureReport.PickFile,
            "The row picked in the Tile Book's room list held against the rooms on the map, and every place it parts with the "
            + "area - the two folds under the list. Nothing where no row is picked."),
        new(TileIdentities, "Rooms by ground and tiles", "tile definitions", CaptureReport.TileIdentitiesFile,
            "Every distinct tile file the area laid, with what a room's slot is checked against - size, tag, edge and corner ground "
            + "types. With the recording's terrain and room-files.txt it is what the ground-and-tile search runs again from without the game."),
        new(Landscape, "Rooms by ground and tiles", "projectile grid", CaptureReport.LandscapeFile,
            "Inside the recording: the terrain's two cell grids whole - the walkable one, and GridLandscapeData beside it, whose "
            + "meaning is not settled - counted against each other, against the ground under every cell, and along the edge of "
            + "where anybody can stand, with both grids round you one character a cell. Whether the second is the grid a "
            + "projectile asks - walls blocking, chasms not - is what it answers. Needs the recording.",
            CaptureReads.Landscape),
        new(SlotCandidates, "Rooms by ground and tiles", "slot candidates", CaptureReport.SlotCandidatesFile,
            "Every room of the area's set, each kind of its slots with the tiles the area's tilesets OFFER that answer it, by the search's rule - "
            + "one tile settles the kind by the files alone, several is the generator's choice - with a summary over the rooms and the arena rooms, "
            + "and every offered tile's definition after it. The measure of whether a boss room could be built without standing in its area."),
        new(TilesetLists, "Area files", "tile lists", CaptureReport.TilesetListsFile,
            "The area's tile lists (.tst) whole, each master's and every list it includes: the weight the generator draws each tile by and the "
            + "words after it. What a room is built from without the area, and where the words' meaning is to be read rather than guessed."),
        new(TileFiles, "Area files", "tile file chains", CaptureReport.TileFilesFile,
            "Every tile file the tile lists or fill lists name or the area laid: its definition followed through every file it inherits, "
            + "and the tile's own doodad list (.tmd, beside the .tdt or its template) whole. Every area lays files no tile list names - "
            + "this says for each whether a listed file inherits it, a fill list names it or a listed file shares its template."),
        new(Masters, "Area files", "master file", CaptureReport.MastersFile,
            "Every master (.tsi) of the area whole, and every file it names - room set, fill list (FillTiles), anything else - whole too; "
            + "a tile list by name, being whole in tileset-lists.txt; then the area's ground types whole, whose names a fill list's sections "
            + "are keyed by. The keys of a master this tool does not read yet are where to look."),
        new(Ground, "Area files", "ground", CaptureReport.GroundFile,
            "What the laid tiles' ground is made of, as the files say: every template's ground blocks by vertex format, with what "
            + "the fields the reader steps over hold; each corner's ground by name and what the master's MaterialsList draws it with, "
            + "the ground materials and their graphs, each colour texture beside it as a picture; the install's shader sources that name where the ground's texture "
            + "coordinates come from and how its materials are blended, whole; every laid template whole; each masked template's "
            + "ground shapes sub-tile by sub-tile with their unnamed ordinals and the mask's green, the biggest drawn from above "
            + "beside it; and every laid tile definition's bytes past what is read. What stands between the rooms' white ground "
            + "and a painted one."),
        new(GroundScreen, "Area files", "ground on screen", CaptureReport.GroundScreenFile,
            "The laid tiles' ground laid over the game's own picture where the Monster Book's laid room has it, each piece at its tiles' own level: every sub-tile of two "
            + "grounds or more on the screen outlined and named as ground.txt names it, and its ground shapes whose coordinates run "
            + "across the mask's atlas drawn - where the tool's mix comes out as a checkerboard - so which ground the game draws there "
            + "is read off its picture (ground-on-screen.png). Each sub-tile's grid corners beside the game's own corner types at that "
            + "place, and each shape's height, green and reach across the mask. And the two ways two grounds may be mixed held against "
            + "the game's picture pixel by pixel - the mask's green and the corners' share, and each weighed by the two grounds' heights as the "
            + "engine's height blending weighs them, every ground's own program run at each pixel - with moved controls (ground-on-screen-corners.png "
            + "tinted by the corners, ground-on-screen-heights.png by the mask weighed by the heights). And which material of its group each "
            + "sub-tile's pattern follows, every material the group offers read over the sub-tile and lined up with the picture's detail, "
            + "shifted up to 24 units for its best line-up, against the same material shifted where nothing lines up "
            + "(ground-on-screen-choices.png tinted by the winners). Best with \"game only\" ticked: over the "
            + "overlay's picture the overlay's own drawing is in the way."),
        new(Water, "Area files", "water", CaptureReport.WaterFile,
            "What the water is drawn from, as the files say: every key the install's environments write in their water "
            + "sections, how many files carry it and what values, every environment's water section file by file - to pick an "
            + "area by what its file sets - and the shader sources of the engine's water pass whole - "
            + "the colours, the waves, the refraction. Water materials are no ordinary ones (\"shadertype\": \"Ocean\"), and "
            + "this is what stands between a white water plane and one drawn as the game does."),
        new(WaterTextures, "Area files", "water textures", CaptureReport.WaterTexturesFile,
            "Where the engine's ripple texture may be - the one its waves' eight octaves read, 128 texels a side, red the height and "
            + "green and blue its slope, which no shader source names: every texture whose path names the water with its size and "
            + "format, those 128 square marked, the likeliest decoded channel by channel as pictures beside it, and the strings the "
            + "game's own program holds that name one. And the engine's lookups (Art/2DArt/Lookup/) the walk or the program names - the "
            + "ripple texture, the swell's profiles and gradient - read as floats, each written whole beside it, with what the water "
            + "takes of them and why. What stands between the tool's stand-in waves and the game's. And the engine's beach waves - the "
            + "foam that floods the rocks: the graphs the program names for them (BreakingOceanWaves and its kin) whole, every graph "
            + "they name, and the textures they name with their headers - and the water's own (water_default and its wetness caster, "
            + "the flowing kin) whole. And the two pass textures the water's caustics and rocky shore "
            + "read and no file names (caustics_tex_sampler, distortion_sampler): every candidate - water_caustics_2.dds and "
            + "muddle_double.dds, which the program names, and the install's other caustics and flow textures - measured as water.ffx "
            + "reads it and written whole beside it."),

        new(WobbleFiles, "Area files", "wobble files", CaptureReport.WobbleFilesFile,
            "What the install says of the engine's wobble - what sways Port's ship, whose model has no rig and no animation: the object "
            + "types (.ot, .otc, .ao, .aoc) of every loaded path naming a wobble and of the doodads that may and may not sway, each "
            + "followed through what it extends, whole; every install path naming a wobble, the text files among them whole; the "
            + "shader sources naming one, and the game's program's strings naming one."),

        new(WaterShore, "Area files", "water's shore", CaptureReport.WaterShoreFile,
            "What the engine's shore field is made from - where its swell runs, which in Port runs between the ship and the quay while "
            + "the tool's runs in under the hull: every object script (.ot, .otc) in the install searched for the water's blocking and "
            + "flow (SetWaterBlocking, SetWaterFlow, the program's script commands), each line naming them and the first scripts whole; "
            + "the area's loaded object types that reach one through what they extend; the shader sources naming the shore field; and "
            + "the game's program's strings naming a shore, the water's blocking or a markup. And the sea's spray thrown up a quay: "
            + "the engine's spray trigger graphs (CrashingSprayTrigger and its kin) whole, then hop by hop what names them - the "
            + "materials, the particles and trails naming those, the effect packages, objects and environments naming those. "
            + "Searches some tens of thousands of files on every core."),

        new(DebugStrings, "Area files", "debug strings", CaptureReport.DebugStringsFile,
            "What the game left in of its debugging: every string its own program holds (ASCII and UTF-16, with its section and "
            + "address), every string of its data tables and every install path that names a debug or dbg function, a cheat, a "
            + "developer's or test switch, the console or a visualisation (show_, draw_, toggle, wireframe, profiler, overlay), or an "
            + "assert, a trace, a dump or a log - grouped so, the words beside each. The program is searched on three cores, the "
            + "tables on every core; the offline reader sorts them by shape (C++ type names, identifiers, chat commands, paths)."),

        new(RenderDebug, "Area files", "render debug views", CaptureReport.RenderDebugFile,
            "The engine's own debug views, to build them in the Monster Book: every graph under Metadata/EngineGraphs/ whole - the "
            + "debug_*.fxgraph first (albedo, normals, overdraw, walkability, the ocean and river markup and some forty more) - and "
            + "every graph they name; what the compile makes of each, node type by node type, and the node types it does not know "
            + "yet; the textures they name with their headers; the debug pass's shader sources whole, and those naming an unknown "
            + "node; and every string of the game's program round its table of the views, at its address."),

        new(StageSweep, "Area files", "stage sweep (hours)", CaptureReport.StageSweepFile,
            "Every room of every room set in the install, each under every master naming its set, built as the Monster Book builds a "
            + "stage from its tileset - and what fails counted over all of them, ranked by how many rooms it reaches: surfaces drawn "
            + "white and why, ground left plain, pieces not whole, slots no tile answered, doodads not drawn, effect objects drawn, "
            + "water drawn white or as an ordinary material, and what the compile left out of a colour or lost of the alpha and the "
            + "other channels; then every room a row. So one mend of a building block is found once for every scene. Runs for hours "
            + "on every core - OFF UNTIL TICKED, and a capture with it ticked waits for it (its stop in the capture's window keeps "
            + "what it built).")
        {
            OffByDefault = true,
        },

        new(CountField, "Area files", "count field sweep", CaptureReport.CountFieldFile,
            "The loaded-file table walked for its count field instead of assuming one: what the area-change counter reads, how many "
            + "slots were walked, records read and paths readable - their difference says which part of the walk is wrong - and the "
            + "buckets in hex where the walk reached nothing. Run at the press; what the Loaded Files page's \"find the count field\" did."),
        new(RoomsInside, "Area files", "room contents", CaptureReport.RoomsInsideFile,
            "Every room file the area loaded, opened out of the game's bundles, with the strings it names - edge and ground types, tiles. "
            + "Needs the install. Run at the press; what the Loaded Files page's \"look inside the rooms\" did."),
        new(RoomsExported, "Area files", "room export", CaptureReport.RoomsExportedFile,
            "Every room file the area loaded and every type file they declare, decoded whole into one file - the format read away from the "
            + "game. Needs the install. Run at the press; what the Loaded Files page's \"write the rooms out\" did."),
        new(RoomPlacements, "Area files", "placement hunt", CaptureReport.RoomPlacementsFile,
            "Whatever in the game's memory refers to one of the area's room files, by the addresses of their records in the loaded-file "
            + "table, with every other file's record as the control. Run at the press; what the Loaded Files page's \"hunt the "
            + "placements\" did."),
        new(Doodads, "Rooms by doodads", "doodads", CaptureReport.DoodadsFile,
            "The survey of the area's entity maps for the rooms' doodads: each room's share, each path found and how many stand, "
            + "then every sighting with its id, path, model and position. With the recording on, both maps are walked once more "
            + "inside it with every named entity's path, position and model, so the placing can be run again from the recording.",
            CaptureReads.Doodads),
        new(RoomsPlaced, "Rooms by doodads", "placed rooms", CaptureReport.PlacedFile,
            "Every place each room's doodads stand, with hits, lines, offset and tiles agreeing, what it yielded to whom, or why "
            + "it has none - the Tile Book's \"rooms:\" line and its hover, as the map draws them."),

        new(Routes, "Windows", "active routes", CaptureReport.RoutesFile,
            "Where you stand and every route - its end and its stops - in world units, as the Routes page lists them. Nothing where none is set."),
        new(SceneLight, "Windows", "light hunt", CaptureReport.SceneLightFile,
            "The whole report of the last \"find the readings\" in the Tile Book's Light fold: every reading, where each was found, "
            + "the raw angles beside them. Nothing where none has run."),
        new(ModelProbe, "Windows", "model probe", CaptureReport.ProbeFile,
            "What the last probe click in a model pane listed under that pixel - on water, the swell's foam and the field waves term "
            + "by term - and how each translucent layer came out over the picture - for every book whose probe is on. Nothing where none is."),
        new(ModelPanes, "Windows", "model panes", CaptureReport.PanesFile,
            "Every model pane showing a model: its lines under the picture, a room stage's own among them, and every doodad file "
            + "of the room it shows with what became of its lines - laid, not drawn, left out by the cap, unseen - its box, where "
            + "its first line puts it and its materials; each file that did not draw written whole with what it extends; the materials "
            + "under the probe, those drawn plain and those naming a shore's node or with nodes left out, whole, each program texture's "
            + "header beside them. Nothing where no pane shows a model."),
        new(MonsterFiles, "Windows", "monster's files", CaptureReport.MonsterFilesFile,
            "The Monster Book's monster as the tool builds and stands it: where the stage put him and the floor it found at nine points round "
            + "his place, the rest's lowest point, each part with its socket and box, his lowest point and root bone in every animation, each "
            + "socketed part's bone in every animation, every part with a rig of its own played by that rig, and "
            + "every .ao and object type file of him whole - beside the game's own entity of him where one is near (its place, the terrain's "
            + "height under it, its bounds, the animation it plays). Stand where the game shows him with him on the Monster Book's stage."),
        new(Dissector, "Windows", "dissector", CaptureReport.DissectorFile,
            "The places the Memory Dissector has open, the route walked to them, and what its last search found with the bytes "
            + "round each place. Nothing where it is untouched."),
        new(AtlasLog, "Windows", "atlas", CaptureReport.AtlasLogFile,
            "The Atlas page's \"Check the Read\" report whole, as its Debug Log tab has it. Nothing where it has not run."),
        new(OverlayLog, "Windows", "overlay log", CaptureReport.OverlayLogFile,
            "The overlay's own log, this session's and the one before - kept beside the tool, so a session ended because the "
            + "overlay stayed away is still there after the restart: the game leaving the front and coming back with the overlay "
            + "window as it stood, what Present answered, the graphics device lost and made anew, frames that stood still, the "
            + "restore key. No offline reader: the lines are the reading."),

        new(RunecraftPanel, "Windows", "runecraft panel", CaptureReport.RunecraftPanelFile,
            "What the Runecraft page read off the game's runecraft panel: its status, what the game calls the elements found, where the "
            + "first row's text was measured, and every row with what priced it and through which door. Nothing where the panel was never seen."),
        new(LightHunt, "Memory", "light & water hunt", CaptureReport.LightHuntFile,
            "The Tile Book light panel's search of the game's memory, run at the press for the area's own environment: "
            + "where the game keeps the sun, the sky's turn and the dust colour, and the water - the water pass's uniform "
            + "block as common.ffx declares it, read out by name, its colours among them, and the file's water values in the "
            + "window round the parsed environment, printed whole, with every value the area's file sets mapped to its slot there. "
            + "What the engine draws an area's water with where its file "
            + "sets no colour. It looks through up to sixteen gigabytes on every core: the capture's window shows how far it is "
            + "and where its time goes, and stops it on a click - what it found until then is written, as it is at five minutes."),
        new(Wobble, "Memory", "wobble watch", CaptureReport.WobbleFile,
            "Every entity carrying a WobblePoint - the wobble core the game lays beside a ship - and the doodads standing by it, "
            + "watched for twenty seconds from the press: the WobblePoint as fast as it reads, every component of the doodads and one "
            + "hop beyond each pointer in them twenty times a second, with a doodad that may not sway as the control. Every field that "
            + "changes is written with its range and period, the first read of every block whole, and the WobblePoint's changing "
            + "fields as a table over time - what moves the ship, by how much and how often. Stand within sight of the ship; the "
            + "window shows how far it is and stops it on a click."),
        new(OceanMarkup, "Memory", "ocean markup hunt", CaptureReport.OceanMarkupFile,
            "Where the engine keeps its sea's shore: the area's terrain - the game's tile map, whose CalculateMapMarkup fills the grid "
            + "the engine's OceanMarkupField is made from - and every object it points to, searched for lists whose length is a grid "
            + "of the area's shape (its tiles, their corners or its cells, at several resolutions). Each found is written as a map, "
            + "its values counted, beside the walkable grid and the corners' ground types at the same places - what the tool's own "
            + "shore, chosen until then, is to be held against. A few megabytes of reads, past the recording. Then the numbers the "
            + "engine draws its shore field by, sought in the whole of the game's memory on every core: beside every place the "
            + "scene's size is kept (250 world units a tile, both ways round, and the walkable grid's cells), the texture sizes, "
            + "boxes, int pairs and small factors standing there - its box, its texture's size and dist_to_color among them - each "
            + "window written whole. It stops itself at ninety seconds; the capture's window shows how far it is and stops it on a click."),
        new(Memory, "Memory", "memory recording", CaptureReport.MemoryFile,
            "The overlay's own reads for three seconds, and inside them one pass reading once more from nothing whatever the "
            + "ticked parts above ask for - memory.txt beside it says what the pass read where. Replays with --replay, game closed."),
        new(RawSweep, "Memory", "in raw bytes", string.Empty,
            "In the recording: the game's root objects whole, one hop beyond every pointer in them, and every component of the "
            + "entities around you - the bytes nothing reads yet, for the questions nobody has asked. A few megabytes.",
            CaptureReads.Sweep),
    ];

    /// <summary>The parts under one heading, in the catalogue's order.</summary>
    public static IEnumerable<CapturePart> In(string group) => All.Where(part => string.Equals(part.Group, group, StringComparison.Ordinal));

    /// <summary>
    /// Whether a part exists only inside the recording - nothing of it is written when the recording is off.
    /// </summary>
    /// <remarks>
    /// The Keys page greys these while the recording's box is unticked, so what they would add
    /// stays readable and a tick that would write nothing is not offered.
    /// </remarks>
    public static bool NeedsRecording(string key) => key is RawSweep or Monsters or Landscape;

    /// <summary>Whether a part is written, given the keys that are off.</summary>
    public static bool IsOn(string key, IReadOnlySet<string> off)
    {
        ArgumentNullException.ThrowIfNull(off);
        return !off.Contains(key);
    }

    /// <summary>
    /// What the pass inside the recording reads for the parts that are on - nothing where the recording itself is off.
    /// </summary>
    public static CaptureReads Reads(IReadOnlySet<string> off)
    {
        ArgumentNullException.ThrowIfNull(off);
        if (off.Contains(Memory))
        {
            return CaptureReads.None;
        }

        CaptureReads reads = CaptureReads.None;
        foreach (CapturePart part in All)
        {
            if (!off.Contains(part.Key))
            {
                reads |= part.Reads;
            }
        }

        return reads;
    }

    /// <summary>The keys that are off, with any the catalogue no longer knows dropped - and those off by default left out: what the settings keep as off.</summary>
    public static IReadOnlyList<string> Kept(IReadOnlySet<string> off)
    {
        ArgumentNullException.ThrowIfNull(off);
        return [.. All.Where(part => !part.OffByDefault && off.Contains(part.Key)).Select(part => part.Key)];
    }

    /// <summary>The parts off by default that are on - what the settings keep as on (OverlaySettings.CaptureOn).</summary>
    public static IReadOnlyList<string> KeptOn(IReadOnlySet<string> off)
    {
        ArgumentNullException.ThrowIfNull(off);
        return [.. All.Where(part => part.OffByDefault && !off.Contains(part.Key)).Select(part => part.Key)];
    }

    /// <summary>The keys that are off, from the settings' two lists: those switched off, and every part off by default not switched on.</summary>
    /// <param name="off">The parts switched off (OverlaySettings.CaptureOff).</param>
    /// <param name="on">The parts off by default switched on (OverlaySettings.CaptureOn).</param>
    public static IReadOnlySet<string> OffOf(IReadOnlyList<string> off, IReadOnlyList<string> on)
    {
        ArgumentNullException.ThrowIfNull(off);
        ArgumentNullException.ThrowIfNull(on);
        var keys = new HashSet<string>(off, StringComparer.Ordinal);
        foreach (CapturePart part in All)
        {
            if (part.OffByDefault && !on.Contains(part.Key, StringComparer.Ordinal))
            {
                keys.Add(part.Key);
            }
        }

        return keys;
    }
}
