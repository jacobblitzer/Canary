using System.Text.Json.Nodes;
using Canary.McpServer.Tools;
using Canary.Session;
using Xunit;

namespace Canary.Tests.Mcp;

[Trait("Category", "Unit")]
public class SessionsToolsTests
{
    [Fact]
    public async Task ListSessionsTool_ReturnsValidJson()
    {
        var tool = new ListSessionsTool();
        var result = await tool.InvokeAsync(new JsonObject());
        var node = JsonNode.Parse(result);
        Assert.NotNull(node);
    }

    [Fact]
    public async Task GetSessionReportTool_NonexistentId_ReturnsNotFoundMessage()
    {
        var tool = new GetSessionReportTool();
        var args = new JsonObject { ["sessionId"] = "99999999-999999-zzzz" };
        var result = await tool.InvokeAsync(args);
        Assert.Contains("No SESSION_REPORT.md found", result);
    }

    [Fact]
    public async Task GetSessionReportTool_MissingSessionIdArg_Throws()
    {
        var tool = new GetSessionReportTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.InvokeAsync(new JsonObject()));
    }

    [Fact]
    public void ListSessionsTool_HasNameAndSchema()
    {
        var tool = new ListSessionsTool();
        Assert.Equal("list_sessions", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.Contains("workload", tool.InputSchemaJson);
        Assert.Contains("limit", tool.InputSchemaJson);
    }

    [Fact]
    public void GetSessionReportTool_HasNameAndSchema()
    {
        var tool = new GetSessionReportTool();
        Assert.Equal("get_session_report", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.Contains("sessionId", tool.InputSchemaJson);
    }
}
