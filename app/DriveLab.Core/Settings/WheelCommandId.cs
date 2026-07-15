// ============================================================================
//  DriveLab
//  WheelCommandId.cs — IDs dos comandos do rim (calibrar pás, salvar, carregar defaults).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public enum WheelCommandId : byte
{
    CalibrateClutchStart = 1,
    CalibrateClutchStop = 2,
    SaveToFlash = 3,
    LoadDefaults = 4,
}
