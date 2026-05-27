using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using Canary.Config;
using Canary.Input;
using Canary.Orchestration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

public enum RecordingState { Idle, Launching, Recording }

public partial class RecordingViewModel : ObservableObject
{
    private InputRecorder? _recorder;
    private Process? _launchedProcess;
    private DispatcherTimer? _updateTimer;
    private int _lastLoggedCount;
    private string? _workloadsDir;
    private readonly Dictionary<string, WorkloadConfig> _workloadConfigs = new();

    public ObservableCollection<WorkloadConfig> Workloads { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private WorkloadConfig? _selectedWorkload;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _testName = "my-test";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private string _targetAppDescription = "Select a workload above";

    [ObservableProperty]
    private string _statusText = "Select a workload, enter a test name, then click Launch App & Record.";

    [ObservableProperty]
    private int _eventCount;

    public event Action? RecordingSaved;

    public Func<IntPtr?>? GetMainWindowHandle { get; set; }

    public void SetWorkloads(IEnumerable<WorkloadConfig> configs)
    {
        Workloads.Clear();
        _workloadConfigs.Clear();
        foreach (var c in configs)
        {
            _workloadConfigs[c.Name] = c;
            Workloads.Add(c);
        }
        SelectedWorkload = Workloads.FirstOrDefault();
    }

    public void SetWorkloadsDir(string dir) => _workloadsDir = dir;

    partial void OnSelectedWorkloadChanged(WorkloadConfig? value)
    {
        if (value == null) { TargetAppDescription = "Select a workload above"; return; }
        TargetAppDescription = $"{value.DisplayName} — {value.AppPath} {value.AppArgs}";
    }

    private bool CanStart() => State == RecordingState.Idle
        && SelectedWorkload != null
        && !string.IsNullOrWhiteSpace(TestName);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var config = SelectedWorkload;
        if (config == null) { StatusText = "Select a workload first."; return; }

        LogLines.Clear();
        _lastLoggedCount = 0;
        State = RecordingState.Launching;
        StatusText = $"Launching {config.DisplayName}...";

        AddLog($"Launching {config.DisplayName}...");
        AddLog($"  Path: {config.AppPath} {config.AppArgs}");

        try
        {
            _launchedProcess = AppLauncher.Launch(config);
            AddLog($"Process started: PID {_launchedProcess.Id}");

            AddLog("Waiting for application window...");
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(config.StartupTimeoutMs);
            IntPtr hwnd = IntPtr.Zero;

            while (DateTime.UtcNow < deadline)
            {
                _launchedProcess.Refresh();
                hwnd = _launchedProcess.MainWindowHandle;
                if (ViewportLocator.IsValidTarget(hwnd)) break;
                await Task.Delay(500).ConfigureAwait(true);
            }

            if (!ViewportLocator.IsValidTarget(hwnd))
            {
                AddLog("ERROR: Application window did not appear within timeout.");
                StatusText = "App window not found. Try increasing startup timeout.";
                Reset();
                return;
            }

            AddLog($"Window found: 0x{hwnd:X}");
            AddLog("Positioning target window...");
            WindowPositioner.PositionTargetWindow(hwnd);

            var ownHwnd = GetMainWindowHandle?.Invoke();
            if (ownHwnd.HasValue && ownHwnd.Value != IntPtr.Zero)
            {
                // Push our own window out of the way using the same helper
                // the WinForms shell uses. Width / height defaults aren't
                // critical — the helper repositions to the right of the
                // primary monitor.
                WindowPositioner.PositionCanaryWindow(ownHwnd.Value, 1280, 900);
            }

            await Task.Delay(500).ConfigureAwait(true);

            var bounds = ViewportLocator.GetViewportBounds(hwnd);
            AddLog($"Viewport: {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y})");
            AddLog("Waiting for app to settle...");
            await Task.Delay(2000).ConfigureAwait(true);

            SetForegroundWindow(hwnd);
            await Task.Delay(300).ConfigureAwait(true);

            WindowPositioner.MoveCursorToHome(bounds);
            await Task.Delay(100).ConfigureAwait(true);

            AddLog("Recording started — interact with the app window.");
            AddLog($"Test: {TestName}");

            _recorder = new InputRecorder(hwnd, config.Name, config.WindowTitle);
            _recorder.StartRecording();

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _updateTimer.Tick += OnUpdateTick;
            _updateTimer.Start();

            State = RecordingState.Recording;
            StatusText = "Recording... interact with the target window. Click Stop & Save when done.";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR: {ex.Message}");
            StatusText = $"Launch failed: {ex.Message}";
            Reset();
        }
    }

    private bool CanStop() => State == RecordingState.Recording;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _updateTimer?.Stop();
        if (_recorder == null) return;

        AddLog("Stopping recording...");
        var recording = _recorder.StopRecording();
        EventCount = recording.Events.Count;
        StatusText = $"Captured {EventCount} events ({recording.Metadata.DurationMs / 1000.0:F1}s).";
        AddLog($"Recording stopped: {EventCount} events, {recording.Metadata.DurationMs / 1000.0:F1}s duration.");

        var testName = (TestName ?? string.Empty).Trim();
        if (_workloadsDir != null && SelectedWorkload != null && !string.IsNullOrEmpty(testName))
        {
            var recordingsDir = Path.Combine(_workloadsDir, SelectedWorkload.Name, "recordings");
            Directory.CreateDirectory(recordingsDir);
            var savePath = Path.Combine(recordingsDir, $"{testName}.input.json");
            var json = JsonSerializer.Serialize(recording, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(savePath, json);
            AddLog($"Saved to: {savePath}");
            StatusText = $"Saved {EventCount} events to {Path.GetFileName(savePath)}";
            RecordingSaved?.Invoke();
        }
        else
        {
            AddLog("WARNING: No workloads directory set or test name empty — recording not saved.");
            StatusText = "Recording not saved (missing workloads dir or test name).";
        }

        _recorder = null;
        if (_launchedProcess != null)
        {
            try
            {
                if (!_launchedProcess.HasExited)
                {
                    _launchedProcess.Kill(entireProcessTree: true);
                    _launchedProcess.WaitForExit(5000);
                }
            }
            catch { }
            _launchedProcess = null;
        }
        Reset();
    }

    private void Reset()
    {
        _updateTimer?.Stop();
        _updateTimer = null;
        State = RecordingState.Idle;
    }

    private void OnUpdateTick(object? sender, EventArgs e)
    {
        if (_recorder == null) return;
        var count = _recorder.EventCount;
        EventCount = count;
        var newEvents = count - _lastLoggedCount;
        if (newEvents > 0)
        {
            AddLog($"+{newEvents} events (total: {count})");
            _lastLoggedCount = count;
        }
    }

    private void AddLog(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss.f}] {message}");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
