using System.CommandLine;
using System.Text.Json;
using Canary.Config;
using Canary.Input;

namespace Canary.Cli;

/// <summary>
/// The <c>canary record</c> command — records mouse/keyboard input for a test.
/// </summary>
public static class RecordCommand
{
    /// <summary>
    /// Creates the <c>record</c> subcommand with its options.
    /// </summary>
    public static Command Create()
    {
        var appOption = new Option<string>(
            "--app",
            "Target application to record input for (e.g., rhino)");

        var nameOption = new Option<string>(
            "--name",
            "Name for the recorded test");

        var command = new Command("record", "Record mouse/keyboard input for a new test")
        {
            appOption,
            nameOption
        };

        command.SetHandler(async (app, name) =>
        {
            await RunRecordAsync(app, name).ConfigureAwait(false);
        }, appOption, nameOption);

        return command;
    }

    private static async Task RunRecordAsync(string app, string name)
    {
        if (string.IsNullOrWhiteSpace(app))
        {
            Program.Log("Error: --app is required (e.g., canary record --app rhino --name my-test)");
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            Program.Log("Error: --name is required (e.g., canary record --app rhino --name my-test)");
            return;
        }

        // Load workload config
        var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");
        var configPath = Path.Combine(workloadsDir, app, "workload.json");

        if (!File.Exists(configPath))
        {
            Program.Log($"Error: Workload config not found: {configPath}");
            return;
        }

        var workload = await WorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
        Program.Log($"Workload: {workload.DisplayName}");
        Program.Log($"Window title: {workload.WindowTitle}");

        // Find target window
        var hwnd = ViewportLocator.FindWindowByTitle(workload.WindowTitle);
        if (!ViewportLocator.IsValidTarget(hwnd))
        {
            Program.Log($"Error: Target window not found: '{workload.WindowTitle}'");
            Program.Log("Make sure the target application is running.");
            return;
        }

        Program.Log($"Found window: 0x{hwnd:X}");
        Program.Log("");
        Program.Log("Press ENTER to start recording, then Ctrl+C to stop.");
        Console.ReadLine();

        var recorder = new InputRecorder(hwnd, app, workload.WindowTitle);
        recorder.StartRecording();
        Program.Log("Recording... interact with the target window. Press ENTER or Ctrl+C to stop.");

        // Wait for ENTER or Ctrl+C
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Enter)
                        break;
                    Thread.Sleep(100);
                }
            }, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C pressed
        }

        var recording = recorder.StopRecording();
        recorder.Dispose();

        Program.Log($"Stopped: {recording.Events.Count} events, {recording.Metadata.DurationMs / 1000.0:F1}s");

        // Save recording
        var recordingsDir = Path.Combine(workloadsDir, app, "recordings");
        Directory.CreateDirectory(recordingsDir);
        var outputPath = Path.Combine(recordingsDir, $"{name}.input.json");

        await recording.SaveAsync(outputPath).ConfigureAwait(false);
        Program.Log($"Saved: {outputPath}");
    }
}
