// ============================================================================
//  DriveLab
//  WheelDeviceIdentity.cs — Identidade USB (VID/PID/versão de protocolo) do volante removível (rim).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

/// <summary>USB identity do rim DriveLab. VID/PID de teste do pid.codes
/// (base = 0x0001, pedaleira = 0x0002, freio de mão = 0x0003, rim = 0x0004).
/// O firmware RP2040 enumera com Product "DriveLab Wheel" (ver firmware-wheel/).</summary>
public static class WheelDeviceIdentity
{
    public const int VendorId = 0x1209;
    public const int ProductId = 0x0004;
    public const byte ProtocolVersion = 1;
}
