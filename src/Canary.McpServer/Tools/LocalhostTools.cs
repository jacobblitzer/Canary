using System.Text.Json;
using System.Text.Json.Nodes;
using Canary.Localhost;
using Canary.Telemetry;

namespace Canary.McpServer.Tools;

internal sealed class ListLocalhostPortsTool : McpTool
{
    public override string Name => "list_localhost_ports";
    public override string Description => "List dev-server-relevant TCP listeners on localhost. Combines passive netstat enumeration (Tier 1) with the Canary spawn registry (Tier 2) so each row carries provenance.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "ports": { "type": "array", "items": { "type": "integer" }, "description": "Filter to these specific ports. Omit for the default port list (3000, 3001, 4173, 4200, 5173, 5174, 8000, 8080, 8081, 1420)." }
          },
          "required": []
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var portsNode = args["ports"] as JsonArray;
        IEnumerable<int>? filter = portsNode?.Select(n => n!.GetValue<int>());

        var manager = new LocalhostManager();
        var entries = manager.EnumeratePorts(filter ?? LocalhostManager.DefaultPorts);

        var payload = entries.Select(e => new
        {
            port = e.Port,
            pid = e.Pid,
            processName = e.ProcessName,
            commandLine = e.CommandLine,
            workingDirectory = e.WorkingDirectory,
            startTimeUtc = e.StartTime?.ToString("o"),
            provenance = e.Provenance.ToString(),
        }).ToArray();

        return Task.FromResult(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class ListRunningAppsTool : McpTool
{
    public override string Name => "list_running_apps";
    public override string Description => "List child processes Canary has spawned across all sessions (per the spawn registry — Tier 2). Each record carries the originating intent (e.g. 'Qualia Vite dev server').";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var records = SpawnRegistry.LoadAllSessions();
        var payload = records.Select(r => new
        {
            pid = r.Pid,
            name = r.Name,
            port = r.Port,
            intent = r.Intent,
            commandLine = r.CommandLine,
            workingDirectory = r.WorkingDirectory,
            spawnedAtUtc = r.SpawnedAt.ToString("o"),
        }).ToArray();

        return Task.FromResult(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class KillLocalhostPortTool : McpTool
{
    public override string Name => "kill_localhost_port";
    public override string Description => "Kill the process holding a specific localhost port (tree-kill). Returns whether the port is free after the operation. Loud action — caller should confirm with the operator before invoking on CanaryHarness-provenance rows.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "port": { "type": "integer", "description": "TCP port to free." }
          },
          "required": ["port"]
        }
        """;

    public override async Task<string> InvokeAsync(JsonObject args)
    {
        var port = args["port"]?.GetValue<int>() ?? throw new ArgumentException("port is required");
        var manager = new LocalhostManager();
        var ok = await manager.KillByPortAsync(port).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { port, killed = ok });
    }
}
