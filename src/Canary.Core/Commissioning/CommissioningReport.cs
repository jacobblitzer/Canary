using System.Text.Json;
using Canary.Orchestration;

namespace Canary.Commissioning;

/// <summary>What happened to one commissioning layer.</summary>
public enum LayerOutcome
{
    /// <summary>The layer could not be attempted. NOT a pass.</summary>
    /// <remarks>
    /// Load-bearing. A layer that did not run has answered nothing, and reporting that as a
    /// pass is how a machine talks itself into being trusted — the same defect as a missing
    /// baseline yielding <c>New</c> and <c>New</c> being excluded from the exit code.
    /// </remarks>
    NotRun,

    /// <summary>The layer ran and the machine satisfied it.</summary>
    Passed,

    /// <summary>The layer ran and the machine did not satisfy it.</summary>
    Failed,
}

/// <summary>One layer of ruling 7A.</summary>
/// <param name="Number">1, 2 or 3.</param>
/// <param name="Name">Short name, e.g. <c>comparer</c>.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Detail">What was measured, in numbers where numbers exist.</param>
/// <param name="Fatal">
/// Whether failing this layer means no test result on this machine is readable.
/// </param>
/// <param name="ContentFault">
/// Whether this layer could not be attempted because something did not arrive on this
/// machine - a reference image the payload never carried, an agent that was never installed
/// - as opposed to the harness running and disagreeing.
/// </param>
/// <remarks>
/// <para>
/// <b>Why the discriminator exists.</b> Every non-Passed fatal layer used to be reported by
/// doctor under one sentence: "This is NOT an install problem. Fix it with `canary commission`
/// first." On a machine set up from a payload, both fatal layers come back NotRun for
/// install and packaging reasons - the commissioning content did not travel, and the Rhino
/// agent is not registered - so that sentence asserted the exact opposite of the truth, and
/// its advice was a dead end.
/// </para>
/// <para>
/// A layer that ran and disagreed is a harness fault. A layer that could not start because
/// its inputs are absent is an install fault. They need different owners, so they need
/// different words.
/// </para>
/// </remarks>
public readonly record struct CommissioningLayer(
    int Number, string Name, LayerOutcome Outcome, string Detail, bool Fatal, bool ContentFault = false);

/// <summary>
/// The answer to "can this machine test at all?" — ruling 7A, three layers.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Stage C2. The campaign requires a <b>three-way distinction</b>, because
/// collapsing it wastes days: commissioning red means the harness is broken and any plug-in
/// result is unreadable; <c>doctor</c> red means the install is incomplete and is *not* a
/// defect in the plug-in; commissioning green + doctor green + smoke red is the only
/// combination that is a real finding.
/// </para>
/// <para>
/// So this <b>gates</b> plug-in results rather than merely preceding them, and it is stamped
/// per ruling 12 with machine id, Canary version and derived tier — a report that cannot say
/// which machine or which build produced it cannot be acted on.
/// </para>
/// </remarks>
public sealed class CommissioningReport
{
    /// <summary>The file name, under the commissioning workload's results.</summary>
    public const string FileName = "commissioning-report.json";

    /// <summary>When it ran, UTC, second precision.</summary>
    public string CapturedUtc { get; init; } = string.Empty;

    /// <summary>Machine id + Canary build + derived tier. See <see cref="MachineIdentity"/>.</summary>
    public IReadOnlyDictionary<string, string> Machine { get; init; } = new Dictionary<string, string>();

    /// <summary>Which workload supplied the app for layers 2 and 3, or empty.</summary>
    public string Workload { get; init; } = string.Empty;

    /// <summary>The three layers, in order.</summary>
    public IReadOnlyList<CommissioningLayer> Layers { get; init; } = Array.Empty<CommissioningLayer>();

    /// <summary>
    /// True only when every FATAL layer actually ran and passed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>NotRun</c> does not count as passing. A machine where layer 2 could not be attempted
    /// has not shown that capture is repeatable on it, and every pixel result from it is
    /// therefore unreadable — saying otherwise would be the silent-green defect this whole
    /// campaign exists to remove.
    /// </para>
    /// <para>
    /// Layer 3 is deliberately NOT fatal. It asks whether a baseline made elsewhere matches
    /// here; a machine that fails it can still test perfectly well, it simply cannot use
    /// travelled pixel baselines. Treating that as "the harness is broken" would report on a
    /// question it did not ask.
    /// </para>
    /// </remarks>
    public bool HarnessUsable =>
        Layers.Where(l => l.Fatal).All(l => l.Outcome == LayerOutcome.Passed)
        && Layers.Any(l => l.Fatal);

