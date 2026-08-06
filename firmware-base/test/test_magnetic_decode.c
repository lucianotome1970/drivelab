// ============================================================================
//  DriveLab
//  test_magnetic_decode.c — Testes de host da decodificacao dos encoders
//  magneticos absolutos.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/magnetic_decode.h"
#include <stdio.h>

static int falhas = 0;

static void check(int cond, const char* nome) {
    if (cond) { printf("  ok     %s\n", nome); }
    else      { printf("  FALHOU %s\n", nome); falhas++; }
}

// Monta um quadro de 24 bits com CRC correto (pela nossa propria funcao).
static uint32_t frame(uint16_t angle, uint8_t status) {
    const uint32_t data18 = ((uint32_t)(angle & 0x3FFFu) << 4) | (status & 0x0Fu);
    return (data18 << 6) | mt6701_crc6(data18);
}

int main(void) {
    // --- MT6701: campos do quadro -------------------------------------------
    {
        Mt6701Frame f;
        mt6701_decode(frame(12345u, 0u), &f);
        check(f.angle == 12345u, "extrai o angulo de 14 bits");
        check(f.crc_ok == 1u,    "CRC confere num quadro bem formado");

        mt6701_decode(frame(0u, 0u), &f);
        check(f.angle == 0u, "angulo zero");
        mt6701_decode(frame(16383u, 0u), &f);
        check(f.angle == 16383u, "angulo maximo (16383)");
    }

    // --- MT6701: bits de status ---------------------------------------------
    {
        Mt6701Frame f;
        mt6701_decode(frame(100u, 0x04u), &f);
        check(f.button == 1u && f.track_loss == 0u, "bit de botao pressionado");

        mt6701_decode(frame(100u, 0x08u), &f);
        check(f.track_loss == 1u && f.button == 0u, "bit de perda de rastreio");

        mt6701_decode(frame(100u, 0x03u), &f);
        check(f.field == 3u, "intensidade do campo nos 2 bits baixos");
    }

    // --- MT6701: o CRC detecta corrupcao ------------------------------------
    // Nao prova conformidade com o chip (ver o aviso no header), prova que a
    // funcao pega erro de bit — que e o que se espera dela.
    {
        Mt6701Frame f;
        uint32_t bom = frame(9000u, 0u);
        mt6701_decode(bom, &f);
        check(f.crc_ok == 1u, "quadro integro passa");

        int pegou = 0;
        for (int bit = 6; bit < 24; bit++) {          // corrompe cada bit de dados
            mt6701_decode(bom ^ (1u << bit), &f);
            if (f.crc_ok == 0u) pegou++;
        }
        check(pegou == 18, "CRC detecta erro em qualquer um dos 18 bits de dados");
    }

    // --- Diferenca angular com wrap -----------------------------------------
    {
        const uint32_t CPR = MT6701_CPR;
        check(angle_delta_wrapped(100u, 200u, CPR) == 100,  "avanco simples");
        check(angle_delta_wrapped(200u, 100u, CPR) == -100, "recuo simples");

        // A passagem 16383 -> 0 e UM passo, nao uma volta inteira para tras.
        check(angle_delta_wrapped(16383u, 0u, CPR) == 1,  "wrap para frente da +1");
        check(angle_delta_wrapped(0u, 16383u, CPR) == -1, "wrap para tras da -1");

        // Sem o wrap isto seria -16283 e viraria um pico de velocidade absurdo.
        check(angle_delta_wrapped(16300u, 17u, CPR) == 101, "wrap com salto maior");

        check(angle_delta_wrapped(0u, 8192u, CPR) == 8192, "meia volta fica no limite positivo");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas);
    else        printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
