// firmware-base — canal A0 (protocolo do DriveLab Studio). Replica a ABORDAGEM do firmware-base
// (lib/base_usb/a0_channel.cpp) adaptada ao TinyUSB CRU + ao nosso ffb_model:
//   - a0_handle_out(): despacha OUT reports por buf[0] (0x14 SETWRITE / 0x15 SETREAD / 0x22 CMD /
//     0x10 DIRECT). SÓ guarda valores/seta flags — trabalho pesado fica no laço, não no callback USB.
//   - a0_service(): no laço, envia com PRIORIDADE a resposta deferida 0x16 (SETVALUE) e, na sobra,
//     a telemetria periódica 0x21 (DeviceState). Cada envio é gated por tud_hid_ready() (um por janela).
//   - DeviceState (0x21): layout casado byte-a-byte com DriveLab.Core/Protocol/BaseState.cs
//     (Position ±10000, AngleDeciDeg em décimos de grau — é o que gira o volante na tela do app).
//   - Settings: store em RAM (48 campos: 45 do schema + trava, tecnologia do encoder e build_id); os principais aplicam
//     no ffb_model (força/direção). Center (ResetCenter) captura a posição atual como zero.
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include <stdint.h>
#include <string.h>
#include "tusb.h"
#include "motor_link.h"
#include "ffb_model.h"
#include "fw_version.h"       // fonte ÚNICA da versão — o carimbo do .bin lê o mesmo header
#include "settings_store.h"   // (de)serialização pura do blob de settings (magic+versão+CRC)
#include "settings_flash.h"   // blob ANTIGO — só para migrar o que já estava salvo
#include "nvm_kv.h"           // persistência por chave/valor: grava só o que mudou
#include "brake_meter.h"      // contadores do brake chopper (energia, acionamentos, pico)
#include "blackbox.h"         // rastro fino: qual chamada prendeu o laço
#include "encoder_eccentricity.h"  // resultado do teste do encoder na telemetria
#include "peak_tracker.h"     // picos de corrente do monitor
#include "wheel_center.h"     // zero unico do volante (ResetCenter escreve nele)
#include "bringup_lock.h"     // trava "Ativar motor" volta a zero quando o firmware muda
#include "ffb_curve_migrate.h" // curva de forca: 5 pontos -> 11 sem trocar o significado do salvo

// Ids da curva de forca. NAO sao contiguos: os cinco primeiros ficaram onde a curva de 5 pontos
// morava (28-32) e os seis que entraram depois foram para 49-54.
enum { SET_FFB_CURVE_0 = 28, SET_FFB_CURVE_5 = 49 };

// Medidor do chopper, alimentado no laço de 8 kHz (low_level.cpp). Só leitura aqui.
extern "C" BrakeMeter g_brake_meter;

// Guarda de coerência do ângulo elétrico (definida em ffb_task.cpp). 1 = disparou e desarmou.
extern volatile int32_t g_guard_trip;
extern volatile int32_t g_overspeed_trip;   // 1 = motor desarmado por girar sozinho
extern volatile int32_t g_overtravel_trip;  // 1 = motor desarmado por curso excedido (guarda por posicao)
// Medidores alimentados no ffb_task de 1 kHz (só leitura).
extern "C" PeakTracker g_current_peak_ma;
extern "C" uint8_t     g_clip_peak;
extern "C" uint8_t     g_clip_peak_game;   // as duas parcelas do pico da sessao (ver ffb_task)
extern "C" uint8_t     g_clip_peak_base;

// Trava de bring-up: definida no ffb_task, lida pelo laco de 1 kHz (ver setting 45).
extern "C" volatile int32_t g_motor_enable;

// Report IDs do canal A0 (ver a0_hid_descriptor.h; definidos localmente p/ não re-incluir o array).
#define A0_RID_STATE    0x21   // IN  DeviceState (telemetria/ângulo)
#define A0_RID_SETVALUE 0x16   // IN  resposta de leitura de setting
#define A0_RID_CMD      0x22   // OUT comando [cmdId, arg]
#define A0_RID_DIRECT   0x10   // OUT forças diretas do app
#define A0_RID_SETWRITE 0x14   // OUT grava setting [fieldId, idx, type, valor]
#define A0_RID_SETREAD  0x15   // OUT pede leitura [fieldId, idx]
#define A0_RID_DEFREAD  0x17   // OUT pede o PADRAO do campo [fieldId] -> responde 0x16
#define A0_RID_NVMREAD  0x18   // OUT pede o valor GRAVADO na flash [fieldId] -> responde 0x16
#define A0_PAYLOAD      63     // ReportConstants.ReportSize
// ⚠️ O CANAL DO APP TEM INTERFACE PRÓPRIA (instância 1). Enquanto era uma só, telemetria, leituras
// e o relatório do jogo disputavam o mesmo endpoint e precisavam de uma regra de prioridade — que
// foi a origem de uma sucessão de bugs (o painel congelando, o "Salvar" acusando campos gravados).
// Aqui a disputa deixou de existir: falamos no nosso canal, o jogo no dele.
#define A0_HID_ITF      0   // interface unica — o canal do app divide o endpoint com o jogo

// SettingType (DriveLab.Core.Settings.SettingType)
enum { T_U8 = 0, T_I8 = 1, T_U16 = 2, T_I16 = 3, T_FLOAT = 4, T_U32 = 5 };

// BaseCommand (DriveLab.Core.Settings.BaseCommand)
enum { CMD_REBOOT = 1, CMD_SAVE = 2, CMD_RESET_CENTER = 3, CMD_DFU = 4, CMD_CALIBRATE = 5,
       CMD_SET_FORCE_ENABLED = 6, CMD_CAL_COGGING = 7, CMD_BRAKE_BENCH = 8, CMD_BRAKE_AUTO = 9,
       CMD_TEST_ENCODER = 10,
       // Rearmar por ação EXPLÍCITA de quem usa: destrava a guarda de curso, limpa os erros e
       // pede malha fechada. Existe porque a guarda travada só zerava no boot — quem está
       // jogando não tem como ir até a fonte. Ver o tratamento no ffb_task.
       CMD_REARM = 11 };

// 48 = 45 settings + motor_enable (45) + encoder_interface (46) + build_id (47). Adicionar campo
// aqui NAO apaga mais os ajustes salvos: unpackSettings migra blob de firmware antigo (menos
// campos) completando com os defaults.
// O 47 e INTERNO: nao existe no schema do app e nao aparece na UI. Guarda a identidade do firmware
// que gravou o blob, para a trava de bring-up saber se o binario mudou (ver bringup_lock.h).
#define A0_NUM_SETTINGS 58

