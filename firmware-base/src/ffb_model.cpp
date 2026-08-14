// firmware-base — modelo FFB (STAGE 3b). Dono dos objetos do pipeline puro do
// firmware-base (portados): EffectManager (banco de efeitos PID), ForceReconstructor
// (força do jogo → contínua) e computeTorqueRaw (força+efeitos+endstop → Nm). A cola HID
// alimenta os reports; o laço de 1kHz pede o torque. O ODrive faz a FOC/proteção — aqui
// só a MATEMÁTICA da força (o que resolveu a dor: separar FFB da malha de corrente).
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include "ffb_model.h"
#include "effect_manager.h"
#include "force_reconstruct.h"
#include "ffb_math.h"
#include "biquad_lp.h"   // filtro de saida (setting 21)
#include "osc_guard.h"   // guarda de oscilacao (setting 22): amortece SO durante o episodio
#include "ffb_report.h"
#include "ffb_hid_descriptor.h"
#include "motor_link.h"
#include <cmath>

using namespace drivelab;

static constexpr float k2Pi = 6.28318530718f;

// --- estado do modelo (compartilhado entre a task USB e a task de 1kHz, sem mutex —
//     mesmo padrão provado do firmware-base a 400Hz; ops curtas, torque capado) ---
static EffectManager      s_effects;
static ForceReconstructor s_recon;

// Config de bancada (ajustável pelo DriveLab Studio no Stage 4). Teto subido após a
// leitura por SWD provar que o ODrive entrega o torque pedido a corrente baixa (0,48A
// p/ 0,29Nm) — 1,0Nm era só fraco, não limitado por corrente. Subir com cautela.
static ForceConfig  s_fc = { /*totalStrengthPct*/100.0f, /*maxTorqueNm*/4.0f,
                             /*torqueLimitNm*/5.0f, /*direction*/1.0f, /*linearity*/1.0f, {} };
// SEM mola de centragem sempre-ativa (decisão do usuário 2026-08-03): o FFB é 100% do JOGO,
// como os concorrentes (Moza/Simucube) — sem jogo, o volante fica LIVRE. Isso também tira o
// susto de "runaway/notchy ao girar à mão" (era a mola reagindo à calibração marginal).
// Mantido um damper LEVE só p/ estabilidade do DD livre (não centra, só freia o movimento).
static EffectConfig s_ef = { /*springNmPerRad*/0.0f,      // SEM centragem sempre-ativa (game-driven)
                             /*damperNmPerRadPerSec*/0.03f, // damper leve de estabilidade (não é mola)
                             /*frictionNm*/0.0f };
static EndstopConfig s_ec = { /*rangeRad*/1.4f * k2Pi,  // batente por SW ~±1.4 volta (antes do fundo de escala do eixo)
                              /*stiffnessNm*/8.0f, /*dampingNmPerRadPerSec*/0.05f };

// FERRAMENTA DE BANCADA — batente desligado. 0 = desligada (uso normal), 1 = sem fim de curso.
//
// Serve para isolar o batente do resto do FFB: se sem ele o volante roda limpo em todo o curso, o
// problema está no batente; se o sintoma continuar, está no caminho de força comum.
//
// ⚠️ ESTEVE LIGADA POR ENGANO de 2026-08-08 a 2026-08-10. Foi ligada para um teste, o teste
// terminou, e ela atravessou um merge sem ninguém notar — o usuário achou o sintoma na bancada
// ("o volante está sem o batente") depois de uma sessão inteira boa na pista. Enganou porque os
// settings CHEGAM certinho (força 70, amortecimento 35) e o apply_tuning os aplica; o zero acontece
// depois, dentro do laço de 1 kHz, a cada tick. Quem olhasse só a configuração salva não veria nada
// de errado.
//
// Se precisar ligar de novo: ligue, teste, e DESLIGUE no mesmo dia. Não deixe para o commit seguinte.
#define DRVLAB_ENDSTOP_DISABLED 0

static uint32_t s_nowMs     = 0;
static uint32_t s_lastFfbMs = 0;
// Último contato com QUALQUER host — jogo (reports de FFB) OU app (canal A0). Separado de
// s_lastFfbMs de propósito: aquele governa só a força do JOGO (o jogo fechar deve calar a força
// dele mesmo com o Studio aberto); este governa o TORQUE TOTAL. Ver o fail-safe em compute_torque.
static uint32_t s_lastHostMs = 0;
static uint8_t  s_deviceGain = 255;
static float    s_telemetryForce = 0.0f;   // força aditiva de telemetria do app (DirectControl 0x10)

// Controle direto do app (report 0x10) — os quatro efeitos. Semântica e escalas no bloco longo
// junto do watchdog, mais abaixo; aqui só o estado, que o setter precisa enxergar.
static constexpr float kDirectSpringSatRad   = 30.0f * 0.01745329f;   // mola satura a 30° do centro
static constexpr float kDirectDamperSatRadPS = 4.0f * k2Pi;           // damper satura a 4 voltas/s
static volatile float s_dcConstant = 0.0f;   // -1..1 (FORÇA pronta)
static volatile float s_dcSpring   = 0.0f;   // 0..1  (GANHO, precisa da posição)
static volatile float s_dcPeriodic = 0.0f;   // -1..1 (FORÇA pronta)
static volatile float s_dcDamper   = 0.0f;   // 0..1  (GANHO, precisa da velocidade)
static uint32_t s_lastDirectMs = 0;
static float    s_slewMaxDeltaNm = 0.0f;    // P0 slew: variação máx de torque por tick (0 = off)
static float    s_prevTorque     = 0.0f;    // P0 slew: torque do tick anterior (estado do slewLimit)

