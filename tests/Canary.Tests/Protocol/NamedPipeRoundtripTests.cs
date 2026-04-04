using Canary.Agent;
using Xunit;

namespace Canary.Tests.Protocol;

public class NamedPipeRoundtripTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public async Task AgentServer_Heartbeat_ReturnsOk()
    {
        var pipeName = $"canary-test-{Guid.NewGuid():N}";
        var agent = new MockAgent();
        using var server = new AgentServer(pipeName, agent);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = server.RunAsync(cts.Token);

        using var client = new HarnessClient(pipeName, TimeSpan.FromSeconds(5));
        await client.ConnectAsync(cts.Token);

        var result = await client.HeartbeatAsync(cts.Token);

        Assert.True(result.Ok);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task HarnessClient_Timeout_ThrowsTimeoutException()
    {
        var pipeName = $"canary-test-{Guid.NewGuid():N}";
        // Agent delays longer than client timeout
        var agent = new MockAgent { HeartbeatDelay = TimeSpan.FromSeconds(30) };
        using var server = new AgentServer(pipeName, agent);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = server.RunAsync(cts.Token);

        // Client has a very short timeout
        using var client = new HarnessClient(pipeName, TimeSpan.FromMilliseconds(500));
        await client.ConnectAsync(cts.Token);

        await Assert.ThrowsAsync<TimeoutException>(() => client.HeartbeatAsync(cts.Token));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task HarnessClient_Execute_PassesParams()
    {
        var pipeName = $"canary-test-{Guid.NewGuid():N}";
        var agent = new MockAgent();
        using var server = new AgentServer(pipeName, agent);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = server.RunAsync(cts.Token);

        using var client = new HarnessClient(pipeName, TimeSpan.FromSeconds(5));
        await client.ConnectAsync(cts.Token);

        var parameters = new Dictionary<string, string> { ["path"] = "test.3dm" };
        var result = await client.ExecuteAsync("OpenFile", parameters, cts.Token);

        Assert.True(result.Success);
        Assert.Equal("OpenFile", agent.LastAction);
        Assert.Equal("test.3dm", agent.LastParams!["path"]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task AgentServer_CaptureScreenshot_ReturnsMockPath()
    {
        var pipeName = $"canary-test-{Guid.NewGuid():N}";
        var agent = new MockAgent();
        using var server = new AgentServer(pipeName, agent);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = server.RunAsync(cts.Token);

        using var client = new HarnessClient(pipeName, TimeSpan.FromSeconds(5));
        await client.ConnectAsync(cts.Token);

        var settings = new CaptureSettings { Width = 800, Height = 600, OutputPath = "/tmp/test.png" };
        var result = await client.CaptureScreenshotAsync(settings, cts.Token);

        Assert.Equal("/tmp/test.png", result.FilePath);
        Assert.Equal(800, result.Width);
        Assert.Equal(600, result.Height);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task HarnessClient_SequentialRequests_AllSucceed()
    {
        var pipeName = $"canary-test-{Guid.NewGuid():N}";
        var agent = new MockAgent();
        using var server = new AgentServer(pipeName, agent);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = server.RunAsync(cts.Token);

        using var client = new HarnessClient(pipeName, TimeSpan.FromSeconds(5));
        await client.ConnectAsync(cts.Token);

        for (int i = 0; i < 5; i++)
        {
            var result = await client.HeartbeatAsync(cts.Token);
            Assert.True(result.Ok);
        }

        Assert.Equal(5, agent.HeartbeatCount);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task AgentServer_Shutdown_DisconnectsCleanly()
    {
        var pipeName = $"canary-test-{Guid.NewGuid():N}";
        var agent = new MockAgent();
        using var server = new AgentServer(pipeName, agent);
        using var serverCts = new CancellationTokenSource();

        _ = server.RunAsync(serverCts.Token);

        using var client = new HarnessClient(pipeName, TimeSpan.FromSeconds(1));
        await client.ConnectAsync();

        // Verify connection works
        var result = await client.HeartbeatAsync();
        Assert.True(result.Ok);

        // Cancel the server — this disconnects the pipe
        serverCts.Cancel();
        server.Dispose();
        await Task.Delay(200);

        // Next request should fail (pipe disconnected or timeout)
        await Assert.ThrowsAnyAsync<Exception>(() => client.HeartbeatAsync());
    }
}

/// <summary>
/// Mock agent for testing IPC without a real application.
/// </summary>
internal class MockAgent : ICanaryAgent
{
    public TimeSpan HeartbeatDelay { get; set; } = TimeSpan.Zero;
    public string? LastAction { get; private set; }
    public Dictionary<string, string>? LastParams { get; private set; }
    public int HeartbeatCount { get; private set; }

    public async Task<HeartbeatResult> HeartbeatAsync()
    {
        if (HeartbeatDelay > TimeSpan.Zero)
            await Task.Delay(HeartbeatDelay);

        HeartbeatCount++;
        return new HeartbeatResult { Ok = true, State = new Dictionary<string, string> { ["test"] = "true" } };
    }

    public Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters)
    {
        LastAction = action;
        LastParams = parameters;
        return Task.FromResult(new AgentResponse
        {
            Success = true,
            Message = $"Executed {action}",
            Data = new Dictionary<string, string>()
        });
    }

    public Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
    {
        return Task.FromResult(new ScreenshotResult
        {
            FilePath = settings.OutputPath,
            Width = settings.Width,
            Height = settings.Height,
            CapturedAt = DateTime.UtcNow
        });
    }

    public Task AbortAsync()
    {
        return Task.CompletedTask;
    }
}