// Tipo de cada FieldId (BaseSettingsSchema). Índice = BaseSettingId.
static const uint8_t s_type[A0_NUM_SETTINGS] = {
    /*0 motion_range*/T_U16, /*1 soft_stop_range*/T_U8, /*2 soft_stop_strength*/T_U8,
    /*3 total_strength*/T_U8, /*4 spring_strength*/T_U8, /*5 damper_strength*/T_U8,
    /*6 static_damping*/T_U8, /*7 max_torque_limit*/T_U8, /*8 force_direction*/T_I8,
    /*9 encoder_direction*/T_I8, /*10 encoder_cpr*/T_U32, /*11 pole_pairs*/T_U8,
    /*12 current_p*/T_FLOAT, /*13 current_i*/T_FLOAT, /*14 calibration_current*/T_U8,
    /*15 position_smoothing*/T_U8, /*16 power_limit*/T_U8, /*17 braking_limit*/T_U8,
    /*18 encoder_type*/T_U8, /*19 reconstruction_steps*/T_U8, /*20 reconstruction_lpf*/T_U8,
    /*21 output_filter_hz*/T_U16, /*22 osc_guard*/T_U8, /*23 endstop_damping*/T_U8,
    /*24 linearity*/T_U8, /*25 cogging_enable*/T_U8, /*26 slew_rate*/T_U8, /*27 bus_nominal_v*/T_U8,
    /*28..32 ffb_curve*/T_U8, T_U8, T_U8, T_U8, T_U8, /*33 board_variant*/T_U8,
    /*34 torque_constant*/T_FLOAT, /*35 thermal_continuous*/T_U8, /*36 thermal_peak_s*/T_U8,
    /*37 fet_temp_limit*/T_U8, /*38 motor_temp_limit*/T_U8, /*39..42 gains*/T_U8, T_U8, T_U8, T_U8,
    /*43 soft_power*/T_U8, /*44 power_button*/T_U8, /*45 motor_enable*/T_U8,
    /*46 encoder_interface*/T_U8,   // ARMAZENAMENTO apenas — nada aplica este valor ainda
    /*47 build_id*/T_U32,           // INTERNO (fora do schema do app) — ver bringup_lock.h
    /*48 current_lim*/T_U8,         // limite de corrente do MOTOR (A) — descreve o motor de cada um
    /*49..54 ffb_curve_5..10*/T_U8, T_U8, T_U8, T_U8, T_U8, T_U8,
    /*55 current_bandwidth_hz*/T_U16,   // substitui os antigos 12/13 (P e I) — ver motor_link
    /*56 brake_resistance_ohm*/T_FLOAT, // resistor de freio [ohm] — peca que cada um monta (era cravado em 2.0)
    /*57 overtravel_action*/T_U8,      // curso excedido: 0=travar (padrao) · 1=re-armar sozinho
    // A curva de forca passou de 5 para 11 pontos (de 25 em 25% para 10 em 10%). Os cinco primeiros
    // ficaram nos ids 28-32; estes seis completam. O meio da escala — onde se dirige a maior parte
    // de uma volta — tinha UM ponto de controle e agora tem cinco.
};

// Valor de cada campo: inteiro em s_ival (u8/i8/u16/i16) OU float em s_fval (T_FLOAT).
static int32_t s_ival[A0_NUM_SETTINGS];
static float   s_fval[A0_NUM_SETTINGS];

// ============================================================================================
// PERSISTÊNCIA: UMA CHAVE POR AJUSTE
// ============================================================================================
// A chave de cada ajuste é o próprio id dele (0..57), que já é estável e conhecido dos dois
// lados. Ids acima de 0xFF00 ficam reservados para uso interno deste módulo — não há ajuste
// lá em cima e nunca haverá, porque o protocolo carrega o id num byte.
static constexpr uint16_t kKvMarca  = 0xFF01u;  // "esta página já foi escrita por este firmware"
static constexpr uint32_t kKvVersao = 1u;

// O valor de um ajuste cabe sempre em 32 bits: inteiro como está, float pelos seus bits. Guardar
// os BITS do float (e não convertê-lo) é o que faz 0,397 voltar exatamente 0,397.
static uint32_t a0_campo_u32(uint8_t id) {
    if (s_type[id] == T_FLOAT) { uint32_t b = 0; memcpy(&b, &s_fval[id], 4); return b; }
    return (uint32_t)s_ival[id];
}
static void a0_u32_campo(uint8_t id, uint32_t v) {
    if (s_type[id] == T_FLOAT) memcpy(&s_fval[id], &v, 4);
    else                       s_ival[id] = (int32_t)v;
}

// 1 = a página encheu e o "Salvar" ficou pela metade. O laço de 1 kHz compacta quando o motor
// estiver parado e o pedido segue pendente — ver ffb_task. Não é erro: é manutenção.
static bool s_precisa_compactar = false;
extern "C" int  a0_precisa_compactar(void) { return s_precisa_compactar ? 1 : 0; }
extern "C" void a0_compactou(void)         { s_precisa_compactar = false; }

// Defaults em escopo de arquivo para poderem ser CONSULTADOS (0x17) sem alterar valor nenhum:
// o app pergunta "qual e o padrao deste campo?" e mostra na tela; nada muda na placa ate salvar.
static const int32_t s_idef[A0_NUM_SETTINGS] = {
        900, 8, 70, 100, 5, 0, 5, 80, 1, 1, 4000, 15, 0, 0, 5, 0, 100, 100, 1, 0, 0, 0, 0, 35,
        //                                                              ^ 18 encoder_type = 1 (E6B2),
        // o id CANONICO do catalogo. Era 0, que e o apelido legado "generico" e o app traduz para
        // E6B2 — mesmo comportamento, numero diferente. Com a interface em ABZ (default) o modelo
        // nem entra na decisao: so o ramo SPI+AS5047P o consulta. Nasce no valor canonico para o
        // que a placa GUARDA ser o mesmo que o app MOSTRA.
        100, 0, 0, 56, 0, 10, 20, 30, 40, 1, 0, 100, 0, 85, 100, 100, 100, 100, 100, 0, 0,
        //          ^^^^^^^^^^^^^^^^^ 28-32: os cinco PRIMEIROS pontos da curva (0/10/20/30/40%)
        //^ 24 linearity: LINEAR. Ver a nota em a0_load_defaults.
        0,  // 45 motor_enable: NASCE DESLIGADO — ver A0_NUM_SETTINGS
        0,  // 46 encoder_interface: 0=ABZ (só armazenamento; nada aplica este valor ainda)
        0,  // 47 build_id: 0 = nunca gravado por firmware que conheça este campo
        25, // 48 current_lim [A]: o valor que estava CRAVADO no firmware → padrão = comportamento de hoje
        50, 60, 70, 80, 90, 100,  // 49-54: o resto da curva (linear = sai o mesmo que entra)
        200,                      // 55 banda da malha de corrente [Hz]: o valor cravado no bring-up
        0,                        // 56 brake_resistance_ohm: T_FLOAT → o padrao vive em s_fdef
        0                         // 57 overtravel_action: TRAVAR. Padrao seguro de proposito —
                                  // trava cuja opcao permissiva esta a um clique acaba ligada e
                                  // esquecida, e esta guarda existe para o motor que disparou.
};
static float s_fdef[A0_NUM_SETTINGS];   // padroes float (preenchidos em a0_load_defaults)

// Array curto nao da erro de compilacao: o C completa com ZERO em silencio, e o resultado seria um
// padrao errado num campo qualquer — do tipo que so aparece na bancada. Acrescentar setting sem
// acrescentar o tipo e o padrao passa a quebrar o BUILD, que e onde deve quebrar.
static_assert(sizeof(s_type) / sizeof(s_type[0]) == A0_NUM_SETTINGS,
              "s_type: falta (ou sobra) o tipo de algum setting");
static_assert(sizeof(s_idef) / sizeof(s_idef[0]) == A0_NUM_SETTINGS,
              "s_idef: falta (ou sobra) o padrao de algum setting");

