# Pinout & wiring — base board (ODrive v3.6 / MKS ODESC / XDrive) / Pinagem e montagem

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> ⚠️ This document maps `microcontroller → function`. The **physical position of each pin on the
> connectors is up to the board maker** — always check the silkscreen before soldering. A GPIO
> mistaken for a motor phase destroys the board.
>
> ⚠️ Este documento mapeia `microcontrolador → função`. A **posição física de cada pino nos
> conectores é do fabricante** — confira sempre a serigrafia antes de soldar. Um GPIO confundido com
> uma fase do motor destrói a placa.

---

## 🇬🇧 English

### Complete pin map

Legend: **🔒 taken** (do not touch) · **🟢 free** · **🔵 used by DriveLab**

#### Power and motor

| Pin | Name | What it is | |
|---|---|---|---|
| PA8, PA9, PA10 | M0_AH, M0_BH, M0_CH | high-side MOSFET drive, motor 0 | 🔒 |
| PB13, PB14, PB15 | M0_AL, M0_BL, M0_CL | low-side MOSFET drive, motor 0 | 🔒 |
| PC6, PC7, PC8 | M1_AH, M1_BH, M1_CH | same, motor 1 (unused) | 🔒 |
| PA7, PB0, PB1 | M1_AL, M1_BL, M1_CL | same, motor 1 | 🔒 |
| PB12 | EN_GATE | enables the DRV8301 gate driver | 🔒 |
| PD2 | nFAULT | DRV8301 fault signal | 🔒 |
| PC13, PC14 | M0_nCS, M1_nCS | DRV8301 SPI chip select | 🔒 |

These drive the motor phases. **Touching them bricks or burns the board** — there is no alternative
use for them.

#### Sensing

| Pin | Name | What it is | |
|---|---|---|---|
| PC0, PC1 | M0_IB, M0_IC | phase B and C current, motor 0 | 🔒 |
| PC2, PC3 | M1_IC, M1_IB | current, motor 1 | 🔒 |
| PA6 | VBUS_S | DC bus voltage | 🔒 |
| PC5 | M0_TEMP | MOSFET temperature, motor 0 | 🔵 |
| PA4 | M1_TEMP | MOSFET temperature, motor 1 | 🔒 |
| PA5 | AUX_TEMP | auxiliary temperature input | 🟢 |

Phase current is the heart of the FOC loop. **Changing ADC sample timing has broken motor control in
this project before** (2026-08-06: 18 A with the wheel standing still). If you need an analog
channel, use a free GPIO — ADC1 already scans all 16 channels continuously, so **nothing needs
reconfiguring**.

#### Encoder

| Pin | Name | What it is | |
|---|---|---|---|
| PB4, PB5 | M0_ENC_A, M0_ENC_B | encoder channels A and B | 🔵 |
| PC9 | M0_ENC_Z | index (Z) — **unused today** | 🟢 |
| PB6, PB7 | M1_ENC_A, M1_ENC_B | motor 1 encoder | 🔒 |
| PC15 | M1_ENC_Z | motor 1 index | 🔒 |

Our encoder's `Z` is **not wired**. Wiring it would allow consistent calibration across boots — it's
recorded as future work, not as a defect.

#### Communication

| Pin | Name | What it is | |
|---|---|---|---|
| PA11, PA12 | USB D−, D+ | **the data USB** — how the game and the app talk to the base | 🔵 |
| PA13, PA14 | SWDIO, SWCLK | flashing and debugging (ST-Link) | 🔵 |
| PB8, PB9 | CAN / I²C | CAN or I²C bus | 🟢 |
| PB10, PB11 | AUX_L, AUX_H | **brake resistor (chopper)** | 🔵 |

⚠️ **PB10/PB11 switch the brake resistor.** They are not ordinary GPIOs — they drive power.

#### The eight connector GPIOs

These are the ones **free to use**. Only the first five accept analog signals.

| GPIO | Pin | Analog? | DriveLab use |
|---|---|---|---|
| 1 | PA0 | ✅ ADC1_IN0 | 🟢 free |
| 2 | PA1 | ✅ ADC1_IN1 | 🟢 free |
| 3 | PA2 | ✅ ADC1_IN2 | 🟢 free |
| **4** | **PA3** | ✅ ADC1_IN3 | 🔵 **motor NTC** (ODrive default) |
| 5 | PC4 | ✅ ADC1_IN14 | 🟢 analog spare |
| 6 | PB2 | ❌ | 🔵 centering button *(planned)* |
| 7 | PA15 | ❌ | 🔵 contactor / soft-power *(planned)* |
| 8 | PB3 | ❌ | 🟢 digital spare |

