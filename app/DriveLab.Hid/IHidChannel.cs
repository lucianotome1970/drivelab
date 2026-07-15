// ============================================================================
//  DriveLab
//  IHidChannel.cs — Interface de I/O sobre um dispositivo HID bruto (wire report = Report ID + payload de 64 bytes).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Hid;

/// <summary>
/// I/O seam over a raw HID device. A wire report is the full HID frame: byte[0] is the
/// HID Report ID, bytes[1..] the 64-byte payload. Implemented by HidSharpChannel (real
/// hardware) and FakeHidChannel (tests).
/// </summary>
public interface IHidChannel : IDisposable
{
    bool IsOpen { get; }
    event EventHandler<byte[]>? ReportReceived;
    Task<bool> OpenAsync(int vendorId, int productId);
    void Close();
    Task WriteAsync(byte[] wireReport);
}
