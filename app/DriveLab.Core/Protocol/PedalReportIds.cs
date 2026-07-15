// ============================================================================
//  DriveLab
//  PedalReportIds.cs — IDs de report HID usados pela pedaleira (estado, comando, settings).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

public static class PedalReportIds
{
    public const byte PedalState = 0x20;
    public const byte Command = 0x02;
    public const byte SettingWrite = 0x14;
    public const byte SettingReadRequest = 0x15;
    public const byte SettingValue = 0x16;
}
