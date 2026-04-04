using System.CommandLine;

namespace Canary;

/// <summary>
/// Entry point for the Canary visual regression testing harness.
/// </summary>
public static class Program
{
    private static readonly CancellationTokenSource Cts = new();

    /// <summary>
    /// Whether to suppress non-summary output.
    /// </summary>
    public static bool Quiet { get; set; }

    /// <summary>
    /// Whether to show detailed per-checkpoint output.
    /// </summary>
    public static bool Verbose { get; set; }

    /// <summary>
    /// Main entry point. Registers Ctrl+C handler first, then parses CLI arguments.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        try { Console.TreatControlCAsInput = false; } catch (IOException) { }
        Console.CancelKeyPress += OnCancelKeyPress;

        var rootCommand = BuildRootCommand();
        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// Gets the cancellation token that is signalled on Ctrl+C.
    /// </summary>
    public static CancellationToken CancellationToken => Cts.Token;

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        Cts.Cancel();
        Log("Aborted by user.");
    }

    /// <summary>
    /// Writes a timestamped log message to the console.
    /// </summary>
    public static void Log(string message)
    {
        if (Quiet) return;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Canary] {message}");
    }

    /// <summary>
    /// Writes a color-coded status line to the console.
    /// </summary>
    public static void LogStatus(string symbol, string message, ConsoleColor color)
    {
        var timestamp = $"[{DateTime.Now:HH:mm:ss}]";
        Console.Write(timestamp + " ");
        var prevColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(symbol);
        Console.ForegroundColor = prevColor;
        Console.WriteLine($" {message}  Press Ctrl+C to abort");
    }

    internal static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Canary — Cross-Application Visual Regression Testing Harness");

        rootCommand.AddCommand(Cli.RunCommand.Create());
        rootCommand.AddCommand(Cli.RecordCommand.Create());
        rootCommand.AddCommand(Cli.ApproveCommand.Create());
        rootCommand.AddCommand(Cli.ReportCommand.Create());

        return rootCommand;
    }
}
