// ============================================================================
//  DriveLab Firmware
//  pid_state.h — Builder PURO do byte do PID State Report (Input, RID 0x02).
//  Monta o bitfield de status do dispositivo FFB conforme o descritor HID PID
//  (ver include/ffb_hid_descriptor.h, bloco 0x85,0x02): 5 bits de status +
//  3 bits de padding constante. A ordem dos bits segue a ordem das usages no
//  descritor (0x9F Device Paused, 0xA0 Actuators Enabled, 0xA4 Safety Switch,
//  0xA6 Actuator Override Switch, 0x94 Actuator Power), Report Size 1/Count 5.
//
//  Header DELIBERADAMENTE sem dependências de Arduino/TinyUSB — mesmo padrão de
//  a0_channel.h — p/ que buildPidStateByte() seja testável no host (ver
//  test/test_pid_state.cpp) sem placa.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

// ⚠️ OS BITS SAO IDENTIFICADOS PELO CODIGO DO PROTOCOLO, NAO PELA ORDEM QUE IMAGINAMOS.
//
// O descritor declara, nesta ordem: 0x9F, 0xA0, 0xA4, 0xA6, 0x94. Pela tabela do protocolo:
//   bit0 = 0x9F  Device Paused          (dispositivo pausado)
//   bit1 = 0xA0  Actuators Enabled      (atuadores habilitados)
//   bit2 = 0xA4  Safety Switch          (chave de seguranca)
//   bit3 = 0xA6  ACTUATOR POWER         (atuadores COM ENERGIA)
//   bit4 = 0x94  EFFECT PLAYING         (efeito tocando)
//   bits5-7 = padding constante (0)
//
// Os dois ultimos estavam TROCADOS aqui: chamavamos 0xA6 de "chave de sobreposicao" e 0x94 de
// "atuadores com energia". Como mandavamos a tal chave em falso, o que saia na linha era
// ATUADORES SEM ENERGIA — e um jogo que consulta este relatorio antes de enviar forca simplesmente
// nao envia. Foi exatamente a divisao observada na bancada em 20/08/2026: ACC e AC Evo consultam e
// ficavam sem forca; AC1 e AMS2 nao consultam e funcionavam com o MESMO firmware. Perseguimos isso
// o dia inteiro no USB, no descritor e no jogo — e o erro estava em duas linhas de tradução.
inline uint8_t buildPidStateByte(bool devicePaused, bool actuatorsEnabled,
                                 bool safetySwitch, bool actuatorPower,
                                 bool effectPlaying)
{
    return (uint8_t)((devicePaused     ? 1  : 0) |
                     (actuatorsEnabled ? 2  : 0) |
                     (safetySwitch     ? 4  : 0) |
                     (actuatorPower    ? 8  : 0) |
                     (effectPlaying    ? 16 : 0));
}
