using System.Diagnostics;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

[Trait("Category", "Unit")]
public class ProcessManagerTests
{
    [Fact]
    public void ProcessManager_Track_KillAll_TerminatesProcess()
    {
        var pm = new ProcessManager();

        // Launch a long-running process (ping with high count)
        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "ping",
            Arguments = "-n 60 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        pm.Track(proc);
        Assert.False(proc.HasExited);

        pm.KillAll();

        // Give it a moment to actually terminate
        proc.WaitForExit(3000);
        Assert.True(proc.HasExited);
    }

    [Fact]
    public void ProcessManager_KillAll_AlreadyExited_NoError()
    {
        var pm = new ProcessManager();

        // Launch a process that exits immediately
        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c exit 0",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        proc.WaitForExit(5000);
        Assert.True(proc.HasExited);

        pm.Track(proc);

        // Should not throw
        pm.KillAll();
    }
}
