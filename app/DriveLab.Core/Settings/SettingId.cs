// ============================================================================
//  DriveLab
//  SettingId.cs — IDs dos settings configuráveis do volante (force feedback, encoder, corrente, etc.).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public enum SettingId : byte
{
    MotionRange = 0,
    SoftStopRange = 1,
    SoftStopStrength = 2,
    TotalStrength = 3,
    SpringStrength = 4,
    DamperStrength = 5,
    StaticDamping = 6,
    MaxTorqueLimit = 7,
    ForceDirection = 8,
    EncoderDirection = 9,
    EncoderCpr = 10,
    PolePairs = 11,
    CurrentP = 12,
    CurrentI = 13,
    CalibrationCurrent = 14,
    PositionSmoothing = 15,
    PowerLimit = 16,
    BrakingLimit = 17,
    EncoderType = 18,
}
