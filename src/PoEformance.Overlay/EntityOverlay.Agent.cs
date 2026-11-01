using System.Globalization;
using System.Text;
using System.Text.Json;
using ImGuiNET;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PoEformance.Overlay;

/// <summary>
/// The overlay's half of the agent link: the capture, the screenshot, the books and their model panes as tools.
/// </summary>
/// <remarks>
/// EVERY TOOL GOES THROUGH THE FRAME (<see cref="AgentFrames"/>): the books, the panes and the capture key are the
/// render thread's, so a tool sets what it asks for between two frames and reads what came of it between two later
/// ones - the drawing in between is the overlay's own, exactly what a hand at the mouse would have caused. What a
/// tool cannot have while another application is in front (a picture, a capture) waits for a frame that draws, and
/// says so when it does not come.
/// </remarks>
public sealed partial class EntityOverlay
{
    private readonly AgentFrames _agentFrames = new();

    /// <summary>Who was in front at the last frame - written by the frame, read by the status tool.</summary>
    private volatile GameWindowTracker.Front _agentFront;

    private AgentLink? _agent;

    /// <summary>The longest a tool waits for a pane's picture.</summary>
    private const double MostPictureSeconds = 900;

    private AgentLinkHost? _agentHost;
    private AgentKeyHolder? _agentKey;
    private string _agentSaid = string.Empty;

