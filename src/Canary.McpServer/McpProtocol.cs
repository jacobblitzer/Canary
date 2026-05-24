using System.Text.Json;
using System.Text.Json.Nodes;

namespace Canary.McpServer;

// Minimal MCP server protocol implementation per the MCP spec
// (2024-11-05 wire version). Stdio transport: newline-delimited JSON
// per line, one JSON-RPC 2.0 message per line. Server handles the
// three methods we care about: initialize / tools/list / tools/call.
//
// We roll our own rather than pull a NuGet SDK because (a) the protocol
// surface is small, (b) self-contained keeps NuGet dependency churn out
// of Canary, and (c) the operator can read the exact wire shape we
// produce when debugging Claude integrations.
internal sealed class McpProtocol
{
    public const string ProtocolVersion = "2024-11-05";
    public const string ServerName = "canary";
    public const string ServerVersion = "0.1.0";

    private readonly Dictionary<string, McpTool> _tools;

    public McpProtocol(IEnumerable<McpTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name);
    }

    // Drives the stdio loop. Reads lines from stdin; for each
    // well-formed JSON-RPC request, dispatches + writes the response on
    // one line of stdout. Notifications (no id) get no response.
    // Returns when stdin closes (the client process exited).
    public async Task RunStdioAsync(TextReader stdin, TextWriter stdout, CancellationToken ct = default)
    {
        string? line;
        while (!ct.IsCancellationRequested && (line = await stdin.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;

            JsonNode? msg;
            try { msg = JsonNode.Parse(line); }
            catch (JsonException) { continue; /* malformed — drop */ }
            if (msg == null) continue;

            var id = msg["id"];
            var method = msg["method"]?.GetValue<string>();
            if (method == null) continue;

            // Notification (no id) — no response.
            if (id == null)
            {
                // Currently only `notifications/initialized` is expected; ignore.
                continue;
            }

            try
            {
                var result = await DispatchAsync(method, msg["params"]).ConfigureAwait(false);
                await WriteResponseAsync(stdout, id, result, error: null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WriteResponseAsync(stdout, id, result: null, error: new JsonObject
                {
                    ["code"] = -32603,  // internal error
                    ["message"] = ex.Message,
                }).ConfigureAwait(false);
            }
        }
    }

    private async Task<JsonNode?> DispatchAsync(string method, JsonNode? @params)
    {
        switch (method)
        {
            case "initialize":
                return new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = ServerName,
                        ["version"] = ServerVersion,
                    },
                };

            case "tools/list":
                var toolsArr = new JsonArray();
                foreach (var t in _tools.Values.OrderBy(t => t.Name))
                {
                    toolsArr.Add(new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["inputSchema"] = JsonNode.Parse(t.InputSchemaJson),
                    });
                }
                return new JsonObject { ["tools"] = toolsArr };

            case "tools/call":
                var name = @params?["name"]?.GetValue<string>() ?? throw new InvalidOperationException("tools/call missing name");
                var args = @params?["arguments"] as JsonObject;
                if (!_tools.TryGetValue(name, out var tool))
                    throw new InvalidOperationException($"Unknown tool: {name}");

                string text;
                bool isError = false;
                try
                {
                    text = await tool.InvokeAsync(args ?? new JsonObject()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    text = $"Tool '{name}' failed: {ex.Message}";
                    isError = true;
                }

                return new JsonObject
                {
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = text },
                    },
                    ["isError"] = isError,
                };

            default:
                throw new InvalidOperationException($"Unknown method: {method}");
        }
    }

    private static async Task WriteResponseAsync(TextWriter stdout, JsonNode id, JsonNode? result, JsonNode? error)
    {
        var resp = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone() };
        if (error != null) resp["error"] = error;
        else resp["result"] = result ?? new JsonObject();

        await stdout.WriteLineAsync(resp.ToJsonString()).ConfigureAwait(false);
        await stdout.FlushAsync().ConfigureAwait(false);
    }
}

// Tool surface for the dispatcher. InputSchemaJson is a raw JSON-Schema
// string — easier to author + read than building JsonNode trees inline.
internal abstract class McpTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    public abstract Task<string> InvokeAsync(JsonObject arguments);
}
