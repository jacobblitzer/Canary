using Canary.Orchestration;
using Canary.Reporting;
using Xunit;

namespace Canary.Tests.Reporting;

// Tests for Phase 3 / §C2 MarkdownReportGenerator. The wire format is the
// contract — the Phase 6 MCP server's `get_run_report` tool, the Phase 7
// PastRuns panel, and Claude itself all read it. Snapshot-style tests of
// the section structure + key strings.
public class MarkdownReportGeneratorTests
{
    private static MarkdownReportGenerator.ReportOptions DefaultOptions(string runId = "20260524-142300-a3f1")
        => new()
        {
            RunId = runId,
            WorkloadDisplayName = "Qualia",
            WorkloadAgentType = "qualia-cdp",
            Mode = "pixel-diff",
            StartedUtc = new DateTime(2026, 5, 24, 14, 23, 0, DateTimeKind.Utc),
            FinishedUtc = new DateTime(2026, 5, 24, 14, 23, 12, DateTimeKind.Utc),
        };

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_HeaderIncludesTestNameAndVerdict()
    {
        var result = new TestResult { TestName = "diag-pencil-baseline", Workload = "qualia", Status = TestStatus.Failed };
        var md = MarkdownReportGenerator.Generate(result, DefaultOptions());

        Assert.Contains("# Canary run — diag-pencil-baseline — FAIL", md);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_AllRequiredSectionsPresent()
    {
        var result = new TestResult
        {
            TestName = "t",
            Workload = "qualia",
            Status = TestStatus.Passed,
            CheckpointResults = { new CheckpointResult { Name = "init" } }
        };
        var md = MarkdownReportGenerator.Generate(result, DefaultOptions());

        Assert.Contains("## Verdict", md);
        Assert.Contains("## Checkpoints", md);
        Assert.Contains("## Files", md);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_RunIdAppearsInHeader()
    {
        var result = new TestResult { TestName = "t", Workload = "qualia", Status = TestStatus.Passed };
        var md = MarkdownReportGenerator.Generate(result, DefaultOptions("custom-run-id-xyz"));

        Assert.Contains("custom-run-id-xyz", md);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_CheckpointLinks_UseRelativePerRunPaths()
    {
        var result = new TestResult
        {
            TestName = "t",
            Workload = "qualia",
            Status = TestStatus.Failed,
            CheckpointResults =
            {
                new CheckpointResult
                {
                    Name = "init",
                    Status = TestStatus.Failed,
                    BaselinePath = "/abs/baselines/init.png",
                    CandidatePath = "/abs/candidates/init.png",
                    DiffImagePath = "/abs/diffs/init.png",
                    DiffPercentage = 12.4,
                }
            }
        };
        var md = MarkdownReportGenerator.Generate(result, DefaultOptions());

        // Relative to <test>/runs/<timestamp>/REPORT.md — images live one level up.
        Assert.Contains("[baseline](../baselines/init.png)", md);
        Assert.Contains("[candidate](../candidates/init.png)", md);
        Assert.Contains("[diff](../diffs/init.png)", md);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_ErrorsSection_OnlyAppearsWhenErrorsPresent()
    {
        var passResult = new TestResult { TestName = "t", Workload = "qualia", Status = TestStatus.Passed };
        Assert.DoesNotContain("## Errors and warnings", MarkdownReportGenerator.Generate(passResult, DefaultOptions()));

        var failResult = new TestResult
        {
            TestName = "t",
            Workload = "qualia",
            Status = TestStatus.Failed,
            CheckpointResults = { new CheckpointResult { Name = "init", ErrorMessage = "diff exceeded tolerance" } }
        };
        var md = MarkdownReportGenerator.Generate(failResult, DefaultOptions());
        Assert.Contains("## Errors and warnings", md);
        Assert.Contains("diff exceeded tolerance", md);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_VlmSection_OnlyAppearsWhenVlmFieldsPresent()
    {
        var noVlm = new TestResult
        {
            TestName = "t",
            Workload = "qualia",
            Status = TestStatus.Passed,
            CheckpointResults = { new CheckpointResult { Name = "init" } }
        };
        Assert.DoesNotContain("## VLM evaluations", MarkdownReportGenerator.Generate(noVlm, DefaultOptions()));

        var withVlm = new TestResult
        {
            TestName = "t",
            Workload = "qualia",
            Status = TestStatus.Passed,
            CheckpointResults =
            {
                new CheckpointResult
                {
                    Name = "init",
                    VlmDescription = "a pencil-toon scene with three nodes",
                    VlmReasoning = "matches; pencil-toon visible",
                    VlmConfidence = 0.92,
                }
            }
        };
        var md = MarkdownReportGenerator.Generate(withVlm, DefaultOptions());
        Assert.Contains("## VLM evaluations", md);
        Assert.Contains("a pencil-toon scene with three nodes", md);
        Assert.Contains("matches; pencil-toon visible", md);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_TelemetryFooterLink_OnlyWhenPathProvided()
    {
        var result = new TestResult { TestName = "t", Workload = "qualia", Status = TestStatus.Passed };

        var withoutPath = MarkdownReportGenerator.Generate(result, new MarkdownReportGenerator.ReportOptions { RunId = "r" });
        Assert.DoesNotContain("telemetry.ndjson", withoutPath);

        var withPath = MarkdownReportGenerator.Generate(result, new MarkdownReportGenerator.ReportOptions
        {
            RunId = "r",
            TelemetryNdjsonRelativePath = "../../../telemetry.ndjson",
        });
        Assert.Contains("../../../telemetry.ndjson", withPath);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_CheckpointsTable_HasHeaderRow()
    {
        var result = new TestResult { TestName = "t", Workload = "qualia", Status = TestStatus.Passed };
        var md = MarkdownReportGenerator.Generate(result, DefaultOptions());

        // Standard markdown table header for the checkpoints section.
        Assert.Contains("| # | Name | Status | Diff % | SSIM | Links |", md);
    }
}
