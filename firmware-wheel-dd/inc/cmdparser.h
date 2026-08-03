// firmware-wheel-dd — stub do cmdparser (MIT). NÃO usamos os comandos do OpenFFBoard;
// cmdparser_feed sempre retorna 0 → o ascii_protocol.cpp cai no switch ASCII do ODrive.
#pragma once
#include <stdint.h>
#include <stddef.h>
#ifdef __cplusplus
extern "C" {
#endif
size_t cmdparser_feed(const uint8_t *in, size_t in_len, char *out, size_t out_len);
int    cmdparser_cdc_write(const uint8_t *buf, uint32_t len);
#ifdef __cplusplus
}
#endif
