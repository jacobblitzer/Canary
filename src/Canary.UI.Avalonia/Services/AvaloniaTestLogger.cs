using Avalonia.Threading;
using Canary;

namespace Canary.UI.Avalonia.Services;

// ITestLogger implementation that marshals events to the Avalonia UI
// thread via Dispatcher.UIThread.Post. Mirrors the WinForms GuiTestLogger
// (src/Canary.UI/Services/GuiTestLogger.cs) in shape; the only
// difference is the synchronization target.
public sealed class AvaloniaTestLogger : ITestLogger
{
    public bool Verbose { get; }

    public event Action<string>? MessageLogged;
    public event Action<string, string, TestStatusLevel>? StatusLogged;
    public event Action<string>? SummaryLogged;

    public AvaloniaTestLogger(bool verbose) { Verbose = verbose; }

    public void Log(string message) => Fire(() => MessageLogged?.Invoke(message));
    public void LogStatus(string symbol, string message, TestStatusLevel level) => Fire(() => StatusLogged?.Invoke(symbol, message, level));
    public void LogSummary(string message) => Fire(() => SummaryLogged?.Invoke(message));

    private static void Fire(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}
