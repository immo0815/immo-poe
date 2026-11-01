using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PoEformance.Features;

/// <summary>One request as the agent link's server read it off the wire.</summary>
public sealed class AgentHttpRequest
{
    /// <summary>GET, POST and so on, as sent.</summary>
    public required string Method { get; init; }

    /// <summary>The path without its query, as sent (still percent-encoded).</summary>
    public required string Path { get; init; }

    /// <summary>The query without its question mark, empty when there is none.</summary>
    public required string Query { get; init; }

    /// <summary>The headers, names compared without regard to case; repeats joined with a comma.</summary>
    public required Dictionary<string, string> Headers { get; init; }

    /// <summary>The body, whole (a length or chunks, decoded).</summary>
    public required byte[] Body { get; init; }

    /// <summary>A header's value, or empty.</summary>
    public string Header(string name) => Headers.TryGetValue(name, out string? value) ? value : string.Empty;
}

/// <summary>What the agent link's server answers: bytes held, or a file streamed from disk.</summary>
public sealed class AgentHttpResponse
{
    public int Status { get; init; } = 200;

    public string ContentType { get; init; } = "application/json";

    /// <summary>The body, or null for none (or for <see cref="FilePath"/>).</summary>
    public byte[]? Body { get; init; }

    /// <summary>A file to stream as the body, read when the answer is written.</summary>
    public string? FilePath { get; init; }

    /// <summary>The name a fetched file is saved under (Content-Disposition), or null.</summary>
    public string? FileName { get; init; }

    public static AgentHttpResponse Json(byte[] body, int status = 200) => new() { Status = status, Body = body };

    public static AgentHttpResponse Text(int status, string text)
        => new() { Status = status, ContentType = "text/plain; charset=utf-8", Body = Encoding.UTF8.GetBytes(text) };

    public static AgentHttpResponse Empty(int status) => new() { Status = status, Body = null };

    public static AgentHttpResponse File(string path, string contentType = "application/octet-stream", string? name = null)
        => new() { ContentType = contentType, FilePath = path, FileName = name };
}

/// <summary>
/// The smallest HTTP/1.1 server the agent link needs, on a socket of its own, bound to the loopback address.
/// </summary>
/// <remarks>
/// WHY NOT HttpListener OR KESTREL. Kestrel is ASP.NET Core - a framework's worth of dependencies for one
/// endpoint, in a single-file Native AOT build. HttpListener is http.sys on Windows and a managed
/// re-implementation everywhere else, so a test on the Linux CI would test the code the tool never runs.
/// What is needed is small and fixed: a request line, headers, a body by length or by chunks, keep-alive,
/// and an answer by length - the same bytes on every platform, the tests exercising exactly what ships.
///
/// THE LOOPBACK ADDRESS ONLY. Nothing on the network reaches this socket; the tunnel's own process
/// (cloudflared, beside the tool) is the one client, and it forwards what the tunnel receives. Every
/// request is still checked for the key by the caller of this class - the tunnel is public.
///
/// LIMITS ON EVERYTHING READ, because what is read comes from the internet through the tunnel: the
/// header block (<see cref="HeaderLimit"/>), the body (<see cref="BodyLimit"/>), how long a connection
/// may sit idle (<see cref="Idle"/>) and how many are served at once (<see cref="MostConnections"/>).
/// </remarks>
public sealed class AgentHttpServer : IDisposable
{
    /// <summary>The most a request line and its headers may come to.</summary>
    public const int HeaderLimit = 32 * 1024;

    /// <summary>The most a body may come to. A tool call's arguments are a few hundred bytes.</summary>
    public const int BodyLimit = 8 * 1024 * 1024;

    /// <summary>How many connections are served at once; the rest wait to be accepted.</summary>
    public const int MostConnections = 64;

    /// <summary>How long a connection may wait for its next request before it is closed.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(90);

    private readonly Socket _listener;
    private readonly Func<AgentHttpRequest, CancellationToken, Task<AgentHttpResponse>> _handle;
    private readonly CancellationTokenSource _closing = new();
    private readonly SemaphoreSlim _slots = new(MostConnections);
    private Task? _accepting;

