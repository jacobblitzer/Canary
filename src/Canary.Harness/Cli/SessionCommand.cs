using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Canary.Harness.Session;
using Canary.Session;

namespace Canary.Cli;

public static class SessionCommand
{
    public static Command Create()
    {
        var cmd = new Command(
            "session",
            "Supervised session mode — launch a workload's target app under Canary supervision (no automated tests) " +
            "and capture-and-annotate on demand.");
        cmd.AddCommand(BuildStart());
        cmd.AddCommand(BuildList());
        cmd.AddCommand(BuildReport());
        return cmd;
    }

    private static Command BuildStart()
    {
        var workloadOpt = new Option<string>("--workload", "Workload to launch (qualia | penumbra).") { IsRequired = true };
        var urlOpt = new Option<string?>("--url", "URL override for the report header (defaults to the workload's Vite URL).");

        var c = new Command("start", "Launch the workload under supervision + enter the capture REPL.")
        {
            workloadOpt,
            urlOpt,
        };

        c.SetHandler(async ctx =>
        {
            var workload = ctx.ParseResult.GetValueForOption(workloadOpt)!;
            var url = ctx.ParseResult.GetValueForOption(urlOpt);
            ctx.ExitCode = await RunStartAsync(workload, url, Program.CancellationToken).ConfigureAwait(false);
        });

        return c;
    }

    private static async Task<int> RunStartAsync(string workload, string? urlOverride, CancellationToken ct)
    {
        var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");
        var configPath = Path.Combine(workloadsDir, workload, "workload.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Error: Workload config not found: {configPath}");
            return 1;
        }

        var factory = new SessionAgentFactory();

