namespace Canary.UI.Controls;

/// <summary>
/// Modal full-resolution image viewer with zoom and pan.
/// Toggle between baseline/candidate/diff images.
/// </summary>
internal sealed class ImageViewerForm : Form
{
    private readonly PictureBox _pictureBox;
    private readonly string[] _imagePaths;
    private int _currentIndex;
    private float _zoom = 1.0f;
    private Point _dragStart;
    private Point _scrollStart;
    private bool _dragging;

    public ImageViewerForm(string initialPath, string[] allPaths)
    {
        _imagePaths = allPaths.Length > 0 ? allPaths : new[] { initialPath };
        _currentIndex = Array.IndexOf(_imagePaths, initialPath);
        if (_currentIndex < 0) _currentIndex = 0;

        Text = "Image Viewer";
        Size = new Size(1200, 900);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(20, 20, 20);
        KeyPreview = true;

        // Picture box in auto-scroll panel
        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(20, 20, 20)
        };

        _pictureBox = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = Point.Empty,
            Cursor = Cursors.Hand
        };

        _pictureBox.MouseDown += OnMouseDown;
        _pictureBox.MouseMove += OnMouseMove;
        _pictureBox.MouseUp += OnMouseUp;

        scrollPanel.Controls.Add(_pictureBox);
        scrollPanel.MouseWheel += OnMouseWheel;

        // Toolbar
        var toolbar = new ToolStrip
        {
            BackColor = Color.FromArgb(45, 45, 48),
            GripStyle = ToolStripGripStyle.Hidden
        };

        var labels = new[] { "Baseline", "Candidate", "Diff" };
        for (int i = 0; i < _imagePaths.Length; i++)
        {
            var label = i < labels.Length ? labels[i] : $"Image {i + 1}";
            var idx = i;
            var btn = new ToolStripButton(label)
            {
                ForeColor = Color.FromArgb(220, 220, 220)
            };
            btn.Click += (_, _) => SwitchImage(idx);
            toolbar.Items.Add(btn);
        }

        toolbar.Items.Add(new ToolStripSeparator());

        var zoomInBtn = new ToolStripButton("Zoom In (+)")
        {
            ForeColor = Color.FromArgb(220, 220, 220)
        };
        zoomInBtn.Click += (_, _) => SetZoom(_zoom * 1.25f);

        var zoomOutBtn = new ToolStripButton("Zoom Out (-)")
        {
            ForeColor = Color.FromArgb(220, 220, 220)
        };
        zoomOutBtn.Click += (_, _) => SetZoom(_zoom / 1.25f);

        var fitBtn = new ToolStripButton("Fit")
        {
            ForeColor = Color.FromArgb(220, 220, 220)
        };
        fitBtn.Click += (_, _) => FitToWindow(scrollPanel);

        toolbar.Items.Add(zoomInBtn);
        toolbar.Items.Add(zoomOutBtn);
        toolbar.Items.Add(fitBtn);

        Controls.Add(scrollPanel);
        Controls.Add(toolbar);

        KeyDown += OnKeyDown;

        LoadCurrentImage();
    }

    private void SwitchImage(int index)
    {
        if (index >= 0 && index < _imagePaths.Length)
        {
            _currentIndex = index;
            LoadCurrentImage();
        }
    }

    private void LoadCurrentImage()
    {
        var path = _imagePaths[_currentIndex];
        try
        {
            if (File.Exists(path))
            {
                // Load into memory copy — avoids file lock and stream-after-dispose
                _pictureBox.Image?.Dispose();
                using (var original = Image.FromFile(path))
                    _pictureBox.Image = new Bitmap(original);
                FitToWindow(_pictureBox.Parent as Panel);
                Text = $"Image Viewer \u2014 {Path.GetFileName(path)}";
            }
        }
        catch
        {
            _pictureBox.Image = null;
            Text = "Image Viewer \u2014 Failed to load";
        }
    }

    private void FitToWindow(Panel? container)
    {
        if (_pictureBox.Image == null || container == null)
        {
            ApplyZoom();
            return;
        }
        float scaleX = (float)container.ClientSize.Width / _pictureBox.Image.Width;
        float scaleY = (float)container.ClientSize.Height / _pictureBox.Image.Height;
        float fit = Math.Min(scaleX, scaleY);
        SetZoom(Math.Clamp(fit, 0.1f, 1.0f));
    }

    private void SetZoom(float zoom)
    {
        _zoom = Math.Clamp(zoom, 0.1f, 10.0f);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (_pictureBox.Image == null) return;

        _pictureBox.SizeMode = PictureBoxSizeMode.StretchImage;
        _pictureBox.Width = (int)(_pictureBox.Image.Width * _zoom);
        _pictureBox.Height = (int)(_pictureBox.Image.Height * _zoom);
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        if (e.Delta > 0)
            SetZoom(_zoom * 1.15f);
        else
            SetZoom(_zoom / 1.15f);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragStart = e.Location;
            if (_pictureBox.Parent is ScrollableControl sc)
                _scrollStart = sc.AutoScrollPosition;
            _pictureBox.Cursor = Cursors.SizeAll;
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging && _pictureBox.Parent is ScrollableControl sc)
        {
            int dx = e.X - _dragStart.X;
            int dy = e.Y - _dragStart.Y;
            sc.AutoScrollPosition = new Point(
                -_scrollStart.X - dx,
                -_scrollStart.Y - dy);
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _dragging = false;
        _pictureBox.Cursor = Cursors.Hand;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Close();
                break;
            case Keys.Left:
                SwitchImage(_currentIndex - 1);
                break;
            case Keys.Right:
                SwitchImage(_currentIndex + 1);
                break;
            case Keys.Add:
            case Keys.Oemplus:
                SetZoom(_zoom * 1.25f);
                break;
            case Keys.Subtract:
            case Keys.OemMinus:
                SetZoom(_zoom / 1.25f);
                break;
        }
    }
}
