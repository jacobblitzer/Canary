using Canary;

namespace Canary.UI.Services;

/// <summary>
/// ITestLogger implementation that fires events for GUI consumption.
/// Thread-safe: marshals to UI thread via Control.BeginInvoke.
/// </summary>
public sealed class GuiTestLogger : ITestLogger
{
    private readonly Control _syncTarget;

    public GuiTestLogger(Control syncTarget, bool verbose)
    {
        _syncTarget = syncTarget;
        Verbose = verbose;
    }

    public bool Verbose { get; }

    public event Action<string>? MessageLogged;
    public event Action<string, string, TestStatusLevel>? StatusLogged;
    public event Action<string>? SummaryLogged;

    public void Log(string message)
    {
        Fire(() => MessageLogged?.Invoke(message));
    }

    public void LogStatus(string symbol, string message, TestStatusLevel level)
    {
        Fire(() => StatusLogged?.Invoke(symbol, message, level));
    }

    public void LogSummary(string message)
    {
        Fire(() => SummaryLogged?.Invoke(message));
    }

    private void Fire(Action action)
    {
        if (_syncTarget.InvokeRequired)
            _syncTarget.BeginInvoke(action);
        else
            action();
    }
}
