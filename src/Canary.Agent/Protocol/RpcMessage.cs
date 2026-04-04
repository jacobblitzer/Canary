using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Agent.Protocol;

/// <summary>
/// A JSON-RPC 2.0 request message.
/// </summary>
public sealed class RpcRequest
{
    /// <summary>JSON-RPC version — always "2.0".</summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>Sequential request identifier.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>The RPC method name (see <see cref="RpcMethods"/>).</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>Method parameters as a string-keyed dictionary.</summary>
    [JsonPropertyName("params")]
    public Dictionary<string, JsonElement>? Params { get; set; }
}

/// <summary>
/// A JSON-RPC 2.0 response message (success).
/// </summary>
public sealed class RpcResponse
{
    /// <summary>JSON-RPC version — always "2.0".</summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>Matching request identifier.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>The result payload. Null when <see cref="Error"/> is set.</summary>
    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    /// <summary>Error payload. Null on success.</summary>
    [JsonPropertyName("error")]
    public RpcError? Error { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 error object.
/// </summary>
public sealed class RpcError
{
    /// <summary>Numeric error code.</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>Human-readable error message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Serialization helpers for JSON-RPC messages.
/// </summary>
public static class RpcSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serialize a request to a single-line JSON string.
    /// </summary>
    public static string Serialize(RpcRequest request)
    {
        return JsonSerializer.Serialize(request, Options);
    }

    /// <summary>
    /// Serialize a response to a single-line JSON string.
    /// </summary>
    public static string Serialize(RpcResponse response)
    {
        return JsonSerializer.Serialize(response, Options);
    }

    /// <summary>
    /// Deserialize a JSON string to an RPC request.
    /// </summary>
    public static RpcRequest DeserializeRequest(string json)
    {
        return JsonSerializer.Deserialize<RpcRequest>(json, Options)
            ?? throw new JsonException("Failed to deserialize RPC request: result was null.");
    }

    /// <summary>
    /// Deserialize a JSON string to an RPC response.
    /// </summary>
    public static RpcResponse DeserializeResponse(string json)
    {
        return JsonSerializer.Deserialize<RpcResponse>(json, Options)
            ?? throw new JsonException("Failed to deserialize RPC response: result was null.");
    }

    /// <summary>
    /// Create a success response with the given result object.
    /// </summary>
    public static RpcResponse SuccessResponse(int id, object result)
    {
        var element = JsonSerializer.SerializeToElement(result, Options);
        return new RpcResponse { Id = id, Result = element };
    }

    /// <summary>
    /// Create an error response.
    /// </summary>
    public static RpcResponse ErrorResponse(int id, int code, string message)
    {
        return new RpcResponse
        {
            Id = id,
            Error = new RpcError { Code = code, Message = message }
        };
    }
}
