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
