using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Canary.Config;

namespace Canary.Orchestration;

/// <summary>Which resolution rule a ledger operation should use.</summary>
public enum LedgerLayout
{
    /// <summary>
    /// Today's behaviour: flat (<c>results/&lt;test&gt;/</c>) first, then any
    /// suite-nested (<c>results/&lt;suite&gt;/&lt;test&gt;/</c>). Used to lock the ledger
    /// GREEN under the pre-cutover code.
    /// </summary>
    Dual,

    /// <summary>The post-cutover contract: flat only.</summary>
    Flat,
}

/// <summary>One armed checkpoint and the baseline that was approved for it.</summary>
public sealed class BaselineRow
{
    /// <summary>Test name (the <c>name</c> field of the test definition).</summary>
    [JsonPropertyName("test")]
    public string Test { get; set; } = string.Empty;

    /// <summary>Checkpoint name within that test.</summary>
    [JsonPropertyName("checkpoint")]
    public string Checkpoint { get; set; } = string.Empty;

    /// <summary>Declared comparison mode at lock time.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "pixel-diff";

    /// <summary>SHA256 of the approved PNG, lowercase hex.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>When the approved PNG was last written, as an ISO-8601 UTC instant.</summary>
    [JsonPropertyName("approvedUtc")]
    public string ApprovedUtc { get; set; } = string.Empty;
}

/// <summary>What a scan found, so the caller can gate on it before writing.</summary>
/// <param name="Rows">Resolvable armed checkpoints, sorted.</param>
/// <param name="Armed">Armed checkpoints declared.</param>
/// <param name="CaptureOnly">Checkpoints opted out of comparison.</param>
/// <param name="Vlm">Checkpoints explicitly VLM-judged.</param>
/// <param name="ResolvedFlat">Armed checkpoints whose baseline was found flat.</param>
/// <param name="ResolvedNested">Armed checkpoints whose baseline was found suite-nested.</param>
/// <param name="Unresolved">Armed checkpoints with no baseline anywhere.</param>
/// <param name="UnparsableTests">Test files that could not be read.</param>
public readonly record struct LedgerScan(
    IReadOnlyList<BaselineRow> Rows,
    int Armed,
    int CaptureOnly,
    int Vlm,
    int ResolvedFlat,
    int ResolvedNested,
    int Unresolved,
    IReadOnlyList<string> UnparsableTests);

/// <summary>What a verify found.</summary>
/// <param name="Ok">Rows whose baseline resolved with a matching hash.</param>
/// <param name="Missing">Rows whose baseline did not resolve at all — <b>hard</b> errors.</param>
/// <param name="Changed">Rows that resolved but whose bytes differ — <b>soft</b>, a warning.</param>
public readonly record struct LedgerVerification(
    int Ok,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Changed)
{
    /// <summary>True when nothing is missing. Hash drift alone does not fail.</summary>
    public bool IsSatisfied => Missing.Count == 0;
}

/// <summary>
/// The git-tracked record of which checkpoints have an approved baseline.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 2b. The harness could report a pass while comparing
/// nothing: a missing baseline yields <c>New</c>, and <c>New</c> is excluded from the
/// exit code. Any change that relocates where baselines are looked up therefore turns
/// every affected test green-but-blind. Six suites are in that state <b>today</b> —
/// 32 tests, 59 approved PNGs the run path cannot see, because approval wrote them
/// suite-nested and the shared run path reads flat.
/// </para>
/// <para>
/// <b>Why a file, and why outside <c>results/</c>.</b> Every other candidate guard
/// consults something that lives inside the directory whose location is the variable
/// under change — prior run records, the baselines themselves — so it is
/// constitutionally incapable of detecting the class it was written for. This ledger is
/// keyed on <b>identity</b> (test + checkpoint), not on path, and sits next to the
/// workload where git tracks it: <c>.gitignore</c> excludes <c>results/</c>, so the
/// 322 PNGs have <b>no</b> git recovery, but this file has.
/// </para>
/// <para>
/// <b>Presence is hard; content is soft.</b> A row whose baseline is missing is a
/// failure. A row whose bytes changed is a warning, so approve-then-run stays
/// frictionless and every re-blessing shows up as a reviewable git diff instead of
/// blocking a migration.
/// </para>
/// </remarks>
public sealed class BaselineLedger
{
    /// <summary>File name, relative to the workload directory.</summary>
    public const string FileName = "baselines.lock.json";

