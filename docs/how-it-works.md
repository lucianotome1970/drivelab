# DriveLab — How It Works (Study Guide) / Como Funciona (Guia de Estudo)

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> **What this is.** A newcomer-friendly guide to how a direct-drive (DD) sim-racing wheel
> works, grounded in the **real DriveLab architecture** — not generic theory. It's built from
> the community course *"Motores BLDC, FOC e Direct Drive"* and from the engineering decisions
> made while building this project. Read it to understand *what each part does, why we chose it,
> and where it lives in the repo.*
>
> **Who it's for.** Anyone building on DriveLab or just wanting to understand a modern
> servo-drive. No prior motor-control knowledge assumed.
>
> ⚠️ **Safety first.** A DD controller moves the wheel with enough force to hurt and runs
> currents that can destroy parts. Always test with **limited current and voltage**, with a
> **fuse** and a way to cut power, and a **clear area** around the wheel. Measurement beats
> "feel" — log current, voltage and temperature.

---

## 🇬🇧 English

### 1. The big picture: a DD wheel is a servosystem

It receives a **torque command**, continuously **measures** position and current, computes the
action needed, and corrects the result thousands of times per second.

```
Game / USB HID
      │
      ▼  "I want 6 N·m"
Microcontroller + FOC algorithm
      │            ▲
      ▼            │
PWM → Driver → MOSFETs ◄── current sensors
      │
      ▼
BLDC motor ◄────── encoder (position)
      │
      ▼
Torque at the wheel

Regen energy → DC bus → brake resistor
```

**The 4-block mental model** (great for debugging — you isolate the block, not guess):

| Block | DriveLab parts | Human-body analogy |
|---|---|---|
| **Brain** | STM32F405 (FOC + Force Feedback) | Brain |
| **Senses** | Encoder + current/voltage/temp sensors | Proprioception + sense of effort |
| **Muscle** | BLDC hub motor | Muscle |
| **Circulation** | PSU, DC bus, MOSFETs, capacitors | Circulatory system |

*If the wheel vibrates → suspect the senses (encoder). If torque is missing → the muscle/circulation
(current, MOSFETs, bus). If behaviour is weird → the brain (FOC, filters, PWM).*

### 2. Physics you actually need

- **Torque** is force at a distance from the axis: `T = F × r`. A 9 N·m base on a 0.30 m wheel
  (r = 0.15 m) gives ~60 N at the rim. A **bigger rim lowers** the felt force for the same torque.
