using System.Text;
using System.Text.Json;

namespace Canary.Comparison;

/// <summary>
/// Calls a local Ollama instance with a screenshot and description,
/// parses the structured JSON verdict from the model response.
/// Ollama must be running at <c>http://localhost:11434</c>.
/// </summary>
public sealed class OllamaVlmProvider : IVlmProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;

    private const string SystemPrompt =
        """
        You are a visual testing oracle. Given a screenshot and a description of what it should show, evaluate whether the screenshot matches the description.
        Respond ONLY with JSON (no markdown fences): {"pass": true/false, "confidence": 0.0-1.0, "reasoning": "..."}
        """;

    public OllamaVlmProvider(HttpClient http, string model, string baseUrl = "http://localhost:11434")
    {
        _http = http;
        _model = model;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<VlmVerdict> EvaluateAsync(byte[] imageBytes, string description, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(imageBytes);

        // Ollama /api/chat format with vision support
        var requestBody = new
        {
            model = _model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = SystemPrompt
                },
                new
                {
                    role = "user",
                    content = $"Evaluate this screenshot against the following description:\n\n{description}",
                    images = new[] { base64 }
                }
            },
            stream = false,
            format = "json"
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync($"{_baseUrl}/api/chat", content, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new VlmVerdict
            {
                Passed = false,
                Confidence = 0,
                Reasoning = $"Cannot reach Ollama at {_baseUrl}: {ex.Message}. Is Ollama running?"
            };
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
                Reasoning = $"Ollama error {(int)response.StatusCode}: {errorBody}"
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

            // Ollama /api/chat response: { "message": { "role": "assistant", "content": "..." }, ... }
            var message = root.GetProperty("message");
            var textContent = message.GetProperty("content").GetString();

            if (string.IsNullOrWhiteSpace(textContent))
            {
                return new VlmVerdict
                {
                    Passed = false,
                    Confidence = 0,
                    Reasoning = "Empty content in Ollama response"
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
                Reasoning = $"Failed to parse Ollama response: {ex.Message}"
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
