using System.Text.Json.Serialization;

namespace Canary.Input;

/// <summary>
/// Type of input event.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InputEventType
{
    /// <summary>Mouse cursor moved.</summary>
    MouseMove,

    /// <summary>Mouse button pressed.</summary>
    MouseDown,

    /// <summary>Mouse button released.</summary>
    MouseUp,

    /// <summary>Mouse wheel scrolled (vertical).</summary>
    MouseWheel,

    /// <summary>Mouse wheel scrolled (horizontal / touchpad pan).</summary>
    MouseHWheel,

    /// <summary>Keyboard key pressed.</summary>
    KeyDown,

    /// <summary>Keyboard key released.</summary>
    KeyUp
}

/// <summary>
/// Mouse button identifier.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MouseButton
{
    /// <summary>Left mouse button.</summary>
    Left,

    /// <summary>Right mouse button.</summary>
    Right,

    /// <summary>Middle mouse button.</summary>
    Middle
}

/// <summary>
/// A single recorded input event with viewport-relative coordinates.
/// </summary>
public sealed class InputEvent
{
    /// <summary>Time offset in milliseconds from the start of the recording.</summary>
    [JsonPropertyName("t")]
    public long TimestampMs { get; set; }

    /// <summary>Type of input event.</summary>
    [JsonPropertyName("type")]
    public InputEventType Type { get; set; }

    /// <summary>Normalized viewport X coordinate [0.0, 1.0]. Null for key events.</summary>
    [JsonPropertyName("vx")]
    public double? ViewportX { get; set; }

    /// <summary>Normalized viewport Y coordinate [0.0, 1.0]. Null for key events.</summary>
    [JsonPropertyName("vy")]
    public double? ViewportY { get; set; }

    /// <summary>Mouse button for MouseDown/MouseUp events.</summary>
    [JsonPropertyName("button")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouseButton? Button { get; set; }

    /// <summary>Key name for KeyDown/KeyUp events (uses .NET Keys enum names).</summary>
    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Key { get; set; }

    /// <summary>Wheel scroll delta for MouseWheel events.</summary>
    [JsonPropertyName("wheelDelta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WheelDelta { get; set; }
}
