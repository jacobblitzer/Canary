namespace Canary.UI.Controls;

/// <summary>
/// Welcome panel shown when no test is selected.
/// </summary>
internal sealed class WelcomePanel : Panel
{
    public WelcomePanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);

        var title = new Label
        {
            Text = "Canary",
            Font = new Font("Segoe UI", 28f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 200, 50),
            AutoSize = true,
            Anchor = AnchorStyles.None
        };

        var subtitle = new Label
        {
            Text = "Visual Regression Testing Harness",
            Font = new Font("Segoe UI", 12f),
            ForeColor = Color.FromArgb(180, 180, 180),
            AutoSize = true,
            Anchor = AnchorStyles.None
        };

        var instructions = new Label
        {
            Text = "Select a workload or test from the tree to get started.\n\n"
                 + "Use the toolbar to:\n"
                 + "  \u2022 Open a workloads folder\n"
                 + "  \u2022 Run tests against a workload\n"
                 + "  \u2022 Record input for new tests\n"
                 + "  \u2022 Approve candidate baselines\n"
                 + "  \u2022 View HTML reports",
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(140, 140, 140),
            AutoSize = true,
            Anchor = AnchorStyles.None
        };

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(40, 60, 40, 40)
        };

        layout.Controls.Add(title);
        layout.Controls.Add(subtitle);
        layout.Controls.Add(new Label { Height = 20 }); // spacer
        layout.Controls.Add(instructions);

        Controls.Add(layout);
    }
}
