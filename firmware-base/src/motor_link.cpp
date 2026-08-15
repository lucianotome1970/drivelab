// firmware-base — ponte ODrive. ÚNICO arquivo que inclui odrive_main.h (isola o
// class Axis do ODrive). API extern "C" plana pro nosso ffb_task. Autor: Luciano Tomé — MIT.
#include "motor_link.h"
#include "encoder_config.h"   // traducao settings -> encoder (host-testada)
#include "motor_config.h"     // traducao settings -> motor (host-testada)
#include "thermal_config.h"   // traducao settings -> limites termicos (host-testada)
#include "odrive_main.h"
#include "low_level.h"   // safety_critical_arm_brake_resistor (armar sem janela — ver autoscale)

// SENTIDO REPORTADO do encoder (setting 9). +1 = como vem do sensor, -1 = invertido.
//
// ⚠️ NAO e o sentido da FOC. Aquele o ODrive DESCOBRE sozinho na calibracao: ele gira o motor e
// olha para onde a contagem anda (encoder.cpp). Forcar aquele valor quebraria o motor, nao o
// configuraria — e por isso o campo nunca teve onde ser aplicado la.
//
// O que este resolve e o sintoma que o proprio texto de ajuda descreve: "o volante gira para um
// lado e a tela mostra o outro". Isso acontece quando o sensor foi montado espelhado — a FOC se
// adapta, mas tudo o que NOS reportamos sai com o sinal trocado.
//
// Aplicado nos DOIS getters de uma vez, que sao o ponto unico por onde a posicao passa para o FFB,
// para o eixo do jogo, para a telemetria e para as guardas. Inverter num so deixaria a velocidade
// discordando da posicao — e velocidade com sinal errado e anti-amortecimento, que ja nos custou
// caro no laco de corrente.
static float s_pos_sign = 1.0f;

extern "C" float motor_link_get_pos_turns(void) {
    int32_t cnt = axes[0].encoder_.shadow_count_;      // int32 acumulado (não wrappa) — ESTADO VALIDADO
    int32_t cpr = axes[0].encoder_.config_.cpr;
    return cpr ? s_pos_sign * (float)cnt / (float)cpr : 0.0f;
}
extern "C" void motor_link_set_input_torque(float nm) {
    // Efetivo só em CLOSED_LOOP + control_mode=TORQUE + input_mode=PASSTHROUGH.
    // Fora disso o Controller::update() ignora → seguro chamar todo tick.
    axes[0].controller_.input_torque_ = nm;
}
extern "C" float motor_link_get_vel_estimate(void) {
    return s_pos_sign * axes[0].encoder_.vel_estimate_.any().value_or(0.0f);
}
extern "C" float motor_link_get_iq_measured(void) {
    return axes[0].motor_.current_control_.Iq_measured_;
}
extern "C" int motor_link_motor_is_armed(void) {
    return axes[0].motor_.is_armed_ ? 1 : 0;
}
extern "C" int      motor_link_axis_state(void)  { return (int)axes[0].current_state_; }
extern "C" uint32_t motor_link_axis_error(void)  { return (uint32_t)axes[0].error_; }
extern "C" uint32_t motor_link_motor_error(void) { return (uint32_t)axes[0].motor_.error_; }
extern "C" uint32_t motor_link_encoder_error(void) { return (uint32_t)axes[0].encoder_.error_; }
// --- Diagnóstico da falha do CONTROLADOR -----------------------------------------------------
// Quando o motor desarma com axis_err=CONTROLLER_FAILED, o "porquê" está aqui — e SÓ aqui. O
// bitfield do axis diz apenas "o controlador falhou"; qual falha foi (SPINOUT_DETECTED,
// INVALID_ESTIMATE, OVERSPEED...) fica neste campo. Capturado em 2026-08-06 na bancada: perdemos
// justamente este dado porque o auto-arme chamou clear_errors() 68 ms depois e apagou tudo.
extern "C" uint32_t motor_link_controller_error(void) {
    return (uint32_t)axes[0].controller_.error_;
}

// O erro GLOBAL, que não é o do eixo. Tudo que é do BARRAMENTO — sobretensão, subtensão, excesso de
// corrente de regeneração — mora aqui, e não em nenhum dos campos acima. Foi por isso que os trips
// de tensão passaram tanto tempo sendo confundidos com "teto de torque": olhávamos o eixo, que
// dizia zero, enquanto a causa estava neste campo.
extern "C" uint32_t motor_link_odrv_error(void) { return (uint32_t)odrv.error_; }

// As duas potências filtradas que alimentam a detecção de spinout do ODrive. Guardá-las no instante
// da falha é o que confirma (ou descarta) a hipótese do spinout sem precisar adivinhar: se no
// momento do desarme a mecânica estiver abaixo do limiar negativo e a elétrica acima do positivo,
// foi spinout — que num volante FFB é condição NORMAL (segurar o volante contra a força freia o
// movimento consumindo corrente), não anomalia.
extern "C" float motor_link_get_mech_power(void) { return axes[0].controller_.mechanical_power_; }
extern "C" float motor_link_get_elec_power(void) { return axes[0].controller_.electrical_power_; }

extern "C" float motor_link_get_vbus(void) { return vbus_voltage; }
extern "C" float motor_link_get_input_torque(void) { return axes[0].controller_.input_torque_; }

// 🔴 TEMPERATURA DO MCU — DESATIVADA EM 2026-08-06. NÃO RELIGAR do jeito anterior.
//
// A implementação anterior lia o sensor interno do STM32 colocando-o no lugar do canal 7 da
// sequência regular do ADC1, com ADC_SAMPLETIME_480CYCLES (contra 15 dos demais). Justifiquei a
// troca com "ninguém lê o índice 7 do buffer" — verdade, e irrelevante. O que mudou foi o TEMPO DE
// CONVERSÃO, que derruba a varredura do ADC1 de ~48 kHz para ~23 kHz. E é no mesmo ADC1 que o vbus
// é amostrado pelo canal INJETADO, dentro da ISR de controle de 8 kHz. Num periférico
// compartilhado, "o dado não é lido" não é a mesma coisa que "não tem efeito".
//
// O que isso causou, medido por SWD na bancada:
//   Iq = 18,05 A com o volante PARADO e torque comandado ZERO (valor travado, sem oscilar)
//   → ~98 W de puro calor no motor, zero torque → motor esquentando parado
//   → SPINOUT_DETECTED → motor desarma → "perdeu o FFB"
// Com este mesmo código removido: 0,2 A parado, e o motor ESFRIA até sob teste forçado.
//
// Se for religar um dia: NÃO tocar no timing da sequência regular do ADC1. Caminhos possíveis a
// investigar — um ADC separado (mas o sensor de temp só existe no ADC1), ou amostrar sob demanda
// com o motor desarmado. Antes de qualquer coisa, medir Iq com o volante parado.
extern "C" float motor_link_get_mcu_temp_c(void) {
    return -128.0f;   // -128 = "sem sensor" no protocolo A0 → o app mostra "—", não "0 °C"
}

// TORQUE MAXIMO QUE A BASE CONSEGUE ENTREGAR DE VERDADE, em Nm.
//
// Nao e o teto de Nm da configuracao — e o que a CORRENTE permite:
//
//     current_lim [A]  x  torque_constant [Nm/A]
//
// Os dois tetos sao independentes e o menor deles e que manda. Com o Kt medido nesta bancada (0,39)
// e o limite em 25 A, a base entrega no maximo 9,75 Nm por mais que a configuracao diga 15: pedir 12
// Nm manda 30,7 A, o ODrive corta em 25, e chegam 9,75. Perda real de forca que o medidor de
// clipping nao via, porque ele so comparava o pedido com o teto de Nm — e 12 e menor que 15.
//
// Zero quando a config ainda nao tem valores validos: o chamador trata como "sem limite conhecido"
// e nao acusa saturacao, em vez de acusar saturacao o tempo todo.
extern "C" float motor_link_get_max_torque_nm(void) {
    const float lim = axes[0].motor_.config_.current_lim;
    const float kt  = axes[0].motor_.config_.torque_constant;
    if (lim <= 0.0f || kt <= 0.0f) return 0.0f;
    return lim * kt;
}

