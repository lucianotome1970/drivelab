using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class SettingFieldViewModel : ViewModelBase
{
    // Presets fixos por setting (mesmos do Dashboard). Vazio => slider contínuo.
    private static readonly int[] MotionRangePresets = { 360, 540, 720, 900, 1080, 1440, 1800 };

    private readonly DeviceSession _session;
    private readonly SettingDescriptor _descriptor;
    private bool _loading;

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectPresetCommand))]
    private bool _isConnected;

    public string DisplayName => _descriptor.DisplayName;
    public double Min => _descriptor.Min;
    public double Max => _descriptor.Max;
    public string Unit => _descriptor.Unit;
    public bool IsInteger => _descriptor.Type != SettingType.Float;
    public string ValueText => IsInteger ? Value.ToString("0") : Value.ToString("0.##");

    /// <summary>Valores fixos oferecidos como botões; vazio quando o campo usa slider livre.</summary>
    public IReadOnlyList<int> Presets { get; }
    public bool HasPresets => Presets.Count > 0;

    public SettingFieldViewModel(DeviceSession session, SettingDescriptor descriptor)
    {
        _session = session;
        _descriptor = descriptor;
        _value = descriptor.Default;
        Presets = descriptor.Id == SettingId.MotionRange ? MotionRangePresets : Array.Empty<int>();
        _isConnected = session.IsConnected;
        _session.SettingChanged += OnSettingChanged;
        _session.Connected += OnConnectionChanged;
        _session.Disconnected += OnConnectionChanged;
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => IsConnected = _session.IsConnected;

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
        if (!_loading)
            _ = WriteAsync();
    }
}
