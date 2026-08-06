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

    private readonly BaseSession _session;

    [ObservableProperty] private string _busVoltageText = "—";
    [ObservableProperty] private string _motorCurrentText = "—";
    [ObservableProperty] private string _estimatedTorqueText = "—";
    [ObservableProperty] private string _fetTempText = "—";
    [ObservableProperty] private string _motorTempText = "—";
    [ObservableProperty] private string _brakeActivationsText = "—";
    [ObservableProperty] private string _brakeEnergyText = "—";
    [ObservableProperty] private string _brakePeakText = "—";
    [ObservableProperty] private string _mcuTempText = "—";
    [ObservableProperty] private string _clippingText = "—";
    [ObservableProperty] private TelemetryLevel _busVoltageLevel = TelemetryLevel.Ok;
    [ObservableProperty] private TelemetryLevel _fetTempLevel = TelemetryLevel.Ok;
    [ObservableProperty] private TelemetryLevel _motorTempLevel = TelemetryLevel.Ok;
    [ObservableProperty] private TelemetryLevel _mcuTempLevel = TelemetryLevel.Ok;
    [ObservableProperty] private TelemetryLevel _clippingLevel = TelemetryLevel.Ok;

    /// <summary>Aviso (não fatal) quando a tensão lida não bate com a variante 24V/56V selecionada.
    /// Vazio/null = sem aviso. A UI mostra só quando houver texto.</summary>
    [ObservableProperty] private string? _voltageWarning;

    /// <summary>Constante de torque Kt (Nm/A) do motor, vinda do setting de hardware (0 = não medido). O dono
    /// desta VM (HardwareTabViewModel) mantém isso sincronizado com o campo torque_constant. Usada p/ estimar
    /// o torque = Kt·corrente SEM balança — igual Moza/Fanatec fazem (Kt·Iq medido).</summary>
    [ObservableProperty] private double _torqueConstant;

    private short _lastMotorCurrentMa;

    /// <summary>Intervalo de atualização do monitor. A telemetria vem a ~50 Hz, mas número trocando 50×
    /// por segundo é ilegível; 500 ms dá para ler e ainda acompanha o que está acontecendo.</summary>
    private const int RefreshMs = 500;
    private long _lastRefreshMs;
    private long _accBusMv;
    private long _accCurrentMa;
    private int  _accCount;

    /// <summary>Maior clipping visto no intervalo. Clipping é BURSTY (satura numa curva pesada e some),
    /// então a média o esconderia e a última amostra o perderia por sorteio: o piloto precisa saber que
    /// houve corte, não a média dele. Por isso este campo usa pico, ao contrário de tensão/corrente.</summary>
    private int _peakClipping;

    public HardwareMonitorViewModel(BaseSession session)
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
        // A telemetria chega a ~50 Hz. Reescrever os textos a cada pacote faz o numero "dancar" na
        // tela e o valor fica ilegivel (constatado na bancada 2026-08-06). Atualizamos a cada
        // RefreshMs e, nos campos que oscilam de verdade (tensao e corrente), mostramos a MEDIA do
        // intervalo em vez de uma amostra instantanea — assim o numero fica estavel E representativo,
        // em vez de congelar num pico aleatorio. Os demais campos (temperaturas, contadores do brake)
        // usam o ultimo valor, que ja e estavel por natureza.
        _accBusMv += s.BusVoltageMv;
        _accCurrentMa += s.MotorCurrentMa;
        _accCount++;
        if (s.ClippingPercent > _peakClipping) _peakClipping = s.ClippingPercent;

        long now = Environment.TickCount64;
        if (_lastRefreshMs != 0 && (now - _lastRefreshMs) < RefreshMs) return;
        _lastRefreshMs = now;

        double avgBusMv = _accCount > 0 ? (double)_accBusMv / _accCount : s.BusVoltageMv;
        double avgCurMa = _accCount > 0 ? (double)_accCurrentMa / _accCount : s.MotorCurrentMa;
        int clipping = _peakClipping;
        _accBusMv = 0; _accCurrentMa = 0; _accCount = 0; _peakClipping = 0;

        BusVoltageText = (avgBusMv / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " V";
        MotorCurrentText = (avgCurMa / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " A";
        _lastMotorCurrentMa = (short)avgCurMa;
        UpdateEstimatedTorque();
        FetTempText = TempText(s.FetTempC);
        MotorTempText = TempText(s.MotorTempC);
        McuTempText = TempText(s.McuTempC);
        ClippingText = clipping + " %";

        // Medidor do brake chopper. Existe porque o resistor quase nunca esquenta
        // (a frenagem dura milissegundos) e "frio" passa a impressão de "quebrado".
        BrakeActivationsText = s.BrakeActivations.ToString(CultureInfo.InvariantCulture);
        BrakeEnergyText = s.BrakeEnergyJoules >= 1000
            ? (s.BrakeEnergyJoules / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " kJ"
            : s.BrakeEnergyJoules.ToString("0.0", CultureInfo.InvariantCulture) + " J";
        BrakePeakText = s.BrakePeakWatts.ToString("0", CultureInfo.InvariantCulture) + " W";
        BusVoltageLevel = VoltageLevel(s.BusVoltageMv);
        FetTempLevel = TempLevel(s.FetTempC);
        MotorTempLevel = TempLevel(s.MotorTempC);
        McuTempLevel = TempLevel(s.McuTempC);
        ClippingLevel = ClipLevel(clipping);

        // Aviso de variante 24V/56V errada (flag do firmware). Mostra a tensão lida p/ ser acionável.
        VoltageWarning = s.Flags.HasFlag(BaseFlags.VoltageImplausible)
            ? string.Format(CultureInfo.InvariantCulture,
                            LocalizationManager.Get("Monitor_VoltageImplausible"), s.BusVoltageMv / 1000.0)
            : null;
    }

    // Torque estimado = Kt·corrente (sem balança). Só mostra quando Kt > 0 (o criador mediu/calibrou); senão "—".
    // É o mesmo cálculo que as bases comerciais usam (Kt·Iq medido) — mais preciso que pendurar peso numa haste.
    partial void OnTorqueConstantChanged(double value) => UpdateEstimatedTorque();

    private void UpdateEstimatedTorque()
    {
        EstimatedTorqueText = TorqueConstant > 0
            ? (TorqueConstant * (_lastMotorCurrentMa / 1000.0)).ToString("0.0", CultureInfo.InvariantCulture) + " Nm"
            : "—";
    }

    // Clipping: qualquer corte já é aviso (perda de detalhe); corte alto é crítico (baixar o ganho).
    private static TelemetryLevel ClipLevel(int percent) =>
        percent >= 50 ? TelemetryLevel.Critical
        : percent >= 15 ? TelemetryLevel.Warning
        : TelemetryLevel.Ok;

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
