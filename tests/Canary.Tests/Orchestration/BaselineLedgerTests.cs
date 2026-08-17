using Canary.Config;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

/// <summary>
/// Deployment campaign Phase 2b — the baseline ledger.
/// </summary>
/// <remarks>
/// The ledger exists because every other candidate guard consults something living
/// inside the directory whose location is the variable under change, and is therefore
/// blind to exactly the class it was written for. These tests pin the properties that
/// make it not blind: it fails closed, it distinguishes absence from emptiness, and its
/// row count is a gate rather than a statistic.
/// </remarks>
public class BaselineLedgerTests
{
    private static string NewRig()
    {
        var d = Path.Combine(Path.GetTempPath(), "canary-ledger-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path.Combine(d, "w", "tests"));
        Directory.CreateDirectory(Path.Combine(d, "w", "results"));
        return d;
    }

    private static void WriteTest(string root, string name, params (string Checkpoint, string Mode)[] cps)
    {
        var items = string.Join(",", cps.Select(c =>
            $"{{ \"name\": \"{c.Checkpoint}\", \"mode\": \"{c.Mode}\" }}"));
        File.WriteAllText(Path.Combine(root, "w", "tests", name + ".json"),
            $"{{ \"name\": \"{name}\", \"workload\": \"w\", \"checkpoints\": [{items}] }}");
    }

    /// <summary>Puts a PNG-ish file where a baseline would live.</summary>
    private static string WriteBaseline(string root, string? scope, string test, string checkpoint, string body)
    {
        var dir = scope is null
            ? Path.Combine(root, "w", "results", test, "baselines")
            : Path.Combine(root, "w", "results", scope, test, "baselines");
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, checkpoint + ".png");
        File.WriteAllText(p, body);
        return p;
    }

    // --- fail closed -------------------------------------------------------

