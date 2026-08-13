// ============================================================================
//  DriveLab
//  WheelSettingId.cs — IDs dos settings configuráveis do rim (calibração das pás de embreagem, LED).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public enum WheelSettingId : byte
{
    ClutchLeftMin = 0,
    ClutchLeftMax = 1,
    ClutchRightMin = 2,
    ClutchRightMax = 3,
    ClutchInvertLeft = 4,
    ClutchInvertRight = 5,
    ClutchMode = 6,        // 0 = combinado, 1 = independente
    ClutchBitePoint = 7,   // 0..100 (%)
    LedBrightness = 8,     // 0..255
    LedCount = 9,
}
