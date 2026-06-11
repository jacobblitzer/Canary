using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Views;

public partial class RunHistoryPaneView : UserControl
{
    public RunHistoryPaneView()
    {
        InitializeComponent();
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Only open when the double-click landed on a row, not the header.
        if (e.Source is Control c && c.FindAncestorOfType<DataGridColumnHeader>() != null) return;
        (DataContext as RunHistoryViewModel)?.OpenSelected();
    }
}
