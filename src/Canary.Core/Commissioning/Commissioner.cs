using Canary.Comparison;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Canary.Commissioning;

/// <summary>
/// The three layers of ruling 7A — "can this machine test at all?"
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Stage C2. Every method here is a <b>pure function over files</b>: no
/// app, no pipe, no workload config. The caller supplies the images; this decides what they
/// mean. That is what makes the layers unit-testable at all, and it is why commissioning is
/// its own verb rather than a test run.
/// </para>
/// <para>
/// It also quietly closes finding F4, which said level-1 commissioning "has no execution path"
/// and needed two new mechanisms — a no-app run mode and a baseline-from-file override —
/// because every run path requires a <c>workload.json</c>, launches the app and connects a
/// pipe, and the baseline side is hard-coded at both comparison sites. All true of the
/// <i>test-run</i> path. But <see cref="PixelDiffComparer"/> takes two images and nothing
/// else, so a verb that calls it directly needs neither mechanism.
/// </para>
/// </remarks>
public static class Commissioner
{
    /// <summary>Folder under the commissioning workload holding the shipped images.</summary>
    public const string ReferencesFolder = "references";

    /// <summary>The two images layer 1 compares, and the sub-threshold control.</summary>
    public const string ReferenceA = "comparer-a.png";
    /// <summary>The B image: A plus an exact 16x16 patch.</summary>
    public const string ReferenceB = "comparer-b.png";
    /// <summary>A copy of A shifted by 2 per channel — below the default threshold of 3.</summary>
    public const string ReferenceANudged = "comparer-a-nudged.png";

    /// <summary>Changed pixels the shipped pair must produce: a 16x16 patch.</summary>
    public const int ExpectedChangedPixels = 256;

    /// <summary>Total pixels in the shipped references: 64x64.</summary>
    public const int ExpectedTotalPixels = 64 * 64;

