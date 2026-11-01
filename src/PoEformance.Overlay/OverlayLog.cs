using System.Globalization;
using System.Text;
using PoEformance.Features;

namespace PoEformance.Overlay;

/// <summary>
/// The overlay's own diary: what its window, its graphics device and the game's window did, kept in a file beside the tool.
/// </summary>
/// <remarks>
/// A FILE, BECAUSE THE PROCESS IS WHAT GETS ENDED. The overlay staying empty after an Alt+Tab ended
/// with the tool being killed and started again (said from the live client, v0.1.336), and the exe
/// has had no console for a while: every line written to standard error, the frame's own failures
/// among them, went nowhere. The file outlives the process, and the one before it is kept as
/// <see cref="PreviousName"/> when the next starts, so the session that was killed is still there
/// after the restart - and a capture writes them all (CaptureParts.OverlayLog). From v0.1.438 the
/// <see cref="Kept"/> sessions before it are kept, older ones as overlay.previous-2.log and on.
///
/// SMALL BY DESIGN: a line per change - Present's answer, the game leaving the front and coming back,
/// a stalled frame - never one per frame, and past <see cref="Ceiling"/> one line saying the rest was
/// left out. Flushed with every line, since a line still in a buffer dies with a killed process.
/// </remarks>
public static class OverlayLog
{
    /// <summary>This session's file, beside the tool.</summary>
    public const string FileName = "overlay.log";

    /// <summary>The session before's, kept when the next one starts.</summary>
    public const string PreviousName = "overlay.previous.log";

    /// <summary>
    /// How many sessions before this one are kept: the one before as <see cref="PreviousName"/>, older ones as
    /// <c>overlay.previous-2.log</c> and on - CHOSEN.
    /// </summary>
    /// <remarks>
    /// MORE THAN ONE (v0.1.438): the tool went down after a capture, was started again while the game still ran, and then both went
    /// down; the session that held the capture was two starts back by the time anybody looked, and its log was gone.
    /// </remarks>
    public const int Kept = 4;

    /// <summary>The file of the session <paramref name="back"/> starts before this one (1 the one before).</summary>
    public static string NameBack(int back) => back <= 1 ? PreviousName : $"overlay.previous-{back}.log";

    /// <summary>How much one session writes at most.</summary>
    private const long Ceiling = 2 * 1024 * 1024;

    private static readonly Lock Gate = new();
    private static StreamWriter? _writer;
    private static bool _opened;
    private static bool _full;
    private static long _written;

    /// <summary>Writes one line, stamped with the time. Any thread; never throws.</summary>
    public static void Write(string line)
    {
        string stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ", CultureInfo.InvariantCulture) + line;
        lock (Gate)
        {
            if (!_opened)
            {
                _opened = true;
                _writer = Open();
            }

            if (_writer is null || _full)
            {
                return;
            }

            try
            {
                if (_written + stamped.Length > Ceiling)
                {
                    _full = true;
                    _writer.WriteLine($"{stamped[..24]}the log is full at {Ceiling / (1024 * 1024)} MB; nothing more is written this session");
                    return;
                }

                _writer.WriteLine(stamped);
                _written += stamped.Length + Environment.NewLine.Length;
            }
            catch (IOException)
            {
                // A full disk or a file somebody locked: the diary is not worth a failed frame.
                _writer = null;
            }
        }
    }

    /// <summary>
    /// Every file whole, this session's first, then the ones before it newest first - what a capture writes.
    /// </summary>
    public static string Text()
    {
        var text = new StringBuilder();
        IEnumerable<(string Name, string Said)> files = new[] { (FileName, "this session"), (PreviousName, "the session before") }
            .Concat(Enumerable.Range(2, Kept - 1).Select(back => (NameBack(back), $"{back} sessions before")));
        foreach ((string name, string said) in files)
        {
            string path = Path.Combine(AppContext.BaseDirectory, name);
            if (name != FileName && name != PreviousName && !File.Exists(path))
            {
                continue;
            }

            text.Append("== ").Append(said).Append(" (").Append(name).AppendLine(") ==");
            try
            {
                // Shared for writing: this session's file is open in Write the whole time.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                text.AppendLine(reader.ReadToEnd());
            }
            catch (IOException exception)
            {
                text.Append("(not read: ").Append(exception.Message).AppendLine(")");
            }
            catch (UnauthorizedAccessException exception)
            {
                text.Append("(not read: ").Append(exception.Message).AppendLine(")");
            }
        }

        return text.ToString();
    }

    private static StreamWriter? Open()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (File.Exists(path))
            {
                // THE OLDER ONES MOVED ON FIRST, the oldest kept dropped.
                for (int back = Kept; back >= 2; back--)
                {
                    string older = Path.Combine(AppContext.BaseDirectory, NameBack(back - 1));
                    if (File.Exists(older))
                    {
                        File.Move(older, Path.Combine(AppContext.BaseDirectory, NameBack(back)), overwrite: true);
                    }
                }

                File.Move(path, Path.Combine(AppContext.BaseDirectory, PreviousName), overwrite: true);
            }

            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine($"PoEformance {ToolVersion.Said}, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return writer;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // A tool unpacked into a folder it may not write: no diary, and nothing else changes.
            return null;
        }
    }
}
