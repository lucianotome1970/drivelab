# DriveLab — Stage 1 Validation Log & Runbook / Registro de Validação & Runbook do Stage 1

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

---

## 🇬🇧 English

> **What this is:** the **single source of truth** for everything that is coded but **not yet validated on hardware**, plus the **first motor spin** runbook (Stage 1).
> Tick the `[ ]` as you validate. The rule: no "I think it's fine" — only tick what was **measured/seen** working.
> Last updated: 2026-08-01.

### Part 1 — Pending validation log

Risk legend: 🔴 safety (before the motor) · 🟠 gated (activates in Stage 1) · 🟡 feel (tune while turning) · 🔵 M1 telemetry · 🟢 motor-OFF (validatable now) · ⚪ peripherals.

#### 🔴 A. Safety blockers — validate BEFORE unlocking the motor
- [x] **Bus voltage reading** (`FocPower::busVoltage`, pin PA6) — the **divider comes from the "Board variant" setting (24V→11 / 56V→19)**, a hardware property INDEPENDENT of the operating voltage (`vbusDividerForVariant`) → one binary serves both boards without recompiling, including a 56V board on a 24V supply. There is also a **plausibility warning** (`busVoltageImplausible` → telemetry flag → banner in HardwareMonitor) that catches a wrong 24V/56V variant choice. **SCALE CALIBRATED (2026-08-01):** on a clean 19V DC, multimeter **19.90V** vs firmware **19.71V** = **~1% low, within the divider resistors' tolerance → no trim**. Confirms the **divider 19 (56V variant) is correct** (a 24V board / divider 11 would read ~34V). Read via HID telemetry 0x21 `[15..16]`, no ST-Link. (The old "ADC ~1.4× off" scare was garbage data from hand-spinning at 5V with the bus bouncing.) **The over-voltage cutoff and the brake chopper depend on this.**
- [ ] **Over-current protection** (`FocCurrent`) — offset 2048 counts + nominal VDDA are guesses (`motor_hal.h`). Calibrate the offset with the motor **stopped/no current** before trusting the protection.
- [ ] **POLE_PAIRS** (=15, `m5/main.cpp`) — confirm with the real motor (otherwise FOC commutates wrong → shakes/locks).
- [ ] **ENC_CPR** (=10000, E6B2 2500 P/R × 4) — confirm by turning a physical 90° and checking 90° on screen.
- [~] **Brake chopper PWM** (half-bridge AUX_L/AUX_H, TIM2) — **PORTED** (faithful to the MKS v0.5.1 fork): pure `duty→timings` logic tested (`brake_pwm.h`, 101 dead-time points) + HW drive in `FocBrake` (TIM2 center-aligned, CH3 low/CH4 high, SW dead-time). **Double safety:** behind the `DRVLAB_BRAKE_CHOPPER_HW` flag (default build = TIM2 untouched) **and** disarmed by default (`arm()` is a deliberate step). **Bench TODO:** (1) define the flag and rebuild; (2) **scope the PB10/PB11 gates** in the disarmed state (confirm no shoot-through) with the resistor disconnected/low voltage; (3) wire `arm()` to a Stage 1 trigger; (4) validate `kBrakeMaxCurrentA`/2Ω.
- [ ] **`kBrakeMaxCurrentA`=12 / `kBrakeResistanceOhm`=2** (`apply_cfg.h`) — confirm against the real resistor and the AUX MOSFET's limit.

#### 🟠 B. Gated — coded, never ran on hardware (activates in Stage 1, `g_calibrated`)
- [ ] full `engine.step()` — the whole force→torque pipeline.
- [ ] `guard.step()` / brake chopper (runs inside engine.step).
- [ ] `motor.initFOC()` (alignment) / `enable()` / `setTorque()`.
- [ ] encoder **Z index** — deliberately off today (see set-center); evaluate using it for refinement/drift.

