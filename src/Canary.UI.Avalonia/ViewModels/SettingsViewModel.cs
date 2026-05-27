using Canary.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Canary.UI.Avalonia.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private CanarySettings _settings;
    private bool _suppressSave;

    public event Action<CanarySettings>? SettingsChanged;

    public string SettingsFilePath => CanarySettings.SettingsFilePath;

    [ObservableProperty]
    private bool _isStabilization;

    [ObservableProperty]
    private bool _isMaturation;

    [ObservableProperty]
    private bool _showTier3;

    [ObservableProperty]
    private int _retentionDays;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public SettingsViewModel() : this(CanarySettings.Load()) { }

    internal SettingsViewModel(CanarySettings settings)
    {
        _settings = settings;
        _suppressSave = true;
        IsMaturation = string.Equals(settings.UiMode, "maturation", StringComparison.OrdinalIgnoreCase);
        IsStabilization = !IsMaturation;
        ShowTier3 = settings.ShowTier3Processes;
        RetentionDays = Math.Clamp(settings.RetentionDays, 1, 365);
        _suppressSave = false;
        UpdateStatus();
    }

    public CanarySettings Snapshot() => _settings;

    partial void OnIsStabilizationChanged(bool value)
    {
        if (value && !_suppressSave)
        {
            _settings.UiMode = "stabilization";
            _suppressSave = true;
            IsMaturation = false;
            _suppressSave = false;
            Persist();
        }
    }

    partial void OnIsMaturationChanged(bool value)
    {
        if (value && !_suppressSave)
        {
            _settings.UiMode = "maturation";
            _suppressSave = true;
            IsStabilization = false;
            _suppressSave = false;
            Persist();
        }
    }

    partial void OnShowTier3Changed(bool value)
    {
        if (_suppressSave) return;
        _settings.ShowTier3Processes = value;
        Persist();
    }

    partial void OnRetentionDaysChanged(int value)
    {
        if (_suppressSave) return;
        _settings.RetentionDays = Math.Clamp(value, 1, 365);
        Persist();
    }

    private void Persist()
    {
        try
        {
            _settings.Save();
            UpdateStatus("Saved");
            SettingsChanged?.Invoke(_settings);
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    private void UpdateStatus(string prefix = "")
    {
        var mode = IsMaturation ? "Maturation" : "Stabilization";
        var p = string.IsNullOrEmpty(prefix) ? "" : prefix + " · ";
        StatusText = $"{p}UI mode: {mode} · Tier 3: {ShowTier3} · retention: {RetentionDays}d";
    }
}
