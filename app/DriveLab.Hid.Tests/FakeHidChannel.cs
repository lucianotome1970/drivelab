// ============================================================================
//  DriveLab
//  FakeHidChannel.cs — Fake de IHidChannel para testes, registrando writes e permitindo emitir reports simulados.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Hid;

namespace DriveLab.Hid.Tests;

public sealed class FakeHidChannel : IHidChannel
{
    public bool IsOpen { get; private set; }
    public bool OpenResult { get; set; } = true;
    public List<byte[]> Writes { get; } = new();
    public byte[]? LastWrite => Writes.Count > 0 ? Writes[^1] : null;

    public event EventHandler<byte[]>? ReportReceived;

    public Task<bool> OpenAsync(int vendorId, int productId)
    {
        IsOpen = OpenResult;
        return Task.FromResult(OpenResult);
    }

    public void Close() => IsOpen = false;
    public Task WriteAsync(byte[] wireReport) { Writes.Add(wireReport); return Task.CompletedTask; }
    public void Emit(byte[] wireReport) => ReportReceived?.Invoke(this, wireReport);
    public void Dispose() => Close();
}
