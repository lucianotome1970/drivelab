// ============================================================================
//  DriveLab
//  ReportConstants.cs — Constante de tamanho fixo dos reports HID do protocolo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

public static class ReportConstants
{
    // 63 (não 64): o report vendor vai enquadrado por Report ID, e o buffer de endpoint HID
    // do TinyUSB (CFG_TUD_HID_EP_BUFSIZE) é 64 = 1 (id) + 63 (dados). Com 64 dados, o report de
    // 65B não caberia e o firmware não conseguiria enviar telemetria/leitura de settings.
    public const int ReportSize = 63;
}
