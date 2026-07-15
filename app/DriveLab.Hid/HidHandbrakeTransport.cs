// ============================================================================
//  DriveLab
//  HidHandbrakeTransport.cs — Transporte HID real do freio de mão DriveLab (RP2040), espelha HidPedalTransport.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using HidSharp;

namespace DriveLab.Hid;

/// <summary>
/// <see cref="IHandbrakeTransport"/> real sobre USB HID para o freio de mão DriveLab (RP2040, firmware P0).
/// Espelha <see cref="HidPedalTransport"/>, mas de eixo único: o byte de índice do wire é sempre 0.
/// </summary>
public sealed class HidHandbrakeTransport : IHandbrakeTransport, IDisposable
{
    private readonly IHidChannel _channel;
    private readonly object _pendingLock = new();
    private readonly Dictionary<(byte field, byte index), TaskCompletionSource<SettingValue>> _pendingReads = new();
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromMilliseconds(500);

    public HidHandbrakeTransport(IHidChannel channel)
    {
        _channel = channel;
        _channel.ReportReceived += OnReport;
    }

    public bool IsConnected { get; private set; }
    public bool SupportsConfig => true;
    public FirmwareVersion FirmwareVersion { get; private set; }

    public event EventHandler<PedalState>? StateReceived;

    /// <summary>Varre o HID por VID/PID do nosso freio de mão (autodetecção). No macOS 26 o HidSharp
    /// não enumera (cai p/ simulador); no Windows funciona.</summary>
    public static bool IsDevicePresent()
    {
        try
        {
            return DeviceList.Local
                .GetHidDevices(HandbrakeDeviceIdentity.VendorId, HandbrakeDeviceIdentity.ProductId)
                .Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = await _channel.OpenAsync(HandbrakeDeviceIdentity.VendorId, HandbrakeDeviceIdentity.ProductId);
    }

    public Task DisconnectAsync()
    {
        _channel.Close();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task WriteSettingAsync(HandbrakeSettingId id, SettingValue value) =>
        _channel.WriteAsync(HidBaseTransport.Frame(
            PedalReportIds.SettingWrite, new SettingReport((byte)id, 0, value).ToBytes()));

    public Task<SettingValue> ReadSettingAsync(HandbrakeSettingId id) =>
        ReadSettingAsync(id, DefaultReadTimeout);

    public async Task<SettingValue> ReadSettingAsync(HandbrakeSettingId id, TimeSpan timeout)
    {
        var key = ((byte)id, (byte)0);
        var tcs = new TaskCompletionSource<SettingValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) _pendingReads[key] = tcs;

        await _channel.WriteAsync(HidBaseTransport.Frame(
            PedalReportIds.SettingReadRequest, new SettingReadRequestReport((byte)id, 0).ToBytes()));

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
