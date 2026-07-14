using DriveLab.Hid.Simagic;

namespace DriveLab.Hid.Tests.Simagic;

public sealed class FakeSimagicReader : ISimagicHidReader
{
    public bool Present { get; set; } = true;
    public bool Opened { get; private set; }
    public event EventHandler<byte[]>? ReportReceived;

    public bool IsPresent() => Present;
    public bool TryOpen() { Opened = true; return true; }
    public void Close() => Opened = false;
    public void Emit(byte[] report) => ReportReceived?.Invoke(this, report);
}
