using System.Text.Json;
using Canary.Config;
using Xunit;

namespace Canary.Tests.Config;

[Trait("Category", "Unit")]
public class TestDefinitionTests
{
    private const string ValidTestJson = """
        {
          "name": "sculpt-standard-undo",
          "workload": "pigment",
          "description": "Sculpt with standard brush, verify displacement, undo",
          "setup": {
            "file": "test_sphere.3dm",
            "viewport": {
              "width": 800,
              "height": 600,
              "projection": "Perspective",
              "displayMode": "Shaded"
            },
            "commands": [
              "SetView World Perspective",
              "Zoom All Extents"
            ]
          },
          "recording": "sculpt-standard-undo.input.json",
          "checkpoints": [
            {
              "name": "after_stroke",
              "atTimeMs": 1500,
              "tolerance": 0.02,
              "description": "Mesh should show displacement"
            },
            {
              "name": "after_undo",
              "atTimeMs": 3000,
              "tolerance": 0.01,
              "description": "Mesh should match original after Ctrl+Z"
            }
          ]
        }
        """;

    [Fact]
    public void TestDefinition_Parse_ValidJson_AllFieldsPopulated()
    {
        var def = TestDefinition.Parse(ValidTestJson);

        Assert.Equal("sculpt-standard-undo", def.Name);
        Assert.Equal("pigment", def.Workload);
        Assert.NotNull(def.Setup);
        Assert.Equal("test_sphere.3dm", def.Setup.File);
        Assert.NotNull(def.Setup.Viewport);
        Assert.Equal(800, def.Setup.Viewport.Width);
        Assert.Equal(600, def.Setup.Viewport.Height);
        Assert.Equal("Perspective", def.Setup.Viewport.Projection);
        Assert.Equal("Shaded", def.Setup.Viewport.DisplayMode);
        Assert.Equal(2, def.Setup.Commands.Count);
        Assert.Equal("sculpt-standard-undo.input.json", def.Recording);
        Assert.Equal(2, def.Checkpoints.Count);
        Assert.Equal("after_stroke", def.Checkpoints[0].Name);
        Assert.Equal(1500, def.Checkpoints[0].AtTimeMs);
        Assert.Equal(0.02, def.Checkpoints[0].Tolerance);
        Assert.Equal("after_undo", def.Checkpoints[1].Name);
    }

    [Fact]
    public void TestDefinition_Parse_MissingName_ThrowsClearError()
    {
        var json = """{"workload": "pigment", "recording": "test.json"}""";

        var ex = Assert.Throws<JsonException>(() => TestDefinition.Parse(json));
        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestDefinition_Parse_EmptyCheckpoints_IsValid()
    {
        var json = """
            {
              "name": "no-checkpoints",
              "workload": "pigment",
              "recording": "test.json",
              "checkpoints": []
            }
            """;

        var def = TestDefinition.Parse(json);

        Assert.Empty(def.Checkpoints);
    }

    [Fact]
    public void TestCheckpoint_Parse_PerCheckpointViewportOverride_RoundTrips()
    {
        // Phase 14.7 — checkpoint.viewport overrides setup.viewport for the
        // 4-views-per-test pattern. Each checkpoint can pin its own projection.
        var json = """
            {
              "name": "kin-fourview",
              "workload": "rhino",
              "setup": {
                "viewport": { "width": 600, "height": 600, "projection": "Perspective", "displayMode": "Shaded" }
              },
              "checkpoints": [
                { "name": "front", "viewport": { "projection": "Front", "displayMode": "Wireframe" } },
                { "name": "top",   "viewport": { "projection": "Top",   "displayMode": "Wireframe" } },
                { "name": "right", "viewport": { "projection": "Right", "displayMode": "Wireframe" } },
                { "name": "persp" }
              ]
            }
            """;

        var def = TestDefinition.Parse(json);
        Assert.Equal(4, def.Checkpoints.Count);
        Assert.NotNull(def.Checkpoints[0].Viewport);
        Assert.Equal("Front", def.Checkpoints[0].Viewport!.Projection);
        Assert.Equal("Wireframe", def.Checkpoints[0].Viewport!.DisplayMode);
        Assert.NotNull(def.Checkpoints[1].Viewport);
        Assert.Equal("Top", def.Checkpoints[1].Viewport!.Projection);
        Assert.NotNull(def.Checkpoints[2].Viewport);
        Assert.Equal("Right", def.Checkpoints[2].Viewport!.Projection);
        // Fourth checkpoint omits viewport — falls back to setup.viewport at runtime.
        Assert.Null(def.Checkpoints[3].Viewport);
    }

    [Fact]
    public void WorkloadConfig_Parse_AllFieldsPopulated()
    {
        var json = """
            {
              "name": "pigment",
              "displayName": "Pigment (Rhino 8)",
              "appPath": "C:\\Program Files\\Rhino 8\\System\\Rhino.exe",
              "appArgs": "/nosplash",
              "agentType": "rhino",
              "pipeName": "canary-rhino",
              "startupTimeoutMs": 30000,
              "windowTitle": "Rhinoceros 8",
              "viewportClass": "Afx:00400000:8:00010011:00000000:00000000"
            }
            """;

        var config = WorkloadConfig.Parse(json);

        Assert.Equal("pigment", config.Name);
        Assert.Equal("Pigment (Rhino 8)", config.DisplayName);
        Assert.Equal(@"C:\Program Files\Rhino 8\System\Rhino.exe", config.AppPath);
        Assert.Equal("/nosplash", config.AppArgs);
        Assert.Equal("rhino", config.AgentType);
        Assert.Equal("canary-rhino", config.PipeName);
        Assert.Equal(30000, config.StartupTimeoutMs);
        Assert.Equal("Rhinoceros 8", config.WindowTitle);
    }
}
