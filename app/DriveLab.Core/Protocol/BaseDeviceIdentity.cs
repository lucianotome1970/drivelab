// ============================================================================
//  DriveLab
//  BaseDeviceIdentity.cs — Identidade USB (VID/PID/versão de protocolo) do dispositivo DriveLab (volante).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

/// <summary>USB identity of the DriveLab device. VID/PID are pid.codes test values (dev placeholder).</summary>
public static class BaseDeviceIdentity
{
    public const int VendorId = 0x1209;
    // Precisa casar com USB_PID em firmware-base/src/usb_descriptors.c — se as duas pontas
    // divergirem, o app simplesmente nao acha a base. Em 20/08/2026 os dois foram para 0x0010
    // enquanto cacavamos os travamentos de USB (identidade nova = Windows sem cache antigo);
    // terminado o teste, voltaram juntos para 0x0001, porque a identidade nova obrigava a
    // remapear o volante em todos os jogos.
    public const int ProductId = 0x0001;
    public const byte ProtocolVersion = 1;
}
