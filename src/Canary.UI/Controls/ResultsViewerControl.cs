using Canary.Orchestration;

namespace Canary.UI.Controls;

/// <summary>
/// Displays test results with per-checkpoint baseline/candidate/diff images and stats.
/// Scrollable, resizable layout using TableLayoutPanel.
/// </summary>
internal sealed class ResultsViewerControl : UserControl
{
    private readonly Panel _scrollPanel;

    public event Action<string>? ApproveCheckpointRequested;
    public event Action<string>? RejectCheckpointRequested;
    public event Action? ApproveAllRequested;

    public ResultsViewerControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);

        _scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(10)
        };

        Controls.Add(_scrollPanel);
    }

    public void LoadResult(TestResult result)
    {
        _scrollPanel.Controls.Clear();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateHeader(result));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            layout.Controls.Add(CreateErrorPanel(result.ErrorMessage));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        foreach (var cp in result.CheckpointResults)
        {
            layout.Controls.Add(CreateCheckpointRow(cp));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        _scrollPanel.Controls.Add(layout);
    }

    private static Button MakeButton(string text, Color backColor)
    {
        return new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = Color.White,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 2, 8, 2)
        };
    }

    private Panel CreateHeader(TestResult result)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = Color.FromArgb(37, 37, 38),
            Padding = new Padding(12)
        };

        var statusColor = StatusColor(result.Status);

        var nameLabel = new Label
        {
            Text = $"{result.TestName}  —  {result.Status.ToString().ToUpperInvariant()}",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = statusColor,
            AutoSize = true,
            Location = new Point(12, 12)
        };

        var infoLabel = new Label
        {
            Text = $"Workload: {result.Workload}  |  Duration: {result.Duration.TotalSeconds:F1}s  |  Checkpoints: {result.CheckpointResults.Count}",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true,
            Location = new Point(12, 46)
        };

        var approveAllBtn = MakeButton("Approve All", Color.FromArgb(0, 122, 204));
        approveAllBtn.Click += (_, _) => ApproveAllRequested?.Invoke();

        panel.Controls.Add(nameLabel);
        panel.Controls.Add(infoLabel);
        panel.Controls.Add(approveAllBtn);
        panel.Resize += (_, _) => approveAllBtn.Location = new Point(panel.Width - approveAllBtn.Width - 16, 20);

        return panel;
    }

    private static Panel CreateErrorPanel(string message)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Color.FromArgb(80, 30, 30),
            Padding = new Padding(12, 8, 12, 8)
        };
        panel.Controls.Add(new Label
        {
            Text = message,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(255, 180, 180),
            Dock = DockStyle.Fill,
            AutoSize = false
        });
        return panel;
    }

    private Panel CreateCheckpointRow(CheckpointResult cp)
    {
        bool hasError = !string.IsNullOrEmpty(cp.ErrorMessage);
        int imageTop = hasError ? 96 : 66;
        int panelHeight = imageTop + 220;

        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = panelHeight,
            BackColor = Color.FromArgb(37, 37, 38),
            Margin = new Padding(0, 6, 0, 0),
            Padding = new Padding(12)
        };

        var statusColor = StatusColor(cp.Status);

        // Checkpoint name
        var nameLabel = new Label
        {
            Text = cp.Name,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = statusColor,
            AutoSize = true,
            Location = new Point(12, 10)
        };

        // Stats line
        var statusText = cp.Status == TestStatus.New
            ? "NEW — no baseline yet"
            : $"Diff: {cp.DiffPercentage:P2}  |  Tolerance: {cp.Tolerance:P2}  |  SSIM: {cp.SsimScore:F4}";

        var statsLabel = new Label
        {
            Text = statusText,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true,
            Location = new Point(12, 36)
        };

        // Approve / Reject buttons — AutoSize so they never clip text
        var approveBtn = MakeButton("Approve", Color.FromArgb(40, 120, 40));
        approveBtn.Click += (_, _) => ApproveCheckpointRequested?.Invoke(cp.Name);

        var rejectBtn = MakeButton("Reject", Color.FromArgb(150, 40, 40));
        rejectBtn.Click += (_, _) => RejectCheckpointRequested?.Invoke(cp.Name);

        panel.Controls.Add(nameLabel);
        panel.Controls.Add(statsLabel);

        // Error label (if present)
        if (hasError)
        {
            var errorLabel = new Label
            {
                Text = cp.ErrorMessage,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(255, 180, 180),
                AutoSize = true,
                Location = new Point(12, 60)
            };
            panel.Controls.Add(errorLabel);
        }

        panel.Controls.Add(approveBtn);
        panel.Controls.Add(rejectBtn);

        // Image row using TableLayoutPanel for even distribution
        var imageTable = new TableLayoutPanel
        {
            Location = new Point(12, imageTop),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Height = 210,
            ColumnCount = 3,
            RowCount = 2,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        imageTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        imageTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        imageTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        imageTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        imageTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var images = new (string label, string? path)[]
        {
            ("Baseline", cp.BaselinePath),
            ("Candidate", cp.CandidatePath),
            ("Diff", cp.DiffImagePath)
        };

        for (int i = 0; i < images.Length; i++)
        {
            var (label, path) = images[i];

            imageTable.Controls.Add(new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(120, 120, 120),
                Dock = DockStyle.Fill
            }, i, 0);

            var pb = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(20, 20, 20),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(2)
            };

            if (path != null && File.Exists(path))
            {
                try
                {
                    // Load into memory copy to avoid holding a file lock
                    using var original = Image.FromFile(path);
                    pb.Image = new Bitmap(original);
                }
                catch { /* Image load failed */ }
            }

            imageTable.Controls.Add(pb, i, 1);
        }

        panel.Controls.Add(imageTable);

        // Position buttons on resize — use actual rendered widths
        panel.Resize += (_, _) =>
        {
            rejectBtn.Location = new Point(panel.Width - rejectBtn.Width - 14, 10);
            approveBtn.Location = new Point(rejectBtn.Left - approveBtn.Width - 8, 10);
            imageTable.Width = panel.Width - 24;
        };

        return panel;
    }

    private static Color StatusColor(TestStatus status) => status switch
    {
        TestStatus.Passed => Color.FromArgb(80, 200, 80),
        TestStatus.Failed => Color.FromArgb(220, 60, 60),
        TestStatus.Crashed => Color.FromArgb(180, 80, 220),
        TestStatus.New => Color.FromArgb(220, 180, 50),
        _ => Color.Gray
    };
}
