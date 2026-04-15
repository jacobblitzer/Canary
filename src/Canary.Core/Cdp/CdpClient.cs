using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Canary.Cdp;

/// <summary>
/// Minimal Chrome DevTools Protocol client over WebSocket.
/// Follows the same async request/response pattern as HarnessClient
/// but communicates via WebSocket instead of named pipes.
/// </summary>
public sealed class CdpClient : IDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> _pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode>> _eventWaiters = new();
    private readonly TimeSpan _timeout;
    private int _nextId;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private bool _disposed;

    /// <summary>
    /// Creates a new CDP client with the specified default command timeout.
    /// </summary>
    /// <param name="timeout">Timeout for individual CDP commands (default 10 seconds).</param>
    public CdpClient(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Connect to a Chrome DevTools Protocol WebSocket endpoint.
    /// </summary>
    /// <param name="webSocketUrl">The WebSocket URL from /json/version endpoint.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ConnectAsync(string webSocketUrl, CancellationToken ct = default)
    {
        await _ws.ConnectAsync(new Uri(webSocketUrl), ct).ConfigureAwait(false);
        _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);
    }

    /// <summary>
    /// Send a CDP command and await the response.
    /// </summary>
    /// <param name="method">CDP method name (e.g., "Page.navigate").</param>
    /// <param name="parameters">Optional parameters object.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The "result" field from the CDP response.</returns>
    public async Task<JsonNode?> SendAsync(string method, object? parameters = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var message = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters != null)
        {
            message["params"] = JsonSerializer.SerializeToNode(parameters);
        }

        var json = message.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            var result = await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _pending.TryRemove(id, out _);
            throw new TimeoutException($"CDP command '{method}' timed out after {_timeout.TotalSeconds}s");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Enable a CDP domain (e.g., "Page.enable", "Runtime.enable").
    /// </summary>
    public Task EnableDomainAsync(string domain, CancellationToken ct = default)
    {
        return SendAsync($"{domain}.enable", null, ct);
    }

    /// <summary>
    /// Evaluate a JavaScript expression and return the result as a string.
    /// </summary>
    /// <param name="expression">JavaScript expression to evaluate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The string value of the result, or null.</returns>
    public async Task<string?> EvaluateAsync(string expression, CancellationToken ct = default)
    {
        var result = await SendAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true
        }, ct).ConfigureAwait(false);

        var value = result?["result"]?["value"];
        return value?.ToJsonString();
    }

    /// <summary>
    /// Evaluate a JavaScript expression and deserialize the result to T.
    /// </summary>
    public async Task<T?> EvaluateAsync<T>(string expression, CancellationToken ct = default)
    {
        var result = await SendAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true
        }, ct).ConfigureAwait(false);

        // Check for exceptions
        var exceptionDetails = result?["exceptionDetails"];
        if (exceptionDetails != null)
        {
            var text = exceptionDetails["text"]?.GetValue<string>() ?? "Unknown JS error";
            throw new InvalidOperationException($"JavaScript evaluation failed: {text}");
        }

        var value = result?["result"]?["value"];
        if (value == null) return default;

        return JsonSerializer.Deserialize<T>(value.ToJsonString());
    }

    /// <summary>
    /// Wait for a specific CDP event to fire.
    /// </summary>
    /// <param name="eventName">Event name (e.g., "Page.loadEventFired").</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The event parameters.</returns>
    public async Task<JsonNode?> WaitForEventAsync(string eventName, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _eventWaiters[eventName] = tcs;

        var effectiveTimeout = timeout ?? _timeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for CDP event '{eventName}'");
        }
        finally
        {
            _eventWaiters.TryRemove(eventName, out _);
        }
    }

    /// <summary>
    /// Navigate to a URL and wait for the page to load.
    /// </summary>
    /// <param name="url">URL to navigate to.</param>
    /// <param name="loadTimeout">How long to wait for page load.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task NavigateAsync(string url, TimeSpan? loadTimeout = null, CancellationToken ct = default)
    {
        var waitTask = WaitForEventAsync("Page.loadEventFired", loadTimeout ?? TimeSpan.FromSeconds(30), ct);
        await SendAsync("Page.navigate", new { url }, ct).ConfigureAwait(false);
        await waitTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Capture a screenshot of the page or a clipped region.
    /// </summary>
    /// <param name="clip">Optional clip region (x, y, width, height in CSS pixels).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>PNG image data as a byte array.</returns>
    public async Task<byte[]> CaptureScreenshotAsync(CdpClipRect? clip = null, CancellationToken ct = default)
    {
        object parameters;
        if (clip != null)
        {
            parameters = new
            {
                format = "png",
                clip = new
                {
                    x = clip.X,
                    y = clip.Y,
                    width = clip.Width,
                    height = clip.Height,
                    scale = 1.0
                }
            };
        }
        else
        {
            parameters = new { format = "png" };
        }

        var result = await SendAsync("Page.captureScreenshot", parameters, ct).ConfigureAwait(false);
        var base64 = result?["data"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Screenshot response missing 'data' field");
        return Convert.FromBase64String(base64);
    }

    /// <summary>
    /// Dispatch a mouse event to the page.
    /// </summary>
    /// <param name="type">Event type: "mouseMoved", "mousePressed", "mouseReleased", "mouseWheel".</param>
    /// <param name="x">X coordinate in CSS pixels (page-relative).</param>
    /// <param name="y">Y coordinate in CSS pixels (page-relative).</param>
    /// <param name="button">Mouse button: "none", "left", "middle", "right".</param>
    /// <param name="clickCount">Click count (1 for single click).</param>
    /// <param name="deltaX">Horizontal scroll delta (for mouseWheel).</param>
    /// <param name="deltaY">Vertical scroll delta (for mouseWheel).</param>
    /// <param name="ct">Cancellation token.</param>
    public Task DispatchMouseEventAsync(
        string type, double x, double y,
        string button = "none", int clickCount = 0,
        double deltaX = 0, double deltaY = 0,
        CancellationToken ct = default)
    {
        return SendAsync("Input.dispatchMouseEvent", new
        {
            type,
            x,
            y,
            button,
            clickCount,
            deltaX,
            deltaY
        }, ct);
    }

    /// <summary>
    /// Close the browser gracefully.
    /// </summary>
    public async Task CloseBrowserAsync(CancellationToken ct = default)
    {
        try
        {
            await SendAsync("Browser.close", null, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Browser may already be closing — ignore errors
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[1024 * 64];
        var messageBuffer = new MemoryStream();

        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            try
            {
                messageBuffer.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    messageBuffer.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                var json = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                var node = JsonNode.Parse(json);
                if (node == null) continue;

                // Is this a response to a command?
                var id = node["id"];
                if (id != null)
                {
                    var idVal = id.GetValue<int>();
                    if (_pending.TryRemove(idVal, out var tcs))
                    {
                        var error = node["error"];
                        if (error != null)
                        {
                            var msg = error["message"]?.GetValue<string>() ?? "Unknown CDP error";
                            tcs.TrySetException(new InvalidOperationException($"CDP error: {msg}"));
                        }
                        else
                        {
                            tcs.TrySetResult(node["result"] ?? new JsonObject());
                        }
                    }
                }
                else
                {
                    // This is an event
                    var method = node["method"]?.GetValue<string>();
                    if (method != null && _eventWaiters.TryRemove(method, out var eventTcs))
                    {
                        eventTcs.TrySetResult(node["params"] ?? new JsonObject());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                break;
            }
        }

        // Fail all pending requests
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetException(new InvalidOperationException("CDP connection closed"));
        }
        _pending.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _readCts?.Cancel();
        try { _readTask?.Wait(2000); } catch { /* best effort */ }
        _readCts?.Dispose();
        _ws.Dispose();
    }
}

/// <summary>
/// Clip rectangle for CDP screenshot capture (CSS pixel coordinates).
/// </summary>
public sealed class CdpClipRect
{
    /// <summary>X offset in CSS pixels.</summary>
    public double X { get; set; }

    /// <summary>Y offset in CSS pixels.</summary>
    public double Y { get; set; }

    /// <summary>Width in CSS pixels.</summary>
    public double Width { get; set; }

    /// <summary>Height in CSS pixels.</summary>
    public double Height { get; set; }
}
