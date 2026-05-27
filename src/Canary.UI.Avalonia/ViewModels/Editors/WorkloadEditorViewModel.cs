using System.Collections.ObjectModel;
using System.Text.Json;
using Canary.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels.Editors;

public sealed partial class SetupCommandRow : ObservableObject
{
    [ObservableProperty] private string _value = string.Empty;
}

public partial class WorkloadEditorViewModel : ObservableObject
{
    private WorkloadConfig _config = new();

    public IReadOnlyList<string> AgentTypes { get; } = new[] { "qualia-cdp", "penumbra-cdp", "rhino", "" };

    public ObservableCollection<SetupCommandRow> SetupCommands { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _appPath = string.Empty;
    [ObservableProperty] private string _appArgs = string.Empty;
    [ObservableProperty] private string _agentType = string.Empty;
    [ObservableProperty] private string _pipeName = string.Empty;
    [ObservableProperty] private int _startupTimeoutMs = 30000;
    [ObservableProperty] private string _windowTitle = string.Empty;
    [ObservableProperty] private string _viewportClass = string.Empty;
    [ObservableProperty] private string? _validationError;

    public event Action<string>? SaveRequested;

    public void Load(WorkloadConfig config)
    {
        _config = config;
        Name = config.Name;
        DisplayName = config.DisplayName;
        AppPath = config.AppPath;
        AppArgs = config.AppArgs;
        AgentType = config.AgentType;
        PipeName = config.PipeName;
        StartupTimeoutMs = config.StartupTimeoutMs;
        WindowTitle = config.WindowTitle;
        ViewportClass = config.ViewportClass;

        SetupCommands.Clear();
        foreach (var c in config.SetupCommands)
        {
            SetupCommands.Add(new SetupCommandRow { Value = c });
        }
        ValidationError = null;
    }

    public WorkloadConfig BuildConfig()
    {
        _config.Name = Name.Trim();
        _config.DisplayName = DisplayName.Trim();
        _config.AppPath = AppPath;
        _config.AppArgs = AppArgs;
        _config.AgentType = AgentType;
        _config.PipeName = PipeName;
        _config.StartupTimeoutMs = StartupTimeoutMs;
        _config.WindowTitle = WindowTitle;
        _config.ViewportClass = ViewportClass;
        _config.SetupCommands = SetupCommands.Select(c => c.Value).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return _config;
    }

    public string ToJson() => JsonSerializer.Serialize(BuildConfig(), EditorJson.Options);

    [RelayCommand]
    public void AddSetupCommand() => SetupCommands.Add(new SetupCommandRow());

    [RelayCommand]
    public void RemoveSetupCommand(SetupCommandRow? row)
    {
        if (row != null) SetupCommands.Remove(row);
    }

    [RelayCommand]
    public void Save()
    {
        ValidationError = null;
        if (string.IsNullOrWhiteSpace(Name)) { ValidationError = "Name is required."; return; }
        SaveRequested?.Invoke(ToJson());
    }
}
