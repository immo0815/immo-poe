using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PoEformance.Features;

/// <summary>What the check decided, once it has an answer.</summary>
public enum UpdateVerdict
{
    /// <summary>Nothing has been asked yet.</summary>
    NotChecked,

    /// <summary>The release is the build that is running.</summary>
    UpToDate,

    /// <summary>There is a newer build than this one.</summary>
    Available,

    /// <summary>An answer came back, but it cannot be compared against this build.</summary>
    CannotCompare,

    /// <summary>Nobody answered, or the answer made no sense.</summary>
    Failed,
}

/// <summary>One published release, as the updater cares about it.</summary>
/// <param name="Notes">The release body - the changelog, as markdown.</param>
/// <param name="DownloadUrl">
/// The zip's API address (the asset's <c>url</c>, not its <c>browser_download_url</c>). Empty
/// when the release carries no build. See <see cref="GitHubReleases.IsAsset"/> for why.
/// </param>
/// <param name="StampUrl">The API address of that build's <see cref="BuildStamp"/>.</param>
/// <param name="AssetUtc">
/// When the zip was last uploaded. The one timestamp that moves on a rolling release: the tag
/// stays <c>latest-dev</c> forever and <c>published_at</c> is the day the tag was created, so
/// neither of those can order two builds. This is what "the newest release" is sorted by.
/// </param>
public sealed record ReleaseInfo(
    string Tag,
    string Name,
    string Notes,
    string DownloadUrl,
    long DownloadSize,
    string StampUrl,
    DateTimeOffset AssetUtc);

/// <summary>Reads the GitHub releases API into <see cref="ReleaseInfo"/>.</summary>
/// <remarks>
/// <see cref="JsonDocument"/> rather than a source-generated model, deliberately. The release
/// payload is somebody else's schema with about forty fields per release, of which six matter;
/// a generated model would be forty properties to keep in step with an API this project does
/// not own, and every one of them a place for an unexpected null to throw. Reading the six by
/// name cannot break when the other thirty-four change.
/// </remarks>
public static class GitHubReleases
{
    /// <summary>
    /// Parses the response of <c>GET /repos/{owner}/{repo}/releases</c>.
    /// </summary>
    /// <param name="assetName">The zip a usable release has to carry.</param>
    /// <param name="stampName">The build stamp uploaded beside it.</param>
    /// <returns>
    /// The releases that carry a build, newest upload first. Drafts are left out - they are
    /// not published, and a token that may write the repository is shown them in the listing.
    /// </returns>
    public static IReadOnlyList<ReleaseInfo> Parse(string json, string assetName, string stampName)
    {
        ArgumentNullException.ThrowIfNull(json);

        var found = new List<ReleaseInfo>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return found;
            }

            foreach (JsonElement release in document.RootElement.EnumerateArray())
            {
                if (Text(release, "draft") is "true")
                {
                    continue;
                }

                string tag = Text(release, "tag_name");
                string name = Text(release, "name");
                string notes = Text(release, "body");

                string download = string.Empty;
                string stamp = string.Empty;
                long size = 0;
                DateTimeOffset uploaded = default;

                if (release.TryGetProperty("assets", out JsonElement assets)
                    && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement asset in assets.EnumerateArray())
                    {
                        string assetLabel = Text(asset, "name");
                        if (assetLabel.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                        {
                            download = Text(asset, "url");
                            size = asset.TryGetProperty("size", out JsonElement bytes)
                                && bytes.ValueKind == JsonValueKind.Number ? bytes.GetInt64() : 0;
                            uploaded = When(asset, "updated_at");
                        }
                        else if (assetLabel.Equals(stampName, StringComparison.OrdinalIgnoreCase))
                        {
                            stamp = Text(asset, "url");
                        }
                    }
                }

                if (download.Length == 0)
                {
                    // A release with no build in it is a release nobody can install.
                    continue;
                }

                found.Add(new ReleaseInfo(
                    tag,
                    name.Length > 0 ? name : tag,
                    notes,
                    download,
                    size,
                    stamp,
                    uploaded));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        found.Sort((left, right) => right.AssetUtc.CompareTo(left.AssetUtc));
        return found;
    }

