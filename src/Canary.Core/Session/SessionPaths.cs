using System.Security.Cryptography;

namespace Canary.Session;

public static class SessionPaths
{
    public const string SessionsSubdir = "sessions";
    public const string CapturesSubdir = "captures";
    public const string ReportFileName = "SESSION_REPORT.md";
    public const string SessionJsonFileName = "session.json";
    public const string TelemetryNdjsonFileName = "telemetry.ndjson";

    public static string GenerateSessionId(DateTime utcNow)
    {
        Span<byte> bytes = stackalloc byte[2];
        RandomNumberGenerator.Fill(bytes);
        var nonce = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{utcNow:yyyyMMdd-HHmmss}-{nonce}";
    }

    public static string SessionsDir(string workloadsDir, string workload)
        => Path.Combine(workloadsDir, workload, SessionsSubdir);

    public static string SessionDir(string workloadsDir, string workload, string sessionId)
        => Path.Combine(SessionsDir(workloadsDir, workload), sessionId);

    public static string CapturesDir(string sessionDir)
        => Path.Combine(sessionDir, CapturesSubdir);

    public static string ReportPath(string sessionDir)
        => Path.Combine(sessionDir, ReportFileName);

    public static string SessionJsonPath(string sessionDir)
        => Path.Combine(sessionDir, SessionJsonFileName);

    public static string TelemetryPath(string sessionDir)
        => Path.Combine(sessionDir, TelemetryNdjsonFileName);

    public static string CapturePngFile(int sequence, DateTime utcNow, string? slug)
        => $"{sequence:D3}-{utcNow:HH-mm-ss}{SlugSuffix(slug)}.png";

    public static string CaptureAnnotatedPngFile(int sequence, DateTime utcNow, string? slug)
        => $"{sequence:D3}-{utcNow:HH-mm-ss}{SlugSuffix(slug)}.annotated.png";

    public static string CaptureAnnotationsJsonFile(int sequence, DateTime utcNow, string? slug)
        => $"{sequence:D3}-{utcNow:HH-mm-ss}{SlugSuffix(slug)}.annotations.json";

    public static string CaptureMarkdownFile(int sequence, DateTime utcNow, string? slug)
        => $"{sequence:D3}-{utcNow:HH-mm-ss}{SlugSuffix(slug)}.md";

    private static string SlugSuffix(string? slug)
        => string.IsNullOrEmpty(slug) ? string.Empty : "-" + slug;
}
