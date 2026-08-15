// ============================================================================
//  DriveLab
//  encoder_eccentricity.h — "o ímã está torto, e por quantos graus"
//
//  O PROBLEMA QUE ISTO RESOLVE, e ele é de quem monta, não nosso:
//  a pessoa monta a base, calibra, nenhum erro aparece — e o volante treme.
//  Não há como ela saber se é o ímã, a fiação, o CPR, a calibração ou o motor.
//  Passamos um dia inteiro nisso em 15/08/2026, com SWD e osciloscópio de
//  software à disposição. Quem monta em casa não tem chance.
//
//  A IDEIA (do usuário, 15/08/2026): em vez de CORRIGIR o erro por software,
//  MEDIR e DIZER. "Ímã descentrado, ±7,2°, máximo aos 210°" não só nomeia o
//  defeito como diz para que lado empurrar o sensor. E quando está tudo bem,
//  informa também — metade do valor de um diagnóstico é descartar hipóteses.
//
//  A RÉGUA É O MOTOR. Com 15 pares de polos, fixar a fase elétrica trava o rotor
//  numa posição FÍSICA determinada pela geometria do ferro — não pelo encoder.
//  A calibração de offset já varre essa fase de ponta a ponta, medindo o encoder
//  a cada milissegundo. Ou seja: a medição que precisamos JÁ ACONTECE em toda
//  calibração; só ninguém guardava os números.
//
//  ⚠️ POR QUE NÃO FAZEMOS UMA VARREDURA PRÓPRIA: tentamos, em 15/08/2026, e ela
//  TRAVOU A BASE. O `wait_for_control_iteration()` copiado da calibração espera
//  um evento do laço de controle que só existe no contexto do eixo — chamado do
//  laço de FFB, ele nunca retorna. Aproveitar a varredura que já roda no lugar
//  certo é mais simples E mais seguro.
//
//  O QUE O ERRO PARECE: ímã descentrado produz erro SENOIDAL com um ciclo por
//  volta mecânica — adianta num setor, atrasa no oposto. Como o período é
//  conhecido, bastam dois números para descrevê-lo: amplitude e fase. Por isso
//  meia volta de varredura já basta para ajustá-los.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#ifndef DRIVELAB_ENCODER_ECCENTRICITY_H
#define DRIVELAB_ENCODER_ECCENTRICITY_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/// Quantas amostras a varredura guarda. A calibração dura ~8 s a 1 kHz = 8000 pontos; guardamos
/// 128 bem distribuídos, que é de sobra para ajustar uma senoide de período conhecido e cabe em 1 KB.
#define ECC_MAX_AMOSTRAS 128

/// Coletado DURANTE a calibração de offset (encoder.cpp). Só escrita ali, só leitura aqui.
extern volatile int32_t  g_ecc_dist_mrad[ECC_MAX_AMOSTRAS];  // fase elétrica andada, em milirradianos
extern volatile int32_t  g_ecc_count[ECC_MAX_AMOSTRAS];      // o que o encoder leu no mesmo instante
extern volatile int32_t  g_ecc_n;                            // quantas amostras válidas

/// De quantos em quantos ticks de 1 ms guardar uma amostra. NÃO é detalhe: com 128 posições no
/// buffer, o intervalo decide QUANTO da volta cabe — e é a cobertura que determina se a medição
/// pode ser feita. Com 32 ms o buffer enchia em 4 s e a varredura longa era truncada em 0,27 volta,
/// exatamente o problema que o teste existe para resolver (medido em 15/08/2026).
/// A calibração normal dura ~8 s e o teste ~15 s por sentido; 128 ms cobre os dois com folga.
extern volatile int32_t  g_ecc_intervalo_ms;

/// Resultado da análise. Preenchido por ecc_analisar(), legível por SWD e pela telemetria.
typedef struct {
    int32_t valido;        // 0 = sem dados suficientes (calibração não rodou, ou foi curta demais)
    int32_t amplitude_cdeg;// amplitude do erro senoidal, em CENTÉSIMOS de grau mecânico
    int32_t fase_cdeg;     // onde o erro é máximo, em centésimos de grau mecânico (0..35999)
    int32_t residuo_cdeg;  // o que sobra depois de tirar a senoide — se for grande, não é excentricidade
    int32_t amostras;      // quantas amostras entraram na conta
    // Quanto da volta a varredura percorreu, em milésimos. É ISTO que decide se a pergunta pode ser
    // respondida: o erro de excentricidade tem período de uma volta, e ajustar a senoide a um
    // pedaço pequeno dá um número que parece medição e não é. Abaixo de 750 (0,75 volta), `valido`
    // fica 0 e não reportamos amplitude nenhuma.
    int32_t cobertura_milivolta;
} EccResultado;

extern volatile EccResultado g_ecc;

/// Ajusta a senoide às amostras colhidas e preenche g_ecc. Chamar DEPOIS da calibração terminar,
/// do laço de FFB (é matemática pura, não toca no motor nem no controle).
/// `cpr` e `pole_pairs` vêm da configuração viva; sem eles não há como converter contagem em graus.
void ecc_analisar(int32_t cpr, int32_t pole_pairs);

/// Zera o buffer. Chamado no início de cada calibração para não misturar varreduras.
void ecc_reset(void);

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_ENCODER_ECCENTRICITY_H
