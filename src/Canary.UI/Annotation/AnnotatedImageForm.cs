using System.Windows.Forms.Integration;
using Canary.Feedback;

// AnnotatedImageForm is a WinForms Form hosting a WPF UserControl via
// ElementHost. Default identifiers resolve to System.Drawing / WinForms
// (the host's natural namespace); WPF brushes for the color picker are
// fully qualified via the WPF aliases below.
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Canary.UI.Annotation;

// Phase 5 / design §C5 — WinForms host for the WPF annotation canvas.
// Embeds AnnotationCanvas via ElementHost so the existing dark-themed
// WinForms shell can launch this surface without a full reshell. Save
// flow produces source.png + annotated.png + annotations.json + the
// markdown frontmatter item via FeedbackInboxWriter.
public sealed class AnnotatedImageForm : Form
{
    private readonly AnnotationCanvas _canvas;
    private readonly ToolStrip _toolbar;
    private readonly TextBox _bodyBox;
    private readonly TextBox _titleBox;
    private readonly Label _statusLabel;

    private readonly string _sourceImagePath;
    private readonly string? _runRef;
    private readonly string? _checkpointRef;
    private readonly string _inboxRoot;

    public AnnotatedImageForm(string sourceImagePath, string inboxRoot, string? runRef = null, string? checkpointRef = null)
    {
        _sourceImagePath = sourceImagePath;
        _inboxRoot = inboxRoot;
        _runRef = runRef;
        _checkpointRef = checkpointRef;

        Text = "Canary — Annotate";
        Size = new Size(1280, 900);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);

        _toolbar = BuildToolbar();

        _canvas = new AnnotationCanvas();
        try { _canvas.LoadImage(sourceImagePath); }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load image: {ex.Message}", "Canary", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30),
            Child = _canvas,
        };

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 180,
            BackColor = Color.FromArgb(45, 45, 48),
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8),
        };
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label { Text = "Title:", AutoSize = true, ForeColor = Color.FromArgb(220, 220, 220) };
        _titleBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle,
        };
        var bodyLabel = new Label { Text = "Notes:", AutoSize = true, ForeColor = Color.FromArgb(220, 220, 220) };
        _bodyBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle,
        };

        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        var saveBtn = new Button { Text = "Save to inbox", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 120, 60), ForeColor = Color.White };
        var cancelBtn = new Button { Text = "Cancel", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
        _statusLabel = new Label { AutoSize = true, ForeColor = Color.FromArgb(150, 220, 130), Padding = new Padding(8, 6, 0, 0) };
        saveBtn.Click += OnSave;
        cancelBtn.Click += (_, _) => Close();
        buttonRow.Controls.Add(saveBtn);
        buttonRow.Controls.Add(cancelBtn);
        buttonRow.Controls.Add(_statusLabel);

        bottom.Controls.Add(titleLabel, 0, 0);
        bottom.Controls.Add(_titleBox, 0, 1);
        bottom.Controls.Add(_bodyBox, 0, 2);
        bottom.Controls.Add(buttonRow, 0, 3);

        Controls.Add(host);
        Controls.Add(bottom);
        Controls.Add(_toolbar);
    }

    private ToolStrip BuildToolbar()
    {
        var t = new ToolStrip
        {
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.FromArgb(220, 220, 220),
            GripStyle = ToolStripGripStyle.Hidden,
        };

        ToolStripButton ToolBtn(string text, AnnotationCanvas.ToolMode mode)
        {
            var b = new ToolStripButton(text) { CheckOnClick = true };
            b.Click += (_, _) =>
            {
                _canvas.Tool = mode;
                foreach (var item in t.Items)
                {
                    if (item is ToolStripButton other && other != b && other.CheckOnClick) other.Checked = false;
                }
                b.Checked = true;
            };
            return b;
        }

        var pointer = ToolBtn("Pointer", AnnotationCanvas.ToolMode.Pointer);
        var rect = ToolBtn("Rectangle", AnnotationCanvas.ToolMode.Rectangle);
        var freehand = ToolBtn("Freehand", AnnotationCanvas.ToolMode.Freehand);
        var text = ToolBtn("Text", AnnotationCanvas.ToolMode.Text);
        rect.Checked = true;

        t.Items.Add(pointer);
        t.Items.Add(rect);
        t.Items.Add(freehand);
        t.Items.Add(text);
        t.Items.Add(new ToolStripSeparator());

        void AddColorBtn(string label, WpfBrush brush)
        {
            var b = new ToolStripButton(label);
            b.Click += (_, _) => _canvas.StrokeBrush = brush;
            t.Items.Add(b);
        }
        AddColorBtn("Red", WpfBrushes.Red);
        AddColorBtn("Yellow", new WpfSolidColorBrush(WpfColor.FromRgb(0xFB, 0xBF, 0x24)));
        AddColorBtn("Green", new WpfSolidColorBrush(WpfColor.FromRgb(0x10, 0xB9, 0x81)));

        t.Items.Add(new ToolStripSeparator());
        var clear = new ToolStripButton("Clear");
        clear.Click += (_, _) => _canvas.Clear();
        t.Items.Add(clear);

        return t;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        try
        {
            var title = string.IsNullOrWhiteSpace(_titleBox.Text)
                ? Path.GetFileNameWithoutExtension(_sourceImagePath)
                : _titleBox.Text;

            var writer = new FeedbackInboxWriter(_inboxRoot);
            var existingSlugs = writer.ExistingSlugs();
            var slug = FeedbackSlugGenerator.Generate(DateTime.UtcNow, title, existingSlugs);

            var sourcePng = File.ReadAllBytes(_sourceImagePath);
            var annotatedPng = _canvas.RenderAnnotatedPng();
            var json = _canvas.SerializeAnnotationsJson("source.png");

            var item = new FeedbackItem
            {
                Slug = slug,
                Date = DateTime.UtcNow,
                Status = "open",
                Project = "canary",
                RunRef = _runRef,
                CheckpointRef = _checkpointRef,
                ImageRef = "annotated.png",
                Title = title,
                Body = string.IsNullOrWhiteSpace(_bodyBox.Text) ? "(no notes)" : _bodyBox.Text,
            };

            writer.Write(item, sourcePng, annotatedPng, json);
            _statusLabel.Text = $"Saved: docs/feedback/inbox/{slug}.md";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}", "Canary", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
