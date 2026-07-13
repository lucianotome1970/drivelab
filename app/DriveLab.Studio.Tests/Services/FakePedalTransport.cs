using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Studio.Tests.Services;

public sealed class FakePedalTransport : IPedalTransport
{
    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion { get; } = new(0, 1, 0, 0);
    public event EventHandler<PedalState>? StateReceived;

    public (PedalSettingId id, PedalIndex pedal, SettingValue value)? LastWrite { get; private set; }
    public (PedalCommandId cmd, byte arg)? LastCommand { get; private set; }
    public SettingValue ReadResult { get; set; } = new(SettingType.UInt16, 4095);

    public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }
    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
    public Task WriteSettingAsync(PedalSettingId id, PedalIndex pedal, SettingValue value) { LastWrite = (id, pedal, value); return Task.CompletedTask; }
    public Task<SettingValue> ReadSettingAsync(PedalSettingId id, PedalIndex pedal) => Task.FromResult(ReadResult);
    public Task SendCommandAsync(PedalCommandId command, byte arg = 0) { LastCommand = (command, arg); return Task.CompletedTask; }

    public void Emit(PedalState state) => StateReceived?.Invoke(this, state);
}
