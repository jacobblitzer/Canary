using System;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using Rhino.PlugIns;

namespace Canary.Agent.Rhino;

/// <summary>
/// Rhino plugin that hosts the Canary agent server.
/// On load, starts an <see cref="AgentServer"/> on a background thread listening for
/// harness connections over a named pipe.
/// </summary>
public sealed class CanaryRhinoPlugin : PlugIn
{
    private AgentServer? _server;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private CancellationTokenSource? _popupCts;
    private Task? _popupTask;

    /// <summary>
    /// Gets the singleton plugin instance.
    /// </summary>
    public static CanaryRhinoPlugin? Instance { get; private set; }

    /// <inheritdoc/>
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    /// <inheritdoc/>
    public CanaryRhinoPlugin()
    {
        Instance = this;
    }

    /// <inheritdoc/>
    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        var pipeName = $"canary-rhino-{pid}";

        RhinoApp.WriteLine($"[Canary] Starting agent on pipe '{pipeName}'...");

        // Bypass native-crash JIT debugger dialogs (bug 0016): when a native access
        // violation fires inside a Grasshopper component (e.g. cpig_native.dll),
        // Windows shows a "choose a debugger" JIT dialog that blocks the UI thread.
        // The harness then times out with a generic "did not respond" because the
        // RPC handler (which runs on the UI thread) can't return. SEM_NOGPFAULTERRORBOX
        // + SEM_NOOPENFILEERRORBOX make the process terminate immediately on a native
        // fault instead of showing a dialog — the harness sees the pipe disconnect
        // and reports a clear error. This also disables the GPF error box so we don't
        // hang waiting for a user to dismiss it during an automated test run.
        SuppressCrashDialogs();

        // Bump the RhinoAgent's InvokeOnUi timeout to match the harness execute timeout.
        // The original hard-coded 180s was too short for slow GH solutions (Field Point
        // Cloud octree at depth 7 takes >180s). The harness sends ExecuteTimeoutMs via
        // an env var (set by RhinoSessionAgent/TestRunner on launch) so the agent-side
        // UI marshal timeout matches the harness-side RPC timeout. Default 180s if
        // the env var isn't set (backwards compatible).
        int uiTimeout = 180000;
        var envTimeout = Environment.GetEnvironmentVariable("CANARY_EXECUTE_TIMEOUT_MS");
        if (int.TryParse(envTimeout, out var parsed) && parsed > 0)
            uiTimeout = parsed;
        RhinoAgent.UiTimeoutMs = uiTimeout;
        RhinoApp.WriteLine($"[Canary] InvokeOnUi timeout: {uiTimeout / 1000}s (from CANARY_EXECUTE_TIMEOUT_MS env).");

        // Install crash capture: intercept unhandled exceptions + native faults and
        // log the FULL crash details (exception type, message, stack trace, faulting
        // module) to a crash file BEFORE the process terminates. The harness reads
        // this file on pipe disconnect and surfaces the real crash info — instead of
        // the generic "did not respond" that hides what actually broke.
        InstallCrashCapture();

        _cts = new CancellationTokenSource();
        var agent = new RhinoAgent();
        _server = new AgentServer(pipeName, agent);

        _serverTask = Task.Run(async () =>
        {
            try
            {
                await _server.RunAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[Canary] Agent server error: {ex.Message}");
            }
        });

        RhinoApp.WriteLine($"[Canary] Agent listening on pipe '{pipeName}'.");

        // Start the popup-dismisser at plugin load time so that Rhino startup
        // popups (Plug-in Load Errors, Component Loader Errors, missing-
        // assembly warnings from third-party plugins) get auto-OK'd before
        // the harness's first action even arrives. This also covers popups
        // that appear DURING test runs (e.g. a CPig native crash that
        // triggers a Rhino crash dialog).
        _popupCts = new CancellationTokenSource();
        _popupTask = Task.Run(() => RhinoAgent.PopupDismisserPublic(_popupCts.Token));

