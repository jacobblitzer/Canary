# Penumbra — Canary Agent Notes

## Agent Method Mapping

| ICanaryAgent Method | Penumbra Implementation |
|---|---|
| `ExecuteAsync("OpenFile", {"path"})` | Load a scene/geometry tree definition |
| `ExecuteAsync("RunCommand", {"command"})` | Execute Penumbra operations (e.g., modify geometry tree parameters) |
| `ExecuteAsync("SetViewport", {...})` | Configure the WebGL/Three.js viewport — camera projection, display mode, canvas size |
| `ExecuteAsync("SetView", {"name"})` | Apply a camera preset |
| `CaptureScreenshotAsync(settings)` | Capture the WebGL canvas — `canvas.toDataURL("image/png")` or `toBlob()` |
| `HeartbeatAsync()` | Return ok=true with renderer info (FPS, geometry node count) |
| `AbortAsync()` | Cancel current operation |

## Architecture Considerations

- **Web-based**: Penumbra runs in a browser/Electron context, not native .NET
- **Agent approach**: Two options:
  1. **Electron main process agent**: .NET agent communicates with Electron via IPC, Electron injects capture scripts into the renderer
  2. **Bridge agent**: Standalone .NET process that drives Penumbra via Chrome DevTools Protocol (CDP) for screenshot capture
- **Canvas capture**: WebGL canvas capture via `toDataURL` or `toBlob` — must call after render completes
- **Startup**: Penumbra is started via `npm run dev` or a built Electron app — the harness would launch the Electron process

## Penumbra-Specific Actions

Additional actions beyond the standard set:

| Action | Parameters | Description |
|---|---|---|
| `SetGeometry` | `json` | Load a geometry tree definition |
| `SetShaderParam` | `name`, `value` | Modify a shader uniform |
| `ToggleWireframe` | — | Toggle wireframe overlay |

## Status

Stub only — agent implementation pending. Requires deciding between Electron IPC vs CDP bridge approach.