// TEMPERATURA DOS FETs — a unica temperatura REAL que esta placa entrega hoje.
//
// Diferente do sensor do MCU acima, aqui nao ha nada a inventar: a placa tem um termistor colado no
// estagio de potencia, o ODrive ja o le no canal 15 do ADC e ja converte para °C pelo polinomio
// (fet_thermistors em board_v3.cpp, temperature_ atualizado pelo proprio update do ODrive). Nos e
// que mandavamos -128 na telemetria, e o app mostrava "—" como se nao houvesse sensor.
//
// NAO confundir com o caso do MCU: la o problema era o TIMING do ADC1 compartilhado com o vbus da
// ISR de 8 kHz. Aqui so lemos um float ja calculado — nenhuma conversao nova, nenhum canal tocado.
//
// NaN durante a inicializacao (antes da primeira conversao) vira -128, senao o app mostraria lixo.
extern "C" float motor_link_get_fet_temp_c(void) {
    const float t = fet_thermistors[0].temperature_;
    if (t != t) return -128.0f;                  // NaN: ainda inicializando
    if (t < -100.0f || t > 200.0f) return -128.0f;  // fora da faixa fisica = leitura invalida
    return t;
}

// SEGURANÇA: desabilita todo o auto-arme/calibração de boot. A calibração de offset do encoder
// no boot é INSTÁVEL nesta placa (CPR_MISMATCH intermitente) e, quando trava, GIRA o motor sem
// parar. Chamado em ffb_storage_preload (antes dos threads do eixo) → boota em IDLE (motor off).
// Arme passa a ser DELIBERADO (não usa o startup do ODrive). Runtime override (não mexe na NVM).
extern "C" void motor_link_disable_autostart(void) {
    axes[0].config_.startup_closed_loop_control = false;
    axes[0].config_.startup_encoder_offset_calibration = false;
    axes[0].config_.startup_motor_calibration = false;
    axes[0].config_.startup_encoder_index_search = false;
}
// Aborta qualquer estado != IDLE (rede de segurança): pede IDLE → o motor desliga.
extern "C" void motor_link_request_idle(void) { axes[0].requested_state_ = Axis::AXIS_STATE_IDLE; }

// Leitores da config do encoder/motor (diagnóstico do CPR_MISMATCH).
extern "C" int32_t motor_link_enc_cpr(void)        { return (int32_t)axes[0].encoder_.config_.cpr; }
extern "C" int32_t motor_link_enc_mode(void)       { return (int32_t)axes[0].encoder_.config_.mode; }
extern "C" int32_t motor_link_enc_use_index(void)  { return axes[0].encoder_.config_.use_index ? 1 : 0; }
extern "C" int32_t motor_link_motor_pole_pairs(void){ return (int32_t)axes[0].motor_.config_.pole_pairs; }
extern "C" int32_t motor_link_enc_shadow_count(void){ return axes[0].encoder_.shadow_count_; }

// Config real (NVM) — diagnóstico do "não calibra/não arma/apito".
extern "C" int32_t motor_link_startup_flags(void) {
    return (axes[0].config_.startup_motor_calibration          ? 1 : 0)
         | (axes[0].config_.startup_encoder_offset_calibration ? 2 : 0)
         | (axes[0].config_.startup_closed_loop_control        ? 4 : 0)
         | (axes[0].config_.startup_encoder_index_search       ? 8 : 0);
}
extern "C" int32_t motor_link_precal_flags(void) {
    return (axes[0].motor_.config_.pre_calibrated  ? 1 : 0)
         | (axes[0].encoder_.config_.pre_calibrated? 2 : 0)
         | (axes[0].encoder_.config_.use_index     ? 4 : 0)
         | (axes[0].motor_.is_calibrated_          ? 8 : 0)
         | (axes[0].encoder_.is_ready_             ? 16 : 0);
}
extern "C" int32_t motor_link_motor_R_uohm(void) { return (int32_t)(axes[0].motor_.config_.phase_resistance * 1000000.0f); }
extern "C" int32_t motor_link_motor_L_nH(void)   { return (int32_t)(axes[0].motor_.config_.phase_inductance * 1e9f); }

// SEM APITO + arme confiável: pula a MEDIÇÃO do motor no boot (o apito) usando o R/L já guardado
// (motor.pre_calibrated=TRUE, is_calibrated=TRUE). Também pula a etapa que falhava (MOTOR_FAILED).
// Mantém a cal de offset do encoder (movimento, não apito) + o auto-arme. Runtime override (não NVM).
extern "C" void motor_link_skip_motor_cal(void) {
    axes[0].config_.startup_motor_calibration = false;
}

// Desliga a EXIGÊNCIA do brake resistor pro arme (o ODrive desarma se enable&&!armed). O resistor da
// bancada nunca conduziu → brake_resistor_armed=0 → o motor desarmava (BRAKE_RESISTOR_DISARMED). A ~19,6V
// a regen do volante é pequena (caps + proteção de sobretensão cobrem). ⚠️ Reabilitar quando o brake
// resistor for validado / em bus alto (56V). Runtime override (não NVM). Ver 08e1b22 / drivelab-brake-chopper.
extern "C" void motor_link_disable_brake_resistor(void) {
    odrv.config_.enable_brake_resistor = false;
}

// Escala do vbus (divider ADC) — definida em ffb_task.cpp, aplicada no low_level (runtime, não compile-time).
extern volatile float g_vbus_voltage_scale;

// PERFIL DE HARDWARE (Placa + Fonte) → deriva os limites de segurança. É a MESMA lógica que o app espelha.
// Chamado do a0_apply_settings quando o app manda o perfil (e no init com defaults).
//   board_variant: convenção do app (SettingOptions) → 0 = placa 24V · 1 = placa 56V (default)
//   bus_nominal_v: tensão nominal da fonte [V] (reservado — display; trips relativos ao nominal = follow-up)
//   supply_amps:   reservado — PENDENTE campo dedicado de amperagem da fonte → dc_max_positive_current.
//                  (o power_limit(16) do schema é "%", NÃO amperagem — por isso NÃO é mapeado aqui.)
extern "C" void motor_link_apply_hw_profile(int board_variant, int bus_nominal_v, int supply_amps) {
    if (board_variant == 0) {                                  // PLACA 24V
        g_vbus_voltage_scale = 3.3f * 11.0f / 4096.0f;         // divider 11
        odrv.config_.dc_bus_overvoltage_trip_level = 28.0f;    // caps ~30V → teto BAIXO
    } else {                                                   // PLACA 56V (default = 1)
        g_vbus_voltage_scale = 3.3f * 19.0f / 4096.0f;         // divider 19
        odrv.config_.dc_bus_overvoltage_trip_level = 55.0f;    // folga p/ regen (caps ~63V)
    }
    odrv.config_.dc_bus_undervoltage_trip_level = 8.0f;        // piso anti brown-out
    (void)bus_nominal_v;
    (void)supply_amps;
}

