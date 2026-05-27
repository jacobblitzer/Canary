using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Views;

public partial class TestsView : UserControl
{
    public TestsView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var tree = this.FindControl<TreeView>("WorkloadTree");
        if (tree != null)
        {
            tree.AddHandler(DragDrop.DropEvent, OnTreeDrop);
            tree.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        }
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        // Accept FileNames drag operations carrying at least one
        // .input.json recording. Anything else passes through to default
        // handling.
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private async void OnTreeDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not TestsViewModel vm) return;
        if (!e.Data.Contains(DataFormats.Files)) return;
        var files = e.Data.GetFiles();
        if (files == null) return;

        foreach (var f in files)
        {
            var path = f.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;
            if (!path.EndsWith(".input.json", StringComparison.OrdinalIgnoreCase)) continue;

            // The recording's parent dir is workloads/<w>/recordings/ so
            // walking two parents up gives us the workload directory.
            var workloadDir = Path.GetDirectoryName(Path.GetDirectoryName(path));
            if (workloadDir == null) continue;
            var workloadName = Path.GetFileName(workloadDir);
            var entry = vm.Tree.LoadedWorkloads.FirstOrDefault(w => string.Equals(w.Config.Name, workloadName, StringComparison.OrdinalIgnoreCase));
            if (entry == null) continue;

            if (vm.PromptForTestNameAsync == null) continue;
            var suggested = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
            var name = await vm.PromptForTestNameAsync(suggested);
            if (string.IsNullOrWhiteSpace(name)) continue;
            await vm.CreateTestFromRecordingFileAsync(name, path, entry);
        }
    }
}
