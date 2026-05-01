namespace Canary.Comparison;

/// <summary>
/// Evaluates a screenshot against a natural-language description using a
/// Vision-Language Model. Returns a pass/fail verdict with reasoning.
/// </summary>
public interface IVlmProvider
{
    Task<VlmVerdict> EvaluateAsync(
        byte[] imageBytes,
        string description,
        CancellationToken ct);
}

/// <summary>
/// Result of a VLM evaluation: pass/fail, confidence, and human-readable reasoning.
/// </summary>
public sealed class VlmVerdict
{
    public bool Passed { get; init; }
    public double Confidence { get; init; }
    public string Reasoning { get; init; } = string.Empty;
}
