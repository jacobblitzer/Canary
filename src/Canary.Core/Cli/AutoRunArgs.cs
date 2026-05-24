using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Cli;

// POCO carrying the subset of `canary run` CLI args that the UI needs to
// auto-run a test from a fresh launch (or a forwarded single-instance message).
// Per design doc §C3 + implementation prompt §3 / §C3.
//
// Round-trips through:
//   - command-line args (`--workload x --test y --suite z --mode m`) for the
//     CLI-launches-UI handoff.
//   - JSON over the single-instance named pipe for the
//     UI-already-running handoff.
public sealed class AutoRunArgs
{
    public string? Workload { get; set; }
    public string? Test { get; set; }
    public string? Suite { get; set; }

    // "pixel-diff" | "vlm" | "both"; null = leave the UI default.
    public string? Mode { get; set; }

    public bool IsEmpty
        => string.IsNullOrEmpty(Workload)
           && string.IsNullOrEmpty(Test)
           && string.IsNullOrEmpty(Suite)
           && string.IsNullOrEmpty(Mode);

    public string[] ToArgs()
    {
        var list = new List<string>();
        if (!string.IsNullOrEmpty(Workload)) { list.Add("--workload"); list.Add(Workload); }
        if (!string.IsNullOrEmpty(Test))     { list.Add("--test");     list.Add(Test);     }
        if (!string.IsNullOrEmpty(Suite))    { list.Add("--suite");    list.Add(Suite);    }
        if (!string.IsNullOrEmpty(Mode))     { list.Add("--mode");     list.Add(Mode);     }
        return list.ToArray();
    }

    // Parse out the auto-run-relevant args from a process command-line. Returns
    // false if no relevant flags are present. Unknown args (e.g. --verbose) are
    // ignored — the UI doesn't care about them at the auto-run boundary.
    public static bool TryParse(string[] args, out AutoRunArgs result)
    {
        result = new AutoRunArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--workload": result.Workload = args[i + 1]; i++; break;
                case "--test":     result.Test     = args[i + 1]; i++; break;
                case "--suite":    result.Suite    = args[i + 1]; i++; break;
                case "--mode":     result.Mode     = args[i + 1]; i++; break;
            }
        }
        return !result.IsEmpty;
    }

    public string ToJson()
        => JsonSerializer.Serialize(this, AutoRunArgsJsonContext.Default.AutoRunArgs);

    public static bool TryParseJson(string json, out AutoRunArgs result)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize(json, AutoRunArgsJsonContext.Default.AutoRunArgs);
            if (parsed == null) { result = new AutoRunArgs(); return false; }
            result = parsed;
            return !result.IsEmpty;
        }
        catch (JsonException)
        {
            result = new AutoRunArgs();
            return false;
        }
    }
}

[JsonSerializable(typeof(AutoRunArgs))]
internal partial class AutoRunArgsJsonContext : JsonSerializerContext
{
}
