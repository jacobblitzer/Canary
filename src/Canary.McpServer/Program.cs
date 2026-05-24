using Canary.McpServer;
using Canary.McpServer.Tools;

// Phase 6 / design §C6 — entry point for `Canary.McpServer.exe`. Stdio
// JSON-RPC transport per the MCP spec; Claude Code spawns this as a
// child process via a `.mcp.json` entry pointing at the built exe.
// See docs/mcp-server.md for setup.

// Stdin / stdout are the wire. Stderr is reserved for diagnostics (Claude
// Code prefixes it with [stderr] in its tool-error UI; we keep it quiet by
// default unless CANARY_MCP_DEBUG is set).

var tools = new McpTool[]
{
    new ListFeedbackTool(),
    new GetFeedbackTool(),
    new MarkFeedbackTriagedTool(),
    new ListRecentRunsTool(),
    new GetRunReportTool(),
    new ListLocalhostPortsTool(),
    new ListRunningAppsTool(),
    new KillLocalhostPortTool(),
};

var protocol = new McpProtocol(tools);

try
{
    await protocol.RunStdioAsync(Console.In, Console.Out);
}
catch (Exception ex)
{
    // Last-ditch logging to stderr so the Claude Code session shows
    // something. The stdio loop catches per-message exceptions already;
    // anything escaping here is a startup or stdio-closure surprise.
    Console.Error.WriteLine($"[canary-mcp] fatal: {ex.Message}");
    Environment.Exit(1);
}
