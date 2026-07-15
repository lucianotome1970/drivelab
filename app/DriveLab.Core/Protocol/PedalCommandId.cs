// ============================================================================
//  DriveLab
//  PedalCommandId.cs — IDs dos comandos suportados pela pedaleira (calibrar, salvar, carregar defaults).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

public enum PedalCommandId : byte
{
    CalibrateStart = 1,
    CalibrateStop = 2,
    SaveToFlash = 3,
    LoadDefaults = 4,
}
