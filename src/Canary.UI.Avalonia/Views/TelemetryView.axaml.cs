using Avalonia;
using Avalonia.Controls;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Views;

public partial class TelemetryView : UserControl
{
    public TelemetryView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is TelemetryViewModel vm) vm.StartPolling();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is TelemetryViewModel vm) vm.StopPolling();
    }
}
