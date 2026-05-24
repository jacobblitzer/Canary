using Canary.Cli;

namespace Canary.UI;

internal static class Program
{
    // Per design §C3 + impl §3 + operator decision Q5: single-instance UI.
    // The first launch acquires this mutex and serves a named pipe for
    // forwarded auto-run args; subsequent launches send their args via the
    // pipe and exit.
    private const string SingleInstanceMutexName = @"Global\Canary.UI.SingleInstance";

    [STAThread]
    static void Main(string[] args)
    {
        AutoRunArgs.TryParse(args, out var autoRunArgs);

        using var mutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out var acquired);

        if (!acquired)
        {
            // Another instance is already running. Forward our args to it and exit.
            if (!autoRunArgs.IsEmpty)
            {
                SingleInstancePipeClient.TrySend(autoRunArgs);
            }
            return;
        }

        ApplicationConfiguration.Initialize();

        var form = new MainForm();
        using var pipeServer = new SingleInstancePipeServer();
        pipeServer.AutoRunRequested += incoming =>
        {
            // Marshal to the UI thread; the pipe loop runs on a Task.
            form.BeginInvoke(new Action(async () =>
            {
                try { await form.AutoRunAsync(incoming).ConfigureAwait(true); } catch { }
            }));
        };
        pipeServer.Start();

        if (!autoRunArgs.IsEmpty)
        {
            form.Load += async (_, _) =>
            {
                try { await form.AutoRunAsync(autoRunArgs).ConfigureAwait(true); } catch { }
            };
        }

        Application.Run(form);
        GC.KeepAlive(mutex);
    }
}