> **PA15 and PB3 are JTAG pins** (JTDI and JTDO). They don't clash with our SWD, which uses
> PA13/PA14, but the firmware must explicitly release them before using them as GPIO. Given the
> choice, prefer GPIO 6 (PB2), which has no such catch.

---

### Wiring: motor temperature sensor (NTC)

Measures motor temperature so the base can shut down before the windings cook.

    3.3 V ──[ 1 kΩ ]──┬── GPIO 4 (PA3)
                      │
                  [  NTC  ]  ← 10 kΩ, next to the winding
                      │
                     GND
                      
    (100 nF capacitor between PA3 and GND, close to the board)

**Why 1 kΩ and not 10 kΩ.** The obvious match would be 10 k, same as the NTC. But the firmware
converts voltage to temperature with a **3rd-degree polynomial**, and the NTC curve is far too
non-linear for that. The pull-up shifts the linear region to where it matters:

| pull-up | worst error between 30 and 140 °C |
|---|---|
| 10 kΩ | **8 °C** |
| 4.7 kΩ | 5 °C |
| **1 kΩ** | **under 1 °C** |

On a protection that trips at 105 °C, that difference is the line between protecting and not.

**Why the NTC goes to GND, not to 3.3 V.** It's a safety choice: if the cable to the motor shorts,
the reading goes to a very high temperature and the protection **trips**. The other way around, a
short would read as "it's cold" while the motor cooks.

**Coefficients** for `motor_thermistors[0].config_.thermistor_poly_coeffs`, in the order the firmware
expects (highest degree first — `T = c₀v³ + c₁v² + c₂v + c₃`, `v` from 0 to 1):

| NTC Beta | coefficients | error |
|---|---|---|
| 3435 | `{-330.06426335f, 581.36664616f, -493.02267122f, 241.78932775f}` | 1.1 °C |
| 3950 | `{-290.08890749f, 500.08591979f, -411.27304570f, 204.40932711f}` | 0.9 °C |

**Finding your NTC's Beta** (listings rarely state it, and getting it wrong costs 7 °C at the trip
point): dip the sensor in **boiling water** and measure with a multimeter.

| reading at 100 °C | Beta |
|---|---|
| ~990 Ω | 3435 |
| ~700 Ω | 3950 |

Also check for ~10 kΩ at room temperature; if it isn't, the sensor is not a 10 k.

**Wiring care:**

- route the NTC cable **away from the phase wires** — they switch at 24 kHz and induce noise
- fix the sensor against the **winding**, not the housing: the housing heats up slowly and the
  protection would arrive late
- resistor tolerance barely matters: 5% costs ~2 °C, 1% costs ~0.4 °C. Anything between **820 Ω and
  1.2 kΩ** works

---

### Wiring: centering button

Sets the wheel's zero without needing the app.

    GPIO 6 (PB2) ──┬──[ button ]── GND
                   │
                [ 10 kΩ ]
                   │
                 3.3 V

The STM32's internal pull-up makes the external resistor optional, but a 10 kΩ one makes the signal
more noise-immune over a long cable to the rim. Button **normally open**, closing to GND.

---

### Wiring: phase contactor

Isolates the motor phases while the base is off. **Why it exists:** turning the wheel with the base
off makes the motor generate voltage (back-EMF) that feeds the DC bus — and the brake resistor does
not protect there, because it depends on firmware running.

    GPIO 7 (PA15) ──[ 1 kΩ ]── transistor base ──> contactor coil (12/24 V)
                                                    (flyback diode across it!)

⚠️ **The flyback diode across the coil is not optional.** Without it, the voltage spike when the coil
switches off comes back through the transistor and kills the microcontroller pin.

⚠️ The contactor must be **3-pole** and switch all three phases together. Breaking only one or two
leaves a path for current to circulate.

---

### Before wiring anything

1. **Check the silkscreen.** This document maps `microcontroller → function`; the position on the
   connector is up to the manufacturer. A GPIO mistaken for a phase burns the board.
2. **Measure before soldering.** With the board powered down, confirm with a multimeter that the
   connector pin is the one you think it is.
3. **No voltage above 3.3 V on the GPIOs.** They are not 5 V tolerant.
4. **Wire with the motor disarmed** and the supply off.

---

## 🇧🇷 Português

### Mapa completo

Legenda: **🔒 ocupado** (não mexa) · **🟢 livre** · **🔵 usado pelo DriveLab**

#### Potência e motor

