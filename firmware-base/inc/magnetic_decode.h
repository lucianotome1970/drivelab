// ============================================================================
//  DriveLab
//  magnetic_decode.h — Decodificacao das palavras dos encoders magneticos
//  absolutos. Logica PURA (sem STM32, sem SPI): roda igual no firmware e num
//  alvo de teste no PC.
//
//  DIVISAO DE TRABALHO: o encanamento SPI (transacao por DMA, chip select) ja
//  existe no ODrive vendorizado e continua la. Aqui fica so a parte que muda de
//  sensor para sensor — como a palavra recebida vira angulo — que e justamente
//  a parte que da para escrever e testar sem hardware.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_MAGNETIC_DECODE_H
#define DRIVELAB_MAGNETIC_DECODE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// ---------------------------------------------------------------------------
// MT6701 — interface SSI
//
// Quadro de 24 bits, MSB primeiro:
//   [23:10] angulo, 14 bits  (0..16383 = uma volta mecanica)
//   [ 9: 6] status do campo, 4 bits
//   [ 5: 0] CRC-6, polinomio x^6 + x + 1
//
// Bits de status (confirmados contra driver de referencia):
//   bit0..1 : intensidade do campo
//   bit2    : botao pressionado (a peca tem funcao de push)
//   bit3    : perda de rastreio
// ---------------------------------------------------------------------------

#define MT6701_CPR 16384u   // contagens por volta (14 bits)

typedef struct {
    uint16_t angle;        // 0..16383
    uint8_t  field;        // intensidade do campo (0..3)
    uint8_t  button;       // 1 = pressionado
    uint8_t  track_loss;   // 1 = perdeu o rastreio do ima
    uint8_t  crc_ok;       // 1 = CRC conferiu
} Mt6701Frame;

// CRC-6 do MT6701: polinomio x^6 + x + 1 (0x03 nos 6 bits baixos), MSB primeiro,
// registrador iniciando em zero, calculado sobre os 18 bits de dados
// (14 de angulo + 4 de status).
//
// ⚠️ NAO VALIDADO CONTRA O CHIP. O datasheet da o polinomio mas nao um vetor de
// teste, e o driver de referencia publico simplesmente nao implementa o CRC.
// Os testes aqui provam que a funcao e auto-consistente e que ela DETECTA erro
// de bit — nao que ela concorda com o silicio. Por isso o campo crc_ok e
// informativo: no bring-up, use o angulo mesmo com crc_ok=0 e confira contra a
// realidade. Se todo quadro vier com crc_ok=0 e o angulo estiver coerente, o
// errado aqui e o CRC, nao o sensor.
static inline uint8_t mt6701_crc6(uint32_t data18) {
    uint8_t crc = 0u;
    for (int i = 17; i >= 0; i--) {
        const uint8_t bit = (uint8_t)((data18 >> i) & 1u);
        const uint8_t top = (uint8_t)((crc >> 5) & 1u);
        crc = (uint8_t)((crc << 1) & 0x3Fu);
        if (top ^ bit) crc ^= 0x03u;
    }
    return crc;
}

static inline void mt6701_decode(uint32_t frame24, Mt6701Frame* out) {
    const uint16_t angle  = (uint16_t)((frame24 >> 10) & 0x3FFFu);
    const uint8_t  status = (uint8_t)((frame24 >> 6) & 0x0Fu);
    const uint8_t  crc_rx = (uint8_t)(frame24 & 0x3Fu);

    out->angle      = angle;
    out->field      = (uint8_t)(status & 0x03u);
    out->button     = (uint8_t)((status & 0x04u) ? 1u : 0u);
    out->track_loss = (uint8_t)((status & 0x08u) ? 1u : 0u);
    out->crc_ok     = (uint8_t)(mt6701_crc6((frame24 >> 6) & 0x3FFFFu) == crc_rx ? 1u : 0u);
}

// ---------------------------------------------------------------------------
// Diferenca angular com wrap
//
// POR QUE EXISTE: a velocidade e DERIVADA da posicao. Na passagem de 16383 para
// 0 a subtracao crua da um salto de uma volta inteira, e a derivada explode —
// vira um pico de velocidade que o FFB interpreta como movimento brutal. Esta
// funcao devolve o menor caminho entre duas leituras, no intervalo
// (-cpr/2, +cpr/2].
// ---------------------------------------------------------------------------
static inline int32_t angle_delta_wrapped(uint32_t prev, uint32_t curr, uint32_t cpr) {
    int32_t d = (int32_t)curr - (int32_t)prev;
    const int32_t half = (int32_t)(cpr / 2u);
    while (d >  half) d -= (int32_t)cpr;
    while (d < -half) d += (int32_t)cpr;
    return d;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_MAGNETIC_DECODE_H
