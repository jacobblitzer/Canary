using Canary.Agent;

namespace Canary.Orchestration;

/// <summary>How much a finding matters.</summary>
public enum ClashSeverity
{
    /// <summary>Worth knowing; does not by itself stop anything working.</summary>
    Note,

    /// <summary>Something is wrong or wasteful, but the content may still run.</summary>
    Warning,

    /// <summary>A dependency the content needs is not usable.</summary>
    Error,
}

/// <summary>One thing wrong, or worth knowing, about a host's plug-in environment.</summary>
/// <param name="Severity">How much it matters.</param>
/// <param name="Kind">Short slug, e.g. <c>present-but-not-loaded</c>.</param>
/// <param name="Detail">What was found, naming the specific artifact.</param>
public readonly record struct EnvironmentClash(ClashSeverity Severity, string Kind, string Detail);

/// <summary>
/// Turns a host-state answer into an environment report: everything loaded, everything
/// found, and the clashes between them.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 5b, at the operator's request — "the plugins that grasshopper
/// loads. all of them. would show loading clashes."
/// </para>
/// <para>
/// <b>This is the QC instrument, not a diagnostic nicety.</b> "Did this install correctly" is
/// answered in practice by capturing the environment on a known-good machine, capturing it on
/// the target, and diffing. That is why the report is data first and prose second.
/// </para>
/// <para>
/// <b>It never repairs anything.</b> It names what it found and stops. The one time this
/// campaign touched an install decision it was the operator who fixed an unregistered plug-in
/// in Developer Settings — a tool that had "helpfully" registered it somewhere else would
/// have hidden the question rather than answered it.
/// </para>
/// </remarks>
public static class EnvironmentReport
{
    /// <summary>A loaded library, parsed out of the flat host-state rows.</summary>
    /// <param name="Id">Namespaced id, e.g. <c>gh:Slop</c>.</param>
    /// <param name="Version">Reported version, or empty.</param>
    /// <param name="Location">Full path it loaded from, or empty.</param>
    /// <param name="Origin">Where that location sits.</param>
    public readonly record struct LoadedItem(string Id, string Version, string Location, PluginOrigin Origin);

