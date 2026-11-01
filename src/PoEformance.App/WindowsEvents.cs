using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PoEformance.App;

/// <summary>
/// Reads Windows' own event logs through wevtutil - the crashes part's source (CaptureCrashes).
/// </summary>
/// <remarks>
/// WEVTUTIL, NOT THE EVENT LOG API: it ships with every Windows, needs no package and nothing the Native AOT build would have to keep,
/// and its text is what a person reads in the Event Viewer. Its output is read as bytes and decoded by what it holds: /uni:true asks
/// for UTF-16, and where a build answers in its console code page instead the names the part looks for are ASCII either way.
/// </remarks>
internal static class WindowsEvents
{
    /// <summary>How long the query may take - CHOSEN.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The events of <paramref name="log"/> matching <paramref name="query"/> (an XPath filter), newest first, at most
    /// <paramref name="most"/>, as text; and what went wrong, where something did.
    /// </summary>
    public static (string? Text, string? Why) Query(string log, string query, int most)
    {
        var start = new ProcessStartInfo("wevtutil.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (string argument in new[] { "qe", log, "/q:" + query, "/rd:true", "/f:text", "/uni:true", "/c:" + most.ToString(System.Globalization.CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return (null, "wevtutil did not start");
            }

            using var bytes = new MemoryStream();
            Task copied = process.StandardOutput.BaseStream.CopyToAsync(bytes);
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Patience))
            {
                process.Kill();
                return (null, $"wevtutil did not answer within {Patience.TotalSeconds:0} s");
            }

            copied.GetAwaiter().GetResult();
            string said = error.GetAwaiter().GetResult().Trim();
            string text = Decoded(bytes.ToArray());
            return process.ExitCode == 0
                ? (text, said.Length > 0 ? said : null)
                : (text.Length > 0 ? text : null, $"wevtutil ended with {process.ExitCode}{(said.Length > 0 ? ": " + said : string.Empty)}");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return (null, exception.Message);
        }
    }

    /// <summary>UTF-16 where every second byte of the start is nought (or a byte order mark says so), else Latin-1.</summary>
    private static string Decoded(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        int probe = Math.Min(bytes.Length, 256) & ~1;
        int zeros = 0;
        for (var at = 1; at < probe; at += 2)
        {
            zeros += bytes[at] == 0 ? 1 : 0;
        }

        return probe > 0 && zeros * 4 >= probe ? Encoding.Unicode.GetString(bytes) : Encoding.Latin1.GetString(bytes);
    }
}
