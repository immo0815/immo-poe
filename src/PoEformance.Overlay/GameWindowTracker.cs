using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PoEformance.Overlay;

/// <summary>The game's drawable area in screen pixels.</summary>
public readonly record struct ClientRect(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>
/// Follows the game window, so the overlay covers exactly the area the game draws into.
/// </summary>
/// <remarks>
/// Without this the overlay keeps its default size and the projection is computed against
/// the WRONG viewport: NDC is resolution-independent, so the maths still "works" and the
/// player still reads as centred, but every dot is placed inside a small window that does
/// not line up with the game. That makes a viewport mismatch easy to miss - hence tracking
/// it explicitly rather than assuming a full-screen game.
///
/// It is the CLIENT rect, not the window rect: in windowed mode the border and title bar
/// are not part of what the game renders, and using the window rect would offset every dot
/// by the title bar height. The client origin is converted to screen coordinates, which
/// makes borderless, windowed and full-screen all fall out of the same code path.
/// </remarks>
[SupportedOSPlatform("windows")]
public static partial class GameWindowTracker
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hWnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(IntPtr hWnd, ref Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool join);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    /// <summary>ShowWindow's SW_RESTORE: a minimised window back to its place.</summary>
    private const int Restore = 9;

    private static readonly Lock Gate = new();
    private static IntPtr _followed;
    private static uint _process;
    private static long _nextLook;
    private static IntPtr _saidGone;

    /// <summary>
    /// Starts following the game's window: <see cref="Game"/> answers with it, and with the game's
    /// next window should this one go. Zero follows nothing (a replay).
    /// </summary>
    public static void Follow(IntPtr window)
    {
        lock (Gate)
        {
            if (window == _followed)
            {
                return;
            }

            _followed = window;
            _process = 0;
            if (window != IntPtr.Zero)
            {
                GetWindowThreadProcessId(window, out _process);
            }
        }
    }

    /// <summary>The process the followed window belongs to, or zero.</summary>
    public static uint GameProcess => Volatile.Read(ref _process);

    /// <summary>
    /// The game's window as it stands: the one followed while it is still a window, else the same
    /// process's main window once it has one. Any thread.
    /// </summary>
    /// <remarks>
    /// READ ONCE AT ATTACH, A HANDLE WAS FOR LIFE. Were the game ever to make its window anew, the
    /// handle read at attach would name nothing, the drawing gate would never see the game in front
    /// again, and the overlay would stay empty until the tool was restarted - which is how the
    /// overlay staying away after an Alt+Tab looked (said from the live client, v0.1.336). Whether
    /// the game does is not known; the log says so if it happens. GameHelper2 holds its
    /// Process.MainWindowHandle the same way and checks only that it is not zero.
    ///
    /// CHEAP WHILE IT HOLDS: one IsWindow a call. The search for a new one - a Process for the id,
    /// its MainWindowHandle - runs at most once a second, and only while the old one is gone.
    /// </remarks>
    public static IntPtr Game
    {
        get
        {
            IntPtr window = Volatile.Read(ref _followed);
            return window == IntPtr.Zero || IsWindow(window) ? window : Refind(window);
        }
    }

    private static IntPtr Refind(IntPtr gone)
    {
        lock (Gate)
        {
            long now = Environment.TickCount64;
            if (_followed != gone || now < _nextLook)
            {
                return _followed;
            }

            _nextLook = now + 1000;
            IntPtr found = IntPtr.Zero;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)_process);
                found = process.MainWindowHandle;
            }
            catch (ArgumentException)
            {
                // The game has exited.
            }
            catch (InvalidOperationException)
            {
                // The same, seen from the other side of the process's lifetime.
            }

            if (found == IntPtr.Zero || found == gone)
            {
                if (_saidGone != gone)
                {
                    _saidGone = gone;
                    OverlayLog.Write($"the game's window 0x{gone:X} is gone and process {_process} shows no other yet");
                }

                return gone;
            }

            OverlayLog.Write($"the game's window 0x{gone:X} is gone; following its process's new window 0x{found:X}");
            Volatile.Write(ref _followed, found);
            return found;
        }
    }

    /// <summary>Who is in front: the game, a window of this tool, or something else.</summary>
    public enum Front
    {
        /// <summary>Something else - another program, or nothing.</summary>
        Elsewhere,

        /// <summary>The followed window of the game.</summary>
        Game,

        /// <summary>The game's process, through a window other than the one followed.</summary>
        GameElsewhere,

        /// <summary>A window of this process - the overlay itself, the config window.</summary>
        Own,
    }

    /// <summary>
    /// Who is in front, from ONE look at the foreground window - what the drawing gate and the log both ask.
    /// </summary>
    public static (Front Who, IntPtr Window, uint Process) InFront(IntPtr game)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return (Front.Elsewhere, foreground, 0);
        }

        if (foreground == game)
        {
            return (Front.Game, foreground, GameProcess);
        }

        GetWindowThreadProcessId(foreground, out uint processId);
        Front who = processId == (uint)Environment.ProcessId ? Front.Own
            : processId != 0 && processId == GameProcess ? Front.GameElsewhere
            : Front.Elsewhere;
        return (who, foreground, processId);
    }

    /// <summary>What <see cref="BringToFront"/> came to: whether the game is in front now, whether it had to be brought, and what to say.</summary>
    public readonly record struct Brought(bool InFront, bool Moved, string Said);

    /// <summary>
    /// Brings the game's window to the front where another window is - the game's own other windows count as the game. Call it on a
    /// thread with a message queue (the render thread): AttachThreadInput fails on one without.
    /// </summary>
    /// <remarks>
    /// ASKED FOR FROM THE LIVE CLIENT (2026-11-01): the agent link brings the game to the front before every call, so a capture or a
    /// picture never waits on a person to click the game. NO KEY IS SENT: the common trick presses Alt so that Windows lets a background
    /// process take the front, and Alt held in the game shows its items' labels. Instead this thread joins the input of the window in
    /// front for the moment of the call (AttachThreadInput), which Windows takes as the right to hand the front on. A minimised game is
    /// restored first.
    /// </remarks>
    public static Brought BringToFront()
    {
        IntPtr game = Game;
        if (game == IntPtr.Zero)
        {
            return new Brought(false, false, "no game window is followed");
        }

        (Front who, IntPtr front, _) = InFront(game);
        if (who is Front.Game or Front.GameElsewhere)
        {
            return new Brought(true, false, "the game was in front");
        }

        if (IsIconic(game))
        {
            ShowWindow(game, Restore);
        }

        uint mine = GetCurrentThreadId();
        uint theirs = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, out _);
        bool joined = theirs != 0 && theirs != mine && AttachThreadInput(mine, theirs, true);
        try
        {
            BringWindowToTop(game);
            SetForegroundWindow(game);
        }
        finally
        {
            if (joined)
            {
                AttachThreadInput(mine, theirs, false);
            }
        }

        (Front now, IntPtr after, _) = InFront(game);
        return now is Front.Game or Front.GameElsewhere
            ? new Brought(true, true, $"brought the game's window to the front ({who} was, 0x{front:X})")
            : new Brought(false, false, $"Windows did not bring the game's window to the front: 0x{after:X} is ({now}, {(joined ? "its input joined" : "its input not joined")})");
    }

    /// <summary>True when the game is the window the user is actually looking at.</summary>
    /// <remarks>
    /// This is the KEYSTROKE test, and it is deliberately strict: synthesised input lands
    /// wherever focus is, not where it was aimed, so anything but the game itself in front
    /// means the keystroke would be typed into something else. Drawing asks a wider
    /// question - see <see cref="IsOwnProcessForeground"/>.
    /// </remarks>
    public static bool IsForeground(IntPtr windowHandle)
        => windowHandle != IntPtr.Zero && GetForegroundWindow() == windowHandle;

    /// <summary>True when a window of THIS process is in front.</summary>
    /// <remarks>
    /// The drawing test needs this alongside the game's own window, and leaving it out
    /// makes the overlay's own controls impossible to use: clicking one gives the overlay
    /// window focus, at which point the game is no longer foreground, so the next frame
    /// draws nothing, so there is nothing left to click. The window vanishes under the
    /// cursor and neither dragging nor a button ever registers.
    ///
    /// It also keeps the overlay up while the config window has focus, which is what makes
    /// a setting visibly take effect as it is changed.
    /// </remarks>
    public static bool IsOwnProcessForeground()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(foreground, out uint processId);
        return processId == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// Where the mouse is inside the game's client area, or null when it is elsewhere.
    /// </summary>
    /// <remarks>
    /// In the same pixels <see cref="PoEformance.Game.World.WorldToScreen"/> projects into, so
    /// a monster's projected point and the cursor can be compared directly - which is what
    /// "how many monsters am I aiming at" needs and what the AHK tool's own cursor radius
    /// does. Un-projecting the cursor into the world would be the other way round, and it
    /// means intersecting a ray with a ground plane that is not flat.
    ///
    /// GetCursorPos rather than a window message, for the reason the overlay's click-through
    /// icon relies on: a message-driven position freezes the moment the pointer leaves, and
    /// the answer here has to stay right while somebody is aiming.
    ///
    /// Null when the pointer is OUTSIDE the client area. Clamping instead would report a
    /// cursor parked over the inventory as being at the edge of the world, and a rule aimed
    /// at it would fire on whatever monster stood nearest that edge.
    /// </remarks>
    public static (float X, float Y)? CursorInClient(IntPtr windowHandle)
    {
        ClientRect client = TryGet(windowHandle);
        if (!client.IsValid || !GetCursorPos(out Point cursor))
        {
            return null;
        }

        float x = cursor.X - client.X;
        float y = cursor.Y - client.Y;

        return x >= 0 && y >= 0 && x < client.Width && y < client.Height ? (x, y) : null;
    }

    /// <summary>
    /// Reads the game window's client area in screen coordinates, or an invalid rect when
    /// the window is gone or minimised.
    /// </summary>
    public static ClientRect TryGet(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle) || !GetClientRect(windowHandle, out Rect rect))
        {
            return default;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return default; // minimised
        }

        var origin = new Point { X = 0, Y = 0 };
        return ClientToScreen(windowHandle, ref origin)
            ? new ClientRect(origin.X, origin.Y, width, height)
            : default;
    }
}
