# DriveLab — Roadmap de Features

> Comparação do NOSSO firmware (`firmware-base`) com outros firmwares de FFB da comunidade (mesma base
> ODrive v0.5.6). Objetivo: o que eles têm e nós **ainda não** — priorizado.
> Legenda: **[LIGAR]** = código já existe, só conectar · **[NOVO]** = implementar · Esforço S/M/L ·
> 🔧 = precisa de bancada · 💻 = off-bench (app/host/doc).
> Regra: firmware só na bancada, uma mudança por vez (ver `ROADMAP.md` e a memória).

---

## Descoberta-chave
**Boa parte do "gap" já está escrita no nosso `ffb_math.h`/`ffb_effects` — só não está ligada aos settings.**
O motor de efeitos HID PID nosso é **sólido** (constant, periodic, ramp, conditions, envelope, direction,
device gain, endstop+damping, watchdog, telemetria de ângulo/torque/corrente). O que falta é **conectar
knobs, persistir, e adicionar segurança/telemetria fina.**

---

## P0 — Quick-wins: LIGAR o que já existe (alto valor, baixo esforço)

| Feature | Nosso status | Ação | Esforço |
|---|---|---|---|
| **Linearity / expo** | `responseCurve` existe, `linearity` fixo em 1.0 | aplicar setting 24 no `s_fc.linearity` | S 🔧 |
| **Force-curve por pontos (5 pts)** | `applyForceCurve` existe, curva nunca preenchida | preencher `s_fc.curve` dos settings 28–32 | S 🔧 |
| **Slew-rate limiter** | `slewLimit` existe, **nunca chamada** | chamar no pipeline + aplicar setting 26 | S 🔧 |
| **Friction always-on** | `frictionTorque` existe, campo nunca setado | ligar `static_damping`(6) no `s_ef.frictionNm` | S 🔧 |
| **Endstop ajustável (mola/damper)** | stiffness/damping são **constantes fixas** | ler `soft_stop_strength`(2)/`endstop_damping`(23) | S 🔧 |
| **Reconstruction steps/lpf** | ativo com defaults, settings 19/20 inertes | aplicar no `s_recon.cfg` | S 🔧 |
| **Ganhos de biquad configuráveis** | cortes fixos (Da 30 / In 15 / Fr 50 Hz) | expor freq/Q por efeito (como eles) | M 🔧 |

> Todos já têm (ou quase) UI no app reincorporado. É o **maior retorno por esforço**.

## P1 — Buracos que afetam o uso HOJE

| Feature | Nosso status | Por quê importa | Esforço |
|---|---|---|---|
| **Persistência (SAVE na flash)** | `CMD_SAVE` é **no-op**; settings voltam ao default a cada boot | o usuário reconfigura tudo todo boot; outros firmwares persistem em EEPROM | **L 🔧** |
| **DOR real no eixo** | eixo do joystick usa **±1.5 volta hardcoded**, não o `motion_range` | a escala de direção fica errada em todos os jogos | M 🔧 |
| **Force-disable real** | comando existe mas **não zera o torque** (só o flag da telemetria) | segurança: "desligar força" no app não desliga de verdade | S 🔧 |
| **Idle-spring (centra só com FFB off)** | temos spring always-on opt-in, não idle-only | feel: auto-centra no menu sem lutar com o jogo | S 🔧 |
| **Fade-in de força (soft-start)** | não temos | evita solavanco ao armar/reconectar (eles têm) | M 🔧 |
| **Centro mecânico persistente + reset** | `ResetCenter` captura mas **não persiste** (depende do SAVE) | manter o "reto" entre boots | S 🔧 (após SAVE) |

## P2 — Segurança (habilita subir a força com segurança)

| Feature | Nosso status | Esforço |
|---|---|---|
| **Corte por sobretemperatura do motor** | NTC não configurado, telemetria de temp = -128 (placeholder) | **L 🔧** |
| **Telemetria de temp (FET + motor) no canal A0** | placeholders -128 | M 🔧 |
| **Failsafe USB** (zerar FFB ao desconectar/suspender) | temos watchdog 800ms; falta hook `tud_umount → stop_FFB` | S 🔧 |
| **Spinout tuning** (afrouxar limiar p/ DD) | não tunado (temos `enable_vel_limit=false`) | S 🔧 |
| **Emergency-stop** (comando para/religa com fade) | não temos | M 🔧 |
| **Speed limiter suave** (reduz torque acima de maxspeed) | não temos | M 🔧 |
| **Corte por sobrecorrente próprio** | `overCurrent()` existe, **nunca chamada** (o core ODrive já protege) | S 🔧 |

## P3 — Telemetria / tuning fino

