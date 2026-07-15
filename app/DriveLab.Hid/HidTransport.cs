// ============================================================================
//  DriveLab
//  HidTransport.cs — Transporte HID real do volante DriveLab: enquadra payloads A0 e despacha reports por Report ID.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using HidSharp;

namespace DriveLab.Hid;

/// <summary>
/// Real <see cref="ITransport"/> over USB HID. Frames A0 payloads with their HID Report ID
/// and sends them through an <see cref="IHidChannel"/>. Incoming reports are dispatched by
/// report id. StateReceived is raised on the channel's read thread; consumers (DeviceSession)
/// marshal it to the UI thread.
/// </summary>
public sealed class HidTransport : ITransport, IDisposable
{
    private readonly IHidChannel _channel;
    private readonly object _pendingLock = new();
    private readonly Dictionary<byte, TaskCompletionSource<SettingValue>> _pendingReads = new();
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromMilliseconds(500);

    public HidTransport(IHidChannel channel)
    {
        _channel = channel;
        _channel.ReportReceived += OnReport;
    }

    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion { get; private set; }

    public event EventHandler<BaseState>? StateReceived;

    /// <summary>Varre o HID pelo VID/PID da base (autodetecção/hotplug). No macOS 26 o HidSharp
    /// não enumera (retorna false); no Windows funciona.</summary>
    public static bool IsDevicePresent()
    {
        try
        {
            return DeviceList.Local
                .GetHidDevices(BaseDeviceIdentity.VendorId, BaseDeviceIdentity.ProductId)
                .Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = await _channel.OpenAsync(BaseDeviceIdentity.VendorId, BaseDeviceIdentity.ProductId);
    }

    public Task DisconnectAsync()
    {
        _channel.Close();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendDirectControlAsync(BaseDirectControl control) =>
        _channel.WriteAsync(Frame(BaseReportIds.BaseDirectControl, control.ToBytes()));

    public Task SendCommandAsync(DeviceCommand command, byte arg = 0) =>
        _channel.WriteAsync(Frame(BaseReportIds.Command, new CommandReport((byte)command, arg).ToBytes()));

    public Task WriteSettingAsync(SettingId id, SettingValue value) =>
        _channel.WriteAsync(Frame(BaseReportIds.SettingWrite, new SettingReport((byte)id, 0, value).ToBytes()));

    public Task<SettingValue> ReadSettingAsync(SettingId id) => ReadSettingAsync(id, DefaultReadTimeout);

    public async Task<SettingValue> ReadSettingAsync(SettingId id, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<SettingValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) _pendingReads[(byte)id] = tcs;

        await _channel.WriteAsync(Frame(BaseReportIds.SettingReadRequest, new SettingReadRequestReport((byte)id, 0).ToBytes()));

        using var cts = new CancellationTokenSource(timeout);
        using (cts.Token.Register(() =>
        {
            lock (_pendingLock) _pendingReads.Remove((byte)id);
            tcs.TrySetException(new TimeoutException($"No SettingValue reply for field {(byte)id} within {timeout.TotalMilliseconds}ms"));
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

            if (reportId == BaseReportIds.BaseState)
            {
                var state = BaseState.Parse(payload);
                FirmwareVersion = state.Firmware;
                StateReceived?.Invoke(this, state);
            }
            else if (reportId == BaseReportIds.SettingValue)
            {
                var report = SettingReport.Parse(payload);
                TaskCompletionSource<SettingValue>? tcs;
                lock (_pendingLock)
                {
                    _pendingReads.Remove(report.FieldId, out tcs);
                }
                tcs?.TrySetResult(report.Value);
            }
        }
        catch
        {
            // Drop malformed or corrupt reports rather than crash the read thread
        }
    }

    internal static byte[] Frame(byte reportId, byte[] payload64)
    {
        var wire = new byte[1 + ReportConstants.ReportSize];
        wire[0] = reportId;
        payload64.CopyTo(wire, 1);
        return wire;
    }

    public void Dispose()
    {
        _channel.ReportReceived -= OnReport;
        _channel.Dispose();
    }
}
