// ============================================================================
//  DriveLab
//  test_motor_config.c — Testes de host da traducao dos settings de motor.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/motor_config.h"
#include <stdio.h>

static int falhas = 0;
static void check(int c, const char* n) {
    if (c) printf("  ok     %s\n", n); else { printf("  FALHOU %s\n", n); falhas++; }
}

int main(void) {
    // 1) A GARANTIA: com os padroes sai o que o firmware ja fazia — 15 pares (hoverboard) e
    //    5 A de calibracao (o valor efetivo depois do relax_calibration).
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, 0.0f);
        check(c.pole_pairs == 15,            "padrao = 15 pares (o de hoje)");
        check(c.calib_current_a == 5.0f,     "padrao = 5 A de calibracao (o de hoje)");
    }

    // 2) Outro motor: 20 pares e 8 A passam adiante
    {
        MotorConfig c = motor_config_from_settings(20, 8, 0, 52.0f, 25.0f, 0.0f);
        check(c.pole_pairs == 20,        "aplica outro numero de pares");
        check(c.calib_current_a == 8.0f, "aplica outra corrente");
    }

    // 3) Zero NUNCA vira configuracao. Zero pares de polos ou zero corrente de calibracao sao
    //    configuracoes sem sentido: o chamador trata zero como "nao informado" e nao aplica.
    {
        MotorConfig c = motor_config_from_settings(0, 0, 0, 52.0f, 25.0f, 0.0f);
        check(c.pole_pairs == 0,        "zero pares = nao informado (chamador nao aplica)");
        check(c.calib_current_a == 0.0f, "zero corrente = nao informado");
    }

    // 4) TETO: calibrar acima do limite de corrente do motor nao pode acontecer. Caso REAL da
    //    bancada em 2026-08-10 — 30 A salvos na flash, current_lim de 25 A. Enquanto o setting era
    //    ignorado o valor dormia; ao passar a ser aplicado, ele energizaria o motor acima do limite.
    {
        MotorConfig c = motor_config_from_settings(15, 30, 0, 52.0f, 25.0f, 0.0f);
        check(c.calib_current_a == 25.0f, "30 A com limite de 25 A -> limitado a 25");
    }
    {
        MotorConfig c = motor_config_from_settings(15, 20, 0, 52.0f, 25.0f, 0.0f);
        check(c.calib_current_a == 20.0f, "abaixo do limite passa intacto");
    }
    {
        MotorConfig c = motor_config_from_settings(15, 25, 0, 52.0f, 25.0f, 0.0f);
        check(c.calib_current_a == 25.0f, "exatamente no limite passa intacto");
    }
    // Limite invalido (0/negativo) nao pode ZERAR a corrente — zero significa "nao informado" e
    // deixaria a calibracao sem corrente nenhuma.
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 0.0f, 0.0f);
        check(c.calib_current_a == 5.0f, "limite invalido nao mexe na corrente");
    }

    // 5) LIMITE DE CORRENTE DO MOTOR — deixou de ser cravado em 25 A (o valor desta bancada).
    //    Teto fisico = alcance do sensor menos a margem (na placa: 60 - 8 = 52 A). Acima disso a
    //    leitura de corrente satura, e medicao cega num laco de corrente e o caminho para corrente
    //    no lugar errado.
    {
        MotorConfig c = motor_config_from_settings(15, 5, 25, 52.0f, 25.0f, 0.0f);
        check(c.current_lim_a == 25.0f, "25 A (o de hoje) passa intacto");
    }
    {
        MotorConfig c = motor_config_from_settings(15, 5, 200, 52.0f, 25.0f, 0.0f);
        check(c.current_lim_a == 52.0f, "acima do alcance do sensor -> limitado ao teto");
    }
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, 0.0f);
        check(c.current_lim_a == 0.0f,  "zero = nao informado (chamador nao aplica)");
    }

    // 6) Baixar o limite do motor ARRASTA a corrente de calibracao junto. Sem isso, escolher um
    //    motor menor (10 A) deixaria a calibracao energizando com os 30 A da flash — exatamente o
    //    perigo que o teto existe para impedir, e que reapareceria pela porta dos fundos.
    {
        MotorConfig c = motor_config_from_settings(15, 30, 10, 52.0f, 25.0f, 0.0f);
        check(c.current_lim_a  == 10.0f, "novo limite de 10 A aplicado");
        check(c.calib_current_a == 10.0f, "calibracao segue o limite NOVO, nao o antigo");
    }
    // Sem limite novo informado, o teto da calibracao continua sendo o limite vigente.
    {
        MotorConfig c = motor_config_from_settings(15, 30, 0, 52.0f, 25.0f, 0.0f);
        check(c.calib_current_a == 25.0f, "sem limite novo, calibracao usa o limite vigente");
    }

    // 7) CONSTANTE DE TORQUE (Kt) — o cambio Nm -> amperes, que era cravado em 0,55 de catalogo.
    //    A GARANTIA vem primeiro: quem nao mediu (campo em 0, como sai de fabrica) tem exatamente
    //    o comportamento de antes deste campo existir.
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, 0.0f);
        check(c.torque_constant == 0.0f, "Kt nao medido (0) = nao aplicar, fica o padrao");
    }
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, 0.42f);
        check(c.torque_constant == 0.42f, "Kt medido passa adiante");
    }
    // O 0,55 de catalogo continua valendo se for digitado — nao ha nada de especial nele.
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, 0.55f);
        check(c.torque_constant == 0.55f, "o proprio 0,55 de hoje passa intacto");
    }
    // FAIXA DE SANIDADE. O erro perigoso e o Kt PEQUENO demais: para o mesmo torque pedido o
    // firmware manda corrente na proporcao inversa, e o calor cresce com o QUADRADO dela. Um zero a
    // menos numa digitacao (0,005 no lugar de 0,05) e o tipo de engano que ninguem percebe na tela.
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, 0.005f);
        check(c.torque_constant == 0.0f, "Kt absurdamente baixo = engano, ignora");
    }
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, 50.0f);
        check(c.torque_constant == 0.0f, "Kt absurdamente alto = engano, ignora");
    }
    // Negativo nao existe em Kt e nao pode virar torque invertido pela porta dos fundos.
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, -0.5f);
        check(c.torque_constant == 0.0f, "Kt negativo = engano, ignora");
    }
    // As bordas da faixa sao validas — quem tem um motor de verdade nelas nao pode ser barrado.
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, MOTOR_CONFIG_KT_MIN);
        check(c.torque_constant == MOTOR_CONFIG_KT_MIN, "borda inferior da faixa e valida");
    }
    {
        MotorConfig c = motor_config_from_settings(15, 5, 0, 52.0f, 25.0f, MOTOR_CONFIG_KT_MAX);
        check(c.torque_constant == MOTOR_CONFIG_KT_MAX, "borda superior da faixa e valida");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas); else printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
