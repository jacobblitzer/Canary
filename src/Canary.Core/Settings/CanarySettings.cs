using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Settings;

// Phase 8 / design §C9 — per-user settings persistence. JSON file at
// %LocalAppData%\Canary\settings.json. Schema is intentionally small
// for now; future knobs (retention threshold, default mode, custom
// port list) get appended here.
public sealed class CanarySettings
{
    // "stabilization" (default v1) or "maturation" — per §C9. Drives
    // whether VLM + regression-cluster panels promote to main nav in
    // future polish work. Phase 8 only persists the choice + surfaces
    // it on the Settings tab; Maturation-mode panels are explicitly NOT
    // built per §C9 ("only the toggle ships").
    public string UiMode { get; set; } = "stabilization";

    // Tier 3 opt-in toggle per §C7. Phase 4 + 6 ship Tier 1 + 2; Tier 3
    // is the noisy heuristic that may show false positives.
    public bool ShowTier3Processes { get; set; } = false;

    // Retention threshold in days for per-run dir purges per §C2 + §0.4
    // default. Defaults to 14.
    public int RetentionDays { get; set; } = 14;

    public static string SettingsFilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Canary");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static CanarySettings Load()
    {
        var path = SettingsFilePath;
        if (!File.Exists(path)) return new CanarySettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CanarySettings>(json, JsonOptions) ?? new CanarySettings();
        }
        catch
        {
            return new CanarySettings();
        }
    }

    public void Save()
    {
        var path = SettingsFilePath;
        var json = JsonSerializer.Serialize(this, JsonOptions);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
