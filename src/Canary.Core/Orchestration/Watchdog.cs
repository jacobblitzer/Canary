using Canary.Agent;

namespace Canary.Orchestration;

/// <summary>
/// Provides heartbeat checking capability for the watchdog.
/// </summary>
public interface IHeartbeatSource
{
    Task<HeartbeatResult> HeartbeatAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Adapter that wraps HarnessClient as an IHeartbeatSource.
/// </summary>
public sealed class HarnessClientHeartbeatSource : IHeartbeatSource
{
    private readonly HarnessClient _client;

    public HarnessClientHeartbeatSource(HarnessClient client) => _client = client;

    public Task<HeartbeatResult> HeartbeatAsync(CancellationToken cancellationToken)
        => _client.HeartbeatAsync(cancellationToken);
}

/// <summary>
/// Monitors agent health by sending periodic heartbeats.
/// Fires OnAppDead after 3 consecutive missed heartbeats.
/// </summary>
public sealed class Watchdog
{
    private readonly IHeartbeatSource _heartbeatSource;
    private readonly TimeSpan _interval;
    private readonly int _maxMisses;

    /// <summary>
    /// Fired when the agent is declared dead (too many consecutive heartbeat failures).
    /// </summary>
    public event Action? OnAppDead;

    /// <summary>
    /// Creates a watchdog that monitors agent health.
    /// </summary>
    /// <param name="heartbeatSource">Source for heartbeat checks.</param>
    /// <param name="interval">Time between heartbeat checks (default 2s).</param>
    /// <param name="maxMisses">Consecutive misses before declaring dead (default 3).</param>
    public Watchdog(IHeartbeatSource heartbeatSource, TimeSpan? interval = null, int maxMisses = 3)
    {
        _heartbeatSource = heartbeatSource;
        _interval = interval ?? TimeSpan.FromSeconds(2);
        _maxMisses = maxMisses;
    }

    /// <summary>
    /// Runs the watchdog loop. Returns when cancelled or when app is declared dead.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        int consecutiveMisses = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var result = await _heartbeatSource.HeartbeatAsync(cancellationToken).ConfigureAwait(false);
                if (result.Ok)
                {
                    consecutiveMisses = 0;
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // TimeoutException, IOException, etc. — counts as a miss
            }

            consecutiveMisses++;
            if (consecutiveMisses >= _maxMisses)
            {
                OnAppDead?.Invoke();
                return;
            }
        }
    }
}
