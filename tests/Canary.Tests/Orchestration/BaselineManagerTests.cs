using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

public class BaselineManagerTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void ApproveCheckpoint_CopiesCandidateToBaseline()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canary_test_{Guid.NewGuid():N}");
        try
        {
            var candidatesDir = Path.Combine(tempDir, "test-wl", "results", "test1", "candidates");
            Directory.CreateDirectory(candidatesDir);
            File.WriteAllBytes(Path.Combine(candidatesDir, "cp1.png"), new byte[] { 1, 2, 3 });

            BaselineManager.ApproveCheckpoint(tempDir, "test-wl", "test1", "cp1");

            var baselinePath = Path.Combine(tempDir, "test-wl", "results", "test1", "baselines", "cp1.png");
            Assert.True(File.Exists(baselinePath));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(baselinePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void RejectCheckpoint_DeletesCandidate()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canary_test_{Guid.NewGuid():N}");
        try
        {
            var candidatesDir = Path.Combine(tempDir, "test-wl", "results", "test1", "candidates");
            Directory.CreateDirectory(candidatesDir);
            var candidatePath = Path.Combine(candidatesDir, "cp1.png");
            File.WriteAllBytes(candidatePath, new byte[] { 1, 2, 3 });

            Assert.True(File.Exists(candidatePath));

            BaselineManager.RejectCheckpoint(tempDir, "test-wl", "test1", "cp1");

            Assert.False(File.Exists(candidatePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