// BRING-UP de PLACA NOVA: calibra o NOSSO motor do zero (a NVM de fábrica é de outro motor). Seta a
// geometria (pole_pairs=15 do hoverboard, cpr=4000 do E6B2 1000 PPR ×4, incremental), corrente de cal
// SEGURA (10A, não os 30 do perfil) e limite modesto (falha-seguro). Faz cal completa (mede R/L → "apito")
// + offset do encoder + arma no fim. Brake desligado. Roda 1x na placa nova pra testar o DRV limpo.
extern "C" void motor_link_newboard_bringup(void) {
    // MOTOR — geometria + R/L do NOSSO hoverboard (medidos na placa antiga: 0,20Ω/0,35mH; batem com o
    // config hoverboard de referência 0,174Ω/0,349mH). PRÉ-CALIBRADO → is_calibrated_ SEM medir R/L.
    // ⚠️ CHAVE: pular a medição de indutância é o que EVITA o DRV_FAULT (L baixa → ΔI=V·Δt/L explode →
    // OCP do DRV8301). Confirmado pelo core do ODrive (measure_phase_inductance). NÃO por
    // startup_motor_calibration=true (foi o meu erro anterior — rodava a medição perigosa).
    axes[0].motor_.config_.motor_type               = Motor::MOTOR_TYPE_HIGH_CURRENT;
    axes[0].motor_.config_.pole_pairs               = 15;
    axes[0].motor_.config_.torque_constant          = 0.55f;      // hoverboard
    // R/L MEDIDOS NESTE CONJUNTO (placa + motor atuais) em 2026-08-08, rodando a medição de verdade
    // com startup_motor_calibration=true — o "apito". Antes daqui, estes dois campos carregavam
    // números da PLACA ANTIGA: 0,20 Ω e 0,35 mH. O usuário apontou que nunca tínhamos medido neste
    // conjunto ("não temos R/L guardado") e estava certo em levantar.
    //
    // O que a medição mostrou: R praticamente igual (0,2016 vs 0,20) e L 12% MAIOR (0,3926 vs 0,35).
    // Ou seja, os emprestados eram bons — não desajustavam o loop de corrente a ponto de explicar
    // nada. Ficam os reais mesmo assim: número medido vale mais que número herdado.
    //
    // E o apito passou LIMPO, sem o DRV_FAULT que nos fez desligar a medição no passado. Aquele
    // fault era mais um efeito dos 5 V no rail de 3,3 V (erro de ligação do ST-Link, achado no mesmo
    // dia) — não da medição em si. Se algum dia for preciso re-medir num conjunto novo, é só inverter
    // as duas linhas abaixo: pre_calibrated=false + startup_motor_calibration=true.
    axes[0].motor_.config_.phase_resistance         = 0.2016f;    // MEDIDO neste conjunto
    axes[0].motor_.config_.phase_inductance         = 0.000393f;  // MEDIDO neste conjunto
    axes[0].motor_.config_.pre_calibrated           = true;       // usa os medidos, SEM apito no boot
    // CORRENTE DE LOCK-IN DA CALIBRAÇÃO — 3A → 8A (2026-08-07).
    //
    // 8A é o valor de configurações de referência para motor de hoverboard com pole_pairs=15 e
    // torque_constant=0,55 — os mesmos do nosso conjunto — e o resto daquelas configurações bate com a
    // nossa (current_lim 25, requested_current_range 60, bandwidth 200, calib_range 0.02,
    // resistance_calib_max_voltage 12). A corrente de calibração era a ÚNICA divergência numérica.
    //
    // POR QUE 3A ERA POUCO: a cal de offset trava o rotor numa posição elétrica conhecida (lock-in). Com
    // 3A são ~1,6 Nm, e o cogging do hoverboard come boa parte disso — o rotor assenta num detente de
    // cogging vizinho em vez da fase elétrica real. A cal CONCLUI (is_ready=1, erro zero) com um offset
    // impreciso, e o erro só aparece sob torque: "centraliza e depois se perde".
    //
    // ⚠️ O comentário antigo dizia "8A tripava o DRV" — mas aquilo foi observado quando a cal AINDA MEDIA
    // R/L, e é a medição de INDUTÂNCIA que dispara o OCP do DRV8301 (L baixa → ΔI=V·Δt/L explode), não a
    // corrente de lock-in. Com pre_calibrated=true (acima) a medição de R/L é pulada, que é exatamente a
    // configuração de referência que roda 8A. Se o DRV_FAULT voltar no boot, ESTE é o primeiro a reverter.
    // CORRENTE DE LOCK-IN — 6 A → 10 A (2026-08-08).
    //
    // ⚠️ ESTE COMENTÁRIO DIZIA "depois de PROVAR que não há índice", e isso é FALSO. O canal Z está
    // ligado e funciona: o índice foi detectado após 0,64 volta girando o volante à mão (ver o bloco
    // do use_index mais abaixo). A "prova" era a medição de 3,03 voltas com index_found=0, que o
    // próprio arquivo declara INVÁLIDA logo adiante — o motor estava desconectado e o encoder, sem
    // alimentação, não gerava pulso nenhum. Um comentário errado sobrevive a quem o escreveu: este
    // ficou meses afirmando que a placa não tem Z, e voltou a induzir essa conclusão em 15/08/2026.
    //
    // O que segue valendo é a CONSEQUÊNCIA, porque hoje o índice está desligado por opção (o teste
    // de 08/08, ver abaixo) e não por ausência: com use_index=false o zero elétrico é achado por
    // LOCK-IN a cada boot — aplica-se corrente numa fase conhecida e
    // assume-se que o rotor parou exatamente ali. No hoverboard o cogging é forte, e o rotor assenta
    // no detente magnético mais próximo em vez do ponto elétrico real. Esse erro vira o offset.
    //
    // A corrente de lock-in é a ÚNICA autoridade que temos contra o cogging: torque de lock-in muito
    // maior que o de detente obriga o rotor a ir para o lugar certo. 6 A foi na direção errada.
    //
    // POR QUE ISSO IMPORTA TANTO: com torque pequeno o desvio é imperceptível (medimos o damper em
    // 0,1 Nm e tudo parecia bem). Com o batente em 10 Nm ele decide o resultado — medimos 18 A
    // aplicados e o volante CONTINUANDO a sair, de 1,606 até 2,335 voltas. Corrente que não vira
    // torque na direção certa vira calor, e é o "ao invés de me empurrar, ela me puxa" da bancada.
    //
    // Se 10 A aquecer demais no lock-in (ele fica PARADO aplicando corrente), o próximo passo não é
    // baixar de novo: é alinhar em vários pontos e promediar, que cancela o cogging sem depender de
    // força bruta. Ver as notas de align sem índice.
    axes[0].motor_.config_.calibration_current      = 7.0f;
    // Teto de tensão da medição de R (default nosso 8 V). 12 V é o valor da config de referência que
    // MEDE e roda estável — se vamos medir como eles, o parâmetro da medição tem de ser o deles.
    axes[0].motor_.config_.resistance_calib_max_voltage = 12.0f;
    axes[0].motor_.config_.requested_current_range  = 60.0f;      // headroom de saturação (não abaixar!)
    axes[0].motor_.config_.current_control_bandwidth= 200.0f;     // L baixa → NÃO 1000 (default nosso já 200)
    axes[0].motor_.config_.current_lim              = 25.0f;
    axes[0].motor_.config_.current_lim_margin       = 8.0f;
    // ENCODER — incremental COM ÍNDICE (canal Z). Ver o bloco do índice logo abaixo.
    axes[0].encoder_.config_.mode                   = Encoder::MODE_INCREMENTAL;
    axes[0].encoder_.config_.cpr                    = 4000;       // E6B2 1000 PPR ×4
    axes[0].encoder_.config_.bandwidth              = 200.0f;
    // ── VARREDURA DA CALIBRAÇÃO DE OFFSET (2026-08-08) ──────────────────────────────────────
    // O que derrubou o FFB na bancada foi CPR_POLEPAIRS_MISMATCH (enc_err=0x2, capturado no latch
    // da 1ª falha). Esse erro NÃO diz que o CPR está errado — nós MEDIMOS o CPR girando o motor em
    // malha aberta por 10 voltas e ele fechou em 0,16% (taxa) / 1% (total). Geometria correta.
    //
    // O que ele valida é a VARREDURA: o ODrive compara quanto o encoder andou durante a calibração
    // com quanto ele esperava, e falha se a diferença passar de calib_range. Se o rotor fica para
    // trás em algum trecho — cogging do hoverboard, atrito, inércia —, a soma não bate e ele acusa
    // mismatch mesmo com o CPR perfeito. É a diferença entre "a geometria está certa" e "o rotor
    // conseguiu seguir o campo".
    //
    // Duas frentes, nesta ordem de mérito:
    //   1) omega 4π → π rad/s: a varredura fica 4× mais LENTA, dando tempo ao rotor de acompanhar.
    //      Ataca a CAUSA. Custo: a calibração passa de ~4 s para ~16 s por varredura (o teto do
    //      ffb_task já subiu para 40 s, então cabe).
    //   2) calib_range 0,02 → 0,05: afrouxa o CRITÉRIO. Sozinho isso só faria aceitar uma
    //      calibração ruim; junto com (1) serve de margem para o resíduo de cogging.
    // ⚠️ π (4× mais lento) TRAVOU O USB (bancada 2026-08-08). A calibração passou de ~10 s para
    // ~35 s, e ela roda na thread do EIXO, que tem prioridade MAIOR que a do nosso laço de FFB — que
    // é quem envia os relatórios USB. 35 s sem o laço ter vez = o Windows derruba o dispositivo. O
    // firmware seguia vivo (uwTick subindo), só não tinha janela para falar. 2π mantém a folga que o
    // rotor precisa para acompanhar a varredura, com metade do tempo.
    axes[0].encoder_.config_.calib_scan_omega       = 6.28318531f;  // 2π: 2× mais lenta que o default (era π = 4×, longa demais)
    axes[0].encoder_.config_.calib_range            = 0.05f;        // era 0,02 — margem p/ cogging
    // ══ ÍNDICE DO ENCODER (canal Z) — VALIDADO NA BANCADA 2026-08-08 ═══════════════════════════
    //
    // O canal Z ESTÁ ligado e FUNCIONA: com a escuta armada e o motor desarmado, girando o volante
    // à mão, o índice foi detectado após 0,64 volta (shadow=574). O encoder tem 5 fios — VCC, GND,
    // A, B e Z (laranja) — e o Z chega em PC9 (o build usa main.h → GPIOC pino 9; confirmado
    // expandindo o macro, porque mxconstants.h diz PA15 e valeria para placas v3.2/v3.4).
    //
    // ⚠️ DOIS TESTES ANTERIORES DERAM "NÃO TEM ÍNDICE" E OS DOIS ERAM INVÁLIDOS:
    //   1) liguei use_index escrevendo direto na memória por SWD — mas quem ARMA a interrupção é
    //      set_idx_subscribe(), chamado em Encoder::setup() no BOOT. Ligar o campo depois não
    //      inscreve ninguém: o índice podia estar chegando sem nada escutando.
    //   2) o motor estava DESCONECTADO — sem VBUS o encoder não é alimentado e não gera pulso
    //      nenhum (o contador ficou cravado em 0, o que eu quase li como "encoder morto").
    // Lição: antes de concluir "o hardware não tem", provar que o caminho de medida está vivo.
    //
    // POR QUE O ÍNDICE RESOLVE O PROBLEMA CENTRAL: sem ele, o zero elétrico vem de um LOCK-IN por
    // boot — aplica-se corrente numa fase e assume-se que o rotor parou ali. Com o cogging do
    // hoverboard ele assenta no detente magnético vizinho, e esse erro VIRA o offset. Sai um valor
    // diferente a cada partida: a base funciona num dia e não no outro, e a assimetria troca de
    // lado entre calibrações. Com torque pequeno o desvio é imperceptível (o damper media 0,1 Nm e
    // parecia tudo bem); com o batente em 10 Nm ele decide o resultado — medimos 18 A aplicados e o
    // volante CONTINUANDO a sair, de 1,606 até 2,335 voltas. Corrente no ângulo errado vira calor,
    // e é o "ao invés de me empurrar, ela me puxa" relatado na bancada.
    //
    // O índice é uma marca FÍSICA no disco do encoder: ancorando nela, o offset passa a ser o mesmo
    // em todo boot, e a corrente de lock-in deixa de ser crítica (era por isso que oscilávamos
    // entre 3, 6, 8 e 10 A sem achar um valor bom — nenhum era, o método é que era frágil).
    //
    // find_idx_on_lockin_only=false: escuta SEMPRE, não só durante a varredura. Custa uma
    // interrupção por volta e permite validar o Z girando à mão, sem energizar o motor.
    // ⚠️ TESTE 2026-08-08 — ÍNDICE DESLIGADO para isolar a assimetria POR ORDEM.
    // O sintoma da bancada: o batente do PRIMEIRO lado visitado segura; o do SEGUNDO falha — e o
    // lado que falha TROCA conforme por onde se começa. Isso não é posição fixa, é algo que muda de
    // estado ao percorrer o curso. A reancoragem contínua pelo índice (que liguei hoje) é candidata:
    // cada passagem pelo Z faz set_circular_count(0), reancorando a FASE ELÉTRICA, e o Z fica num
    // ponto físico único — cruza-se indo para um lado, não para o outro. Se o índice não coincidir
    // com o offset calibrado, a fase SALTA ao cruzar, e o extremo seguinte fica errado.
    // Desligado: volta ao comportamento anterior (offset só do lock-in, sem reancoragem).
    // Se a assimetria por ordem SUMIR → era a reancoragem (introduzida por mim hoje).
    // Se PERSISTIR → o offset já nasce ruim da calibração, e o alvo é o método de alinhamento.
    axes[0].encoder_.config_.use_index               = false;
    axes[0].encoder_.config_.find_idx_on_lockin_only = false;
    // 🔴 use_index_offset = FALSE — NÃO deixar o índice mexer na POSIÇÃO do volante.
    //
    // Com ele true (default do ODrive), enc_index_cb chama set_linear_count(index_offset * cpr) a
    // cada passagem pelo Z — e set_linear_count escreve shadow_count_ direto, que é EXATAMENTE o que
    // motor_link_get_pos_turns() lê. Com index_offset=0, a posição do volante SALTA PARA ZERO
    // toda vez que o eixo cruza o índice: a 450° do centro o firmware passa a achar que está
    // centrado, o batente deixa de existir e o torque residual empurra para fora.
    //
    // E o efeito é ASSIMÉTRICO por construção: o índice fica num ponto físico fixo do eixo, então um
    // lado do curso cruza por ele e o outro não. É o relato exato da bancada — "para a esquerda o
    // batente segura; para a direita, a 450°, ele puxa em vez de bloquear", sempre o mesmo lado.
    //
    // O que continua valendo: set_circular_count(0) segue rodando no callback e reancora a fase
    // ELÉTRICA a cada volta, que é o benefício que queríamos do índice (corrige pulso perdido antes
    // de virar erro de comutação). O que sai é só a escrita na posição LINEAR, que é nossa
    // referência de volante e não pode ser reescrita por ninguém depois do set-center.
    axes[0].encoder_.config_.use_index_offset        = false;
    axes[0].encoder_.config_.pre_calibrated          = false;  // 1ª vez ainda mede o offset
    // STARTUP — sem apito (R/L já medidos). Procura o ÍNDICE, calibra o offset ancorado nele, arma.
    axes[0].config_.startup_motor_calibration            = false; // sem apito (R/L medidos, não estimados)
    axes[0].config_.startup_encoder_index_search        = false; // TESTE: sem busca de indice (ver acima)
    axes[0].config_.startup_encoder_offset_calibration   = true;  // 2º: offset, agora ancorado no índice
    axes[0].config_.startup_closed_loop_control          = true;  // 3º: arma
    // Sim racing: sem clamp de velocidade cortando torque (girar na mão sem OVERSPEED)
    axes[0].controller_.config_.enable_vel_limit         = false;
    // ...e o ERRO de overspeed, que a linha acima NÃO desliga. Descoberto em 14/08/2026: desligar o
    // clamp tira o corte de torque, mas o ODrive continua comparando a velocidade com o MESMO
    // `vel_limit` que acabamos de mandar ignorar, e desarma na hora se passar de vel_limit ×
    // vel_limit_tolerance. Com os defaults isso dava 5,0 × 1,2 = 6 voltas/s — dentro do que este
    // motor alcança nesta fonte, e o desarme vinha como um TRANCO, porque o torque cai a zero no
    // meio do movimento. Foi assim que o teste de regeneração derrubou a base com o volante
    // desacoplado: sem o aro a inércia é mínima e a velocidade sobe em picos perfeitamente normais.
    //
    // POR QUE ISTO NÃO É "DESLIGAR UMA PROTEÇÃO": a proteção contra o volante girar sozinho é NOSSA
    // (ver kOverspeedTurnsS em ffb_task.cpp) e é mais bem pensada — exige 5 voltas/s SUSTENTADAS por
    // 150 ms, então ignora o transiente inofensivo e pega o disparo de verdade. A do ODrive dispara
    // num pico instantâneo, sem persistência nenhuma. Subindo o teto dela para muito além do que a
    // mecânica alcança, ela fica como último recurso absoluto e a nossa age primeiro, que é a ordem
    // certa. O `if` do ODrive continua ativo — o outro ramo dele detecta estimativa de velocidade
    // inválida, e essa checagem queremos manter.
    //
    // ⚠️ Só mexe no disparo do erro: `enable_torque_mode_vel_limit` está desligado e o clamp também,
    // então `vel_limit` não entra em nenhuma conta de torque. Conferido campo a campo antes de mudar.
    axes[0].controller_.config_.vel_limit                = 25.0f;   // erro só acima de 30 voltas/s
    // BRAKE RESISTOR (chopper) — config funcional para placas classe ODESC: dissipa a regen que
    // estourava dc_bus_overvoltage_trip_level (a queda de FFB na chicane). Antes ficava DESLIGADO (contorno
    // do não-arme); agora o clear_errors (delegado a odrv.clear_errors, fix 046c421) RE-ARMA o brake no
    // auto-arme → satisfaz enable&&armed → o motor arma junto. Setado no boot (não depende de NVM stale).
    // ⚠️ Valores por-PLACA (56V aqui) → vira board profile na Fase 2.
    // O resistor de freio virou SETTING da aba Hardware (id 56) em 14/08/2026 — ele é peça que cada
    // um compra e monta, e ficar cravado obrigava a recompilar o firmware para trocar de resistor.
    // Este 2.0 continua sendo o ponto de partida do bring-up; motor_link_apply_motor_settings()
    // aplica por cima o que o usuário declarou. Padrão do setting também é 2.0, então uma base que
    // nunca tocou no campo se comporta exatamente como antes.
    odrv.config_.brake_resistance                = 2.0f;   // ponto de partida; o setting 56 manda
    odrv.config_.dc_bus_undervoltage_trip_level  = 8.0f;   // evita brown-out; garante no boot

    // BANDA MORTA DA REGENERAÇÃO — sem isto o chopper dispara com o volante PARADO.
    //
    // O ODrive decide acionar assim (low_level.cpp, update_brake_current):
    //     brake_current = -Ibus_sum - max_regen_current
    //     brake_duty    = brake_current * brake_resistance / vbus
    // Com max_regen_current = 0 (o default dele, que nunca sobrescrevemos) NÃO EXISTE banda morta:
    // basta a corrente do barramento oscilar para o lado negativo — ruído normal de medição — para
    // brake_current ficar positivo e o resistor ser acionado. Motor ARMADO tem esse ruído mesmo
    // parado, motor desarmado não tem: é por isso que o sintoma acompanha o "Ativar motor".
    //
    // MEDIDO na bancada em 14/08/2026, numa placa com a NVM recém-apagada: 674.586 acionamentos com
    // o volante imóvel, 49,9 J. Energia baixa (não é perigoso), mas é o resistor chaveando à toa o
    // tempo todo, e o medidor da tela vira ruído — some a evidência de quando ele trabalhou DE
    // VERDADE, que é a razão de o medidor existir.
    //
    // 0,5 A é pequeno o bastante para não ser regeneração de verdade (a capacitância do barramento
    // absorve isso sem a tensão subir) e grande o bastante para o ruído não atravessar. E a proteção
    // por TENSÃO continua intacta: se o vbus subir de fato, a rampa entra em dc_bus_overvoltage_ramp_start
    // e aciona o resistor pelo outro termo da mesma conta — que é o caminho legítimo.
    odrv.config_.max_regen_current               = 0.5f;

    // QUANTA REGENERAÇÃO O BARRAMENTO ACEITA ANTES DE DESARMAR.
    //
    // O ODrive desarma com ERROR_DC_BUS_OVER_REGEN_CURRENT quando a corrente do barramento fica
    // mais negativa que este limite (low_level.cpp: `if (Ibus_sum < dc_max_negative_current)`).
    // Nunca declarávamos o valor, então ele vinha da NVM — e numa placa zerada cai no padrão
    // **-0,1 A**, que é restritivo demais para um volante: girar o aro com o motor armado passa
    // disso com folga, porque o motor vira gerador.
    //
    // MEDIDO na bancada em 14/08/2026, com a placa recém-instalada: girando devagar, 14 desarmes em
    // 10 segundos (`odrv.error_ = 0x8`), o auto-arme religando a cada um. O usuário descreveu como
    // "pedrinhas dentro da base" — a força sumindo e voltando em pulsos —, e notou que acontecia
    // mais num sentido de giro que no outro, que é o esperado: um sentido regenera mais que o outro.
    //
    // -20 A é generoso DE PROPÓSITO, e quem protege de verdade continua no lugar: o resistor de
    // freio dissipa a energia (2 Ω a ~27 V dá ~13 A de capacidade) e a rampa de SOBRETENSÃO age
    // antes, em dc_bus_overvoltage_ramp_start. Este limite é a última barreira, não a primeira —
    // deixá-lo apertado faz a base desarmar em uso normal, que é pior do que o risco que ele cobre.
    //
    // ⚠️ Depende do resistor de freio estar montado e habilitado. Sem resistor, a energia não tem
    // para onde ir e a proteção que sobra é a de tensão.
    odrv.config_.dc_max_negative_current         = -20.0f;

    // FEED-FORWARDS DA MALHA DE CORRENTE — sem eles o volante gira "pulando degraus".
    //
    // Os dois são `false` no ODrive de fábrica, e nós nunca os declarávamos: a base herdava o que
    // estivesse na NVM, que ninguém escreveu de propósito. Numa placa apagada caem no padrão, e o
    // sintoma aparece.
    //
    //   R_wL_FF  compensa as quedas R·I e ω·L·I — o que a malha teria de descobrir por erro
    //   bEMF_FF  compensa a tensão que o PRÓPRIO MOTOR gera ao girar
    //
    // O segundo é o que se sente num volante. Girando o aro, o motor vira gerador e essa tensão se
    // opõe à malha; sem antecipá-la, o controlador só reage DEPOIS que o erro aparece — corrige aos
    // trancos, e o rotor prende e escapa entre posições. Descrito na bancada em 14/08/2026 como
    // "uma pedra solta dentro, pulando degraus", e SÓ com o motor armado (desarmado não há malha, e
    // não havia barulho — foi o que provou que era elétrico, não cogging). Ligar os dois em tempo de
    // execução, por SWD, fez o ruído parar na hora.
    //
    // ⚠️ Os dois dependem de R, L e Kt estarem CERTOS: eles injetam uma correção calculada a partir
    // desses valores. Com R/L fixados aqui no bring-up e o Kt vindo do campo da aba Hardware, isso
    // vale. Se um dia o Kt aceitar valores muito fora do real, este é o lugar que passa a amplificar
    // o erro em vez de compensá-lo — a compensação certa some, a errada empurra.
    // 🔴 DEIXADOS DESLIGADOS (o padrão do ODrive), depois de TESTADOS na bancada em 14/08/2026.
    //
    // Ligá-los foi a hipótese para o "pulando degraus" ao girar o volante com o motor armado. O
    // teste ao vivo (escrevendo os dois por SWD, com a base rodando) pareceu resolver na hora — mas
    // com eles ligados DE VERDADE no boot, e confirmados por leitura (FF = 1 1), o ruído continuou
    // exatamente igual. A melhora foi impressão, não efeito.
    //
    // Ficam desligados porque não resolvem e não são de graça: os dois calculam uma compensação a
    // partir de R, L e Kt, e um Kt mal informado passa a EMPURRAR o erro em vez de cancelá-lo. Ligar
    // sem ganho medido é acrescentar uma dependência a valores que o usuário digita.
    //
    // ✅ A CAUSA DO RUÍDO FOI ACHADA, e não era nada disto: a base DESARMAVA ao girar, por
    // dc_max_negative_current no padrão de -0,1 A (ver o bloco desse campo, mais acima). O que se
    // sentia como "degraus" e como "pedrinhas" era o MESMO defeito — a força sumindo e voltando a
    // cada desarme/rearme —, e não dois problemas diferentes como chegamos a tratar.
    //
    // Estes feed-forwards ficam desligados porque foram testados nesse meio-tempo e não mudaram
    // nada: ligados de verdade no boot (confirmado por leitura, FF = 1 1), o ruído continuou igual.
    // Também descartados por teste, pela mesma razão: alinhamento elétrico (não varia entre três
    // calibrações) e deadband da malha (zerado ao vivo, sem diferença).

    // Chopper MUDO aqui. Ligá-lo depende de MEDIR a fonte, e neste ponto do boot o ADC ainda não leu:
    // `vbus_voltage` vale o inicializador 12.0f de low_level.cpp ("arbitrary non-zero initial value to
    // avoid division by zero if ADC reading is late"). Confiar nele aqui dimensiona para uma fonte de
    // 12 V IMAGINÁRIA (chegamos a gerar rampa 14/16 com a fonte real em 24 V). Fail-safe: desligado até
    // motor_link_autoscale_bus_limits() rodar com leitura de verdade.
    odrv.config_.enable_brake_resistor          = false;
    odrv.config_.enable_dc_bus_overvoltage_ramp = false;

    // ⚠️ REAPLICAR A CONFIG DO MOTOR — sem isto, PLACA VIRGEM NUNCA ARMA.
    //
    // Motor::apply_config() faz `is_calibrated_ = config_.pre_calibrated`, e quem CONSULTA o estado
    // do motor é o is_calibrated_, não o pre_calibrated. O apply_config roda uma vez no boot, ANTES
    // desta função (main.cpp: config_read_all → config_apply_all → ... → nosso bring-up). Então a
    // cópia acontece com o valor que veio da NVM, e tudo o que setamos aqui em pre_calibrated chega
    // tarde: o flag já foi copiado.
    //
    // Numa placa NOSSA isso nunca apareceu — a config salva já trazia pre_calibrated=true, então a
    // cópia do boot pegava true e o motor armava. Numa placa VIRGEM (NVM apagada, primeiro uso, ou
    // depois de um erase) a NVM diz false, is_calibrated_ fica false para sempre, e o eixo RECUSA a
    // calibração de offset com ERROR_INVALID_STATE (axis.cpp: `if (!motor_.is_calibrated_) goto
    // invalid_state_label`). O auto-arme então bate contra um estado impossível até o teto de 300
    // tentativas, sem erro de motor nem de encoder — só um eixo dizendo "não". Medido em 14/08/2026
    // no teste de instalação do zero: 149 tentativas, axis_err=1, motor_err=0, enc_err=0.
    //
    // apply_config() também recalcula os ganhos do controlador de corrente, que dependem do R/L que
    // acabamos de fixar — então reaplicar aqui é o lugar certo, não só o conserto do flag.
    axes[0].motor_.apply_config();
}