    /// <summary>Whether an address is a release asset's API address, which answers with the file's bytes.</summary>
    /// <remarks>
    /// THE API ADDRESS AND NOT THE BROWSER ONE, because the repository is private. GitHub's
    /// documentation ("Get a release asset") gives two ways to the bytes: in a browser, the
    /// <c>browser_download_url</c>; otherwise the asset's own API address asked for
    /// <c>application/octet-stream</c>, which a fine-grained token with Contents: read may
    /// fetch. The answer is the file or a 302 to a signed address on another host - and
    /// SocketsHttpHandler clears the Authorization header on every redirect (RedirectHandler),
    /// so the token goes to GitHub and nowhere else.
    ///
    /// Read from the address rather than passed alongside it, so the one fetcher the check uses
    /// for the listing and the stamp alike sends the right Accept for each.
    /// </remarks>
    public static bool IsAsset(string address)
        => address.Contains("/releases/assets/", StringComparison.Ordinal);

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty,
            }
            : string.Empty;

    private static DateTimeOffset When(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTimeOffset when)
                ? when
                : default;
}

/// <summary>
/// Asks GitHub whether there is a newer build than the one running, and holds the answer.
/// </summary>
/// <remarks>
/// TWO REQUESTS, and the second is what makes the answer exact. The releases API says what is
/// published and carries the changelog, but on a rolling tag it cannot say WHICH BUILD the zip
/// contains - the tag is <c>latest-dev</c> for every build ever made. So the release also
/// carries a <c>version.json</c>, written by the same publish step that wrote the one beside
/// this executable, and comparing the two is comparing two stamps rather than reading meaning
/// into a timestamp.
///
/// A release WITHOUT that stamp is answered with <see cref="UpdateVerdict.CannotCompare"/>
/// rather than a guess. The obvious guess - treat the asset's upload time as the build time -
/// is wrong in the one direction that matters: the upload finishes after the build, so the
/// running build would compare as older than itself and every launch would offer an update to
/// the copy already installed.
///
/// The check is OFF THE HOT PATH entirely: one background task, at most one in flight, and the
/// callers ask <see cref="RefreshIfStale"/> from wherever they already tick. Nothing here
/// blocks a reader, a frame or the config window's message thread.
/// </remarks>
public sealed class UpdateCheck : IDisposable
{
    /// <summary>The repository the releases come from.</summary>
    /// <remarks>
    /// Moved from imm0r/PoEformance2 on 2026-10-11, when that account was suspended. The new
    /// repository is PRIVATE, which is why every request here carries the saved token when
    /// there is one - see the constructor's <c>token</c> and <see cref="Refusal"/>.
    /// </remarks>
    public const string Owner = "immo0815";

    /// <summary>The repository the releases come from.</summary>
    public const string Repository = "immo-poe";

    /// <summary>The release asset holding a Windows build.</summary>
    public const string AssetName = "PoEformance-win-x64.zip";

    /// <summary>The release asset holding that build's <see cref="BuildStamp"/>.</summary>
    public const string StampName = "version.json";

    /// <summary>How long one request is given before it counts as lost.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How old an answer may be before it is worth asking again.
    /// </summary>
    /// <remarks>
    /// Six hours. The API allows sixty requests an hour per address without a token and five
    /// thousand with one, and this spends two per check, so the interval is nowhere near the
    /// limit either way - it is set
    /// by how often a build actually appears, which is a handful of times on a busy day. A
    /// tool that asks every five minutes learns nothing extra and is one shared address away
    /// from being rate-limited when somebody presses the button on purpose.
    /// </remarks>
    public static readonly TimeSpan GoesStale = TimeSpan.FromHours(6);

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly Func<string, CancellationToken, Task<string?>> _ask;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string> _token;

    private HttpClient? _http;
    private ReleaseInfo? _newest;
    private BuildStamp _remote = BuildStamp.Unknown;
    private DateTimeOffset _asked;
    private bool _busy;

    /// <summary>The token this check sends, read once when it starts. Empty for none.</summary>
    private string _bearer = string.Empty;

    /// <summary>The status of the last request GitHub refused; zero when none was or none came back.</summary>
    private int _refused;

    /// <param name="local">What is running - see <see cref="BuildStamp"/>.</param>
    /// <param name="ask">
    /// Fetches one address as text, or null when it could not. Injected so every test here
    /// runs without a network, and so the two requests share one transport.
    /// </param>
    /// <param name="token">
    /// The saved GitHub token, or empty. Asked at the start of every check rather than once,
    /// so a token entered after launch is used by the very next check without a restart.
    /// </param>
    public UpdateCheck(
        BuildStamp? local = null,
        Func<string, CancellationToken, Task<string?>>? ask = null,
        Func<DateTimeOffset>? clock = null,
        Func<string>? token = null)
    {
        Local = local ?? BuildStamp.Load();
        _ask = ask ?? Download;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _token = token ?? (static () => string.Empty);
    }

    /// <summary>The build that is running.</summary>
    public BuildStamp Local { get; }

