using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PickWorkloadsDirAsync = PickWorkloadsDirAsync;
        }
    }

    private async Task<string?> PickWorkloadsDirAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick workloads directory",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return null;
        return folders[0].TryGetLocalPath();
    }
}