static void a0_load_defaults(void) {
    // Defaults = a configuração VALIDADA na pista em 2026-08-09 (lida da placa por SWD), não valores
    // teóricos. Quem grava o firmware pela primeira vez já começa num ponto que roda bem.
    // Mudou em relação ao conjunto anterior: soft_stop_range 5->8, soft_stop_strength 80->70,
    // spring 0->5, damper 10->0, endstop_damping 0->35 (era ZERO: batente sem freio = catapulta).
    //
    // LINEARITY: voltou para 100 (LINEAR) em 2026-08-11, MEDIDO. Tinha virado 159 so porque era o
    // valor que estava na placa no dia em que a pista rodou bem — nunca foi escolhido. Com 1,59 a
    // forca do jogo passa por |x|^1,59 antes de virar torque, e o estrago fica no meio da escala,
    // que e onde se dirige: o jogo pede 50% e chegam 33%; pede 30% e chegam 15%.
    //
    // Isso explicava os DOIS sintomas ao mesmo tempo, que pareciam contraditorios: base "mais fraca
    // que um Moza R9 de 9 Nm" e, ainda assim, 31% de clipping. As forcas medias vinham achatadas
    // (fraco na maior parte da volta), a pessoa compensava subindo o ganho do jogo, e ai so os picos
    // passavam inteiros (saturacao). Linear e o que as bases comerciais fazem.
    // calibration_current 30->5: os 30 A nunca foram o valor real. Quem manda na varredura é o
    // motor_link_relax_calibration(), que grava 5 A — o que vence o cogging do hoverboard sem
    // jogar o rotor entre posições. O default agora diz a verdade sobre o que a placa faz.
    const int32_t* def = s_idef;
    for (int i = 0; i < A0_NUM_SETTINGS; ++i) { s_ival[i] = def[i]; s_fval[i] = 0.0f; }
    for (int i = 0; i < A0_NUM_SETTINGS; ++i) s_fdef[i] = 0.0f;
    s_fdef[12] = 0.05f;   // current_p
    s_fdef[13] = 10.0f;   // current_i
    // torque_constant (Kt) — 0,55 Nm/A, o valor de catalogo do motor de hoverboard, que e o mesmo
    // que o bring-up cravava antes deste campo passar a ser lido. Vir preenchido evita que a tela
    // mostre um zero com cara de campo esquecido. TEM DE BATER com o Default do descritor no app
    // (BaseSettingsSchema.cs): se os dois se separarem, "padrao" no app escreve um valor diferente
    // do de uma placa recem-gravada, e os dois parecem ser "o padrao".
    // ⚠️ Vai em s_fdef ANTES da copia abaixo, nao em s_fval depois dela: s_fdef e quem responde o
    // report 0x17 ("qual e o padrao deste campo?"). Escrever so em s_fval deixaria o valor inicial
    // certo e o botao "Padrao" do app devolvendo zero.
    s_fdef[34] = 0.55f;   // torque_constant
    // 56 brake_resistance_ohm: 2 ohms = o valor que estava CRAVADO no firmware, entao o padrao
    // reproduz exatamente o comportamento de antes deste campo existir. Quem montou outro resistor
    // (ha montagens com 12) passa a ter onde declarar, em vez de precisar recompilar.
    s_fdef[56] = 2.0f;
    for (int i = 0; i < A0_NUM_SETTINGS; ++i) s_fval[i] = s_fdef[i];
}

// --- estado de envio / center ---
// (o offset de centro saiu daqui para wheel_center.cpp — era um zero paralelo ao do FFB)
static bool    s_force_enabled = true;
static uint8_t s_pending_read = 0xFF;
// 0 = valor EM USO (0x15) · 1 = valor de FABRICA (0x17) · 2 = valor GRAVADO na flash (0x18)
static uint8_t s_pending_is_default = 0;     // fieldId pendente de resposta 0x16 (0xFF = nenhum)
static uint32_t s_last_state_ms = 0;
// Ha quanto tempo uma resposta de leitura espera a vez dela. Sem isto a telemetria podia furar a
// fila da leitura indefinidamente — ver o bloco da fome em a0_service.
static uint8_t  s_read_esperando = 0;
static uint32_t s_read_desde_ms  = 0;
static bool    s_inited = false;
static bool    s_save_requested = false;
static bool    s_reboot_requested = false;
static bool    s_enc_test_requested = false;   // CMD_TEST_ENCODER
static bool    s_rearm_requested    = false;   // CMD_REARM
static bool    s_dfu_requested = false;      // CMD_DFU: o ffb_task desarma e salta pro bootloader   // CMD_REBOOT: o ffb_task desarma e reseta o MCU  // CMD_SAVE pediu persistir → o ffb_task grava com motor IDLE

static inline uint16_t rd_u16(const uint8_t* p) { return (uint16_t)p[0] | ((uint16_t)p[1] << 8); }
static inline void put_i16(uint8_t* p, int16_t v)  { p[0] = (uint8_t)(v & 0xFF); p[1] = (uint8_t)((v >> 8) & 0xFF); }
static inline void put_u16(uint8_t* p, uint16_t v) { p[0] = (uint8_t)(v & 0xFF); p[1] = (uint8_t)((v >> 8) & 0xFF); }
static inline void put_u32(uint8_t* p, uint32_t v) { p[0] = (uint8_t)(v); p[1] = (uint8_t)(v >> 8); p[2] = (uint8_t)(v >> 16); p[3] = (uint8_t)(v >> 24); }
static inline int16_t clip_i16(float v) { return v > 32767.0f ? 32767 : (v < -32767.0f ? -32767 : (int16_t)v); }
// Temperaturas viajam como int8 em °C. -128 e reservado para "sem sensor" (o app mostra "—"), entao
// o piso util e -127: uma leitura real absurdamente baixa nao pode virar "sem sensor" por acidente.
static inline int8_t clip_i8(float v) { return v > 127.0f ? 127 : (v < -127.0f ? -127 : (int8_t)v); }

// Aplica os settings que afetam o FFB no ffb_model (os demais ficam guardados p/ read-back).
// `no_boot` separa DUAS naturezas de ajuste, e a separação é de segurança, não de organização.
//
// Os de FEEL (força, batente, curva, filtros) valem NA HORA: é assim que se ajusta um volante —
// mexeu, sentiu. Eles só alteram contas do laço de FFB e não podem derrubar nada.
//
// Os de HARDWARE (variante da placa, tensão da fonte) NÃO. Eles redefinem a escala com que a base lê
// o barramento e os limites em que ela se protege; aplicá-los com o motor armado já derrubou a base
// nesta bancada, e a pessoa que está digitando o número ainda nem terminou de digitar — um "24" a
// caminho de "240" vale como 24 por um instante. Passam a valer no PRÓXIMO BOOT, junto do resto da
// configuração de hardware (pares de polos, Kt e limite de corrente já eram assim: ffb_task chama
// motor_link_apply_motor_settings uma única vez, no início).
//
// Ou seja: a aba Hardware é um formulário que você preenche, salva e reinicia. As outras abas são
// controles que respondem enquanto você mexe.
static void a0_apply_settings(bool no_boot) {
    ffb_model_set_config(
        (float)s_ival[3],    // total_strength %  (força)
        (float)s_ival[7],    // max_torque_limit %
        (int)s_ival[8],      // force_direction (-1/+1)
        (float)s_ival[4],    // spring_strength %
        (float)s_ival[5],    // damper_strength %
        (int)s_ival[0],      // motion_range (graus, DOR)
        (int)s_ival[39], (int)s_ival[40], (int)s_ival[41], (int)s_ival[42], // gains do jogo
        (int)s_ival[24]   // P0: linearity (curva de resposta)
    );
    // P0: ajustes avançados (struct que cresce por setting ligado)
    FfbTuning tune = { (int)s_ival[6], (int)s_ival[23], (int)s_ival[26],   // static_damping, endstop_damping, slew_rate
        { (int)s_ival[28], (int)s_ival[29], (int)s_ival[30], (int)s_ival[31], (int)s_ival[32],
          (int)s_ival[49], (int)s_ival[50], (int)s_ival[51], (int)s_ival[52], (int)s_ival[53],
          (int)s_ival[54] },   // curva de forca: 11 pontos de 10 em 10%
        (int)s_ival[19], (int)s_ival[20],     // reconstruction steps, lpf
        (int)s_ival[1], (int)s_ival[2],       // soft_stop range (°) e strength (%)
        (int)s_ival[21],                      // filtro de saida da forca, em Hz (0 = off)
        (int)s_ival[22] };                    // guarda de oscilacao (0 = off)
    ffb_model_apply_tuning(&tune);
    // PERFIL DE HARDWARE (Placa + Fonte) → deriva divider + trips (mesma lógica espelhada no app).
    //   board_variant(33): 0=placa 24V · 1=placa 56V (default)   ·   bus_nominal_v(27): tensão da fonte [V]
    //   amperagem: 0 (sem campo dedicado ainda; power_limit(16) é "%", não A) → dc_max = follow-up.
    if (no_boot) motor_link_apply_hw_profile((int)s_ival[33], (int)s_ival[27], 0);
    // ATIVAR MOTOR (id 45): trava de bring-up. Firmware novo nao sabe que hardware esta do outro
    // lado — se armar no boot com pole_pairs/CPR/variante errados, o motor esquenta ou dispara.
    // Nasce em 0: a base sobe DESARMADA, o usuario confere os campos de hardware e so entao ativa.
    // Se der errado, desativa e a base desarma na hora (nao depende de reboot nem de tirar da tomada).
    // Religar a permissão é o REARME: destrava a guarda de curso e limpa os erros, senão o campo
    // voltaria a 1 e o motor continuaria parado — que é exatamente a confusão que isto resolve.
    // SENTIDO DO VOLANTE (setting 9). Vale ao vivo: quem descobre que o volante gira ao contrario
    // muda o campo e sente na hora, sem reiniciar. So inverte o que o mundo ve — nunca a FOC.
    wheel_center_set_direction((int)s_ival[9]);

    if (s_ival[45] != 0 && g_motor_enable == 0) s_rearm_requested = true;
    g_motor_enable = (int32_t)s_ival[45];
}

