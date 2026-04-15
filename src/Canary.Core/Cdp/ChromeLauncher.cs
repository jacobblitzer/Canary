using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Canary.Cdp;

/// <summary>
/// Options for launching Chrome with remote debugging enabled.
/// </summary>
public sealed class ChromeOptions
{
    /// <summary>Path to Chrome/Edge executable. Empty string = auto-detect.</summary>
    public string ChromePath { get; set; } = string.Empty;

    /// <summary>Remote debugging port (default 9222).</summary>
    public int CdpPort { get; set; } = 9222;

    /// <summary>Initial window width in pixels.</summary>
    public int WindowWidth { get; set; } = 1024;

    /// <summary>Initial window height in pixels.</summary>
    public int WindowHeight { get; set; } = 768;

    /// <summary>Initial window X position.</summary>
    public int WindowX { get; set; } = 0;

    /// <summary>Initial window Y position.</summary>
    public int WindowY { get; set; } = 0;

    /// <summary>Additional Chrome command-line flags.</summary>
    public List<string> ExtraFlags { get; set; } = new();

    /// <summary>Timeout for Chrome to become ready with CDP endpoint.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Result of launching Chrome — the process and its CDP WebSocket URL.
/// </summary>
public sealed class ChromeLaunchResult : IDisposable
{
    /// <summary>The Chrome browser process.</summary>
    public Process Process { get; init; } = null!;

    /// <summary>WebSocket URL for the CDP protocol (browser-level target).</summary>
    public string WebSocketUrl { get; init; } = string.Empty;

    /// <summary>Temporary user data directory (cleaned up on dispose).</summary>
    public string TempProfileDir { get; init; } = string.Empty;

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(3000);
            }
        }
        catch { /* best effort */ }
        Process.Dispose();

        // Clean up temp profile
        try
        {
            if (Directory.Exists(TempProfileDir))
                Directory.Delete(TempProfileDir, recursive: true);
        }
        catch { /* best effort, Chrome may still hold locks briefly */ }
    }
}

/// <summary>
/// Finds and launches Chrome or Edge with remote debugging enabled.
/// Follows Canary's safety rules: process can always be killed, startup has a timeout.
/// </summary>
public static class ChromeLauncher
{
    /// <summary>
    /// Find Chrome or Edge on the system. Checks Edge first (more likely on Windows),
    /// then Chrome, then Chromium.
    /// </summary>
    /// <returns>Path to the browser executable, or null if not found.</returns>
    public static string? FindChromePath()
    {
        var candidates = new[]
        {
            // Brave (preferred — user's primary browser, Chromium-based with full CDP support)
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),

            // Edge
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft", "Edge", "Application", "msedge.exe"),

            // Chrome
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "Application", "chrome.exe"),

            // Chromium
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Chromium", "Application", "chrome.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Launch Chrome/Edge with remote debugging and wait for the CDP endpoint to become available.
    /// </summary>
    /// <param name="options">Launch configuration.</param>
    /// <param name="ct">Cancellation token — kills Chrome if cancelled.</param>
    /// <returns>The launched browser process and CDP WebSocket URL.</returns>
    public static async Task<ChromeLaunchResult> LaunchAsync(ChromeOptions options, CancellationToken ct = default)
    {
        var chromePath = string.IsNullOrEmpty(options.ChromePath)
            ? FindChromePath()
            : options.ChromePath;

        if (chromePath == null || !File.Exists(chromePath))
            throw new FileNotFoundException(
                "Chrome/Edge not found. Install Chrome or Edge, or set chromePath in workload.json.");

        // Create a temp profile directory so we get a clean browser with no extensions
        var tempProfile = Path.Combine(Path.GetTempPath(), "canary-chrome-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempProfile);

        var flags = new List<string>
        {
            $"--remote-debugging-port={options.CdpPort}",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-default-apps",
            "--disable-extensions",
            "--disable-sync",
            "--disable-translate",
            "--disable-background-timer-throttling",
            "--disable-renderer-backgrounding",
            "--disable-backgrounding-occluded-windows",
            "--disable-hang-monitor",
            "--disable-ipc-flooding-protection",
            "--disable-infobars",

            // CRITICAL for screenshot determinism — lock DPI to 1x
            "--force-device-scale-factor=1",

            // Window position and size
            $"--window-size={options.WindowWidth},{options.WindowHeight}",
            $"--window-position={options.WindowX},{options.WindowY}",

            // Clean profile
            $"--user-data-dir={tempProfile}",

            // Start with blank page (we navigate later)
            "about:blank"
        };

        // Add any workload-specific flags
        flags.AddRange(options.ExtraFlags);

        var psi = new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = string.Join(" ", flags),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Chrome process");

        try
        {
            // Poll for the CDP endpoint to become available
            var wsUrl = await PollForCdpEndpointAsync(
                options.CdpPort, options.StartupTimeout, ct).ConfigureAwait(false);

            return new ChromeLaunchResult
            {
                Process = process,
                WebSocketUrl = wsUrl,
                TempProfileDir = tempProfile
            };
        }
        catch
        {
            // If we fail to connect, kill the browser
            try { process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
            try { Directory.Delete(tempProfile, true); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Poll for a page-level CDP WebSocket URL. Uses /json/list to find the first
    /// "page" type target, which supports Page.enable, Runtime.evaluate, etc.
    /// The browser-level target from /json/version only supports Browser.* methods.
    /// </summary>
    private static async Task<string> PollForCdpEndpointAsync(
        int port, TimeSpan timeout, CancellationToken ct)
    {
        using var http = new HttpClient();
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // First check if CDP is responding at all
                var json = await http.GetStringAsync($"http://localhost:{port}/json/list", ct).ConfigureAwait(false);
                var targets = JsonNode.Parse(json)?.AsArray();
                if (targets != null)
                {
                    // Find the first page-type target
                    foreach (var target in targets)
                    {
                        var type = target?["type"]?.GetValue<string>();
                        var wsUrl = target?["webSocketDebuggerUrl"]?.GetValue<string>();
                        if (type == "page" && !string.IsNullOrEmpty(wsUrl))
                            return wsUrl;
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Not ready yet — retry
            }
            catch (JsonException)
            {
                // Malformed response — retry
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Chrome CDP endpoint did not become available at localhost:{port} within {timeout.TotalSeconds}s");
    }
}
