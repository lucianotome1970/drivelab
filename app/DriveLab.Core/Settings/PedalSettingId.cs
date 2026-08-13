// ============================================================================
//  DriveLab
//  PedalSettingId.cs — IDs dos settings configuráveis de um pedal.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public enum PedalSettingId : byte
{
    SensorType = 0,
    InputMin = 1,
    InputMax = 2,
    Invert = 3,
    Smooth = 4,
    CurvePoint0 = 5,
    CurvePoint1 = 6,
    CurvePoint2 = 7,
    CurvePoint3 = 8,
    CurvePoint4 = 9,
    CurvePoint5 = 10,
    LoadCellScale = 11,
    DeadzoneLow = 12,
    DeadzoneHigh = 13,
}