static void a0_init(void) {
    a0_load_defaults();
    // Sobrepõe os defaults com o que estiver salvo na FFB_NVM (blob válido: magic+versão+CRC). Blob
    // inválido/apagado (flash 0xFF ou reflash) → unpackSettings retorna false SEM tocar os arrays → defaults.
    {
        // ⚠️ A ORDEM AQUI É O QUE SALVA A CONFIGURAÇÃO DE QUEM ATUALIZA O FIRMWARE.
        //
        // O blob antigo mora no MESMO setor que a persistência nova usa como primeira página, e
        // nvm_kv_init() apaga essa página quando não reconhece nada válido nela — que é
        // exatamente o caso no primeiro boot depois desta mudança. Por isso o blob é lido para a
        // RAM ANTES: primeiro salvamos o passado, só então deixamos o novo mecanismo arrumar a
        // casa. Invertida, esta ordem apagaria os ajustes de todo mundo, uma única vez, sem
        // deixar rastro de que existiram.
        static uint8_t blob[drivelab::settingsBlobSize(A0_NUM_SETTINGS)];
        const size_t got = settings_flash_read(blob, sizeof(blob));
        const uint16_t campos_gravados = drivelab::settingsBlobFieldCount(blob, got);
        const bool tinha_blob = (campos_gravados > 0);
        if (tinha_blob) drivelab::unpackSettings(blob, got, s_ival, s_fval, (uint16_t)A0_NUM_SETTINGS);

        nvm_kv_init();

        uint32_t marca = 0;
        if (nvm_kv_read(kKvMarca, &marca)) {
            // Já existe configuração no formato novo: ela manda, campo a campo. O que nunca foi
            // gravado simplesmente não está lá, e fica com o padrão — que é o comportamento certo
            // para um ajuste que a pessoa nunca tocou.
            for (uint8_t id = 0; id < A0_NUM_SETTINGS; ++id) {
                uint32_t v = 0;
                if (nvm_kv_read(id, &v)) a0_u32_campo(id, v);
            }
        } else if (tinha_blob) {
            // Primeira vez com o formato novo e havia ajustes salvos: copia tudo para lá agora,
            // de uma vez, para que a próxima leitura já venha do mecanismo novo. Se algo falhar,
            // não há prejuízo — os valores já estão na RAM e o blob antigo continua intacto.
            for (uint8_t id = 0; id < A0_NUM_SETTINGS; ++id) nvm_kv_write(id, a0_campo_u32(id));
            nvm_kv_write(kKvMarca, kKvVersao);
        }

        // CURVA DE FORÇA: 5 pontos → 11. Os cinco ids antigos (28-32) foram mantidos, mas a grade
        // de X mudou de 25 em 25% para 10 em 10% — o valor gravado pensando em "50% da entrada"
        // passou a ser lido como "20%". A curva fica espremida na metade de baixo e AMPLIFICA as
        // forças pequenas, que é o que existe em linha reta: na bancada isso apareceu como o
        // volante tremendo na reta, com 22% saindo onde deviam sair 9%.
        //
        // Reinterpola os 11 pontos em cima da curva antiga. Não grava: o blob na flash segue o
        // antigo, então a migração parte sempre da mesma origem e é idempotente. Quem salvar depois
        // grava os 55 campos e isto deixa de disparar sozinho.
        if (ffb_curve_precisa_migrar(campos_gravados, (uint16_t)SET_FFB_CURVE_5)) {
            int32_t antiga[FFB_CURVE_PONTOS_ANTIGOS], nova[FFB_CURVE_PONTOS_NOVOS];
            for (int i = 0; i < FFB_CURVE_PONTOS_ANTIGOS; ++i) antiga[i] = s_ival[SET_FFB_CURVE_0 + i];
            ffb_curve_migrar_5_para_11(antiga, nova);
            for (int i = 0; i < FFB_CURVE_PONTOS_ANTIGOS; ++i) s_ival[SET_FFB_CURVE_0 + i] = nova[i];
            for (int i = FFB_CURVE_PONTOS_ANTIGOS; i < FFB_CURVE_PONTOS_NOVOS; ++i)
                s_ival[SET_FFB_CURVE_5 + (i - FFB_CURVE_PONTOS_ANTIGOS)] = nova[i];
        }
    }

    // TRAVA DE BRING-UP vs. firmware novo. Os settings sobrevivem à gravação (de propósito), então
    // sem isto quem já ativou o motor uma vez receberia toda atualização com a base armando sozinha
    // — justo quando a configuração de hardware tem mais chance de não bater com o binário novo.
    // Aqui a trava volta a zero UMA vez, no primeiro boot depois de gravar; no dia a dia (mesmo
    // firmware) nada muda e a base sobe pronta. Ver inc/bringup_lock.h.
    {
        const BringupLockDecision d = bringup_lock_decide(
            (uint32_t)s_ival[47], (uint32_t)DRVLAB_BUILD_ID, s_ival[45]);
        s_ival[45] = d.motor_enable;
        s_ival[47] = (int32_t)d.build_id;
        // Persistir aqui só quando houve trava de verdade: gravar a identidade nova a cada boot
        // gastaria ciclos de flash à toa. O ffb_task faz a gravação com o motor IDLE.
        if (d.relocked) s_save_requested = true;
    }

    a0_apply_settings(/*no_boot=*/true);   // no boot vale tudo, hardware incluso
    s_inited = true;
}

// Lê a trava "Ativar motor" da NVM ANTES do canal A0 (e do eixo) existir. Serve ao ffb_storage_preload,
// que decide se a calibração de boot pode rodar — e essa decisão acontece antes de qualquer a0_service.
// Não toca em estado nenhum do canal: lê o blob, aplica a MESMA regra de bringup_lock_decide (senão o
// boot calibraria com a trava que o a0_init vai zerar logo em seguida) e devolve o valor efetivo.
extern "C" int32_t a0_peek_motor_enable(void) {
    uint8_t blob[drivelab::settingsBlobSize(A0_NUM_SETTINGS)];
    int32_t iv[A0_NUM_SETTINGS] = {0};
    float   fv[A0_NUM_SETTINGS] = {0};
    const size_t got = settings_flash_read(blob, sizeof(blob));
    if (!drivelab::unpackSettings(blob, got, iv, fv, (uint16_t)A0_NUM_SETTINGS))
        return 0;   // blob inválido/apagado → default é DESARMADO
    const BringupLockDecision d = bringup_lock_decide(
        (uint32_t)iv[47], (uint32_t)DRVLAB_BUILD_ID, iv[45]);
    return d.motor_enable;
}