// FILTRO DE SAÍDA (setting 21) — o ÚLTIMO estágio antes do torque virar corrente.
//
// Diferente dos filtros do effect_manager, que suavizam UM efeito cada (Damper, Inertia), este pega
// a soma final. Serve para o caso em que o conjunto treme mesmo com cada parcela comportada — folga
// mecânica, ressonância do rig, encoder ruidoso.
//
// ⚠️ É a faca de dois gumes mais afiada do FFB: filtrar demais é a causa MAIS COMUM de "FFB sem
// detalhe". Cada Hz a menos apaga textura de pista junto com o tremor. Por isso o padrão é 0
// (bypass) e o texto de ajuda avisa — quem liga isto deve subir o corte até o tremor sumir, e parar.
//
// O BiquadLP já trata fc<=0 como bypass, então o padrão não gasta nem CPU.
static drivelab::BiquadLP s_outputFilter;
static int   s_outputFilterHz = 0;
static OscGuard s_oscGuard = {0, 0, 0, 0.0f};
static int      s_oscGuardEnabled = 0;   // 0 = desligado (espelha o setting, para não reconfigurar à toa)

// Onde a parede do batente começa. Vem de DUAS fontes que chegam por caminhos diferentes: a DOR
// (set_config) e o "range do batente" (apply_tuning). Guardar as duas e recalcular em ambas evita
// que a última chamada apague o efeito da outra — set_config é reenviado a cada mudança de config.
static float    s_dorHalfRad       = 1.4f * k2Pi;   // DOR/2 (fim do curso)
static float    s_softStopRangeRad = 0.0f;          // quanto ANTES do fim do curso a parede começa

// ============================================================================================
// FAIXA NOVA SÓ VALE QUANDO O VOLANTE ENTRAR NELA
// ============================================================================================
// O QUE PROTEGE: reduzir a DOR com o volante fora da faixa nova é um soco instantâneo. Medido com
// a config real da bancada (rigidez 7,0 Nm/rad, recuo 8°): volante em 350°, DOR trocada para 360°
// → parede em 172° → invasão de 178° = 3,107 rad → 21,7 Nm, cortados pelo teto duro em 10 Nm.
// Dez newton-metro num tick de 1 ms, com a mão da pessoa no volante. E sem rampa: o limitador de
// variação (slew_rate) é opt-in e está em 0 por padrão.
//
// Piora porque a DOR NÃO é da aba Hardware: ela vale ao vivo, no instante do Salvar.
//
// COMO EVITA: a faixa nova fica PENDENTE e só é adotada num tick em que o volante já esteja dentro
// dela. Não existe salto possível — quando a parede passa a valer, a invasão é zero por construção.
// Se o volante já estiver dentro (o caso comum, incluindo o boot), a adoção acontece no primeiro
// tick e é imperceptível.
//
// O preço, e ele é justo: pedir uma faixa menor com o volante fora dela não faz efeito até a pessoa
// girar para dentro do curso novo. É explicável, e o alternativo é o soco.
static float    s_pendingDorHalfRad = 0.0f;   // 0 = nada pendente
volatile int32_t g_dor_pending_mdeg = 0;      // diagnóstico por SWD: faixa esperando em milideg (0 = nenhuma)

