// firmware-base — cola HID (STAGE 3a): TinyUSB HID ↔ DriveLab.
//
// Faz o device virar CONTROLE DE JOGO:
//   - hid_send_joystick(): monta o RID_JOYSTICK (8 botões + 8 eixos int16) a partir
//     do encoder (via motor_link) → o eixo X é a DIREÇÃO. Chamado pelo laço.
//   - tud_hid_get_report_cb / tud_hid_set_report_cb: o handshake PID mínimo que o
//     DirectInput exige pra reconhecer o dispositivo como Force-Feedback (PID State,
//     Create New Effect → aloca bloco, Block Load / Pool respondem o pool).
//
// STAGE 3a NÃO gera força ainda (só torna o controle visível + direção viva). O
// roteamento de efeitos → torque pela ponte entra no 3b (effect_manager/reconstruct).
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include <stdint.h>
#include <string.h>
#include "tusb.h"
#include "ffb_hid_descriptor.h"
#include "pid_state.h"
#include "motor_link.h"
#include "ffb_model.h"
#include "wheel_center.h"   // eixo do jogo tem de sair do MESMO zero que o FFB

// Canal A0 (app DriveLab Studio) — trata os OUT reports vendor (a0_channel.cpp).
extern "C" void a0_handle_out(const uint8_t* buf, uint16_t len);

// --- Report de Input do RID_JOYSTICK (idêntico ao firmware-base): 8 botões + 8 eixos x16b = 24 bytes ---
typedef struct __attribute__((packed)) {
    uint8_t buttons[8];
    int16_t axes[8];
} JoystickInputReport;
_Static_assert(sizeof(JoystickInputReport) == 24, "RID_JOYSTICK deve ter 24 bytes");

// Curso do volante mapeado pro fundo de escala do eixo (turns). ±1.5 turns (=540°)
// por enquanto; o curso real (DOR) vem da config no Stage 4.
static const float kSteerRangeTurns = 1.5f;

// Bloco alocado no último Create New Effect (p/ o Block Load responder). O banco
// real de efeitos vive no ffb_model (EffectManager, com reuso de blocos livres).
static uint8_t s_last_effect_block = 0;

// Monta e envia o RID_JOYSTICK com a direção lida do encoder. Chamar quando o
// endpoint IN do HID estiver livre (tud_hid_ready()). Retorna true se enfileirou.
// --- Watchdog do endpoint IN (fix do "eixo congela ao sair do jogo") --------------------------
// TODO envio é gated por tud_hid_ready(), que é tud_ready() && !edpt_busy(EP_IN). Se um transfer
// fica pendente e nunca completa, edpt_busy trava em true e o firmware NUNCA MAIS envia: o Windows
// segue pollando, leva NAK, e o eixo congela — com o device enumerado e a placa perfeita (medido
// em 2026-08-09: encoder contando 241°, erros 0, USB OK, e o X parado). Antes só o reset por
// ST-Link recuperava. Agora a própria placa se re-enumera.
//
// Contadores expostos (não-static) para inspeção por SWD sem precisar de log.
extern "C" {
uint32_t g_hid_sent        = 0;   // reports de joystick enfileirados
uint32_t g_hid_stall_ticks = 0;   // ticks consecutivos com o EP travado (1 tick = 1 ms)
uint32_t g_hid_recoveries  = 0;   // quantas vezes tivemos de re-enumerar
}
static uint32_t s_reconnect_at = 0;   // tick para religar o USB (0 = não estamos reconectando)

// ⚠️ DISPARO DESLIGADO em 2026-08-10 — a cura era pior que a doença.
//
// O QUE ACONTECEU: com o watchdog ativo, a base CONGELOU JUNTO COM O JOGO ao iniciar uma volta.
// Repetiu depois de power-cycle, então não era placa em mau estado. O mecanismo:
//
//   `tud_mounted() && !tud_suspended()` NÃO distingue "host sumiu" de "host montado que parou de
//   consumir". Carregar uma pista prende o host por vários segundos — o contador chega aos 2 s, o
//   watchdog conclui "endpoint travado" e DESCONECTA o USB no meio do carregamento. O jogo perde o
//   dispositivo e trava. Ou seja: ele derrubava a base justamente quando ela estava saudável.
//
// E o benefício nunca foi comprovado: em toda a bancada o contador de recuperações ficou em ZERO —
// o congelamento que motivou o watchdog (eixo parado ao sair do jogo) foi visto UMA vez e pode ter
// sido resolvido pelas outras correções do mesmo dia (starvation da telemetria, prioridade do 0x16).
//
// Risco alto e certo contra benefício hipotético: fica desligado. O código e os contadores
// permanecem porque a instrumentação é útil (g_hid_stall_ticks mostra o EP travando de verdade).
// Para religar seria preciso, antes, um jeito de saber que o HOST está pollando — o simples
// "não consegui enviar" não é evidência de endpoint travado.
#define DRVLAB_USB_WATCHDOG_ENABLED 0

