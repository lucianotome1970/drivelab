// ============================================================================
//  DriveLab
//  SettingsGroupViewModel.cs — VM de uma página de settings para um conjunto curado de campos, carregados do dispositivo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Página de ajustes para um conjunto curado de settings (ex.: "Base do Volante",
/// "Avançado"). Carrega os valores do dispositivo ao conectar.
/// </summary>
public class SettingsGroupViewModel : ViewModelBase
{
    private readonly DeviceSession _session;

    public string Title { get; }
    public IReadOnlyList<SettingFieldViewModel> Fields { get; }

    /// <summary>Metade dos campos, para o layout em 2 colunas (estilo MOZA).</summary>
    public IReadOnlyList<SettingFieldViewModel> LeftColumn { get; }
    public IReadOnlyList<SettingFieldViewModel> RightColumn { get; }

    public SettingsGroupViewModel(DeviceSession session, string title, IEnumerable<SettingId> ids)
    {
        _session = session;
        Title = title;
        Fields = ids.Select(id => new SettingFieldViewModel(session, SettingsSchema.Get(id))).ToList();

        var half = (Fields.Count + 1) / 2; // coluna esquerda leva o excedente
        LeftColumn = Fields.Take(half).ToList();
        RightColumn = Fields.Skip(half).ToList();

        _session.Connected += OnConnected;
    }

    public async Task LoadAsync()
    {
        foreach (var field in Fields)
            await field.LoadAsync();
    }

    private void OnConnected(object? sender, EventArgs e) => _ = LoadAsync();

    public override void Dispose()
    {
        _session.Connected -= OnConnected;
        foreach (var field in Fields)
            field.Dispose();
        base.Dispose();
    }
}