// Deriva os limites de bus da tensão REAL medida e só então libera o chopper.
// Chamada do ffb_task com o motor JÁ ARMADO (ver ffb_task.cpp) — nunca no boot.
//
// POR QUE existe: em 2026-08-05 torramos um resistor de 50 W. O firmware ligava o chopper com valor
// fixo, mas a RAMPA vinha da NVM calibrada para a fonte antiga de 19,5 V (start 20,29 / end 22,79).
// Ao trocar por uma fonte de 24 V, o vbus EM REPOUSO já estava acima do ramp_end → duty saturado em
// 0,95 CONTÍNUO → 24²/2 × 0,95 ≈ 273 W. Config que não bate com o hardware não pode queimar hardware.
//
// Regra "trip = fonte + 4 V", com a rampa SEMPRE acima do repouso:
//   ramp_start = V+2 · ramp_end = V+4 · trip = V+6 (clampado ao teto da placa)
// Em repouso (sem carga) vbus == tensão da fonte. Vale para qualquer fonte de 12 a 48 V.
// VALIDADO na bancada: com a fonte medida em 27,1 V, rampa em 29,2 V e o chopper mudo parado; na
// pista o pico de vbus caiu de 33,5 V (sem resistor) para 27,5 V, com 3,3 W médios no resistor.
// Retorna 1 se dimensionou (fonte presente), 0 se ficou mudo.
extern "C" int motor_link_autoscale_bus_limits(void) {
    const float vsup = vbus_voltage;
    if (!(vsup >= 10.0f)) {              // USB sozinho dá ~2,6 V → sem fonte, segue mudo
        odrv.config_.enable_brake_resistor          = false;
        odrv.config_.enable_dc_bus_overvoltage_ramp = false;
        return 0;
    }
    float trip = vsup + 6.0f;
    if (trip > 55.0f) trip = 55.0f;      // teto da placa 56V (caps ~63V)
    odrv.config_.dc_bus_overvoltage_ramp_start  = vsup + 2.0f;
    odrv.config_.dc_bus_overvoltage_ramp_end    = vsup + 4.0f;
    odrv.config_.dc_bus_overvoltage_trip_level  = trip;
    odrv.config_.dc_bus_undervoltage_trip_level = 8.0f;   // fixo: anti brown-out
    odrv.config_.enable_dc_bus_overvoltage_ramp = true;
    // ⚠️ ARMAR JUNTO, sem janela: apply_pwm_timings() roda a 8 kHz e derruba o motor com
    // BRAKE_RESISTOR_DISARMED se vir (enable && !armed) — nem que seja por um ciclo. Foi o que
    // aconteceu ao ligar isto no meio da calibração: o motor desarmava e a cal de offset abortava
    // ("gira um pouco pra um lado e para" → UNKNOWN_PHASE_ESTIMATE).
    safety_critical_arm_brake_resistor();
    odrv.config_.enable_brake_resistor          = true;
    return 1;
}

