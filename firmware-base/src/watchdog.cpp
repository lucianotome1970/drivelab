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

    // ⚠️ O LSI PRECISA ESTAR PRONTO ANTES. O IWDG conta nele, e enquanto ele nao oscila o IWDG->SR
    // nunca zera. A primeira versao disto esperava `while (IWDG->SR != 0u) {}` sem limite — e
    // travou a base inteira de um jeito nada obvio:
    //
    //   · watchdog_init() roda dentro do ffb_thread, que e osPriorityAboveNormal
    //   · a task que processa a pilha USB (tud_task) e osPriorityNormal
    //   · uma espera infinita na tarefa de prioridade MAIOR nunca cede a CPU para a menor
    //   → tud_task nunca roda → o Windows pede o descritor e ninguem responde
    //     ("Dispositivo USB Desconhecido — Falha na Solicitacao de Descritor de Dispositivo")
    //
    // E o pior: o tick do FreeRTOS CONTINUAVA avancando, porque vem de interrupcao e nao de tarefa.
    // Todos os testes de "o firmware esta vivo?" davam positivo com o USB morto. Custou horas em
    // 14/08/2026, e a licao vale alem do watchdog: **nunca esperar sem limite dentro de uma tarefa
    // de prioridade alta** — o que trava nao e so ela, e tudo o que roda abaixo.
    RCC->CSR |= RCC_CSR_LSION;
    for (uint32_t i = 0; i < 100000u && !(RCC->CSR & RCC_CSR_LSIRDY); ++i) { }
    if (!(RCC->CSR & RCC_CSR_LSIRDY)) {
        return;   // sem LSI nao ha watchdog; seguir sem ele e melhor que travar a base
    }

    IWDG->KR  = IWDG_KEY_UNLOCK;    // libera escrita em PR/RLR
    IWDG->PR  = WDG_PRESCALER_64;
    IWDG->RLR = WDG_RELOAD;
    for (uint32_t i = 0; i < 100000u && IWDG->SR != 0u; ++i) { }   // COM teto, sempre
    IWDG->KR  = IWDG_KEY_FEED;      // recarrega antes de armar
    IWDG->KR  = IWDG_KEY_START;     // arma — daqui em diante so reset desliga
}

void watchdog_feed(void) {
    IWDG->KR = IWDG_KEY_FEED;
}

uint32_t watchdog_reset_count(void) {
    return (g_bb_reset_reason == BB_RESET_IWDG) ? 1u : 0u;
}
