using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using PoEformance.Features;

namespace PoEformance.App;

/// <summary>
/// The agent link's tools that belong to the process rather than the overlay: the status, the log and the update.
/// </summary>
/// <remarks>
/// THE UPDATE IS THE BUTTONS' OWN (see <see cref="UpdateService"/>): check, download, install - and install restarts the
/// tool. Its answer is sent first, the restart a moment after, so the agent hears that it is coming; the tool comes back
/// on a new tunnel address the bridge finds on the agent-link branch.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class AgentAppTools
{
    /// <summary>How long after its answer an install ends the process - long enough for the answer to leave through the tunnel.</summary>
    private static readonly TimeSpan RestartAfter = TimeSpan.FromSeconds(3);

    /// <param name="link">The protocol to bind to.</param>
    /// <param name="host">The link itself, for its own lines.</param>
    /// <param name="updates">The update check and installer.</param>
    /// <param name="overlay">The overlay, once it runs.</param>
    /// <param name="attached">A line saying which game process the tool reads.</param>
    public static void Bind(AgentLink link, AgentLinkHost host, UpdateService updates, Func<PoEformance.Overlay.EntityOverlay?> overlay, string attached)
    {
        DateTime started = DateTime.Now;
        link.Bind(AgentCatalogue.Status, async (_, _) =>
        {
            var text = new StringBuilder()
                .Append("PoEformance ").Append(ToolVersion.Said).Append(" - build ").AppendLine(BuildStamp.Load().Describe())
                .Append(CultureInfo.InvariantCulture, $"process {Environment.ProcessId}, started {started:yyyy-MM-dd HH:mm:ss}, up {DateTime.Now - started:hh\\:mm\\:ss}")
                .AppendLine()
                .AppendLine("game: " + attached)
                .Append("update: ").Append(updates.Check.Status);
            if (updates.Installer.Step != UpdateStep.Idle)
            {
                text.Append(" · installer ").Append(updates.Installer.Step).Append(": ").Append(updates.Installer.Status);
            }

            text.AppendLine().AppendLine("link: " + string.Join(" · ", host.Lines()));
            IReadOnlyList<string> lines = overlay() is { } running
                ? await running.AgentStatus().ConfigureAwait(false)
                : ["overlay: not running in this session (started without it, or attached to a replay)"];
            foreach (string line in lines)
            {
                text.AppendLine(line);
            }

            return AgentResult.Say(text.ToString());
        });

        link.Bind(AgentCatalogue.Log, (arguments, _) =>
        {
            int count = (int)Math.Clamp(arguments.Number("lines", 200), 1, 5000);
            string name = arguments.Flag("previous", false) ? PoEformance.Overlay.OverlayLog.PreviousName : PoEformance.Overlay.OverlayLog.FileName;
            string path = Path.Combine(AppContext.BaseDirectory, name);
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                string[] all = reader.ReadToEnd().Split('\n');
                return Task.FromResult(AgentResult.Say($"{name}, its last {Math.Min(count, all.Length)} of {all.Length} lines:\n"
                    + string.Join('\n', all.Skip(Math.Max(0, all.Length - count)))));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(AgentResult.Fail($"{name} could not be read: {exception.Message}"));
            }
        });

        link.Bind(AgentCatalogue.Update, (arguments, cancelling) => Update(updates, arguments.Text("action", "check"), cancelling));
    }

    private static async Task<AgentResult> Update(UpdateService updates, string action, CancellationToken cancelling)
    {
        UpdateCheck check = updates.Check;
        UpdateInstaller installer = updates.Installer;
        if (action is not ("check" or "download" or "install"))
        {
            return AgentResult.Fail($"'{action}' is not an update action (check, download, install)");
        }

        // THE CHECK FIRST, always: a download or an install of a build nobody has asked about is the build of six hours ago.
        // Awaited rather than Refresh: Refresh starts the check on a task and Busy is claimed inside it, so a wait on Busy
        // right after could read the answer of six hours ago. One already on its way is waited for instead.
        await check.CheckAsync(cancelling).ConfigureAwait(false);
        await Until(() => !check.Busy, TimeSpan.FromSeconds(60), cancelling).ConfigureAwait(false);
        string checkedSays = $"running {check.Local.Describe()}; published {check.Remote.Describe()}: {check.Status}";
        if (action == "check")
        {
            return AgentResult.Say(checkedSays);
        }

        if (check.Verdict != UpdateVerdict.Available)
        {
            return AgentResult.Say(checkedSays + " - nothing to " + action);
        }

        if (!updates.ReadyToApply)
        {
            if (installer.Step is not (UpdateStep.Downloading or UpdateStep.Extracting))
            {
                updates.Download();
            }

            await Until(() => installer.Step is UpdateStep.Ready or UpdateStep.Failed or UpdateStep.Idle, TimeSpan.FromMinutes(20), cancelling)
                .ConfigureAwait(false);
            if (!updates.ReadyToApply)
            {
                return AgentResult.Fail($"{checkedSays}\nthe download did not finish: {installer.Step} - {installer.Status}");
            }
        }

        if (action == "download")
        {
            return AgentResult.Say($"{checkedSays}\ndownloaded and unpacked - install restarts the tool");
        }

        // THE ANSWER LEAVES FIRST: the restart a few seconds later, off this thread - Apply ends the process and does not return.
        _ = Task.Run(async () =>
        {
            await Task.Delay(RestartAfter).ConfigureAwait(false);
            PoEformance.Overlay.OverlayLog.Write($"agent   installing {check.Remote.Describe()} - the tool restarts");
            updates.Apply();
        }, CancellationToken.None);
        return AgentResult.Say($"{checkedSays}\ninstalling: the tool closes in {RestartAfter.TotalSeconds:F0} s and starts again as the new build;"
            + " the link comes back on a new tunnel address - ask status again in a minute or two");
    }

    private static async Task Until(Func<bool> done, TimeSpan within, CancellationToken cancelling)
    {
        long until = Environment.TickCount64 + (long)within.TotalMilliseconds;
        while (!done() && Environment.TickCount64 < until)
        {
            await Task.Delay(250, cancelling).ConfigureAwait(false);
        }
    }
}
