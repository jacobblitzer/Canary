using Canary.Session;
using Xunit;

namespace Canary.Tests.Session;

[Trait("Category", "Unit")]
public class CaptureSlugGeneratorTests
{
    [Fact]
    public void FromTitle_NullReturnsNull() => Assert.Null(CaptureSlugGenerator.FromTitle(null));

    [Fact]
    public void FromTitle_EmptyReturnsNull() => Assert.Null(CaptureSlugGenerator.FromTitle(""));

    [Fact]
    public void FromTitle_WhitespaceReturnsNull() => Assert.Null(CaptureSlugGenerator.FromTitle("   "));

    [Fact]
    public void FromTitle_HandlesSimpleTitle()
        => Assert.Equal("landing-screen", CaptureSlugGenerator.FromTitle("Landing Screen"));

    [Fact]
    public void FromTitle_LowercasesAndStripsPunctuation()
        => Assert.Equal("foo-bar-baz", CaptureSlugGenerator.FromTitle("Foo! Bar?? Baz"));

    [Fact]
    public void FromTitle_CapsAtFiveWords()
        => Assert.Equal("one-two-three-four-five", CaptureSlugGenerator.FromTitle("one two three four five six seven"));

    [Fact]
    public void FromTitle_OnlyPunctuationReturnsNull()
        => Assert.Null(CaptureSlugGenerator.FromTitle("!?@#$%"));
}
