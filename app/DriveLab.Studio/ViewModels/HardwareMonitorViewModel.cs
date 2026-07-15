// ============================================================================
//  DriveLab
//  HardwareMonitorViewModel.cs — VM do monitor de telemetria de hardware (tensão, corrente e temperaturas).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Protocol;
using DriveLab.Studio.Localization;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class HardwareMonitorViewModel : ViewModelBase
{
    private const sbyte NoSensor = -128;

    private readonly DeviceSession _session;

    [ObservableProperty] private string _busVoltageText = "—";
    [ObservableProperty] private string _motorCurrentText = "—";
    [ObservableProperty] private string _fetTempText = "—";
    [ObservableProperty] private string _motorTempText = "—";
    [ObservableProperty] private string _mcuTempText = "—";
    [ObservableProperty] private TelemetryLevel _busVoltageLevel = TelemetryLevel.Ok;
    [ObservableProperty] private TelemetryLevel _fetTempLevel = TelemetryLevel.Ok;
    [ObservableProperty] private TelemetryLevel _motorTempLevel = TelemetryLevel.Ok;
    [ObservableProperty] private TelemetryLevel _mcuTempLevel = TelemetryLevel.Ok;

    public HardwareMonitorViewModel(DeviceSession session)
    {
        _session = session;
        _session.StateReceived += OnState;
    }

    public override void Dispose()
    {
        _session.StateReceived -= OnState;
        base.Dispose();
    }

    private void OnState(object? sender, BaseState s)
    {
        BusVoltageText = (s.BusVoltageMv / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " V";
        MotorCurrentText = (s.MotorCurrentMa / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " A";
        FetTempText = TempText(s.FetTempC);
        MotorTempText = TempText(s.MotorTempC);
        McuTempText = TempText(s.McuTempC);
        BusVoltageLevel = VoltageLevel(s.BusVoltageMv);
        FetTempLevel = TempLevel(s.FetTempC);
        MotorTempLevel = TempLevel(s.MotorTempC);
        McuTempLevel = TempLevel(s.McuTempC);
    }

    private static string TempText(sbyte c) =>
        c == NoSensor ? LocalizationManager.Get("Monitor_NoSensor") : $"{c} {LocalizationManager.Get("Monitor_DegC")}";

    private static TelemetryLevel VoltageLevel(ushort mv) =>
        mv is < 16000 or > 28000 ? TelemetryLevel.Critical
        : mv is >= 18000 and <= 26000 ? TelemetryLevel.Ok
        : TelemetryLevel.Warning;

    private static TelemetryLevel TempLevel(sbyte c) =>
        c == NoSensor ? TelemetryLevel.Ok
        : c >= 80 ? TelemetryLevel.Critical
        : c < 60 ? TelemetryLevel.Ok
        : TelemetryLevel.Warning;
}
