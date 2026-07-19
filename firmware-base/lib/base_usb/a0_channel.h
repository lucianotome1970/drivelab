// ============================================================================
//  DriveLab Firmware
//  a0_channel.h — Canal de configuração "A0" (vendor, ver a0_hid_descriptor.h)
//  extraído do monolito src/m05/main.cpp: framing de SET_REPORT (SETWRITE/
//  SETREAD/CMD/DIRECT), resposta deferida de leitura (0x16), telemetria
//  periódica DeviceState (0x21) e persistência do BaseCfg na flash. Reusável
//  por futuros firmwares (M5) sem duplicar a lógica.
//
//  Este header é DELIBERADAMENTE livre de dependências de Arduino/TinyUSB
//  (só inclui base_cfg.h e fw_signature.h, ambos puros — mesmo padrão de
//  sensor_convert.h vs sensors.h) para que buildDeviceStatePayload() (a
//  função de framing pura, usada pela telemetria 0x21) seja testável no
//  host (ver test/test_a0_channel.cpp) sem precisar linkar a0_channel.cpp
//  (que SIM depende de Arduino.h/Adafruit_TinyUSB.h/EEPROM.h — só compilado
//  nos envs do PlatformIO, nunca no host).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

#include "base_cfg.h"
#include "fw_signature.h"

// Tamanho do payload de cada report A0 (Input/Output) — payload + Report ID
// = 64 bytes, cabe no endpoint HID (ver a0_hid_descriptor.h, Report Count 63).
static constexpr uint16_t kA0PayloadLen = 63;

class A0Channel
{
public:
    // Carrega o BaseCfg da flash (magic+struct via EEPROM emulada, ver
    // a0_channel.cpp); se a flash estiver vazia/com magic errado, semeia os
    // defaults do schema (baseSeedDefaults). Chamar uma vez em setup().
    void begin();

    // Chamada do hid_set_report_callback para TODOS os reports que chegam
    // pelo endpoint OUT (buffer[0] = Report ID de verdade). Trata os 4
    // Report IDs do canal A0 (SETWRITE 0x14 / SETREAD 0x15 / CMD 0x22 /
    // DIRECT 0x10). Retorna true se consumiu o report (era A0 — o chamador
    // não deve tratar mais nada); false se não é A0 (o chamador deve seguir
    // para ffb_parse_out, ex.: FFB_SET_CONSTANT_FORCE etc.).
    bool handleOutReport(const uint8_t* buf, uint16_t len);

    // Chamada uma vez por iteração do loop(): envia a resposta deferida de
    // leitura (A0_RID_SETVALUE / 0x16, se houver uma pendente de um
    // A0_RID_SETREAD anterior) e, se o intervalo já passou, a telemetria
    // periódica DeviceState (A0_RID_STATE / 0x21). `sender` é o wrapper do
    // chamador em torno de g_hid.sendReport() (deve devolver false sem
    // efeito colateral se o endpoint IN ainda estiver ocupado — mesma regra
    // "um sendReport() por janela de EP" documentada no fix P0/HID EP).
    void serviceLoop(uint32_t nowMs, bool (*sender)(uint8_t reportId, const uint8_t* payload, uint16_t len));

    // A0_RID_CMD cmd=2 (SaveSettings) só seta esta flag dentro de
    // handleOutReport() — a escrita de fato na flash (save(), abaixo) fica a
    // cargo do chamador, fora do contexto do callback SET_REPORT da pilha
    // USB (mesma regra do EnterDfu: nada de trabalho pesado/irreversível
    // dentro do callback).
    bool saveRequested() const { return m_saveRequested; }
    void clearSave() { m_saveRequested = false; }

    // Persiste o BaseCfg atual na flash (magic+struct via EEPROM emulada).
    // Chamar do loop() quando saveRequested() for true, ANTES de clearSave().
    void save();

    // A0_RID_CMD cmd=4 (EnterDfu): consome a flag (só dispara uma vez por
    // pedido) e devolve true — o chamador (loop()) deve então acionar o
    // salto de verdade pro bootloader (fora deste módulo: envolve o magic em
    // RAM .noinit + NVIC_SystemReset(), mecanismo específico do main.cpp).
    bool dfuRequested();

    // Estado do A0_RID_CMD cmd=6 (SetForceEnabled) — usado pelo M5 (motor)
    // para saber se deve aplicar força. M0.5 não tem motor: só guarda o
    // estado (inerte aqui). Default true no boot (nenhum comando recebido
    // ainda == força habilitada, mesmo default implícito do M5).
    bool forceEnabled() const { return m_forceEnabled; }

    // Monta o payload do report DeviceState (A0_RID_STATE / 0x21) — função
    // PURA (sem tocar hardware), espelha byte-a-byte
    // app/DriveLab.Core/Protocol/BaseState.cs (ToBytes/Parse):
    //   [0..3]   FirmwareVersion (ReleaseType=0/dev, Major, Minor, Patch)
    //   [4]      flags (BaseFlags) — 0 (nenhuma flag definida ainda)
    //   [5..12]  Position/AngleDeciDeg/Torque/MotorCurrentMa — adiado (M1)
    //   [13]     FetTempC — adiado (M1, pinos do clone MKS divergem)
    //   [14]     ErrorCode — 0
    //   [15..16] BusVoltageMv — adiado (M1)
    //   [17]     MotorTempC — adiado (M1)
    //   [18]     McuTempC — único sensor real do M0.5 (sensorMcuTempC())
    //   [19..62] reservado — zerado.
    // `out` deve ter >= kA0PayloadLen (63) bytes. Retorna o tamanho do
    // payload escrito (sempre kA0PayloadLen).
    static uint16_t buildDeviceStatePayload(uint8_t* out, int8_t mcuTempC)
    {
        for (uint16_t i = 0; i < kA0PayloadLen; ++i)
        {
            out[i] = 0;
        }
        out[0] = 0; // FirmwareVersion.ReleaseType (0 = dev)
        out[1] = DRVLAB_FW_VER_MAJOR;
        out[2] = DRVLAB_FW_VER_MINOR;
        out[3] = DRVLAB_FW_VER_PATCH;
        out[4] = 0;  // flags
        out[13] = 0; // FetTempC — adiado (M1)
        out[14] = 0; // ErrorCode
        out[15] = 0; // BusVoltageMv (lo) — adiado (M1)
        out[16] = 0; // BusVoltageMv (hi) — adiado (M1)
        out[18] = static_cast<uint8_t>(mcuTempC);
        return kA0PayloadLen;
    }

private:
    BaseCfg m_cfg{};

    bool m_pendingReadValue = false;
    uint8_t m_pendingField = 0;

    bool m_saveRequested = false;
    bool m_dfuRequested = false;
    bool m_forceEnabled = true;

    uint32_t m_lastStateSendMs = 0;
};