    /// <summary>Schema version.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Workload this ledger describes.</summary>
    [JsonPropertyName("workload")]
    public string Workload { get; set; } = string.Empty;

    /// <summary>The armed checkpoints, sorted by (test, checkpoint).</summary>
    [JsonPropertyName("rows")]
    public List<BaselineRow> Rows { get; set; } = new();

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Absolute path of a workload's ledger.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <returns>The ledger path, whether or not it exists.</returns>
    public static string PathFor(string workloadsDir, string workload)
        => Path.Combine(workloadsDir, workload, FileName);

    /// <summary>
    /// Loads a workload's ledger, <b>throwing</b> when it is absent, empty or corrupt.
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <returns>The ledger.</returns>
    /// <exception cref="FileNotFoundException">No ledger for this workload.</exception>
    /// <exception cref="InvalidDataException">Present but unusable.</exception>
    /// <remarks>
    /// Fail closed, deliberately. <b>An absent ledger is not an empty ledger.</b> If a
    /// missing file silently meant "nothing is armed", then deleting the file — or
    /// shipping a payload that omits it — would disable the guard while every run still
    /// printed a pass, which is the exact failure this class exists to prevent. A
    /// workload with nothing armed carries a committed <c>"rows": []</c>: empty by
    /// declaration is legal and reviewable, empty by absence is an error.
    /// </remarks>
    public static BaselineLedger LoadRequired(string workloadsDir, string workload)
    {
        var path = PathFor(workloadsDir, workload);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"{FileName} not found for workload '{workload}'. Run 'canary baselines lock --workload {workload}'. " +
                "An absent ledger is not an empty ledger.", path);

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException($"{path} is empty. An absent ledger is not an empty ledger.");

        BaselineLedger? led;
        try
        {
            led = JsonSerializer.Deserialize<BaselineLedger>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not valid JSON: {ex.Message}", ex);
        }

        if (led is null)
            throw new InvalidDataException($"{path} deserialized to nothing.");

