// ============================================================================
//  DriveLab Firmware
//  test_contactor_manager.cpp — Teste host da máquina de estados do contator
//  fail-safe (proteção off-state). Puro, sem placa.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include <cassert>
#include <cstdio>
#include "contactor_manager.h"

int main()
{
    ContactorManager m(30, 10);   // closeMs=30, openMs=10
    assert(m.state() == ContactorState::Open);
    assert(!m.coilOn() && !m.readyToDrive());

    // driveIntent -> CLOSING (bobina liga, ainda não pronto)
    m.step(true, false, 0);
    assert(m.state() == ContactorState::Closing);
    assert(m.coilOn() && !m.readyToDrive());

    // antes de kCloseMs continua CLOSING
    m.step(true, false, 20);
    assert(m.state() == ContactorState::Closing);

    // passou kCloseMs -> CLOSED (pronto pra dirigir)
    m.step(true, false, 35);
    assert(m.state() == ContactorState::Closed);
    assert(m.coilOn() && m.readyToDrive());

    // driveIntent cai -> OPENING (bobina desliga, não mais pronto)
    m.step(false, false, 100);
    assert(m.state() == ContactorState::Opening);
    assert(!m.coilOn() && !m.readyToDrive());

    // passou kOpenMs -> OPEN
    m.step(false, false, 115);
    assert(m.state() == ContactorState::Open);
    assert(!m.coilOn());

    // FAULT durante CLOSED força OPENING
    ContactorManager m2(30, 10);
    m2.step(true, false, 0); m2.step(true, false, 40);
    assert(m2.state() == ContactorState::Closed);
    m2.step(true, /*faulted=*/true, 50);
    assert(m2.state() == ContactorState::Opening && !m2.coilOn());

    // FAULT durante CLOSING também força OPENING
    ContactorManager m3(30, 10);
    m3.step(true, false, 0);
    assert(m3.state() == ContactorState::Closing);
    m3.step(true, /*faulted=*/true, 10);
    assert(m3.state() == ContactorState::Opening);

    std::printf("test_contactor_manager: OK\n");
    return 0;
}
