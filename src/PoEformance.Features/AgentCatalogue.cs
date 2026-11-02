namespace PoEformance.Features;

/// <summary>
/// Every tool the agent link offers, as the agent sees them listed - the one place they are named and described.
/// </summary>
/// <remarks>
/// DATA, NOT HANDLERS: the App binds the tools that need the process (status, log, update) and the overlay those that
/// need the frame (the capture, the books, the pane), each by name (<see cref="AgentLink.Bind"/>). The cloud bridge
/// lists the same tools while the tool cannot be reached, from tools/agent/tools.json, which a test keeps equal to this
/// list - so a tool added here reaches the bridge in the same push.
/// </remarks>
public static class AgentCatalogue
{
    public const string Status = "status";
    public const string Front = "front";
    public const string Log = "log";
    public const string Update = "update";
    public const string Capture = "capture";
    public const string CaptureParts = "capture_parts";
    public const string Captures = "captures";
    public const string Screenshot = "screenshot";
    public const string MonsterBook = "monster_book";
    public const string TileBook = "tile_book";
    public const string Pane = "pane";

    public static IReadOnlyList<AgentToolInfo> Tools { get; } =
    [
        new(Status,
            "What the tool is doing now: its version and build, the game it is attached to, the area and the player's place, which window is in front (the overlay draws only while the game or one of its own windows is), a capture under way, the update check, the tunnel and the model panes. Call this first.",
            "{\"type\":\"object\",\"properties\":{}}"),
        new(Front,
            "Brings the game's window to the front, as every call does first anyway where another window is: the window is only raised - no key is sent to the game. Answers with who is in front after.",
            "{\"type\":\"object\",\"properties\":{}}"),
        new(Log,
            "The tail of overlay.log, the tool's own log of this session (or of the session before, kept as overlay.previous.log).",
            "{\"type\":\"object\",\"properties\":{\"lines\":{\"type\":\"integer\",\"description\":\"How many of the last lines (default 200, at most 5000).\"},\"previous\":{\"type\":\"boolean\",\"description\":\"Read the session before's log instead.\"}}}"),
        new(Update,
            "The tool's own update from its GitHub release: check whether a newer build is published, download it, or install it. Install downloads first where needed and then RESTARTS THE TOOL; the link comes back on a new tunnel address within a minute or two (the bridge finds it).",
            "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"check\",\"download\",\"install\"],\"description\":\"Default check.\"}}}"),
        new(Capture,
            "Takes a capture for diagnosis as the capture key (F9) does: one zip of the pictures, the recording and every part asked for. The game is brought to the front first; it begins on the next frame the game or one of the tool's windows is in front, hides the overlay for a moment and may use every core for minutes; a long one is answered as pending and collected by the bridge. Answers with the capture's folder and its zip offered for download (the bridge's download tool fetches and unpacks it).",
            "{\"type\":\"object\",\"properties\":{\"parts\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"The parts to write, by key (capture_parts lists them); every other part is off for this capture. Default: the parts ticked in the tool's settings.\"},\"wait_minutes\":{\"type\":\"number\",\"description\":\"How long to wait for the capture to end (default 30).\"},\"cancel\":{\"type\":\"boolean\",\"description\":\"Withdraw a capture asked for that has not begun yet.\"}}}"),
        new(CaptureParts,
            "Every part a capture can write, by key, with its group, file, what it is for and whether the settings tick it.",
            "{\"type\":\"object\",\"properties\":{}}"),
        new(Captures,
            "The captures in the tool's captures folder, newest first - the player's own presses of F9 among them - and, for one of them, its zip offered for download.",
            "{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\",\"description\":\"How many to list (default 10).\"},\"offer\":{\"type\":\"string\",\"description\":\"A capture's folder name to offer for download, or latest.\"}}}"),
        new(Screenshot,
            "The game's window as it is on the screen now, the overlay included, as a picture; the full size offered for download too.",
            "{\"type\":\"object\",\"properties\":{\"width\":{\"type\":\"integer\",\"description\":\"The widest the picture shown may be (default 1600); the download keeps the full size.\"}}}"),
        new(MonsterBook,
            "Opens the Monster Book and shows a monster in its model pane - by name or path - and stands it on a stage, or shows the player as the game draws him. Answers with the pane's lines and, by default, its picture once drawn.",
            "{\"type\":\"object\",\"properties\":{\"monster\":{\"type\":\"string\",\"description\":\"A monster's path (Metadata/Monsters/...) or name, or words of either.\"},\"stage\":{\"type\":\"string\",\"description\":\"none, a label the stage list offers (arena: ..., laid in play: ..., built · ...: ..., the Tile Book's: ..., around you), or a stage key or tile path.\"},\"stages\":{\"type\":\"boolean\",\"description\":\"List the stages offered for the monster shown.\"},\"around\":{\"type\":\"integer\",\"description\":\"The side of the area round you in tiles, 3 to 25 (default 9).\"},\"player\":{\"type\":\"boolean\",\"description\":\"Take the player as the game draws him now and show him.\"},\"weapon_set\":{\"type\":\"integer\",\"description\":\"The player's weapon set to show, from 1.\"},\"picture\":{\"type\":\"boolean\",\"description\":\"Wait for the model pane to draw what was asked and return its picture (default true).\"},\"timeout\":{\"type\":\"number\",\"description\":\"Seconds to wait for the model to build and the picture to be drawn (default 120, at most 900).\"}}}"),
        new(TileBook,
            "Opens the Tile Book and shows a tile or room in its model pane - by path or words of it - or the area round you as the game laid it. Answers with the pane's lines and, by default, its picture once drawn.",
            "{\"type\":\"object\",\"properties\":{\"tile\":{\"type\":\"string\",\"description\":\"A tile's .tdt or a room's .arm path, or words of one.\"},\"around\":{\"type\":\"integer\",\"description\":\"Show the area round you, this many tiles a side (3 to 25; 0 for the side set).\"},\"doodads\":{\"type\":\"integer\",\"description\":\"A room's doodad cap.\"},\"tools\":{\"type\":\"boolean\",\"description\":\"Draw a room's tool shapes.\"},\"as_laid\":{\"type\":\"boolean\",\"description\":\"A room as the area laid it rather than as written.\"},\"fitted\":{\"type\":\"boolean\",\"description\":\"A laid room fitted to the area's ground rather than at its tiles' level.\"},\"picture\":{\"type\":\"boolean\",\"description\":\"Wait for the model pane to draw what was asked and return its picture (default true).\"},\"timeout\":{\"type\":\"number\",\"description\":\"Seconds to wait for the model to build and the picture to be drawn (default 120, at most 900).\"}}}"),
        new(Pane,
            "Works a book's model pane as a hand does: the view (the edge buttons, or a camera pose), the animation, the clock, the light panel's switches and values, the probe at a pixel. Answers with the pane's lines (the probe's among them) and, by default, the picture drawn after the changes - the card's, or the processor's where the card cannot draw it.",
            "{\"type\":\"object\",\"properties\":{\"book\":{\"type\":\"string\",\"enum\":[\"monster\",\"tile\"],\"description\":\"Default monster.\"},\"view\":{\"type\":\"string\",\"enum\":[\"reset\",\"game\",\"perspective\",\"parallel\"],\"description\":\"An edge button: fit the view again, the game's view, or the projection.\"},\"camera\":{\"type\":\"object\",\"description\":\"A pose: focus [x,y,z] in the model's frame, turn and tilt in radians, distance, parallel.\",\"properties\":{\"focus\":{\"type\":\"array\",\"items\":{\"type\":\"number\"}},\"turn\":{\"type\":\"number\"},\"tilt\":{\"type\":\"number\"},\"distance\":{\"type\":\"number\"},\"parallel\":{\"type\":\"boolean\"}}},\"animation\":{\"type\":\"string\",\"description\":\"An animation's name, or words of it.\"},\"playing\":{\"type\":\"boolean\",\"description\":\"Run or stop the clock.\"},\"light\":{\"type\":\"object\",\"description\":\"Light panel switches by label to true or false: game light, room, carried, flicker, swing, sun, shadows, player, exposure, colour grade, emissive, glints, reflection, scene, refraction, caustics, crash, way round, past walls, break by ground, wetness, spray from occlusion, fog, clouds, roof fade, free sun.\",\"additionalProperties\":{\"type\":\"boolean\"}},\"light_values\":{\"type\":\"object\",\"description\":\"Light panel values by label: lift, wind, flat, ambient (0 none, 1 flat, 2 cube), sun round, sun height.\",\"additionalProperties\":{\"type\":\"number\"}},\"shows\":{\"type\":\"string\",\"description\":\"What the picture shows, the light panel's shows row: Final Color, Albedo, Specular, Gloss, Roughness, World Normal (Fit), World Normal (True), Lighting Only.\"},\"probe\":{\"description\":\"A pixel to probe as shares of the picture across and down, for example {\\\"x\\\":0.5,\\\"y\\\":0.5}, or the word off.\",\"anyOf\":[{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"}}},{\"type\":\"string\"}]},\"built\":{\"type\":\"boolean\",\"description\":\"Add where the model's build spent its time.\"},\"picture\":{\"type\":\"boolean\",\"description\":\"Wait for the model pane to draw what was asked and return its picture (default true).\"},\"timeout\":{\"type\":\"number\",\"description\":\"Seconds to wait for the model to build and the picture to be drawn (default 120, at most 900).\"}}}"),
    ];
}
