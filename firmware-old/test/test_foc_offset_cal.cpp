// test_foc_offset_cal.cpp — testes host da lógica de offset de calibração FOC.
// Autor: Luciano Tomé
// Licença: MIT
#include "foc_offset_cal.h"
#include <cassert>
#include <cmath>
#include <cstdio>
using namespace drivelab;

static bool near(float a, float b, float tol) { return std::fabs(a - b) < tol; }

// Varredura ideal: o encoder (em elétrico) segue o campo com um OFFSET fixo +
// cogging senoidal. Convenção: poleMech = dirReal*field + offset + cog.
static void feedSweep(OffsetAccumulator& acc, float offset, int dirReal, float cogAmp) {
    for (int i = 0; i < 500; ++i) {
        const float field = 0.02f * i;
        const float cog   = cogAmp * std::sin(3.0f * field);
        const float poleMech = dirReal * field + offset + cog;
        acc.add(poleMech, field);
    }
}

int main() {
    // Recupera offset conhecido sob cogging, direção CW (+1): mean(aP)=offset.
    {
        OffsetAccumulator acc;
        feedSweep(acc, 0.7f, +1, 0.15f);
        OffsetResult r = computeOffset(acc, 0.5f);
        assert(r.ok);
        assert(r.direction == +1);
        assert(near(r.zeroElectric, 0.7f, 0.05f));
    }
    // Escolhe direção CCW (-1): a hipótese M tem o phasor mais apertado.
    {
        OffsetAccumulator acc;
        feedSweep(acc, 0.3f, -1, 0.1f);
        OffsetResult r = computeOffset(acc, 0.5f);
        assert(r.ok);
        assert(r.direction == -1);
    }
    // Cogging enorme → phasor curto → fit baixo → reprova.
    {
        OffsetAccumulator acc;
        feedSweep(acc, 0.0f, +1, 3.0f);
        OffsetResult r = computeOffset(acc, 0.8f);
        assert(!r.ok);
    }
    // Corte por corrente (usa magnitude).
    assert(!currentOverLimit(0.4f, 0.5f));
    assert(currentOverLimit(0.6f, 0.5f));
    assert(currentOverLimit(-0.6f, 0.5f));

    std::printf("test_foc_offset_cal: OK\n");
    return 0;
}
