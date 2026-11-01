using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PoEformance.Features;

/// <summary>
/// The public address the agent link is reached at: a Cloudflare quick tunnel, run by cloudflared beside the tool.
/// </summary>
/// <remarks>
/// WHY A QUICK TUNNEL (decided from the live client, 2026-11-01: "free, if possible"). The session using the tool
/// runs in a cloud container and the tool on the player's own Windows machine; the one has to reach the other
/// through something both can reach. A quick tunnel needs no account, no domain and no open port - cloudflared
/// dials out to Cloudflare and Cloudflare forwards what reaches its random trycloudflare.com name. What it costs,
/// from Cloudflare's own page on them: no uptime promised, 200 requests in flight at most, no server-sent
/// events, and a NEW NAME EVERY START - which is why the name is published (<see cref="AgentBeacon"/>) for the
/// bridge to find, rather than written into anybody's settings.
///
/// CLOUDFLARED IS FETCHED ONCE, PINNED: <see cref="CloudflaredVersion"/> from Cloudflare's own GitHub release,
/// held to its size and its SHA-256 (the release notes print it; the file fetched in the cloud container on
/// 2026-11-01 hashed the same) before it is ever run, and kept in <see cref="DefaultFolder"/>. A copy that does
/// not match is deleted, not run. A newer cloudflared is a new pin in a new build, not something that arrives
/// by itself, hence <c>--no-autoupdate</c>.
///
/// IT DIES WITH THE TOOL. The process is put in a Windows job object that kills what it holds once the tool's
/// handle to it closes - however the tool ends: a crash, an update's restart, the window closed - so no
/// tunnel is left forwarding to a port nobody listens on.
///
/// IT COMES BACK BY ITSELF. A cloudflared that exits is started again after 5 s, doubling to a minute while it
/// keeps failing; each start brings a new name, published again.
/// </remarks>
public sealed partial class AgentTunnel : IDisposable
{
    public const string CloudflaredVersion = "2026.10.7";

    public const string DownloadUrl =
        "https://github.com/cloudflare/cloudflared/releases/download/" + CloudflaredVersion + "/cloudflared-windows-amd64.exe";

    /// <summary>From the release notes of 2026.10.7, and hashed again from the file itself.</summary>
    public const string Sha256 = "2493dbc168923bb3e59e5e7716f71deced92992eea9acd6e9d74f84ac7751343";

    public const long Size = 55_390_512;

