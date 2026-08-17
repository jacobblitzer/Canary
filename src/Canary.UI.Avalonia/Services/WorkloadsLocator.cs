using Canary.Config;

namespace Canary.UI.Avalonia.Services;

/// <summary>
/// Locates the workloads content root for the UI.
/// </summary>
/// <remarks>
/// Delegates to <see cref="CanaryPaths"/> so the UI and the CLI cannot disagree about
/// where content lives. This previously kept its own candidate list whose last entry was
/// a hard-coded <c>C:\Repos\Canary\workloads</c> — on a machine carrying any Canary
/// checkout that literal would silently bind the UI to the repo instead of to installed
/// content, with no indication that it had happened. Deployment campaign Phase 1.
/// </remarks>
internal static class WorkloadsLocator
{
    /// <summary>
    /// Finds the workloads root, or <c>null</c> when no such directory exists.
    /// </summary>
    /// <returns>An absolute path to an existing workloads directory, or <c>null</c>.</returns>
    public static string? AutoDetect()
    {
        var r = CanaryPaths.ResolveWorkloadsRootDetailed();
        return r.Exists ? r.Path : null;
    }
}
