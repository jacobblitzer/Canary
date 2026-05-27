using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Canary.UI.Avalonia.Controls;

namespace Canary.UI.Avalonia.Views;

public partial class AnnotateWindow : Window
{
    private readonly string _sourcePngPath;
    private readonly Action<byte[], byte[], string>? _sink;
    private AnnotationCanvas? _canvas;

    // Parameterless ctor for AXAML preview / activator support. Use the
    // (path, sink) ctor for the real flow.
    public AnnotateWindow() : this(string.Empty, null) { }

    public AnnotateWindow(string sourcePngPath, Action<byte[], byte[], string>? sink)
    {
        InitializeComponent();
        _sourcePngPath = sourcePngPath;
        _sink = sink;

        _canvas = this.FindControl<AnnotationCanvas>("Canvas");
        if (_canvas != null)
        {
            _canvas.TextPromptAsync = PromptForTextAsync;
            if (!string.IsNullOrEmpty(_sourcePngPath))
            {
                try { _canvas.LoadImage(_sourcePngPath); }
                catch { /* surfaced on save attempt */ }
            }
        }
    }

    private async Task<string?> PromptForTextAsync()
    {
        var dlg = new TextInputWindow();
        return await dlg.ShowDialog<string?>(this);
    }

    private void OnPointer(object? sender, RoutedEventArgs e) { if (_canvas != null) _canvas.Tool = AnnotationCanvas.ToolMode.Pointer; }
    private void OnRect(object? sender, RoutedEventArgs e) { if (_canvas != null) _canvas.Tool = AnnotationCanvas.ToolMode.Rectangle; }
    private void OnFreehand(object? sender, RoutedEventArgs e) { if (_canvas != null) _canvas.Tool = AnnotationCanvas.ToolMode.Freehand; }
    private void OnText(object? sender, RoutedEventArgs e) { if (_canvas != null) _canvas.Tool = AnnotationCanvas.ToolMode.Text; }

    private void OnRed(object? sender, RoutedEventArgs e) { if (_canvas != null) _canvas.StrokeBrush = Brushes.Red; }
    private void OnYellow(object? sender, RoutedEventArgs e) { if (_canvas != null) _canvas.StrokeBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)); }
    private void OnGreen(object? sender, RoutedEventArgs e) { if (_canvas != null) _canvas.StrokeBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); }

    private void OnClear(object? sender, RoutedEventArgs e) { _canvas?.Clear(); }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_canvas == null || _sink == null)
        {
            Close();
            return;
        }
        try
        {
            var sourcePng = File.ReadAllBytes(_sourcePngPath);
            var annotatedPng = _canvas.RenderAnnotatedPng();
            var json = _canvas.SerializeAnnotationsJson("source.png");
            _sink(sourcePng, annotatedPng, json);
            var status = this.FindControl<TextBlock>("StatusLabel");
            if (status != null) status.Text = "Saved into session.";
            Close();
        }
        catch (Exception ex)
        {
            var status = this.FindControl<TextBlock>("StatusLabel");
            if (status != null)
            {
                status.Text = $"Save failed: {ex.Message}";
                status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x80));
            }
        }
    }
}
