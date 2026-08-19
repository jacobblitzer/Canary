using System.Text.Json;

namespace Canary.Orchestration;

/// <summary>
/// One machine's environment capture — the <c>environment.json</c> contract, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 5b. This type exists because of a lesson paid for the same day it
/// was written: <see cref="TestRunner"/> serialised this file as an inline anonymous object and
/// the UI parsed it back by hand, with nothing connecting the two spellings. That is precisely
/// the shape of bug 0022 — an agent writing <c>grasshopperReady</c> while its reader looked for
/// <c>hostReady</c>, a mismatch that made a whole guard silently never run. Four parties now
/// read or write this file (the runner, <c>canary env</c>, <c>canary doctor</c>, the
/// Environment tab), so the shape is declared once and the field names are constants.
/// </para>
/// <para>
/// <b>Its job is to be diffed.</b> "Did this install correctly" is answered in practice by
/// capturing on a known-good machine, capturing on the target, and comparing — which is why
/// <see cref="Machine"/> is not optional decoration and why serialisation is stable and
/// indented rather than compact.
/// </para>
/// </remarks>
public sealed class EnvironmentCapture
{
    /// <summary>The file name, under a workload's results root.</summary>
    public const string FileName = "environment.json";

    /// <summary>JSON field: when the capture was taken (UTC, second precision).</summary>
    public const string FieldCapturedUtc = "capturedUtc";

    /// <summary>JSON field: which workload was probed.</summary>
    public const string FieldWorkload = "workload";

    /// <summary>JSON field: which machine the capture came from.</summary>
    public const string FieldMachine = "machine";

    /// <summary>JSON field: the raw <c>GetHostState</c> payload.</summary>
    public const string FieldHost = "host";

    /// <summary>JSON field: the analysed clashes.</summary>
    public const string FieldFindings = "findings";

    /// <summary>JSON field inside a finding: severity name.</summary>
    public const string FieldSeverity = "severity";

    /// <summary>JSON field inside a finding: kind slug.</summary>
    public const string FieldKind = "kind";

    /// <summary>JSON field inside a finding: detail text.</summary>
    public const string FieldDetail = "detail";

    /// <summary>When the capture was taken, UTC, second precision.</summary>
    public string CapturedUtc { get; init; } = string.Empty;

    /// <summary>Which workload was probed.</summary>
    public string Workload { get; init; } = string.Empty;

    /// <summary>Which machine it came from. See <see cref="MachineIdentity"/>.</summary>
    public IReadOnlyDictionary<string, string> Machine { get; init; } =
        new Dictionary<string, string>();

    /// <summary>The host's raw answer, field names per <c>HostStateFields</c>.</summary>
    public IReadOnlyDictionary<string, string> Host { get; init; } =
        new Dictionary<string, string>();

    /// <summary>The analysed clashes, most severe first.</summary>
    public IReadOnlyList<EnvironmentClash> Findings { get; init; } = Array.Empty<EnvironmentClash>();

    /// <summary>The capture's path for a workload.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <returns>Full path, whether or not it exists.</returns>
    /// <remarks>
    /// Routed through <see cref="ResultPaths"/> so this file lands beside the results rather
    /// than in a second place only this type knows about.
    /// </remarks>
    public static string PathFor(string workloadsDir, string workload)
        => Path.Combine(ResultPaths.RollupDir(workloadsDir, workload, null), FileName);

    /// <summary>Builds a capture of the machine this process is running on.</summary>
    /// <param name="workload">Workload name.</param>
    /// <param name="host">The host's <c>GetHostState</c> payload.</param>
    /// <param name="findings">Result of <see cref="EnvironmentReport.Analyse(IReadOnlyDictionary{string, string}, IReadOnlyDictionary{string, string}?)"/>.</param>
    /// <param name="capturedUtc">Capture time; defaults to now.</param>
    /// <returns>The capture.</returns>
    public static EnvironmentCapture Create(
        string workload,
        IReadOnlyDictionary<string, string> host,
        IReadOnlyList<EnvironmentClash> findings,
        DateTime? capturedUtc = null)
        => new()
        {
            CapturedUtc = (capturedUtc ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Workload = workload,
            Machine = MachineIdentity.Describe(),
            Host = host,
            Findings = findings,
        };

    /// <summary>Writes the capture, creating the directory.</summary>
    /// <param name="path">Destination file.</param>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var payload = new Dictionary<string, object>
        {
            [FieldCapturedUtc] = CapturedUtc,
            [FieldWorkload] = Workload,
            [FieldMachine] = Machine,
            [FieldHost] = Host,
            [FieldFindings] = Findings.Select(c => new Dictionary<string, string>
            {
                [FieldSeverity] = c.Severity.ToString(),
                [FieldKind] = c.Kind,
                [FieldDetail] = c.Detail,
            }).ToList(),
        };

        File.WriteAllText(path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Reads a capture.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The capture.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable capture.</exception>
    /// <remarks>
    /// Throws rather than returning an empty capture. An unreadable capture that reads as "this
    /// machine has nothing loaded" is worse than no capture at all — the caller decides how to
    /// present the failure, but it does not get to miss it.
    /// </remarks>
    public static EnvironmentCapture Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("no environment capture", path);

        try
        {
            // UTF8 with or without a BOM: a BOM already cost this campaign one silently
            // unverified manifest row, and JsonDocument rejects it outright.
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("root is not an object");

            return new EnvironmentCapture
            {
                CapturedUtc = Str(root, FieldCapturedUtc),
                Workload = Str(root, FieldWorkload),
                Machine = Map(root, FieldMachine),
                Host = Map(root, FieldHost),
                Findings = ReadFindings(root),
            };
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"{path} is not a readable environment capture: {ex.Message}", ex);
        }
    }

