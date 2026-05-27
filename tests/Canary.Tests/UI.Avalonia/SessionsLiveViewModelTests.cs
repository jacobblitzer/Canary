using Canary.Agent;
using Canary.Config;
using Canary.Session;
using Canary.Telemetry;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class SessionsLiveViewModelTests
{
    private sealed class StubAgent : ICanaryAgent
    {
        public int CaptureCount;
        public Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters)
            => Task.FromResult(new AgentResponse { Success = true });
        public Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
        {
            CaptureCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath)!);
            File.WriteAllBytes(settings.OutputPath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            return Task.FromResult(new ScreenshotResult { FilePath = settings.OutputPath, Width = 100, Height = 100, CapturedAt = DateTime.UtcNow });
        }
        public Task<HeartbeatResult> HeartbeatAsync() => Task.FromResult(new HeartbeatResult { Ok = true });
        public Task AbortAsync() => Task.CompletedTask;
    }

    private sealed class StubFactory : ISessionAgentFactory
    {
        public StubAgent Agent { get; } = new();
        public Task<SessionAgentBundle> CreateAndInitializeAsync(string _, ITelemetrySink __, CancellationToken ___)
            => Task.FromResult(new SessionAgentBundle { Agent = Agent, Url = "http://stub/" });
    }

    private static string CreateWorkloadFixture(out WorkloadConfig cfg)
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-avalonia-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var workloadDir = Path.Combine(root, "qualia");
        Directory.CreateDirectory(workloadDir);
        cfg = new WorkloadConfig { Name = "qualia", DisplayName = "Qualia", AgentType = "qualia-cdp" };
        var configPath = Path.Combine(workloadDir, "workload.json");
        File.WriteAllText(configPath, "{\"name\":\"qualia\",\"displayName\":\"Qualia\",\"agentType\":\"qualia-cdp\"}");
        return root;
    }

    [Fact]
    public void Initial_State_IsIdle_WithNoWorkloadSelected()
    {
        var vm = new SessionsLiveViewModel(new StubFactory());
        Assert.Equal(SessionState.Idle, vm.State);
        Assert.Null(vm.SelectedWorkload);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.False(vm.CaptureCommand.CanExecute(null));
        Assert.False(vm.EndCommand.CanExecute(null));
    }

    [Fact]
    public void SetWorkloads_FiltersToSupportedAgentTypes()
    {
        var vm = new SessionsLiveViewModel(new StubFactory());
        var workloads = new[]
        {
            new WorkloadConfig { Name = "qualia", DisplayName = "Qualia", AgentType = "qualia-cdp" },
            new WorkloadConfig { Name = "penumbra", DisplayName = "Penumbra", AgentType = "penumbra-cdp" },
            new WorkloadConfig { Name = "rhino", DisplayName = "Rhino", AgentType = "rhino" },
        };
        vm.SetWorkloads("/tmp/x", workloads);

        Assert.Equal(2, vm.Workloads.Count);
        Assert.Contains(vm.Workloads, w => w.Name == "qualia");
        Assert.Contains(vm.Workloads, w => w.Name == "penumbra");
        Assert.DoesNotContain(vm.Workloads, w => w.Name == "rhino");
        Assert.NotNull(vm.SelectedWorkload);
    }

    [Fact]
    public void CanStart_RequiresIdleStateAndSelectedWorkload()
    {
        var vm = new SessionsLiveViewModel(new StubFactory());
        var workloads = new[] { new WorkloadConfig { Name = "qualia", DisplayName = "Qualia", AgentType = "qualia-cdp" } };
        vm.SetWorkloads("/tmp/x", workloads);

        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Start_TransitionsIdleToArmed_OnSuccess()
    {
        var factory = new StubFactory();
        var vm = new SessionsLiveViewModel(factory);
        var root = CreateWorkloadFixture(out var cfg);
        try
        {
            vm.SetWorkloads(root, new[] { cfg });
            await vm.StartCommand.ExecuteAsync(null);

            Assert.Equal(SessionState.Armed, vm.State);
            Assert.True(vm.CaptureCommand.CanExecute(null));
            Assert.True(vm.CaptureAnnotateCommand.CanExecute(null));
            Assert.True(vm.CaptureWithNoteCommand.CanExecute(null));
            Assert.True(vm.EndCommand.CanExecute(null));
            Assert.False(vm.StartCommand.CanExecute(null));
            Assert.NotNull(vm.CurrentSessionId);
            Assert.NotNull(vm.CurrentSessionDirectory);
        }
        finally
        {
            if (vm.EndCommand.CanExecute(null)) await vm.EndCommand.ExecuteAsync(null);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Capture_PopulatesThumbnail_AndAdvancesStatus()
    {
        var factory = new StubFactory();
        var vm = new SessionsLiveViewModel(factory);
        var root = CreateWorkloadFixture(out var cfg);
        try
        {
            vm.SetWorkloads(root, new[] { cfg });
            await vm.StartCommand.ExecuteAsync(null);
            await vm.CaptureForTestAsync(withNote: false, annotate: false);

            Assert.Equal(1, factory.Agent.CaptureCount);
            Assert.Single(vm.Captures);
            Assert.Contains("Captured #1", vm.StatusText);
        }
        finally
        {
            if (vm.EndCommand.CanExecute(null)) await vm.EndCommand.ExecuteAsync(null);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task End_TransitionsArmedToIdle_AndWritesReport()
    {
        var factory = new StubFactory();
        var vm = new SessionsLiveViewModel(factory);
        var root = CreateWorkloadFixture(out var cfg);
        try
        {
            vm.SetWorkloads(root, new[] { cfg });
            await vm.StartCommand.ExecuteAsync(null);
            var sessionDir = vm.CurrentSessionDirectory!;
            await vm.CaptureForTestAsync(withNote: false, annotate: false);
            await vm.EndCommand.ExecuteAsync(null);

            Assert.Equal(SessionState.Idle, vm.State);
            Assert.Empty(vm.Captures);
            Assert.True(File.Exists(SessionPaths.ReportPath(sessionDir)));
            Assert.True(File.Exists(SessionPaths.SessionJsonPath(sessionDir)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureWithNote_DefersToPromptDelegate()
    {
        var factory = new StubFactory();
        var vm = new SessionsLiveViewModel(factory);
        var root = CreateWorkloadFixture(out var cfg);
        var promptInvoked = 0;
        vm.NotePromptAsync = () =>
        {
            promptInvoked++;
            return Task.FromResult<NoteResult?>(new NoteResult { Title = "test", Body = "body" });
        };
        try
        {
            vm.SetWorkloads(root, new[] { cfg });
            await vm.StartCommand.ExecuteAsync(null);
            await vm.CaptureForTestAsync(withNote: true, annotate: false);

            Assert.Equal(1, promptInvoked);
            Assert.Equal(1, factory.Agent.CaptureCount);
        }
        finally
        {
            if (vm.EndCommand.CanExecute(null)) await vm.EndCommand.ExecuteAsync(null);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureWithNote_AbortsIfPromptReturnsNull()
    {
        var factory = new StubFactory();
        var vm = new SessionsLiveViewModel(factory);
        var root = CreateWorkloadFixture(out var cfg);
        vm.NotePromptAsync = () => Task.FromResult<NoteResult?>(null);
        try
        {
            vm.SetWorkloads(root, new[] { cfg });
            await vm.StartCommand.ExecuteAsync(null);
            await vm.CaptureForTestAsync(withNote: true, annotate: false);

            Assert.Equal(0, factory.Agent.CaptureCount);
            Assert.Empty(vm.Captures);
        }
        finally
        {
            if (vm.EndCommand.CanExecute(null)) await vm.EndCommand.ExecuteAsync(null);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HotkeyHint_IsAlwaysPresent()
    {
        var vm = new SessionsLiveViewModel(new StubFactory());
        Assert.Contains("Ctrl+Shift+C", vm.HotkeyHint);
        Assert.Contains("Ctrl+Shift+A", vm.HotkeyHint);
    }
}
