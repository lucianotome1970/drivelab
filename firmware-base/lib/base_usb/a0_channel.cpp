// ============================================================================
//  DriveLab Firmware
//  a0_channel.cpp — Implementação do canal A0 (framing SET_REPORT, resposta
//  deferida 0x16, telemetria periódica 0x21, persistência do BaseCfg na
//  flash). Extraído do monolito src/m05/main.cpp (Task 2 do M5 Stage 0). Ao
//  contrário de a0_channel.h (puro), esta TU depende de Arduino/TinyUSB/
//  EEPROM — só compilada nos envs do PlatformIO (m05, futuro m5), nunca no
//  host (test/run.sh linka só o header, ver a0_channel.h).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "a0_channel.h"

#include <Arduino.h>
#include <Adafruit_TinyUSB.h>
#include <EEPROM.h>

#include <cstring>

#include "a0_hid_descriptor.h"
#include "sensors.h"
#include "dbg_ring.h"   // debug por ring buffer em RAM (SWD) — CDC removido

// ----------------------------------------------------------------------
// Persistência em flash — EEPROM emulada do STM32duino (Arduino_Core_STM32/
// libraries/EEPROM), ver comentário original (mantido) em main.cpp antes
// desta extração. Layout: offset 0 = magic uint32 "DLB1" (0x444C4231),
// offset 4 = BaseCfg inteiro (EEPROM.put/get cobre a struct como bytes crus).
// ----------------------------------------------------------------------
namespace
{
constexpr uint32_t kBaseFlashMagic = 0x444C4237; // "DLB7" (bump: BaseCfg ganhou powerButtonEnable)
constexpr int kBaseFlashMagicAddr = 0;
constexpr int kBaseFlashCfgAddr = kBaseFlashMagicAddr + sizeof(kBaseFlashMagic);

} // namespace

void A0Channel::begin()
{
    uint32_t magic = 0;
    EEPROM.get(kBaseFlashMagicAddr, magic);
    if (magic == kBaseFlashMagic)
    {
        EEPROM.get(kBaseFlashCfgAddr, m_cfg);
    }
    else
    {
        baseSeedDefaults(m_cfg);
    }
}

void A0Channel::save()
{
    EEPROM.put(kBaseFlashMagicAddr, kBaseFlashMagic);
    EEPROM.put(kBaseFlashCfgAddr, m_cfg);
}

// ----------------------------------------------------------------------
// Canal A0 (config): buffer[0] é o Report ID de verdade (mesmo caminho 1 —
// endpoint OUT — do FFB; ver comentário original junto de
// hid_set_report_callback em main.cpp). Layout (payload após o Report ID em
// buf[0]):
//   0x14 SETWRITE: buf[1]=fieldId buf[2]=index(0) buf[3]=type
//                  buf[4..]=value LE (contrato SettingWrite do app).
//   0x15 SETREAD:  buf[1]=fieldId buf[2]=index(0).
//   0x22 CMD:      buf[1]=cmd buf[2]=arg.
//   0x10 DIRECT:   ignorado por ora (só log) -- sem uso definido ainda.
// ----------------------------------------------------------------------
bool A0Channel::handleOutReport(const uint8_t* buf, uint16_t len)
{
    if (buf[0] == A0_RID_SETWRITE)
    {
        if (len >= 4)
        {
            uint8_t fieldId = buf[1];
            uint8_t type = buf[3];
            uint16_t valLen = len - 4;
            baseWriteField(m_cfg, fieldId, type, &buf[4], valLen);
            m_cfgDirty = true;
            drivelab::dbgRingPrintf("A0 write field=%u type=%u len=%u\n", fieldId, type, valLen);
        }
        return true;
    }

    if (buf[0] == A0_RID_SETREAD)
    {
        if (len >= 2)
        {
            m_pendingField = buf[1];
            m_pendingReadValue = true;
            drivelab::dbgRingPrintf("A0 read field=%u\n", m_pendingField);
        }
        return true;
    }

    if (buf[0] == A0_RID_CMD)
    {
        if (len >= 3)
        {
            uint8_t cmd = buf[1];
            uint8_t arg = buf[2];
            if (cmd == 2 /* SaveSettings */)
            {
                m_saveRequested = true;
                drivelab::dbgRingPrintf("A0 cmd=%u (SaveSettings) arg=%u -> m_saveRequested\n", cmd, arg);
            }
            else if (cmd == 4 /* EnterDfu */)
            {
                // Só sinaliza -- o salto de verdade acontece no loop() do
                // chamador (nunca aqui dentro do callback SET_REPORT da
                // pilha USB -- nada de trabalho pesado/irreversível dentro
                // do contexto de interrupção/callback do TinyUSB).
                m_dfuRequested = true;
                drivelab::dbgRingPrintf("A0 EnterDfu\n");
            }
            else if (cmd == 3 /* ResetCenter */)
            {
                // BaseCommand.ResetCenter: só marca — o m5 captura a posição atual do encoder como centro
                // fora deste callback USB (nada de trabalho no contexto do SET_REPORT do TinyUSB).
                m_centerRequested = true;
                drivelab::dbgRingPrintf("A0 cmd=%u (ResetCenter) -> m_centerRequested\n", cmd);
            }
            else if (cmd == 6 /* SetForceEnabled */)
            {
                // BaseCommand.SetForceEnabled (app/DriveLab.Core/Transport/
                // BaseCommand.cs) -- arg!=0 habilita. M0.5 não tem motor:
                // só guarda o estado (usado pelo M5, ver forceEnabled()).
                m_forceEnabled = (arg != 0);
                drivelab::dbgRingPrintf("A0 cmd=%u (SetForceEnabled) arg=%u -> forceEnabled=%u\n",
                                      cmd, arg, m_forceEnabled ? 1U : 0U);
            }
            else if (cmd == 5 /* Calibrate (Stage 1a: initFOC do motor) */)
            {
                // BaseCommand.Calibrate: só marca — o m5 roda o Stage 1a (open-loop) fora deste callback USB.
                // arg = tensão open-loop em décimos de volt (ex.: 15 = 1,5V) → ajustável sem reflash. Deliberado.
                m_calibrateRequested = true;
                m_calibrateArg = arg;
                drivelab::dbgRingPrintf("A0 cmd=5 (Calibrate) arg=%u -> Stage 1a (%u.%uV)\n", arg, arg/10, arg%10);
            }
            else if (cmd == 7 /* CalibrateCogging */)
            {
                // BaseCommand.CalibrateCogging: só marca — o m5 dispara a rotina de calibração fora deste
                // callback USB. Só roda de fato no Stage 1 (com motor); motor-OFF apenas registra o pedido.
                m_coggingCalibRequested = true;
                drivelab::dbgRingPrintf("A0 cmd=%u (CalibrateCogging) -> m_coggingCalibRequested\n", cmd);
            }
            else
            {
                drivelab::dbgRingPrintf("A0 cmd=%u arg=%u (sem handler ainda)\n", cmd, arg);
            }
        }
        return true;
    }

    if (buf[0] == A0_RID_DIRECT)
    {
        // Report DIRECT (0x10): payload espelha BaseDirectControl. Só usamos, por ora, a força ADITIVA de
        // efeitos por telemetria (int16 em [9..10] do payload = buf[10..11], depois do report id em buf[0]).
        if (len >= 12)
        {
            m_telemetryForce = (int16_t)((uint16_t)buf[10] | ((uint16_t)buf[11] << 8));
            m_hasNewDirect = true;
        }
        return true;
    }

    return false;
}

