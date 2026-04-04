using Canary.Agent;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

[Trait("Category", "Unit")]
public class WatchdogTests
{
    private sealed class HealthyHeartbeat : IHeartbeatSource
    {
        public Task<HeartbeatResult> HeartbeatAsync(CancellationToken cancellationToken)
            => Task.FromResult(new HeartbeatResult { Ok = true });
    }

    private sealed class UnresponsiveHeartbeat : IHeartbeatSource
    {
        public Task<HeartbeatResult> HeartbeatAsync(CancellationToken cancellationToken)
            => throw new TimeoutException("Agent did not respond");
    }

    [Fact]
    public async Task Watchdog_HealthyAgent_NoEvent()
    {
        var watchdog = new Watchdog(new HealthyHeartbeat(), interval: TimeSpan.FromMilliseconds(200), maxMisses: 3);
        bool appDeadFired = false;
        watchdog.OnAppDead += () => appDeadFired = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await watchdog.RunAsync(cts.Token);

        Assert.False(appDeadFired);
    }

    [Fact]
    public async Task Watchdog_UnresponsiveAgent_FiresDeadEvent()
    {
        // interval=200ms, maxMisses=3 → fires after ~600ms
        var watchdog = new Watchdog(new UnresponsiveHeartbeat(), interval: TimeSpan.FromMilliseconds(200), maxMisses: 3);
        bool appDeadFired = false;
        watchdog.OnAppDead += () => appDeadFired = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await watchdog.RunAsync(cts.Token);

        Assert.True(appDeadFired);
    }

    [Fact]
    public async Task Watchdog_Cancellation_StopsCleanly()
    {
        var watchdog = new Watchdog(new HealthyHeartbeat(), interval: TimeSpan.FromMilliseconds(200));
        bool appDeadFired = false;
        watchdog.OnAppDead += () => appDeadFired = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Should complete without throwing
        await watchdog.RunAsync(cts.Token);

        Assert.False(appDeadFired);
    }
}
