using Canary.Agent;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

/// <summary>
/// Deployment campaign Phase 5b — the <c>environment.json</c> contract and machine identity.
/// </summary>
/// <remarks>
/// This file is written by two producers (a run, and <c>canary env</c>) and read by three
/// consumers (<c>canary env --show</c>, <c>canary doctor</c>, the Environment tab). Before
/// <see cref="EnvironmentCapture"/> existed, the producer serialised an anonymous object and one
/// consumer parsed it back by hand — the same two-sides-disagree shape as bug 0022. These tests
/// pin the round trip, and the honesty properties that make the file usable as QC evidence.
/// </remarks>
[Trait("Category", "Unit")]
public class EnvironmentCaptureTests
{
    private static string NewDir() =>
        Path.Combine(Path.GetTempPath(), "canary-capture-" + Guid.NewGuid().ToString("N"));

    private static void With(Action<string> body)
    {
        var dir = NewDir();
        try { Directory.CreateDirectory(dir); body(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    private static IReadOnlyDictionary<string, string> SampleHost() => new Dictionary<string, string>
    {
        [HostStateFields.Host] = "rhino",
        [HostStateFields.HostVersion] = "8.34.26223.11001",
        [HostStateFields.HostReady] = "true",
        [HostStateFields.Loaded] = @"gh:Slop=1.0@C:\GH\Libraries\Slop.gha",
    };

    // -----------------------------------------------------------------------
    // Round trip.
    // -----------------------------------------------------------------------

    [Fact]
    public void SaveThenLoad_PreservesEverything()
    {
        With(dir =>
        {
            var path = Path.Combine(dir, EnvironmentCapture.FileName);
            var findings = new[]
            {
                new EnvironmentClash(ClashSeverity.Error, "duplicate-id", "gh:Slop loaded twice"),
                new EnvironmentClash(ClashSeverity.Warning, "origin-deviates", "gh:Slop is declared as 'deployed'"),
                new EnvironmentClash(ClashSeverity.Note, "dead-scan-folder", @"D:\gone"),
            };

            EnvironmentCapture.Create("rhino", SampleHost(), findings).Save(path);
            var read = EnvironmentCapture.Load(path);

            Assert.Equal("rhino", read.Workload);
            Assert.Equal("rhino", read.Host[HostStateFields.Host]);
            Assert.Equal(3, read.Findings.Count);
            Assert.Equal(ClashSeverity.Error, read.Findings[0].Severity);
            Assert.Equal("duplicate-id", read.Findings[0].Kind);
            Assert.NotEqual(string.Empty, read.CapturedUtc);
            // Identity is the whole point of the file being diffable.
            Assert.Equal(Environment.MachineName, read.Machine[MachineIdentity.MachineName]);
            Assert.True(read.IsFromThisMachine());
        });
    }

    [Fact]
    public void Save_CreatesTheDirectory()
    {
        With(dir =>
        {
            var path = Path.Combine(dir, "results", EnvironmentCapture.FileName);
            EnvironmentCapture.Create("rhino", SampleHost(), Array.Empty<EnvironmentClash>()).Save(path);
            Assert.True(File.Exists(path));
        });
    }

    [Fact]
    public void PathFor_IsTheWorkloadsResultsRoot()
    {
        Assert.Equal(
            Path.Combine(@"C:\wl", "rhino", "results", "environment.json"),
            EnvironmentCapture.PathFor(@"C:\wl", "rhino"));
    }

    [Fact]
    public void Load_ReadsACaptureWrittenWithABom()
    {
        With(dir =>
        {
            var path = Path.Combine(dir, EnvironmentCapture.FileName);
            EnvironmentCapture.Create("rhino", SampleHost(), Array.Empty<EnvironmentClash>()).Save(path);
            var json = File.ReadAllText(path);
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            Assert.Equal("rhino", EnvironmentCapture.Load(path).Workload);
        });
    }

    // -----------------------------------------------------------------------
    // An unreadable capture must not read as an empty one.
    // -----------------------------------------------------------------------

    [Fact]
    public void Load_OnAnAbsentFile_ThrowsFileNotFound()
    {
        With(dir =>
        {
            Assert.Throws<FileNotFoundException>(
                () => EnvironmentCapture.Load(Path.Combine(dir, "nope.json")));
        });
    }

    /// <summary>
    /// Corrupt content throws rather than yielding a capture with nothing in it.
    /// </summary>
    /// <remarks>
    /// An empty capture would render as "this machine has nothing loaded" — a confident,
    /// false answer. The caller decides how to present the failure; it does not get to miss it.
    /// </remarks>
    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    [InlineData("")]
    public void Load_OnUnreadableContent_ThrowsRatherThanReturningAnEmptyCapture(string content)
    {
        With(dir =>
        {
            var path = Path.Combine(dir, EnvironmentCapture.FileName);
            File.WriteAllText(path, content);

            Assert.Throws<InvalidDataException>(() => EnvironmentCapture.Load(path));
        });
    }

    /// <summary>
    /// A finding whose severity cannot be read becomes a Warning, never a Note.
    /// </summary>
    /// <remarks>
    /// Demoting an unreadable severity to the tier nobody triages would quietly hide it.
    /// </remarks>
    [Fact]
    public void Load_OnAnUnknownSeverity_TreatsItAsAWarning()
    {
        With(dir =>
        {
            var path = Path.Combine(dir, EnvironmentCapture.FileName);
            File.WriteAllText(path,
                """
                {
                  "capturedUtc": "2026-08-18T17:11:11Z",
                  "workload": "rhino",
                  "machine": {},
                  "host": {},
                  "findings": [ { "severity": "Catastrophe", "kind": "x", "detail": "y" } ]
                }
                """);

            var read = EnvironmentCapture.Load(path);
            Assert.Equal(ClashSeverity.Warning, Assert.Single(read.Findings).Severity);
        });
    }

    // -----------------------------------------------------------------------
    // Staleness and provenance.
    // -----------------------------------------------------------------------

    [Fact]
    public void Age_IsMeasuredFromCapturedUtc()
    {
        var capture = EnvironmentCapture.Create(
            "rhino", SampleHost(), Array.Empty<EnvironmentClash>(),
            capturedUtc: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        var age = capture.Age(new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(age);
        Assert.Equal(17, (int)age!.Value.TotalDays);
    }

    [Fact]
    public void Age_OnAnUnreadableTimestamp_IsNull()
    {
        var capture = new EnvironmentCapture { CapturedUtc = "sometime last week" };

        Assert.Null(capture.Age());
        Assert.Null(capture.CapturedAt());
    }

    /// <summary>
    /// A capture naming another machine is not from this one.
    /// </summary>
    /// <remarks>
    /// The QC trap: copy a results tree between machines and the target looks verified when
    /// nothing on it was probed.
    /// </remarks>
    [Fact]
    public void ACaptureFromAnotherMachine_IsNotFromThisOne()
    {
        var capture = new EnvironmentCapture
        {
            Machine = new Dictionary<string, string>
            {
                [MachineIdentity.MachineName] = Environment.MachineName + "-SOMEWHERE-ELSE",
            },
        };

        Assert.False(capture.IsFromThisMachine());
    }

    /// <summary>
    /// A capture that does not name a machine has not established that it came from here.
    /// </summary>
    /// <remarks>
    /// Absence of evidence, again: captures written before machine identity existed land here,
    /// and the honest answer is "cannot vouch", not "yes".
    /// </remarks>
    [Fact]
    public void ACaptureWithNoMachineName_DoesNotClaimToBeFromThisMachine()
    {
        Assert.False(new EnvironmentCapture().IsFromThisMachine());
        Assert.False(MachineIdentity.IsThisMachine(null));
        Assert.False(MachineIdentity.IsThisMachine(new Dictionary<string, string>()));
        Assert.False(MachineIdentity.IsThisMachine(
            new Dictionary<string, string> { [MachineIdentity.MachineName] = "   " }));
    }

    [Fact]
    public void MachineIdentity_NamesThisMachineAndItsOs()
    {
        var id = MachineIdentity.Describe();

        Assert.Equal(Environment.MachineName, id[MachineIdentity.MachineName]);
        Assert.True(id.ContainsKey(MachineIdentity.Os));
        Assert.True(MachineIdentity.IsThisMachine(id));
        Assert.Contains(Environment.MachineName, MachineIdentity.Format(id), StringComparison.Ordinal);
    }

    [Fact]
    public void MachineIdentity_Format_OnNothing_SaysSoRatherThanReturningBlank()
    {
        Assert.Contains("not recorded", MachineIdentity.Format(null), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not recorded", MachineIdentity.Format(new Dictionary<string, string>()), StringComparison.OrdinalIgnoreCase);
    }
}
