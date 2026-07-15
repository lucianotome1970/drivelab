// ============================================================================
//  DriveLab
//  TestViewModel.cs — VM da aba de teste: envia forças (mola, constante, periódica, damper) ao dispositivo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Protocol;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class TestViewModel : ViewModelBase
{
    private readonly DeviceSession _session;

    [ObservableProperty] private double _spring;
    [ObservableProperty] private double _constant;
    [ObservableProperty] private double _periodic;
    [ObservableProperty] private double _damper;
    [ObservableProperty] private bool _forceEnabled;

    public TestViewModel(DeviceSession session) => _session = session;

    public Task SendAsync()
    {
        if (!_session.IsConnected)
            return Task.CompletedTask;

        return _session.SendDirectControlAsync(new DirectControl
        {
            SpringForce = ToInt16(Spring),
            ConstantForce = ToInt16(Constant),
            PeriodicForce = ToInt16(Periodic),
            DamperForce = ToInt16(Damper),
        });
    }

    partial void OnSpringChanged(double value) => _ = SendAsync();
    partial void OnConstantChanged(double value) => _ = SendAsync();
    partial void OnPeriodicChanged(double value) => _ = SendAsync();
    partial void OnDamperChanged(double value) => _ = SendAsync();

    partial void OnForceEnabledChanged(bool value)
    {
        if (!_session.IsConnected)
            return;
        _ = _session.SendCommandAsync(DeviceCommand.SetForceEnabled, (byte)(value ? 1 : 0));
    }

    private static short ToInt16(double normalized) =>
        (short)Math.Round(Math.Clamp(normalized, -1, 1) * 10000);
}
