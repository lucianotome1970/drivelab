using System.Threading;
using HidSharp;

namespace DriveLab.Hid.Simagic;

/// <summary>Leitura real do HID Simagic via HidSharp (Windows). Integração — validar na bancada.
/// No macOS 26 o HidSharp não enumera (ver docs/reference/simagic-p2000-hid-protocol.md).</summary>
public sealed class SimagicHidSharpReader : ISimagicHidReader, IDisposable
{
    private HidStream? _stream;
    private Thread? _thread;
    private volatile bool _running;

    public event EventHandler<byte[]>? ReportReceived;

    public bool IsPresent() =>
        DeviceList.Local.GetHidDevices(SimagicIdentity.VendorId, SimagicIdentity.ProductId).Any();

    public bool TryOpen()
    {
        var device = DeviceList.Local
            .GetHidDevices(SimagicIdentity.VendorId, SimagicIdentity.ProductId)
            .FirstOrDefault();
        if (device is null || !device.TryOpen(out _stream))
            return false;

        _stream!.ReadTimeout = 1000;
        var len = Math.Max(1, device.GetMaxInputReportLength());
        _running = true;
        _thread = new Thread(() => ReadLoop(len)) { IsBackground = true, Name = "SimagicHidReader" };
        _thread.Start();
        return true;
    }

    private void ReadLoop(int len)
    {
        var buf = new byte[len];
        while (_running)
        {
            int n;
            try { n = _stream!.Read(buf, 0, buf.Length); }
            catch (TimeoutException) { continue; }
            catch { break; }
            if (n <= 0) continue;
            var copy = new byte[n];
            Array.Copy(buf, copy, n);
            ReportReceived?.Invoke(this, copy);
        }
    }

    public void Close()
    {
        _running = false;
        _thread?.Join(500);
        _thread = null;
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Close();
}