        Console.WriteLine($"[Canary] Starting supervised session for workload '{workload}'...");
        SupervisedSession session;
        try
        {
            session = await SupervisedSession.StartAsync(workloadsDir, workload, configPath, factory, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to start session: {ex.Message}");
            return 1;
        }

        if (urlOverride != null) session.Url = urlOverride;

        await using (session)
        {
            Console.WriteLine();
            Console.WriteLine($"[supervised session armed] sessionId={session.SessionId} workload={workload} url={session.Url ?? "(unknown)"}");
            Console.WriteLine("   c = capture     a = capture + open in viewer     n = capture with note     q = end + write report");
            if (Console.IsInputRedirected)
                Console.WriteLine("   (stdin is redirected — line-mode REPL: one command character per line)");
            Console.WriteLine($"   dir: {session.Directory}");
            Console.WriteLine();

            try { await RunReplAsync(session, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* Ctrl+C — fall through to close-out */ }

            Console.Write("Closeout notes (one line, optional): ");
            string? closeout = null;
            try { closeout = Console.In.ReadLine(); }
            catch { /* swallow — close-out is optional */ }

            await session.EndAsync(closeout, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[Canary] Session ended. Report: {SessionPaths.ReportPath(session.Directory)}");
        }

        return 0;
    }

    private static async Task RunReplAsync(SupervisedSession session, CancellationToken ct)
    {
        // Console.KeyAvailable / Console.ReadKey throw InvalidOperationException
        // when stdin is redirected from a file or pipe (CI scripts, smoke
        // tests, `printf "c\nq\n" | canary session start`, etc). Detect
        // up front and fall back to a line-mode REPL: one command character
        // per line.
        if (Console.IsInputRedirected)
        {
            await RunLineModeReplAsync(session, ct).ConfigureAwait(false);
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            if (!Console.KeyAvailable)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
                continue;
            }
            var k = Console.ReadKey(intercept: true);
            switch (char.ToLowerInvariant(k.KeyChar))
            {
                case 'c':
                    await DoCaptureAsync(session, openInViewer: false, withNote: false, ct).ConfigureAwait(false);
                    break;
                case 'a':
                    await DoCaptureAsync(session, openInViewer: true, withNote: false, ct).ConfigureAwait(false);
                    break;
                case 'n':
                    await DoCaptureAsync(session, openInViewer: false, withNote: true, ct).ConfigureAwait(false);
                    break;
                case 'q':
                    return;
            }
        }
    }

    private static async Task RunLineModeReplAsync(SupervisedSession session, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try { line = await Console.In.ReadLineAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            if (line == null) return;
            var trimmed = line.Trim().ToLowerInvariant();
            if (trimmed.Length == 0) continue;
            switch (trimmed[0])
            {
                case 'c':
                    await DoCaptureAsync(session, openInViewer: false, withNote: false, ct).ConfigureAwait(false);
                    break;
                case 'a':
                    await DoCaptureAsync(session, openInViewer: true, withNote: false, ct).ConfigureAwait(false);
                    break;
                case 'n':
                    await DoCaptureAsync(session, openInViewer: false, withNote: true, ct).ConfigureAwait(false);
                    break;
                case 'q':
                    return;
            }
        }
    }

    private static async Task DoCaptureAsync(SupervisedSession session, bool openInViewer, bool withNote, CancellationToken ct)
    {
        string? title = null;
        string? body = null;
        if (withNote)
        {
            Console.Write("  title: ");
            title = Console.In.ReadLine();
            Console.Write("  note: ");
            body = Console.In.ReadLine();
        }

        try
        {
            var result = await session.CaptureAsync(title, body, ct).ConfigureAwait(false);
            Console.WriteLine($"  [{session.Captures.Count}] captured → {Path.GetFileName(result.PngPath)}");
            if (openInViewer)
            {
                try { Process.Start(new ProcessStartInfo { FileName = result.PngPath, UseShellExecute = true }); }
                catch (Exception ex) { Console.WriteLine($"  (open failed: {ex.Message})"); }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  capture failed: {ex.Message}");
        }
    }

    private static Command BuildList()
    {
        var workloadOpt = new Option<string?>("--workload", "Filter by workload");
        var limitOpt = new Option<int>("--limit", () => 20, "Max sessions to print");
        var c = new Command("list", "List past supervised sessions.") { workloadOpt, limitOpt };
        c.SetHandler(ctx =>
        {
            var workload = ctx.ParseResult.GetValueForOption(workloadOpt);
            var limit = ctx.ParseResult.GetValueForOption(limitOpt);
            ctx.ExitCode = RunList(workload, limit);
        });
        return c;
    }

    private static int RunList(string? workloadFilter, int limit)
    {
        var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");
        if (!Directory.Exists(workloadsDir))
        {
            Console.Error.WriteLine($"Error: workloads dir not found: {workloadsDir}");
            return 1;
        }

        var rows = new List<(string workload, string id, DateTime started, int captures, string dir)>();
        foreach (var wDir in Directory.EnumerateDirectories(workloadsDir))
        {
            var wName = Path.GetFileName(wDir);
            if (workloadFilter != null && !string.Equals(wName, workloadFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            var sessionsDir = Path.Combine(wDir, "sessions");
            if (!Directory.Exists(sessionsDir)) continue;
            foreach (var sDir in Directory.EnumerateDirectories(sessionsDir))
            {
                var data = SessionReportWriter.TryReadJson(sDir);
                if (data == null) continue;
                rows.Add((wName, data.SessionId, data.StartedAtUtc, data.Captures.Count, sDir));
            }
        }

        var ordered = rows.OrderByDescending(r => r.started).Take(limit).ToList();
        if (ordered.Count == 0)
        {
            Console.WriteLine("(no sessions found)");
            return 0;
        }

        Console.WriteLine($"{"Started (UTC)",-20} {"Workload",-10} {"SessionId",-22} {"Caps",4}  Dir");
        foreach (var r in ordered)
            Console.WriteLine($"{r.started:yyyy-MM-dd HH:mm:ss}  {r.workload,-10} {r.id,-22} {r.captures,4}  {r.dir}");
        return 0;
    }

    private static Command BuildReport()
    {
        var idOpt = new Option<string>("--id", "Session id (yyyyMMdd-HHmmss-xxxx)") { IsRequired = true };
        var workloadOpt = new Option<string?>("--workload", "Restrict search to a specific workload");
        var c = new Command("report", "Print SESSION_REPORT.md for a session id.") { idOpt, workloadOpt };
        c.SetHandler(ctx =>
        {
            var id = ctx.ParseResult.GetValueForOption(idOpt)!;
            var workload = ctx.ParseResult.GetValueForOption(workloadOpt);
            ctx.ExitCode = RunReport(id, workload);
        });
        return c;
    }

    private static int RunReport(string id, string? workloadFilter)
    {
        var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");
        if (!Directory.Exists(workloadsDir))
        {
            Console.Error.WriteLine($"Error: workloads dir not found: {workloadsDir}");
            return 1;
        }

        foreach (var wDir in Directory.EnumerateDirectories(workloadsDir))
        {
            var wName = Path.GetFileName(wDir);
            if (workloadFilter != null && !string.Equals(wName, workloadFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            var sDir = Path.Combine(wDir, "sessions", id);
            var report = Path.Combine(sDir, SessionPaths.ReportFileName);
            if (File.Exists(report))
            {
                Console.WriteLine($"# {report}");
                Console.WriteLine();
                Console.WriteLine(File.ReadAllText(report));
                return 0;
            }
        }

        Console.Error.WriteLine($"Error: session '{id}' not found.");
        return 1;
    }
}
