using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Canary.Comparison;

/// <summary>
/// Calls the Anthropic Messages API with a screenshot and description,
/// parses the structured JSON verdict from the model response.
/// </summary>
public sealed class ClaudeVlmProvider : IVlmProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _maxTokens;

    private const string SystemPrompt =
        """
        You are a visual testing oracle. Given a screenshot and a description of what it should show, evaluate whether the screenshot matches the description.
        Respond ONLY with JSON (no markdown fences): {"pass": true/false, "confidence": 0.0-1.0, "reasoning": "..."}
        """;

    public ClaudeVlmProvider(HttpClient http, string apiKey, string model, int maxTokens)
    {
        _http = http;
        _model = model;
        _maxTokens = maxTokens;

        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<VlmVerdict> EvaluateAsync(byte[] imageBytes, string description, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(imageBytes);

        var requestBody = new
        {
            model = _model,
            max_tokens = _maxTokens,
            system = SystemPrompt,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = "image/png",
                                data = base64
                            }
                        },
                        new
                        {
                            type = "text",
                            text = $"Evaluate this screenshot against the following description:\n\n{description}"
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync("https://api.anthropic.com/v1/messages", content, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new VlmVerdict
            {
                Passed = false,
                Confidence = 0,
                Reasoning = $"HTTP request failed: {ex.Message}"
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new VlmVerdict
            {
                Passed = false,
                Confidence = 0,
                Reasoning = $"API error {(int)response.StatusCode}: {errorBody}"
            };
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseResponse(responseJson);
    }

    internal static VlmVerdict ParseResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // Extract the text content from the Messages API response
            var contentArray = root.GetProperty("content");
            string? textContent = null;
            foreach (var block in contentArray.EnumerateArray())
            {
                if (block.GetProperty("type").GetString() == "text")
                {
                    textContent = block.GetProperty("text").GetString();
                    break;
                }
            }

            if (textContent == null)
            {
                return new VlmVerdict
                {
                    Passed = false,
                    Confidence = 0,
                    Reasoning = "No text content in API response"
                };
            }

            return ParseVerdict(textContent);
        }
        catch (Exception ex)
        {
            return new VlmVerdict
            {
                Passed = false,
                Confidence = 0,
                Reasoning = $"Failed to parse API response: {ex.Message}"
            };
        }
    }

    internal static VlmVerdict ParseVerdict(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var pass = root.GetProperty("pass").GetBoolean();
            var confidence = root.TryGetProperty("confidence", out var confEl)
                ? confEl.GetDouble()
                : 0.0;
            var reasoning = root.TryGetProperty("reasoning", out var reasonEl)
                ? reasonEl.GetString() ?? string.Empty
                : string.Empty;

            return new VlmVerdict
            {
                Passed = pass,
                Confidence = confidence,
                Reasoning = reasoning
            };
        }
        catch (Exception ex)
        {
            return new VlmVerdict
            {
                Passed = false,
                Confidence = 0,
                Reasoning = $"Failed to parse verdict JSON: {ex.Message}. Raw text: {Truncate(text)}"
            };
        }
    }

    private static string Truncate(string s, int max = 200)
        => s.Length <= max ? s : s[..(max - 3)] + "...";
}
