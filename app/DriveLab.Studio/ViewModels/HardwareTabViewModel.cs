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

    public HardwareTabViewModel(DeviceSession session, string title, IEnumerable<SettingId> ids)
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
