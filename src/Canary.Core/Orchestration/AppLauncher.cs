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
        // 2026-06-23 — pre-launch orphan sweep. Kills any node.exe processes whose parent
        // is dead before we spawn the next Rhino. Catches the accumulating leak from prior
        // sessions where Rhino was force-killed before its Penumbra node host could shut
        // down (or just crashed). Operator opt-out: CANARY_DISABLE_ORPHAN_KILL=1.
        try { OrphanNodeCleaner.KillOrphans("pre-launch"); } catch { }

        var startInfo = new ProcessStartInfo
        {
            FileName = config.AppPath,
            Arguments = config.AppArgs,
            UseShellExecute = false,
        };

        // 2026-06-23 — auto-resolve PENUMBRA_* env vars from the User-scope registry.
        // Canary.UI inherits its env block at spawn time; if it was started before the
        // operator updated PENUMBRA_HOST_DEV in the registry, the inherited (stale) value
        // propagates to every Rhino Canary spawns -> Rhino's node host loads the wrong
        // repo's main.ts -> wrong artifacts -> hours of debugging. Caught live in Canary
        // session 20260623-150708-51d0. Reading the User reg + overriding here makes
        // Canary's spawned Rhino always match a fresh Explorer launch, regardless of how
        // stale Canary.UI's inherited env is. Operator opt-out:
        // CANARY_USE_INHERITED_PENUMBRA_ENV=1 (intentional A/B testing of legacy env).
        //
        // 2026-06-24 — ENUMERATE the PENUMBRA_* env-var space instead of hardcoding a list.
        // Original implementation hardcoded {HOST_DEV, USE_NATIVE_DLL, PIPELINE_CACHE_DIR}
        // and silently failed to forward any later additions (HOST_FSM_TS, ALLOW_VERSION_SKEW,
        // and future ones). The cascade-toggle bug recurring across 5+ sessions traced to
        // PENUMBRA_HOST_FSM_TS not being in the hardcoded list. Now we discover the var space
        // dynamically: every PENUMBRA_* var present in the User registry OR the current
        // process env is considered. Adding a new Penumbra env var requires ZERO Canary
        // changes. Cross-repo scope chosen deliberately (the `PENUMBRA_` prefix is the
        // namespace boundary; no other project uses it).
        bool useInherited = string.Equals(
            Environment.GetEnvironmentVariable("CANARY_USE_INHERITED_PENUMBRA_ENV"), "1",
            StringComparison.OrdinalIgnoreCase);
        if (!useInherited)
        {
            var penumbraVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var k in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User).Keys)
                    if (k is string s && s.StartsWith("PENUMBRA_", StringComparison.OrdinalIgnoreCase))
                        penumbraVars.Add(s);
                foreach (var k in Environment.GetEnvironmentVariables().Keys)
                    if (k is string s && s.StartsWith("PENUMBRA_", StringComparison.OrdinalIgnoreCase))
                        penumbraVars.Add(s);
            }
            catch { }
            Console.WriteLine($"[canary-env] auto-resolve scanning {penumbraVars.Count} PENUMBRA_* var(s) from User reg + proc env");
            foreach (var v in penumbraVars)
            {
                try
                {
                    string? userValue = Environment.GetEnvironmentVariable(v, EnvironmentVariableTarget.User);
                    string? procValue = Environment.GetEnvironmentVariable(v);
                    if (!string.IsNullOrEmpty(userValue))
                    {
                        if (!string.Equals(userValue, procValue ?? "", StringComparison.Ordinal))
                        {
                            startInfo.EnvironmentVariables[v] = userValue;
                            Console.WriteLine($"[canary-env] override {v}: '{procValue ?? "(unset)"}' -> '{userValue}' (from User reg)");
                        }
                        else
                        {
                            // Already aligned; forward explicitly anyway so child sees it even if
                            // the user-reg version was set AFTER Canary.UI started (rare belt-and-braces).
                            startInfo.EnvironmentVariables[v] = userValue;
                        }
                    }
                    else if (!string.IsNullOrEmpty(procValue))
                    {
                        // User reg unset but proc has a value -> clear it to match user state.
                        startInfo.EnvironmentVariables.Remove(v);
                        Console.WriteLine($"[canary-env] clear {v}: was '{procValue}' (User reg is unset)");
                    }
                }
                catch { }
            }
        }

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
