# Creating a Canary Workload Agent

This guide explains how to add visual regression testing for a new application using the Canary harness.

## Overview

Canary uses a **two-process architecture**:

1. **Harness** (`canary.exe`) — orchestrates tests, compares screenshots, generates reports
2. **Agent** — a plugin/module running *inside* the target application that responds to commands over a named pipe

To add a new application, you implement an agent that speaks the Canary protocol.

## Step 1: Create the Agent Project

Create a new .NET class library targeting the framework your application uses:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Match your host application's framework -->
    <TargetFramework>net48</TargetFramework>   <!-- or net8.0, net6.0-windows, etc. -->
    <RootNamespace>Canary.Agent.YourApp</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Canary.Agent\Canary.Agent.csproj" />
  </ItemGroup>

  <!-- Add your application's SDK/API references here -->
</Project>
```

Add the project to `Canary.sln`:

```
dotnet sln Canary.sln add src/Canary.Agent.YourApp/Canary.Agent.YourApp.csproj --solution-folder src
```

## Step 2: Implement `ICanaryAgent`

The core interface your agent must implement:

```csharp
public interface ICanaryAgent
{
    Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters);
    Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings);
    Task<HeartbeatResult> HeartbeatAsync();
    Task AbortAsync();
}
```

### Required Actions

Your `ExecuteAsync` method must handle these standard actions:

| Action | Parameters | Description |
|--------|-----------|-------------|
| `OpenFile` | `path` | Open/load a document |
| `RunCommand` | `command` | Execute an application command |
| `SetViewport` | `width`, `height`, `projection`, `displayMode` | Configure the viewport |
| `SetView` | `name` | Set a named or standard view |

You can add application-specific actions beyond these four.

### Heartbeat

Return `Ok = true` and optionally include application state:

```csharp
public Task<HeartbeatResult> HeartbeatAsync()
{
    return Task.FromResult(new HeartbeatResult
    {
        Ok = true,
        State = new Dictionary<string, string>
        {
            ["version"] = "1.0.0",
            ["documentName"] = GetCurrentDocumentName()
        }
    });
}
```

### Screenshot Capture

Capture the application's viewport at the requested dimensions and save as PNG:

```csharp
public Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
{
    // Use your application's rendering API to capture the viewport
    var bitmap = CaptureViewport(settings.Width, settings.Height);

    Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath)!);
    bitmap.Save(settings.OutputPath, ImageFormat.Png);

    return Task.FromResult(new ScreenshotResult
    {
        FilePath = settings.OutputPath,
        Width = bitmap.Width,
        Height = bitmap.Height,
        CapturedAt = DateTime.UtcNow
    });
}
```

## Step 3: Start the Agent Server

When your application loads the agent (plugin load, startup hook, etc.), start the `AgentServer` on a **background thread**:

```csharp
public void OnApplicationStartup()
{
    var pid = Process.GetCurrentProcess().Id;
    var pipeName = $"canary-yourapp-{pid}";

    _cts = new CancellationTokenSource();
    var agent = new YourAppAgent();
    _server = new AgentServer(pipeName, agent);

    // IMPORTANT: Run on a background thread — do NOT block the UI thread
    _serverTask = Task.Run(async () =>
    {
        try
        {
            await _server.RunAsync(_cts.Token);
        }
        catch (OperationCanceledException) { }
    });
}
```

On shutdown, cancel and dispose:

```csharp
public void OnApplicationShutdown()
{
    _cts?.Cancel();
    _serverTask?.Wait(TimeSpan.FromSeconds(3));
    _server?.Dispose();
    _cts?.Dispose();
}
```

## Step 4: Create the Workload Configuration

Create `workloads/yourapp/workload.json`:

```json
{
  "name": "yourapp",
  "displayName": "Your Application",
  "appPath": "C:\\Path\\To\\YourApp.exe",
  "appArgs": "",
  "agentType": "yourapp",
  "pipeName": "canary-yourapp",
  "startupTimeoutMs": 15000,
  "windowTitle": "Your Application",
  "viewportClass": ""
}
```

| Field | Description |
|-------|-------------|
| `name` | Short identifier, used in CLI (`canary run --workload yourapp`) |
| `displayName` | Human-readable name for reports |
| `appPath` | Absolute path to the application executable |
| `appArgs` | Command-line arguments for the application |
| `agentType` | Identifies which agent type to expect |
| `pipeName` | Base pipe name — the harness appends `-{pid}` at runtime |
| `startupTimeoutMs` | How long to wait for the agent pipe to become available |
| `windowTitle` | Window title substring for viewport location |
| `viewportClass` | Optional: window class name for precise viewport targeting |

## Step 5: Create Test Definitions

Create test files in `workloads/yourapp/tests/`:

```json
{
  "name": "my-test",
  "workload": "yourapp",
  "description": "Test description",
  "setup": {
    "file": "path/to/document.ext",
    "viewport": {
      "width": 800,
      "height": 600,
      "projection": "Perspective",
      "displayMode": "Shaded"
    },
    "commands": [
      "SomeCommand arg1 arg2"
    ]
  },
  "checkpoints": [
    {
      "name": "after_setup",
      "tolerance": 0.02
    }
  ]
}
```

### Tolerance

Each checkpoint has a `tolerance` (0.0 to 1.0) — the maximum fraction of pixels that can differ before the test fails. Typical values:

- `0.01` (1%) — strict, for static content
- `0.02` (2%) — standard, accounts for antialiasing
- `0.05` (5%) — lenient, for content with minor rendering variations

## Step 6: Run Tests

```bash
# First run — captures candidates (no baselines yet, all tests show NEW)
canary run --workload yourapp