    /// <summary>The report's path for a workloads root.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <returns>Full path, whether or not it exists.</returns>
    public static string PathFor(string workloadsDir)
        => Path.Combine(ResultPaths.RollupDir(workloadsDir, Orchestration.MachineTier.CommissioningWorkload, null), FileName);

    /// <summary>Writes the report, creating the directory.</summary>
    /// <param name="path">Destination file.</param>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var payload = new Dictionary<string, object>
        {
            ["capturedUtc"] = CapturedUtc,
            ["workload"] = Workload,
            ["harnessUsable"] = HarnessUsable,
            ["machine"] = Machine,
            ["layers"] = Layers.Select(l => new Dictionary<string, object>
            {
                ["number"] = l.Number,
                ["name"] = l.Name,
                ["outcome"] = l.Outcome.ToString(),
                ["fatal"] = l.Fatal,
                ["contentFault"] = l.ContentFault,
                ["detail"] = l.Detail,
            }).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Reads a report.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The report.</returns>
    /// <exception cref="FileNotFoundException">Absent.</exception>
    /// <exception cref="InvalidDataException">Present but not readable as a report.</exception>
    /// <remarks>
    /// Throws rather than returning an empty report, for the same reason
    /// <see cref="EnvironmentCapture.Load"/> does: an unreadable report that renders as "no
    /// layers failed" is a confident false answer.
    /// </remarks>
    public static CommissioningReport Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("no commissioning report", path);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("root is not an object");

            var layers = new List<CommissioningLayer>();
            if (root.TryGetProperty("layers", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in arr.EnumerateArray())
                {
                    // An unreadable outcome becomes Failed, never Passed and never NotRun:
                    // a layer whose result cannot be read has not demonstrated anything, and
                    // the safe reading of "cannot tell" here is "do not trust this machine".
                    var outcome = Enum.TryParse<LayerOutcome>(Str(l, "outcome"), ignoreCase: true, out var o)
                        ? o
                        : LayerOutcome.Failed;
                    layers.Add(new CommissioningLayer(
                        l.TryGetProperty("number", out var n) && n.TryGetInt32(out var ni) ? ni : 0,
                        Str(l, "name"),
                        outcome,
                        Str(l, "detail"),
                        l.TryGetProperty("fatal", out var f) && f.ValueKind == JsonValueKind.True,
                        // Absent reads as false, so a report written by an older build is read
                        // as "this was a harness fault" - the louder of the two, and the one
                        // that stops a machine being trusted rather than the one that excuses it.
                        l.TryGetProperty("contentFault", out var cf) && cf.ValueKind == JsonValueKind.True));
                }
            }

            return new CommissioningReport
            {
                CapturedUtc = Str(root, "capturedUtc"),
                Workload = Str(root, "workload"),
                Machine = Map(root, "machine"),
                Layers = layers,
            };
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"{path} is not a readable commissioning report: {ex.Message}", ex);
        }
    }

    /// <summary>Renders the report for a console.</summary>
    /// <returns>Lines to print.</returns>
    public IReadOnlyList<string> Format()
    {
        var lines = new List<string>
        {
            $"machine        : {MachineIdentity.Format(Machine)}",
            $"commissioned   : {CapturedUtc}",
        };
        if (!string.IsNullOrWhiteSpace(Workload)) lines.Add($"app workload   : {Workload}");
        lines.Add(string.Empty);

        foreach (var l in Layers.OrderBy(l => l.Number))
        {
            var mark = l.Outcome switch
            {
                LayerOutcome.Passed => "PASS",
                LayerOutcome.Failed => l.Fatal ? "FAIL" : "warn",
                // A reader skimming for FAIL saw three [----] rows and no failure, directly
                // above "harness usable: NO". A fatal layer that never ran has to be as loud
                // on the page as one that ran and lost - it is the same verdict.
                LayerOutcome.NotRun => l.Fatal ? "STOP" : "----",
                _ => "----",
            };
            lines.Add($"  [{mark}] layer {l.Number} {l.Name,-12} {l.Detail}");
        }

        lines.Add(string.Empty);
        lines.Add(HarnessUsable
            ? "harness usable: yes - test results from this machine can be read."
            : "harness usable: NO - results from this machine are unreadable until this is fixed.");
        return lines;
    }

    private static Dictionary<string, string> Map(JsonElement root, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object) return map;
        foreach (var p in obj.EnumerateObject())
            map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? string.Empty) : p.Value.ToString();
        return map;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : string.Empty;
}
