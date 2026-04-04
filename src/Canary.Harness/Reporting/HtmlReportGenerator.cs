using System.Text;
using Canary.Orchestration;

namespace Canary.Reporting;

/// <summary>
/// Generates a self-contained HTML report from test results.
/// Images are embedded as inline base64 PNGs.
/// </summary>
public static class HtmlReportGenerator
{
    public static string Generate(SuiteResult suite, string workloadName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<title>Canary Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(CssStyles);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Header
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine($"<h1>Canary Test Report</h1>");
        sb.AppendLine($"<p class=\"meta\">Workload: <strong>{Escape(workloadName)}</strong> | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        sb.AppendLine("<div class=\"summary\">");
        sb.AppendLine($"<span class=\"badge pass\">{suite.Passed} Passed</span>");
        if (suite.Failed > 0) sb.AppendLine($"<span class=\"badge fail\">{suite.Failed} Failed</span>");
        if (suite.Crashed > 0) sb.AppendLine($"<span class=\"badge crash\">{suite.Crashed} Crashed</span>");
        if (suite.New > 0) sb.AppendLine($"<span class=\"badge new\">{suite.New} New</span>");
        sb.AppendLine($"<span class=\"badge total\">Total: {suite.TestResults.Count}</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        // Sort: failed first, then crashed, then new, then passed
        var sorted = suite.TestResults
            .OrderBy(t => t.Status switch
            {
                TestStatus.Failed => 0,
                TestStatus.Crashed => 1,
                TestStatus.New => 2,
                TestStatus.Passed => 3,
                _ => 4
            })
            .ThenBy(t => t.TestName);

        foreach (var test in sorted)
        {
            RenderTest(sb, test);
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    public static async Task SaveAsync(SuiteResult suite, string workloadName, string outputPath)
    {
        var html = Generate(suite, workloadName);
        await File.WriteAllTextAsync(outputPath, html).ConfigureAwait(false);
    }

    private static void RenderTest(StringBuilder sb, TestResult test)
    {
        var statusClass = test.Status switch
        {
            TestStatus.Passed => "pass",
            TestStatus.Failed => "fail",
            TestStatus.Crashed => "crash",
            TestStatus.New => "new",
            _ => ""
        };

        var statusLabel = test.Status.ToString().ToUpperInvariant();

        sb.AppendLine($"<div class=\"test {statusClass}\">");
        sb.AppendLine($"<h2><span class=\"status-badge {statusClass}\">{statusLabel}</span> {Escape(test.TestName)}</h2>");

        if (test.ErrorMessage != null)
            sb.AppendLine($"<p class=\"error\">{Escape(test.ErrorMessage)}</p>");

        sb.AppendLine($"<p class=\"duration\">Duration: {test.Duration.TotalSeconds:F1}s</p>");

        // Checkpoints table
        if (test.CheckpointResults.Count > 0)
        {
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Checkpoint</th><th>Status</th><th>Diff %</th><th>Tolerance</th><th>SSIM</th></tr>");
            foreach (var cp in test.CheckpointResults)
            {
                var cpClass = cp.Status switch
                {
                    TestStatus.Passed => "pass",
                    TestStatus.Failed => "fail",
                    _ => "crash"
                };
                sb.AppendLine($"<tr class=\"{cpClass}\">");
                sb.AppendLine($"<td>{Escape(cp.Name)}</td>");
                sb.AppendLine($"<td><span class=\"status-badge {cpClass}\">{cp.Status.ToString().ToUpperInvariant()}</span></td>");
                sb.AppendLine($"<td>{cp.DiffPercentage:P2}</td>");
                sb.AppendLine($"<td>{cp.Tolerance:P2}</td>");
                sb.AppendLine($"<td>{cp.SsimScore:F4}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
        }

        // Composite image (base64 embedded)
        if (test.CompositeImagePath != null && File.Exists(test.CompositeImagePath))
        {
            try
            {
                var bytes = File.ReadAllBytes(test.CompositeImagePath);
                var base64 = Convert.ToBase64String(bytes);
                sb.AppendLine($"<img class=\"composite\" src=\"data:image/png;base64,{base64}\" alt=\"Composite for {Escape(test.TestName)}\">");
            }
            catch
            {
                sb.AppendLine("<p class=\"error\">Failed to embed composite image.</p>");
            }
        }

        sb.AppendLine("</div>");
    }

    private static string Escape(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private const string CssStyles = """
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #1a1a2e; color: #e0e0e0; padding: 20px; }
        .header { margin-bottom: 24px; padding: 20px; background: #16213e; border-radius: 8px; }
        .header h1 { font-size: 1.5em; margin-bottom: 8px; }
        .meta { color: #999; margin-bottom: 12px; }
        .summary { display: flex; gap: 8px; flex-wrap: wrap; }
        .badge { padding: 4px 12px; border-radius: 4px; font-weight: 600; font-size: 0.85em; }
        .badge.pass { background: #1b5e20; color: #a5d6a7; }
        .badge.fail { background: #b71c1c; color: #ef9a9a; }
        .badge.crash { background: #4a148c; color: #ce93d8; }
        .badge.new { background: #e65100; color: #ffcc80; }
        .badge.total { background: #333; color: #ccc; }
        .test { margin-bottom: 16px; padding: 16px; background: #16213e; border-radius: 8px; border-left: 4px solid #333; }
        .test.pass { border-left-color: #4caf50; }
        .test.fail { border-left-color: #f44336; }
        .test.crash { border-left-color: #9c27b0; }
        .test.new { border-left-color: #ff9800; }
        .test h2 { font-size: 1.1em; margin-bottom: 8px; }
        .status-badge { display: inline-block; padding: 2px 8px; border-radius: 3px; font-size: 0.75em; font-weight: 700; margin-right: 8px; }
        .status-badge.pass { background: #1b5e20; color: #a5d6a7; }
        .status-badge.fail { background: #b71c1c; color: #ef9a9a; }
        .status-badge.crash { background: #4a148c; color: #ce93d8; }
        .status-badge.new { background: #e65100; color: #ffcc80; }
        .error { color: #ef9a9a; margin: 4px 0; }
        .duration { color: #777; font-size: 0.85em; margin: 4px 0; }
        table { width: 100%; border-collapse: collapse; margin: 12px 0; }
        th { text-align: left; padding: 6px 10px; background: #0f3460; font-size: 0.85em; }
        td { padding: 6px 10px; border-bottom: 1px solid #2a2a4a; font-size: 0.85em; }
        .composite { max-width: 100%; margin-top: 12px; border-radius: 4px; border: 1px solid #333; }
        """;
}
