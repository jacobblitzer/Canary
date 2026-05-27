using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Canary.UI.Avalonia.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Canary.UI.Avalonia.Views;

public partial class LocalhostView : UserControl
{
    public LocalhostView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is LocalhostViewModel vm)
        {
            vm.ConfirmKillAsync = ConfirmKillAsync;
        }
    }

    private async Task<bool> ConfirmKillAsync(string message)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return false;

        var dialog = new ContentDialog
        {
            Title = "Confirm kill",
            Content = message,
            PrimaryButtonText = "Kill",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        var result = await dialog.ShowAsync(window);
        return result == ContentDialogResult.Primary;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is LocalhostViewModel vm)
        {
            vm.StartPolling();
            _ = vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is LocalhostViewModel vm)
        {
            vm.StopPolling();
        }
    }
}
