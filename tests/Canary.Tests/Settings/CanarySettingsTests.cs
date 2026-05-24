using Canary.Settings;
using Xunit;

namespace Canary.Tests.Settings;

// Phase 8 / design §C9 — CanarySettings round-trip + defaults.
public class CanarySettingsTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void Defaults_AreStabilizationMode_NoTier3_14DayRetention()
    {
        var s = new CanarySettings();
        Assert.Equal("stabilization", s.UiMode);
        Assert.False(s.ShowTier3Processes);
        Assert.Equal(14, s.RetentionDays);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SaveAndLoad_RoundTripsCustomValues()
    {
        // Use a temp settings file to avoid clobbering the real one.
        var path = Path.Combine(Path.GetTempPath(), "canary-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var written = new CanarySettings
            {
                UiMode = "maturation",
                ShowTier3Processes = true,
                RetentionDays = 30,
            };
            var json = System.Text.Json.JsonSerializer.Serialize(written, CanarySettings.JsonOptions);
            File.WriteAllText(path, json);

            var read = System.Text.Json.JsonSerializer.Deserialize<CanarySettings>(
                File.ReadAllText(path), CanarySettings.JsonOptions);

            Assert.NotNull(read);
            Assert.Equal("maturation", read!.UiMode);
            Assert.True(read.ShowTier3Processes);
            Assert.Equal(30, read.RetentionDays);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        // Real Load() reads from LocalAppData — we just sanity that
        // calling it doesn't throw and returns a populated instance.
        var s = CanarySettings.Load();
        Assert.NotNull(s);
        Assert.False(string.IsNullOrEmpty(s.UiMode));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SettingsFilePath_IsUnderLocalAppDataCanary()
    {
        var path = CanarySettings.SettingsFilePath;
        Assert.EndsWith($"Canary{Path.DirectorySeparatorChar}settings.json", path);
    }
}
