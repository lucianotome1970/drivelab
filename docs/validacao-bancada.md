# Registro de Validação & Runbook do Stage 1

> **O que é isto:** a **fonte única** de tudo que está codado mas ainda **não foi validado em hardware**, mais o roteiro do **primeiro giro do motor** (Stage 1).
> Marque os `[ ]` conforme validar. A regra: nada de "achar que está ok" — só marca o que foi **medido/visto** funcionando.
> Atualizado pela última vez: 2026-08-01.

---

## Parte 1 — Registro de validação pendente

Legenda de risco: 🔴 segurança (antes do motor) · 🟠 gated (ativa no Stage 1) · 🟡 feel (ajustar girando) · 🔵 telemetria M1 · 🟢 motor-OFF (validável já) · ⚪ periféricos.

### 🔴 A. Bloqueadores de segurança — validar ANTES de destravar o motor
- [x] **Leitura da tensão do bus** (`FocPower::busVoltage`, pino PA6) — o **divisor vem do setting "Variante da placa" (24V→11 / 56V→19)**, propriedade do hardware INDEPENDENTE da tensão de operação (`vbusDividerForVariant`) → um binário atende as duas placas sem recompilar, inclusive placa 56V rodando fonte 24V. Há também um **aviso de plausibilidade** (`busVoltageImplausible` → flag na telemetria → banner no HardwareMonitor) que pega variante 24V/56V escolhida errada. **ESCALA CALIBRADA (2026-08-01):** num DC limpo a 19V, multímetro **19,90V** vs firmware **19,71V** = **~1% baixo, dentro da tolerância dos resistores do divisor → sem trim**. Confirma o **divisor 19 (variante 56V) correto** (se fosse placa 24V/divisor 11 leria ~34V). Leitura via telemetria HID 0x21 `[15..16]`, sem ST-Link. (O antigo susto de "ADC ~1,4× errado" era dado lixo do giro-na-mão a 5V com o bus balançando.) **O corte de sobretensão e o brake chopper dependem disto.**
- [ ] **Proteção de sobrecorrente** (`FocCurrent`) — offset 2048 counts + VDDA nominal são chutes (`motor_hal.h`). Calibrar o offset com o motor **parado/sem corrente** antes de confiar na proteção.
- [ ] **POLE_PAIRS** (=15, `m5/main.cpp`) — confirmar com o motor real (senão o FOC comuta errado → treme/trava).
- [ ] **ENC_CPR** (=10000, E6B2 2500 P/R × 4) — confirmar girando 90° físicos e conferindo 90° na tela.
- [~] **Brake chopper PWM** (meio-ponte AUX_L/AUX_H, TIM2) — **PORTADO** (fiel ao fork MKS v0.5.1): lógica pura `duty→timings` testada (`brake_pwm.h`, 101 pts de dead-time) + acionamento HW em `FocBrake` (TIM2 center-aligned, CH3 low/CH4 high, dead-time por SW). **Segurança dupla:** atrás da flag `DRVLAB_BRAKE_CHOPPER_HW` (build padrão = TIM2 intocado) **e** desarmado por padrão (`arm()` é passo deliberado). **Falta na bancada:** (1) definir a flag e rebuildar; (2) **escopo nos gates PB10/PB11** no estado desarmado (confirmar sem shoot-through) com resistor desconectado/baixa tensão; (3) wire do `arm()` a um gatilho do Stage 1; (4) validar `kBrakeMaxCurrentA`/2Ω.
- [ ] **`kBrakeMaxCurrentA`=12 / `kBrakeResistanceOhm`=2** (`apply_cfg.h`) — confirmar contra o resistor real e o limite do MOSFET AUX.

### 🟠 B. Gated — codado, nunca rodou em hardware (ativa no Stage 1, `g_calibrated`)
- [ ] `engine.step()` inteiro — todo o pipeline força→torque.
- [ ] `guard.step()` / brake chopper (roda dentro do engine.step).
- [ ] `motor.initFOC()` (alinhamento) / `enable()` / `setTorque()`.
- [ ] **Índice Z** do encoder — hoje desligado de propósito (ver set-center); avaliar uso p/ refino/drift.