- **Power** is `P = T × ω` (ω in rad/s). A **stalled motor** makes torque but ~zero mechanical
  power — yet still draws current and heats up (like pushing a wall: you tire and warm up, the
  wall doesn't move).
- **Current makes torque**: within the linear range, `T ≈ Kt × Iq` (Iq = the torque-producing
  current). Voltage doesn't make torque directly — it gives *headroom* to push current, especially
  as the motor spins and generates Back-EMF.
- **Current makes heat**: `P_copper = I_rms² × R`. Double the current → **4×** the heating. This
  is why we care about *RMS* current, not peaks.
- **Magnetism**: current in a coil creates a magnetic pole; reverse the current, reverse the pole.
  The controller sets one **global stator field orientation**; all rotor magnets interact with it
  at once — no need to address magnets individually.
- **Pole pairs** (the big one for us): our hub motor has **30 magnets = 15 pole pairs**. The
  electrical angle is `θ_elec = pole_pairs × θ_mech`, so **1° of mechanical error = 15° electrical**.
  This is *exactly* why a loose/wobbling encoder wrecked our FOC: the wobble was amplified 15×,
  commutation went to the wrong angle → high current, no torque, heat.

### 3. The motor (muscle)

A **BLDC outrunner** hub motor: the coil **stator is fixed** on the shaft, the **magnet rotor**
(the outer shell) spins. Three wires = phases **A, B, C**; internally many coils are grouped per
phase. Our rotor has 30 alternating magnets → **15 pole pairs**. Gearless = no backlash, which is
why DD feels "direct". See **[Motor selection notes]** in the project docs for sizing (peak vs
continuous N·m, KV, diameter).

### 4. Seeing position: the encoder (senses)

FOC *must* know the rotor angle to energise the right coils. DriveLab supports three sensors that
all feed the same code (via SimpleFOC's `Sensor` interface):

| Sensor | Type | Keeps center after power-off? | Status |
|---|---|---|---|
| **Omron E6B2-CWZ6C** | Optical incremental (ABZ) | ❌ needs an alignment "dance" each boot | Current bench sensor |
| **MT6701** | Magnetic absolute | ✅ | Planned default |
| **AS5047P** | Magnetic absolute + SPI diagnostics | ✅ | Planned premium |

Because of the 15× pole-pair amplification, encoder **rigidity and on-axis mounting matter enormously**.
Full trade-offs: **[encoders.md](encoders.md)**.

### 5. Making torque: MOSFETs, driver, and FOC (brain)

- **MOSFETs** are ultra-fast electronic valves. Six of them (a 3-phase bridge) switch ~**20,000
  times/second**, dosing energy from the DC bus into the coils. They don't *push* energy — the PSU
  does; the MOSFETs meter it precisely (garden-hose analogy: PSU = water tank, MOSFETs = fast valve,
  coils = turbine).
- The **gate driver** (our board: **DRV8301**) translates the MCU's logic PWM into the high-current
  gate drive the MOSFETs need, and reports faults.
- **FOC (Field-Oriented Control)** is the brain's algorithm. Each control tick it: reads the
  currents, reads the angle, transforms to a rotor-aligned frame (**Clarke → Park**), runs a **PI**
  loop to hold the desired torque current (Iq) and zero the wasteful current (Id), then transforms
  back and generates the phase PWM (**inverse Park → SVPWM**). We use **SimpleFOC** for this.

### 6. Sensing: current, voltage, temperature

- **Phase current** (shunts + amplifiers) → the FOC loop's feedback *and* the torque/thermal limit.
- **Bus voltage** → headroom, and the trigger for brake/over-voltage protection.
- **Temperature** (motor/FET NTCs, MCU internal) → thermal limiting so nothing cooks.

### 7. Power & protection

- **DC bus**: PSU + big capacitors. Match the PSU to the board variant (24 V or 56 V).
- **Brake resistor (on-state protection)**: when the wheel is back-driven, the motor regenerates
  into the bus and voltage rises. A **chopper** (a MOSFET half-bridge on the AUX output) dumps that
  energy into a **2 Ω** resistor as heat. Why 2 Ω, and Ω vs W ratings: **[brake-resistor.md](brake-resistor.md)**.
- **Off-state protection (contactor)**: with the board *off*, spinning the motor rectifies Back-EMF
  through the MOSFET body diodes into the bus — the chopper can't help (it's not switching). A
  **3-pole contactor** on the phases physically disconnects the motor. It must **close only after the
  controller is ready and open before power is cut**, never switching under high torque.
- **Soft-power (UX)**: an optional tap-to-on / hold-to-off button that runs a **sequenced shutdown**
  (disarm motor → open contactor → cut). Both features are **opt-in and off by default**. Details and
  wiring: **[soft-power-contator.md](soft-power-contator.md)**.

> **Rule that ties it together:** the E-stop / power cut must act on the **hardware power path**,
> not just send a firmware command — a critical safety function needs a path that survives firmware
> hanging (like an elevator's emergency brake, which doesn't depend on the app).

### 8. Force Feedback: from the game to the wheel

The wheel presents to the PC as a **USB HID** device with Force Feedback. The game creates/updates
effects; the firmware turns them into torque:

| Effect | Interpretation |
|---|---|
| Constant force | Direct torque |
| Spring | Torque ∝ displacement (return-to-center) |
| Damper | Torque ∝ velocity (viscous) |
| Friction | Torque opposing motion (dry) |
| Inertia | Torque ∝ acceleration (virtual mass) |
| Periodic | Sine/square/other waveforms |

**The pipeline** (our `engine.step()`): receive USB → validate timing/sequence → update active
effects → compute virtual torque → apply per-effect + global gains → apply filters/limits →
convert torque to Iq → run the FOC loop. The **FOC loop runs far faster than USB** (kHz vs ~1 kHz),
so the firmware holds the last command and interpolates/limits changes.

- **Slew rate** limits how fast torque may change — protects mechanics, avoids exciting resonances;
  too low erases detail.
- **Safe-zero**: on losing USB, encoder, or sync, the torque reference converges to zero (or cuts
  immediately, if that's safer).
- **Sign conventions** (positive torque, encoder direction, HID scaling) must be documented — many
  bugs are just an inverted sign or a double conversion. (We hit exactly this with `sensor_direction`.)

The full USB-HID contract is in **[PROTOCOL.md](PROTOCOL.md)** — implement it and DriveLab Studio
drives your own board.

### 9. Safety: system states and how to test

A predictable **state machine** keeps things safe:

| State | Bridge | Phase contactor | Torque |
|---|---|---|---|
| Off | no command | open | zero |
| Init | disabled | open | zero |
| Calibration | limited | closed | low & controlled |
| Ready | enabled | closed | allowed |
| Fault | disabled | per safe strategy | zero / defined braking |

**Watchdogs** (MCU, USB timeout, encoder timeout, current-loop update check) catch different
failures — one alone doesn't cover them all. **Test progressively**: start with limited current/voltage,
inject one fault at a time (unplug encoder, interrupt USB, disconnect the brake resistor) and confirm
the wheel goes to a safe state.

### 10. Systematic diagnosis

Change **one variable at a time**, measure, and start with limited power — don't mask an encoder
problem by raising current or filters.

**Wheel vibrates** → check: CPR/counts (encoder scale) · pole pairs (electrical angle drift) ·
direction (inverted sign) · offset (misaligned field) · magnet/mount (eccentricity — *our loose
encoder!*) · phase order · current-loop PI gains.

**Motor heats at standstill** → Iq/Id not as expected · wrong current offset · noisy encoder causing
corrections · wrong electrical angle · position/velocity gain active without need. (*This was our
13 A-at-the-wrong-angle case.*)

**Low torque** → low current limit · wrong Kt · bus sags under load · a phase not conducting · wrong
electrical angle · FFB clipping/limit in the pipeline.

### 11. How to study this repo

- **Milestones**: `M0` toolchain → `M0.5` USB FFB → `M1` open-loop motor → `M2` encoder + closed
  loop + brake resistor → `M3` app↔firmware → `M4` settings → `M5` FFB force → motor → `M6` game
  effects → `M7` sim validation.
- **Deeper docs**: [encoders.md](encoders.md) · [brake-resistor.md](brake-resistor.md) ·
  [soft-power-contator.md](soft-power-contator.md) · [PROTOCOL.md](PROTOCOL.md) ·
  [guia-criador.md](guia-criador.md).
- **Where the code lives**: the FFB/motor firmware is under `firmware-base/` (SimpleFOC + the FOC
  state machine); the pure, host-testable "brain" (torque pipeline, brake control, safety guards)
  lives in `firmware-base/lib/brain/` and `lib/base_motor/`; the desktop app is under `app/`.

---

## 🇧🇷 Português

### 1. O panorama: um volante DD é um servossistema

Ele recebe um **comando de torque**, **mede** continuamente a posição e a corrente, calcula a ação
necessária e corrige o resultado milhares de vezes por segundo.

```
Jogo / USB HID
      │
      ▼  "Quero 6 N·m"
Microcontrolador + algoritmo FOC
      │            ▲
      ▼            │
PWM → Driver → MOSFETs ◄── sensores de corrente
      │
      ▼
Motor BLDC ◄────── encoder (posição)
      │
      ▼
Torque no volante

Energia regenerativa → barramento DC → resistor de frenagem
```

**O modelo mental de 4 blocos** (ótimo pra depurar — você isola o bloco, não chuta):

| Bloco | Peças do DriveLab | Analogia com o corpo |
|---|---|---|
| **Cérebro** | STM32F405 (FOC + Force Feedback) | Cérebro |
| **Sentidos** | Encoder + sensores de corrente/tensão/temperatura | Propriocepção + sentido de esforço |
| **Músculo** | Motor BLDC (hub) | Músculo |
| **Circulação** | Fonte, barramento DC, MOSFETs, capacitores | Sistema circulatório |

*Se o volante vibra → suspeite dos sentidos (encoder). Se falta torque → músculo/circulação
(corrente, MOSFETs, barramento). Se o comportamento está estranho → o cérebro (FOC, filtros, PWM).*

### 2. A física que você realmente precisa

- **Torque** é força a uma distância do eixo: `T = F × r`. Uma base de 9 N·m num aro de 0,30 m
  (r = 0,15 m) dá ~60 N no aro. Um **aro maior reduz** a força sentida para o mesmo torque.
- **Potência** é `P = T × ω` (ω em rad/s). Um **motor parado** faz torque mas ~zero potência
  mecânica — e mesmo assim puxa corrente e esquenta (como empurrar uma parede: você cansa e esquenta,
  a parede não anda).
- **Corrente faz torque**: na faixa linear, `T ≈ Kt × Iq` (Iq = corrente que gera torque). Tensão não
  faz torque direto — ela dá *margem* para estabelecer a corrente, principalmente quando o motor gira
  e gera Back-EMF.
- **Corrente faz calor**: `P_cobre = I_rms² × R`. Dobrar a corrente → **4×** o aquecimento. Por isso
  o que importa é a corrente *RMS*, não os picos.
- **Magnetismo**: corrente numa bobina cria um polo magnético; inverter a corrente inverte o polo. O
  controlador define **uma orientação global do campo do estator**; todos os ímãs do rotor interagem
  com ela ao mesmo tempo — sem precisar "endereçar" ímã por ímã.
- **Pares de polos** (o grande pra nós): nosso motor hub tem **30 ímãs = 15 pares de polos**. O ângulo
  elétrico é `θ_elec = pares × θ_mec`, então **1° de erro mecânico = 15° elétricos**. É *exatamente*
  por isso que um encoder solto/balançando destruiu nosso FOC: o balanço foi amplificado 15×, a
  comutação foi pro ângulo errado → corrente alta, sem torque, calor.

### 3. O motor (músculo)

Um **BLDC outrunner** (hub): o **estator com bobinas fica fixo** no eixo e o **rotor de ímãs** (a
carcaça externa) gira. Três fios = fases **A, B, C**; internamente várias bobinas são agrupadas por
fase. Nosso rotor tem 30 ímãs alternados → **15 pares de polos**. Sem caixa de redução = sem folga
(backlash), e é por isso que DD é "direto". Veja as notas de seleção de motor nos docs do projeto
(pico vs contínuo em N·m, KV, diâmetro).

### 4. Enxergar a posição: o encoder (sentidos)

O FOC *precisa* saber o ângulo do rotor pra energizar as bobinas certas. O DriveLab aceita três
sensores que alimentam o mesmo código (pela interface `Sensor` do SimpleFOC):

| Sensor | Tipo | Guarda o centro ao desligar? | Status |
|---|---|---|---|
| **Omron E6B2-CWZ6C** | Óptico incremental (ABZ) | ❌ precisa da "dança" de alinhamento a cada boot | Sensor atual da bancada |
| **MT6701** | Magnético absoluto | ✅ | Padrão planejado |
| **AS5047P** | Magnético absoluto + diagnóstico SPI | ✅ | Premium planejado |

Por causa da amplificação de 15× dos pares de polos, a **rigidez e a montagem no eixo do encoder
importam muito**. Trade-offs completos: **[encoders.md](encoders.md)**.

### 5. Fazer torque: MOSFETs, driver e FOC (cérebro)

- **MOSFETs** são válvulas eletrônicas ultra-rápidas. Seis deles (uma ponte trifásica) chaveiam
  ~**20.000 vezes/segundo**, dosando a energia do barramento pras bobinas. Eles não *empurram* a
  energia — a fonte empurra; os MOSFETs dosam com precisão (analogia da mangueira: fonte = caixa
  d'água, MOSFETs = válvula rápida, bobinas = turbina).
- O **driver de gate** (nossa placa: **DRV8301**) traduz o PWM lógico do MCU no acionamento de gate
  de alta corrente que os MOSFETs precisam, e reporta falhas.
- **FOC (Controle Orientado ao Campo)** é o algoritmo do cérebro. A cada tick ele: lê as correntes,
  lê o ângulo, transforma pro referencial alinhado ao rotor (**Clarke → Park**), roda um **PI** pra
  manter a corrente de torque (Iq) e zerar a corrente inútil (Id), e transforma de volta gerando o
  PWM das fases (**Park inversa → SVPWM**). Usamos o **SimpleFOC** pra isso.

### 6. Medir: corrente, tensão, temperatura

- **Corrente de fase** (shunts + amplificadores) → realimentação do FOC *e* o limite de torque/térmico.
- **Tensão do barramento** → margem, e o gatilho da proteção de freio/sobretensão.
- **Temperatura** (NTCs do motor/FET, interno do MCU) → limitação térmica pra nada cozinhar.

### 7. Energia & proteção

- **Barramento DC**: fonte + capacitores grandes. Case a fonte com a variante da placa (24 V ou 56 V).
- **Resistor de frenagem (proteção com a placa LIGADA)**: quando o volante é forçado ao contrário, o
  motor regenera no barramento e a tensão sobe. Um **chopper** (meia-ponte MOSFET na saída AUX) queima
  essa energia num resistor de **2 Ω** em calor. Por que 2 Ω, e Ω vs W: **[brake-resistor.md](brake-resistor.md)**.
- **Proteção com a placa DESLIGADA (contator)**: com a placa *desligada*, girar o motor retifica o
  Back-EMF pelos diodos de corpo dos MOSFETs pro barramento — o chopper não ajuda (não está chaveando).
  Um **contator de 3 polos** nas fases desconecta o motor fisicamente. Ele deve **fechar só depois que
  a controladora estiver pronta e abrir antes de cortar a energia**, nunca comutando sob torque alto.
- **Soft-power (conforto)**: um botão opcional de toque-liga / segura-desliga que roda um
  **desligamento sequenciado** (desarma motor → abre contator → corta). Os dois recursos são **opt-in
  e desligados por padrão**. Detalhes e fiação: **[soft-power-contator.md](soft-power-contator.md)**.

> **Regra que amarra tudo:** o E-stop / corte de energia deve atuar no **caminho de energia por
> hardware**, não só mandar um comando ao firmware — uma função crítica de segurança precisa de um
> caminho que sobrevive ao firmware travar (como o freio de emergência de um elevador, que não depende
> do aplicativo).

### 8. Force Feedback: do jogo ao volante

O volante se apresenta ao PC como um dispositivo **USB HID** com Force Feedback. O jogo cria/atualiza
efeitos; o firmware os transforma em torque:

| Efeito | Interpretação |
|---|---|
| Constant force | Torque direto |
| Spring | Torque ∝ deslocamento (retorno ao centro) |
| Damper | Torque ∝ velocidade (viscoso) |
| Friction | Torque contra o movimento (atrito seco) |
| Inertia | Torque ∝ aceleração (massa virtual) |
| Periodic | Ondas seno/quadrada/outras |

**O pipeline** (nosso `engine.step()`): recebe USB → valida tempo/sequência → atualiza efeitos ativos
→ calcula torque virtual → aplica ganhos por-efeito + global → aplica filtros/limites → converte torque
em Iq → roda o FOC. O **FOC roda muito mais rápido que o USB** (kHz vs ~1 kHz), então o firmware mantém
o último comando e interpola/limita as variações.

- **Slew rate** limita quão rápido o torque pode mudar — protege a mecânica, evita excitar ressonâncias;
  baixo demais apaga detalhe.
- **Zero seguro**: ao perder USB, encoder ou sincronismo, a referência de torque converge a zero (ou
  corta na hora, se for mais seguro).
- **Convenções de sinal** (torque positivo, sentido do encoder, escala do HID) precisam ser
  documentadas — muitos bugs são só um sinal invertido ou uma conversão duplicada. (Batemos exatamente
  nisso com o `sensor_direction`.)

O contrato USB-HID completo está em **[PROTOCOL.md](PROTOCOL.md)** — implemente-o e o DriveLab Studio
controla a sua própria placa.

### 9. Segurança: estados do sistema e como testar

Uma **máquina de estados** previsível mantém tudo seguro:

| Estado | Ponte | Contator de fases | Torque |
|---|---|---|---|
| Desligado | sem comando | aberto | zero |
| Inicialização | desabilitada | aberto | zero |
| Calibração | limitada | fechado | baixo e controlado |
| Pronto | habilitada | fechado | permitido |
| Falha | desabilitada | conforme estratégia segura | zero / frenagem definida |

**Watchdogs** (MCU, timeout de USB, timeout de encoder, verificação da malha de corrente) pegam falhas
diferentes — um sozinho não cobre todas. **Teste progressivamente**: comece com corrente/tensão
limitadas, injete uma falha por vez (tira o encoder, interrompe o USB, desconecta o resistor de freio)
e confirme que o volante vai pra um estado seguro.

### 10. Diagnóstico sistemático

Mude **uma variável por vez**, meça, e comece com energia limitada — não mascare um problema de encoder
aumentando corrente ou filtros.

**Volante vibra** → checar: CPR/contagem (escala do encoder) · pares de polos (deriva do ângulo
elétrico) · sentido (sinal invertido) · offset (campo desalinhado) · ímã/montagem (excentricidade — *o
nosso encoder solto!*) · ordem das fases · ganhos do PI de corrente.

**Motor aquece parado** → Iq/Id fora do esperado · offset de corrente errado · encoder ruidoso gerando
correções · ângulo elétrico errado · ganho de posição/velocidade ativo sem necessidade. (*Foi o nosso
caso dos 13 A no ângulo errado.*)

**Baixo torque** → limite de corrente baixo · Kt errado · barramento cai sob carga · uma fase sem
conduzir · ângulo elétrico errado · clipping/limite no pipeline de FFB.

### 11. Como estudar este repositório

- **Milestones**: `M0` toolchain → `M0.5` USB FFB → `M1` motor malha aberta → `M2` encoder + malha
  fechada + resistor de freio → `M3` app↔firmware → `M4` settings → `M5` força FFB → motor → `M6`
  efeitos de jogo → `M7` validação no sim.
- **Docs mais fundos**: [encoders.md](encoders.md) · [brake-resistor.md](brake-resistor.md) ·
  [soft-power-contator.md](soft-power-contator.md) · [PROTOCOL.md](PROTOCOL.md) ·
  [guia-criador.md](guia-criador.md).
- **Onde mora o código**: o firmware de FFB/motor está em `firmware-base/` (SimpleFOC + a máquina de
  estados FOC); o "cérebro" puro e testável no host (pipeline de torque, controle do freio, guardas de
  segurança) mora em `firmware-base/lib/brain/` e `lib/base_motor/`; o app desktop está em `app/`.

---

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT. Construído a partir do curso "Motores BLDC, FOC e Direct Drive" e das decisões de engenharia do projeto.</sub>
