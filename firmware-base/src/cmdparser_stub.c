// firmware-base — stub MIT do cmdparser (não usamos os comandos OpenFFBoard).
#include "cmdparser.h"
size_t cmdparser_feed(const uint8_t *in, size_t in_len, char *out, size_t out_len) {
    (void)in; (void)in_len; (void)out; (void)out_len;
    return 0;   // não-tratado → ascii_protocol usa o switch do ODrive
}
int cmdparser_cdc_write(const uint8_t *buf, uint32_t len) { (void)buf; return (int)len; }