/// Leitura de um setting inteiro por id, para quem precisa configurar hardware a partir do que o
/// usuário escolheu. Devolve 0 se o id não existe — e 0 nos campos do encoder significa
/// "não informado", que o chamador trata como "não aplicar".
extern "C" int32_t a0_get_setting(uint8_t id) {
    if (!s_inited) a0_init();
    return (id < A0_NUM_SETTINGS) ? s_ival[id] : 0;
}

/// Mesma coisa para os campos declarados T_FLOAT (o valor mora em s_fval, não em s_ival). Ler um
/// campo float por a0_get_setting devolveria o inteiro paralelo, que não é o valor — daí a função
/// separada. Devolve 0.0f para id inexistente, e 0.0f nos campos de hardware significa
/// "não informado", que o chamador trata como "não aplicar".
extern "C" float a0_get_setting_f(uint8_t id) {
    if (!s_inited) a0_init();
    return (id < A0_NUM_SETTINGS) ? s_fval[id] : 0.0f;
}

extern "C" bool a0_reboot_pending(void) { return s_reboot_requested; }
extern "C" bool a0_enc_test_pending(void) { return s_enc_test_requested; }
extern "C" void a0_enc_test_clear(void)   { s_enc_test_requested = false; }
extern "C" bool a0_rearm_pending(void)    { return s_rearm_requested; }

// ⚠️ A PROTEÇÃO RETIRA A PERMISSÃO, em vez de deixar o campo ligado sobre um motor parado.
//
// Antes, quando a guarda de curso travava, "Permitir armar o motor" continuava em 1 e o motor ficava
// desarmado — e quem olhava a tela não tinha o que ligar para voltar a ter força. Zerando o campo, a
// saída passa a ser a óbvia: o usuário vê 0, liga de novo, e ligar É o rearme (ver a0_apply_settings).
//
// Semanticamente também fecha melhor: a permissão não foi retirada por quem usa, foi retirada PELA
// BASE, e o campo passa a contar essa história em vez de mentir que está tudo liberado.
extern "C" void a0_revoke_motor_enable(void) {
    if (s_ival[45] == 0) return;
    s_ival[45] = 0;
    g_motor_enable = 0;
}
extern "C" void a0_rearm_clear(void)      { s_rearm_requested = false; }
extern "C" bool a0_dfu_pending(void)    { return s_dfu_requested; }

// Pedido de save pendente? O ffb_task consulta e grava SÓ com o motor IDLE (a flash congela a CPU).
extern "C" bool a0_save_pending(void) { return s_save_requested; }

// Empacota os settings atuais e grava na FFB_NVM. Chamado pelo ffb_task SÓ com o motor IDLE. Limpa o
// pedido em qualquer caso (sucesso ou falha de flash) p/ não travar. Retorna true se gravou.
// ⚠️ CONTADOR DE GRAVAÇÕES CONCLUÍDAS — sobe UMA vez por gravação que de fato foi para a flash.
//
// Existe porque "Salvar" era um pedido sem resposta. A gravação exige o motor PARADO (ela congela a
// CPU por ~250 ms, e fazer isso com as fases energizadas é pior), então numa base que está tentando
// calibrar sem parar o momento pode nunca chegar — e se a placa reinicia antes, o valor volta ao que
// estava gravado. Do lado de quem usa: "salvei, reiniciei, perdeu". Foi o que aconteceu na bancada em
// 15/08/2026, e a conclusão natural (e errada) foi "o app não salva".
//
// Com o contador na telemetria, o app espera ele subir e diz o que aconteceu. Um "Salvar" que não
// confirma é pior que um que falha avisando.
volatile uint32_t g_save_count = 0;

extern "C" uint32_t a0_get_save_count(void) { return g_save_count; }



extern "C" bool a0_commit_save(void) {
    // ⚠️ GRAVA CAMPO A CAMPO, E SÓ O QUE MUDOU.
    //
    // O modelo anterior empacotava os 58 ajustes, APAGAVA o setor e reescrevia tudo. Apagar
    // congela o processador por ~250 ms, e daí vinha toda a fragilidade: precisava do motor
    // parado, não acontecia numa base ocupada, e gravava o conteúdo da MEMÓRIA — se um ajuste
    // se perdesse no caminho do USB, o valor velho ia para a flash com o contador subindo
    // igual. Foi assim que um encoder de 2500 pulsos ficou gravado como 1000 três vezes
    // seguidas, com o app dizendo que tinha salvo (bancada, 18/08/2026).
    //
    // Agora cada ajuste é uma chave. Escrever é acrescentar 8 bytes; ajuste que não mudou não
    // gasta nada; cada escrita é conferida lendo de volta. Nada disso apaga flash, então o
    // motor pode estar armado — e "Salvar" deixa de depender de um momento que talvez nunca
    // chegue.
    nvm_kv_balanco_zerar();
    s_precisa_compactar = false;

    for (uint8_t id = 0; id < A0_NUM_SETTINGS; ++id) {
        const NvmKvResultado r = nvm_kv_write(id, a0_campo_u32(id));
        if (r == NVM_KV_CHEIO) {
            // A página encheu no meio. NÃO é falha e o pedido NÃO se perde: sinalizamos que
            // é preciso compactar (o único passo que apaga flash, e portanto o único que quer
            // o motor parado) e o laço tenta de novo depois. O que já foi escrito continua
            // válido — a leitura sempre pega a última ocorrência de cada chave.
            s_precisa_compactar = true;
            return false;
        }
    }
    nvm_kv_write(kKvMarca, kKvVersao);

    const NvmKvBalanco b = nvm_kv_balanco();
    if (b.erros > 0) return false;      // pedido segue pendente: o laço tenta na próxima volta

    g_save_count++;                     // só conta o que de fato foi para a flash
    s_save_requested = false;
    return true;
}

