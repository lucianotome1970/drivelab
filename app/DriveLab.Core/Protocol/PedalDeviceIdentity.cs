// ============================================================================
//  DriveLab
//  PedalDeviceIdentity.cs — Identidade USB (VID/PID/versão de protocolo) da pedaleira DriveLab.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

/// <summary>USB identity da pedaleira DriveLab. VID/PID de teste do pid.codes (volante = 0x0001).
/// O firmware RP2040 enumera com Product "DriveLab Pedal" (ver firmware-pedal/).</summary>
public static class PedalDeviceIdentity
{
    public const int VendorId = 0x1209;
    public const int ProductId = 0x0002;
    public const byte ProtocolVersion = 1;
}