// Afrouxa a calibração p/ vencer o cogging do hoverboard (raiz do CPR_MISMATCH intermitente):
// mais tolerância no check de CPR + mais corrente no scan (motion suave → counts corretos).
extern "C" void motor_link_relax_calibration(void) {
    axes[0].encoder_.config_.calib_range = 0.05f;         // 2% → 5% (tolera desvio do cogging)
    axes[0].motor_.config_.calibration_current = 5.0f;    // 3A → 5A (vence o cogging no scan)
}
// Arme deliberado: pede CLOSED_LOOP (roda cal → closed loop).
extern "C" void motor_link_request_closed_loop(void) {
    axes[0].requested_state_ = Axis::AXIS_STATE_CLOSED_LOOP_CONTROL;
}

// O encoder já tem referência (offset válido)? Encoder incremental sem Z perde a referência a cada
// boot, e sem ela o CLOSED_LOOP é recusado por falta de estimativa de fase. É este flag que decide
// entre "calibrar" e "armar".
extern "C" int motor_link_encoder_is_ready(void) { return axes[0].encoder_.is_ready_ ? 1 : 0; }

// Pede a VARREDURA DE OFFSET do encoder (estado 7): o motor gira ~meia volta e volta para IDLE.
// Dois nomes para a mesma ação, herdados de linhas de trabalho que se encontraram no merge de
// 2026-08-10 — mantidos porque cada chamador usa o seu:
//   _request_encoder_cal         → trava de bring-up (a cal só roda quando o usuário ativa o motor)
//   _request_encoder_calibration → auto-arme do ffb_task, que precisa RE-TENTAR a cal e não só o
//                                  arme (o ODrive roda a cal de boot uma única vez; se falhar,
//                                  ninguém mais tenta)
extern "C" void motor_link_request_encoder_cal(void) {
    axes[0].requested_state_ = Axis::AXIS_STATE_ENCODER_OFFSET_CALIBRATION;
}
extern "C" void motor_link_request_encoder_calibration(void) {
    motor_link_request_encoder_cal();
}
// Limpa erros p/ permitir nova tentativa de arme. Delega ao clear_errors() do ODrive
// (MotorControl/main.cpp) em vez de zerar os campos na mão, porque ele faz DUAS coisas
// que a versão manual não fazia e que travavam a recuperação:
//   1) zera o erro GLOBAL (odrv.error_) — senão o BUS_OVER_V fica grudado pra sempre;
//   2) RE-ARMA o brake resistor se enable_brake_resistor estiver ligado.
// Sem (2) o motor NUNCA voltava com o chopper ligado: a sobretensão da regen chama
// ODrive::disarm_with_error(), que desarma o brake junto (low_level.cpp:97); a partir daí
// (enable && !armed) faz apply_pwm_timings() derrubar TODA tentativa de arme com
// BRAKE_RESISTOR_DISARMED, até o auto-arme esgotar as 15 tentativas e desistir de vez.
// Diagnosticado na bancada 2026-08-04 (perda TOTAL de FFB, 82 amostras presas em IDLE).
extern "C" void motor_link_clear_errors(void) {
    odrv.clear_errors();
}

