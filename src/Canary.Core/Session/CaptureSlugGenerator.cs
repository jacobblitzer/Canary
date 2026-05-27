using System.Text.RegularExpressions;

namespace Canary.Session;

public static class CaptureSlugGenerator
{
    public static string? FromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var lowered = title.ToLowerInvariant();
        var stripped = Regex.Replace(lowered, @"[^a-z0-9\s-]", " ");
        var words = stripped
            .Split(new[] { ' ', '\t', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Take(5)
            .ToArray();
        return words.Length == 0 ? null : string.Join('-', words);
    }
}
