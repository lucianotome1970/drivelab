/*
 * test_brake_auto.cpp — testes host dos limiares auto-calibrados do modo auto-brake.
 * Autor: Luciano Tomé
 * Licença: MIT
 */
#include "brake_auto.h"
#include <cassert>
#include <cmath>
#include <cstdio>

static bool near(float a, float b) { return std::fabs(a - b) < 1e-4f; }

int main() {
    // Bus ocioso de 12V → on=14, full=16, off=13.
    {
        BrakeAutoThresholds t = brakeAutoThresholds(12.0f);
        assert(near(t.onVoltage, 14.0f));
        assert(near(t.fullVoltage, 16.0f));
        assert(near(t.offVoltage, 13.0f));
    }
    // Bus ocioso de 19,9V → on=21.9, full=23.9, off=20.9.
    {
        BrakeAutoThresholds t = brakeAutoThresholds(19.9f);
        assert(near(t.onVoltage, 21.9f));
        assert(near(t.fullVoltage, 23.9f));
        assert(near(t.offVoltage, 20.9f));
    }
    // Margens custom.
    {
        BrakeAutoThresholds t = brakeAutoThresholds(12.0f, 3.0f, 6.0f, 1.5f);
        assert(near(t.onVoltage, 15.0f));
        assert(near(t.fullVoltage, 18.0f));
        assert(near(t.offVoltage, 13.5f));
    }
    // Coerência da histerese: off < on < full.
    {
        BrakeAutoThresholds t = brakeAutoThresholds(15.0f);
        assert(t.offVoltage < t.onVoltage);
        assert(t.onVoltage < t.fullVoltage);
    }
    printf("test_brake_auto OK\n");
    return 0;
}
