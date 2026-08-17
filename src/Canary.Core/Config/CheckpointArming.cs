namespace Canary.Config;

/// <summary>
/// Whether a checkpoint is <b>armed</b> — i.e. whether a run actually compares it
/// against a baseline.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 2b. This rule already existed, as a private static inside
/// <c>TestRunner</c>. It is lifted here because the baseline ledger has to ask exactly
/// the same question, and a second copy of the rule is the same defect Phase 2b exists
/// to remove: two derivations of one fact, drifting apart silently. The ledger would
/// have gone stale the first time an alias was added to one copy and not the other.
/// </para>
/// <para>
/// The precedence is <b>not</b> arbitrary and must not be reordered: capture-only wins
/// over everything, including an explicit <c>--mode</c> override, because such a
/// checkpoint has opted out of producing a verdict at all.
/// </para>
/// </remarks>
public static class CheckpointArming
{
    /// <summary>
    /// True when a checkpoint opts out of comparison entirely (<c>mode = "capture"</c>,
    /// or the aliases <c>"none"</c> / <c>"off"</c>): the candidate is saved and no
    /// verdict is produced.
    /// </summary>
    /// <param name="checkpoint">Checkpoint to classify.</param>
    /// <returns>True when no comparison will run.</returns>
    public static bool IsCaptureOnly(TestCheckpoint checkpoint) =>
        string.Equals(checkpoint.Mode, "capture", StringComparison.OrdinalIgnoreCase)
        || string.Equals(checkpoint.Mode, "none", StringComparison.OrdinalIgnoreCase)
        || string.Equals(checkpoint.Mode, "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the checkpoint itself declares <c>mode = "vlm"</c>, which wins over a
    /// <c>--mode</c> flag.
    /// </summary>
    /// <param name="checkpoint">Checkpoint to classify.</param>
    /// <returns>True when the checkpoint is VLM-judged rather than pixel-compared.</returns>
    public static bool IsExplicitVlm(TestCheckpoint checkpoint) =>
        string.Equals(checkpoint.Mode, "vlm", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the checkpoint is compared against a baseline image, and therefore
    /// <b>needs</b> one. This is the ledger's inclusion rule.
    /// </summary>
    /// <param name="checkpoint">Checkpoint to classify.</param>
    /// <returns>True when a baseline PNG is load-bearing for this checkpoint.</returns>
    /// <remarks>
    /// A <c>--mode vlm</c> or <c>--mode both</c> override can still send an armed
    /// checkpoint down the VLM path for one run. That does not un-arm it: the content
    /// declares a pixel comparison, and the ledger records what the content declares,
    /// not what one invocation happened to do.
    /// </remarks>
    public static bool IsArmedForPixelDiff(TestCheckpoint checkpoint) =>
        !IsCaptureOnly(checkpoint) && !IsExplicitVlm(checkpoint);
}