static void applyEndstopRange(void) {
    const float r = s_dorHalfRad - s_softStopRangeRad;
    s_ec.rangeRad = (r > 0.1f) ? r : 0.1f;   // nunca colapsa o curso a zero
}
// --- Medidor de CLIPPING ---------------------------------------------------------------------
// Clipping = o jogo pediu mais força do que a base consegue expressar. Dali pra frente o DETALHE
// some: zebra, perda de aderência e batida de suspensão saem todos no mesmo valor, achatados. É a
// métrica que o piloto usa pra achar o ganho máximo útil — subir a força até o clipping começar a
// aparecer nas curvas mais pesadas, e então recuar.
//
// Medimos a FRAÇÃO DO TEMPO saturado numa janela de 500 ms (a mesma cadência do monitor do app).
// Tempo, e não pico: com pico, um único solavanco pintaria 100% na tela e o número não serviria
// pra decidir nada. Duas saturações contam, porque as duas cortam detalhe:
//   1) a força do jogo estourou o fundo de escala (|hostF| ≥ 255) — o clipping clássico, do SINAL;
//   2) o teto duro de torque cortou a demanda crua — o clipping da SAÍDA.
// Escala 0-255 no fio (o app converte pra %), casando com o campo Clipping do BaseState.
//
// AS DUAS SATURAÇÕES SÃO CONTADAS SEPARADO (2026-08-11) — porque só UMA delas responde a ajuste
// nosso, e enquanto o número era um só, não dava para saber em qual botão mexer:
//
//   JOGO  — |hostF| no talo do protocolo. A força já chegou cortada; a informação se perdeu ANTES
//           de entrar na base. Nenhum ajuste daqui recupera. Quem resolve é BAIXAR O GANHO DO JOGO.
//   BASE  — o teto duro de torque cortou a demanda crua. A base recebeu o pedido inteiro e não deu
//           conta. Quem resolve é mais torque, ou menos ganho.
//
// PLATÔ, e não amostra solta: um 255 isolado é AMBÍGUO — pode ser a física pedindo o máximo naquele
// instante (pico legítimo) ou o jogo tendo cortado 150% em 100%. Os dois chegam idênticos, e o
// "quanto passou" não existe mais. O que separa é a DURAÇÃO: força de carro varia continuamente,
// então ficar cravada no máximo não é curva física, é assinatura de corte — a mesma lógica que o
// áudio usa há décadas (amostras consecutivas em fundo de escala).
//
// Contar amostra solta, como fazíamos, INFLA o número: pico legítimo entra junto com saturação de
// verdade. Isso importava mais do que parecia — a ideia de um ajuste automático que persegue um alvo
// de clipping correria atrás de um número que nunca chega lá, porque parte dele nunca foi perda.
static constexpr uint16_t kClipWindowTicks  = 500;   // ticks de 1 ms
static constexpr uint16_t kClipPlateauTicks = 3;     // 3 ms no talo = platô, não pico de passagem
static uint16_t s_clipTicks     = 0;
static uint16_t s_clipHitsGame  = 0;   // amostras dentro de um platô do jogo
static uint16_t s_clipHitsBase  = 0;   // amostras acima do nosso teto de torque
static uint16_t s_hostTopRun    = 0;   // amostras consecutivas com |hostF| no talo
static uint8_t  s_clipLevel     = 0;   // total (o campo histórico do BaseState)
static uint8_t  s_clipLevelGame = 0;
static uint8_t  s_clipLevelBase = 0;

// ACUMULADO DA SESSÃO — a fração do tempo ARMADO em que a base saturou.
//
// Substituiu o "pico de clipping da sessão" (o maior valor visto numa janela de 500 ms) porque
// aquele número era lido errado, e o jeito errado era o mais natural: "28%" parecia dizer "um terço
// da volta teve clipping" quando dizia "no PIOR meio-segundo da sessão, 28% daquele meio-segundo".
// Duas grandezas diferentes com a mesma cara.
//
// Este responde a pergunta que a pessoa realmente faz: de todo o tempo que dirigi, quanto foi no
// talo? É razão simples entre ticks, sem janela no meio. A 1 kHz, o uint32 aguenta 49 dias armado.
static uint32_t s_sessionTicks     = 0;   // ticks com o motor armado
static uint32_t s_sessionClipTicks = 0;   // destes, quantos saturaram na base

// --- Setters do canal A0 (app DriveLab Studio) ---
/// Controle direto do app (report 0x10). `constant`/`periodic` são FORÇA -1..1; `spring`/`damper`
/// são GANHO 0..1 — ver o bloco longo em compute_torque. Renova o watchdog: se o app parar de
/// mandar, a força decai sozinha em ~800 ms, que é o que impede um teste interrompido (app fechado,
/// USB puxado) de deixar o volante empurrando para sempre.
extern "C" void ffb_model_set_direct_control(float constant, float spring, float periodic, float damper) {
    const float c = (constant < -1.0f) ? -1.0f : (constant > 1.0f ? 1.0f : constant);
    const float p = (periodic < -1.0f) ? -1.0f : (periodic > 1.0f ? 1.0f : periodic);
    const float s = (spring   <  0.0f) ?  0.0f : (spring   > 1.0f ? 1.0f : spring);
    const float d = (damper   <  0.0f) ?  0.0f : (damper   > 1.0f ? 1.0f : damper);
    s_dcConstant = c; s_dcPeriodic = p; s_dcSpring = s; s_dcDamper = d;
    s_lastDirectMs = s_nowMs;
}

extern "C" void ffb_model_set_telemetry_force(float f255) {
    s_telemetryForce = f255;
    s_lastHostMs     = s_nowMs;   // o app falando também conta como host vivo
}

