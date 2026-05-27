using Avalonia.Media;
using Canary.Feedback;
using Canary.UI.Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

// Phase 4 polish: extracts the AnnotateWindow's behavior out of code-
// behind so the inbox-mode + session-sink-mode flows are both testable
// without spinning up an Avalonia window. Mirrors the WinForms
// AnnotatedImageForm's two-constructor design — the View hands the VM
// either an Action<sourcePng, annotatedPng, annotationsJson> session
// sink OR a FeedbackInbox triple (inboxRoot + runRef + checkpointRef);
// Save dispatches accordingly.
public partial class AnnotateWindowViewModel : ObservableObject
{
    private readonly string _sourceImagePath;
    private readonly Action<byte[], byte[], string>? _sessionSink;
    private readonly string? _inboxRoot;
    private readonly string? _runRef;
    private readonly string? _checkpointRef;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _statusColor = "#96DC82";
    [ObservableProperty] private AnnotationCanvas.ToolMode _currentTool = AnnotationCanvas.ToolMode.Rectangle;
    [ObservableProperty] private IBrush _currentBrush = Brushes.Red;

    public bool IsInboxMode => _sessionSink == null;
    public bool IsSessionSinkMode => _sessionSink != null;

    public event Action? RequestClose;
    public Func<byte[]>? GetAnnotatedPngBytes { get; set; }
    public Func<string>? GetAnnotationsJson { get; set; }
    public Func<int>? GetUndoCount { get; set; }
    public Action? RequestUndo { get; set; }
    public Action? RequestClear { get; set; }

    // Session-sink mode constructor: write to the session's captures/
    // dir via the operator-supplied sink. Phase 0 wired this for the
    // Sessions Live → Capture+Annotate flow.
    public AnnotateWindowViewModel(string sourceImagePath, Action<byte[], byte[], string> sessionSink)
    {
        _sourceImagePath = sourceImagePath;
        _sessionSink = sessionSink;
    }

    // Inbox-mode constructor: write to docs/feedback/inbox/ via
    // FeedbackInboxWriter. The WinForms shell uses this from the Past
    // Runs Annotate button. Phase 4 brings parity to the Avalonia path
    // so the Phase 5 wire-in only has to hook the constructor.
    public AnnotateWindowViewModel(string sourceImagePath, string inboxRoot, string? runRef = null, string? checkpointRef = null)
    {
        _sourceImagePath = sourceImagePath;
        _inboxRoot = inboxRoot;
        _runRef = runRef;
        _checkpointRef = checkpointRef;
    }

    public string SourceImagePath => _sourceImagePath;

    [RelayCommand]
    private void Save()
    {
        try
        {
            var sourcePng = File.ReadAllBytes(_sourceImagePath);
            var annotatedPng = GetAnnotatedPngBytes?.Invoke() ?? Array.Empty<byte>();
            var json = GetAnnotationsJson?.Invoke() ?? string.Empty;

            if (_sessionSink != null)
            {
                _sessionSink(sourcePng, annotatedPng, json);
                StatusText = "Saved into session.";
                StatusColor = "#96DC82";
                RequestClose?.Invoke();
                return;
            }

            if (string.IsNullOrEmpty(_inboxRoot))
            {
                StatusText = "No sink + no inbox root configured.";
                StatusColor = "#FFB4B4";
                return;
            }

            var title = string.IsNullOrWhiteSpace(Title)
                ? Path.GetFileNameWithoutExtension(_sourceImagePath)
                : Title;

            var writer = new FeedbackInboxWriter(_inboxRoot);
            var slug = FeedbackSlugGenerator.Generate(DateTime.UtcNow, title, writer.ExistingSlugs());
            var item = new FeedbackItem
            {
                Slug = slug,
                Date = DateTime.UtcNow,
                Status = "open",
                Project = "canary",
                RunRef = _runRef,
                CheckpointRef = _checkpointRef,
                ImageRef = "annotated.png",
                Title = title,
                Body = string.IsNullOrWhiteSpace(Body) ? "(no notes)" : Body,
            };

            writer.Write(item, sourcePng, annotatedPng, json);
            StatusText = $"Saved: {slug}.md";
            StatusColor = "#96DC82";
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            StatusColor = "#FFB4B4";
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();

    [RelayCommand]
    private void Undo() => RequestUndo?.Invoke();

    [RelayCommand]
    private void Clear() => RequestClear?.Invoke();

    [RelayCommand]
    private void PickTool(string toolName)
    {
        if (Enum.TryParse<AnnotationCanvas.ToolMode>(toolName, ignoreCase: true, out var tool))
        {
            CurrentTool = tool;
        }
    }

    [RelayCommand]
    private void PickColor(string colorHex)
    {
        CurrentBrush = colorHex.ToLowerInvariant() switch
        {
            "red" => Brushes.Red,
            "yellow" => new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
            "green" => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
            _ => CurrentBrush,
        };
    }

    public bool IsToolActive(AnnotationCanvas.ToolMode mode) => CurrentTool == mode;
}