    /// <summary>Parses <see cref="CapturedUtc"/>.</summary>
    /// <returns>The capture time, or null when unparseable or absent.</returns>
    public DateTime? CapturedAt()
        => DateTime.TryParse(CapturedUtc, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var t)
            ? t
            : null;

    /// <summary>How old the capture is, or null when the time is unreadable.</summary>
    /// <param name="now">Reference time, UTC; defaults to now.</param>
    /// <returns>The age.</returns>
    public TimeSpan? Age(DateTime? now = null)
        => CapturedAt() is { } t ? (now ?? DateTime.UtcNow) - t : null;

    /// <summary>True when this capture came from the machine reading it.</summary>
    public bool IsFromThisMachine() => MachineIdentity.IsThisMachine(Machine);

    /// <summary>
    /// Why this capture should not be read at face value, or empty when there is no caveat.
    /// </summary>
    /// <param name="staleAfter">How old a capture may be before it is worth mentioning.</param>
    /// <param name="now">Reference time, UTC; defaults to now.</param>
    /// <returns>One sentence, or empty.</returns>
    /// <remarks>
    /// <para>
    /// Lives here rather than at each display site because it was written twice within an hour
    /// — once for the Environment tab and once for <c>canary env --show</c> — and the two
    /// copies immediately disagreed: the CLI announced a capture as coming FROM ANOTHER MACHINE
    /// when it merely predated machine identity and could not say. That is the third appearance
    /// of the same shape in one day (see bug 0022), so the rule gets one home.
    /// </para>
    /// <para>
    /// Three distinct situations, deliberately not collapsed:
    /// a capture naming a different machine is evidence of the wrong machine; a capture naming
    /// none has simply not established anything, which is milder; and an old capture from the
    /// right machine is only a question of age.
    /// </para>
    /// </remarks>
    public string Caveat(TimeSpan? staleAfter = null, DateTime? now = null)
    {
        var namesAMachine = Machine.TryGetValue(MachineIdentity.MachineName, out var name)
                            && !string.IsNullOrWhiteSpace(name);

        if (!IsFromThisMachine())
        {
            return namesAMachine
                ? $"This capture is from {MachineIdentity.Format(Machine)} — not this machine " +
                  $"({Environment.MachineName}). It says nothing about the machine you are on."
                : "This capture does not record which machine it came from, so it cannot vouch " +
                  "for this one. Re-capture to confirm.";
        }

        var limit = staleAfter ?? TimeSpan.FromDays(7);
        return Age(now) is { } age && age > limit
            ? $"This capture is {(int)age.TotalDays} days old; plug-ins may have moved since."
            : string.Empty;
    }

    /// <summary>One difference between two captures.</summary>
    /// <param name="Kind">Short slug: <c>only-here</c>, <c>only-there</c>, <c>origin</c>, <c>version</c>, <c>host</c>, <c>scan-folder</c>.</param>
    /// <param name="Detail">What differs, naming the item.</param>
    public readonly record struct CaptureDifference(string Kind, string Detail);

