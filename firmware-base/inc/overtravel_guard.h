// ============================================================================
//  DriveLab
//  overtravel_guard.h — O volante não pode estar onde ele não tem direito de
//  estar. Guarda por POSIÇÃO, complementar às de velocidade e de corrente.
//
//  O ARGUMENTO: o curso é conhecido (o DOR configurado), e o batente por
//  software começa alguns graus antes do fim. Se o volante aparece MUITO além
//  do fim do curso, só há duas explicações, e as duas são falha: o batente foi
//  vencido, ou a FOC está girando o motor sozinha. Não existe terceira — nenhum
//  piloto, com nenhuma força de braço, leva o volante para lá com o firmware
//  funcionando. É um teste binário, sem zona cinzenta.
//
//  POR QUE AS OUTRAS DUAS GUARDAS NÃO COBREM ISTO:
//    · a de sobrevelocidade exige 5 voltas/s por 150 ms — 0,75 volta de curso
//      percorrido antes de agir, e é CEGA para o disparo lento (um offset ruim
//      que produza torque parasita fraco gira meia volta por segundo para
//      sempre sem nunca cruzar o limiar);
//    · a de coerência do ângulo elétrico exige corrente alta E torque comandado
//      baixo E velocidade baixa — o retrato do motor travado. Um motor que gira
//      solto não se parece com isso.
//  Nenhuma pergunta "onde o volante está?", que é o dado mais simples e mais
//  verdadeiro que temos.
//
//  A AÇÃO: EMPURRA DE VOLTA. Zera a força do jogo e aplica no lugar dela uma
//  MOLA de retorno (proporcional a quanto passou) mais amortecimento. O motor
//  continua vivo e trabalhando a favor: o volante é trazido para dentro do
//  curso, e no instante em que entra a guarda devolve o controle ao jogo.
//
//  ⚠️ POR QUE NÃO DESARMA MAIS. Desarmava — e era a decisão errada, pelo custo:
//  numa corrida, passar do fim do curso e voltar é um susto; ficar sem base é o
//  fim da sessão. Pior, o desarme punia o sintoma de um defeito que não era
//  desta guarda (a parede competia com a força do jogo dentro do mesmo teto e
//  era engolida), e a janela de 150 ms expirava com a pessoa ainda girando —
//  bastava empurrar o aro contra o batente para a base desligar.
//
//  O DESARME SOBROU PARA UM CASO SÓ — FUGA: com a mola aplicada, o volante
//  continua ACELERANDO e se AFASTANDO. Isso nenhum braço faz contra o motor;
//  é a assinatura limpa de calibração ruim (o torque não sai no ângulo certo,
//  que é justamente a falha que a mola não consegue corrigir). Exige as duas
//  coisas ao mesmo tempo, por uma janela larga: velocidade alta E distância
//  crescente. Empurrar firme com a mão satisfaz uma, nunca as duas.
//
//  RE-ARME (escolha do usuário, padrão = travar): re-armar sozinho exige DUAS
//  condições, não uma — o volante de volta DENTRO do curso E parado. Só a
//  primeira geraria um laço infinito: re-arma ainda fora, dispara no mesmo tick,
//  repete. E há teto de re-armes: três disparos seguidos não são mais "bati no
//  muro", são defeito, e aí trava de qualquer forma.
//
//  Lógica PURA (sem STM32, sem motor): roda igual no firmware e no PC.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#ifndef DRIVELAB_OVERTRAVEL_GUARD_H
#define DRIVELAB_OVERTRAVEL_GUARD_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/// O que o chamador deve fazer NESTE tick.
typedef enum {
    OT_ACT_NORMAL = 0,   ///< nada a ver aqui: aplica o torque do pipeline
    OT_ACT_RECENTER = 1, ///< força do jogo FORA; aplica mola de retorno + amortecimento (ver ffb_task)
    OT_ACT_DISARM = 2,   ///< torque zero + pedir IDLE
    OT_ACT_HOLD   = 3    ///< já desarmado; segura assim (esperando voltar ao curso, ou travado)
} OvertravelAction;

typedef enum {
    OT_MODE_LOCK   = 0,  ///< disparou → trava; só volta com reinício (PADRÃO)
    OT_MODE_REARM  = 1   ///< disparou → re-arma quando voltar ao curso e parar
} OvertravelMode;