// Aplica os settings do app no config do FFB.
// ESCALA DE FUNDO 10 Nm (2026-08-05, era 5): medido na bancada, o sistema usava só ~18% do que o
// conjunto entrega — pico real de 2,46 Nm com Iq 4,5 A dos 25 A disponíveis. A conta fecha com folga:
// 10 Nm / 0,55 Nm/A = 18,2 A (current_lim 25 A) e 1,5×0,2016Ω×18,2² = 100 W no motor parado (fonte 27 V/30 A).
// (O fator 1,5 e da conversao trifasica — Iq e a AMPLITUDE da corrente de fase, e as tres bobinas
//  dissipam 1,5×R×Iq². Sem ele a conta subestima o calor em um terco. Confere com o caso medido do
//  ADC1: 18,05 A travados deram ~98 W. Ver a secao de fonte no README.)
// O usuário REGULA A FORÇA PELO APP — isto é só o teto da escala: total 100% × maxlimit 80% × 10 Nm
// = 8 Nm de trabalho, teto duro 10 Nm.
// ⚠️ Sem sensor de temperatura no motor (MotorTempC = -128) não há corte térmico: o limite prático é
// o calor, controlado pelo usuário (motor já validado por ele em outros firmwares).
extern "C" void ffb_model_set_config(float total_pct, float maxlimit_pct, int direction,
                                     float spring_pct, float damper_pct, int motion_range_deg,
                                     int gspring, int gdamper, int gfriction, int ginertia,
                                     int linearity_pct) {
    const float kFullScaleTorqueNm = DRVLAB_FULL_SCALE_TORQUE_NM;   // definido em ffb_model.h
    s_fc.maxTorqueNm   = (total_pct * 0.01f) * (maxlimit_pct * 0.01f) * kFullScaleTorqueNm;
    s_fc.torqueLimitNm = kFullScaleTorqueNm;                        // teto duro fixo (segurança)
    s_fc.direction     = (direction < 0) ? -1.0f : 1.0f;           // flip extra do usuário (base já é kGameForceSign)
    s_ef.springNmPerRad       = (spring_pct * 0.01f) * 0.5f;       // 100% → 0.5 Nm/rad
    s_ef.damperNmPerRadPerSec = (damper_pct * 0.01f) * 0.3f;       // 10% → 0.03 (validado)
    // A faixa NÃO é adotada aqui: vira pendente e o compute_torque adota quando o volante estiver
    // dentro dela (ver o comentário longo em s_pendingDorHalfRad). Sem isto, reduzir a DOR com o
    // volante fora da faixa nova entrega o teto de torque num único tick.
    const float half_rad = (float)motion_range_deg * 0.5f * 0.01745329f;  // DOR/2 em rad
    if (half_rad > 0.1f) {
        s_pendingDorHalfRad = half_rad;
        g_dor_pending_mdeg  = (int32_t)(half_rad * 57295.7795f);
    }
    s_effects.setTypeGains((uint8_t)gspring, (uint8_t)gdamper, (uint8_t)gfriction, (uint8_t)ginertia);
    // P0: LINEARITY (curva de resposta |x|^lin). Setting 50-200% → expoente 0.5-2.0. 100% = linear.
    // A responseCurve já é chamada no computeTorqueRaw; faltava aplicar o setting (era fixo 1.0).
    s_fc.linearity = (linearity_pct >= 10) ? (float)linearity_pct * 0.01f : 1.0f;
}

// Aplica os ajustes avançados (P0). Cada campo com valor válido é aplicado; -1 = mantém o default.
extern "C" void ffb_model_apply_tuning(const FfbTuning* t) {
    if (!t) return;
    // P0: STATIC DAMPING — atrito always-on. frictionTorque já é chamada, mas frictionNm ficava fixo em 0.
    // Setting 0-100% → 0..0.5 Nm de atrito constante. Default do schema 5% = 0,025 Nm (bem leve).
    if (t->static_damping_pct >= 0)
        s_ef.frictionNm = (float)t->static_damping_pct * 0.01f * 0.5f;
    // P0: ENDSTOP DAMPING — amortecimento do batente (não quica). Era fixo 0.05. Setting 0-100% → 0..0.2
    // Nm/rad/s. Default do schema = 25% ≈ 0,05 (sem regressão do anti-bounce atual).
    if (t->endstop_damping_pct >= 0)
        s_ec.dampingNmPerRadPerSec = (float)t->endstop_damping_pct * 0.01f * 0.2f;
    // SOFT-STOP RANGE (setting 1, 0-30°) — a parede passa a agir esse tanto ANTES do fim do curso,
    // em vez de exatamente no fim. Maior = batente mais "antecipado"/progressivo. 0 = na DOR exata.
    if (t->soft_stop_range_deg >= 0) {
        s_softStopRangeRad = (float)t->soft_stop_range_deg * 0.01745329f;
        applyEndstopRange();
    }
    // SOFT-STOP STRENGTH (setting 2, 0-100%) — rigidez da parede. Era FIXA em 8 Nm/rad; a escala de
    // 10 Nm/rad mantém o default do schema (80%) valendo os mesmos 8 Nm/rad → sem regressão.
    if (t->soft_stop_strength_pct >= 0)
        s_ec.stiffnessNm = (float)t->soft_stop_strength_pct * 0.01f * 10.0f;
    // P0: SLEW-RATE — limita a variação de torque por tick (mata spikes/clunks). slewLimit já existia mas
    // NUNCA era chamada. Setting 0=off; 1-100% → maxDelta = 2/pct Nm/tick (maior % = mais suave). Aplicado
    // no output do compute_torque (loop 1kHz).
    if (t->slew_rate_pct > 0)
        s_slewMaxDeltaNm = 2.0f / (float)t->slew_rate_pct;
    else
        s_slewMaxDeltaNm = 0.0f;
    // FILTRO DE SAÍDA (setting 21) — reconfigura SÓ quando o valor muda de verdade. Refazer os
    // coeficientes a cada Salvar zeraria o estado interno do biquad, e um filtro que perde o estado
    // dá um degrau na saída — justamente o tipo de solavanco que ele existe para evitar.
    // Q = 0,707 (Butterworth): o mais plano possível na banda que passa, sem ressalto no corte.
    if (t->osc_guard_enabled != s_oscGuardEnabled) {
        s_oscGuardEnabled = t->osc_guard_enabled;
        osc_guard_reset(&s_oscGuard);   // trocar de estado nao herda nivel da configuracao anterior
    }
    if (t->output_filter_hz != s_outputFilterHz) {
        s_outputFilterHz = t->output_filter_hz;
        s_outputFilter.configure((float)s_outputFilterHz, 0.707f, 1000.0f);  // laço de FFB = 1 kHz
        s_outputFilter.reset();
    }
    // P0: FORCE CURVE — curva de força por 5 pontos. applyForceCurve já é chamada, mas s_fc.curve ficava
    // na identidade (0/25/50/75/100). Default = linear → sem regressão. pts[0]<0 = não mexer.
    if (t->curve_pts[0] >= 0) {
        for (int i = 0; i < ForceCurve::kPontos; ++i) {
            int v = t->curve_pts[i];
            s_fc.curve.p[i] = (uint8_t)(v < 0 ? 0 : (v > 100 ? 100 : v));
        }
        s_fc.curve.prepare();   // recalcula as inclinacoes — sem isto a curva sai reta e com canto
    }
    // P0: RECONSTRUCTION — janela + LPF do reconstrutor da força constante (s_recon já ativo com defaults).
    // steps 0 = mantém o default 8 (sem regressão); >0 = janela fixa. lpf 0 = off; 1-100% → alpha 0.99..0.10
    // (maior % = mais suave). Era guardado, não aplicado.
    // ⚠️ TESTE 2026-08-08 — o setting da flash NÃO manda na janela do reconstrutor.
    // O valor salvo (provavelmente 8, o default antigo) sobrescreveria o ZOH e o atraso de fase
    // voltaria, invalidando o teste da oscilação ao inverter a direção. Reativar a linha original
    // quando a janela certa estiver decidida — e aí o default do setting também precisa mudar.
    (void)t->recon_steps;
    s_recon.cfg.steps = 1;   // ZOH — ver o comentário longo em force_reconstruct.h
    s_recon.cfg.lpfAlpha = (t->recon_lpf_pct > 0) ? (1.0f - (float)t->recon_lpf_pct * 0.009f) : 0.0f;
}