// Chamado pelo loop de 1 kHz. Não bloqueia: a espera entre desconectar e reconectar é contada em
// ticks. Com o disparo desligado, só mantém a contagem para diagnóstico.
extern "C" void hid_usb_watchdog(uint32_t nowTick) {
#if DRVLAB_USB_WATCHDOG_ENABLED
    if (s_reconnect_at) {                                    // fase "desconectado", esperando religar
        if ((int32_t)(nowTick - s_reconnect_at) >= 0) { tud_connect(); s_reconnect_at = 0; }
        return;
    }
    if (g_hid_stall_ticks < 2000) return;
    g_hid_stall_ticks = 0;
    g_hid_recoveries++;
    tud_disconnect();                                        // host vê o device sumir...
    s_reconnect_at = nowTick + 150;                          // ...e voltar 150 ms depois (re-enumera)
#else
    (void)nowTick;
    (void)s_reconnect_at;
#endif
}

extern "C" int hid_send_joystick(void) {
    if (!tud_hid_ready()) {
        // Só conta como travado quando o host DEVERIA estar pollando. Sem isto, um device
        // desmontado ou suspenso (situações normais) dispararia a recuperação à toa.
        if (tud_mounted() && !tud_suspended()) g_hid_stall_ticks++;
        else                                   g_hid_stall_ticks = 0;
        return 0;
    }
    g_hid_stall_ticks = 0;
    JoystickInputReport rep;
    memset(&rep, 0, sizeof(rep));

    // Posição RELATIVA ao centro — nunca a contagem crua do encoder. Com a contagem crua o eixo
    // saturava em 32767 assim que o acumulado passava de 1,5 volta: o jogo via o volante travado
    // no batente sem ninguém tocar nele (bancada 2026-08-07). Ver inc/wheel_center.h.
    float pos = wheel_center_pos_turns();          // turns a partir do centro
    float norm = pos / kSteerRangeTurns;           // -1..+1 no curso
    if (norm > 1.0f) norm = 1.0f; else if (norm < -1.0f) norm = -1.0f;
    rep.axes[0] = (int16_t)(norm * 32767.0f);      // eixo X = direção

    if (!tud_hid_report(RID_JOYSTICK, &rep, sizeof(rep))) return 0;
    g_hid_sent++;
    return 1;
}

// ---------------------------------------------------------------------------
// SET_REPORT (host → device): Feature = Create New Effect / Output = efeitos.
// Stage 3b: roteia pro ffb_model (EffectManager + ForceReconstructor) → torque.
// ---------------------------------------------------------------------------
extern "C" void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id,
                                      hid_report_type_t report_type,
                                      uint8_t const* buffer, uint16_t bufsize) {
    (void)instance;
    // TinyUSB entrega o report_id em `report_id` quando != 0; se 0, o 1º byte do buffer é o ID.
    uint8_t rid = report_id;
    const uint8_t* buf = buffer;
    uint16_t len = bufsize;
    if (rid == 0 && len > 0) { rid = buffer[0]; buf = buffer; }

    if (report_type == HID_REPORT_TYPE_FEATURE) {
        if (rid == RID_PID_CREATE_NEW_EFFECT)
            s_last_effect_block = ffb_model_create_effect();   // reserva o 1º bloco livre (1-based)
        return;
    }

    // OUTPUT reports: canal A0 do app (0x10 DIRECT / 0x14 SETWRITE / 0x15 SETREAD / 0x22 CMD) vs
    // FFB do jogo (0x01-0x06,0x0A-0x0D...). Despacha por report ID (buf[0]=rid quando veio pelo EP OUT).
    if (rid == 0x10 || rid == 0x14 || rid == 0x15 || rid == 0x22) {
        a0_handle_out(buf, len);
    } else {
        ffb_model_handle_out(buf, len);
    }
}

