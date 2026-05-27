using System.Text.Json;
using Canary.Telemetry;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class TelemetryViewModelTests
{
    [Fact]
    public void InitialState_NoWorkloadsDir_ShowsNoFileMessage()
    {
        var vm = new TelemetryViewModel();
        Assert.Contains("no telemetry.ndjson", vm.TailingPath);
    }

    [Fact]
    public void SetWorkloadsDir_FindsAndParsesTelemetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-tel-vm-" + Guid.NewGuid().ToString("N"));
        var resultsDir = Path.Combine(root, "qualia", "results");
        Directory.CreateDirectory(resultsDir);
        var ndjsonPath = Path.Combine(resultsDir, "telemetry.ndjson");

        var t = DateTime.UtcNow;
        File.WriteAllText(ndjsonPath, string.Join('\n', new[]
        {
            JsonSerializer.Serialize(new TelemetryRecord { T = t, Kind = TelemetryKind.Log, Source = "qualia", Level = "info", Data = new { msg = "hello" } }, TelemetryRecord.SerializerOptions),
            JsonSerializer.Serialize(new TelemetryRecord { T = t.AddSeconds(1), Kind = TelemetryKind.Log, Source = "penumbra", Level = "warn", Data = new { msg = "ouch" } }, TelemetryRecord.SerializerOptions),
        }));

        try
        {
            var vm = new TelemetryViewModel();
            vm.SetWorkloadsDir(root);
            Assert.Contains("telemetry.ndjson", vm.TailingPath);
            Assert.Equal(2, vm.Rows.Count);
            Assert.Equal("qualia", vm.Rows[0].Source);
            Assert.Equal("warn", vm.Rows[1].Level);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SourceFilter_NarrowsRowsToSelectedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-tel-vm-" + Guid.NewGuid().ToString("N"));
        var resultsDir = Path.Combine(root, "qualia", "results");
        Directory.CreateDirectory(resultsDir);
        var ndjsonPath = Path.Combine(resultsDir, "telemetry.ndjson");
        var t = DateTime.UtcNow;
        File.WriteAllText(ndjsonPath, string.Join('\n', new[]
        {
            JsonSerializer.Serialize(new TelemetryRecord { T = t, Kind = TelemetryKind.Log, Source = "qualia", Data = new { } }, TelemetryRecord.SerializerOptions),
            JsonSerializer.Serialize(new TelemetryRecord { T = t.AddSeconds(1), Kind = TelemetryKind.Log, Source = "penumbra", Data = new { } }, TelemetryRecord.SerializerOptions),
        }));
        try
        {
            var vm = new TelemetryViewModel();
            vm.SetWorkloadsDir(root);
            Assert.Equal(2, vm.Rows.Count);

            vm.SelectedSource = "qualia";
            Assert.Single(vm.Rows);
            Assert.Equal("qualia", vm.Rows[0].Source);

            vm.SelectedSource = "(all)";
            Assert.Equal(2, vm.Rows.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