// ---------------------------------------------------------------------------
// OUT reports (host→device) — chamado do tud_hid_set_report_cb. Só guarda/flag.
// ---------------------------------------------------------------------------
extern "C" void a0_handle_out(const uint8_t* buf, uint16_t len) {
    if (!s_inited) a0_init();
    if (buf == nullptr || len < 1) return;

    switch (buf[0]) {
        case A0_RID_SETWRITE: {            // wire: [0x14, FieldId, Index, Type, valor LE]
            if (len < 5) return;
            const uint8_t id = buf[1];
            if (id >= A0_NUM_SETTINGS) return;
            const uint8_t type = buf[3];   // buf[2]=Index (=0, ignorado)
            switch (type) {
                case T_U8:  s_ival[id] = buf[4]; break;
                case T_I8:  s_ival[id] = (int8_t)buf[4]; break;
                case T_U16: if (len >= 6) s_ival[id] = rd_u16(&buf[4]); break;
                case T_I16: if (len >= 6) s_ival[id] = (int16_t)rd_u16(&buf[4]); break;
                case T_U32: if (len >= 8) s_ival[id] = (int32_t)((uint32_t)buf[4]
                                                       | ((uint32_t)buf[5] << 8)
                                                       | ((uint32_t)buf[6] << 16)
                                                       | ((uint32_t)buf[7] << 24)); break;
                case T_FLOAT: if (len >= 8) memcpy(&s_fval[id], &buf[4], 4); break;
                default: break;
            }
            a0_apply_settings(/*no_boot=*/false);   // ao vivo: feel sim, hardware não
            break;
        }
        case A0_RID_SETREAD:               // wire: [0x15, FieldId, Index] → responde 0x16 no a0_service
            if (len >= 2 && buf[1] < A0_NUM_SETTINGS) { s_pending_read = buf[1]; s_pending_is_default = 0; }
            break;
        // ⚠️ LER O QUE ESTA GRAVADO — a diferenca entre acreditar e verificar. O "Salvar" precisa
        // dizer se gravou, e um contador so diz que ALGO aconteceu, nao QUE valor ficou la. Pior:
        // aquele contador ia na telemetria, que e justamente o caminho onde o firmware trava.
        case A0_RID_NVMREAD:               // wire: [0x18, FieldId] → responde 0x16 com o GRAVADO
            if (len >= 2 && buf[1] < A0_NUM_SETTINGS) { s_pending_read = buf[1]; s_pending_is_default = 2; }
            break;
        case A0_RID_DEFREAD:               // wire: [0x17, FieldId] → responde 0x16 com o PADRAO
            // Consulta pura: NAO altera s_ival/s_fval. O app mostra o padrao na tela e so o Salvar
            // (que grava campo a campo pelo 0x14) muda alguma coisa na placa.
            if (len >= 2 && buf[1] < A0_NUM_SETTINGS) { s_pending_read = buf[1]; s_pending_is_default = 1; }
            break;
        case A0_RID_CMD: {                 // wire: [0x22, CommandId, Arg]
            if (len < 2) return;
            const uint8_t cmd = buf[1];
            const uint8_t arg = (len >= 3) ? buf[2] : 0;
            switch (cmd) {
                case CMD_RESET_CENTER:  wheel_center_capture(); break;   // zero unico (FFB + eixo do jogo + telemetria)
                case CMD_SET_FORCE_ENABLED: s_force_enabled = (arg != 0); break;
                case CMD_SAVE: s_save_requested = true; break;  // grava na flash no ffb_task com motor IDLE
                // Reiniciar a base pelo app. Deferido para o laço como o save: NUNCA resetar com as
                // fases energizadas — o ffb_task desarma primeiro e só então reseta o MCU.
                case CMD_REBOOT: s_reboot_requested = true; break;
                // Entrar em DFU pelo app (atualizar firmware sem ST-Link e sem jumper).
                // Deferido como o reboot: o motor precisa estar DESARMADO antes do reset.
                case CMD_DFU: s_dfu_requested = true; break;
                // TESTE DO ENCODER (ver encoder_eccentricity.h). Deferido para o laço como o save:
                // ele precisa alongar a varredura e pedir o estado de calibração ao ODrive, o que
                // não se faz de dentro do callback do USB.
                case CMD_TEST_ENCODER: s_enc_test_requested = true; break;
                case CMD_REARM:        s_rearm_requested    = true; break;
                // CMD_CALIBRATE: TODO (trabalho no loop, não aqui)
                default: break;
            }
            break;
        }
        // [rid, spring i16, constant i16, periodic i16, damper i16, drop u8, telem i16]
        // Offsets deslocados de 1 porque buf[0] e o report ID (veio pelo EP OUT).
        case A0_RID_DIRECT:
            if (len >= 12) {
                // Os QUATRO efeitos, que ate 2026-08-12 trafegavam e eram descartados: so o `telem`
                // era lido. A aba de Testes do Studio manda a forca por eles, e o volante nao se
                // mexia. `spring`/`damper` sao GANHO, `constant`/`periodic` sao FORCA — ver o bloco
                // em ffb_model.cpp. O app manda ±10000 = ±100%.
                const float kEscala = 1.0f / 10000.0f;
                ffb_model_set_direct_control(
                    (float)(int16_t)rd_u16(&buf[3]) * kEscala,   // constant
                    (float)(int16_t)rd_u16(&buf[1]) * kEscala,   // spring
                    (float)(int16_t)rd_u16(&buf[5]) * kEscala,   // periodic
                    (float)(int16_t)rd_u16(&buf[7]) * kEscala);  // damper
                int16_t telem = (int16_t)rd_u16(&buf[10]);
                ffb_model_set_telemetry_force((float)telem);   // força aditiva de telemetria do app
            }
            break;
        default: break;
    }
}

// Posicao do volante ja com o offset de centro. O zero mora em wheel_center.cpp e e o MESMO que o
// FFB e o eixo do jogo usam — antes cada um tinha o seu, e o batente saturava enquanto a tela do
// app mostrava o angulo certo, escondendo o problema. Ver inc/wheel_center.h.
static float a0_centered_turns(void) { return wheel_center_pos_turns(); }

