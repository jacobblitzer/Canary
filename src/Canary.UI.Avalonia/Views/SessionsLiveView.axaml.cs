using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Canary.UI.Avalonia.Hotkeys;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Views;

public partial class SessionsLiveView : UserControl
{
    private SessionHotkeyHook? _hotkeyHook;

    public SessionsLiveView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SessionsLiveViewModel vm)
        {
            vm.NotePromptAsync = PromptForNoteAsync;
            vm.CloseoutPromptAsync = PromptForCloseoutAsync;
            vm.AnnotatePromptAsync = PromptForAnnotateAsync;
        }
    }

    private async Task<NoteResult?> PromptForNoteAsync()
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return null;
        var dlg = new NotePromptWindow();
        var result = await dlg.ShowDialog<NoteResult?>(window);
        return result;
    }

    private async Task<string?> PromptForCloseoutAsync()
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return null;
        var dlg = new CloseoutPromptWindow();
        var result = await dlg.ShowDialog<string?>(window);
        return result;
    }

    private async Task PromptForAnnotateAsync(string sourcePngPath, Action<byte[], byte[], string> sink)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;
        var dlg = new AnnotateWindow(sourcePngPath, sink);
        await dlg.ShowDialog(window);
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;
        var handle = window.TryGetPlatformHandle();
        if (handle == null) return;
        _hotkeyHook = new SessionHotkeyHook(handle.Handle);
        _hotkeyHook.CaptureRequested += OnHotkeyCapture;
        _hotkeyHook.AnnotateRequested += OnHotkeyAnnotate;
        _hotkeyHook.Register();
        window.Closing += (_, _) => _hotkeyHook?.Dispose();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _hotkeyHook?.Dispose();
        _hotkeyHook = null;
    }

    private void OnHotkeyCapture()
    {
        if (DataContext is SessionsLiveViewModel vm)
        {
            _ = vm.DoCaptureAsync(withNote: false, annotate: false);
        }
    }

    private void OnHotkeyAnnotate()
    {
        if (DataContext is SessionsLiveViewModel vm)
        {
            _ = vm.DoCaptureAsync(withNote: false, annotate: true);
        }
    }
}
