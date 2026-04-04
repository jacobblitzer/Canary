using System.Text.Json;
using Canary.Config;
using Xunit;

namespace Canary.Tests.UI;

public class TestEditorTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void TestDefinition_SerializeDeserialize_RoundTrips()
    {
        var def = new TestDefinition
        {
            Name = "editor-test",
            Workload = "test-wl",
            Description = "Test from editor",
            Setup = new TestSetup
            {
                File = "test.3dm",
                Viewport = new ViewportSetup
                {
                    Width = 1024,
                    Height = 768,
                    Projection = "Perspective",
                    DisplayMode = "Shaded"
                },
                Commands = new List<string> { "_Sphere", "_Zoom _All" }
            },
            Checkpoints = new List<TestCheckpoint>
            {
                new TestCheckpoint { Name = "cp1", Tolerance = 0.02 },
                new TestCheckpoint { Name = "cp2", Tolerance = 0.05 }
            }
        };

        var json = JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true });
        var loaded = TestDefinition.Parse(json);

        Assert.Equal("editor-test", loaded.Name);
        Assert.Equal("test-wl", loaded.Workload);
        Assert.NotNull(loaded.Setup);
        Assert.Equal("test.3dm", loaded.Setup!.File);
        Assert.Equal(1024, loaded.Setup.Viewport!.Width);
        Assert.Equal(2, loaded.Checkpoints.Count);
        Assert.Equal("cp1", loaded.Checkpoints[0].Name);
        Assert.Equal(0.02, loaded.Checkpoints[0].Tolerance);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TestDefinition_MissingName_ThrowsJsonException()
    {
        var json = JsonSerializer.Serialize(new { workload = "test-wl", checkpoints = Array.Empty<object>() });

        Assert.Throws<JsonException>(() => TestDefinition.Parse(json));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TestDefinition_MissingWorkload_ThrowsJsonException()
    {
        var json = JsonSerializer.Serialize(new { name = "test", checkpoints = Array.Empty<object>() });

        Assert.Throws<JsonException>(() => TestDefinition.Parse(json));
    }
}