    /// <summary>Whether the check is allowed to talk to GitHub at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>What happened last, in words. Shown wherever the verdict is.</summary>
    public string Status { get; private set; } = "not asked yet";

    /// <summary>The decision, as far as it has been made.</summary>
    public UpdateVerdict Verdict { get; private set; } = UpdateVerdict.NotChecked;

    /// <summary>Whether a request is on the way.</summary>
    public bool Busy
    {
        get { lock (_gate) { return _busy; } }
    }

    /// <summary>The newest release carrying a build, once one has been seen.</summary>
    public ReleaseInfo? Newest
    {
        get { lock (_gate) { return _newest; } }
    }

    /// <summary>What that release's build is - only meaningful when it carried a stamp.</summary>
    public BuildStamp Remote
    {
        get { lock (_gate) { return _remote; } }
    }

    /// <summary>When the last answer came back. Default when none has.</summary>
    public DateTimeOffset LastAsked
    {
        get { lock (_gate) { return _asked; } }
    }

    /// <summary>Called on the checking thread whenever an answer settles.</summary>
    /// <remarks>
    /// For the surface that cannot poll: the console. The two windows read this object once a
    /// second or once a frame, but a line printed at startup is all somebody running without
    /// the overlay or the config window ever sees - and the check finishes seconds after that
    /// line has already scrolled past.
    /// </remarks>
    public Action<UpdateVerdict>? Answered { get; set; }

    /// <summary>A build the user said no to, so the same one is not offered again.</summary>
    /// <remarks>
    /// A COMMIT rather than a flag. "Do not tell me again" means this build, not every build
    /// from now on - the next one has to be able to reach the same person.
    /// </remarks>
    public string Skipped { get; set; } = string.Empty;

