using System.Numerics;
using System.Runtime.Versioning;
using ImGuiNET;
using PoEformance.Core.Diagnostics;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PoEformance.Overlay;

/// <summary>
/// One key, and every ticked part of the catalogue goes into one folder: the pictures, the rooms, the entities, the area's files, what the diagnostic windows hold, and a recording of memory read once more from nothing.
/// </summary>
/// <remarks>
/// ASKED FOR so that what the tool's work keeps needing from the game arrives together. Until now
/// it came a piece at a time - a screenshot here, a Tile Book dump there, a recording from another
/// session - and pieces from different spots and builds do not line up. One press takes them from
/// one place at one moment; see <see cref="CaptureParts"/> for what each part is and
/// <see cref="CaptureReport"/> for what each file holds.
///
/// THE CATALOGUE DECIDES WHAT IS WRITTEN. Each part has a box on the Keys page, and the key writes
/// the ticked ones - so a question about a room's doodads is a capture of the doodads and the rooms,
/// and no sweep of raw bytes to go with it. The windows' own diagnoses - the routes, the light hunt,
/// the probe, the dissector, the atlas check - are parts too, written as they stood when the key
/// went down: the window offers its text through <see cref="Offer"/>, and the capture asks for it
/// on the frame of the press, where the window's state is safe to read.
///
/// TWO PICTURES, AND THE ORDER IS THE POINT. The one with the overlay is taken the moment the key is
/// seen, while the screen still shows the frame before. Then the overlay draws NOTHING for a few
/// frames - at least <see cref="HiddenFrames"/> and <see cref="HiddenMs"/>, since the compositor
/// shows a frame some time after it was drawn and a fast frame rate would otherwise outrun it - and
/// the game alone is taken. Hiding is what makes the second picture the game's whatever the window
/// manager does with a layered window, and a tenth of a second of no overlay on a key somebody
/// pressed to take a picture is not a cost anybody will notice. With that picture unticked the
/// overlay is not hidden at all.
///
/// BOTH ON THIS THREAD, the one place a frame's hitch is acceptable: the overlay must stay hidden
/// until the copy is done, and the copy is tens of milliseconds. The encoding and the files are
/// written on the pool.
///
/// THE ROOMS NEED THE ROOM SEARCH, the one the large map's outlines use, and it takes seconds the
/// first time in an area; the doodad survey follows it. So the capture waits for them - up to
/// <see cref="RoomWaitMs"/> - and writes without them if they are not done by then, saying why. It
/// waits only where a ticked part wants them.
///
/// ONE WINDOW SAYS WHERE IT IS (<see cref="CaptureProgress"/>), from the end of the hiding to the
/// zip: every ticked part, the long ones - the rooms, the memory hunt, the recording - with what
/// they are doing, and a summary once it is written. It stays up until closed or the next press.
///
/// OBSERVED, NEVER SWALLOWED, like the screenshot key: the game gets the key too, so the row that
/// binds it says what the game itself does with it, read from the player's own config.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class CaptureKey
{
    /// <summary>F9, until somebody binds another.</summary>
    public const int DefaultKey = 0x78;

    /// <summary>Frames drawn empty before the game alone is taken.</summary>
    public const int HiddenFrames = 3;

    /// <summary>And the least time they take, whatever the frame rate.</summary>
    public const int HiddenMs = 120;

    /// <summary>How long to wait for the room search and the doodad survey before writing without them.</summary>
    public const int RoomWaitMs = 60_000;

    /// <summary>How long the files wait for a piece of work started at the press - see <see cref="OfferWork"/>.</summary>
    private const int WorkWaitSeconds = 120;

    /// <summary>How long the files wait for the memory half - past the longest a recording runs.</summary>
    private const int MemoryWaitSeconds = 90;

    private readonly Func<WorldSnapshot> _snapshot;
    private readonly Func<ClientRect> _client;
    private readonly Func<IReadOnlyList<(string Room, RoomLayout Layout, RoomCandidate Where)>?> _standing;
    private readonly Func<bool> _roomsPossible;
    private readonly Func<(int Done, int Of)> _roomsProgress;
    private readonly Func<AreaRooms?> _areaRooms;
    private readonly Func<IReadOnlyList<string>> _loaded;
    private readonly Func<IReadOnlyList<string>> _masters;
    private readonly Func<string> _version;

    /// <summary>The parts switched off, by key - what the settings keep. See CaptureParts. Those off by default are off until the settings say otherwise.</summary>
    private readonly HashSet<string> _off = new(CaptureParts.OffOf([], []), StringComparer.Ordinal);

    /// <summary>The windows' diagnoses, by part key - each asked for its text as the key goes down. See <see cref="Offer"/>.</summary>
    private readonly Dictionary<string, Func<string?>> _offers = new(StringComparer.Ordinal);

    /// <summary>The diagnoses that take seconds, by part key - each started as the key goes down. See <see cref="OfferWork"/>.</summary>
    private readonly Dictionary<string, Func<string, IReadOnlyList<string>?>> _works = new(StringComparer.Ordinal);

    /// <summary>The diagnoses that take seconds and read the model panes as they stood at the press, by part key - see <see cref="OfferWork"/>.</summary>
    private readonly Dictionary<string, Func<string, IReadOnlyList<CapturePane>, IReadOnlyList<string>?>> _paneWorks = new(StringComparer.Ordinal);

    /// <summary>What a piece of work says of itself in the window while it runs, by part key - see <see cref="OfferWork"/>.</summary>
    private readonly Dictionary<string, CaptureWorkProgress> _workProgress = new(StringComparer.Ordinal);

    /// <summary>The model panes, asked for as the key goes down - see <see cref="OfferPanes"/>.</summary>
    private Func<IReadOnlyList<CapturePane>>? _panes;

    private bool _wasDown;
    private bool _listening;
    private Run? _run;

    /// <summary>A capture asked for from another thread, waiting for the next frame to begin it - see <see cref="Request"/>.</summary>
    private CaptureRequest? _asked;

    /// <summary>Why the last press could not begin, for a request that met the same refusal.</summary>
    private string _refusedWhy = string.Empty;

    /// <summary>
    /// Asks for a capture from any thread; it begins on the next frame that polls (the game or one of the tool's
    /// windows in front), as a press of the key would, with the given parts on or the settings' ticks.
    /// </summary>
    /// <remarks>
    /// FOR THE AGENT LINK (asked for from the live client, 2026-11-01): a Claude session taking the capture it needs
    /// without a key being pressed. One request waits at a time; a second while one waits or runs is refused at once,
    /// as a second press is ignored, rather than queued behind a capture that may take minutes.
    /// </remarks>
    /// <param name="on">The parts to write, by key, every other one off; null for the settings' ticks.</param>
    public Task<CaptureOutcome> Request(IReadOnlyCollection<string>? on)
    {
        HashSet<string>? off = on is null
            ? null
            : new HashSet<string>(CaptureParts.All.Where(part => !on.Contains(part.Key)).Select(part => part.Key), StringComparer.Ordinal);
        var request = new CaptureRequest(off);
        if (Busy || Interlocked.CompareExchange(ref _asked, request, null) is not null)
        {
            return Task.FromResult(new CaptureOutcome(false, string.Empty, [], "a capture is already running or waiting to begin"));
        }

        return request.Done.Task;
    }

    /// <summary>Withdraws a requested capture that has not begun; false where none waits.</summary>
    public bool CancelRequest()
    {
        if (Interlocked.Exchange(ref _asked, null) is not { } waiting)
        {
            return false;
        }

        waiting.Done.TrySetResult(new CaptureOutcome(false, string.Empty, [], "withdrawn before it began"));
        return true;
    }

    /// <summary>Whether a capture runs now. Read from any thread; a moment stale at worst.</summary>
    public bool Busy => Volatile.Read(ref _run) is not null;

    /// <summary>Where the running capture stands, in a few words, for the agent link's status; empty when none runs.</summary>
    public string Phase => Volatile.Read(ref _run) is { } run
        ? run.Stage switch
        {
            Stage.Hiding => "taking the game's picture",
            Stage.Rooms => run.Waiting.Length > 0 ? "waiting for " + run.Waiting : "waiting for the rooms",
            _ => "writing the parts",
        }
        : Volatile.Read(ref _asked) is not null ? "asked, waiting for the game or the tool to be in front" : string.Empty;

    /// <summary>The window's capture - the one running, or the last until its window is closed.</summary>
    private CaptureProgress? _shown;

    private int _conflictOf = -1;
    private string _conflict = string.Empty;

    /// <param name="snapshot">The world, this frame.</param>
    /// <param name="client">The game's client area on the screen.</param>
    /// <param name="standing">Where every room of the area stands, or null while that is being worked out - asking starts the work. See AreaRooms.Standing.</param>
    /// <param name="roomsPossible">Whether there is anything to wait for at all: an install to read and a terrain to place the rooms on.</param>
    /// <param name="roomsProgress">How far the rooms' reading or search is.</param>
    /// <param name="areaRooms">The area's rooms as read, surveyed and placed, for the room parts - or null where there is no install to read them from.</param>
    /// <param name="loaded">The files the area loaded.</param>
    /// <param name="masters">Every master (<c>.tsi</c>) the install has, as its index lists them - for the room set where the loaded list names neither set nor master. See AreaRoomSet.</param>
    /// <param name="version">The version and build, as the title bar has them.</param>
    public CaptureKey(
        Func<WorldSnapshot> snapshot,
        Func<ClientRect> client,
        Func<IReadOnlyList<(string Room, RoomLayout Layout, RoomCandidate Where)>?> standing,
        Func<bool> roomsPossible,
        Func<(int Done, int Of)> roomsProgress,
        Func<AreaRooms?> areaRooms,
        Func<IReadOnlyList<string>> loaded,
        Func<IReadOnlyList<string>> masters,
        Func<string> version)
    {
        _snapshot = snapshot;
        _client = client;
        _standing = standing;
        _roomsPossible = roomsPossible;
        _roomsProgress = roomsProgress;
        _areaRooms = areaRooms;
        _loaded = loaded;
        _masters = masters;
        _version = version;
    }

    /// <summary>The key, as a virtual-key code - zero for the default.</summary>
    public int Key { get; set; }

    /// <summary>The key in force.</summary>
    public int KeyOrDefault => Key == 0 ? DefaultKey : Key;

    /// <summary>The parts switched off, by the catalogue's keys - as the settings keep them. Keys the catalogue no longer has are dropped.</summary>
    public IReadOnlyList<string> Off => CaptureParts.Kept(_off);

    /// <summary>The parts off by default switched on - as the settings keep them (CapturePart.OffByDefault).</summary>
    public IReadOnlyList<string> SwitchedOn => CaptureParts.KeptOn(_off);

    /// <summary>The settings' two lists taken in: the parts switched off, and those off by default switched on.</summary>
    public void Switch(IReadOnlyList<string> off, IReadOnlyList<string> on)
    {
        _off.Clear();
        _off.UnionWith(CaptureParts.OffOf(off, on));
    }

    /// <summary>How to read a file out of the install, or null where there is none - for the rooms' files.</summary>
    public Func<string, byte[]?>? Read { get; set; }

    /// <summary>The install's shader sources, as its walk found them - empty until it has run. For the ground part's search.</summary>
    public IReadOnlyList<string> Shaders { get; set; } = [];

    /// <summary>The install's environments (<c>.env</c>), as its walk found them - empty until it has run. For the water part.</summary>
    public IReadOnlyList<string> Environments { get; set; } = [];

    /// <summary>The install's textures whose paths name the water (CaptureWaterTextures.Words), as its walk found them - empty until it has run.</summary>
    public IReadOnlyList<string> WaterTextures { get; set; } = [];

    /// <summary>The install's paths naming a wobble (CaptureWobble.Words under CaptureWobble.Folders), as its walk found them - empty until it has run.</summary>
    public IReadOnlyList<string> WobblePaths { get; set; } = [];

    /// <summary>The install's files the water shore part searches (CaptureWaterShore), as the walks found them - empty until they have run.</summary>
    public IReadOnlyList<string> ShoreFiles { get; set; } = [];

    /// <summary>The install's data tables (CaptureWaterShore.TableExtension under its TableFolder), as the walk found them - empty until it has run.</summary>
    public IReadOnlyList<string> DataTables { get; set; } = [];

    /// <summary>The install's paths naming a debug word (CaptureDebugStrings.PathWords), as the walk found them - empty until it has run.</summary>
    public IReadOnlyList<string> DebugPaths { get; set; } = [];

    /// <summary>The install's engine graphs (CaptureRenderDebug.Folder), as the walk found them - empty until it has run.</summary>
    public IReadOnlyList<string> EngineGraphs { get; set; } = [];

    /// <summary>
    /// Starts the light hunt for the area's own environment and hands back its whole report, or null where nothing can search - the light-hunt part. Set by whoever owns the search.
    /// </summary>
    /// <remarks>The progress is the capture's: its window shows how far the hunt is, and stops it.</remarks>
    public Func<FloatHuntProgress, Task<string>?>? HuntLight { get; set; }

    /// <summary>How long the writer waits for the light hunt started at the press before it stops it and writes what it found.</summary>
    /// <remarks>
    /// NOT A CUT THAT LOSES IT: at two minutes the first capture's hunt, on one thread, was thrown away
    /// whole. Now it runs on every core, the window shows it and stops it on a click, and reaching this
    /// stops it the same way - what it found until then is written, the file saying it was stopped.
    /// </remarks>
    private const int HuntWaitSeconds = 300;

    /// <summary>How long a stopped hunt has to hand back what it found - one chunk a thread, then the bytes round its places.</summary>
    private const int HuntStopSeconds = 30;

    /// <summary>
    /// Starts the memory half of a capture - the recording, and the pass inside it that reads what the ask says - or null where one is already running. Set by whoever owns the reader.
    /// </summary>
    /// <remarks>Completes with what it wrote, in a line, once the recording is closed. See CaptureMemory.</remarks>
    public Func<CaptureMemoryAsk, Task<string>?>? RecordMemory { get; set; }

    /// <summary>Says why there is no memory recording, where <see cref="RecordMemory"/> is not set.</summary>
    public string NoMemory { get; set; } = "this session's reader cannot record on demand";

    /// <summary>Called when the key is rebound or a part ticked, so it is saved.</summary>
    public Action? Changed { get; set; }

    /// <summary>The last capture's folder, or empty.</summary>
    public string Last { get; private set; } = string.Empty;

    /// <summary>
    /// A window's diagnosis as a part of the capture: its text as it stands, or null where there is nothing yet - asked for on the frame the key goes down.
    /// </summary>
    /// <param name="key">The part's key - see CaptureParts.</param>
    /// <param name="report">The window's text, or null for nothing.</param>
    public void Offer(string key, Func<string?> report)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(report);
        _offers[key] = report;
    }

    /// <summary>
    /// A diagnosis that takes seconds as a part of the capture: started off the drawing thread on the frame the key goes down, and
    /// waited for when the files are written.
    /// </summary>
    /// <remarks>
    /// FOR WHAT WAS A BUTTON ON A PLAYER PAGE (the Loaded Files page's four - see CaptureParts.RunsAtPress): they walk the file
    /// table or open the bundles, which <see cref="Offer"/>'s on-the-frame text cannot afford. A part no session offered says so
    /// in capture.txt - no install to read, say - rather than waiting.
    /// </remarks>
    /// <param name="key">The part's key - see CaptureParts.</param>
    /// <param name="work">
    /// Given the path of the part's file, its lines to write there - or null where the work wrote that file itself (the room
    /// export, whose writer takes a path).
    /// </param>
    /// <param name="progress">What it says of itself in the window while it runs - how far, and a stop that keeps what it found - or null for a line saying nothing.</param>
    public void OfferWork(string key, Func<string, IReadOnlyList<string>?> work, CaptureWorkProgress? progress = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(work);
        _works[key] = work;
        if (progress is not null)
        {
            _workProgress[key] = progress;
        }
    }

    /// <summary>
    /// A diagnosis that takes seconds and reads the model panes as they stood when the key went down (<see cref="OfferPanes"/>) - the
    /// cloth watch's tool cloths. Otherwise as <see cref="OfferWork(string, Func{string, IReadOnlyList{string}?}, CaptureWorkProgress?)"/>.
    /// </summary>
    /// <param name="key">The part's key - see CaptureParts.</param>
    /// <param name="work">Given the path of the part's file and the panes, its lines to write there.</param>
    /// <param name="progress">What it says of itself in the window while it runs.</param>
    public void OfferWork(string key, Func<string, IReadOnlyList<CapturePane>, IReadOnlyList<string>?> work, CaptureWorkProgress? progress = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(work);
        _paneWorks[key] = work;
        if (progress is not null)
        {
            _workProgress[key] = progress;
        }
    }

    /// <summary>
    /// The model panes showing a model, asked for on the frame the key goes down - the model-panes part, whose files are read with the rest. See CapturePanes.
    /// </summary>
    /// <param name="panes">The panes as they stand.</param>
    public void OfferPanes(Func<IReadOnlyList<CapturePane>> panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        _panes = panes;
    }

    /// <summary>
    /// One frame, behind the foreground gate: sees the key, moves a capture along. True while the overlay must draw nothing.
    /// </summary>
    public bool Poll(long now, bool typing)
    {
        bool down = !_listening && ScreenInput.IsDown(KeyOrDefault);
        bool pressed = down && !_wasDown;
        _wasDown = down;

        if (pressed && !typing && _run is null)
        {
            Begin(now, null);
        }

        // A CAPTURE ASKED FOR BY THE AGENT LINK begins here as the key's would, on this thread, with the
        // parts it asked for - the typing check is the key's alone, nobody typed this.
        if (_run is null && Interlocked.Exchange(ref _asked, null) is { } asked)
        {
            Begin(now, asked.Off);
            if (_run is { } begun)
            {
                begun.Asked = asked;
            }
            else
            {
                asked.Done.TrySetResult(new CaptureOutcome(false, _shown?.Folder ?? string.Empty, [], _refusedWhy));
            }
        }

        if (_run is { } run)
        {
            if (run.Stage == Stage.Hiding)
            {
                run.Hidden++;
                if (run.Hidden <= HiddenFrames || now - run.Since < HiddenMs)
                {
                    return true;
                }

                // The frames drawn empty are on screen now; this one has not been drawn yet.
                run.Game = ScreenCapture.Grab(run.Client.X, run.Client.Y, run.Client.Width, run.Client.Height);

                // THE CAMERA AS THE PICTURE WAS TAKEN, not as the key went down: the parts drawing through it line up with this picture.
                run.Shot = _snapshot();
                run.HiddenFor = now - run.Since;
                run.Stage = Stage.Rooms;
                run.Since = now;
            }

            if (run.Stage == Stage.Rooms)
            {
                Rooms(run, now);
            }

            if (run.Stage == Stage.Writing && run.Writing is { IsCompleted: true } written)
            {
                _run = null;
                if (written.IsCompletedSuccessfully)
                {
                    Last = run.Packed.Length > 0 ? run.Packed : run.Folder;
                    run.Progress.Finish(
                        run.LeftOut.Count == 0
                            ? "saved and packed: " + Last
                            : $"saved: {run.Folder} - packed without {string.Join(", ", run.LeftOut)}",
                        run.LeftOut.Count == 0,
                        now);
                }
                else
                {
                    run.Progress.Finish("failed: " + written.Exception?.GetBaseException().Message, false, now);
                }

                // BACK UP WITH ITS SUMMARY, even where its window was closed while it ran.
                _shown = run.Progress;
                run.Asked?.Done.TrySetResult(new CaptureOutcome(
                    written.IsCompletedSuccessfully && run.LeftOut.Count == 0,
                    run.Folder,
                    run.Files,
                    written.IsCompletedSuccessfully
                        ? run.LeftOut.Count == 0 ? "saved and packed" : "packed without " + string.Join(", ", run.LeftOut)
                        : "failed: " + written.Exception?.GetBaseException().Message));
            }
        }

        DrawWindow(now);
        return false;
    }

    /// <summary>Whether a part is ticked in the settings - for the controls; a capture reads its own copy.</summary>
    private bool Ticked(string key) => !_off.Contains(key);

    /// <param name="only">The parts switched off for this one capture, or null for the ticks the settings keep.</param>
    private void Begin(long now, HashSet<string>? only)
    {
        // THIS CAPTURE'S PARTS, copied: everything from here to the zip reads this set, never the live
        // ticks, so a box ticked meanwhile - or a request's own choice - holds for exactly one capture.
        var off = new HashSet<string>(only ?? _off, StringComparer.Ordinal);
        bool On(string key) => !off.Contains(key);

        WorldSnapshot snapshot = _snapshot();
        ClientRect client = _client();
        if (!client.IsValid)
        {
            Refused(snapshot, "the game's window was not found", now);
            return;
        }

        DateTime local = DateTime.Now;
        string folder = Path.Combine(CaptureReport.Root, CaptureReport.FolderName(local, snapshot.Area));
        try
        {
            // Two presses in one second must not write into one folder.
            string unique = folder;
            for (var again = 2; Directory.Exists(unique); again++)
            {
                unique = $"{folder} ({again})";
            }

            folder = unique;
            Directory.CreateDirectory(folder);
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            Refused(snapshot, failed.Message, now);
            return;
        }

        var hunt = new FloatHuntProgress();
        var shore = new CaptureWaterShore.Search();
        var debugs = new CaptureWaterShore.Search();
        var sweep = new CaptureStageSweep.Search();
        var atGame = new CaptureAtGame.Search();
        var run = new Run(folder, local, snapshot, client, now, off, Progress(folder, snapshot, hunt, shore, debugs, sweep, atGame, off, now))
        {
            Hunt = hunt,
            Shore = shore,
            Debugs = debugs,
            Sweep = sweep,
            AtGame = atGame,
            Overlay = On(CaptureParts.OverlayPicture) ? ScreenCapture.Grab(client.X, client.Y, client.Width, client.Height) : null,
            Loaded = _loaded(),
            Masters = _masters(),

            // NO HIDING where the game alone is not wanted: the frames drawn empty are for that picture.
            Stage = On(CaptureParts.GamePicture) ? Stage.Hiding : Stage.Rooms,
        };

        // THE WINDOWS' DIAGNOSES NOW, on this thread, as they stood when the key went down.
        foreach (CapturePart part in CaptureParts.All)
        {
            if (_offers.TryGetValue(part.Key, out Func<string?>? report) && On(part.Key))
            {
                run.Reports.Add((part, report()));
            }
        }

        // THE PANES AS THEY STAND, their models immutable - the files they name are read with the rest, off this thread; and before the
        // slow diagnoses start, which may read them (the cloth watch holds the game's cloth against the Monster Book's; the particle emitters'
        // globals are looked for by the numbers its monster's effects carry - v0.1.436 left that part out here, and its first press took no panes).
        if ((On(CaptureParts.ModelPanes) || On(CaptureParts.MonsterFiles) || On(CaptureParts.LightCompare) || On(CaptureParts.ShadowCasters) || On(CaptureParts.LightExtras)
                || On(CaptureParts.RoofFade) || On(CaptureParts.Particles) || On(CaptureParts.ClothWatch) || On(CaptureParts.ParticleEmitters))
            && _panes is { } panes)
        {
            run.Panes = panes();
        }

        // THE SLOW DIAGNOSES FROM THE PRESS ON, off this thread, each with the file it writes - see OfferWork.
        foreach (CapturePart part in CaptureParts.All)
        {
            if (!CaptureParts.RunsAtPress(part.Key) || !On(part.Key))
            {
                continue;
            }

            string path = CaptureParts.PathOf(folder, part.File);
            Func<string, IReadOnlyList<string>?>? work = _works.GetValueOrDefault(part.Key);
            Func<string, IReadOnlyList<CapturePane>, IReadOnlyList<string>?>? paneWork = _paneWorks.GetValueOrDefault(part.Key);
            IReadOnlyList<CapturePane> shown = run.Panes ?? [];
            Task<IReadOnlyList<string>?>? started = paneWork is not null ? Task.Run(() => paneWork(path, shown))
                : work is not null ? Task.Run(() => work(path))
                : null;
            run.Works.Add((part, path, started));
            if (started is not null)
            {
                run.Progress.Of(part.File)?.Start(now);
            }
        }

        // THE LIGHT AND WATER HUNT FROM THE PRESS ON, beside everything else - it searches the game's memory for a minute.
        if (On(CaptureParts.LightHunt))
        {
            run.LightHunt = HuntLight?.Invoke(run.Hunt);
            run.LightHuntNote = HuntLight is null ? "nothing can search the game's memory in this session" : "the hunt could not start";
            if (run.LightHunt is not null)
            {
                run.Progress.Of(CaptureReport.LightHuntFile)?.Start(now);
            }
        }

        if (!On(CaptureParts.Memory))
        {
            run.MemoryNote = "switched off";
        }
        else if (RecordMemory is { } record)
        {
            try
            {
                run.Memory = record(new CaptureMemoryAsk(folder, CaptureParts.Reads(off)));
                run.MemoryNote = run.Memory is null ? "a recording was already running" : string.Empty;
                if (run.Memory is not null)
                {
                    run.Progress.Of(CaptureReport.MemoryFile)?.Start(now);
                }
            }
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
            {
                run.MemoryNote = failed.Message;
            }
        }
        else
        {
            run.MemoryNote = NoMemory;
        }

        _run = run;
        _shown = run.Progress;
    }

    /// <summary>
    /// The window's list for this press: the rooms where a ticked part waits for them, every ticked part that writes a file in the catalogue's order, and the packing.
    /// </summary>
    /// <remarks>
    /// THE LONG ONES SAY WHAT THEY ARE DOING: the rooms how many are read, the memory hunt how much of its budget it has looked
    /// through and how fast, with a stop that keeps what it found; the recording that it closes itself.
    /// </remarks>
    private CaptureProgress Progress(
        string folder, WorldSnapshot snapshot, FloatHuntProgress hunt, CaptureWaterShore.Search shore, CaptureWaterShore.Search debugs, CaptureStageSweep.Search sweep,
        CaptureAtGame.Search atGame, HashSet<string> off, long now)
    {
        bool On(string key) => !off.Contains(key);
        var progress = new CaptureProgress(folder, snapshot.Area.Name.Length > 0 ? snapshot.Area.Name : snapshot.Area.Id, now);
        if (WantsRooms(off))
        {
            progress.Add(new CaptureStep(RoomsStep, "the rooms, placed")
            {
                Live = () => _run is { Waiting.Length: > 0 } run && _roomsProgress() is var (done, of) ? $"waiting for {run.Waiting} - {done} of {of} rooms" : "waiting for the rooms",
                Fraction = () => _roomsProgress() is { Of: > 0 } rooms ? (float)rooms.Done / rooms.Of : 0f,
            });
        }

        foreach (CapturePart part in CaptureParts.All)
        {
            if (!On(part.Key) || part.File.Length == 0)
            {
                continue;
            }

            progress.Add(part.Key switch
            {
                CaptureParts.LightHunt => new CaptureStep(part.File, part.Label)
                {
                    Live = () => Hunting(hunt, progress.Of(CaptureReport.LightHuntFile)),
                    Fraction = () => hunt.Budget > 0 ? (float)((double)hunt.Bytes / hunt.Budget) : 0f,
                    Stop = hunt.Stop,
                },
                CaptureParts.WaterShore => new CaptureStep(part.File, part.Label)
                {
                    Live = () => shore.Of > 0 ? $"{shore.Done:N0} of {shore.Of:N0} files searched, {shore.Found:N0} name a word" : "waiting for its turn",
                    Fraction = () => shore.Of > 0 ? (float)((double)shore.Done / shore.Of) : 0f,
                    Stop = shore.Stop,
                },
                CaptureParts.DebugStrings => new CaptureStep(part.File, part.Label)
                {
                    Live = () => debugs.Of > 0 ? $"{debugs.Done:N0} of {debugs.Of:N0} files searched (the program, then the data tables), {debugs.Found:N0} hold a word" : "waiting for its turn",
                    Fraction = () => debugs.Of > 0 ? (float)((double)debugs.Done / debugs.Of) : 0f,
                    Stop = debugs.Stop,
                },
                CaptureParts.StageSweep => new CaptureStep(part.File, part.Label)
                {
                    Live = () => sweep.Of > 0 ? $"{sweep.Done:N0} of {sweep.Of:N0} rooms built, {sweep.Failed:N0} did not - {sweep.Now}" : "waiting for its turn",
                    Fraction = () => sweep.Of > 0 ? (float)((double)sweep.Done / sweep.Of) : 0f,
                    Stop = sweep.Stop,
                },
                CaptureParts.LightCompare or CaptureParts.ShadowCasters => new CaptureStep(part.File, part.Label)
                {
                    Live = () => atGame.Of > 0 ? $"{atGame.Done} of {atGame.Of} steps - {atGame.Now}" : "waiting for its turn",
                    Fraction = () => atGame.Of > 0 ? (float)((double)atGame.Done / atGame.Of) : 0f,
                },
                _ when CaptureParts.RunsAtPress(part.Key) && _workProgress.TryGetValue(part.Key, out CaptureWorkProgress? work) => new CaptureStep(part.File, part.Label)
                {
                    Live = work.Live,
                    Fraction = work.Fraction,
                    Stop = work.Stop,
                    Ask = work.Ask,
                },
                _ when CaptureParts.RunsAtPress(part.Key) => new CaptureStep(part.File, part.Label)
                {
                    Live = () => "running since the press, off the drawing thread - it says nothing of how far it is",
                },
                CaptureParts.Memory => new CaptureStep(part.File, part.Label)
                {
                    Live = () => "the overlay's own reads, and one pass reading the ticked parts once more from nothing - it closes itself once its ticks are spent and the pass is done",
                },
                _ => new CaptureStep(part.File, part.Label),
            });
        }

        progress.Add(new CaptureStep(CaptureReport.SummaryFile, "the summary, and the zip"));
        return progress;
    }

    /// <summary>The hunt for the window: how much of its budget it has looked through, how fast, where.</summary>
    private static string Hunting(FloatHuntProgress hunt, CaptureStep? step)
    {
        const double Gb = 1024.0 * 1024 * 1024;
        double seconds = step is null ? 0 : (Environment.TickCount64 - step.Read().Began) / 1000.0;
        long bytes = hunt.Bytes;
        string rate = seconds > 0.5 ? $" · {bytes / Gb / seconds:0.00} GB/s" : string.Empty;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{bytes / Gb:0.00} of {hunt.Budget / Gb:0} GB of the game's memory looked through{rate} · {hunt.Regions} regions · {hunt.Places} places found");
    }

    /// <summary>A press that could not begin, in the window as a capture that failed at once.</summary>
    private void Refused(WorldSnapshot snapshot, string why, long now)
    {
        var refused = new CaptureProgress(string.Empty, snapshot.Area.Name.Length > 0 ? snapshot.Area.Name : snapshot.Area.Id, now);
        refused.Finish("not captured: " + why, false, now);
        _shown = refused;
        _refusedWhy = "not captured: " + why;
    }

    /// <summary>The rooms' step - not a file, the wait before the files.</summary>
    private const string RoomsStep = "(the rooms)";

    /// <summary>Whether a part on waits for the rooms to stand.</summary>
    private static bool WantsRooms(HashSet<string> off)
        => !off.Contains(CaptureParts.RoomsNear) || !off.Contains(CaptureParts.RoomsArranged) || !off.Contains(CaptureParts.Environments)
            || !off.Contains(CaptureParts.Doodads) || !off.Contains(CaptureParts.RoomsPlaced);

    /// <summary>Waits for the rooms to stand - the doodad survey, or the search where nothing reads the entities - where a ticked part wants them, then hands everything to the pool.</summary>
    private void Rooms(Run run, long now)
    {
        bool wantsRooms = WantsRooms(run.Off);
        CaptureStep? step = run.Progress.Of(RoomsStep);
        step?.Start(now);
        bool wantsSurvey = !run.Off.Contains(CaptureParts.Doodads) || !run.Off.Contains(CaptureParts.RoomsPlaced);
        bool sameArea = _snapshot().AreaHash == run.Snapshot.AreaHash;
        bool possible = wantsRooms && sameArea && _roomsPossible();
        IReadOnlyList<(string Room, RoomLayout Layout, RoomCandidate Where)>? standing = possible ? _standing() : null;
        AreaRooms? rooms = possible ? _areaRooms() : null;
        if (standing is null && possible && now - run.Since < RoomWaitMs)
        {
            run.Waiting = rooms is { Surveying: true } ? "the doodad survey" : rooms is { ReadDoodads: null } ? "the room search" : "the rooms to be read";
            return;
        }

        run.Standing = standing;
        run.Arranged = rooms?.Last;
        run.RoomsNote = standing is not null ? string.Empty
            : !wantsRooms ? "no ticked part wanted them"
            : !sameArea ? "the area changed before the rooms were placed"
            : !_roomsPossible() ? "no install to read the rooms from, or no terrain to place them on"
            : $"the rooms were not placed after {RoomWaitMs / 1000} s";
        if (rooms is not null)
        {
            run.Rooms = rooms.Rooms;
            run.Survey = rooms.Doodads;
            run.Placed = rooms.Placed;
        }

        step?.End(
            standing is not null ? CaptureStepState.Done : CaptureStepState.NotWritten,
            standing is not null
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{standing.Count} rooms stand, after {(now - run.Since) / 1000.0:0.0} s")
                : "not placed: " + run.RoomsNote,
            now);
        run.SurveyNote = run.Survey is not null ? string.Empty
            : !wantsSurvey ? "no ticked part wanted it"
            : rooms is null || rooms.ReadDoodads is null ? "nothing can read the area's entities in this session"
            : standing is null ? run.RoomsNote
            : "the rooms stand by their ground and tiles, not their doodads";
        run.Stage = Stage.Writing;
        Func<string, byte[]?>? read = Read;
        string version = _version();
        IReadOnlyList<string> shaders = Shaders;
        IReadOnlyList<string> environments = Environments;
        IReadOnlyList<string> waterTextures = WaterTextures;
        IReadOnlyList<string> wobblePaths = WobblePaths;
        IReadOnlyList<string> shoreFiles = ShoreFiles;
        IReadOnlyList<string> dataTables = DataTables;
        IReadOnlyList<string> debugPaths = DebugPaths;
        IReadOnlyList<string> engineGraphs = EngineGraphs;
        run.Writing = Task.Run(() => Write(run, read, version, shaders, environments, waterTextures, wobblePaths, shoreFiles, dataTables, debugPaths, engineGraphs));
    }

    /// <summary>Every ticked part of the capture, each on its own so one failing loses only itself.</summary>
    private static void Write(
        Run run, Func<string, byte[]?>? read, string version, IReadOnlyList<string> shaders, IReadOnlyList<string> environments,
        IReadOnlyList<string> waterTextures, IReadOnlyList<string> wobblePaths, IReadOnlyList<string> shoreFiles, IReadOnlyList<string> dataTables,
        IReadOnlyList<string> debugPaths, IReadOnlyList<string> engineGraphs)
    {
        var parts = new List<string>();

        // EACH PART'S LINE IN capture.txt AND IN THE WINDOW at once - see CaptureProgress.
        void Said(string file, string line, CaptureStepState state)
        {
            parts.Add($"{file}  {line}");
            run.Progress.Of(file)?.End(state, line, Environment.TickCount64);
        }

        void Not(string file, string why) => Said(file, "not written: " + why, CaptureStepState.NotWritten);

        // EACH PART'S FILES IN ITS HEADING'S FOLDER, the pictures and dumps it writes beside its file with it (asked for from the
        // live client, 2026-10-13 - see CaptureParts.Folders); set while the part writes, the top again after.
        string folder = run.Folder;
        void Part(string file, Func<string> write)
        {
            run.Progress.Of(file)?.Start(Environment.TickCount64);
            try
            {
                folder = Path.GetDirectoryName(CaptureParts.PathOf(run.Folder, file)) ?? run.Folder;
                string line = write();
                Said(
                    file,
                    line,
                    line.StartsWith("not written", StringComparison.Ordinal) ? CaptureStepState.NotWritten
                    : line.StartsWith("FAILED", StringComparison.Ordinal) ? CaptureStepState.Failed
                    : CaptureStepState.Done);
            }
            // A PARALLEL LOOP'S FAILURE ARRIVES WRAPPED (Parallel.For's AggregateException), whatever it was - let through, it would end the
            // whole writing, every part after it and the zip with it.
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or AggregateException)
            {
                Said(file, "FAILED: " + failed.GetBaseException().Message, CaptureStepState.Failed);
            }
            finally
            {
                folder = run.Folder;
            }
        }

        string At(string file) => Path.Combine(folder, file);
        bool On(string key) => !run.Off.Contains(key);

        // A PART'S PICTURES BESIDE ITS FILE.
        void Pictures(IReadOnlyList<(string File, GamePicture Picture)> pictures)
        {
            foreach ((string file, GamePicture picture) in pictures)
            {
                using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(picture.Rgba, picture.Width, picture.Height);
                image.SaveAsPng(At(file));
            }
        }
        ClientRect client = run.Client;

        if (On(CaptureParts.GamePicture))
        {
            Part(CaptureReport.GameShot, () => Png(run.Game, client, At(CaptureReport.GameShot), $"the game alone, the overlay hidden for {run.HiddenFor} ms"));
        }

        if (On(CaptureParts.OverlayPicture))
        {
            Part(CaptureReport.OverlayShot, () => Png(run.Overlay, client, At(CaptureReport.OverlayShot), "the game with the overlay, as the key went down"));
        }

        if (On(CaptureParts.Entities))
        {
            Part(CaptureReport.EntitiesFile, () =>
            {
                File.WriteAllText(At(CaptureReport.EntitiesFile), CaptureReport.Entities(run.Snapshot));
                return $"{run.Snapshot.Entities.Count} entities, nearest first";
            });
        }

        if (On(CaptureParts.LoadedFiles))
        {
            Part(CaptureReport.LoadedFile, () =>
            {
                File.WriteAllText(At(CaptureReport.LoadedFile), CaptureReport.Loaded(run.Loaded));
                return $"{run.Loaded.Count} files";
            });
        }

        List<RoomNearby>? near = null;
        if (run.Standing is { } standing && run.Snapshot.Player is { } player && run.Snapshot.Terrain is TerrainGrid terrain)
        {
            (_, _, int tileX, int tileY) = CaptureReport.Where(player.WorldX, player.WorldY);
            near = CaptureReport.RoomsNear(standing, terrain.TilesX, terrain.TilesY, tileX, tileY);
        }

        string noRooms = run.RoomsNote.Length > 0 ? run.RoomsNote : "the player was not read";
        if (On(CaptureParts.Environments))
        {
            if (read is null)
            {
                Not(CaptureReport.EnvironmentsFile, "no install to read them from");
            }
            else
            {
                // The environments whether or not the rooms were found: the area's own is known from
                // its row, and it is the one that certainly applies.
                string[] rooms = near is null ? [] : [.. near.Select(one => one.Room).Distinct(StringComparer.OrdinalIgnoreCase)];
                string area = run.Snapshot.Area.Environment;
                Part(CaptureReport.EnvironmentsFile, () =>
                {
                    File.WriteAllText(At(CaptureReport.EnvironmentsFile), ModelDump.Environments(read, rooms, run.Loaded, area));
                    return $"the area's environment ({(area.Length > 0 ? area : "did not resolve")}) and those of {rooms.Length} rooms";
                });
            }
        }

        if (On(CaptureParts.RoomFiles))
        {
            if (read is null)
            {
                Not(CaptureReport.RoomFilesFile, "no install to read them from");
            }
            else
            {
                Part(CaptureReport.RoomFilesFile, () =>
                {
                    string files = CaptureReport.RoomFiles(read, run.Loaded, run.Masters);
                    File.WriteAllText(At(CaptureReport.RoomFilesFile), files);
                    return files[..files.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.TileIdentities))
        {
            if (read is null)
            {
                Not(CaptureReport.TileIdentitiesFile, "no install to read them from");
            }
            else if (run.Snapshot.Terrain is not TerrainGrid { Tiles: { } laid })
            {
                Not(CaptureReport.TileIdentitiesFile, "the terrain's tiles were not read");
            }
            else
            {
                Part(CaptureReport.TileIdentitiesFile, () =>
                {
                    string identities = CaptureReport.TileIdentities(read, laid);
                    File.WriteAllText(At(CaptureReport.TileIdentitiesFile), identities);
                    return identities[..identities.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.SlotCandidates))
        {
            if (read is null)
            {
                Not(CaptureReport.SlotCandidatesFile, "no install to read the rooms and tilesets from");
            }
            else
            {
                Part(CaptureReport.SlotCandidatesFile, () =>
                {
                    string candidates = CaptureReport.SlotCandidates(read, run.Loaded, run.Masters);
                    File.WriteAllText(At(CaptureReport.SlotCandidatesFile), candidates);
                    return candidates[..candidates.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.TilesetLists))
        {
            if (read is null)
            {
                Not(CaptureReport.TilesetListsFile, "no install to read the lists from");
            }
            else
            {
                Part(CaptureReport.TilesetListsFile, () =>
                {
                    string lists = CaptureReport.TilesetLists(read, run.Loaded, run.Masters);
                    File.WriteAllText(At(CaptureReport.TilesetListsFile), lists);
                    return lists[..lists.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.TileFiles))
        {
            if (read is null)
            {
                Not(CaptureReport.TileFilesFile, "no install to read the tile files from");
            }
            else
            {
                // The laid half only where the terrain was read; the listed half needs nothing but the install.
                Part(CaptureReport.TileFilesFile, () =>
                {
                    string files = CaptureReport.TileFiles(
                        read, run.Loaded, run.Masters, run.Snapshot.Terrain is TerrainGrid { Tiles: { } laid } ? laid : null);
                    File.WriteAllText(At(CaptureReport.TileFilesFile), files);
                    return files[..files.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.Masters))
        {
            if (read is null)
            {
                Not(CaptureReport.MastersFile, "no install to read the masters from");
            }
            else
            {
                Part(CaptureReport.MastersFile, () =>
                {
                    string files = CaptureReport.Masters(read, run.Loaded, run.Masters, run.Snapshot.Terrain is TerrainGrid { Ground: { } ground } ? ground.Types : null);
                    File.WriteAllText(At(CaptureReport.MastersFile), files);
                    return files[..files.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.Ground))
        {
            if (read is null)
            {
                Not(CaptureReport.GroundFile, "no install to read the ground from");
            }
            else
            {
                Part(CaptureReport.GroundFile, () =>
                {
                    string ground = CaptureGround.Write(
                        read, run.Snapshot.Terrain is TerrainGrid { Tiles: { } laid } ? laid : null, run.Loaded, run.Masters, shaders,
                        out IReadOnlyList<GroundMask> masks, out IReadOnlyList<(string File, GamePicture Picture)> shapes);
                    File.WriteAllText(At(CaptureReport.GroundFile), ground);

                    // THE BIGGEST MASKED TEMPLATES FROM ABOVE - their shapes, the green as the mix reads it, their height - and each
                    // ground material's colour texture.
                    foreach ((string file, GamePicture picture) in shapes)
                    {
                        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(picture.Rgba, picture.Width, picture.Height);
                        image.SaveAsPng(At(file));
                    }

                    // EACH MASK'S CHANNELS AS PICTURES beside it - the blend reads red and green apart, so each is its own.
                    foreach (GroundMask mask in masks)
                    {
                        if (mask.Picture is { } picture)
                        {
                            for (var channel = 0; channel < 4; channel++)
                            {
                                Channel(picture, channel, At(mask.FileOf("rgba"[channel])));
                            }
                        }
                    }

                    return ground[..ground.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.GroundScatter))
        {
            if (read is null)
            {
                Not(CaptureReport.GroundScatterFile, "no install to read the ground's layers from");
            }
            else
            {
                Part(CaptureReport.GroundScatterFile, () =>
                {
                    string text = CaptureGroundScatter.Write(read, run.Loaded, run.Masters, run.Snapshot.Terrain as TerrainGrid, folder);
                    File.WriteAllText(At(CaptureReport.GroundScatterFile), text);
                    return text[..text.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.GroundScreen))
        {
            if (read is null)
            {
                Not(CaptureReport.GroundScreenFile, "no install to read the ground from");
            }
            else
            {
                Part(CaptureReport.GroundScreenFile, () =>
                {
                    // THE GAME ALONE where it was taken; the overlay's picture else, whose own drawing is then in the way.
                    (byte[]? shot, string over) = run.Game is not null ? (run.Game, "the game alone")
                        : run.Overlay is not null ? (run.Overlay, "the game with the overlay - tick \"the game alone\" for a clean picture")
                        : ((byte[]?)null, "black - no picture of the game was taken");
                    string said = CaptureGroundScreen.Write(
                        read, run.Snapshot, run.Loaded, run.Masters, shot, client.Width, client.Height, over, out GamePicture? picture, out GamePicture? corners, out GamePicture? heights,
                        out GamePicture? choices);
                    File.WriteAllText(At(CaptureReport.GroundScreenFile), said);
                    foreach ((GamePicture? one, string file) in new[]
                    {
                        (picture, CaptureReport.GroundScreenShot), (corners, CaptureReport.GroundScreenCorners), (heights, CaptureReport.GroundScreenHeights),
                        (choices, CaptureReport.GroundScreenChoices),
                    })
                    {
                        if (one is { } drawn)
                        {
                            using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(drawn.Rgba, drawn.Width, drawn.Height);
                            image.SaveAsPng(At(file));
                        }
                    }

                    return said[..said.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.Water))
        {
            if (read is null)
            {
                Not(CaptureReport.WaterFile, "no install to read the water from");
            }
            else
            {
                Part(CaptureReport.WaterFile, () =>
                {
                    string water = CaptureWater.Write(read, environments, shaders);
                    File.WriteAllText(At(CaptureReport.WaterFile), water);
                    return water[..water.IndexOf('\n')].TrimEnd('\r');
                });
            }
        }

        if (On(CaptureParts.WaterTextures))
        {
            if (read is null)
            {
                Not(CaptureReport.WaterTexturesFile, "no install to read the textures from");
            }
            else
            {
                Part(CaptureReport.WaterTexturesFile, () =>
                {
                    // THE GAME'S OWN PROGRAMS, beside the install - a texture the engine binds itself is named in one if anywhere.
                    string? folder = GameInstall.Find(null);
                    string[] programs = folder is null ? []
                        : [.. GameInstall.Names.Select(name => Path.Combine(folder, name + ".exe")).Where(File.Exists)];
                    string text = CaptureWaterTextures.Write(
                        read, waterTextures, programs, out IReadOnlyList<WaterTexture> pictures, out IReadOnlyList<WaterLookupFile> lookups);
                    File.WriteAllText(At(CaptureReport.WaterTexturesFile), text);

                    // THE ENGINE'S LOOKUPS WHOLE beside it, for WaterLookups to read again offline (CaptureReplayTests).
                    foreach (WaterLookupFile lookup in lookups)
                    {
                        File.WriteAllBytes(At(lookup.File), lookup.Bytes);
                    }

                    // EACH DECODED TEXTURE'S CHANNELS AS PICTURES beside it - the ripple's red is its height, green and blue its slope.
                    foreach (WaterTexture texture in pictures)
                    {
                        for (var channel = 0; channel < 4; channel++)
                        {
                            Channel(texture.Picture, channel, At(texture.FileOf("rgba"[channel])));
                        }
                    }

                    return text[..text.IndexOf('\n')].TrimEnd('\r');
                });
            }
        }

        if (On(CaptureParts.WobbleFiles))
        {
            if (read is null)
            {
                Not(CaptureReport.WobbleFilesFile, "no install to read the wobble's files from");
            }
            else
            {
                Part(CaptureReport.WobbleFilesFile, () =>
                {
                    string? install = GameInstall.Find(null);
                    string[] programs = install is null ? []
                        : [.. GameInstall.Names.Select(name => Path.Combine(install, name + ".exe")).Where(File.Exists)];
                    string text = CaptureWobble.Write(read, run.Loaded, wobblePaths, shaders, programs);
                    File.WriteAllText(At(CaptureReport.WobbleFilesFile), text);
                    return text[..text.IndexOf('\n')].TrimEnd('\r');
                });
            }
        }

        if (On(CaptureParts.WaterShore))
        {
            if (read is null)
            {
                Not(CaptureReport.WaterShoreFile, "no install to read the files from");
            }
            else
            {
                Part(CaptureReport.WaterShoreFile, () =>
                {
                    string? install = GameInstall.Find(null);
                    string[] programs = install is null ? []
                        : [.. GameInstall.Names.Select(name => Path.Combine(install, name + ".exe")).Where(File.Exists)];
                    string text = CaptureWaterShore.Write(read, shoreFiles, dataTables, run.Loaded, shaders, programs, run.Shore);
                    File.WriteAllText(At(CaptureReport.WaterShoreFile), text);
                    return text[..text.IndexOf('\n')].TrimEnd('\r');
                });
            }
        }

        if (On(CaptureParts.StageSweep))
        {
            if (read is null)
            {
                Not(CaptureReport.StageSweepFile, "no install to build the rooms from");
            }
            else
            {
                Part(CaptureReport.StageSweepFile, () =>
                {
                    string text = CaptureStageSweep.Write(read, run.Masters, run.Sweep, folder);
                    File.WriteAllText(At(CaptureReport.StageSweepFile), text);
                    return text[..text.IndexOf('\n')].TrimEnd('\r');
                });
            }
        }

        if (On(CaptureParts.DebugStrings))
        {
            // THE PROGRAM IS READ FROM DISK, so it is searched even where the install's files cannot be read; the tables need them.
            string? install = GameInstall.Find(null);
            string[] programs = install is null ? []
                : [.. GameInstall.Names.Select(name => Path.Combine(install, name + ".exe")).Where(File.Exists)];
            if (programs.Length == 0 && read is null)
            {
                Not(CaptureReport.DebugStringsFile, "no install: neither the game's program nor its files to search");
            }
            else
            {
                Part(CaptureReport.DebugStringsFile, () =>
                {
                    string text = CaptureDebugStrings.Write(read, programs, dataTables, debugPaths, run.Debugs);
                    File.WriteAllText(At(CaptureReport.DebugStringsFile), text);
                    return text[..text.IndexOf('\n')].TrimEnd('\r');
                });
            }
        }

        if (On(CaptureParts.RenderDebug))
        {
            if (read is null)
            {
                Not(CaptureReport.RenderDebugFile, "no install to read the engine's graphs from");
            }
            else
            {
                Part(CaptureReport.RenderDebugFile, () =>
                {
                    string? install = GameInstall.Find(null);
                    string[] programs = install is null ? []
                        : [.. GameInstall.Names.Select(name => Path.Combine(install, name + ".exe")).Where(File.Exists)];
                    string text = CaptureRenderDebug.Write(read, programs, engineGraphs, shaders);
                    File.WriteAllText(At(CaptureReport.RenderDebugFile), text);
                    return text[..text.IndexOf('\n')].TrimEnd('\r');
                });
            }
        }

        if (On(CaptureParts.LightExtras))
        {
            if (read is null)
            {
                Not(CaptureReport.LightExtrasFile, "no install to read the environments, sources and graphs from");
            }
            else
            {
                Part(CaptureReport.LightExtrasFile, () =>
                {
                    // EVERY GRAPH THE WALKS FOUND: the engine's own folder and the rest of Metadata's, as the stage hunt reads them.
                    string? install = GameInstall.Find(null);
                    string[] programs = install is null ? []
                        : [.. GameInstall.Names.Select(name => Path.Combine(install, name + ".exe")).Where(File.Exists)];
                    string text = CaptureLightExtras.Write(
                        read, environments, shaders, [.. engineGraphs, .. shoreFiles], programs, run.Panes, run.Snapshot.Area.Environment,
                        out IReadOnlyList<(string File, GamePicture Picture)> pictures, out IReadOnlyList<(string File, byte[] Bytes)> wholes);
                    File.WriteAllText(At(CaptureReport.LightExtrasFile), text);
                    Pictures(pictures);

                    // THE NOISE CANDIDATES AND THE FOG'S TEXTURE WHOLE beside it, for light-against-game's clouds to be measured again offline.
                    foreach ((string file, byte[] bytes) in wholes)
                    {
                        File.WriteAllBytes(At(file), bytes);
                    }
                    return text[..text.IndexOf('\n')].TrimEnd('\r');
                });
            }
        }

        if (On(CaptureParts.RoomsNear))
        {
            if (read is null)
            {
                Not(CaptureReport.RoomsFile, "no install to read them from");
            }
            else if (near is null)
            {
                Not(CaptureReport.RoomsFile, noRooms);
            }
            else
            {
                Part(CaptureReport.RoomsFile, () =>
                {
                    File.WriteAllText(At(CaptureReport.RoomsFile), CaptureReport.Rooms(read, near));
                    return $"{near.Count} rooms within {CaptureReport.RoomReach} tiles, their doodads' lights and the pieces attached to them, and the file of each room the player stands in";
                });
            }
        }

        if (On(CaptureParts.RoomsArranged))
        {
            if (run.Arranged is { } all)
            {
                Part(CaptureReport.ArrangedFile, () =>
                {
                    (string said, string detail) = CaptureReport.Arranged(all);
                    File.WriteAllText(At(CaptureReport.ArrangedFile), CaptureReport.Lined((said, detail)));
                    return said;
                });
            }
            else
            {
                Not(CaptureReport.ArrangedFile, run.RoomsNote.Length > 0 ? run.RoomsNote : "the rooms stand by their doodads - see " + CaptureReport.PlacedFile);
            }
        }

        if (On(CaptureParts.Doodads))
        {
            if (run.Survey is { } survey)
            {
                Part(CaptureReport.DoodadsFile, () =>
                {
                    (string said, string detail) = CaptureReport.Doodads(survey, run.Rooms);
                    File.WriteAllText(At(CaptureReport.DoodadsFile), CaptureReport.Lined((said, detail)) + "\n\n" + CaptureReport.Sightings(survey));
                    return said;
                });
            }
            else
            {
                Not(CaptureReport.DoodadsFile, run.SurveyNote);
            }
        }

        if (On(CaptureParts.RoomsPlaced))
        {
            if (run.Placed is { } placed)
            {
                Part(CaptureReport.PlacedFile, () =>
                {
                    (string said, string detail) = CaptureReport.Placed(placed);
                    string entities = run.Survey is { } survey ? "\n\n" + CaptureReport.PlacedEntities(placed, survey) : string.Empty;
                    File.WriteAllText(At(CaptureReport.PlacedFile), CaptureReport.Lined((said, detail)) + entities);
                    return said;
                });
            }
            else
            {
                Not(CaptureReport.PlacedFile, run.SurveyNote);
            }
        }

        if (On(CaptureParts.ModelPanes))
        {
            if (run.Panes is not { Count: > 0 } panes)
            {
                Not(CaptureReport.PanesFile, "no model pane showed a model when the key went down");
            }
            else
            {
                Part(CaptureReport.PanesFile, () =>
                {
                    string text = CapturePanes.Write(read, panes);
                    File.WriteAllText(At(CaptureReport.PanesFile), text);

                    // EACH ROOM'S SHORE AS A PICTURE beside it - the swell's field is made here, CHOSEN, and held against the game's water by eye.
                    foreach ((string file, GamePicture picture) in CapturePanes.ShorePictures(panes))
                    {
                        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(picture.Rgba, picture.Width, picture.Height);
                        image.SaveAsPng(At(file));
                    }

                    // AND AS NUMBERS, every cell's kind and every way in - a video of the game is read into the room's frame against them.
                    foreach ((string file, byte[] bytes) in CapturePanes.ShoreFields(panes))
                    {
                        File.WriteAllBytes(At(file), bytes);
                    }

                    return text[..text.IndexOf('\n')];
                });
            }
        }

        if (On(CaptureParts.MonsterFiles))
        {
            CapturePane? monster = run.Panes?.FirstOrDefault(one => one.Book == "monster" && one.Path.Length > 0);
            if (monster is null)
            {
                Not(CaptureReport.MonsterFilesFile, "the Monster Book showed no monster when the key went down");
            }
            else
            {
                Vector2? feet = run.Snapshot.Player is { } you ? new Vector2(you.WorldX, you.WorldY) : null;
                Part(CaptureReport.MonsterFilesFile, () =>
                {
                    string text = CaptureMonsterFiles.Write(read, monster, run.Snapshot.Entities, feet, out IReadOnlyList<(string File, byte[] Bytes)> wholes);
                    File.WriteAllText(At(CaptureReport.MonsterFilesFile), text);

                    // THE EFFECTS' TEXTURES WHOLE beside it - a particle's sprite is what it looks like (the lantern's flame, v0.1.401).
                    foreach ((string file, byte[] bytes) in wholes)
                    {
                        File.WriteAllBytes(At(file), bytes);
                    }

                    return text[..text.IndexOf('\n')];
                });
            }
        }

        // THE PARTICLES: every effect the panes' monsters name, their graphs and the engine's, and the sources defining their nodes.
        if (On(CaptureParts.Particles))
        {
            if (read is null)
            {
                Not(CaptureReport.ParticlesFile, "no install to read the effects, graphs and sources from");
            }
            else
            {
                Part(CaptureReport.ParticlesFile, () =>
                {
                    string? install = GameInstall.Find(null);
                    string[] programs = install is null ? []
                        : [.. GameInstall.Names.Select(name => Path.Combine(install, name + ".exe")).Where(File.Exists)];
                    string text = CaptureParticles.Write(read, run.Panes, engineGraphs, shaders, programs, PoEformance.Gpu.ParticleCompile.Fxc);
                    File.WriteAllText(At(CaptureReport.ParticlesFile), text);
                    return text[..text.IndexOf('\n')].TrimEnd('\r');
                });
            }
        }

        // THE STAGE THROUGH THE GAME'S CAMERA, drawn once for the parts that hold it against the game's picture.
        if (On(CaptureParts.LightCompare) || On(CaptureParts.ShadowCasters) || On(CaptureParts.RoofFade))
        {
            WorldSnapshot shot = run.Shot ?? run.Snapshot;
            CapturePane? chosen = CaptureAtGame.Chosen(run.Panes, shot, out string notChosen);
            CaptureAtGame? scene = null;
            string notDrawn = notChosen;
            var drawFailed = false;
            if (chosen is not null)
            {
                // THE DRAWING IS THE LONG STEP, so the first part waiting on it runs from here - its line in the window says what is drawn.
                run.Progress.Of(On(CaptureParts.LightCompare) ? CaptureReport.LightCompareFile
                    : On(CaptureParts.ShadowCasters) ? CaptureReport.ShadowCastersFile : CaptureReport.RoofFadeFile)?.Start(Environment.TickCount64);
                try
                {
                    scene = CaptureAtGame.Draw(chosen, shot, run.Game, client.Width, client.Height, run.AtGame, out notDrawn);
                }

                // THE DRAWING TAKES WHAT THE PANE'S OWN RENDER TAKES: every input is out of the game's files, and a band's failure arrives
                // wrapped by its parallel loop - the buffers' size, too, is no reason to lose the rest of the capture.
                catch (Exception failed) when (failed is not StackOverflowException)
                {
                    notDrawn = "drawing it: " + failed.GetBaseException().Message;
                    drawFailed = true;
                }
            }

            if (On(CaptureParts.LightCompare))
            {
                if (drawFailed)
                {
                    Said(CaptureReport.LightCompareFile, "FAILED: " + notDrawn, CaptureStepState.Failed);
                }
                else if (scene is null)
                {
                    Not(CaptureReport.LightCompareFile, notDrawn.Length > 0 ? notDrawn : "nothing to draw");
                }
                else if (scene.Game.Length == 0)
                {
                    Not(CaptureReport.LightCompareFile, "no picture of the game alone was taken - tick \"game picture\"");
                }
                else
                {
                    Part(CaptureReport.LightCompareFile, () =>
                    {
                        string text = CaptureLightCompare.Write(
                            read, scene, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers, run.AtGame, run.Snapshot.Area.Environment);
                        File.WriteAllText(At(CaptureReport.LightCompareFile), text);
                        Pictures(pictures);
                        File.WriteAllBytes(At(CaptureLightCompare.Numbers), numbers);
                        return text[..text.IndexOf('\n')];
                    });
                }
            }

            if (On(CaptureParts.ShadowCasters))
            {
                // WITH NO STAGE LAID IN THIS INSTANCE, another stage with ground: its casters measured, nothing held against the game.
                string noGround = string.Empty;
                if ((chosen ?? CaptureShadowCasters.Fallback(run.Panes, out noGround)) is not { } pane)
                {
                    Not(CaptureReport.ShadowCastersFile, notChosen.Length > 0 ? notChosen + "; " + noGround : noGround);
                }
                else
                {
                    Part(CaptureReport.ShadowCastersFile, () =>
                    {
                        string text = CaptureShadowCasters.Write(read, pane, scene, run.AtGame, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);
                        File.WriteAllText(At(CaptureReport.ShadowCastersFile), text);
                        Pictures(pictures);
                        if (numbers.Length > 0)
                        {
                            File.WriteAllBytes(At(CaptureShadowCasters.Numbers), numbers);
                        }

                        int line = text.IndexOf('\n');
                        return line < 0 ? text : text[..line];
                    });
                }
            }

            if (On(CaptureParts.RoofFade))
            {
                if (drawFailed)
                {
                    Said(CaptureReport.RoofFadeFile, "FAILED: " + notDrawn, CaptureStepState.Failed);
                }
                else if (scene is null)
                {
                    Not(CaptureReport.RoofFadeFile, notDrawn.Length > 0 ? notDrawn : "nothing to draw");
                }
                else if (scene.Game.Length == 0)
                {
                    Not(CaptureReport.RoofFadeFile, "no picture of the game alone was taken - tick \"game picture\"");
                }
                else
                {
                    Part(CaptureReport.RoofFadeFile, () =>
                    {
                        TerrainGrid? terrain = run.Snapshot.Terrain as TerrainGrid;
                        string text = CaptureRoofFade.Write(
                            read, scene, run.Snapshot.Area.Environment, terrain?.Tiles, terrain?.Ground, run.AtGame,
                            out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);
                        File.WriteAllText(At(CaptureReport.RoofFadeFile), text);
                        Pictures(pictures);
                        if (numbers.Length > 0)
                        {
                            File.WriteAllBytes(At(CaptureRoofFade.Numbers), numbers);
                        }

                        int line = text.IndexOf('\n');
                        return line < 0 ? text : text[..line];
                    });
                }
            }
        }

        // THE WINDOWS' DIAGNOSES, as they stood on the press - see Offer.
        foreach ((CapturePart part, string? text) in run.Reports)
        {
            if (text is null)
            {
                Not(part.File, "nothing there when the key went down");
                continue;
            }

            Part(part.File, () =>
            {
                File.WriteAllText(At(part.File), text);
                return $"{text.AsSpan().Count('\n') + 1} lines";
            });
        }

        // THE SLOW DIAGNOSES, started at the press - see OfferWork.
        foreach ((CapturePart part, string path, Task<IReadOnlyList<string>?>? work) in run.Works)
        {
            if (work is null)
            {
                Not(part.File, "nothing in this session can make it - no install to read, or no file table");
                continue;
            }

            Part(part.File, () =>
            {
                try
                {
                    if (!work.Wait(TimeSpan.FromSeconds(WorkWaitSeconds)))
                    {
                        return $"not written: still running {WorkWaitSeconds} s after the press";
                    }
                }
                catch (AggregateException failed)
                {
                    // ITS TYPE AS WELL: "KeyNotFoundException: ... has no field" says where to look where a bare message may not.
                    Exception cause = failed.GetBaseException();
                    return $"FAILED: {cause.GetType().Name}: {cause.Message}";
                }

                if (work.Result is not { } lines)
                {
                    return File.Exists(path) ? "written by the part itself" : "not written: the part wrote nothing";
                }

                File.WriteAllLines(path, lines);
                return lines.Count > 0 ? lines[0] : "nothing to say";
            });
        }

        if (On(CaptureParts.LightHunt))
        {
            if (run.LightHunt is not { } hunt)
            {
                Not(CaptureReport.LightHuntFile, run.LightHuntNote);
            }
            else
            {
                Part(CaptureReport.LightHuntFile, () =>
                {
                    try
                    {
                        // ITS LIMIT REACHED, it is stopped and what it found until then is written - see HuntWaitSeconds.
                        if (!hunt.Wait(TimeSpan.FromSeconds(HuntWaitSeconds)))
                        {
                            run.Hunt?.Stop();
                            if (!hunt.Wait(TimeSpan.FromSeconds(HuntStopSeconds)))
                            {
                                return $"not written: the hunt still ran {HuntStopSeconds} s after it was stopped at {HuntWaitSeconds} s";
                            }
                        }
                    }
                    catch (AggregateException failed)
                    {
                        return "FAILED: " + failed.GetBaseException().Message;
                    }

                    File.WriteAllText(At(CaptureReport.LightHuntFile), hunt.Result);
                    return hunt.Result[..Math.Max(0, hunt.Result.IndexOf('\n'))].TrimEnd('\r');
                });
            }
        }

        // Last, so the recording has had the time the rest took. It closes once its ticks are
        // spent and the pass inside it is done - seconds, or a minute at the very most.
        if (run.Memory is { } memory)
        {
            try
            {
                if (memory.Wait(TimeSpan.FromSeconds(MemoryWaitSeconds)))
                {
                    Said(CaptureReport.MemoryFile, memory.Result, CaptureStepState.Done);
                }
                else
                {
                    Said(CaptureReport.MemoryFile, $"still recording after {MemoryWaitSeconds} s when this was written - it closes itself", CaptureStepState.NotWritten);
                }
            }
            catch (AggregateException failed)
            {
                Said(CaptureReport.MemoryFile, "FAILED: " + failed.GetBaseException().Message, CaptureStepState.Failed);
            }

            if (On(CaptureParts.Monsters))
            {
                Said(CaptureReport.MonstersFile, $"written by the pass inside the recording - {CaptureMemory.IndexFile} says what it holds", CaptureStepState.Done);
            }

            if (On(CaptureParts.Landscape))
            {
                Said(CaptureReport.LandscapeFile, $"written by the pass inside the recording - {CaptureMemory.IndexFile} says what it holds", CaptureStepState.Done);
            }
        }
        else
        {
            Said(CaptureReport.MemoryFile, "not recorded: " + run.MemoryNote, CaptureStepState.NotWritten);
            if (On(CaptureParts.Monsters))
            {
                Not(CaptureReport.MonstersFile, "it is read inside the recording, and there is none");
            }

            if (On(CaptureParts.Landscape))
            {
                Not(CaptureReport.LandscapeFile, "it is read inside the recording, and there is none");
            }
        }

        string[] off = [.. CaptureParts.All.Where(part => run.Off.Contains(part.Key)).Select(part => part.Key)];
        parts.Add(off.Length == 0 ? "every part of the catalogue was on" : "off: " + string.Join(", ", off));
        parts.Add($"{Path.GetFileName(run.Folder)}.zip  beside this folder: every file here, packed to send - past 29 MB cut into pieces "
            + $"{Path.GetFileName(run.Folder)}.zip.001, .002 and on, each under 29 MB, joined again by 7-Zip (open the .001) or copy /b");
        CaptureStep? packing = run.Progress.Of(CaptureReport.SummaryFile);
        packing?.Start(Environment.TickCount64);
        File.WriteAllText(
            At(CaptureReport.SummaryFile),
            CaptureReport.Summary(run.Snapshot, run.Local, version, (client.X, client.Y, client.Width, client.Height), near, parts));

        // LAST, so the zip holds the summary and the closed recording. A zip that cannot be made
        // leaves the folder as it is - every file in it was written - and says why.
        try
        {
            (IReadOnlyList<string> files, List<string> leftOut) = CaptureReport.Pack(run.Folder);
            string packed = files.Count == 1
                ? Path.GetFileName(files[0])
                : $"{Path.GetFileName(files[0])} to {Path.GetFileName(files[^1])} - {files.Count} pieces under 29 MB, joined by 7-Zip or copy /b";
            run.Packed = files.Count == 1 ? files[0] : $"{files[0]} ({files.Count} pieces)";
            run.Files = files;
            run.LeftOut = leftOut;
            packing?.End(
                leftOut.Count == 0 ? CaptureStepState.Done : CaptureStepState.Failed,
                leftOut.Count == 0 ? $"{CaptureReport.SummaryFile}, and every file packed into {packed}" : $"packed into {packed} without " + string.Join(", ", leftOut),
                Environment.TickCount64);
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            run.LeftOut = [$"everything ({failed.Message})"];
            packing?.End(CaptureStepState.Failed, "not packed: " + failed.Message, Environment.TickCount64);
        }
    }

    /// <summary>One channel of a decoded texture as a grey PNG.</summary>
    private static void Channel(GamePicture picture, int channel, string path)
    {
        int texels = picture.Width * picture.Height;
        var grey = new byte[texels];
        byte[] rgba = picture.Rgba;
        for (var at = 0; at < texels; at++)
        {
            grey[at] = rgba[(at * 4) + channel];
        }

        using Image<L8> image = Image.LoadPixelData<L8>(grey, picture.Width, picture.Height);
        image.SaveAsPng(path);
    }

    private static string Png(byte[]? pixels, ClientRect client, string path, string what)
    {
        if (pixels is null)
        {
            throw new InvalidOperationException("the screen could not be copied");
        }

        using Image<Bgr24> image = Image.LoadPixelData<Bgr24>(pixels, client.Width, client.Height);
        image.SaveAsPng(path);
        return $"{client.Width} x {client.Height}, {what}";
    }

    /// <summary>
    /// The capture's window, from the end of the hiding until it is closed or the key goes down again - see CaptureProgress.
    /// </summary>
    /// <remarks>
    /// NOT FOCUSED WHEN IT APPEARS: the key is pressed in the game, and a window taking the keyboard from
    /// it at that moment would swallow the next skill. Closed while the capture runs, it comes back
    /// with the summary once the zip is written.
    /// </remarks>
    private void DrawWindow(long now)
    {
        if (_shown is not { } shown)
        {
            return;
        }

        ImGui.SetNextWindowPos(new Vector2(12, 30), ImGuiCond.FirstUseEver);
        bool open = true;
        if (ImGui.Begin(
            "diagnosis capture###captureprogress",
            ref open,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav))
        {
            shown.Draw(now);
        }

        ImGui.End();
        if (!open)
        {
            _shown = null;
        }
    }

    /// <summary>The rows on Appearance → Keys: the key, what the game does with it, and a box for every part of the catalogue.</summary>
    public void DrawControls()
    {
        OverlayLayout.Group("diagnosis capture");
        if (_listening)
        {
            ImGui.Button("press a key… (Esc cancels)##capturekey");
            if (PressedKey() is int key and > 0)
            {
                if (key != 0x1B)
                {
                    Key = key;
                    Changed?.Invoke();
                }

                _listening = false;
            }
        }
        else if (ImGui.Button($"{FlaskKeyBindings.Describe((ushort)KeyOrDefault)}##capturekey"))
        {
            _listening = true;
        }

        OverlayLayout.Hint(
            "Writes the ticked parts below into one folder beside the tool, and packs them into a zip to send: pictures, "
            + "the rooms and their doodads, every entity, the area's files, what the diagnostic windows hold, and a memory "
            + "recording in which what the ticked parts need is read once more. Tick what the question is about.");

        if (_conflictOf != KeyOrDefault)
        {
            _conflictOf = KeyOrDefault;
            _conflict = FlaskKeyBindings.SayActionsOn((ushort)KeyOrDefault);
        }

        OverlayLayout.Note(_conflict);
        if (Last.Length > 0)
        {
            PathLink.Line("last: " + Last);
        }

        DrawCatalogue();
    }

    /// <summary>A box per part, flowing under its group's heading - see CaptureParts.</summary>
    private void DrawCatalogue()
    {
        bool memory = Ticked(CaptureParts.Memory);
        foreach (string group in CaptureParts.Groups)
        {
            ImGui.TextColored(OverlayInk.Quiet, CaptureParts.GroupLabel(group));
            float indent = ImGui.GetCursorPosX() + ImGui.GetStyle().FramePadding.X;
            float right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            float end = float.NaN;
            foreach (CapturePart part in CaptureParts.In(group))
            {
                if (float.IsNaN(end))
                {
                    ImGui.SetCursorPosX(indent);
                }
                else
                {
                    OverlayLayout.Flow(end, OverlayLayout.CheckboxWidth(part.Label), right, indent);
                }

                // A part that lives inside a recording nobody makes writes nothing: the box stays,
                // greyed, so what it would add is still readable.
                bool moot = CaptureParts.NeedsRecording(part.Key) && !memory;
                ImGui.BeginDisabled(moot);
                bool on = Ticked(part.Key);
                if (OverlayLayout.Toggle(part.Box, ref on))
                {
                    _ = on ? _off.Remove(part.Key) : _off.Add(part.Key);
                    Changed?.Invoke();
                }

                ImGui.EndDisabled();
                end = ImGui.GetItemRectMax().X;
                OverlayLayout.Hint(part.Hover);
            }
        }
    }

    /// <summary>
    /// The key going down this frame, for a row waiting to be bound - or zero.
    /// </summary>
    /// <remarks>
    /// Mouse buttons and the generic modifiers are not keys somebody binds: the click that opened the
    /// row is still down, and Ctrl is the screenshot key's "whole overlay" modifier.
    /// </remarks>
    public static int PressedKey()
    {
        for (int key = 8; key < 255; key++)
        {
            if (key is 0x01 or 0x02 or 0x04 or 0x05 or 0x06 or (>= 0x10 and <= 0x12) or (>= 0xA0 and <= 0xA5))
            {
                continue;
            }

            if (ScreenInput.IsDown(key))
            {
                return key;
            }
        }

        return 0;
    }

    private enum Stage
    {
        Hiding,
        Rooms,
        Writing,
    }

    private sealed class Run(string folder, DateTime local, WorldSnapshot snapshot, ClientRect client, long since, HashSet<string> off, CaptureProgress progress)
    {
        public string Folder { get; } = folder;

        /// <summary>Where each part stands, for the window.</summary>
        public CaptureProgress Progress { get; } = progress;

        /// <summary>The light hunt's progress, and its stop - the window's and the writer's.</summary>
        public FloatHuntProgress? Hunt { get; init; }

        /// <summary>The water shore part's search, and its stop - the window's and the writer's.</summary>
        public CaptureWaterShore.Search? Shore { get; init; }

        /// <summary>The debug strings part's search, and its stop - the window's and the writer's.</summary>
        public CaptureWaterShore.Search? Debugs { get; init; }

        /// <summary>The stage sweep's progress, and its stop - the window's and the writer's.</summary>
        public CaptureStageSweep.Search? Sweep { get; init; }

        /// <summary>How far the parts drawing through the game's camera have got - the window's and the writers'.</summary>
        public CaptureAtGame.Search? AtGame { get; init; }

        /// <summary>The world as read when the game's picture was taken - its camera lines up with that picture; null where none was taken.</summary>
        public WorldSnapshot? Shot { get; set; }

        public DateTime Local { get; } = local;

        public WorldSnapshot Snapshot { get; } = snapshot;

        public ClientRect Client { get; } = client;

        /// <summary>The parts off as the key went down - a copy, so a box ticked while the files are written changes nothing of this capture.</summary>
        public HashSet<string> Off { get; } = off;

        /// <summary>The request that began this capture, when one did - told the outcome at the end.</summary>
        public CaptureRequest? Asked { get; set; }

        /// <summary>The zip, or its pieces in order, once packed.</summary>
        public IReadOnlyList<string> Files { get; set; } = [];

        public Stage Stage { get; set; } = Stage.Hiding;

        public long Since { get; set; } = since;

        public int Hidden { get; set; }

        public long HiddenFor { get; set; }

        public byte[]? Overlay { get; init; }

        public byte[]? Game { get; set; }

        public IReadOnlyList<string> Loaded { get; init; } = [];

        /// <summary>Every master the install has, as the index listed them on the press - the third road to the room set, see AreaRoomSet.</summary>
        public IReadOnlyList<string> Masters { get; init; } = [];

        /// <summary>The windows' diagnoses as they stood on the press, one per ticked part that offers one - null text for nothing there.</summary>
        public List<(CapturePart Part, string? Text)> Reports { get; } = [];

        /// <summary>The model panes showing a model on the press, or null where the part is off or none was offered - see OfferPanes.</summary>
        public IReadOnlyList<CapturePane>? Panes { get; set; }

        /// <summary>The light hunt started at the press, its whole report to come - or null, with why in <see cref="LightHuntNote"/>.</summary>
        public Task<string>? LightHunt { get; set; }

        /// <summary>The slow diagnoses started at the press, each with the file it writes - null where no session offered it.</summary>
        public List<(CapturePart Part, string Path, Task<IReadOnlyList<string>?>? Work)> Works { get; } = [];

        /// <summary>Why there is no light hunt.</summary>
        public string LightHuntNote { get; set; } = string.Empty;

        public Task<string>? Memory { get; set; }

        public string MemoryNote { get; set; } = string.Empty;

        /// <summary>Where every room stands, by whichever way placed them - see AreaRooms.Standing.</summary>
        public IReadOnlyList<(string Room, RoomLayout Layout, RoomCandidate Where)>? Standing { get; set; }

        /// <summary>The arrangement by ground and tiles - the fallback's, null in the doodad path.</summary>
        public RoomArrangement? Arranged { get; set; }

        public string RoomsNote { get; set; } = string.Empty;

        /// <summary>What the line at the top says is being waited for.</summary>
        public string Waiting { get; set; } = string.Empty;

        public IReadOnlyList<(string Room, RoomLayout Layout)>? Rooms { get; set; }

        public DoodadSurvey? Survey { get; set; }

        public IReadOnlyList<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)>? Placed { get; set; }

        public string SurveyNote { get; set; } = string.Empty;

        public Task? Writing { get; set; }

        public string Packed { get; set; } = string.Empty;

        public List<string> LeftOut { get; set; } = [];
    }
}

/// <summary>What a piece of capture work started at the press says of itself in the capture's window - see CaptureKey.OfferWork.</summary>
/// <param name="Live">Its line: what it is doing.</param>
/// <param name="Fraction">How far it is, nought to one.</param>
/// <param name="Stop">Stops it early, what it found until then kept - or null where it cannot be stopped.</param>
public sealed record CaptureWorkProgress(Func<string> Live, Func<float> Fraction, Action? Stop)
{
    /// <summary>What it asks of the person at the game now, shown as a banner over the capture's window - null for nothing (see <see cref="CaptureAsk"/>).</summary>
    public Func<CaptureAsk?>? Ask { get; init; }
}

/// <summary>
/// What a part running at the press asks of the person at the game - a coloured banner over the capture's window, its countdown, and a tone
/// as the asking to act begins and ends (v0.1.407, DECIDED from the live client: "in that window finding anything is hard").
/// </summary>
/// <param name="Act">Whether the person is to act now - the banner green, a high tone as it begins and a low one as it ends - or to wait, standing still.</param>
/// <param name="Text">What to do, in a few words, set large.</param>
/// <param name="Detail">A line under it - what comes next, how long.</param>
/// <param name="Left">The seconds left of the asking, counted down in the banner - NaN where not known.</param>
public sealed record CaptureAsk(bool Act, string Text, string Detail, double Left);

/// <summary>A capture asked for from another thread: the parts it switched off, and where its outcome goes.</summary>
internal sealed class CaptureRequest(HashSet<string>? off)
{
    public HashSet<string>? Off { get; } = off;

    public TaskCompletionSource<CaptureOutcome> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>What a requested capture came to.</summary>
/// <param name="Ok">Written and packed whole.</param>
/// <param name="Folder">Its folder, every file unpacked; empty when it never began.</param>
/// <param name="Files">The zip, or its pieces in order.</param>
/// <param name="Said">What happened, in a line.</param>
public sealed record CaptureOutcome(bool Ok, string Folder, IReadOnlyList<string> Files, string Said);

