using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Canary.Agent;
using Canary.Config;
using Canary.Orchestration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

/// <summary>One plug-in or library the host reported as loaded.</summary>
public sealed class EnvironmentPluginRow
{
    /// <summary>Namespaced id, e.g. <c>gh:Slop</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Reported version, or blank.</summary>
    public required string Version { get; init; }

    /// <summary>Full path it loaded from, or blank.</summary>
    public required string Location { get; init; }

    /// <summary>package / libraries / developer / unknown.</summary>
    public required string Origin { get; init; }
}

/// <summary>One environment finding.</summary>
public sealed class EnvironmentFindingRow
{
    /// <summary>Error / Warning / Note.</summary>
    public required string Severity { get; init; }

    /// <summary>Short slug, e.g. <c>present-but-not-loaded</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>What was found.</summary>
    public required string Detail { get; init; }

    /// <summary>
    /// Severity as a colour: red for Error, <b>yellow for Warning</b>, dim for Note.
    /// </summary>
    /// <remarks>
    /// Operator ruling 2026-08-18 — deviations from what was declared are to be marked
    /// "yellow". A severity you have to read the word for is a severity nobody triages.
    /// </remarks>
    public string SeverityBrush => Severity.ToLowerInvariant() switch
    {
        "error" => "#FF6B68",
        "warning" => "#E8C547",
        _ => "#9A9A9A",
    };
}

/// <summary>One declared requirement, with its last checked status.</summary>
public sealed class EnvironmentRequirementRow
{
    /// <summary>Human-readable requirement, from <see cref="Requirement.Describe"/>.</summary>
    public required string Requirement { get; init; }

    /// <summary>Which workload or test declared it.</summary>
    public required string DeclaredBy { get; init; }

    /// <summary>OK / MISSING / in-app only / not checked.</summary>
    public required string Status { get; init; }

    /// <summary>Why it missed, or the fix hint.</summary>
    public required string Detail { get; init; }
}

/// <summary>One folder the host was told to scan.</summary>
public sealed class EnvironmentFolderRow
{
    /// <summary>The configured path.</summary>
    public required string Path { get; init; }

    /// <summary>Whether it exists on this machine.</summary>
    public required string Exists { get; init; }
}

/// <summary>
/// The Environment tab — what each target application ACTUALLY has loaded, where it loaded
/// it from, and the clashes between what is present and what is registered.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 5b, at the operator's request: "i want to see in canary, as part
/// of canary's health/setup/doctor/environment monitering the plugins that grasshopper loads.
/// all of them. would show loading clashes."
/// </para>
/// <para>
/// <b>The plug-in list reads a captured report; it does not launch anything.</b> Every run
/// writes <c>results/environment.json</c>, so the data is already on disk, and a tab that
/// silently started Rhino to fill itself in would be a surprising thing for a click to do.
/// The capture timestamp is shown for exactly this reason — a stale report should <i>look</i>
/// stale rather than authoritative. Only the host itself can answer what it registered, so
/// there is no honest way to refresh that half without a run.
/// </para>
/// <para>
/// <b>The requirement check is live, because it can be.</b> file and service requirements are
/// decidable from outside the application, so the Check button answers them on the spot with
/// no launch. plugin requirements are shown as in-app only rather than silently omitted —
/// leaving them out would make a half-checked machine read as fully checked.
/// </para>
/// <para>
/// This is also the QC comparison surface: the same JSON captured on two machines is what
/// "did this install correctly" actually reduces to.
/// </para>
/// </remarks>
public partial class EnvironmentViewModel : ObservableObject
{
    private string? _workloadsDir;

    /// <summary>Workloads found on disk, for the picker.</summary>
    public ObservableCollection<string> Workloads { get; } = new();

    /// <summary>Everything the host reported as loaded.</summary>
    public ObservableCollection<EnvironmentPluginRow> Plugins { get; } = new();

    /// <summary>Clashes, most severe first.</summary>
    public ObservableCollection<EnvironmentFindingRow> Findings { get; } = new();

    /// <summary>Folders the host was told to scan, with existence.</summary>
    public ObservableCollection<EnvironmentFolderRow> ScanFolders { get; } = new();

    /// <summary>Requirements this workload's content declares.</summary>
    public ObservableCollection<EnvironmentRequirementRow> Requirements { get; } = new();

    [ObservableProperty]
    private string? _selectedWorkload;

    [ObservableProperty]
    private string _hostSummary = "no capture on this machine yet";

    [ObservableProperty]
    private string _capturedAt = string.Empty;

    [ObservableProperty]
    private string _statusText = "Open a workloads folder to see this machine's environment.";

    /// <summary>Which machine the capture came from.</summary>
    [ObservableProperty]
    private string _machineSummary = string.Empty;

    /// <summary>
    /// Why the displayed capture should not be trusted at face value, or empty.
    /// </summary>
    [ObservableProperty]
    private string _captureWarning = string.Empty;