bool A0Channel::dfuRequested()
{
    if (m_dfuRequested)
    {
        m_dfuRequested = false;
        return true;
    }
    return false;
}

// ----------------------------------------------------------------------
// Prioridade dos dois sends do EP IN compartilhado (mesmo achado de bancada
// documentado no fix P0/HID EP -- só dá pra ter um sendReport() "no ar" por
// vez): resposta deferida do 0x15 via 0x16 primeiro (só existe quando o host
// pediu uma leitura -- prioridade alta, é uma ação de UI aguardando reply),
// telemetria periódica 0x21 depois, só se o endpoint ainda estiver livre.
// `sender` (wrapper de g_hid.sendReport() em torno de g_hid.ready()) só
// atualiza estado (limpa m_pendingReadValue / avança m_lastStateSendMs)
// quando o envio realmente aconteceu -- se o EP estava ocupado, tenta de
// novo na próxima iteração do loop().
// ----------------------------------------------------------------------
void A0Channel::serviceLoop(uint32_t nowMs, bool (*sender)(uint8_t, const uint8_t*, uint16_t))
{
    if (m_pendingReadValue)
    {
        uint8_t type = 0;
        uint8_t val[8] = {0};
        int n = baseReadField(m_cfg, m_pendingField, &type, val);

        // Payload do Input (SEM o Report ID -- o sender antepõe sozinho,
        // igual ao RID_JOYSTICK): [0]=fieldId [1]=index(0) [2]=type
        // [3..]=value LE (contrato SettingValue do app).
        uint8_t payload[kA0PayloadLen] = {0};
        payload[0] = m_pendingField;
        payload[1] = 0;
        payload[2] = type;
        if (n > 0)
        {
            memcpy(&payload[3], val, n);
        }

        if (sender(A0_RID_SETVALUE, payload, sizeof(payload)))
        {
            m_pendingReadValue = false;
            drivelab::dbgRingPrintf("A0 reply field=%u type=%u len=%d\n", m_pendingField, type, n);
        }
    }

    if (nowMs - m_lastStateSendMs >= 15)
    {
        uint8_t payload[kA0PayloadLen];
        uint16_t plen = buildDeviceStatePayload(payload, sensorMcuTempC(), m_clipping,
                                                m_wheelPos, m_wheelAngleDeci, m_busVoltageMv, m_flags,
                                                m_fetTempC, m_motorTempC, m_fetNtcRaw);

        if (sender(A0_RID_STATE, payload, plen))
        {
            m_lastStateSendMs = nowMs;
        }
    }
}