// ---------------------------------------------------------------------------
// GET_REPORT (device → host): PID State, Block Load, Pool — o que o DirectInput
// consulta pra achar que o dispositivo é FFB e tem onde alocar efeitos.
// ---------------------------------------------------------------------------
extern "C" uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id,
                                          hid_report_type_t report_type,
                                          uint8_t* buffer, uint16_t reqlen) {
    (void)instance;

    if (report_type == HID_REPORT_TYPE_INPUT && report_id == RID_PID_STATE && reqlen >= 1) {
        buffer[0] = buildPidStateByte(false /*devicePaused*/, true /*actuatorsEnabled*/,
                                      true /*safetySwitch*/, false /*actuatorOverride*/,
                                      true /*actuatorPower*/);
        return 1;
    }

    if (report_type == HID_REPORT_TYPE_FEATURE) {
        // Block Load (Feature, 0x12) — 4 bytes, CASADO com o m5 provado (ACC ok):
        //   [block, status(1=Success/2=Full), ramPoolAvail_lo, ramPoolAvail_hi]. Budget 16 B/slot.
        if (report_id == RID_PID_BLOCK_LOAD && reqlen >= 4) {
            buffer[0] = s_last_effect_block;
            buffer[1] = (s_last_effect_block != 0) ? 1 : 2;   // 2 = pool cheio (bloco 0)
            // ramPoolAvailable também em EFEITOS (era × 16 = bytes), casando com o ramPoolSize do
            // Pool Report — as duas respostas precisam falar a MESMA unidade, senão o host compara
            // "disponível" com "total" em escalas diferentes e conclui que o pool está estourado.
            uint16_t avail = (uint16_t)(ffb_model_max_blocks() - ffb_model_used_blocks());
            buffer[2] = (uint8_t)(avail & 0xFF);
            buffer[3] = (uint8_t)(avail >> 8);
            return 4;
        }

        // Pool (Feature, 0x13) — 4 bytes: [ramPoolSize_lo, hi, maxSimultaneousEffects, memoryManagement].
        //
        // 🔴 CORRIGIDO 2026-08-08 — o ACC travava ("Fatal error", UE4-AC2) ao abrir a PÁGINA DE
        // CONFIGURAÇÃO DO CONTROLE, que é onde o DirectInput faz o handshake PID completo e aloca
        // efeitos. Entrar no jogo funcionava; abrir a configuração derrubava o USB e matava o jogo.
        //
        // O que estava errado, comparando com a implementação de referência que roda estável:
        //   ramPoolSize      — mandávamos BYTES (blocos × 16 = 256); o campo é contado em EFEITOS
        //   memoryManagement — mandávamos 0 (DeviceManagedPool); a referência usa 1
        //   maxSimultaneous  — 8 fixo, sem relação com o banco real
        //
        // Com memoryManagement=0 o host acredita que NÓS gerenciamos o pool e segue outro protocolo
        // de alocação, usando um ramPoolSize que ele lê como contagem de efeitos — mas recebia 256.
        // Pedia então um número de blocos que o nosso banco não tem, e o handshake morria no meio.
        //
        // Agora os três campos saem na mesma unidade e semântica da referência: tudo em EFEITOS, e
        // o pool declarado como blocos de parâmetros compartilhados.
        if (report_id == RID_PID_POOL && reqlen >= 4) {
            const uint16_t maxEffects = (uint16_t)ffb_model_max_blocks();
            buffer[0] = (uint8_t)(maxEffects & 0xFF);   // ramPoolSize, em EFEITOS (não bytes)
            buffer[1] = (uint8_t)(maxEffects >> 8);
            buffer[2] = (uint8_t)maxEffects;            // maxSimultaneousEffects = o banco inteiro
            buffer[3] = 1;                              // memoryManagement: 1 = SharedParameterBlocks
            return 4;
        }
    }

    // ⚠️ CRÍTICO (fix ACC 2026-08-03): NUNCA retornar 0. O TinyUSB faz TU_ASSERT(xferlen>0) no
    // get_report → retorno 0 = STALL no EP0 → o Windows HALTA a pipe do device → para de pollar o
    // endpoint IN → o eixo do joystick CONGELA (e fica congelado mesmo após sair do jogo). O ACC dispara
    // GET_REPORT (Block Load/Pool/State) ao ligar a FFB; qualquer consulta não tratada caía aqui.
    // Fallback: preenche com zeros e retorna comprimento não-nulo — NUNCA 0, senão o EP0 dá STALL.
    uint16_t n = reqlen ? reqlen : 1;
    if (n > 64) n = 64;                 // limite do buffer do EP (CFG_TUD_HID_EP_BUFSIZE)
    memset(buffer, 0, n);
    return n;
}