# Review the report
canary report --workload yourapp

# Approve baselines
canary approve --workload yourapp --test my-test

# Subsequent runs compare against baselines
canary run --workload yourapp
```

## Example: WPF Application Agent

For a WPF application, screenshot capture uses `RenderTargetBitmap`:

```csharp
public ScreenshotResult Capture(CaptureSettings settings)
{
    // Must run on the UI thread
    ScreenshotResult? result = null;
    Application.Current.Dispatcher.Invoke(() =>
    {
        var visual = GetMainViewport(); // your viewport UIElement
        var dpi = VisualTreeHelper.GetDpi(visual);

        var renderTarget = new RenderTargetBitmap(
            settings.Width,
            settings.Height,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        renderTarget.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath)!);
        using var stream = File.Create(settings.OutputPath);
        encoder.Save(stream);

        result = new ScreenshotResult
        {
            FilePath = settings.OutputPath,
            Width = settings.Width,
            Height = settings.Height,
            CapturedAt = DateTime.UtcNow
        };
    });

    return result!;
}
```

Key considerations for WPF:
- `RenderTargetBitmap` must be called on the UI thread — use `Dispatcher.Invoke`
- The agent server runs on a background thread, so all UI access must be marshalled
- Use `PixelFormats.Pbgra32` for correct alpha handling

## Example: Web/Electron Application Agent

For Electron or browser-based applications, use the DevTools Protocol or a headless browser API:

```csharp
public async Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
{
    // Use Chrome DevTools Protocol (CDP) to capture
    var screenshot = await _cdpClient.SendAsync("Page.captureScreenshot", new
    {
        format = "png",
        clip = new { x = 0, y = 0, width = settings.Width, height = settings.Height, scale = 1 }
    });

    var bytes = Convert.FromBase64String(screenshot["data"].GetString()!);
    Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath)!);
    await File.WriteAllBytesAsync(settings.OutputPath, bytes);

    return new ScreenshotResult
    {
        FilePath = settings.OutputPath,
        Width = settings.Width,
        Height = settings.Height,
        CapturedAt = DateTime.UtcNow
    };
}
```

## Checklist

Before marking your agent as ready:

- [ ] Agent project builds with 0 errors, 0 warnings
- [ ] Agent starts `AgentServer` on a background thread (not the UI thread)
- [ ] `HeartbeatAsync` returns `Ok = true`
- [ ] `ExecuteAsync("OpenFile", ...)` opens documents
- [ ] `ExecuteAsync("RunCommand", ...)` executes application commands
- [ ] `ExecuteAsync("SetViewport", ...)` configures viewport dimensions and projection
- [ ] `CaptureScreenshotAsync` saves a valid PNG at the requested dimensions
- [ ] `AbortAsync` cancels any in-progress operation
- [ ] `workload.json` created with correct app path and pipe name
- [ ] At least one test definition created
- [ ] `canary run --workload yourapp` completes without crash
- [ ] Agent shuts down gracefully when the application closes
