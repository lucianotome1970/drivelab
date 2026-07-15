// ============================================================================
//  DriveLab
//  HidSharpChannel.cs — Canal HID real via HidSharp, com thread de leitura contínua dos reports.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using HidSharp;

namespace DriveLab.Hid;

/// <summary>
/// Real HID I/O via HidSharp. NOTE: exercised only against physical hardware — there is no
/// unit test for the USB path; the transport logic is tested via IHidChannel/FakeHidChannel.
/// </summary>
public sealed class HidSharpChannel : IHidChannel
{
    private HidStream? _stream;
    private Thread? _readThread;
    private volatile bool _running;

    public bool IsOpen => _stream != null;

    public event EventHandler<byte[]>? ReportReceived;

    public Task<bool> OpenAsync(int vendorId, int productId)
    {
        var device = DeviceList.Local.GetHidDevices(vendorId, productId).FirstOrDefault();
        if (device == null || !device.TryOpen(out var stream))
            return Task.FromResult(false);

        _stream = stream;
        _stream.ReadTimeout = Timeout.Infinite;
        _running = true;
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "DriveLab-HID-Read" };
        _readThread.Start();
        return Task.FromResult(true);
    }

    public void Close()
    {
        _running = false;
        _stream?.Close();
        _stream = null;
    }

    public Task WriteAsync(byte[] wireReport)
    {
        _stream?.Write(wireReport);
        return Task.CompletedTask;
    }

    private void ReadLoop()
    {
        var stream = _stream;
        if (stream == null) return;
        var buffer = new byte[stream.Device.GetMaxInputReportLength()];
        while (_running)
        {
            int count;
            try { count = stream.Read(buffer, 0, buffer.Length); }
            catch { break; }
            if (count <= 0) continue;
            var report = new byte[count];
            Array.Copy(buffer, report, count);
            ReportReceived?.Invoke(this, report);
        }
    }

    public void Dispose() => Close();
}
