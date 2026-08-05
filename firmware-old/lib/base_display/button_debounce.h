// ============================================================================
//  DriveLab Firmware
//  button_debounce.h — Debounce de botão + detecção de borda de clique. Puro/host-testável.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Um botão mecânico "chia" (bounce) por alguns ms ao pressionar/soltar. Este debouncer só COMMITA a
// mudança de estado depois que a leitura fica estável por `debounceMs`, e devolve true UMA vez por
// clique confirmado (borda de "pressionado"). Sem hardware — a leitura crua vem de fora. Host-testável.
#pragma once

#include <cstdint>

namespace drivelab {

struct ButtonDebounce {
    /// Alimenta a leitura CRUA (true = pressionado) + o tempo atual (ms). Retorna true exatamente uma vez
    /// por clique confirmado (transição estável solto→pressionado).
    bool update(bool raw, uint32_t nowMs, uint32_t debounceMs = 30) {
        if (raw != _lastRaw) { _lastRaw = raw; _lastChangeMs = nowMs; }   // reinicia a janela de estabilidade
        if ((nowMs - _lastChangeMs) >= debounceMs && raw != _stable) {
            _stable = raw;
            if (_stable) return true;   // borda confirmada de "pressionado"
        }
        return false;
    }

    bool pressed() const { return _stable; }
    void reset() { _stable = false; _lastRaw = false; _lastChangeMs = 0; }

private:
    bool _stable = false;         ///< estado debounced (true = pressionado)
    bool _lastRaw = false;        ///< última leitura crua
    uint32_t _lastChangeMs = 0;   ///< quando a leitura crua mudou pela última vez
};

}  // namespace drivelab