// ── TRANCAR A CALIBRAÇÃO E LIGAR A RE-SINCRONIZAÇÃO PELO ÍNDICE (2026-08-08) ─────────────────
// Chamar UMA vez, assim que a cal de offset concluir (encoder.is_ready_ == true). Faz duas coisas:
//
//  1) pre_calibrated = true → o offset recém-medido passa a valer como DEFINITIVO. A partir daqui
//     o firmware não tem mais motivo para recalibrar, e o ffb_task para de pedir calibração nova.
//     POR QUE IMPORTA: o auto-arme disparava uma calibração NOVA sempre que o motor caía para IDLE
//     com is_ready=false — e ele cai justamente quando o batente satura, ou seja, com o volante
//     ENCOSTADO NO FIM DE CURSO. A cal de offset precisa girar o rotor livremente para varrer; presa
//     no batente ela varre torto e grava um offset ruim. É exatamente o relato da bancada: "corrijo
//     o lado do batente, giro para o outro, e o lado que estava perfeito fica estragado".
//
//  2) re-inscreve o índice (set_idx_subscribe) → o Z volta a ser escutado DEPOIS da calibração.
//     O core desinscreve no primeiro pulso (enc_index_cb faz unsubscribe), então normalmente o
//     índice serve só para achar o zero e depois cala. Com pre_calibrated=true, cada passagem pelo
//     Z reancora a contagem (set_circular_count(0)) e MANTÉM is_ready=true — vira correção contínua
//     de erro acumulado, uma vez por volta. Sem isso, contagens perdidas num movimento rápido (o
//     volante "correndo" no batente) deslocam a referência para sempre, e um lado desregula o outro.
//
// ⚠️ A ORDEM É OBRIGATÓRIA: pre_calibrated ANTES de re-inscrever. Com pre_calibrated=false, o
// callback do índice faz is_ready_=false — reancorar nesse estado DESARMARIA o motor a cada volta.
extern "C" void motor_link_lock_calibration(void) {
    if (!axes[0].encoder_.is_ready_) return;      // só tranca o que está de fato calibrado
    axes[0].encoder_.config_.pre_calibrated = true;
    axes[0].encoder_.set_idx_subscribe();         // volta a escutar o Z (agora reancorando)
}

