using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The agent link's catalogue, and the copy of it the cloud bridge lists while the tool cannot be reached.
/// </summary>
/// <remarks>
/// tools/agent/tools.json is what Claude Code sees at a session's start, before the tool has been reached; it has to
/// be the list this build serves. Set POEF_WRITE_AGENT_TOOLS=1 to write it from the catalogue after changing a tool.
/// </remarks>
public sealed class AgentCatalogueTests
{
    private static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "schema", "poe2.offsets.json")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return dir.FullName;
        }
    }

    /// <summary>The catalogue as the bridge's file holds it: name, description, inputSchema.</summary>
    private static JsonArray Listed()
    {
        var listed = new JsonArray();
        foreach (AgentToolInfo tool in AgentCatalogue.Tools)
        {
            listed.Add((JsonNode)new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.Schema),
            });
        }

        return listed;
    }

    [Fact]
    public void EVERYSchemaIsAJsonObjectSchemaAndEveryNameIsItsOwn()
    {
        Assert.Equal(AgentCatalogue.Tools.Count, AgentCatalogue.Tools.Select(tool => tool.Name).Distinct().Count());
        foreach (AgentToolInfo tool in AgentCatalogue.Tools)
        {
            JsonNode schema = JsonNode.Parse(tool.Schema)!;
            Assert.Equal("object", schema["type"]!.GetValue<string>());
            Assert.NotNull(schema["properties"]);
            Assert.Matches("^[a-z_]+$", tool.Name);
        }
    }

    [Fact]
    public void THEBRIDGESListIsTheCatalogue()
    {
        string path = Path.Combine(RepositoryRoot, "tools", "agent", "tools.json");
        JsonArray listed = Listed();
        if (Environment.GetEnvironmentVariable("POEF_WRITE_AGENT_TOOLS") == "1")
        {
            File.WriteAllText(path, listed.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }) + "\n");
        }

        Assert.True(File.Exists(path), $"{path} is missing - run this test with POEF_WRITE_AGENT_TOOLS=1");
        Assert.True(
            JsonNode.DeepEquals(listed, JsonNode.Parse(File.ReadAllText(path))),
            "tools/agent/tools.json is not the catalogue - run this test with POEF_WRITE_AGENT_TOOLS=1 and commit the file");
    }

    [Fact]
    public async Task THELINKListsTheWholeCatalogueAsValidJson()
    {
        var link = new AgentLink(() => "k", AgentCatalogue.Tools);
        var request = new AgentHttpRequest
        {
            Method = "POST",
            Path = "/mcp",
            Query = string.Empty,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = "Bearer k" },
            Body = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""u8.ToArray(),
        };

        AgentHttpResponse answer = await link.Handle(request, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(answer.Body!);
        JsonElement tools = document.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Equal(AgentCatalogue.Tools.Count, tools.GetArrayLength());
        Assert.All(tools.EnumerateArray(), tool => Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString()));
    }
}