        return led;
    }

    /// <summary>Loads a ledger if one is present and readable, else null.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <returns>The ledger, or null.</returns>
    /// <remarks>For advisory callers (reporting, diagnostics) that must not throw.</remarks>
    public static BaselineLedger? TryLoad(string workloadsDir, string workload)
    {
        try { return LoadRequired(workloadsDir, workload); }
        catch (FileNotFoundException) { return null; }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>Writes the ledger, rows sorted so a change is a readable diff.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    public void Save(string workloadsDir)
    {
        Rows = Rows
            .OrderBy(r => r.Test, StringComparer.Ordinal)
            .ThenBy(r => r.Checkpoint, StringComparer.Ordinal)
            .ToList();

        var path = PathFor(workloadsDir, Workload);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, WriteOptions) + Environment.NewLine);
    }

    /// <summary>Finds a row by identity.</summary>
    /// <param name="test">Test name.</param>
    /// <param name="checkpoint">Checkpoint name.</param>
    /// <returns>The row, or null when this checkpoint is not ledgered.</returns>
    public BaselineRow? Find(string test, string checkpoint)
        => Rows.FirstOrDefault(r =>
            string.Equals(r.Test, test, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Checkpoint, checkpoint, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves where a baseline lives under a given layout rule.
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <param name="test">Test name.</param>
    /// <param name="checkpoint">Checkpoint name.</param>
    /// <param name="layout">Which rule to apply.</param>
    /// <returns>The resolved path, or null when nothing is there.</returns>
    /// <remarks>
    /// The <see cref="LedgerLayout.Flat"/> answer is <see cref="ResultPaths"/>'s and only
    /// its — this class composes no path of its own (TODO(phase-2b-C3), redeemed).
    /// <see cref="LedgerLayout.Dual"/> survives as a LEGACY-READ mode: it is what let the
    /// ledger be locked green under the pre-cutover code, which is the only reason the
    /// migration could be proven safe before it ran. It has no other use, and nothing on
    /// the run path calls it.
    /// </remarks>
    public static string? ResolveBaseline(
        string workloadsDir, string workload, string test, string checkpoint, LedgerLayout layout)
    {
        var flat = ResultPaths.BaselinePath(workloadsDir, workload, test, checkpoint);
        if (File.Exists(flat)) return flat;
        if (layout == LedgerLayout.Flat) return null;

        var results = ResultPaths.ResultsRoot(workloadsDir, workload);

        if (!Directory.Exists(results)) return null;
        foreach (var dir in Directory.EnumerateDirectories(results))
        {
            var scope = Path.GetFileName(dir);
            if (string.Equals(scope, test, StringComparison.OrdinalIgnoreCase)) continue;
            var nested = Path.Combine(dir, test, "baselines", $"{checkpoint}.png");
            if (File.Exists(nested)) return nested;
        }
        return null;
    }

    /// <summary>
    /// Scans content and disk to produce the rows a lock would write.
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <param name="layout">Resolution rule to apply.</param>
    /// <returns>The scan, including the counts a caller should gate on.</returns>
    /// <remarks>
    /// Reads the <b>filesystem</b>, not a prior ledger, so the first lock is grounded in
    /// what is actually approved rather than in a belief about it.
    /// </remarks>
    public static LedgerScan Scan(string workloadsDir, string workload, LedgerLayout layout)
    {
        var testsDir = Path.Combine(workloadsDir, workload, "tests");
        var rows = new List<BaselineRow>();
        var unparsable = new List<string>();
        int armed = 0, capture = 0, vlm = 0, flat = 0, nested = 0, unresolved = 0;

        if (!Directory.Exists(testsDir))
            return new LedgerScan(rows, 0, 0, 0, 0, 0, 0, unparsable);

        var resultsPrefix = ResultPaths.ResultsRoot(workloadsDir, workload) + Path.DirectorySeparatorChar;

        foreach (var file in Directory.GetFiles(testsDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            TestDefinition def;
            try
            {
                def = TestDefinition.Parse(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                // Counted and named, never silently skipped: eleven qualia tests have
                // been invalid JSON for months precisely because nothing added them up.
                unparsable.Add($"{Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            foreach (var cp in def.Checkpoints)
            {
                if (CheckpointArming.IsCaptureOnly(cp)) { capture++; continue; }
                if (CheckpointArming.IsExplicitVlm(cp)) { vlm++; continue; }
                armed++;

                var path = ResolveBaseline(workloadsDir, workload, def.Name, cp.Name, layout);
                if (path is null) { unresolved++; continue; }

                var rel = path.StartsWith(resultsPrefix, StringComparison.OrdinalIgnoreCase)
                    ? path.Substring(resultsPrefix.Length)
                    : path;
                if (rel.Replace('\\', '/').StartsWith(def.Name + "/", StringComparison.OrdinalIgnoreCase))
                    flat++;
                else
                    nested++;

                rows.Add(new BaselineRow
                {
                    Test = def.Name,
                    Checkpoint = cp.Name,
                    Mode = string.IsNullOrWhiteSpace(cp.Mode) ? "pixel-diff" : cp.Mode,
                    Sha256 = HashFile(path),
                    ApprovedUtc = File.GetLastWriteTimeUtc(path).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                });
            }
        }

        return new LedgerScan(rows, armed, capture, vlm, flat, nested, unresolved, unparsable);
    }

    /// <summary>
    /// Checks every row against disk.
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="layout">Resolution rule to apply.</param>
    /// <returns>The verification result.</returns>
    public LedgerVerification Verify(string workloadsDir, LedgerLayout layout)
    {
        var missing = new List<string>();
        var changed = new List<string>();
        var ok = 0;

        foreach (var r in Rows)
        {
            var path = ResolveBaseline(workloadsDir, Workload, r.Test, r.Checkpoint, layout);
            if (path is null)
            {
                missing.Add($"{r.Test}/{r.Checkpoint} — ledgered {r.ApprovedUtc} but no baseline resolves " +
                            $"(layout {layout.ToString().ToLowerInvariant()})");
                continue;
            }

            if (!string.Equals(HashFile(path), r.Sha256, StringComparison.OrdinalIgnoreCase))
                changed.Add($"{r.Test}/{r.Checkpoint} — bytes differ from the ledgered hash ({path})");
            else
                ok++;
        }

        return new LedgerVerification(ok, missing, changed);
    }

    /// <summary>SHA256 of a file, lowercase hex.</summary>
    /// <param name="path">File to hash.</param>
    /// <returns>Lowercase hex digest.</returns>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
