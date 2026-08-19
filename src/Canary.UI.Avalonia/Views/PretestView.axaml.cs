using Avalonia.Controls;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Views;

/// <summary>
/// Code-behind for the Pretest tab.
/// </summary>
/// <remarks>
/// Only wires the clipboard. The view model never reaches for a clipboard or a window itself —
/// the same delegate pattern every dialog in this app uses, so the VM stays testable without
/// a real one.
/// </remarks>
public partial class PretestView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public PretestView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PretestViewModel vm)
            {
                vm.CopyToClipboard = text =>
                {
                    var top = TopLevel.GetTopLevel(this);
                    top?.Clipboard?.SetTextAsync(text);
                };
            }
        };
    }
}
