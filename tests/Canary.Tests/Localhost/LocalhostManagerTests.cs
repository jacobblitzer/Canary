using Canary.Localhost;
using Xunit;

namespace Canary.Tests.Localhost;

// Phase 4 / §C7 Tier 1 — unit tests for the netstat parser + the
// public enumeration contract. Real netstat invocation is covered via
// the integration suite (and the smoke check the operator runs).
public class LocalhostManagerTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void ParseNetstat_TypicalIPv4_Listening_ReturnsPortPid()
    {
        var sample =
            "Active Connections\r\n\r\n" +
            "  Proto  Local Address          Foreign Address        State           PID\r\n" +
            "  TCP    0.0.0.0:135            0.0.0.0:0              LISTENING       1234\r\n";
        var entries = LocalhostManager.ParseNetstat(sample).ToList();

        Assert.Single(entries);
        Assert.Equal((135, (int?)1234), entries[0]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseNetstat_IPv6_ReadsPortFromBracket()
    {
        var sample = "  TCP    [::]:5173              [::]:0                 LISTENING       9876\r\n";
        var entries = LocalhostManager.ParseNetstat(sample).ToList();

        Assert.Single(entries);
        Assert.Equal(5173, entries[0].Port);
        Assert.Equal(9876, entries[0].Pid);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseNetstat_IgnoresEstablishedAndTimeWait()
    {
        var sample =
            "  TCP    127.0.0.1:5173         127.0.0.1:54321        ESTABLISHED     1111\r\n" +
            "  TCP    127.0.0.1:5174         127.0.0.1:54322        TIME_WAIT       0\r\n" +
            "  TCP    0.0.0.0:3000           0.0.0.0:0              LISTENING       2222\r\n";
        var entries = LocalhostManager.ParseNetstat(sample).ToList();

        Assert.Single(entries);
        Assert.Equal(3000, entries[0].Port);
        Assert.Equal(2222, entries[0].Pid);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseNetstat_IgnoresUdp()
    {
        var sample =
            "  UDP    0.0.0.0:1234           *:*                                    7890\r\n" +
            "  TCP    0.0.0.0:4173           0.0.0.0:0              LISTENING       5555\r\n";
        var entries = LocalhostManager.ParseNetstat(sample).ToList();

        Assert.Single(entries);
        Assert.Equal(4173, entries[0].Port);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseNetstat_EmptyInput_ReturnsEmpty()
    {
        var entries = LocalhostManager.ParseNetstat(string.Empty).ToList();
        Assert.Empty(entries);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseNetstat_MalformedLines_AreSkipped()
    {
        var sample =
            "garbage\r\n" +
            "  TCP    not-a-port             0.0.0.0:0              LISTENING       abc\r\n" +
            "  TCP    0.0.0.0:7777           0.0.0.0:0              LISTENING       8\r\n";
        var entries = LocalhostManager.ParseNetstat(sample).ToList();

        Assert.Single(entries);
        Assert.Equal(7777, entries[0].Port);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DefaultPorts_IncludesCommonDevServerPorts()
    {
        Assert.Contains(3000, LocalhostManager.DefaultPorts);  // generic node
        Assert.Contains(5173, LocalhostManager.DefaultPorts);  // Vite default
        Assert.Contains(4173, LocalhostManager.DefaultPorts);  // Vite preview
        Assert.Contains(8080, LocalhostManager.DefaultPorts);  // common HTTP
        Assert.Contains(1420, LocalhostManager.DefaultPorts);  // Tauri default
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void PortProvenance_DefaultIsUnknown()
    {
        // Sanity for the enum default — code paths rely on Unknown being 0.
        Assert.Equal(0, (int)PortProvenance.Unknown);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void EnumeratePorts_RealNetstat_ReturnsListWithoutThrowing()
    {
        // Smoke that the public API doesn't blow up on a real machine —
        // can't assert specific ports without a known fixture, just that
        // the call completes and returns a sensible shape. CanaryHarness
        // entries are likely (the test runner itself).
        var manager = new LocalhostManager();
        var entries = manager.EnumeratePorts();
        Assert.NotNull(entries);
        Assert.All(entries, e =>
        {
            Assert.InRange(e.Port, 1, 65535);
        });
    }
}
