// ============================================================================
//  DriveLab
//  wheel_center.cpp — O centro do volante. Ver wheel_center.h para o porquê.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include "wheel_center.h"
#include "motor_link.h"

// volatile: escrito pelo laço de FFB (1 kHz) e lido pelo caminho do HID. São
// leituras/escritas de 32 bits alinhadas — atômicas no Cortex-M4 —, então não
// há palavra rasgada nem necessidade de seção crítica. O volatile só impede o
// compilador de cachear o valor num registrador entre os dois contextos.
static volatile float s_offset_turns = 0.0f;
static volatile int   s_is_set       = 0;

// Espelhos legíveis por SWD (o depurador acha por nome, sem precisar de mapa).
volatile int32_t g_center_turns_m = 0;   // o zero adotado, em milésimos de volta
volatile int32_t g_center_done    = 0;   // 1 = já centrou neste boot

extern "C" void wheel_center_capture(void) {
    const float raw = motor_link_get_pos_turns();
    s_offset_turns   = raw;
    s_is_set         = 1;
    g_center_turns_m = (int32_t)(raw * 1000.0f);
    g_center_done    = 1;
}

extern "C" float wheel_center_pos_turns(void) {
    return motor_link_get_pos_turns() - s_offset_turns;
}

extern "C" float wheel_center_offset_turns(void) { return s_offset_turns; }

extern "C" int wheel_center_is_set(void) { return s_is_set; }