### 🟡 C. Placeholders de "feel" — ajustar girando (pós primeiro-giro)
- [ ] `maxTorqueNm`=2.5 (`m5/main.cpp`).
- [ ] `CurrentP` / `CurrentI` (PI da malha de corrente) — defaults do app, não tunados.
- [ ] `CalibrationCurrent`.
- [ ] `kMaxVel`=20 / `kMaxAccel`=500 (normalização damper/inertia, `effect_manager.h`).
- [ ] `kSlewMaxNmPerStep`=0.5 (`apply_cfg.h`).
- [ ] `kFrictionSmoothVel`=0.3 (atrito suave — **nunca foi sentido**, `ffb_math.h`).
- [ ] **Tabela de cogging** — VAZIA (nullptr); calibrar girando devagar.

### 🔵 D. Telemetria adiada p/ M1 — campos ainda em 0
- [ ] `Torque`, `MotorCurrentMa` — sem malha de corrente rodando.
- [x] `FetTempC` — **habilitado E CALIBRADO (2026-07-27)**. Descoberta na bancada: a **ODESC 54V (clone) tem o divisor do NTC INVERTIDO** vs ODrive genuíno (NTC no lado do VDDA) → a fórmula do genuíno dava **95°C falsos**. Diagnóstico via ADC cru exposto na telemetria (byte 20-21) + MCU como referência de verdade. Corrigido: `fetThermistorCentiC` usa `R_ntc=Rload*(4096-counts)/counts` com Rload=2900. **Validado:** FET lê 30°C = MCU 31°C. Refinar por 2 pontos se precisar de precisão perto do corte (85°C).
- [x] `BusVoltageMv` — **plugado na telemetria** (v0.3.2): m5 lê `FocPower::busVoltage()` (PA6) e envia em `[15..16]`. **Escala CALIBRADA (2026-08-01):** ~1% a 19,9V, dentro da tolerância, sem trim (ver item 🔴A1).
- [~] `MotorTempC` — **caminho pronto** (v0.3.x): m5 lê `FocPower::motorTempC()` → telemetria `[17]` → app já mostra "Motor temperature". **Falta o sensor físico:** soldar um **NTC MF52A 10k B3435** no enrolamento, fios pelo eixo oco até o pad **AUX_TEMP (PA5)** + GND, e **definir `DRVLAB_MOTOR_NTC`** no build (sem a flag = -128 "sem sensor"). Reaproveita a fórmula Beta do FET (mesmo NTC). **Validar na bancada:** (a) o pull-up do AUX_TEMP (assume 3k3, igual M0_TEMP) contra um termômetro; (b) definir o **corte de sobretemperatura ~105°C** (o epóxi do MF52A só vai a ~125°C). Alternativa de pino: `M1_TEMP` (PA4). Fecha o loop com o **derate térmico** (modelo I²t + NTC real).
- [x] `McuTempC` — único sensor real hoje. ✅

### 🟢 E. Motor-OFF — validável JÁ (sem 56V, base alimentada pelo **bus ≥~12V** ou pelo **3.3V do ST-Link**)

> ⚠️ **Correção 2026-08-01:** a base **NÃO** é alimentada pela porta USB de dados — o USB é só dados (comportamento de projeto do clone MKS/ODrive, confirmado numa placa nova também). O buck interno precisa de **≥~12V no bus** (5V no bus **não liga** o MCU); alternativamente o **3.3V/VTref do ST-Link** alimenta a lógica. Só depois de a placa estar acesa por um desses caminhos é que a USB de dados enumera.
- [ ] **Set-center + leitura do encoder** (novo, commit 35a6222) — girar na mão, ver o ângulo mudar, clicar Center, conferir 90°→90°.
- [ ] **Telemetria app↔base na placa BASE real** (até agora só o simulador).
- [x] **Firmware update na BASE** — **VALIDADO (2026-08-01)**: ciclo **zero-toque** completo pela USB de dados (firmware → `EnterDfu` → DFU `0483:df11` → `dfu-util` grava → `:leave` reinicia no firmware), **sem ST-Link, sem SW1, sem power-cycle**. O antigo travamento do auto-jump era `PRIMASK=1` (IRQ mascarada antes do salto pro ROM); fix `__enable_irq()` em `lib/base_usb/dfu_jump.cpp` (commit 96f4d51). Falta re-validar o mesmo fluxo pelo **botão Enviar do DriveLab Studio** (BaseUpdater).
- [ ] **Clip meter** na placa real (a demanda de força vs teto).

