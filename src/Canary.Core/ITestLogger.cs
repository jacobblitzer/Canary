namespace Canary;

/// <summary>
/// Abstraction for test output logging, allowing both CLI and GUI consumers.
/// </summary>
public interface ITestLogger
{
    /// <summary>
    /// Whether verbose (per-checkpoint) output is enabled.
    /// </summary>
    bool Verbose { get; }

    /// <summary>
    /// Log a general message.
    /// </summary>
    void Log(string message);

    /// <summary>
    /// Log a status line with a symbol and semantic level.
    /// </summary>
    void LogStatus(string symbol, string message, TestStatusLevel level);

    /// <summary>
    /// Log a summary line (always shown, even in quiet mode).
    /// </summary>
    void LogSummary(string message);
}

/// <summary>
/// Semantic level for test status messages.
/// </summary>
public enum TestStatusLevel
{
    Info,
    Pass,
    Fail,
    Crash,
    New
}
