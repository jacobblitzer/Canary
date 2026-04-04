using Canary.Input;
using Xunit;

namespace Canary.Tests.Input;

public class ViewportLocatorTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void FindWindow_BadTitle_ReturnsZero()
    {
        var handle = ViewportLocator.FindWindowByTitle("xyznonexistent_window_title_12345");
        Assert.Equal(IntPtr.Zero, handle);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void IsValidTarget_Zero_ReturnsFalse()
    {
        Assert.False(ViewportLocator.IsValidTarget(IntPtr.Zero));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void GetViewportBounds_Zero_ReturnsEmptyBounds()
    {
        var bounds = ViewportLocator.GetViewportBounds(IntPtr.Zero);
        Assert.Equal(0, bounds.Width);
        Assert.Equal(0, bounds.Height);
    }
}
