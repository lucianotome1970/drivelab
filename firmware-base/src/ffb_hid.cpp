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
extern "C" bool ffb_model_algum_efeito_tocando(void);
#include "motor_link.h"
#include "ffb_model.h"
#include "wheel_center.h"   // eixo do jogo tem de sair do MESMO zero que o FFB
#include "blackbox.h"       // rastro do laço: separar "travou no TinyUSB" de "travou aqui"

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
    blackbox_step(BB_STEP_TLM_HID);
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

    // A partir daqui estamos DENTRO do TinyUSB, que toma o mutex do endpoint com espera infinita.
    // Se o rastro parar neste passo, o travamento e ali — e nao no nosso codigo.
    blackbox_step(BB_STEP_TLM_HID_XFER);
    if (!tud_hid_n_report(0 /*interface do jogo*/, RID_JOYSTICK, &rep, sizeof(rep))) return 0;
    g_hid_sent++;
    return 1;
}

// ---------------------------------------------------------------------------
// SET_REPORT (host → device): Feature = Create New Effect / Output = efeitos.
// Stage 3b: roteia pro ffb_model (EffectManager + ForceReconstructor) → torque.
// ---------------------------------------------------------------------------
// ------------------------------------------------------------------------------------------------
// O QUE O JOGO PEDE, NA ORDEM — e onde ele para
// ------------------------------------------------------------------------------------------------
// O jogo monta o efeito por etapas: cria o efeito, descreve o efeito, manda a forca, manda tocar, e
// dai em diante so atualiza a forca a cada quadro. Medimos que ele cria DOIS efeitos, manda UMA
// forca e para — e sem saber QUAIS pedidos chegaram, "para" nao diz onde.
//
// Aqui fica o rastro: os ultimos 24 pedidos, cada um com o tipo (pergunta ou escrita) e o numero do
// relatorio. Lido por SWD, mostra a sequencia exata e o degrau em que ela morre.
//   0x00 | id  = escrita do jogo (efeito, forca, tocar/parar)
//   0x80 | id  = pergunta do jogo (quanto cabe, o efeito entrou, qual o estado)
volatile uint8_t  g_ffb_pedidos[24] = {0};
// OS PRIMEIROS pedidos da sessao, que o rastro circular acima NAO consegue guardar.
// Num jogo que funciona, a forca chega mil vezes por segundo e varre o circulo em instantes — o
// comeco, que e onde o jogo negocia o efeito, se perde. Aqui ele fica: grava os 32 primeiros e
// congela. E o unico jeito de comparar a NEGOCIACAO de um jogo que funciona com a de um que nao.
volatile uint8_t  g_ffb_inicio[32] = {0};
volatile uint32_t g_ffb_inicio_n = 0;
// Os pacotes INTEIROS do comeco da sessao — 12 pacotes, 10 bytes cada.
// Saber QUE o jogo descreveu um efeito nao basta: o que decide se ele vai continuar mandando forca
// esta DENTRO do pacote (o tipo do efeito, a duracao, o eixo, o modo de tocar). Dois jogos podem
// fazer a mesma sequencia de pedidos e divergir inteiramente no conteudo — e e essa a diferenca
// entre o que funciona e o que emudece.
volatile uint8_t  g_ffb_bytes[12][10] = {{0}};
volatile uint32_t g_ffb_bytes_n = 0;
// E o que NOS respondemos. Ate aqui o rastro so guardava o que o jogo mandou — metade da conversa.
// A decisao de continuar mandando forca e do jogo, mas ele a toma com base nas NOSSAS respostas:
// quantos efeitos cabem, se o efeito entrou, em que bloco. Sem esta metade, comparar dois jogos so
// mostra que um parou; com ela, da para ver o que ele ouviu antes de parar.
volatile uint8_t  g_ffb_respostas[8][6] = {{0}};
volatile uint32_t g_ffb_respostas_n = 0;
static inline void anota_resposta(uint8_t rid, const uint8_t* b, uint8_t n) {
    if (g_ffb_respostas_n >= 8) return;
    const uint32_t k = g_ffb_respostas_n++;
    g_ffb_respostas[k][0] = rid;
    g_ffb_respostas[k][1] = n;
    for (uint8_t i = 0; i < 4; ++i) g_ffb_respostas[k][2 + i] = (i < n) ? b[i] : 0;
}
// [0]=numero entregue pelo driver  [1]=tamanho  [2..9]=os 8 primeiros bytes crus
volatile uint8_t  g_ffb_pacote[10] = {0};
volatile uint32_t g_ffb_pedidos_pos = 0;
static inline void anota_pedido(uint8_t marca) {
    if (g_ffb_inicio_n < 32) g_ffb_inicio[g_ffb_inicio_n++] = marca;
    g_ffb_pedidos[g_ffb_pedidos_pos % 24] = marca;
    g_ffb_pedidos_pos = (g_ffb_pedidos_pos + 1) % 24;
}

