using Canary.Orchestration;

namespace Canary.UI.Controls;

/// <summary>
/// Displays test results with per-checkpoint baseline/candidate/diff images and stats.
/// Supports both single-test view and suite aggregation view.
/// Scrollable, resizable layout using TableLayoutPanel.
/// </summary>
internal sealed class ResultsViewerControl : UserControl
{
    private readonly Panel _scrollPanel;

    /// <summary>Fired with (testName, checkpointName).</summary>
    public event Action<string, string>? ApproveCheckpointRequested;
    /// <summary>Fired with (testName, checkpointName).</summary>
    public event Action<string, string>? RejectCheckpointRequested;
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

    /// <summary>Load a single test result (per-test drill-down view).</summary>
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
            layout.Controls.Add(CreateCheckpointRow(cp, result.TestName));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        _scrollPanel.Controls.Add(layout);
    }

    /// <summary>Load an aggregated suite result view.</summary>
    public void LoadSuiteResult(SuiteResult suite, string suiteName)
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

        // Suite header
        layout.Controls.Add(CreateSuiteHeader(suite, suiteName));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Per-test summary grid
        layout.Controls.Add(CreateSuiteGrid(suite));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Per-test checkpoint details (expandable)
        foreach (var result in suite.TestResults)
        {
            layout.Controls.Add(CreateTestSection(result));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        _scrollPanel.Controls.Add(layout);
    }

    #region Suite Aggregation

    private Panel CreateSuiteHeader(SuiteResult suite, string suiteName)
    {
        var total = suite.TestResults.Count;
        var allPassed = suite.Failed == 0 && suite.Crashed == 0;
        var headerColor = allPassed ? Color.FromArgb(80, 200, 80) : Color.FromArgb(220, 60, 60);

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Color.FromArgb(37, 37, 38),
            Padding = new Padding(12),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;

        // Row 0: Name + Approve All button
        var nameLabel = new Label
        {
            Text = $"Suite: {suiteName}  \u2014  {(allPassed ? "ALL PASSED" : "FAILURES")}",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = headerColor,
            AutoSize = true
        };

        var approveAllBtn = MakeButton("Approve All", Color.FromArgb(0, 122, 204));
        approveAllBtn.Click += (_, _) => ApproveAllRequested?.Invoke();

        panel.Controls.Add(nameLabel, 0, row);
        panel.Controls.Add(approveAllBtn, 1, row);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        // Row 1: Stats
        var statsText = $"Passed: {suite.Passed}  |  Failed: {suite.Failed}  |  Crashed: {suite.Crashed}  |  New: {suite.New}  |  Duration: {suite.TotalDuration.TotalSeconds:F1}s";
        var statsLabel = new Label
        {
            Text = statsText,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(180, 180, 180),
            AutoSize = true
        };
        panel.Controls.Add(statsLabel, 0, row);
        panel.SetColumnSpan(statsLabel, 2);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        // Row 2: Pass rate bar
        var passRate = total > 0 ? (double)suite.Passed / total : 0;
        var barPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 14,
            MinimumSize = new Size(0, 14),
            BackColor = Color.FromArgb(60, 60, 60)
        };
        var barColor = passRate >= 0.8 ? Color.FromArgb(80, 200, 80) :
                       passRate >= 0.5 ? Color.FromArgb(220, 180, 50) :
                       Color.FromArgb(220, 60, 60);
        var barFill = new Panel
        {
            Dock = DockStyle.Left,
            BackColor = barColor
        };
        barPanel.Controls.Add(barFill);
        barPanel.SizeChanged += (_, _) => barFill.Width = (int)(barPanel.Width * passRate);

        panel.Controls.Add(barPanel, 0, row);
        panel.SetColumnSpan(barPanel, 2);
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));

        return panel;
    }

    private Panel CreateSuiteGrid(SuiteResult suite)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White,
            DefaultCellStyle = { BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White, SelectionBackColor = Color.FromArgb(60, 60, 65) },
            ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(37, 37, 38), ForeColor = Color.FromArgb(200, 200, 200) },
            EnableHeadersVisualStyles = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(55, 55, 58)
        };

        grid.Columns.Add("Test", "Test");
        grid.Columns.Add("Status", "Status");
        grid.Columns.Add("Duration", "Duration");
        grid.Columns.Add("Checkpoints", "Checkpoints");
        grid.Columns.Add("VLM", "VLM");
        grid.Columns["Status"]!.Width = 80;
        grid.Columns["Duration"]!.Width = 80;
        grid.Columns["Checkpoints"]!.Width = 100;
        grid.Columns["VLM"]!.Width = 50;

        foreach (var r in suite.TestResults)
        {
            var vlmCount = r.CheckpointResults.Count(cp => cp.VlmDescription != null);
            var vlmText = vlmCount > 0 ? vlmCount.ToString() : "";
            var rowIdx = grid.Rows.Add(r.TestName, r.Status.ToString().ToUpperInvariant(), $"{r.Duration.TotalSeconds:F1}s", r.CheckpointResults.Count, vlmText);
            grid.Rows[rowIdx].DefaultCellStyle.ForeColor = StatusColor(r.Status);
        }

        // Click row to scroll to that test's detail section
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= suite.TestResults.Count) return;
            var testName = suite.TestResults[e.RowIndex].TestName;
            // Find the test section panel and scroll to it
            foreach (Control c in _scrollPanel.Controls)
            {
                if (c is TableLayoutPanel tlp)
                {
                    foreach (Control row in tlp.Controls)
                    {
                        if (row is Panel p && p.Tag is string tag && tag == testName)
                        {
                            _scrollPanel.ScrollControlIntoView(p);
                            return;
                        }
                    }
                }
            }
        };

        var height = Math.Min(30 + suite.TestResults.Count * 28, 590);
        var wrapper = new Panel
        {
            Dock = DockStyle.Top,
            Height = height,
            Margin = new Padding(0, 6, 0, 6),
            Padding = new Padding(12, 0, 12, 0)
        };
        wrapper.Controls.Add(grid);
        return wrapper;
    }

    private Panel CreateTestSection(TestResult result)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Color.FromArgb(30, 30, 30),
            Margin = new Padding(0, 4, 0, 0),
            Padding = new Padding(0),
            Tag = result.TestName
        };

        var innerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        innerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Test sub-header
        var header = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MinimumSize = new Size(0, 36),
            BackColor = Color.FromArgb(42, 42, 45),
            Padding = new Padding(12, 8, 12, 8)
        };
        header.Controls.Add(new Label
        {
            Text = $"{result.TestName}  \u2014  {result.Status.ToString().ToUpperInvariant()}  ({result.Duration.TotalSeconds:F1}s)",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = StatusColor(result.Status),
            AutoSize = true,
            Dock = DockStyle.Fill
        });
        innerLayout.Controls.Add(header);
        innerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            innerLayout.Controls.Add(CreateErrorPanel(result.ErrorMessage));
            innerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        foreach (var cp in result.CheckpointResults)
        {
            innerLayout.Controls.Add(CreateCheckpointRow(cp, result.TestName));
            innerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        panel.Controls.Add(innerLayout);
        return panel;
    }

    #endregion

    #region Shared Components

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
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(3)
        };
    }

    private Panel CreateHeader(TestResult result)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.FromArgb(37, 37, 38),
            Padding = new Padding(12),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var statusColor = StatusColor(result.Status);

        var nameLabel = new Label
        {
            Text = $"{result.TestName}  \u2014  {result.Status.ToString().ToUpperInvariant()}",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = statusColor,
            AutoSize = true
        };

        var vlmCount = result.CheckpointResults.Count(cp => cp.VlmDescription != null);
        var vlmSuffix = vlmCount > 0 ? $"  |  VLM: {vlmCount}" : "";
        var infoLabel = new Label
        {
            Text = $"Workload: {result.Workload}  |  Duration: {result.Duration.TotalSeconds:F1}s  |  Checkpoints: {result.CheckpointResults.Count}{vlmSuffix}",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true
        };

        var approveAllBtn = MakeButton("Approve All", Color.FromArgb(0, 122, 204));
        approveAllBtn.Click += (_, _) => ApproveAllRequested?.Invoke();

        panel.Controls.Add(nameLabel, 0, 0);
        panel.Controls.Add(approveAllBtn, 1, 0);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(infoLabel, 0, 1);
        panel.SetColumnSpan(infoLabel, 2);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        return panel;
    }

    private static Panel CreateErrorPanel(string message)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MinimumSize = new Size(0, 36),
            BackColor = Color.FromArgb(80, 30, 30),
            Padding = new Padding(12, 8, 12, 8)
        };
        panel.Controls.Add(new Label
        {
            Text = message,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(255, 180, 180),
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(0, 200)
        });
        return panel;
    }

    private Control CreateCheckpointRow(CheckpointResult cp, string testName)
    {
        bool isVlm = cp.VlmDescription != null;
        bool hasError = !string.IsNullOrEmpty(cp.ErrorMessage);

        if (isVlm)
            return CreateVlmCheckpointRow(cp, testName, hasError);

        return CreatePixelDiffCheckpointRow(cp, testName, hasError);
    }

    private Control CreatePixelDiffCheckpointRow(CheckpointResult cp, string testName, bool hasError)
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.FromArgb(37, 37, 38),
            Margin = new Padding(0, 6, 0, 0),
            Padding = new Padding(12),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        var statusColor = StatusColor(cp.Status);

        // Row 0: Name label (left) + Approve/Reject buttons (right)
        var headerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            MinimumSize = new Size(0, 34),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var nameLabel = new Label
        {
            Text = cp.Name,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = statusColor,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 4, 0, 0)
        };

        var btnFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right
        };

        var approveBtn = MakeButton("Approve", Color.FromArgb(40, 120, 40));
        approveBtn.Click += (_, _) => ApproveCheckpointRequested?.Invoke(testName, cp.Name);
        var rejectBtn = MakeButton("Reject", Color.FromArgb(150, 40, 40));
        rejectBtn.Click += (_, _) => RejectCheckpointRequested?.Invoke(testName, cp.Name);
        btnFlow.Controls.Add(approveBtn);
        btnFlow.Controls.Add(rejectBtn);

        headerPanel.Controls.Add(nameLabel, 0, 0);
        headerPanel.Controls.Add(btnFlow, 1, 0);
        headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        outer.Controls.Add(headerPanel, 0, row);
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        // Row 1: Stats line
        var statusText = cp.Status == TestStatus.New
            ? "NEW \u2014 no baseline yet"
            : $"Diff: {cp.DiffPercentage:P2}  |  Tolerance: {cp.Tolerance:P2}  |  SSIM: {cp.SsimScore:F4}";

        var statsLabel = new Label
        {
            Text = statusText,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        outer.Controls.Add(statsLabel, 0, row);
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        // Row 2: Error label (conditional)
        if (hasError)
        {
            var errorLabel = new Label
            {
                Text = cp.ErrorMessage,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(255, 180, 180),
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            outer.Controls.Add(errorLabel, 0, row);
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row++;
        }

        // Row 3: Image table (3 columns: Baseline/Candidate/Diff)
        var imageTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 180),
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

        var allPaths = images.Select(i => i.path).Where(p => p != null).ToArray()!;

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
                Margin = new Padding(2),
                Cursor = Cursors.Hand
            };

            if (path != null && File.Exists(path))
            {
                try
                {
                    using var original = Image.FromFile(path);
                    pb.Image = new Bitmap(original);
                }
                catch { /* Image load failed */ }
            }

            // Click to open full-res viewer
            var clickPath = path;
            pb.Click += (_, _) =>
            {
                if (clickPath != null)
                    new ImageViewerForm(clickPath, allPaths!).Show();
            };

            imageTable.Controls.Add(pb, i, 1);
        }

        outer.Controls.Add(imageTable, 0, row);
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));

        return outer;
    }

    private Control CreateVlmCheckpointRow(CheckpointResult cp, string testName, bool hasError)
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.FromArgb(37, 37, 38),
            Margin = new Padding(0, 6, 0, 0),
            Padding = new Padding(12),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        var statusColor = StatusColor(cp.Status);

        // Row 0: Name [VLM] + buttons
        var headerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            MinimumSize = new Size(0, 34),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var nameLabel = new Label
        {
            Text = $"{cp.Name}  [VLM]",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = statusColor,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 4, 0, 0)
        };

        var btnFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right
        };

        var approveBtn = MakeButton("Approve", Color.FromArgb(40, 120, 40));
        approveBtn.Click += (_, _) => ApproveCheckpointRequested?.Invoke(testName, cp.Name);
        var rejectBtn = MakeButton("Reject", Color.FromArgb(150, 40, 40));
        rejectBtn.Click += (_, _) => RejectCheckpointRequested?.Invoke(testName, cp.Name);
        btnFlow.Controls.Add(approveBtn);
        btnFlow.Controls.Add(rejectBtn);

        headerPanel.Controls.Add(nameLabel, 0, 0);
        headerPanel.Controls.Add(btnFlow, 1, 0);
        headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        outer.Controls.Add(headerPanel, 0, row);
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        // Row 1: VLM stats line
        var confidenceText = cp.VlmConfidence > 0 ? $"Confidence: {cp.VlmConfidence:F2}" : "Confidence: \u2014";
        var statsLabel = new Label
        {
            Text = $"VLM: {cp.Status.ToString().ToUpperInvariant()}  |  {confidenceText}",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        outer.Controls.Add(statsLabel, 0, row);
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        // Row 2: Error label (conditional)
        if (hasError)
        {
            var errorLabel = new Label
            {
                Text = cp.ErrorMessage,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(255, 180, 180),
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            outer.Controls.Add(errorLabel, 0, row);
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row++;
        }

        // Row 3: Confidence bar
        var confBarBg = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 10,
            MinimumSize = new Size(0, 10),
            BackColor = Color.FromArgb(60, 60, 60)
        };
        var confColor = cp.VlmConfidence >= 0.8 ? Color.FromArgb(80, 200, 80) :
                        cp.VlmConfidence >= 0.5 ? Color.FromArgb(220, 180, 50) :
                        Color.FromArgb(220, 60, 60);
        var confBarFill = new Panel
        {
            Dock = DockStyle.Left,
            BackColor = confColor
        };
        confBarBg.Controls.Add(confBarFill);
        confBarBg.SizeChanged += (_, _) =>
            confBarFill.Width = (int)(confBarBg.Width * Math.Clamp(cp.VlmConfidence, 0, 1));

        outer.Controls.Add(confBarBg, 0, row);
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        row++;

        // Row 4: Expected description label
        var descLabel = new Label
        {
            Text = $"Expected: {cp.VlmDescription}",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(120, 170, 220),
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        outer.Controls.Add(descLabel, 0, row);
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        // Row 5: Reasoning RichTextBox
        var reasoningBox = new RichTextBox
        {
            Text = cp.VlmReasoning ?? "(no reasoning available)",
            Dock = DockStyle.Fill,
            Height = 80,
            BackColor = Color.FromArgb(25, 25, 28),
            ForeColor = Color.FromArgb(190, 190, 190),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f),
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        outer.Controls.Add(reasoningBox, 0, row);
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        row++;

        // Row 6: Candidate label + PictureBox
        var candidatePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        candidatePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        candidatePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        candidatePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        candidatePanel.Controls.Add(new Label
        {
            Text = "Candidate",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(120, 120, 120),
            Dock = DockStyle.Fill
        }, 0, 0);

        var candidatePb = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(20, 20, 20),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand
        };

        if (cp.CandidatePath != null && File.Exists(cp.CandidatePath))
        {
            try
            {
                using var original = Image.FromFile(cp.CandidatePath);
                candidatePb.Image = new Bitmap(original);
            }
            catch { /* Image load failed */ }
        }

        // Click to open full-res viewer
        var clickPath = cp.CandidatePath;
        candidatePb.Click += (_, _) =>
        {
            if (clickPath != null)
                new ImageViewerForm(clickPath, new[] { clickPath }).Show();
        };

        candidatePanel.Controls.Add(candidatePb, 0, 1);

        outer.Controls.Add(candidatePanel, 0, row);
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));

        return outer;
    }

    #endregion

    private static Color StatusColor(TestStatus status) => status switch
    {
        TestStatus.Passed => Color.FromArgb(80, 200, 80),
        TestStatus.Failed => Color.FromArgb(220, 60, 60),
        TestStatus.Crashed => Color.FromArgb(180, 80, 220),
        TestStatus.New => Color.FromArgb(220, 180, 50),
        _ => Color.Gray
    };
}
