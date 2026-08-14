// ============================================================================
//  DriveLab
//  watchdog.cpp — IWDG: a base reinicia sozinha em vez de congelar. Ver o header.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "watchdog.h"
#include "blackbox.h"
#include "stm32f4xx.h"

// JANELA: ~2 segundos.
//
// Escolhida pelo que precisa CABER dentro dela sem ser um travamento:
//   · a calibracao de offset do encoder gira o motor por varios segundos — mas roda
//     na thread do eixo, e o laco de FFB (quem alimenta) continua girando a 1 kHz
//     durante ela. Ou seja, a calibracao NAO e o caso critico, apesar de ser o mais
//     longo. Era essa a duvida que segurou o watchdog ate agora.
//   · a gravacao de settings na flash PARA o core durante o erase (o F405 congela o
//     barramento). Um erase de setor de 16K leva ~250 ms; 2 s da folga de 8x.
//
// Curto demais reinicia a base em operacao normal, que e MUITO pior que o problema
// que o watchdog resolve. Longo demais deixa o volante morto tempo demais. 2 s e
// imperceptivel como interrupcao e generoso como margem.
//
// O IWDG conta no LSI (~32 kHz, RC interno). Com prescaler /64:
//     T = 64 x reload / 32000  ->  reload 1000 = 2,0 s
// O LSI varia com temperatura e peca (±50% no pior caso do datasheet), entao a
// janela real fica entre ~1 s e ~4 s. Para o nosso uso — distinguir "travou" de
// "esta trabalhando" — essa impressao e irrelevante.
#define WDG_PRESCALER_64   0x04u
#define WDG_RELOAD         1000u

#define IWDG_KEY_FEED      0xAAAAu
#define IWDG_KEY_UNLOCK    0x5555u
#define IWDG_KEY_START     0xCCCCu

void watchdog_init(void) {
    // ⚠️ CONGELAR O WATCHDOG COM O DEPURADOR PARADO. Sem isto, toda vez que
    // parassemos o core pelo SWD para inspecionar alguma coisa — que e como
    // diagnosticamos praticamente tudo neste projeto — o IWDG continuaria contando e
    // reiniciaria a placa no meio da leitura. A ferramenta de diagnostico passaria a
    // destruir o estado que ela existe para observar. Ja tivemos exatamente esse tipo
    // de armadilha com o breakpoint do TinyUSB; nao vamos criar outra.
    // (no F4 o bloco DBGMCU é sempre alimentado — não há clock a habilitar)
    DBGMCU->APB1FZ |= DBGMCU_APB1_FZ_DBG_IWDG_STOP;

    IWDG->KR  = IWDG_KEY_UNLOCK;    // libera escrita em PR/RLR
    IWDG->PR  = WDG_PRESCALER_64;
    IWDG->RLR = WDG_RELOAD;
    while (IWDG->SR != 0u) { }      // espera PR/RLR serem aceitos
    IWDG->KR  = IWDG_KEY_FEED;      // recarrega antes de armar
    IWDG->KR  = IWDG_KEY_START;     // arma — daqui em diante so reset desliga
}

void watchdog_feed(void) {
    IWDG->KR = IWDG_KEY_FEED;
}

uint32_t watchdog_reset_count(void) {
    return (g_bb_reset_reason == BB_RESET_IWDG) ? 1u : 0u;
}
