// ============================================================================
//  DriveLab Firmware
//  test_clip_meter.cpp — Teste de HOST do ClipMeter (lib/brain/clip_meter.h): excesso instantâneo,
//  peak-hold com decaimento e mapeamento 0..255. Roda sem placa: firmware-base/test/run.sh.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "../lib/brain/clip_meter.h"

#include <cstdio>
#include <cmath>

static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)
#define NEAR(a, b) (std::fabs((a) - (b)) < 1e-4f)

int main()
{
    // ---- excesso instantâneo (estático) ----
    CHECK(NEAR(ClipMeter::instantaneous(1.0f, 2.0f), 0.0f));     // abaixo do teto → não corta
    CHECK(NEAR(ClipMeter::instantaneous(2.0f, 2.0f), 0.0f));     // exatamente no teto → limite, sem corte
    CHECK(NEAR(ClipMeter::instantaneous(4.0f, 2.0f), 0.5f));     // 2× teto → metade cortada
    CHECK(NEAR(ClipMeter::instantaneous(-4.0f, 2.0f), 0.5f));    // usa módulo (força negativa)
    CHECK(NEAR(ClipMeter::instantaneous(1.0f, 0.0f), 1.0f));     // sem teto útil e há força → satura tudo
    CHECK(NEAR(ClipMeter::instantaneous(0.0f, 0.0f), 0.0f));     // sem força → 0

    // ---- peak-hold: sobe na hora ao cortar ----
    ClipMeter m;
    CHECK(NEAR(m.level(), 0.0f));
    m.update(4.0f, 2.0f, 0.1f);                                  // corta 0,5
    CHECK(NEAR(m.level(), 0.5f));

    // ---- decaimento: sem corte, o nível cai ----
    float before = m.level();
    m.update(1.0f, 2.0f, 0.1f);                                  // sem corte → decai
    CHECK(m.level() < before);
    CHECK(m.level() > 0.0f);

    // ---- um corte maior levanta o nível ----
    m.update(6.0f, 2.0f, 0.1f);                                  // (6-2)/6 = 0,667
    CHECK(NEAR(m.level(), 0.66667f));

    // ---- decai até zero com tempo suficiente ----
    for (int i = 0; i < 100; ++i) m.update(0.0f, 2.0f, 0.1f);
    CHECK(NEAR(m.level(), 0.0f));

    // ---- mapeamento 0..255 ----
    ClipMeter m2;
    m2.update(4.0f, 2.0f, 0.0f);                                 // nível 0,5
    CHECK(m2.level255() == 128);                                 // 0,5*255+0,5 = 128
    m2.reset();
    CHECK(m2.level255() == 0);

    std::printf("clip_meter: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails == 0 ? 0 : 1;
}
