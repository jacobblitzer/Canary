using Avalonia;
using Avalonia.Controls;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Views;

public partial class FeedbackView : UserControl
{
    public FeedbackView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is FeedbackViewModel vm)
        {
            vm.ReloadCommand.Execute(null);
        }
    }
}