### ⚪ F. Periféricos RP2040 — não gravados/validados
- [ ] **Handbrake** — 1ª gravação precisa BOOTSEL manual.
- [ ] **Wheel (aro)** — idem.

---

## Parte 2 — Runbook do Stage 1 (primeiro giro do motor)

> **Regras de ouro:** você **presente**, mão perto do disjuntor/E-stop, **corrente baixa** primeiro, aumentar aos poucos. Nada de "só testar rápido no talo".

### Pré-requisitos de hardware (comprar/montar antes)
- [ ] **Fonte** casada à variante da placa (56V) — de preferência **com limite de corrente ajustável** no primeiro dia.
- [ ] **Resistor de frenagem 2Ω** de potência (cerâmico/alumínio 50–100W) ligado nos terminais de brake.
- [ ] **Capacitor de barramento**: ≥63V (ideal 100V), ~470–1000µF, baixo ESR, 105°C — ex. **Nichicon LGU2A102MELZ** (1000µF/100V). O mais perto possível da entrada DC, fios curtos/grossos, **polaridade conferida**.
- [ ] **Anti-inrush**: NTC **MF72-5D20** em série no positivo (ou relé de pré-carga: resistor ~22–47Ω/5W + bypass).
- [ ] **ST-Link** disponível p/ regravar (Mac: revezar com o USB de dados — 1 porta só).

### Sequência de energização (a cada liga)
1. [ ] **Com tudo DESLIGADO:** conferir visualmente — polaridade do cap, resistor de brake ligado, sem curto/fio solto.
2. [ ] Fonte em **limite de corrente baixo**; ligar via **pré-carga** (NTC/relé) e só então o bus pleno.
3. [ ] Conectar o **USB** e abrir o app. **NÃO habilitar força ainda.**
4. [ ] **Conferir item 🔴A1:** comparar a **tensão do bus na telemetria** com o **multímetro** — deve bater em **~1%** (escala já validada 2026-08-01, sem ajuste necessário; se a variante da placa estiver certa, não precisa mexer). *(se destoar muito, é variante 24V/56V errada — corrigir antes de seguir.)*

### Calibração (motor ainda sem torque de força)
5. [ ] Confirmar **POLE_PAIRS** e **ENC_CPR** (girar 90°, conferir na tela).
6. [ ] Calibrar **offset de corrente** (`FocCurrent`) com o motor parado.
7. [ ] `CalibrationCurrent` **baixo** → `motor.initFOC()` (alinhamento). Conferir: sem fault, direção de encoder/força coerente.

### Primeiro giro
8. [ ] Destravar o gate (`g_calibrated`) com **`MaxTorqueLimit` bem baixo** e **mão no volante**.
9. [ ] Aplicar uma força constante fraca pela tela de Teste → sentir o torque, conferir **direção**.
10. [ ] Subir `CurrentP`/`CurrentI` e o torque **aos poucos**, observando ruído/oscilação (o osc-guard ajuda, mas observe).
11. [ ] Provocar **regen** (frear o volante com a mão) e observar a **tensão do bus** — validar o brake chopper (quando a PWM do TIM2 estiver portada).

### Abortos de segurança (parar na hora se…)
- ⛔ **Fault de sobretensão** (bus passou de `overVoltageV`) — o firmware já desabilita a força; investigar antes de religar.
- ⛔ Motor **treme/apita/esquenta rápido** → POLE_PAIRS/encoder/comutação errados. Desligar.
- ⛔ Fonte bate no **limite de corrente** ao ligar → inrush/curto. Desligar, checar pré-carga/cap.
- ⛔ Qualquer cheiro/fumaça/ruído mecânico anormal → **corta tudo**.

---

## Ordem sugerida (o que fazer primeiro)
1. **Agora, sem bancada:** validar 🟢E (set-center, telemetria, update na base) e ⚪F (gravar handbrake/wheel).
2. **Portar, sem bancada:** ✅ brake chopper TIM2 (portado, atrás de flag/desarmado) + ✅ `BusVoltageMv` na telemetria (🔵D).
3. **Bancada, dia do motor:** seguir a Parte 2 na ordem, marcando 🔴A → 🟠B → 🟡C.

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
