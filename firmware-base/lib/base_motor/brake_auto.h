/*
 * brake_auto.h — limiares auto-calibrados do modo auto-brake de bancada (cmd=9, BrakeAuto).
 *
 * Puro e host-testável. A partir do bus OCIOSO (medido no arm), calcula os limiares do BrakeController
 * (ffb_power.h): liga em (ocioso + onMargin), duty 100% em (ocioso + fullMargin), desliga em (ocioso +
 * offMargin) — histerese. Adapta a 12V/19V sem hardcode. A ação (armar/dirigir o FocBrake) fica no m5.
 *
 * Autor: Luciano Tomé
 * Licença: MIT
 */
#pragma once

struct BrakeAutoThresholds {
    float onVoltage;    ///< começa a dissipar (regen elevou o bus)
    float fullVoltage;  ///< duty 100%
    float offVoltage;   ///< histerese: desliga abaixo disso
};

inline BrakeAutoThresholds brakeAutoThresholds(float idleBusV,
                                               float onMargin   = 2.0f,
                                               float fullMargin = 4.0f,
                                               float offMargin  = 1.0f) {
    return BrakeAutoThresholds{ idleBusV + onMargin, idleBusV + fullMargin, idleBusV + offMargin };
}
