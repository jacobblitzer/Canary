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

            _tracked.Clear();
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
