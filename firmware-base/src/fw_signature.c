// ============================================================================
//  DriveLab
//  fw_signature.c — Assinatura embutida no binário, para o Studio validar o
//  arquivo ANTES de gravar por USB.
//
//  PARA QUE SERVE: o app procura a sequência "DRVLABFW" dentro do .bin e lê o
//  tipo de dispositivo e a versão logo depois. Sem ela, o Studio recusa o
//  arquivo ("firmware inválido") — que é o que acontecia até 2026-08-07: a
//  assinatura existia no firmware ANTIGO (src/m05/fw_signature.h) e se perdeu
//  na migração para a base ODrive. Resultado: só dava para gravar por ST-Link.
//
//  A proteção é contra o erro que estraga o dia de alguém: gravar o firmware do
//  pedal na base, ou uma imagem truncada. O app compara o byte de `kind` com o
//  dispositivo conectado e só libera o envio se baterem.
//
//  LAYOUT (espelha DriveLab.Core/Update/FirmwareFile.cs):
//      "DRVLABFW"  8 bytes  magic
//      kind        1 byte   DeviceKind: 1=Base 2=Pedal 3=Handbrake 4=Wheel
//      major       1 byte
//      minor       1 byte
//      patch       1 byte
//
//  ⚠️ `used` + `section(".rodata.fwsig")` são obrigatórios: sem eles o linker
//  descarta o array (`-ffunction-sections -fdata-sections -Wl,--gc-sections`)
//  porque ninguém o referencia, e o .bin sai sem assinatura de novo.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include <stdint.h>
#include "fw_version.h"   // fonte ÚNICA da versão — ver o cabeçalho de lá

#define FW_KIND_BASE  1u   // DeviceKind.Base

__attribute__((used, section(".rodata.fwsig")))
const uint8_t g_fw_signature[12] = {
    'D','R','V','L','A','B','F','W',
    FW_KIND_BASE,
    DRVLAB_FW_VER_MAJOR, DRVLAB_FW_VER_MINOR, DRVLAB_FW_VER_PATCH
};