    /// <summary>Where cloudflared is kept, beside the tool and outside anything an update copies over.</summary>
    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "agent");

    private readonly int _port;
    private readonly string _folder;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _closing = new();
    private readonly Lock _processLock = new();
    private Process? _process;
    private Task? _loop;
    private string _url = string.Empty;
    private string _state = "not started";
    private bool _ready;

    /// <param name="port">The loopback port the agent link's server listens on.</param>
    /// <param name="log">Where what happens is written, a line at a time.</param>
    public AgentTunnel(int port, Action<string> log, string? folder = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        _port = port;
        _log = log;
        _folder = folder ?? DefaultFolder;
    }

    /// <summary>Raised with the public address once cloudflared has registered a connection for it.</summary>
    public event Action<string>? Up;

    /// <summary>The public address, or empty until one is known.</summary>
    public string Url => Volatile.Read(ref _url);

    /// <summary>Whether a connection is registered for <see cref="Url"/>.</summary>
    public bool Ready => Volatile.Read(ref _ready);

    /// <summary>What the tunnel is doing, in a few words, for the overlay's line.</summary>
    public string State => Volatile.Read(ref _state);

    public void Start() => _loop ??= Task.Run(Loop);

    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.CultureInvariant)]
    private static partial Regex QuickName();

    private async Task Loop()
    {
        CancellationToken closing = _closing.Token;
        int failures = 0;
        while (!closing.IsCancellationRequested)
        {
            string exe;
            try
            {
                exe = await Binary(closing).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (exe.Length > 0)
            {
                long began = Environment.TickCount64;
                await RunOnce(exe, closing).ConfigureAwait(false);
                if (Environment.TickCount64 - began > 5 * 60_000)
                {
                    failures = 0;
                }
            }

            if (closing.IsCancellationRequested)
            {
                return;
            }

            failures++;
            int wait = Math.Min(60, 5 << Math.Min(failures - 1, 4));
            Set(false, string.Empty, $"{State} - trying again in {wait} s");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(wait), closing).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>The pinned cloudflared, fetched and checked first when it is not here; empty when it cannot be had.</summary>
    private async Task<string> Binary(CancellationToken closing)
    {
        if (!OperatingSystem.IsWindows())
        {
            Set(false, string.Empty, "the tunnel runs on Windows only");
            return string.Empty;
        }

        string exe = Path.Combine(_folder, "cloudflared.exe");
        if (File.Exists(exe))
        {
            if (Matches(exe))
            {
                return exe;
            }

            _log($"agent   {exe} is not cloudflared {CloudflaredVersion} as pinned - fetching it again");
            TryDelete(exe);
        }

        Directory.CreateDirectory(_folder);
        string part = exe + ".part";
        try
        {
            Set(false, string.Empty, $"fetching cloudflared {CloudflaredVersion}");
            // ITS OWN CLIENT, not UpdateCheck's: that one gives a request twenty seconds, and 53 MB takes longer on an ordinary line.
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PoEformance/2.0");
            using HttpResponseMessage answer = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, closing)
                .ConfigureAwait(false);
            if (!answer.IsSuccessStatusCode)
            {
                Set(false, string.Empty, $"could not fetch cloudflared: GitHub answered {(int)answer.StatusCode}");
                return string.Empty;
            }

            await using (Stream body = await answer.Content.ReadAsStreamAsync(closing).ConfigureAwait(false))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long have = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, closing).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), closing).ConfigureAwait(false);
                    have += read;
                    if (have > Size)
                    {
                        break;
                    }

                    Set(false, string.Empty, $"fetching cloudflared {CloudflaredVersion} ({have / 1_048_576} of {Size / 1_048_576} MB)");
                }
            }

            if (!Matches(part))
            {
                TryDelete(part);
                Set(false, string.Empty, "the fetched cloudflared is not the pinned file (size or SHA-256) - not run");
                return string.Empty;
            }

            File.Move(part, exe, overwrite: true);
            _log($"agent   fetched cloudflared {CloudflaredVersion} into {_folder}, SHA-256 as pinned");
            return exe;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException
            or OperationCanceledException)
        {
            TryDelete(part);
            if (closing.IsCancellationRequested)
            {
                throw new OperationCanceledException(closing);
            }

            Set(false, string.Empty, $"could not fetch cloudflared: {exception.Message}");
            return string.Empty;
        }
    }

    /// <summary>Whether a file is the pinned cloudflared, by its size and then its hash.</summary>
    public static bool Matches(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != Size)
            {
                return false;
            }

            using FileStream file = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(file)) == Sha256;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task RunOnce(string exe, CancellationToken closing)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = _folder,
        };
        start.ArgumentList.Add("tunnel");
        start.ArgumentList.Add("--no-autoupdate");
        start.ArgumentList.Add("--url");
        start.ArgumentList.Add($"http://127.0.0.1:{_port.ToString(CultureInfo.InvariantCulture)}");

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Process.Start returned nothing");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Set(false, string.Empty, $"cloudflared would not start: {exception.Message}");
            return;
        }

        using (process)
        {
            if (OperatingSystem.IsWindows())
            {
                DiesWithTheTool(process);
            }

            lock (_processLock)
            {
                _process = process;
            }

            Set(false, string.Empty, "cloudflared started, waiting for its address");
            process.ErrorDataReceived += (_, line) => Heard(line.Data);
            process.OutputDataReceived += (_, line) => Heard(line.Data);
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            try
            {
                await process.WaitForExitAsync(closing).ConfigureAwait(false);
                Set(false, string.Empty, $"cloudflared ended (exit code {process.ExitCode})");
                _log($"agent   {State}");
            }
            catch (OperationCanceledException)
            {
                Kill(process);
            }
            finally
            {
                lock (_processLock)
                {
                    _process = null;
                }
            }
        }
    }

    /// <summary>One line of cloudflared's log: its address, its connections, and anything it calls an error.</summary>
    private void Heard(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        if (QuickName().Match(line) is { Success: true } named && named.Value != Url)
        {
            Set(false, named.Value, "address given, waiting for a connection");
            _log($"agent   tunnel address {named.Value}");
            return;
        }

        if (line.Contains("Registered tunnel connection", StringComparison.Ordinal))
        {
            bool first = !Ready;
            Set(true, Url, Url.Length > 0 ? $"up at {Url}" : "connected, address not yet heard");
            if (first && Url.Length > 0)
            {
                _log($"agent   tunnel up: {line.Trim()}");
                Up?.Invoke(Url);
            }

            return;
        }

        if (line.Contains(" ERR ", StringComparison.Ordinal) || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            _log($"agent   cloudflared: {line.Trim()}");
        }
    }

    private void Set(bool ready, string url, string state)
    {
        Volatile.Write(ref _url, url);
        Volatile.Write(ref _ready, ready);
        Volatile.Write(ref _state, state);
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Gone already, or not ours to end - either way there is nothing left to stop.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be deleted is checked again next time, and refused again if it is wrong.
        }
    }

    public void Dispose()
    {
        _closing.Cancel();
        lock (_processLock)
        {
            if (_process is { } running)
            {
                Kill(running);
            }
        }

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // The loop ends in a cancellation, which is what was asked for.
        }

        _closing.Dispose();
    }

    // ── The job object that ends cloudflared with the tool ──────────────────────────────────────────

    /// <summary>One job for every cloudflared this run starts, its handle held until the process ends.</summary>
    private static nint _job;

    [SupportedOSPlatform("windows")]
    private static void DiesWithTheTool(Process process)
    {
        if (_job == 0)
        {
            nint job = CreateJobObjectW(0, null);
            if (job == 0)
            {
                return;
            }

            var limits = new ExtendedLimit { Basic = new BasicLimit { LimitFlags = KillOnJobClose } };
            if (!SetInformationJobObject(job, ExtendedLimitInformation, ref limits, Marshal.SizeOf<ExtendedLimit>()))
            {
                return;
            }

            _job = job;
        }

        // Refused only when the tool itself runs in a job that forbids nesting - then the tunnel merely
        // outlives a crash, and the next start's new one replaces it.
        _ = AssignProcessToJobObject(_job, process.Handle);
    }

    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial nint CreateJobObjectW(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimit info, int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);
}
