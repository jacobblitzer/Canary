using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

public sealed partial class FeedbackNode : ObservableObject
{
    public required string Label { get; init; }
    public string? FilePath { get; init; }
    public string Bucket { get; init; } = string.Empty;
    public ObservableCollection<FeedbackNode> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;
}

public partial class FeedbackViewModel : ObservableObject
{
    public ObservableCollection<FeedbackNode> Buckets { get; } = new();

    [ObservableProperty]
    private FeedbackNode? _selectedNode;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private string _statusText = "Feedback — loading…";

    public string FeedbackRoot { get; private set; } = string.Empty;

    public FeedbackViewModel()
    {
        FeedbackRoot = DiscoverFeedbackRoot();
    }

    partial void OnSelectedNodeChanged(FeedbackNode? value)
    {
        if (value?.FilePath == null) { Preview = string.Empty; return; }
        try { Preview = File.ReadAllText(value.FilePath); }
        catch (Exception ex) { Preview = $"(failed to read: {ex.Message})"; }
    }

    [RelayCommand]
    public void Reload()
    {
        Buckets.Clear();
        FeedbackRoot = DiscoverFeedbackRoot();
        foreach (var bucket in new[] { "inbox", "triaged", "resolved" })
        {
            var dir = Path.Combine(FeedbackRoot, bucket);
            var files = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.md").OrderByDescending(p => File.GetLastWriteTimeUtc(p)).ToList()
                : new List<string>();
            var bucketNode = new FeedbackNode
            {
                Label = $"{bucket} ({files.Count})",
                Bucket = bucket,
            };
            foreach (var md in files)
            {
                bucketNode.Children.Add(new FeedbackNode
                {
                    Label = Path.GetFileNameWithoutExtension(md),
                    FilePath = md,
                    Bucket = bucket,
                });
            }
            Buckets.Add(bucketNode);
        }
        StatusText = $"Feedback root: {FeedbackRoot}";
    }

    [RelayCommand]
    public void OpenInboxFolder()
    {
        var path = Path.Combine(FeedbackRoot, "inbox");
        Directory.CreateDirectory(path);
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
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