#### 🟡 C. "Feel" placeholders — tune while turning (after the first spin)
- [ ] `maxTorqueNm`=2.5 (`m5/main.cpp`).
- [ ] `CurrentP` / `CurrentI` (current-loop PI) — app defaults, not tuned.
- [ ] `CalibrationCurrent`.
- [ ] `kMaxVel`=20 / `kMaxAccel`=500 (damper/inertia normalization, `effect_manager.h`).
- [ ] `kSlewMaxNmPerStep`=0.5 (`apply_cfg.h`).
- [ ] `kFrictionSmoothVel`=0.3 (smooth friction — **never felt**, `ffb_math.h`).
- [ ] **Cogging table** — EMPTY (nullptr); calibrate by turning slowly.

#### 🔵 D. Telemetry deferred to M1 — fields still 0
- [ ] `Torque`, `MotorCurrentMa` — no current loop running.
- [x] `FetTempC` — **enabled AND CALIBRATED (2026-07-27)**. Bench finding: the **ODESC 54V (clone) has the NTC divider INVERTED** vs genuine ODrive (NTC on the VDDA side) → the genuine formula gave **95°C false readings**. Diagnosed via the raw ADC exposed in telemetry (byte 20-21) + the MCU as ground truth. Fixed: `fetThermistorCentiC` uses `R_ntc=Rload*(4096-counts)/counts` with Rload=2900. **Validated:** FET reads 30°C = MCU 31°C. Refine with 2 points if you need precision near the cutoff (85°C).
- [x] `BusVoltageMv` — **wired into telemetry** (v0.3.2): m5 reads `FocPower::busVoltage()` (PA6) and sends it in `[15..16]`. **Scale CALIBRATED (2026-08-01):** ~1% at 19.9V, within tolerance, no trim (see item 🔴A1).
- [~] `MotorTempC` — **path ready** (v0.3.x): m5 reads `FocPower::motorTempC()` → telemetry `[17]` → the app already shows "Motor temperature". **Missing the physical sensor:** solder an **NTC MF52A 10k B3435** on the winding, wires through the hollow shaft to the **AUX_TEMP (PA5)** pad + GND, and **define `DRVLAB_MOTOR_NTC`** in the build (without the flag = -128 "no sensor"). Reuses the FET's Beta formula (same NTC). **Bench validation:** (a) the AUX_TEMP pull-up (assumes 3k3, same as M0_TEMP) against a thermometer; (b) set the **over-temperature cutoff ~105°C** (the MF52A's epoxy only goes to ~125°C). Alternate pin: `M1_TEMP` (PA4). Closes the loop with the **thermal derate** (I²t model + real NTC).
- [x] `McuTempC` — the only real sensor today. ✅

#### 🟢 E. Motor-OFF — validatable NOW (no 56V, board powered from the **bus ≥~12V** or the **ST-Link's 3.3V**)

> ⚠️ **Correction 2026-08-01:** the base is **NOT** powered from the data USB port — USB is data-only (by-design behavior of the MKS/ODrive clone, confirmed on a brand-new board too). The internal buck needs **≥~12V on the bus** (5V on the bus does **not** power the MCU); alternatively the **ST-Link's 3.3V/VTref** powers the logic. Only once the board is alive through one of those paths does the data USB enumerate.
- [ ] **Set-center + encoder reading** (new, commit 35a6222) — turn by hand, watch the angle change, click Center, check 90°→90°.
- [ ] **app↔base telemetry on the real BASE board** (so far only the simulator).
- [x] **Firmware update on the BASE** — **VALIDATED (2026-08-01)**: full **zero-touch** cycle over the data USB (firmware → `EnterDfu` → DFU `0483:df11` → `dfu-util` flashes → `:leave` reboots into the firmware), **no ST-Link, no SW1, no power-cycle**. The old auto-jump hang was `PRIMASK=1` (interrupts masked before the jump to the ROM); fix `__enable_irq()` in `lib/base_usb/dfu_jump.cpp` (commit 96f4d51). **Also validated through DriveLab Studio's Send button** (0.3.6 → 0.3.7, fully automatic, no `SW1` prompt; telemetry confirmed v0.3.7).
- [ ] **Clip meter** on the real board (force demand vs ceiling).

#### ⚪ F. RP2040 peripherals — not flashed/validated
- [ ] **Handbrake** — first flash needs manual BOOTSEL.
- [ ] **Wheel (rim)** — same.