    /// <param name="port">The loopback port to listen on; nought takes any free one (see <see cref="Port"/>).</param>
    /// <param name="handle">Answers one request. Runs on the thread pool, one call per request.</param>
    public AgentHttpServer(int port, Func<AgentHttpRequest, CancellationToken, Task<AgentHttpResponse>> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        _handle = handle;
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            // Not ReuseAddress: a second tool on the same port must fail loudly, not share it.
            _listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
            _listener.Listen(MostConnections);
        }
        catch
        {
            _listener.Dispose();
            throw;
        }

        Port = ((IPEndPoint)_listener.LocalEndPoint!).Port;
    }

    /// <summary>The port this server listens on.</summary>
    public int Port { get; }

    /// <summary>Starts accepting connections.</summary>
    public void Start() => _accepting ??= Task.Run(AcceptAll);

    public void Dispose()
    {
        _closing.Cancel();
        _listener.Dispose();
        try
        {
            _accepting?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Accepting ends in a cancellation or a disposed socket - both are the point of disposing.
        }

        _closing.Dispose();
        _slots.Dispose();
    }

    private async Task AcceptAll()
    {
        CancellationToken closing = _closing.Token;
        while (!closing.IsCancellationRequested)
        {
            Socket client;
            try
            {
                await _slots.WaitAsync(closing).ConfigureAwait(false);
                client = await _listener.AcceptAsync(closing).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (closing.IsCancellationRequested)
                {
                    return;
                }

                // A refused accept (the far side gone before it was taken) costs that one connection.
                ReleaseSlot();
                continue;
            }

            _ = Task.Run(() => Serve(client, closing), CancellationToken.None);
        }
    }

    private void ReleaseSlot()
    {
        try
        {
            _slots.Release();
        }
        catch (ObjectDisposedException)
        {
            // Released after Dispose: nobody is waiting for the slot any more.
        }
    }

    private async Task Serve(Socket client, CancellationToken closing)
    {
        client.NoDelay = true;
        var stream = new NetworkStream(client, ownsSocket: true);
        var reader = new Reader(stream);
        try
        {
            while (!closing.IsCancellationRequested)
            {
                AgentHttpRequest? request;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(closing))
                {
                    idle.CancelAfter(Idle);
                    request = await ReadRequest(reader, stream, idle.Token).ConfigureAwait(false);
                }

                if (request is null)
                {
                    return;
                }

                AgentHttpResponse answer;
                try
                {
                    answer = await _handle(request, closing).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    answer = AgentHttpResponse.Text(500, "the tool failed to answer: " + exception.GetType().Name);
                }

                bool keepOpen = KeepsOpen(request);
                if (!await WriteResponse(stream, answer, keepOpen, closing).ConfigureAwait(false) || !keepOpen)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException
            or ObjectDisposedException or BadRequest)
        {
            if (exception is BadRequest bad)
            {
                await TryWrite(stream, AgentHttpResponse.Text(bad.Status, bad.Message), closing).ConfigureAwait(false);
            }
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            reader.Dispose();
            ReleaseSlot();
        }
    }

    /// <summary>HTTP/1.1 keeps a connection unless told not to; HTTP/1.0 closes it unless told to keep it.</summary>
    private static bool KeepsOpen(AgentHttpRequest request)
    {
        string connection = request.Header("Connection");
        bool http10 = request.Header(VersionHeader) == "HTTP/1.0";
        return http10
            ? connection.Contains("keep-alive", StringComparison.OrdinalIgnoreCase)
            : !connection.Contains("close", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The request's protocol version, kept among the headers under a name no client can send.</summary>
    private const string VersionHeader = ":version";

    /// <summary>Reads one request, or null when the connection closed cleanly before one began.</summary>
    private static async Task<AgentHttpRequest?> ReadRequest(Reader reader, Stream stream, CancellationToken cancelling)
    {
        int end = await reader.FindHeaderEnd(cancelling).ConfigureAwait(false);
        if (end < 0)
        {
            return null;
        }

        string head = Encoding.Latin1.GetString(reader.Buffer, reader.Start, end - reader.Start);
        reader.Consume(end + 4 - reader.Start);

        string[] lines = head.Split("\r\n");
        string[] first = lines[0].Split(' ');
        if (first.Length != 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            throw new BadRequest(400, "not an HTTP/1.x request line");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [VersionHeader] = first[2] };
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                throw new BadRequest(400, "a header line without a name");
            }

            string name = lines[i][..colon].Trim();
            string value = lines[i][(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out string? already) ? already + ", " + value : value;
        }

        if (headers.TryGetValue("Expect", out string? expect) && expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
        {
            await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), cancelling).ConfigureAwait(false);
        }

        byte[] body;
        if (headers.TryGetValue("Transfer-Encoding", out string? coding) && coding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            body = await reader.ReadChunked(cancelling).ConfigureAwait(false);
        }
        else if (headers.TryGetValue("Content-Length", out string? length) && length.Length > 0)
        {
            if (!long.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out long size) || size > BodyLimit)
            {
                throw new BadRequest(413, "the body is larger than the tool takes");
            }

            body = await reader.ReadExactly((int)size, cancelling).ConfigureAwait(false);
        }
        else
        {
            body = [];
        }

        string target = first[1];
        int question = target.IndexOf('?', StringComparison.Ordinal);
        return new AgentHttpRequest
        {
            Method = first[0],
            Path = question < 0 ? target : target[..question],
            Query = question < 0 ? string.Empty : target[(question + 1)..],
            Headers = headers,
            Body = body,
        };
    }

    private static async Task<bool> WriteResponse(Stream stream, AgentHttpResponse answer, bool keepOpen, CancellationToken cancelling)
    {
        FileStream? file = null;
        try
        {
            long length = answer.Body?.Length ?? 0;
            if (answer.FilePath is { } path)
            {
                try
                {
                    file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                    length = file.Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return await TryWrite(stream, AgentHttpResponse.Text(404, "the file is gone"), cancelling).ConfigureAwait(false);
                }
            }

            var head = new StringBuilder(160)
                .Append("HTTP/1.1 ").Append(answer.Status.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(Reason(answer.Status)).Append("\r\n")
                .Append("Content-Length: ").Append(length.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
                .Append("Cache-Control: no-store\r\n")
                .Append("Connection: ").Append(keepOpen ? "keep-alive" : "close").Append("\r\n");
            if (answer.Body is not null || file is not null)
            {
                head.Append("Content-Type: ").Append(answer.ContentType).Append("\r\n");
            }

            if (file is not null && answer.FileName is { Length: > 0 } name)
            {
                // The name as the tool knows it, its quotes and line breaks left out: it lands in a header.
                string safe = new([.. name.Where(c => c >= ' ' && c != '"' && c < 127)]);
                head.Append("Content-Disposition: attachment; filename=\"").Append(safe).Append("\"\r\n");
            }

            head.Append("\r\n");
            await stream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()), cancelling).ConfigureAwait(false);
            if (file is not null)
            {
                await file.CopyToAsync(stream, 1 << 16, cancelling).ConfigureAwait(false);
            }
            else if (answer.Body is { Length: > 0 } body)
            {
                await stream.WriteAsync(body, cancelling).ConfigureAwait(false);
            }

            await stream.FlushAsync(cancelling).ConfigureAwait(false);
            return true;
        }
        finally
        {
            if (file is not null)
            {
                await file.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> TryWrite(Stream stream, AgentHttpResponse answer, CancellationToken cancelling)
    {
        try
        {
            return await WriteResponse(stream, answer, keepOpen: false, cancelling).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException
            or OperationCanceledException)
        {
            return false;
        }
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        413 => "Payload Too Large",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => "Status",
    };

    /// <summary>A request that cannot be served, answered with its status before the connection closes.</summary>
    private sealed class BadRequest(int status, string message) : Exception(message)
    {
        public int Status { get; } = status;
    }

    /// <summary>A buffer over the connection, so a header block, a body and the next request can share reads.</summary>
    private sealed class Reader(Stream stream) : IDisposable
    {
        public byte[] Buffer { get; private set; } = ArrayPool<byte>.Shared.Rent(8192);

        /// <summary>Where the unread bytes start.</summary>
        public int Start { get; private set; }

        /// <summary>Where the unread bytes end.</summary>
        public int End { get; private set; }

        public void Consume(int count) => Start += count;

        /// <summary>Reads until the blank line ending the headers; its index, or -1 on a clean close before any byte.</summary>
        public async Task<int> FindHeaderEnd(CancellationToken cancelling)
        {
            // How far past Start has been searched already, relative to Start: a fill may move the
            // unread bytes to the front of the buffer, which an absolute index would not survive.
            int searched = 0;
            while (true)
            {
                int found = Buffer.AsSpan(Start + searched, End - Start - searched).IndexOf("\r\n\r\n"u8);
                if (found >= 0)
                {
                    return Start + searched + found;
                }

                searched = Math.Max(0, End - Start - 3);
                if (End - Start >= HeaderLimit)
                {
                    throw new BadRequest(400, "the headers are larger than the tool takes");
                }

                if (await Fill(cancelling).ConfigureAwait(false) == 0)
                {
                    if (End == Start)
                    {
                        return -1;
                    }

                    throw new IOException("the connection closed inside a request");
                }
            }
        }

        public async Task<byte[]> ReadExactly(int count, CancellationToken cancelling)
        {
            var body = new byte[count];
            int have = Math.Min(count, End - Start);
            Buffer.AsSpan(Start, have).CopyTo(body);
            Start += have;
            while (have < count)
            {
                int read = await stream.ReadAsync(body.AsMemory(have), cancelling).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("the connection closed inside a body");
                }

                have += read;
            }

            return body;
        }

        public async Task<byte[]> ReadChunked(CancellationToken cancelling)
        {
            using var body = new MemoryStream();
            while (true)
            {
                string line = await ReadLine(cancelling).ConfigureAwait(false);
                int semicolon = line.IndexOf(';', StringComparison.Ordinal);
                string size = (semicolon < 0 ? line : line[..semicolon]).Trim();
                if (!int.TryParse(size, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int chunk) || chunk < 0)
                {
                    throw new BadRequest(400, "a chunk without a size");
                }

                if (chunk == 0)
                {
                    // Trailers, if any, up to the blank line.
                    while ((await ReadLine(cancelling).ConfigureAwait(false)).Length > 0)
                    {
                    }

                    return body.ToArray();
                }

                if (body.Length + chunk > BodyLimit)
                {
                    throw new BadRequest(413, "the body is larger than the tool takes");
                }

                byte[] part = await ReadExactly(chunk, cancelling).ConfigureAwait(false);
                body.Write(part);
                if ((await ReadLine(cancelling).ConfigureAwait(false)).Length != 0)
                {
                    throw new BadRequest(400, "a chunk not ended by a line break");
                }
            }
        }

        private async Task<string> ReadLine(CancellationToken cancelling)
        {
            while (true)
            {
                int found = Buffer.AsSpan(Start, End - Start).IndexOf("\r\n"u8);
                if (found >= 0)
                {
                    string line = Encoding.Latin1.GetString(Buffer, Start, found);
                    Start += found + 2;
                    return line;
                }

                if (End - Start >= HeaderLimit)
                {
                    throw new BadRequest(400, "a line longer than the tool takes");
                }

                if (await Fill(cancelling).ConfigureAwait(false) == 0)
                {
                    throw new IOException("the connection closed inside a body");
                }
            }
        }

        /// <summary>Reads more into the buffer, moving or growing it first; how many bytes came.</summary>
        private async Task<int> Fill(CancellationToken cancelling)
        {
            if (Start > 0 && Start == End)
            {
                Start = End = 0;
            }

            if (End == Buffer.Length)
            {
                int unread = End - Start;
                byte[] target = unread * 2 > Buffer.Length ? ArrayPool<byte>.Shared.Rent(Buffer.Length * 2) : Buffer;
                Buffer.AsSpan(Start, unread).CopyTo(target);
                if (!ReferenceEquals(target, Buffer))
                {
                    ArrayPool<byte>.Shared.Return(Buffer);
                    Buffer = target;
                }

                Start = 0;
                End = unread;
            }

            int read = await stream.ReadAsync(Buffer.AsMemory(End), cancelling).ConfigureAwait(false);
            End += read;
            return read;
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(Buffer);
    }
}
