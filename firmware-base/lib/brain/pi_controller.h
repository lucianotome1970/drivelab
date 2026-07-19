// ============================================================================
//  DriveLab Firmware
//  pi_controller.h — Controlador PI genérico com anti-windup (malha fechada; M2).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// PI reutilizável, puro e testável. Ganhos espelham BaseSettingId.CurrentP/CurrentI.
// Nota: a malha de corrente FOC em si roda dentro do SimpleFOC no firmware; este PI é o
// componente genérico (mesma matemática) que usamos/testamos p/ qualquer laço fechado e
// para entender/validar a sintonia. Anti-windup por clamp do integrador ao range de saída.
#pragma once

#include "ffb_math.h"   // clampf

namespace drivelab {

struct PiController {
    float kp = 0.0f;
    float ki = 0.0f;
    float outMin = -1e9f;
    float outMax =  1e9f;
    float integral = 0.0f;   ///< estado acumulado (público p/ inspeção/teste)

    /// Um passo: erro = setpoint - measured; saída = P + I, com anti-windup e clamp.
    float update(float setpoint, float measured, float dt) {
        const float e = setpoint - measured;
        integral += e * dt;
        if (ki != 0.0f)                                   // anti-windup: o I não pode sozinho
            integral = clampf(integral, outMin / ki, outMax / ki);  // estourar o range de saída
        const float out = kp * e + ki * integral;
        return clampf(out, outMin, outMax);
    }

    void reset() { integral = 0.0f; }
};

}  // namespace drivelab
