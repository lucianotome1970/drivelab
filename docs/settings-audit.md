# DriveLab — Auditoria de Settings (app ↔ firmware)

> Cruza os 45 settings do schema (`app/DriveLab.Core/Settings/BaseSettingsSchema.cs`) com o que o firmware
> **realmente aplica** (`firmware-wheel-dd/src/a0_channel.cpp::a0_apply_settings` + `odrive_bridge_apply_hw_profile`).
> **Princípio:** todo setting tem um **default pré-definido** (funciona out-of-box); o usuário só altera se quiser.

## Enviar/ler funciona pra TODOS os 45
O canal A0 recebe (SETWRITE), guarda em RAM e lê de volta (SETREAD→0x16) os 45. **O gap não é o envio** — é o
firmware **AGIR** sobre o valor. Hoje só **~11 aplicam**; o resto é controle "fantasma" (mexe mas não age ainda).

## ✅ APLICADOS (agem no motor/FFB) — 11
`motion_range(0)` · `total_strength(3)` · `spring_strength(4)` · `damper_strength(5)` · `max_torque_limit(7)` ·
`force_direction(8)` · `board_variant(33)` ✨ · `spring/damper/friction/inertia_gain(39-42)`

## 🟡 PARCIAL / RESERVADO — 2
- `motion_range(0)`: aplica no endstop, mas o **eixo do joystick usa 1.5 volta hardcoded** (`ffb_hid.cpp kSteerRangeTurns`) → a DOR não escala o eixo. **[P1 — DOR real]**
- `bus_nominal_v(27)`: reservado (display; trips relativos ao nominal = follow-up)

## ⚪ GUARDADO — código PRONTO, só LIGAR — **[P0 quick-win]**
Matemática existe no `ffb_math.h`, falta conectar: `static_damping(6)` · `reconstruction_steps/lpf(19,20)` ·
`endstop_damping(23)` · `linearity(24)` · `slew_rate(26)` · `ffb_curve_0..4(28-32)`

## 🔧 GUARDADO — precisa BANCADA (config de motor/hw, hoje hardcoded no `newboard_bringup`) — **[P4/Fase 2]**
`encoder_direction(9)` · `encoder_cpr(10)` · `pole_pairs(11)` · `current_p/i(12,13)` · `calibration_current(14)` ·
`encoder_type(18)` · `torque_constant(34)` · `thermal_*(35-38)`

## 🔴 GUARDADO — SEM suporte no firmware (precisa código novo) — **[P2/P3/P5]**
`position_smoothing(15)` · `power_limit(16)` · `braking_limit(17)` · `osc_guard_enable(22)` ·
`cogging_enable(25)` · `soft_power_enable(43)` · `power_button_enable(44)`

## ⚠️ Problemas de SENTIDO
| # | Setting | Problema | Ação |
|---|---|---|---|
| 1 | `calibration_current(14)` | schema em **"%"** (0-100, def 30) mas o firmware usa **AMPERES** (3A validado) | **CORRIGIDO** → unidade A, range 0-30, def 3 |
| 2 | `torque_constant(34)` | default **0** → Kt=0 é inválido (quebra `torque=Kt×I` e o pico) | **CORRIGIDO** → def 0.55 (hoverboard) |
| 3 | `power_limit(16)` | é "%", mas a visão da fonte quer **AMPERAGEM** → `dc_max_positive_current` | **FOLLOW-UP:** campo dedicado de amperagem |

## Defaults (revisados — os principais)
`motion_range=900°` · `pole_pairs=15` · `encoder_cpr=4000` · `encoder_type=0(E6B2)` · `board_variant=1(56V)` ·
`total_strength=100%` · `max_torque_limit=80%` · `spring=0%` · `damper=10%` · `linearity=100%` · `motor_temp=100°C`.
Batem com o nosso conjunto (hoverboard + E6B2 + placa 56V). O criador ajusta no perfil de hardware.

## Mapa pro roadmap
Ligar os ⚪ (P0) e a DOR real (P1) desbloqueia a maior parte do "fantasma". Os 🔧 entram no board/hardware
profile (Fase 2). Os 🔴 são features novas (P2/P3/P5). Ver `docs/ROADMAP-features.md`.
