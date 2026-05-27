using Avalonia.Controls;
using Avalonia.Media;
using Canary.UI.Avalonia.Controls;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Views;

public partial class AnnotateWindow : Window
{
    private readonly AnnotationCanvas? _canvas;
    private AnnotateWindowViewModel? _vm;

    // Parameterless ctor for AXAML preview / activator support.
    public AnnotateWindow() : this(string.Empty, vm: null) { }

    // Session-sink mode (Phase 0 — wired by Sessions Live → Capture+Annotate).
    public AnnotateWindow(string sourcePngPath, Action<byte[], byte[], string>? sink)
        : this(sourcePngPath, vm: sink != null ? new AnnotateWindowViewModel(sourcePngPath, sink) : null) { }

    // Feedback-inbox mode (Phase 4 — wired by Past Runs Annotate in Phase 5).
    public AnnotateWindow(string sourcePngPath, string inboxRoot, string? runRef = null, string? checkpointRef = null)
        : this(sourcePngPath, vm: new AnnotateWindowViewModel(sourcePngPath, inboxRoot, runRef, checkpointRef)) { }

    private AnnotateWindow(string sourcePngPath, AnnotateWindowViewModel? vm)
    {
        InitializeComponent();
        _canvas = this.FindControl<AnnotationCanvas>("Canvas");
        if (_canvas != null)
        {
            _canvas.TextPromptAsync = PromptForTextAsync;
            if (!string.IsNullOrEmpty(sourcePngPath))
            {
                try { _canvas.LoadImage(sourcePngPath); }
                catch { /* surfaced on save attempt */ }
            }
        }

        if (vm != null)
        {
            _vm = vm;
            DataContext = vm;
            WireVm(vm);
        }
    }

    private void WireVm(AnnotateWindowViewModel vm)
    {
        vm.GetAnnotatedPngBytes = () => _canvas?.RenderAnnotatedPng() ?? Array.Empty<byte>();
        vm.GetAnnotationsJson = () => _canvas?.SerializeAnnotationsJson("source.png") ?? string.Empty;
        vm.GetUndoCount = () => _canvas?.UndoCount ?? 0;
        vm.RequestUndo = () => _canvas?.Undo();
        vm.RequestClear = () => _canvas?.Clear();
        vm.RequestClose += Close;
        vm.PropertyChanged += (_, e) =>
        {
            if (_canvas == null) return;
            if (e.PropertyName == nameof(AnnotateWindowViewModel.CurrentTool))
                _canvas.Tool = vm.CurrentTool;
            else if (e.PropertyName == nameof(AnnotateWindowViewModel.CurrentBrush))
                _canvas.StrokeBrush = vm.CurrentBrush;
        };
        // Apply initial selections so the canvas starts in the right state.
        _canvas!.Tool = vm.CurrentTool;
        _canvas!.StrokeBrush = vm.CurrentBrush;
    }

    private async Task<string?> PromptForTextAsync()
    {
        var dlg = new TextInputWindow();
        return await dlg.ShowDialog<string?>(this);
    }
}
