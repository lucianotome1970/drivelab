// ============================================================================
//  DriveLab
//  encoder_eccentricity.cpp — mede o quanto o ímã está fora de centro.
//  Ver o cabeçalho para o porquê. Aqui é só a matemática.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include "encoder_eccentricity.h"
#include <math.h>

volatile int32_t g_ecc_dist_mrad[ECC_MAX_AMOSTRAS] = {0};
volatile int32_t g_ecc_count[ECC_MAX_AMOSTRAS]     = {0};
volatile int32_t g_ecc_n = 0;
volatile int32_t g_ecc_intervalo_ms = 128;   // ver o header: é ele que decide a cobertura
volatile EccResultado g_ecc = {0, 0, 0, 0, 0};

/// Zera o buffer E o resultado anterior. Zerar o RESULTADO não é limpeza: é o sinal de "estou
/// medindo" que o app usa para distinguir uma medição nova da anterior. Sem isso, quem clica no
/// teste com uma medição já na telemetria receberia a velha de volta no mesmo instante — com cara
/// de resposta imediata, e sem nada ter sido medido.
void ecc_reset(void) {
    g_ecc_n = 0;
    g_ecc.valido = 0;
    g_ecc.cobertura_milivolta = 0;
}

// ---------------------------------------------------------------------------------------------
// A CONTA, e por que ela é simples.
//
// Durante a varredura o motor gira em velocidade constante de fase elétrica. Se o encoder fosse
// perfeito, a contagem cresceria em linha reta com a fase — e qualquer curvatura nessa reta é ERRO
// DE LEITURA, não do motor: o rotor está onde o campo manda, e o campo é comandado por nós.
//
// O erro de ímã descentrado tem forma conhecida: uma senoide com UM ciclo por volta mecânica. Como
// o período é conhecido, sobram dois números — amplitude e fase — e eles saem de um ajuste linear:
//
//     erro(θ) ≈ A·sen(θ) + B·cos(θ)      →  amplitude = √(A²+B²),  fase = atan2(B, A)
//
// Isso é projeção do erro nas duas componentes, e é exato para a primeira harmônica. O que NÃO for
// primeira harmônica sobra no resíduo — e é por isso que reportamos o resíduo junto: se ele for da
// ordem da amplitude, o defeito não é excentricidade e a mensagem não deve culpar o ímã.
//
// ⚠️ A reta de referência é ajustada aos próprios dados (mínimos quadrados), não assumida. A
// varredura começa e termina em pontos arbitrários, e uma reta chutada jogaria o próprio erro de
// chute dentro do resultado.
// ---------------------------------------------------------------------------------------------
void ecc_analisar(int32_t cpr, int32_t pole_pairs) {
    const int32_t n = g_ecc_n;
    g_ecc.valido = 0;
    g_ecc.amostras = n;
    if (n < 16 || cpr <= 0 || pole_pairs <= 0) return;

    // ⚠️ O QUE DECIDE SE DÁ PARA RESPONDER É A COBERTURA ANGULAR, NÃO O NÚMERO DE AMOSTRAS.
    //
    // Errei exatamente nisto na primeira versão: validei por ter 128 pontos — de sobra — e reportei
    // "ímã bem centrado, erro de 0,17°". Só que a varredura da calibração cobre **0,27 volta**, e o
    // erro de excentricidade tem período de UMA volta. Ajustar uma senoide a um quarto do ciclo é
    // mal condicionado: qualquer amplitude encaixa mudando a fase. O número era o ajuste de um
    // modelo a dados que não o determinam — e saiu com cara de medição.
    //
    // Uma ferramenta de diagnóstico que responde sem base é pior que uma que se cala: quem lê não
    // tem como saber que aquilo não é medição. Abaixo de 3/4 de volta, dizemos que não sabemos.
    const double cobertura_volta =
        (double)(g_ecc_dist_mrad[n - 1] - g_ecc_dist_mrad[0]) / 1000.0
        / (2.0 * M_PI * (double)pole_pairs);
    g_ecc.cobertura_milivolta = (int32_t)(cobertura_volta * 1000.0);
    if (cobertura_volta < 0.75) return;   // valido continua 0: "nao consigo medir com esta varredura"

    // Contagens que o encoder deveria andar por radiano elétrico.
    const double cont_por_rad_elec = (double)cpr / (2.0 * M_PI * (double)pole_pairs);

    // 1) reta de referência: contagem = a + b·distância  (mínimos quadrados)
    double sx = 0, sy = 0, sxx = 0, sxy = 0;
    for (int32_t i = 0; i < n; ++i) {
        const double x = g_ecc_dist_mrad[i] / 1000.0;   // rad elétricos
        const double y = (double)g_ecc_count[i];
        sx += x; sy += y; sxx += x * x; sxy += x * y;
    }
    const double det = n * sxx - sx * sx;
    if (fabs(det) < 1e-9) return;
    const double b = (n * sxy - sx * sy) / det;
    const double a = (sy - b * sx) / n;

    // 2) projeta o resíduo nas componentes de UMA volta mecânica.
    //    ângulo mecânico = fase elétrica / pole_pairs
    double sA = 0, sB = 0, soma_quad = 0;
    for (int32_t i = 0; i < n; ++i) {
        const double x   = g_ecc_dist_mrad[i] / 1000.0;
        const double res = (double)g_ecc_count[i] - (a + b * x);   // erro em contagens
        const double th  = x / (double)pole_pairs;                 // rad mecânicos
        sA += res * sin(th);
        sB += res * cos(th);
        soma_quad += res * res;
    }
    const double A = 2.0 * sA / n;
    const double B = 2.0 * sB / n;
    const double amp_cont = sqrt(A * A + B * B);        // amplitude em contagens
    double fase = atan2(B, A);                          // rad mecânicos
    if (fase < 0) fase += 2.0 * M_PI;

    // 3) o que a senoide NÃO explica. Se sobrar tanto quanto a amplitude, não é excentricidade.
    double resto = 0;
    for (int32_t i = 0; i < n; ++i) {
        const double x   = g_ecc_dist_mrad[i] / 1000.0;
        const double res = (double)g_ecc_count[i] - (a + b * x);
        const double th  = x / (double)pole_pairs;
        const double mod = A * sin(th) + B * cos(th);
        resto += (res - mod) * (res - mod);
    }
    resto = sqrt(resto / n);

    // Contagens → graus mecânicos (o número que a pessoa entende e usa para empurrar o sensor).
    const double cont_por_grau = (double)cpr / 360.0;
    g_ecc.amplitude_cdeg = (int32_t)(amp_cont / cont_por_grau * 100.0);
    g_ecc.residuo_cdeg   = (int32_t)(resto    / cont_por_grau * 100.0);
    g_ecc.fase_cdeg      = (int32_t)(fase * 180.0 / M_PI * 100.0);
    g_ecc.valido         = 1;

    // ⚠️ A ORDEM DE JULGAR IMPORTA, e errei nela na primeira versão: comparava a AMPLITUDE com um
    // limiar e dizia "bem centrado" quando ela era pequena — mesmo com o resíduo cinco vezes maior.
    // Medido em 15/08/2026: amplitude 0,17° e resíduo 0,86°. A mensagem saiu "ímã bem centrado"
    // quando o correto era "há quase 1° de erro e ele NÃO é excentricidade".
    //
    // O resíduo vem primeiro porque ele responde a pergunta anterior: a senoide DESCREVE este erro?
    // Se não descreve, a amplitude dela não significa nada — é o ajuste de um modelo que não serve.
    // Culpar o ímã com base num modelo que não explica os dados é dar número a um palpite.
    (void)0;
    (void)cont_por_rad_elec;   // deixado à vista: é a conversão que justifica a inclinação `b`
}