    /// <summary>
    /// Compares this capture against another — "did this install correctly", mechanised.
    /// </summary>
    /// <param name="other">The capture to compare against, typically a known-good machine.</param>
    /// <param name="thisLabel">Name for this side in the output.</param>
    /// <param name="otherLabel">Name for the other side.</param>
    /// <returns>Differences, most consequential first.</returns>
    /// <remarks>
    /// <para>
    /// The whole point of writing this file was to diff two machines, and until now diffing
    /// meant reading two JSON documents side by side and holding 96 loaded libraries in your
    /// head. A comparison you have to perform by eye is one that gets skipped on the machine
    /// where it matters.
    /// </para>
    /// <para>
    /// Ordered so the answers that decide "is this install correct" come first: something
    /// present on one machine and absent on the other, then the same library loading from a
    /// different KIND of location — which is how an install silently loses to a build-output
    /// folder — then version skew, then the host itself.
    /// </para>
    /// </remarks>
    public IReadOnlyList<CaptureDifference> DiffAgainst(
        EnvironmentCapture other, string thisLabel = "this", string otherLabel = "other")
    {
        var diffs = new List<CaptureDifference>();

        var mine = EnvironmentReport.ParseLoaded(Host.TryGetValue(Agent.HostStateFields.Loaded, out var a) ? a : null)
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);
        var theirs = EnvironmentReport.ParseLoaded(other.Host.TryGetValue(Agent.HostStateFields.Loaded, out var b) ? b : null)
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var id in mine.Keys.Where(k => !theirs.ContainsKey(k)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new CaptureDifference("only-here", $"{id} is loaded on {thisLabel} but not on {otherLabel}"));

        foreach (var id in theirs.Keys.Where(k => !mine.ContainsKey(k)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new CaptureDifference("only-there", $"{id} is loaded on {otherLabel} but not on {thisLabel}"));

        foreach (var id in mine.Keys.Where(theirs.ContainsKey).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var m = mine[id];
            var t = theirs[id];
            // Origin before version: a library loading from a developer folder on one machine
            // and a package on the other is a DIFFERENT INSTALL, not a different build.
            if (m.Origin != t.Origin)
            {
                diffs.Add(new CaptureDifference("origin",
                    $"{id} loads from {m.Origin.ToString().ToLowerInvariant()} on {thisLabel} " +
                    $"({m.Location}) but {t.Origin.ToString().ToLowerInvariant()} on {otherLabel} ({t.Location})"));
            }
            if (!string.Equals(m.Version, t.Version, StringComparison.OrdinalIgnoreCase))
                diffs.Add(new CaptureDifference("version", $"{id} is {m.Version} on {thisLabel}, {t.Version} on {otherLabel}"));
        }

        foreach (var key in new[] { Agent.HostStateFields.Host, Agent.HostStateFields.HostVersion, Agent.HostStateFields.Framework })
        {
            Host.TryGetValue(key, out var mv);
            other.Host.TryGetValue(key, out var tv);
            if (!string.Equals(mv ?? string.Empty, tv ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                diffs.Add(new CaptureDifference("host", $"{key}: '{mv}' on {thisLabel}, '{tv}' on {otherLabel}"));
        }

        var myFolders = Folders(this);
        var theirFolders = Folders(other);
        foreach (var p in myFolders.Except(theirFolders, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new CaptureDifference("scan-folder", $"{p} is scanned on {thisLabel} only"));
        foreach (var p in theirFolders.Except(myFolders, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new CaptureDifference("scan-folder", $"{p} is scanned on {otherLabel} only"));

        var rank = new Dictionary<string, int>
        {
            ["only-there"] = 0, ["only-here"] = 1, ["origin"] = 2,
            ["version"] = 3, ["host"] = 4, ["scan-folder"] = 5,
        };
        return diffs.OrderBy(d => rank.TryGetValue(d.Kind, out var r) ? r : 9).ToList();
    }

    private static HashSet<string> Folders(EnvironmentCapture c)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!c.Host.TryGetValue(Agent.HostStateFields.ScanFolders, out var raw) || string.IsNullOrWhiteSpace(raw))
            return set;
        foreach (var row in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var bar = row.LastIndexOf('|');
            set.Add((bar > 0 ? row.Substring(0, bar) : row).Trim());
        }
        return set;
    }

    private static IReadOnlyList<EnvironmentClash> ReadFindings(JsonElement root)
    {
        var findings = new List<EnvironmentClash>();
        if (!root.TryGetProperty(FieldFindings, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return findings;

        foreach (var item in arr.EnumerateArray())
        {
            // An unrecognised severity becomes a Warning, never a Note: a finding whose
            // importance cannot be read must not be quietly demoted to the tier nobody looks at.
            var severity = Enum.TryParse<ClashSeverity>(Str(item, FieldSeverity), ignoreCase: true, out var s)
                ? s
                : ClashSeverity.Warning;
            findings.Add(new EnvironmentClash(severity, Str(item, FieldKind), Str(item, FieldDetail)));
        }
        return findings;
    }

    private static Dictionary<string, string> Map(JsonElement root, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object) return map;
        foreach (var p in obj.EnumerateObject())
        {
            map[p.Name] = p.Value.ValueKind == JsonValueKind.String
                ? (p.Value.GetString() ?? string.Empty)
                : p.Value.ToString();
        }
        return map;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? string.Empty)
            : string.Empty;
}
