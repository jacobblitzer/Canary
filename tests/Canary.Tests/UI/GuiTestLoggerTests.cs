using Canary;
using Canary.UI.Services;
using Xunit;

namespace Canary.Tests.UI;

public class GuiTestLoggerTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void GuiTestLogger_Log_FiresMessageLoggedEvent()
    {
        // Use a dummy control for synchronization (STA thread not needed for direct invoke)
        using var form = new Form();
        var logger = new GuiTestLogger(form, verbose: true);

        string? captured = null;
        logger.MessageLogged += msg => captured = msg;

        logger.Log("test message");

        Assert.Equal("test message", captured);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void GuiTestLogger_LogStatus_FiresStatusLoggedEvent()
    {
        using var form = new Form();
        var logger = new GuiTestLogger(form, verbose: false);

        string? capturedSymbol = null;
        string? capturedMessage = null;
        TestStatusLevel? capturedLevel = null;
        logger.StatusLogged += (symbol, msg, level) =>
        {
            capturedSymbol = symbol;
            capturedMessage = msg;
            capturedLevel = level;
        };

        logger.LogStatus("PASS", "test1 (0.0% max diff)", TestStatusLevel.Pass);

        Assert.Equal("PASS", capturedSymbol);
        Assert.Contains("test1", capturedMessage);
        Assert.Equal(TestStatusLevel.Pass, capturedLevel);
    }
}
