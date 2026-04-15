using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using Rhino.Display;

namespace Canary.Agent.Rhino;

/// <summary>
/// Canary agent implementation for Rhino. Handles commands from the harness
/// including opening files, running commands, configuring viewports, and capturing screenshots.
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
}
