using System.Diagnostics;
using System.IO.Pipes;
using Canary.Config;

namespace Canary.Orchestration;

/// <summary>
/// Launches the target application and waits for the agent to become available.
/// </summary>
public static class AppLauncher
{
    /// <summary>
    /// Launch the target application as specified in the workload config.
    /// </summary>
    public static Process Launch(WorkloadConfig config)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = config.AppPath,
            Arguments = config.AppArgs,
            UseShellExecute = false,
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {config.AppPath}");

        return process;
    }

    /// <summary>
    /// Polls for the named pipe to become available, indicating the agent is ready.
    /// Retries every 500ms until the timeout is reached.
    /// </summary>
    /// <param name="pipeName">Full pipe name including PID suffix.</param>
    /// <param name="timeout">Maximum time to wait for the agent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the pipe became available, false if timed out.</returns>
    public static async Task<bool> WaitForAgentAsync(
        string pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(500, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                // Pipe not yet available, retry
            }
            catch (IOException)
            {
                // Pipe exists but may not be ready
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }
}
