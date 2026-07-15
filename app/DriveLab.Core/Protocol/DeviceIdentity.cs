// ============================================================================
//  DriveLab
//  DeviceIdentity.cs — Identidade USB (VID/PID/versão de protocolo) do dispositivo DriveLab (volante).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

/// <summary>USB identity of the DriveLab device. VID/PID are pid.codes test values (dev placeholder).</summary>
public static class DeviceIdentity
{
    public const int VendorId = 0x1209;
    public const int ProductId = 0x0001;
    public const byte ProtocolVersion = 1;
}
