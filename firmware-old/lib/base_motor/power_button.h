/*
 * power_button.h — máquina de estados do soft-power por botão (tap-liga / segura-desliga).
 *
 * Pura e host-testável (sem Arduino). O boot já latcha a energia (powerEnable=true).
 * Segurar o botão por >= holdMs pede o desligamento; a energia só é solta (powerEnable=false)
 * DEPOIS que o contator abriu (contactorOpen) + cutDelayMs, para o motor não cortar sob carga.
 * Toque curto (release antes de holdMs) é ignorado.
 *
 * Autor: Luciano Tomé
 * Licença: MIT
 */
#pragma once
#include <cstdint>

enum class PowerButtonState : uint8_t { Running = 0, ShuttingDown = 1, Cutting = 2, Cut = 3 };

class PowerButton {
public:
    explicit PowerButton(uint32_t holdMs = 2000, uint32_t cutDelayMs = 200)
        : m_holdMs(holdMs), m_cutDelayMs(cutDelayMs) {}

    void step(bool buttonDown, bool contactorOpen, uint32_t nowMs) {
        switch (m_state) {
            case PowerButtonState::Running:
                if (buttonDown) {
                    if (!m_btnPrev) {
                        m_btnT = nowMs;  // borda de descida: marca início do hold
                    } else if ((int32_t)(nowMs - m_btnT) >= (int32_t)m_holdMs) {
                        m_state = PowerButtonState::ShuttingDown;  // segurou o suficiente
                    }
                }
                // release antes do hold → toque curto: nada a fazer (m_btnPrev abaixo reseta)
                break;
            case PowerButtonState::ShuttingDown:
                // o m5 desarma o motor + pede o contator abrir; esperamos ele abrir
                if (contactorOpen) {
                    m_cutT = nowMs;
                    m_state = PowerButtonState::Cutting;
                }
                break;
            case PowerButtonState::Cutting:
                if ((int32_t)(nowMs - m_cutT) >= (int32_t)m_cutDelayMs) {
                    m_state = PowerButtonState::Cut;
                }
                break;
            case PowerButtonState::Cut:
                break;  // terminal: energia caindo
        }
        m_btnPrev = buttonDown;
    }

    bool powerEnable() const { return m_state != PowerButtonState::Cut; }
    bool shuttingDown() const { return m_state != PowerButtonState::Running; }
    PowerButtonState state() const { return m_state; }

    void reset() {
        m_state = PowerButtonState::Running;
        m_btnPrev = false;
        m_btnT = 0;
        m_cutT = 0;
    }

private:
    uint32_t m_holdMs;
    uint32_t m_cutDelayMs;
    uint32_t m_btnT = 0;
    uint32_t m_cutT = 0;
    PowerButtonState m_state = PowerButtonState::Running;
    bool m_btnPrev = false;
};
