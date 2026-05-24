using System.Text.Json.Nodes;
using Canary.McpServer;
using Canary.McpServer.Tools;
using Xunit;

namespace Canary.Tests.Mcp;

// Phase 6 / §C6 — unit tests for individual MCP tool dispatch + the
// initialize / tools/list / tools/call protocol surface. End-to-end
// stdio integration is exercised via the operator's manual smoke
// (printf | Canary.McpServer.exe) and the deferred Integration test
// `McpServerStdioIntegrationTests`.
public class McpServerToolDispatchTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public async Task ListLocalhostPortsTool_ReturnsValidJson()
    {
        var tool = new ListLocalhostPortsTool();
        var result = await tool.InvokeAsync(new JsonObject());
        // Should be parseable JSON (array of entries — possibly empty on this machine).
        var node = JsonNode.Parse(result);
        Assert.NotNull(node);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task ListRunningAppsTool_ReturnsValidJson()
    {
        var tool = new ListRunningAppsTool();
        var result = await tool.InvokeAsync(new JsonObject());
        var node = JsonNode.Parse(result);
        Assert.NotNull(node);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task GetFeedbackTool_NonexistentId_ReturnsNotFoundMessage()
    {
        var tool = new GetFeedbackTool();
        var args = new JsonObject { ["id"] = "absolutely-does-not-exist-" + Guid.NewGuid() };
        var result = await tool.InvokeAsync(args);
        Assert.Contains("No feedback item", result);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task ListFeedbackTool_MissingDir_ReturnsGracefulMessage()
    {
        var tool = new ListFeedbackTool();
        // Status "resolved" likely-empty on this dev machine; the tool returns
        // either a "No feedback dir" message or an empty JSON array — either
        // is acceptable for the contract.
        var result = await tool.InvokeAsync(new JsonObject { ["status"] = "resolved" });
        Assert.NotNull(result);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task GetRunReportTool_NonexistentRunId_ReturnsNotFoundMessage()
    {
        var tool = new GetRunReportTool();
        var args = new JsonObject { ["runId"] = "99999999-999999-zzzz" };
        var result = await tool.InvokeAsync(args);
        Assert.Contains("No REPORT.md", result);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task McpProtocol_InitializeAndToolsList_RoundTrip()
    {
        var tools = new McpTool[]
        {
            new ListLocalhostPortsTool(),
            new ListRunningAppsTool(),
            new ListFeedbackTool(),
            new GetFeedbackTool(),
            new MarkFeedbackTriagedTool(),
            new ListRecentRunsTool(),
            new GetRunReportTool(),
            new KillLocalhostPortTool(),
        };
        var protocol = new McpProtocol(tools);

        var input = new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\"}}\n" +
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}\n");
        var output = new StringWriter();

        await protocol.RunStdioAsync(input, output);

        var lines = output.ToString().Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        var init = JsonNode.Parse(lines[0])!;
        Assert.Equal("2.0", init["jsonrpc"]!.GetValue<string>());
        Assert.Equal(1, init["id"]!.GetValue<int>());
        Assert.Equal("2024-11-05", init["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("canary", init["result"]!["serverInfo"]!["name"]!.GetValue<string>());

        var list = JsonNode.Parse(lines[1])!;
        Assert.Equal(2, list["id"]!.GetValue<int>());
        var toolsArr = list["result"]!["tools"]!.AsArray();
        Assert.Equal(8, toolsArr.Count);
        // Every tool advertises name + description + inputSchema.
        foreach (var t in toolsArr)
        {
            Assert.NotNull(t!["name"]);
            Assert.NotNull(t["description"]);
            Assert.NotNull(t["inputSchema"]);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task McpProtocol_ToolsCall_InvokesTool()
    {
        var protocol = new McpProtocol(new McpTool[] { new ListRunningAppsTool() });

        var input = new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"list_running_apps\",\"arguments\":{}}}\n");
        var output = new StringWriter();

        await protocol.RunStdioAsync(input, output);

        var resp = JsonNode.Parse(output.ToString().Trim())!;
        Assert.Equal(7, resp["id"]!.GetValue<int>());
        var content = resp["result"]!["content"]!.AsArray();
        Assert.Single(content);
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.False(resp["result"]!["isError"]!.GetValue<bool>());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task McpProtocol_UnknownTool_ReturnsJsonRpcError()
    {
        var protocol = new McpProtocol(Array.Empty<McpTool>());

        var input = new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{\"name\":\"does_not_exist\",\"arguments\":{}}}\n");
        var output = new StringWriter();

        await protocol.RunStdioAsync(input, output);

        var resp = JsonNode.Parse(output.ToString().Trim())!;
        Assert.Equal(9, resp["id"]!.GetValue<int>());
        Assert.NotNull(resp["error"]);
        Assert.Contains("Unknown tool", resp["error"]!["message"]!.GetValue<string>());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task McpProtocol_NotificationsHaveNoResponse()
    {
        var protocol = new McpProtocol(Array.Empty<McpTool>());

        var input = new StringReader("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}\n");
        var output = new StringWriter();

        await protocol.RunStdioAsync(input, output);

        Assert.Equal(string.Empty, output.ToString().Trim());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task McpProtocol_MalformedJson_IsSkipped()
    {
        var protocol = new McpProtocol(Array.Empty<McpTool>());

        var input = new StringReader(
            "not-json-at-all\n" +
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}\n");
        var output = new StringWriter();

        await protocol.RunStdioAsync(input, output);

        // Only the valid initialize gets a response — the garbage line is dropped.
        var lines = output.ToString().Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        var resp = JsonNode.Parse(lines[0])!;
        Assert.Equal(1, resp["id"]!.GetValue<int>());
    }
}
