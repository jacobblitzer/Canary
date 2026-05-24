namespace Canary.UI.Panels;

// Phase 7 / design §C4 — Feedback nav tab. Lists items in
// docs/feedback/{inbox,triaged,resolved}/ with a Markdown preview pane.
// Discovery: walks up from AppContext.BaseDirectory looking for
// docs/feedback/ (same heuristic as ImageViewerForm's FindInboxRoot
// and the MCP server's FeedbackRoot.Discover).
public sealed class FeedbackPanel : UserControl
{
    private readonly SplitContainer _split;
    private readonly TreeView _tree;
    private readonly TextBox _preview;
    private readonly ToolStripStatusLabel _status;

    public FeedbackPanel()
    {
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(45, 45, 48),
            Padding = new Padding(6, 4, 6, 4),
        };
        var refreshBtn = new Button { Text = "Refresh", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
        refreshBtn.Click += (_, _) => Reload();
        var openFolderBtn = new Button { Text = "Open inbox folder", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
        openFolderBtn.Click += (_, _) => OpenInboxFolder();
        toolbar.Controls.Add(refreshBtn);
        toolbar.Controls.Add(openFolderBtn);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 380,
            BackColor = Color.FromArgb(45, 45, 48),
            SplitterWidth = 6,
        };
        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
            HideSelection = false,
            ShowLines = true,
            ItemHeight = 22,
        };
        _tree.AfterSelect += (_, _) => LoadSelected();

        _preview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9.5f),
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.None,
        };

        _split.Panel1.Controls.Add(_tree);
        _split.Panel2.Controls.Add(_preview);

        var statusStrip = new StatusStrip { BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, SizingGrip = false };
        _status = new ToolStripStatusLabel("Feedback — loading…") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusStrip.Items.Add(_status);

        Controls.Add(_split);
        Controls.Add(statusStrip);
        Controls.Add(toolbar);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Reload();
    }

    private void Reload()
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        var root = DiscoverFeedbackRoot();
        foreach (var bucket in new[] { "inbox", "triaged", "resolved" })
        {
            var dir = Path.Combine(root, bucket);
            var node = new TreeNode($"{bucket} ({(Directory.Exists(dir) ? Directory.GetFiles(dir, "*.md").Length : 0)})")
            {
                ForeColor = bucket switch
                {
                    "inbox" => Color.FromArgb(255, 220, 80),
                    "triaged" => Color.FromArgb(100, 180, 255),
                    _ => Color.FromArgb(150, 220, 130),
                }
            };
            if (Directory.Exists(dir))
            {
                foreach (var md in Directory.GetFiles(dir, "*.md").OrderByDescending(p => File.GetLastWriteTimeUtc(p)))
                {
                    var slug = Path.GetFileNameWithoutExtension(md);
                    var child = new TreeNode(slug) { Tag = md };
                    node.Nodes.Add(child);
                }
            }
            _tree.Nodes.Add(node);
            node.Expand();
        }
        _tree.EndUpdate();
        _status.Text = $"Feedback root: {root}";
    }

    private void LoadSelected()
    {
        if (_tree.SelectedNode?.Tag is not string path) { _preview.Text = string.Empty; return; }
        try { _preview.Text = File.ReadAllText(path); }
        catch (Exception ex) { _preview.Text = $"(failed to read: {ex.Message})"; }
    }

    private void OpenInboxFolder()
    {
        var path = Path.Combine(DiscoverFeedbackRoot(), "inbox");
        Directory.CreateDirectory(path);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch { /* shell open fails silently */ }
    }

    internal static string DiscoverFeedbackRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "feedback");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "docs", "feedback");
    }
}
