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
    /// Marshals a function to Rhino's UI thread and waits for the result.
    /// 180s timeout is intentional — the original 30s was too short for cold
    /// GH first-load (plugin discovery happens on the UI thread and can
    /// exceed 60s when several heavy plugins like fTetWild/CGAL are
    /// installed). Throws TimeoutException on expiry rather than returning
    /// default(T), so AgentServer surfaces it as an ErrorResponse instead
    /// of producing the "Response result was null" mystery on the client.
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

        bool completed = done.Wait(TimeSpan.FromSeconds(180));
        if (!completed)
            throw new TimeoutException("Rhino UI thread did not run the agent action within 180s. " +
                                       "Likely a modal dialog or solver hang on the UI thread.");

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

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP   = 0x0101;
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
        var keywords = new[]
        {
            "loading errors",    // GH "Grasshopper loading errors" — confirmed live
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
                    if (!match) return true;

                    // Skip the main Rhino window (its title contains the doc path).
                    // The match heuristic above shouldn't pick it up, but belt-and-braces.
                    if (title.EndsWith("Rhinoceros 8", StringComparison.OrdinalIgnoreCase)) return true;
                    if (title.EndsWith("Grasshopper", StringComparison.OrdinalIgnoreCase)) return true;

                    dismissed.Add(hWnd);
                    PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                    PostMessage(hWnd, WM_KEYUP,   (IntPtr)VK_RETURN, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
            }
            catch { /* never throw from the watchdog */ }

            try { System.Threading.Thread.Sleep(250); }
            catch { return; }
        }
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
