using System.Text.Json;
using Canary.Comparison;
using Canary.Config;
using Xunit;

namespace Canary.Tests.Comparison;

[Trait("Category", "Unit")]
public class VlmTests
{
    [Fact]
    public void TestCheckpoint_Mode_DefaultsToPixelDiff()
    {
        var checkpoint = new TestCheckpoint();
        Assert.Equal("pixel-diff", checkpoint.Mode);
    }

    [Fact]
    public void TestCheckpoint_Mode_RoundTripsJsonSerialization()
    {
        var json = """
            {
                "name": "vlm-test",
                "mode": "vlm",
                "description": "A red cube should be visible"
            }
            """;

        var checkpoint = JsonSerializer.Deserialize<TestCheckpoint>(json);
        Assert.NotNull(checkpoint);
        Assert.Equal("vlm", checkpoint.Mode);
        Assert.Equal("A red cube should be visible", checkpoint.Description);

        // Round-trip
        var serialized = JsonSerializer.Serialize(checkpoint);
        var deserialized = JsonSerializer.Deserialize<TestCheckpoint>(serialized);
        Assert.NotNull(deserialized);
        Assert.Equal("vlm", deserialized.Mode);
    }

    [Fact]
    public void TestCheckpoint_Mode_PixelDiff_ExplicitRoundTrips()
    {
        var json = """{"name": "pixel-test", "mode": "pixel-diff"}""";

        var checkpoint = JsonSerializer.Deserialize<TestCheckpoint>(json);
        Assert.NotNull(checkpoint);
        Assert.Equal("pixel-diff", checkpoint.Mode);
    }

    [Fact]
    public void TestCheckpoint_Mode_OmittedDefaultsToPixelDiff()
    {
        var json = """{"name": "no-mode-test"}""";

        var checkpoint = JsonSerializer.Deserialize<TestCheckpoint>(json);
        Assert.NotNull(checkpoint);
        Assert.Equal("pixel-diff", checkpoint.Mode);
    }

    [Fact]
    public void VlmConfig_DefaultValues()
    {
        var config = new VlmConfig();
        Assert.Equal("claude", config.Provider);
        Assert.Equal("claude-sonnet-4-20250514", config.Model);
        Assert.Equal(1024, config.MaxTokens);
    }

    [Fact]
    public void VlmConfig_RoundTripsJsonSerialization()
    {
        var json = """
            {
                "provider": "claude",
                "model": "claude-sonnet-4-20250514",
                "maxTokens": 2048
            }
            """;

        var config = JsonSerializer.Deserialize<VlmConfig>(json);
        Assert.NotNull(config);
        Assert.Equal("claude", config.Provider);
        Assert.Equal("claude-sonnet-4-20250514", config.Model);
        Assert.Equal(2048, config.MaxTokens);
    }

    [Fact]
    public void TestSetup_Vlm_DeserializesFromJson()
    {
        var json = """
            {
                "backend": "webgpu",
                "vlm": {
                    "provider": "claude",
                    "model": "claude-sonnet-4-20250514"
                }
            }
            """;

        var setup = JsonSerializer.Deserialize<TestSetup>(json);
        Assert.NotNull(setup);
        Assert.NotNull(setup.Vlm);
        Assert.Equal("claude", setup.Vlm.Provider);
    }

    [Fact]
    public void TestSetup_Vlm_NullWhenOmitted()
    {
        var json = """{"backend": "webgpu"}""";

        var setup = JsonSerializer.Deserialize<TestSetup>(json);
        Assert.NotNull(setup);
        Assert.Null(setup.Vlm);
    }

    [Fact]
    public void VlmVerdict_PassingVerdict()
    {
        var verdict = new VlmVerdict
        {
            Passed = true,
            Confidence = 0.95,
            Reasoning = "The screenshot shows a red cube centered on screen."
        };

        Assert.True(verdict.Passed);
        Assert.Equal(0.95, verdict.Confidence);
        Assert.Contains("red cube", verdict.Reasoning);
    }

    [Fact]
    public void ClaudeVlmProvider_ParseVerdict_ValidJson()
    {
        var json = """{"pass": true, "confidence": 0.92, "reasoning": "Image matches description."}""";

        var verdict = ClaudeVlmProvider.ParseVerdict(json);

        Assert.True(verdict.Passed);
        Assert.Equal(0.92, verdict.Confidence);
        Assert.Equal("Image matches description.", verdict.Reasoning);
    }

