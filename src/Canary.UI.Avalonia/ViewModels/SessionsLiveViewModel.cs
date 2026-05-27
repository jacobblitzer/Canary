using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Canary.Config;
using Canary.Harness.Session;
using Canary.Session;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

public enum SessionState { Idle, Starting, Armed, Ending }

public sealed class WorkloadOption
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public override string ToString() => $"{DisplayName} ({Name})";
}

public sealed class NoteResult
{
    public string? Title { get; init; }
    public string? Body { get; init; }
}

public sealed partial class CaptureThumbnail : ObservableObject
{
    public required int Sequence { get; init; }
    public required string PngPath { get; init; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private string? _annotatedPath;
}

public partial class SessionsLiveViewModel : ObservableObject
{
    private readonly ISessionAgentFactory _factory;
    private string? _workloadsDir;
    private SupervisedSession? _session;
    private bool _capturing;

    public ObservableCollection<WorkloadOption> Workloads { get; } = new();
    public ObservableCollection<CaptureThumbnail> Captures { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private WorkloadOption? _selectedWorkload;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CaptureCommand))]
    [NotifyCanExecuteChangedFor(nameof(CaptureAnnotateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CaptureWithNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndCommand))]
    private SessionState _state = SessionState.Idle;

    [ObservableProperty]
    private string _statusText = "Pick a workload, then Start session. The target app opens visibly; capture on demand.";

    public string HotkeyHint => "Hotkeys (while session is live, anywhere): Ctrl+Shift+C = capture · Ctrl+Shift+A = capture + annotate";

    // Wire-up points the View provides. Phase 0 spike keeps them
    // delegate-shaped so unit tests can inject no-op fakes.
    public Func<Task<NoteResult?>>? NotePromptAsync { get; set; }
    public Func<Task<string?>>? CloseoutPromptAsync { get; set; }
    public Func<string, Action<byte[], byte[], string>, Task>? AnnotatePromptAsync { get; set; }

    public string? CurrentSessionDirectory => _session?.Directory;
    public string? CurrentSessionId => _session?.SessionId;

    public SessionsLiveViewModel() : this(new SessionAgentFactory()) { }

    internal SessionsLiveViewModel(ISessionAgentFactory factory)
    {
        _factory = factory;
    }

    internal void SetWorkloads(string? workloadsDir, IEnumerable<WorkloadConfig> workloads)
    {
        _workloadsDir = workloadsDir;
        var prev = SelectedWorkload?.Name;
        Workloads.Clear();
        foreach (var w in workloads)
        {
            if (w.AgentType is "qualia-cdp" or "penumbra-cdp")
            {
                Workloads.Add(new WorkloadOption { Name = w.Name, DisplayName = w.DisplayName });
            }
        }
        if (Workloads.Count > 0)
        {
            SelectedWorkload = prev != null
                ? Workloads.FirstOrDefault(x => x.Name == prev) ?? Workloads[0]
                : Workloads[0];
        }
        else
        {
            SelectedWorkload = null;
        }
    }

    private bool CanStart() => State == SessionState.Idle && SelectedWorkload != null;
    private bool CanCapture() => State == SessionState.Armed && !_capturing;
    private bool CanEnd() => State == SessionState.Armed;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_workloadsDir == null || SelectedWorkload == null)
        {
            StatusText = "Pick a workload first.";
            return;
        }

        var configPath = Path.Combine(_workloadsDir, SelectedWorkload.Name, "workload.json");
        if (!File.Exists(configPath))
        {
            StatusText = $"workload.json not found: {configPath}";
            return;
        }

        State = SessionState.Starting;
        StatusText = $"Starting Vite + Chrome for '{SelectedWorkload.Name}'... this can take up to 30s.";

        try
        {
            _session = await SupervisedSession.StartAsync(
                _workloadsDir, SelectedWorkload.Name, configPath, _factory).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to start session: {ex.Message}";
            State = SessionState.Idle;
            return;
        }

        State = SessionState.Armed;
        StatusText = $"Session armed · {_session.SessionId} · {_session.Url ?? "(no url)"} · dir: {_session.Directory}";
    }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private Task CaptureAsync() => DoCaptureAsync(withNote: false, annotate: false);

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private Task CaptureAnnotateAsync() => DoCaptureAsync(withNote: false, annotate: true);

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private Task CaptureWithNoteAsync() => DoCaptureAsync(withNote: true, annotate: false);

    public async Task DoCaptureAsync(bool withNote, bool annotate)
    {
        if (_session == null || _capturing) return;
        _capturing = true;
        CaptureCommand.NotifyCanExecuteChanged();
        try
        {
            string? title = null;
            string? body = null;
            if (withNote)
            {
                if (NotePromptAsync == null) return;
                var note = await NotePromptAsync().ConfigureAwait(true);
                if (note == null) return;
                title = note.Title;
                body = note.Body;
            }

            CaptureResult result;
            try
            {
                result = await _session.CaptureAsync(title, body).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusText = $"Capture failed: {ex.Message}";
                return;
            }

            AddThumbnail(result);
            StatusText = $"Captured #{result.Sequence} → {Path.GetFileName(result.PngPath)} (total: {_session.Captures.Count})";

            if (annotate && AnnotatePromptAsync != null)
            {
                await OpenAnnotateAsync(result).ConfigureAwait(true);
            }
        }
        finally
        {
            _capturing = false;
            CaptureCommand.NotifyCanExecuteChanged();
            CaptureAnnotateCommand.NotifyCanExecuteChanged();
            CaptureWithNoteCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task OpenAnnotateAsync(CaptureResult result)
    {
        if (_session == null || AnnotatePromptAsync == null) return;
        var session = _session;
        var captured = session.Captures.FirstOrDefault(c => c.Sequence == result.Sequence);
        if (captured == null) return;

        var captureDir = SessionPaths.CapturesDir(session.Directory);
        var annotatedFile = SessionPaths.CaptureAnnotatedPngFile(result.Sequence, captured.CapturedAtUtc, captured.Slug);
        var annotationsFile = SessionPaths.CaptureAnnotationsJsonFile(result.Sequence, captured.CapturedAtUtc, captured.Slug);

        await AnnotatePromptAsync(result.PngPath, (sourcePng, annotatedPng, annotationsJson) =>
        {
            _ = sourcePng;
            File.WriteAllBytes(Path.Combine(captureDir, annotatedFile), annotatedPng);
            File.WriteAllText(Path.Combine(captureDir, annotationsFile), annotationsJson);
            session.AttachAnnotation(result.Sequence, annotatedFile, annotationsFile);
            RefreshThumbnail(result.Sequence);
        }).ConfigureAwait(true);
    }

    private void AddThumbnail(CaptureResult result)
    {
        try
        {
            var thumb = new CaptureThumbnail { Sequence = result.Sequence, PngPath = result.PngPath };
            try
            {
                using var fs = File.OpenRead(result.PngPath);
                thumb.Thumbnail = new Bitmap(fs);
            }
            catch { /* file may be locked momentarily; skip thumbnail */ }
            Captures.Add(thumb);
        }
        catch
        {
            // If anything goes wrong building the thumbnail, the PNG is
            // still on disk and the report will still embed it.
        }
    }

    private void RefreshThumbnail(int sequence)
    {
        if (_session == null) return;
        var thumb = Captures.FirstOrDefault(c => c.Sequence == sequence);
        if (thumb == null) return;
        var cap = _session.Captures.FirstOrDefault(x => x.Sequence == sequence);
        if (cap?.AnnotatedPngFile == null) return;
        var annotatedPath = Path.Combine(SessionPaths.CapturesDir(_session.Directory), cap.AnnotatedPngFile);
        try
        {
            using var fs = File.OpenRead(annotatedPath);
            var old = thumb.Thumbnail;
            thumb.Thumbnail = new Bitmap(fs);
            thumb.AnnotatedPath = annotatedPath;
            old?.Dispose();
        }
        catch
        {
            // Keep the source thumbnail.
        }
    }

    [RelayCommand(CanExecute = nameof(CanEnd))]
    private async Task EndAsync()
    {
        if (_session == null) return;

        string? closeout = null;
        if (CloseoutPromptAsync != null)
        {
            closeout = await CloseoutPromptAsync().ConfigureAwait(true);
        }

        State = SessionState.Ending;
        StatusText = "Ending session — writing report...";
        var session = _session;
        try
        {
            await session.EndAsync(closeout).ConfigureAwait(true);
            await session.DisposeAsync().ConfigureAwait(true);
            StatusText = $"Session ended. Report: {SessionPaths.ReportPath(session.Directory)}";
        }
        catch (Exception ex)
        {
            StatusText = $"End failed: {ex.Message}";
        }

        _session = null;
        Captures.Clear();
        State = SessionState.Idle;
    }

    // Test seam: drive the capture pathway without going through the
    // RelayCommand (which the test harness can't await directly).
    internal Task CaptureForTestAsync(bool withNote, bool annotate) => DoCaptureAsync(withNote, annotate);
}
