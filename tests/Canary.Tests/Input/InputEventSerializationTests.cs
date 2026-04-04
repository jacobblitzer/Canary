using Canary.Input;
using Xunit;

namespace Canary.Tests.Input;

public class InputEventSerializationTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void InputEvent_Serialize_RoundTrips()
    {
        var recording = new InputRecording
        {
            Metadata = new RecordingMetadata
            {
                Workload = "pigment",
                RecordedAt = new DateTime(2026, 4, 4, 10, 30, 0, DateTimeKind.Utc),
                ViewportWidth = 800,
                ViewportHeight = 600,
                WindowTitle = "Rhinoceros 8 - test_sphere.3dm",
                DurationMs = 3200
            },
            Events = new List<InputEvent>
            {
                new() { TimestampMs = 0, Type = InputEventType.MouseMove, ViewportX = 0.50, ViewportY = 0.50 },
                new() { TimestampMs = 15, Type = InputEventType.MouseDown, ViewportX = 0.50, ViewportY = 0.50, Button = MouseButton.Left },
                new() { TimestampMs = 60, Type = InputEventType.MouseUp, ViewportX = 0.55, ViewportY = 0.45, Button = MouseButton.Left },
                new() { TimestampMs = 200, Type = InputEventType.KeyDown, Key = "ControlKey" },
                new() { TimestampMs = 210, Type = InputEventType.KeyDown, Key = "Z" },
                new() { TimestampMs = 280, Type = InputEventType.KeyUp, Key = "Z" },
                new() { TimestampMs = 290, Type = InputEventType.KeyUp, Key = "ControlKey" }
            }
        };

        var json = recording.ToJson();
        var deserialized = InputRecording.FromJson(json);

        Assert.Equal("pigment", deserialized.Metadata.Workload);
        Assert.Equal(800, deserialized.Metadata.ViewportWidth);
        Assert.Equal(600, deserialized.Metadata.ViewportHeight);
        Assert.Equal(3200, deserialized.Metadata.DurationMs);
        Assert.Equal(7, deserialized.Events.Count);

        var move = deserialized.Events[0];
        Assert.Equal(InputEventType.MouseMove, move.Type);
        Assert.Equal(0.50, move.ViewportX);
        Assert.Equal(0.50, move.ViewportY);
        Assert.Null(move.Button);
        Assert.Null(move.Key);

        var mouseDown = deserialized.Events[1];
        Assert.Equal(InputEventType.MouseDown, mouseDown.Type);
        Assert.Equal(MouseButton.Left, mouseDown.Button);

        var keyDown = deserialized.Events[3];
        Assert.Equal(InputEventType.KeyDown, keyDown.Type);
        Assert.Equal("ControlKey", keyDown.Key);
        Assert.Null(keyDown.ViewportX);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void InputRecording_Serialize_PreservesMetadata()
    {
        var recording = new InputRecording
        {
            Metadata = new RecordingMetadata
            {
                Workload = "qualia",
                ViewportWidth = 1920,
                ViewportHeight = 1080,
                WindowTitle = "Qualia Viewer",
                DurationMs = 5000
            },
            Events = new List<InputEvent>
            {
                new() { TimestampMs = 100, Type = InputEventType.MouseWheel, ViewportX = 0.5, ViewportY = 0.5, WheelDelta = 120 }
            }
        };

        var json = recording.ToJson();
        var deserialized = InputRecording.FromJson(json);

        Assert.Equal("qualia", deserialized.Metadata.Workload);
        Assert.Equal(1920, deserialized.Metadata.ViewportWidth);
        Assert.Equal(1080, deserialized.Metadata.ViewportHeight);
        var evt = Assert.Single(deserialized.Events);
        Assert.Equal(120, evt.WheelDelta);
    }
}
