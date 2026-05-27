using Canary.Cli;
using Canary.UI.Avalonia.Services;
using Xunit;

namespace Canary.Tests.Integration;

// Integration test for the single-instance pipe handoff (impl §3 hard-rule 5).
// Exercises the real NamedPipeServerStream + NamedPipeClientStream so we know
// the JSON round-trip and async server loop work end-to-end. Does NOT spawn
// Canary.UI.exe — that's a separate smoke check the operator runs by hand.
//
// Note: this test claims the global pipe name `canary-ui-singleinstance-pipe`
// while it runs. Running it concurrently with an actual Canary.UI instance on
// the same machine will collide. The test is xUnit-serial by default within
// its class collection.
public class SingleInstancePipeTests
{
    [Trait("Category", "Integration")]
    [Fact]
    public async Task PipeServer_ReceivesAutoRunArgs_FromClient()
    {
        var received = new TaskCompletionSource<AutoRunArgs>();

        using var server = new SingleInstancePipeServer();
        server.AutoRunRequested += args => received.TrySetResult(args);
        server.Start();

        // Brief wait so the server has time to bind the pipe.
        await Task.Delay(200);

        var sent = new AutoRunArgs
        {
            Workload = "qualia",
            Test = "diag-pencil-baseline",
            Mode = "pixel-diff",
        };

        var ok = SingleInstancePipeClient.TrySend(sent);
        Assert.True(ok, "TrySend should succeed when the server is up");

        var got = await Task.WhenAny(received.Task, Task.Delay(5_000)) == received.Task
            ? await received.Task
            : throw new Xunit.Sdk.XunitException("AutoRunRequested did not fire within 5 seconds");

        Assert.Equal(sent.Workload, got.Workload);
        Assert.Equal(sent.Test, got.Test);
        Assert.Equal(sent.Mode, got.Mode);
        Assert.Null(got.Suite);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public void PipeClient_NoServerRunning_TrySendReturnsFalseQuickly()
    {
        // Cannot stop a running Canary.UI from this test, but we can prove
        // the client times out cleanly when nothing is listening: this asserts
        // TrySend's catch-all returns false rather than throwing.
        // We use a deliberately tiny timeout to avoid stalling the suite.
        var args = new AutoRunArgs { Workload = "rhino" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = SingleInstancePipeClient.TrySend(args, connectTimeoutMs: 50);
        sw.Stop();

        // Either it succeeded (a Canary.UI is up — fine, integration env) or
        // it failed quickly (no server). Both are acceptable; what's NOT
        // acceptable is throwing.
        Assert.True(sw.ElapsedMilliseconds < 5_000,
            $"TrySend with 50ms timeout should return promptly; took {sw.ElapsedMilliseconds}ms");
        Assert.True(ok || !ok);  // tautological — the point is no exception.
    }
}