// ---------------------------------------------------------------------------
// Monta o DeviceState (0x21) — layout de BaseState.cs.
// ---------------------------------------------------------------------------
static void a0_build_state(uint8_t* p) {
    memset(p, 0, A0_PAYLOAD);
    // VERSAO DO FIRMWARE — [tipo, major, minor, patch]. Casa com a versao da RELEASE e com o
    // <Version> do Studio: os tres eram numeros diferentes (app 0.1.5, firmware 0.4.0, release
    // 0.2.3) e nenhum respondia a unica pergunta que o usuario faz — "preciso atualizar?".
    // Ao cortar uma release nova, subir os TRES juntos.
    //
    // Vem do fw_version.h, e nao mais escrito a mao aqui: o carimbo do .bin (fw_signature.c) ficou
    // em 0.4.0 enquanto esta linha andava para 0.2.3, e o efeito era a tela de atualizacao dizer
    // "arquivo versao 0.4.0" e, depois de gravar com sucesso, "base v0.2.3" — que parece gravacao
    // que nao pegou. Enquanto forem duas declaracoes, elas voltam a divergir.
    p[0] = 0;
    p[1] = DRVLAB_FW_VER_MAJOR; p[2] = DRVLAB_FW_VER_MINOR; p[3] = DRVLAB_FW_VER_PATCH;

    uint8_t flags = 0;
    const bool armed = motor_link_motor_is_armed();
    if (armed && s_force_enabled) flags |= 0x01;            // ForceEnabled
    if (armed)                    flags |= 0x02;            // Calibrated (armado = calibrado)
    if (motor_link_axis_error() != 0) flags |= 0x04;     // Error
    // Guarda de coerência do ângulo elétrico disparou → motor desarmado e auto-arme travado.
    // Vale 0x40 (BaseFlags.AngleGuardTripped). É informação que o usuário PRECISA ver: sem ela,
    // "o FFB sumiu" é indistinguível de cabo solto — e a causa real (corcente virando calor por
    // ângulo errado) é justamente a que não se pode ignorar.
    if (g_guard_trip || g_overspeed_trip) flags |= 0x40;    // AngleGuardTripped (ângulo OU sobrevelocidade)
    // Guarda de CURSO EXCEDIDO, em bit próprio e não junto da de ângulo: as duas desarmam, mas por
    // motivos diferentes e com saídas diferentes — a de ângulo exige power-cycle, esta pode
    // re-armar sozinha se o usuário escolheu. Somá-las num flag só faria a tela dar a instrução
    // errada na metade dos casos. É também o que o teste de bancada consulta para saber se a guarda
    // fez o trabalho dela, em vez de inferir do desarme (que tem muitas outras causas).
    if (g_overtravel_trip) flags |= 0x80;                   // OvertravelTripped
    p[4] = flags;                                           // NÃO setar bit3 (UsingSimulator)

    const float turns = a0_centered_turns();
    float norm = turns / 1.5f;                              // ±1.5 turns → ±100%
    if (norm > 1.0f) norm = 1.0f; else if (norm < -1.0f) norm = -1.0f;
    put_i16(&p[5], (int16_t)(norm * 10000.0f));            // Position ±10000
    put_i16(&p[7], clip_i16(turns * 3600.0f));            // AngleDeciDeg (décimos de grau) ← gira o volante
    // Torque ±10000 = fração do FUNDO DE ESCALA, que vem de DRVLAB_FULL_SCALE_TORQUE_NM (ffb_model.h).
    // Antes o divisor era um 10.0f digitado aqui, obrigado a ser mantido em sincronia na mão — e já
    // errou: com o 5 antigo ainda escrito, a barra marcava o DOBRO do real. Agora é a mesma constante.
    put_i16(&p[9], clip_i16(motor_link_get_input_torque() / DRVLAB_FULL_SCALE_TORQUE_NM * 10000.0f));
    put_i16(&p[11], clip_i16(motor_link_get_iq_measured() * 1000.0f));          // MotorCurrentMa
    // FetTempC — temperatura REAL do estagio de potencia. Era -128 fixo ("sem sensor") enquanto o
    // termistor existia, era lido pelo ODrive e ja vinha convertido em °C. Ver motor_link.
    // O -128 da ponte e SENTINELA e passa direto: clipa-lo daria -127, que o app leria como uma
    // temperatura real de -127 °C em vez de "sem leitura".
    const float fet_c = motor_link_get_fet_temp_c();
    p[13] = (uint8_t)(fet_c <= -127.5f ? (int8_t)-128 : clip_i8(fet_c));
    p[14] = (uint8_t)(motor_link_axis_error() & 0xFF);  // ErrorCode
    put_u16(&p[15], (uint16_t)(motor_link_get_vbus() * 1000.0f));               // BusVoltageMv
    p[17] = (uint8_t)(int8_t)(-128);                       // MotorTempC (sem sensor)
    // Temperatura do MCU: sensor interno do STM32 (ver motor_link). Única temperatura real desta
    // placa. Satura em ±127 porque o campo é i8; -128 fica reservado p/ "sem sensor".
    {
        const float mcu = motor_link_get_mcu_temp_c();
        int8_t mcu_c = -128;                                // default: sem leitura
        if (mcu > -127.5f) mcu_c = (int8_t)(mcu > 127.0f ? 127 : (mcu < -127.0f ? -127 : mcu));
        p[18] = (uint8_t)mcu_c;                             // McuTempC
    }
    p[19] = ffb_model_get_clipping();                       // Clipping 0-255 (o app converte p/ %)

    // Medidor do brake chopper (bytes 20-29) — layout casado com BaseState.cs.
    // Bytes que antes iam zerados: nenhum pacote novo, nenhuma taxa nova.
    put_u32(&p[20], g_brake_meter.energy_mj);              // BrakeEnergyMilliJ
    put_u32(&p[24], g_brake_meter.activations);            // BrakeActivations
    put_u16(&p[28], g_brake_meter.peak_dw);                // BrakePeakDeciW

    // Medidores do monitor (bytes 30-34) — layout casado com BaseState.cs.
    // Fracao do tempo ARMADO em que a base saturou. Este byte carregava o PICO de uma janela de
    // 500 ms, e aquele numero era lido errado pelo caminho mais natural: "28%" parecia "um terco da
    // volta teve clipping" quando significava "no pior meio-segundo, 28% dele". Trocado pela razao
    // acumulada, que e o que a pessoa entende ao ler.
    p[30] = ffb_model_get_clipping_session();              // ClippingSession
    put_i16(&p[31], g_current_peak_ma.pos);                // CurrentPeakPosMa
    put_i16(&p[33], g_current_peak_ma.neg);                // CurrentPeakNegMa

    // As DUAS PARCELAS do clipping (bytes 35-36). O total em p[19] nao diz em que botao mexer: a
    // parcela do JOGO so cede baixando o ganho DENTRO do jogo (a forca ja chegou cortada, e nada
    // aqui recupera o que se perdeu), a da BASE cede com mais torque ou menos ganho. Ver o bloco do
    // medidor em ffb_model.cpp. Bytes que ja iam zerados: nenhum pacote novo, nenhuma taxa nova.
    p[35] = ffb_model_get_clipping_game();                 // ClippingGame (ao vivo, janela de 500 ms)
    p[36] = ffb_model_get_clipping_base();                 // ClippingBase (ao vivo)
    // 37-38 ficaram como estao (picos por parcela). Nao vao para a tela — o monitor mostra o
    // acumulado da sessao em p[30] — mas seguem uteis por SWD para entender uma sessao ruim.
    p[37] = g_clip_peak_game;                              // ClippingPeakGame (sessao)
    p[38] = g_clip_peak_base;                              // ClippingPeakBase (sessao)

    // TESTE DO ENCODER — o que a varredura mediu (ver encoder_eccentricity.h). Fica na telemetria
    // e nao numa resposta propria porque o app precisa VER o resultado aparecer quando o teste
    // termina, sem ficar perguntando.
    p[39] = (uint8_t)(g_ecc.valido ? 1 : 0);
    // Cobertura em centesimos de volta. E ELA que diz se o numero abaixo vale: excentricidade tem
    // periodo de uma volta, e abaixo de 0,75 a medicao nao se sustenta.
    p[40] = (uint8_t)(g_ecc.cobertura_milivolta / 10 > 255 ? 255 : g_ecc.cobertura_milivolta / 10);
    put_i16(&p[41], (int16_t)g_ecc.amplitude_cdeg);        // excentricidade, centesimos de grau
    put_i16(&p[43], (int16_t)g_ecc.residuo_cdeg);          // o que a senoide nao explica
    put_i16(&p[45], (int16_t)(g_ecc.fase_cdeg / 10));      // onde o erro e maximo, decimos de grau
    p[47] = (uint8_t)motor_link_motor_pole_pairs();        // o app converte grau mecanico -> eletrico

    // Gravacoes concluidas desde o boot. O app NAO usa mais isto para confirmar um "Salvar" — ele
    // rele o valor da memoria permanente e compara, porque um contador so diz que ALGO aconteceu, e
    // ia justamente pela telemetria, que e o caminho que trava. Fica como diagnostico por SWD: e o
    // numero que separa "nao gravou" de "gravou outra coisa".
    p[48] = (uint8_t)(g_save_count & 0xFFu);

    // ⚠️ O BALANÇO DO ÚLTIMO "SALVAR" — o que substituiu o app adivinhando.
    //
    // Antes, o app tinha de reler campo a campo e deduzir o que havia acontecido, e mesmo assim
    // não sabia distinguir "não gravou" de "não consegui conferir". Agora a base diz, porque ela
    // é quem sabe: quantas chaves foram escritas de fato, quantas já estavam com o valor certo
    // (e por isso nem foram tocadas) e quantas a flash recusou.
    //
    // "Iguais" não é enfeite: é a prova de que salvar duas vezes seguidas não gasta flash, e é o
    // número que explica um Salvar instantâneo sem nenhuma escrita.
    {
        const NvmKvBalanco b = nvm_kv_balanco();
        put_u16(&p[49], b.gravadas);
        put_u16(&p[51], b.iguais);
        put_u16(&p[53], b.erros);
        // Quanto ainda cabe antes da compactação (a única operação que ainda pede motor parado).
        // Saturado em 65535 porque o que importa é "está perto de encher?", não o número exato.
        const uint32_t livre = nvm_kv_espaco_livre();
        put_u16(&p[55], (uint16_t)(livre > 0xFFFFu ? 0xFFFFu : livre));
    }
}

// ---------------------------------------------------------------------------
// Envio no laço (a0_service): PRIORIDADE resposta 0x16 > telemetria 0x21. Gated por ready().
// nowMs: relógio em ms. Retorna true se enviou algo (o chamador pode dar prioridade ao joystick).
// ---------------------------------------------------------------------------
// Chave de EXPERIMENTO: 1 desliga o envio da telemetria (0x21), sem mexer em mais nada. Serviu para
// isolar o travamento — 8,7 h sem um reinício com ela ligada, contra 5 reinícios em 10 h com a
// telemetria ativa, ambas com a base ociosa. Fica em 0 (comportamento normal); quem for repetir o
// teste liga por SWD, ou grava um binário com 1 para ele sobreviver a reset e queda de energia.
volatile int32_t g_telemetria_off = 0;

