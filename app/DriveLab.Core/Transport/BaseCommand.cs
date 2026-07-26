// ============================================================================
//  DriveLab
//  BaseCommand.cs — Enum dos comandos de dispositivo do volante (reboot, salvar settings, calibrar, DFU, etc.).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Transport;

public enum BaseCommand : byte
{
    Reboot = 1,
    SaveSettings = 2,
    ResetCenter = 3,
    EnterDfu = 4,
    Calibrate = 5,
    SetForceEnabled = 6,

    /// <summary>Dispara a calibração de cogging por-motor (giro lento medindo o ripple, grava a tabela na
    /// flash do dispositivo). Exige motor energizado — só roda na bancada (Stage 1). Só o criador tem acesso
    /// (botão na aba Hardware). Ver coggingCalibRequested no firmware.</summary>
    CalibrateCogging = 7,
}
