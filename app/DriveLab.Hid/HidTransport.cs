using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

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

    public HidTransport(IHidChannel channel)
    {
        _channel = channel;
        _channel.ReportReceived += OnReport;
    }

    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion { get; private set; }

    public event EventHandler<DeviceState>? StateReceived;

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

    // Settings are implemented in the next task.
    public Task WriteSettingAsync(SettingId id, SettingValue value) => throw new NotImplementedException();
    public Task<SettingValue> ReadSettingAsync(SettingId id) => throw new NotImplementedException();

    private void OnReport(object? sender, byte[] wire)
    {
        if (wire.Length < 1 + ReportConstants.ReportSize)
            return;
        var reportId = wire[0];
        var payload = wire.AsSpan(1, ReportConstants.ReportSize);

        if (reportId == ReportIds.DeviceState)
        {
            var state = DeviceState.Parse(payload);
            FirmwareVersion = state.Firmware;
            StateReceived?.Invoke(this, state);
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
