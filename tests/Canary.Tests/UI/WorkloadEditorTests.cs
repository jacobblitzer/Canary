using System.Text.Json;
using Canary.Config;
using Xunit;

namespace Canary.Tests.UI;

public class WorkloadEditorTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void WorkloadConfig_SerializeDeserialize_RoundTrips()
    {
        var config = new WorkloadConfig
        {
            Name = "test-wl",
            DisplayName = "Test Workload",
            AppPath = @"C:\app\test.exe",
            AppArgs = "/nosplash",
            AgentType = "rhino",
            PipeName = "canary-test",
            StartupTimeoutMs = 20000,
            WindowTitle = "Test App"
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var loaded = WorkloadConfig.Parse(json);

        Assert.Equal("test-wl", loaded.Name);
        Assert.Equal("Test Workload", loaded.DisplayName);
        Assert.Equal(@"C:\app\test.exe", loaded.AppPath);
        Assert.Equal("/nosplash", loaded.AppArgs);
        Assert.Equal("rhino", loaded.AgentType);
        Assert.Equal("canary-test", loaded.PipeName);
        Assert.Equal(20000, loaded.StartupTimeoutMs);
        Assert.Equal("Test App", loaded.WindowTitle);
    }
}