        return LoadReturnCode.Success;
    }

    /// <inheritdoc/>
    protected override void OnShutdown()
    {
        _popupCts?.Cancel();
        _cts?.Cancel();

        if (_serverTask != null)
        {
            try { _serverTask.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException) { }
        }
        if (_popupTask != null)
        {
            try { _popupTask.Wait(TimeSpan.FromSeconds(1)); }
            catch (AggregateException) { }
        }

        _server?.Dispose();
        _cts?.Dispose();
        _popupCts?.Dispose();
        base.OnShutdown();
    }

    // ── Native crash dialog suppression (bug 0016) ──────────────────────────

    /// <summary>
    /// Windows error mode flags — SEM_NOGPFAULTERRORBOX makes the process
    /// terminate silently on a GPF/access violation instead of showing a JIT
    /// debugger dialog that blocks the UI thread during automated test runs.
    /// </summary>
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetErrorMode(uint uMode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetErrorMode();

    /// <summary>
    /// Suppresses Windows JIT debugger dialogs for native crashes. When a native
    /// access violation fires (e.g. in cpig_native.dll), the process terminates
    /// immediately instead of showing a "choose a debugger" dialog. This prevents
    /// the harness from hanging for the full RPC timeout waiting for a dialog no
    /// one will dismiss. The pipe disconnects, and the harness reports a clear error.
    /// </summary>
    private static void SuppressCrashDialogs()
    {
        try
        {
            // Preserve existing flags, add the two that suppress crash dialogs.
            uint existing = GetErrorMode();
            uint desired = existing | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX | SEM_FAILCRITICALERRORS;
            SetErrorMode(desired);
            RhinoApp.WriteLine($"[Canary] Crash dialogs suppressed (error mode: {desired:X}). " +
                               "Native faults will terminate instead of showing a JIT debugger dialog.");
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Canary] Warning: could not suppress crash dialogs: {ex.Message}");
        }
    }

    // ── Crash capture (bug 0016: surface the real crash info) ──────────────

    /// <summary>
    /// Path where crash details are written when the process is about to die.
    /// The harness reads this file on pipe disconnect and surfaces the content.
    /// </summary>
    public static string CrashLogPath { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "Canary", "canary-crash.log");

    /// <summary>
    /// Installs global exception handlers that capture the FULL crash details
    /// (exception type, message, stack trace, inner exceptions, faulting module)
    /// and write them to <see cref="CrashLogPath"/> before the process terminates.
    /// Without this, a native fault or unhandled exception kills Rhino silently and
    /// the harness can only report "pipe disconnected" — losing the actual error.
    /// </summary>
    private static void InstallCrashCapture()
    {
        // Clear any stale crash log from a previous run.
        try { if (System.IO.File.Exists(CrashLogPath)) System.IO.File.Delete(CrashLogPath); }
        catch { }

        // Managed unhandled exceptions (e.g. NullReferenceException in a component).
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                WriteCrashLog("AppDomain.UnhandledException", ex, e.IsTerminating);
            }
            catch { }
        };

        // Unobserved task exceptions (async void, fire-and-forget Task.Run).
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            try
            {
                WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception, terminating: false);
                e.SetObserved(); // don't crash for unobserved — log and continue
            }
            catch { }
        };

        // First-chance exceptions: log every AccessViolationException /
        // AccessViolation at the moment it's thrown, before it's caught or
        // unwinds. This catches native faults that would otherwise disappear.
        AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
        {
            try
            {
                var ex = e.Exception;
                if (ex is AccessViolationException || ex is System.Runtime.InteropServices.SEHException)
                {
                    WriteCrashLog("AppDomain.FirstChanceException (native fault)", ex, terminating: false);
                }
            }
            catch { }
        };

        // Native structured exception handling via VectoredExceptionRecord.
        // This catches access violations from native DLLs (cpig_native.dll etc.)
        // that don't surface as managed exceptions.
        InstallVectoredExceptionHandler();

        RhinoApp.WriteLine($"[Canary] Crash capture installed. Crash log: {CrashLogPath}");
    }

    /// <summary>
    /// Writes crash details to <see cref="CrashLogPath"/>. Includes the exception
    /// type, message, stack trace, inner exceptions, and timestamp.
    /// </summary>
    private static void WriteCrashLog(string source, Exception? ex, bool terminating)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== Canary Crash Log ===");
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"Terminating: {terminating}");
            sb.AppendLine($"PID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            sb.AppendLine();
            if (ex != null)
            {
                AppendException(sb, ex, depth: 0);
            }
            else
            {
                sb.AppendLine("Exception object was null.");
            }
            sb.AppendLine();
            sb.AppendLine("=== End Crash Log ===");

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLogPath)!);
            // Append so multiple faults in one session accumulate (first = root cause).
            System.IO.File.AppendAllText(CrashLogPath, sb.ToString());
            RhinoApp.WriteLine($"[Canary] Crash details written to {CrashLogPath}");
        }
        catch { /* never throw from a crash handler */ }
    }

    private static void AppendException(System.Text.StringBuilder sb, Exception ex, int depth)
    {
        var indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}Exception [{depth}]: {ex.GetType().FullName}");
        sb.AppendLine($"{indent}Message: {ex.Message}");
        sb.AppendLine($"{indent}Source: {ex.Source}");
        sb.AppendLine($"{indent}TargetSite: {ex.TargetSite?.Name} in {ex.TargetSite?.DeclaringType?.FullName}");
        sb.AppendLine($"{indent}StackTrace:");
        if (!string.IsNullOrEmpty(ex.StackTrace))
            foreach (var line in ex.StackTrace.Split('\n'))
                sb.AppendLine($"{indent}  {line.TrimEnd()}");
        else
            sb.AppendLine($"{indent}  (no stack trace)");

        // Native exception HRESULT (for access violations: 0x80004003 E_POINTER,
        // 0xE06D7363 C++ exception, etc.)
        try
        {
            sb.AppendLine($"{indent}HRESULT: 0x{System.Runtime.InteropServices.Marshal.GetHRForException(ex):X8}");
        }
        catch { }

        if (ex is System.Runtime.InteropServices.ExternalException ext)
        {
            sb.AppendLine($"{indent}ErrorCode: 0x{ext.ErrorCode:X8}");
        }

        if (ex.InnerException != null)
        {
            sb.AppendLine($"{indent}Inner:");
            AppendException(sb, ex.InnerException, depth + 1);
        }
    }

    // ── Vectored exception handling for native faults ──────────────────────

    private const uint EXCEPTION_ACCESS_VIOLATION = 0xC0000005;
    private const uint STATUS_ACCESS_VIOLATION = EXCEPTION_ACCESS_VIOLATION;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern System.IntPtr AddVectoredExceptionHandler(uint first, VectoredHandlerDelegate handler);

    private delegate uint VectoredHandlerDelegate(System.IntPtr exceptionInfo);

    private static VectoredHandlerDelegate? _vectoredHandler;

    /// <summary>
    /// Installs a vectored exception handler that catches native access violations
    /// and other hardware exceptions BEFORE the CLR's unhandled-exception filter.
    /// This is the only way to capture a fault in a native DLL (cpig_native.dll)
    /// that doesn't surface as a managed exception.
    /// </summary>
    private static void InstallVectoredExceptionHandler()
    {
        try
        {
            _vectoredHandler = (exceptionInfo) =>
            {
                try
                {
                    // EXCEPTION_RECORD: read the exception code from the structure.
                    // The layout of EXCEPTION_POINTERS is { EXCEPTION_RECORD*, CONTEXT* }.
                    // EXCEPTION_RECORD starts with: DWORD ExceptionCode; DWORD ExceptionFlags;
                    var recordPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(exceptionInfo);
                    if (recordPtr != System.IntPtr.Zero)
                    {
                        int code = System.Runtime.InteropServices.Marshal.ReadInt32(recordPtr);
                        // 0xC0000005 = access violation. Log all, but flag the common ones.
                        // Compare as uint because the codes are > int.MaxValue (0xC0000005).
                        uint codeUnsigned = (uint)code;
                        string codeName = codeUnsigned switch
                        {
                            EXCEPTION_ACCESS_VIOLATION => "ACCESS_VIOLATION (0xC0000005)",
                            0xC000001Du => "ILLEGAL_INSTRUCTION (0xC000001D)",
                            0xC0000025u => "NONCONTINUABLE_EXCEPTION (0xC0000025)",
                            0xC0000094u => "INT_DIVIDE_BY_ZERO (0xC0000094)",
                            0xC0000096u => "PRIVILEGED_INSTRUCTION (0xC0000096)",
                            0xC00000FDu => "STACK_OVERFLOW (0xC00000FD)",
                            _ => $"0x{codeUnsigned:X8}"
                        };

                        // For access violations, bits 0-1 of ExceptionInformation[0] tell
                        // if it was a read (0) or write (1). ExceptionInformation[1] is the
                        // faulting address.
                        string accessDetail = "";
                        if (codeUnsigned == EXCEPTION_ACCESS_VIOLATION)
                        {
                            try
                            {
                                long accessType = System.Runtime.InteropServices.Marshal.ReadInt32(recordPtr, 4 + 4); // after flags
                                long faultAddr = System.Runtime.InteropServices.Marshal.ReadInt64(recordPtr, 4 + 4 + 4 * 2);
                                accessDetail = $" — {(accessType == 0 ? "read" : "write")} at 0x{faultAddr:X}";
                            }
                            catch { }
                        }

                        WriteCrashLogNative(
                            $"VectoredExceptionHandler — NATIVE FAULT: {codeName}{accessDetail}",
                            code);
                    }
                }
                catch { }
                // EXCEPTION_CONTINUE_SEARCH = 0 — let other handlers run too
                return 0;
            };

            // 1 = first in chain (call before existing handlers)
            var handle = AddVectoredExceptionHandler(1, _vectoredHandler);
            if (handle != System.IntPtr.Zero)
                RhinoApp.WriteLine("[Canary] Vectored exception handler installed for native fault capture.");
            else
                RhinoApp.WriteLine("[Canary] Warning: AddVectoredExceptionHandler returned null — native fault capture disabled.");
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Canary] Warning: could not install vectored exception handler: {ex.Message}");
        }
    }

    private static void WriteCrashLogNative(string source, int exceptionCode)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== Canary Crash Log (native) ===");
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"ExceptionCode: 0x{exceptionCode:X8}");
            sb.AppendLine($"PID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            sb.AppendLine();
            sb.AppendLine("This is a native fault (likely from cpig_native.dll or another native dependency).");
            sb.AppendLine("The process will terminate — check the last Canary action in telemetry to");
            sb.AppendLine("identify which Grasshopper component triggered the crash.");
            sb.AppendLine();
            sb.AppendLine("=== End Crash Log ===");

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLogPath)!);
            System.IO.File.AppendAllText(CrashLogPath, sb.ToString());
            RhinoApp.WriteLine($"[Canary] Native fault captured: {source}");
        }
        catch { }
    }
}