    [Fact]
    public void ClaudeVlmProvider_ParseVerdict_FailingVerdict()
    {
        var json = """{"pass": false, "confidence": 0.85, "reasoning": "No cube visible in screenshot."}""";

        var verdict = ClaudeVlmProvider.ParseVerdict(json);

        Assert.False(verdict.Passed);
        Assert.Equal(0.85, verdict.Confidence);
        Assert.Contains("No cube", verdict.Reasoning);
    }

    [Fact]
    public void ClaudeVlmProvider_ParseVerdict_MissingOptionalFields()
    {
        var json = """{"pass": true}""";

        var verdict = ClaudeVlmProvider.ParseVerdict(json);

        Assert.True(verdict.Passed);
        Assert.Equal(0.0, verdict.Confidence);
        Assert.Equal(string.Empty, verdict.Reasoning);
    }

    [Fact]
    public void ClaudeVlmProvider_ParseVerdict_InvalidJson_ReturnsFail()
    {
        var verdict = ClaudeVlmProvider.ParseVerdict("not json at all");

        Assert.False(verdict.Passed);
        Assert.Equal(0.0, verdict.Confidence);
        Assert.Contains("Failed to parse", verdict.Reasoning);
    }

    [Fact]
    public void ClaudeVlmProvider_ParseResponse_ValidApiResponse()
    {
        var apiResponse = """
            {
                "id": "msg_123",
                "type": "message",
                "role": "assistant",
                "content": [
                    {
                        "type": "text",
                        "text": "{\"pass\": true, \"confidence\": 0.88, \"reasoning\": \"The scene contains a red cube.\"}"
                    }
                ],
                "model": "claude-sonnet-4-20250514",
                "stop_reason": "end_turn"
            }
            """;

        var verdict = ClaudeVlmProvider.ParseResponse(apiResponse);

        Assert.True(verdict.Passed);
        Assert.Equal(0.88, verdict.Confidence);
        Assert.Contains("red cube", verdict.Reasoning);
    }

    [Fact]
    public void ClaudeVlmProvider_ParseResponse_NoTextBlock_ReturnsFail()
    {
        var apiResponse = """
            {
                "id": "msg_123",
                "type": "message",
                "role": "assistant",
                "content": []
            }
            """;

        var verdict = ClaudeVlmProvider.ParseResponse(apiResponse);

        Assert.False(verdict.Passed);
        Assert.Contains("No text content", verdict.Reasoning);
    }

    [Fact]
    public void VlmEvaluator_ResolveApiKey_ReturnsFirstNonEmpty()
    {
        // This test checks the logic, not actual env vars.
        // We can't easily mock Environment.GetEnvironmentVariable,
        // so just verify it doesn't throw when keys are missing.
        var result = VlmEvaluator.ResolveApiKey("CANARY_TEST_NONEXISTENT_VAR_12345");
        Assert.Null(result);
    }

    [Fact]
    public void VlmEvaluator_Create_UnknownProvider_Throws()
    {
        var config = new VlmConfig { Provider = "gpt-vision-unknown" };

        var ex = Assert.Throws<InvalidOperationException>(() => VlmEvaluator.Create(config));
        Assert.Contains("Unknown VLM provider", ex.Message);
    }

    [Fact]
    public void FullTestDefinition_WithVlm_DeserializesCorrectly()
    {
        var json = """
            {
                "name": "vlm-red-cube-visible",
                "workload": "penumbra",
                "setup": {
                    "scene": { "sceneName": "basic-shapes" },
                    "backend": "webgpu",
                    "canvas": { "width": 960, "height": 540 },
                    "vlm": {
                        "provider": "claude",
                        "model": "claude-sonnet-4-20250514"
                    }
                },
                "checkpoints": [
                    {
                        "name": "red-cube-centered",
                        "mode": "vlm",
                        "description": "A red cube should be centered in the viewport."
                    }
                ]
            }
            """;

        var def = TestDefinition.Parse(json);

        Assert.Equal("vlm-red-cube-visible", def.Name);
        Assert.NotNull(def.Setup?.Vlm);
        Assert.Equal("claude", def.Setup.Vlm.Provider);
        Assert.Single(def.Checkpoints);
        Assert.Equal("vlm", def.Checkpoints[0].Mode);
        Assert.Contains("red cube", def.Checkpoints[0].Description);
    }

