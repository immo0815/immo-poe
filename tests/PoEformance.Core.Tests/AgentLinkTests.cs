using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The agent link's server and protocol, over a real loopback socket - the same bytes the tunnel forwards.
/// </summary>
public sealed class AgentLinkTests : IDisposable
{
    private const string Key = "poef_test-key-for-the-agent-link";

    private static readonly AgentToolInfo[] Catalogue =
    [
        new("echo", "Says its argument back.", """{"type":"object","properties":{"text":{"type":"string"}}}"""),
        new("slow", "Takes a while.", """{"type":"object"}"""),
        new("unbound", "Nothing binds this one.", """{"type":"object"}"""),
        new("picture", "A one-pixel picture.", """{"type":"object"}"""),
    ];

    private readonly AgentLink _link;
    private readonly AgentHttpServer _server;
    private readonly HttpClient _http;
    private readonly TaskCompletionSource _release = new();

    public AgentLinkTests()
    {
        _link = new AgentLink(() => Key, Catalogue) { ReplyWithin = TimeSpan.FromMilliseconds(300) };
        _link.Bind("echo", (arguments, _) => Task.FromResult(AgentResult.Say("echo: " + arguments.Text("text"))));
        _link.Bind("slow", async (_, _) =>
        {
            await _release.Task.ConfigureAwait(false);
            return AgentResult.Say("slow is done");
        });
        _link.Bind("picture", (_, _) =>
        {
            var result = new AgentResult();
            result.Content.Add(AgentContent.Picture([0x89, 0x50, 0x4E, 0x47]));
            return Task.FromResult(result);
        });

        _server = new AgentHttpServer(0, _link.Handle);
        _server.Start();
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
    }

    public void Dispose()
    {
        _release.TrySetResult();
        _http.Dispose();
        _server.Dispose();
    }

