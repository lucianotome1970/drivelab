// ============================================================================
//  DriveLab
//  HidPedalTransport.cs — Transporte HID real da pedaleira DriveLab (RP2040): enquadra reports P0 e trata settings/telemetria.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using HidSharp;

namespace DriveLab.Hid;

/// <summary>
/// <see cref="IPedalTransport"/> real sobre USB HID para a NOSSA pedaleira (RP2040, firmware P0).
/// Espelha o <see cref="HidBaseTransport"/> do volante: enquadra os reports P0 por Report ID e
/// os envia por um <see cref="IHidChannel"/>. Telemetria (<c>PedalState</c> 0x20) e respostas de
/// settings chegam pela read-thread do canal; o <c>PedalDeviceSession</c> marshala p/ a UI.
/// </summary>
public sealed class HidPedalTransport : IPedalTransport, IDisposable
{
    private readonly IHidChannel _channel;
    private readonly object _pendingLock = new();
    private readonly Dictionary<(byte field, byte index), TaskCompletionSource<SettingValue>> _pendingReads = new();
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromMilliseconds(500);

    public HidPedalTransport(IHidChannel channel)
    {
        _channel = channel;
        _channel.ReportReceived += OnReport;
    }

    public bool IsConnected { get; private set; }
    public bool SupportsConfig => true;
    public FirmwareVersion FirmwareVersion { get; private set; }

    public event EventHandler<PedalState>? StateReceived;

    /// <summary>Varre o HID por VID/PID da nossa pedaleira (autodetecção). No macOS 26 o HidSharp
    /// não enumera (cai p/ Simagic/simulador); no Windows funciona.</summary>
    public static bool IsDevicePresent()
    {
        try
        {
            return DeviceList.Local
                .GetHidDevices(PedalDeviceIdentity.VendorId, PedalDeviceIdentity.ProductId)
                .Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = await _channel.OpenAsync(PedalDeviceIdentity.VendorId, PedalDeviceIdentity.ProductId);
    }

    public Task DisconnectAsync()
    {
        _channel.Close();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task WriteSettingAsync(PedalSettingId id, PedalIndex pedal, SettingValue value) =>
        _channel.WriteAsync(HidBaseTransport.Frame(
            PedalReportIds.SettingWrite, new SettingReport((byte)id, (byte)pedal, value).ToBytes()));

    public Task<SettingValue> ReadSettingAsync(PedalSettingId id, PedalIndex pedal) =>
        ReadSettingAsync(id, pedal, DefaultReadTimeout);

    public async Task<SettingValue> ReadSettingAsync(PedalSettingId id, PedalIndex pedal, TimeSpan timeout)
    {
        var key = ((byte)id, (byte)pedal);
        var tcs = new TaskCompletionSource<SettingValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) _pendingReads[key] = tcs;

        await _channel.WriteAsync(HidBaseTransport.Frame(
            PedalReportIds.SettingReadRequest, new SettingReadRequestReport((byte)id, (byte)pedal).ToBytes()));

        using var cts = new CancellationTokenSource(timeout);
        using (cts.Token.Register(() =>
        {
            lock (_pendingLock) _pendingReads.Remove(key);
            tcs.TrySetException(new TimeoutException(
                $"Sem SettingValue p/ field {(byte)id} pedal {(byte)pedal} em {timeout.TotalMilliseconds}ms"));
        }))
        {
            return await tcs.Task;
        }
    }

    public Task SendCommandAsync(PedalCommandId command, byte arg = 0) =>
        _channel.WriteAsync(HidBaseTransport.Frame(
            PedalReportIds.Command, new CommandReport((byte)command, arg).ToBytes()));

    private void OnReport(object? sender, byte[] wire)
    {
        if (wire.Length < 1 + ReportConstants.ReportSize)
            return;

        try
        {
            var reportId = wire[0];
            var payload = wire.AsSpan(1, ReportConstants.ReportSize);

            if (reportId == PedalReportIds.PedalState)
            {
                var state = PedalState.Parse(payload);
                FirmwareVersion = state.Firmware;
                StateReceived?.Invoke(this, state);
            }
            else if (reportId == PedalReportIds.SettingValue)
            {
                var report = SettingReport.Parse(payload);
                TaskCompletionSource<SettingValue>? tcs;
                lock (_pendingLock)
                {
                    _pendingReads.Remove((report.FieldId, report.Index), out tcs);
                }
                tcs?.TrySetResult(report.Value);
            }
        }
        catch
        {
            // Descarta reports malformados em vez de derrubar a read-thread.
        }
    }

    public void Dispose()
    {
        _channel.ReportReceived -= OnReport;
        _channel.Dispose();
    }
}
