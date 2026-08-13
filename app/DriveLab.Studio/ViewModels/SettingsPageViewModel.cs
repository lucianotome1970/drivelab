// ============================================================================
//  DriveLab
//  SettingsPageViewModel.cs — VM de página com abas de settings e barra inferior (Padrão/Salvar), estilo MOZA.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Especificação de uma aba de settings: cabeçalho + settings que ela contém.</summary>
public sealed record SettingsTabSpec(string Header, IReadOnlyList<BaseSettingId> Ids);

/// <summary>Uma aba da página: cabeçalho + conteúdo (renderizado pelo ViewLocator).</summary>
public sealed record PageTab(string Header, ViewModelBase Content);

/// <summary>
/// Página com abas (ex.: "Base do Volante" → Basic / Advanced / Hardware / Telemetria)
/// e a barra inferior (Padrão / Salvar), no estilo MOZA.
/// </summary>
public sealed partial class SettingsPageViewModel : ViewModelBase
{
    private readonly BaseSession _session;

    public string Title { get; }
    public IReadOnlyList<PageTab> Tabs { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetDefaultsCommand))]
    private bool _isConnected;

    /// <summary>App difere da flash da placa (alteração não salva) — habilita Salvar; zera ao carregar/salvar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    public SettingsPageViewModel(BaseSession session, string title, IEnumerable<PageTab> tabs)
    {
        _session = session;
        Title = title;
        Tabs = tabs.ToList();
        _isConnected = session.IsConnected;
        _session.Connected += OnConnectionChanged;
        _session.Disconnected += OnConnectionChanged;
        _session.SettingChanged += OnSettingWritten;
        // Mexer num controle não envia mais nada à base; é isto que passa a habilitar o Salvar.
        foreach (var field in AllFields())
            field.Edited += OnFieldEdited;
    }

    private void OnFieldEdited(object? sender, EventArgs e) => IsDirty = true;

    /// <summary>Todos os campos de setting das abas (cada aba de config é um SettingsGroupViewModel).</summary>
    private IEnumerable<SettingFieldViewModel> AllFields() =>
        Tabs.Select(t => t.Content).OfType<SettingsGroupViewModel>().SelectMany(g => g.Fields);

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        IsConnected = _session.IsConnected;
        // Ao (re)conectar os grupos recarregam da placa via read (não dispara SettingChanged):
        // app passa a refletir a flash, então zera o dirty.
        IsDirty = false;
    }

    // SettingChanged só dispara em WriteSettingAsync (nunca em read/load) → todo write marca dirty.
    private void OnSettingWritten(object? sender, SettingChangedEventArgs e)
    {
        IsDirty = true;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        // ENVIA primeiro, persiste depois. Mexer nos controles não manda mais nada para a base
        // (SettingFieldViewModel.OnValueChanged) — o usuário monta o ajuste inteiro na tela e só aqui
        // ele vai para a placa, de uma vez. Evita reconfigurar a base ao vivo com o motor armado e a
        // rajada de writes de quando se arrasta um slider.
        foreach (var field in AllFields())
            if (field.IsModified)
                await field.WriteAsync();

        await _session.SendCommandAsync(BaseCommand.SaveSettings);
        IsDirty = false; // gravou na flash: firmware == app
    }

    private bool CanSave() => IsConnected && IsDirty;

    /// <summary>
    /// Preenche a tela com os valores de FÁBRICA, perguntados à base (report 0x17).
    ///
    /// <para>Não grava nada: a placa continua com o que tinha até alguém clicar em Salvar. Isso é
    /// deliberado — "Padrão" mostra o que a base usaria, e a decisão continua sendo de quem está
    /// na frente do volante.</para>
    ///
    /// <para>Perguntamos em vez de usar o padrão do descritor porque os dois valores moram em
    /// lugares diferentes — o array do firmware e o schema do app. Hoje coincidem, mas nada
    /// garantia isso: bastava editar um lado para "Padrão" escrever valores diferentes dos de uma
    /// placa recém-gravada, com os dois parecendo o padrão. Com a base respondendo, existe uma
    /// resposta só.</para>
    ///
    /// <para>Se a base não responder por um campo, aquele campo cai no padrão do schema — é o
    /// comportamento anterior, e vale mais que deixar a tela pela metade.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task ResetDefaults()
    {
        foreach (var field in AllFields())
        {
            try
            {
                var padrao = await _session.ReadSettingDefaultAsync(field.SettingId);
                field.ApplyDefaultFromDevice(padrao.AsDouble);
            }
            catch (Exception)
            {
                // Firmware antigo (sem o 0x17) ou resposta perdida: usa o do schema.
                field.ResetToDefault();
            }
        }
    }

    public override void Dispose()
    {
        _session.Connected -= OnConnectionChanged;
        _session.Disconnected -= OnConnectionChanged;
        _session.SettingChanged -= OnSettingWritten;
        foreach (var field in AllFields())
            field.Edited -= OnFieldEdited;
        foreach (var tab in Tabs)
            tab.Content.Dispose();
        base.Dispose();
    }
}
