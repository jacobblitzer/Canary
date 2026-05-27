using Avalonia.Controls;

namespace Canary.UI.Avalonia.Views;

// Phase 5: simple non-modal Window that hosts one editor View as its
// content. Used by the Tests-tab context menus (Edit Test / Edit Suite /
// Edit Config) to show editors in their own window — keeps the main
// shell unchanged while routing the orphan Phase 3 editors into the
// operator workflow.
public partial class EditorHostWindow : Window
{
    public EditorHostWindow()
    {
        InitializeComponent();
    }

    public EditorHostWindow(string title, Control content) : this()
    {
        Title = title;
        var host = this.FindControl<ContentControl>("HostContent");
        if (host != null) host.Content = content;
    }
}
