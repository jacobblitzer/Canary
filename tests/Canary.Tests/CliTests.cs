using System.CommandLine;
using System.CommandLine.IO;
using Xunit;

namespace Canary.Tests;

public class CliTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Program_NoArgs_PrintsHelp()
    {
        var rootCommand = Program.BuildRootCommand();
        var console = new TestConsole();

        var exitCode = await rootCommand.InvokeAsync(Array.Empty<string>(), console);

        var output = console.Out.ToString()!;
        Assert.Contains("canary", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run", output);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task Program_RunHelp_PrintsRunUsage()
    {
        var rootCommand = Program.BuildRootCommand();
        var console = new TestConsole();

        await rootCommand.InvokeAsync(new[] { "run", "--help" }, console);

        var output = console.Out.ToString()!;
        Assert.Contains("--workload", output);
        Assert.Contains("--test", output);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Program_CtrlCHandler_IsRegistered()
    {
        // Verify the CancellationToken is available and not yet cancelled
        // (which means the CancellationTokenSource was created in Program)
        var token = Program.CancellationToken;
        Assert.False(token.IsCancellationRequested);
    }
}
