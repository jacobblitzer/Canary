using System.Collections.ObjectModel;
using System.Text.Json;
using Canary.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels.Editors;

public sealed partial class CheckpointRow : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _mode = "pixel-diff";
    [ObservableProperty] private long _atTimeMs;
    [ObservableProperty] private double _tolerance;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _source = "viewport";
    [ObservableProperty] private string? _panelNickname;
}

public sealed partial class AssertRow : ObservableObject
{
    [ObservableProperty] private string _type = "PanelEquals";
    [ObservableProperty] private string _nickname = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
}

public partial class TestEditorViewModel : ObservableObject
{
    // Holds the underlying POCO so fields we don't surface on the editor
    // (Penumbra scene/canvas/display preset, VLM config, Setup.Commands,
    // CameraPosition per checkpoint, etc.) round-trip byte-identical when
    // Save re-serializes.
    private TestDefinition _definition = new();

    public IReadOnlyList<string> RunModes { get; } = new[] { "fresh", "shared" };
    public IReadOnlyList<string> CheckpointModes { get; } = new[] { "pixel-diff", "vlm" };
    public IReadOnlyList<string> CheckpointSources { get; } = new[] { "viewport", "file" };
    public IReadOnlyList<string> AssertTypes { get; } = new[] { "PanelEquals", "PanelContains", "PanelDoesNotContain" };

    public ObservableCollection<CheckpointRow> Checkpoints { get; } = new();
    public ObservableCollection<AssertRow> Asserts { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _workload = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _runMode = "fresh";
    [ObservableProperty] private bool _keepOpenOnFailure;
    [ObservableProperty] private string _setupFile = string.Empty;
    [ObservableProperty] private string _recording = string.Empty;
    [ObservableProperty] private int _viewportWidth = 800;
    [ObservableProperty] private int _viewportHeight = 600;
    [ObservableProperty] private string _viewportProjection = string.Empty;
    [ObservableProperty] private string _viewportDisplayMode = string.Empty;
    [ObservableProperty] private string _actionsJson = "[]";
    [ObservableProperty] private string? _validationError;

    public event Action<string>? SaveRequested;

    public void Load(TestDefinition definition)
    {
        _definition = definition;
        Name = definition.Name;
        Workload = definition.Workload;
        Description = definition.Description;
        RunMode = string.IsNullOrEmpty(definition.RunMode) ? "fresh" : definition.RunMode;
        KeepOpenOnFailure = definition.KeepOpenOnFailure;
        Recording = definition.Recording;
        SetupFile = definition.Setup?.File ?? string.Empty;

        var vp = definition.Setup?.Viewport;
        ViewportWidth = vp?.Width ?? 800;
        ViewportHeight = vp?.Height ?? 600;
        ViewportProjection = vp?.Projection ?? string.Empty;
        ViewportDisplayMode = vp?.DisplayMode ?? string.Empty;

        Checkpoints.Clear();
        foreach (var cp in definition.Checkpoints)
        {
            Checkpoints.Add(new CheckpointRow
            {
                Name = cp.Name,
                Mode = string.IsNullOrEmpty(cp.Mode) ? "pixel-diff" : cp.Mode,
                AtTimeMs = cp.AtTimeMs,
                Tolerance = cp.Tolerance,
                Description = cp.Description,
                Source = string.IsNullOrEmpty(cp.Source) ? "viewport" : cp.Source,
                PanelNickname = cp.PanelNickname,
            });
        }

        Asserts.Clear();
        foreach (var a in definition.Asserts)
        {
            Asserts.Add(new AssertRow
            {
                Type = string.IsNullOrEmpty(a.Type) ? "PanelEquals" : a.Type,
                Nickname = a.Nickname,
                Text = a.Text,
                Description = a.Description,
            });
        }

        ActionsJson = definition.Actions.Count == 0
            ? "[]"
            : JsonSerializer.Serialize(definition.Actions, EditorJson.Options);
        ValidationError = null;
    }

    public TestDefinition BuildDefinition()
    {
        // Mutate the underlying POCO so unmanaged fields (Penumbra scene,
        // VLM provider config, displayPreset, Setup.Commands, etc.) carry
        // through unchanged. That keeps Phase 3 round-trip-faithful even
        // though the editor surfaces only the common-case fields.
        _definition.Name = Name.Trim();
        _definition.Workload = Workload.Trim();
        _definition.Description = Description;
        _definition.RunMode = RunMode;
        _definition.KeepOpenOnFailure = KeepOpenOnFailure;
        _definition.Recording = Recording;

        _definition.Setup ??= new TestSetup();
        _definition.Setup.File = SetupFile;
        _definition.Setup.Viewport ??= new ViewportSetup();
        _definition.Setup.Viewport.Width = ViewportWidth;
        _definition.Setup.Viewport.Height = ViewportHeight;
        _definition.Setup.Viewport.Projection = ViewportProjection;
        _definition.Setup.Viewport.DisplayMode = ViewportDisplayMode;

        _definition.Checkpoints = Checkpoints.Select(cp => new TestCheckpoint
        {
            Name = cp.Name,
            AtTimeMs = cp.AtTimeMs,
            Tolerance = cp.Tolerance,
            Description = cp.Description,
            Source = cp.Source,
            PanelNickname = cp.PanelNickname,
            Mode = cp.Mode,
        }).ToList();

        _definition.Asserts = Asserts.Select(a => new TestAssert
        {
            Type = a.Type,
            Nickname = a.Nickname,
            Text = a.Text,
            Description = a.Description,
        }).ToList();

        try
        {
            _definition.Actions = string.IsNullOrWhiteSpace(ActionsJson)
                ? new List<TestAction>()
                : JsonSerializer.Deserialize<List<TestAction>>(ActionsJson, EditorJson.Options) ?? new List<TestAction>();
        }
        catch (Exception ex)
        {
            ValidationError = $"Actions JSON invalid: {ex.Message}";
            throw;
        }

        return _definition;
    }

    public string ToJson() => JsonSerializer.Serialize(BuildDefinition(), EditorJson.Options);

    [RelayCommand]
    public void AddCheckpoint() => Checkpoints.Add(new CheckpointRow { Name = "new-checkpoint" });

    [RelayCommand]
    public void RemoveCheckpoint(CheckpointRow? row)
    {
        if (row != null) Checkpoints.Remove(row);
    }

    [RelayCommand]
    public void AddAssert() => Asserts.Add(new AssertRow());

    [RelayCommand]
    public void RemoveAssert(AssertRow? row)
    {
        if (row != null) Asserts.Remove(row);
    }

    [RelayCommand]
    public void Save()
    {
        ValidationError = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Name is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Workload))
        {
            ValidationError = "Workload is required.";
            return;
        }
        string json;
        try { json = ToJson(); }
        catch (Exception ex) { ValidationError = ex.Message; return; }
        SaveRequested?.Invoke(json);
    }
}

internal static class EditorJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}
