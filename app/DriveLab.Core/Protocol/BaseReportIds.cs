// ============================================================================
//  DriveLab
//  BaseReportIds.cs — IDs de report HID usados pelo volante (estado, comando, controle direto, settings).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

public static class BaseReportIds
{
    public const byte DeviceState = 0x01;
    public const byte Command = 0x02;
    public const byte DirectControl = 0x10;
    public const byte SettingWrite = 0x14;
    public const byte SettingReadRequest = 0x15;
    public const byte SettingValue = 0x16;
}
