// ============================================================================
//  DriveLab
//  WheelReportIds.cs — IDs de report HID do rim (telemetria de botões/eixos, comando, LED, settings).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

public static class WheelReportIds
{
    public const byte State = 0x21;
    public const byte Command = 0x02;
    public const byte Led = 0x18;
    public const byte LedValue = 0x19;   // resposta (device→host) da leitura das cores pré-definidas
    public const byte SettingWrite = 0x14;
    public const byte SettingReadRequest = 0x15;
    public const byte SettingValue = 0x16;
}