---

### Part 2 — Stage 1 runbook (first motor spin)

> **Golden rules:** you **present**, hand near the breaker/E-stop, **low current** first, raise gradually. No "just a quick test at full send".

#### Hardware prerequisites (buy/build first)
- [ ] **Power supply** matched to the board variant (56V) — preferably **with adjustable current limit** on day one.
- [ ] **2Ω brake resistor** power-rated (ceramic/aluminum 50–100W) wired to the brake terminals.
- [ ] **Bus capacitor**: ≥63V (ideally 100V), ~470–1000µF, low ESR, 105°C — e.g. **Nichicon LGU2A102MELZ** (1000µF/100V). As close as possible to the DC input, short/thick wires, **polarity checked**.
- [ ] **Anti-inrush**: NTC **MF72-5D20** in series on the positive (or a pre-charge relay: ~22–47Ω/5W resistor + bypass).
- [ ] **ST-Link** available to re-flash (Mac: alternate with the data USB — only 1 port).

#### Power-up sequence (every time)
1. [ ] **With everything OFF:** visual check — cap polarity, brake resistor connected, no short/loose wire.
2. [ ] Supply at **low current limit**; power up via **pre-charge** (NTC/relay) and only then the full bus.
3. [ ] Connect **USB** and open the app. **Do NOT enable force yet.**
4. [ ] **Check item 🔴A1:** compare the **bus voltage in telemetry** with the **multimeter** — should match within **~1%** (scale already validated 2026-08-01, no adjustment needed; if the board variant is right, nothing to change). *(if it's way off, it's the wrong 24V/56V variant — fix before proceeding.)*

#### Calibration (motor still without force torque)
5. [ ] Confirm **POLE_PAIRS** and **ENC_CPR** (turn 90°, check on screen).
6. [ ] Calibrate the **current offset** (`FocCurrent`) with the motor stopped.
7. [ ] `CalibrationCurrent` **low** → `motor.initFOC()` (alignment). Check: no fault, coherent encoder/force direction.

#### First spin
8. [ ] Unlock the gate (`g_calibrated`) with a **very low `MaxTorqueLimit`** and **hand on the wheel**.
9. [ ] Apply a weak constant force from the Test screen → feel the torque, check **direction**.
10. [ ] Raise `CurrentP`/`CurrentI` and the torque **gradually**, watching for noise/oscillation (the osc-guard helps, but watch).
11. [ ] Induce **regen** (brake the wheel by hand) and watch the **bus voltage** — validate the brake chopper (once the TIM2 PWM is ported).

#### Safety aborts (stop immediately if…)
- ⛔ **Over-voltage fault** (bus exceeded `overVoltageV`) — the firmware already disables force; investigate before re-powering.
- ⛔ Motor **shakes/whines/heats fast** → wrong POLE_PAIRS/encoder/commutation. Power off.
- ⛔ Supply hits the **current limit** on power-up → inrush/short. Power off, check pre-charge/cap.
- ⛔ Any abnormal smell/smoke/mechanical noise → **cut everything**.

---

### Suggested order (what to do first)
1. **Now, no bench:** validate 🟢E (set-center, telemetry, update on the base) and ⚪F (flash handbrake/wheel).
2. **Port, no bench:** ✅ brake chopper TIM2 (ported, behind flag/disarmed) + ✅ `BusVoltageMv` in telemetry (🔵D).
3. **Bench, motor day:** follow Part 2 in order, ticking 🔴A → 🟠B → 🟡C.

---

## 🇧🇷 Português

> **O que é isto:** a **fonte única** de tudo que está codado mas ainda **não foi validado em hardware**, mais o roteiro do **primeiro giro do motor** (Stage 1).
> Marque os `[ ]` conforme validar. A regra: nada de "achar que está ok" — só marca o que foi **medido/visto** funcionando.
> Atualizado pela última vez: 2026-08-01.

### Parte 1 — Registro de validação pendente

