// ============================================================================
//  DriveLab
//  SimagicPedalTransport.cs — Transporte de pedais do perfil Simagic P2000: lê o report 0x01 (read-only) e alimenta o pipeline de pedais.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Pedals;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Hid.Simagic;

/// <summary>Perfil P2000: lê o report 0x01 do Simagic (read-only) e alimenta o pipeline de pedais.
/// Config é overlay/autoria local — não é gravada no dispositivo.</summary>
public sealed class SimagicPedalTransport : IPedalTransport
{
    private readonly object _sync = new();
    private readonly PedalDeviceModel _model = new();
    private readonly ISimagicHidReader _reader;

    public SimagicPedalTransport(ISimagicHidReader reader)
    {
        _reader = reader;
        _reader.ReportReceived += OnReport;
    }

    public bool IsConnected { get; private set; }
    public bool SupportsConfig => false;
    public FirmwareVersion FirmwareVersion { get; } = new(0, 0, 0, 0);
    public byte Flags { get; private set; }

    public event EventHandler<PedalState>? StateReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        lock (_sync) { _model.SeedDefaults(); }
        IsConnected = _reader.TryOpen();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _reader.Close();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task WriteSettingAsync(PedalSettingId id, PedalIndex pedal, SettingValue value)
    {
        lock (_sync) { _model.WriteSetting(id, pedal, value); }
        return Task.CompletedTask;
    }

    public Task<SettingValue> ReadSettingAsync(PedalSettingId id, PedalIndex pedal)
    {
        lock (_sync) { return Task.FromResult(_model.ReadSetting(id, pedal)); }
    }

    public Task SendCommandAsync(PedalCommandId command, byte arg = 0)
    {
        lock (_sync)
        {
            switch (command)
            {
                case PedalCommandId.LoadDefaults: _model.LoadDefaults(); break;
                case PedalCommandId.CalibrateStart: if (arg < 3) _model.CalibrateStart((PedalIndex)arg); break;
                case PedalCommandId.CalibrateStop: if (arg < 3) _model.CalibrateStop((PedalIndex)arg); break;
                case PedalCommandId.SaveToFlash: break; // read-only: no-op
            }
        }
        return Task.CompletedTask;
    }

    private void OnReport(object? sender, byte[] report)
    {
        if (report.Length < 7 || report[0] != 0x01)
            return;
        var rx = (ushort)(report[1] | (report[2] << 8)); // Rx → embreagem
        var ry = (ushort)(report[3] | (report[4] << 8)); // Ry → freio
        var rz = (ushort)(report[5] | (report[6] << 8)); // Rz → acelerador

        PedalState state;
        lock (_sync)
        {
            _model.SetRawInputs(rx, ry, rz);
            state = _model.BuildState(FirmwareVersion, Flags);
        }
        StateReceived?.Invoke(this, state);
    }
}
