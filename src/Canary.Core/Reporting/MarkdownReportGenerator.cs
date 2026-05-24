using System.Text;
using Canary.Orchestration;

namespace Canary.Reporting;

// Phase 3 / §C2 — Claude-readable REPORT.md per run, written alongside
// the existing result.json + report.html in the per-run dir
// (runs/<timestamp>/). Stable section structure (Verdict / Checkpoints
// table / Errors / Console tail / Network failures / Agent actions /
// Files) so the Phase 6 MCP server's `get_run_report` tool + the Phase 7
// PastRuns panel can both parse it.
//
// Cross-link convention: relative Markdown links. REPORT.md lives at
// `runs/<timestamp>/REPORT.md`. Baselines, candidates, diffs, and
// composite are at the test level (one directory up from runs/, two up
// from REPORT.md's own location): `../../baselines/<checkpoint>.png`.
// (Phase 3 keeps per-run-overwriting for the images themselves — only
// result.json + REPORT.md go per-run. A future phase can move images
// under per-run dirs if PastRuns browsing needs historical image
// preservation.)
public static class MarkdownReportGenerator
{
    // Caps from design §C2 + §0.4 defaults.
    private const int ConsoleTailLines = 50;
    private const int InputTailEvents = 20;

    public sealed class ReportOptions
    {
        public string? RunId { get; init; }
        public string? WorkloadDisplayName { get; init; }
        public string? WorkloadAgentType { get; init; }
        public string Mode { get; init; } = "pixel-diff";
        public DateTime? StartedUtc { get; init; }
        public DateTime? FinishedUtc { get; init; }

        // Optional pointer to the per-suite telemetry NDJSON; rendered as a
        // relative link from the REPORT.md location. Null = no "Full console
        // log" footer link.
        public string? TelemetryNdjsonRelativePath { get; init; }
    }

