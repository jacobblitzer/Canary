using Canary.Feedback;
using Xunit;

namespace Canary.Tests.Feedback;

// Phase 5 / §C5 — slug format YYYY-MM-DD-NNN-<3-to-5-word-slug>.
public class FeedbackSlugGeneratorTests
{
    private static readonly DateTime Date = new(2026, 5, 24);

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_FirstSlugOfDay_HasNumber001()
    {
        var slug = FeedbackSlugGenerator.Generate(Date, "Pencil toon background too bright", Array.Empty<string>());
        Assert.StartsWith("2026-05-24-001-", slug);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_FollowsExistingSequence()
    {
        var existing = new[]
        {
            "2026-05-24-001-foo-bar",
            "2026-05-24-002-baz-qux",
            "2026-05-24-005-some-other-thing",
        };
        var slug = FeedbackSlugGenerator.Generate(Date, "Pencil toon background too bright", existing);
        Assert.StartsWith("2026-05-24-006-", slug);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_DifferentDate_RestartsAt001()
    {
        var existing = new[]
        {
            "2026-05-24-001-foo",
            "2026-05-24-009-bar",
        };
        var slug = FeedbackSlugGenerator.Generate(new DateTime(2026, 5, 25), "New day item", existing);
        Assert.StartsWith("2026-05-25-001-", slug);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_TitleLowercaseHyphenated_MaxFiveWords()
    {
        var slug = FeedbackSlugGenerator.Generate(Date, "Pencil Toon Background Too Bright And Other Stuff", Array.Empty<string>());
        // First five words only.
        Assert.Equal("2026-05-24-001-pencil-toon-background-too-bright", slug);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_StripsPunctuation()
    {
        var slug = FeedbackSlugGenerator.Generate(Date, "Foo, bar! Baz?", Array.Empty<string>());
        Assert.Equal("2026-05-24-001-foo-bar-baz", slug);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_EmptyTitle_FallsBackToItem()
    {
        var slug = FeedbackSlugGenerator.Generate(Date, "", Array.Empty<string>());
        Assert.Equal("2026-05-24-001-item", slug);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Generate_IgnoresMalformedExistingSlugs()
    {
        var existing = new[]
        {
            "not-a-slug-at-all",
            "2026-05-24-",         // missing NNN
            "2026-05-24-XXX-bad",  // non-numeric NNN
            "2026-05-24-007-fine", // valid
        };
        var slug = FeedbackSlugGenerator.Generate(Date, "next", existing);
        Assert.StartsWith("2026-05-24-008-", slug);
    }
}
