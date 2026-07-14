using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.ViewModels;

public partial class SettingFieldViewModel : ViewModelBase
{
    private readonly DeviceSession _session;
    private readonly SettingDescriptor _descriptor;
    private bool _loading;

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectPresetCommand))]
    private bool _isConnected;

    public string DisplayName
    {
        get
        {
            var key = $"Setting_{_descriptor.Id}";
            var text = L.Get(key);
            return text == key ? _descriptor.DisplayName : text; // fallback: nome do schema
        }
    }
    public double Min => _descriptor.Min;
    public double Max => _descriptor.Max;
    public string Unit => _descriptor.Unit;
    public bool IsInteger => _descriptor.Type != SettingType.Float;
    public string ValueText => IsInteger ? Value.ToString("0") : Value.ToString("0.##");

    /// <summary>Valores fixos oferecidos como botões; vazio quando o campo usa slider livre.</summary>
    public IReadOnlyList<int> Presets { get; }
    public bool HasPresets => Presets.Count > 0;

    /// <summary>Chips de preset (com estado selecionado/habilitado) para a UI.</summary>
    public IReadOnlyList<PresetOptionViewModel> PresetOptions { get; }

    public SettingFieldViewModel(DeviceSession session, SettingDescriptor descriptor)
    {
        _session = session;
        _descriptor = descriptor;
        _value = descriptor.Default;
        Presets = SettingPresets.For(descriptor.Id);
        PresetOptions = Presets.Select(p => new PresetOptionViewModel(p, () => Value = p)).ToList();
        _isConnected = session.IsConnected;
        foreach (var option in PresetOptions)
            option.CanSelect = _isConnected;
        UpdatePresetSelection();

        _session.SettingChanged += OnSettingChanged;
        _session.Connected += OnConnectionChanged;
        _session.Disconnected += OnConnectionChanged;
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => IsConnected = _session.IsConnected;

    partial void OnIsConnectedChanged(bool value)
    {
        foreach (var option in PresetOptions)
            option.CanSelect = value;
    }

    private void UpdatePresetSelection()
    {
        var current = (int)Math.Round(Value);
        foreach (var option in PresetOptions)
            option.IsSelected = option.Value == current;
    }

    /// <summary>Volta o campo ao valor padrão do schema (grava se conectado).</summary>
    public void ResetToDefault() => Value = _descriptor.Default;

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void SelectPreset(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            Value = v; // dispara OnValueChanged -> grava + notifica outras telas
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Id != _descriptor.Id)
            return;

        // Atualiza sem disparar WriteAsync de volta (evita eco/loop).
        _loading = true;
        Value = e.Value.AsDouble;
        _loading = false;
    }

    public override void Dispose()
    {
        _session.SettingChanged -= OnSettingChanged;
        _session.Connected -= OnConnectionChanged;
        _session.Disconnected -= OnConnectionChanged;
        base.Dispose();
    }

    public async Task LoadAsync()
    {
        if (!_session.IsConnected)
            return;

        var value = await _session.ReadSettingAsync(_descriptor.Id);
        _loading = true;
        Value = value.AsDouble;
        _loading = false;
    }

    public Task WriteAsync()
    {
        if (!_session.IsConnected)
            return Task.CompletedTask;

        return _session.WriteSettingAsync(_descriptor.Id, new SettingValue(_descriptor.Type, _descriptor.Clamp(Value)));
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(ValueText));
        UpdatePresetSelection();
        if (!_loading)
            _ = WriteAsync();
    }
}
