using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Config;

/// <summary>
/// A test definition loaded from a JSON file. Describes what to do and what to verify.
/// </summary>
public sealed class TestDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("workload")]
    public string Workload { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("setup")]
    public TestSetup? Setup { get; set; }

    /// <summary>
    /// "fresh" (default) launches a new app instance per test. "shared" allows
    /// consecutive tests sharing the same workload + setup.file to run inside
    /// one app instance — only the first test launches and opens the fixture;
    /// subsequent tests reuse the running app and only run their actions.
    /// Tests in "shared" mode should start their actions list with a cleanup
    /// step (e.g. Slop Cleanup toggle pulse) so prior test state is wiped.
    /// </summary>
    [JsonPropertyName("runMode")]
    public string RunMode { get; set; } = "fresh";

    [JsonPropertyName("recording")]
    public string Recording { get; set; } = string.Empty;

    [JsonPropertyName("checkpoints")]
    public List<TestCheckpoint> Checkpoints { get; set; } = new();

    /// <summary>
    /// Pre-checkpoint actions dispatched directly to the agent. Each action's
    /// <c>type</c> is the agent action name (e.g. "GrasshopperSetPanelText");
    /// remaining JSON fields become the parameter dictionary. Used by the CPig
    /// regression workload to set Slop's JsonPath panel + Build toggle before
    /// the first checkpoint. Run sequentially; failure of any action aborts
    /// the test with status=Crashed.
    /// </summary>
    [JsonPropertyName("actions")]
    public List<TestAction> Actions { get; set; } = new();

    /// <summary>
    /// Per-checkpoint assertions evaluated after the screenshot capture/diff.
    /// Asserts are evaluated even if pixel diff passes — they catch errors
    /// that don't visually surface (e.g. CPig's Slop component reports
    /// Success=False without changing the canvas geometry).
    /// </summary>
    [JsonPropertyName("asserts")]
    public List<TestAssert> Asserts { get; set; } = new();

    /// <summary>
    /// When true and this test fails or crashes, the target application is kept
    /// alive after the suite completes so the user can inspect state manually.
    /// In the CLI, acts as an automatic --keep-open. In the UI, the runner
    /// panel shows a "Close App" button instead of killing immediately.
    /// </summary>
    [JsonPropertyName("keepOpenOnFailure")]
    public bool KeepOpenOnFailure { get; set; }

    /// <summary>
    /// Parse a test definition from a JSON string.
    /// </summary>
    public static TestDefinition Parse(string json)
    {
        var def = JsonSerializer.Deserialize<TestDefinition>(json)
            ?? throw new JsonException("Failed to deserialize test definition.");

        if (string.IsNullOrWhiteSpace(def.Name))
            throw new JsonException("Test definition is missing required field 'name'.");

        if (string.IsNullOrWhiteSpace(def.Workload))
            throw new JsonException("Test definition is missing required field 'workload'.");

        return def;
    }

    /// <summary>
    /// Load a test definition from a file.
    /// </summary>
    public static async Task<TestDefinition> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return Parse(json);
    }
}

/// <summary>
/// Setup configuration for a test — what file to open, viewport settings, setup commands.
/// </summary>
public sealed class TestSetup
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("viewport")]
    public ViewportSetup? Viewport { get; set; }

    [JsonPropertyName("commands")]
    public List<string> Commands { get; set; } = new();

    /// <summary>
    /// Scene to load (Penumbra). Contains "index" for the scene number.
    /// </summary>
    [JsonPropertyName("scene")]
    public SceneSetup? Scene { get; set; }

    /// <summary>
    /// Rendering backend (Penumbra): "webgpu" or "webgl2".
    /// </summary>
    [JsonPropertyName("backend")]
    public string Backend { get; set; } = string.Empty;

    /// <summary>
    /// Canvas dimensions for deterministic captures (Penumbra).
    /// </summary>
    [JsonPropertyName("canvas")]
    public CanvasSetup? Canvas { get; set; }

    /// <summary>
    /// VLM oracle configuration for checkpoints with <c>mode = "vlm"</c>.
    /// Optional — only needed when at least one checkpoint uses VLM mode.
    /// </summary>
    [JsonPropertyName("vlm")]
    public VlmConfig? Vlm { get; set; }
}