    /// <summary>Whether there is an update the user has not already waved away.</summary>
    public bool Offering
        => Verdict == UpdateVerdict.Available
           && !string.Equals(Remote.Commit, Skipped, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the last answer has aged out, or none has ever come back.</summary>
    public bool Old
    {
        get
        {
            lock (_gate)
            {
                return _asked == default || _clock() - _asked > GoesStale;
            }
        }
    }

    /// <summary>The comparison itself, kept pure so it can be tested without a network.</summary>
    /// <param name="local">What is running.</param>
    /// <param name="remote">What the release carries.</param>
    public static UpdateVerdict Compare(BuildStamp local, BuildStamp remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        if (!remote.Known || !local.Known)
        {
            return UpdateVerdict.CannotCompare;
        }

        if (string.Equals(local.Commit, remote.Commit, StringComparison.OrdinalIgnoreCase))
        {
            return UpdateVerdict.UpToDate;
        }

        // A DIFFERENT commit is not automatically a newer one. Somebody running a build from a
        // branch, or from a run that was published and then superseded by a revert, is AHEAD of
        // the release rather than behind it, and offering to "update" them would walk their
        // build backwards without saying so.
        return remote.BuiltUtc > local.BuiltUtc ? UpdateVerdict.Available : UpdateVerdict.UpToDate;
    }

    /// <summary>Asks again if the last answer has aged out. Cheap to call from a tick.</summary>
    public void RefreshIfStale()
    {
        if (Enabled && Old && !Busy)
        {
            Refresh();
        }
    }

    /// <summary>Asks now, in the background, unless a request is already on the way.</summary>
    public void Refresh()
    {
        if (Busy || _closing.IsCancellationRequested)
        {
            return;
        }

        _ = Task.Run(() => CheckAsync(_closing.Token));
    }

    /// <summary>
    /// One check, awaited. What <see cref="Refresh"/> runs on a background task.
    /// </summary>
    /// <remarks>
    /// Public because a check that can only be started and never awaited is a check that can
    /// only be tested by sleeping and hoping. The busy flag is claimed inside rather than by
    /// the caller, so two overlapping calls collapse to one request wherever they came from.
    /// </remarks>
    public async Task CheckAsync(CancellationToken cancelling = default)
    {
        lock (_gate)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
        }

        try
        {
            Status = "asking GitHub";
            _bearer = _token();
            _refused = 0;

            string address =
                $"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=10";

            string? listing = await _ask(address, cancelling).ConfigureAwait(false);
            if (listing is null)
            {
                Settle(UpdateVerdict.Failed, Refusal(_refused, _bearer.Length > 0));
                return;
            }

            IReadOnlyList<ReleaseInfo> releases = GitHubReleases.Parse(listing, AssetName, StampName);
            ReleaseInfo? newest = releases.Count > 0 ? releases[0] : null;
            if (newest is null)
            {
                Settle(UpdateVerdict.Failed, $"no published release carries {AssetName}");
                return;
            }

            lock (_gate)
            {
                _newest = newest;
            }

            if (newest.StampUrl.Length == 0)
            {
                Settle(
                    UpdateVerdict.CannotCompare,
                    $"the release \"{newest.Name}\" carries no {StampName}, so which build it holds "
                    + "cannot be established - it predates the update check");
                return;
            }

            string? stampText = await _ask(newest.StampUrl, cancelling).ConfigureAwait(false);
            BuildStamp remote = stampText is null ? BuildStamp.Unknown : BuildStamp.Parse(stampText);
            lock (_gate)
            {
                _remote = remote;
            }

            UpdateVerdict verdict = Compare(Local, remote);
            Settle(verdict, verdict switch
            {
                UpdateVerdict.Available =>
                    $"{remote.ShortCommit} is available, built "
                    + $"{remote.BuiltUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC",
                UpdateVerdict.UpToDate => "this is the newest build",
                _ when !Local.Known =>
                    "this is a local build with no version.json beside it, so it cannot be "
                    + "compared against the release",
                _ => $"the release's {StampName} could not be read",
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Settle(UpdateVerdict.Failed, $"the check did not finish: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            Settle(UpdateVerdict.Failed, $"the answer could not be read: {exception.Message}");
        }
        finally
        {
            _bearer = string.Empty;
            lock (_gate)
            {
                _busy = false;
            }
        }
    }

    /// <summary>Why the release listing did not come back, said as what to do about it.</summary>
    /// <param name="status">What GitHub answered; zero when nothing did.</param>
    /// <param name="token">Whether the request carried a token.</param>
    /// <remarks>
    /// 404 IS WHAT A PRIVATE REPOSITORY LOOKS LIKE to anybody not allowed in - GitHub answers
    /// "not found" rather than "forbidden", so as not to say the repository exists. Without a
    /// token that is the normal state of a fresh install and the answer is to enter one; with
    /// one, it is a token made for the wrong repository or without Contents: read.
    /// </remarks>
    internal static string Refusal(int status, bool token) => (status, token) switch
    {
        (0, _) => "GitHub did not answer - no releases could be read",
        (401, _) => "GitHub refused the saved token - forget it and enter a new one",
        (404, false) => $"{Owner}/{Repository} is private - the check needs a GitHub token that may read it "
            + "(fine-grained, Contents: read)",
        (403 or 404, true) => $"the saved token may not read {Owner}/{Repository} - it needs Contents: read "
            + "on that repository",
        _ => $"GitHub answered {status} - no releases could be read",
    };

    /// <summary>One GET to GitHub, with the token when there is one.</summary>
    /// <remarks>
    /// The Accept set here REPLACES the client's default rather than joining it - HttpHeaders
    /// only copies a default header onto a request that does not set that header itself - so
    /// an asset is asked for as bytes and everything else as the API's JSON.
    /// </remarks>
    internal static HttpRequestMessage Request(string address, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        if (token.Length > 0)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (GitHubReleases.IsAsset(address))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        }

        return request;
    }

    private void Settle(UpdateVerdict verdict, string status)
    {
        Verdict = verdict;
        Status = status;
        lock (_gate)
        {
            _asked = _clock();
        }

        // NOT inside the lock: a listener prints, and holding this while somebody else's code
        // runs is how a background check ends up blocking the property every frame reads.
        Answered?.Invoke(verdict);
    }

    /// <summary>The one place that talks to GitHub.</summary>
    /// <remarks>
    /// THE USER AGENT IS NOT OPTIONAL. The GitHub API answers a request without one with 403
    /// and no body, which arrives here as "the check did not finish" and looks exactly like
    /// being offline - so it is set once, here, rather than left to a default that does not
    /// exist.
    /// </remarks>
    private async Task<string?> Download(string address, CancellationToken cancelling)
    {
        _http ??= Client();

        using HttpRequestMessage request = Request(address, _bearer);
        using HttpResponseMessage answer = await _http
            .SendAsync(request, cancelling)
            .ConfigureAwait(false);

        if (!answer.IsSuccessStatusCode)
        {
            _refused = (int)answer.StatusCode;
            return null;
        }

        return await answer.Content.ReadAsStringAsync(cancelling).ConfigureAwait(false);
    }

    /// <summary>An HttpClient GitHub will talk to.</summary>
    internal static HttpClient Client()
    {
        var http = new HttpClient { Timeout = Patience };
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PoEformance", "2.0"));
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public void Dispose()
    {
        _closing.Cancel();
        _closing.Dispose();
        _http?.Dispose();
    }
}