// Escala on-wire da força PID (±32767) → escala do engine (±255).
static constexpr float kPidForceToF255 = 255.0f / 32767.0f;

// Sinal GLOBAL da força do jogo no NOSSO frame. -1 é o sinal validado em bancada
// (result_torque = -forceVector × angle_ratio): a força do jogo é NEGADA relativa ao
// nosso eixo, senão o auto-alinhamento do ACC vira runaway ao soltar e os solavancos
// empurram pro lado errado (incoerência). Ajustável no Stage 4 pelo app.
static constexpr float kGameForceSign = -1.0f;

// Instrumentação p/ leitura por SWD (openocd mdw &g_ffb_dbg) — diagnostica o fluxo
// de força do jogo sem USB/printf. NÃO otimizar: volatile + símbolo global.
volatile int32_t g_ffb_dbg[12] = {0};
// [0]=handle_out calls  [1]=set-constant count  [2]=last constantForce(raw ±32767)
// [3]=last hostF(±255)  [4]=last torque*1000(Nm)  [5]=compute(armed) calls
// [6]=last deviceGain   [7]=last pos*1000(turns) [8]=usedBlocks  [9]=last Iq*1000(A)
// [10]=last dirFactor*1000  [11]=last directed force(±32767)

// ---------------------------------------------------------------------------------------------
// CONTROLE DIRETO (report 0x10) — os quatro efeitos que o app manda, e que o firmware IGNORAVA.
//
// O report sempre carregou [spring, constant, periodic, damper, drop, telem], e o firmware lia
// SÓ o `telem`. Os outros quatro trafegavam e morriam. Ficou visível quando a aba de Testes do
// Studio passou a usá-los: o volante nao se mexia, e o teste da mola concluia "comecou a 5,1°,
// terminou a 5,1°" — medindo corretamente que nada acontecera.
//
// SEMANTICA, que NAO e a mesma para os quatro:
//   constant, periodic — FORCA pronta, -1..+1 do fundo de escala. O app ja calculou a forma de
//                        onda (a senoide da varredura, o pulso do impacto) e manda o valor do
//                        instante. Somam na demanda do jogo e herdam curva, teto e batente.
//   spring, damper     — GANHO, 0..1. Nao sao forca: dependem de onde o volante esta e de quao
//                        rapido ele vai. O teste manda 0,5 e 0,15 FIXOS pelos 6 segundos, e quem
//                        faz o volante voltar ao centro e a posicao entrando na conta aqui.
//
// ESCALA DA MOLA: satura em 30° do centro. Abaixo disso e proporcional. Escolhido pelo que o
// teste precisa enxergar — ele gira o volante uns 5-20° e espera ver voltar; usar a escala da mola
// do usuario (0,5 Nm/rad no fundo) daria 0,02 Nm a 5°, que nao vence nem o atrito do rolamento.
// (As variaveis moram junto com s_telemetryForce, la em cima — o setter precisa delas.)

