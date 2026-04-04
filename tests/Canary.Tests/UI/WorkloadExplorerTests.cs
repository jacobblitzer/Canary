using System.Text.Json;
using Canary.UI.Services;
using Xunit;

namespace Canary.Tests.UI;

public class WorkloadExplorerTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public async Task LoadWorkloads_ValidDirectory_DiscoversWorkloads()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canary_test_{Guid.NewGuid():N}");
        try
        {
            // Create a workload with a config and a test
            var workloadDir = Path.Combine(tempDir, "test-workload");
            var testsDir = Path.Combine(workloadDir, "tests");
            Directory.CreateDirectory(testsDir);

            var config = new
            {
                name = "test-workload",
                displayName = "Test Workload",
                appPath = "test.exe",
                pipeName = "canary-test",
                startupTimeoutMs = 10000
            };
            await File.WriteAllTextAsync(
                Path.Combine(workloadDir, "workload.json"),
                JsonSerializer.Serialize(config));

            var testDef = new
            {
                name = "my-test",
                workload = "test-workload",
                checkpoints = new[] { new { name = "cp1", tolerance = 0.02 } }
            };
            await File.WriteAllTextAsync(
                Path.Combine(testsDir, "my-test.json"),
                JsonSerializer.Serialize(testDef));

            var explorer = new WorkloadExplorer();
            var entries = await explorer.LoadWorkloadsAsync(tempDir);

            Assert.Single(entries);
            Assert.Equal("Test Workload", entries[0].Config.DisplayName);
            Assert.Single(entries[0].Tests);
            Assert.Equal("my-test", entries[0].Tests[0].Name);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task LoadWorkloads_EmptyDirectory_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canary_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var explorer = new WorkloadExplorer();
            var entries = await explorer.LoadWorkloadsAsync(tempDir);

            Assert.Empty(entries);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task LoadWorkloads_MissingTestsDir_ReturnsWorkloadWithNoTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canary_test_{Guid.NewGuid():N}");
        try
        {
            var workloadDir = Path.Combine(tempDir, "no-tests");
            Directory.CreateDirectory(workloadDir);

            var config = new
            {
                name = "no-tests",
                displayName = "No Tests",
                appPath = "test.exe",
                pipeName = "canary-test",
                startupTimeoutMs = 10000
            };
            await File.WriteAllTextAsync(
                Path.Combine(workloadDir, "workload.json"),
                JsonSerializer.Serialize(config));

            var explorer = new WorkloadExplorer();
            var entries = await explorer.LoadWorkloadsAsync(tempDir);

            Assert.Single(entries);
            Assert.Empty(entries[0].Tests);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
