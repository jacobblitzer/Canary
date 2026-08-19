using System.Text.Json;
using Canary.Agent;
using Canary.Config;

namespace Canary.Orchestration;

/// <summary>Whether one declared plug-in requirement is satisfied on this machine.</summary>
public enum RequirementState
{
    /// <summary>No capture was read, so this cannot be decided. NOT the same as missing.</summary>
    /// <remarks>
    /// Load-bearing. With nothing to compare against, "everything is missing" would be a
    /// confident false answer, and it is the answer that would send a setup pass installing
    /// things the machine already has.
    /// </remarks>
    Unknown,

    /// <summary>The host reported it loaded.</summary>
    Present,

    /// <summary>Declared, and the host did not report it.</summary>
    Missing,
}

/// <summary>One row of the readiness join.</summary>
/// <param name="Id">Requirement id, as the host reports it.</param>
/// <param name="State">Present / Missing / Unknown.</param>
/// <param name="Package">The yak package providing it, or empty when nothing does.</param>
/// <param name="Grounded">
/// <c>capture</c> when the id was read from a live host, <c>inferred</c> when it is a guess.
/// </param>
/// <param name="NeededBy">Which workloads declare it.</param>
/// <param name="Origin">Where it actually loaded from, when it is present.</param>
/// <param name="Version">The version the host reported, when it is present.</param>
public readonly record struct ReadinessRow(
    string Id, RequirementState State, string Package, string Grounded,
    string NeededBy, string Origin, string Version);

/// <summary>
/// Joins what the content DECLARES against what the host actually HAS, and names the package
/// that would close each gap.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Stage C4. The 210 declarations say what each workload needs;
/// <c>plugin-packages.json</c> says which yak package provides each id; an environment capture
/// says what the host reported. Needed − had = the install list.
/// </para>
/// <para>
/// <b>Known duplication, deliberately recorded:</b> <c>scripts/machine-setup.ps1</c> computes
/// this same join in PowerShell. Two implementations of one rule is the shape that produced
/// bug 0022, so they should converge — the script already requires <c>canary.exe</c> for its
/// re-capture step, so it can call this rather than reimplement it. Until it does, a change
/// to the join has to be made in both places.
/// </para>
/// </remarks>
public static class InstallReadiness
{
    /// <summary>The id→package map file, beside <c>tokens.json</c>.</summary>
    public const string PackageMapFileName = "plugin-packages.json";

    /// <summary>Reads the id→package map.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <returns>Id → (package, grounded). Empty when the map is absent or unreadable.</returns>
    /// <remarks>
    /// Absent is tolerated rather than fatal: the join is still useful without it — it just
    /// cannot name a fix. A missing map must not stop a machine reporting what it has.
    /// </remarks>
    /// <summary>Whether the id-to-package map is present at all.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <returns>True when the file exists.</returns>
    /// <remarks>
    /// Asked separately because <see cref="LoadPackageMap"/> returns an empty map for an
    /// absent file and for a file that lists nothing, and a caller that renders those the
    /// same way tells a machine with no map that nothing can be installed on it.
    /// </remarks>
    public static bool PackageMapExists(string workloadsDir)
        => File.Exists(Path.Combine(workloadsDir, PackageMapFileName));

    public static IReadOnlyDictionary<string, (string Package, string Grounded)> LoadPackageMap(string workloadsDir)
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(workloadsDir, PackageMapFileName);
        if (!File.Exists(path)) return map;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("packages", out var packages)) return map;

            foreach (var p in packages.EnumerateArray())
            {
                var name = p.TryGetProperty("package", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var grounded = p.TryGetProperty("grounded", out var g) ? g.GetString() ?? string.Empty : string.Empty;
                if (!p.TryGetProperty("ids", out var ids) || ids.ValueKind != JsonValueKind.Array) continue;
                foreach (var id in ids.EnumerateArray())
                {
                    var key = id.GetString();
                    if (!string.IsNullOrWhiteSpace(key)) map[key!] = (name, grounded);
                }
            }
        }
        catch
        {
            // A malformed map yields no fixes rather than a crash; doctor is where content
            // problems are reported, not here.
        }
        return map;
    }

    /// <summary>Builds the readiness rows for one workload.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <returns>One row per declared plug-in requirement, ordered by id.</returns>
    /// <remarks>
    /// Only <c>plugin</c> requirements: <c>file</c> and <c>service</c> are doctor's business
    /// and are not things a package manager can fix.
    /// </remarks>
    public static IReadOnlyList<ReadinessRow> ForWorkload(string workloadsDir, string workload)
    {
        var packageMap = LoadPackageMap(workloadsDir);

        WorkloadConfig? cfg = null;
        var wl = Path.Combine(workloadsDir, workload, "workload.json");
        if (File.Exists(wl))
        {
            try { cfg = WorkloadConfig.Parse(File.ReadAllText(wl)); } catch { }
        }

        var tests = new List<TestDefinition>();
        var testsDir = Path.Combine(workloadsDir, workload, "tests");
        if (Directory.Exists(testsDir))
        {
            foreach (var file in Directory.GetFiles(testsDir, "*.json"))
            {
                // One unparsable test must not hide every other declaration. Doctor reports
                // that fault; this surface reports what is declared.
                try { tests.Add(TestDefinition.Parse(File.ReadAllText(file))); } catch { }
            }
        }

        var declared = RequirementChecker.Collect(cfg, tests, workload)
            .Where(d => string.Equals(d.Requirement.Kind, Requirement.KindPlugin, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(d.Requirement.Id))
            .ToList();

        // What the host actually reported, if anything has ever asked it.
        var loaded = new Dictionary<string, EnvironmentReport.LoadedItem>(StringComparer.OrdinalIgnoreCase);
        var haveCapture = false;
        try
        {
            var capture = EnvironmentCapture.Load(EnvironmentCapture.PathFor(workloadsDir, workload));
            haveCapture = true;
            foreach (var item in EnvironmentReport.ParseLoaded(
                         capture.Host.TryGetValue(HostStateFields.Loaded, out var l) ? l : null))
            {
                loaded[item.Id] = item;
            }
        }
        catch
        {
            // No capture, or an unreadable one. Every row below is therefore Unknown, which
            // is the honest answer - not Missing.
        }

        var rows = new List<ReadinessRow>();
        foreach (var group in declared.GroupBy(d => d.Requirement.Id!, StringComparer.OrdinalIgnoreCase))
        {
            var id = group.Key;
            packageMap.TryGetValue(id, out var pkg);
            loaded.TryGetValue(id, out var item);

            var state = !haveCapture
                ? RequirementState.Unknown
                : loaded.ContainsKey(id) ? RequirementState.Present : RequirementState.Missing;

            rows.Add(new ReadinessRow(
                id,
                state,
                pkg.Package ?? string.Empty,
                pkg.Grounded ?? string.Empty,
                string.Join(", ", group.Select(g => g.DeclaredBy).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(3)),
                state == RequirementState.Present ? item.Origin.ToString().ToLowerInvariant() : string.Empty,
                state == RequirementState.Present ? item.Version : string.Empty));
        }

        return rows.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