Legenda de risco: 🔴 segurança (antes do motor) · 🟠 gated (ativa no Stage 1) · 🟡 feel (ajustar girando) · 🔵 telemetria M1 · 🟢 motor-OFF (validável já) · ⚪ periféricos.

#### 🔴 A. Bloqueadores de segurança — validar ANTES de destravar o motor
- [x] **Leitura da tensão do bus** (`FocPower::busVoltage`, pino PA6) — o **divisor vem do setting "Variante da placa" (24V→11 / 56V→19)**, propriedade do hardware INDEPENDENTE da tensão de operação (`vbusDividerForVariant`) → um binário atende as duas placas sem recompilar, inclusive placa 56V rodando fonte 24V. Há também um **aviso de plausibilidade** (`busVoltageImplausible` → flag na telemetria → banner no HardwareMonitor) que pega variante 24V/56V escolhida errada. **ESCALA CALIBRADA (2026-08-01):** num DC limpo a 19V, multímetro **19,90V** vs firmware **19,71V** = **~1% baixo, dentro da tolerância dos resistores do divisor → sem trim**. Confirma o **divisor 19 (variante 56V) correto** (se fosse placa 24V/divisor 11 leria ~34V). Leitura via telemetria HID 0x21 `[15..16]`, sem ST-Link. (O antigo susto de "ADC ~1,4× errado" era dado lixo do giro-na-mão a 5V com o bus balançando.) **O corte de sobretensão e o brake chopper dependem disto.**
- [ ] **Proteção de sobrecorrente** (`FocCurrent`) — offset 2048 counts + VDDA nominal são chutes (`motor_hal.h`). Calibrar o offset com o motor **parado/sem corrente** antes de confiar na proteção.
- [ ] **POLE_PAIRS** (=15, `m5/main.cpp`) — confirmar com o motor real (senão o FOC comuta errado → treme/trava).
- [ ] **ENC_CPR** (=10000, E6B2 2500 P/R × 4) — confirmar girando 90° físicos e conferindo 90° na tela.
- [~] **Brake chopper PWM** (meio-ponte AUX_L/AUX_H, TIM2) — **PORTADO** (fiel ao fork MKS v0.5.1): lógica pura `duty→timings` testada (`brake_pwm.h`, 101 pts de dead-time) + acionamento HW em `FocBrake` (TIM2 center-aligned, CH3 low/CH4 high, dead-time por SW). **Segurança dupla:** atrás da flag `DRVLAB_BRAKE_CHOPPER_HW` (build padrão = TIM2 intocado) **e** desarmado por padrão (`arm()` é passo deliberado). **Falta na bancada:** (1) definir a flag e rebuildar; (2) **escopo nos gates PB10/PB11** no estado desarmado (confirmar sem shoot-through) com resistor desconectado/baixa tensão; (3) wire do `arm()` a um gatilho do Stage 1; (4) validar `kBrakeMaxCurrentA`/2Ω.
- [ ] **`kBrakeMaxCurrentA`=12 / `kBrakeResistanceOhm`=2** (`apply_cfg.h`) — confirmar contra o resistor real e o limite do MOSFET AUX.

#### 🟠 B. Gated — codado, nunca rodou em hardware (ativa no Stage 1, `g_calibrated`)
- [ ] `engine.step()` inteiro — todo o pipeline força→torque.
- [ ] `guard.step()` / brake chopper (roda dentro do engine.step).
- [ ] `motor.initFOC()` (alinhamento) / `enable()` / `setTorque()`.
- [ ] **Índice Z** do encoder — hoje desligado de propósito (ver set-center); avaliar uso p/ refino/drift.

#### 🟡 C. Placeholders de "feel" — ajustar girando (pós primeiro-giro)
- [ ] `maxTorqueNm`=2.5 (`m5/main.cpp`).
- [ ] `CurrentP` / `CurrentI` (PI da malha de corrente) — defaults do app, não tunados.
- [ ] `CalibrationCurrent`.
- [ ] `kMaxVel`=20 / `kMaxAccel`=500 (normalização damper/inertia, `effect_manager.h`).
- [ ] `kSlewMaxNmPerStep`=0.5 (`apply_cfg.h`).
- [ ] `kFrictionSmoothVel`=0.3 (atrito suave — **nunca foi sentido**, `ffb_math.h`).
- [ ] **Tabela de cogging** — VAZIA (nullptr); calibrar girando devagar.

