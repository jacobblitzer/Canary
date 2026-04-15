using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Canary.Agent.Protocol;

namespace Canary.Agent;

/// <summary>
/// Named pipe client that runs in the harness process.
/// Sends JSON-RPC requests to the agent and awaits responses.
/// </summary>
public sealed class HarnessClient : IDisposable
{
    private readonly string _pipeName;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _pipeLock = new(1, 1);
    private NamedPipeClientStream? _pipeClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private int _nextId;

    /// <summary>
    /// Creates a harness client targeting the specified pipe.
    /// </summary>
    /// <param name="pipeName">Named pipe name, e.g. "canary-rhino-12345".</param>
    /// <param name="timeout">Timeout for each request/response round-trip.</param>
    public HarnessClient(string pipeName, TimeSpan? timeout = null)
    {
        _pipeName = pipeName;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Connects to the agent's named pipe server.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
        => await ConnectAsync((int)_timeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Connects to the agent's named pipe server with a custom connect timeout.
    /// </summary>
    /// <param name="connectTimeoutMs">
    /// Max time to wait for the pipe to appear (e.g. app startup timeout).
    /// </param>
    public async Task ConnectAsync(int connectTimeoutMs, CancellationToken cancellationToken = default)
    {
        var connectTimeout = connectTimeoutMs;
        _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipeClient.ConnectAsync(connectTimeout, cancellationToken).ConfigureAwait(false);

        var utf8NoBom = new UTF8Encoding(false);
        _reader = new StreamReader(_pipeClient, utf8NoBom, false, 1024, leaveOpen: true);
        _writer = new StreamWriter(_pipeClient, utf8NoBom, 1024, leaveOpen: true) { AutoFlush = true };
    }

    /// <summary>
    /// Sends a heartbeat request and returns the result.
    /// </summary>
    public async Task<HeartbeatResult> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(RpcMethods.Heartbeat, null, cancellationToken).ConfigureAwait(false);
        return Deserialize<HeartbeatResult>(response);
    }

    /// <summary>
    /// Sends an execute request with the given action and parameters.
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var rpcParams = new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement(action)
        };
        foreach (var kvp in parameters)
        {
            rpcParams[kvp.Key] = JsonSerializer.SerializeToElement(kvp.Value);
        }

        var response = await SendRequestAsync(RpcMethods.Execute, rpcParams, cancellationToken).ConfigureAwait(false);
        return Deserialize<AgentResponse>(response);
    }

    /// <summary>
    /// Sends a capture screenshot request.
    /// </summary>
    public async Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings, CancellationToken cancellationToken = default)
    {
        var rpcParams = new Dictionary<string, JsonElement>
        {
            ["width"] = JsonSerializer.SerializeToElement(settings.Width),
            ["height"] = JsonSerializer.SerializeToElement(settings.Height),
            ["outputPath"] = JsonSerializer.SerializeToElement(settings.OutputPath)
        };

        var response = await SendRequestAsync(RpcMethods.CaptureScreenshot, rpcParams, cancellationToken).ConfigureAwait(false);
        return Deserialize<ScreenshotResult>(response);
    }

    /// <summary>
    /// Sends an abort request.
    /// </summary>
    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(RpcMethods.Abort, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the underlying pipe is currently connected.
    /// </summary>
    public bool IsConnected => _pipeClient?.IsConnected ?? false;

    private async Task<RpcResponse> SendRequestAsync(
        string method,
        Dictionary<string, JsonElement>? parameters,
        CancellationToken cancellationToken)
    {
        await _pipeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendRequestCoreAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pipeLock.Release();
        }
    }

    private async Task<RpcResponse> SendRequestCoreAsync(
        string method,
        Dictionary<string, JsonElement>? parameters,
        CancellationToken cancellationToken)
    {
        if (_writer == null || _reader == null)
            throw new InvalidOperationException("Client is not connected. Call ConnectAsync first.");

        var request = new RpcRequest
        {
            Id = Interlocked.Increment(ref _nextId),
            Method = method,
            Params = parameters
        };

        var line = RpcSerializer.Serialize(request);

        try
        {
            await _writer.WriteLineAsync(line).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new IOException("Pipe disconnected while sending request.", ex);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        string? responseLine;
        try
        {
            var readTask = _reader.ReadLineAsync();
            var delayTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
            var completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);

            if (completed != readTask)
                throw new TimeoutException($"Agent did not respond within {_timeout.TotalSeconds}s for method '{method}'.");

            responseLine = await readTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Agent did not respond within {_timeout.TotalSeconds}s for method '{method}'.");
        }
        catch (IOException ex)
        {
            throw new IOException("Pipe disconnected while awaiting response.", ex);
        }

        if (responseLine == null)
            throw new IOException("Pipe closed by agent (received null).");

        var response = RpcSerializer.DeserializeResponse(responseLine);

        if (response.Error != null)
            throw new InvalidOperationException($"Agent error [{response.Error.Code}]: {response.Error.Message}");

        return response;
    }

    private static T Deserialize<T>(RpcResponse response)
    {
        if (response.Result == null)
            throw new InvalidOperationException("Response result was null.");
        return response.Result.Value.Deserialize<T>()
            ?? throw new InvalidOperationException($"Failed to deserialize response result to {typeof(T).Name}.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _reader?.Dispose();
        try { _writer?.Dispose(); } catch (IOException) { }
        _pipeClient?.Dispose();
        _pipeLock.Dispose();
    }
}
