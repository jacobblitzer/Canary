using System.Text.Json;
using Canary.Config;

namespace Canary.UI.Controls;

/// <summary>
/// Editor for creating and editing test definitions.
/// </summary>
internal sealed class TestEditorControl : UserControl
{
    private readonly TextBox _nameBox;
    private readonly TextBox _workloadBox;
    private readonly TextBox _descriptionBox;
    private readonly TextBox _fileBox;
    private readonly NumericUpDown _vpWidth;
    private readonly NumericUpDown _vpHeight;
    private readonly ComboBox _projectionCombo;
    private readonly ComboBox _displayModeCombo;
    private readonly TextBox _commandsBox;
    private readonly DataGridView _checkpointsGrid;
    private readonly ErrorProvider _errorProvider;
    private readonly Button _saveButton;

    public event Action<string>? SaveRequested;

    public TestEditorControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);
        AutoScroll = true;

        _errorProvider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(15)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // Name
        layout.Controls.Add(CreateLabel("Name:"), 0, row);
        _nameBox = CreateTextBox();
        layout.Controls.Add(_nameBox, 1, row++);

        // Workload
        layout.Controls.Add(CreateLabel("Workload:"), 0, row);
        _workloadBox = CreateTextBox();
        layout.Controls.Add(_workloadBox, 1, row++);

        // Description
        layout.Controls.Add(CreateLabel("Description:"), 0, row);
        _descriptionBox = CreateTextBox();
        _descriptionBox.Multiline = true;
        _descriptionBox.Height = 60;
        layout.Controls.Add(_descriptionBox, 1, row++);

        // Setup: File
        layout.Controls.Add(CreateLabel("Setup File:"), 0, row);
        var filePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _fileBox = CreateTextBox();
        _fileBox.Width = 300;
        var browseBtn = new Button
        {
            Text = "...",
            Size = new Size(30, 23),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        browseBtn.Click += OnBrowseFile;
        filePanel.Controls.Add(_fileBox);
        filePanel.Controls.Add(browseBtn);
        layout.Controls.Add(filePanel, 1, row++);

        // Viewport
        layout.Controls.Add(CreateLabel("Viewport:"), 0, row);
        var vpPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        vpPanel.Controls.Add(new Label { Text = "W:", ForeColor = Color.FromArgb(180, 180, 180), AutoSize = true });
        _vpWidth = new NumericUpDown { Value = 800, Minimum = 100, Maximum = 4096, Width = 70, BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White };
        vpPanel.Controls.Add(_vpWidth);
        vpPanel.Controls.Add(new Label { Text = "H:", ForeColor = Color.FromArgb(180, 180, 180), AutoSize = true });
        _vpHeight = new NumericUpDown { Value = 600, Minimum = 100, Maximum = 4096, Width = 70, BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White };
        vpPanel.Controls.Add(_vpHeight);
        _projectionCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White };
        _projectionCombo.Items.AddRange(new object[] { "Perspective", "Parallel", "Top", "Front", "Right" });
        _projectionCombo.SelectedIndex = 0;
        vpPanel.Controls.Add(_projectionCombo);
        _displayModeCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White };
        _displayModeCombo.Items.AddRange(new object[] { "Shaded", "Wireframe", "Rendered", "Ghosted", "XRay" });
        _displayModeCombo.SelectedIndex = 0;
        vpPanel.Controls.Add(_displayModeCombo);
        layout.Controls.Add(vpPanel, 1, row++);

        // Commands
        layout.Controls.Add(CreateLabel("Commands:"), 0, row);
        _commandsBox = CreateTextBox();
        _commandsBox.Multiline = true;
        _commandsBox.Height = 60;
        _commandsBox.ScrollBars = ScrollBars.Vertical;
        layout.Controls.Add(_commandsBox, 1, row++);

        // Checkpoints grid
        layout.Controls.Add(CreateLabel("Checkpoints:"), 0, row);
        _checkpointsGrid = new DataGridView
        {
            Width = 500,
            Height = 150,
            BackgroundColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White,
            DefaultCellStyle = { BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White },
            ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White },
            EnableHeadersVisualStyles = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true
        };
        _checkpointsGrid.Columns.Add("Name", "Name");
        _checkpointsGrid.Columns.Add("Tolerance", "Tolerance");
        _checkpointsGrid.Columns["Name"]!.Width = 250;
        _checkpointsGrid.Columns["Tolerance"]!.Width = 100;
        layout.Controls.Add(_checkpointsGrid, 1, row++);

        // Save button
        _saveButton = new Button
        {
            Text = "Save Test Definition",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            Size = new Size(180, 35),
            Font = new Font("Segoe UI", 10f)
        };
        _saveButton.Click += OnSave;
        layout.Controls.Add(_saveButton, 1, row);

        Controls.Add(layout);
    }

    public void LoadDefinition(TestDefinition def)
    {
        _nameBox.Text = def.Name;
        _workloadBox.Text = def.Workload;
        _descriptionBox.Text = def.Description;

        if (def.Setup != null)
        {
            _fileBox.Text = def.Setup.File;
            if (def.Setup.Viewport != null)
            {
                _vpWidth.Value = def.Setup.Viewport.Width;
                _vpHeight.Value = def.Setup.Viewport.Height;
                SelectComboItem(_projectionCombo, def.Setup.Viewport.Projection);
                SelectComboItem(_displayModeCombo, def.Setup.Viewport.DisplayMode);
            }
            _commandsBox.Text = string.Join(Environment.NewLine, def.Setup.Commands);
        }

        _checkpointsGrid.Rows.Clear();
        foreach (var cp in def.Checkpoints)
        {
            _checkpointsGrid.Rows.Add(cp.Name, cp.Tolerance.ToString("F3"));
        }
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
            if (tolStr != null && !double.TryParse(tolStr, out var tol))
            {
                _errorProvider.SetError(_checkpointsGrid, $"Invalid tolerance in row {row.Index}");
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

            checkpoints.Add(new TestCheckpoint
            {
                Name = name,
                Tolerance = tolerance
            });
        }

        return new TestDefinition
        {
            Name = _nameBox.Text.Trim(),
            Workload = _workloadBox.Text.Trim(),
            Description = _descriptionBox.Text.Trim(),
            Setup = new TestSetup
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
            },
            Checkpoints = checkpoints
        };
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (!Validate()) return;

        var def = BuildDefinition();
        var json = JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true });
        SaveRequested?.Invoke(json);
    }

    private void OnBrowseFile(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "3D Model Files|*.3dm;*.obj;*.stl;*.step;*.iges|All Files|*.*"
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            _fileBox.Text = dialog.FileName;
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

    private static void SelectComboItem(ComboBox combo, string value)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }
}