typedef struct {
    float    margin_rad;      ///< quanto além do fim do curso ainda é tolerado
    uint16_t fuga_ms;         ///< quanto tempo de FUGA contínua até desarmar
    float    fuga_rad_s;      ///< velocidade que caracteriza fuga (braço nenhum sustenta contra a mola)
    float    fuga_rad;        ///< e quanto ainda precisa se AFASTAR do ponto do disparo
    float    stopped_rad_s;   ///< abaixo disto o volante conta como parado
    uint8_t  mode;            ///< OvertravelMode
    uint8_t  max_trips;       ///< re-armes permitidos antes de travar de vez
} OvertravelCfg;

typedef struct {
    uint8_t  state;           ///< interno: 0=normal 1=empurrando-de-volta 2=fora-desarmado 3=travado
    uint16_t fuga_ticks;      ///< ms de fuga CONTÍNUA (zera a cada tick que não é fuga)
    float    vel_at_trip;     ///< velocidade no instante do disparo — a referência do "caiu?"
    /// Quantas vezes a guarda RE-ARMOU neste boot — não quantas disparou. É este que `max_trips`
    /// limita: três re-armes seguidos deixam de ser "bati no muro" e viram defeito.
    uint8_t  trips;
    /// ⚠️ DISPAROS DE VERDADE, contados no instante em que a guarda começa a agir.
    ///
    /// `trips` só incrementa na RECUPERAÇÃO (estado OUT → NORMAL), e no modo travar essa transição
    /// nunca acontece: o estado vai direto para LOCKED. Resultado medido em 15/08/2026 — a guarda
    /// desarmou o motor com o volante a −496,5°, e o relatório imprimiu "disparou 0x neste boot"
    /// logo abaixo de mostrar o disparo. Um contador que não conta estraga o diagnóstico seguinte,
    /// que é justamente quando ele seria útil.
    uint8_t  disparos;
    // provas, para SWD e telemetria (só leitura)
    int32_t  last_pos_mrad;   ///< posição no disparo, em milirradianos
    int32_t  last_vel_mrad_s; ///< velocidade no disparo
    uint8_t  disarmed_by_us;  ///< 1 = fomos nós que desarmamos (não outra guarda)
} OvertravelState;

enum { OT_ST_NORMAL = 0, OT_ST_RECENTER = 1, OT_ST_OUT = 2, OT_ST_LOCKED = 3 };

static inline void overtravel_init(OvertravelState* s) {
    s->state = OT_ST_NORMAL;
    s->fuga_ticks = 0;
    s->vel_at_trip = 0.0f;
    s->trips = 0;
    s->disparos = 0;
    s->last_pos_mrad = 0;
    s->last_vel_mrad_s = 0;
    s->disarmed_by_us = 0;
}

static inline float ot_abs(float v) { return v < 0.0f ? -v : v; }

