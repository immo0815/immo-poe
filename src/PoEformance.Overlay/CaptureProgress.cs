using System.Globalization;
using System.Numerics;
using System.Runtime.Versioning;
using ImGuiNET;
using PoEformance.Features;

namespace PoEformance.Overlay;

/// <summary>Where one part of a capture stands.</summary>
internal enum CaptureStepState
{
    Waiting,
    Running,
    Done,
    NotWritten,
    Failed,
}

/// <summary>
/// One part of a capture as its window shows it: what it is, where it stands, what it wrote - and while it runs, what it is doing.
/// </summary>
/// <remarks>
/// WRITTEN BY THE CAPTURE'S THREADS AND READ BY THE OVERLAY'S, so every change and every read goes
/// through one small lock: a state, a line and two times have to be read together, and a frame reads
/// some thirty of them.
/// </remarks>
internal sealed class CaptureStep(string file, string label)
{
    private readonly Lock _gate = new();
    private CaptureStepState _state;
    private string _result = string.Empty;
    private long _began;
    private long _ended;

    /// <summary>The file it writes - the name capture.txt lists it under.</summary>
    public string File { get; } = file;

    /// <summary>What it is, as the Keys page calls it.</summary>
    public string Label { get; } = label;

    /// <summary>What it is doing while it runs, asked every frame - null for nothing more than its time.</summary>
    public Func<string>? Live { get; init; }

    /// <summary>How far it is while it runs, nought to one - null where that is not known.</summary>
    public Func<float>? Fraction { get; init; }

    /// <summary>Ends it early, keeping what it has - null where it cannot be.</summary>
    public Action? Stop { get; init; }

    /// <summary>What it asks of the person at the game while it runs - a banner over the window - null for nothing.</summary>
    public Func<CaptureAsk?>? Ask { get; init; }

    /// <summary>Whether <see cref="Stop"/> was pressed.</summary>
    public bool Stopped { get; set; }

    /// <summary>Marks it running from now, unless it already is.</summary>
    public void Start(long now)
    {
        lock (_gate)
        {
            if (_state == CaptureStepState.Waiting)
            {
                _state = CaptureStepState.Running;
                _began = now;
            }
        }
    }

    /// <summary>Marks it finished, with the line capture.txt has for it.</summary>
    public void End(CaptureStepState state, string result, long now)
    {
        lock (_gate)
        {
            if (_state == CaptureStepState.Waiting)
            {
                _began = now;
            }

            _state = state;
            _result = result;
            _ended = now;
        }
    }

    /// <summary>Everything about it at once.</summary>
    public (CaptureStepState State, string Result, long Began, long Ended) Read()
    {
        lock (_gate)
        {
            return (_state, _result, _began, _ended);
        }
    }
}

