using Canary.UI.Navigation;
using Xunit;

namespace Canary.Tests.Navigation;

// Phase 7 / §C4 — smoke tests for the INavMode implementations.
// Asserts each mode's Name + Description are present, CreateContent
// returns a usable Control, and consecutive calls return the same
// cached instance.
public class NavModeTests
{
    public static IEnumerable<object[]> AllNavModes() => new INavMode[]
    {
        new PastRunsNavMode(),
        new LocalhostNavMode(),
        new FeedbackNavMode(),
        new TelemetryNavMode(),
        new SettingsNavMode(),
    }.Select(m => new object[] { m });

    [Trait("Category", "Unit")]
    [Theory]
    [MemberData(nameof(AllNavModes))]
    public void NavMode_HasNameAndDescription(INavMode mode)
    {
        Assert.False(string.IsNullOrWhiteSpace(mode.Name), "Name must be non-empty");
        Assert.False(string.IsNullOrWhiteSpace(mode.Description), "Description must be non-empty");
    }

    [Trait("Category", "Unit")]
    [Theory]
    [MemberData(nameof(AllNavModes))]
    public void NavMode_CreateContent_ReturnsNonNullControl(INavMode mode)
    {
        var content = mode.CreateContent();
        Assert.NotNull(content);
        Assert.IsAssignableFrom<Control>(content);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [MemberData(nameof(AllNavModes))]
    public void NavMode_CreateContent_IsIdempotent_ReturnsSameInstance(INavMode mode)
    {
        var first = mode.CreateContent();
        var second = mode.CreateContent();
        Assert.Same(first, second);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void NavMode_NamesAreUnique()
    {
        var names = AllNavModes().Select(args => ((INavMode)args[0]).Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
