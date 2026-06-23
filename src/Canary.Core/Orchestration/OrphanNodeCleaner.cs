using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Canary.Orchestration;

/// <summary>
/// Kills orphaned node.exe processes (parent process has exited) at Canary session/test
/// boundaries. Driven by the 2026-06-23 finding that 139 orphaned node hosts had
/// accumulated across the operator's dev day going back 4 days — the Penumbra Rhino plug-in
/// spawns node hosts via npx tsx, and when Rhino crashes / is force-killed before clean
/// shutdown, the node host child becomes an orphan that keeps running indefinitely.
///
/// Two entry points:
/// (1) KillOrphans() — enumerates ALL node.exe processes whose parent is dead, kills them.
///     Called PRE-LAUNCH (AppLauncher.Launch) to clean accumulated state from prior runs,
///     and POST-KILL (ProcessManager.KillAll + RhinoSessionAgent.DisposeAsync) to catch
///     any node hosts newly-orphaned by the just-killed Rhino.
/// (2) KillChildrenOf(pid) — kills node.exe processes whose parent PID matches the given
///     pid. Used right BEFORE killing a Rhino process so the children die cleanly.
///
/// Override: setting CANARY_DISABLE_ORPHAN_KILL=1 in the environment skips all cleanup
/// (intentional opt-out for the rare operator who's running a legit orphaned node tool).
///
/// Parent-PID resolution uses NtQueryInformationProcess PROCESS_BASIC_INFORMATION — no
/// System.Management / WMI dependency. The PID stored in InheritedFromUniqueProcessId is
/// the spawn-time parent; PID reuse is rare enough that the heuristic is safe in practice.
/// </summary>
public static class OrphanNodeCleaner
{
    private static bool Disabled =>
        string.Equals(Environment.GetEnvironmentVariable("CANARY_DISABLE_ORPHAN_KILL"), "1",
                      StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("CANARY_DISABLE_ORPHAN_KILL"), "true",
                      StringComparison.OrdinalIgnoreCase);

    /// <summary>Kill every node.exe process whose parent PID is dead (orphaned). Returns
    /// the count killed. Safe to call from any thread.</summary>
    public static int KillOrphans(string contextLabel = "")
    {
        if (Disabled) return 0;
        int killed = 0;
        Process[] allNode;
        try { allNode = Process.GetProcessesByName("node"); }
        catch { return 0; }

        foreach (var np in allNode)
        {
            try
            {
                int parentPid = GetParentProcessId(np);
                bool orphan = false;
                if (parentPid <= 0)
                {
                    orphan = true;
                }
                else
                {
                    try { _ = Process.GetProcessById(parentPid); /* parent alive — keep */ }
                    catch { orphan = true; /* parent dead */ }
                }
                if (!orphan) continue;

                int pid = np.Id;
                try { np.Kill(entireProcessTree: true); killed++; }
                catch { }
            }
            catch { }
            finally { try { np.Dispose(); } catch { } }
        }

        if (killed > 0)
        {
            try
            {
                string ctx = string.IsNullOrEmpty(contextLabel) ? "" : $" ({contextLabel})";
                Console.WriteLine($"[canary-cleanup] killed {killed} orphan node.exe process(es){ctx}");
            }
            catch { }
        }
        return killed;
    }

    /// <summary>Kill every node.exe process whose parent PID == the given pid. Called BEFORE
    /// killing a Rhino so the node-host child dies cleanly via parent rather than
    /// becoming an orphan we have to mop up. Returns the count killed.</summary>
    public static int KillChildrenOf(int parentPid, string contextLabel = "")
    {
        if (Disabled || parentPid <= 0) return 0;
        int killed = 0;
        Process[] allNode;
        try { allNode = Process.GetProcessesByName("node"); }
        catch { return 0; }

        foreach (var np in allNode)
        {
            try
            {
                if (GetParentProcessId(np) != parentPid) continue;
                try { np.Kill(entireProcessTree: true); killed++; }
                catch { }
            }
            catch { }
            finally { try { np.Dispose(); } catch { } }
        }

        if (killed > 0)
        {
            try
            {
                string ctx = string.IsNullOrEmpty(contextLabel) ? "" : $" ({contextLabel})";
                Console.WriteLine($"[canary-cleanup] killed {killed} node.exe child(ren) of PID {parentPid}{ctx}");
            }
            catch { }
        }
        return killed;
    }

    // ─── NtQueryInformationProcess for parent PID ──────────────────────────────────────

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    private static int GetParentProcessId(Process proc)
    {
        try
        {
            var info = new PROCESS_BASIC_INFORMATION();
            int outLen;
            int status = NtQueryInformationProcess(
                proc.Handle, 0, ref info,
                Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)),
                out outLen);
            if (status == 0) return info.InheritedFromUniqueProcessId.ToInt32();
        }
        catch { }
        return 0;
    }
}
