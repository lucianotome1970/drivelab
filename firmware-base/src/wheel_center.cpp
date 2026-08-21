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

// ⚠️ SENTIDO DO VOLANTE (setting 9, "direcao do encoder"). +1 = como esta ligado, -1 = invertido.
//
// O ajuste existia na tela, era salvo na base e NAO ERA LIDO POR NINGUEM — um dos nove orfaos que o
// nosso proprio verificador ja contava. Quem visse o volante girando ao contrario mudava o campo,
// salvava, e nada acontecia (bancada, 17/08/2026: "se seto para o outro lado ele nao acata").
//
// Ele age AQUI, e nao na FOC, e a diferenca importa: a relacao entre encoder e motor e' medida na
// calibracao, e mexer nela com o motor calibrado faz o torque sair no sentido errado — runaway.
// Este sinal inverte apenas o que o mundo VE do volante: a forca do FFB, o eixo que o jogo le e a
// telemetria. Os tres passam por aqui, entao um sinal so mantem os tres coerentes entre si — que e
// exatamente o bug de 07/08, quando cada consumidor tinha o seu proprio zero.
static volatile float s_sentido = 1.0f;

// Espelhos legíveis por SWD (o depurador acha por nome, sem precisar de mapa).
volatile int32_t g_center_turns_m = 0;   // o zero adotado, em milésimos de volta
volatile int32_t g_center_done    = 0;   // 1 = já centrou neste boot

extern "C" void wheel_center_set_direction(int dir) { s_sentido = (dir < 0) ? -1.0f : 1.0f; }

extern "C" void wheel_center_capture(void) {
    const float raw = motor_link_get_pos_turns();
    s_offset_turns   = raw;
    s_is_set         = 1;
    g_center_turns_m = (int32_t)(raw * 1000.0f);
    g_center_done    = 1;
}

extern "C" float wheel_center_pos_turns(void) {
    // O sentido multiplica DEPOIS de descontar o zero: inverter antes moveria o centro junto, e o
    // volante passaria a centrar num ponto diferente so por mudar o sinal.
    return (motor_link_get_pos_turns() - s_offset_turns) * s_sentido;
}

extern "C" float wheel_center_offset_turns(void) { return s_offset_turns; }

extern "C" int wheel_center_is_set(void) { return s_is_set; }
