using Avalonia.Controls;
using Avalonia.Interactivity;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Views;

public partial class NotePromptWindow : Window
{
    public NotePromptWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var title = this.FindControl<TextBox>("TitleBox");
        var body = this.FindControl<TextBox>("BodyBox");
        Close(new NoteResult { Title = title?.Text, Body = body?.Text });
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
