// ============================================================================
//  DriveLab
//  DeviceKind.cs — Tipos de dispositivo DriveLab, espelhando o campo `kind`
//  da assinatura de firmware (fw_signature.h) usada na atualização por USB.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Update;

/// <summary>
/// Espelha o byte `kind` de <c>FwSignature</c> (firmware-base/src/m05/fw_signature.h)
/// e o equivalente nos demais firmwares (Pedal/Handbrake/Wheel).
/// </summary>
public enum DeviceKind : byte
{
    Base = 1,
    Pedal = 2,
    Handbrake = 3,
    Wheel = 4,
}