// ---------------------------------------------------------------------------
// Aplica a configuracao de encoder escolhida no app.
//
// POR QUE EXISTE: ate 2026-08-10 o CPR era CRAVADO em 4000 (E6B2 1000 PPR x4). Quem usasse
// qualquer outro encoder tinha de editar o codigo — o app deixava escolher, salvava na flash, e
// nada disso chegava na placa. Com um E6B2 de 2500 PPR a base entenderia uma volta como 2,5.
//
// A GARANTIA: com os settings no padrao (cpr=4000, igual ao que o bring-up crava) o resultado e
// IDENTICO ao de hoje. E cpr==0 ("nao informado") tambem nao aplica nada.
//
// Chamar DEPOIS do bring-up: e ele quem fixa os valores de fabrica, e este aplica por cima.
// ---------------------------------------------------------------------------
extern "C" int32_t a0_get_setting(uint8_t id);
extern "C" float   a0_get_setting_f(uint8_t id);   // campos T_FLOAT (ex.: Kt)

enum { SET_ENCODER_CPR = 10, SET_ENCODER_TYPE = 18, SET_ENCODER_IFACE = 46, SET_ENCODER_DIR = 9 };

extern "C" int motor_link_apply_encoder_settings(void) {
    // Sentido REPORTADO (ver s_pos_sign). So -1 inverte; qualquer outro valor mantem o de hoje —
    // inclusive o 0, que na configuracao do ODrive significa "nao calibrado" e nao pode virar sinal.
    s_pos_sign = (a0_get_setting(SET_ENCODER_DIR) == -1) ? -1.0f : 1.0f;

    const EncoderConfig ec = encoder_config_from_settings(
        (uint8_t)a0_get_setting(SET_ENCODER_TYPE),
        (uint8_t)a0_get_setting(SET_ENCODER_IFACE),
        (uint32_t)a0_get_setting(SET_ENCODER_CPR));

    if (ec.cpr == 0) return 0;   // nao informado -> mantem o que o bring-up deixou

    // Combinacao que a base nao sabe acionar (MT6701 em SSI, MT6835 em SPI, qualquer absoluto
    // sem driver): NAO aplica NADA e mantem o bring-up, que funciona.
    //
    // Aplicar so o CPR — que era o comportamento ate 2026-08-11 — e pior do que ignorar: grava a
    // resolucao do sensor magnetico por cima de uma leitura A/B/Z, e a base segue girando
    // reportando um angulo errado por um fator de quatro, sem nada acusar.
    if (!ec.drivable) return -1;

    const int32_t modo_odrive = (ec.mode == ENC_MODE_SPI_ABS_AMS)
                              ? (int32_t)Encoder::MODE_SPI_ABS_AMS
                              : (int32_t)Encoder::MODE_INCREMENTAL;

    const bool mudou = (axes[0].encoder_.config_.cpr != (int32_t)ec.cpr)
                    || ((int32_t)axes[0].encoder_.config_.mode != modo_odrive);

    axes[0].encoder_.config_.cpr  = (int32_t)ec.cpr;
    axes[0].encoder_.config_.mode = (Encoder::Mode)modo_odrive;

    // Configuracao de encoder diferente da que a calibracao usou torna a calibracao INVALIDA. O
    // perigo nao e perder a calibracao: e ARMAR com calibracao velha e CPR novo, que e corrente no
    // angulo errado — motor quente sem torque, e possivelmente fuga.
    if (mudou) {
        axes[0].encoder_.config_.pre_calibrated = false;
        axes[0].motor_.config_.pre_calibrated   = false;
    }
    return mudou ? 1 : 0;
}

// ---------------------------------------------------------------------------
// Aplica a configuracao de MOTOR escolhida no app (pares de polos, corrente de calibracao).
//
// POR QUE EXISTE: os dois estavam CRAVADOS — 15 pares do hoverboard e 5 A. Junto com o CPR, sao o
// que faz o motor de OUTRA pessoa funcionar. Numero de pares errado manda corrente para a bobina
// errada: o motor esquenta e nao entrega torque, que e a assinatura que ja perseguimos em 06/08.
//
// A GARANTIA: os defaults dos settings sao 15 e 5, os mesmos valores que o bring-up + o
// relax_calibration deixavam. Zero = "nao informado" e nao aplica.
//
// Chamar DEPOIS do relax_calibration, que e quem fixa os 5 A.
// ---------------------------------------------------------------------------
enum { SET_POLE_PAIRS = 11, SET_CALIB_CURRENT = 14, SET_CURRENT_LIM = 48, SET_CURRENT_BW = 55,
       SET_TORQUE_CONSTANT = 34,     // T_FLOAT — ler com a0_get_setting_f, nao a0_get_setting
       SET_BRAKE_RESISTANCE = 56 };  // T_FLOAT — idem

extern "C" int motor_link_apply_motor_settings(void) {
    // Teto fisico do limite de corrente: o sensor da placa satura em requested_current_range e o
    // ODrive exige a margem de folga. Acima disso nao ha mais torque, so medicao cega.
    const float teto = axes[0].motor_.config_.requested_current_range
                     - axes[0].motor_.config_.current_lim_margin;

    const MotorConfig mc = motor_config_from_settings(
        (uint8_t)a0_get_setting(SET_POLE_PAIRS),
        (uint8_t)a0_get_setting(SET_CALIB_CURRENT),
        (float)a0_get_setting(SET_CURRENT_LIM),
        teto,
        axes[0].motor_.config_.current_lim,   // o limite de hoje, se o usuario nao informou outro
        a0_get_setting_f(SET_TORQUE_CONSTANT));

    if (mc.current_lim_a != 0.0f) {
        axes[0].motor_.config_.current_lim = mc.current_lim_a;
    }

    // Kt MEDIDO pelo usuario substitui o 0,55 de catalogo do bring-up. Zero (ou valor fora da faixa
    // plausivel) nao aplica nada: fica o padrao, e o comportamento e identico ao de antes deste
    // campo existir — que e a garantia de que ligar o orfao nao muda a bancada de ninguem.
    if (mc.torque_constant != 0.0f) {
        axes[0].motor_.config_.torque_constant = mc.torque_constant;
    }

    int mudou = 0;

    if (mc.pole_pairs != 0) {
        if (axes[0].motor_.config_.pole_pairs != (int32_t)mc.pole_pairs) mudou = 1;
        axes[0].motor_.config_.pole_pairs = (int32_t)mc.pole_pairs;
    }
    // BANDA DA MALHA DE CORRENTE (setting 55). Estava cravada em 200 Hz no bring-up.
    //
    // Nao existem ganhos P e I para ajustar: o ODrive os DERIVA — p_gain = banda x indutancia,
    // i_gain = (resistencia / indutancia) x p_gain. Por isso os antigos campos P e I nunca tiveram
    // onde chegar, e por isso este e um numero com significado fisico em vez de dois sem.
    //
    // update_current_controller_gains() TEM de ser chamado depois de mexer na banda: sem ele o
    // valor fica na config e os ganhos em uso continuam os antigos — o setting pareceria aplicado
    // e nao estaria, que e o defeito que este trabalho inteiro existe para eliminar.
    const int32_t bw = a0_get_setting(SET_CURRENT_BW);
    if (bw >= 50 && bw <= 2000) {
        axes[0].motor_.config_.current_control_bandwidth = (float)bw;
        axes[0].motor_.update_current_controller_gains();
    }

    if (mc.calib_current_a != 0.0f) {
        axes[0].motor_.config_.calibration_current = mc.calib_current_a;
    }

    // RESISTOR DE FREIO (setting 56). Estava CRAVADO em 2 ohms — o valor do nosso conjunto —, e o
    // proprio comentario no bring-up ja registrava que ha montagens com 12. O firmware nao MEDE a
    // resistencia: ele acredita neste numero para calcular a corrente e a potencia que passam pelo
    // resistor, e e essa conta que decide quando cortar por dissipacao. Declarar 2 tendo montado 12
    // erra por seis vezes, em silencio.
    //
    // Fora da faixa plausivel (ou zero, de um blob antigo que nao tinha o campo) NAO aplica nada:
    // fica o valor do bring-up, e o comportamento e identico ao de antes deste campo existir.
    const float r_brake = a0_get_setting_f(SET_BRAKE_RESISTANCE);
    if (r_brake >= 0.5f && r_brake <= 50.0f) {
        odrv.config_.brake_resistance = r_brake;
    }

    // Numero de pares diferente do que a calibracao usou torna a calibracao INVALIDA: o angulo
    // eletrico e derivado dele, entao o offset guardado deixa de significar a mesma coisa.
    if (mudou) {
        axes[0].encoder_.config_.pre_calibrated = false;
        axes[0].motor_.config_.pre_calibrated   = false;
    }
    return mudou;
}

