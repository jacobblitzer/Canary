using System.Diagnostics;
using Canary.Config;
using Canary.Orchestration;
using Canary.UI.Controls;
using Canary.UI.Services;

namespace Canary.UI;

/// <summary>
/// Main application window with toolbar, tree navigation, and content panel.
/// </summary>
public sealed class MainForm : Form
{
    private readonly ToolStrip _toolStrip;
    private readonly SplitContainer _splitContainer;
    private readonly TreeView _treeView;
    private readonly Panel _contentPanel;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly ToolStripStatusLabel _testCountLabel;
    private readonly ContextMenuStrip _workloadContextMenu;
    private readonly ContextMenuStrip _testContextMenu;
    private readonly WorkloadExplorer _explorer = new();

    private string? _workloadsDir;

    public MainForm()
    {
        Text = "Canary \u2014 Visual Regression Testing";
        MinimumSize = new Size(1024, 768);
        Size = new Size(1280, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);
        KeyPreview = true;

        // ToolStrip
        _toolStrip = BuildToolStrip();

        // TreeView (left panel)
        _treeView = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.5f),
            FullRowSelect = true,
            HideSelection = false,
            ShowLines = true,
            ShowRootLines = true,
            ItemHeight = 24,
            AllowDrop = true
        };
        _treeView.NodeMouseClick += OnTreeNodeClick;
        _treeView.DragEnter += OnTreeDragEnter;
        _treeView.DragDrop += OnTreeDragDrop;

        // Content panel (right panel)
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30)
        };

        // SplitContainer
        _splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 250,
            FixedPanel = FixedPanel.Panel1,
            BackColor = Color.FromArgb(45, 45, 48),
            SplitterWidth = 3
        };

        _splitContainer.Panel1.Controls.Add(_treeView);
        _splitContainer.Panel2.Controls.Add(_contentPanel);

        // StatusStrip
        _statusStrip = new StatusStrip
        {
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            SizingGrip = false
        };

        _statusLabel = new ToolStripStatusLabel("Ready")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _testCountLabel = new ToolStripStatusLabel("No workloads loaded")
        {
            TextAlign = ContentAlignment.MiddleRight
        };

        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_testCountLabel);

        // Context menus
        _workloadContextMenu = BuildWorkloadContextMenu();
        _testContextMenu = BuildTestContextMenu();

        // Layout
        Controls.Add(_splitContainer);
        Controls.Add(_toolStrip);
        Controls.Add(_statusStrip);

        // Keyboard shortcuts
        KeyDown += OnKeyDown;

        // Show welcome panel
        ShowWelcome();

        // Auto-detect workloads directory
        AutoDetectWorkloadsDir();
    }

    #region Keyboard Shortcuts

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O)
        {
            OnOpenFolder(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.R && !e.Shift)
        {
            OnRunTests(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F5)
        {
            OnRunTests(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.R)
        {
            OnRecord(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.A)
        {
            OnApprove(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            OnDeleteSelected();
            e.Handled = true;
        }
    }

    #endregion

    #region Drag-and-Drop

    private void OnTreeDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnTreeDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".json")
            {
                _statusLabel.Text = $"Imported test: {Path.GetFileName(file)}";
            }
            else if (ext == ".3dm")
            {
                _statusLabel.Text = $"Dropped model: {Path.GetFileName(file)} — create a test with this file";
            }
        }
    }

    #endregion

    #region Context Menus

    private ContextMenuStrip BuildWorkloadContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Run All Tests", null, (_, _) => OnRunTests(this, EventArgs.Empty));
        menu.Items.Add("Edit Config", null, (_, _) => OnEditWorkloadConfig());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open in Explorer", null, (_, _) => OnOpenInExplorer());
        return menu;
    }

    private ContextMenuStrip BuildTestContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Run Test", null, (_, _) => OnRunTests(this, EventArgs.Empty));
        menu.Items.Add("Edit Test", null, (_, _) => OnEditTest());
        menu.Items.Add("Approve", null, (_, _) => OnApprove(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete", null, (_, _) => OnDeleteSelected());
        menu.Items.Add("Open in Explorer", null, (_, _) => OnOpenInExplorer());
        return menu;
    }

    private void OnTreeNodeClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _treeView.SelectedNode = e.Node;

            if (e.Node.Tag is WorkloadExplorer.WorkloadEntry)
                _workloadContextMenu.Show(_treeView, e.Location);
            else if (e.Node.Tag is TestDefinition)
                _testContextMenu.Show(_treeView, e.Location);
        }
    }

    #endregion

    #region Toolbar

    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip
        {
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.FromArgb(220, 220, 220),
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = new DarkToolStripRenderer()
        };

        var openBtn = new ToolStripButton("Open Folder") { ToolTipText = "Open Workloads Folder (Ctrl+O)" };
        openBtn.Click += OnOpenFolder;

        var runBtn = new ToolStripButton("Run Tests") { ToolTipText = "Run Tests (Ctrl+R / F5)" };
        runBtn.Click += OnRunTests;

        var recordBtn = new ToolStripButton("Record") { ToolTipText = "Record Input (Ctrl+Shift+R)" };
        recordBtn.Click += OnRecord;

        var approveBtn = new ToolStripButton("Approve") { ToolTipText = "Approve Baselines (Ctrl+A)" };
        approveBtn.Click += OnApprove;

        var reportBtn = new ToolStripButton("View Report") { ToolTipText = "Open HTML Report" };
        reportBtn.Click += OnViewReport;

        strip.Items.Add(openBtn);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(runBtn);
        strip.Items.Add(recordBtn);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(approveBtn);
        strip.Items.Add(reportBtn);

        return strip;
    }

    #endregion

    #region Actions

    private void ShowWelcome()
    {
        _contentPanel.Controls.Clear();
        _contentPanel.Controls.Add(new WelcomePanel());
    }

    private async void OnOpenFolder(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the workloads directory",
            UseDescriptionForTitle = true
        };

        if (_workloadsDir != null)
            dialog.InitialDirectory = _workloadsDir;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            await LoadWorkloadsDirAsync(dialog.SelectedPath).ConfigureAwait(true);
    }

    private void OnRunTests(object? sender, EventArgs e)
    {
        _statusLabel.Text = "Run Tests: Select a workload from the tree first.";
    }

    private void OnRecord(object? sender, EventArgs e)
    {
        var panel = new RecordingPanel();
        _contentPanel.Controls.Clear();
        _contentPanel.Controls.Add(panel);
        _statusLabel.Text = "Recording mode — set target window and click Start.";
    }

    private void OnApprove(object? sender, EventArgs e)
    {
        _statusLabel.Text = "Approve: Select a test from the tree first.";
    }

    private void OnViewReport(object? sender, EventArgs e)
    {
        if (_workloadsDir == null) return;

        var reportPath = Directory.Exists(_workloadsDir)
            ? Directory.GetFiles(_workloadsDir, "report.html", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        if (reportPath != null)
        {
            Process.Start(new ProcessStartInfo { FileName = reportPath, UseShellExecute = true });
            _statusLabel.Text = $"Opened report: {reportPath}";
        }
        else
        {
            _statusLabel.Text = "No report found. Run tests first.";
        }
    }

    private void OnEditWorkloadConfig()
    {
        if (_treeView.SelectedNode?.Tag is WorkloadExplorer.WorkloadEntry entry)
        {
            var editor = new WorkloadEditorControl();
            editor.LoadConfig(entry.Config);
            _contentPanel.Controls.Clear();
            _contentPanel.Controls.Add(editor);
        }
    }

    private void OnEditTest()
    {
        if (_treeView.SelectedNode?.Tag is TestDefinition testDef)
        {
            var editor = new TestEditorControl();
            editor.LoadDefinition(testDef);
            _contentPanel.Controls.Clear();
            _contentPanel.Controls.Add(editor);
        }
    }

    private void OnDeleteSelected()
    {
        var node = _treeView.SelectedNode;
        if (node?.Tag is TestDefinition testDef)
        {
            var result = MessageBox.Show(
                $"Delete test '{testDef.Name}'?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                node.Remove();
                _statusLabel.Text = $"Deleted test '{testDef.Name}'.";
            }
        }
    }

    private void OnOpenInExplorer()
    {
        string? dir = null;

        if (_treeView.SelectedNode?.Tag is WorkloadExplorer.WorkloadEntry entry)
            dir = entry.Directory;
        else if (_treeView.SelectedNode?.Parent?.Tag is WorkloadExplorer.WorkloadEntry parentEntry)
            dir = parentEntry.Directory;

        if (dir != null && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    #endregion

    #region Workload Loading

    private void AutoDetectWorkloadsDir()
    {
        var exeDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, "workloads"),
            Path.Combine(exeDir, "..", "workloads"),
            Path.Combine(exeDir, "..", "..", "workloads"),
            Path.Combine(exeDir, "..", "..", "..", "workloads"),
            Path.Combine(Directory.GetCurrentDirectory(), "workloads")
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full))
            {
                _ = LoadWorkloadsDirAsync(full);
                return;
            }
        }
    }

    private async Task LoadWorkloadsDirAsync(string dir)
    {
        _workloadsDir = dir;
        _statusLabel.Text = $"Loading workloads from {dir}...";
        _treeView.Nodes.Clear();

        try
        {
            var workloads = await _explorer.LoadWorkloadsAsync(dir).ConfigureAwait(true);

            foreach (var entry in workloads)
            {
                var workloadNode = new TreeNode(entry.Config.DisplayName) { Tag = entry };

                foreach (var test in entry.Tests)
                {
                    var testNode = new TreeNode(test.Name) { Tag = test };
                    workloadNode.Nodes.Add(testNode);
                }

                _treeView.Nodes.Add(workloadNode);
            }

            _treeView.ExpandAll();

            var totalTests = workloads.Sum(w => w.Tests.Count);
            _testCountLabel.Text = $"{workloads.Count} workload(s), {totalTests} test(s)";
            _statusLabel.Text = "Ready";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    #endregion

    private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Color.FromArgb(45, 45, 48));
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(63, 63, 70));
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 3, y, e.Item.Width - 3, y);
        }
    }
}
