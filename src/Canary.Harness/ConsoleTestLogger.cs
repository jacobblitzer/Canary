namespace Canary;

/// <summary>
/// Console-based implementation of ITestLogger for the CLI harness.
/// </summary>
public sealed class ConsoleTestLogger : ITestLogger
{
    private readonly bool _quiet;

    public ConsoleTestLogger(bool verbose, bool quiet)
    {
        Verbose = verbose;
        _quiet = quiet;
    }

    public bool Verbose { get; }

    public void Log(string message)
    {
        if (_quiet) return;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Canary] {message}");
    }

    public void LogStatus(string symbol, string message, TestStatusLevel level)
    {
        var color = level switch
        {
            TestStatusLevel.Pass => ConsoleColor.Green,
            TestStatusLevel.Fail => ConsoleColor.Red,
            TestStatusLevel.Crash => ConsoleColor.Magenta,
            TestStatusLevel.New => ConsoleColor.Yellow,
            _ => ConsoleColor.Gray
        };

        var timestamp = $"[{DateTime.Now:HH:mm:ss}]";
        Console.Write(timestamp + " ");
        var prevColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(symbol);
        Console.ForegroundColor = prevColor;
        Console.WriteLine($" {message}  Press Ctrl+C to abort");
    }

    public void LogSummary(string message)
    {
        Console.WriteLine(message);
    }
}
