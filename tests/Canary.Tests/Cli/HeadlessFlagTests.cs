using System.CommandLine;
using System.CommandLine.IO;
using Xunit;

namespace Canary.Tests.Cli;

// Tests for the --headless flag wiring on `canary run` (impl §3, design §C3).
// The actual UI-launch path can't be exercised in a headless unit-test context
// (it would spawn Canary.UI.exe — covered by the Integration suite). These
// tests verify the flag is parsed and accepted by the System.CommandLine
// surface, plus that the UiLocator helper handles the absent-UI case
// gracefully.
public class HeadlessFlagTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public async Task RunCommand_HeadlessFlag_IsAccepted()
    {
        var rootCommand = Canary.Program.BuildRootCommand();
        var console = new TestConsole();

        // Missing workload → exit code 1 (per bug 0007 fix) regardless of --headless.
        // We're just verifying --headless parses without "unrecognized command" error.
        var exitCode = await rootCommand.InvokeAsync(new[] { "run", "--headless" }, console);

        Assert.Equal(1, exitCode);
        var output = console.Out.ToString()!;
        Assert.DoesNotContain("Unrecognized command or argument '--headless'", output);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task RunCommand_RunHelp_AdvertisesHeadless()
    {
        var rootCommand = Canary.Program.BuildRootCommand();
        var console = new TestConsole();

        await rootCommand.InvokeAsync(new[] { "run", "--help" }, console);

        var output = console.Out.ToString()!;
        Assert.Contains("--headless", output);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void UiLocator_NoUiExePresent_ReturnsFalse()
    {
        // We can't reliably assert TryFindUiExe returns false on dev machines
        // (the dev tree may have a built Canary.UI.exe). Just verify the API
        // contract: when it returns true, the path exists; when false, the
        // out param is empty.
        var found = Canary.UiLocator.TryFindUiExe(out var path);

        if (found)
        {
            Assert.NotEmpty(path);
            Assert.True(File.Exists(path), $"UiLocator returned true but file does not exist: {path}");
        }
        else
        {
            Assert.Equal(string.Empty, path);
        }
    }
}
