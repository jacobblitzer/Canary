using Canary.Cli;
using Xunit;

namespace Canary.Tests.Cli;

// Tests for Canary.Core's AutoRunArgs — the POCO that round-trips between the
// CLI's argv handoff and the UI's single-instance pipe message. Phase 1 / §C3.
public class AutoRunArgsTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void TryParse_NoMatchingArgs_ReturnsFalseAndEmpty()
    {
        var parsed = AutoRunArgs.TryParse(new[] { "--verbose", "--quiet" }, out var result);

        Assert.False(parsed);
        Assert.True(result.IsEmpty);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TryParse_AllFlagsPresent_PopulatesAll()
    {
        var argv = new[] { "--workload", "qualia", "--test", "main-pencil", "--suite", "smoke", "--mode", "vlm", "--verbose" };

        var parsed = AutoRunArgs.TryParse(argv, out var result);

        Assert.True(parsed);
        Assert.Equal("qualia", result.Workload);
        Assert.Equal("main-pencil", result.Test);
        Assert.Equal("smoke", result.Suite);
        Assert.Equal("vlm", result.Mode);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TryParse_WorkloadOnly_IsValid()
    {
        var parsed = AutoRunArgs.TryParse(new[] { "--workload", "rhino" }, out var result);

        Assert.True(parsed);
        Assert.Equal("rhino", result.Workload);
        Assert.Null(result.Test);
        Assert.Null(result.Suite);
        Assert.Null(result.Mode);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ToArgs_RoundTrip_PreservesAllFields()
    {
        var original = new AutoRunArgs
        {
            Workload = "penumbra",
            Test = "diag-pencil-baseline",
            Mode = "both",
        };

        var argv = original.ToArgs();
        Assert.True(AutoRunArgs.TryParse(argv, out var roundtripped));

        Assert.Equal(original.Workload, roundtripped.Workload);
        Assert.Equal(original.Test, roundtripped.Test);
        Assert.Equal(original.Suite, roundtripped.Suite);
        Assert.Equal(original.Mode, roundtripped.Mode);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ToJson_RoundTrip_PreservesAllFields()
    {
        var original = new AutoRunArgs
        {
            Workload = "qualia",
            Suite = "pencil",
            Mode = "vlm",
        };

        var json = original.ToJson();
        Assert.True(AutoRunArgs.TryParseJson(json, out var roundtripped));

        Assert.Equal(original.Workload, roundtripped.Workload);
        Assert.Equal(original.Test, roundtripped.Test);
        Assert.Equal(original.Suite, roundtripped.Suite);
        Assert.Equal(original.Mode, roundtripped.Mode);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TryParseJson_MalformedJson_ReturnsFalseAndEmpty()
    {
        var parsed = AutoRunArgs.TryParseJson("not json at all {{", out var result);

        Assert.False(parsed);
        Assert.True(result.IsEmpty);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TryParseJson_EmptyObject_ReturnsFalseAndEmpty()
    {
        var parsed = AutoRunArgs.TryParseJson("{}", out var result);

        Assert.False(parsed);
        Assert.True(result.IsEmpty);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ToArgs_OmitsNullFields()
    {
        var args = new AutoRunArgs { Workload = "rhino" };

        var argv = args.ToArgs();

        Assert.Equal(new[] { "--workload", "rhino" }, argv);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void IsEmpty_AllNull_IsTrue()
    {
        Assert.True(new AutoRunArgs().IsEmpty);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void IsEmpty_AnyFieldSet_IsFalse()
    {
        Assert.False(new AutoRunArgs { Workload = "x" }.IsEmpty);
        Assert.False(new AutoRunArgs { Test = "x" }.IsEmpty);
        Assert.False(new AutoRunArgs { Suite = "x" }.IsEmpty);
        Assert.False(new AutoRunArgs { Mode = "x" }.IsEmpty);
    }
}
