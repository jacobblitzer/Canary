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

        done.Wait(TimeSpan.FromSeconds(30));

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
        var view = RhinoDoc.ActiveDoc?.Views.ActiveView;
        if (view == null)
        {
            return new AgentResponse
            {
                Success = false,
                Message = "No active viewport."
            };
        }

        var vp = view.ActiveViewport;

        // Set projection
        if (parameters.TryGetValue("projection", out var projection))
        {
            switch (projection.ToLowerInvariant())
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

        // Ensure Grasshopper is loaded — if no canvas, launch GH and wait
        if (Grasshopper.Instances.ActiveCanvas == null)
        {
            RhinoApp.RunScript("_-Grasshopper _W _T ENTER", echo: false);
            // Wait for GH to initialize
            var ghSw = System.Diagnostics.Stopwatch.StartNew();
            while (Grasshopper.Instances.ActiveCanvas == null && ghSw.ElapsedMilliseconds < 15000)
                System.Threading.Thread.Sleep(500);
        }

        var editor = Grasshopper.Instances.ActiveCanvas;
        if (editor == null)
        {
            return new AgentResponse
            {
                Success = false,
                Message = "Grasshopper canvas not available after 15s timeout."
            };
        }

        var io = new Grasshopper.Kernel.GH_DocumentIO();
        if (!io.Open(path))
        {
            return new AgentResponse
            {
                Success = false,
                Message = $"Failed to open Grasshopper definition: {path}"
            };
        }

        var doc = io.Document;
        if (doc == null)
        {
            return new AgentResponse
            {
                Success = false,
                Message = $"Grasshopper definition loaded but document is null: {path}"
            };
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

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (doc.SolutionState == Grasshopper.Kernel.GH_ProcessStep.PostProcess)
            {
                // Give Rhino a moment to process display updates from the solution
                RhinoApp.Wait();
                System.Threading.Thread.Sleep(500);
                RhinoDoc.ActiveDoc?.Views.Redraw();
                RhinoApp.Wait();

                return new AgentResponse
                {
                    Success = true,
                    Message = $"Solution completed in {sw.ElapsedMilliseconds}ms.",
                    Data = new Dictionary<string, string>
                    {
                        ["elapsedMs"] = sw.ElapsedMilliseconds.ToString(),
                        ["objectCount"] = doc.ObjectCount.ToString()
                    }
                };
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
    /// Read a Panel's text content. Tests use this to assert against component
    /// `Report` outputs (e.g. CPig's Slop component reports build success +
    /// per-step log; the test asserts no "FATAL" / "CRASH" substring).
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
                string text = panel.UserText ?? string.Empty;
                return new AgentResponse
                {
                    Success = true,
                    Message = $"Read panel '{nickname}' ({text.Length} chars).",
                    Data = new Dictionary<string, string>
                    {
                        ["text"] = text,
                        ["length"] = text.Length.ToString()
                    }
                };
            }
        }
        return new AgentResponse { Success = false, Message = $"Panel with nickname '{nickname}' not found." };
    }
}
