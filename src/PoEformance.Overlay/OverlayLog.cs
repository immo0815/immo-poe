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
/// after the restart - and a capture writes both (CaptureParts.OverlayLog).
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
    /// Both files whole, this session's first - what a capture writes. Empty where neither exists.
    /// </summary>
    public static string Text()
    {
        var text = new StringBuilder();
        foreach ((string name, string said) in new[] { (FileName, "this session"), (PreviousName, "the session before") })
        {
            string path = Path.Combine(AppContext.BaseDirectory, name);
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
