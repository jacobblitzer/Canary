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

    /// <summary>Running doctor — reads content and captures, launches nothing.</summary>
    Checking,
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

    /// <summary>Doctor's own verdict — the install signal, distinct from the harness one.</summary>
    [ObservableProperty]
    private string _doctorVerdict = "Not run.";

    /// <summary>Doctor's exit code, or null when it has not run here.</summary>
    [ObservableProperty]
    private int? _doctorExit;

    /// <summary>Green on 0, red on anything else, dim when it has not run.</summary>
    public string DoctorBrush => DoctorExit switch { 0 => "#4EC94E", null => "#9A9A9A", _ => "#FF6B68" };

    partial void OnDoctorExitChanged(int? value) => OnPropertyChanged(nameof(DoctorBrush));

    /// <summary>Every line doctor printed, verbatim.</summary>
    public ObservableCollection<string> DoctorLines { get; } = new();

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

    /// <summary>
    /// Runs <c>doctor</c> in-process. <b>Reads content and captures; launches nothing.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// In-process rather than shelling out to canary.exe, because the answer wanted here is
    /// the exit CODE as much as the text, and scraping a verdict back out of stdout is how a
    /// distinction gets collapsed. The three signals stay separate only if each is read from
    /// its own producer.
    /// </para>
    /// <para>
    /// The first version of the Pretest report carried commissioning and the machine survey
    /// but no doctor verdict, so a reader who saw a green harness and a green survey had no
    /// way to tell whether the INSTALL was complete - and the campaign's whole point is that
    /// "harness broken", "install incomplete" and "plug-in defective" are three different
    /// findings with three different owners.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task RunDoctorAsync()
    {
        if (_workloadsDir == null) return;

        State = PretestState.Checking;
        _cts = new CancellationTokenSource();
        DoctorLines.Clear();
        var workloadsDir = _workloadsDir;
        var workloadName = SelectedWorkload;
        Append($"Checking the install with doctor ({workloadName ?? "all workloads"}) - nothing is launched...");

        try
        {
            var logger = new Services.AvaloniaTestLogger(verbose: false);
            void Capture(string line) { DoctorLines.Add(line); Append(line); }
            logger.MessageLogged += Capture;
            logger.SummaryLogged += Capture;

            var exit = await Canary.Cli.DoctorCommand
                .RunAsync(workloadName, null, workloadsDir, logger)
                .ConfigureAwait(true);

            logger.MessageLogged -= Capture;
            logger.SummaryLogged -= Capture;

            DoctorExit = exit;
            DoctorVerdict = exit == 0
                ? "Install complete for what this content declares."
                : "INSTALL INCOMPLETE - doctor found something this content needs and this machine does not have. "
                  + "This is not a defect in any plug-in.";
            StatusText = $"doctor exited {exit}. {DoctorVerdict}";
        }
        catch (OperationCanceledException) { Append("Cancelled."); }
        catch (Exception ex)
        {
            // An exception is not a pass. Leaving DoctorExit null would render as "not run",
            // which is the one reading that must never follow from a failure.
            DoctorExit = -1;
            DoctorVerdict = $"doctor could not complete: {ex.GetType().Name}: {ex.Message}";
            Append(DoctorVerdict);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            State = PretestState.Idle;
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

    /// <summary>Copies the whole tab as one pasteable report.</summary>
    [RelayCommand]
    private void CopyReport()
    {
        CopyToClipboard?.Invoke(BuildReport());
        StatusText = "Full report copied — paste it to an agent, or into the QC write-up.";
    }

    /// <summary>
    /// Renders everything on this tab as one block of Markdown.
    /// </summary>
    /// <returns>The report.</returns>
    /// <remarks>
    /// <para>
    /// Operator feedback, 2026-08-19: every panel here was readable and none of it was
    /// copyable in one go — and the actual workflow on the QC machine is pasting state back
    /// and forth with an agent. A surface whose findings have to be retyped is one whose
    /// findings get summarised from memory, and a summarised-from-memory machine state is
    /// exactly what this campaign exists to replace.
    /// </para>
    /// <para>
    /// Markdown rather than JSON: the audience is a person pasting into a chat, and the
    /// machine-readable form already exists on disk as <c>commissioning-report.json</c> and
    /// <c>environment.json</c>. This says where those are, so a reader who wants the raw
    /// article can go and get it.
    /// </para>
    /// <para>
    /// It states the exit-code semantics inline, because the three-way distinction between a
    /// broken harness, an incomplete install and a broken plug-in is the thing most likely to
    /// be collapsed by whoever reads this out of context.
    /// </para>
    /// </remarks>
    public string BuildReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Canary pretest — {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"**{Verdict}**");
        sb.AppendLine();
        sb.AppendLine($"- machine: `{Identity}`");
        if (!string.IsNullOrWhiteSpace(TierEvidence)) sb.AppendLine($"- tier evidence: `{TierEvidence}`");
        sb.AppendLine($"- app workload: `{SelectedWorkload ?? "(none selected)"}`");
        sb.AppendLine();

        sb.AppendLine("## Commissioning — can this machine test at all?");
        sb.AppendLine();
        if (Layers.Count == 0)
        {
            sb.AppendLine("_Not commissioned. Nothing below has been proven._");
        }
        else
        {
            sb.AppendLine("| # | layer | outcome | fatal | detail |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var l in Layers)
                sb.AppendLine($"| {l.Number} | {l.Name} | **{l.Outcome}** | {(l.Fatal ? "yes" : "no")} | {l.Detail} |");
            sb.AppendLine();
            sb.AppendLine("_A fatal layer failing, or never running, means no result from this machine is readable._");
            sb.AppendLine("_Layer 3 is not fatal: failing it means pixel baselines do not TRAVEL here, not that the harness is broken._");
        }
        sb.AppendLine();

        sb.AppendLine("## Install — what doctor says");
        sb.AppendLine();
        if (DoctorExit == null)
        {
            sb.AppendLine("_doctor has not been run here. Press \"Check install\" — it launches nothing._");
            sb.AppendLine("_This is not a pass: an unrun check and a passing check are different answers._");
        }
        else
        {
            sb.AppendLine($"**exit {DoctorExit}** — {DoctorVerdict}");
            if (DoctorLines.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("```");
                foreach (var line in DoctorLines) sb.AppendLine(line);
                sb.AppendLine("```");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Installed vs declared");
        sb.AppendLine();
        if (Readiness.Count == 0)
        {
            sb.AppendLine("_No plug-in requirements declared for this workload._");
        }
        else
        {
            sb.AppendLine("| state | requirement | version | origin | provided by | id grounded |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var r in Readiness)
                sb.AppendLine($"| **{r.State}** | `{r.Id}` | {r.Version} | {r.Origin} | {r.Package} | {r.Grounded} |");
            sb.AppendLine();
            sb.AppendLine("_`Unknown` means no capture has been taken — it is NOT the same as missing._");
            sb.AppendLine("_An `inferred` id has never been observed on a real machine; installing is safe, trusting the id is not._");
            sb.AppendLine("_A `developer` origin shadows a deployed install, so an install can report success while old code runs._");
        }
        sb.AppendLine();

        if (HasInstallPlan)
        {
            sb.AppendLine("## Install plan (NOT run from the UI)");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(InstallCommand);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## This machine");
        sb.AppendLine();
        if (MachineFacts.Count == 0)
        {
            sb.AppendLine("_Not surveyed. Press \"Read this machine\" — it launches nothing._");
        }
        else
        {
            foreach (var group in MachineFacts.GroupBy(f => f.Group))
            {
                sb.AppendLine($"**{group.Key}**");
                foreach (var f in group) sb.AppendLine($"- {f.Name}: `{f.Value}`");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Exit codes, so these are not collapsed");
        sb.AppendLine();
        sb.AppendLine("- `canary commission` **4** — the harness is broken; every result here is unreadable");
        sb.AppendLine("- `canary doctor` **1** — the install is incomplete; NOT a defect in the plug-in");
        sb.AppendLine("- run path **3** — a declared precondition is missing");
        sb.AppendLine();
        sb.AppendLine("Machine-readable originals: `workloads/commissioning/results/commissioning-report.json`, " +
                      "`workloads/<w>/results/environment.json`.");

        if (Log.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("<details><summary>Log</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var line in Log.TakeLast(60)) sb.AppendLine(line);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
        }

        return sb.ToString();
    }

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
                // An id with no entry in plugin-packages.json has no grounding claim either
                // way, and a blank cell reads as "grounded: no" rather than "nobody has said".
                Grounded = string.IsNullOrWhiteSpace(r.Grounded) ? "(unmapped)" : r.Grounded,
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

    /// <summary>Renders a survey JSON file into the machine-facts table.</summary>
    /// <param name="jsonPath">The file <c>machine-survey.ps1 -OutFile</c> wrote.</param>
    /// <remarks>
    /// <b>internal</b> so its rendering can be tested against a fixture without running
    /// PowerShell. Three of the four defects the operator found in the first pasted report
    /// were in here rather than in what the survey measured - a blank row, a duplicated OS
    /// string, ten empty repo lines - and every one of them was a rendering choice.
    /// </remarks>
    internal void LoadSurvey(string jsonPath)
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
            foreach (var p in id.EnumerateObject())
            {
                // Skip what the stamp above already carries. The survey reads the OS product
                // name from CIM and the stamp reads RuntimeInformation.OSDescription, so
                // showing both put two different-looking OS strings in one report.
                if (p.Name is "machineName" or "user" or "os") continue;
                Fact("Machine", p.Name, p.Value.ToString());
            }
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
            // The FOLDER is the identity and the VERSION is what may be missing - the first
            // version of this had them the other way round, so a Rhino 9 WIP folder with no
            // Rhino.exe rendered as a blank row. An install that cannot report a version is
            // itself worth seeing: more than one Rhino on a machine raises "which one did the
            // test actually use", which nothing else here answers.
            foreach (var i in installs.EnumerateArray())
            {
                var dir = i.TryGetProperty("dir", out var d) ? d.ToString() : "(unknown folder)";
                var ver = i.TryGetProperty("version", out var v) ? v.ToString() : string.Empty;
                var exe = i.TryGetProperty("exe", out var e) ? e.ToString() : string.Empty;
                Fact("Rhino", dir, string.IsNullOrWhiteSpace(ver)
                    ? (string.IsNullOrWhiteSpace(exe) ? "NO Rhino.exe found - cannot report a version" : "(no version reported)")
                    : ver);
            }
        }
        if (root.TryGetProperty("repos", out var repos))
        {
            // Only actual checkouts. The survey lists every directory under the root, so a
            // scratch folder or a cache appears with an empty branch and head - and a blank
            // row in a pasted table reads as "fine" rather than "not a repo".
            var skipped = 0;
            foreach (var r in repos.EnumerateArray())
            {
                var isGit = r.TryGetProperty("isGit", out var g) && g.ValueKind == JsonValueKind.True;
                if (!isGit) { skipped++; continue; }
                var dirty = r.TryGetProperty("dirty", out var dy) && dy.ValueKind == JsonValueKind.True ? " (dirty)" : string.Empty;
                Fact("Repos",
                    r.TryGetProperty("name", out var n) ? n.ToString() : "?",
                    $"{(r.TryGetProperty("branch", out var b) ? b.ToString() : "?")} @ " +
                    $"{(r.TryGetProperty("head", out var h) ? h.ToString() : "?")}{dirty}");
            }
            if (skipped > 0)
                Fact("Repos", "(not checkouts)", $"{skipped} director(ies) under the root are not git repos - omitted");
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