    [Fact]
    public void MixedModeTest_PixelDiffAndVlm_DeserializesCorrectly()
    {
        var json = """
            {
                "name": "mixed-mode-test",
                "workload": "penumbra",
                "setup": {
                    "vlm": { "provider": "claude" }
                },
                "checkpoints": [
                    {
                        "name": "pixel-check",
                        "tolerance": 0.02,
                        "description": "Standard pixel comparison"
                    },
                    {
                        "name": "vlm-check",
                        "mode": "vlm",
                        "description": "VLM evaluation of scene"
                    }
                ]
            }
            """;

        var def = TestDefinition.Parse(json);

        Assert.Equal(2, def.Checkpoints.Count);
        Assert.Equal("pixel-diff", def.Checkpoints[0].Mode);
        Assert.Equal("vlm", def.Checkpoints[1].Mode);
    }

    // --- Ollama provider tests ---

    [Fact]
    public void OllamaVlmProvider_ParseVerdict_ValidJson()
    {
        var json = """{"pass": true, "confidence": 0.87, "reasoning": "Scene matches description."}""";

        var verdict = OllamaVlmProvider.ParseVerdict(json);

        Assert.True(verdict.Passed);
        Assert.Equal(0.87, verdict.Confidence);
        Assert.Equal("Scene matches description.", verdict.Reasoning);
    }

    [Fact]
    public void OllamaVlmProvider_ParseVerdict_FailingVerdict()
    {
        var json = """{"pass": false, "confidence": 0.72, "reasoning": "Expected red cube but none visible."}""";

        var verdict = OllamaVlmProvider.ParseVerdict(json);

        Assert.False(verdict.Passed);
        Assert.Equal(0.72, verdict.Confidence);
        Assert.Contains("Expected red cube", verdict.Reasoning);
    }

    [Fact]
    public void OllamaVlmProvider_ParseVerdict_InvalidJson_ReturnsFail()
    {
        var verdict = OllamaVlmProvider.ParseVerdict("garbage text");

        Assert.False(verdict.Passed);
        Assert.Equal(0.0, verdict.Confidence);
        Assert.Contains("Failed to parse", verdict.Reasoning);
    }

    [Fact]
    public void OllamaVlmProvider_ParseResponse_ValidChatResponse()
    {
        var chatResponse = """
            {
                "model": "gemma4:e4b",
                "created_at": "2026-04-28T12:00:00Z",
                "message": {
                    "role": "assistant",
                    "content": "{\"pass\": true, \"confidence\": 0.91, \"reasoning\": \"Orange box with spherical cavity visible.\"}"
                },
                "done": true,
                "total_duration": 5000000000
            }
            """;

        var verdict = OllamaVlmProvider.ParseResponse(chatResponse);

        Assert.True(verdict.Passed);
        Assert.Equal(0.91, verdict.Confidence);
        Assert.Contains("spherical cavity", verdict.Reasoning);
    }

    [Fact]
    public void OllamaVlmProvider_ParseResponse_EmptyContent_ReturnsFail()
    {
        var chatResponse = """
            {
                "model": "gemma4:e4b",
                "message": {
                    "role": "assistant",
                    "content": ""
                },
                "done": true
            }
            """;

        var verdict = OllamaVlmProvider.ParseResponse(chatResponse);

        Assert.False(verdict.Passed);
        Assert.Contains("Empty content", verdict.Reasoning);
    }

    [Fact]
    public void VlmEvaluator_Create_OllamaProvider_Succeeds()
    {
        var config = new VlmConfig { Provider = "ollama", Model = "gemma4:e4b" };

        var provider = VlmEvaluator.Create(config);

        Assert.NotNull(provider);
        Assert.IsType<OllamaVlmProvider>(provider);
    }

    [Fact]
    public void VlmConfig_OllamaProvider_RoundTrips()
    {
        var json = """
            {
                "provider": "ollama",
                "model": "gemma4:e4b",
                "maxTokens": 1024
            }
            """;

        var config = JsonSerializer.Deserialize<VlmConfig>(json);
        Assert.NotNull(config);
        Assert.Equal("ollama", config.Provider);
        Assert.Equal("gemma4:e4b", config.Model);

        var serialized = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<VlmConfig>(serialized);
        Assert.NotNull(deserialized);
        Assert.Equal("ollama", deserialized.Provider);
    }
}