| Pino | Nome | O que é | |
|---|---|---|---|
| PA8, PA9, PA10 | M0_AH, M0_BH, M0_CH | comando dos MOSFETs de cima, motor 0 | 🔒 |
| PB13, PB14, PB15 | M0_AL, M0_BL, M0_CL | comando dos MOSFETs de baixo, motor 0 | 🔒 |
| PC6, PC7, PC8 | M1_AH, M1_BH, M1_CH | idem, motor 1 (não usamos) | 🔒 |
| PA7, PB0, PB1 | M1_AL, M1_BL, M1_CL | idem, motor 1 | 🔒 |
| PB12 | EN_GATE | liga o driver DRV8301 | 🔒 |
| PD2 | nFAULT | o DRV8301 avisa falha | 🔒 |
| PC13, PC14 | M0_nCS, M1_nCS | seleção SPI do DRV8301 | 🔒 |

Estes pinos comandam as fases do motor. **Encostar neles trava ou queima a placa** — não há uso
alternativo possível.

#### Medição

| Pino | Nome | O que é | |
|---|---|---|---|
| PC0, PC1 | M0_IB, M0_IC | corrente das fases B e C, motor 0 | 🔒 |
| PC2, PC3 | M1_IC, M1_IB | corrente, motor 1 | 🔒 |
| PA6 | VBUS_S | tensão do barramento | 🔒 |
| PC5 | M0_TEMP | temperatura dos MOSFETs, motor 0 | 🔵 |
| PA4 | M1_TEMP | temperatura dos MOSFETs, motor 1 | 🔒 |
| PA5 | AUX_TEMP | entrada auxiliar de temperatura | 🟢 |

A corrente das fases é o coração da FOC. **Mexer no tempo de amostragem do ADC já quebrou o controle
neste projeto** (2026-08-06: 18 A com o volante parado). Se precisar de um canal analógico, use um
GPIO livre — o ADC1 já varre os 16 canais continuamente, então **não é preciso reconfigurar nada**.

#### Encoder

| Pino | Nome | O que é | |
|---|---|---|---|
| PB4, PB5 | M0_ENC_A, M0_ENC_B | canais A e B do encoder | 🔵 |
| PC9 | M0_ENC_Z | índice (Z) — **não usado hoje** | 🟢 |
| PB6, PB7 | M1_ENC_A, M1_ENC_B | encoder do motor 1 | 🔒 |
| PC15 | M1_ENC_Z | índice do motor 1 | 🔒 |

O `Z` do nosso encoder **não está ligado**. Ligá-lo permitiria calibração consistente entre boots —
está registrado como trabalho futuro, não como defeito.

#### Comunicação

| Pino | Nome | O que é | |
|---|---|---|---|
| PA11, PA12 | USB D−, D+ | **a USB de dados** — é por aqui que o jogo e o app falam | 🔵 |
| PA13, PA14 | SWDIO, SWCLK | gravação e depuração (ST-Link) | 🔵 |
| PB8, PB9 | CAN / I²C | barramento CAN ou I²C | 🟢 |
| PB10, PB11 | AUX_L, AUX_H | **resistor de freio (chopper)** | 🔵 |

⚠️ **PB10/PB11 chaveiam o resistor de freio.** Não são GPIOs comuns — comandam potência.

#### Os oito GPIOs do conector

Estes são os **livres para uso**. Só os cinco primeiros aceitam sinal analógico.

| GPIO | Pino | Analógico? | Uso no DriveLab |
|---|---|---|---|
| 1 | PA0 | ✅ ADC1_IN0 | 🟢 livre |
| 2 | PA1 | ✅ ADC1_IN1 | 🟢 livre |
| 3 | PA2 | ✅ ADC1_IN2 | 🟢 livre |
| **4** | **PA3** | ✅ ADC1_IN3 | 🔵 **NTC do motor** (padrão do ODrive) |
| 5 | PC4 | ✅ ADC1_IN14 | 🟢 reserva analógica |
| 6 | PB2 | ❌ | 🔵 botão de centralização *(previsto)* |
| 7 | PA15 | ❌ | 🔵 contator / soft-power *(previsto)* |
| 8 | PB3 | ❌ | 🟢 reserva digital |

> **PA15 e PB3 são pinos de JTAG** (JTDI e JTDO). Não atrapalham o nosso SWD, que usa PA13/PA14, mas
> o firmware precisa liberá-los explicitamente antes de usá-los como GPIO. Se puder escolher,
> prefira o GPIO 6 (PB2), que não tem essa pegadinha.

