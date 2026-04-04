using Canary.Input;
using Xunit;

namespace Canary.Tests.Input;

public class CoordinateNormalizationTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void NormalizeDenormalize_RoundTrips()
    {
        // Viewport at screen position (100, 200) with size 800x600
        var bounds = new ViewportBounds(100, 200, 800, 600);
        int screenX = 500, screenY = 500;

        var (vx, vy) = ViewportLocator.NormalizeCoord(screenX, screenY, bounds);
        var (rx, ry) = ViewportLocator.DenormalizeCoord(vx, vy, bounds);

        Assert.InRange(rx, screenX - 1, screenX + 1);
        Assert.InRange(ry, screenY - 1, screenY + 1);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TopLeft_ReturnsZeroZero()
    {
        var bounds = new ViewportBounds(100, 200, 800, 600);

        var (vx, vy) = ViewportLocator.NormalizeCoord(100, 200, bounds);

        Assert.Equal(0.0, vx, 5);
        Assert.Equal(0.0, vy, 5);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void BottomRight_ReturnsOneOne()
    {
        var bounds = new ViewportBounds(100, 200, 800, 600);

        var (vx, vy) = ViewportLocator.NormalizeCoord(900, 800, bounds);

        Assert.Equal(1.0, vx, 5);
        Assert.Equal(1.0, vy, 5);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DifferentViewportSize_ScalesCorrectly()
    {
        // Recorded at 800x600, replaying at 1024x768
        var replayBounds = new ViewportBounds(0, 0, 1024, 768);

        // Center point: vx=0.5, vy=0.5
        var (rx, ry) = ViewportLocator.DenormalizeCoord(0.5, 0.5, replayBounds);

        Assert.Equal(512, rx);
        Assert.Equal(384, ry);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Center_NormalizesToHalf()
    {
        var bounds = new ViewportBounds(0, 0, 800, 600);

        var (vx, vy) = ViewportLocator.NormalizeCoord(400, 300, bounds);

        Assert.Equal(0.5, vx, 5);
        Assert.Equal(0.5, vy, 5);
    }
}
