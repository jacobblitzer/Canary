using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Telemetry;

// Phase 6 / design §C7 Tier 2 + §0.1 decision 3 (voluntary registration).
// Tracks child processes Canary spawns (Vite, Chrome, etc.) so the
// Localhost panel + the MCP server's `list_running_apps` /
// `list_localhost_ports` tools can attribute provenance ("Qualia Vite
// dev server (Canary qualia workload)" instead of just "node.exe").
//
// Storage: %LocalAppData%\Canary\claude-spawns\<session-id>.json per
// §0.3 default. Each Canary process gets its own session file at start.
// Files are best-effort cleanup; stale files don't harm Tier 1 fallback
// (passive netstat still works).
public sealed class SpawnRegistry
{
    public string SessionFilePath { get; }
    private readonly object _lock = new();
    private readonly SessionDocument _doc;

    public SpawnRegistry(string sessionFilePath, string sessionId)
    {
        SessionFilePath = sessionFilePath;
        _doc = new SessionDocument
        {
            SessionId = sessionId,
            Started = DateTime.UtcNow,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(sessionFilePath)!);
        Flush();
    }

    // Default singleton — discoverable by every Canary process. Session
    // id is the process id + start time, unique per launch.
    private static SpawnRegistry? _default;
    private static readonly object _defaultLock = new();
    public static SpawnRegistry Default
    {
        get
        {
            lock (_defaultLock)
            {
                if (_default != null) return _default;

                var sessionId = $"canary-{Process.GetCurrentProcess().Id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Canary", "claude-spawns");
                var path = Path.Combine(dir, sessionId + ".json");
                _default = new SpawnRegistry(path, sessionId);
                return _default;
            }
        }
    }

    public void Register(int pid, string name, string commandLine, string workingDirectory, int? port, string intent)
    {
        lock (_lock)
        {
            // Replace any existing record for the same pid (Vite/Chrome can
            // re-spawn during a long-running test).
            _doc.Spawns.RemoveAll(s => s.Pid == pid);
            _doc.Spawns.Add(new SpawnRecord
            {
                Pid = pid,
                Name = name,
                CommandLine = commandLine,
                WorkingDirectory = workingDirectory,
                Port = port,
                Intent = intent,
                SpawnedAt = DateTime.UtcNow,
            });
            Flush();
        }
    }

    public void Unregister(int pid)
    {
        lock (_lock)
        {
            var removed = _doc.Spawns.RemoveAll(s => s.Pid == pid);
            if (removed > 0) Flush();
        }
    }

    public IReadOnlyList<SpawnRecord> Snapshot()
    {
        lock (_lock) return _doc.Spawns.ToList();
    }

    // Cross-session view: load every claude-spawns/*.json file under
    // %LocalAppData%\Canary\claude-spawns\ and merge their spawn lists.
    // Used by LocalhostManager's Tier 1 + Tier 2 union and the MCP
    // server's `list_running_apps` tool.
    public static IReadOnlyList<SpawnRecord> LoadAllSessions()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Canary", "claude-spawns");
        if (!Directory.Exists(dir)) return Array.Empty<SpawnRecord>();

        var results = new List<SpawnRecord>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var doc = JsonSerializer.Deserialize<SessionDocument>(json, JsonOptions);
                if (doc?.Spawns != null) results.AddRange(doc.Spawns);
            }
            catch
            {
                // Skip unparseable files.
            }
        }
        return results;
    }

    // Best-effort purge of session files older than the threshold. Called
    // periodically (or at process start) so stale records don't accumulate.
    public static int PurgeOldSessions(TimeSpan maxAge)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Canary", "claude-spawns");
        if (!Directory.Exists(dir)) return 0;

        var cutoffUtc = DateTime.UtcNow - maxAge;
        int purged = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                {
                    File.Delete(file);
                    purged++;
                }
            }
            catch { /* skip */ }
        }
        return purged;
    }

    private void Flush()
    {
        try
        {
            var json = JsonSerializer.Serialize(_doc, JsonOptions);
            var tmp = SessionFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(SessionFilePath)) File.Delete(SessionFilePath);
            File.Move(tmp, SessionFilePath);
        }
        catch
        {
            // Registry persistence is best-effort. A failed flush leaves
            // in-memory state intact for this process; cross-session
            // visibility via LoadAllSessions just won't see the missing
            // record.
        }
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed class SessionDocument
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime Started { get; set; }
        public List<SpawnRecord> Spawns { get; set; } = new();
    }
}

public sealed class SpawnRecord
{
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string Intent { get; set; } = string.Empty;
    public DateTime SpawnedAt { get; set; }
}
