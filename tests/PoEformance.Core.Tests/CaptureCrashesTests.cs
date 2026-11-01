using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The crashes part (crashes.txt): of wevtutil's text, the events naming the tool or the game, each whole, whatever language the
/// system writes the rest in.
/// </summary>
public sealed class CaptureCrashesTests
{
    private const string Events =
        "Event[0]:\r\n  Log Name: Application\r\n  Source: Application Error\r\n  Event ID: 1000\r\n  Description: \r\n"
        + "Name der fehlerhaften Anwendung: PoEformance.exe, Version: 0.1.436.0\r\nAusnahmecode: 0xc0000005\r\n"
        + "Event[1]:\r\n  Log Name: Application\r\n  Source: Application Error\r\n  Event ID: 1000\r\n  Description: \r\n"
        + "Faulting application name: chrome.exe\r\n"
        + "Event[2]:\r\n  Log Name: Application\r\n  Source: .NET Runtime\r\n  Event ID: 1026\r\n  Description: \r\n"
        + "Application: PathOfExileSteam.exe\r\n";

    [Fact]
    public void ONLYTheEventsNamingTheToolOrTheGameAreKeptEachWhole()
    {
        List<string> picked = CaptureCrashes.Pick(Events, CaptureCrashes.Names);

        Assert.Equal(2, picked.Count);
        Assert.StartsWith("Event[0]:", picked[0], StringComparison.Ordinal);
        Assert.Contains("Ausnahmecode: 0xc0000005", picked[0], StringComparison.Ordinal);
        Assert.Contains("PathOfExileSteam.exe", picked[1], StringComparison.Ordinal);
        Assert.DoesNotContain(picked, one => one.Contains("chrome", StringComparison.Ordinal));
    }

    [Fact]
    public void THEREPORTSaysHowManyOfTheEventsReadNamedEitherAndWhyNoneWereRead()
    {
        Assert.Contains("2 of the 3 events read name the tool or the game", CaptureCrashes.Report(Events, null)[1], StringComparison.Ordinal);
        Assert.Equal("not read: wevtutil did not start", CaptureCrashes.Report(null, "wevtutil did not start")[1]);
    }
}
