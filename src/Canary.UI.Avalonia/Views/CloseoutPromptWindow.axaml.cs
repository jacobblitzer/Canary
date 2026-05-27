using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Canary.UI.Avalonia.Views;

public partial class CloseoutPromptWindow : Window
{
    public CloseoutPromptWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var body = this.FindControl<TextBox>("BodyBox");
        Close(body?.Text);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
