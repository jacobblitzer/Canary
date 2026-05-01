using System.Text.Json;
using Canary.Config;

namespace Canary.UI.Controls;

/// <summary>
/// Editor for creating and editing test definitions.
/// Organized into tabs: Basic, Checkpoints, Actions/Asserts, VLM, Penumbra.
/// </summary>
internal sealed class TestEditorControl : UserControl
{
    private readonly TabControl _tabControl;
    private readonly ErrorProvider _errorProvider;

    // Basic tab
    private readonly TextBox _nameBox;
    private readonly TextBox _workloadBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _runModeCombo;
    private readonly CheckBox _keepOpenOnFailureCheck;
    private readonly TextBox _fileBox;
    private readonly TextBox _recordingBox;
    private readonly NumericUpDown _vpWidth;
    private readonly NumericUpDown _vpHeight;
    private readonly ComboBox _projectionCombo;
    private readonly ComboBox _displayModeCombo;
    private readonly TextBox _commandsBox;

    // Checkpoints tab
    private readonly DataGridView _checkpointsGrid;

    // Actions/Asserts tab
    private readonly TextBox _actionsJsonBox;
    private readonly DataGridView _assertsGrid;
    private readonly Label _actionsValidationLabel;

    // VLM tab
    private readonly TabPage _vlmTab;
    private readonly ComboBox _vlmProviderCombo;
    private readonly TextBox _vlmModelBox;
    private readonly NumericUpDown _vlmMaxTokensBox;

    // Penumbra tab
    private readonly TabPage _penumbraTab;
    private readonly TextBox _sceneNameBox;
    private readonly NumericUpDown _sceneIndexBox;
    private readonly ComboBox _penBackendCombo;
    private readonly NumericUpDown _canvasWidthBox;
    private readonly NumericUpDown _canvasHeightBox;

    public event Action<string>? SaveRequested;