/// <summary>
/// Viewport configuration for test setup.
/// </summary>
public sealed class ViewportSetup
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 800;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 600;

    [JsonPropertyName("projection")]
    public string Projection { get; set; } = string.Empty;

    [JsonPropertyName("displayMode")]
    public string DisplayMode { get; set; } = string.Empty;
}

/// <summary>
/// A checkpoint within a test — a moment to capture and compare a screenshot.
/// </summary>
public sealed class TestCheckpoint
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("atTimeMs")]
    public long AtTimeMs { get; set; }

    [JsonPropertyName("tolerance")]
    public double Tolerance { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Capture source: "viewport" (default) takes a screenshot of the target
    /// window; "file" reads a file path from a Grasshopper panel (identified
    /// by <see cref="PanelNickname"/>) and copies that file as the candidate
    /// image. Use "file" for render outputs (e.g. Pigture Cycles renders)
    /// that are saved to disk by the definition itself.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "viewport";

    /// <summary>
    /// Nickname of a Grasshopper panel whose text contains the file path to
    /// use as the candidate image. Required when <see cref="Source"/> is "file";
    /// ignored otherwise.
    /// </summary>
    [JsonPropertyName("panelNickname")]
    public string? PanelNickname { get; set; }

    /// <summary>
    /// Comparison mode: "pixel-diff" (default) uses baseline comparison,
    /// "vlm" uses a Vision-Language Model to evaluate the screenshot against
    /// <see cref="Description"/>.
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "pixel-diff";

    /// <summary>
    /// Camera position for scripted captures (Penumbra). If set, the test runner
    /// uses SetCamera instead of input replay timing.
    /// </summary>
    [JsonPropertyName("camera")]
    public CameraPosition? Camera { get; set; }

    /// <summary>
    /// Milliseconds to wait for render stabilization after camera change (Penumbra).
    /// </summary>
    [JsonPropertyName("stabilizeMs")]
    public int? StabilizeMs { get; set; }
}

/// <summary>
/// Scene selection for Penumbra test setup. If SceneName is set, it is preferred
/// over Index (the runner calls LoadSceneByName and falls back to LoadScene).
/// </summary>
public sealed class SceneSetup
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// Case-insensitive substring match against the scene name. Preferred over
    /// Index because scene indices differ between WebGL2 and WebGPU backends.
    /// </summary>
    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; } = string.Empty;
}

/// <summary>
/// Canvas dimensions for deterministic Penumbra captures.
/// </summary>
public sealed class CanvasSetup
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 960;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 540;
}

/// <summary>
/// Scripted camera position in spherical coordinates (Penumbra).
/// </summary>
public sealed class CameraPosition
{
    [JsonPropertyName("azimuth")]
    public double Azimuth { get; set; }

    [JsonPropertyName("elevation")]
    public double Elevation { get; set; }

    [JsonPropertyName("distance")]
    public double Distance { get; set; }
}

/// <summary>
/// A pre-checkpoint action dispatched directly to the agent's <c>ExecuteAsync</c>.
/// <c>Type</c> is the agent action name; all other JSON fields are flattened
/// into the parameter dictionary via <see cref="AsParameters"/>.
/// </summary>
public sealed class TestAction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Captures every JSON field other than <c>type</c>. Stored as JsonElement
    /// so booleans, numbers, and strings all round-trip; <see cref="AsParameters"/>
    /// flattens them into the string-string dictionary the agent expects.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();

    public Dictionary<string, string> AsParameters()
    {
        var dict = new Dictionary<string, string>();
        foreach (var kvp in Extra)
        {
            dict[kvp.Key] = kvp.Value.ValueKind switch
            {
                JsonValueKind.String => kvp.Value.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => kvp.Value.GetRawText()
            };
        }
        return dict;
    }
}

/// <summary>
/// A post-checkpoint assertion against an agent-readable property. Today the
/// supported types are <c>PanelEquals</c>, <c>PanelContains</c>, and
/// <c>PanelDoesNotContain</c>; each calls <c>GrasshopperGetPanelText</c> and
/// string-compares the result. Unknown types are reported as failed asserts
/// rather than ignored, so typos surface.
/// </summary>
public sealed class TestAssert
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Optional friendly label shown in the result. Defaults to a description
    /// derived from <see cref="Type"/> + <see cref="Nickname"/>.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
