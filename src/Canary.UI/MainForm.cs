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
    private readonly ContextMenuStrip _recordingContextMenu;
    private readonly ContextMenuStrip _suiteContextMenu;
    private readonly WorkloadExplorer _explorer = new();
    private readonly ResultsHistory _resultsHistory = new();
    private AbortHotkey? _abortHotkey;
    private TestRunnerPanel? _activeRunnerPanel;
    private ToolStripButton? _closeWorkloadBtn;

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
            AllowDrop = true,
            ShowNodeToolTips = true
        };
        _treeView.NodeMouseClick += OnTreeNodeClick;
        _treeView.NodeMouseDoubleClick += OnTreeNodeDoubleClick;
        _treeView.AfterSelect += OnTreeNodeSelected;
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
            SplitterDistance = 420,
            Panel1MinSize = 300,
            FixedPanel = FixedPanel.None,
            BackColor = Color.FromArgb(45, 45, 48),
            SplitterWidth = 8
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
        _recordingContextMenu = BuildRecordingContextMenu();
        _suiteContextMenu = BuildSuiteContextMenu();

        // Wire load warnings to status bar
        _explorer.LoadWarning += msg => BeginInvoke(() => _statusLabel.Text = $"Warning: {msg}");

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
        menu.Items.Add("New Suite", null, (_, _) => OnNewSuite());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open in Explorer", null, (_, _) => OnOpenInExplorer());
        return menu;
    }

    private ContextMenuStrip BuildTestContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Run Test", null, (_, _) => OnRunTests(this, EventArgs.Empty));
        menu.Items.Add("Edit Test", null, (_, _) => OnEditTest());
        menu.Items.Add("Duplicate Test", null, (_, _) => OnDuplicateTest());
        menu.Items.Add("Approve", null, (_, _) => OnApprove(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete", null, (_, _) => OnDeleteSelected());
        menu.Items.Add("Open in Explorer", null, (_, _) => OnOpenInExplorer());
        return menu;
    }

    private ContextMenuStrip BuildRecordingContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Create Test from Recording", null, (_, _) => OnCreateTestFromRecording());
        menu.Items.Add("View Recording", null, (_, _) => OnViewRecording());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete", null, (_, _) => OnDeleteSelected());
        menu.Items.Add("Open in Explorer", null, (_, _) => OnOpenInExplorer());
        return menu;
    }

    private ContextMenuStrip BuildSuiteContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Run Suite", null, (_, _) => OnRunTests(this, EventArgs.Empty));
        menu.Items.Add("Edit Suite", null, (_, _) => OnEditSuite());
        menu.Items.Add("Approve All", null, (_, _) => OnApproveSuite());
        menu.Items.Add(new ToolStripSeparator());
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
            else if (e.Node.Tag is SuiteDefinition)
                _suiteContextMenu.Show(_treeView, e.Location);
            else if (e.Node.Tag is TestDefinition)
                _testContextMenu.Show(_treeView, e.Location);
            else if (e.Node.Tag is string path && path.EndsWith(".input.json", StringComparison.OrdinalIgnoreCase))
                _recordingContextMenu.Show(_treeView, e.Location);
        }
    }

    private void OnTreeNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node.Tag is TestDefinition)
            OnEditTest();
        else if (e.Node.Tag is SuiteDefinition)
            OnRunTests(this, EventArgs.Empty);
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

        var deployBtn = new ToolStripButton("Deploy Agent") { ToolTipText = "Deploy Rhino agent plugin" };
        deployBtn.Click += OnDeployAgent;

        var expandBtn = new ToolStripButton("Expand All") { ToolTipText = "Expand / Collapse tree" };
        expandBtn.Click += (_, _) =>
        {
            if (expandBtn.Text == "Expand All")
            {
                _treeView.ExpandAll();
                expandBtn.Text = "Collapse";
            }
            else
            {
                _treeView.CollapseAll();
                foreach (TreeNode workloadNode in _treeView.Nodes)
                    workloadNode.Expand();
                expandBtn.Text = "Expand All";
            }
        };

        _closeWorkloadBtn = new ToolStripButton("Close Workload")
        {
            ToolTipText = "Kill workload processes (Rhino, etc.)",
            Enabled = false
        };
        _closeWorkloadBtn.Click += OnCloseWorkload;

        strip.Items.Add(openBtn);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(runBtn);
        strip.Items.Add(recordBtn);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(approveBtn);
        strip.Items.Add(reportBtn);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(deployBtn);
        strip.Items.Add(_closeWorkloadBtn);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(expandBtn);

        return strip;
    }

    #endregion

    /// <summary>
    /// Flush-saves any active <see cref="TestEditorControl"/> or
    /// <see cref="SuiteEditorControl"/> before clearing the content panel,
    /// so unsaved changes aren't silently lost on navigation.
    /// </summary>
    private void ClearContentPanel()
    {
        foreach (Control c in _contentPanel.Controls)
        {
            if (c is TestEditorControl editor)
                editor.FlushSave();
            else if (c is SuiteEditorControl suiteEditor)
                suiteEditor.FlushSave();
        }
        _contentPanel.Controls.Clear();
    }

    #region Actions

    private void ShowWelcome()
    {
        ClearContentPanel();
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

    private async void OnRunTests(object? sender, EventArgs e)
    {
        if (_workloadsDir == null)
        {
            _statusLabel.Text = "Open a workloads folder first (Ctrl+O).";
            return;
        }

        // Determine workload + tests from the selected tree node
        WorkloadExplorer.WorkloadEntry? entry = null;
        IReadOnlyList<TestDefinition> testsToRun;
        string? suiteName = null;
        bool useSharedMode = false;
        bool suiteKeepOpen = false;

        var selected = _treeView.SelectedNode;

        if (selected?.Tag is SuiteDefinition suiteDef)
        {
            // Suite node selected — resolve tests via discovery
            entry = FindWorkloadEntry(selected);
            if (entry == null) { _statusLabel.Text = "Cannot find workload for this suite."; return; }

            try
            {
                var (suite, suiteTests) = await TestDiscovery.DiscoverTestsForSuiteAsync(
                    _workloadsDir, entry.Config.Name, suiteDef.Name).ConfigureAwait(true);
                testsToRun = suiteTests;
                suiteName = suiteDef.Name;
                suiteKeepOpen = suite.KeepOpen;
                useSharedMode = suiteTests.Count > 0 && suiteTests.All(t => t.RunMode == "shared");
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Failed to resolve suite '{suiteDef.Name}': {ex.Message}";
                return;
            }
        }
        else if (selected?.Tag is TestDefinition selectedTest)
        {
            // Single test selected — reload from disk so edits are picked up
            entry = FindWorkloadEntry(selected);
            if (entry == null) { _statusLabel.Text = "Cannot find workload for this test."; return; }
            try
            {
                var testPath = Path.Combine(entry.Directory, "tests", $"{selectedTest.Name}.json");
                var freshDef = await TestDefinition.LoadAsync(testPath).ConfigureAwait(true);
                testsToRun = new[] { freshDef };
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Failed to reload test: {ex.Message}";
                return;
            }
        }
        else if (selected?.Tag is WorkloadExplorer.WorkloadEntry we)
        {
            // Workload node selected — reload all tests from disk
            entry = we;
            try
            {
                testsToRun = await TestDiscovery.DiscoverTestsAsync(
                    _workloadsDir, entry.Config.Name).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Failed to reload tests: {ex.Message}";
                return;
            }
        }
        else
        {
            _statusLabel.Text = $"Select a test, suite, or workload node. (Selected: \"{selected?.Text ?? "none"}\")";
            return;
        }

        if (testsToRun.Count == 0)
        {
            _statusLabel.Text = "No tests found for this selection.";
            return;
        }

        var testNames = testsToRun.Count == 1 ? testsToRun[0].Name : $"{testsToRun.Count} tests";
        var suiteLabel = suiteName != null ? $" (suite: {suiteName})" : "";

        var panel = new TestRunnerPanel();
        _activeRunnerPanel = panel;
        if (_closeWorkloadBtn != null) _closeWorkloadBtn.Enabled = true;
        panel.RunCompleted += suite => OnRunCompleted(suite, panel, suiteName);
        ClearContentPanel();
        _contentPanel.Controls.Add(panel);

        _statusLabel.Text = $"Running {testNames}{suiteLabel} for {entry.Config.DisplayName}...";
        await panel.RunAsync(entry.Config, testsToRun, _workloadsDir, suiteName: suiteName, useSharedMode: useSharedMode, suiteKeepOpen: suiteKeepOpen).ConfigureAwait(true);
    }

    private void OnCloseWorkload(object? sender, EventArgs e)
    {
        if (_activeRunnerPanel != null && _activeRunnerPanel.HasActiveProcesses)
        {
            _activeRunnerPanel.ForceKillProcesses();
            _statusLabel.Text = "Workload processes killed.";
        }
        else
        {
            _statusLabel.Text = "No active workload processes to close.";
        }
        if (_closeWorkloadBtn != null) _closeWorkloadBtn.Enabled = false;
    }

    private void OnRunCompleted(SuiteResult suite, TestRunnerPanel panel, string? suiteName)
    {
        // Update toolbar button based on whether processes are still alive (keepOpen)
        if (_closeWorkloadBtn != null)
            _closeWorkloadBtn.Enabled = panel.HasActiveProcesses;

        if (suite.TestResults.Count > 1 || suiteName != null)
        {
            // Multi-test or suite run — show aggregated suite view
            var viewer = new ResultsViewerControl();
            WireViewerEvents(viewer, suite.TestResults);
            viewer.LoadSuiteResult(suite, suiteName ?? "All Tests");

            ShowResultsWithLog(viewer, panel.LogText);
        }
        else if (suite.TestResults.Count == 1)
        {
            // Single test — show per-test results
            var firstResult = suite.TestResults[0];
            var viewer = new ResultsViewerControl();
            WireViewerEvents(viewer, suite.TestResults);
            viewer.LoadResult(firstResult);

            ShowResultsWithLog(viewer, panel.LogText);
        }
        _statusLabel.Text = $"Done: {suite.Passed} passed, {suite.Failed} failed, {suite.Crashed} crashed, {suite.New} new";
    }

    private void WireViewerEvents(ResultsViewerControl viewer, List<TestResult> results)
    {
        if (_workloadsDir == null) return;
        var workloadsDir = _workloadsDir;

        viewer.ApproveCheckpointRequested += (testName, cpName) =>
        {
            var result = results.FirstOrDefault(r => r.TestName == testName);
            if (result == null) return;
            try
            {
                BaselineManager.ApproveCheckpoint(workloadsDir, result.Workload, result.TestName, cpName);
                _statusLabel.Text = $"Approved baseline for '{cpName}' in '{testName}'.";
            }
            catch (Exception ex) { _statusLabel.Text = $"Approve failed: {ex.Message}"; }
        };
        viewer.RejectCheckpointRequested += (testName, cpName) =>
        {
            var result = results.FirstOrDefault(r => r.TestName == testName);
            if (result == null) return;
            try
            {
                BaselineManager.RejectCheckpoint(workloadsDir, result.Workload, result.TestName, cpName);
                _statusLabel.Text = $"Rejected checkpoint '{cpName}' in '{testName}'.";
            }
            catch (Exception ex) { _statusLabel.Text = $"Reject failed: {ex.Message}"; }
        };
        viewer.ApproveAllRequested += () =>
        {
            var total = 0;
            foreach (var r in results)
            {
                try { total += BaselineManager.ApproveTest(workloadsDir, r.Workload, r.TestName); }
                catch { /* skip tests with no candidates */ }
            }
            _statusLabel.Text = $"Approved {total} baseline(s).";
        };
    }

    private void ShowResultsWithLog(ResultsViewerControl viewer, string logText)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 400,
            BackColor = Color.FromArgb(30, 30, 30),
            Panel1MinSize = 100,
            Panel2MinSize = 60
        };

        split.Panel1.Controls.Add(viewer);

        var logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(180, 180, 180),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f),
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Text = logText
        };

        var logLabel = new Label
        {
            Text = "Run Log (Ctrl+A, Ctrl+C to copy)",
            Dock = DockStyle.Top,
            ForeColor = Color.FromArgb(120, 120, 120),
            Font = new Font("Segoe UI", 8f),
            Height = 20,
            Padding = new Padding(4, 2, 0, 0),
            BackColor = Color.FromArgb(30, 30, 30)
        };

        split.Panel2.Controls.Add(logBox);
        split.Panel2.Controls.Add(logLabel);

        ClearContentPanel();
        _contentPanel.Controls.Add(split);
    }

    private void OnRecord(object? sender, EventArgs e)
    {
        var panel = new RecordingPanel();

        // Populate workload dropdown with full configs
        var configs = new List<WorkloadConfig>();
        foreach (TreeNode node in _treeView.Nodes)
        {
            if (node.Tag is WorkloadExplorer.WorkloadEntry we)
                configs.Add(we.Config);
        }
        panel.SetWorkloads(configs);

        // Set the workloads directory so Save defaults to the right location
        if (_workloadsDir != null)
            panel.SetWorkloadsDir(_workloadsDir);

        // Refresh the tree when a recording is saved
        panel.RecordingSaved += () =>
        {
            if (_workloadsDir != null)
                _ = LoadWorkloadsDirAsync(_workloadsDir);
        };

        ClearContentPanel();
        _contentPanel.Controls.Add(panel);
        _statusLabel.Text = "Recording mode — select workload and click Launch App & Record.";
    }

    private void OnApprove(object? sender, EventArgs e)
    {
        if (_workloadsDir == null) { _statusLabel.Text = "Open a workloads folder first."; return; }

        var selected = _treeView.SelectedNode;
        var entry = FindWorkloadEntry(selected);

        if (selected?.Tag is TestDefinition testDef && entry != null)
        {
            try
            {
                var count = BaselineManager.ApproveTest(_workloadsDir, entry.Config.Name, testDef.Name);
                _statusLabel.Text = $"Approved {count} baseline(s) for '{testDef.Name}'.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Approve failed: {ex.Message}";
            }
        }
        else if (selected?.Tag is WorkloadExplorer.WorkloadEntry we)
        {
            var total = 0;
            foreach (var test in we.Tests)
            {
                try { total += BaselineManager.ApproveTest(_workloadsDir, we.Config.Name, test.Name); }
                catch { /* skip tests with no candidates */ }
            }
            _statusLabel.Text = $"Approved {total} baseline(s) for '{we.Config.DisplayName}'.";
        }
        else
        {
            _statusLabel.Text = "Select a test or workload to approve.";
        }
    }

    private void OnApproveSuite()
    {
        if (_workloadsDir == null || _treeView.SelectedNode?.Tag is not SuiteDefinition suiteDef) return;
        var entry = FindWorkloadEntry(_treeView.SelectedNode);
        if (entry == null) return;

        var total = 0;
        foreach (var testName in suiteDef.Tests)
        {
            try { total += BaselineManager.ApproveTest(_workloadsDir, entry.Config.Name, testName); }
            catch { /* skip tests with no candidates */ }
        }
        _statusLabel.Text = $"Approved {total} baseline(s) for suite '{suiteDef.Name}'.";
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

    private void OnDeployAgent(object? sender, EventArgs e)
    {
        var rhinoPluginBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "McNeel", "Rhinoceros", "8.0", "Plug-ins");

        if (!Directory.Exists(rhinoPluginBase))
        {
            _statusLabel.Text = "Rhino 8 plugin directory not found. Is Rhino 8 installed?";
            return;
        }

        // Find the built agent plugin
        var solutionDir = FindSolutionDir();
        if (solutionDir == null)
        {
            _statusLabel.Text = "Cannot locate solution directory for agent plugin.";
            return;
        }

        var agentBinDir = Path.Combine(solutionDir, "src", "Canary.Agent.Rhino", "bin", "Debug", "net48");
        if (!Directory.Exists(agentBinDir))
        {
            _statusLabel.Text = "Agent not built. Run 'dotnet build' first.";
            return;
        }

        var targetDir = Path.Combine(rhinoPluginBase, "Canary (B4E7C920-3A1F-4D00-B800-CA0A4700A001)");
        Directory.CreateDirectory(targetDir);

        var copied = 0;
        foreach (var file in Directory.GetFiles(agentBinDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            copied++;
        }

        _statusLabel.Text = $"Deployed {copied} files to {targetDir}";
    }

    private static string? FindSolutionDir()
    {
        // Try multiple starting points: BaseDirectory, CWD, and executable location
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(Environment.ProcessPath) ?? ""
        };

        foreach (var start in candidates)
        {
            var dir = start;
            for (var i = 0; i < 10; i++)
            {
                if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "Canary.sln")))
                    return dir;
                var parent = Directory.GetParent(dir!);
                if (parent == null) break;
                dir = parent.FullName;
            }
        }

        return null;
    }

    private void OnEditWorkloadConfig()
    {
        if (_treeView.SelectedNode?.Tag is WorkloadExplorer.WorkloadEntry entry)
        {
            var editor = new WorkloadEditorControl();
            editor.LoadConfig(entry.Config);

            // Load Penumbra-specific config if applicable
            var configPath = Path.Combine(entry.Directory, "workload.json");
            if (entry.Config.AgentType == "penumbra-cdp" && File.Exists(configPath))
                editor.LoadPenumbraConfig(configPath);

            // Wire save to write JSON and refresh tree
            editor.SaveRequested += json =>
            {
                try
                {
                    File.WriteAllText(configPath, json);
                    _statusLabel.Text = $"Saved workload config: {configPath}";
                    if (_workloadsDir != null)
                        _ = LoadWorkloadsDirAsync(_workloadsDir);
                }
                catch (Exception ex)
                {
                    _statusLabel.Text = $"Save failed: {ex.Message}";
                }
            };

            ClearContentPanel();
            _contentPanel.Controls.Add(editor);
        }
    }

    private void OnEditTest()
    {
        if (_treeView.SelectedNode?.Tag is TestDefinition testDef)
        {
            var entry = FindWorkloadEntry(_treeView.SelectedNode);
            var editor = new TestEditorControl();
            editor.LoadDefinition(testDef);

            // Wire save to write JSON and refresh tree
            editor.SaveRequested += json =>
            {
                if (_workloadsDir == null || entry == null) return;
                try
                {
                    var testPath = Path.Combine(entry.Directory, "tests", $"{testDef.Name}.json");
                    File.WriteAllText(testPath, json);
                    _statusLabel.Text = $"Saved test: {testPath}";
                    _ = LoadWorkloadsDirAsync(_workloadsDir);
                }
                catch (Exception ex) { _statusLabel.Text = $"Save failed: {ex.Message}"; }
            };

            ClearContentPanel();
            _contentPanel.Controls.Add(editor);
        }
    }

    private void OnCreateTestFromRecording()
    {
        if (_treeView.SelectedNode?.Tag is not string recPath || !File.Exists(recPath))
            return;

        var entry = FindWorkloadEntry(_treeView.SelectedNode);
        if (entry == null || _workloadsDir == null)
        {
            _statusLabel.Text = "Cannot determine workload for this recording.";
            return;
        }

        try
        {
            var json = File.ReadAllText(recPath);
            var recording = System.Text.Json.JsonSerializer.Deserialize<Canary.Input.InputRecording>(json);
            if (recording == null) { _statusLabel.Text = "Failed to parse recording."; return; }

            // Build a relative path from the workload dir to the recording
            var workloadDir = entry.Directory;
            var relativeRecPath = Path.GetRelativePath(workloadDir, recPath).Replace('\\', '/');

            // Derive test name from recording filename (strip .input.json)
            var testName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(recPath));

            var meta = recording.Metadata;
            var testDef = new TestDefinition
            {
                Name = testName,
                Workload = entry.Config.Name,
                Description = $"Test created from recording '{Path.GetFileName(recPath)}'",
                Recording = relativeRecPath,
                Setup = new TestSetup
                {
                    Viewport = new ViewportSetup
                    {
                        Projection = "Perspective",
                        DisplayMode = "Shaded"
                    }
                },
                Checkpoints = new List<TestCheckpoint>
                {
                    new TestCheckpoint
                    {
                        Name = "final",
                        AtTimeMs = meta.DurationMs,
                        Tolerance = 0.02,
                        Description = "Screenshot after replaying the full recording"
                    }
                }
            };

            var testJson = System.Text.Json.JsonSerializer.Serialize(testDef,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            // Save to tests directory
            var testsDir = Path.Combine(workloadDir, "tests");
            Directory.CreateDirectory(testsDir);
            var testPath = Path.Combine(testsDir, $"{testName}.json");

            if (File.Exists(testPath))
            {
                var result = MessageBox.Show(
                    $"Test '{testName}.json' already exists. Overwrite?",
                    "Confirm Overwrite",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes) return;
            }

            File.WriteAllText(testPath, testJson);
            _statusLabel.Text = $"Created test '{testName}' from recording.";

            // Refresh tree
            _ = LoadWorkloadsDirAsync(_workloadsDir);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error creating test: {ex.Message}";
        }
    }

    private void OnViewRecording()
    {
        if (_treeView.SelectedNode?.Tag is string path && File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var recording = System.Text.Json.JsonSerializer.Deserialize<Canary.Input.InputRecording>(json);
                if (recording == null) { _statusLabel.Text = "Failed to parse recording."; return; }

                var info = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(20, 20, 20),
                    ForeColor = Color.FromArgb(200, 200, 200),
                    BorderStyle = BorderStyle.None,
                    Font = new Font("Consolas", 9f),
                    ReadOnly = true,
                    WordWrap = false
                };

                var meta = recording.Metadata;
                info.AppendText($"Recording: {Path.GetFileName(path)}\n");
                info.AppendText($"Workload: {meta.Workload}\n");
                info.AppendText($"Window: {meta.WindowTitle}\n");
                info.AppendText($"Viewport: {meta.ViewportWidth} x {meta.ViewportHeight}\n");
                info.AppendText($"Duration: {meta.DurationMs / 1000.0:F1}s\n");
                info.AppendText($"Events: {recording.Events.Count}\n");
                info.AppendText($"Recorded: {meta.RecordedAt:yyyy-MM-dd HH:mm:ss}\n");
                info.AppendText($"\n--- Event Summary ---\n");

                var groups = recording.Events.GroupBy(e => e.Type.ToString())
                    .OrderByDescending(g => g.Count());
                foreach (var g in groups)
                    info.AppendText($"  {g.Key}: {g.Count()}\n");

                ClearContentPanel();
                _contentPanel.Controls.Add(info);
                _statusLabel.Text = $"Viewing recording: {Path.GetFileName(path)} ({recording.Events.Count} events)";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error reading recording: {ex.Message}";
            }
        }
    }

    private async void OnTreeNodeSelected(object? sender, TreeViewEventArgs e)
    {
        if (_workloadsDir == null || e.Node?.Tag is not TestDefinition testDef) return;

        var entry = FindWorkloadEntry(e.Node);
        if (entry == null) return;

        try
        {
            var history = await _resultsHistory.ScanAsync(_workloadsDir, entry.Config.Name).ConfigureAwait(true);
            var lastResult = history.FirstOrDefault(h => h.Result.TestName == testDef.Name);
            if (lastResult != null)
            {
                var viewer = new ResultsViewerControl();
                WireViewerEvents(viewer, new List<TestResult> { lastResult.Result });
                viewer.LoadResult(lastResult.Result);
                ClearContentPanel();
                _contentPanel.Controls.Add(viewer);
            }
        }
        catch { /* scan failed — ignore silently */ }
    }

    private void OnDuplicateTest()
    {
        if (_treeView.SelectedNode?.Tag is not TestDefinition testDef) return;
        var entry = FindWorkloadEntry(_treeView.SelectedNode);
        if (entry == null || _workloadsDir == null) return;

        // Serialize and deserialize to deep-copy the definition
        var json = System.Text.Json.JsonSerializer.Serialize(testDef,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var copy = System.Text.Json.JsonSerializer.Deserialize<TestDefinition>(json);
        if (copy == null) return;

        copy.Name = testDef.Name + "-copy";

        var editor = new TestEditorControl();
        editor.LoadDefinition(copy);

        editor.SaveRequested += saveJson =>
        {
            try
            {
                var testsDir = Path.Combine(entry.Directory, "tests");
                Directory.CreateDirectory(testsDir);
                var testPath = Path.Combine(testsDir, $"{copy.Name}.json");
                File.WriteAllText(testPath, saveJson);
                _statusLabel.Text = $"Saved duplicate test: {testPath}";
                _ = LoadWorkloadsDirAsync(_workloadsDir);
            }
            catch (Exception ex) { _statusLabel.Text = $"Save failed: {ex.Message}"; }
        };

        ClearContentPanel();
        _contentPanel.Controls.Add(editor);
        _statusLabel.Text = $"Duplicating test '{testDef.Name}' as '{copy.Name}'...";
    }

    private void OnEditSuite()
    {
        if (_treeView.SelectedNode?.Tag is not SuiteDefinition suiteDef) return;
        var entry = FindWorkloadEntry(_treeView.SelectedNode);
        if (entry == null || _workloadsDir == null) return;

        var editor = new SuiteEditorControl();
        editor.LoadDefinition(suiteDef, entry.Tests.ToList());

        editor.SaveRequested += json =>
        {
            try
            {
                var suitesDir = Path.Combine(entry.Directory, "suites");
                Directory.CreateDirectory(suitesDir);
                var suitePath = Path.Combine(suitesDir, $"{suiteDef.Name}.json");
                File.WriteAllText(suitePath, json);
                _statusLabel.Text = $"Saved suite: {suitePath}";
                _ = LoadWorkloadsDirAsync(_workloadsDir);
            }
            catch (Exception ex) { _statusLabel.Text = $"Save failed: {ex.Message}"; }
        };

        ClearContentPanel();
        _contentPanel.Controls.Add(editor);
    }

    private void OnNewSuite()
    {
        var entry = FindWorkloadEntry(_treeView.SelectedNode);
        if (entry == null || _workloadsDir == null) return;

        var newSuite = new SuiteDefinition { Name = "new-suite", Description = "" };

        var editor = new SuiteEditorControl();
        editor.LoadDefinition(newSuite, entry.Tests.ToList());

        editor.SaveRequested += json =>
        {
            try
            {
                var built = editor.BuildDefinition();
                var suitesDir = Path.Combine(entry.Directory, "suites");
                Directory.CreateDirectory(suitesDir);
                var suitePath = Path.Combine(suitesDir, $"{built.Name}.json");
                File.WriteAllText(suitePath, json);
                _statusLabel.Text = $"Created suite: {suitePath}";
                _ = LoadWorkloadsDirAsync(_workloadsDir);
            }
            catch (Exception ex) { _statusLabel.Text = $"Save failed: {ex.Message}"; }
        };

        ClearContentPanel();
        _contentPanel.Controls.Add(editor);
        _statusLabel.Text = "Creating new suite...";
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
                // Delete the test JSON file from disk
                if (_workloadsDir != null)
                {
                    var testFile = Path.Combine(_workloadsDir, testDef.Workload, "tests", $"{testDef.Name}.json");
                    if (File.Exists(testFile))
                        File.Delete(testFile);
                }
                node.Remove();
                _statusLabel.Text = $"Deleted test '{testDef.Name}'.";
            }
        }
        else if (node?.Tag is string path && File.Exists(path))
        {
            var fileName = Path.GetFileName(path);
            var result = MessageBox.Show(
                $"Delete recording '{fileName}'?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                File.Delete(path);
                node.Remove();
                _statusLabel.Text = $"Deleted recording '{fileName}'.";
            }
        }
    }

    private void OnOpenInExplorer()
    {
        var entry = FindWorkloadEntry(_treeView.SelectedNode);
        if (entry != null && Directory.Exists(entry.Directory))
            Process.Start(new ProcessStartInfo { FileName = entry.Directory, UseShellExecute = true });
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
            Path.Combine(exeDir, "..", "..", "..", "..", "..", "..", "workloads"),
            Path.Combine(Directory.GetCurrentDirectory(), "workloads"),
            @"C:\Repos\Canary\workloads"
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

            // Collect history for status coloring
            var historyByWorkload = new Dictionary<string, Dictionary<string, TestStatus>>();
            foreach (var entry in workloads)
            {
                try
                {
                    var history = await _resultsHistory.ScanAsync(dir, entry.Config.Name).ConfigureAwait(true);
                    var statusMap = new Dictionary<string, TestStatus>();
                    foreach (var h in history)
                    {
                        // Most recent result wins (history is ordered by timestamp desc)
                        if (!statusMap.ContainsKey(h.Result.TestName))
                            statusMap[h.Result.TestName] = h.Result.Status;
                    }
                    historyByWorkload[entry.Config.Name] = statusMap;
                }
                catch { historyByWorkload[entry.Config.Name] = new Dictionary<string, TestStatus>(); }
            }

            var totalSuites = 0;

            foreach (var entry in workloads)
            {
                var workloadNode = new TreeNode(entry.Config.DisplayName) { Tag = entry };
                historyByWorkload.TryGetValue(entry.Config.Name, out var statusMap);

                // Suites group
                if (entry.Suites.Count > 0)
                {
                    var suitesGroup = new TreeNode($"Suites ({entry.Suites.Count})");
                    suitesGroup.ForeColor = Color.FromArgb(150, 220, 130);
                    foreach (var suite in entry.Suites)
                    {
                        var suiteNode = new TreeNode($"{suite.Name} ({suite.Tests.Count} tests)") { Tag = suite };
                        suiteNode.ForeColor = Color.FromArgb(150, 220, 130);
                        suiteNode.ToolTipText = string.Join(", ", suite.Tests.Take(8))
                            + (suite.Tests.Count > 8 ? $" (+{suite.Tests.Count - 8} more)" : "");
                        suitesGroup.Nodes.Add(suiteNode);
                    }
                    workloadNode.Nodes.Add(suitesGroup);
                    totalSuites += entry.Suites.Count;
                }

                // All Tests group
                if (entry.Tests.Count > 0)
                {
                    var testsGroup = new TreeNode($"All Tests ({entry.Tests.Count})");
                    testsGroup.ForeColor = Color.FromArgb(100, 180, 255);
                    foreach (var test in entry.Tests)
                    {
                        var testNode = new TreeNode(test.Name) { Tag = test };
                        testNode.ForeColor = GetTestStatusColor(statusMap, test.Name);
                        testNode.ToolTipText = !string.IsNullOrEmpty(test.Description) ? test.Description : test.Name;
                        testsGroup.Nodes.Add(testNode);
                    }
                    workloadNode.Nodes.Add(testsGroup);
                }

                // Recordings group
                if (entry.Recordings.Count > 0)
                {
                    var recGroup = new TreeNode($"Recordings ({entry.Recordings.Count})");
                    recGroup.ForeColor = Color.FromArgb(220, 180, 50);
                    foreach (var recPath in entry.Recordings)
                    {
                        var recNode = new TreeNode(Path.GetFileNameWithoutExtension(
                            Path.GetFileNameWithoutExtension(recPath)));
                        recNode.Tag = recPath; // store file path as tag
                        recGroup.Nodes.Add(recNode);
                    }
                    workloadNode.Nodes.Add(recGroup);
                }

                _treeView.Nodes.Add(workloadNode);
            }

            // Expand workload nodes to show group headers, but keep groups collapsed
            foreach (TreeNode workloadNode in _treeView.Nodes)
                workloadNode.Expand();

            var totalTests = workloads.Sum(w => w.Tests.Count);
            var totalRecordings = workloads.Sum(w => w.Recordings.Count);
            _testCountLabel.Text = $"{workloads.Count} workload(s), {totalSuites} suite(s), {totalTests} test(s)";

            if (workloads.Count == 0)
                _statusLabel.Text = $"No workloads found in {dir}. Each subfolder needs a workload.json file.";
            else
                _statusLabel.Text = "Ready";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private static Color GetTestStatusColor(Dictionary<string, TestStatus>? statusMap, string testName)
    {
        if (statusMap == null || !statusMap.TryGetValue(testName, out var status))
            return Color.FromArgb(140, 140, 140); // gray — not run

        return status switch
        {
            TestStatus.Passed => Color.FromArgb(80, 200, 80),
            TestStatus.Failed => Color.FromArgb(220, 60, 60),
            TestStatus.Crashed => Color.FromArgb(180, 80, 220),
            TestStatus.New => Color.FromArgb(220, 180, 50),
            _ => Color.FromArgb(140, 140, 140)
        };
    }

    #endregion

    /// <summary>Walk up from a tree node to find the ancestor with a WorkloadEntry tag.</summary>
    private static WorkloadExplorer.WorkloadEntry? FindWorkloadEntry(TreeNode? node)
    {
        while (node != null)
        {
            if (node.Tag is WorkloadExplorer.WorkloadEntry we) return we;
            node = node.Parent;
        }
        return null;
    }

    /// <summary>Register the global Pause abort hotkey.</summary>
    internal AbortHotkey RegisterAbortHotkey()
    {
        _abortHotkey?.Dispose();
        _abortHotkey = new AbortHotkey(this);
        _abortHotkey.Register();
        return _abortHotkey;
    }

    /// <summary>Unregister the global Pause abort hotkey.</summary>
    public void UnregisterAbortHotkey()
    {
        _abortHotkey?.Dispose();
        _abortHotkey = null;
    }

    protected override void WndProc(ref Message m)
    {
        if (_abortHotkey?.ProcessMessage(ref m) == true)
            return;
        base.WndProc(ref m);
    }

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