// Watchdog: 1.0 até 500ms de silêncio; rampa 1→0 em +300ms; 0 depois (segurança).
static float watchdogGain(uint32_t silentMs) {
    if (silentMs <= 500) return 1.0f;
    uint32_t over = silentMs - 500;
    if (over >= 300) return 0.0f;
    return 1.0f - (float)over / 300.0f;
}

extern "C" void ffb_model_advance_clock(uint32_t dms) { s_nowMs += dms; }

extern "C" float ffb_model_compute_torque(float posTurns, float velTurnsPerSec) {
    const float posRad = posTurns * k2Pi;
    const float velRad = velTurnsPerSec * k2Pi;

    // Faixa nova esperando: adota SÓ quando o volante já está dentro dela — assim a parede nasce
    // com invasão zero, em vez de aparecer empurrando. Ver s_pendingDorHalfRad.
    if (s_pendingDorHalfRad > 0.0f) {
        float parede = s_pendingDorHalfRad - s_softStopRangeRad;
        if (parede < 0.1f) parede = 0.1f;                    // mesma guarda do applyEndstopRange
        if (fabsf(posRad) <= parede) {
            s_dorHalfRad = s_pendingDorHalfRad;
            applyEndstopRange();
            s_pendingDorHalfRad = 0.0f;
            g_dor_pending_mdeg  = 0;
        }
    }

#if DRVLAB_ENDSTOP_DISABLED
    // Teste de bancada: batente OFF. Aqui, e não na inicialização, porque o app reescreve s_ec.
    s_ec.stiffnessNm           = 0.0f;
    s_ec.dampingNmPerRadPerSec = 0.0f;
#endif

    float hostF = s_recon.tick();                              // força constante do jogo, reconstruída
    hostF += s_effects.computeForce(posRad, velRad, s_nowMs);  // + efeitos periódicos/condição (Constant é pulado lá)
    hostF *= watchdogGain(s_nowMs - s_lastFfbMs);              // sinal do jogo perdido → decai a 0
    hostF *= (float)s_deviceGain / 255.0f;                     // Device Gain global do host (0x0D)
    // Telemetria do app: passa pelo watchdog de HOST. Ela é um valor ENVIADO e fica onde foi posto —
    // se o app cala, congela e empurra para sempre (foi o que fez o motor "girar igual doido" com o
    // USB fora). Mola e batente NÃO entram aqui de propósito: são recalculados a cada tick a partir
    // da posição e da velocidade atuais, então não têm como congelar — se o volante para, eles param
    // junto. Cortá-los junto foi excesso da 1ª versão deste fail-safe, e tirava o batente da base
    // sempre que não houvesse jogo nem Studio abertos (constatado na bancada minutos depois).
    hostF += s_telemetryForce * watchdogGain(s_nowMs - s_lastHostMs);

    // Controle direto do app (report 0x10) — ver o bloco longo lá em cima. Entra AQUI, na demanda em
    // force255, e não como torque no fim: assim herda curva de resposta, teto duro, batente e o
    // fail-safe de perda de host, exatamente como a força de um jogo. Um caminho de força paralelo
    // seria um jeito de dar torque sem passar por nenhuma das proteções.
    {
        const float g = watchdogGain(s_nowMs - s_lastDirectMs);
        if (g > 0.0f) {
            float direta = s_dcConstant + s_dcPeriodic;          // forças prontas, -1..1

            // Mola: puxa PARA o centro, então o sinal é o oposto da posição. Satura a 30°.
            if (s_dcSpring > 0.0f) {
                float x = posRad / kDirectSpringSatRad;
                if (x > 1.0f) x = 1.0f; else if (x < -1.0f) x = -1.0f;
                direta -= s_dcSpring * x;
            }
            // Amortecedor: opõe-se ao MOVIMENTO, então o sinal é o oposto da velocidade.
            if (s_dcDamper > 0.0f) {
                float v = velRad / kDirectDamperSatRadPS;
                if (v > 1.0f) v = 1.0f; else if (v < -1.0f) v = -1.0f;
                direta -= s_dcDamper * v;
            }
            hostF += direta * 255.0f * g;
        }
    }

    const float raw = computeTorqueRaw(hostF, posRad, velRad, s_fc, s_ef, s_ec);  // força→Nm + spring/endstop
    float t = clampf(raw, -s_fc.torqueLimitNm, s_fc.torqueLimitNm);       // TETO DURO por último

    // Clipping do JOGO — por platô. O 254.5 (e não 255.0) é de propósito: a conversão ±32767 → ±255
    // devolve 254,99… no fundo de escala exato, e um `>= 255.0f` nunca dispararia.
    if (fabsf(hostF) >= 254.5f) {
        if (s_hostTopRun < 0xFFFFu) s_hostTopRun++;
    } else {
        s_hostTopRun = 0;
    }
    // Ao CONFIRMAR o platô, contam-se também as amostras que já tinham passado — senão um platô de
    // exatamente kClipPlateauTicks contaria 1 em vez dos 3 ticks que ficou saturado.
    if (s_hostTopRun == kClipPlateauTicks)     s_clipHitsGame += kClipPlateauTicks;
    else if (s_hostTopRun > kClipPlateauTicks) s_clipHitsGame++;

    // Clipping da BASE — o pedido passou do que a base consegue ENTREGAR. Aqui não há ambiguidade
    // de platô: ou passou, ou não passou.
    //
    // ⚠️ São DOIS tetos independentes, e comparar só com o de Nm dava zero para sempre:
    //   torqueLimitNm  — o teto de configuração (15 Nm)
    //   current_lim×Kt — o que a CORRENTE permite (25 A × 0,39 = 9,75 Nm nesta bancada)
    // Com força total e limite máximo em 100%, o pedido máximo é exatamente torqueLimitNm, então
    // `> torqueLimitNm` nunca dispara — o medidor marcava 0% depois de voltas inteiras enquanto a
    // base cortava de verdade na corrente. Quem manda é o MENOR dos dois.
    float tetoEntregavel = s_fc.torqueLimitNm;
    { const float porCorrente = motor_link_get_max_torque_nm();   // 0 = config ainda sem valores
      if (porCorrente > 0.0f && porCorrente < tetoEntregavel) tetoEntregavel = porCorrente; }
    const int baseSaturou = (fabsf(raw) > tetoEntregavel);
    if (baseSaturou) s_clipHitsBase++;

    // Acumulado da sessão (ver o bloco lá em cima). Fora da janela de 500 ms de propósito: é razão
    // direta entre ticks, e é isso que faz "3%" significar "3% do tempo que dirigi".
    s_sessionTicks++;
    if (baseSaturou) s_sessionClipTicks++;

    if (++s_clipTicks >= kClipWindowTicks) {
        s_clipLevelGame = (uint8_t)((uint32_t)s_clipHitsGame * 255u / kClipWindowTicks);
        s_clipLevelBase = (uint8_t)((uint32_t)s_clipHitsBase * 255u / kClipWindowTicks);
        // Total = soma saturada. Os dois PODEM coincidir no mesmo tick (jogo no talo E pedido acima
        // do teto), e aí a soma conta duas vezes — preferimos superestimar a perda a escondê-la, e o
        // app agora mostra as duas parcelas, que é onde se lê a verdade.
        const uint32_t soma = (uint32_t)s_clipLevelGame + s_clipLevelBase;
        s_clipLevel = (uint8_t)(soma > 255u ? 255u : soma);
        s_clipTicks    = 0;
        s_clipHitsGame = 0;
        s_clipHitsBase = 0;
    }
    // ── FAIL-SAFE DE PERDA DE HOST — ver os dois watchdogs acima ─────────────────────────────
    // A regra que ficou: o que pode CONGELAR é gated (força do jogo por s_lastFfbMs, telemetria do
    // app por s_lastHostMs); o que é recalculado todo tick a partir do estado físico (mola, batente)
    // segue vivo. Um valor congelado num direct-drive não tem quem o interrompa — foi o motor
    // girando sozinho com o USB fora. Já mola e batente sem host não são risco: são função da
    // posição atual, e ainda protegem o volante de girar sem fim quando não há jogo nem app.
    if (s_slewMaxDeltaNm > 0.0f) t = slewLimit(t, s_prevTorque, s_slewMaxDeltaNm);  // P0 slew (0=off)
    s_prevTorque = t;                                                     // estado do slew p/ o próximo tick

    // FILTRO DE SAÍDA (setting 21) — o ÚLTIMO estágio, depois do slew. É o lugar certo porque ele
    // existe para tratar o torque QUE SAI: se estivesse antes do slew, o limitador de variação
    // reintroduziria degraus no sinal já suavizado.
    //
    // Não atrasa nenhum corte de segurança: os cortes duros (perda de host no ffb_task, desarme,
    // trava de bring-up) chamam motor_link_set_input_torque(0) DIRETO, sem passar por aqui. O que
    // passa por este caminho é a queda suave do watchdog, que já é uma rampa de 300 ms — muito mais
    // lenta que qualquer corte que este filtro possa ter.
    //
    // Com o padrão 0 o biquad está em bypass e devolve a entrada intacta.
    t = s_outputFilter.process(t);

    // GUARDA DE OSCILAÇÃO (setting 22) — DEPOIS do filtro, e de propósito: ela é uma força de
    // amortecimento nova, não um tratamento do sinal que já existe. Filtrá-la junto atrasaria
    // justamente a reação que precisa ser rápida para cortar o episódio.
    //
    // Fica FORA do slew pelo mesmo motivo: limitar a variação dela seria limitar a frenagem.
    //
    // Ela é somada ao torque final e passa pelo teto duro logo abaixo — nenhuma força escapa do
    // limite, nem a que existe para proteger.
    if (s_oscGuardEnabled) {
        t += osc_guard_update(&s_oscGuard, velRad, 1, 1);
        if (t >  s_fc.torqueLimitNm) t =  s_fc.torqueLimitNm;
        if (t < -s_fc.torqueLimitNm) t = -s_fc.torqueLimitNm;
    }
    g_ffb_dbg[10] = (int32_t)(s_oscGuard.nivel * 1000.0f);   // p/ ver por SWD a guarda agindo

    g_ffb_dbg[5]++;
    g_ffb_dbg[3] = (int32_t)hostF;
    g_ffb_dbg[4] = (int32_t)(t * 1000.0f);
    g_ffb_dbg[6] = s_deviceGain;
    g_ffb_dbg[7] = (int32_t)(posTurns * 1000.0f);
    g_ffb_dbg[8] = s_effects.usedBlocks();
    g_ffb_dbg[9] = (int32_t)(motor_link_get_iq_measured() * 1000.0f);
    return t;
}