    /// <summary>Parses the <c>loaded</c> rows into structured items.</summary>
    /// <param name="loaded">Newline-delimited <c>id=version@location</c>.</param>
    /// <returns>The parsed items.</returns>
    public static IReadOnlyList<LoadedItem> ParseLoaded(string? loaded)
    {
        var items = new List<LoadedItem>();
        if (string.IsNullOrWhiteSpace(loaded)) return items;

        foreach (var line in loaded.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) { items.Add(new LoadedItem(line.Trim(), string.Empty, string.Empty, PluginOrigin.Unknown)); continue; }

            var id = line.Substring(0, eq).Trim();
            var detail = line.Substring(eq + 1).Trim();
            var at = detail.LastIndexOf('@');
            var ver = at >= 0 ? detail.Substring(0, at) : detail;
            var loc = at >= 0 ? detail.Substring(at + 1) : string.Empty;
            items.Add(new LoadedItem(id, ver, loc, PluginOrigins.Classify(loc)));
        }
        return items;
    }

    /// <summary>
    /// Finds the clashes in a host-state answer.
    /// </summary>
    /// <param name="data">The <c>GetHostState</c> payload.</param>
    /// <returns>Findings, most severe first.</returns>
    public static IReadOnlyList<EnvironmentClash> Analyse(IReadOnlyDictionary<string, string> data)
    {
        var findings = new List<EnvironmentClash>();

        data.TryGetValue(HostStateFields.Loaded, out var loadedRaw);
        data.TryGetValue(HostStateFields.Discovered, out var discoveredRaw);
        data.TryGetValue(HostStateFields.ScanFolders, out var foldersRaw);
        data.TryGetValue(HostStateFields.LoadErrors, out var loadErrors);

        var loaded = ParseLoaded(loadedRaw);
        var loadedPaths = new HashSet<string>(
            loaded.Where(l => l.Location.Length > 0).Select(l => Normalise(l.Location)),
            StringComparer.OrdinalIgnoreCase);

        // --- 1. PRESENT BUT NOT LOADED. The one that cost an afternoon. ------------
        var discovered = Split(discoveredRaw);
        foreach (var file in discovered)
        {
            if (loadedPaths.Contains(Normalise(file))) continue;
            findings.Add(new EnvironmentClash(ClashSeverity.Warning, "present-but-not-loaded",
                $"{file} sits on a scanned folder but the host did not register it. " +
                "Presence is not loaded - check Developer Settings, and the host's load errors"));
        }

        // --- 2. Load failures. Invisible from the loaded list by definition. -------
        foreach (var e in Split(loadErrors))
        {
            findings.Add(new EnvironmentClash(ClashSeverity.Warning, "load-error", e.Trim()));
        }

        // --- 3. One id, two locations ---------------------------------------------
        foreach (var g in loaded.GroupBy(l => l.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var where = string.Join(" AND ", g.Select(x => x.Location.Length > 0 ? x.Location : "(unknown)"));
            findings.Add(new EnvironmentClash(ClashSeverity.Error, "duplicate-id",
                $"{g.Key} is loaded more than once: {where}"));
        }

        // --- 4. Two files of the same NAME on the scan surface --------------------
        // Not necessarily loaded twice - GH picks one - but which one it picked is then
        // luck, and an install that updates the other has no visible effect.
        foreach (var g in discovered
                     .GroupBy(f => System.IO.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            findings.Add(new EnvironmentClash(ClashSeverity.Warning, "same-file-two-places",
                $"{g.Key} exists in {g.Count()} scanned locations: {string.Join(" AND ", g)}. " +
                "Whichever the host picks, updating the other one will appear to do nothing"));
        }

        // --- 5. Dead scan folders -------------------------------------------------
        foreach (var row in Split(foldersRaw))
        {
            var bar = row.LastIndexOf('|');
            if (bar <= 0) continue;
            if (row.Substring(bar + 1).Trim().Equals("MISSING", StringComparison.OrdinalIgnoreCase))
                findings.Add(new EnvironmentClash(ClashSeverity.Note, "dead-scan-folder",
                    $"{row.Substring(0, bar)} is configured as a plug-in folder but does not exist"));
        }

        // --- 6. Loaded from a developer folder ------------------------------------
        // A NOTE on a dev machine, where it is the point. It becomes an error only when a
        // requirement pins origin - which is how QC and USER content says so.
        foreach (var l in loaded.Where(x => x.Origin == PluginOrigin.Developer && x.Location.Length > 0))
        {
            findings.Add(new EnvironmentClash(ClashSeverity.Note, "developer-origin",
                $"{l.Id} loaded from {l.Location} - a build output or hand-added folder, not a deployed install"));
        }

        return findings
            .OrderByDescending(f => (int)f.Severity)
            .ThenBy(f => f.Kind, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Renders the report for a console.</summary>
    /// <param name="data">The <c>GetHostState</c> payload.</param>
    /// <param name="findings">Result of <see cref="Analyse"/>.</param>
    /// <returns>Lines to print.</returns>
    public static IReadOnlyList<string> Format(
        IReadOnlyDictionary<string, string> data, IReadOnlyList<EnvironmentClash> findings)
    {
        var lines = new List<string>();
        data.TryGetValue(HostStateFields.Host, out var host);
        data.TryGetValue(HostStateFields.HostVersion, out var hostVer);
        data.TryGetValue(HostStateFields.Framework, out var framework);
        data.TryGetValue(HostStateFields.HostReady, out var ready);

        lines.Add($"host           : {host ?? "?"} {hostVer ?? string.Empty}".TrimEnd());
        if (!string.IsNullOrWhiteSpace(framework)) lines.Add($"framework      : {framework}");
        lines.Add($"ready          : {ready ?? "?"}");

        var loaded = ParseLoaded(data.TryGetValue(HostStateFields.Loaded, out var l) ? l : null);
        lines.Add($"loaded         : {loaded.Count}");
        foreach (var g in loaded.GroupBy(x => x.Origin).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
            lines.Add($"  {g.Key.ToString().ToLowerInvariant(),-10} {g.Count()}");

        var discovered = Split(data.TryGetValue(HostStateFields.Discovered, out var d) ? d : null);
        var folders = Split(data.TryGetValue(HostStateFields.ScanFolders, out var f) ? f : null);
        if (folders.Count > 0) lines.Add($"scan folders   : {folders.Count}");
        if (discovered.Count > 0) lines.Add($"discovered     : {discovered.Count} loadable file(s) on those folders");

        lines.Add(string.Empty);
        if (findings.Count == 0)
        {
            lines.Add("no environment clashes found.");
            return lines;
        }

        foreach (var group in findings.GroupBy(x => x.Severity).OrderByDescending(g => (int)g.Key))
        {
            lines.Add($"{group.Key.ToString().ToUpperInvariant()} ({group.Count()})");
            foreach (var c in group) lines.Add($"  [{c.Kind}] {c.Detail}");
        }
        return lines;
    }

    private static List<string> Split(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? new List<string>()
            : s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    // Compared case-insensitively with separators normalised: the host reports one spelling
    // and the filesystem walk another, and a mismatch here would invent a
    // present-but-not-loaded finding for every single file.
    private static string Normalise(string path) =>
        path.Trim().Replace(System.IO.Path.DirectorySeparatorChar, '/').TrimEnd('/');
}
