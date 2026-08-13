// ============================================================================
//  DriveLab
//  bringup_lock.h — Decide se a trava "Ativar motor" volta a ZERO no boot.
//
//  PARA QUE SERVE: a trava (setting 45) faz a base subir desarmada até a pessoa
//  conferir os campos de hardware. Só que os settings SOBREVIVEM à gravação do
//  firmware (é de propósito: senão o batente acertado na pista se perdia a cada
//  atualização). Consequência indesejada: quem já ativou o motor uma vez e
//  salvou passa a receber TODA atualização de firmware com a base armando
//  sozinha — exatamente a situação que a trava existe para evitar, porque
//  firmware novo é justamente quando a configuração de hardware pode não bater.
//
//  A REGRA: guardamos junto dos settings a identidade do firmware que gravou.
//  Se no boot ela for diferente da do binário que está rodando, a trava volta a
//  zero — uma vez, só naquele primeiro boot. Depois disso a placa se comporta
//  como sempre: liga, arma e roda, sem clique nenhum no dia a dia.
//
//  Lógica pura (sem STM32, sem flash) para poder rodar no PC.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#ifndef DRIVELAB_BRINGUP_LOCK_H
#define DRIVELAB_BRINGUP_LOCK_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    int32_t  motor_enable;   // valor que a trava deve assumir agora
    uint32_t build_id;       // identidade a guardar junto dos settings
    uint8_t  relocked;       // 1 = travou agora (o chamador deve persistir)
} BringupLockDecision;

// stored_build_id: o que veio da NVM (0 quando o blob é de firmware anterior a este campo).
// current_build_id: a identidade deste binário (DRVLAB_BUILD_ID).
// stored_motor_enable: a trava como veio da NVM.
static inline BringupLockDecision bringup_lock_decide(uint32_t stored_build_id,
                                                      uint32_t current_build_id,
                                                      int32_t  stored_motor_enable) {
    BringupLockDecision d;
    d.build_id = current_build_id;

    if (stored_build_id == current_build_id) {
        // Mesmo firmware de antes: respeita o que a pessoa salvou.
        d.motor_enable = stored_motor_enable;
        d.relocked     = 0;
        return d;
    }

    // Firmware diferente (ou blob antigo, sem o campo). Trava.
    d.motor_enable = 0;
    // Só vale gravar se havia o que travar: se já estava em 0, o único motivo de
    // escrever seria registrar a nova identidade — e isso pode esperar o próximo
    // save do usuário, em vez de gastar um ciclo de flash a cada atualização.
    d.relocked     = (stored_motor_enable != 0) ? 1u : 0u;
    return d;
}

#ifdef __cplusplus
}
#endif

#endif  // DRIVELAB_BRINGUP_LOCK_H
