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

    /// <summary>Inteiro de 32 bits. Existe porque encoder magnético de alta resolução não cabe
    /// em 16 bits: o MT6835 tem 2.097.152 contagens por volta (21 bits).</summary>
    UInt32 = 5,
}