    public static string Generate(TestResult result, ReportOptions options)
    {
        var sb = new StringBuilder();

        var verdict = VerdictLabel(result.Status);
        sb.Append("# Canary run — ").Append(result.TestName).Append(" — ").AppendLine(verdict);
        sb.AppendLine();

        sb.Append("> Run ID: `").Append(options.RunId ?? "(none)").AppendLine("`  ");
        sb.Append("> Workload: ").Append(options.WorkloadDisplayName ?? result.Workload);
        if (!string.IsNullOrEmpty(options.WorkloadAgentType))
            sb.Append(" (").Append(options.WorkloadAgentType).Append(")");
        sb.Append(" | Mode: ").Append(options.Mode);
        sb.Append(" | Duration: ").AppendFormat("{0:0.0}", result.Duration.TotalSeconds).AppendLine("s  ");
        if (options.StartedUtc.HasValue)
            sb.Append("> Started: ").Append(options.StartedUtc.Value.ToString("o"));
        if (options.FinishedUtc.HasValue)
            sb.Append(" | Finished: ").Append(options.FinishedUtc.Value.ToString("o"));
        sb.AppendLine();
        sb.AppendLine();

        sb.Append("## Verdict").AppendLine();
        sb.Append("**").Append(verdict).Append("** — ").AppendLine(SummariseVerdict(result));
        sb.AppendLine();

        sb.AppendLine("## Checkpoints");
        sb.AppendLine();
        sb.AppendLine("| # | Name | Status | Diff % | SSIM | Links |");
        sb.AppendLine("|---|------|--------|--------|------|-------|");
        for (int i = 0; i < result.CheckpointResults.Count; i++)
        {
            var cp = result.CheckpointResults[i];
            sb.Append("| ").Append(i + 1).Append(" | ");
            sb.Append(EscapeCell(cp.Name)).Append(" | ");
            sb.Append(VerdictLabel(cp.Status)).Append(" | ");
            sb.AppendFormat("{0:0.0}%", cp.DiffPercentage).Append(" | ");
            sb.AppendFormat("{0:0.000}", cp.SsimScore).Append(" | ");
            sb.Append(RenderCheckpointLinks(cp));
            sb.AppendLine(" |");
        }
        sb.AppendLine();

        if (HasAnyError(result))
        {
            sb.AppendLine("## Errors and warnings");
            foreach (var cp in result.CheckpointResults)
            {
                if (!string.IsNullOrEmpty(cp.ErrorMessage))
                    sb.Append("- `").Append(cp.Name).Append("`: ").AppendLine(cp.ErrorMessage);
            }
            if (!string.IsNullOrEmpty(result.ErrorMessage))
                sb.Append("- ").AppendLine(result.ErrorMessage);
            sb.AppendLine();
        }

        if (HasAnyVlm(result))
        {
            sb.AppendLine("## VLM evaluations");
            foreach (var cp in result.CheckpointResults)
            {
                if (string.IsNullOrEmpty(cp.VlmReasoning) && string.IsNullOrEmpty(cp.VlmDescription)) continue;
                sb.Append("### ").AppendLine(cp.Name);
                if (!string.IsNullOrEmpty(cp.VlmDescription))
                {
                    sb.AppendLine("**Prompt:**");
                    sb.AppendLine();
                    sb.Append("> ").AppendLine(cp.VlmDescription);
                    sb.AppendLine();
                }
                if (!string.IsNullOrEmpty(cp.VlmReasoning))
                {
                    sb.AppendLine("**Reasoning:**");
                    sb.AppendLine();
                    sb.Append("> ").AppendLine(cp.VlmReasoning);
                    sb.AppendLine();
                }
                sb.AppendFormat("Confidence: {0:0.000}", cp.VlmConfidence).AppendLine();
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Files");
        sb.AppendLine("- `result.json` — typed verdict (input to UI / CI tooling)");
        if (!string.IsNullOrEmpty(options.TelemetryNdjsonRelativePath))
            sb.Append("- [`").Append(options.TelemetryNdjsonRelativePath).Append("`](").Append(options.TelemetryNdjsonRelativePath).AppendLine(") — full event stream (input to Claude / MCP server)");
        sb.AppendLine("- `../composite.png` — baseline | candidate | diff strips (if generated; overwrites per run)");
        sb.AppendLine("- `../candidates/`, `../diffs/`, `../baselines/` — per-checkpoint images at the test level (Phase 3 keeps these flat; rerun overwrites)");
        sb.AppendLine();

        return sb.ToString();
    }

    public static async Task SaveAsync(TestResult result, ReportOptions options, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(filePath, Generate(result, options)).ConfigureAwait(false);
    }

    private static string VerdictLabel(TestStatus s) => s switch
    {
        TestStatus.Passed => "PASS",
        TestStatus.Failed => "FAIL",
        TestStatus.Crashed => "CRASH",
        TestStatus.New => "NEW",
        _ => s.ToString().ToUpperInvariant(),
    };

    private static string SummariseVerdict(TestResult result)
    {
        var total = result.CheckpointResults.Count;
        if (total == 0)
        {
            return result.Status switch
            {
                TestStatus.Passed => "no checkpoints; passed.",
                TestStatus.Crashed => $"crashed before any checkpoint{(string.IsNullOrEmpty(result.ErrorMessage) ? "." : $": {result.ErrorMessage}")}",
                _ => $"no checkpoints; {result.Status}.",
            };
        }
        var failed = result.CheckpointResults.Count(c => c.Status == TestStatus.Failed);
        var crashed = result.CheckpointResults.Count(c => c.Status == TestStatus.Crashed);
        var passed = result.CheckpointResults.Count(c => c.Status == TestStatus.Passed);
        var @new = result.CheckpointResults.Count(c => c.Status == TestStatus.New);
        var parts = new List<string>();
        if (passed > 0) parts.Add($"{passed} passed");
        if (failed > 0) parts.Add($"{failed} failed");
        if (crashed > 0) parts.Add($"{crashed} crashed");
        if (@new > 0) parts.Add($"{@new} new");
        return $"{string.Join(", ", parts)} of {total} checkpoint{(total == 1 ? string.Empty : "s")}.";
    }

    private static string RenderCheckpointLinks(CheckpointResult cp)
    {
        // Per Phase 3 layout: REPORT.md lives at runs/<timestamp>/; images
        // are one level up at the test dir. Links use ../<dir>/.
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(cp.BaselinePath))
            parts.Add($"[baseline](../baselines/{cp.Name}.png)");
        if (!string.IsNullOrEmpty(cp.CandidatePath))
            parts.Add($"[candidate](../candidates/{cp.Name}.png)");
        if (!string.IsNullOrEmpty(cp.DiffImagePath))
            parts.Add($"[diff](../diffs/{cp.Name}.png)");
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    private static bool HasAnyError(TestResult result)
        => !string.IsNullOrEmpty(result.ErrorMessage)
           || result.CheckpointResults.Any(c => !string.IsNullOrEmpty(c.ErrorMessage));

    private static bool HasAnyVlm(TestResult result)
        => result.CheckpointResults.Any(c => !string.IsNullOrEmpty(c.VlmReasoning) || !string.IsNullOrEmpty(c.VlmDescription));

    private static string EscapeCell(string text)
        => text.Replace("|", "\\|").Replace("\n", " ");
}
