using System.Diagnostics;

namespace Canary.Orchestration;

/// <summary>
/// Tracks launched processes and kills them all on shutdown.
/// </summary>
public sealed class ProcessManager
{
    private readonly List<Process> _tracked = new();
    private readonly object _lock = new();

    /// <summary>
    /// Add a process to the tracked set.
    /// </summary>
    public void Track(Process process)
    {
        lock (_lock)
        {
            _tracked.Add(process);
        }
    }

    /// <summary>
    /// Kill ONE tracked process and its tree, leaving any others alone.
    /// </summary>
    /// <param name="process">The process to kill; ignored if not tracked.</param>
    /// <remarks>
    /// For callers that own a single app for a bounded task — <c>canary env</c> launches one
    /// application, asks it one question and closes it. <see cref="KillAll"/> would be wrong
    /// there the moment the same <see cref="ProcessManager"/> is shared with anything else, and
    /// re-implementing the teardown at the call site would lose the orphan-child sweep and the
    /// stubborn-process retry that were both added here for a reason.
    /// </remarks>
    public void KillTracked(Process process)
    {
        lock (_lock)
        {
            if (!_tracked.Remove(process)) return;
        }

        // Children first, so they die with their parent rather than becoming orphans.
        try { if (!process.HasExited) OrphanNodeCleaner.KillChildrenOf(process.Id, "pre-killTracked"); } catch { }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { /* exited between check and kill */ }
        catch (System.ComponentModel.Win32Exception) { /* access denied or already gone */ }

        // One retry for a stubborn process, mirroring KillAll.
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch { }
    }

    /// <summary>
    /// Kill all tracked processes that are still running.
    /// </summary>
    public void KillAll()
    {
        lock (_lock)
        {
            // 2026-06-23 — pre-emptively kill node.exe children of each tracked process
            // (typically Rhino) BEFORE killing the parent, so they die with their parent
            // rather than become orphans we have to mop up. Operator opt-out:
            // CANARY_DISABLE_ORPHAN_KILL=1.
            foreach (var proc in _tracked)
            {
                try { if (!proc.HasExited) OrphanNodeCleaner.KillChildrenOf(proc.Id, "pre-killAll"); } catch { }
            }

            foreach (var proc in _tracked)
            {
                try
                {
                    if (!proc.HasExited)
                        proc.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between check and kill
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Access denied or process already gone
                }
            }

            // Wait for all processes to fully exit before clearing
            foreach (var proc in _tracked)
            {
                try
                {
                    if (!proc.HasExited)
                        proc.WaitForExit(5000);
                }
                catch { }
            }

            // Retry kill on any stubborn processes
            foreach (var proc in _tracked)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
            }

            _tracked.Clear();

            // Post-kill sweep — catches anything still orphaned (e.g., a Rhino that crashed
            // earlier in this session before we could kill its children, or a node host whose
            // parent PID was lost during tree-kill).
            try { OrphanNodeCleaner.KillOrphans("post-killAll"); } catch { }
        }
    }

    /// <summary>
    /// Number of currently tracked processes.
    /// </summary>
    public int Count
    {
        get { lock (_lock) { return _tracked.Count; } }
    }
}
