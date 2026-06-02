using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Canary.Config;
using Canary.UI.Avalonia.Hotkeys;
using Canary.UI.Avalonia.ViewModels;
using Canary.UI.Avalonia.ViewModels.Editors;

namespace Canary.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    private AbortHotkey? _abortHotkey;

    // Phase 4.6.F Session B+ — exclude Canary.UI from DWM screen capture so the
    // 📷 Capture Screen button (which brings Canary.UI to the foreground on click)
    // doesn't end up photographing Canary.UI's own chrome instead of the warning
    // balloon / modal toast the operator was trying to catch. Also affects the
    // automated full-screen sibling capture and any external screenshot tool
    // (Snip & Sketch, Greenshot, etc.) — desirable: the operator never wants
    // Canary in their screenshots. Backward-safe: on Windows older than 10 2004
    // (build 19041) the call returns false and the window behaves as before.
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PickWorkloadsDirAsync = PickWorkloadsDirAsync;
            vm.Tests.EditTestAsync = EditTestAsync;
            vm.Tests.EditSuiteAsync = EditSuiteAsync;
            vm.Tests.EditWorkloadAsync = EditWorkloadAsync;
            vm.Tests.PromptForTestNameAsync = suggested =>
                PromptForStringAsync("New test name", suggested ?? string.Empty);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        // Best-effort capture exclusion. Failure is silent (older Windows).
        try { SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE); } catch { /* ignore */ }

        _abortHotkey = new AbortHotkey(handle);
        _abortHotkey.AbortRequested += () =>
        {
            // Arm only during a run; Stop is idempotent so a stray Pause
            // press while idle is harmless.
            if (vm.Tests.Runner.StopCommand.CanExecute(null))
            {
                vm.Tests.Runner.StopCommand.Execute(null);
            }
        };
        vm.Tests.Runner.OnRunStarted = () => _abortHotkey?.Register();
        vm.Tests.Runner.OnRunFinished = () => _abortHotkey?.Unregister();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _abortHotkey?.Dispose();
        _abortHotkey = null;
    }

    private async Task<string?> PickWorkloadsDirAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick workloads directory",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return null;
        return folders[0].TryGetLocalPath();
    }

    private async Task<string?> PromptForStringAsync(string title, string initial)
    {
        var dlg = new TextInputWindow();
        if (!string.IsNullOrEmpty(title)) dlg.Title = title;
        var inputBox = dlg.FindControl<TextBox>("InputBox");
        if (inputBox != null) inputBox.Text = initial;
        return await dlg.ShowDialog<string?>(this);
    }

    private async Task EditTestAsync(TestDefinition td)
    {
        var vm = new TestEditorViewModel();
        vm.Load(td);
        await ShowEditorAsync($"Test — {td.Name}", new TestEditorView { DataContext = vm }, vm,
            (DataContext as MainWindowViewModel)?.Tests).ConfigureAwait(true);
    }

    private async Task EditSuiteAsync(SuiteDefinition sd)
    {
        var vm = new SuiteEditorViewModel();
        var tests = (DataContext as MainWindowViewModel)?.Tests.Tree.LoadedWorkloads
            .SelectMany(w => w.Tests)
            .ToList() ?? new List<TestDefinition>();
        vm.Load(sd, tests);
        await ShowEditorAsync($"Suite — {sd.Name}", new SuiteEditorView { DataContext = vm }, vm,
            (DataContext as MainWindowViewModel)?.Tests).ConfigureAwait(true);
    }

    private async Task EditWorkloadAsync(WorkloadConfig wc)
    {
        var vm = new WorkloadEditorViewModel();
        vm.Load(wc);
        await ShowEditorAsync($"Workload — {wc.Name}", new WorkloadEditorView { DataContext = vm }, vm,
            (DataContext as MainWindowViewModel)?.Tests).ConfigureAwait(true);
    }

    private async Task ShowEditorAsync(string title, Control content, object editorVm, TestsViewModel? testsVm)
    {
        var win = new EditorHostWindow(title, content);
        // Subscribe to the editor's SaveRequested event via reflection
        // so a single helper handles all three editor VM types.
        var saveEvent = editorVm.GetType().GetEvent("SaveRequested");
        Action<string>? handler = null;
        if (saveEvent != null)
        {
            handler = async json =>
            {
                try
                {
                    await PersistAndRefreshAsync(editorVm, json, testsVm).ConfigureAwait(true);
                }
                catch { /* error surfaced via the editor's ValidationError */ }
                win.Close();
            };
            saveEvent.AddEventHandler(editorVm, handler);
        }
        await win.ShowDialog(this);
        if (saveEvent != null && handler != null)
            saveEvent.RemoveEventHandler(editorVm, handler);
    }

    private static async Task PersistAndRefreshAsync(object editorVm, string json, TestsViewModel? testsVm)
    {
        if (testsVm?.Tree.WorkloadsDir == null) return;
        var workloadsDir = testsVm.Tree.WorkloadsDir;

        if (editorVm is TestEditorViewModel teVm)
        {
            var def = teVm.BuildDefinition();
            var workload = testsVm.Tree.LoadedWorkloads.FirstOrDefault(w => w.Config.Name == def.Workload);
            if (workload != null)
            {
                var path = Path.Combine(workload.Directory, "tests", $"{def.Name}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
            }
        }
        else if (editorVm is SuiteEditorViewModel seVm)
        {
            var def = seVm.BuildDefinition();
            // Suites live under the workload of the currently-selected
            // tree node. Falls back to the first workload otherwise.
            var workload = testsVm.Tree.SelectedNode?.OwningWorkload
                ?? testsVm.Tree.LoadedWorkloads.FirstOrDefault();
            if (workload != null)
            {
                var path = Path.Combine(workload.Directory, "suites", $"{def.Name}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
            }
        }
        else if (editorVm is WorkloadEditorViewModel weVm)
        {
            var cfg = weVm.BuildConfig();
            var workload = testsVm.Tree.LoadedWorkloads.FirstOrDefault(w => w.Config.Name == cfg.Name);
            if (workload != null)
            {
                var path = Path.Combine(workload.Directory, "workload.json");
                File.WriteAllText(path, json);
            }
        }

        await testsVm.Tree.LoadAsync(workloadsDir).ConfigureAwait(true);
    }
}