    /// <summary>
    /// Binds the overlay's tools to the agent link and puts its switch on the About page. Called once, before the overlay starts.
    /// </summary>
    public void AttachAgent(AgentLink link, AgentLinkHost host, AgentKeyHolder key)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(key);
        _agent = link;
        _agentHost = host;
        _agentKey = key;
        _tools.Add(210, "agent", "agent link", DrawAgentTab, page: About, pageLabel: "about");
        link.Bind(AgentCatalogue.Capture, AgentCapture);
        link.Bind(AgentCatalogue.CaptureParts, AgentCaptureParts);
        link.Bind(AgentCatalogue.Captures, AgentCaptures);
        link.Bind(AgentCatalogue.Screenshot, AgentScreenshot);
        link.Bind(AgentCatalogue.MonsterBook, AgentMonsterBook);
        link.Bind(AgentCatalogue.TileBook, AgentTileBook);
        link.Bind(AgentCatalogue.Pane, AgentPane);
        link.Bind(AgentCatalogue.Front, AgentFrontTool);
        link.Before = AgentFront;
    }

    /// <summary>
    /// The game's window to the front before every call where another window is (asked for from the live client, 2026-11-01) - on the
    /// render thread, whose message queue AttachThreadInput needs; what it says opens the call's answer, nothing where the game was in front.
    /// </summary>
    private async Task<string?> AgentFront(string tool)
    {
        GameWindowTracker.Brought brought = await Bring().ConfigureAwait(false);
        if (brought.Moved || !brought.InFront)
        {
            OverlayLog.Write($"agent   {brought.Said} - for {tool}");
        }

        return brought.Moved || !brought.InFront ? brought.Said : null;
    }

    /// <summary>
    /// The raising on a thread of its own (GameWindowTracker.RaiseAside) - never the render thread, which a raise hanging on the game's
    /// window held for good in v0.1.416 - then the waiting for Windows to hand the front on.
    /// </summary>
    private static async Task<GameWindowTracker.Brought> Bring()
    {
        (GameWindowTracker.Raised? raised, string why) = await GameWindowTracker.RaiseAside().ConfigureAwait(false);
        if (why.Length > 0)
        {
            return new GameWindowTracker.Brought(false, false, why);
        }

        return await GameWindowTracker.Settled(raised).ConfigureAwait(false);
    }

    /// <summary>The front tool: the game to the front (the call's own preamble has done it already) and who is in front now.</summary>
    private async Task<AgentResult> AgentFrontTool(JsonElement arguments, CancellationToken cancelling)
    {
        GameWindowTracker.Brought brought = await Bring().ConfigureAwait(false);
        return brought.InFront ? AgentResult.Say("the game is in front") : AgentResult.Fail(brought.Said);
    }

    /// <summary>The agent link's switch, its key and what it is doing - the About page's "agent link".</summary>
    private void DrawAgentTab()
    {
        if (_agentHost is not { } host || _agentKey is not { } key)
        {
            return;
        }

        ImGui.PushTextWrapPos(0f);
        bool on = host.On;
        if (ImGui.Checkbox("on##agent-link-on", ref on))
        {
            // OFF THE FRAME: stopping waits for cloudflared to end, and starting binds a socket.
            bool wanted = on;
            _ = Task.Run(() =>
            {
                _agentSaid = wanted ? host.Start() : string.Empty;
                if (!wanted)
                {
                    host.Stop();
                }

                AgentKeyStore.SetOn(wanted);
            });
        }

        OverlayLayout.Hint(
            "Lets a Claude session in the cloud use this tool - take captures, update it, show and look at the model panes - through a"
            + " Cloudflare quick tunnel (free, no account): cloudflared is fetched once, pinned to its SHA-256, and the tunnel's address is"
            + " published on the repository's agent-link branch with your GitHub token, which must be allowed to write (Contents: read and"
            + " write). Every request must carry the key below. Every call brings the game's window to the front first; nothing here sends"
            + " anything to the game.");

        ImGui.TextDisabled(ImGuiText.Escape($"key {AgentKeyStore.Fingerprint(key.Key)}… - put it in the cloud environment as {AgentKeyStore.EnvironmentVariable}"));
        if (ImGui.SmallButton("copy key##agent-link-copy"))
        {
            ImGui.SetClipboardText(key.Key);
            _agentSaid = "the key is in the clipboard - paste it into the cloud environment's settings, never into a chat";
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("renew key##agent-link-renew"))
        {
            _agentSaid = key.Renew()
                ? "a new key - the old one no longer works; copy the new one into the cloud environment"
                : "the new key could not be saved";
        }

        OverlayLayout.Hint("Renew when the key may have gone where it should not: the old one stops working at once.");
        foreach (string line in host.Lines())
        {
            ImGui.TextDisabled(ImGuiText.Escape(line));
        }

        if (_agentSaid.Length > 0)
        {
            ImGui.TextDisabled(ImGuiText.Escape(_agentSaid));
        }

        ImGui.PopTextWrapPos();
    }

    /// <summary>
    /// The overlay's lines for the status tool: the area, the player, who is in front, a capture under way and the panes -
    /// read on the next frame, or why there was none.
    /// </summary>
    public async Task<IReadOnlyList<string>> AgentStatus()
    {
        long pumped = _agentFrames.Pumped;
        long since = pumped == 0 ? -1 : Environment.TickCount64 - pumped;
        try
        {
            return await _agentFrames.Next<IReadOnlyList<string>>(StatusLines).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return [since < 0 ? "overlay: its frames have not run yet" : $"overlay: no frame for {since / 1000.0:F1} s - the overlay is not drawing (its device lost, or it is shutting down)"];
        }
    }

    private List<string> StatusLines()
    {
        WorldSnapshot shot = _snapshot;
        var lines = new List<string>
        {
            $"in front: {_agentFront switch
            {
                GameWindowTracker.Front.Game => "the game - the overlay draws",
                GameWindowTracker.Front.GameElsewhere => "another window of the game - the overlay draws",
                GameWindowTracker.Front.Own => "one of the tool's own windows - the overlay draws",
                _ => "another application - the overlay draws nothing, and pictures and captures wait for the game",
            }}",
            shot.InGame
                ? $"area: {shot.Area.Name} ({shot.Area.Id}), level {shot.AreaLevel}{(shot.Area.IsTown ? ", a town" : string.Empty)}{(shot.Area.IsHideout ? ", a hideout" : string.Empty)}"
                : "area: not in game (a menu, the character select or a loading screen)",
        };
        if (shot.Player is { } player)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"player: {player.Path} at {player.WorldX:F0} {player.WorldY:F0} {player.WorldZ:F0}, level {shot.PlayerLevel}"));
        }

        lines.Add(_capture.Busy || _capture.Phase.Length > 0 ? "capture: " + _capture.Phase : "capture: none running");
        lines.Add($"game window: {(_tracked.IsValid ? $"{_tracked.Width} x {_tracked.Height} at {_tracked.X}, {_tracked.Y}" : "not found")}");
        if (_monsterBook?.Model is { } monster)
        {
            lines.Add("monster pane: " + string.Join(" | ", monster.AgentLines(built: false).Take(4)));
        }

        if (_tileBook?.Model is { } tile)
        {
            lines.Add("tile pane: " + string.Join(" | ", tile.AgentLines(built: false).Take(4)));
        }

        return lines;
    }

    // ── The capture ─────────────────────────────────────────────────────────────────────────────

    private async Task<AgentResult> AgentCapture(JsonElement arguments, CancellationToken cancelling)
    {
        if (arguments.Flag("cancel", false))
        {
            return AgentResult.Say(_capture.CancelRequest() ? "the capture asked for is withdrawn" : "no capture waits to begin");
        }

        List<string> parts = arguments.Texts("parts");
        string[] unknown = [.. parts.Where(part => !CaptureParts.All.Any(known => known.Key == part))];
        if (unknown.Length > 0)
        {
            return AgentResult.Fail($"no part {string.Join(", ", unknown)} - capture_parts lists the keys");
        }

        double minutes = Math.Clamp(arguments.Number("wait_minutes", 30), 0.1, 24 * 60);
        Task<CaptureOutcome> asked = _capture.Request(parts.Count > 0 ? parts : null);
        CaptureOutcome outcome;
        try
        {
            outcome = await asked.WaitAsync(TimeSpan.FromMinutes(minutes), cancelling).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return AgentResult.Say($"the capture has not ended after {minutes:F0} min: {_capture.Phase}. The captures tool lists it once it has.");
        }

        if (outcome.Folder.Length == 0)
        {
            return AgentResult.Fail(outcome.Said);
        }

        var result = new AgentResult { IsError = !outcome.Ok };
        var said = new StringBuilder()
            .Append(outcome.Ok ? "captured: " : "captured, not whole: ").AppendLine(outcome.Said)
            .Append("folder: ").AppendLine(outcome.Folder);
        string summary = Path.Combine(outcome.Folder, CaptureReport.SummaryFile);
        if (File.Exists(summary))
        {
            said.AppendLine().AppendLine(CaptureReport.SummaryFile + ", its head:");
            foreach (string line in File.ReadLines(summary).Take(60))
            {
                said.AppendLine(line);
            }
        }

        result.Content.Add(AgentContent.Of(said.ToString()));
        foreach (string file in outcome.Files)
        {
            result.Files.Add(_agent!.Offer(file));
        }

        return result;
    }

    private async Task<AgentResult> AgentCaptureParts(JsonElement arguments, CancellationToken cancelling)
    {
        IReadOnlySet<string> offSet = (await _agentFrames.Next(() => new Boxed<IReadOnlySet<string>>(CaptureParts.OffOf(_capture.Off, _capture.SwitchedOn)))
            .ConfigureAwait(false)).Value;
        var text = new StringBuilder("key | group | file | ticked | what it is for\n");
        foreach (CapturePart part in CaptureParts.All)
        {
            text.Append(part.Key).Append(" | ").Append(part.Group).Append(" | ").Append(part.File.Length > 0 ? part.File : "-")
                .Append(" | ").Append(offSet.Contains(part.Key) ? "off" : "on").Append(" | ").Append(part.Label)
                .Append(" - ").AppendLine(part.Hint);
        }

        return AgentResult.Say(text.ToString());
    }

    private Task<AgentResult> AgentCaptures(JsonElement arguments, CancellationToken cancelling)
    {
        string root = CaptureReport.Root;
        if (!Directory.Exists(root))
        {
            return Task.FromResult(AgentResult.Say("no captures yet - " + root + " does not exist"));
        }

        int count = (int)Math.Clamp(arguments.Number("count", 10), 1, 200);
        DirectoryInfo[] folders = [.. new DirectoryInfo(root).GetDirectories().OrderByDescending(folder => folder.CreationTimeUtc)];
        var text = new StringBuilder();
        foreach (DirectoryInfo folder in folders.Take(count))
        {
            FileInfo[] zip = Zipped(folder);
            text.Append(folder.Name).Append("  ").Append(zip.Length == 0 ? "not packed" : $"{zip.Sum(piece => piece.Length):N0} bytes in {zip.Length} piece(s)")
                .AppendLine();
        }

        var result = new AgentResult();
        string offer = arguments.Text("offer");
        if (offer.Length > 0)
        {
            DirectoryInfo? chosen = offer == "latest"
                ? folders.FirstOrDefault(folder => Zipped(folder).Length > 0)
                : folders.FirstOrDefault(folder => string.Equals(folder.Name, offer, StringComparison.OrdinalIgnoreCase));
            if (chosen is null)
            {
                return Task.FromResult(AgentResult.Fail($"no packed capture '{offer}'\n{text}"));
            }

            foreach (FileInfo piece in Zipped(chosen))
            {
                result.Files.Add(_agent!.Offer(piece.FullName));
            }

            text.Insert(0, $"offered: {chosen.Name}\n\n");
        }

        result.Content.Add(AgentContent.Of(text.Length > 0 ? text.ToString() : "no captures yet"));
        return Task.FromResult(result);

        static FileInfo[] Zipped(DirectoryInfo folder)
        {
            DirectoryInfo parent = folder.Parent!;
            var whole = new FileInfo(Path.Combine(parent.FullName, folder.Name + ".zip"));
            return whole.Exists
                ? [whole]
                : [.. parent.GetFiles(folder.Name + ".zip.*").Where(piece => piece.Extension.Length == 4).OrderBy(piece => piece.Name, StringComparer.Ordinal)];
        }
    }

    // ── The screenshot ──────────────────────────────────────────────────────────────────────────

    private sealed record Boxed<T>(T Value);

    private async Task<AgentResult> AgentScreenshot(JsonElement arguments, CancellationToken cancelling)
    {
        ClientRect client = (await _agentFrames.Next(() => new Boxed<ClientRect>(_tracked)).ConfigureAwait(false)).Value;
        if (!client.IsValid)
        {
            return AgentResult.Fail("the game's window was not found");
        }

        byte[]? bgr = ScreenCapture.Grab(client.X, client.Y, client.Width, client.Height);
        if (bgr is null)
        {
            return AgentResult.Fail("the screen could not be read");
        }

        int widest = (int)Math.Clamp(arguments.Number("width", 1600), 200, 4096);
        string folder = Path.Combine(AgentTunnel.DefaultFolder, "pictures");
        Directory.CreateDirectory(folder);
        string full = Path.Combine(folder, $"screenshot-{DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
        using Image<Bgr24> image = Image.LoadPixelData<Bgr24>(bgr, client.Width, client.Height);
        await image.SaveAsPngAsync(full, cancelling).ConfigureAwait(false);
        if (image.Width > widest)
        {
            image.Mutate(picture => picture.Resize(widest, 0));
        }

        using var shown = new MemoryStream();
        await image.SaveAsPngAsync(shown, cancelling).ConfigureAwait(false);
        var result = new AgentResult();
        result.Content.Add(AgentContent.Picture(shown.ToArray()));
        result.Content.Add(AgentContent.Of($"the game's window, {client.Width} x {client.Height}, as on the screen now (shown at {image.Width} x {image.Height})"));
        result.Files.Add(_agent!.Offer(full));
        return result;
    }

    // ── The books ───────────────────────────────────────────────────────────────────────────────

    private async Task<AgentResult> AgentMonsterBook(JsonElement arguments, CancellationToken cancelling)
    {
        if (_monsterBook is not { Model: { } } book)
        {
            return AgentResult.Fail("the Monster Book has no model pane in this session (no install to read)");
        }

        var said = new List<string>();
        string monster = arguments.Text("monster");
        bool player = arguments.Flag("player", false);
        string? refused = await _agentFrames.Next(() =>
        {
            _tools.Show("monster-book");
            if (monster.Length > 0)
            {
                (string path, IReadOnlyList<string> others) = book.AgentFind(monster);
                if (path.Length == 0)
                {
                    return $"no monster matches '{monster}'";
                }

                book.AgentChoose(path);
                said.Add("shows " + path);
                if (others.Count > 0)
                {
                    said.Add("also matched: " + string.Join("; ", others));
                }
            }

            if (player)
            {
                said.Add(book.AgentTakePlayer() ? "taking the player" : "the player cannot be taken (no reader, or a take already runs)");
            }

            return string.Empty;
        }).ConfigureAwait(false);
        if (refused.Length > 0)
        {
            return AgentResult.Fail(refused);
        }

        if (player)
        {
            string landed = await _agentFrames.Until(
                () => book.AgentPlayer() is { Landed: true } taken ? (taken.Shown ? "the player: " : "the player was not taken: ") + taken.Said : null,
                TimeSpan.FromSeconds(60),
                static () => "the player's read did not land within a minute").ConfigureAwait(false);
            said.Add(landed);
        }

        int set = (int)arguments.Number("weapon_set", 0);
        string stage = arguments.Text("stage");
        int around = (int)arguments.Number("around", 0);
        bool listing = arguments.Flag("stages", false);
        if (set > 0 || stage.Length > 0 || listing)
        {
            said.AddRange(await _agentFrames.Next<List<string>>(() =>
            {
                var done = new List<string>();
                if (set > 0)
                {
                    done.Add(book.AgentWeaponSet(set));
                }

                if (stage.Length > 0)
                {
                    done.Add(book.AgentStage(stage, around));
                }

                if (listing)
                {
                    done.Add("stages offered: " + string.Join("; ", book.AgentStages().Select(offered => offered.Label)));
                }

                return done;
            }).ConfigureAwait(false));
        }

        return await Answered(book.Model!, arguments, said, cancelling).ConfigureAwait(false);
    }

    private async Task<AgentResult> AgentTileBook(JsonElement arguments, CancellationToken cancelling)
    {
        if (_tileBook is not { Model: { } } book)
        {
            return AgentResult.Fail("the Tile Book has no model pane in this session (no install to read)");
        }

        var said = new List<string>();
        string tile = arguments.Text("tile");
        bool around = arguments.Has("around");
        int side = (int)arguments.Number("around", 0);
        int? doodads = arguments.Has("doodads") ? (int)arguments.Number("doodads", 0) : null;
        bool? tools = arguments.Has("tools") ? arguments.Flag("tools", false) : null;
        bool? asLaid = arguments.Has("as_laid") ? arguments.Flag("as_laid", true) : null;
        bool? fitted = arguments.Has("fitted") ? arguments.Flag("fitted", false) : null;
        string refused = await _agentFrames.Next(() =>
        {
            _tools.Show("tile-book");
            if (tile.Length > 0)
            {
                (string path, IReadOnlyList<string> others) = book.AgentFind(tile);
                if (path.Length == 0)
                {
                    return $"no tile or room matches '{tile}'";
                }

                book.AgentChoose(path);
                said.Add("shows " + path);
                if (others.Count > 0)
                {
                    said.Add("also matched: " + string.Join("; ", others));
                }
            }

            if (doodads is not null || tools is not null || asLaid is not null || fitted is not null)
            {
                said.Add(book.AgentRoom(doodads, tools, asLaid, fitted));
            }

            if (around)
            {
                said.Add(book.AgentAround(side));
            }

            return string.Empty;
        }).ConfigureAwait(false);
        if (refused.Length > 0)
        {
            return AgentResult.Fail(refused);
        }

        return await Answered(book.Model!, arguments, said, cancelling).ConfigureAwait(false);
    }

    private async Task<AgentResult> AgentPane(JsonElement arguments, CancellationToken cancelling)
    {
        bool tile = arguments.Text("book", "monster") == "tile";
        BookWindowParts? parts = tile
            ? _tileBook is { Model: { } tilePane } tileBook ? new BookWindowParts(tilePane, tileBook.Lighting, "tile-book") : null
            : _monsterBook is { Model: { } monsterPane } monsterBook ? new BookWindowParts(monsterPane, monsterBook.Lighting, "monster-book") : null;
        if (parts is null)
        {
            return AgentResult.Fail($"the {(tile ? "Tile" : "Monster")} Book has no model pane in this session");
        }

        var switches = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("light", out JsonElement light) && light.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty one in light.EnumerateObject())
            {
                if (one.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    switches[one.Name] = one.Value.GetBoolean();
                }
            }
        }

        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("light_values", out JsonElement numbers) && numbers.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty one in numbers.EnumerateObject())
            {
                if (one.Value.ValueKind == JsonValueKind.Number)
                {
                    values[one.Name] = one.Value.GetDouble();
                }
            }
        }

        string view = arguments.Text("view");
        string animation = arguments.Text("animation");
        bool? playing = arguments.Has("playing") ? arguments.Flag("playing", true) : null;
        AskedPose? pose = PoseOf(arguments);
        (float X, float Y)? probe = null;
        bool probeOff = false;
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("probe", out JsonElement probed))
        {
            if (probed.ValueKind == JsonValueKind.Object)
            {
                probe = ((float)probed.Number("x", 0.5), (float)probed.Number("y", 0.5));
            }
            else if (probed.ValueKind == JsonValueKind.String && probed.GetString() == "off")
            {
                probeOff = true;
            }
        }

        MonsterPortrait pane = parts.Pane;
        List<string> said = await _agentFrames.Next<List<string>>(() =>
        {
            _tools.Show(parts.Tab);
            var done = new List<string>();
            if (switches.Count > 0 || values.Count > 0)
            {
                if (parts.Lighting is { } panel)
                {
                    done.AddRange(panel.AgentSet(switches, values));
                }
                else
                {
                    done.Add("this book has no light panel");
                }
            }

            if (view.Length > 0)
            {
                done.Add(pane.AgentPress(view));
            }

            if (pose is { } look)
            {
                done.Add(pane.AgentLook(look.Focus, look.Turn, look.Tilt, look.Distance, look.Parallel)
                    ? "the view set"
                    : "the view was not set - no model is shown yet, or the pose is not valid");
            }

            if (animation.Length > 0 || playing is not null)
            {
                done.Add(pane.AgentAnimate(animation, playing));
            }

            if (probeOff)
            {
                pane.AgentProbeOff();
                done.Add("the probe is off");
            }
            else if (probe is { } at)
            {
                pane.AgentProbe(at.X, at.Y);
                done.Add(string.Create(CultureInfo.InvariantCulture, $"probing {at.X:F3} across, {at.Y:F3} down"));
            }

            if (parts.Lighting is { } lit)
            {
                done.Add("light: " + lit.AgentState());
            }

            return done;
        }).ConfigureAwait(false);

        return await Answered(pane, arguments, said, cancelling).ConfigureAwait(false);
    }

    private sealed record BookWindowParts(MonsterPortrait Pane, SceneLightPanel? Lighting, string Tab);

    /// <summary>The parts of a camera pose a call asked for; each left out stays as the pane has it (see MonsterPortrait.AgentLook).</summary>
    private sealed record AskedPose(System.Numerics.Vector3? Focus, float? Turn, float? Tilt, float? Distance, bool? Parallel);

    /// <summary>The pose in the call's "camera" object, or null where it has none.</summary>
    private static AskedPose? PoseOf(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("camera", out JsonElement camera)
            || camera.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        System.Numerics.Vector3? focus = null;
        if (camera.TryGetProperty("focus", out JsonElement at) && at.ValueKind == JsonValueKind.Array && at.GetArrayLength() == 3
            && at.EnumerateArray().All(one => one.ValueKind == JsonValueKind.Number))
        {
            focus = new System.Numerics.Vector3(at[0].GetSingle(), at[1].GetSingle(), at[2].GetSingle());
        }

        static float? Part(JsonElement camera, string name)
            => camera.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : null;

        return new AskedPose(
            focus, Part(camera, "turn"), Part(camera, "tilt"), Part(camera, "distance"),
            camera.Has("parallel") ? camera.Flag("parallel", false) : null);
    }

    /// <summary>
    /// The pane's answer to whatever was just asked of it: the lines, and the picture once drawn (unless the call said no picture).
    /// </summary>
    private async Task<AgentResult> Answered(MonsterPortrait pane, JsonElement arguments, List<string> said, CancellationToken cancelling)
    {
        bool picture = arguments.Flag("picture", true);
        bool built = arguments.Flag("built", false);
        double seconds = Math.Clamp(arguments.Number("timeout", 120), 1, MostPictureSeconds);
        int ticket = await _agentFrames.Next(() => new Boxed<int>(pane.AgentOpen())).ConfigureAwait(false) is { } opened ? opened.Value : 0;

        PaneShot shot = await _agentFrames.Until(
            () => !picture || pane.AgentSettled(ticket) ? Shot(pane, picture, built, string.Empty) : null,
            TimeSpan.FromSeconds(picture ? seconds : 0),
            () => Shot(pane, picture, built, $"waited {seconds:F0} s for the picture: {pane.AgentWaiting()}")).ConfigureAwait(false);

        var result = new AgentResult();
        if (shot.Rgba is { } rgba)
        {
            result.Content.Add(AgentContent.Picture(await Task.Run(() => Png(rgba, shot.Size), cancelling).ConfigureAwait(false)));
        }

        var text = new StringBuilder();
        foreach (string line in said.Where(line => line.Length > 0))
        {
            text.AppendLine(line);
        }

        if (shot.Late.Length > 0)
        {
            text.AppendLine(shot.Late);
        }

        if (shot.Rgba is not null)
        {
            text.AppendLine($"picture: {shot.Size} x {shot.Size}, drawn by {(shot.Card ? "the card" : "the processor")}");
        }

        text.AppendLine();
        foreach (string line in shot.Lines)
        {
            text.AppendLine(line);
        }

        result.Content.Add(AgentContent.Of(text.ToString()));
        return result;
    }

    private sealed record PaneShot(byte[]? Rgba, int Size, bool Card, List<string> Lines, string Late);

    private static PaneShot Shot(MonsterPortrait pane, bool picture, bool built, string late)
    {
        (byte[] Rgba, int Size, bool Card)? drawn = picture ? pane.AgentPicture() : null;
        return new PaneShot(drawn?.Rgba, drawn?.Size ?? 0, drawn?.Card ?? false, pane.AgentLines(built), late);
    }

    /// <summary>A square picture of straight RGBA as a PNG, opaque: the pane draws over its window, which a file has not.</summary>
    private static byte[] Png(byte[] rgba, int size)
    {
        for (int i = 3; i < rgba.Length; i += 4)
        {
            rgba[i] = 255;
        }

        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(rgba, size, size);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
