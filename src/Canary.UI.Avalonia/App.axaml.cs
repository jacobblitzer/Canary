using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Canary.Cli;
using Canary.UI.Avalonia.Services;
using Canary.UI.Avalonia.ViewModels;
using Canary.UI.Avalonia.Views;

namespace Canary.UI.Avalonia;

public partial class App : Application
{
    internal static AutoRunArgs? PendingAutoRunArgs;

    private SingleInstancePipeServer? _pipeServer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            _pipeServer = new SingleInstancePipeServer();
            _pipeServer.AutoRunRequested += args =>
            {
                Dispatcher.UIThread.Post(() => vm.HandleAutoRun(args));
            };
            _pipeServer.Start();

            desktop.Exit += (_, _) =>
            {
                _pipeServer?.Dispose();
                _pipeServer = null;
            };

            if (PendingAutoRunArgs is { } pending)
            {
                window.Opened += (_, _) => vm.HandleAutoRun(pending);
                PendingAutoRunArgs = null;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
