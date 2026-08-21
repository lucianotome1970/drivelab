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
    // Identidade NOVA (era 0x0001). Trocar o produto foi o unico jeito de o Windows tratar a base
    // como aparelho inedito: ele guarda o que aprendeu por fabricante+produto+serie e nao rele as
    // descricoes de quem julga ja conhecer. Precisa casar com usb_descriptors.c do firmware — se as
    // duas pontas divergirem, o app simplesmente nao acha a base.
    public const int ProductId = 0x0010;
    public const byte ProtocolVersion = 1;
}
