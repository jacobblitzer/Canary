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

    // ── R1.6 flight-recorder Phase D tools ────────────────────────────────────

    [Fact]
    public void GetSessionManifestTool_HasNameAndSchema()
    {
        var tool = new GetSessionManifestTool();
        Assert.Equal("get_session_manifest", tool.Name);
        Assert.Contains("sessionId", tool.InputSchemaJson);
    }

    [Fact]
    public async Task GetSessionManifestTool_NonexistentId_ReturnsNotFoundMessage()
    {
        var tool = new GetSessionManifestTool();
        var result = await tool.InvokeAsync(new JsonObject { ["sessionId"] = "99999999-999999-zzzz" });
        Assert.Contains("No manifest.json found", result);
    }

    [Fact]
    public async Task GetSessionTelemetryTool_MissingSessionIdArg_Throws()
    {
        var tool = new GetSessionTelemetryTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.InvokeAsync(new JsonObject()));
    }

    [Fact]
    public async Task GetSessionTelemetryTool_FiltersByEventPrefix_WithKindFallback_AndTails()
    {
        // Fabricate a session dir in a temp workloads root and point discovery at it.
        var root = Path.Combine(Path.GetTempPath(), "canary-mcp-telemetry-" + Guid.NewGuid().ToString("N"));
        var sessionId = "20260703-120000-test";
        var sessionDir = Path.Combine(root, "rhino", SessionPaths.SessionsSubdir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var lines = new List<string>();
        // Tailed Penumbra/CPig shape: kind=Log with the domain kind nested at data.event.
        for (int i = 0; i < 5; i++)
            lines.Add($"{{\"t\":\"2026-07-03T12:00:0{i}Z\",\"kind\":\"Log\",\"source\":\"penumbra\",\"data\":{{\"event\":\"cpig.push.done\",\"payload\":{{\"seq\":{i}}}}}}}");
        lines.Add("{\"t\":\"2026-07-03T12:00:06Z\",\"kind\":\"Log\",\"source\":\"penumbra\",\"data\":{\"event\":\"gl.scene.snapshot\",\"payload\":{}}}");
        // Native Canary record: no data.event — the filter must fall back to kind.
        lines.Add("{\"t\":\"2026-07-03T12:00:07Z\",\"kind\":\"Screenshot\",\"source\":\"canary-session\",\"data\":{\"sequence\":1}}");
        File.WriteAllLines(Path.Combine(sessionDir, SessionPaths.TelemetryNdjsonFileName), lines);

        var prev = Environment.GetEnvironmentVariable("CANARY_WORKLOADS_DIR");
        Environment.SetEnvironmentVariable("CANARY_WORKLOADS_DIR", root);
        try
        {
            var tool = new GetSessionTelemetryTool();

            var pushes = await tool.InvokeAsync(new JsonObject { ["sessionId"] = sessionId, ["eventPrefix"] = "cpig.push" });
            Assert.Contains("5 match(es)", pushes);
            Assert.DoesNotContain("gl.scene.snapshot", pushes);

            var tailed = await tool.InvokeAsync(new JsonObject { ["sessionId"] = sessionId, ["eventPrefix"] = "cpig.push", ["tail"] = 2 });
            Assert.Contains("showing the LAST 2", tailed);
            Assert.Contains("\"seq\":4", tailed);
            Assert.DoesNotContain("\"seq\":0", tailed);

            var byKind = await tool.InvokeAsync(new JsonObject { ["sessionId"] = sessionId, ["eventPrefix"] = "Screenshot" });
            Assert.Contains("1 match(es)", byKind);
            Assert.Contains("canary-session", byKind);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CANARY_WORKLOADS_DIR", prev);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
