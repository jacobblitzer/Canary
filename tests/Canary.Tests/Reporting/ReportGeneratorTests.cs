using System.Xml.Linq;
using Canary.Orchestration;
using Canary.Reporting;
using Xunit;

namespace Canary.Tests.Reporting;

[Trait("Category", "Unit")]
public class ReportGeneratorTests
{
    private static SuiteResult CreateSampleSuite(bool includeFailed = false)
    {
        var suite = new SuiteResult();

        var passedTest = new TestResult
        {
            TestName = "sculpt-undo",
            Workload = "pigment",
            Status = TestStatus.Passed,
            Duration = TimeSpan.FromSeconds(2.5),
            CheckpointResults =
            {
                new CheckpointResult
                {
                    Name = "after_stroke",
                    Status = TestStatus.Passed,
                    DiffPercentage = 0.003,
                    Tolerance = 0.02,
                    SsimScore = 0.998
                },
                new CheckpointResult
                {
                    Name = "after_undo",
                    Status = TestStatus.Passed,
                    DiffPercentage = 0.0,
                    Tolerance = 0.01,
                    SsimScore = 1.0
                }
            }
        };
        suite.TestResults.Add(passedTest);

        if (includeFailed)
        {
            var failedTest = new TestResult
            {
                TestName = "rotate-view",
                Workload = "pigment",
                Status = TestStatus.Failed,
                Duration = TimeSpan.FromSeconds(1.8),
                CheckpointResults =
                {
                    new CheckpointResult
                    {
                        Name = "after_rotate",
                        Status = TestStatus.Failed,
                        DiffPercentage = 0.052,
                        Tolerance = 0.02,
                        SsimScore = 0.91
                    }
                }
            };
            suite.TestResults.Add(failedTest);
        }

        return suite;
    }

    [Fact]
    public void HtmlReportGenerator_SingleTest_ProducesValidHtml()
    {
        var suite = CreateSampleSuite();

        var html = HtmlReportGenerator.Generate(suite, "pigment");

        Assert.Contains("<html", html);
        Assert.Contains("sculpt-undo", html);
        Assert.Contains("PASSED", html);
    }

    [Fact]
    public void HtmlReportGenerator_FailedTest_ShowsRed()
    {
        var suite = CreateSampleSuite(includeFailed: true);

        var html = HtmlReportGenerator.Generate(suite, "pigment");

        Assert.Contains("<html", html);
        Assert.Contains("rotate-view", html);
        Assert.Contains("FAILED", html);
        // Failed badge uses fail class which has red styling
        Assert.Contains("status-badge fail", html);
        // Failed tests should appear before passed (sorted by status)
        int failPos = html.IndexOf("rotate-view");
        int passPos = html.IndexOf("sculpt-undo");
        Assert.True(failPos < passPos, "Failed test should appear before passed test");
    }

    [Fact]
    public void JUnitReportGenerator_ProducesValidXml()
    {
        var suite = CreateSampleSuite(includeFailed: true);

        var xml = JUnitReportGenerator.Generate(suite, "pigment");

        var doc = XDocument.Parse(xml);
        var testsuite = doc.Root!;
        Assert.Equal("testsuite", testsuite.Name.LocalName);
        Assert.Equal("2", testsuite.Attribute("tests")!.Value);
        Assert.Equal("1", testsuite.Attribute("failures")!.Value);

        var testcases = testsuite.Elements("testcase").ToList();
        Assert.Equal(2, testcases.Count);

        // Check the failed test has a <failure> element
        var failedCase = testcases.First(tc => tc.Attribute("name")!.Value == "rotate-view");
        var failure = failedCase.Element("failure");
        Assert.NotNull(failure);
        Assert.Contains("tolerance", failure.Attribute("message")!.Value, StringComparison.OrdinalIgnoreCase);

        // Check the passed test has no <failure>
        var passedCase = testcases.First(tc => tc.Attribute("name")!.Value == "sculpt-undo");
        Assert.Null(passedCase.Element("failure"));
    }
}