// ---------------------------------------------------------------------------
// PROTECAO TERMICA DOS FETs — liga o setting 37, que era orfao.
//
// A protecao em si NAO e nova: o ODrive tem um limitador que le o termistor da placa e, entre um
// limite inferior e um superior, corta a corrente proporcionalmente — no superior ela chega a zero,
// e 5 °C acima o eixo desarma. Ele ja roda hoje, com 100/120 °C de fabrica. O que faltava era o
// nosso campo chegar la: existia no app, era salvo, voltava certo ao reiniciar, e ninguem lia.
//
// Isto so passou a valer a pena agora que a temperatura dos FETs virou leitura REAL (43 °C em
// repouso nesta bancada). Antes a telemetria mandava -128 e nao havia numero em que confiar.
//
// ⚠️ MUDANCA DE COMPORTAMENTO NA BANCADA: com o padrao do app (85), a base passa a reduzir corrente
// a partir de 85 °C em vez dos 100 °C do ODrive. E de proposito — 85 protege mais cedo, e a decisao
// de subir o current_lim (calor cresce com o QUADRADO da corrente) fica coberta.
//
// Chamar DEPOIS do bring-up, junto dos outros applies.
// ---------------------------------------------------------------------------
enum { SET_FET_TEMP_LIMIT = 37 };

extern "C" int motor_link_apply_thermal_settings(void) {
    const ThermalConfig tc = thermal_config_from_settings(
        (uint8_t)a0_get_setting(SET_FET_TEMP_LIMIT));

    if (!tc.apply) return 0;   // nao informado -> mantem o que o ODrive tem

    fet_thermistors[0].config_.temp_limit_lower = tc.lower;
    fet_thermistors[0].config_.temp_limit_upper = tc.upper;
    fet_thermistors[0].config_.enabled          = true;
    return 1;
}

// ---------------------------------------------------------------------------
// ENTRAR EM DFU (atualizar firmware sem ST-Link e sem jumper)
//
// Usa o mecanismo NATIVO do ODrive em vez de um salto proprio. Ele grava o cookie
// 0xDEADBEEF em .noinit, reseta, e o early_start_checks() — chamado pelo startup em
// assembly, ANTES de main() e dos construtores estaticos — salta para a ROM da ST.
//
// Por que o nativo e melhor que o salto proprio que chegamos a escrever:
//  · salta MAIS CEDO, com o chip ainda no estado de reset. Nao precisa de
//    HAL_RCC_DeInit (o SystemInit ja deixou o clock no default) nem de
//    __enable_irq (o reset zera o PRIMASK) — as duas licoes que nos custaram
//    sessoes de bancada no firmware anterior sao satisfeitas de graca aqui.
//  · HAL_RCC_DeInit usa SysTick para timeout, e nesse ponto do boot o SysTick
//    ainda nao foi configurado: chama-lo cedo demais pode travar esperando um
//    contador que nao avanca.
//  · tem anti-loop mais rico (cookie de transito 0xCAFEFEED + cookie de sanidade
//    42 que reseta de novo se o boot nao veio de estado limpo).
//
// ⚠️ SEGURANCA DE HARDWARE: o bootloader da ST liga pull-ups internos em PB10
// (AUX_H) e PB11 (AUX_L). Em placas ODrive ANTERIORES a v3.5, sem pull-down
// externo de 3k3, isso liga os FETs do brake resistor e os destroi. Por isso o
// proprio ODrive so libera DFU em v3.5+ — e a nossa build declara v3.6
// (HW_VERSION_MAJOR/MINOR no Makefile). O gate esta em ODrive::enter_dfu_mode().
extern "C" void motor_link_enter_dfu(void) {
    odrv.enter_dfu_mode();   // nao retorna (NVIC_SystemReset)
}

// ============================================================================================
// TESTE DO ENCODER — a varredura da calibração, porém longa o bastante para MEDIR
// ============================================================================================
// POR QUE ESTE TESTE EXISTE: a calibração normal varre 16π rad elétricos, que com 15 pares de polos
// dá 0,27 VOLTA mecânica. O erro de ímã descentrado tem período de UMA volta — ajustar a senoide a
// um quarto do ciclo é mal condicionado, e o número que sai parece medição sem ser (medido em
// 15/08/2026: reportou "0,17° de amplitude" com resíduo 5× maior, ou seja, nada).
//
// A CORREÇÃO: alongar a varredura para pouco mais de uma volta, SÓ durante o teste. Tudo o mais é
// idêntico à calibração — mesmo estado do ODrive, mesma malha aberta, mesma coleta.
//
// ⚠️ POR QUE REUSAR O ESTADO DO ODRIVE, E NÃO COMANDAR O MOTOR POR FORA: tentamos a segunda opção em
// 15/08/2026 e ela TRAVOU A BASE. O `wait_for_control_iteration()` espera um evento do laço de
// controle que só existe no contexto do eixo; chamado do laço de FFB, nunca retorna. Pedir o estado
// e deixar o ODrive executá-lo no lugar certo é mais simples e não tem esse risco.
//
// ⚠️ DEMORA, E ISSO TEM CONSEQUÊNCIA: a varredura é ida e volta a 2π rad/s elétricos, então uma
// volta mecânica leva ~15 s por sentido, ~30 s no total. A thread do eixo tem prioridade MAIOR que
// a do nosso laço de FFB, que é quem fala USB — e uma calibração de 35 s já derrubou o dispositivo
// no Windows antes. O teste avisa e o app deve esperar; se a conexão cair, ela volta sozinha.
// O alinhamento ANTES do teste. A varredura recalcula o offset, e uma varredura longa contra o
// cogging do hoverboard pode terminar com um valor PIOR que o de partida — foi o que suspeitamos ter
// disparado o motor a 12 voltas/s em 15/08/2026. Guardar e devolver torna o teste NAO-DESTRUTIVO:
// ele mede e deixa a base exatamente como encontrou.
static float   s_ecc_offset_float = 0.0f;
static int32_t s_ecc_offset       = 0;
static int32_t s_ecc_direction    = 0;
static bool    s_ecc_ready        = false;
static bool    s_ecc_salvo        = false;

extern "C" int motor_link_start_encoder_test(void) {
    if (axes[0].motor_.is_armed_) return 0;              // com o motor armado, nao: seriam dois donos
    if (!axes[0].motor_.is_calibrated_)  return 0;       // a varredura de offset exige R/L medidos
    // Sem um alinhamento valido para devolver, o teste deixaria a base pior do que achou.
    if (!axes[0].encoder_.is_ready_) return 0;

    s_ecc_offset_float = axes[0].encoder_.config_.phase_offset_float;
    s_ecc_offset       = axes[0].encoder_.config_.phase_offset;
    s_ecc_direction    = axes[0].encoder_.config_.direction;
    s_ecc_ready        = axes[0].encoder_.is_ready_;
    s_ecc_salvo        = true;

    // Pouco mais de uma volta: a margem garante que a senoide feche o ciclo mesmo com folga
    // mecanica, e o custo de 10% a mais de tempo e irrelevante perto de nao poder concluir.
    const float uma_volta_elec = 2.0f * (float)M_PI * (float)axes[0].motor_.config_.pole_pairs;
    axes[0].encoder_.config_.calib_scan_distance = uma_volta_elec * 1.1f;

    axes[0].requested_state_ = Axis::AXIS_STATE_ENCODER_OFFSET_CALIBRATION;
    return 1;
}

/// Devolve a varredura ao tamanho de produção. Chamar quando o teste terminar — senão TODA
/// calibração seguinte passa a levar 30 s, e o usuário paga o preço do teste sem tê-lo pedido.
extern "C" void motor_link_end_encoder_test(void) {
    axes[0].encoder_.config_.calib_scan_distance = 16.0f * (float)M_PI;   // padrao do ODrive

    // DEVOLVE O ALINHAMENTO. Sem isto o teste vira um recalibrador disfarcado de medidor: mede
    // certo e deixa o motor com um offset que ninguem pediu. Um diagnostico nao pode piorar o que
    // veio examinar.
    if (s_ecc_salvo) {
        axes[0].encoder_.config_.phase_offset_float = s_ecc_offset_float;
        axes[0].encoder_.config_.phase_offset       = s_ecc_offset;
        axes[0].encoder_.config_.direction          = s_ecc_direction;
        axes[0].encoder_.is_ready_                  = s_ecc_ready;
        s_ecc_salvo = false;
    }
}
