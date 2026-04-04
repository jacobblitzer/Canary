using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Canary.Agent.Protocol;

namespace Canary.Agent;

/// <summary>
/// Named pipe server that runs inside the target application.
/// Listens for JSON-RPC requests from the harness and dispatches them to an <see cref="ICanaryAgent"/>.
/// </summary>
public sealed class AgentServer : IDisposable
{
    private readonly string _pipeName;
    private readonly ICanaryAgent _agent;
    private NamedPipeServerStream? _pipeServer;

    /// <summary>
    /// Creates an agent server that listens on the specified pipe name.
    /// </summary>
    /// <param name="pipeName">Named pipe name, e.g. "canary-rhino-12345".</param>
    /// <param name="agent">The agent implementation to dispatch requests to.</param>
    public AgentServer(string pipeName, ICanaryAgent agent)
    {
        _pipeName = pipeName;
        _agent = agent;
    }

    /// <summary>
    /// Starts listening for a single client connection and processes requests until
    /// cancellation or disconnection.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _pipeServer = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await _pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

        var utf8NoBom = new UTF8Encoding(false);
        using var reader = new StreamReader(_pipeServer, utf8NoBom, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(_pipeServer, utf8NoBom, 1024, leaveOpen: true) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested && _pipeServer.IsConnected)
        {
            string? line;
            try
            {
                var readTask = reader.ReadLineAsync();
                var completed = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
                if (completed != readTask)
                    break;
                line = await readTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }

            if (line == null)
                break;

            var response = await DispatchAsync(line).ConfigureAwait(false);
            var responseLine = RpcSerializer.Serialize(response);

            try
            {
                await writer.WriteLineAsync(responseLine).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
        }
    }

    private async Task<RpcResponse> DispatchAsync(string json)
    {
        RpcRequest request;
        try
        {
            request = RpcSerializer.DeserializeRequest(json);
        }
        catch (JsonException ex)
        {
            return RpcSerializer.ErrorResponse(0, -32700, $"Parse error: {ex.Message}");
        }

        try
        {
            return request.Method switch
            {
                RpcMethods.Heartbeat => RpcSerializer.SuccessResponse(request.Id, await _agent.HeartbeatAsync().ConfigureAwait(false)),
                RpcMethods.Execute => await HandleExecuteAsync(request).ConfigureAwait(false),
                RpcMethods.CaptureScreenshot => await HandleCaptureAsync(request).ConfigureAwait(false),
                RpcMethods.Abort => await HandleAbortAsync(request).ConfigureAwait(false),
                _ => RpcSerializer.ErrorResponse(request.Id, -32601, $"Method not found: {request.Method}")
            };
        }
        catch (Exception ex)
        {
            return RpcSerializer.ErrorResponse(request.Id, -1, ex.Message);
        }
    }

    private async Task<RpcResponse> HandleExecuteAsync(RpcRequest request)
    {
        var action = GetStringParam(request, "action") ?? string.Empty;
        var parameters = new Dictionary<string, string>();

        if (request.Params != null)
        {
            foreach (var kvp in request.Params)
            {
                if (kvp.Key != "action")
                    parameters[kvp.Key] = kvp.Value.ToString();
            }
        }

        var result = await _agent.ExecuteAsync(action, parameters).ConfigureAwait(false);
        return RpcSerializer.SuccessResponse(request.Id, result);
    }

    private async Task<RpcResponse> HandleCaptureAsync(RpcRequest request)
    {
        var settings = new CaptureSettings();
        if (request.Params != null)
        {
            if (request.Params.TryGetValue("width", out var w)) settings.Width = w.GetInt32();
            if (request.Params.TryGetValue("height", out var h)) settings.Height = h.GetInt32();
            if (request.Params.TryGetValue("outputPath", out var p)) settings.OutputPath = p.GetString() ?? string.Empty;
        }

        var result = await _agent.CaptureScreenshotAsync(settings).ConfigureAwait(false);
        return RpcSerializer.SuccessResponse(request.Id, result);
    }

    private async Task<RpcResponse> HandleAbortAsync(RpcRequest request)
    {
        await _agent.AbortAsync().ConfigureAwait(false);
        return RpcSerializer.SuccessResponse(request.Id, new { ok = true });
    }

    private static string? GetStringParam(RpcRequest request, string key)
    {
        if (request.Params != null && request.Params.TryGetValue(key, out var val))
            return val.GetString();
        return null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _pipeServer?.Dispose();
    }
}
