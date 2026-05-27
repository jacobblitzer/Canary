namespace Canary;

// Locates Canary.UI.exe for the CLI-launches-UI handoff (design §C3 + impl §3).
//
// Phase 6 cutover (Avalonia migration, 2026-05-27): the project that
// produces Canary.UI.exe is now src/Canary.UI.Avalonia/ (its csproj
// sets <AssemblyName>Canary.UI</AssemblyName>); the legacy
// src/Canary.UI/ WinForms project was deleted. The sibling-solution
// path below now walks to Canary.UI.Avalonia/bin/... rather than the
// retired Canary.UI/bin/... directory.
//
// Search order (first hit wins):
//   1. Same directory as the running canary.exe (the deployed layout).
//   2. Sibling solution layout:
//      ../../../../Canary.UI.Avalonia/bin/Release/net8.0-windows/Canary.UI.exe
//      then the Debug equivalent.
//   3. `Canary UI.lnk` shortcut alongside canary.exe (resolved via WScript.Shell COM).
//
// Returns false if none found; the caller falls back to the headless path.
public static class UiLocator
{
    private const string UiExeName = "Canary.UI.exe";
    private const string ShortcutName = "Canary UI.lnk";

    public static bool TryFindUiExe(out string path)
    {
        // 1. Same directory as the running canary.exe.
        var entryDir = AppContext.BaseDirectory;
        var sameDir = Path.Combine(entryDir, UiExeName);
        if (File.Exists(sameDir)) { path = sameDir; return true; }

        // 2. Sibling solution layout (dev-tree): walk up to find a sibling
        //    Canary.UI project's bin output. Try Release first, then Debug.
        if (TryFindInSiblingSolutionLayout(entryDir, out path)) return true;

        // 3. Resolve `Canary UI.lnk` next to canary.exe.
        var shortcutPath = Path.Combine(entryDir, ShortcutName);
        if (File.Exists(shortcutPath) && TryResolveShortcut(shortcutPath, out path)) return true;

        path = string.Empty;
        return false;
    }

    private static bool TryFindInSiblingSolutionLayout(string startDir, out string path)
    {
        // Typical dev tree: src/Canary.Harness/bin/Debug/net8.0-windows/canary.exe
        // Sibling:          src/Canary.UI.Avalonia/bin/{Release|Debug}/net8.0-windows/Canary.UI.exe
        // The Avalonia csproj has <AssemblyName>Canary.UI</AssemblyName>, so
        // the filename UiExeName=Canary.UI.exe still applies — only the
        // containing project directory changes from Canary.UI to
        // Canary.UI.Avalonia. Walk up to find the sibling.
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            var candidateRelease = Path.Combine(dir.FullName, "..", "..", "..", "..", "Canary.UI.Avalonia", "bin", "Release", "net8.0-windows", UiExeName);
            var candidateDebug   = Path.Combine(dir.FullName, "..", "..", "..", "..", "Canary.UI.Avalonia", "bin", "Debug",   "net8.0-windows", UiExeName);
            foreach (var c in new[] { candidateRelease, candidateDebug })
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full)) { path = full; return true; }
            }
            dir = dir.Parent;
        }
        path = string.Empty;
        return false;
    }

    private static bool TryResolveShortcut(string lnkPath, out string target)
    {
        try
        {
            // WScript.Shell COM-based .lnk resolution. Windows-only — Canary.Harness
            // already targets net8.0-windows so this is fine.
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) { target = string.Empty; return false; }

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) { target = string.Empty; return false; }

            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string resolved = shortcut.TargetPath ?? string.Empty;
            if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
            {
                target = resolved;
                return true;
            }
        }
        catch
        {
            // COM failure / missing host / etc. — fall through to false.
        }
        target = string.Empty;
        return false;
    }
}
