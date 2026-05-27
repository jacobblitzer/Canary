using System.Diagnostics;
using Canary.Config;
using Canary.Harness.Session;
using Canary.Session;
using Canary.UI.Annotation;
using Canary.UI.Hotkeys;

namespace Canary.UI.Panels;

public sealed class SessionsLiveSubPanel : UserControl
{
    private readonly ComboBox _workloadPicker;
    private readonly Button _startBtn;
    private readonly Button _captureBtn;
    private readonly Button _annotateBtn;
    private readonly Button _noteBtn;
    private readonly Button _endBtn;
    private readonly Label _statusLabel;
    private readonly Label _hotkeyHintLabel;
    private readonly FlowLayoutPanel _thumbnailStrip;
    private readonly Panel _stripContainer;

    private string? _workloadsDir;
    private SupervisedSession? _session;
    private SessionHotkeyHook? _hotkeyHook;
    private bool _capturing;

    public SessionsLiveSubPanel()
    {
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = Color.FromArgb(45, 45, 48),
            ColumnCount = 6,
            RowCount = 2,
            Padding = new Padding(8, 6, 8, 6),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _workloadPicker = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
        };

        _startBtn = MakeButton("Start session", Color.FromArgb(60, 120, 60));
        _startBtn.Click += async (_, _) => await OnStartAsync().ConfigureAwait(true);

        _captureBtn = MakeButton("Capture (Ctrl+Shift+C)", Color.FromArgb(60, 100, 160));
        _captureBtn.Click += async (_, _) => await DoCaptureAsync(openInViewer: false, withNote: false).ConfigureAwait(true);
        _captureBtn.Enabled = false;

        _annotateBtn = MakeButton("Capture + Annotate (Ctrl+Shift+A)", Color.FromArgb(80, 120, 180));
        _annotateBtn.Click += async (_, _) => await DoCaptureAsync(openInViewer: false, withNote: false, annotate: true).ConfigureAwait(true);
        _annotateBtn.Enabled = false;

        _noteBtn = MakeButton("Capture with note", Color.FromArgb(120, 100, 60));
        _noteBtn.Click += async (_, _) => await DoCaptureAsync(openInViewer: false, withNote: true).ConfigureAwait(true);
        _noteBtn.Enabled = false;

        _endBtn = MakeButton("End session", Color.FromArgb(160, 60, 60));
        _endBtn.Click += async (_, _) => await OnEndAsync().ConfigureAwait(true);
        _endBtn.Enabled = false;

        top.Controls.Add(_workloadPicker, 0, 0);
        top.Controls.Add(_startBtn, 1, 0);
        top.Controls.Add(_captureBtn, 2, 0);
        top.Controls.Add(_annotateBtn, 3, 0);
        top.Controls.Add(_noteBtn, 4, 0);
        top.Controls.Add(_endBtn, 5, 0);

        _statusLabel = new Label
        {
            Text = "Pick a workload, then Start session. The target app opens visibly; capture on demand.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(180, 180, 180),
            AutoEllipsis = true,
        };
        top.SetColumnSpan(_statusLabel, 6);
        top.Controls.Add(_statusLabel, 0, 1);

        _hotkeyHintLabel = new Label
        {
            Text = "Hotkeys (while session is live, anywhere): Ctrl+Shift+C = capture · Ctrl+Shift+A = capture + annotate",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.FromArgb(120, 120, 120),
            BackColor = Color.FromArgb(30, 30, 30),
            Padding = new Padding(8, 4, 0, 0),
            Font = new Font("Segoe UI", 8.5f),
        };

