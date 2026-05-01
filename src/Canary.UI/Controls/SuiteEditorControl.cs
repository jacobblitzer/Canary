using System.Text.Json;
using Canary.Config;

namespace Canary.UI.Controls;

/// <summary>
/// Editor for creating and editing suite definitions.
/// Shows name, description, and a checked list of available tests.
/// </summary>
internal sealed class SuiteEditorControl : UserControl
{
    private readonly TextBox _nameBox;
    private readonly TextBox _descriptionBox;
    private readonly CheckBox _keepOpenCheck;
    private readonly CheckedListBox _testListBox;
    private readonly ErrorProvider _errorProvider;

    public event Action<string>? SaveRequested;

    public SuiteEditorControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);

        _errorProvider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(15)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // Name
        layout.Controls.Add(CreateLabel("Name:"), 0, row);
        _nameBox = CreateTextBox();
        layout.Controls.Add(_nameBox, 1, row++);

        // Description
        layout.Controls.Add(CreateLabel("Description:"), 0, row);
        _descriptionBox = CreateTextBox();
        _descriptionBox.Multiline = true;
        _descriptionBox.Height = 60;
        layout.Controls.Add(_descriptionBox, 1, row++);

        // Keep Open
        layout.Controls.Add(CreateLabel("After Run:"), 0, row);
        _keepOpenCheck = new CheckBox
        {
            Text = "Keep app open for inspection",
            ForeColor = Color.FromArgb(180, 180, 180),
            AutoSize = true
        };
        layout.Controls.Add(_keepOpenCheck, 1, row++);

        // Tests
        layout.Controls.Add(CreateLabel("Tests:"), 0, row);
        _testListBox = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            CheckOnClick = true,
            MinimumSize = new Size(0, 200)
        };
        layout.Controls.Add(_testListBox, 1, row++);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Save button
        var saveButton = new Button
        {
            Text = "Save Suite Definition",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            Dock = DockStyle.Bottom,
            Height = 36,
            Font = new Font("Segoe UI", 10f)
        };
        saveButton.Click += OnSave;

        Controls.Add(layout);
        Controls.Add(saveButton);
    }

    public void LoadDefinition(SuiteDefinition def, List<TestDefinition> availableTests)
    {
        _nameBox.Text = def.Name;
        _descriptionBox.Text = def.Description;
        _keepOpenCheck.Checked = def.KeepOpen;

        _testListBox.Items.Clear();
        var selected = new HashSet<string>(def.Tests, StringComparer.OrdinalIgnoreCase);
        foreach (var test in availableTests)
        {
            var isChecked = selected.Contains(test.Name);
            _testListBox.Items.Add(test.Name, isChecked);
        }
    }

    public SuiteDefinition BuildDefinition()
    {
        var tests = new List<string>();
        for (int i = 0; i < _testListBox.Items.Count; i++)
        {
            if (_testListBox.GetItemChecked(i))
                tests.Add(_testListBox.Items[i].ToString()!);
        }

        return new SuiteDefinition
        {
            Name = _nameBox.Text.Trim(),
            Description = _descriptionBox.Text.Trim(),
            KeepOpen = _keepOpenCheck.Checked,
            Tests = tests
        };
    }

    public void FlushSave()
    {
        _errorProvider.Clear();

        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            _errorProvider.SetError(_nameBox, "Name is required");
            return;
        }

        var checkedCount = _testListBox.CheckedItems.Count;
        if (checkedCount == 0)
        {
            _errorProvider.SetError(_testListBox, "Select at least one test");
            return;
        }

        var def = BuildDefinition();
        var json = JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true });
        SaveRequested?.Invoke(json);
    }

    private void OnSave(object? sender, EventArgs e) => FlushSave();

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
}
