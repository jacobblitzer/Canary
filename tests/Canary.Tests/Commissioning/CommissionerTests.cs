using Canary.Commissioning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Canary.Tests.Commissioning;

/// <summary>
/// Deployment campaign Stage C2 — ruling 7A's three layers.
/// </summary>
/// <remarks>
/// Commissioning is the gate that decides whether any other result from a machine is readable,
/// so these tests are mostly about the layers going RED for the right reason. A gate nobody has
/// watched fail is not yet a gate.
/// </remarks>
[Trait("Category", "Unit")]
public class CommissionerTests
{
    private static string ShippedReferences()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Canary.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "workloads", "commissioning", Commissioner.ReferencesFolder);
    }

    private static void With(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "canary-commission-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(dir); body(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    private static string Png(string dir, string name, int w, int h, Rgba32 fill, Action<Image<Rgba32>>? mutate = null)
    {
        using var img = new Image<Rgba32>(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                img[x, y] = fill;
        mutate?.Invoke(img);
        var path = Path.Combine(dir, name);
        img.SaveAsPng(path);
        return path;
    }

    // ------------------------------------------------------------- layer 1

    /// <summary>
    /// The images Canary actually ships produce exactly the answer they are supposed to.
    /// </summary>
    /// <remarks>
    /// This is the one that runs on a machine where nothing else works, so it is tested against
    /// the REAL shipped files rather than fixtures — a layer that only passes against images the
    /// test invented would prove nothing about what installs on a QC box.
    /// </remarks>
    [Fact]
    public void Layer1_AgainstTheShippedImages_Passes()
    {
        var layer = Commissioner.CheckComparer(ShippedReferences());

        Assert.Equal(LayerOutcome.Passed, layer.Outcome);
        Assert.True(layer.Fatal);
        Assert.Equal(1, layer.Number);
        // 256 of 4096 is 6.25% - the P-format must not render it as 0.0625%.
        Assert.Contains("6.25", layer.Detail, StringComparison.Ordinal);
        Assert.Contains("256/4096", layer.Detail, StringComparison.Ordinal);
    }

    /// <summary>Missing content is NotRun, not a pass and not a comparer fault.</summary>
    /// <remarks>
    /// A QC install whose commissioning images did not travel has not proved its comparer is
    /// broken — it has proved the install is incomplete. Those are different findings with
    /// different owners.
    /// </remarks>
    [Fact]
    public void Layer1_WithNoShippedImages_IsNotRun()
    {
        With(dir =>
        {
            var layer = Commissioner.CheckComparer(dir);
            Assert.Equal(LayerOutcome.NotRun, layer.Outcome);
            Assert.Contains("did not travel", layer.Detail, StringComparison.Ordinal);
        });
    }

    /// <summary>MUTATION: a corrupted reference makes layer 1 fail for the right reason.</summary>
    [Fact]
    public void Layer1_WhenAReferenceIsWrong_FailsNamingTheCount()
    {
        With(dir =>
        {
            var black = new Rgba32(18, 20, 24, 255);
            Png(dir, Commissioner.ReferenceA, 64, 64, black);
            Png(dir, Commissioner.ReferenceANudged, 64, 64, new Rgba32(20, 22, 26, 255));
            // B with a 4x4 patch instead of 16x16 - 16 changed pixels, not 256.
            Png(dir, Commissioner.ReferenceB, 64, 64, black, img =>
            {
                for (var y = 0; y < 4; y++)
                    for (var x = 0; x < 4; x++)
                        img[x, y] = new Rgba32(240, 240, 240, 255);
            });

            var layer = Commissioner.CheckComparer(dir);

            Assert.Equal(LayerOutcome.Failed, layer.Outcome);
            Assert.Contains("16 changed pixels", layer.Detail, StringComparison.Ordinal);
            Assert.Contains("expected 256", layer.Detail, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// MUTATION: a comparer ignoring its colour threshold is caught.
    /// </summary>
    /// <remarks>
    /// Simulated by shipping a "nudged" control that is far outside the threshold. A comparer
    /// that passed the first two assertions and failed this one would report every
    /// anti-aliasing difference on earth as a regression — which is why the third assertion
    /// exists at all.
    /// </remarks>
    [Fact]
    public void Layer1_WhenTheSubThresholdControlDiffers_Fails()
    {
        With(dir =>
        {
            var black = new Rgba32(18, 20, 24, 255);
            Png(dir, Commissioner.ReferenceA, 64, 64, black);
            Png(dir, Commissioner.ReferenceB, 64, 64, black, img =>
            {
                for (var y = 16; y < 32; y++)
                    for (var x = 16; x < 32; x++)
                        img[x, y] = new Rgba32(240, 240, 240, 255);
            });
            // 100 per channel away - far outside the threshold of 3.
            Png(dir, Commissioner.ReferenceANudged, 64, 64, new Rgba32(118, 120, 124, 255));

            var layer = Commissioner.CheckComparer(dir);

            Assert.Equal(LayerOutcome.Failed, layer.Outcome);
            Assert.Contains("colour threshold is not being applied", layer.Detail, StringComparison.Ordinal);
        });
    }

    // ------------------------------------------------------------- layer 2

    [Fact]
    public void Layer2_TwoIdenticalCaptures_Pass()
    {
        With(dir =>
        {
            var fill = new Rgba32(30, 40, 50, 255);
            var a = Png(dir, "one.png", 80, 60, fill);
            var b = Png(dir, "two.png", 80, 60, fill);

            var layer = Commissioner.CheckRepeatable(a, b);

            Assert.Equal(LayerOutcome.Passed, layer.Outcome);
            Assert.True(layer.Fatal);
        });
    }

    /// <summary>
    /// MUTATION: a nondeterministic capture path is caught, and named as such.
    /// </summary>
    /// <remarks>
    /// This is the quiet star of ruling 7A — if a machine cannot reproduce its own frame
    /// seconds apart, no baseline from anywhere will ever match on it.
    /// </remarks>
    [Fact]
    public void Layer2_WhenCapturesDiffer_FailsAndSaysBaselinesCannotMatch()
    {
        With(dir =>
        {
            var fill = new Rgba32(30, 40, 50, 255);
            var a = Png(dir, "one.png", 80, 60, fill);
            var b = Png(dir, "two.png", 80, 60, fill, img => img[0, 0] = new Rgba32(255, 255, 255, 255));

            var layer = Commissioner.CheckRepeatable(a, b);

            Assert.Equal(LayerOutcome.Failed, layer.Outcome);
            Assert.Contains("1 px", layer.Detail, StringComparison.Ordinal);
            Assert.Contains("no pixel baseline can ever match here", layer.Detail, StringComparison.Ordinal);
        });
    }

    /// <summary>A size change is reported as a sizing fault, not a pixel diff.</summary>
    /// <remarks>
    /// The comparer THROWS on mismatched dimensions, so without this branch layer 2 would
    /// report an exception message instead of the actual problem — a viewport that is not
    /// being sized deterministically.
    /// </remarks>
    [Fact]
    public void Layer2_WhenTheViewportResizes_SaysSo()
    {
        With(dir =>
        {
            var fill = new Rgba32(30, 40, 50, 255);
            var a = Png(dir, "one.png", 80, 60, fill);
            var b = Png(dir, "two.png", 81, 60, fill);

            var layer = Commissioner.CheckRepeatable(a, b);

            Assert.Equal(LayerOutcome.Failed, layer.Outcome);
            Assert.Contains("differ in SIZE", layer.Detail, StringComparison.Ordinal);
            Assert.Contains("sized deterministically", layer.Detail, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Layer2_WithNoCaptures_IsNotRun_NotPassed()
    {
        With(dir =>
        {
            var layer = Commissioner.CheckRepeatable(Path.Combine(dir, "nope.png"), Path.Combine(dir, "also-nope.png"));
            Assert.Equal(LayerOutcome.NotRun, layer.Outcome);
        });
    }

    // ------------------------------------------------------------- layer 3

    /// <summary>
    /// Layer 3 failing is a WARNING about travelling baselines, never a broken harness.
    /// </summary>
    /// <remarks>
    /// A machine that fails this can still test perfectly well — it simply has to approve its
    /// own baselines, or use VLM mode. Marking it fatal would ground a machine that is fine,
    /// and would report on a question the run never asked.
    /// </remarks>
    [Fact]
    public void Layer3_IsNeverFatal_EvenWhenItFails()
    {
        With(dir =>
        {
            var shipped = Png(dir, "shipped.png", 40, 40, new Rgba32(10, 10, 10, 255));
            var here = Png(dir, "here.png", 40, 40, new Rgba32(200, 200, 200, 255));

            var layer = Commissioner.CheckShippedReference(shipped, here);

            Assert.Equal(LayerOutcome.Failed, layer.Outcome);
            Assert.False(layer.Fatal);
            Assert.Contains("do NOT", layer.Detail, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Layer3_WithNoShippedReference_IsNotRun()
    {
        With(dir =>
        {
            var here = Png(dir, "here.png", 40, 40, new Rgba32(10, 10, 10, 255));
            var layer = Commissioner.CheckShippedReference(Path.Combine(dir, "absent.png"), here);

            Assert.Equal(LayerOutcome.NotRun, layer.Outcome);
            Assert.False(layer.Fatal);
        });
    }

    // --------------------------------------------------------- the verdict

    /// <summary>
    /// <c>NotRun</c> never counts as a pass.
    /// </summary>
    /// <remarks>
    /// The whole campaign exists because a missing baseline yielded <c>New</c> and <c>New</c>
    /// was excluded from the exit code. A layer nobody attempted has answered nothing.
    /// </remarks>
    [Fact]
    public void HarnessUsable_TreatsNotRunAsUnusable()
    {
        var report = new CommissioningReport
        {
            Layers = new[]
            {
                new CommissioningLayer(1, "comparer", LayerOutcome.Passed, "", true),
                new CommissioningLayer(2, "repeatable", LayerOutcome.NotRun, "", true),
                new CommissioningLayer(3, "reference", LayerOutcome.Passed, "", false),
            },
        };

        Assert.False(report.HarnessUsable);
    }

    [Fact]
    public void HarnessUsable_IgnoresANonFatalLayerFailing()
    {
        var report = new CommissioningReport
        {
            Layers = new[]
            {
                new CommissioningLayer(1, "comparer", LayerOutcome.Passed, "", true),
                new CommissioningLayer(2, "repeatable", LayerOutcome.Passed, "", true),
                new CommissioningLayer(3, "reference", LayerOutcome.Failed, "", false),
            },
        };

        Assert.True(report.HarnessUsable);
    }

    /// <summary>An empty report is not usable — nothing was proved.</summary>
    [Fact]
    public void HarnessUsable_OnAReportWithNoFatalLayers_IsFalse()
    {
        Assert.False(new CommissioningReport().HarnessUsable);
    }

    [Fact]
    public void SaveThenLoad_PreservesTheLayersAndTheVerdict()
    {
        With(dir =>
        {
            var path = Path.Combine(dir, CommissioningReport.FileName);
            new CommissioningReport
            {
                CapturedUtc = "2026-08-19T00:00:00Z",
                Workload = "rhino",
                Machine = new Dictionary<string, string> { ["machineName"] = "BOX" },
                Layers = new[]
                {
                    new CommissioningLayer(1, "comparer", LayerOutcome.Passed, "self 0", true),
                    new CommissioningLayer(2, "repeatable", LayerOutcome.Failed, "differs", true),
                    new CommissioningLayer(3, "reference", LayerOutcome.NotRun, "none", false),
                },
            }.Save(path);

            var read = CommissioningReport.Load(path);

            Assert.Equal("rhino", read.Workload);
            Assert.Equal(3, read.Layers.Count);
            Assert.Equal(LayerOutcome.Failed, read.Layers[1].Outcome);
            Assert.True(read.Layers[1].Fatal);
            Assert.False(read.Layers[2].Fatal);
            Assert.False(read.HarnessUsable);
        });
    }

    /// <summary>An unreadable outcome loads as Failed — never Passed, never NotRun.</summary>
    /// <remarks>
    /// A layer whose result cannot be read has demonstrated nothing, and the safe reading of
    /// "cannot tell" for a gate is "do not trust this machine".
    /// </remarks>
    [Fact]
    public void Load_OnAnUnreadableOutcome_IsFailed()
    {
        With(dir =>
        {
            var path = Path.Combine(dir, CommissioningReport.FileName);
            File.WriteAllText(path,
                """
                { "capturedUtc": "x", "workload": "w", "machine": {},
                  "layers": [ { "number": 1, "name": "comparer", "outcome": "Sideways", "fatal": true, "detail": "" } ] }
                """);

            var read = CommissioningReport.Load(path);
            Assert.Equal(LayerOutcome.Failed, Assert.Single(read.Layers).Outcome);
            Assert.False(read.HarnessUsable);
        });
    }

    [Fact]
    public void Load_OnAnAbsentOrCorruptReport_Throws()
    {
        With(dir =>
        {
            Assert.Throws<FileNotFoundException>(() => CommissioningReport.Load(Path.Combine(dir, "nope.json")));

            var bad = Path.Combine(dir, CommissioningReport.FileName);
            File.WriteAllText(bad, "{ not json");
            Assert.Throws<InvalidDataException>(() => CommissioningReport.Load(bad));
        });
    }
}
