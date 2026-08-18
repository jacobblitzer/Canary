using System.Text.Json.Serialization;

namespace Canary.Config;

/// <summary>
/// Something a machine must already have for a workload or test to run at all.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 5. The question this answers is <b>"did this install
/// correctly"</b>, not "does the picture match" — no verdict about images is involved.
/// </para>
/// <para>
/// Before this, the only precondition in the corpus was PROSE. <c>bristle-03</c>'s
/// description reads "PRECONDITION (machine-2 feedback 2026-08-16): needs the decoded
/// Lightro bundle at %CANARY_REPO_LIGHTRO%/decoded/bundles/IMG_0007". A human reads that;
/// nothing verified it. On 2026-08-17 an unregistered <c>Slop.gha</c> cost a 300-second
/// timeout that logged nothing, while <c>canary doctor</c> exited 0 throughout.
/// </para>
/// <para>
/// <b>Exactly three kinds, deliberately.</b> A survey of all five workloads found 130
/// distinct requirements, and every one of them fits <c>file</c>, <c>service</c> or
/// <c>plugin</c>. Each extra kind is a checker somebody has to write and maintain, so the
/// vocabulary stays at what the corpus actually justifies rather than becoming a
/// general-purpose dependency language. In particular there are no versions, ranges or
/// severities: version skew is real here but it is a <i>warning</i>, and a version grammar
/// is a large maintenance surface for a small return.
/// </para>
/// </remarks>
public sealed class Requirement
{
    /// <summary>Requirement kind: <c>file</c>, <c>service</c> or <c>plugin</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// For <c>file</c>: a path, token-expanded, satisfied by a file OR a directory.
    /// </summary>
    /// <remarks>
    /// One kind covers executables, Grasshopper graphs, assets, decoded bundles,
    /// <c>node_modules</c>, built <c>dist/</c> trees and app binaries, because checking any
    /// of them is the same syscall. The survey found 39 assets, 200 graphs and 6 fixtures
    /// that would otherwise have been three kinds for no gain.
    /// </remarks>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>For <c>service</c>: a URL that must answer 2xx.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>For <c>service</c>: optional substring the response body must contain.</summary>
    [JsonPropertyName("contains")]
    public string? Contains { get; set; }

    /// <summary>
    /// For <c>plugin</c>: <c>gh:&lt;Name&gt;</c>, <c>rhino:&lt;Name&gt;</c> or
    /// <c>js:&lt;hook&gt;</c> — the same namespace the host-state verb reports in.
    /// </summary>
    /// <remarks>
    /// A <c>plugin</c> can only be checked from inside the running app: Grasshopper's
    /// library table and Rhino's plug-in table exist nowhere else. Note that a file check
    /// on the <c>.gha</c> is NOT a substitute and is deliberately not modelled as one — on
    /// this machine <c>Slop.gha</c> was present on a scanned path and still did not
    /// register. <b>Presence is not loaded.</b>
    /// </remarks>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// One imperative sentence, printed verbatim when this requirement is not met.
    /// </summary>
    /// <remarks>
    /// The failure message is the whole product here. "gh:Slop not loaded" tells an
    /// operator what broke; "build C:/Repos/Slop — build IS deploy" tells them what to do
    /// about it, which is the difference between a diagnosis and a fix.
    /// </remarks>
    [JsonPropertyName("fix")]
    public string? Fix { get; set; }

    /// <summary>Kind constant: a file or directory that must exist.</summary>
    public const string KindFile = "file";

    /// <summary>Kind constant: an HTTP endpoint that must answer.</summary>
    public const string KindService = "service";

    /// <summary>Kind constant: a plug-in that must be LOADED inside the app.</summary>
    public const string KindPlugin = "plugin";

    /// <summary>
    /// True when this can be checked without launching the target application.
    /// </summary>
    /// <remarks>
    /// The offline half is a syscall or one HTTP GET from the harness process, so
    /// <c>canary doctor</c> can run it with no launch at all — which is what makes it
    /// cheap enough to run every time.
    /// </remarks>
    public bool IsOfflineCheckable =>
        string.Equals(Kind, KindFile, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Kind, KindService, StringComparison.OrdinalIgnoreCase);

    /// <summary>A short stable identity, for de-duplication and for messages.</summary>
    /// <returns>Human-readable label.</returns>
    public string Describe() => Kind.ToLowerInvariant() switch
    {
        KindFile => $"file {Path}",
        KindService => Contains is null ? $"service {Url}" : $"service {Url} (containing '{Contains}')",
        KindPlugin => $"plugin {Id}",
        _ => $"{Kind} (unknown kind)",
    };
}
