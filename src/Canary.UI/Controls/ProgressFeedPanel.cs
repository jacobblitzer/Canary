using Canary;
using Canary.Orchestration;
using System.Drawing.Drawing2D;

namespace Canary.UI.Controls;

/// <summary>
/// Live progress feed for a test run. One card per (test, checkpoint) appears
/// as the suite runs: thumbnail of the captured screenshot, the VLM prompt
/// (editable expectation), and the verdict + reasoning once VLM completes.
/// </summary>
internal sealed class ProgressFeedPanel : UserControl, ITestProgressEvents
{
    private readonly FlowLayoutPanel _flow;
    private readonly Label _header;
    private readonly Dictionary<string, CheckpointCard> _cards = new(StringComparer.Ordinal);

    public ProgressFeedPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(24, 24, 24);

        _header = new Label
        {
            Dock = DockStyle.Top,
            Text = "Progress feed — waiting…",
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Padding = new Padding(10, 8, 10, 6),
            AutoSize = false,
            Height = 32,
            BackColor = Color.FromArgb(36, 36, 36),
        };

        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.FromArgb(24, 24, 24),
            Padding = new Padding(8),
        };

        Controls.Add(_flow);
        Controls.Add(_header);
    }

    private static string Key(string testName, string checkpointName) => $"{testName}::{checkpointName}";

    private CheckpointCard EnsureCard(string testName, string checkpointName)
    {
        var k = Key(testName, checkpointName);
        if (_cards.TryGetValue(k, out var existing)) return existing;
        var card = new CheckpointCard(testName, checkpointName);
        _cards[k] = card;
        _flow.Controls.Add(card);
        return card;
    }

    public void OnTestStarted(string testName)
    {
        BeginInvoke(new Action(() =>
        {
            _header.Text = $"Running: {testName}";
        }));
    }

    public void OnCheckpointStarted(string testName, string checkpointName, string? vlmDescription)
    {
        BeginInvoke(new Action(() =>
        {
            var card = EnsureCard(testName, checkpointName);
            card.SetPrompt(vlmDescription ?? "(pixel-diff)");
            card.SetStatus(CheckpointCardStatus.Pending, "starting…");
            _flow.ScrollControlIntoView(card);
        }));
    }

    public void OnScreenshotCaptured(string testName, string checkpointName, string imagePath)
    {
        BeginInvoke(new Action(() =>
        {
            var card = EnsureCard(testName, checkpointName);
            card.SetScreenshot(imagePath);
            card.SetStatus(CheckpointCardStatus.Evaluating, "screenshot captured");
        }));
    }

    public void OnVlmEvaluating(string testName, string checkpointName, string prompt)
    {
        BeginInvoke(new Action(() =>
        {
            var card = EnsureCard(testName, checkpointName);
            card.SetPrompt(prompt);
            card.SetStatus(CheckpointCardStatus.Evaluating, "VLM evaluating…");
        }));
    }

    public void OnVlmVerdict(string testName, string checkpointName, bool passed, double confidence, string reasoning)
    {
        BeginInvoke(new Action(() =>
        {
            var card = EnsureCard(testName, checkpointName);
            card.SetStatus(
                passed ? CheckpointCardStatus.Pass : CheckpointCardStatus.Fail,
                $"conf {confidence:F2}");
            card.SetReasoning(reasoning);
        }));
    }

    public void OnTestCompleted(string testName, TestStatus status, double durationSeconds)
    {
        BeginInvoke(new Action(() =>
        {
            _header.Text = $"Last: {testName} → {status} ({durationSeconds:F1}s)";
        }));
    }

    public void Clear()
    {
        _flow.Controls.Clear();
        _cards.Clear();
        _header.Text = "Progress feed — waiting…";
    }
}

internal enum CheckpointCardStatus { Pending, Evaluating, Pass, Fail, Crash }

internal sealed class CheckpointCard : UserControl
{
    private const int CardWidth = 540;
    private const int ThumbSize = 120;

    private readonly Label _title;
    private readonly Label _status;
    private readonly PictureBox _thumb;
    private readonly Label _prompt;
    private readonly Label _reasoning;
    private string? _imagePath;

    public CheckpointCard(string testName, string checkpointName)
    {
        Width = CardWidth;
        Height = ThumbSize + 90;
        Margin = new Padding(0, 0, 0, 8);
        BackColor = Color.FromArgb(32, 32, 32);
        Padding = new Padding(8);

        _title = new Label
        {
            Text = $"{testName}  ›  {checkpointName}",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 6),
        };

        _status = new Label
        {
            Text = "pending",
            ForeColor = Color.FromArgb(180, 180, 180),
            BackColor = Color.FromArgb(60, 60, 60),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Width = 90,
            Height = 18,
            Location = new Point(CardWidth - 100, 8),
        };

        _thumb = new PictureBox
        {
            Width = ThumbSize,
            Height = (int)(ThumbSize * 9.0 / 16),
            Location = new Point(8, 28),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(20, 20, 20),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
        };
        _thumb.Click += OnThumbClick;

        _prompt = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(176, 188, 197),
            Font = new Font("Segoe UI", 8.25f),
            Location = new Point(ThumbSize + 16, 28),
            Width = CardWidth - ThumbSize - 24,
            Height = (int)(ThumbSize * 9.0 / 16),
            AutoSize = false,
            AutoEllipsis = true,
        };

        _reasoning = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            Location = new Point(8, 28 + (int)(ThumbSize * 9.0 / 16) + 6),
            Width = CardWidth - 16,
            Height = 30,
            AutoSize = false,
            AutoEllipsis = true,
        };

        Controls.Add(_title);
        Controls.Add(_status);
        Controls.Add(_thumb);
        Controls.Add(_prompt);
        Controls.Add(_reasoning);

        Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(60, 60, 60), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
    }

    public void SetStatus(CheckpointCardStatus s, string text)
    {
        var (bg, fg) = s switch
        {
            CheckpointCardStatus.Pending    => (Color.FromArgb(60, 60, 60), Color.FromArgb(180, 180, 180)),
            CheckpointCardStatus.Evaluating => (Color.FromArgb(120, 100, 30), Color.FromArgb(255, 230, 150)),
            CheckpointCardStatus.Pass       => (Color.FromArgb(27, 94, 32), Color.FromArgb(165, 214, 167)),
            CheckpointCardStatus.Fail       => (Color.FromArgb(183, 28, 28), Color.FromArgb(239, 154, 154)),
            CheckpointCardStatus.Crash      => (Color.FromArgb(74, 20, 140), Color.FromArgb(206, 147, 216)),
            _                                => (Color.FromArgb(60, 60, 60), Color.FromArgb(180, 180, 180)),
        };
        _status.BackColor = bg;
        _status.ForeColor = fg;
        _status.Text = text;
    }

    public void SetPrompt(string prompt)
    {
        _prompt.Text = prompt;
    }

    public void SetReasoning(string reasoning)
    {
        _reasoning.Text = reasoning;
    }

    public void SetScreenshot(string imagePath)
    {
        _imagePath = imagePath;
        if (!File.Exists(imagePath)) return;
        try
        {
            using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var img = Image.FromStream(fs);
            _thumb.Image?.Dispose();
            _thumb.Image = img;
        }
        catch
        {
            // ignore read races / partial writes
        }
    }

    private void OnThumbClick(object? sender, EventArgs e)
    {
        if (_imagePath == null || !File.Exists(_imagePath)) return;
        try { new ImageViewerForm(_imagePath, new[] { _imagePath }).Show(this); }
        catch { /* viewer form unavailable */ }
    }
}