// Nível de clipping 0-255 pra telemetria (a0_channel). Ver o bloco do medidor lá em cima.
extern "C" uint8_t ffb_model_get_clipping(void) { return s_clipLevel; }

// As duas parcelas, separadas. É nelas que se lê EM QUE BOTÃO MEXER: a do jogo só cede baixando o
// ganho dentro do jogo (a força já chegou cortada), a da base cede com mais torque ou menos ganho.
extern "C" uint8_t ffb_model_get_clipping_game(void) { return s_clipLevelGame; }
extern "C" uint8_t ffb_model_get_clipping_base(void) { return s_clipLevelBase; }

// Fração do tempo ARMADO em que a base saturou, 0-255. Ver o bloco do acumulado lá em cima.
extern "C" uint8_t ffb_model_get_clipping_session(void) {
    if (s_sessionTicks == 0) return 0;
    // 64 bits no numerador: s_sessionClipTicks x 255 estoura 32 bits depois de ~4,7 horas armado.
    return (uint8_t)(((uint64_t)s_sessionClipTicks * 255u) / s_sessionTicks);
}

// Zera o medidor quando o motor NÃO está armado. Sem isto o último valor medido congela na tela
// (o compute_torque só roda armado), e o usuário veria "40% de clipping" numa base parada.
extern "C" float ffb_model_dor_half_rad(void) { return s_dorHalfRad; }

