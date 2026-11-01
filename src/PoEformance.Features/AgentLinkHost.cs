using System.Globalization;

namespace PoEformance.Features;

/// <summary>
/// The agent link as one switch: its server on the loopback port, the tunnel to it, and the address published - on
/// together, off together.
/// </summary>
/// <remarks>
/// OFF UNTIL SWITCHED ON (the About page, "agent link"), and remembered: switching it on is the moment the player also
/// copies the key into the cloud environment, which nobody does by accident. On, the tool fetches cloudflared once
/// (<see cref="AgentTunnel"/>), opens the tunnel and publishes its address (<see cref="AgentBeacon"/>) with the saved
/// GitHub token, again on every new address the tunnel comes back with.
/// </remarks>
public sealed class AgentLinkHost : IDisposable
{
    /// <summary>The loopback port tried first; any free one where it is taken.</summary>
    public const int UsualPort = 47815;

    private readonly AgentLink _link;
    private readonly Func<string> _githubToken;
    private readonly Action<string> _log;
    private readonly string _version;
    private readonly string _commit;
    private readonly Lock _gate = new();
    private AgentHttpServer? _server;
    private AgentTunnel? _tunnel;
    private CancellationTokenSource? _publishing;
    private string _published = string.Empty;
    private string _failed = string.Empty;

    /// <param name="link">The protocol and the tools bound to it.</param>
    /// <param name="githubToken">The saved GitHub token, read when an address is published.</param>
    /// <param name="log">Where what happens is written.</param>
    /// <param name="version">The tool's version, published with the address.</param>
    /// <param name="commit">The build's commit, published with the address.</param>
    public AgentLinkHost(AgentLink link, Func<string> githubToken, Action<string> log, string version, string commit)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(githubToken);
        ArgumentNullException.ThrowIfNull(log);
        _link = link;
        _githubToken = githubToken;
        _log = log;
        _version = version;
        _commit = commit;
        link.Log = log;
    }

    /// <summary>The protocol, for the tools to be bound to.</summary>
    public AgentLink Link => _link;

    /// <summary>Whether the link runs now.</summary>
    public bool On
    {
        get
        {
            lock (_gate)
            {
                return _server is not null;
            }
        }
    }

    /// <summary>The public address, or empty.</summary>
    public string Url => _tunnel?.Url ?? string.Empty;

    /// <summary>The loopback port the server listens on, or nought while off.</summary>
    public int Port => _server?.Port ?? 0;

    /// <summary>Starts the server and the tunnel; what went wrong, or empty.</summary>
    public string Start()
    {
        lock (_gate)
        {
            if (_server is not null)
            {
                return string.Empty;
            }

            try
            {
                _server = TryServe(UsualPort) ?? new AgentHttpServer(0, _link.Handle);
            }
            catch (System.Net.Sockets.SocketException exception)
            {
                _failed = "the link could not listen on the loopback address: " + exception.Message;
                _log("agent   " + _failed);
                return _failed;
            }

            _failed = string.Empty;
            _server.Start();
            _tunnel = new AgentTunnel(_server.Port, _log);
            _tunnel.Up += Publish;
            _tunnel.Start();
            _log($"agent   link on: 127.0.0.1:{_server.Port.ToString(CultureInfo.InvariantCulture)}, the tunnel starting");
            return string.Empty;
        }
    }

    private AgentHttpServer? TryServe(int port)
    {
        try
        {
            return new AgentHttpServer(port, _link.Handle);
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    /// <summary>Stops the tunnel and the server.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _publishing?.Cancel();
            _publishing = null;
            _tunnel?.Dispose();
            _tunnel = null;
            _server?.Dispose();
            _server = null;
            _published = string.Empty;
            _log("agent   link off");
        }
    }

    /// <summary>Publishes a new address, trying again a minute apart while GitHub does not take it.</summary>
    private void Publish(string url)
    {
        CancellationTokenSource cancel;
        lock (_gate)
        {
            _publishing?.Cancel();
            cancel = _publishing = new CancellationTokenSource();
        }

        string fingerprint = AgentKeyStore.Fingerprint(_link.KeyNow());
        _ = Task.Run(async () =>
        {
            for (int attempt = 1; attempt <= 30 && !cancel.IsCancellationRequested; attempt++)
            {
                UploadOutcome outcome = await AgentBeacon.Publish(_githubToken(), url, fingerprint, _version, _commit, cancel.Token)
                    .ConfigureAwait(false);
                if (outcome.Ok)
                {
                    Volatile.Write(ref _published, url);
                    _log($"agent   published {url} on the {AgentBeacon.Branch} branch");
                    return;
                }

                Volatile.Write(ref _failed, "the address was not published: " + outcome.Said);
                _log("agent   " + _failed);
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), cancel.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    /// <summary>What the link is doing, a line each: the server, the tunnel, the publishing, the calls.</summary>
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>();
        AgentHttpServer? server = _server;
        AgentTunnel? tunnel = _tunnel;
        if (server is null)
        {
            lines.Add(_failed.Length > 0 ? "off: " + _failed : "off");
            return lines;
        }

        lines.Add($"listening on 127.0.0.1:{server.Port.ToString(CultureInfo.InvariantCulture)}");
        lines.Add("tunnel: " + (tunnel?.State ?? "none"));
        string published = Volatile.Read(ref _published);
        lines.Add(published.Length > 0 && published == tunnel?.Url
            ? $"published on the {AgentBeacon.Branch} branch"
            : Volatile.Read(ref _failed) is { Length: > 0 } failed ? failed : "not published yet");
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{_link.Calls} call(s), {_link.Refused} refused for the key{(_link.LastSeen == default ? string.Empty : ", the last " + _link.LastSeen.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))}"));
        return lines;
    }

    public void Dispose() => Stop();
}
