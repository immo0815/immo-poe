using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PoEformance.Features;

/// <summary>One piece of a tool's answer: text, or a picture the agent sees.</summary>
public readonly record struct AgentContent(string Text, byte[]? Image, string MimeType)
{
    public static AgentContent Of(string text) => new(text, null, string.Empty);

    public static AgentContent Picture(byte[] png) => new(string.Empty, png, "image/png");
}

/// <summary>A file the agent may fetch, named by an id that means nothing outside this run of the tool.</summary>
public sealed record AgentFile(string Id, string Name, string Path, long Size);

/// <summary>What one tool call came to.</summary>
public sealed class AgentResult
{
    public List<AgentContent> Content { get; } = [];

    /// <summary>Files offered for download with the answer (a capture's pieces, a picture at full size).</summary>
    public List<AgentFile> Files { get; } = [];

    public bool IsError { get; init; }

    public static AgentResult Say(string text)
    {
        var result = new AgentResult();
        result.Content.Add(AgentContent.Of(text));
        return result;
    }

    public static AgentResult Fail(string text)
    {
        var result = new AgentResult { IsError = true };
        result.Content.Add(AgentContent.Of(text));
        return result;
    }
}

/// <summary>A tool as the agent sees it listed: a name, what it does, and its arguments as a JSON schema.</summary>
public sealed record AgentToolInfo(string Name, string Description, string Schema);

/// <summary>Runs one tool call. Its arguments are the call's own JSON object (an empty one when none were sent).</summary>
public delegate Task<AgentResult> AgentHandler(JsonElement arguments, CancellationToken cancelling);

/// <summary>
/// The tool's side of the agent link: the Model Context Protocol over the tunnel, answered with JSON.
/// </summary>
/// <remarks>
/// WHAT IT IS FOR (asked for from the live client, 2026-11-01): a Claude session in the cloud using this tool
/// on the player's own machine - taking the captures it needs, updating the tool, looking at what the model pane
/// draws - without the player pressing a key for it. The session reaches it through a Cloudflare quick tunnel
/// (<see cref="AgentTunnel"/>) and a small bridge committed in the repository (tools/agent/poef.py), which
/// speaks MCP to Claude Code over stdio and forwards each message here.
///
/// THE STREAMABLE HTTP TRANSPORT, ANSWERED WITH JSON ONLY. A quick tunnel carries no server-sent events
/// (Cloudflare's own page on them says so), and the transport lets a server answer every POST with one JSON
/// body, which is all a tool call needs. No session id is handed out: every request stands alone, so a tool
/// restarted by an update answers the next request as though nothing happened.
///
/// A CALL THAT TAKES LONG GOES ON IN THE BACKGROUND. Cloudflare closes a request whose answer has not begun
/// within a hundred seconds, and a capture takes minutes. So every call runs as a job: one that ends within
/// <see cref="ReplyWithin"/> is answered at once, one that does not is answered with its job's id in
/// <c>structuredContent.pending</c>, and the bridge asks after it (<c>poeformance/wait</c>) until it ends.
///
/// EVERY REQUEST CARRIES THE KEY (<see cref="AgentKeyStore"/>), compared in constant time. The tunnel's
/// address is public; the key is what makes it the player's.
/// </remarks>
public sealed class AgentLink
{
    /// <summary>The protocol revision answered when the client asks for one this does not know.</summary>
    public const string ProtocolVersion = "2025-06-18";

    /// <summary>How long a call is waited for before it is answered as still running.</summary>
    /// <remarks>Forty seconds: well inside the hundred Cloudflare allows an answer to start in.</remarks>
    public TimeSpan ReplyWithin { get; init; } = TimeSpan.FromSeconds(40);

    /// <summary>How long a finished job's answer is kept for the bridge to collect.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(30);

    /// <summary>How many files are offered at once; the oldest offer goes first.</summary>
    public const int MostFiles = 1024;

    private static readonly string[] KnownVersions = ["2024-11-05", "2025-03-26", ProtocolVersion];