    private async Task<JsonElement> Rpc(string method, string parameters = "{}", int id = 1)
    {
        using var content = new StringContent(
            $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{parameters}}}""",
            Encoding.UTF8, "application/json");
        using HttpResponseMessage answer = await _http.PostAsync("/mcp", content);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await answer.Content.ReadAsByteArrayAsync());
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task INITIALIZEAnswersWithTheToolsNameVersionAndTheAskedRevision()
    {
        JsonElement answer = await Rpc("initialize", """{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"t","version":"1"}}""");
        JsonElement result = answer.GetProperty("result");
        Assert.Equal("2025-03-26", result.GetProperty("protocolVersion").GetString());
        Assert.Equal("poeformance", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal(ToolVersion.Number, result.GetProperty("serverInfo").GetProperty("version").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task AREVISIONTheServerDoesNotKnowIsAnsweredWithItsOwn()
    {
        JsonElement answer = await Rpc("initialize", """{"protocolVersion":"2099-01-01"}""");
        Assert.Equal(AgentLink.ProtocolVersion, answer.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task AWRONGKeyIsRefusedBeforeAnythingIsRead()
    {
        using var stranger = new HttpClient { BaseAddress = _http.BaseAddress };
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "poef_wrong");
        using var content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage answer = await stranger.PostAsync("/mcp", content);
        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);

        using var bare = new HttpClient { BaseAddress = _http.BaseAddress };
        using var again = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage none = await bare.PostAsync("/mcp", again);
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(2, _link.Refused);
    }

    [Fact]
    public async Task THELISTNamesEveryToolOfTheCatalogueWithItsSchema()
    {
        JsonElement tools = (await Rpc("tools/list")).GetProperty("result").GetProperty("tools");
        Assert.Equal(Catalogue.Select(tool => tool.Name), tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()));
        Assert.Equal("object", tools[0].GetProperty("inputSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ACALLIsAnsweredWithItsResult()
    {
        JsonElement result = (await Rpc("tools/call", """{"name":"echo","arguments":{"text":"hello"}}""")).GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal("echo: hello", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    /// <summary>
    /// What runs before every call (v0.1.412: the overlay brings the game's window to the front there) is handed the tool's name, and
    /// what it says opens the answer; one that says nothing leaves the answer as it was, and one that fails does not stop the call.
    /// </summary>
    [Fact]
    public async Task WHATRUNSBeforeEveryCallOpensItsAnswerAndNeverStopsIt()
    {
        var asked = new List<string>();
        _link.Before = name =>
        {
            lock (asked)
            {
                asked.Add(name);
            }

            return Task.FromResult<string?>(name == "echo" ? "brought the game's window to the front" : null);
        };
        JsonElement brought = (await Rpc("tools/call", """{"name":"echo","arguments":{"text":"hello"}}""")).GetProperty("result").GetProperty("content");
        Assert.Equal("brought the game's window to the front\n", brought[0].GetProperty("text").GetString());
        Assert.Equal("echo: hello", brought[1].GetProperty("text").GetString());

        JsonElement quiet = (await Rpc("tools/call", """{"name":"picture"}""", 2)).GetProperty("result").GetProperty("content");
        Assert.Equal(1, quiet.GetArrayLength());
        Assert.Equal("image", quiet[0].GetProperty("type").GetString());

        _link.Before = _ => throw new InvalidOperationException("no window");
        JsonElement failed = (await Rpc("tools/call", """{"name":"echo","arguments":{"text":"still"}}""", 3)).GetProperty("result");
        Assert.False(failed.GetProperty("isError").GetBoolean());
        Assert.Equal("before the call: InvalidOperationException: no window\n", failed.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("echo: still", failed.GetProperty("content")[1].GetProperty("text").GetString());
        Assert.Equal(["echo", "picture"], asked);
    }

    [Fact]
    public async Task APICTUREIsSentAsAnImageTheAgentSees()
    {
        JsonElement part = (await Rpc("tools/call", """{"name":"picture"}""")).GetProperty("result").GetProperty("content")[0];
        Assert.Equal("image", part.GetProperty("type").GetString());
        Assert.Equal("image/png", part.GetProperty("mimeType").GetString());
        Assert.Equal(Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]), part.GetProperty("data").GetString());
    }

    [Fact]
    public async Task ALONGCallIsAnsweredAsPendingAndCollectedOnceItEnds()
    {
        JsonElement first = (await Rpc("tools/call", """{"name":"slow"}""")).GetProperty("result");
        string job = first.GetProperty("structuredContent").GetProperty("pending").GetString()!;

        JsonElement still = (await Rpc("poeformance/wait", $$"""{"job":"{{job}}","seconds":0.1}""")).GetProperty("result");
        Assert.Equal(job, still.GetProperty("structuredContent").GetProperty("pending").GetString());

        _release.SetResult();
        JsonElement done = (await Rpc("poeformance/wait", $$"""{"job":"{{job}}","seconds":5}""")).GetProperty("result");
        Assert.False(done.TryGetProperty("structuredContent", out _));
        Assert.Equal("slow is done", done.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ATOOLNothingBoundSaysSoAsAnError()
    {
        JsonElement result = (await Rpc("tools/call", """{"name":"unbound"}""")).GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task ANUNKNOWNToolOrMethodIsAJsonRpcError()
    {
        Assert.Equal(-32602, (await Rpc("tools/call", """{"name":"nope"}""")).GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32601, (await Rpc("resources/list")).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ANOTIFICATIONIsAcceptedWithNoBody()
    {
        using var content = new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage answer = await _http.PostAsync("/mcp", content);
        Assert.Equal(HttpStatusCode.Accepted, answer.StatusCode);
    }

    [Fact]
    public async Task AFILEOfferedIsServedWholeByItsId()
    {
        string path = Path.Combine(Path.GetTempPath(), $"agent-link-{Guid.NewGuid():N}.bin");
        byte[] bytes = new byte[300_000];
        new Random(7).NextBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            AgentFile file = _link.Offer(path);
            Assert.Equal(bytes, await _http.GetByteArrayAsync($"/files/{file.Id}"));
            using HttpResponseMessage missing = await _http.GetAsync("/files/0123456789abcdef01234567");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ACHUNKEDBodyAndASecondRequestOnTheSameConnectionAreBothRead()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, _server.Port);
        await using var stream = new NetworkStream(socket);
        const string Body = """{"jsonrpc":"2.0","id":5,"method":"ping"}""";
        string chunked = $"POST /mcp HTTP/1.1\r\nHost: x\r\nAuthorization: Bearer {Key}\r\nTransfer-Encoding: chunked\r\n\r\n"
            + $"{10:x}\r\n{Body[..10]}\r\n{Body.Length - 10:x}\r\n{Body[10..]}\r\n0\r\n\r\n";
        string plain = $"POST /mcp HTTP/1.1\r\nHost: x\r\nAuthorization: Bearer {Key}\r\nContent-Length: {Body.Length}\r\n\r\n{Body}";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(chunked + plain));

        string answers = await ReadAtLeast(stream, 2, "\"id\":5");
        Assert.Equal(2, CountOf(answers, "HTTP/1.1 200 OK"));
    }

    [Fact]
    public async Task AHEADERBlockLargerThanTheLimitIsRefused()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, _server.Port);
        await using var stream = new NetworkStream(socket);
        string huge = "GET /mcp HTTP/1.1\r\nX-Big: " + new string('a', AgentHttpServer.HeaderLimit + 10) + "\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(huge));
        string answer = await ReadAtLeast(stream, 1, "HTTP/1.1 400");
        Assert.StartsWith("HTTP/1.1 400", answer, StringComparison.Ordinal);
    }

    private static async Task<string> ReadAtLeast(Stream stream, int count, string marker)
    {
        var text = new StringBuilder();
        var buffer = new byte[8192];
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (CountOf(text.ToString(), marker) < count)
        {
            int read = await stream.ReadAsync(buffer, limit.Token);
            if (read == 0)
            {
                break;
            }

            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return text.ToString();
    }

    private static int CountOf(string text, string what)
    {
        int count = 0;
        for (int at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void AKEYIsMadeOnceKeptSealedAndReadBack()
    {
        string path = Path.Combine(Path.GetTempPath(), $"agent-key-{Guid.NewGuid():N}.json");
        try
        {
            static byte[] Seal(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).ToArray();
            string made = AgentKeyStore.LoadOrMake(Seal, Seal, path);
            Assert.StartsWith("poef_", made, StringComparison.Ordinal);
            Assert.True(made.Length > 40);
            Assert.DoesNotContain(made, File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Equal(made, AgentKeyStore.LoadOrMake(Seal, Seal, path));
            Assert.Equal(8, AgentKeyStore.Fingerprint(made).Length);
            Assert.NotEqual(made, AgentKeyStore.Make());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void THEBEACONSaysTheAddressAndNoSecret()
    {
        byte[] content = AgentBeacon.Content("https://a-b-c.trycloudflare.com", "0123abcd", "0.1.405", "abc", DateTimeOffset.UnixEpoch);
        using JsonDocument document = JsonDocument.Parse(content);
        Assert.Equal("https://a-b-c.trycloudflare.com", document.RootElement.GetProperty("url").GetString());
        Assert.Equal("0123abcd", document.RootElement.GetProperty("key").GetString());
    }
}
