// ============================================================================
//  DriveLab
//  test_bringup_lock.c — A trava "Ativar motor" volta a zero quando o firmware
//  muda, e SÓ quando muda.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include <stdio.h>
#include "../inc/bringup_lock.h"

static int falhas = 0;

static void ok(const char* nome, int cond) {
    printf("  %s     %s\n", cond ? "ok" : "FALHOU", nome);
    if (!cond) falhas++;
}

int main(void) {
    printf("== test_bringup_lock.c\n");

    // Mesmo firmware: o que a pessoa salvou vale. É o dia a dia — liga e roda.
    {
        BringupLockDecision d = bringup_lock_decide(0xABCD1234u, 0xABCD1234u, 1);
        ok("mesmo firmware mantem a trava ligada", d.motor_enable == 1);
        ok("mesmo firmware nao grava flash", d.relocked == 0);
    }

    // Firmware diferente com o motor ativado: trava e pede para persistir.
    {
        BringupLockDecision d = bringup_lock_decide(0xABCD1234u, 0x99887766u, 1);
        ok("firmware novo desativa o motor", d.motor_enable == 0);
        ok("firmware novo pede save", d.relocked == 1);
        ok("guarda a identidade nova", d.build_id == 0x99887766u);
    }

    // Firmware diferente mas o motor já estava desativado: nada a fazer, e
    // principalmente nada a gravar — flash tem ciclos contados.
    {
        BringupLockDecision d = bringup_lock_decide(0xABCD1234u, 0x99887766u, 0);
        ok("ja desativado continua desativado", d.motor_enable == 0);
        ok("ja desativado nao gasta ciclo de flash", d.relocked == 0);
    }

    // Blob de firmware anterior a este campo: chega com id 0 e conta como
    // "firmware diferente" — que é o comportamento seguro.
    {
        BringupLockDecision d = bringup_lock_decide(0u, 0x99887766u, 1);
        ok("blob antigo (id 0) trava", d.motor_enable == 0);
        ok("blob antigo pede save", d.relocked == 1);
    }

    // Um id 0 no binário seria degenerado (bateria com blob antigo e nunca
    // travaria); o Makefile nunca gera 0, mas a regra continua consistente.
    {
        BringupLockDecision d = bringup_lock_decide(0u, 0u, 1);
        ok("ids iguais respeitam o salvo, mesmo em zero", d.motor_enable == 1);
    }

    printf(falhas ? "\nFALHAS: %d\n" : "\nTUDO OK\n", falhas);
    return falhas ? 1 : 0;
}