    private readonly Func<string> _key;
    private readonly Dictionary<string, AgentHandler> _handlers = new(StringComparer.Ordinal);
    private readonly Lock _handlersLock = new();
    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AgentFile> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _fileOrder = new();
    private int _nextJob;
    private int _refused;

    /// <param name="key">The key every request must carry; read per request, so a renewed key holds at once.</param>
    /// <param name="tools">What is listed. A listed tool nothing has bound answers that it is not available.</param>
    public AgentLink(Func<string> key, IReadOnlyList<AgentToolInfo> tools)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tools);
        _key = key;
        Tools = tools;
    }

    /// <summary>The key requests must carry now.</summary>
    public string KeyNow() => _key();

    /// <summary>Everything listed, in order.</summary>
    public IReadOnlyList<AgentToolInfo> Tools { get; }

    /// <summary>Says what happened, a line at a time, for the overlay's log. Never handed the key.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>How many requests were refused for a wrong or missing key since the start.</summary>
    public int Refused => Volatile.Read(ref _refused);

    /// <summary>How many calls have been made since the start.</summary>
    public int Calls => Volatile.Read(ref _nextJob);

    /// <summary>When the last request with the right key came, or default.</summary>
    public DateTimeOffset LastSeen { get; private set; }

    /// <summary>
    /// Runs before every call, given the tool's name - the overlay brings the game's window to the front there (asked for from the live
    /// client, 2026-11-01). What it says, where anything, opens the call's answer; it failing does not stop the call.
    /// </summary>
    public Func<string, Task<string?>>? Before { get; set; }

    /// <summary>Gives a listed tool its handler, replacing any before it.</summary>
    public void Bind(string name, AgentHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!Tools.Any(tool => tool.Name == name))
        {
            throw new ArgumentException($"'{name}' is not in the agent link's catalogue", nameof(name));
        }

        lock (_handlersLock)
        {
            _handlers[name] = handler;
        }
    }

    /// <summary>Offers a file for download and says under which id.</summary>
    public AgentFile Offer(string path, string? name = null)
    {
        var info = new FileInfo(path);
        var file = new AgentFile(
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12)),
            name ?? info.Name,
            info.FullName,
            info.Exists ? info.Length : 0);
        _files[file.Id] = file;
        _fileOrder.Enqueue(file.Id);
        while (_fileOrder.Count > MostFiles && _fileOrder.TryDequeue(out string? oldest))
        {
            _files.TryRemove(oldest, out _);
        }

        return file;
    }

    /// <summary>Answers one request that came through the tunnel.</summary>
    public async Task<AgentHttpResponse> Handle(AgentHttpRequest request, CancellationToken cancelling)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Admitted(request))
        {
            Interlocked.Increment(ref _refused);

            // A slower no to a wrong key: the key is 256 bits, so this is not what keeps it safe, only what
            // keeps a script guessing at it from costing the tool more than a trickle.
            await Task.Delay(250, cancelling).ConfigureAwait(false);
            return AgentHttpResponse.Text(401, "the agent link's key is missing or wrong");
        }

        LastSeen = DateTimeOffset.UtcNow;
        if (request.Path == "/mcp")
        {
            return request.Method switch
            {
                "POST" => await Rpc(request.Body, cancelling).ConfigureAwait(false),
                // No stream of the server's own messages is offered - the transport's way of saying so.
                "GET" => AgentHttpResponse.Text(405, "this server sends no event stream"),
                "DELETE" => AgentHttpResponse.Empty(200),
                _ => AgentHttpResponse.Text(405, "POST a JSON-RPC message"),
            };
        }

        const string FilesPrefix = "/files/";
        if (request.Method == "GET" && request.Path.StartsWith(FilesPrefix, StringComparison.Ordinal))
        {
            return _files.TryGetValue(request.Path[FilesPrefix.Length..], out AgentFile? file)
                ? AgentHttpResponse.File(file.Path, MimeOf(file.Name), file.Name)
                : AgentHttpResponse.Text(404, "no file offered under that id");
        }

        return AgentHttpResponse.Text(404, "the agent link answers /mcp and /files/<id>");
    }

    private bool Admitted(AgentHttpRequest request)
    {
        string key = _key();
        string sent = request.Header("Authorization");
        const string Bearer = "Bearer ";
        if (key.Length == 0 || !sent.StartsWith(Bearer, StringComparison.Ordinal))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(sent[Bearer.Length..].Trim()), Encoding.UTF8.GetBytes(key));
    }

    private static string MimeOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".txt" or ".log" => "text/plain; charset=utf-8",
        ".json" => "application/json",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };

    /// <summary>One POST: a message, or an array of them.</summary>
    private async Task<AgentHttpResponse> Rpc(byte[] body, CancellationToken cancelling)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return AgentHttpResponse.Json(Error(default, -32700, "the body is not JSON"), 400);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                var answers = new List<byte[]>();
                foreach (JsonElement message in root.EnumerateArray())
                {
                    if (await One(message, cancelling).ConfigureAwait(false) is { } answer)
                    {
                        answers.Add(answer);
                    }
                }

                if (answers.Count == 0)
                {
                    return AgentHttpResponse.Empty(202);
                }

                using var joined = new MemoryStream();
                joined.WriteByte((byte)'[');
                for (int i = 0; i < answers.Count; i++)
                {
                    if (i > 0)
                    {
                        joined.WriteByte((byte)',');
                    }

                    joined.Write(answers[i]);
                }

                joined.WriteByte((byte)']');
                return AgentHttpResponse.Json(joined.ToArray());
            }

            byte[]? single = await One(root, cancelling).ConfigureAwait(false);
            return single is null ? AgentHttpResponse.Empty(202) : AgentHttpResponse.Json(single);
        }
    }

    /// <summary>One JSON-RPC message; its answer, or null for a notification or a response.</summary>
    private async Task<byte[]?> One(JsonElement message, CancellationToken cancelling)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return Error(default, -32600, "a message must be a JSON object");
        }

        bool hasId = message.TryGetProperty("id", out JsonElement id);
        if (!message.TryGetProperty("method", out JsonElement method) || method.ValueKind != JsonValueKind.String)
        {
            // A response to something this server never asks, or nonsense: nothing to answer either way.
            return hasId ? Error(id, -32600, "a request needs a method") : null;
        }

        string name = method.GetString()!;
        if (!hasId)
        {
            // notifications/initialized, notifications/cancelled and the like: nothing to answer.
            return null;
        }

        JsonElement parameters = message.TryGetProperty("params", out JsonElement given) ? given : default;
        switch (name)
        {
            case "initialize":
                return Result(id, writer => Initialized(writer, parameters));
            case "ping":
                return Result(id, _ => { });
            case "tools/list":
                return Result(id, Listed);
            case "tools/call":
                return await Called(id, parameters, cancelling).ConfigureAwait(false);
            case "poeformance/wait":
                return await Waited(id, parameters, cancelling).ConfigureAwait(false);
            default:
                return Error(id, -32601, $"'{name}' is not a method this server knows");
        }
    }

    private void Initialized(Utf8JsonWriter writer, JsonElement parameters)
    {
        string asked = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("protocolVersion", out JsonElement version)
            && version.ValueKind == JsonValueKind.String
                ? version.GetString()!
                : ProtocolVersion;
        writer.WriteString("protocolVersion", Array.IndexOf(KnownVersions, asked) >= 0 ? asked : ProtocolVersion);
        writer.WriteStartObject("capabilities");
        writer.WriteStartObject("tools");
        writer.WriteBoolean("listChanged", false);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteStartObject("serverInfo");
        writer.WriteString("name", "poeformance");
        writer.WriteString("version", ToolVersion.Number);
        writer.WriteEndObject();
        writer.WriteString("instructions", Instructions);
    }

    /// <summary>What the agent is told about this server once, at the start.</summary>
    public const string Instructions =
        "PoEformance v2 running on the player's own Windows PC beside Path of Exile 2. The tools drive the tool, "
        + "never the game: nothing here moves the character or sends a key to the game. A capture hides the "
        + "overlay while it runs and may use every core; the update restarts the tool, which comes back with a "
        + "new tunnel address a minute later (the bridge finds it).";

    private void Listed(Utf8JsonWriter writer)
    {
        writer.WriteStartArray("tools");
        foreach (AgentToolInfo tool in Tools)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            writer.WriteString("description", tool.Description);
            writer.WritePropertyName("inputSchema");
            writer.WriteRawValue(tool.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private async Task<byte[]> Called(JsonElement id, JsonElement parameters, CancellationToken cancelling)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out JsonElement nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
        {
            return Error(id, -32602, "tools/call needs a tool name");
        }

        string name = nameElement.GetString()!;
        if (!Tools.Any(tool => tool.Name == name))
        {
            return Error(id, -32602, $"there is no tool '{name}'");
        }

        AgentHandler? handler;
        lock (_handlersLock)
        {
            _handlers.TryGetValue(name, out handler);
        }

        // The arguments outlive the request's document: the job may run on after the answer is sent.
        JsonElement arguments = parameters.TryGetProperty("arguments", out JsonElement given) && given.ValueKind == JsonValueKind.Object
            ? given.Clone()
            : EmptyObject();

        string jobId = Interlocked.Increment(ref _nextJob).ToString(CultureInfo.InvariantCulture);
        Log?.Invoke($"agent   call {jobId}: {name} {Shortened(arguments.GetRawText())}");
        Task<AgentResult> run = handler is null
            ? Task.FromResult(AgentResult.Fail($"'{name}' is not available in this run of the tool"))
            : Run(name, handler, arguments);
        var job = new Job(jobId, name, run, DateTimeOffset.UtcNow);
        _jobs[jobId] = job;
        Sweep();
        return await Answer(id, job, ReplyWithin, cancelling).ConfigureAwait(false);
    }

    private async Task<AgentResult> Run(string name, AgentHandler handler, JsonElement arguments)
    {
        string? before = null;
        if (Before is { } hook)
        {
            try
            {
                before = await Task.Run(() => hook(name)).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                before = $"before the call: {exception.GetType().Name}: {exception.Message}";
            }
        }

        AgentResult result;
        try
        {
            // Off the request's thread and free of its cancellation: a job runs to its end whether or not
            // anybody is still waiting for it, and the bridge collects it later.
            result = await Task.Run(() => handler(arguments, CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            result = AgentResult.Fail($"the tool failed: {exception.GetType().Name}: {exception.Message}");
        }

        if (before is { Length: > 0 })
        {
            // ITS OWN LINE: a client may join an answer's texts with nothing between them (MEASURED, v0.1.412).
            result.Content.Insert(0, AgentContent.Of(before.EndsWith('\n') ? before : before + "\n"));
        }

        return result;
    }

    private async Task<byte[]> Waited(JsonElement id, JsonElement parameters, CancellationToken cancelling)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("job", out JsonElement jobElement)
            || jobElement.ValueKind != JsonValueKind.String)
        {
            return Error(id, -32602, "poeformance/wait needs a job id");
        }

        if (!_jobs.TryGetValue(jobElement.GetString()!, out Job? job))
        {
            return Error(id, -32602, "no such job - the tool may have restarted since it was asked");
        }

        double seconds = parameters.TryGetProperty("seconds", out JsonElement wait) && wait.ValueKind == JsonValueKind.Number
            ? Math.Clamp(wait.GetDouble(), 0, ReplyWithin.TotalSeconds)
            : ReplyWithin.TotalSeconds;
        return await Answer(id, job, TimeSpan.FromSeconds(seconds), cancelling).ConfigureAwait(false);
    }

    private async Task<byte[]> Answer(JsonElement id, Job job, TimeSpan within, CancellationToken cancelling)
    {
        try
        {
            await job.Run.WaitAsync(within, cancelling).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Result(id, writer =>
            {
                writer.WriteStartArray("content");
                writer.WriteStartObject();
                writer.WriteString("type", "text");
                writer.WriteString("text", $"'{job.Name}' is still running (job {job.Id}, {(DateTimeOffset.UtcNow - job.Started).TotalSeconds:F0} s so far)");
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteBoolean("isError", false);
                writer.WriteStartObject("structuredContent");
                writer.WriteString("pending", job.Id);
                writer.WriteEndObject();
            });
        }

        AgentResult result = await job.Run.ConfigureAwait(false);
        job.Finished = DateTimeOffset.UtcNow;
        Log?.Invoke($"agent   done {job.Id}: {job.Name}{(result.IsError ? " (failed)" : string.Empty)}");
        return Result(id, writer => Written(writer, result));
    }

    private static void Written(Utf8JsonWriter writer, AgentResult result)
    {
        writer.WriteStartArray("content");
        foreach (AgentContent part in result.Content)
        {
            writer.WriteStartObject();
            if (part.Image is { } image)
            {
                writer.WriteString("type", "image");
                writer.WriteBase64String("data", image);
                writer.WriteString("mimeType", part.MimeType);
            }
            else
            {
                writer.WriteString("type", "text");
                writer.WriteString("text", part.Text);
            }

            writer.WriteEndObject();
        }

        if (result.Files.Count > 0)
        {
            var listing = new StringBuilder("files offered (fetch with the bridge's download tool):");
            foreach (AgentFile file in result.Files)
            {
                listing.Append(CultureInfo.InvariantCulture, $"\n  {file.Id}  {file.Name}  {file.Size:N0} bytes");
            }

            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", listing.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteBoolean("isError", result.IsError);
        if (result.Files.Count > 0)
        {
            writer.WriteStartObject("structuredContent");
            writer.WriteStartArray("files");
            foreach (AgentFile file in result.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("id", file.Id);
                writer.WriteString("name", file.Name);
                writer.WriteNumber("size", file.Size);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    /// <summary>Drops finished jobs nobody collected within <see cref="KeptFor"/>.</summary>
    private void Sweep()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach ((string key, Job job) in _jobs)
        {
            if (job.Run.IsCompleted && now - (job.Finished == default ? job.Started : job.Finished) > KeptFor)
            {
                _jobs.TryRemove(key, out _);
            }
        }
    }

    private static string Shortened(string text) => text.Length <= 200 ? text : text[..200] + "...";

    private static JsonElement EmptyObject()
    {
        using JsonDocument empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    private static byte[] Result(JsonElement id, Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            WriteId(writer, id);
            writer.WriteStartObject("result");
            write(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static byte[] Error(JsonElement id, int code, string message)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            WriteId(writer, id);
            writer.WriteStartObject("error");
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteId(Utf8JsonWriter writer, JsonElement id)
    {
        writer.WritePropertyName("id");
        if (id.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            id.WriteTo(writer);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private sealed class Job(string id, string name, Task<AgentResult> run, DateTimeOffset started)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public Task<AgentResult> Run { get; } = run;

        public DateTimeOffset Started { get; } = started;

        public DateTimeOffset Finished { get; set; }
    }
}

/// <summary>Reading a tool call's arguments without a model class for each.</summary>
public static class AgentArgs
{
    public static string Text(this JsonElement arguments, string name, string fallback = "")
        => arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : fallback;

    public static double Number(this JsonElement arguments, string name, double fallback)
        => arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : fallback;

    public static bool Flag(this JsonElement arguments, string name, bool fallback)
        => arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : fallback;

    public static bool Has(this JsonElement arguments, string name)
        => arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value)
            && value.ValueKind != JsonValueKind.Null;

    public static List<string> Texts(this JsonElement arguments, string name)
    {
        var found = new List<string>();
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        found.Add(item.GetString()!);
                    }
                }
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                found.AddRange(value.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        return found;
    }
}