    [Trait("Category", "Unit")]
    [Fact]
    public void LoadRequired_Throws_WhenAbsent()
    {
        var root = NewRig();
        var ex = Assert.Throws<FileNotFoundException>(() => BaselineLedger.LoadRequired(root, "w"));
        Assert.Contains("absent ledger is not an empty ledger", ex.Message);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void LoadRequired_Throws_WhenEmptyFile()
    {
        var root = NewRig();
        File.WriteAllText(BaselineLedger.PathFor(root, "w"), "   ");
        Assert.Throws<InvalidDataException>(() => BaselineLedger.LoadRequired(root, "w"));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void LoadRequired_Throws_WhenCorrupt()
    {
        var root = NewRig();
        File.WriteAllText(BaselineLedger.PathFor(root, "w"), "{");
        Assert.Throws<InvalidDataException>(() => BaselineLedger.LoadRequired(root, "w"));
    }

    // The counter-mutation. Empty BY DECLARATION is legal and reviewable; empty by
    // absence is an error. If this ever starts throwing, the guard has become too strict
    // to live with and someone will switch it off - which is worse than not having it.
    [Trait("Category", "Unit")]
    [Fact]
    public void LoadRequired_Succeeds_OnAnExplicitlyEmptyRowSet()
    {
        var root = NewRig();
        new BaselineLedger { Workload = "w" }.Save(root);

        var led = BaselineLedger.LoadRequired(root, "w");

        Assert.Empty(led.Rows);
        Assert.True(led.Verify(root, LedgerLayout.Dual).IsSatisfied);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Save_SortsRows_SoAChangeIsAReadableDiff()
    {
        var root = NewRig();
        var led = new BaselineLedger
        {
            Workload = "w",
            Rows =
            {
                new BaselineRow { Test = "b-test", Checkpoint = "front" },
                new BaselineRow { Test = "a-test", Checkpoint = "top" },
                new BaselineRow { Test = "a-test", Checkpoint = "front" },
            },
        };
        led.Save(root);

        var rows = BaselineLedger.LoadRequired(root, "w").Rows;
        Assert.Equal(new[] { "a-test/front", "a-test/top", "b-test/front" },
            rows.Select(r => $"{r.Test}/{r.Checkpoint}"));
    }

    // --- the arming rule ---------------------------------------------------

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("capture", false)]
    [InlineData("CAPTURE", false)]
    [InlineData("none", false)]
    [InlineData("off", false)]
    [InlineData("vlm", false)]
    [InlineData("pixel-diff", true)]
    [InlineData("", true)]
    public void Arming_ClassifiesEveryModeAlias(string mode, bool armed)
    {
        var cp = new TestCheckpoint { Name = "front", Mode = mode };
        Assert.Equal(armed, CheckpointArming.IsArmedForPixelDiff(cp));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Scan_CountsArmedCaptureAndVlmSeparately()
    {
        var root = NewRig();
        WriteTest(root, "t", ("a", "pixel-diff"), ("b", "capture"), ("c", "vlm"), ("d", "off"));
        WriteBaseline(root, null, "t", "a", "one");

        var scan = BaselineLedger.Scan(root, "w", LedgerLayout.Dual);

        Assert.Equal(1, scan.Armed);
        Assert.Equal(2, scan.CaptureOnly);   // capture + off
        Assert.Equal(1, scan.Vlm);
        Assert.Single(scan.Rows);
    }

    // THE trap, as a unit test: locking with the post-cutover resolver silently produces
    // a SHORT ledger, and verify is green on it because every row it contains resolves.
    // Live, this is 40 penumbra rows instead of 93, leaving 53 checkpoints unprotected.
    [Trait("Category", "Unit")]
    [Fact]
    public void Scan_FlatLayout_CannotSeeNestedBaselines_WhichIsWhyRowCountIsAGate()
    {
        var root = NewRig();
        WriteTest(root, "t", ("flatcp", "pixel-diff"), ("nestedcp", "pixel-diff"));
        WriteBaseline(root, null, "t", "flatcp", "one");
        WriteBaseline(root, "some-suite", "t", "nestedcp", "two");

        var dual = BaselineLedger.Scan(root, "w", LedgerLayout.Dual);
        var flat = BaselineLedger.Scan(root, "w", LedgerLayout.Flat);

        Assert.Equal(2, dual.Rows.Count);
        Assert.Equal(1, dual.ResolvedFlat);
        Assert.Equal(1, dual.ResolvedNested);

        Assert.Single(flat.Rows);                    // the short ledger
        Assert.Equal(1, flat.Unresolved);

        // and the short one VERIFIES CLEAN - the reason a row count has to gate the write
        var shortLedger = new BaselineLedger { Workload = "w", Rows = flat.Rows.ToList() };
        Assert.True(shortLedger.Verify(root, LedgerLayout.Flat).IsSatisfied);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Scan_ReportsUnparsableTests_RatherThanSkippingThemSilently()
    {
        var root = NewRig();
        WriteTest(root, "good", ("a", "pixel-diff"));
        File.WriteAllText(Path.Combine(root, "w", "tests", "bad.json"), "{ \"name\": \"bad\", ");

        var scan = BaselineLedger.Scan(root, "w", LedgerLayout.Dual);

        Assert.Single(scan.UnparsableTests);
        Assert.Contains("bad.json", scan.UnparsableTests[0]);
    }

    // --- verify: presence is hard, content is soft -------------------------

    [Trait("Category", "Unit")]
    [Fact]
    public void Verify_MissingBaseline_IsAHardFailure()
    {
        var root = NewRig();
        WriteTest(root, "t", ("front", "pixel-diff"));
        var png = WriteBaseline(root, null, "t", "front", "one");
        var scan = BaselineLedger.Scan(root, "w", LedgerLayout.Dual);
        var led = new BaselineLedger { Workload = "w", Rows = scan.Rows.ToList() };

        File.Delete(png);
        var v = led.Verify(root, LedgerLayout.Dual);

        Assert.False(v.IsSatisfied);
        Assert.Single(v.Missing);
        Assert.Contains("t/front", v.Missing[0]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Verify_ChangedBytes_IsASoftWarning_NotAFailure()
    {
        var root = NewRig();
        WriteTest(root, "t", ("front", "pixel-diff"));
        var png = WriteBaseline(root, null, "t", "front", "one");
        var scan = BaselineLedger.Scan(root, "w", LedgerLayout.Dual);
        var led = new BaselineLedger { Workload = "w", Rows = scan.Rows.ToList() };

        File.WriteAllText(png, "re-blessed");
        var v = led.Verify(root, LedgerLayout.Dual);

        // Re-approving is legitimate and shows up as a git diff on the ledger. Blocking
        // it would make approve-then-run painful enough to route around.
        Assert.True(v.IsSatisfied);
        Assert.Single(v.Changed);
        Assert.Equal(0, v.Ok);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Scan_RecordsTheHashAndApprovalTimeOfTheResolvedFile()
    {
        var root = NewRig();
        WriteTest(root, "t", ("front", "pixel-diff"));
        var png = WriteBaseline(root, null, "t", "front", "pixels");

        var row = Assert.Single(BaselineLedger.Scan(root, "w", LedgerLayout.Dual).Rows);

        Assert.Equal(BaselineLedger.HashFile(png), row.Sha256);
        Assert.EndsWith("Z", row.ApprovedUtc);
        Assert.Equal("pixel-diff", row.Mode);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Find_IsCaseInsensitive_AndReturnsNullWhenUnledgered()
    {
        var led = new BaselineLedger
        {
            Workload = "w",
            Rows = { new BaselineRow { Test = "t", Checkpoint = "front" } },
        };

        Assert.NotNull(led.Find("T", "FRONT"));
        Assert.Null(led.Find("t", "back"));
    }
}