#### 🔵 D. Telemetria adiada p/ M1 — campos ainda em 0
- [ ] `Torque`, `MotorCurrentMa` — sem malha de corrente rodando.
- [x] `FetTempC` — **habilitado E CALIBRADO (2026-07-27)**. Descoberta na bancada: a **ODESC 54V (clone) tem o divisor do NTC INVERTIDO** vs ODrive genuíno (NTC no lado do VDDA) → a fórmula do genuíno dava **95°C falsos**. Diagnóstico via ADC cru exposto na telemetria (byte 20-21) + MCU como referência de verdade. Corrigido: `fetThermistorCentiC` usa `R_ntc=Rload*(4096-counts)/counts` com Rload=2900. **Validado:** FET lê 30°C = MCU 31°C. Refinar por 2 pontos se precisar de precisão perto do corte (85°C).
- [x] `BusVoltageMv` — **plugado na telemetria** (v0.3.2): m5 lê `FocPower::busVoltage()` (PA6) e envia em `[15..16]`. **Escala CALIBRADA (2026-08-01):** ~1% a 19,9V, dentro da tolerância, sem trim (ver item 🔴A1).
- [~] `MotorTempC` — **caminho pronto** (v0.3.x): m5 lê `FocPower::motorTempC()` → telemetria `[17]` → app já mostra "Motor temperature". **Falta o sensor físico:** soldar um **NTC MF52A 10k B3435** no enrolamento, fios pelo eixo oco até o pad **AUX_TEMP (PA5)** + GND, e **definir `DRVLAB_MOTOR_NTC`** no build (sem a flag = -128 "sem sensor"). Reaproveita a fórmula Beta do FET (mesmo NTC). **Validar na bancada:** (a) o pull-up do AUX_TEMP (assume 3k3, igual M0_TEMP) contra um termômetro; (b) definir o **corte de sobretemperatura ~105°C** (o epóxi do MF52A só vai a ~125°C). Alternativa de pino: `M1_TEMP` (PA4). Fecha o loop com o **derate térmico** (modelo I²t + NTC real).
- [x] `McuTempC` — único sensor real hoje. ✅

#### 🟢 E. Motor-OFF — validável JÁ (sem 56V, base alimentada pelo **bus ≥~12V** ou pelo **3.3V do ST-Link**)

> ⚠️ **Correção 2026-08-01:** a base **NÃO** é alimentada pela porta USB de dados — o USB é só dados (comportamento de projeto do clone MKS/ODrive, confirmado numa placa nova também). O buck interno precisa de **≥~12V no bus** (5V no bus **não liga** o MCU); alternativamente o **3.3V/VTref do ST-Link** alimenta a lógica. Só depois de a placa estar acesa por um desses caminhos é que a USB de dados enumera.
- [ ] **Set-center + leitura do encoder** (novo, commit 35a6222) — girar na mão, ver o ângulo mudar, clicar Center, conferir 90°→90°.
- [ ] **Telemetria app↔base na placa BASE real** (até agora só o simulador).
- [x] **Firmware update na BASE** — **VALIDADO (2026-08-01)**: ciclo **zero-toque** completo pela USB de dados (firmware → `EnterDfu` → DFU `0483:df11` → `dfu-util` grava → `:leave` reinicia no firmware), **sem ST-Link, sem SW1, sem power-cycle**. O antigo travamento do auto-jump era `PRIMASK=1` (IRQ mascarada antes do salto pro ROM); fix `__enable_irq()` em `lib/base_usb/dfu_jump.cpp` (commit 96f4d51). **Também validado pelo botão Enviar do DriveLab Studio** (0.3.6 → 0.3.7, totalmente automático, sem pedir `SW1`; telemetria confirmou v0.3.7).
- [ ] **Clip meter** na placa real (a demanda de força vs teto).

#### ⚪ F. Periféricos RP2040 — não gravados/validados
- [ ] **Handbrake** — 1ª gravação precisa BOOTSEL manual.
- [ ] **Wheel (aro)** — idem.

