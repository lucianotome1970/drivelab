// ============================================================================
//  DriveLab Firmware
//  ffb_power.h — Proteção de potência: brake resistor (regen) + cortes de sobretensão/sobretemp (M2).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================
//
// Camada de PROTEÇÃO do M2 — separada da malha de força (ffb_controller.h). Ao desacelerar,
// o motor devolve energia ao barramento (regeneração) e a tensão sobe; o brake resistor a
// dissipa antes de estourar o limite. Além dele, cortes duros por sobretensão/sobretemperatura.
// Tudo puro/testável no host (o hardware — ADC/NTC/PWM — fica atrás de IPowerSense/IBrakeResistor).
#pragma once

#include "ffb_math.h"   // clampf
#include "hal.h"

namespace drivelab {

/// Limiares do brake resistor. Com histerese: liga acima de onVoltage, só desliga abaixo de
/// offVoltage; entre onVoltage e fullVoltage o duty é proporcional (0→1).
struct BrakeConfig {
    float onVoltage   = 26.0f;   ///< começa a dissipar (regen elevou a tensão)
    float fullVoltage = 30.0f;   ///< duty 100%
    float offVoltage  = 25.0f;   ///< histerese: desliga abaixo disso
};

/// Controlador do brake resistor (stateful por causa da histerese).
class BrakeController {
public:
    BrakeConfig cfg;

    /// Retorna o duty (0..1) para a tensão de barramento atual.
    float update(float busVoltage) {
        if (_on) { if (busVoltage < cfg.offVoltage) _on = false; }
        else     { if (busVoltage > cfg.onVoltage)  _on = true;  }
        if (!_on) return 0.0f;
        const float span = cfg.fullVoltage - cfg.onVoltage;
        const float duty = span > 0.0f ? (busVoltage - cfg.onVoltage) / span : 1.0f;
        return clampf(duty, 0.0f, 1.0f);
    }

    bool isOn() const { return _on; }

private:
    bool _on = false;
};

inline bool overVoltage(float busV, float limitV) { return busV > limitV; }
inline bool overTemp(float tempC, float limitC)   { return tempC > limitC; }

/// Guarda de potência: comanda o brake resistor e sinaliza FALHA em sobretensão/sobretemperatura.
/// No firmware, se `faulted`, o laço principal desliga a força (FfbController.enabled = false).
class PowerGuard {
public:
    BrakeController brake;
    float overVoltageV = 30.0f;   ///< corte duro de tensão (sistema 24V — nunca deixar disparar)
    float overTempC    = 80.0f;   ///< corte duro de temperatura (FET/motor)
    bool  faulted = false;        ///< latched — só volta com clearFault() (após investigar)

    /// Um passo da proteção: atualiza o brake resistor e avalia os cortes. Retorna o duty.
    float step(IPowerSense& power, IBrakeResistor& resistor) {
        const float v = power.busVoltage();
        const float duty = brake.update(v);
        resistor.setDuty(duty);
        if (overVoltage(v, overVoltageV) ||
            overTemp(power.mosfetTempC(), overTempC) ||
            overTemp(power.motorTempC(), overTempC)) {
            faulted = true;
        }
        return duty;
    }

    void clearFault() { faulted = false; }
};

}  // namespace drivelab
