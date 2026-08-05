// ============================================================================
//  DriveLab Firmware
//  ffb_power.h — Proteção de potência: brake resistor (regen) + cortes de sobretensão/sobretemp (M2).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
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
///
/// resistanceOhm/maxCurrentA são o equivalente ao `brake_resistance` da ODrive stock + a policy de
/// "não empurrar regen pra fonte" (max_regen_current=0, implícito num chopper por tensão): o resistor
/// dissipa tudo, mas o duty é LIMITADO para a corrente de pico (busV·duty/R) não passar de maxCurrentA —
/// protege o MOSFET AUX e o resistor. Se o clamp impedir de segurar o bus, o corte duro (overVoltageV)
/// desabilita o motor — cascata de segurança correta.
struct BrakeConfig {
    float onVoltage     = 26.0f;   ///< começa a dissipar (regen elevou a tensão)
    float fullVoltage   = 30.0f;   ///< duty 100%
    float offVoltage    = 25.0f;   ///< histerese: desliga abaixo disso
    float resistanceOhm = 2.0f;    ///< ohms do resistor (= brake_resistance) — p/ limitar a corrente de pico
    float maxCurrentA   = 0.0f;    ///< limite de corrente de pico no resistor/MOSFET (0 = sem limite)
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
        float duty = span > 0.0f ? (busVoltage - cfg.onVoltage) / span : 1.0f;
        duty = clampf(duty, 0.0f, 1.0f);
        // Clamp de corrente (brake_resistance): I_pico = busV·duty/R ≤ maxCurrentA → duty ≤ maxCurrentA·R/busV.
        if (cfg.maxCurrentA > 0.0f && cfg.resistanceOhm > 0.0f && busVoltage > 0.0f) {
            const float maxDuty = cfg.maxCurrentA * cfg.resistanceOhm / busVoltage;
            if (duty > maxDuty) duty = maxDuty;
        }
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
    float overVoltageV   = 30.0f;   ///< corte duro de tensão (sistema 24V — nunca deixar disparar)
    // Cortes de temperatura SEPARADOS: o NTC dos FETs mede a placa (limite ~85°C) e o do motor mede o
    // enrolamento (limite conforme a classe de isolamento — B/F/H). Sensor ausente devolve -128 → nunca dispara.
    float fetOverTempC   = 85.0f;   ///< corte duro do NTC dos FETs (placa)
    float motorOverTempC = 100.0f;  ///< corte duro do NTC do motor (enrolamento)
    bool  faulted = false;          ///< latched — só volta com clearFault() (após investigar)

    /// Um passo da proteção: atualiza o brake resistor e avalia os cortes. Retorna o duty.
    float step(IPowerSense& power, IBrakeResistor& resistor) {
        const float v = power.busVoltage();
        const float duty = brake.update(v);
        resistor.setDuty(duty);
        if (overVoltage(v, overVoltageV) ||
            overTemp(power.mosfetTempC(), fetOverTempC) ||
            overTemp(power.motorTempC(), motorOverTempC)) {
            faulted = true;
        }
        return duty;
    }

    void clearFault() { faulted = false; }
};

}  // namespace drivelab
