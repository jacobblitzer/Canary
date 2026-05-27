using Canary.Config;
using Canary.UI.Controls;
using Canary.UI.Panels;

namespace Canary.UI.Navigation;

// Phase 7 / design §C4 — concrete INavMode implementations.
// Each mode lazy-creates its content panel on first activation +
// caches it for re-activation. MainForm constructs an instance of
// each and adds them to the top tab strip.

public sealed class PastRunsNavMode : INavMode
{
    public string Name => "Past Runs";
    public string Description => "Browse historical test runs (REPORT.md per run from Phase 3).";

    private PastRunsPanel? _panel;

    public Control CreateContent()
    {
        _panel ??= new PastRunsPanel { Dock = DockStyle.Fill };
        return _panel;
    }

    public void SetWorkloadsDir(string? workloadsDir)
    {
        _panel?.SetWorkloadsDir(workloadsDir);
    }
}

public sealed class LocalhostNavMode : INavMode
{
    public string Name => "Localhost";
    public string Description => "TCP listeners on common dev-server ports (Tier 1 + Tier 2 provenance from Canary.Localhost).";

    private LocalhostPanel? _panel;

    public Control CreateContent()
    {
        _panel ??= new LocalhostPanel { Dock = DockStyle.Fill };
        return _panel;
    }
}

public sealed class FeedbackNavMode : INavMode
{
    public string Name => "Feedback";
    public string Description => "Operator-authored feedback items in docs/feedback/{inbox,triaged,resolved}/.";

    private FeedbackPanel? _panel;

    public Control CreateContent()
    {
        _panel ??= new FeedbackPanel { Dock = DockStyle.Fill };
        return _panel;
    }
}

public sealed class TelemetryNavMode : INavMode
{
    public string Name => "Telemetry";
    public string Description => "Live tail of the most recent telemetry.ndjson (Phase 2).";

    private TelemetryPanel? _panel;

    public Control CreateContent()
    {
        _panel ??= new TelemetryPanel { Dock = DockStyle.Fill };
        return _panel;
    }

    public void SetWorkloadsDir(string? workloadsDir)
    {
        _panel?.SetWorkloadsDir(workloadsDir);
    }
}

public sealed class SettingsNavMode : INavMode
{
    public string Name => "Settings";
    public string Description => "UI mode (Stabilization / Maturation) and other knobs.";

    private SettingsPanel? _panel;

    public Control CreateContent()
    {
        _panel ??= new SettingsPanel { Dock = DockStyle.Fill };
        return _panel;
    }
}

public sealed class SessionsNavMode : INavMode
{
    public string Name => "Sessions";
    public string Description => "Supervised sessions — operator-driven debugging with on-demand capture (Phase 2 / supervised-session feature).";

    private SessionsPanel? _panel;

    public Control CreateContent()
    {
        _panel ??= new SessionsPanel { Dock = DockStyle.Fill };
        return _panel;
    }

    public void SetWorkloads(string? workloadsDir, IEnumerable<WorkloadConfig> workloads)
        => _panel?.SetWorkloads(workloadsDir, workloads);

    public bool ProcessHotkeyMessage(ref Message m) => _panel?.ProcessHotkeyMessage(ref m) ?? false;
}

