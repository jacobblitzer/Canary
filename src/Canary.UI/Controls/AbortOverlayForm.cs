using Canary.Input;

namespace Canary.UI.Controls;

/// <summary>
/// Small floating overlay near the target app showing how to abort.
/// Click to abort. Does not steal focus from the target app.
/// </summary>
internal sealed class AbortOverlayForm : Form
{
    private readonly System.Windows.Forms.Timer _trackTimer;
    private readonly IntPtr _targetWindow;

    /// <summary>Fired when the user clicks the overlay or presses the abort key.</summary>
    public event Action? Aborted;

    public AbortOverlayForm(IntPtr targetWindow, string mode)
    {
        _targetWindow = targetWindow;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = mode == "RECORDING" ? Color.FromArgb(180, 50, 50) : Color.FromArgb(200, 130, 20);
        ForeColor = Color.White;
        Opacity = 0.88;
        Size = new Size(230, 32);
        Font = new Font("Segoe UI", 9f, FontStyle.Bold);

        var label = new Label
        {
            Text = $"  \u25CF {mode}  |  Pause to abort",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand
        };
        label.Click += (_, _) => Aborted?.Invoke();
        Controls.Add(label);
        Click += (_, _) => Aborted?.Invoke();

        _trackTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _trackTimer.Tick += OnTrackTick;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdatePosition();
        _trackTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _trackTimer.Stop();
        _trackTimer.Dispose();
        base.OnFormClosed(e);
    }

    private void OnTrackTick(object? sender, EventArgs e)
    {
        if (_targetWindow != IntPtr.Zero && !ViewportLocator.IsValidTarget(_targetWindow))
        {
            Close();
            return;
        }
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (_targetWindow != IntPtr.Zero)
        {
            var bounds = ViewportLocator.GetViewportBounds(_targetWindow);
            int x = bounds.X + bounds.Width - Width - 8;
            int y = bounds.Y + 8;
            Location = new Point(Math.Max(0, x), Math.Max(0, y));
        }
        else
        {
            var (x, y) = WindowPositioner.GetOverlayPosition(Width, Height);
            Location = new Point(x, y);
        }
    }

    // Prevent stealing focus from target app
    protected override bool ShowWithoutActivation => true;

    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }
}
