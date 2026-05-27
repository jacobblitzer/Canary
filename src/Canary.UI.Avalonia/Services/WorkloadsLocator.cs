namespace Canary.UI.Avalonia.Services;

internal static class WorkloadsLocator
{
    public static string? AutoDetect()
    {
        var exeDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, "workloads"),
            Path.Combine(exeDir, "..", "workloads"),
            Path.Combine(exeDir, "..", "..", "workloads"),
            Path.Combine(exeDir, "..", "..", "..", "workloads"),
            Path.Combine(exeDir, "..", "..", "..", "..", "..", "..", "workloads"),
            Path.Combine(Directory.GetCurrentDirectory(), "workloads"),
            @"C:\Repos\Canary\workloads",
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full)) return full;
        }
        return null;
    }
}
