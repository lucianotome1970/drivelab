// ============================================================================
//  DriveLab
//  SettingType.cs — Enum dos tipos de dado de um setting (UInt8, Int8, UInt16, Int16, Float).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public enum SettingType : byte
{
    UInt8 = 0,
    Int8 = 1,
    UInt16 = 2,
    Int16 = 3,
    Float = 4,
}
