// ============================================================================
//  DriveLab
//  HidWheelTransport.cs — Transporte HID real do volante removível DriveLab (RP2040), espelha HidHandbrakeTransport.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using HidSharp;

namespace DriveLab.Hid;

/// <summary>
/// <see cref="IWheelTransport"/> real sobre USB HID para o volante DriveLab (RP2040, firmware P0).
/// Espelha <see cref="HidHandbrakeTransport"/>: telemetria <see cref="WheelState"/> (0x21), settings
/// (0x14/0x15/0x16), comandos (0x02) e, a mais, saída de LEDs <see cref="WheelLedReport"/> (0x18).
/// </summary>
public sealed class HidWheelTransport : IWheelTransport, IDisposable
{
    private readonly IHidChannel _channel;
    private readonly object _pendingLock = new();
    private readonly Dictionary<(byte field, byte index), TaskCompletionSource<SettingValue>> _pendingReads = new();
    private TaskCompletionSource<WheelLedReport>? _pendingLedRead;
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromMilliseconds(500);

    public HidWheelTransport(IHidChannel channel)
    {
        _channel = channel;
        _channel.ReportReceived += OnReport;
    }

    public bool IsConnected { get; private set; }
    public bool SupportsConfig => true;
    public FirmwareVersion FirmwareVersion { get; private set; }

    public event EventHandler<WheelState>? StateReceived;

    /// <summary>Varre o HID por VID/PID do nosso volante (autodetecção). No macOS 26 o HidSharp
    /// não enumera (cai p/ simulador); no Windows funciona.</summary>
    public static bool IsDevicePresent()
    {
        try
        {
            return DeviceList.Local
                .GetHidDevices(WheelDeviceIdentity.VendorId, WheelDeviceIdentity.ProductId)
                .Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = await _channel.OpenAsync(WheelDeviceIdentity.VendorId, WheelDeviceIdentity.ProductId);
    }

    public Task DisconnectAsync()
    {
        _channel.Close();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task WriteSettingAsync(WheelSettingId id, SettingValue value) =>
        _channel.WriteAsync(HidBaseTransport.Frame(
            WheelReportIds.SettingWrite, new SettingReport((byte)id, 0, value).ToBytes()));

    public Task<SettingValue> ReadSettingAsync(WheelSettingId id) =>
        ReadSettingAsync(id, DefaultReadTimeout);

    public async Task<SettingValue> ReadSettingAsync(WheelSettingId id, TimeSpan timeout)
    {
        var key = ((byte)id, (byte)0);
        var tcs = new TaskCompletionSource<SettingValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) _pendingReads[key] = tcs;

        await _channel.WriteAsync(HidBaseTransport.Frame(
            WheelReportIds.SettingReadRequest, new SettingReadRequestReport((byte)id, 0).ToBytes()));

        using var cts = new CancellationTokenSource(timeout);
        using (cts.Token.Register(() =>
        {
            lock (_pendingLock) _pendingReads.Remove(key);
            tcs.TrySetException(new TimeoutException(
                $"Sem SettingValue p/ field {(byte)id} em {timeout.TotalMilliseconds}ms"));
        }))
        {
            return await tcs.Task;
        }
    }

    public Task SendCommandAsync(WheelCommandId command, byte arg = 0) =>
        _channel.WriteAsync(HidBaseTransport.Frame(
            WheelReportIds.Command, new CommandReport((byte)command, arg).ToBytes()));

    public Task SendLedAsync(WheelLedReport led) =>
        _channel.WriteAsync(HidBaseTransport.Frame(WheelReportIds.Led, led.ToBytes()));

    public async Task<WheelLedReport> ReadLedsAsync()
    {
        var tcs = new TaskCompletionSource<WheelLedReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) _pendingLedRead = tcs;

        // Pede as cores ao aro (comando 0x02); ele responde com o report 0x19 (tratado em OnReport).
        await _channel.WriteAsync(HidBaseTransport.Frame(
            WheelReportIds.Command, new CommandReport((byte)WheelCommandId.RequestLeds, 0).ToBytes()));

        using var cts = new CancellationTokenSource(DefaultReadTimeout);
        using (cts.Token.Register(() =>
        {
            lock (_pendingLock) { if (ReferenceEquals(_pendingLedRead, tcs)) _pendingLedRead = null; }
            tcs.TrySetException(new TimeoutException(
                $"Sem LedValue (0x19) em {DefaultReadTimeout.TotalMilliseconds}ms"));
        }))
        {
            return await tcs.Task;
        }
    }

    private void OnReport(object? sender, byte[] wire)
    {
        if (wire.Length < 1 + ReportConstants.ReportSize)
            return;

        try
        {
            var reportId = wire[0];
            var payload = wire.AsSpan(1, ReportConstants.ReportSize);

            if (reportId == WheelReportIds.State)
            {
                var state = WheelState.Parse(payload);
                FirmwareVersion = state.Firmware;
                StateReceived?.Invoke(this, state);
            }
            else if (reportId == WheelReportIds.SettingValue)
            {
                var report = SettingReport.Parse(payload);
                TaskCompletionSource<SettingValue>? tcs;
                lock (_pendingLock)
                {
                    _pendingReads.Remove((report.FieldId, report.Index), out tcs);
                }
                tcs?.TrySetResult(report.Value);
            }
            else if (reportId == WheelReportIds.LedValue)
            {
                var led = WheelLedReport.Parse(payload);
                TaskCompletionSource<WheelLedReport>? tcs;
                lock (_pendingLock) { tcs = _pendingLedRead; _pendingLedRead = null; }
                tcs?.TrySetResult(led);
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
