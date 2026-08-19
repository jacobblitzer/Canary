using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Canary.Commissioning;
using Canary.Config;
using Canary.Orchestration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

/// <summary>What the Pretest tab is doing.</summary>
public enum PretestState
{
    /// <summary>Nothing running.</summary>
    Idle,

    /// <summary>Reading the machine — filesystem and registry only, no app.</summary>
    Surveying,

    /// <summary>Running commissioning, which launches the target application.</summary>
    Commissioning,
}

/// <summary>One line of the machine survey.</summary>
public sealed class MachineFactRow
{
    /// <summary>Grouping, e.g. <c>Rhino</c>.</summary>
    public required string Group { get; init; }

    /// <summary>What it is.</summary>
    public required string Name { get; init; }

    /// <summary>What was found.</summary>
    public required string Value { get; init; }
}

/// <summary>One commissioning layer, for display.</summary>
public sealed class PretestLayerRow
{
    /// <summary>1, 2 or 3.</summary>
    public required int Number { get; init; }

    /// <summary>Layer name.</summary>
    public required string Name { get; init; }

    /// <summary>Passed / Failed / NotRun.</summary>
    public required string Outcome { get; init; }

    /// <summary>Whether failing it makes results unreadable.</summary>
    public required bool Fatal { get; init; }

    /// <summary>What was measured.</summary>
    public required string Detail { get; init; }

    /// <summary>Green when passed, red when a fatal layer failed, amber otherwise.</summary>
    public string OutcomeBrush => Outcome switch
    {
        nameof(LayerOutcome.Passed) => "#4EC94E",
        nameof(LayerOutcome.Failed) => Fatal ? "#FF6B68" : "#E8C547",
        _ => Fatal ? "#FF6B68" : "#9A9A9A",
    };
}

/// <summary>One declared plug-in requirement and whether this machine has it.</summary>
public sealed class ReadinessRowVm
{
    /// <summary>Requirement id.</summary>
    public required string Id { get; init; }

    /// <summary>Present / Missing / Unknown.</summary>
    public required string State { get; init; }

    /// <summary>Version the host reported, when present.</summary>
    public required string Version { get; init; }

    /// <summary>Where it loaded from, when present.</summary>
    public required string Origin { get; init; }

    /// <summary>The yak package that provides it.</summary>
    public required string Package { get; init; }

    /// <summary>Whether the id is grounded at a real capture or inferred.</summary>
    public required string Grounded { get; init; }

    /// <summary>Which workloads declare it.</summary>
    public required string NeededBy { get; init; }

    /// <summary>Green present, red missing, dim unknown.</summary>
    public string StateBrush => State switch
    {
        nameof(RequirementState.Present) => "#4EC94E",
        nameof(RequirementState.Missing) => "#FF6B68",
        _ => "#9A9A9A",
    };
}

/// <summary>
/// The Pretest tab — is this machine ready to be believed, before any test runs?
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Stage C4. Four layers of machine truth already existed with real
/// producers and no consumer joining more than two: machine facts
/// (<c>scripts/machine-survey.ps1</c>, which until now nothing in C# had ever read), content
/// readiness (<c>doctor</c>), in-app truth (the environment capture), and the harness itself
/// (<c>canary commission</c>). This is the first surface that shows them together.
/// </para>
/// <para>
/// <b>It reports and plans. It never changes the machine.</b> Operator ruling: no control on
/// this tab installs, unblocks or edits Developer Settings. The install plan is rendered and
/// the command to run it is shown for copying — deliberately not executed, because an install
/// performed before the machine was measured destroys the evidence it existed to provide.
/// </para>
/// <para>
/// It <i>does</i> launch the target application, for commissioning layers 2 and 3. Launching
/// to observe is not mutating, and it is the only way to answer whether capture is repeatable
/// here. The distinction is stated on the tab so nobody has to infer it.
/// </para>
/// <para>
/// This is a separate surface from Environment on purpose: that tab's class comment and its
/// tooltips promise it launches nothing, and bolting capture onto it would make shipped
/// documentation false.
/// </para>
/// </remarks>
public partial class PretestViewModel : ObservableObject
{
    private string? _workloadsDir;
    private CancellationTokenSource? _cts;
    private ProcessManager? _pm;

    /// <summary>Workloads found on disk.</summary>
    public ObservableCollection<string> Workloads { get; } = new();

    /// <summary>What the machine has, from the survey.</summary>
    public ObservableCollection<MachineFactRow> MachineFacts { get; } = new();

    /// <summary>The three commissioning layers.</summary>
    public ObservableCollection<PretestLayerRow> Layers { get; } = new();

