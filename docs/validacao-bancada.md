# Registro de Validação & Runbook do Stage 1

> **O que é isto:** a **fonte única** de tudo que está codado mas ainda **não foi validado em hardware**, mais o roteiro do **primeiro giro do motor** (Stage 1).
> Marque os `[ ]` conforme validar. A regra: nada de "achar que está ok" — só marca o que foi **medido/visto** funcionando.
> Atualizado pela última vez: 2026-07-24.

---

## Parte 1 — Registro de validação pendente

Legenda de risco: 🔴 segurança (antes do motor) · 🟠 gated (ativa no Stage 1) · 🟡 feel (ajustar girando) · 🔵 telemetria M1 · 🟢 motor-OFF (validável já) · ⚪ periféricos.

### 🔴 A. Bloqueadores de segurança — validar ANTES de destravar o motor
- [ ] **Leitura da tensão do bus** (`FocPower::busVoltage`, pino PA6) — o divisor resistivo/escala é placeholder (`busMilliVolts`, VDDA nominal). **Todo o corte de sobretensão e o brake chopper dependem disto.** Validar: comparar a leitura da telemetria com um multímetro no barramento; ajustar a escala até bater.
- [ ] **Proteção de sobrecorrente** (`FocCurrent`) — offset 2048 counts + VDDA nominal são chutes (`motor_hal.h`). Calibrar o offset com o motor **parado/sem corrente** antes de confiar na proteção.
- [ ] **POLE_PAIRS** (=15, `m5/main.cpp`) — confirmar com o motor real (senão o FOC comuta errado → treme/trava).
- [ ] **ENC_CPR** (=10000, E6B2 2500 P/R × 4) — confirmar girando 90° físicos e conferindo 90° na tela.
- [ ] **Brake chopper PWM** (meio-ponte AUX_L/AUX_H, TIM2 — `FocBrake::setDuty` é **NO-OP**) — portar a PWM real (ref.: fork ODrive v0.5.1 da MKS em `~/Downloads`). Até lá, a regen sustentada não é dissipada de fato.
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
- [ ] `FetTempC` — NTC dos FETs (pinos do clone MKS podem divergir).
- [ ] `BusVoltageMv` — **o `FocPower` já tem o read, falta plugar na telemetria** (hoje envia 0).
- [ ] `MotorTempC` — sem NTC dedicado confirmado.
- [x] `McuTempC` — único sensor real hoje. ✅

### 🟢 E. Motor-OFF — validável JÁ (sem 56V, só plugar a base por USB)
- [ ] **Set-center + leitura do encoder** (novo, commit 35a6222) — girar na mão, ver o ângulo mudar, clicar Center, conferir 90°→90°.
- [ ] **Telemetria app↔base na placa BASE real** (até agora só o simulador).
- [ ] **Firmware update na BASE** (só validado no **pedal** até agora).
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
4. [ ] **Validar item 🔴A1:** comparar a **tensão do bus na telemetria** com o **multímetro**. Ajustar a escala do divisor até bater. *(sem isto, não seguir.)*

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
2. **Portar, sem bancada:** brake chopper TIM2 (código) + plugar `BusVoltageMv` na telemetria (🔵D).
3. **Bancada, dia do motor:** seguir a Parte 2 na ordem, marcando 🔴A → 🟠B → 🟡C.

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
