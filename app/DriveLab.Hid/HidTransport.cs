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

    public event EventHandler<DeviceState>? StateReceived;

    /// <summary>Varre o HID pelo VID/PID da base (autodetecção/hotplug). No macOS 26 o HidSharp
    /// não enumera (retorna false); no Windows funciona.</summary>
    public static bool IsDevicePresent()
    {
        try
        {
            return DeviceList.Local
                .GetHidDevices(DeviceIdentity.VendorId, DeviceIdentity.ProductId)
                .Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = await _channel.OpenAsync(DeviceIdentity.VendorId, DeviceIdentity.ProductId);
    }

    public Task DisconnectAsync()
    {
        _channel.Close();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendDirectControlAsync(DirectControl control) =>
        _channel.WriteAsync(Frame(ReportIds.DirectControl, control.ToBytes()));

    public Task SendCommandAsync(DeviceCommand command, byte arg = 0) =>
        _channel.WriteAsync(Frame(ReportIds.Command, new CommandReport((byte)command, arg).ToBytes()));

    public Task WriteSettingAsync(SettingId id, SettingValue value) =>
        _channel.WriteAsync(Frame(ReportIds.SettingWrite, new SettingReport((byte)id, 0, value).ToBytes()));

    public Task<SettingValue> ReadSettingAsync(SettingId id) => ReadSettingAsync(id, DefaultReadTimeout);

    public async Task<SettingValue> ReadSettingAsync(SettingId id, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<SettingValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) _pendingReads[(byte)id] = tcs;

        await _channel.WriteAsync(Frame(ReportIds.SettingReadRequest, new SettingReadRequestReport((byte)id, 0).ToBytes()));

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

            if (reportId == ReportIds.DeviceState)
            {
                var state = DeviceState.Parse(payload);
                FirmwareVersion = state.Firmware;
                StateReceived?.Invoke(this, state);
            }
            else if (reportId == ReportIds.SettingValue)
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
