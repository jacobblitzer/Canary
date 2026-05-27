using System.Collections.ObjectModel;
using System.Text.Json;
using Canary.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels.Editors;

public sealed partial class TestPickRow : ObservableObject
{
    public required string TestName { get; init; }
    [ObservableProperty] private bool _isSelected;
}

public partial class SuiteEditorViewModel : ObservableObject
{
    private SuiteDefinition _definition = new();

    public ObservableCollection<TestPickRow> AvailableTests { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _keepOpen;
    [ObservableProperty] private string? _validationError;

    public event Action<string>? SaveRequested;

    public void Load(SuiteDefinition definition, IEnumerable<TestDefinition> availableTests)
    {
        _definition = definition;
        Name = definition.Name;
        Description = definition.Description;
        KeepOpen = definition.KeepOpen;

        var selected = new HashSet<string>(definition.Tests, StringComparer.OrdinalIgnoreCase);
        AvailableTests.Clear();
        foreach (var t in availableTests)
        {
            AvailableTests.Add(new TestPickRow
            {
                TestName = t.Name,
                IsSelected = selected.Contains(t.Name),
            });
        }
        ValidationError = null;
    }

    public SuiteDefinition BuildDefinition()
    {
        _definition.Name = Name.Trim();
        _definition.Description = Description.Trim();
        _definition.KeepOpen = KeepOpen;
        _definition.Tests = AvailableTests.Where(r => r.IsSelected).Select(r => r.TestName).ToList();
        return _definition;
    }

    public string ToJson() => JsonSerializer.Serialize(BuildDefinition(), EditorJson.Options);

    [RelayCommand]
    public void Save()
    {
        ValidationError = null;
        if (string.IsNullOrWhiteSpace(Name)) { ValidationError = "Name is required."; return; }
        if (!AvailableTests.Any(r => r.IsSelected)) { ValidationError = "Select at least one test."; return; }
        SaveRequested?.Invoke(ToJson());
    }
}