    /// <summary>True when the capture came from a different machine than this one.</summary>
    [ObservableProperty]
    private bool _isForeignCapture;

    /// <summary>Whether there is a caveat worth showing.</summary>
    public bool HasCaptureWarning => !string.IsNullOrWhiteSpace(CaptureWarning);

    partial void OnCaptureWarningChanged(string value) => OnPropertyChanged(nameof(HasCaptureWarning));

    /// <summary>Points the tab at a workloads root and re-scans.</summary>
    /// <param name="workloadsDir">Workloads root, or null to clear.</param>
    public void SetWorkloadsDir(string? workloadsDir)
    {
        _workloadsDir = workloadsDir;
        Workloads.Clear();
        foreach (var name in SafeWorkloads()) Workloads.Add(name);

        // Assigning SelectedWorkload only fires OnSelectedWorkloadChanged when the value
        // actually differs, so Refresh explicitly rather than relying on the hook — the
        // same-name-different-root case would otherwise show the previous root's report.
        SelectedWorkload = Workloads.FirstOrDefault();
        Refresh();
    }

    partial void OnSelectedWorkloadChanged(string? value) => Refresh();

    /// <summary>Re-reads the captured report and the declarations for the selected workload.</summary>
    [RelayCommand]
    private void Refresh()
    {
        Plugins.Clear();
        Findings.Clear();
        ScanFolders.Clear();
        Requirements.Clear();
        HostSummary = "no capture on this machine yet";
        CapturedAt = string.Empty;
        MachineSummary = string.Empty;
        CaptureWarning = string.Empty;
        IsForeignCapture = false;

        if (_workloadsDir == null)
        {
            StatusText = "Open a workloads folder to see this machine's environment.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedWorkload))
        {
            StatusText = $"No workloads found under {_workloadsDir}.";
            return;
        }

        LoadDeclared(SelectedWorkload!);
        LoadReport(SelectedWorkload!);
    }

    /// <summary>
    /// Checks the offline-decidable requirements right now, with no application launch.
    /// </summary>
    [RelayCommand]
    private async Task CheckAsync()
    {
        if (_workloadsDir == null || string.IsNullOrWhiteSpace(SelectedWorkload)) return;

        var declared = Declared(SelectedWorkload!);
        if (declared.Count == 0)
        {
            StatusText = $"'{SelectedWorkload}' declares no requirements yet — nothing to check.";
            return;
        }

        IReadOnlyList<RequirementMiss> misses;
        try
        {
            misses = await RequirementChecker
                .CheckOfflineAsync(declared, _workloadsDir)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Check failed: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        // Keyed on Describe(), which Collect() already de-duplicates on, so one entry per row.
        var missByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in misses) missByKey[m.Requirement.Describe()] = m.Reason;

        Requirements.Clear();
        var checkable = 0;
        foreach (var (req, who) in declared)
        {
            var key = req.Describe();
            var offline = req.IsOfflineCheckable;
            if (offline) checkable++;

            var (status, detail) = !offline
                ? ("in-app only", "only the running application can answer this — it is judged during a run")
                : missByKey.TryGetValue(key, out var reason)
                    ? ("MISSING", string.IsNullOrWhiteSpace(req.Fix) ? reason : $"{reason} — fix: {req.Fix}")
                    : ("OK", string.Empty);

            Requirements.Add(new EnvironmentRequirementRow
            {
                Requirement = key,
                DeclaredBy = who,
                Status = status,
                Detail = detail,
            });
        }

        var missing = Requirements.Count(r => r.Status == "MISSING");
        var inApp = Requirements.Count - checkable;
        StatusText = $"checked {checkable} of {Requirements.Count} declared requirement(s) offline: "
                   + $"{checkable - missing} OK, {missing} missing"
                   + (inApp > 0 ? $"; {inApp} can only be judged inside the application" : string.Empty);
    }

    /// <summary>Reveals the captured JSON in the file manager.</summary>
    [RelayCommand]
    private void OpenReport()
    {
        if (_workloadsDir == null || string.IsNullOrWhiteSpace(SelectedWorkload)) return;
        var path = ReportPath(SelectedWorkload!);
        if (!File.Exists(path)) { StatusText = "No capture to open — run any test in this workload first."; return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { "/select,", path },
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { StatusText = $"Could not open: {ex.GetType().Name}: {ex.Message}"; }
    }

    private void LoadReport(string workload)
    {
        var path = ReportPath(workload);
        if (!File.Exists(path))
        {
            StatusText = $"No capture for '{workload}'. Run `canary env --workload {workload}`, or any test "
                       + "in this workload — only the host can report what it loaded.";
            return;
        }

        try
        {
            // Through EnvironmentCapture, not hand-rolled JSON reading: this view and
            // TestRunner used to spell the field names separately, which is the shape of
            // bug 0022.
            var capture = EnvironmentCapture.Load(path);
            CapturedAt = capture.CapturedUtc;
            MachineSummary = MachineIdentity.Format(capture.Machine);

            // A capture from another machine is the QC trap: a results tree copied between
            // machines makes the target look verified when nothing on it was probed.
            IsForeignCapture = !capture.IsFromThisMachine();
            // One rule, in EnvironmentCapture: this and `canary env --show` used to each own a
            // copy and immediately disagreed about what an unidentified capture meant.
            CaptureWarning = capture.Caveat();

            var host = capture.Host;
            var loaded = EnvironmentReport.ParseLoaded(Get(host, HostStateFields.Loaded));
            foreach (var item in loaded
                         .OrderBy(x => x.Origin.ToString(), StringComparer.Ordinal)
                         .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
            {
                Plugins.Add(new EnvironmentPluginRow
                {
                    Id = item.Id,
                    Version = item.Version,
                    Location = item.Location,
                    Origin = item.Origin.ToString().ToLowerInvariant(),
                });
            }

            // "path|exists" per HostStateFields.ScanFolders.
            foreach (var row in SplitLines(Get(host, HostStateFields.ScanFolders)))
            {
                var bar = row.LastIndexOf('|');
                ScanFolders.Add(bar > 0
                    ? new EnvironmentFolderRow { Path = row.Substring(0, bar).Trim(), Exists = row.Substring(bar + 1).Trim() }
                    : new EnvironmentFolderRow { Path = row.Trim(), Exists = "?" });
            }

            foreach (var c in capture.Findings)
            {
                Findings.Add(new EnvironmentFindingRow
                {
                    Severity = c.Severity.ToString(),
                    Kind = c.Kind,
                    Detail = c.Detail,
                });
            }

            var name = Get(host, HostStateFields.Host);
            var version = Get(host, HostStateFields.HostVersion);
            var framework = Get(host, HostStateFields.Framework);
            var ready = Get(host, HostStateFields.HostReady);
            HostSummary = $"{(string.IsNullOrWhiteSpace(name) ? "?" : name)} {version}".Trim()
                        + (string.IsNullOrWhiteSpace(framework) ? string.Empty : $"   ·   {framework}")
                        + (string.IsNullOrWhiteSpace(ready) ? string.Empty : $"   ·   ready: {ready}");

            var partial = Get(host, HostStateFields.PartialFailures);
            var errors = Findings.Count(x => x.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
            var warnings = Findings.Count(x => x.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));
            StatusText = $"{Plugins.Count} loaded · {ScanFolders.Count} scan folder(s) · "
                       + $"{errors} error, {warnings} warning, {Findings.Count - errors - warnings} note"
                       + (string.IsNullOrWhiteSpace(partial) ? string.Empty : $"  ·  PARTIAL PROBE: {partial}");
        }
        catch (Exception ex)
        {
            // A corrupt report must not read as a clean one.
            StatusText = $"Could not read {path}: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void LoadDeclared(string workload)
    {
        foreach (var (req, who) in Declared(workload))
        {
            Requirements.Add(new EnvironmentRequirementRow
            {
                Requirement = req.Describe(),
                DeclaredBy = who,
                Status = req.IsOfflineCheckable ? "not checked" : "in-app only",
                Detail = req.IsOfflineCheckable ? string.Empty : "judged during a run, inside the application",
            });
        }
    }

    private IReadOnlyList<(Requirement Requirement, string DeclaredBy)> Declared(string workload)
    {
        if (_workloadsDir == null) return Array.Empty<(Requirement, string)>();
        try
        {
            var wl = Path.Combine(_workloadsDir, workload, "workload.json");
            var cfg = File.Exists(wl) ? WorkloadConfig.Parse(File.ReadAllText(wl)) : null;

            var tests = new List<TestDefinition>();
            var testsDir = Path.Combine(_workloadsDir, workload, "tests");
            if (Directory.Exists(testsDir))
            {
                foreach (var file in Directory.GetFiles(testsDir, "*.json"))
                {
                    // One unparseable test must not hide every other declaration; doctor is
                    // where a bad definition is reported, not here.
                    try { tests.Add(TestDefinition.Parse(File.ReadAllText(file))); }
                    catch { }
                }
            }

            return RequirementChecker.Collect(cfg, tests, workload);
        }
        catch
        {
            return Array.Empty<(Requirement, string)>();
        }
    }

    private string ReportPath(string workload)
        => Path.Combine(ResultPaths.RollupDir(_workloadsDir!, workload, null), "environment.json");

    private IEnumerable<string> SafeWorkloads()
    {
        if (_workloadsDir == null || !Directory.Exists(_workloadsDir)) return Array.Empty<string>();
        try
        {
            return Directory.GetDirectories(_workloadsDir)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static string? Get(IReadOnlyDictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) ? v : null;

    private static IEnumerable<string> SplitLines(string? s)
        => string.IsNullOrWhiteSpace(s)
            ? Array.Empty<string>()
            : s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
}
