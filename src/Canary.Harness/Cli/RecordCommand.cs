using System.CommandLine;

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

        command.SetHandler((app, name) =>
        {
            Program.Log($"record: app={app}, name={name}  Press Ctrl+C to abort");
            Program.Log("record: not yet implemented");
        }, appOption, nameOption);

        return command;
    }
}
