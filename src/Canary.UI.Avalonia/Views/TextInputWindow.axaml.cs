using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Canary.UI.Avalonia.Views;

public partial class TextInputWindow : Window
{
    public TextInputWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var input = this.FindControl<TextBox>("InputBox");
        Close(input?.Text);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
