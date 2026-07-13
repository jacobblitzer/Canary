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

    // Phase 14.2 — Capture sub-object (Phase 4.6.F Session B+). When CaptureGif
    // is false AND ScrubNickname/ScrubValues are empty, the Capture object is
    // omitted on save. Otherwise it round-trips.
    [ObservableProperty] private bool _captureGif;
    [ObservableProperty] private int _captureFrameCount = 30;
    [ObservableProperty] private int _captureIntervalMs = 100;
    [ObservableProperty] private string _scrubNickname = string.Empty;
    /// <summary>Comma- or whitespace-separated list of slider values (parsed on save).</summary>
    [ObservableProperty] private string _scrubValuesText = string.Empty;
    [ObservableProperty] private int _scrubSettleMs;
    [ObservableProperty] private int _scrubSolveTimeoutMs = 10_000;
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

    // Phase 14.2 — VLM oracle config + setup.commands surfaced inline. All
    // optional; round-trip through the backing POCO if left blank.
    [ObservableProperty] private string _vlmDescription = string.Empty;
    [ObservableProperty] private string _vlmProvider = string.Empty;
    [ObservableProperty] private string _vlmModel = string.Empty;
    /// <summary>One command per line. Empty lines are ignored on save.</summary>
    [ObservableProperty] private string _commandsText = string.Empty;

    // Operator context fields (R6.5 Phase E) — optional prose that surfaces in
    // the editor and on checkpoint cards. whatItDoes = what the component computes
    // (operator-facing). whatYouShouldSee = discriminating visual signature
    // (human/agent-grade, from the Slop grounding card). Both null when absent.
    [ObservableProperty] private string _whatItDoes = string.Empty;
    [ObservableProperty] private string _whatYouShouldSee = string.Empty;

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

        // Phase 14.2 — VLM + commands.
        VlmDescription = definition.Setup?.VlmDescription ?? string.Empty;
        VlmProvider = definition.Setup?.Vlm?.Provider ?? string.Empty;
        VlmModel = definition.Setup?.Vlm?.Model ?? string.Empty;
        CommandsText = (definition.Setup?.Commands == null || definition.Setup.Commands.Count == 0)
            ? string.Empty
            : string.Join("\n", definition.Setup.Commands);

        // R6.5 Phase E — operator context fields.
        WhatItDoes = definition.Setup?.WhatItDoes ?? string.Empty;
        WhatYouShouldSee = definition.Setup?.WhatYouShouldSee ?? string.Empty;

        Checkpoints.Clear();
        foreach (var cp in definition.Checkpoints)
        {
            var row = new CheckpointRow
            {
                Name = cp.Name,
                Mode = string.IsNullOrEmpty(cp.Mode) ? "pixel-diff" : cp.Mode,
                AtTimeMs = cp.AtTimeMs,
                Tolerance = cp.Tolerance,
                Description = cp.Description,
                Source = string.IsNullOrEmpty(cp.Source) ? "viewport" : cp.Source,
                PanelNickname = cp.PanelNickname,
            };
            if (cp.Capture != null)
            {
                row.CaptureGif = cp.Capture.Gif;
                row.CaptureFrameCount = cp.Capture.FrameCount;
                row.CaptureIntervalMs = cp.Capture.IntervalMs;
                if (cp.Capture.Scrub != null)
                {
                    row.ScrubNickname = cp.Capture.Scrub.Nickname;
                    row.ScrubValuesText = cp.Capture.Scrub.Values == null || cp.Capture.Scrub.Values.Length == 0
                        ? string.Empty
                        : string.Join(", ", cp.Capture.Scrub.Values);
                    row.ScrubSettleMs = cp.Capture.Scrub.SettleMs;
                    row.ScrubSolveTimeoutMs = cp.Capture.Scrub.SolveTimeoutMs;
                }
            }
            Checkpoints.Add(row);
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

        // Phase 14.2 — VLM + commands. Empty → null (omit on serialize).
        _definition.Setup.VlmDescription = string.IsNullOrWhiteSpace(VlmDescription) ? null : VlmDescription;

        // R6.5 Phase E — operator context fields. Empty → null (omit on serialize).
        _definition.Setup.WhatItDoes = string.IsNullOrWhiteSpace(WhatItDoes) ? null : WhatItDoes;
        _definition.Setup.WhatYouShouldSee = string.IsNullOrWhiteSpace(WhatYouShouldSee) ? null : WhatYouShouldSee;
        if (!string.IsNullOrWhiteSpace(VlmProvider) || !string.IsNullOrWhiteSpace(VlmModel))
        {
            _definition.Setup.Vlm ??= new VlmConfig();
            if (!string.IsNullOrWhiteSpace(VlmProvider)) _definition.Setup.Vlm.Provider = VlmProvider.Trim();
            if (!string.IsNullOrWhiteSpace(VlmModel)) _definition.Setup.Vlm.Model = VlmModel.Trim();
        }
        else
        {
            // Leave existing Vlm in place if both fields are blanked; the
            // backing POCO preserves any fields the editor doesn't surface
            // (MaxTokens etc.). Operators who want to truly delete the Vlm
            // block can hand-edit JSON.
        }
        _definition.Setup.Commands = string.IsNullOrWhiteSpace(CommandsText)
            ? new List<string>()
            : CommandsText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        _definition.Checkpoints = Checkpoints.Select(cp =>
        {
            var built = new TestCheckpoint
            {
                Name = cp.Name,
                AtTimeMs = cp.AtTimeMs,
                Tolerance = cp.Tolerance,
                Description = cp.Description,
                Source = cp.Source,
                PanelNickname = cp.PanelNickname,
                Mode = cp.Mode,
            };
            // Phase 14.2 — Capture / Scrub. Capture sub-object is emitted only
            // when at least one capture feature is active (gif on OR scrub
            // nickname+values populated). Otherwise null (omit on serialize).
            bool hasScrub = !string.IsNullOrWhiteSpace(cp.ScrubNickname)
                            && !string.IsNullOrWhiteSpace(cp.ScrubValuesText);
            if (cp.CaptureGif || hasScrub)
            {
                var cap = new TestCheckpointCapture
                {
                    Gif = cp.CaptureGif,
                    FrameCount = cp.CaptureFrameCount,
                    IntervalMs = cp.CaptureIntervalMs,
                };
                if (hasScrub)
                {
                    var values = cp.ScrubValuesText
                        .Split(new[] { ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t =>
                        {
                            return double.TryParse(t.Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
                        })
                        .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                    cap.Scrub = new TestCheckpointScrub
                    {
                        Nickname = cp.ScrubNickname.Trim(),
                        Values = values,
                        SettleMs = cp.ScrubSettleMs,
                        SolveTimeoutMs = cp.ScrubSolveTimeoutMs,
                    };
                }
                built.Capture = cap;
            }
            return built;
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
