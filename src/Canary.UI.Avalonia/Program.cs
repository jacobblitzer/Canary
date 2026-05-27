using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Canary.Cli;

namespace Canary.UI.Avalonia;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Global\Canary.UI.SingleInstance.Avalonia";

    [STAThread]
    public static int Main(string[] args)
    {
        AutoRunArgs.TryParse(args, out var autoRunArgs);

        using var mutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out var acquired);

        if (!acquired)
        {
            if (!autoRunArgs.IsEmpty)
            {
                Services.SingleInstancePipeClient.TrySend(autoRunArgs);
            }
            return 0;
        }

        App.PendingAutoRunArgs = autoRunArgs.IsEmpty ? null : autoRunArgs;
        var exit = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(mutex);
        return exit;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