// ------------------------------------------------------------------------------------------------
// AVISAR O JOGO DO NOSSO ESTADO — o que faltava para ACC e AC Evo darem forca
// ------------------------------------------------------------------------------------------------
// Este relatorio diz ao jogo se os atuadores estao com energia e se o efeito esta tocando. Nos o
// declaravamos no descritor e so respondiamos quando perguntados — e o ACC nao pergunta: ele espera
// RECEBER. Sem receber, ele reinicia a negociacao do force feedback em circulo e nunca chega a
// enviar forca. Era essa a diferenca entre os jogos: AC1 e AMS2 nao dependem deste relatorio.
//
// A implementacao de referencia envia nos mesmos dois momentos usados aqui: quando um efeito e
// carregado e quando o jogo manda tocar ou parar. Sao poucos envios por sessao — nao disputa espaco
// com o relatorio do volante.
// ⚠️ NUNCA ENVIAR DE DENTRO DO CALLBACK DE RECEPCAO.
//
// A primeira versao disto chamava o envio direto no callback que recebe o pacote do jogo — ou seja,
// pedia a pilha USB que transmitisse enquanto ela ainda estava processando o que acabara de chegar.
// Isso a trava; o cao-de-guarda reinicia a base; e a base entra em ciclo de reinicio, sem conseguir
// nem abrir o jogo (bancada, 20/08/2026). Reentrar na pilha de dentro dela mesma nao e detalhe de
// estilo, e uma trava.
//
// Aqui apenas ANOTAMOS que ha aviso a dar. Quem envia e o laco, no seu proprio tempo, com a pilha
// livre — ver ffb_hid_enviar_estado_pendente(), chamada de ffb_task.
static volatile uint8_t s_estado_pendente = 0;

extern "C" void ffb_hid_avisar_estado(void) { s_estado_pendente = 1; }

extern "C" void ffb_hid_enviar_estado_pendente(void) {
    if (!s_estado_pendente) return;
    if (!tud_hid_ready()) return;                 // sem a vez do endpoint, tenta no proximo laco
    uint8_t st = buildPidStateByte(false /*pausado*/, true /*atuadores habilitados*/,
                                   true /*chave de seguranca*/, true /*ATUADORES COM ENERGIA*/,
                                   ffb_model_algum_efeito_tocando() /*efeito tocando*/);
    if (tud_hid_n_report(0, RID_PID_STATE, &st, 1)) s_estado_pendente = 0;
}

extern "C" void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id,
                                      hid_report_type_t report_type,
                                      uint8_t const* buffer, uint16_t bufsize) {
    // ⚠️ A INTERFACE JÁ DIZ DE QUEM É O RELATÓRIO — não precisamos mais adivinhar pelo id.
    //
    // Com um canal só, tudo chegava misturado e o despacho era por faixa de report id: 0x10/0x14/
    // 0x15/0x22 do app, o resto do jogo. Funcionava, mas dependia de os dois espaços de id nunca se
    // cruzarem — uma regra frágil que ninguém do lado de fora conhece. Agora a interface separa na
    // origem: instância 1 é o app, instância 0 é o jogo. O despacho por id fica como rede de
    // segurança para firmware antigo do lado do PC.
    // TinyUSB entrega o report_id em `report_id` quando != 0; se 0, o 1º byte do buffer é o ID.
    uint8_t rid = report_id;
    const uint8_t* buf = buffer;
    uint16_t len = bufsize;
    if (rid == 0 && len > 0) { rid = buffer[0]; buf = buffer; }

    // Retrato do ultimo pacote do jogo, como ele CHEGOU. O leitor de pacotes assume que o primeiro
    // byte e o numero do relatorio — verdade quando o pacote vem pelo canal de dados, FALSO quando
    // vem pelo canal de controle (ali o numero vem separado e o pacote comeca nos dados). Nesse
    // caso tudo e lido deslocado e a forca sai zero. Isto aqui mostra qual dos dois esta
    // acontecendo, sem adivinhacao: o numero que o driver entregou, o tamanho, e os bytes crus.
    if (report_type != HID_REPORT_TYPE_FEATURE) {
        g_ffb_pacote[0] = report_id;
        g_ffb_pacote[1] = (uint8_t)bufsize;
        for (uint8_t i = 0; i < 8; ++i) g_ffb_pacote[2 + i] = (i < bufsize) ? buffer[i] : 0;
        if (g_ffb_bytes_n < 12) {
            const uint32_t k = g_ffb_bytes_n++;
            g_ffb_bytes[k][0] = report_id;
            g_ffb_bytes[k][1] = (uint8_t)bufsize;
            for (uint8_t i = 0; i < 8; ++i) g_ffb_bytes[k][2 + i] = (i < bufsize) ? buffer[i] : 0;
        }
    }

    anota_pedido((uint8_t)(rid & 0x7F));
    if (report_type == HID_REPORT_TYPE_FEATURE) {
        if (rid == RID_PID_CREATE_NEW_EFFECT) {
            s_last_effect_block = ffb_model_create_effect();   // reserva o 1º bloco livre (1-based)
            ffb_hid_avisar_estado();
        }
        return;
    }

    // OUTPUT reports: canal A0 do app (0x10 DIRECT / 0x14 SETWRITE / 0x15 SETREAD / 0x22 CMD) vs
    // FFB do jogo (0x01-0x06,0x0A-0x0D...). Despacha por report ID (buf[0]=rid quando veio pelo EP OUT).
    if (instance == 1 || rid == 0x10 || rid == 0x14 || rid == 0x15 || rid == 0x22) {
        a0_handle_out(buf, len);
    } else {
        ffb_model_handle_out(buf, len);
        // Tocar/parar muda o estado que o jogo esta esperando ouvir de volta.
        if (rid == RID_PID_EFFECT_OPERATION || rid == RID_PID_DEVICE_CONTROL) ffb_hid_avisar_estado();
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
    anota_pedido((uint8_t)(0x80u | (report_id & 0x7Fu)));

    if (report_type == HID_REPORT_TYPE_INPUT && report_id == RID_PID_STATE && reqlen >= 1) {
        buffer[0] = buildPidStateByte(false /*pausado*/, true /*atuadores habilitados*/,
                                      true /*chave de seguranca*/, true /*ATUADORES COM ENERGIA*/,
                                      ffb_model_algum_efeito_tocando() /*efeito tocando*/);
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
            anota_resposta(RID_PID_BLOCK_LOAD, buffer, 4);
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
            anota_resposta(RID_PID_POOL, buffer, 4);
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