        _stripContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            AutoScroll = true,
        };

        var stripLabel = new Label
        {
            Text = "Captures in this session:",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.FromArgb(140, 140, 140),
            Padding = new Padding(8, 4, 0, 0),
            BackColor = Color.FromArgb(30, 30, 30),
            Font = new Font("Segoe UI", 8.5f),
        };

        _thumbnailStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.FromArgb(20, 20, 20),
            Padding = new Padding(8),
            AutoScroll = true,
        };
        _stripContainer.Controls.Add(_thumbnailStrip);

        Controls.Add(_stripContainer);
        Controls.Add(stripLabel);
        Controls.Add(_hotkeyHintLabel);
        Controls.Add(top);
    }

    private static Button MakeButton(string text, Color back) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = back,
        ForeColor = Color.White,
        Margin = new Padding(4, 0, 4, 0),
    };

    public void SetWorkloads(string? workloadsDir, IEnumerable<WorkloadConfig> workloads)
    {
        _workloadsDir = workloadsDir;
        var prev = _workloadPicker.SelectedItem as WorkloadItem;
        _workloadPicker.Items.Clear();
        foreach (var w in workloads)
        {
            if (w.AgentType is "qualia-cdp" or "penumbra-cdp")
                _workloadPicker.Items.Add(new WorkloadItem(w.Name, w.DisplayName));
        }
        if (_workloadPicker.Items.Count > 0)
        {
            var match = prev != null ? _workloadPicker.Items.OfType<WorkloadItem>().FirstOrDefault(i => i.Name == prev.Name) : null;
            _workloadPicker.SelectedItem = match ?? _workloadPicker.Items[0];
        }
    }

    public bool ProcessHotkeyMessage(ref Message m) => _hotkeyHook?.ProcessMessage(ref m) ?? false;

    private async Task OnStartAsync()
    {
        if (_session != null) return;
        if (_workloadsDir == null || _workloadPicker.SelectedItem is not WorkloadItem item)
        {
            _statusLabel.Text = "Pick a workload first.";
            return;
        }

        var configPath = Path.Combine(_workloadsDir, item.Name, "workload.json");
        if (!File.Exists(configPath))
        {
            _statusLabel.Text = $"workload.json not found: {configPath}";
            return;
        }

        SetButtonsForState(starting: true);
        _statusLabel.Text = $"Starting Vite + Chrome for '{item.Name}'... this can take up to 30s.";

        try
        {
            _session = await SupervisedSession.StartAsync(
                _workloadsDir, item.Name, configPath, new SessionAgentFactory()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Failed to start session: {ex.Message}";
            SetButtonsForState(starting: false);
            return;
        }

        SetButtonsForState(armed: true);
        _statusLabel.Text = $"Session armed · {_session.SessionId} · {_session.Url ?? "(no url)"} · dir: {_session.Directory}";

        var hostForm = FindForm();
        if (hostForm != null)
        {
            _hotkeyHook = new SessionHotkeyHook(hostForm);
            _hotkeyHook.CaptureRequested += () => BeginInvoke(new Action(async () => await DoCaptureAsync(openInViewer: false, withNote: false).ConfigureAwait(true)));
            _hotkeyHook.AnnotateRequested += () => BeginInvoke(new Action(async () => await DoCaptureAsync(openInViewer: false, withNote: false, annotate: true).ConfigureAwait(true)));
            _hotkeyHook.Register();
        }
    }

    private async Task DoCaptureAsync(bool openInViewer, bool withNote, bool annotate = false)
    {
        if (_session == null || _capturing) return;
        _capturing = true;
        try
        {
            string? title = null;
            string? body = null;
            if (withNote)
            {
                using var dlg = new NotePromptDialog();
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                title = dlg.NoteTitle;
                body = dlg.NoteBody;
            }

            CaptureResult result;
            try
            {
                result = await _session.CaptureAsync(title, body).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Capture failed: {ex.Message}";
                return;
            }

            AddThumbnail(result);
            _statusLabel.Text = $"Captured #{result.Sequence} → {Path.GetFileName(result.PngPath)} (total: {_session.Captures.Count})";

            if (openInViewer)
            {
                try { Process.Start(new ProcessStartInfo { FileName = result.PngPath, UseShellExecute = true }); }
                catch { /* swallow */ }
            }

            if (annotate)
            {
                OpenAnnotate(result);
            }
        }
        finally
        {
            _capturing = false;
        }
    }

    private void OpenAnnotate(CaptureResult result)
    {
        if (_session == null) return;
        var session = _session;
        var captureDir = SessionPaths.CapturesDir(session.Directory);
        var captured = session.Captures.FirstOrDefault(c => c.Sequence == result.Sequence);
        if (captured == null) return;

        var annotatedFile = SessionPaths.CaptureAnnotatedPngFile(result.Sequence, captured.CapturedAtUtc, captured.Slug);
        var annotationsFile = SessionPaths.CaptureAnnotationsJsonFile(result.Sequence, captured.CapturedAtUtc, captured.Slug);

        using var form = new AnnotatedImageForm(
            result.PngPath,
            (sourcePng, annotatedPng, annotationsJson) =>
            {
                File.WriteAllBytes(Path.Combine(captureDir, annotatedFile), annotatedPng);
                File.WriteAllText(Path.Combine(captureDir, annotationsFile), annotationsJson);
                session.AttachAnnotation(result.Sequence, annotatedFile, annotationsFile);
                RefreshThumbnail(result.Sequence);
            });
        form.ShowDialog(this);
    }

    private void AddThumbnail(CaptureResult result)
    {
        try
        {
            var pic = new PictureBox
            {
                Width = 160,
                Height = 100,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(37, 37, 38),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(4),
                Tag = result.Sequence,
                Cursor = Cursors.Hand,
            };
            using (var fs = File.OpenRead(result.PngPath))
                pic.Image = Image.FromStream(fs);
            var label = new Label
            {
                Text = $"#{result.Sequence}",
                Dock = DockStyle.Bottom,
                Height = 18,
                ForeColor = Color.FromArgb(180, 180, 180),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8f),
            };
            pic.Controls.Add(label);
            pic.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = result.PngPath, UseShellExecute = true }); }
                catch { /* swallow */ }
            };
            _thumbnailStrip.Controls.Add(pic);
        }
        catch { /* if thumbnail fails, just skip — the file is still on disk */ }
    }

    private void RefreshThumbnail(int sequence)
    {
        foreach (Control c in _thumbnailStrip.Controls)
        {
            if (c is PictureBox pb && pb.Tag is int seq && seq == sequence && _session != null)
            {
                var cap = _session.Captures.FirstOrDefault(x => x.Sequence == sequence);
                if (cap?.AnnotatedPngFile == null) return;
                var annotatedPath = Path.Combine(SessionPaths.CapturesDir(_session.Directory), cap.AnnotatedPngFile);
                try
                {
                    var old = pb.Image;
                    using var fs = File.OpenRead(annotatedPath);
                    pb.Image = Image.FromStream(fs);
                    old?.Dispose();
                }
                catch { /* keep the source thumbnail */ }
                return;
            }
        }
    }

    private async Task OnEndAsync()
    {
        if (_session == null) return;
        _hotkeyHook?.Unregister();
        _hotkeyHook?.Dispose();
        _hotkeyHook = null;

        string? closeout = null;
        using (var dlg = new ClosoutPromptDialog())
        {
            if (dlg.ShowDialog(this) == DialogResult.OK) closeout = dlg.Body;
        }

        SetButtonsForState(ending: true);
        _statusLabel.Text = "Ending session — writing report...";
        try
        {
            await _session.EndAsync(closeout).ConfigureAwait(true);
            await _session.DisposeAsync().ConfigureAwait(true);
            _statusLabel.Text = $"Session ended. Report: {SessionPaths.ReportPath(_session.Directory)}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"End failed: {ex.Message}";
        }

        _session = null;
        _thumbnailStrip.Controls.Clear();
        SetButtonsForState();
    }

    private void SetButtonsForState(bool starting = false, bool armed = false, bool ending = false)
    {
        var idle = !starting && !armed && !ending;
        _workloadPicker.Enabled = idle;
        _startBtn.Enabled = idle;
        _captureBtn.Enabled = armed;
        _annotateBtn.Enabled = armed;
        _noteBtn.Enabled = armed;
        _endBtn.Enabled = armed;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeyHook?.Dispose();
            if (_session != null)
            {
                try { _session.DisposeAsync().AsTask().Wait(2000); } catch { }
            }
        }
        base.Dispose(disposing);
    }

    private sealed class WorkloadItem
    {
        public string Name { get; }
        public string DisplayName { get; }
        public WorkloadItem(string name, string displayName) { Name = name; DisplayName = displayName; }
        public override string ToString() => $"{DisplayName} ({Name})";
    }

    private sealed class NotePromptDialog : Form
    {
        private readonly TextBox _title;
        private readonly TextBox _body;
        public string? NoteTitle => _title.Text;
        public string? NoteBody => _body.Text;

        public NotePromptDialog()
        {
            Text = "Add note";
            Size = new Size(480, 320);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.FromArgb(220, 220, 220);
            MaximizeBox = false; MinimizeBox = false;

            var titleLabel = new Label { Text = "Title:", AutoSize = true, Top = 10, Left = 10 };
            _title = new TextBox { Top = 30, Left = 10, Width = 440, BackColor = Color.FromArgb(37, 37, 38), ForeColor = Color.FromArgb(220, 220, 220), BorderStyle = BorderStyle.FixedSingle };
            var bodyLabel = new Label { Text = "Note:", AutoSize = true, Top = 60, Left = 10 };
            _body = new TextBox { Top = 80, Left = 10, Width = 440, Height = 150, Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(37, 37, 38), ForeColor = Color.FromArgb(220, 220, 220), BorderStyle = BorderStyle.FixedSingle };

            var ok = new Button { Text = "Capture", Top = 240, Left = 280, Width = 80, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 120, 60), ForeColor = Color.White, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Top = 240, Left = 370, Width = 80, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White, DialogResult = DialogResult.Cancel };
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { titleLabel, _title, bodyLabel, _body, ok, cancel });
        }
    }

    private sealed class ClosoutPromptDialog : Form
    {
        private readonly TextBox _body;
        public string Body => _body.Text;

        public ClosoutPromptDialog()
        {
            Text = "Closeout notes (optional)";
            Size = new Size(520, 280);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.FromArgb(220, 220, 220);
            MaximizeBox = false; MinimizeBox = false;

            var label = new Label { Text = "What did you find? (will be embedded in SESSION_REPORT.md)", AutoSize = true, Top = 10, Left = 10 };
            _body = new TextBox { Top = 36, Left = 10, Width = 480, Height = 160, Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(37, 37, 38), ForeColor = Color.FromArgb(220, 220, 220), BorderStyle = BorderStyle.FixedSingle };
            var ok = new Button { Text = "End + save", Top = 210, Left = 320, Width = 80, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 120, 60), ForeColor = Color.White, DialogResult = DialogResult.OK };
            var skip = new Button { Text = "Skip", Top = 210, Left = 410, Width = 80, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White, DialogResult = DialogResult.Cancel };
            AcceptButton = ok; CancelButton = skip;
            Controls.AddRange(new Control[] { label, _body, ok, skip });
        }
    }
}