    /// <summary>Declared plug-in requirements vs what loaded.</summary>
    public ObservableCollection<ReadinessRowVm> Readiness { get; } = new();

    /// <summary>Running commentary, newest last.</summary>
    public ObservableCollection<string> Log { get; } = new();

    [ObservableProperty]
    private string? _selectedWorkload;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SurveyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommissionCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private PretestState _state = PretestState.Idle;

    /// <summary>Machine, Canary build and derived tier — ruling 12's stamp.</summary>
    [ObservableProperty]
    private string _identity = "(not read yet)";

    /// <summary>Why the tier was derived as it was.</summary>
    [ObservableProperty]
    private string _tierEvidence = string.Empty;

    /// <summary>The headline verdict.</summary>
    [ObservableProperty]
    private string _verdict = "This machine has not been commissioned. Nothing here has been proven yet.";

    /// <summary>Colour for the verdict banner.</summary>
    [ObservableProperty]
    private string _verdictBrush = "#9A9A9A";

    /// <summary>The command that would fix what is missing — shown, never run.</summary>
    [ObservableProperty]
    private string _installCommand = string.Empty;

    /// <summary>Whether there is anything to install.</summary>
    [ObservableProperty]
    private bool _hasInstallPlan;

    [ObservableProperty]
    private string _statusText = "Open a workloads folder to begin.";

