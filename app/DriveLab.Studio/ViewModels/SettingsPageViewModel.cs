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
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        IsConnected = _session.IsConnected;
        // Ao (re)conectar os grupos recarregam da placa via read (não dispara SettingChanged):
        // app passa a refletir a flash, então zera o dirty.
        IsDirty = false;
    }

    // SettingChanged só dispara em WriteSettingAsync (nunca em read/load) → todo write marca dirty.
    private void OnSettingWritten(object? sender, SettingChangedEventArgs e) => IsDirty = true;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await _session.SendCommandAsync(BaseCommand.SaveSettings);
        IsDirty = false; // gravou na flash: firmware == app
    }

    private bool CanSave() => IsConnected && IsDirty;

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void ResetDefaults()
    {
        foreach (var tab in Tabs)
            if (tab.Content is SettingsGroupViewModel group)
                foreach (var field in group.Fields)
                    field.ResetToDefault();
    }

    public override void Dispose()
    {
        _session.Connected -= OnConnectionChanged;
        _session.Disconnected -= OnConnectionChanged;
        _session.SettingChanged -= OnSettingWritten;
        foreach (var tab in Tabs)
            tab.Content.Dispose();
        base.Dispose();
    }
}
