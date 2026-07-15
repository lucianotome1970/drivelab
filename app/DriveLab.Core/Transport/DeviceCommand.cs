// ============================================================================
//  DriveLab
//  DeviceCommand.cs — Enum dos comandos de dispositivo do volante (reboot, salvar settings, calibrar, DFU, etc.).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Transport;

public enum DeviceCommand : byte
{
    Reboot = 1,
    SaveSettings = 2,
    ResetCenter = 3,
    EnterDfu = 4,
    Calibrate = 5,
    SetForceEnabled = 6,
}
