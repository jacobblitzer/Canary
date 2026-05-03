using System.Text.Json;
using Canary.Config;
using Xunit;

namespace Canary.Tests.Config;

/// <summary>
/// Penumbra ADR 0011 — DisplayState model. Verifies that the new
/// <c>setup.displayPreset</c> field deserializes correctly, defaults to
/// null, and round-trips through JSON without modification.
///
/// The runner-level dispatch (calling LoadDisplayPreset on the agent) is
/// covered indirectly by the Penumbra workload's smoke suite once it
/// lands; here we just lock the schema.
/// </summary>
[Trait("Category", "Unit")]
public class DisplayPresetTests
{
    [Fact]
    public void TestSetup_DisplayPreset_DefaultsToNull()
    {
        var setup = new TestSetup();
        Assert.Null(setup.DisplayPreset);
    }

    [Fact]
    public void TestSetup_DisplayPreset_RoundTripsJsonSerialization()
    {
        var json = """
            {
                "file": "fixtures/sdf-teapot.scene.json",
                "scene": { "name": "sdf-teapot" },
                "displayPreset": "particulate-cloud"
            }
            """;
        var setup = JsonSerializer.Deserialize<TestSetup>(json);
        Assert.NotNull(setup);
        Assert.Equal("particulate-cloud", setup!.DisplayPreset);

        var serialized = JsonSerializer.Serialize(setup);
        var redeserialized = JsonSerializer.Deserialize<TestSetup>(serialized);
        Assert.NotNull(redeserialized);
        Assert.Equal(setup.DisplayPreset, redeserialized!.DisplayPreset);
    }

    [Fact]
    public void TestSetup_DisplayPreset_OmittedRoundTripsAsNull()
    {
        var json = """{"file": "fixtures/x.scene.json"}""";
        var setup = JsonSerializer.Deserialize<TestSetup>(json);
        Assert.NotNull(setup);
        Assert.Null(setup!.DisplayPreset);
    }

    [Fact]
    public void TestSetup_DisplayPreset_AcceptsAllShippedPresetNames()
    {
        // Names mirror Penumbra/packages/runtime/src/display-presets/*.json.
        // Drift here means the catalog moved underneath us — bump the test
        // and update Canary/spec/PENUMBRA_WORKLOAD.md.
        string[] shipped = {
            "default",
            "diagnostic-bricks",
            "diagnostic-aabbs",
            "diagnostic-march-steps",
            "particulate-cloud",
            "particulate-blend",
            "smoke-test",
            "monument",
        };

        foreach (var name in shipped)
        {
            var json = $$"""
                {
                    "file": "fixtures/x.scene.json",
                    "displayPreset": "{{name}}"
                }
                """;
            var setup = JsonSerializer.Deserialize<TestSetup>(json);
            Assert.NotNull(setup);
            Assert.Equal(name, setup!.DisplayPreset);
        }
    }
}
