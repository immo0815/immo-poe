using System.Globalization;
using System.Text;

namespace PoEformance.Features;

/// <summary>
/// The crashes Windows wrote down for the tool and the game (crashes.txt): the Application log's crash and hang events of the last
/// fortnight naming either - what an ended process can no longer say for itself.
/// </summary>
/// <remarks>
/// WHY (v0.1.438): the game and the tool both went down after a capture (said from the live client, 2026-11-01), the tool first - its
/// restart at 20:48:55 found the game's process still running - and nothing said why: the tool caught no exception it did not expect,
/// wrote nothing as it ended, and kept one log before its own, which the second restart overwrote. A native fault, a fail-fast or a
/// stack overflow ends a process before any of its own code runs; Windows writes those down regardless - Application Error (1000:
/// the faulting module, the exception code and offset), Windows Error Reporting (1001), Application Hang (1002) and .NET Runtime
/// (1026: a managed exception's stack) - so the capture reads them.
///
/// READ BY NAME, NOT BY WORDING: the events' text is the system language's, the exe names are not, so a block is kept where it names
/// <see cref="Names"/> in any case.
/// </remarks>
public static class CaptureCrashes
{
    /// <summary>The Application log's query: crash, report, hang and .NET runtime events of the last fourteen days.</summary>
    public const string Query =
        "*[System[(EventID=1000 or EventID=1001 or EventID=1002 or EventID=1026) and TimeCreated[timediff(@SystemTime) <= 1209600000]]]";

    /// <summary>How many events the query reads at most, newest first - CHOSEN.</summary>
    public const int MostEvents = 500;

    /// <summary>The names a kept event mentions: the tool's exe and the game's.</summary>
    public static IReadOnlyList<string> Names { get; } = ["PoEformance", "PathOfExile"];

    /// <summary>
    /// The events of <paramref name="events"/> (wevtutil's text: each one opening with a line <c>Event[n]:</c>) that name one of
    /// <paramref name="names"/>, each whole, newest first as the query gave them.
    /// </summary>
    /// <param name="events">The query's output.</param>
    /// <param name="names">The names an event must mention, any case.</param>
    public static List<string> Pick(string events, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(names);
        var picked = new List<string>();
        var block = new StringBuilder();
        void Close()
        {
            string text = block.ToString().TrimEnd();
            if (text.Length > 0 && names.Any(name => text.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                picked.Add(text);
            }

            block.Clear();
        }

        foreach (string line in events.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("Event[", StringComparison.Ordinal))
            {
                Close();
            }

            block.AppendLine(line);
        }

        Close();
        return picked;
    }

    /// <summary>The part's lines: what was asked, then every event naming the tool or the game, or why there are none.</summary>
    /// <param name="events">The query's output, or null where it did not run.</param>
    /// <param name="why">Why the query did not run or what it said on failing; null where it ran.</param>
    public static List<string> Report(string? events, string? why)
    {
        var lines = new List<string>
        {
            "crashes Windows wrote down: the Application log's events 1000 (Application Error), 1001 (Windows Error Reporting), 1002 (Application Hang) and 1026 (.NET Runtime) of the last 14 days naming "
                + string.Join(" or ", Names) + string.Create(CultureInfo.InvariantCulture, $", newest first (at most {MostEvents} events read)"),
        };
        if (events is null)
        {
            lines.Add("not read: " + (why ?? "the query did not run"));
            return lines;
        }

        List<string> picked = Pick(events, Names);
        int all = events.Split('\n').Count(line => line.StartsWith("Event[", StringComparison.Ordinal));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{picked.Count} of the {all} events read name the tool or the game{(why is null ? string.Empty : "; the query also said: " + why)}"));
        foreach (string one in picked)
        {
            lines.Add(string.Empty);
            lines.AddRange(one.Split('\n').Select(line => line.TrimEnd('\r')));
        }

        return lines;
    }
}