extern "C" int a0_service(uint32_t nowMs) {
    if (!s_inited) a0_init();
    blackbox_step(BB_STEP_A0_READY);
    if (!tud_hid_n_ready(A0_HID_ITF)) return 0;

    // 0) ANTI-STARVATION: a leitura tem prioridade sobre a telemetria (abaixo), mas prioridade
    // absoluta virou fome. O app lê os ~48 campos UM A UM ao abrir/reconectar, e cada leitura ocupa
    // esta vez: medido por SWD em 2026-08-10, a telemetria ficou 1,2 s SEM SAIR e o desenho do
    // volante congelou na tela ("não acompanha o giro do motor"). Quem paga é justamente o feedback
    // visual, no exato momento em que o usuário está olhando para ele.
    //
    // Regra: passou de 40 ms sem telemetria, ela fura a fila UMA vez. A leitura perde um ciclo de
    // 4 ms — imperceptível para quem carrega uma aba — e o desenho nunca congela.
    //
    // ⚠️ 40 ms VIRARAM 200: o motivo dos 40 deixou de existir.
    //
    // Eles existiam pelo DESENHO DO VOLANTE. O registro anterior: "com 100 ms, girando o volante a
    // ~1000 °/s, o ângulo pulava até 100° de uma amostra para a outra; o app trata salto acima de 90°
    // como 'assume direto', e o desenho SALTAVA". Era verdade — enquanto o ângulo viesse daqui.
    //
    // Agora o app lê a direção do relatório que já vai para o JOGO, a 1 kHz, pelo mesmo endpoint e
    // sem custo nenhum: aquele relatório é enviado de qualquer forma. Sobrou para a telemetria o que
    // ela sempre foi — dados de painel: temperatura, corrente, tensão, clipping. Nada disso precisa
    // de vinte e cinco amostras por segundo.
    //
    // E o ganho não é economia de banda: é REDUZIR A EXPOSIÇÃO. Todos os travamentos que capturamos
    // pararam no envio deste relatório, e a base ficou 8,7 horas sem travar quando ele foi desligado.
    // Cinco envios por segundo em vez de vinte e cinco é um quinto das oportunidades de tropeçar,
    // enquanto a causa não é encontrada.
    const int telemetria_atrasada = (uint32_t)(nowMs - s_last_state_ms) >= 40;

    // ============================================================================================
    // QUEM FALA AGORA: REVEZAMENTO, NÃO PRIORIDADE
    // ============================================================================================
    // Este ponto já teve três regras diferentes, e cada uma criou o bug que a seguinte tentou
    // consertar. A leitura tinha prioridade absoluta e matava a telemetria de fome (o desenho do
    // volante congelava 1,2 s). Aí a telemetria passou a furar a fila quando atrasava — e a leitura
    // ficou esperando para sempre, porque quem julga se a telemetria está atrasada é ela mesma; o
    // "Salvar" acusou três campos como não conferidos com a base parada e a gravação já feita.
    // Depois a leitura ganhou o direito de furar de volta, e passamos a ter duas regras se vigiando.
    //
    // O erro é anterior aos números: prioridade entre dois produtores do MESMO canal precisa que
    // alguém decida quem é mais importante, e essa decisão nunca é estável — muda com a carga, com
    // o app aberto, com o jogo rodando. A implementação de referência que o usuário trouxe
    // (19/08/2026) não tem essa disputa: ela publica UM relatório e pronto.
    //
    // Aqui fica o revezamento: cada um manda na sua vez. Não há como um caminho ficar preso atrás do
    // outro, porque não há mais "atrás" — há alternância. A leitura ainda anda na frente quando a
    // telemetria não tem nada novo a dizer (ela só fala a cada 20 ms), então carregar uma aba
    // continua rápido; e a telemetria nunca perde a vez, então o painel não congela.
    //
    // Custo de uma vez perdida: 1 ms. Não existe caso em que isso se note.
    static uint8_t s_vez_da_leitura = 1;
    const bool tem_leitura    = (s_pending_read != 0xFF);
    const bool tem_telemetria = ((uint32_t)(nowMs - s_last_state_ms) >= 20u);
    // Só reveza quando os DOIS querem falar; com um só, ele fala sempre.
    const bool leitura_agora  = tem_leitura && (!tem_telemetria || s_vez_da_leitura);
    if (tem_leitura && tem_telemetria) s_vez_da_leitura = (uint8_t)!s_vez_da_leitura;

    // 1) resposta deferida de leitura (0x16), quando for a vez dela
    if (leitura_agora) {
        const uint8_t id = s_pending_read;
        uint8_t p[A0_PAYLOAD]; memset(p, 0, sizeof(p));
        p[0] = id; p[1] = 0; p[2] = s_type[id];
        // O blob da flash e desempacotado NA HORA, e nao guardado numa copia em RAM: copia
        // envelheceria, e o app "verificaria" contra um retrato velho — concluindo que gravou quando
        // nao gravou, que e o erro que esta leitura existe para impedir.
        static int32_t nvm_i[A0_NUM_SETTINGS];
        static float   nvm_f[A0_NUM_SETTINGS];
        const int32_t* src_i;
        const float*   src_f;
        if (s_pending_is_default == 2) {
            // O que está GRAVADO vem da persistência por chave/valor. Campo que nunca foi salvo
            // responde o PADRÃO — é o que a base usaria num boot agora, e é a resposta honesta a
            // "o que está guardado aqui?".
            memcpy(nvm_i, s_idef, sizeof(nvm_i));
            memcpy(nvm_f, s_fdef, sizeof(nvm_f));
            for (uint8_t k = 0; k < A0_NUM_SETTINGS; ++k) {
                uint32_t v = 0;
                if (!nvm_kv_read(k, &v)) continue;
                if (s_type[k] == T_FLOAT) memcpy(&nvm_f[k], &v, 4);
                else                      nvm_i[k] = (int32_t)v;
            }
            src_i = nvm_i; src_f = nvm_f;
        } else {
            src_i = s_pending_is_default ? s_idef : s_ival;
            src_f = s_pending_is_default ? s_fdef : s_fval;
        }
        if (s_type[id] == T_FLOAT)      memcpy(&p[3], &src_f[id], 4);
        else if (s_type[id] == T_U32)   put_u32(&p[3], (uint32_t)src_i[id]);
        else if (s_type[id] == T_U16 || s_type[id] == T_I16) put_i16(&p[3], (int16_t)src_i[id]);
        else                            p[3] = (uint8_t)(src_i[id] & 0xFF);
        blackbox_step(BB_STEP_A0_LEITURA);
        if (tud_hid_n_report(A0_HID_ITF, A0_RID_SETVALUE, p, A0_PAYLOAD)) { s_pending_read = 0xFF; s_pending_is_default = 0; return 1; }
        return 0;   // EP ocupou entre o ready() e o report — re-tenta no próximo loop
    }

    // 2) telemetria DeviceState (0x21) — na vez dela
    if (tem_telemetria) {
        uint8_t p[A0_PAYLOAD];
        blackbox_step(BB_STEP_A0_MONTA);
        a0_build_state(p);
        blackbox_step(BB_STEP_A0_TELEMETRIA);
        if (g_telemetria_off) { s_last_state_ms = nowMs; return 1; }
        if (tud_hid_n_report(A0_HID_ITF, A0_RID_STATE, p, A0_PAYLOAD)) { s_last_state_ms = nowMs; return 1; }
    }
    return 0;
}
