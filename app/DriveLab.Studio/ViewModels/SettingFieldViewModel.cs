// ============================================================================
//  DriveLab
//  SettingFieldViewModel.cs — VM de um campo de setting: valor, presets e leitura/gravação no dispositivo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Globalization;
using System.Linq;
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
            // No campo de resolução o rótulo depende da tecnologia: "pulsos" em ABZ (o número
            // impresso no encoder), "contagens" no resto. É o rótulo que ensina o que digitar.
            if (_descriptor.Id == BaseSettingId.EncoderCpr) return DisplayLabel;

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

    /// <summary>Limites no espaço EXIBIDO. Iguais a Min/Max em todo campo, exceto a resolução em
    /// ABZ, onde a tela fala em pulsos e a placa em contagens.</summary>
    public double DisplayMin => IsQuadrature ? Min / 4.0 : Min;
    public double DisplayMax => IsQuadrature ? Max / 4.0 : Max;

    public string Unit => _descriptor.Unit;
    public bool IsInteger => _descriptor.Type != SettingType.Float;
    public string ValueText => !IsLoaded ? "—" : (IsInteger ? DisplayValue.ToString("0") : DisplayValue.ToString("0.##"));

    /// <summary>Valores fixos oferecidos como botões; vazio quando o campo usa slider livre.</summary>
    public IReadOnlyList<int> Presets { get; }
    public bool HasPresets => Presets.Count > 0;

    /// <summary>Chips de preset (com estado selecionado/habilitado) para a UI.</summary>
    public IReadOnlyList<PresetOptionViewModel> PresetOptions { get; }

    /// <summary>Chips de opção (enum) rotulados, ex.: tipo de encoder; vazio quando o campo não é enum.</summary>
    public IReadOnlyList<EnumOptionViewModel> Options { get; private set; }
    public bool HasOptions => Options.Count > 0;

    /// <summary>Recalcula as opções deste campo em função de OUTRO campo — hoje: a tecnologia
    /// depende do modelo do encoder. Se o valor atual deixar de ser válido (trocou de sensor e a
    /// tecnologia escolhida não existe nele), cai para a primeira opção. Assim a tela nunca fica
    /// com uma combinação que o hardware não tem.</summary>
    public void RefreshOptions(int modelId)
    {
        if (_descriptor.Id != BaseSettingId.EncoderInterface) return;

        var techs = EncoderCatalog.TechnologiesFor(modelId);
        Options = techs
            .Select(t => new EnumOptionViewModel((int)t, L.Get(TechLabelKey(t)), () => Value = (int)t))
            .ToList();
        foreach (var option in Options)
            option.CanSelect = IsConnected;

        if (!techs.Any(t => (int)t == (int)System.Math.Round(Value)))
            Value = (int)techs[0];

        // As opções são objetos NOVOS: sem isto, nenhuma nasce marcada como selecionada e a
        // sincronização só aconteceria na próxima mudança de valor. O sintoma era clicar na
        // tecnologia que já estava ativa e nada acontecer — porque não havia mudança para
        // disparar a sincronização.
        UpdateOptionSelection();

        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(IsDropdown));
        OnPropertyChanged(nameof(HasChipOptions));
        OnPropertyChanged(nameof(SelectedOption));
    }

    private EncoderTech _tech = EncoderTech.Abz;

    /// <summary>Em ABZ o valor guardado na placa é CONTAGENS, mas a pessoa digita PULSOS — que é o
    /// número impresso no encoder. A multiplicação por 4 acontece aqui, e é o que elimina o erro
    /// mais recorrente do fórum: gente digitando 600 onde a placa precisa de 2400.</summary>
    public void ApplyEncoderTech(EncoderTech tech)
    {
        _tech = tech;
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayMin));
        OnPropertyChanged(nameof(DisplayMax));
        OnPropertyChanged(nameof(ValueText));
    }

    private bool IsQuadrature => _descriptor.Id == BaseSettingId.EncoderCpr && _tech == EncoderTech.Abz;

    /// <summary>O que a tela mostra e edita: pulsos em ABZ, contagens no resto.</summary>
    public double DisplayValue
    {
        get => IsQuadrature ? Value / 4.0 : Value;
        set => Value = IsQuadrature ? value * 4.0 : value;
    }

    public string DisplayLabel => IsQuadrature
        ? L.Get("Setting_EncoderRes_Ppr")
        : L.Get("Setting_EncoderRes_Counts");

    private static string TechLabelKey(EncoderTech t) => t switch
    {
        EncoderTech.Abz => "Setting_EncoderTech_Abz",
        EncoderTech.Ssi => "Setting_EncoderTech_Ssi",
        _               => "Setting_EncoderTech_Spi",
    };

    /// <summary>Enum "catálogo" (muitas opções, ex.: modelo do encoder, que cresce a cada sensor
    /// novo suportado) → vira ComboBox/dropdown. Poucas opções ficam como chips lado a lado, que é
    /// mais rápido de usar: a escolha inteira fica visível, sem abrir nada.
    ///
    /// O limiar é 3 porque é o que cabe numa linha com rótulo curto — tecnologia (ABZ/SSI/SPI) e
    /// variante da placa (24V/56V) são chips; modelo do sensor, com nome longo e lista crescente,
    /// é dropdown.</summary>
    public bool IsDropdown => Options.Count > 3;

    /// <summary>Enum curto renderizado como chips (é enum, mas não é dropdown).</summary>
    public bool HasChipOptions => HasOptions && !IsDropdown;

    /// <summary>Item selecionado do dropdown — mapeia de/para <see cref="Value"/> (SelectedItem do ComboBox).</summary>
    public EnumOptionViewModel? SelectedOption
    {
        get => Options.FirstOrDefault(o => o.Value == (int)System.Math.Round(Value));
        set { if (value is not null) Value = value.Value; }
    }

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