    /// <summary>
    /// Layer 1 — the comparer agrees with images Canary ships. <b>No app required.</b>
    /// </summary>
    /// <param name="referencesDir">Folder holding the shipped reference images.</param>
    /// <returns>The layer result.</returns>
    /// <remarks>
    /// <para>
    /// This is the layer that runs on a machine where nothing else does, which is the entire
    /// point: if the comparer disagrees with images whose answer is known exactly, then no
    /// other result from this machine means anything, and the failure is in Canary rather than
    /// in whatever was being tested.
    /// </para>
    /// <para>
    /// Three assertions, because "it ran without throwing" is not evidence:
    /// an image against itself must find <b>zero</b> differences (the comparer must not invent
    /// them); the shipped pair must find <b>exactly</b> 256 (it must detect what is there);
    /// and a copy shifted by 2 per channel must find <b>zero</b> at the default threshold of 3
    /// (the threshold must actually work). A comparer that passed the first two and failed the
    /// third would report every anti-aliasing difference on earth as a regression.
    /// </para>
    /// </remarks>
    public static CommissioningLayer CheckComparer(string referencesDir)
    {
        const string name = "comparer";
        try
        {
            var pathA = Path.Combine(referencesDir, ReferenceA);
            var pathB = Path.Combine(referencesDir, ReferenceB);
            var pathN = Path.Combine(referencesDir, ReferenceANudged);

            foreach (var p in new[] { pathA, pathB, pathN })
            {
                if (!File.Exists(p))
                {
                    return new CommissioningLayer(1, name, LayerOutcome.NotRun,
                        $"reference image missing: {p} - the commissioning content did not travel with this install",
                        true, ContentFault: true);
                }
            }

            var comparer = new PixelDiffComparer();
            using var a = Image.Load<Rgba32>(pathA);
            using var b = Image.Load<Rgba32>(pathB);
            using var nudged = Image.Load<Rgba32>(pathN);

            using var self = comparer.Compare(a, a);
            using var pair = comparer.Compare(a, b);
            using var sub = comparer.Compare(a, nudged);

            var faults = new List<string>();
            if (self.ChangedPixels != 0)
                faults.Add($"an image compared with itself reported {self.ChangedPixels} changed pixels (expected 0)");
            if (pair.ChangedPixels != ExpectedChangedPixels)
                faults.Add($"the shipped pair reported {pair.ChangedPixels} changed pixels (expected {ExpectedChangedPixels})");
            if (pair.TotalPixels != ExpectedTotalPixels)
                faults.Add($"the shipped pair reported {pair.TotalPixels} total pixels (expected {ExpectedTotalPixels})");
            if (sub.ChangedPixels != 0)
                faults.Add($"a sub-threshold difference reported {sub.ChangedPixels} changed pixels (expected 0 - the colour threshold is not being applied)");

            return faults.Count == 0
                ? new CommissioningLayer(1, name, LayerOutcome.Passed,
                    $"self 0, pair {pair.ChangedPixels}/{pair.TotalPixels} ({pair.DiffPercentage:P4}), sub-threshold 0", true)
                : new CommissioningLayer(1, name, LayerOutcome.Failed, string.Join("; ", faults), true);
        }
        catch (Exception ex)
        {
            return new CommissioningLayer(1, name, LayerOutcome.Failed,
                $"{ex.GetType().Name}: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Layer 2 — two captures taken back-to-back in one session are identical.
    /// </summary>
    /// <param name="firstPng">First capture.</param>
    /// <param name="secondPng">Second capture, taken immediately after.</param>
    /// <returns>The layer result.</returns>
    /// <remarks>
    /// <para>
    /// The quiet star of ruling 7A. It needs no shipped baseline, and it is the only check that
    /// says whether app baselines could ever travel between machines: if this machine cannot
    /// reproduce its <i>own</i> frame seconds apart, then no baseline from anywhere will ever
    /// match here, and every pixel comparison is noise.
    /// </para>
    /// <para>
    /// Fatal, and the strictest of the three: the tolerance is zero. Two frames of the same
    /// unchanged scene are either identical or the capture path is nondeterministic, and
    /// "nearly identical" is precisely the state that produces flaky suites nobody trusts.
    /// </para>
    /// </remarks>
    public static CommissioningLayer CheckRepeatable(string firstPng, string secondPng)
    {
        const string name = "repeatable";
        try
        {
            if (!File.Exists(firstPng) || !File.Exists(secondPng))
            {
                return new CommissioningLayer(2, name, LayerOutcome.NotRun,
                    "the app did not produce two captures - layer not attempted. The usual cause is that " +
                    "the Canary agent is not registered in the host, so the app starts and never connects: " +
                    "check `canary doctor` for the agent package before reading this as a harness fault.",
                    true, ContentFault: true);
            }

            using var first = Image.Load<Rgba32>(firstPng);
            using var second = Image.Load<Rgba32>(secondPng);

            if (first.Width != second.Width || first.Height != second.Height)
            {
                return new CommissioningLayer(2, name, LayerOutcome.Failed,
                    $"two captures of the same scene differ in SIZE: {first.Width}x{first.Height} then " +
                    $"{second.Width}x{second.Height} - the viewport is not being sized deterministically", true);
            }

            using var cmp = new PixelDiffComparer().Compare(first, second);
            return cmp.ChangedPixels == 0
                ? new CommissioningLayer(2, name, LayerOutcome.Passed,
                    $"two back-to-back captures identical ({cmp.TotalPixels} px, {first.Width}x{first.Height})", true)
                : new CommissioningLayer(2, name, LayerOutcome.Failed,
                    $"two captures of an unchanged scene differ by {cmp.ChangedPixels} px ({cmp.DiffPercentage:P4}) - " +
                    "capture is not repeatable on this machine, so no pixel baseline can ever match here", true);
        }
        catch (Exception ex)
        {
            return new CommissioningLayer(2, name, LayerOutcome.Failed, $"{ex.GetType().Name}: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Layer 3 — a reference captured elsewhere matches here. <b>Informational, not fatal.</b>
    /// </summary>
    /// <param name="shippedPng">A capture made on another machine and shipped with the content.</param>
    /// <param name="capturedPng">A capture made here.</param>
    /// <param name="tolerance">Diff fraction allowed before it is reported as a mismatch.</param>
    /// <returns>The layer result.</returns>
    /// <remarks>
    /// <para>
    /// Deliberately not fatal. This asks a different question from layers 1 and 2: not "does
    /// the harness work" but "can a pixel baseline made on another machine be believed on this
    /// one?" A machine that fails it can still test perfectly well — it simply has to approve
    /// its own baselines, or use VLM mode, which the operator already prefers for most content.
    /// </para>
    /// <para>
    /// Treating a failure here as "the harness is broken" would report on a question the run
    /// never asked, and would ground a machine that is in fact fine. Its real value is as
    /// evidence for the campaign's open question about whether baselines travel at all.
    /// </para>
    /// </remarks>
    public static CommissioningLayer CheckShippedReference(string shippedPng, string capturedPng, double tolerance = 0.02)
    {
        const string name = "reference";
        try
        {
            if (!File.Exists(shippedPng))
            {
                return new CommissioningLayer(3, name, LayerOutcome.NotRun,
                    $"no shipped reference capture at {shippedPng} - nothing to compare against",
                    false, ContentFault: true);
            }
            if (!File.Exists(capturedPng))
            {
                return new CommissioningLayer(3, name, LayerOutcome.NotRun,
                    $"the app did not produce a capture at {capturedPng} - layer not attempted", false);
            }

            using var shipped = Image.Load<Rgba32>(shippedPng);
            using var here = Image.Load<Rgba32>(capturedPng);

            if (shipped.Width != here.Width || shipped.Height != here.Height)
            {
                return new CommissioningLayer(3, name, LayerOutcome.Failed,
                    $"the shipped reference is {shipped.Width}x{shipped.Height} but this machine captured " +
                    $"{here.Width}x{here.Height} - baselines cannot travel between these two machines at all", false);
            }

            using var cmp = new PixelDiffComparer().Compare(shipped, here, tolerance: tolerance);
            return cmp.Passed
                ? new CommissioningLayer(3, name, LayerOutcome.Passed,
                    $"a reference captured elsewhere matches here ({cmp.DiffPercentage:P4} diff)", false)
                : new CommissioningLayer(3, name, LayerOutcome.Failed,
                    $"a reference captured elsewhere differs by {cmp.DiffPercentage:P4} - pixel baselines do NOT " +
                    "travel to this machine; approve baselines here, or use VLM mode", false);
        }
        catch (Exception ex)
        {
            return new CommissioningLayer(3, name, LayerOutcome.Failed, $"{ex.GetType().Name}: {ex.Message}", false);
        }
    }
}
