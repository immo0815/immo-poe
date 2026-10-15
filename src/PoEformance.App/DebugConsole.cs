using System.Runtime.InteropServices;

namespace PoEformance.App;

/// <summary>Opens a console window for the debug run, and only then.</summary>
/// <remarks>
/// The exe is a windowed program, so a normal start shows the game overlay and nothing else. The
/// console that the diagnostics print to is attached on demand, before anything writes to it - the
/// standard output handles are bound on first use, so attaching late would leave the output going
/// nowhere.
/// </remarks>
internal static partial class DebugConsole
{
    public static void Attach() => AllocConsole();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();
}
