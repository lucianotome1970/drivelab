// ============================================================================
//  DriveLab Firmware
//  contactor_manager.h — Máquina de estados PURA do contator fail-safe da
//  proteção off-state (isola as fases do motor quando não-armado). Sem
//  Arduino → host-testável (ver test/test_contactor_manager.cpp). O m5 chama
//  step() todo loop (gated por softPowerEnable) e aciona a bobina via GPIO
//  a partir de coilOn(); só energiza o motor quando readyToDrive().
//  Contator NO: bobina energizada = FECHADO (fases conectadas); sem energia
//  = ABERTO (fail-safe). Regra anti-arco: o chamador DESARMA o motor antes de
//  baixar driveIntent, então o contator só abre sem corrente.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once
#include <cstdint>

enum class ContactorState : uint8_t { Open = 0, Closing = 1, Closed = 2, Opening = 3 };

class ContactorManager
{
public:
    // closeMs: tempo físico p/ o contator fechar. openMs: folga antes de declarar aberto.
    explicit ContactorManager(uint32_t closeMs = 30, uint32_t openMs = 10)
        : m_closeMs(closeMs), m_openMs(openMs) {}

    void step(bool driveIntent, bool faulted, uint32_t nowMs)
    {
        switch (m_state)
        {
            case ContactorState::Open:
                if (driveIntent && !faulted) { m_state = ContactorState::Closing; m_t = nowMs; }
                break;
            case ContactorState::Closing:
                if (!driveIntent || faulted) { m_state = ContactorState::Opening; m_t = nowMs; }
                else if ((int32_t)(nowMs - m_t) >= (int32_t)m_closeMs) m_state = ContactorState::Closed;
                break;
            case ContactorState::Closed:
                if (!driveIntent || faulted) { m_state = ContactorState::Opening; m_t = nowMs; }
                break;
            case ContactorState::Opening:
                if ((int32_t)(nowMs - m_t) >= (int32_t)m_openMs) m_state = ContactorState::Open;
                break;
        }
    }

    bool coilOn() const { return m_state == ContactorState::Closing || m_state == ContactorState::Closed; }
    bool readyToDrive() const { return m_state == ContactorState::Closed; }
    ContactorState state() const { return m_state; }
    void reset() { m_state = ContactorState::Open; }

private:
    uint32_t m_closeMs, m_openMs;
    uint32_t m_t = 0;
    ContactorState m_state = ContactorState::Open;
};
