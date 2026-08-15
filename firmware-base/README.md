# firmware-base — Direct-Drive wheelbase

Firmware for the **direct-drive base** — the FFB motor stage. Runs on **STM32F405 ODrive v3.6-class** boards, validated on an **MKS ODRIVE-S V3.6-S6V**.

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

---

## 🇬🇧 English

The game sees the base as a DirectInput wheel with force feedback: no plugin, no extra driver.

## Architecture

Rather than writing field-oriented control from scratch, this firmware builds on two proven foundations and keeps the force-feedback layer as ours:

| Layer | Source | License |
|---|---|---|
| FOC, motor control, calibration | **ODrive v0.5.6**, vendored in `vendor/odrive-fw` | MIT |
| USB stack | **TinyUSB**, vendored in `vendor/tinyusb` | MIT / Apache |
| ODrive bridge, FFB task, USB descriptors, persistence | **ours** (`src/`, `inc/`) | MIT |
| FFB effects (spring, damper, friction, inertia) | **ours** (`engine.step`) | MIT |
| Configuration and telemetry | **our** DriveLab Studio app | MIT |

The whole project is MIT, and every source file carries a header saying so.

## Status (2026-08-12)

**Working and validated on the bench:**

- Motor running under FOC, arming repeatably.
- Force feedback validated in-game: **11 laps at Monza in Assetto Corsa Competizione** without
  losing FFB or disarming, plus AMS2 and EVO. Monza matters here — its chicanes are what exposed
  the regen voltage loss back in August.
- **Torque constant measured** (0.397 Nm/A) instead of the 0.55 from a generic catalogue formula,
  which was overstating delivered force by 39%.
- **Response curve back to linear.** It had been sitting at 1.59, which flattened mid-range forces
  by half — the game asked for 50% and 33% arrived.
- Settings persisted to the board's own flash (`CMD_SAVE`) — the base is the source of truth, not
  the app.
- Live telemetry: bus voltage, motor current with positive and negative peaks, **FET temperature**
  (43 °C at rest), clipping split into the game's share and the base's share.
- Protections: brake-resistor chopper, over-current cutoff in the ISR, soft stop, under- and
  over-voltage trips sized from the measured supply, electrical-angle coherence guard, overspeed
  guard.

**Built and awaiting bench validation:**

- Configurable FET thermal derating (the board starts backing off at 85 °C instead of the ODrive
  default of 100 °C).
- Force output filter.
- Encoder guard: a sensor/interface combination the firmware cannot drive now applies **nothing**,
  instead of applying the resolution over an A/B/Z reading.
- Force response curve with 11 points and smooth interpolation.

**Pending:**

- **The motor has no temperature sensor.** `MotorTempC` reports -128 and there is no thermal cutoff
  for the motor itself — the only protection is your hand on it. Fitting an NTC is what unlocks
  raising the current limit safely.
- MCU temperature is **deliberately disabled**: reading it shared ADC1 with the current sense and
  cost the FFB. See the note in `src/motor_link.cpp`.
- The encoder is still incremental, so the centre is not kept across power cycles. See
  [`../docs/encoders.md`](../docs/encoders.md).

## Build

Needs **ARM GCC** (`arm-none-eabi-`) and Python with `pyyaml`, `jinja2` and `jsonschema` — ODrive generates code at build time.

The PlatformIO toolchain works and is what we use, so the build comes out identical on macOS and Windows:

```bash
export PATH="$HOME/.platformio/packages/toolchain-gccarmnoneeabi/bin:$PATH"
cd firmware-base
make -j8
```

Output lands in `build/`: `drivelab-base.elf`, `.bin` and `.hex`.

## Flashing