/// <summary>
/// One capture's progress, part by part, and the window that shows it - one window for every part, from the key's press to the zip.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT: a capture that searches the game's memory for a minute needs to say
/// so while it does, in a small window of its own rather than the main one - every part with where it
/// stands, the long ones with what they are doing and how far they are, and a summary once the zip is
/// written. One window for all of them, not one per diagnosis.
///
/// NEVER IN THE PICTURE OF THE GAME ALONE: the key hides the whole overlay for that shot and this
/// window is drawn only after it, so the first frames of a capture show nothing of it.
///
/// THE CATALOGUE'S ORDER, which is the order the Keys page lists the boxes in - not the order they are
/// written in, which is the writer's business and changes as parts are added. The parts running are
/// named above the list, and the list scrolls to the one being written.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class CaptureProgress(string folder, string area, long began)
{
    /// <summary>The window's content width.</summary>
    private const float Width = 620f;

    /// <summary>Most rows the list shows before it scrolls.</summary>
    private const int MostRows = 14;

    /// <summary>The tone as an asking to act begins: high and short - CHOSEN.</summary>
    private const int ActPitch = 1320;

    /// <summary>The tone as it ends: low and longer - CHOSEN.</summary>
    private const int DonePitch = 520;

    private readonly List<CaptureStep> _steps = [];
    private readonly Lock _gate = new();
    private string _outcome = string.Empty;
    private bool _ok = true;
    private long _ended;
    private CaptureStep? _scrolledTo;
    private bool _acting;

    /// <summary>The capture's folder.</summary>
    public string Folder { get; } = folder;

    /// <summary>The area it was taken in.</summary>
    public string Area { get; } = area;

    /// <summary>When the key went down.</summary>
    public long Began { get; } = began;

    /// <summary>Whether it is finished - written and packed, or given up.</summary>
    public bool Finished
    {
        get
        {
            lock (_gate)
            {
                return _ended != 0;
            }
        }
    }

    /// <summary>Adds a part, in the order the window lists them. Only before the capture's threads start.</summary>
    public CaptureStep Add(CaptureStep step)
    {
        _steps.Add(step);
        return step;
    }

    /// <summary>The part writing a file, or null where none does.</summary>
    public CaptureStep? Of(string file) => _steps.Find(step => string.Equals(step.File, file, StringComparison.Ordinal));

    /// <summary>
    /// Ends the capture: its outcome, and every part nothing reached marked as such.
    /// </summary>
    /// <remarks>
    /// A PART STILL WAITING HERE was ticked but nothing wrote it - a window's diagnosis whose window this
    /// session never made. Said so, rather than left waiting in a finished capture.
    /// </remarks>
    public void Finish(string outcome, bool ok, long now)
    {
        foreach (CaptureStep step in _steps)
        {
            if (step.Read().State == CaptureStepState.Waiting)
            {
                step.End(CaptureStepState.NotWritten, "not written: nothing in this session offers it", now);
            }
        }

        lock (_gate)
        {
            _outcome = outcome;
            _ok = ok;
            _ended = now;
        }
    }

    /// <summary>The window's content - inside its Begin and End.</summary>
    public void Draw(long now)
    {
        string outcome;
        bool ok;
        long ended;
        lock (_gate)
        {
            outcome = _outcome;
            ok = _ok;
            ended = _ended;
        }

        var states = new (CaptureStepState State, string Result, long Began, long Ended)[_steps.Count];
        int finished = 0, written = 0, notWritten = 0, failed = 0;
        float partly = 0f;
        for (var at = 0; at < _steps.Count; at++)
        {
            states[at] = _steps[at].Read();
            switch (states[at].State)
            {
                case CaptureStepState.Done:
                    written++;
                    finished++;
                    break;
                case CaptureStepState.NotWritten:
                    notWritten++;
                    finished++;
                    break;
                case CaptureStepState.Failed:
                    failed++;
                    finished++;
                    break;
                case CaptureStepState.Running when _steps[at].Fraction is { } fraction:
                    partly += Math.Clamp(fraction(), 0f, 1f);
                    break;
            }
        }

        long took = (ended != 0 ? ended : now) - Began;
        Banner(states);
        ImGui.TextColored(OverlayInk.Quiet, Folder.Length > 0 ? $"{Area} · {Path.GetFileName(Folder)}" : Area);
        float whole = _steps.Count == 0 ? 1f : (finished + partly) / _steps.Count;
        ImGui.ProgressBar(ended != 0 ? 1f : whole, new Vector2(Width, 0), Invariant($"{finished} of {_steps.Count} parts · {took / 1000.0:0.0} s"));

        if (ended == 0)
        {
            Running(states, now);
        }
        else
        {
            Summary(outcome, ok, took, written, notWritten, failed);
        }

        ImGui.Spacing();
        List(states, now);
    }

    /// <summary>
    /// What a running part asks of the person at the game, as a banner over everything else: a wide coloured bar - green to act, amber to
    /// stand still and wait - its words set large, a line under them and the seconds left; a high tone as an asking to act begins and a low
    /// one as it ends, so the person looking at the game rather than this window knows when to move and when to stop.
    /// </summary>
    /// <remarks>
    /// ASKED FOR FROM THE LIVE CLIENT (v0.1.407): the bone watch's "walk, stop and turn" stood in a line among thirty parts, and the 04:49
    /// capture was taken standing still - "in that window finding anything is hard". The first running part asking anything is shown.
    /// </remarks>
    private void Banner((CaptureStepState State, string Result, long Began, long Ended)[] states)
    {
        CaptureAsk? ask = null;
        for (var at = 0; at < _steps.Count && ask is null; at++)
        {
            if (states[at].State == CaptureStepState.Running && _steps[at].Ask is { } asking)
            {
                ask = asking();
            }
        }

        bool acting = ask is { Act: true };
        if (acting != _acting)
        {
            SoundCue.Play(acting ? ActPitch : DonePitch, acting ? 250 : 450);
            _acting = acting;
        }

        if (ask is null)
        {
            return;
        }

        Vector4 fill = ask.Act ? OverlayInk.Good : OverlayInk.Warn;
        string left = double.IsFinite(ask.Left) ? Invariant($"{Math.Ceiling(Math.Max(0, ask.Left)):0} s left") : string.Empty;
        Vector2 from = ImGui.GetCursorScreenPos();
        float pad = ImGui.GetStyle().FramePadding.Y * 2;
        float big = 0;
        OverlayFonts.Heading(() => big = ImGui.CalcTextSize(ask.Text, false, Width - (2 * pad)).Y);
        float line = ImGui.GetTextLineHeight();
        float height = big + line + (3 * pad);
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(from, from + new Vector2(Width, height), ImGui.GetColorU32(fill), 6f);
        uint ink = ImGui.GetColorU32(OverlayInk.Panel);

        // THE WORDS LARGE, then the line under them and the count on its right.
        ImGui.SetCursorScreenPos(from + new Vector2(pad, pad));
        ImGui.PushStyleColor(ImGuiCol.Text, ink);
        OverlayFonts.Heading(() =>
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Width - (2 * pad));
            ImGui.TextUnformatted(ask.Text);
            ImGui.PopTextWrapPos();
        });
        ImGui.SetCursorScreenPos(from + new Vector2(pad, big + (2 * pad)));
        ImGui.TextUnformatted(ask.Detail);
        if (left.Length > 0)
        {
            float wide = ImGui.CalcTextSize(left).X;
            ImGui.SetCursorScreenPos(from + new Vector2(Width - pad - wide, big + (2 * pad)));
            ImGui.TextUnformatted(left);
        }

        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(from + new Vector2(0, height + ImGui.GetStyle().ItemSpacing.Y));
        ImGui.Dummy(new Vector2(Width, 0));
    }

    /// <summary>The parts running now - the long ones with what they are doing, how far they are and a stop where there is one.</summary>
    private void Running((CaptureStepState State, string Result, long Began, long Ended)[] states, long now)
    {
        for (var at = 0; at < _steps.Count; at++)
        {
            if (states[at].State != CaptureStepState.Running)
            {
                continue;
            }

            CaptureStep step = _steps[at];
            string live = step.Live?.Invoke() ?? string.Empty;
            ImGui.TextColored(OverlayInk.Accent, step.Label);
            ImGui.SameLine();
            ImGui.TextColored(OverlayInk.Aside, Invariant($"{step.File} · {(now - states[at].Began) / 1000.0:0.0} s"));
            if (live.Length > 0)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Width);
                ImGui.TextUnformatted(live);
                ImGui.PopTextWrapPos();
            }

            if (step.Fraction is { } fraction)
            {
                float stopWidth = step.Stop is null ? 0f : ImGui.CalcTextSize(StopLabel).X + (ImGui.GetStyle().FramePadding.X * 2) + ImGui.GetStyle().ItemSpacing.X;
                ImGui.ProgressBar(Math.Clamp(fraction(), 0f, 1f), new Vector2(Width - stopWidth, 0), string.Empty);
                if (step.Stop is { } stop)
                {
                    ImGui.SameLine();
                    ImGui.BeginDisabled(step.Stopped);
                    if (ImGui.Button(StopLabel + "##" + step.File))
                    {
                        step.Stopped = true;
                        stop();
                    }

                    ImGui.EndDisabled();
                    OverlayLayout.Hint("Ends this part now and writes what it found so far - the file says it was stopped.");
                }
            }
        }
    }

    private const string StopLabel = "stop, keep what it found";

    /// <summary>What came of it: where the zip is, and how many parts wrote, did not, and failed.</summary>
    private static void Summary(string outcome, bool ok, long took, int written, int notWritten, int failed)
    {
        PathLink.Line(ok ? OverlayInk.Good : OverlayInk.Bad, outcome);
        ImGui.TextColored(
            failed > 0 ? OverlayInk.Bad : OverlayInk.Quiet,
            Invariant($"{written} written · {notWritten} not written · {failed} failed · {took / 1000.0:0.0} s"));
    }

    /// <summary>Every part: where it stands, how long it took and the line capture.txt has for it - the one being written scrolled to.</summary>
    private void List((CaptureStepState State, string Result, long Began, long Ended)[] states, long now)
    {
        float row = ImGui.GetTextLineHeightWithSpacing() + 2f;
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV;
        if (!ImGui.BeginTable("##captureparts", 3, flags, new Vector2(Width, (Math.Min(_steps.Count, MostRows) + 0.5f) * row)))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("part", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("s", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("what it wrote", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        for (var at = 0; at < _steps.Count; at++)
        {
            CaptureStep step = _steps[at];
            (CaptureStepState state, string result, long began, long ended) = states[at];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Vector4 ink = state switch
            {
                CaptureStepState.Done => OverlayInk.Good,
                CaptureStepState.NotWritten => OverlayInk.Aside,
                CaptureStepState.Failed => OverlayInk.Bad,
                CaptureStepState.Running => OverlayInk.Accent,
                _ => OverlayInk.Quiet,
            };
            ImGui.TextColored(ink, step.File);
            OverlayLayout.Hint(step.Label);
            if (state == CaptureStepState.Running && _scrolledTo != step)
            {
                _scrolledTo = step;
                ImGui.SetScrollHereY(0.5f);
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(OverlayInk.Aside, state == CaptureStepState.Waiting ? string.Empty : Invariant($"{((ended != 0 ? ended : now) - began) / 1000.0:0.0}"));
            ImGui.TableNextColumn();
            string said = state switch
            {
                CaptureStepState.Waiting => "waiting",
                CaptureStepState.Running => step.Stopped ? "stopping..." : "running...",
                _ => result,
            };
            ImGui.PushStyleColor(ImGuiCol.Text, state is CaptureStepState.Done ? OverlayInk.Ink : ink);
            ImGui.TextWrapped(said);
            ImGui.PopStyleColor();
        }

        ImGui.EndTable();
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
