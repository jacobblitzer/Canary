using System.IO;
using System.Linq;
using Canary.Config;
using Xunit;

namespace Canary.Tests.Config;

[Trait("Category", "Unit")]
public class TestCheckpointCaptureTests
{
    [Fact]
    public void Parse_CheckpointWithCaptureGif_PopulatesCaptureSubObject()
    {
        const string json = @"{
            ""name"": ""smoke"",
            ""workload"": ""rhino"",
            ""checkpoints"": [
                {
                    ""name"": ""post-build"",
                    ""atTimeMs"": 5000,
                    ""tolerance"": 0.02,
                    ""capture"": {
                        ""gif"": true,
                        ""frameCount"": 12,
                        ""intervalMs"": 150
                    }
                }
            ]
        }";

        var def = TestDefinition.Parse(json);

        Assert.Single(def.Checkpoints);
        var cp = def.Checkpoints[0];
        Assert.NotNull(cp.Capture);
        Assert.True(cp.Capture!.Gif);
        Assert.Equal(12, cp.Capture.FrameCount);
        Assert.Equal(150, cp.Capture.IntervalMs);
    }

    [Fact]
    public void Parse_CheckpointWithoutCapture_LeavesCaptureNull()
    {
        const string json = @"{
            ""name"": ""smoke"",
            ""workload"": ""rhino"",
            ""checkpoints"": [
                { ""name"": ""post-build"", ""atTimeMs"": 5000, ""tolerance"": 0.02 }
            ]
        }";

        var def = TestDefinition.Parse(json);

        Assert.Single(def.Checkpoints);
        Assert.Null(def.Checkpoints[0].Capture);
    }

    [Fact]
    public void Parse_RealKin18File_HasCaptureGif()
    {
        var path = "C:/Repos/Canary/workloads/rhino/tests/cpig-kin-18-2link-arm.json";
        if (!File.Exists(path)) return; // skip if not present

        // Phase 14.7 — kin-18 retrofit to 4-view layout: capture+scrub lives
        // on the 'persp' checkpoint, not 'post-build'. Find any checkpoint
        // with a capture block rather than relying on positional index so
        // future shuffles don't re-break this.
        var def = TestDefinition.Parse(File.ReadAllText(path));
        var cp = def.Checkpoints.FirstOrDefault(c => c.Capture != null);
        Assert.NotNull(cp);
        Assert.NotNull(cp!.Capture);
        Assert.True(cp.Capture!.Gif);
    }
}
