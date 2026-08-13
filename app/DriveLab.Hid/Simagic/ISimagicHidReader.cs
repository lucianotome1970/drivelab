// ============================================================================
//  DriveLab
//  ISimagicHidReader.cs — Seam de leitura do HID Simagic, permitindo testar o transporte sem hardware.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Hid.Simagic;

/// <summary>Seam de leitura do HID Simagic — permite testar o transporte sem hardware.</summary>
public interface ISimagicHidReader
{
    bool IsPresent();
    bool TryOpen();
    void Close();
    event EventHandler<byte[]>? ReportReceived;
}
