// ============================================================================
//  DriveLab
//  SettingFieldViewModel.cs — VM de um campo de setting: valor, presets e leitura/gravação no dispositivo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.ViewModels;

public partial class SettingFieldViewModel : ViewModelBase
{
    private readonly BaseSession _session;
    private readonly SettingDescriptor _descriptor;
    private bool _loading;

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectPresetCommand))]
    private bool _isConnected;

    /// <summary>O valor já foi LIDO da base? A base é a fonte de verdade: sem conexão / antes do load,
    /// o campo NÃO exibe nada (mostra "—" e nenhum chip selecionado) — não inventa o default do schema como
    /// se fosse o que está na placa. Vira true após <see cref="LoadAsync"/> ou um eco de escrita do device;
    /// volta a false ao desconectar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    private bool _isLoaded;

    public string DisplayName
    {
        get
        {
            var key = $"Setting_{_descriptor.Id}";
            var text = L.Get(key);
            return text == key ? _descriptor.DisplayName : text; // fallback: nome do schema
        }
    }
    /// <summary>Chave estável do setting (nome do id) — usada pelos perfis nomeados.</summary>
    public string Key => _descriptor.Id.ToString();
    /// <summary>Chave "snake_case" do schema (ex.: "board_variant") — usada pelo perfil de hardware.</summary>
    public string SchemaKey => _descriptor.Key;
    /// <summary>Id do setting (BaseSettingId).</summary>
    public BaseSettingId SettingId => _descriptor.Id;
    public double Min => _descriptor.Min;
    public double Max => _descriptor.Max;
    public string Unit => _descriptor.Unit;
    public bool IsInteger => _descriptor.Type != SettingType.Float;
    public string ValueText => !IsLoaded ? "—" : (IsInteger ? Value.ToString("0") : Value.ToString("0.##"));

    /// <summary>Valores fixos oferecidos como botões; vazio quando o campo usa slider livre.</summary>
    public IReadOnlyList<int> Presets { get; }
    public bool HasPresets => Presets.Count > 0;

    /// <summary>Chips de preset (com estado selecionado/habilitado) para a UI.</summary>
    public IReadOnlyList<PresetOptionViewModel> PresetOptions { get; }

    /// <summary>Chips de opção (enum) rotulados, ex.: tipo de encoder; vazio quando o campo não é enum.</summary>
    public IReadOnlyList<EnumOptionViewModel> Options { get; }
    public bool HasOptions => Options.Count > 0;

    public SettingFieldViewModel(BaseSession session, SettingDescriptor descriptor)
    {
        _session = session;
        _descriptor = descriptor;
        _value = descriptor.Default;
        Presets = SettingPresets.For(descriptor.Id);
        PresetOptions = Presets.Select(p => new PresetOptionViewModel(p, () => Value = p)).ToList();
        Options = SettingOptions.For(descriptor.Id)
            .Select(spec => new EnumOptionViewModel(spec.Value, L.Get(spec.LabelKey), () => Value = spec.Value))
            .ToList();
        _isConnected = session.IsConnected;
        foreach (var option in PresetOptions)
            option.CanSelect = _isConnected;
        foreach (var option in Options)
            option.CanSelect = _isConnected;
        UpdatePresetSelection();
        UpdateOptionSelection();

        _session.SettingChanged += OnSettingChanged;
        _session.Connected += OnConnectionChanged;
        _session.Disconnected += OnConnectionChanged;
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        IsConnected = _session.IsConnected;
        // Ao desconectar, o campo deixa de refletir a base → volta ao estado "não lido" (vazio).
        if (!IsConnected) IsLoaded = false;
    }

    partial void OnIsConnectedChanged(bool value)
    {
        foreach (var option in PresetOptions)
            option.CanSelect = value;
        foreach (var option in Options)
            option.CanSelect = value;
    }

    partial void OnIsLoadedChanged(bool value)
    {
        UpdatePresetSelection();
        UpdateOptionSelection();
    }

    private void UpdatePresetSelection()
    {
        var current = (int)Math.Round(Value);
        foreach (var option in PresetOptions)
            option.IsSelected = IsLoaded && option.Value == current;
    }

    private void UpdateOptionSelection()
    {
        var current = (int)Math.Round(Value);
        foreach (var option in Options)
            option.IsSelected = IsLoaded && option.Value == current;
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
        IsLoaded = true;   // veio do device (leitura/eco) → o campo agora reflete a base
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
        IsLoaded = true;   // lido da base → passa a exibir o valor real
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
        UpdateOptionSelection();
        if (!_loading)
            _ = WriteAsync();
    }
}