    /// <summary>Points the tab at a workloads root and reads what is already on disk.</summary>
    /// <param name="workloadsDir">Workloads root, or null to clear.</param>
    public void SetWorkloadsDir(string? workloadsDir)
    {
        _workloadsDir = workloadsDir;
        Workloads.Clear();
        if (workloadsDir != null && Directory.Exists(workloadsDir))
        {
            foreach (var d in Directory.GetDirectories(workloadsDir)
                         .Select(Path.GetFileName)
                         .Where(n => !string.IsNullOrEmpty(n)
                                     && File.Exists(Path.Combine(workloadsDir, n!, "workload.json"))
                                     && !string.Equals(n, MachineTier.CommissioningWorkload, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                Workloads.Add(d!);
            }
        }
        SelectedWorkload = Workloads.FirstOrDefault();
        Refresh();
    }

    partial void OnSelectedWorkloadChanged(string? value) => Refresh();

    private bool CanWork() => State == PretestState.Idle && _workloadsDir != null;
    private bool CanStop() => State != PretestState.Idle;

    /// <summary>Re-reads everything already on disk. Launches nothing.</summary>
    [RelayCommand]
    private void Refresh()
    {
        Layers.Clear();
        Readiness.Clear();

        if (_workloadsDir == null)
        {
            StatusText = "Open a workloads folder to begin.";
            return;
        }

        var machine = MachineIdentity.Describe(_workloadsDir);
        Identity = MachineIdentity.Format(machine);
        TierEvidence = machine.TryGetValue(MachineIdentity.TierEvidence, out var ev) ? ev : string.Empty;

        LoadCommissioning();
        LoadReadiness();
        StatusText = $"{Layers.Count} commissioning layer(s), {Readiness.Count} declared requirement(s) read from disk.";
    }

    /// <summary>
    /// Reads the machine — filesystem and registry only. <b>Launches no application.</b>
    /// </summary>
    /// <remarks>
    /// Shells out to <c>scripts/machine-survey.ps1</c> and reads the JSON it writes, rather
    /// than streaming its stdout: nothing in this UI has ever piped a child process's output
    /// into a view, and inventing that plumbing here would be a second way of doing a thing
    /// the script already does by writing a file. It also keeps ONE implementation of the
    /// survey — the script stays the zero-dependency path for a machine with no canary.exe,
    /// no SDK and no repo, and this is simply another consumer of its output.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task SurveyAsync()
    {
        if (_workloadsDir == null) return;
        var script = FindSurveyScript();
        if (script == null)
        {
            Append("machine-survey.ps1 not found beside this install - cannot read the machine.");
            return;
        }

        State = PretestState.Surveying;
        _cts = new CancellationTokenSource();
        Append("Reading the machine (no application is launched)...");
        try
        {
            var outFile = Path.Combine(Path.GetTempPath(), $"canary-survey-{Guid.NewGuid():N}.json");
            var ok = await Task.Run(() => RunSurvey(script, outFile, _cts.Token), _cts.Token).ConfigureAwait(true);

            if (ok && File.Exists(outFile))
            {
                LoadSurvey(outFile);
                Append($"Machine read: {MachineFacts.Count} facts.");
                try { File.Delete(outFile); } catch { }
            }
            else
            {
                Append("The survey produced no output.");
            }
        }
        catch (OperationCanceledException) { Append("Cancelled."); }
        catch (Exception ex) { Append($"Survey failed: {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            State = PretestState.Idle;
        }
    }

    /// <summary>
    /// Runs commissioning. <b>Launches the application to measure it — installs nothing.</b>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task CommissionAsync()
    {
        if (_workloadsDir == null || string.IsNullOrWhiteSpace(SelectedWorkload)) return;

        State = PretestState.Commissioning;
        _cts = new CancellationTokenSource();
        _pm = new ProcessManager();
        var workloadsDir = _workloadsDir;
        var workloadName = SelectedWorkload!;
        Append($"Commissioning against {workloadName} - the application will start, be measured, and close.");

        try
        {
            var referencesDir = Path.Combine(workloadsDir, MachineTier.CommissioningWorkload, Commissioner.ReferencesFolder);
            var outDir = ResultPaths.RollupDir(workloadsDir, MachineTier.CommissioningWorkload, null);
            var first = Path.Combine(outDir, "repeat-1.png");
            var second = Path.Combine(outDir, "repeat-2.png");

            var layers = new List<CommissioningLayer> { Commissioner.CheckComparer(referencesDir) };
            Append($"Layer 1 (comparer, no app): {layers[0].Outcome}.");

            var cfgPath = Path.Combine(workloadsDir, workloadName, "workload.json");
            var cfg = await WorkloadConfig.LoadAsync(cfgPath).ConfigureAwait(true);
            var logger = new Services.AvaloniaTestLogger(verbose: false);
            logger.MessageLogged += Append;

            var captured = await Task.Run(() =>
            {
                var runner = new TestRunner(_pm!, workloadsDir, logger, new BaselineLedger { Workload = workloadName });
                return runner.CaptureCommissioningFramesAsync(cfg, first, second, 800, 600, _cts.Token);
            }, _cts.Token).ConfigureAwait(true);

            layers.Add(captured
                ? Commissioner.CheckRepeatable(first, second)
                : new CommissioningLayer(2, "repeatable", LayerOutcome.NotRun,
                    $"{cfg.DisplayName} produced no captures - layer not attempted", true));
            layers.Add(Commissioner.CheckShippedReference(
                Path.Combine(referencesDir, $"{workloadName}-reference.png"), first));

            var report = new CommissioningReport
            {
                CapturedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Machine = MachineIdentity.Describe(workloadsDir),
                Workload = workloadName,
                Layers = layers,
            };
            report.Save(CommissioningReport.PathFor(workloadsDir));
            Append(report.HarnessUsable
                ? "Commissioning passed - results from this machine can be read."
                : "Commissioning FAILED - results from this machine are not readable.");
        }
        catch (OperationCanceledException) { Append("Cancelled."); }
        catch (Exception ex) { Append($"Commissioning failed: {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            _pm?.KillAll();
            _pm = null;
            _cts?.Dispose();
            _cts = null;
            State = PretestState.Idle;
            Refresh();
        }
    }

    /// <summary>Cancels whatever is running and closes anything it launched.</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _cts?.Cancel();
        _pm?.KillAll();
        _pm = null;
        Append("Stopping...");
    }

    /// <summary>Copies the install command to the clipboard. Does not run it.</summary>
    [RelayCommand]
    private void CopyInstallCommand()
    {
        // Assigned by the view - the VM never reaches for a clipboard or a window, matching
        // how every dialog in this app is done.
        CopyToClipboard?.Invoke(InstallCommand);
        StatusText = "Install command copied. It is not run from here - paste it in a terminal.";
    }

    /// <summary>Set by the view so the VM stays testable without a real clipboard.</summary>
    public Action<string>? CopyToClipboard { get; set; }

    private void LoadCommissioning()
    {
        if (_workloadsDir == null) return;
        try
        {
            var report = CommissioningReport.Load(CommissioningReport.PathFor(_workloadsDir));
            foreach (var l in report.Layers.OrderBy(l => l.Number))
            {
                Layers.Add(new PretestLayerRow
                {
                    Number = l.Number,
                    Name = l.Name,
                    Outcome = l.Outcome.ToString(),
                    Fatal = l.Fatal,
                    Detail = l.Detail,
                });
            }

            var fromHere = MachineIdentity.IsThisMachine(report.Machine);
            if (!fromHere)
            {
                Verdict = "This commissioning report came from a DIFFERENT machine. It says nothing about this one.";
                VerdictBrush = "#FF6B68";
            }
            else if (report.HarnessUsable)
            {
                Verdict = $"Harness proven here on {report.CapturedUtc}. Results from this machine can be read.";
                VerdictBrush = "#4EC94E";
            }
            else
            {
                Verdict = "THE HARNESS IS NOT PROVEN on this machine. No test result from it is readable yet.";
                VerdictBrush = "#FF6B68";
            }
        }
        catch (FileNotFoundException)
        {
            Verdict = "This machine has not been commissioned. Run it below - nothing else here can be trusted first.";
            VerdictBrush = "#9A9A9A";
        }
        catch (InvalidDataException ex)
        {
            // A corrupt report must not read as an absent one, and neither may read as a pass.
            Verdict = $"The commissioning report is unreadable: {ex.Message}";
            VerdictBrush = "#FF6B68";
        }
    }

    private void LoadReadiness()
    {
        if (_workloadsDir == null || string.IsNullOrWhiteSpace(SelectedWorkload)) return;

        var rows = InstallReadiness.ForWorkload(_workloadsDir, SelectedWorkload!);
        foreach (var r in rows)
        {
            Readiness.Add(new ReadinessRowVm
            {
                Id = r.Id,
                State = r.State.ToString(),
                Version = r.Version,
                Origin = r.Origin,
                Package = string.IsNullOrWhiteSpace(r.Package) ? "(no package provides this)" : r.Package,
                Grounded = r.Grounded,
                NeededBy = r.NeededBy,
            });
        }

        var missing = rows.Where(r => r.State == RequirementState.Missing && r.Package.Length > 0)
            .Select(r => r.Package).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        HasInstallPlan = missing.Count > 0;
        InstallCommand = missing.Count == 0
            ? string.Empty
            : $"powershell -File scripts\\machine-setup.ps1 -Apply -Only {string.Join(",", missing)}";
    }

    private string? FindSurveyScript()
    {
        foreach (var root in new[] { _workloadsDir == null ? null : Path.GetDirectoryName(_workloadsDir), AppContext.BaseDirectory })
        {
            var dir = root;
            for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir!, "scripts", "machine-survey.ps1");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
        }
        return null;
    }

    private static bool RunSurvey(string script, string outFile, CancellationToken ct)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-OutFile", outFile })
            proc.StartInfo.ArgumentList.Add(a);

        proc.Start();
        // Drained, not streamed: the survey's product is the JSON file. Leaving the pipes
        // unread would deadlock a chatty child on a full buffer.
        proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit(120_000);
        return !ct.IsCancellationRequested;
    }

    private void LoadSurvey(string jsonPath)
    {
        MachineFacts.Clear();
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;

        void Fact(string group, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) MachineFacts.Add(new MachineFactRow { Group = group, Name = name, Value = value! });
        }

        if (root.TryGetProperty("identity", out var id))
        {
            foreach (var p in id.EnumerateObject()) Fact("Machine", p.Name, p.Value.ToString());
        }
        if (root.TryGetProperty("toolchain", out var tc))
        {
            foreach (var p in tc.EnumerateObject())
            {
                Fact("Toolchain", p.Name, p.Value.ValueKind == JsonValueKind.Array
                    ? string.Join("; ", p.Value.EnumerateArray().Select(x => x.ToString()))
                    : p.Value.ToString());
            }
        }
        if (root.TryGetProperty("rhino", out var rh) && rh.TryGetProperty("installs", out var installs))
        {
            foreach (var i in installs.EnumerateArray())
            {
                Fact("Rhino",
                    i.TryGetProperty("version", out var v) ? v.ToString() : "(unknown version)",
                    i.TryGetProperty("dir", out var d) ? d.ToString() : string.Empty);
            }
        }
        if (root.TryGetProperty("repos", out var repos))
        {
            foreach (var r in repos.EnumerateArray().Take(40))
            {
                var dirty = r.TryGetProperty("dirty", out var dy) && dy.ValueKind == JsonValueKind.True ? " (dirty)" : string.Empty;
                Fact("Repos",
                    r.TryGetProperty("name", out var n) ? n.ToString() : "?",
                    $"{(r.TryGetProperty("branch", out var b) ? b.ToString() : "?")} @ " +
                    $"{(r.TryGetProperty("head", out var h) ? h.ToString() : "?")}{dirty}");
            }
        }
        if (root.TryGetProperty("yakPackages", out var yaks))
        {
            foreach (var y in yaks.EnumerateArray())
                Fact("Packages available", y.TryGetProperty("name", out var n) ? n.ToString() : "?", "staged");
        }
    }

    private void Append(string line) => Post(() =>
    {
        Log.Add(line);
        while (Log.Count > 300) Log.RemoveAt(0);
        StatusText = line;
    });

    // The runner fires its logger events on whatever thread the work is on, so every
    // collection touch is marshalled - the same Post helper TestRunnerViewModel uses.
    private static void Post(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}