| Feature | Fonte | Esforço |
|---|---|---|
| **Stream de telemetria a 1kHz** (vel, Iq, torque, vbus, ibus, ibrake) p/ overlays | comunidade | M 🔧 |
| **Medidor de clipping** (satura em ±força) | ambos | S 🔧 |
| **Stats por efeito** (qual efeito, força atual/máx) | ambos | M 💻/🔧 |
| **Taxa de update HID** (hidrate) | ambos | S 🔧 |
| **Rampa senoidal de fricção perto de zero** (mata o "notch" central) | ambos | S 🔧 |

## P4 — Flexibilidade de hardware (= Fase 2 do roadmap principal, modelo FFBeast)

| Feature | Nosso status | Esforço |
|---|---|---|
| **Encoder SPI (AS5047P / MT6701)** | infra ODrive presente, config **forçada a incremental** | M 🔧 |
| **Geometria configurável** (pole_pairs/cpr/dir das settings) | hardcoded no `newboard_bringup` | M 🔧 |
| **Índice Z** (offset consistente por boot + wizard/contadores de glitch) | desligado | M 🔧 |
| **Board profiles** (`board_variant` já existe no A0) | só guardado | M 🔧 |
| **Ler motor+encoder da flash** (não cravar) → 1 binário XDrive-S/MINI | cravado | L 🔧 |

### ⭐ Perfil de Fonte + Hardware (criador configura, cliente recebe travado)

**Visão (do usuário):** o **desenvolvedor** do DD preenche a fonte (tensão + amperagem), a placa e o motor
numa área **fechada** (aba Hardware, hoje já **só do criador**); o firmware **deriva todos os limites de
segurança + o torque**; embute no `hardware-profile.json` → **installer** → o **cliente recebe pré-configurado
e travado** (não vê nem mexe na área de hardware). Base já existe: aba Hardware criador-only + export do perfil.

**O criador seta → o firmware deriva:**
| Entrada (aba Hardware) | Derivado pelo firmware |
|---|---|
| **Tensão da fonte** (24V / 56V…) | `vbus divider` (24V→11, 56V→19) + `dc_bus_undervoltage/overvoltage_trip_level` por variante (~28V na 24V, ~55V na 56V) |
| **Amperagem da fonte** (13,5A / 30A…) | `dc_max_positive_current` (proteção da fonte) + limites sustentados |
| **Motor** (Kt, pole_pairs, cpr, R/L) | `current_lim` (teto de torque) + exibe **pico = current_lim × Kt** |
| **Brake** (Ω, on/off) | `brake_resistance` + `enable_brake_resistor` |

**Nuance (não prometer errado):** o **pico de torque** vem do `current_lim × Kt` (lado **motor** — no stall
a fonte quase não limita, `Ibus ≈ I²R/Vbus`); a **fonte (V+A)** define **trips de tensão + proteção de
corrente do bus + o que sustenta movendo**. O cálculo usa fonte **E** motor juntos.

**Divisão do trabalho:**
- **App (off-bench):** campos de Fonte na aba Hardware + fórmulas de derivação (mostrar torque/limites pro
  criador) + gravar tudo no `hardware-profile.json` / settings A0.
- **Firmware (bancada, 1 por vez):** aplicar os derivados via settings A0 — `power_limit`(16)→`dc_max_positive_current`,
  `bus_nominal`(27)→trips+`g_vbus_voltage_scale` (divider já é runtime!), `board_variant`(33). Validar o arme
  após cada um.

**Depende de:** P4 (board profile / ler config da flash) + o porte dos settings A0 (P1). É o "board/supply
profile" do modelo FFBeast — o mesmo mecanismo do "1 binário pra família ODrive".

## P5 — Premium (longe; feel de DD caro)

| Feature | Fonte | Nota |
|---|---|---|
| **Anticogging** (mapa na flash) | ODrive nativo | L 🔧 |
| **Auto-tune do PI de corrente/velocidade** | ambos | L 🔧 |
| **Filtro de torque notch/peak** (mata ressonância mecânica) | OpenFFBoard | M 🔧 |
| **Testes de performance host** (Bode, FFT, coastdown, inércia) | comunidade | L 💻 |
| **Presets de motor (JSON import/export)** | comunidade | M 💻 |
| **Botão "Zero Wheel" via GPIO** | ambos | S 🔧 |
| **PiP overlay** (bus/wheel/spectrum por HID) | comunidade | L 💻 |
| **Field weakening / SVPWM / PWM freq** | OpenFFBoard(TMC) | — (ODrive já faz SVPWM) |

---

## Sugestão de ordem (após o core estável — ver `ROADMAP.md` Fase 1)
1. **P0 inteiro** (ligar o que existe) — maior retorno, mexe pouco.
2. **P1: SAVE + DOR real + force-disable** — corrige o que atrapalha hoje.
3. **P2: sobretemp + failsafe USB** — pré-requisito pra liberar mais força.
4. **P3: telemetria/clipping** — dá visibilidade pro tuning.
5. **P4** = Fase 2 (multi-placa). **P5** = quando maduro.

> Nada aqui muda o fato de o firmware **já rodar no ACC com FFB**. Isto é polimento e paridade, não conserto.
