// ============================================================================
//  DriveLab
//  HandbrakeDeviceIdentity.cs — Identidade USB (VID/PID/versão de protocolo) do freio de mão DriveLab.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

/// <summary>USB identity do freio de mão DriveLab. VID/PID de teste do pid.codes
/// (volante = 0x0001, pedaleira = 0x0002). Firmware enumera como "DriveLab Handbrake".</summary>
public static class HandbrakeDeviceIdentity
{
    public const int VendorId = 0x1209;
    public const int ProductId = 0x0003;
    public const byte ProtocolVersion = 1;
}
