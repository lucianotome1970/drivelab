// ============================================================================
//  DriveLab Firmware
//  apply_cfg.h — Ponte PURA: mapeia BaseCfg (settings) → FfbEngine (cérebro).
//  Header-only, sem Arduino; host-testável junto com lib/brain.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Único ponto de tradução entre "o que o usuário ajusta" (BaseCfg, 0..100/0..200
// etc.) e "o que o motor de força entende" (Nm, rad, coeficientes de biquad).
// As constantes de escala abaixo são o "quanto é 100%" de cada efeito — chutes
// de bancada, a validar com o hardware real (ver comentário em cada uma).
// NÃO incluir este header em base_cfg.h: base_shared precisa continuar
// utilizável sem puxar o cérebro inteiro (ex.: m05, que só fala P0).
#pragma once

#include "base_cfg.h"
#include "../brain/ffb_engine.h"

namespace drivelab {

// Escalas "0..100% do setting" → grandeza física do efeito. AJUSTAR na bancada.
static constexpr float kSpringMaxNmPerRad = 2.0f;   // spring 100% → 2 Nm/rad
static constexpr float kDamperMax         = 0.5f;   // damper 100% → 0.5 Nm/(rad/s)
static constexpr float kFrictionMaxNm     = 1.0f;   // friction 100% → 1.0 Nm
static constexpr float kEndstopDampMax    = 0.5f;   // endstop damping 100% → 0.5 Nm/(rad/s)
static constexpr float kGameForceHz       = 60.0f;  // taxa típica de FFB do jogo (p/ steps=auto)
static constexpr float kSlewMaxNmPerStep  = 0.5f;   // slew rate 100% → 0.5 Nm/step. AJUSTAR na bancada.
// Tensão do barramento: o usuário escolhe a NOMINAL (fonte) e derivamos a janela segura.
static constexpr float kBusMinFraction    = 0.70f;  // motor não liga abaixo de 70% da nominal (tolera sag)
static constexpr float kBusOverFraction   = 1.08f;  // corte de sobretensão a 108% da nominal (headroom p/ regen)
static constexpr float kBusHardCeilingV   = 60.0f;  // TETO ABSOLUTO do hardware (FETs da placa 56V) — nunca passar

/// Aplica todos os settings relacionados a força de `c` em `e` (config, sem tocar em estado
/// dinâmico do motor). `loopHz` é a taxa do laço de torque (usada p/ steps=auto e p/ o biquad).
/// Pura: sem I/O, sem Arduino — chamável do host e do firmware igualmente.
inline void applyCfgToEngine(const BaseCfg& c, FfbEngine& e, float loopHz) {
    e.force.totalStrengthPct = c.totalStrength;
    e.force.torqueLimitNm    = (c.maxTorqueLimit / 100.0f) * e.force.maxTorqueNm;
    e.force.direction        = c.forceDirection >= 0 ? 1.0f : -1.0f;
    e.force.linearity        = c.linearity / 100.0f;
    for (int i = 0; i < 5; ++i) e.force.curve.p[i] = c.ffbCurve[i];   // curva de resposta por pontos

    e.effect.springNmPerRad       = (c.springStrength / 100.0f) * kSpringMaxNmPerRad;
    e.effect.damperNmPerRadPerSec = (c.damperStrength / 100.0f) * kDamperMax;
    e.effect.frictionNm           = (c.staticDamping / 100.0f) * kFrictionMaxNm;

    e.endstop.rangeRad              = (c.motionRange * 0.5f) * 0.01745329f; // graus→rad, meia-faixa
    e.endstop.stiffnessNm           = (c.softStopStrength / 100.0f) * 3.0f;
    e.endstop.dampingNmPerRadPerSec = (c.endstopDamping / 100.0f) * kEndstopDampMax;

    e.reconstructor.cfg.steps = c.reconstructionSteps > 0
        ? static_cast<int>(c.reconstructionSteps)
        : static_cast<int>(loopHz / kGameForceHz + 0.5f);
    if (e.reconstructor.cfg.steps < 1) e.reconstructor.cfg.steps = 1;
    e.reconstructor.cfg.lpfAlpha = c.reconstructionLpf / 100.0f;

    e.outputFilter = c.outputFilterHz > 0
        ? makeLowPass(static_cast<float>(c.outputFilterHz), loopHz, 0.707f)
        : Biquad{}; // identidade (b0=1, resto=0 → passa-tudo)

    e.oscGuardEnabled = c.oscGuardEnable != 0;

    e.maxSlewNmPerStep = (c.slewRate / 100.0f) * kSlewMaxNmPerStep;

    // cogging: apenas o on/off do setting; a tabela em si vem da calibração de bancada
    // (fora deste sub-projeto) — por isso `e.cogging` fica nullptr aqui mesmo com
    // coggingEnable=1. Quem carregar a tabela decide se/quando atribuir e.cogging.
    (void)c.coggingEnable;

    // Janela de tensão do barramento derivada da nominal escolhida pelo usuário (fonte 24/36/48/56V…):
    // busMin permite sag (motor não liga abaixo dela); a sobretensão é o corte duro (regen), SEMPRE limitado
    // ao teto do hardware (FETs). Assim a tensão é escolha do usuário, não mais hardcode.
    const float nominal = static_cast<float>(c.busNominalV);
    e.startup.cfg.busMinV = nominal * kBusMinFraction;
    const float over = nominal * kBusOverFraction;
    e.guard.overVoltageV  = over < kBusHardCeilingV ? over : kBusHardCeilingV;
    e.startup.cfg.busMaxV = e.guard.overVoltageV;
}

}  // namespace drivelab
