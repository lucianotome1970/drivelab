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

    /// <summary>Erros de I/O HID (engolidos p/ não derrubar o app) — o app pode plugar um log aqui.</summary>
    public static Action<string, Exception>? OnError;

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
        var stream = _stream;
        if (stream == null)
            return Task.CompletedTask;

        // Escreve fora da thread de UI e blinda contra falhas: uma escrita HID que lança
        // (device sumiu, tamanho, I/O) NUNCA pode derrubar o app. O Windows exige o buffer
        // no tamanho EXATO do output report (com o report id no byte 0) — ajusta se preciso.
        return Task.Run(() =>
        {
            try
            {
                var buffer = wireReport;
                var max = stream.Device.GetMaxOutputReportLength();
                if (max > 0 && wireReport.Length != max)
                {
                    buffer = new byte[max];
                    Array.Copy(wireReport, buffer, Math.Min(wireReport.Length, max));
                }
                stream.Write(buffer);
            }
            catch (Exception ex)
            {
                // Falha de escrita não é fatal: o próximo write/telemetria segue. Registra o motivo.
                OnError?.Invoke("HID write failed", ex);
            }
        });
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
            catch (Exception ex) { OnError?.Invoke("HID read loop ended", ex); break; }
            if (count <= 0) continue;
            var report = new byte[count];
            Array.Copy(buffer, report, count);
            try { ReportReceived?.Invoke(this, report); }
            catch { /* um handler/parse ruim não pode matar a read-thread (=processo) */ }
        }
    }

    public void Dispose() => Close();
}
