using System.Xml.Linq;
using Canary.Orchestration;

namespace Canary.Reporting;

/// <summary>
/// Generates JUnit XML test reports for CI integration.
/// </summary>
public static class JUnitReportGenerator
{
    public static string Generate(SuiteResult suite, string workloadName)
    {
        var testsuite = new XElement("testsuite",
            new XAttribute("name", workloadName),
            new XAttribute("tests", suite.TestResults.Count),
            new XAttribute("failures", suite.Failed),
            new XAttribute("errors", suite.Crashed),
            new XAttribute("time", suite.TotalDuration.TotalSeconds.ToString("F3")),
            new XAttribute("timestamp", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")));

        foreach (var test in suite.TestResults)
        {
            var testcase = new XElement("testcase",
                new XAttribute("name", test.TestName),
                new XAttribute("classname", $"{workloadName}.{test.TestName}"),
                new XAttribute("time", test.Duration.TotalSeconds.ToString("F3")));

            if (test.Status == TestStatus.Failed)
            {
                var maxDiff = test.CheckpointResults.Count > 0
                    ? test.CheckpointResults.Max(c => c.DiffPercentage)
                    : 0;

                var failedCheckpoints = test.CheckpointResults
                    .Where(c => c.Status == TestStatus.Failed)
                    .Select(c => $"{c.Name}: {c.DiffPercentage:P2} diff (tolerance {c.Tolerance:P2})");

                testcase.Add(new XElement("failure",
                    new XAttribute("message", $"Visual diff exceeded tolerance ({maxDiff:P2})"),
                    new XAttribute("type", "VisualRegressionFailure"),
                    string.Join("\n", failedCheckpoints)));
            }
            else if (test.Status == TestStatus.Crashed)
            {
                testcase.Add(new XElement("error",
                    new XAttribute("message", test.ErrorMessage ?? "Application crashed"),
                    new XAttribute("type", "CrashError"),
                    test.ErrorMessage ?? ""));
            }
            else if (test.Status == TestStatus.New)
            {
                testcase.Add(new XElement("skipped",
                    new XAttribute("message", "No baseline exists. Run 'canary approve' to establish.")));
            }

            // Add checkpoint details as system-out
            if (test.CheckpointResults.Count > 0)
            {
                var details = test.CheckpointResults
                    .Select(c => $"[{c.Status}] {c.Name}: diff={c.DiffPercentage:P2}, ssim={c.SsimScore:F4}, tol={c.Tolerance:P2}");
                testcase.Add(new XElement("system-out", string.Join("\n", details)));
            }

            testsuite.Add(testcase);
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), testsuite);
        return doc.ToString();
    }

    public static async Task SaveAsync(SuiteResult suite, string workloadName, string outputPath)
    {
        var xml = Generate(suite, workloadName);
        await File.WriteAllTextAsync(outputPath, xml).ConfigureAwait(false);
    }
}
