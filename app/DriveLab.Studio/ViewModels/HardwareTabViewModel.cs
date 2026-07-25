// ============================================================================
//  DriveLab
//  HardwareTabViewModel.cs — VM da aba Hardware: monitor de telemetria + settings de hardware + EXPORTAR o
//  perfil de hardware (fecha o ciclo do criador: configura → exporta → embute no instalador).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Aba "Hardware" (só aparece no modo avançado/criador): monitor de telemetria + os settings de
/// hardware + o botão <b>Exportar perfil de hardware</b>. É uma <see cref="SettingsGroupViewModel"/> para
/// que Save/Reset da página continuem agindo sobre os campos desta aba.</summary>
public sealed partial class HardwareTabViewModel : SettingsGroupViewModel
{
    public HardwareMonitorViewModel Monitor { get; }

    // Metadados do perfil (o criador preenche antes de exportar).
    [ObservableProperty] private string _vendor = "";
    [ObservableProperty] private string _device = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string? _exportStatus;

    private IProfileFilePicker? _picker;

    public HardwareTabViewModel(BaseSession session, string title, IEnumerable<BaseSettingId> ids)
        : base(session, title, ids)
    {
        Monitor = new HardwareMonitorViewModel(session);
    }

    /// <summary>Liga o Export (precisa do file picker, que só existe depois da janela). Chamado pela
    /// composição, como o EnableFileExchange do ProfileLibrary.</summary>
    public void EnableExport(IProfileFilePicker picker)
    {
        _picker = picker;
        ExportProfileCommand.NotifyCanExecuteChanged();
    }

    public bool CanExport => _picker is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportProfileAsync()
    {
        if (_picker is null) return;

        // Monta o perfil a partir dos VALORES ATUAIS dos campos de hardware desta aba.
        var profile = HardwareProfileService.Build(Vendor, Device, Notes, DateTimeOffset.Now, GetFieldValue);
        var json = HardwareProfileService.Serialize(profile);

        var path = await _picker.PickSaveAsync("hardware-profile.json");
        if (path is null) { ExportStatus = null; return; }   // cancelado

        try
        {
            await File.WriteAllTextAsync(path, json);
            ExportStatus = $"Exportado: {path}";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Falha ao exportar: {ex.Message}";
        }
    }

    private double GetFieldValue(BaseSettingId id)
    {
        var f = Fields.FirstOrDefault(x => x.SettingId == id);
        return f?.Value ?? BaseSettingsSchema.Get(id).Default;
    }

    public override void Dispose()
    {
        Monitor.Dispose();
        base.Dispose();
    }
}