---

### Parte 2 — Runbook do Stage 1 (primeiro giro do motor)

> **Regras de ouro:** você **presente**, mão perto do disjuntor/E-stop, **corrente baixa** primeiro, aumentar aos poucos. Nada de "só testar rápido no talo".

#### Pré-requisitos de hardware (comprar/montar antes)
- [ ] **Fonte** casada à variante da placa (56V) — de preferência **com limite de corrente ajustável** no primeiro dia.
- [ ] **Resistor de frenagem 2Ω** de potência (cerâmico/alumínio 50–100W) ligado nos terminais de brake.
- [ ] **Capacitor de barramento**: ≥63V (ideal 100V), ~470–1000µF, baixo ESR, 105°C — ex. **Nichicon LGU2A102MELZ** (1000µF/100V). O mais perto possível da entrada DC, fios curtos/grossos, **polaridade conferida**.
- [ ] **Anti-inrush**: NTC **MF72-5D20** em série no positivo (ou relé de pré-carga: resistor ~22–47Ω/5W + bypass).
- [ ] **ST-Link** disponível p/ regravar (Mac: revezar com o USB de dados — 1 porta só).

#### Sequência de energização (a cada liga)
1. [ ] **Com tudo DESLIGADO:** conferir visualmente — polaridade do cap, resistor de brake ligado, sem curto/fio solto.
2. [ ] Fonte em **limite de corrente baixo**; ligar via **pré-carga** (NTC/relé) e só então o bus pleno.
3. [ ] Conectar o **USB** e abrir o app. **NÃO habilitar força ainda.**
4. [ ] **Conferir item 🔴A1:** comparar a **tensão do bus na telemetria** com o **multímetro** — deve bater em **~1%** (escala já validada 2026-08-01, sem ajuste necessário; se a variante da placa estiver certa, não precisa mexer). *(se destoar muito, é variante 24V/56V errada — corrigir antes de seguir.)*

#### Calibração (motor ainda sem torque de força)
5. [ ] Confirmar **POLE_PAIRS** e **ENC_CPR** (girar 90°, conferir na tela).
6. [ ] Calibrar **offset de corrente** (`FocCurrent`) com o motor parado.
7. [ ] `CalibrationCurrent` **baixo** → `motor.initFOC()` (alinhamento). Conferir: sem fault, direção de encoder/força coerente.

#### Primeiro giro
8. [ ] Destravar o gate (`g_calibrated`) com **`MaxTorqueLimit` bem baixo** e **mão no volante**.
9. [ ] Aplicar uma força constante fraca pela tela de Teste → sentir o torque, conferir **direção**.
10. [ ] Subir `CurrentP`/`CurrentI` e o torque **aos poucos**, observando ruído/oscilação (o osc-guard ajuda, mas observe).
11. [ ] Provocar **regen** (frear o volante com a mão) e observar a **tensão do bus** — validar o brake chopper (quando a PWM do TIM2 estiver portada).

#### Abortos de segurança (parar na hora se…)
- ⛔ **Fault de sobretensão** (bus passou de `overVoltageV`) — o firmware já desabilita a força; investigar antes de religar.
- ⛔ Motor **treme/apita/esquenta rápido** → POLE_PAIRS/encoder/comutação errados. Desligar.
- ⛔ Fonte bate no **limite de corrente** ao ligar → inrush/curto. Desligar, checar pré-carga/cap.
- ⛔ Qualquer cheiro/fumaça/ruído mecânico anormal → **corta tudo**.

---

### Ordem sugerida (o que fazer primeiro)
1. **Agora, sem bancada:** validar 🟢E (set-center, telemetria, update na base) e ⚪F (gravar handbrake/wheel).
2. **Portar, sem bancada:** ✅ brake chopper TIM2 (portado, atrás de flag/desarmado) + ✅ `BusVoltageMv` na telemetria (🔵D).
3. **Bancada, dia do motor:** seguir a Parte 2 na ordem, marcando 🔴A → 🟠B → 🟡C.

---

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
