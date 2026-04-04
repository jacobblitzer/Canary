using System.CommandLine;
using System.CommandLine.IO;
using Canary.Orchestration;
using Canary.UI;
using Xunit;

namespace Canary.Tests.UI;

public class IntegrationTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Cli_Help_StillWorks_AfterCoreExtraction()
    {
        var rootCommand = Program.BuildRootCommand();
        var console = new TestConsole();

        var exitCode = await rootCommand.InvokeAsync(Array.Empty<string>(), console);

        var output = console.Out.ToString()!;
        Assert.Contains("run", output);
        Assert.Contains("approve", output);
        Assert.Contains("report", output);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TestRunner_UsesCore_WithITestLogger()
    {
        // Verify TestRunner accepts ITestLogger (Core type) and can be constructed
        var logger = new TestLogger();
        var pm = new ProcessManager();
        var runner = new TestRunner(pm, "workloads", logger);
        Assert.NotNull(runner);
        pm.KillAll();
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void MainForm_CanBeConstructed()
    {
        // Verify MainForm can be instantiated without errors
        // (does not require visible display — constructor only)
        using var form = new MainForm();
        Assert.Contains("Canary", form.Text);
        Assert.True(form.MinimumSize.Width >= 1024);
        Assert.True(form.MinimumSize.Height >= 768);
    }

    private sealed class TestLogger : Canary.ITestLogger
    {
        public bool Verbose => false;
        public void Log(string message) { }
        public void LogStatus(string symbol, string message, Canary.TestStatusLevel level) { }
        public void LogSummary(string message) { }
    }
}