**Over DFU (USB, no programmer):** this is the normal path, and DriveLab Studio does it for you. If the board will not enter DFU, see the [FAQ](../docs/faq-hoverboard.md#cant-flash-the-firmware) — on many boards the manual method is mandatory, and on some the jumper has to be **removed**, not fitted.

**Over ST-Link (SWD):**

```bash
openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
        -c "program build/drivelab-base.elf verify reset exit"
```

> ⚠️ Do not halt the core over SWD while the motor is **armed** — that drops the motor mid-control. To watch the firmware run, use the USB serial (CDC) instead.

## Power supply

Every number below was **measured on our own bench** or read from the firmware source, not taken
from a datasheet. The motor is a hoverboard motor; the board is a v3.6-class 56 V variant.

| Measured | Value | Where it comes from |
|---|---|---|
| Phase resistance | **0.2016 Ω** | measured on this motor (`motor_link.cpp`) |
| Torque constant Kt | **0.397 Nm/A** | measured 2026-08-11 (234 samples, regression through origin) |
| Bench supply | 27 V / 30 A | |
| Regen peak, no brake resistor | **33.5 V** from a 27.1 V supply | on track |
| Same peak, with a 2 Ω brake resistor | **27.5 V** (3.3 W average) | on track |

### What the supply actually has to deliver

The current your **supply** provides is not the current your **motor** draws. The motor pulls its
current through the winding resistance, and that is all the supply has to cover:

```
P = 1.5 × R × I²        (the 1.5 is the three-phase conversion: Iq is the phase-current amplitude)
P = 1.5 × 0.2016 × 25²  = 189 W at the 25 A default current limit
```

At 27 V that is **7 A from the supply**, while the motor sees 25 A in the windings.

The formula is confirmed by our own bench: a fault once locked the motor at 18.05 A, and
`1.5 × 0.2016 × 18.05² = 98.5 W`, matching the ~98 W of heat recorded at the time.

### Voltage does not buy torque

Torque is `current × Kt`. Voltage only has to be enough to push that current through the winding —
`R × I = 0.2016 × 25 ≈ 5 V` with the wheel held still.

Voltage starts to matter only at speed, where the motor's own back-EMF eats the headroom. Working
out where a 27 V bus would run out:

```
available phase voltage ≈ 27 / √3 ≈ 15.6 V
back-EMF = λ × ω_electrical,  λ = Kt / (1.5 × pole_pairs) = 0.0176 V·s/rad
15.6 = 0.0176 × 15 × ω_mechanical   →   ω ≈ 59 rad/s ≈ 9.4 rev/s
```

**9.4 revolutions per second.** On a wheel with 2.5 turns lock to lock, that never happens. A 48 V
bus pushes it to 16.7 rev/s — headroom you will never use.

### What a higher voltage does cost

Regeneration adds on top of the supply. When you reverse the wheel quickly the motor returns
energy, and the bus rises. On our bench that was **+6.5 V** (27.1 V supply, 33.5 V peak, no brake
resistor).

The firmware sizes its limits from the supply it measures when arming
(`motor_link_autoscale_bus_limits`):

| | 27 V supply | 48 V supply |
|---|---|---|
| brake-chopper ramp starts | 29 V | 50 V |
| ramp ends | 31 V | 52 V |
| over-voltage trip | 33 V | 54 V |
| measured regen peak (no resistor) | 33.5 V | ~54.5 V |

At 27 V the peak lands inside the ramp and the resistor absorbs it. **At 48 V the same event lands
on the trip.** The board's own ceiling is 55 V (bus capacitors ~63 V), so the margin goes from
comfortable to none — and if that +6.5 V grows with voltage instead of staying fixed (we have one
data point, so we do not know), it goes over.

### Recommendation for this class of motor

- **24–30 V is the sweet spot.** It delivers full torque, keeps a wide regen margin, and is easy on
  the bus capacitors.
- **Size the supply by power, not by motor current.** ~200 W covers the 25 A default; a 30 A supply
  is roughly four times what is needed.
- **A brake resistor is not optional**, and above ~36 V it stops being a safety net and becomes part
  of normal operation.
- **More force comes from more current, not more volts** — and current costs heat with the *square*:
  25 A → 9.75 Nm and 189 W; 30 A → 11.7 Nm and 272 W. Fit a motor thermistor before raising it.

### Under-voltage is a real failure mode too

A supply that sags under load trips the base just as surely as one that spikes. The firmware keeps
the under-voltage trip fixed at **8 V** for exactly this reason: a trip level of 14.79 V once caused
repeated cut-outs during slow, high-current corners, because the bus dipped to 14.79 V under load
and the base disarmed. Long or thin supply leads make this worse.

## Safety

Read [`../docs/faq-hoverboard.md#safety`](../docs/faq-hoverboard.md#safety) before powering a motor. The two that cost the most:

- **Match the supply voltage to your board variant** (24 V or 56 V). Going over destroys the board without warning.
- **A brake resistor is mandatory** before any closed-loop torque.

---

## 🇧🇷 Português

Firmware da **base direct-drive** (o estágio do motor de FFB). Roda em placas
**STM32F405 classe ODrive v3.6** — validado numa **MKS ODRIVE-S V3.6-S6V**.

O jogo enxerga a base como um volante DirectInput com force feedback: nenhum plugin,
nenhum driver extra.

## Arquitetura

Em vez de escrever uma FOC do zero, o firmware monta duas fundações prontas e maduras, e
a camada de FFB é nossa:

| Camada | Origem | Licença |
|---|---|---|
| FOC, controle do motor, calibração | **ODrive v0.5.6**, vendorizado em `vendor/odrive-fw` | MIT |
| Pilha USB | **TinyUSB**, vendorizado em `vendor/tinyusb` | MIT / Apache |
| Ponte com o ODrive, task de FFB, descritores USB, persistência | **nosso** (`src/`, `inc/`) | MIT |
| Efeitos de FFB (mola, damper, atrito, inércia) | **nosso** (`engine.step`) | MIT |
| Configuração e telemetria | **nosso** app DriveLab Studio | MIT |

O projeto inteiro é MIT, e cada arquivo-fonte traz o cabeçalho declarando isso.

## Estado (2026-08-12)

**Funcionando e validado em bancada:**

- Motor rodando em FOC, armando de forma repetível.
- Force feedback validado em jogo: **11 voltas em Monza no Assetto Corsa Competizione** sem perder
  FFB e sem desarme, mais AMS2 e EVO. Monza importa aqui — foram as chicanes dela que expuseram a
  perda de força por regeneração.
- **Constante de torque medida** (0,397 Nm/A) no lugar dos 0,55 de uma fórmula genérica de
  catálogo, que superestimava a força entregue em 39%.
- **Curva de resposta de volta ao linear.** Estava em 1,59, o que achatava as forças médias pela
  metade — o jogo pedia 50% e chegavam 33%.
- Persistência dos ajustes na flash da própria placa (`CMD_SAVE`) — a base é a fonte da verdade,
  não o app.
- Telemetria ao vivo: tensão do barramento, corrente do motor com picos positivo e negativo,
  **temperatura dos FETs** (43 °C em repouso), clipping separado entre a parcela do jogo e a da base.
- Proteções: chopper do resistor de freio, corte por sobrecorrente na ISR, batente por software,
  trips de sub e sobretensão dimensionados pela fonte medida, guarda de coerência do ângulo
  elétrico, guarda de sobrevelocidade.

**Feito e aguardando validação em bancada:**

- Corte térmico dos FETs configurável (a placa passa a recuar a partir de 85 °C, em vez dos 100 °C
  padrão do ODrive).
- Filtro de saída da força.
- Trava do encoder: combinação de sensor e interface que o firmware não sabe acionar passa a não
  aplicar **nada**, em vez de aplicar a resolução sobre uma leitura A/B/Z.
- Curva de resposta da força com 11 pontos e interpolação suave.

**Pendente:**

- **O motor não tem sensor de temperatura.** O `MotorTempC` reporta -128 e não existe corte térmico
  para o motor — a única proteção é a sua mão nele. Instalar um NTC é o que destrava subir o limite
  de corrente com segurança.
- A temperatura do MCU está **desligada de propósito**: lê-la compartilhava o ADC1 com o sensor de
  corrente e custou o FFB. Ver a nota em `src/motor_link.cpp`.
- O encoder ainda é incremental, então o centro não sobrevive ao desligar. Ver
  [`../docs/encoders.md`](../docs/encoders.md).

## Build

Precisa do **ARM GCC** (`arm-none-eabi-`) e do Python com `pyyaml`, `jinja2` e
`jsonschema` — o ODrive gera código na hora do build.

O toolchain do PlatformIO serve e é o que usamos, pra o build sair idêntico no Mac e no
Windows:

```bash
export PATH="$HOME/.platformio/packages/toolchain-gccarmnoneeabi/bin:$PATH"
cd firmware-base
make -j8
```

Saída em `build/`: `drivelab-base.elf`, `.bin` e `.hex`.

## Gravar

**Por DFU (pelo USB, sem gravador):** é o caminho normal, e o DriveLab Studio faz isso
sozinho. Se a placa não entrar em DFU, veja o
[FAQ](../docs/faq-hoverboard.md#não-consigo-gravar-o-firmware) — em muitas placas o modo
manual é obrigatório, e em algumas o jumper precisa ser **retirado**, não colocado.

**Por ST-Link (SWD):**

```bash
openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
        -c "program build/drivelab-base.elf verify reset exit"
```

> ⚠️ Com o motor **armado**, não pare o núcleo pelo SWD — isso derruba o motor no meio do
> controle. Para acompanhar o firmware rodando, use a serial USB (CDC).

## Fonte de alimentação

Todos os números abaixo foram **medidos na nossa bancada** ou lidos do código do firmware — nenhum
veio de datasheet. O motor é de hoverboard; a placa é da classe v3.6, variante 56 V.

| Medido | Valor | De onde vem |
|---|---|---|
| Resistência de fase | **0,2016 Ω** | medida neste motor (`motor_link.cpp`) |
| Constante de torque Kt | **0,397 Nm/A** | medida em 11/08/2026 (234 amostras, regressão pela origem) |
| Fonte da bancada | 27 V / 30 A | |
| Pico de regeneração, sem resistor de freio | **33,5 V** com fonte de 27,1 V | na pista |
| O mesmo pico, com resistor de 2 Ω | **27,5 V** (3,3 W médios) | na pista |

### O que a fonte precisa entregar de verdade

A corrente que a **fonte** fornece não é a corrente que o **motor** puxa. O motor puxa a corrente
dele através da resistência do enrolamento, e é só isso que a fonte tem que cobrir:

```
P = 1,5 × R × I²        (o 1,5 é da conversão trifásica: Iq é a amplitude da corrente de fase)
P = 1,5 × 0,2016 × 25²  = 189 W no limite padrão de 25 A
```

A 27 V isso são **7 A da fonte**, enquanto o motor vê 25 A nas bobinas.

A fórmula é confirmada pela nossa própria bancada: uma falha travou o motor em 18,05 A, e
`1,5 × 0,2016 × 18,05² = 98,5 W`, batendo com os ~98 W de calor registrados na época.

### Tensão não compra torque

Torque é `corrente × Kt`. A tensão só precisa ser suficiente para empurrar essa corrente pelo
enrolamento — `R × I = 0,2016 × 25 ≈ 5 V` com o volante parado.

Ela só passa a importar em velocidade, onde a geração do próprio motor come a margem. Calculando
onde um barramento de 27 V se esgotaria:

```
tensão de fase disponível ≈ 27 / √3 ≈ 15,6 V
back-EMF = λ × ω_elétrico,  λ = Kt / (1,5 × pares_de_polos) = 0,0176 V·s/rad
15,6 = 0,0176 × 15 × ω_mecânico   →   ω ≈ 59 rad/s ≈ 9,4 voltas/s
```

**9,4 voltas por segundo.** Num volante de duas voltas e meia de batente a batente, isso nunca
acontece. Um barramento de 48 V levaria para 16,7 voltas/s — margem que você jamais vai usar.

### O que uma tensão maior custa

A regeneração soma em cima da fonte. Ao reverter o volante rápido o motor devolve energia e o
barramento sobe. Na nossa bancada isso foi **+6,5 V** (fonte de 27,1 V, pico de 33,5 V, sem
resistor de freio).

O firmware dimensiona os limites pela fonte que mede no arme
(`motor_link_autoscale_bus_limits`):

| | fonte de 27 V | fonte de 48 V |
|---|---|---|
| rampa do chopper começa | 29 V | 50 V |
| rampa termina | 31 V | 52 V |
| corte por sobretensão | 33 V | 54 V |
| pico de regeneração medido (sem resistor) | 33,5 V | ~54,5 V |

A 27 V o pico cai dentro da rampa e o resistor o absorve. **A 48 V o mesmo evento cai em cima do
corte.** O teto da própria placa é 55 V (capacitores de barramento ~63 V), então a margem passa de
confortável a nenhuma — e se aquele +6,5 V crescer com a tensão em vez de ser fixo (temos um único
ponto de medição, então não sabemos), passa do limite.

### Recomendação para este tipo de motor

- **24 a 30 V é o ponto ideal.** Entrega o torque inteiro, mantém margem larga de regeneração e é
  leve com os capacitores.
- **Dimensione a fonte pela POTÊNCIA, não pela corrente do motor.** ~200 W cobrem o padrão de 25 A;
  uma fonte de 30 A é cerca de quatro vezes o necessário.
- **Resistor de freio não é opcional**, e acima de ~36 V ele deixa de ser rede de segurança e passa
  a fazer parte do funcionamento normal.
- **Mais força vem de mais corrente, não de mais volts** — e corrente custa calor pelo *quadrado*:
  25 A dão 9,75 Nm e 189 W; 30 A dão 11,7 Nm e 272 W. Instale o termistor no motor antes de subir.

### Subtensão também derruba

Fonte que afunda sob carga desarma a base tanto quanto uma que estoura. O firmware mantém o corte
por subtensão fixo em **8 V** justamente por isso: um limite de 14,79 V já causou cortes repetidos
em curvas lentas de corrente alta, porque o barramento caía a 14,79 V sob carga e a base desarmava.
Cabos longos ou finos pioram isso.

## Segurança

Leia [`../docs/faq-hoverboard.md#segurança`](../docs/faq-hoverboard.md#segurança) antes de
energizar um motor. Os dois pontos que mais custam caro:

- **Case a tensão da fonte com a variante da placa** (24 V ou 56 V). Passar disso destrói
  a placa sem aviso.
- **Resistor de freio é obrigatório** antes de qualquer torque em malha fechada.
