using Xunit;

namespace Canary.Tests.Orchestration;

public class TestLoggerTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void ConsoleTestLogger_Log_WritesTimestampedOutput()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var logger = new ConsoleTestLogger(verbose: false, quiet: false);
            logger.Log("hello world");

            var output = sw.ToString();
            Assert.Contains("[Canary]", output);
            Assert.Contains("hello world", output);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ConsoleTestLogger_Quiet_SuppressesLog()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var logger = new ConsoleTestLogger(verbose: false, quiet: true);
            logger.Log("should be suppressed");

            var output = sw.ToString();
            Assert.DoesNotContain("should be suppressed", output);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ConsoleTestLogger_LogSummary_AlwaysWrites()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var logger = new ConsoleTestLogger(verbose: false, quiet: true);
            logger.LogSummary("Results: 3 passed");

            var output = sw.ToString();
            Assert.Contains("Results: 3 passed", output);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }
}
