using Canary.Orchestration;
using Canary.UI.Services;
using Xunit;

namespace Canary.Tests.UI;

public class ResultsHistoryTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Scan_FindsSavedResults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canary_test_{Guid.NewGuid():N}");
        try
        {
            var resultDir = Path.Combine(tempDir, "test-wl", "results", "test1");
            Directory.CreateDirectory(resultDir);

            var result = new TestResult
            {
                TestName = "test1",
                Workload = "test-wl",
                Status = TestStatus.Passed,
                Duration = TimeSpan.FromSeconds(1)
            };
            await TestResultSerializer.SaveAsync(result, Path.Combine(resultDir, "result.json"));

            var history = new ResultsHistory();
            var entries = await history.ScanAsync(tempDir, "test-wl");

            Assert.Single(entries);
            Assert.Equal("test1", entries[0].Result.TestName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task Scan_EmptyDirectory_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canary_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var history = new ResultsHistory();
            var entries = await history.ScanAsync(tempDir, "nonexistent");

            Assert.Empty(entries);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
