using System.Diagnostics;
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

    /// <summary>
    /// The PID of the target app process (extracted from the pipe name), used for
    /// breakpoint-detection diagnostics when an RPC times out. Set by RhinoSessionAgent
    /// after launch. Zero = unknown (skip the check).
    /// </summary>
    public int TargetProcessId { get; set; }
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
        // Per-field serialization (vs whole-object) keeps the wire format stable and
        // explicit. Any new CaptureSettings field MUST be added BOTH here and in
        // AgentServer.HandleCaptureAsync — otherwise the field is silently dropped on
        // the wire (the original Phase 4.6.F Session B bug: RecordGif passed
        // orchestrator-side but never reached the agent).
        var rpcParams = new Dictionary<string, JsonElement>
        {
            ["width"] = JsonSerializer.SerializeToElement(settings.Width),
            ["height"] = JsonSerializer.SerializeToElement(settings.Height),
            ["outputPath"] = JsonSerializer.SerializeToElement(settings.OutputPath),
            ["includeFullScreen"] = JsonSerializer.SerializeToElement(settings.IncludeFullScreen),
            ["recordGif"] = JsonSerializer.SerializeToElement(settings.RecordGif),
            ["gifFrameCount"] = JsonSerializer.SerializeToElement(settings.GifFrameCount),
            ["gifFrameIntervalMs"] = JsonSerializer.SerializeToElement(settings.GifFrameIntervalMs)
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
                throw new TimeoutException(BuildTimeoutMessage(method));

            responseLine = await readTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(BuildTimeoutMessage(method));
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

    /// <summary>
    /// Builds a timeout error message that includes breakpoint detection.
    /// When the target process has a debugger attached, the most likely cause of an
    /// RPC timeout is a Debugger.Break() in a Grasshopper component's SolveInstance —
    /// which blocks the UI thread, and RhinoApp.Wait() on the RPC thread then blocks
    /// waiting for the UI thread to pump messages. The operator sees "did not respond
    /// within Xs" with no hint that a breakpoint is the cause. This checks the target
    /// process for a debugger and appends a clear hint.
    /// </summary>
    private string BuildTimeoutMessage(string method)
    {
        var baseMsg = $"Agent did not respond within {_timeout.TotalSeconds}s for method '{method}'.";

        // Check if the target process has a debugger attached. A debugger-attached
        // process is the strongest signal that a Debugger.Break() fired inside a
        // component and is blocking the solution.
        if (TargetProcessId != 0)
        {
            try
            {
                var proc = Process.GetProcessById(TargetProcessId);
                // The most reliable check: if the process is being debugged, its
                // threads are often in a wait state. We can also check whether any
                // debugger process (vsjitdebugger, devenv, etc.) is running.
                bool debuggerRunning = IsDebuggerPresentOnSystem();
                if (debuggerRunning)
                {
                    return baseMsg + " LIKELY BREAKPOINT: a debugger is attached to the target process " +
                           $"(PID {TargetProcessId}). A Debugger.Break() in a Grasshopper component " +
                           "may be blocking the solution. Check the Rhino window — if a 'No debugger " +
                           "available / JIT debugger' dialog is showing, dismiss it and remove the " +
                           "Debugger.Break() from the component source. Search for " +
                           "'Debugger.Break' in CPig.Grasshopper/Components/ and CPig.Interop/.";
                }
            }
            catch { /* process may have exited */ }
        }

        return baseMsg + " This may be a breakpoint (Debugger.Break in a component) or a genuinely slow solution. " +
               "If Rhino's UI is frozen, check for a JIT-debugger dialog.";
    }

    /// <summary>
    /// Checks whether any common debugger process is running on the system.
    /// Not perfect (can't detect all debuggers), but catches the common ones that
    /// attach to a crashed process: VS JIT debugger, Visual Studio, Rider, etc.
    /// </summary>
    private static bool IsDebuggerPresentOnSystem()
    {
        try
        {
            var procs = Process.GetProcesses();
            foreach (var p in procs)
            {
                try
                {
                    var name = p.ProcessName.ToLowerInvariant();
                    if (name.Contains("vsjitdebugger") || name.Contains("devenv") ||
                        name.Contains("rider") || name.Contains("vsdbg") ||
                        name.Contains("mono-debugger") || name.Contains("lldb"))
                        return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
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
