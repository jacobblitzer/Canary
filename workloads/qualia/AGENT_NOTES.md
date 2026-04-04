# Qualia — Canary Agent Notes

## Agent Method Mapping

| ICanaryAgent Method | Qualia Implementation |
|---|---|
| `ExecuteAsync("OpenFile", {"path"})` | Load scene file via Qualia's scene loader API |
| `ExecuteAsync("RunCommand", {"command"})` | Execute Qualia command through its command system |
| `ExecuteAsync("SetViewport", {...})` | Configure the 3D viewport — set camera projection, display mode, resize |
| `ExecuteAsync("SetView", {"name"})` | Apply a saved camera preset or standard view |
| `CaptureScreenshotAsync(settings)` | Capture via the rendering backend (WPF: `RenderTargetBitmap`, or offscreen framebuffer readback) |
| `HeartbeatAsync()` | Return ok=true with scene info (object count, frame rate) |
| `AbortAsync()` | Cancel current operation |

## Architecture Considerations

- **Framework**: Determine Qualia's target framework (net48 or net8.0) for the agent project
- **Rendering**: If Qualia uses a GPU viewport, may need to use framebuffer readback (`glReadPixels` or equivalent) instead of `RenderTargetBitmap`
- **UI Thread**: Agent server runs on a background thread; marshal viewport access to the UI/render thread
- **Plugin System**: Determine how the agent loads — application plugin, startup hook, or injected assembly

## Status

Stub only — agent implementation pending. Update `appPath` in `workload.json` once Qualia's executable location is known.
