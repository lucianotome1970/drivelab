// ============================================================================
//  DriveLab
//  HidButtons.cs — Lógica PURA da fonte HID do atalho de centralizar: montar a máscara de botões a partir
//    dos botões pressionados e decidir se um dispositivo deve ser escutado. Separado do I/O do HidSharp
//    (que não tem teste unitário, como o resto do caminho USB) para poder testar sem hardware.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;

namespace DriveLab.Studio.Services;

/// <summary>Helpers puros da fonte HID (sem dependência de hardware/HidSharp).</summary>
public static class HidButtons
{
    // Usage pages / usages relevantes do HID (Generic Desktop).
    private const int PageGenericDesktop = 0x01;
    private const int UsageMouse = 0x02;
    private const int UsageKeyboard = 0x06;
    private const int UsageKeypad = 0x07;

    /// <summary>Máscara de bits a partir dos números de botão pressionados (1-based). Bit i = botão i+1.
    /// Botões fora de 1..32 são ignorados (a máscara é uint = 32 botões).</summary>
    public static uint MaskFromPressed(IEnumerable<int> pressedButtonNumbers)
    {
        uint mask = 0u;
        foreach (var n in pressedButtonNumbers)
            if (n >= 1 && n <= 32) mask |= 1u << (n - 1);
        return mask;
    }

    /// <summary>Decide se um dispositivo HID entra como fonte do atalho. Escuta QUALQUER dispositivo que
    /// exponha botões (buttonbox, gamepad, joystick, volante, vendor-defined), EXCETO o mouse/teclado/keypad
    /// do sistema (esses não são controladores de atalho; o teclado tem sua própria fonte por hook global)
    /// e EXCETO a própria BASE — ver <see cref="IsOwnBase"/>.</summary>
    public static bool ShouldListen(bool hasButtons, int topUsagePage, int topUsageId)
    {
        if (!hasButtons) return false;
        if (topUsagePage == PageGenericDesktop &&
            (topUsageId == UsageMouse || topUsageId == UsageKeyboard || topUsageId == UsageKeypad))
            return false;
        return true;
    }

    /// <summary>A BASE (VID 0x1209 / PID 0x0001) NUNCA pode ser escutada por esta fonte.
    ///
    /// Ela é um game controller com botões, então passava no <see cref="ShouldListen"/> e era aberta
    /// aqui — enquanto a BaseSession já a mantém aberta para o FFB. Dois handles disputando o mesmo
    /// endpoint HID derrubaram a base do USB na bancada (2026-08-05): o app reconhecia o dispositivo,
    /// o volante parava de responder na tela e em seguida a base sumia do "controle de jogo" do Windows.
    ///
    /// O ARO (PID 0x0004) continua valendo como fonte — ele é um dispositivo separado, e usar um botão
    /// do volante para centralizar é justamente um dos casos de uso.</summary>
    public static bool IsOwnBase(int vendorId, int productId) =>
        vendorId == DriveLab.Core.Protocol.BaseDeviceIdentity.VendorId &&
        productId == DriveLab.Core.Protocol.BaseDeviceIdentity.ProductId;
}
