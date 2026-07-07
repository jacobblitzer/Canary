using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using Rhino.Display;

namespace Canary.Agent.Rhino;

/// <summary>
/// Canary agent implementation for Rhino. Handles commands from the harness
/// including opening files, running commands, configuring viewports, capturing screenshots,
/// and Grasshopper-specific actions (loading definitions, waiting for solutions).
/// All Rhino SDK calls are marshalled to the UI thread via <see cref="RhinoApp.InvokeOnUiThread"/>.
/// </summary>
public sealed class RhinoAgent : ICanaryAgent
{
    private readonly RhinoScreenCapture _screenCapture = new();

    /// <inheritdoc/>
    public Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters)
    {
        return Task.FromResult(InvokeOnUi(() =>
        {
            try
            {
                return action switch
                {
                    "OpenFile" => HandleOpenFile(parameters),
                    "RunCommand" => HandleRunCommand(parameters),
                    "SetViewport" => HandleSetViewport(parameters),
                    "SetView" => HandleSetView(parameters),
                    "OpenGrasshopperDefinition" => HandleOpenGrasshopperDefinition(parameters),
                    "WaitForGrasshopperSolution" => HandleWaitForGrasshopperSolution(parameters),
                    "GrasshopperSetSlider" => HandleGrasshopperSetSlider(parameters),
                    "GrasshopperSetToggle" => HandleGrasshopperSetToggle(parameters),
                    "GrasshopperSetPanelText" => HandleGrasshopperSetPanelText(parameters),
                    "GrasshopperGetPanelText" => HandleGrasshopperGetPanelText(parameters),
                    "WaitForPenumbraFrame" => HandleWaitForPenumbraFrame(parameters),
                    "GetPenumbraFrameState" => HandleGetPenumbraFrameState(parameters),
                    "DumpPenumbraSceneState" => HandleDumpPenumbraSceneState(parameters),
                    "SaveDocument" => HandleSaveDocument(parameters),
                    _ => new AgentResponse
                    {
                        Success = false,
                        Message = $"Unknown action: {action}"
                    }
                };
            }
            catch (Exception ex)
            {
                return new AgentResponse
                {
                    Success = false,
                    Message = $"Error executing '{action}': {ex.Message}"
                };
            }
        }));
    }

    /// <inheritdoc/>
    public Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
    {
        return Task.FromResult(InvokeOnUi(() => _screenCapture.Capture(settings)));
    }

    /// <inheritdoc/>
    public Task<HeartbeatResult> HeartbeatAsync()
    {
        return Task.FromResult(InvokeOnUi(() =>
        {
            var doc = RhinoDoc.ActiveDoc;
            var state = new Dictionary<string, string>
            {
                ["rhinoVersion"] = RhinoApp.Version.ToString()
            };

            if (doc != null)
            {
                state["documentName"] = doc.Name ?? "(untitled)";
                state["objectCount"] = doc.Objects.Count.ToString();
            }

            return new HeartbeatResult
            {
                Ok = true,
                State = state
            };
        }));
    }

    /// <inheritdoc/>
    public Task AbortAsync()
    {
        InvokeOnUi(() => RhinoApp.SendKeystrokes("_Cancel ", true));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Configurable UI-thread marshal timeout in ms. Default 180s (original hard-coded
    /// value). The harness sets this from WorkloadConfig.ExecuteTimeoutMs so slow GH
    /// solutions (e.g. Field Point Cloud octree) don't hit the 180s cap before the
    /// harness-side ExecuteTimeoutMs fires. Set via <see cref="UiTimeoutMs"/>.
    /// </summary>
    public static int UiTimeoutMs { get; set; } = 180000;

    /// <summary>
    /// Marshals a function to Rhino's UI thread and waits for the result.
    /// Timeout is <see cref="UiTimeoutMs"/> (default 180s, configurable from workload config).
    /// The original 30s was too short for cold GH first-load (plugin discovery happens on
    /// the UI thread and can exceed 60s when several heavy plugins like fTetWild/CGAL are
    /// installed). Throws TimeoutException on expiry rather than returning default(T), so
    /// AgentServer surfaces it as an ErrorResponse instead of producing the
    /// "Response result was null" mystery on the client.
    /// </summary>
    private static T InvokeOnUi<T>(Func<T> func)
    {
        T result = default!;
        Exception? caught = null;
        using var done = new ManualResetEventSlim(false);

        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
            finally
            {
                done.Set();
            }
        }));

        bool completed = done.Wait(TimeSpan.FromMilliseconds(UiTimeoutMs));
        if (!completed)
            throw new TimeoutException($"Rhino UI thread did not run the agent action within {UiTimeoutMs / 1000}s. " +
                                       "Likely a modal dialog or solver hang on the UI thread. " +
                                       "If this is a slow GH solution (not a hang), bump executeTimeoutMs in the workload config.");

        if (caught != null)
            throw caught;

        return result;
    }

    /// <summary>
    /// Marshals an action to Rhino's UI thread and waits for completion.
    /// </summary>
    private static void InvokeOnUi(Action action)
    {
        InvokeOnUi<object?>(() => { action(); return null; });
    }

    private static AgentResponse HandleOpenFile(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
        {
            return new AgentResponse
            {
                Success = false,
                Message = "Missing required parameter 'path'."
            };
        }

        if (!System.IO.File.Exists(path))
        {
            return new AgentResponse
            {
                Success = false,
                Message = $"File not found: {path}"
            };
        }

        var doc = RhinoDoc.Open(path, out var alreadyOpen);
        return new AgentResponse
        {
            Success = doc != null,
            Message = doc != null
                ? (alreadyOpen ? $"Document already open: {path}" : $"Opened: {path}")
                : $"Failed to open: {path}",
            Data = doc != null
                ? new Dictionary<string, string> { ["documentId"] = doc.RuntimeSerialNumber.ToString() }
                : new Dictionary<string, string>()
        };
    }

    private static AgentResponse HandleRunCommand(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
        {
            return new AgentResponse
            {
                Success = false,
                Message = "Missing required parameter 'command'."
            };
        }

        var result = RhinoApp.RunScript(command, echo: false);
        return new AgentResponse
        {
            Success = result,
            Message = result ? $"Executed: {command}" : $"Command failed: {command}"
        };
    }

    /// <summary>Save the active document to a .3dm path via RhinoDoc.WriteFile — deterministic,
    /// no command-line prompt to park on (the reason the `-_SaveAs` MACRO was unreliable). Creates
    /// the target directory if missing. Param: path (absolute .3dm path).</summary>
    private static AgentResponse HandleSaveDocument(Dictionary<string, string> parameters)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc == null) return new AgentResponse { Success = false, Message = "No active document." };
        if (!parameters.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
            return new AgentResponse { Success = false, Message = "Missing required parameter 'path'." };
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            var opts = new global::Rhino.FileIO.FileWriteOptions();
            bool ok = doc.WriteFile(path, opts);
            return new AgentResponse
            {
                Success = ok && System.IO.File.Exists(path),
                Message = ok ? $"Saved: {path}" : $"WriteFile returned false for {path}"
            };
        }
        catch (Exception ex)
        {
            return new AgentResponse { Success = false, Message = $"SaveDocument threw: {ex.Message}" };
        }
    }

    private static AgentResponse HandleSetViewport(Dictionary<string, string> parameters)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc == null)
        {
            return new AgentResponse { Success = false, Message = "No active document." };
        }

        // Diagnostic: log all viewport names + persist to a file so the
        // harness can inspect what the agent saw, even though RhinoApp.WriteLine
        // surfaces only inside Rhino's command line.
        try
        {
            var allViews = string.Join(", ", System.Linq.Enumerable.Select(
                doc.Views, v => $"{v.ActiveViewport.Name}({(v == doc.Views.ActiveView ? "active" : "_")})"));
            RhinoApp.WriteLine($"[Canary] SetViewport — views: {allViews}");
            try
            {
                System.IO.File.AppendAllText(@"C:\Repos\CPig\logs\agent_viewport_diag.log",
                    $"{DateTime.Now:HH:mm:ss} SetViewport — views: {allViews}\n");
            }
            catch { }
        }
        catch { }

        // Activate a viewport whose name matches the requested projection name
        // (Rhino's default view names "Top"/"Front"/"Right"/"Perspective"
        // double as projection labels). Then maximize so the captured bitmap
        // covers only that one view rather than a quadrant of the 4-view
        // layout. RhinoScreenCapture grabs doc.Views.ActiveView — without
        // this activation step the default Top view stays active and
        // captures an empty CPlane.
        if (parameters.TryGetValue("projection", out var projection))
        {
            var pl = projection.ToLowerInvariant();
            string? viewName = pl switch
            {
                "perspective" => "Perspective",
                "top"         => "Top",
                "front"       => "Front",
                "right"       => "Right",
                _             => null,
            };
            if (viewName != null)
            {
                bool activated = false;
                foreach (var v in doc.Views)
                {
                    if (string.Equals(v.ActiveViewport.Name, viewName, StringComparison.OrdinalIgnoreCase))
                    {
                        doc.Views.ActiveView = v;
                        // Force activation by setting the viewport active +
                        // running RhinoScript SetActiveView. The C# property
                        // setter alone isn't enough when no view was active
                        // before — Rhino expects a focus event.
                        try
                        {
                            RhinoApp.RunScript($"_-SetActiveView _{viewName} _Enter", echo: false);
                        }
                        catch { }
                        activated = doc.Views.ActiveView == v;
                        RhinoApp.WriteLine($"[Canary] SetViewport — '{viewName}' activated={activated}");
                        try
                        {
                            System.IO.File.AppendAllText(@"C:\Repos\CPig\logs\agent_viewport_diag.log",
                                $"  -> activated '{viewName}': result={(doc.Views.ActiveView?.ActiveViewport.Name ?? "null")}\n");
                        }
                        catch { }
                        break;
                    }
                }
                if (!activated)
                    RhinoApp.WriteLine($"[Canary] SetViewport — no viewport named '{viewName}' found");
            }
        }

        // Maximize the (now-active) viewport unconditionally — screenshots are
        // a single view's bitmap, never a multi-pane layout.
        try { RhinoApp.RunScript("_-MaxViewport _Enter", echo: false); }
        catch (Exception mex) { RhinoApp.WriteLine($"[Canary] MaxViewport: {mex.Message}"); }

        // Recenter the camera. Tests typically build geometry via Grasshopper
        // (post-setup), so doc geometry is empty here — but a default
        // Perspective camera looks at the world origin from a fixed distance,
        // which may be too close or too far for downstream-built content.
        // Setting the camera to a known orbital position provides a
        // predictable starting point. ZoomExtents in WaitForGrasshopperSolution's
        // quiesce branch then re-frames once geometry exists.
        //
        // Phase 14.7: ONLY reset for Perspective. Front/Top/Right have their
        // axis-aligned camera directions baked in by Rhino's view system; the
        // off-axis (40, -40, 30) target would rotate them into a perspective-
        // like pose and the 4-view checkpoint pattern would all look the same.
        bool isPerspective = !parameters.TryGetValue("projection", out var projForCam)
                             || string.Equals(projForCam, "Perspective", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(projForCam, "Parallel", StringComparison.OrdinalIgnoreCase);
        if (isPerspective)
        {
            try
            {
                var v = doc.Views.ActiveView;
                if (v != null)
                {
                    var avp = v.ActiveViewport;
                    avp.SetCameraLocation(new global::Rhino.Geometry.Point3d(40, -40, 30), updateTargetLocation: false);
                    avp.SetCameraTarget(global::Rhino.Geometry.Point3d.Origin, updateCameraLocation: false);
                    v.Redraw();
                }
            }
            catch (Exception cex) { RhinoApp.WriteLine($"[Canary] camera reset: {cex.Message}"); }
        }

        // Phase 14.7: After the viewport switch, refit the camera to the
        // existing geometry on the now-active view. Without this, switching
        // from Perspective to Front in a per-checkpoint override leaves Front
        // looking at whatever camera position Rhino's view system remembers
        // — which may be miles away from where the GH-built geometry sits.
        //
        // BUG-CANARY-0009 diagnostic: counts + diagonal + camera position
        // are logged to agent_viewport_diag.log so the shared-vs-solo
        // framing-regression hypotheses can be tested without re-instrumenting.
        string zoomDiag = "(unset)";
        try
        {
            var v = doc.Views.ActiveView;
            if (v != null)
            {
                var bbox = global::Rhino.Geometry.BoundingBox.Empty;
                int docCount = 0, ghValid = 0, ghHidden = 0, ghInvalid = 0;
                double ghMaxDiag = 0;
                var rhDoc = RhinoDoc.ActiveDoc;
                if (rhDoc != null)
                {
                    foreach (var ro in rhDoc.Objects)
                    {
                        try
                        {
                            var b = ro.Geometry.GetBoundingBox(true);
                            if (b.IsValid) { bbox.Union(b); docCount++; }
                        }
                        catch { }
                    }
                    var ghDoc = Grasshopper.Instances.ActiveCanvas?.Document;
                    if (ghDoc != null)
                    {
                        foreach (var obj in ghDoc.Objects)
                        {
                            if (obj is Grasshopper.Kernel.IGH_PreviewObject prev)
                            {
                                if (prev.Hidden) { ghHidden++; continue; }
                                try
                                {
                                    var pb = prev.ClippingBox;
                                    if (pb.IsValid && pb.Diagonal.Length > 1e-9)
                                    {
                                        bbox.Union(pb);
                                        ghValid++;
                                        if (pb.Diagonal.Length > ghMaxDiag) ghMaxDiag = pb.Diagonal.Length;
                                    }
                                    else { ghInvalid++; }
                                }
                                catch { ghInvalid++; }
                            }
                        }
                    }
                }
                if (bbox.IsValid && bbox.Diagonal.Length > 1e-6) v.ActiveViewport.ZoomBoundingBox(bbox);
                else v.ActiveViewport.ZoomExtents();
                v.Redraw();

                var cam = v.ActiveViewport.CameraLocation;
                var tgt = v.ActiveViewport.CameraTarget;
                zoomDiag = $"doc={docCount} gh={ghValid} ghH={ghHidden} ghI={ghInvalid} ghMaxDiag={ghMaxDiag:F1} bbox.diag={(bbox.IsValid ? bbox.Diagonal.Length : 0):F1} bbox.min=({bbox.Min.X:F1},{bbox.Min.Y:F1},{bbox.Min.Z:F1}) bbox.max=({bbox.Max.X:F1},{bbox.Max.Y:F1},{bbox.Max.Z:F1}) cam=({cam.X:F1},{cam.Y:F1},{cam.Z:F1}) tgt=({tgt.X:F1},{tgt.Y:F1},{tgt.Z:F1})";
            }
        }
        catch (Exception zex) { zoomDiag = $"ERR: {zex.Message}"; RhinoApp.WriteLine($"[Canary] SetViewport zoom: {zex.Message}"); }

        try
        {
            System.IO.File.AppendAllText(@"C:\Repos\CPig\logs\agent_viewport_diag.log",
                $"  -> zoom: {zoomDiag}\n");
        }
        catch { }

        try
        {
            System.IO.File.AppendAllText(@"C:\Repos\CPig\logs\agent_viewport_diag.log",
                $"  -> after MaxViewport+camera: active={(doc.Views.ActiveView?.ActiveViewport.Name ?? "null")}\n");
        }
        catch { }

        var view = doc.Views.ActiveView;
        if (view == null)
        {
            return new AgentResponse { Success = false, Message = "No active viewport." };
        }

        var vp = view.ActiveViewport;

        // Set projection (mutates the projection of the now-active viewport, in
        // case the named-view activation above couldn't find a match).
        if (parameters.TryGetValue("projection", out var projection2))
        {
            switch (projection2.ToLowerInvariant())
            {
                case "perspective":
                    vp.ChangeToPerspectiveProjection(true, 50.0);
                    break;
                case "parallel":
                case "orthographic":
                    vp.ChangeToParallelProjection(true);
                    break;
                case "top":
                    vp.SetProjection(DefinedViewportProjection.Top, "Top", false);
                    break;
                case "front":
                    vp.SetProjection(DefinedViewportProjection.Front, "Front", false);
                    break;
                case "right":
                    vp.SetProjection(DefinedViewportProjection.Right, "Right", false);
                    break;
            }
        }

        // Set display mode
        if (parameters.TryGetValue("displayMode", out var displayMode))
        {
            var mode = DisplayModeDescription.FindByName(displayMode);
            if (mode != null)
                vp.DisplayMode = mode;
        }

        // Set viewport size
        if (parameters.TryGetValue("width", out var widthStr) &&
            parameters.TryGetValue("height", out var heightStr) &&
            int.TryParse(widthStr, out var width) &&
            int.TryParse(heightStr, out var height))
        {
            view.Size = new System.Drawing.Size(width, height);
        }

        view.Redraw();

        return new AgentResponse
        {
            Success = true,
            Message = "Viewport configured."
        };
    }

    private static AgentResponse HandleSetView(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("name", out var viewName) || string.IsNullOrWhiteSpace(viewName))
        {
            return new AgentResponse
            {
                Success = false,
                Message = "Missing required parameter 'name'."
            };
        }

        var doc = RhinoDoc.ActiveDoc;
        if (doc == null)
        {
            return new AgentResponse
            {
                Success = false,
                Message = "No active document."
            };
        }

        // Try named views first
        var namedViewIndex = doc.NamedViews.FindByName(viewName);
        if (namedViewIndex >= 0)
        {
            var view = doc.Views.ActiveView;
            if (view != null)
            {
                doc.NamedViews.Restore(namedViewIndex, view.ActiveViewport);
                view.Redraw();
                return new AgentResponse
                {
                    Success = true,
                    Message = $"Restored named view: {viewName}"
                };
            }
        }

        // Try standard projection names
        var result = RhinoApp.RunScript($"_-SetView _World _{viewName} _Enter", echo: false);
        return new AgentResponse
        {
            Success = result,
            Message = result ? $"Set view: {viewName}" : $"View not found: {viewName}"
        };
    }

    // ── Grasshopper Actions ─────────────────────────────────────────────

    private static AgentResponse HandleOpenGrasshopperDefinition(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
        {
            return new AgentResponse { Success = false, Message = "Missing required parameter 'path'." };
        }
        if (!System.IO.File.Exists(path))
        {
            return new AgentResponse { Success = false, Message = $"File not found: {path}" };
        }

        // Background popup-dismisser. While we're loading GH and running its
        // first solution, GH or Rhino may throw modal dialogs (plugin
        // compatibility warnings, missing-component prompts, "old file
        // format" alerts). They block the UI thread, which means the open
        // call hangs and the harness times out. The dismisser polls
        // top-level windows on a worker thread and posts Enter to anything
        // that looks like a #32770 warning dialog.
        using var dismissCts = new System.Threading.CancellationTokenSource();
        var dismissTask = System.Threading.Tasks.Task.Run(() => PopupDismisser(dismissCts.Token));

        try
        {
            if (Grasshopper.Instances.ActiveCanvas == null)
            {
                RhinoApp.RunScript("_-Grasshopper _W _T ENTER", echo: false);
                var ghSw = System.Diagnostics.Stopwatch.StartNew();
                while (Grasshopper.Instances.ActiveCanvas == null && ghSw.ElapsedMilliseconds < 30000)
                    System.Threading.Thread.Sleep(500);
            }

            var editor = Grasshopper.Instances.ActiveCanvas;
            if (editor == null)
            {
                return new AgentResponse { Success = false, Message = "Grasshopper canvas not available after 30s timeout." };
            }

            var io = new Grasshopper.Kernel.GH_DocumentIO();
            if (!io.Open(path))
            {
                return new AgentResponse { Success = false, Message = $"Failed to open Grasshopper definition: {path}" };
            }

            var doc = io.Document;
            if (doc == null)
            {
                return new AgentResponse { Success = false, Message = $"Grasshopper definition loaded but document is null: {path}" };
            }

            doc.Enabled = true;
            Grasshopper.Instances.DocumentServer.AddDocument(doc);
            editor.Document = doc;
            doc.NewSolution(true);

            return new AgentResponse
            {
                Success = true,
                Message = $"Opened Grasshopper definition: {path}",
                Data = new Dictionary<string, string>
                {
                    ["objectCount"] = doc.ObjectCount.ToString(),
                    ["filePath"] = path
                }
            };
        }
        finally
        {
            dismissCts.Cancel();
            try { dismissTask.Wait(1000); } catch { /* never throw from cleanup */ }
        }
    }

    // ── Win32 popup dismisser (auto-OKs modal warning dialogs) ──

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP   = 0x0101;
    private const uint BM_CLICK   = 0x00F5;
    private const int  VK_RETURN  = 0x0D;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// Public entry for the popup dismisser. Called from CanaryRhinoPlugin's
    /// OnLoad so it runs for the lifetime of the Rhino process — catching
    /// startup popups (Rhino plugin load failures, GH Component Loader
    /// Errors) that appear BEFORE any agent action arrives. Internal scope
    /// because Canary.Agent.Rhino is the only caller.
    /// </summary>
    internal static void PopupDismisserPublic(System.Threading.CancellationToken token)
        => PopupDismisser(token);

    /// <summary>
    /// Background thread that scans every 250ms for top-level dialog windows
    /// owned by this process and posts Enter to dismiss them. Matches on
    /// title keyword alone — class name is too fragile because GH's custom
    /// dialogs (e.g. "Component Loader Errors", which surfaced as a
    /// non-#32770 window from a Ghowl/GL shader plugin failing to load
    /// System.ObjectModel) don't use the standard #32770 class. Title-only
    /// matching is safer if the keyword list is specific. Stops on token.
    /// </summary>
    private static void PopupDismisser(System.Threading.CancellationToken token)
    {
        uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        var dismissed = new HashSet<IntPtr>();

        // Title keywords that strongly imply a dismissable warning/error.
        // Order: most specific first. Avoid generic words like "Rhino" or
        // "Grasshopper" that would also match the main app windows.
        // CRITICAL: ANY dialog that needs a non-default button clicked must
        // be handled by the body-text fallback below, NOT here — title-keyword
        // matches always send VK_RETURN which clicks the dialog's DEFAULT button.
        var keywords = new[]
        {
            "loading errors",    // GH "Grasshopper loading errors" — confirmed live
            "loading sequence",  // GH "Grasshopper loading sequence" (single-OK follow-on after a Component ID conflict — confirmed live 2026-06-02)
            "Component Loader",  // GH plugin load errors
            "Loader Errors",     // GH plugin load errors (variant)
            "Old file format",   // GH version mismatch
            "Compatibility",     // GH compatibility warnings
            "Plug-in Error",     // Rhino plugin load failures
            "Plugin Error",      // (variant)
            "Plug-in Warning",
            "Missing",           // missing components dialog
            "Could not load",    // plugin load assertions
            "FileNotFoundException", // .NET assembly missing — bubbles up to dialogs
            "Rhino Error",       // generic Rhino error dialog
            "Rhinoceros Error",  // (variant)
            // Removed 2026-06-02: "Component conflict", "Component ID", "ID conflict".
            // The "Component ID conflict" modal HAS those substrings in its title, but
            // its default button is Replace All — destructive (overwrites installed
            // plugin components). The body-text-+Skip All path below handles it
            // correctly; matching by title here was clicking the wrong button.
        };

        while (!token.IsCancellationRequested)
        {
            try
            {
                EnumWindows((hWnd, _) =>
                {
                    if (token.IsCancellationRequested) return false;
                    if (!IsWindowVisible(hWnd)) return true;

                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid != ourPid) return true;

                    if (dismissed.Contains(hWnd)) return true;

                    var titleSb = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, titleSb, titleSb.Capacity);
                    var title = titleSb.ToString();
                    if (string.IsNullOrEmpty(title)) return true;

                    bool match = false;
                    foreach (var kw in keywords)
                    {
                        if (title.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            match = true;
                            break;
                        }
                    }

                    // Skip the main Rhino window (its title contains the doc path).
                    // The match heuristic above shouldn't pick it up, but belt-and-braces.
                    if (title.EndsWith("Rhinoceros 8", StringComparison.OrdinalIgnoreCase)) return true;
                    if (title.EndsWith("Grasshopper", StringComparison.OrdinalIgnoreCase)) return true;

                    if (match)
                    {
                        dismissed.Add(hWnd);
                        PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                        PostMessage(hWnd, WM_KEYUP,   (IntPtr)VK_RETURN, IntPtr.Zero);
                        return true;
                    }

                    // Body-text fallback: GH dialogs whose title isn't matchable
                    // OR whose default button is wrong. We scan child windows once
                    // and try every known pattern, preferring an explicit BM_CLICK
                    // on the named button over a generic Enter.

                    IntPtr skipAllBtn = IntPtr.Zero; // "Component ID conflict" → Skip All
                    IntPtr noBtn      = IntPtr.Zero; // "Grasshopper IO" yes/no → No
                    bool hasConflictText = false;    // 2026-06-01 GH GUID-conflict modal
                    bool hasGhIoText     = false;    // 2026-06-02 GH "IO generated N messages" modal
                    EnumChildWindows(hWnd, (child, _) =>
                    {
                        var ct = new System.Text.StringBuilder(256);
                        GetWindowText(child, ct, ct.Capacity);
                        var childText = ct.ToString();
                        if (string.IsNullOrEmpty(childText)) return true;

                        // Buttons we know about. Match button text exactly so a
                        // body label that *mentions* the word doesn't accidentally
                        // get clicked.
                        if (childText.Equals("Skip All", StringComparison.OrdinalIgnoreCase)
                         || childText.Equals("&Skip All", StringComparison.OrdinalIgnoreCase))
                        {
                            skipAllBtn = child;
                        }
                        else if (childText.Equals("No", StringComparison.OrdinalIgnoreCase)
                              || childText.Equals("&No", StringComparison.OrdinalIgnoreCase))
                        {
                            noBtn = child;
                        }

                        // Body-text markers (substring match — labels can be
                        // multi-line and include surrounding prose).
                        if (childText.IndexOf("must be discarded", StringComparison.OrdinalIgnoreCase) >= 0
                         || childText.IndexOf("share the same ID", StringComparison.OrdinalIgnoreCase) >= 0
                         || childText.IndexOf("Component ID conflict", StringComparison.OrdinalIgnoreCase) >= 0
                         || childText.IndexOf("Conflicting Component", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hasConflictText = true;
                        }
                        else if (childText.IndexOf("IO generated", StringComparison.OrdinalIgnoreCase) >= 0
                              || childText.IndexOf("would you like to see them", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hasGhIoText = true;
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (hasConflictText)
                    {
                        // GH "Component ID conflict" — click Skip All (don't let
                        // CPig.gha overwrite an installed plugin's components).
                        dismissed.Add(hWnd);
                        if (skipAllBtn != IntPtr.Zero)
                            PostMessage(skipAllBtn, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        else
                        {
                            // Fallback: Enter clicks the default button — which is
                            // Replace All in this dialog. Suboptimal but unblocks
                            // the UI thread. (Should never happen — the button is
                            // always reachable via EnumChildWindows on a #32770.)
                            PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                            PostMessage(hWnd, WM_KEYUP,   (IntPtr)VK_RETURN, IntPtr.Zero);
                        }
                    }
                    else if (hasGhIoText)
                    {
                        // GH "Grasshopper IO — IO generated N messages, would you
                        // like to see them?" — click NO so the messages window
                        // doesn't open and trap focus. Confirmed live 2026-06-02
                        // as the third dialog in the cold-load chain after a
                        // Component ID conflict + loading sequence pair.
                        dismissed.Add(hWnd);
                        if (noBtn != IntPtr.Zero)
                            PostMessage(noBtn, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        else
                        {
                            // No "No" button found — send ESC, which typically
                            // closes a Yes/No dialog the same way as No.
                            const int VK_ESCAPE = 0x1B;
                            PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                            PostMessage(hWnd, WM_KEYUP,   (IntPtr)VK_ESCAPE, IntPtr.Zero);
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { /* never throw from the watchdog */ }

            try { System.Threading.Thread.Sleep(250); }
            catch { return; }
        }
    }

    /// <summary>
    /// Wait until the Penumbra viewport overlay has presented a frame showing the
    /// REAL field (not the bounding-sphere companion stand-in the atlas path shows
    /// while baking), so a subsequent screenshot captures the lattice, not a blob.
    ///
    /// Peer-agnostic by design: reflects into Penumbra.Bridge.PenumbraBridge
    /// (loaded by the Penumbra plug-in) rather than referencing it, so Canary keeps
    /// no compile-time dependency on a specific peer plug-in. Returns failure (not a
    /// throw) when the Bridge isn't loaded.
    ///
    /// Params: timeoutMs (default 120000), minRevision (default 1),
    /// requireReal (default true — wait for RealRevision; false waits for any presented frame).
    /// </summary>
    private static AgentResponse HandleWaitForPenumbraFrame(Dictionary<string, string> parameters)
    {
        int timeoutMs = 120000;
        if (parameters.TryGetValue("timeoutMs", out var ts) && int.TryParse(ts, out var pt)) timeoutMs = pt;
        // minRevision is DEPRECATED + no longer gates. The wait is now RELATIVE: it snapshots the revision at
        // action start and returns once the revision INCREASES past it. This makes the same action correct in a
        // SHARED Rhino (chained tests) — test 2 waits for ITS OWN new frame instead of passing immediately on
        // test 1's stale, process-global, never-reset revision. (Still parsed so old test JSONs don't error.)
        parameters.TryGetValue("minRevision", out _);
        bool requireReal = true;
        if (parameters.TryGetValue("requireReal", out var rs) && bool.TryParse(rs, out var pr)) requireReal = pr;
        // quietMs (optional, default 0 = legacy first-new-frame behavior): converged-wait mode.
        // After at least one new frame past the baseline, keep pumping RhinoApp idle (which is what
        // drives the conduit's refine ramp) until the revision stops advancing for quietMs. The
        // conduit is event-driven — at steady state it stops scheduling redraws — so "revision went
        // quiet after progressing" IS convergence. Added 2026-07-02 (Phase 6 finding F8: captures
        // fired on the FIRST post-push frame, which is the 30%-resolution motion frame, and the
        // checkpoint stabilize sleep pumps nothing, so the viewport parked coarse in BOTH FSMs).
        int quietMs = 0;
        if (parameters.TryGetValue("quietMs", out var qs) && int.TryParse(qs, out var pq)) quietMs = pq;
        // requireSteady (optional, default false): gate on TRUE convergence — the conduit publishes
        // the last drawn frame's FSM state in FrameState.Status ("scene steady q=100% steps=192").
        // Robust where quietMs is not: dense-path refinement can stall on async work longer than any
        // quiet window, but Status only says "steady" once the FSM actually converged. This action
        // pumps RhinoApp idle while polling, which is what drives the refine ramp forward.
        bool requireSteady = false;
        if (parameters.TryGetValue("requireSteady", out var ss) && bool.TryParse(ss, out var ps)) requireSteady = ps;

        var getFrameState = ResolveGetFrameState(out var resolveError);
        if (getFrameState == null)
            return new AgentResponse { Success = false, Message = resolveError ?? "Penumbra.Bridge.GetFrameState unavailable." };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string lastDiag = "(no frame state yet)";
        long baseline = -1;   // revision snapshotted on the first read; we return once it INCREASES (relative gate)
        long lastSeen = -1;   // quietMs mode: last revision observed (quiet timer restarts on any advance)
        var quietSw = new System.Diagnostics.Stopwatch();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var frame = ReadFrameState(getFrameState);
            if (frame != null)
            {
                long realRev = frame.RealRevision;
                long presRev = frame.PresentedRevision;
                bool disabled = frame.DisabledByError;
                var evalMode = frame.EvalMode;
                var status = frame.Status;
                lastDiag = $"presented={presRev} real={realRev} evalMode={evalMode} status={status} disabled={disabled} bakes={(frame.BakesOutstanding?.ToString() ?? "n/a")}";

                if (disabled)
                    return new AgentResponse { Success = false, Message = "Penumbra viewer disabled by error: " + status };

                if (requireSteady)
                {
                    // Bake-complete gate (R1.2, Penumbra bug 0058): "steady" alone means the FSM's
                    // quality ramp converged — cascade refinement can still be landing bricks (or
                    // silently failing), which is exactly the state the 0058 matrix photographed.
                    // Require BakesOutstanding==0 too, when the plugin reports it; null = plugin
                    // predates the additive FrameState field → fall back to steady-only (old
                    // behavior, no false failure against an older Penumbra).
                    bool bakesDone = frame.BakesOutstanding == null || frame.BakesOutstanding == 0;
                    if (status != null && status.Contains(" steady") && bakesDone)
                        return new AgentResponse { Success = true, Message = "Penumbra STEADY (converged last-drawn frame, bakes drained): " + lastDiag };
                    // Diagnostic probe (2026-07-02 hang investigation): ground-truth the loop from
                    // inside — thread identity, frame state per iteration, and whether the posted
                    // redraws ever execute. %TEMP%\canary-steady-probe.log.
                    try
                    {
                        System.IO.File.AppendAllText(
                            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "canary-steady-probe.log"),
                            DateTime.Now.ToString("HH:mm:ss.fff") + " loop tid=" + Thread.CurrentThread.ManagedThreadId + " " + lastDiag + Environment.NewLine);
                    }
                    catch { }
                    // Drive the refine ramp: post a redraw to the UI thread, then pump. If this
                    // handler IS the UI thread, Wait() processes the queued post; if it isn't, the
                    // UI thread processes it on its own loop. Never call Views.Redraw() directly
                    // cross-thread (blocks).
                    try
                    {
                        RhinoApp.InvokeOnUiThread((System.Action)(() =>
                        {
                            try
                            {
                                // ACTIVE VIEW ONLY (2026-07-02 probe finding): Views.Redraw() repaints
                                // every viewport, and the conduit's per-frame motion detector compares
                                // consecutive draws' cameras — alternating viewport matrices read as
                                // perpetual camera motion, so the FSM NEVER leaves 30%/40 coarse
                                // (2,114-frame probe, quality never ramped). One viewport = one camera
                                // = the ramp can complete. Same blind spot explains the operator's
                                // "sphere doesn't appear until I pan" — file conduit-side.
                                global::Rhino.RhinoDoc.ActiveDoc?.Views.ActiveView?.Redraw();
                                System.IO.File.AppendAllText(
                                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "canary-steady-probe.log"),
                                    DateTime.Now.ToString("HH:mm:ss.fff") + " redraw-exec tid=" + Thread.CurrentThread.ManagedThreadId + Environment.NewLine);
                            }
                            catch { }
                        }));
                    }
                    catch { }
                    RhinoApp.Wait();
                    Thread.Sleep(150);
                    continue;
                }

                long target = requireReal ? realRev : presRev;
                if (baseline < 0)
                {
                    baseline = target; lastSeen = target;   // first read: snapshot
                    if (quietMs > 0) quietSw.Restart();     // quiet mode: timer runs from the start —
                    // "no revision advance for quietMs WHILE THIS ACTION PUMPS RhinoApp idle" means the
                    // FSM has nothing left to refine (already-steady scenes produce no frames at all;
                    // gating the timer on a first new frame hangs on them — 2026-07-02 sphere crash).
                }
                else if (quietMs <= 0)
                {
                    if (target > baseline)                  // legacy: first new frame is enough
                        return new AgentResponse { Success = true, Message = $"Penumbra frame ready (baseline={baseline}): " + lastDiag };
                    // Same F10-family root cause as the steady wait (2026-07-02): the frame this
                    // wait is waiting FOR requires a redraw, and nothing pumps one while the agent
                    // holds this loop. Post an active-view redraw (proven mechanism from the
                    // requireSteady path) so the just-pushed scene actually paints.
                    try
                    {
                        RhinoApp.InvokeOnUiThread((System.Action)(() =>
                        {
                            try { global::Rhino.RhinoDoc.ActiveDoc?.Views.ActiveView?.Redraw(); } catch { }
                        }));
                    }
                    catch { }
                }
                else
                {
                    if (target != lastSeen)                 // advancing (refine ramp / bakes) — restart quiet timer
                    {
                        lastSeen = target;
                        quietSw.Restart();
                    }
                    else if (quietSw.ElapsedMilliseconds >= quietMs)
                        return new AgentResponse
                        {
                            Success = true,
                            Message = target > baseline
                                ? $"Penumbra CONVERGED (baseline={baseline}, settled at rev={lastSeen} after {quietMs}ms quiet): " + lastDiag
                                : $"Penumbra already steady (rev={lastSeen} unchanged for {quietMs}ms under idle pumping): " + lastDiag
                        };
                }
            }
            RhinoApp.Wait();
            Thread.Sleep(100);
        }
        return new AgentResponse
        {
            Success = false,
            Message = $"Timed out ({timeoutMs}ms) waiting for a NEW Penumbra frame past baseline={baseline} (requireReal={requireReal}, quietMs={quietMs}). Last: {lastDiag}"
        };
    }

    /// <summary>Snapshot of Penumbra.Bridge.PenumbraBridge.GetFrameState() read via reflection.</summary>
    private sealed class BridgeFrameState
    {
        public long RealRevision;
        public long PresentedRevision;
        public bool DisabledByError;
        public string? EvalMode;
        public string? Status;
        /// <summary>Cascade bakes still expected to land (Penumbra FrameState.BakesOutstanding,
        /// additive 2026-07-03 / bug 0058). Null = the loaded plugin predates the field —
        /// consumers must treat null as "unknown", NOT as 0 or as a failure.</summary>
        public long? BakesOutstanding;
    }

    /// <summary>
    /// Flight-recorder R1.6 (2026-07-03) — trigger Penumbra's on-demand scene snapshot
    /// (`Bridge.DumpSceneState()` → `gl.scene.snapshot` NDJSON). Session captures call this
    /// (best-effort) right before the pixel grab so the render-side field inventory + per-view
    /// cameras land in the tailed telemetry adjacent to the Screenshot record. Always
    /// Success=true — absence of the Bridge/sink is evidence, not an abort (mirrors
    /// GetPenumbraFrameState's philosophy).
    /// </summary>
    private static AgentResponse HandleDumpPenumbraSceneState(Dictionary<string, string> parameters)
    {
        var data = new Dictionary<string, string>();
        var bridgeType = ResolveBridgeType();
        if (bridgeType == null)
        {
            data["bridge"] = "unavailable";
            return new AgentResponse { Success = true, Message = "Penumbra.Bridge not loaded.", Data = data };
        }
        try
        {
            var mi = bridgeType.GetMethod("DumpSceneState",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (mi == null)
            {
                data["bridge"] = "no-dump-verb";
                return new AgentResponse { Success = true, Message = "DumpSceneState not present (older Penumbra).", Data = data };
            }
            var ok = mi.Invoke(null, null);
            data["bridge"] = "ok";
            data["emitted"] = (ok as bool? ?? false).ToString();
            return new AgentResponse { Success = true, Message = "scene snapshot requested", Data = data };
        }
        catch (Exception ex)
        {
            data["bridge"] = "invoke-failed";
            return new AgentResponse { Success = true, Message = $"DumpSceneState failed: {ex.Message}", Data = data };
        }
    }

    /// <summary>Shared assembly scan for the Penumbra.Bridge type (one scan, several verbs).</summary>
    private static System.Type? ResolveBridgeType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType("Penumbra.Bridge.PenumbraBridge");
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// THE single reflection seam into Penumbra.Bridge.GetFrameState (pinned as one contract —
    /// R1 audit-c). Every consumer (WaitForPenumbraFrame, GetPenumbraFrameState) resolves through
    /// here; do not duplicate the assembly scan or the field reads elsewhere. (The assembly scan
    /// itself is factored into ResolveBridgeType, shared with DumpPenumbraSceneState — R1.6.)
    /// </summary>
    private static System.Reflection.MethodInfo? ResolveGetFrameState(out string? error)
    {
        System.Type? bridgeType = ResolveBridgeType();
        if (bridgeType == null)
        {
            error = "Penumbra.Bridge not loaded — is the Penumbra plug-in installed and was PenumbraShow run first?";
            return null;
        }
        var mi = bridgeType.GetMethod("GetFrameState",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (mi == null)
        {
            error = "Penumbra.Bridge.GetFrameState not found (version mismatch?).";
            return null;
        }
        error = null;
        return mi;
    }

    private static BridgeFrameState? ReadFrameState(System.Reflection.MethodInfo getFrameState)
    {
        var state = getFrameState.Invoke(null, null);
        if (state == null) return null;
        var st = state.GetType();
        // BakesOutstanding is ADDITIVE (2026-07-03): older Penumbra plugins don't have the field,
        // so its absence is a valid state (null), not an error. All other fields are the frozen
        // pre-existing contract and stay hard reads.
        var bakesField = st.GetField("BakesOutstanding");
        return new BridgeFrameState
        {
            RealRevision = System.Convert.ToInt64(st.GetField("RealRevision").GetValue(state)),
            PresentedRevision = System.Convert.ToInt64(st.GetField("PresentedRevision").GetValue(state)),
            DisabledByError = System.Convert.ToBoolean(st.GetField("DisabledByError").GetValue(state)),
            EvalMode = st.GetField("EvalMode").GetValue(state) as string,
            Status = st.GetField("Status").GetValue(state) as string,
            BakesOutstanding = bakesField != null ? System.Convert.ToInt64(bakesField.GetValue(state)) : (long?)null,
        };
    }

    /// <summary>
    /// One-shot, non-blocking frame-state + viewport read (flight-recorder Phase A): the session
    /// capture path calls this immediately before AND after each pixel grab so a capture can be
    /// tied to the exact FSM state it photographed. Always Success=true — absence of the Bridge
    /// is reported in Data.bridge, because a missing Penumbra plug-in is itself evidence, not an
    /// error that should abort a capture.
    /// </summary>
    private static AgentResponse HandleGetPenumbraFrameState(Dictionary<string, string> parameters)
    {
        var data = new Dictionary<string, string>();

        // Rhino view identity — independent of Penumbra, always available (F10-class forensics:
        // "which viewport was active when this was captured, and what views existed?").
        try
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc != null)
            {
                data["activeView"] = doc.Views.ActiveView?.ActiveViewport?.Name ?? "(none)";
                var names = new List<string>();
                foreach (var v in doc.Views)
                {
                    var n = v?.ActiveViewport?.Name;
                    // Explicit null test: net48 reference assemblies lack IsNullOrEmpty's
                    // NotNullWhen annotation, so the compiler can't narrow through it.
                    if (n != null && n.Length > 0) names.Add(n);
                }
                data["views"] = string.Join(";", names);
            }
        }
        catch { }

        var getFrameState = ResolveGetFrameState(out var error);
        if (getFrameState == null)
        {
            data["bridge"] = "unavailable";
            return new AgentResponse { Success = true, Message = error ?? "Penumbra.Bridge not loaded.", Data = data };
        }

        BridgeFrameState? frame;
        try { frame = ReadFrameState(getFrameState); }
        catch (Exception ex)
        {
            data["bridge"] = "read-failed";
            return new AgentResponse { Success = true, Message = $"GetFrameState read failed: {ex.Message}", Data = data };
        }
        if (frame == null)
        {
            data["bridge"] = "no-state";
            return new AgentResponse { Success = true, Message = "GetFrameState returned null (no conduit active yet).", Data = data };
        }

        data["bridge"] = "ok";
        data["realRevision"] = frame.RealRevision.ToString();
        data["presentedRevision"] = frame.PresentedRevision.ToString();
        data["disabledByError"] = frame.DisabledByError.ToString();
        data["evalMode"] = frame.EvalMode ?? "";
        data["status"] = frame.Status ?? "";
        // "n/a" = plugin predates the additive BakesOutstanding field (bug 0058 / R1.2).
        data["bakesOutstanding"] = frame.BakesOutstanding?.ToString() ?? "n/a";
        return new AgentResponse { Success = true, Message = "frame state read", Data = data };
    }

    private static AgentResponse HandleWaitForGrasshopperSolution(Dictionary<string, string> parameters)
    {
        int timeoutMs = 30000;
        if (parameters.TryGetValue("timeoutMs", out var timeoutStr) && int.TryParse(timeoutStr, out var parsed))
            timeoutMs = parsed;

        var doc = Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc == null)
        {
            return new AgentResponse
            {
                Success = false,
                Message = "No active Grasshopper document."
            };
        }

        // Breakpoint detection (bug 0016): if a debugger is attached to this process,
        // a Debugger.Break() in a component's SolveInstance will block the UI thread.
        // RhinoApp.Wait() below pumps the message loop and will block waiting for the
        // UI thread — so the harness-side RPC times out with a generic "did not respond"
        // message and the operator has no idea a breakpoint fired. Warn up-front.
        if (System.Diagnostics.Debugger.IsAttached)
        {
            RhinoApp.WriteLine("[Canary] WARNING: a debugger is attached to Rhino. If a Debugger.Break() " +
                               "fires inside a Grasshopper component, the solution will hang and Canary " +
                               "will time out. Remove Debugger.Break() calls from component source before " +
                               "running Canary tests.");
        }

        // Wait for the canvas to QUIESCE: not just the first PostProcess, but
        // a stable run of consecutive PostProcess polls. Slop's BUILD callback
        // runs via doc.ScheduleSolution(10ms, ...), which means after the
        // toggle pulse fires solution N, Slop schedules solution N+1; the
        // first PostProcess we see is for solution N (canvas-level toggle
        // expiration), not the build itself. By requiring 600ms of
        // contiguous quiescence we let Slop's deferred work finish before
        // we report the canvas as settled.
        const int quiesceMs = 600;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var quiesceSw = new System.Diagnostics.Stopwatch();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (doc.SolutionState == Grasshopper.Kernel.GH_ProcessStep.PostProcess)
            {
                if (!quiesceSw.IsRunning)
                    quiesceSw.Start();

                if (quiesceSw.ElapsedMilliseconds >= quiesceMs)
                {
                    // Canvas has been at PostProcess continuously for
                    // quiesceMs — Slop's deferred build (if any) should
                    // have completed.
                    RhinoApp.Wait();
                    System.Threading.Thread.Sleep(200);

                    // Zoom-extents on every viewport so the screenshot frames
                    // the resulting geometry tightly. Grasshopper preview meshes
                    // (Custom Preview, default component previews) live in the
                    // GH preview pipeline — not in the Rhino doc — so a plain
                    // RhinoDoc-only ZoomExtents misses them. Compute the union
                    // of GH preview bboxes plus doc geometry and zoom to that.
                    string zoomDiagOut = "(unset)";
                    try
                    {
                        var rhDoc = RhinoDoc.ActiveDoc;
                        if (rhDoc != null)
                        {
                            // Force a redraw so GH preview pipeline computes its
                            // ClippingBoxes — some components compute lazily on
                            // first draw, so a second pass after a short wait
                            // catches anything the first pass scheduled instead
                            // of computing inline.
                            rhDoc.Views.Redraw();
                            RhinoApp.Wait();
                            System.Threading.Thread.Sleep(150);
                            rhDoc.Views.Redraw();
                            RhinoApp.Wait();
                            System.Threading.Thread.Sleep(150);

                            var bbox = global::Rhino.Geometry.BoundingBox.Empty;
                            int countDoc = 0, countGh = 0;

                            foreach (var ro in rhDoc.Objects)
                            {
                                try
                                {
                                    var b = ro.Geometry.GetBoundingBox(true);
                                    if (b.IsValid) { bbox.Union(b); countDoc++; }
                                }
                                catch { }
                            }

                            try
                            {
                                var ghDoc = Grasshopper.Instances.ActiveCanvas?.Document;
                                if (ghDoc != null)
                                {
                                    foreach (var obj in ghDoc.Objects)
                                    {
                                        if (obj is Grasshopper.Kernel.IGH_PreviewObject prev && !prev.Hidden)
                                        {
                                            try
                                            {
                                                var pb = prev.ClippingBox;
                                                if (pb.IsValid && pb.Diagonal.Length > 1e-9)
                                                {
                                                    bbox.Union(pb);
                                                    countGh++;
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                            catch { }

                            zoomDiagOut = $"doc={countDoc} gh={countGh} diag={(bbox.IsValid ? bbox.Diagonal.Length : 0):F1}";

                            foreach (var view in rhDoc.Views)
                            {
                                if (bbox.IsValid && bbox.Diagonal.Length > 1e-6)
                                    view.ActiveViewport.ZoomBoundingBox(bbox);
                                else
                                    view.ActiveViewport.ZoomExtents();
                            }
                        }
                    }
                    catch (Exception zex)
                    {
                        zoomDiagOut = $"ERROR: {zex.Message}";
                    }

                    RhinoDoc.ActiveDoc?.Views.Redraw();
                    RhinoApp.Wait();

                    return new AgentResponse
                    {
                        Success = true,
                        Message = $"Solution completed in {sw.ElapsedMilliseconds}ms (quiesced for {quiesceSw.ElapsedMilliseconds}ms). zoom: {zoomDiagOut}",
                        Data = new Dictionary<string, string>
                        {
                            ["elapsedMs"]   = sw.ElapsedMilliseconds.ToString(),
                            ["quiesceMs"]   = quiesceSw.ElapsedMilliseconds.ToString(),
                            ["objectCount"] = doc.ObjectCount.ToString(),
                            ["zoomDiag"]    = zoomDiagOut
                        }
                    };
                }
            }
            else
            {
                // Solver re-entered a non-PostProcess state — Slop's deferred
                // solution has fired. Reset the quiesce timer and keep waiting.
                quiesceSw.Reset();
            }

            System.Threading.Thread.Sleep(100);
        }

        return new AgentResponse
        {
            Success = false,
            Message = $"Solution did not complete within {timeoutMs}ms. State: {doc.SolutionState}"
        };
    }

    private static AgentResponse HandleGrasshopperSetSlider(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("nickname", out var nickname) || string.IsNullOrWhiteSpace(nickname))
        {
            return new AgentResponse
            {
                Success = false,
                Message = "Missing required parameter 'nickname'."
            };
        }

        if (!parameters.TryGetValue("value", out var valueStr) || !double.TryParse(valueStr, out var value))
        {
            return new AgentResponse
            {
                Success = false,
                Message = "Missing or invalid parameter 'value'."
            };
        }

        var doc = Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc == null)
        {
            return new AgentResponse
            {
                Success = false,
                Message = "No active Grasshopper document."
            };
        }

        // Find slider by nickname
        foreach (var obj in doc.Objects)
        {
            if (obj is Grasshopper.Kernel.Special.GH_NumberSlider slider &&
                string.Equals(slider.NickName, nickname, StringComparison.OrdinalIgnoreCase))
            {
                slider.SetSliderValue((decimal)value);
                slider.ExpireSolution(true);
                // BUG-CANARY-pending — ExpireSolution(true) alone wasn't actually
                // re-solving downstream when called from the agent's RPC thread
                // (bound-body scrub captures were pixel-identical across all
                // slider values, going back to 2026-06-02 08:10 archived runs).
                // Explicitly call doc.NewSolution(false) on the UI thread so the
                // recompute fires before WaitForGrasshopperSolution checks quiesce.
                try
                {
                    if (global::Rhino.RhinoApp.InvokeRequired)
                        global::Rhino.RhinoApp.InvokeOnUiThread(new Action(() => doc.NewSolution(false)));
                    else
                        doc.NewSolution(false);
                }
                catch (Exception nex) { global::Rhino.RhinoApp.WriteLine($"[Canary] SetSlider NewSolution: {nex.Message}"); }

                return new AgentResponse
                {
                    Success = true,
                    Message = $"Set slider '{nickname}' to {value}.",
                    Data = new Dictionary<string, string>
                    {
                        ["actualValue"] = slider.CurrentValue.ToString()
                    }
                };
            }
        }

        return new AgentResponse
        {
            Success = false,
            Message = $"Slider with nickname '{nickname}' not found."
        };
    }

    /// <summary>
    /// Set a Boolean Toggle's value. Used by the CPig regression workload to
    /// pulse Slop's Build trigger via Canary.
    /// </summary>
    private static AgentResponse HandleGrasshopperSetToggle(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("nickname", out var nickname) || string.IsNullOrWhiteSpace(nickname))
        {
            return new AgentResponse { Success = false, Message = "Missing required parameter 'nickname'." };
        }
        if (!parameters.TryGetValue("value", out var valueStr) || !bool.TryParse(valueStr, out var value))
        {
            return new AgentResponse { Success = false, Message = "Missing or invalid parameter 'value' (expected true/false)." };
        }

        var doc = Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc == null)
        {
            return new AgentResponse { Success = false, Message = "No active Grasshopper document." };
        }

        foreach (var obj in doc.Objects)
        {
            if (obj is Grasshopper.Kernel.Special.GH_BooleanToggle toggle &&
                string.Equals(toggle.NickName, nickname, StringComparison.OrdinalIgnoreCase))
            {
                toggle.Value = value;
                toggle.ExpireSolution(true);
                return new AgentResponse
                {
                    Success = true,
                    Message = $"Set toggle '{nickname}' to {value}.",
                    Data = new Dictionary<string, string> { ["actualValue"] = toggle.Value.ToString() }
                };
            }
        }
        return new AgentResponse { Success = false, Message = $"Toggle with nickname '{nickname}' not found." };
    }

    /// <summary>
    /// Set a Panel's text content. Useful for driving Slop's `Files` input port
    /// (a wired panel containing a JSON path) and any other text-driven CPig input.
    /// </summary>
    private static AgentResponse HandleGrasshopperSetPanelText(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("nickname", out var nickname) || string.IsNullOrWhiteSpace(nickname))
        {
            return new AgentResponse { Success = false, Message = "Missing required parameter 'nickname'." };
        }
        if (!parameters.TryGetValue("text", out var text))
        {
            text = string.Empty;
        }

        var doc = Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc == null)
        {
            return new AgentResponse { Success = false, Message = "No active Grasshopper document." };
        }

        foreach (var obj in doc.Objects)
        {
            if (obj is Grasshopper.Kernel.Special.GH_Panel panel &&
                string.Equals(panel.NickName, nickname, StringComparison.OrdinalIgnoreCase))
            {
                panel.UserText = text;
                panel.ExpireSolution(true);
                return new AgentResponse
                {
                    Success = true,
                    Message = $"Set panel '{nickname}' text ({text.Length} chars).",
                    Data = new Dictionary<string, string> { ["length"] = text.Length.ToString() }
                };
            }
        }
        return new AgentResponse { Success = false, Message = $"Panel with nickname '{nickname}' not found." };
    }

    /// <summary>
    /// Read a Panel's content. For panels with incoming wires, GH stores the
    /// displayed text in VolatileData, NOT UserText (UserText is only what
    /// the user manually typed). We prefer VolatileData when present so the
    /// caller sees the *live* value flowing through the panel — exactly what
    /// CPig regression tests need to assert on Slop's Success/Log outputs.
    /// </summary>
    private static AgentResponse HandleGrasshopperGetPanelText(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("nickname", out var nickname) || string.IsNullOrWhiteSpace(nickname))
        {
            return new AgentResponse { Success = false, Message = "Missing required parameter 'nickname'." };
        }

        var doc = Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc == null)
        {
            return new AgentResponse { Success = false, Message = "No active Grasshopper document." };
        }

        foreach (var obj in doc.Objects)
        {
            if (obj is Grasshopper.Kernel.Special.GH_Panel panel &&
                string.Equals(panel.NickName, nickname, StringComparison.OrdinalIgnoreCase))
            {
                // Try VolatileData first (live data from incoming wires).
                string text = string.Empty;
                string source = "UserText";
                try
                {
                    if (panel.VolatileDataCount > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        bool first = true;
                        foreach (var goo in panel.VolatileData.AllData(true))
                        {
                            if (!first) sb.Append('\n');
                            first = false;
                            sb.Append(goo?.ToString() ?? string.Empty);
                        }
                        text = sb.ToString();
                        source = "VolatileData";
                    }
                }
                catch { /* fall through to UserText */ }

                if (string.IsNullOrEmpty(text))
                {
                    text = panel.UserText ?? string.Empty;
                    source = "UserText";
                }

                return new AgentResponse
                {
                    Success = true,
                    Message = $"Read panel '{nickname}' ({text.Length} chars from {source}).",
                    Data = new Dictionary<string, string>
                    {
                        ["text"] = text,
                        ["length"] = text.Length.ToString(),
                        ["source"] = source
                    }
                };
            }
        }
        return new AgentResponse { Success = false, Message = $"Panel with nickname '{nickname}' not found." };
    }
}
