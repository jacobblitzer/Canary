using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

public class TestResultSerializerTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public async Task RoundTrip_PreservesAllFields()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canary_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, "result.json");

            var original = new TestResult
            {
                TestName = "my-test",
                Workload = "test-wl",
                Status = TestStatus.Failed,
                Duration = TimeSpan.FromSeconds(3.5),
                ErrorMessage = "Something broke",
                CheckpointResults = new List<CheckpointResult>
                {
                    new CheckpointResult
                    {
                        Name = "cp1",
                        Status = TestStatus.Failed,
                        DiffPercentage = 0.05,
                        Tolerance = 0.02,
                        SsimScore = 0.98
                    }
                }
            };

            await TestResultSerializer.SaveAsync(original, filePath);
            var loaded = await TestResultSerializer.LoadAsync(filePath);

            Assert.Equal("my-test", loaded.TestName);
            Assert.Equal("test-wl", loaded.Workload);
            Assert.Equal(TestStatus.Failed, loaded.Status);
            Assert.Equal("Something broke", loaded.ErrorMessage);
            Assert.True(loaded.Duration.TotalSeconds > 3.0);
            Assert.Single(loaded.CheckpointResults);
            Assert.Equal("cp1", loaded.CheckpointResults[0].Name);
            Assert.Equal(0.05, loaded.CheckpointResults[0].DiffPercentage, precision: 3);
            Assert.Equal(0.98, loaded.CheckpointResults[0].SsimScore, precision: 3);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
