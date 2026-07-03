using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Canary.Session;

/// <summary>
/// Reads/writes the per-session manifest.json (flight-recorder Phase A) and harvests
/// identity fields out of the session's tailed Penumbra telemetry.
/// </summary>
public static class SessionManifestWriter
{
    public static void Write(string sessionDir, SessionManifest manifest)
    {
        Directory.CreateDirectory(sessionDir);
        AtomicWriteAllText(
            SessionPaths.ManifestPath(sessionDir),
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    public static SessionManifest? TryRead(string sessionDir)
    {
        var path = SessionPaths.ManifestPath(sessionDir);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(path), JsonOptions); }
        catch { return null; }
    }

    /// <summary>
    /// The Penumbra event names whose payload fields are worth lifting into the manifest, and the
    /// payload keys lifted from each. penumbra.startup-diagnostics fires on the FIRST scene push
    /// (Tier-2); the startup banner + gl.build.probe keys are future-proofing for the Phase B
    /// Penumbra-side emissions (banner-before-Init fix, GPU identity in the probe payload) — the
    /// harvest is tolerant: absent keys are simply not lifted.
    /// </summary>
    private static readonly Dictionary<string, string[]> HarvestSpec = new(StringComparer.OrdinalIgnoreCase)
    {
        ["penumbra.startup-diagnostics"] = new[]
        {
            "pluginGitSha", "pluginGitDirty", "pluginBuiltUtc",
            "bundleGitSha", "bundleGitDirty", "nodeHostPid", "skewVerdict",
        },
        ["penumbra.startup-banner"] = new[]
        {
            "pluginGitSha", "pluginGitDirty", "pluginBuiltUtc",
        },
        ["gl.build.probe"] = new[]
        {
            "renderer", "vendor", "glVersion", "glslVersion",
        },
    };

    /// <summary>
    /// Scan a session telemetry NDJSON for Penumbra-sourced records (as wrapped by
    /// PenumbraPreviewTelemetryTail: source=="penumbra", Data.event = domain kind,
    /// Data.payload = original payload) and lift the HarvestSpec fields. Later records win
    /// (a re-push refreshes the SHAs). Robust to garbled lines; never throws.
    /// </summary>
    public static Dictionary<string, string> HarvestFromTelemetry(string telemetryPath)
    {
        var harvested = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(telemetryPath)) return harvested;
        string[] lines;
        try { lines = File.ReadAllLines(telemetryPath); } catch { return harvested; }
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonNode.Parse(line) is not JsonObject o) continue;
                if (!string.Equals(TryStr(o["source"]), "penumbra", StringComparison.OrdinalIgnoreCase)) continue;
                if (o["data"] is not JsonObject data) continue;
                var evt = TryStr(data["event"]);
                if (evt == null || !HarvestSpec.TryGetValue(evt, out var keys)) continue;
                if (data["payload"] is not JsonObject payload) continue;
                foreach (var key in keys)
                {
                    var value = payload[key];
                    if (value == null) continue;
                    var s = value is JsonValue ? TryStr(value) ?? value.ToJsonString() : value.ToJsonString();
                    if (!string.IsNullOrEmpty(s)) harvested[key] = s;
                }
            }
            catch { /* skip a garbled line */ }
        }
        return harvested;
    }

    private static string? TryStr(JsonNode? node)
    {
        try { return node?.GetValue<string>(); }
        catch { return node?.ToJsonString(); }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static void AtomicWriteAllText(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Encoding.UTF8);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}
