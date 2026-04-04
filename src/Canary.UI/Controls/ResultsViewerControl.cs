using Canary.Orchestration;

namespace Canary.UI.Controls;

/// <summary>
/// Displays test results with per-checkpoint baseline/candidate/diff images and stats.
/// </summary>
internal sealed class ResultsViewerControl : UserControl
{
    private readonly FlowLayoutPanel _checkpointsPanel;
    private TestResult? _testResult;

    public event Action<string>? ApproveCheckpointRequested;
    public event Action<string>? RejectCheckpointRequested;
    public event Action? ApproveAllRequested;
    public event Action<string, string[]>? ImageClicked;

    public ResultsViewerControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);
        AutoScroll = true;

        _checkpointsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            AutoSize = false,
            Padding = new Padding(10)
        };

        Controls.Add(_checkpointsPanel);
    }

    public void LoadResult(TestResult result)
    {
        _testResult = result;
        _checkpointsPanel.Controls.Clear();

        // Test header
        var header = CreateHeader(result);
        _checkpointsPanel.Controls.Add(header);

        // Per-checkpoint rows
        foreach (var cp in result.CheckpointResults)
        {
            var row = CreateCheckpointRow(cp);
            _checkpointsPanel.Controls.Add(row);
        }
    }

    private Panel CreateHeader(TestResult result)
    {
        var panel = new Panel
        {
            Width = _checkpointsPanel.ClientSize.Width - 30,
            Height = 60,
            BackColor = Color.FromArgb(37, 37, 38),
            Padding = new Padding(10)
        };

        var statusColor = result.Status switch
        {
            TestStatus.Passed => Color.FromArgb(80, 200, 80),
            TestStatus.Failed => Color.FromArgb(220, 60, 60),
            TestStatus.Crashed => Color.FromArgb(180, 80, 220),
            TestStatus.New => Color.FromArgb(220, 180, 50),
            _ => Color.Gray
        };

        var nameLabel = new Label
        {
            Text = result.TestName,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 220, 220),
            AutoSize = true,
            Location = new Point(10, 10)
        };

        var statusLabel = new Label
        {
            Text = result.Status.ToString().ToUpperInvariant(),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = statusColor,
            AutoSize = true,
            Location = new Point(10, 38)
        };

        var durationLabel = new Label
        {
            Text = $"Duration: {result.Duration.TotalSeconds:F1}s",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(140, 140, 140),
            AutoSize = true,
            Location = new Point(200, 40)
        };

        var approveAllBtn = new Button
        {
            Text = "Approve All",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            Size = new Size(100, 30),
            Anchor = AnchorStyles.Right,
            Location = new Point(panel.Width - 120, 15)
        };
        approveAllBtn.Click += (_, _) => ApproveAllRequested?.Invoke();

        panel.Controls.Add(nameLabel);
        panel.Controls.Add(statusLabel);
        panel.Controls.Add(durationLabel);
        panel.Controls.Add(approveAllBtn);

        return panel;
    }

    private Panel CreateCheckpointRow(CheckpointResult cp)
    {
        var panel = new Panel
        {
            Width = _checkpointsPanel.ClientSize.Width - 30,
            Height = 240,
            BackColor = Color.FromArgb(37, 37, 38),
            Margin = new Padding(0, 5, 0, 5),
            Padding = new Padding(10)
        };

        // Stats header
        var statusColor = cp.Status switch
        {
            TestStatus.Passed => Color.FromArgb(80, 200, 80),
            TestStatus.Failed => Color.FromArgb(220, 60, 60),
            TestStatus.Crashed => Color.FromArgb(180, 80, 220),
            TestStatus.New => Color.FromArgb(220, 180, 50),
            _ => Color.Gray
        };

        var nameLabel = new Label
        {
            Text = cp.Name,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = statusColor,
            AutoSize = true,
            Location = new Point(10, 5)
        };
        panel.Controls.Add(nameLabel);

        var statsLabel = new Label
        {
            Text = $"Diff: {cp.DiffPercentage:P2}  |  Tolerance: {cp.Tolerance:P2}  |  SSIM: {cp.SsimScore:F4}",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true,
            Location = new Point(10, 28)
        };
        panel.Controls.Add(statsLabel);

        // Image row: baseline | candidate | diff
        int imgTop = 50;
        int imgWidth = (panel.Width - 50) / 3;
        int imgHeight = 150;

        var images = new (string label, string? path)[]
        {
            ("Baseline", cp.BaselinePath),
            ("Candidate", cp.CandidatePath),
            ("Diff", cp.DiffImagePath)
        };

        var allPaths = images.Where(i => i.path != null).Select(i => i.path!).ToArray();

        for (int i = 0; i < images.Length; i++)
        {
            var (label, path) = images[i];
            int x = 10 + i * (imgWidth + 5);

            var imgLabel = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(x, imgTop),
                AutoSize = true
            };
            panel.Controls.Add(imgLabel);

            var pb = new PictureBox
            {
                Location = new Point(x, imgTop + 18),
                Size = new Size(imgWidth, imgHeight),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(20, 20, 20),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };

            if (path != null && File.Exists(path))
            {
                try
                {
                    pb.Image = Image.FromFile(path);
                }
                catch
                {
                    // Image load failed — leave blank
                }
            }

            var capturedPath = path ?? "";
            pb.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(capturedPath))
                    ImageClicked?.Invoke(capturedPath, allPaths);
            };

            panel.Controls.Add(pb);
        }

        // Approve/Reject buttons
        var approveBtn = new Button
        {
            Text = "Approve",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(40, 120, 40),
            ForeColor = Color.White,
            Size = new Size(80, 26),
            Location = new Point(panel.Width - 190, 5)
        };
        approveBtn.Click += (_, _) => ApproveCheckpointRequested?.Invoke(cp.Name);

        var rejectBtn = new Button
        {
            Text = "Reject",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(150, 40, 40),
            ForeColor = Color.White,
            Size = new Size(80, 26),
            Location = new Point(panel.Width - 100, 5)
        };
        rejectBtn.Click += (_, _) => RejectCheckpointRequested?.Invoke(cp.Name);

        panel.Controls.Add(approveBtn);
        panel.Controls.Add(rejectBtn);

        return panel;
    }
}