---

### Montagem: sensor de temperatura do motor (NTC)

Mede a temperatura do motor para desarmar antes de cozinhar o enrolamento.

    3,3 V ──[ 1 kΩ ]──┬── GPIO 4 (PA3)
                      │
                  [  NTC  ]  ← 10 kΩ, junto do enrolamento
                      │
                     GND
                      
    (capacitor de 100 nF entre PA3 e GND, junto da placa)

**Por que 1 kΩ e não 10 kΩ.** O casamento óbvio seria 10 k, igual ao NTC. Mas o firmware converte
tensão em temperatura com um polinômio de **grau 3**, e a curva do NTC é não-linear demais para
isso. O pull-up desloca a região linear para onde ela importa:

| pull-up | erro máximo entre 30 e 140 °C |
|---|---|
| 10 kΩ | **8 °C** |
| 4,7 kΩ | 5 °C |
| **1 kΩ** | **menos de 1 °C** |

Numa proteção que desarma a 105 °C, essa diferença separa proteger de não proteger.

**Por que o NTC vai para o GND, e não para o 3,3 V.** É escolha de segurança: se o cabo até o motor
entrar em curto, a leitura vai para temperatura altíssima e a proteção **desarma**. Com o NTC do
outro lado, um curto seria lido como "está frio" enquanto o motor cozinha.

**Coeficientes** para `motor_thermistors[0].config_.thermistor_poly_coeffs`, na ordem que o firmware
espera (maior grau primeiro — `T = c₀v³ + c₁v² + c₂v + c₃`, com `v` de 0 a 1):

| Beta do NTC | coeficientes | erro |
|---|---|---|
| 3435 | `{-330.06426335f, 581.36664616f, -493.02267122f, 241.78932775f}` | 1,1 °C |
| 3950 | `{-290.08890749f, 500.08591979f, -411.27304570f, 204.40932711f}` | 0,9 °C |

**Descobrir o Beta do seu NTC** (os anúncios raramente informam, e errar custa 7 °C no ponto de
desarme): mergulhe o sensor em **água fervendo** e meça com o multímetro.

| leitura a 100 °C | Beta |
|---|---|
| ~990 Ω | 3435 |
| ~700 Ω | 3950 |

Confira também ~10 kΩ à temperatura ambiente; se não der, o sensor não é de 10 k.

**Cuidados de montagem:**

- passe o cabo do NTC **longe dos cabos de fase** — eles chaveiam a 24 kHz e induzem ruído
- prenda o sensor junto do **enrolamento**, não na carcaça: a carcaça demora a esquentar e a
  proteção chegaria tarde
- a tolerância do resistor quase não importa: 5% custa ~2 °C, 1% custa ~0,4 °C. Qualquer valor entre
  **820 Ω e 1,2 kΩ** serve

---

### Montagem: botão de centralização

Define o zero do volante sem precisar do app.

    GPIO 6 (PB2) ──┬──[ botão ]── GND
                   │
                 [ 10 kΩ ]
                   │
                 3,3 V

O pull-up interno do STM32 dispensa o resistor externo, mas um de 10 kΩ deixa o sinal mais imune a
ruído num cabo longo até o aro. Botão **normalmente aberto**, fechando para o GND.

---

### Montagem: contator das fases

Isola as fases do motor quando a base está desligada. **Por que existe:** girar o volante com a base
desligada faz o motor gerar tensão (back-EMF) que volta para o barramento — e o resistor de freio
não protege nesse estado, porque ele depende do firmware rodando.

    GPIO 7 (PA15) ──[ 1 kΩ ]── base do transistor ──> bobina do contator (12/24 V)
                                                       (diodo de roda-livre em paralelo!)

⚠️ **O diodo de roda-livre na bobina não é opcional.** Sem ele, o pico de tensão ao desligar a bobina
volta pelo transistor e mata o pino do microcontrolador.

⚠️ O contator precisa ser de **3 polos** e chavear as três fases juntas. Cortar só uma ou duas deixa
caminho para a corrente circular.

---

### Antes de ligar qualquer coisa

1. **Confira a serigrafia.** Este documento diz `microcontrolador → função`; a posição no conector é
   do fabricante. Um GPIO trocado por uma fase queima a placa.
2. **Meça antes de soldar.** Com a placa desligada, confirme com o multímetro que o pino do conector
   é o que você pensa que é.
3. **Nada de tensão acima de 3,3 V nos GPIOs.** Eles não são tolerantes a 5 V.
4. **Ligue com o motor desarmado** e a fonte desligada.
