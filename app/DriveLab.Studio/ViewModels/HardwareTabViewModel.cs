// ============================================================================
//  DriveLab
//  HardwareTabViewModel.cs — VM da aba Hardware: painel de monitor de telemetria acima dos settings de hardware.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Aba "Hardware" da Base do Volante: painel de monitor de telemetria (somente leitura)
/// acima dos settings de hardware já existentes. É uma <see cref="SettingsGroupViewModel"/>
/// para que Save/Reset da página (<see cref="SettingsPageViewModel"/>) continuem agindo
/// sobre os campos desta aba normalmente.
/// </summary>
public sealed class HardwareTabViewModel : SettingsGroupViewModel
{
    public HardwareMonitorViewModel Monitor { get; }

    public HardwareTabViewModel(DeviceSession session, string title, IEnumerable<BaseSettingId> ids)
        : base(session, title, ids)
    {
        Monitor = new HardwareMonitorViewModel(session);
    }

    public override void Dispose()
    {
        Monitor.Dispose();
        base.Dispose();
    }
}
