using System.Text.Json;
using Canary.Config;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

/// <summary>
/// Phase 8.6 — runtime test mode duality. Verifies the <c>--mode</c> CLI
/// flag's semantics + <c>setup.vlmDescription</c> JSON round-trip.
///
/// The runner-level mode resolution is tested via
/// <see cref="ModeOverrideResolutionFunctionalTests"/> (separate file) which
/// instantiates a real TestRunner. This file covers the pure-data layer:
/// enum default, JSON schema, fallback precedence rule.
/// </summary>
[Trait("Category", "Unit")]
public class ModeOverrideTests
{
    [Fact]
    public void TestSetup_VlmDescription_DefaultsToNull()
    {
        var setup = new TestSetup();
        Assert.Null(setup.VlmDescription);
    }

    [Fact]
    public void TestSetup_VlmDescription_RoundTripsJsonSerialization()
    {
        var json = """
            {
                "file": "fixtures/cpig_slop_loader.gh",
                "vlm": { "provider": "ollama", "model": "gemma4:e2b" },
                "vlmDescription": "A quad-dominant remesh of a sphere with cleanly aligned faces."
            }
            """;
        var setup = JsonSerializer.Deserialize<TestSetup>(json);
        Assert.NotNull(setup);
        Assert.Equal("A quad-dominant remesh of a sphere with cleanly aligned faces.", setup.VlmDescription);
        Assert.Equal("ollama", setup.Vlm?.Provider);

        // Round-trip preserves the field.
        var serialized = JsonSerializer.Serialize(setup);
        var redeserialized = JsonSerializer.Deserialize<TestSetup>(serialized);
        Assert.NotNull(redeserialized);
        Assert.Equal(setup.VlmDescription, redeserialized.VlmDescription);
    }

    [Fact]
    public void TestSetup_VlmDescription_OmittedRoundTripsAsNull()
    {
        var json = """{"file": "fixtures/x.gh"}""";
        var setup = JsonSerializer.Deserialize<TestSetup>(json);
        Assert.NotNull(setup);
        Assert.Null(setup.VlmDescription);
        Assert.Null(setup.Vlm);
    }

    [Fact]
    public void ModeOverride_Enum_HasFourValues()
    {
        // Guards against accidental enum changes that would break the
        // resolver's switch expression in ResolveEffectiveModes.
        var values = System.Enum.GetValues<ModeOverride>();
        Assert.Contains(ModeOverride.None, values);
        Assert.Contains(ModeOverride.PixelDiff, values);
        Assert.Contains(ModeOverride.Vlm, values);
        Assert.Contains(ModeOverride.Both, values);
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void CheckpointMode_Enum_HasTwoValues()
    {
        // ResolveEffectiveModes returns 1-or-2 of these. Adding a third would
        // change the dispatcher's iteration semantics.
        var values = System.Enum.GetValues<CheckpointMode>();
        Assert.Contains(CheckpointMode.PixelDiff, values);
        Assert.Contains(CheckpointMode.Vlm, values);
        Assert.Equal(2, values.Length);
    }
}
