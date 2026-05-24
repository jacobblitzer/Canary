using System.Text.RegularExpressions;

namespace Canary.Feedback;

// Produces YYYY-MM-DD-NNN-<3-to-5-word-slug> slugs per design §C5 +
// §0.4 default. NNN is a 3-digit zero-padded counter unique within a
// given date — collisions resolved by inspecting the existing inbox
// dir and incrementing past the highest used NNN for the same date.
public static class FeedbackSlugGenerator
{
    public static string Generate(DateTime date, string title, IEnumerable<string> existingSlugs)
    {
        var datePart = date.ToString("yyyy-MM-dd");
        var slugPart = SlugifyTitle(title);
        var nextN = NextSequence(datePart, existingSlugs);
        return $"{datePart}-{nextN:D3}-{slugPart}";
    }

    private static int NextSequence(string datePart, IEnumerable<string> existingSlugs)
    {
        var prefix = datePart + "-";
        var highest = 0;
        foreach (var slug in existingSlugs)
        {
            if (slug == null) continue;
            if (!slug.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            // Format: 2026-05-24-007-foo-bar  -> NNN at index of prefix.Length
            if (slug.Length < prefix.Length + 3) continue;
            var nStr = slug.AsSpan(prefix.Length, 3);
            if (int.TryParse(nStr, out var n) && n > highest)
                highest = n;
        }
        return highest + 1;
    }

    // Lowercase, hyphen-separated, alpha-numeric, capped at 5 words.
    // Falls back to "item" if the title has no usable tokens.
    private static string SlugifyTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "item";
        var lowered = title.ToLowerInvariant();
        var stripped = Regex.Replace(lowered, @"[^a-z0-9\s-]", " ");
        var words = stripped
            .Split(new[] { ' ', '\t', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Take(5)
            .ToArray();
        return words.Length == 0 ? "item" : string.Join('-', words);
    }
}