extern "C" void ffb_model_reset_transient(void) {
    // "Memória do último torque emitido" — sem sentido depois de uma pausa. Ver o header.
    s_prevTorque = 0.0f;
    s_outputFilter.reset();
    s_recon.reset();
}

extern "C" void ffb_model_reset_clipping(void) {
    s_clipTicks     = 0;
    s_clipHitsGame  = 0;
    s_clipHitsBase  = 0;
    s_hostTopRun    = 0;
    s_clipLevelGame = 0;
    s_clipLevelBase = 0;
    s_clipLevel = 0;
    // A sessão é o período ARMADO: desarmar encerra e o próximo arme começa do zero. Sem isto, o
    // número da volta de hoje viria diluído por todo o tempo parado desde que a placa ligou.
    s_sessionTicks     = 0;
    s_sessionClipTicks = 0;
}

extern "C" uint8_t ffb_model_create_effect(void) { return s_effects.allocateBlock(); }
extern "C" int     ffb_model_used_blocks(void)   { return s_effects.usedBlocks(); }
extern "C" int     ffb_model_max_blocks(void)    { return kEffectSlots; }
extern "C" void    ffb_model_set_device_gain(uint8_t g) { s_deviceGain = g; }

extern "C" void ffb_model_handle_out(const uint8_t* buf, uint16_t len) {
    if (buf == nullptr || len < 1) return;
    s_lastFfbMs  = s_nowMs;                         // qualquer report = jogo ativo (reseta watchdog)
    s_lastHostMs = s_nowMs;                         // e também conta como host vivo (fail-safe geral)
    g_ffb_dbg[0]++;

    s_effects.handleReport(buf, len, s_nowMs);      // roteia 0x01-0x06, 0x0A, 0x0B, 0x0C pro banco

    if (buf[0] == RID_PID_DEVICE_GAIN && len >= 2)  // 0x0D Device Gain global
        s_deviceGain = buf[1];

    if (buf[0] == RID_PID_DEVICE_CONTROL && len >= 2) {   // 0x0C: stop/reset → zera a força constante também
        s_recon.setTarget(0.0f);
    }

    FfbOut o = ffb_parse_out(buf, len);
    if (o.type == FFB_SET_CONSTANT_FORCE) {         // força constante do jogo → reconstrutor (suave)
        // SENTIDO: aplica o campo Direction do Set Effect (axisMagnitudes[0]) — sem isso a
        // força fica com sinal fixo/errado (incoerente + runaway ao soltar). Validado em bancada.
        const float dir = s_effects.axisDirFactor(o.effectBlock);
        const float directed = (float)o.constantForce * dir * kGameForceSign;
        // FIX de escala: ±32767 (on-wire PID) → ±255 (escala do engine).
        s_recon.setTarget(directed * kPidForceToF255);
        g_ffb_dbg[1]++;
        g_ffb_dbg[2] = o.constantForce;
        g_ffb_dbg[10] = (int32_t)(dir * 1000.0f);
        g_ffb_dbg[11] = (int32_t)directed;
    }
}