/// Um tick. `pos_rad` é a posição A PARTIR DO CENTRO (o MESMO zero do batente — se forem zeros
/// diferentes, a guarda mede um curso que não é o que a parede usa), `dor_half_rad` é o fim do
/// curso (DOR/2) e `vel_rad_s` a velocidade angular. Chamar a 1 kHz; `dt_ms` existe para o teste
/// poder andar mais rápido que tempo real.
///
/// HISTERESE: dispara em `dor_half + margem` e só se recupera em `dor_half`. Sem essa diferença o
/// volante pousado exatamente no limiar entraria e sairia de disparo a cada tick.
static inline OvertravelAction overtravel_update(OvertravelState* s, const OvertravelCfg* c,
                                                 float pos_rad, float dor_half_rad,
                                                 float vel_rad_s, uint16_t dt_ms) {
    const int   fora    = ot_abs(pos_rad) > (dor_half_rad + c->margin_rad);
    const int   dentro  = ot_abs(pos_rad) <= dor_half_rad;
    const int   parado  = ot_abs(vel_rad_s) <= c->stopped_rad_s;

    switch (s->state) {

    case OT_ST_NORMAL:
        if (!fora) return OT_ACT_NORMAL;
        // Guarda a prova ANTES de agir: depois de desarmar, a posição e a velocidade do instante
        // do disparo já não existem em lugar nenhum.
        s->last_pos_mrad   = (int32_t)(pos_rad   * 1000.0f);
        s->last_vel_mrad_s = (int32_t)(vel_rad_s * 1000.0f);
        s->vel_at_trip     = ot_abs(vel_rad_s);
        s->fuga_ticks      = 0;
        s->disparos        = (uint8_t)(s->disparos + 1);   // aqui, e não na recuperação
        s->state           = OT_ST_RECENTER;
        return OT_ACT_RECENTER;

    case OT_ST_RECENTER: {
        // VOLTOU AO CURSO: devolve o controle na hora, sem cerimônia e sem re-arme — o motor nunca
        // chegou a desarmar. É o comportamento que se quer numa corrida: passou do fim, a mola
        // trouxe de volta, a volta continua. A histerese evita entrar e sair a cada tick: dispara
        // em `dor_half + margem` e só solta em `dor_half`.
        if (dentro) {
            s->fuga_ticks = 0;
            s->state = OT_ST_NORMAL;
            return OT_ACT_NORMAL;
        }

        // FUGA — o ÚNICO caminho que ainda desarma. As duas condições juntas, e mantidas:
        // velocidade alta E ainda se afastando do ponto onde disparou, com a mola já aplicada.
        // Separadas, cada uma tem explicação inocente: velocidade alta sozinha é a mão empurrando
        // o aro contra o batente; afastar-se devagar sozinho é a mão vencendo a mola no braço.
        // Juntas, e por 400 ms, não têm — o motor está girando sozinho.
        const float pos_no_disparo = ot_abs((float)s->last_pos_mrad) * 0.001f;
        const int   fugindo = (ot_abs(vel_rad_s) >= c->fuga_rad_s) &&
                              (ot_abs(pos_rad)   >= pos_no_disparo + c->fuga_rad);
        if (!fugindo) {
            // Qualquer tick que não seja fuga ZERA o relógio. A contagem tem de ser contínua: uma
            // sequência de sustos somados não é uma fuga, e somá-los desarmaria por acúmulo.
            s->fuga_ticks = 0;
            return OT_ACT_RECENTER;
        }
        s->fuga_ticks = (uint16_t)(s->fuga_ticks + dt_ms);
        if (s->fuga_ticks >= c->fuga_ms) {
            // A mola está aplicada e o eixo acelera contra ela: o torque não está saindo no ângulo
            // certo — calibração ruim. Trava SEMPRE, ignorando o modo escolhido: re-armar sobre uma
            // calibração que não responde é repetir o disparo de propósito.
            s->disarmed_by_us = 1;
            s->state = OT_ST_LOCKED;
            return OT_ACT_DISARM;
        }
        return OT_ACT_RECENTER;
    }

    case OT_ST_OUT:
        // Desarmado, esperando a pessoa trazer o volante de volta. DUAS condições: dentro do curso
        // E parado. Só a primeira re-armaria com o aro ainda em movimento.
        // (Só se chega aqui pelo caminho de exceção — o freio não ter obedecido.)
        if (dentro && parado) {
            s->trips = (uint8_t)(s->trips + 1);
            if (s->trips >= c->max_trips) {
                // Três disparos seguidos não são mais "bati no muro" — são defeito.
                s->state = OT_ST_LOCKED;
                return OT_ACT_HOLD;
            }
            s->state = OT_ST_NORMAL;
            return OT_ACT_NORMAL;
        }
        return OT_ACT_HOLD;

    case OT_ST_LOCKED:
    default:
        return OT_ACT_HOLD;
    }
}

/// Padrões: 45° de margem além do fim do curso, e uma fuga só conta como fuga se durar 400 ms a
/// mais de 1,3 volta/s enquanto se afasta outros 57°. O modo continua travar por default — mas ele
/// só se aplica ao caminho de fuga, que é o único que ainda desarma.
static inline OvertravelCfg overtravel_default_cfg(void) {
    OvertravelCfg c;
    c.margin_rad    = 0.7853982f;   // 45°
    c.fuga_ms       = 400u;
    c.fuga_rad_s    = 8.0f;         // ~1,3 volta/s CONTRA a mola — braço nenhum sustenta
    c.fuga_rad      = 1.0f;         // e ainda ganhando 57° de distância
    c.stopped_rad_s = 0.5f;         // ~4,8 RPM — parado para efeito prático
    c.mode          = (uint8_t)OT_MODE_LOCK;
    c.max_trips     = 3u;
    return c;
}

#ifdef __cplusplus
}
#endif

#endif  // DRIVELAB_OVERTRAVEL_GUARD_H