    public TestEditorControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);

        _errorProvider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };

        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f)
        };

        // ── Basic tab ────────────────────────────────────────────────────
        var basicTab = new TabPage("Basic") { BackColor = Color.FromArgb(30, 30, 30) };
        var basicLayout = CreateFormLayout();
        int row = 0;

        // Name
        basicLayout.Controls.Add(CreateLabel("Name:"), 0, row);
        _nameBox = CreateTextBox();
        basicLayout.Controls.Add(_nameBox, 1, row++);

        // Workload
        basicLayout.Controls.Add(CreateLabel("Workload:"), 0, row);
        _workloadBox = CreateTextBox();
        basicLayout.Controls.Add(_workloadBox, 1, row++);

        // Description
        basicLayout.Controls.Add(CreateLabel("Description:"), 0, row);
        _descriptionBox = CreateTextBox();
        _descriptionBox.Multiline = true;
        _descriptionBox.Height = 100;
        basicLayout.Controls.Add(_descriptionBox, 1, row++);

        // Run Mode
        basicLayout.Controls.Add(CreateLabel("Run Mode:"), 0, row);
        _runModeCombo = new ComboBox
        {
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        _runModeCombo.Items.AddRange(new object[] { "fresh", "shared" });
        _runModeCombo.SelectedIndex = 0;
        basicLayout.Controls.Add(_runModeCombo, 1, row++);

        // Keep Open on Failure
        basicLayout.Controls.Add(CreateLabel("On Failure:"), 0, row);
        _keepOpenOnFailureCheck = new CheckBox
        {
            Text = "Keep app open for inspection",
            ForeColor = Color.FromArgb(180, 180, 180),
            AutoSize = true
        };
        basicLayout.Controls.Add(_keepOpenOnFailureCheck, 1, row++);

        // Setup: File
        basicLayout.Controls.Add(CreateLabel("Setup File:"), 0, row);
        var filePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _fileBox = CreateTextBox();
        _fileBox.Width = 300;
        var browseBtn = new Button
        {
            Text = "...",
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        browseBtn.Click += OnBrowseFile;
        filePanel.Controls.Add(_fileBox);
        filePanel.Controls.Add(browseBtn);
        basicLayout.Controls.Add(filePanel, 1, row++);

        // Recording
        basicLayout.Controls.Add(CreateLabel("Recording:"), 0, row);
        var recPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _recordingBox = CreateTextBox();
        _recordingBox.Width = 300;
        var recBrowseBtn = new Button
        {
            Text = "...",
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        recBrowseBtn.Click += OnBrowseRecording;
        recPanel.Controls.Add(_recordingBox);
        recPanel.Controls.Add(recBrowseBtn);
        basicLayout.Controls.Add(recPanel, 1, row++);

        // Viewport
        basicLayout.Controls.Add(CreateLabel("Viewport:"), 0, row);
        var vpPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        vpPanel.Controls.Add(new Label { Text = "W:", ForeColor = Color.FromArgb(180, 180, 180), AutoSize = true });
        _vpWidth = CreateNumericUpDown(800, 100, 4096);
        vpPanel.Controls.Add(_vpWidth);
        vpPanel.Controls.Add(new Label { Text = "H:", ForeColor = Color.FromArgb(180, 180, 180), AutoSize = true });
        _vpHeight = CreateNumericUpDown(600, 100, 4096);
        vpPanel.Controls.Add(_vpHeight);
        _projectionCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White };
        _projectionCombo.Items.AddRange(new object[] { "Perspective", "Parallel", "Top", "Front", "Right" });
        _projectionCombo.SelectedIndex = 0;
        vpPanel.Controls.Add(_projectionCombo);
        _displayModeCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White };
        _displayModeCombo.Items.AddRange(new object[] { "Shaded", "Wireframe", "Rendered", "Ghosted", "XRay" });
        _displayModeCombo.SelectedIndex = 0;
        vpPanel.Controls.Add(_displayModeCombo);
        basicLayout.Controls.Add(vpPanel, 1, row++);

        // Commands
        basicLayout.Controls.Add(CreateLabel("Commands:"), 0, row);
        _commandsBox = CreateTextBox();
        _commandsBox.Multiline = true;
        _commandsBox.Height = 100;
        _commandsBox.ScrollBars = ScrollBars.Vertical;
        basicLayout.Controls.Add(_commandsBox, 1, row++);

        basicTab.Controls.Add(basicLayout);

        // ── Checkpoints tab ──────────────────────────────────────────────
        var cpTab = new TabPage("Checkpoints") { BackColor = Color.FromArgb(30, 30, 30) };
        _checkpointsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White,
            DefaultCellStyle = { BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White },
            ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White },
            EnableHeadersVisualStyles = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true
        };
        _checkpointsGrid.Columns.Add("Name", "Name");
        _checkpointsGrid.Columns.Add("AtTimeMs", "At Time (ms)");
        _checkpointsGrid.Columns.Add("Tolerance", "Tolerance");
        _checkpointsGrid.Columns.Add("Description", "Description");

        var modeCol = new DataGridViewComboBoxColumn
        {
            Name = "Mode",
            HeaderText = "Mode",
            Items = { "pixel-diff", "vlm" },
            FlatStyle = FlatStyle.Flat,
            DefaultCellStyle = { BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White }
        };
        _checkpointsGrid.Columns.Add(modeCol);
        _checkpointsGrid.Columns.Add("Azimuth", "Azimuth");
        _checkpointsGrid.Columns.Add("Elevation", "Elevation");
        _checkpointsGrid.Columns.Add("Distance", "Distance");

        _checkpointsGrid.Columns["Name"]!.Width = 150;
        _checkpointsGrid.Columns["AtTimeMs"]!.Width = 100;
        _checkpointsGrid.Columns["Tolerance"]!.Width = 80;
        _checkpointsGrid.Columns["Description"]!.Width = 200;
        _checkpointsGrid.Columns["Mode"]!.Width = 100;
        _checkpointsGrid.Columns["Azimuth"]!.Width = 70;
        _checkpointsGrid.Columns["Elevation"]!.Width = 70;
        _checkpointsGrid.Columns["Distance"]!.Width = 70;

        cpTab.Controls.Add(_checkpointsGrid);

        // ── Actions/Asserts tab ──────────────────────────────────────────
        var actionsTab = new TabPage("Actions / Asserts") { BackColor = Color.FromArgb(30, 30, 30) };
        var actionsSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 200,
            BackColor = Color.FromArgb(30, 30, 30)
        };

        // Actions (JSON)
        var actionsPanel = new Panel { Dock = DockStyle.Fill };
        actionsPanel.Controls.Add(new Label
        {
            Text = "Actions (JSON array — pre-checkpoint actions dispatched to agent):",
            Dock = DockStyle.Top, Height = 22,
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 9f)
        });
        _actionsJsonBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            BackColor = Color.FromArgb(25, 25, 28),
            ForeColor = Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f),
            WordWrap = false,
            AcceptsTab = true
        };
        _actionsValidationLabel = new Label
        {
            Dock = DockStyle.Bottom, Height = 20,
            ForeColor = Color.FromArgb(140, 140, 140),
            Font = new Font("Segoe UI", 8f),
            Text = "Valid JSON array expected. Example: [{\"type\": \"ActionName\", \"param\": \"value\"}]"
        };
        _actionsJsonBox.TextChanged += OnActionsJsonChanged;
        actionsPanel.Controls.Add(_actionsJsonBox);
        actionsPanel.Controls.Add(_actionsValidationLabel);

        // Asserts (grid)
        var assertsPanel = new Panel { Dock = DockStyle.Fill };
        assertsPanel.Controls.Add(new Label
        {
            Text = "Asserts (post-checkpoint assertions):",
            Dock = DockStyle.Top, Height = 22,
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 9f)
        });
        _assertsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White,
            DefaultCellStyle = { BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White },
            ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White },
            EnableHeadersVisualStyles = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true
        };

        var assertTypeCol = new DataGridViewComboBoxColumn
        {
            Name = "Type",
            HeaderText = "Type",
            Items = { "PanelEquals", "PanelContains", "PanelDoesNotContain" },
            FlatStyle = FlatStyle.Flat,
            Width = 160,
            DefaultCellStyle = { BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White }
        };
        _assertsGrid.Columns.Add(assertTypeCol);
        _assertsGrid.Columns.Add("Nickname", "Nickname");
        _assertsGrid.Columns.Add("Text", "Text");
        _assertsGrid.Columns.Add("AssertDescription", "Description");
        _assertsGrid.Columns["Nickname"]!.Width = 130;
        _assertsGrid.Columns["Text"]!.Width = 150;
        _assertsGrid.Columns["AssertDescription"]!.Width = 200;

        assertsPanel.Controls.Add(_assertsGrid);

        actionsSplit.Panel1.Controls.Add(actionsPanel);
        actionsSplit.Panel2.Controls.Add(assertsPanel);
        actionsTab.Controls.Add(actionsSplit);

        // ── VLM tab (visible when any checkpoint mode="vlm") ─────────────
        _vlmTab = new TabPage("VLM") { BackColor = Color.FromArgb(30, 30, 30) };
        var vlmLayout = CreateFormLayout();
        int vr = 0;

        vlmLayout.Controls.Add(CreateLabel("Provider:"), 0, vr);
        _vlmProviderCombo = new ComboBox
        {
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        _vlmProviderCombo.Items.AddRange(new object[] { "claude", "ollama" });
        _vlmProviderCombo.SelectedIndex = 0;
        vlmLayout.Controls.Add(_vlmProviderCombo, 1, vr++);

        vlmLayout.Controls.Add(CreateLabel("Model:"), 0, vr);
        _vlmModelBox = CreateTextBox();
        _vlmModelBox.Text = "claude-sonnet-4-20250514";
        vlmLayout.Controls.Add(_vlmModelBox, 1, vr++);

        vlmLayout.Controls.Add(CreateLabel("Max Tokens:"), 0, vr);
        _vlmMaxTokensBox = CreateNumericUpDown(1024, 1, 16384);
        vlmLayout.Controls.Add(_vlmMaxTokensBox, 1, vr++);

        vlmLayout.Controls.Add(new Label
        {
            Text = "VLM config applies to all checkpoints with mode=\"vlm\".\nSet in the test's Setup.Vlm section.",
            ForeColor = Color.FromArgb(120, 120, 120),
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0)
        }, 1, vr);

        _vlmTab.Controls.Add(vlmLayout);

        // ── Penumbra tab (visible for Penumbra workloads) ────────────────
        _penumbraTab = new TabPage("Penumbra") { BackColor = Color.FromArgb(30, 30, 30) };
        var penLayout = CreateFormLayout();
        int pr = 0;

        penLayout.Controls.Add(CreateLabel("Scene Name:"), 0, pr);
        _sceneNameBox = CreateTextBox();
        penLayout.Controls.Add(_sceneNameBox, 1, pr++);

        penLayout.Controls.Add(CreateLabel("Scene Index:"), 0, pr);
        _sceneIndexBox = CreateNumericUpDown(0, 0, 999);
        penLayout.Controls.Add(_sceneIndexBox, 1, pr++);

        penLayout.Controls.Add(CreateLabel("Backend:"), 0, pr);
        _penBackendCombo = new ComboBox
        {
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        _penBackendCombo.Items.AddRange(new object[] { "webgpu", "webgl2" });
        _penBackendCombo.SelectedIndex = 0;
        penLayout.Controls.Add(_penBackendCombo, 1, pr++);

        penLayout.Controls.Add(CreateLabel("Canvas:"), 0, pr);
        var canvasPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _canvasWidthBox = CreateNumericUpDown(960, 64, 7680);
        _canvasHeightBox = CreateNumericUpDown(540, 64, 4320);
        canvasPanel.Controls.Add(_canvasWidthBox);
        canvasPanel.Controls.Add(new Label { Text = "x", ForeColor = Color.FromArgb(140, 140, 140), AutoSize = true, Padding = new Padding(2, 4, 2, 0) });
        canvasPanel.Controls.Add(_canvasHeightBox);
        penLayout.Controls.Add(canvasPanel, 1, pr++);

        _penumbraTab.Controls.Add(penLayout);

        // ── Assemble tabs ────────────────────────────────────────────────
        _tabControl.TabPages.Add(basicTab);
        _tabControl.TabPages.Add(cpTab);
        _tabControl.TabPages.Add(actionsTab);
        // VLM and Penumbra tabs added dynamically in LoadDefinition

        // ── Save button ──────────────────────────────────────────────────
        var saveButton = new Button
        {
            Text = "Save Test Definition",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            Dock = DockStyle.Bottom,
            Height = 36,
            Font = new Font("Segoe UI", 10f)
        };
        saveButton.Click += OnSave;

        Controls.Add(_tabControl);
        Controls.Add(saveButton);
    }

    public void LoadDefinition(TestDefinition def)
    {
        _nameBox.Text = def.Name;
        _workloadBox.Text = def.Workload;
        _descriptionBox.Text = def.Description;
        SelectComboItem(_runModeCombo, def.RunMode);
        _keepOpenOnFailureCheck.Checked = def.KeepOpenOnFailure;

        _recordingBox.Text = def.Recording ?? "";

        if (def.Setup != null)
        {
            _fileBox.Text = def.Setup.File;
            if (def.Setup.Viewport != null)
            {
                _vpWidth.Value = Math.Clamp(def.Setup.Viewport.Width, 100, 4096);
                _vpHeight.Value = Math.Clamp(def.Setup.Viewport.Height, 100, 4096);
                SelectComboItem(_projectionCombo, def.Setup.Viewport.Projection);
                SelectComboItem(_displayModeCombo, def.Setup.Viewport.DisplayMode);
            }
            _commandsBox.Text = string.Join(Environment.NewLine, def.Setup.Commands);

            // VLM config
            if (def.Setup.Vlm != null)
            {
                SelectComboItem(_vlmProviderCombo, def.Setup.Vlm.Provider);
                _vlmModelBox.Text = def.Setup.Vlm.Model;
                _vlmMaxTokensBox.Value = Math.Clamp(def.Setup.Vlm.MaxTokens, 1, 16384);
            }

            // Penumbra setup
            if (def.Setup.Scene != null)
            {
                _sceneNameBox.Text = def.Setup.Scene.SceneName;
                _sceneIndexBox.Value = Math.Clamp(def.Setup.Scene.Index, 0, 999);
            }
            if (!string.IsNullOrEmpty(def.Setup.Backend))
                SelectComboItem(_penBackendCombo, def.Setup.Backend);
            if (def.Setup.Canvas != null)
            {
                _canvasWidthBox.Value = Math.Clamp(def.Setup.Canvas.Width, 64, 7680);
                _canvasHeightBox.Value = Math.Clamp(def.Setup.Canvas.Height, 64, 4320);
            }
        }

        // Checkpoints
        _checkpointsGrid.Rows.Clear();
        foreach (var cp in def.Checkpoints)
        {
            _checkpointsGrid.Rows.Add(
                cp.Name,
                cp.AtTimeMs.ToString(),
                cp.Tolerance.ToString("F3"),
                cp.Description,
                cp.Mode,
                cp.Camera?.Azimuth.ToString("F1") ?? "",
                cp.Camera?.Elevation.ToString("F1") ?? "",
                cp.Camera?.Distance.ToString("F1") ?? "");
        }

        // Actions (JSON)
        if (def.Actions.Count > 0)
        {
            _actionsJsonBox.Text = JsonSerializer.Serialize(def.Actions,
                new JsonSerializerOptions { WriteIndented = true });
        }
        else
        {
            _actionsJsonBox.Text = "[]";
        }

        // Asserts
        _assertsGrid.Rows.Clear();
        foreach (var a in def.Asserts)
        {
            _assertsGrid.Rows.Add(a.Type, a.Nickname, a.Text, a.Description);
        }

        // Show/hide optional tabs
        var hasVlm = def.Checkpoints.Any(cp => cp.Mode == "vlm") || def.Setup?.Vlm != null;
        UpdateOptionalTab(_vlmTab, hasVlm);

        var isPenumbra = !string.IsNullOrEmpty(def.Setup?.Scene?.SceneName)
            || def.Setup?.Scene?.Index > 0
            || !string.IsNullOrEmpty(def.Setup?.Backend)
            || def.Setup?.Canvas != null
            || def.Workload.Contains("penumbra", StringComparison.OrdinalIgnoreCase);
        UpdateOptionalTab(_penumbraTab, isPenumbra);

        // Listen for mode changes in checkpoints grid to show/hide VLM tab
        _checkpointsGrid.CellValueChanged += (_, _) => UpdateVlmTabVisibility();
        _checkpointsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_checkpointsGrid.IsCurrentCellDirty)
                _checkpointsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    public new bool Validate()
    {
        _errorProvider.Clear();
        bool valid = true;

        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            _errorProvider.SetError(_nameBox, "Name is required");
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(_workloadBox.Text))
        {
            _errorProvider.SetError(_workloadBox, "Workload is required");
            valid = false;
        }

        // Validate checkpoint tolerances
        foreach (DataGridViewRow row in _checkpointsGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var tolStr = row.Cells["Tolerance"]?.Value?.ToString();
            if (!string.IsNullOrEmpty(tolStr) && !double.TryParse(tolStr, out _))
            {
                _errorProvider.SetError(_checkpointsGrid, $"Invalid tolerance in row {row.Index}");
                valid = false;
            }
        }

        // Validate actions JSON
        if (!string.IsNullOrWhiteSpace(_actionsJsonBox.Text) && _actionsJsonBox.Text.Trim() != "[]")
        {
            try { JsonSerializer.Deserialize<List<TestAction>>(_actionsJsonBox.Text); }
            catch
            {
                _errorProvider.SetError(_actionsJsonBox, "Invalid JSON for actions");
                valid = false;
            }
        }

        return valid;
    }

    public TestDefinition BuildDefinition()
    {
        var commands = _commandsBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .ToList();

        var checkpoints = new List<TestCheckpoint>();
        foreach (DataGridViewRow row in _checkpointsGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var name = row.Cells["Name"]?.Value?.ToString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            double.TryParse(row.Cells["Tolerance"]?.Value?.ToString(), out var tolerance);
            long.TryParse(row.Cells["AtTimeMs"]?.Value?.ToString(), out var atTimeMs);
            var description = row.Cells["Description"]?.Value?.ToString() ?? "";
            var mode = row.Cells["Mode"]?.Value?.ToString() ?? "pixel-diff";

            CameraPosition? camera = null;
            var azStr = row.Cells["Azimuth"]?.Value?.ToString() ?? "";
            var elStr = row.Cells["Elevation"]?.Value?.ToString() ?? "";
            var distStr = row.Cells["Distance"]?.Value?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(azStr) || !string.IsNullOrWhiteSpace(elStr) || !string.IsNullOrWhiteSpace(distStr))
            {
                double.TryParse(azStr, out var az);
                double.TryParse(elStr, out var el);
                double.TryParse(distStr, out var dist);
                camera = new CameraPosition { Azimuth = az, Elevation = el, Distance = dist };
            }

            checkpoints.Add(new TestCheckpoint
            {
                Name = name,
                AtTimeMs = atTimeMs,
                Tolerance = tolerance,
                Description = description,
                Mode = mode,
                Camera = camera
            });
        }

        // Parse actions from JSON
        var actions = new List<TestAction>();
        if (!string.IsNullOrWhiteSpace(_actionsJsonBox.Text) && _actionsJsonBox.Text.Trim() != "[]")
        {
            try { actions = JsonSerializer.Deserialize<List<TestAction>>(_actionsJsonBox.Text) ?? new(); }
            catch { /* validation catches this */ }
        }

        // Build asserts from grid
        var asserts = new List<TestAssert>();
        foreach (DataGridViewRow row in _assertsGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var type = row.Cells["Type"]?.Value?.ToString();
            if (string.IsNullOrWhiteSpace(type)) continue;

            asserts.Add(new TestAssert
            {
                Type = type,
                Nickname = row.Cells["Nickname"]?.Value?.ToString() ?? "",
                Text = row.Cells["Text"]?.Value?.ToString() ?? "",
                Description = row.Cells["AssertDescription"]?.Value?.ToString() ?? ""
            });
        }

        var setup = new TestSetup
        {
            File = _fileBox.Text.Trim(),
            Viewport = new ViewportSetup
            {
                Width = (int)_vpWidth.Value,
                Height = (int)_vpHeight.Value,
                Projection = _projectionCombo.SelectedItem?.ToString() ?? "Perspective",
                DisplayMode = _displayModeCombo.SelectedItem?.ToString() ?? "Shaded"
            },
            Commands = commands
        };

        // VLM config — include if VLM tab is showing
        if (_tabControl.TabPages.Contains(_vlmTab))
        {
            setup.Vlm = new VlmConfig
            {
                Provider = _vlmProviderCombo.SelectedItem?.ToString() ?? "claude",
                Model = _vlmModelBox.Text.Trim(),
                MaxTokens = (int)_vlmMaxTokensBox.Value
            };
        }

        // Penumbra config — include if Penumbra tab is showing
        if (_tabControl.TabPages.Contains(_penumbraTab))
        {
            if (!string.IsNullOrWhiteSpace(_sceneNameBox.Text) || _sceneIndexBox.Value > 0)
            {
                setup.Scene = new SceneSetup
                {
                    SceneName = _sceneNameBox.Text.Trim(),
                    Index = (int)_sceneIndexBox.Value
                };
            }
            setup.Backend = _penBackendCombo.SelectedItem?.ToString() ?? "";
            setup.Canvas = new CanvasSetup
            {
                Width = (int)_canvasWidthBox.Value,
                Height = (int)_canvasHeightBox.Value
            };
        }

        var recording = _recordingBox.Text.Trim();

        return new TestDefinition
        {
            Name = _nameBox.Text.Trim(),
            Workload = _workloadBox.Text.Trim(),
            Description = _descriptionBox.Text.Trim(),
            RunMode = _runModeCombo.SelectedItem?.ToString() ?? "fresh",
            KeepOpenOnFailure = _keepOpenOnFailureCheck.Checked,
            Recording = recording,
            Setup = setup,
            Checkpoints = checkpoints,
            Actions = actions,
            Asserts = asserts
        };
    }

    /// <summary>
    /// Auto-saves the current editor state by firing <see cref="SaveRequested"/>.
    /// Called by the main form when navigating away from the editor so unsaved
    /// changes are not silently lost.
    /// </summary>
    public void FlushSave()
    {
        if (!Validate()) return;
        var def = BuildDefinition();
        var json = JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true });
        SaveRequested?.Invoke(json);
    }

    #region Event Handlers

    private void OnSave(object? sender, EventArgs e) => FlushSave();

    private void OnBrowseFile(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "3D Model Files|*.3dm;*.obj;*.stl;*.step;*.iges|All Files|*.*"
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            _fileBox.Text = dialog.FileName;
    }

    private void OnBrowseRecording(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Input Recordings|*.input.json|All Files|*.*"
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            _recordingBox.Text = dialog.FileName;
    }

    private void OnActionsJsonChanged(object? sender, EventArgs e)
    {
        var text = _actionsJsonBox.Text.Trim();
        if (string.IsNullOrEmpty(text) || text == "[]")
        {
            _actionsValidationLabel.Text = "Empty (no actions)";
            _actionsValidationLabel.ForeColor = Color.FromArgb(140, 140, 140);
            return;
        }
        try
        {
            var actions = JsonSerializer.Deserialize<List<TestAction>>(text);
            _actionsValidationLabel.Text = $"Valid: {actions?.Count ?? 0} action(s)";
            _actionsValidationLabel.ForeColor = Color.FromArgb(80, 200, 80);
        }
        catch (Exception ex)
        {
            _actionsValidationLabel.Text = $"Invalid JSON: {ex.Message}";
            _actionsValidationLabel.ForeColor = Color.FromArgb(220, 60, 60);
        }
    }

    private void UpdateVlmTabVisibility()
    {
        bool hasVlm = false;
        foreach (DataGridViewRow row in _checkpointsGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.Cells["Mode"]?.Value?.ToString() == "vlm")
            {
                hasVlm = true;
                break;
            }
        }
        UpdateOptionalTab(_vlmTab, hasVlm);
    }

    #endregion

    #region Helpers

    private void UpdateOptionalTab(TabPage tab, bool visible)
    {
        if (visible && !_tabControl.TabPages.Contains(tab))
            _tabControl.TabPages.Add(tab);
        else if (!visible && _tabControl.TabPages.Contains(tab))
            _tabControl.TabPages.Remove(tab);
    }

    private static TableLayoutPanel CreateFormLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoScroll = true,
            Padding = new Padding(15)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(180, 180, 180),
        Font = new Font("Segoe UI", 9.5f),
        AutoSize = true,
        Padding = new Padding(0, 6, 0, 0)
    };

    private static TextBox CreateTextBox() => new()
    {
        BackColor = Color.FromArgb(50, 50, 50),
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Width = 350
    };

    private static NumericUpDown CreateNumericUpDown(int defaultValue, int min, int max) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = defaultValue,
        Width = 70,
        BackColor = Color.FromArgb(50, 50, 50),
        ForeColor = Color.White
    };

    private static void SelectComboItem(ComboBox combo, string? value)
    {
        if (value == null) return;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    #endregion
}
