namespace Canary.Telemetry;

/// <summary>
/// Rescues the GLOBAL Penumbra preview telemetry file before a Rhino spawn destroys it
/// (flight-recorder Phase A, gap G-B): the Penumbra Rhino plug-in truncates
/// %LocalAppData%\Penumbra\preview\telemetry.ndjson on EVERY plugin load
/// (NdjsonLog.Init, FileMode.Create), so the PREVIOUS session's evidence survives only if
/// copied aside before the next Rhino starts. Retire/reframe once Penumbra archives its own
/// history (flight-recorder Phase B item 1).
/// All methods are best-effort and never throw — a failed rescue must not block a launch.
/// </summary>
public static class PenumbraTelemetryRescue
{
    /// <summary>Where rescues from TEST-RUN spawns land (sessions rescue into their own session dir).</summary>
    public static string GlobalRescueDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Canary", "telemetry-rescue");

    /// <summary>How many rescue files GlobalRescueDir keeps (oldest pruned first).</summary>
    public const int GlobalKeep = 10;

    /// <summary>
    /// Copy the global Penumbra telemetry file (and its .prev rotation sibling, if present) to
    /// <paramref name="destFilePath"/> (sibling gets a ".prev.ndjson" suffix). Returns true if
    /// the primary file existed and was copied.
    /// </summary>
    public static bool TryRescueTo(string destFilePath, string? sourcePath = null)
    {
        try
        {
            var src = sourcePath ?? PenumbraPreviewTelemetryTail.DefaultPath;
            bool copied = false;
            var destDir = Path.GetDirectoryName(destFilePath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            if (File.Exists(src))
            {
                CopyShared(src, destFilePath);
                copied = true;
            }
            var prev = src + ".prev";
            if (File.Exists(prev))
            {
                CopyShared(prev, Path.ChangeExtension(destFilePath, null) + ".prev.ndjson");
            }
            return copied;
        }
        catch { return false; }
    }

    /// <summary>Rescue into GlobalRescueDir with a timestamped name, then prune to GlobalKeep.
    /// Skips empty sources so worthless rescues don't evict real evidence from the cap.</summary>
    public static void RescueGlobal()
    {
        try
        {
            var fi = new FileInfo(PenumbraPreviewTelemetryTail.DefaultPath);
            if (!fi.Exists || fi.Length == 0) return;
            var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}.ndjson";
            TryRescueTo(Path.Combine(GlobalRescueDir, name));
            Prune(GlobalRescueDir, GlobalKeep);
        }
        catch { }
    }

    /// <summary>Keep the newest <paramref name="keep"/> primary rescues (plus their .prev siblings);
    /// delete the rest. Never throws.</summary>
    public static void Prune(string dir, int keep)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var primaries = Directory.GetFiles(dir, "*.ndjson")
                .Where(f => !f.EndsWith(".prev.ndjson", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            foreach (var stale in primaries.Skip(keep))
            {
                try { File.Delete(stale); } catch { }
                var prevSibling = Path.ChangeExtension(stale, null) + ".prev.ndjson";
                try { if (File.Exists(prevSibling)) File.Delete(prevSibling); } catch { }
            }
        }
        catch { }
    }

    /// <summary>Copy allowing a concurrent writer (the file is AutoFlush-appended by Rhino).</summary>
    private static void CopyShared(string src, string dest)
    {
        using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }
}
