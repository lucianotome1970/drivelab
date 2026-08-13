# DriveLab — Encoder Guide / Guia de Encoders

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> **Why four options?** A position sensor is what tells the base where the wheel is.
> DriveLab is meant to be built on a budget **or** with premium parts, so the plan is to
> support **four** sensors behind the same interface. This guide explains the trade-offs so
> you can pick what to buy.

> ⚠️ **Status — read before buying.** The firmware today drives **one** path: incremental
> **A/B/Z**. That is what the ACC / AMS2 / EVO validation ran on, with an Omron E6B2-CWZ6C.
>
> The app **lets you pick** a magnetic sensor and the SSI/SPI interface, and it saves your
> choice to the board — but the firmware only applies the **resolution**. It keeps reading in
> A/B/Z. So a sensor wired over SSI or SPI **will not work yet**, even though the screen
> accepts it.
>
> All four sensors also have an **A/B/Z output**. Wired that way, any of them works today.
>
> 🇧🇷 **Antes de comprar.** O firmware hoje aciona **um** caminho: **A/B/Z** incremental — é
> nele que rodou a validação em ACC / AMS2 / EVO, com um Omron E6B2-CWZ6C.
>
> O app **deixa escolher** sensor magnético e interface SSI/SPI, e salva a escolha na placa —
> mas o firmware só aplica a **resolução**. Ele continua lendo em A/B/Z. Ou seja: sensor
> ligado por SSI ou SPI **ainda não funciona**, mesmo a tela aceitando.
>
> Os quatro sensores também têm **saída A/B/Z**. Ligados assim, qualquer um funciona hoje.

---

## 🇬🇧 English

### The three sensors at a glance

| Quality | **E6B2-CWZ6C** (optical ABI) | **MT6701** (magnetic SSI) | **AS5047P** (magnetic SPI) | **MT6835** (magnetic SPI) |
|---|---|---|---|---|
| **Technology** | Optical incremental | Magnetic on-axis (Hall) | Magnetic on-axis (Hall) | Magnetic on-axis (**AMR**) |
| **Measurement** | ❌ Incremental | ✅ Absolute (single-turn) | ✅ Absolute (single-turn) | ✅ Absolute (single-turn) |
| **Keeps center after power-off** | ❌ No (home via Z) | ✅ Yes (within ±½ turn) | ✅ Yes (within ±½ turn) | ✅ Yes (within ±½ turn) |
| **FOC startup** | ⚠️ Alignment "dance" every boot | ✅ Immediate | ✅ Immediate | ✅ Immediate |
| **Resolution** | PPR da etiqueta × 4 (ex.: 1000 → 4000) | 14-bit / 16384 (0.022°) | 14-bit / 16384 (0.022°) | ✅ **21-bit / 2097152 (0.00017°)** |
| **Accuracy (raw INL)** | ✅ Excellent (optical) | ✅ ~±0.09° | ⚠️ ~±0.35° (DAEC + calibrates out) | ✅ **~±0.07°** (with its auto-calibration) |
| **Interface** | A/B/Z, NPN open-collector | SSI / I²C / ABZ | SPI / ABI / PWM | SPI (≤16 MHz) / ABZ / PWM / UVW |
| **Logic voltage** | ⚠️ Needs pull-ups to 3.3V | ✅ 3.3V native | ✅ 3.3V native | ✅ 3.3–5.0V |
| **Mounting diagnostics** | ❌ None | ⚠️ Coarse field flag | ✅ AGC + magnitude + MAGL/MAGH | ✅ Status flags + on-chip auto-calibration |
| **Immunity to magnetic field** | ✅ Immune (optical) | ⚠️ Sensitive (DD motor field) | ⚠️ Sensitive (idem) | ⚠️ Sensitive (idem) |
| **Max speed** | ✅ High | ✅ Very high | ✅ Very high (28k+ rpm) | ✅ Very high (120k rpm) |
| **Size / mounting** | ❌ Large (Ø40 mm, 6 mm shaft, coupling) | ✅ Chip + magnet, on-axis | ✅ Chip + magnet, on-axis | ✅ Chip + magnet, on-axis |
| **Wiring** | ⚠️ 4 wires + pull-ups + coupling | ✅ Simple (SPI + magnet) | ✅ Simple (SPI + magnet) | ✅ Simple (SPI + magnet) |
| **DriveLab support** | ✅ **Works today** (A/B/Z) | ✅ in A/B/Z · 🔧 SSI | ✅ in A/B/Z · 🔧 SPI | ✅ in A/B/Z · 🔧 SPI |
| **Reference price (BR)** | (bench, industrial) | **R$13** | R$58 | module — check current listings |
| **FFB feel** | ✅ Great | ✅ Great | ✅ Great | ✅ Great (none of them is the bottleneck) |

### Which one for what

| Role | Best pick | Why |
|---|---|---|
| **Bench development (now)** | **E6B2** | Plugs into the ODrive ABI port, immune to the motor field, spins FOC today |
| **Product — lowest cost** | **MT6701** | R$13, absolute, compact, 3.3V native, keeps center |
| **Product — easy build** | **AS5047P** | AGC diagnostics help set the airgap; DAEC; ODrive ecosystem |
| **Product — best sensor** | **MT6835** | 21-bit, ~±0.07° after its own auto-calibration, ABZ fallback while SPI support lands |

### Bottom line
- **Feel:** technical tie — all four exceed what you can feel; FFB feel is set by the FFB
  rate + torque loop, not the encoder's LSB. The MT6835's 21 bits are real, but they are not
  what you will notice; **absolute vs incremental** is.
- **The real difference** is **absolute vs incremental** (keeping center / instant FOC start)
  and **mounting** (compact magnet vs a large optical head with a coupling).
- **Honest caveat:** the E6B2 has one edge the magnetics don't — it is **immune to the
  motor's magnetic field**. A magnetic encoder placed too close to a strong DD motor can
  suffer interference, so the diametric magnet must sit right on the shaft end, away from the
  stator. Not a blocker (magnetic on-axis is the norm on DD wheels), but a mounting point the
  optical doesn't have.

### Mounting & wiring notes
- **E6B2-CWZ6C:** power the encoder from **5V**; its A/B/Z outputs are **NPN open-collector**,
  so add **~1–3.3 kΩ pull-ups to 3.3V** on each (the STM32F405 is 3.3V — do not feed 5V logic
  straight into a pin). The PPR is part of the model code (an **E6B2-CWZ6C 1000P/R** is 1000 PPR), and the family ranges
  from 100 to 2500 PPR — so **read your label**: effective resolution is that number **× 4**. It is incremental:
  it loses position on power-off; the **Z index** (1 pulse/turn) lets you home to a reference,
  and FOC needs a startup alignment each boot.
- **MT6701 / AS5047P / MT6835:** a **diametric magnet** (~6×2.5 mm) on the shaft end,
  **on-axis**, at the datasheet airgap (~0.5–2 mm). All three read over the **same SPI header**
  (MT6701 in SSI mode); same wiring, same magnet — only the frame/driver differs. **Check the
  magnet is included** — many modules ship without one, and nothing works without it.
  On a hoverboard motor, mind *what actually turns*: the shaft is often the fixed part and the
  housing rotates, so the magnet goes on a piece fixed to the housing and the sensor on a piece
  fixed to the shaft. Both are **single-turn absolute**:
  store the center offset in flash and they recover center on boot, as long as the wheel was
  not turned more than **±½ turn** while off. For the full range regardless of movement you'd
  need homing to a hard stop or a multi-turn absolute encoder (overkill for a wheel).
- **Eccentricity:** a slightly off-center magnet is a repeatable once-per-turn error — it
  **calibrates out** with a lookup table built by spinning the motor once. What
  a table can't fix is a bad airgap (weak field / low SNR) — fix that mechanically first, using
  the AS5047P's AGC/magnitude readout.

---

## 🇧🇷 Português

### Os três sensores num relance

| Qualidade | **E6B2-CWZ6C** (óptico ABI) | **MT6701** (magnético SSI) | **AS5047P** (magnético SPI) | **MT6835** (magnético SPI) |
|---|---|---|---|---|
| **Tecnologia** | Óptico incremental | Magnético on-axis (Hall) | Magnético on-axis (Hall) | Magnético on-axis (**AMR**) |
| **Medição** | ❌ Incremental | ✅ Absoluto (uma volta) | ✅ Absoluto (uma volta) | ✅ Absoluto (uma volta) |
| **Guarda o centro ao desligar** | ❌ Não (homing pelo Z) | ✅ Sim (dentro de ±½ volta) | ✅ Sim (dentro de ±½ volta) | ✅ Sim (dentro de ±½ volta) |
| **Partida da FOC** | ⚠️ "Dança" de alinhamento todo boot | ✅ Imediata | ✅ Imediata | ✅ Imediata |
| **Resolução** | PPR da etiqueta × 4 (ex.: 1000 → 4000) | 14 bits / 16384 (0,022°) | 14 bits / 16384 (0,022°) | ✅ **21 bits / 2097152 (0,00017°)** |
| **Precisão (INL bruto)** | ✅ Excelente (óptico) | ✅ ~±0,09° | ⚠️ ~±0,35° (DAEC + calibra fora) | ✅ **~±0,07°** (com a auto-calibração dele) |
| **Interface** | A/B/Z, NPN coletor aberto | SSI / I²C / ABZ | SPI / ABI / PWM | SPI (≤16 MHz) / ABZ / PWM / UVW |
| **Tensão de lógica** | ⚠️ Precisa de pull-ups p/ 3,3V | ✅ 3,3V nativo | ✅ 3,3V nativo | ✅ 3,3–5,0V |
| **Diagnóstico de montagem** | ❌ Nenhum | ⚠️ Flag grosseira de campo | ✅ AGC + magnitude + MAGL/MAGH | ✅ Flags de status + auto-calibração no chip |
| **Imunidade a campo magnético** | ✅ Imune (óptico) | ⚠️ Sensível (campo do motor DD) | ⚠️ Sensível (idem) | ⚠️ Sensível (idem) |
| **Velocidade máxima** | ✅ Alta | ✅ Muito alta | ✅ Muito alta (28k+ rpm) | ✅ Muito alta (120k rpm) |
| **Tamanho / montagem** | ❌ Grande (Ø40 mm, eixo 6 mm, acoplamento) | ✅ Chip + ímã, on-axis | ✅ Chip + ímã, on-axis | ✅ Chip + ímã, on-axis |
| **Fiação** | ⚠️ 4 fios + pull-ups + acoplamento | ✅ Simples (SPI + ímã) | ✅ Simples (SPI + ímã) | ✅ Simples (SPI + ímã) |
| **Suporte no DriveLab** | ✅ **Funciona hoje** (A/B/Z) | ✅ em A/B/Z · 🔧 SSI | ✅ em A/B/Z · 🔧 SPI | ✅ em A/B/Z · 🔧 SPI |
| **Preço de referência (BR)** | (bancada, industrial) | **R$13** | R$58 | módulo — confira os anúncios |
| **Feel do FFB** | ✅ Ótimo | ✅ Ótimo | ✅ Ótimo | ✅ Ótimo (nenhum deles é o gargalo) |

### Qual usar pra quê

| Papel | Melhor escolha | Porquê |
|---|---|---|
| **Dev de bancada (agora)** | **E6B2** | Pluga na porta ABI da ODrive, imune ao campo do motor, gira o FOC hoje |
| **Produto — menor custo** | **MT6701** | R$13, absoluto, compacto, 3,3V nativo, guarda o centro |
| **Produto — montagem fácil** | **AS5047P** | Diagnóstico AGC ajuda no airgap; DAEC; ecossistema ODrive |
| **Produto — melhor sensor** | **MT6835** | 21 bits, ~±0,07° depois da auto-calibração dele, e tem saída ABZ como ponte até o SPI ficar pronto |

### Resumo
- **Feel:** empate técnico — os quatro excedem o que você sente; o feel vem da taxa de FFB +
  loop de torque, não do LSB do encoder. Os 21 bits do MT6835 são reais, mas não é isso que
  você vai perceber; **absoluto vs incremental** é.
- **A real diferença** é **absoluto vs incremental** (guardar o centro / arranque FOC imediato)
  e **montagem** (ímã compacto vs cabeçote óptico grande com acoplamento).
- **Detalhe honesto:** o E6B2 tem uma vantagem que os magnéticos **não** têm — é **imune ao
  campo do motor**. Encoder magnético perto demais de um motor DD forte pode sofrer
  interferência; por isso o ímã diametral tem que ficar bem na ponta do eixo, longe do estator.
  Não é bloqueante (magnético on-axis é o normal em DD), mas é um ponto de atenção que o óptico
  não tem.

### Notas de montagem e fiação
- **E6B2-CWZ6C:** alimente o encoder pelos **5V**; as saídas A/B/Z são **NPN coletor aberto**,
  então ponha **pull-ups ~1–3,3 kΩ pro 3,3V** em cada uma (o STM32F405 é 3,3V — não jogue lógica
  de 5V direto no pino). O PPR faz parte do código do modelo (um **E6B2-CWZ6C 1000P/R** tem 1000
  PPR), e a família vai de 100 a 2500 PPR — então **leia a etiqueta do seu**: a resolução
  efetiva é esse número **× 4**. É incremental: perde a posição ao desligar; o **índice Z** (1 pulso/volta) permite homing a uma referência, e o FOC
  precisa de alinhamento a cada boot.
- **MT6701 / AS5047P / MT6835:** um **ímã diametral** (~6×2,5 mm) na ponta do eixo, **on-axis**,
  no airgap do datasheet (~0,5–2 mm). **Confira se o ímã vem junto** — muitos módulos vêm sem, e
  sem ele nada funciona. Num motor de hoverboard, preste atenção em *o que realmente gira*: o
  eixo costuma ser a parte parada e a carcaça é que roda, então o ímã vai numa peça presa à
  carcaça e o sensor numa peça presa ao eixo.
  Os três leem pelo **mesmo header SPI** (o MT6701 em modo SSI); mesma
  fiação, mesmo ímã — só muda o frame/driver. Ambos são **absolutos de volta única**: grave o
  offset de centro na flash e eles recuperam o centro no boot, desde que o volante não tenha
  girado mais de **±½ volta** desligado. Pra cobrir o range inteiro independente do movimento,
  seria homing na batente ou encoder multi-volta (overkill num volante).
- **Excentricidade:** um ímã levemente descentrado é um erro repetível 1×/volta — ele **calibra
  fora** com uma tabela de correção levantada girando o motor uma vez. O que a tabela
  não conserta é airgap ruim (campo fraco / baixo SNR) — isso resolve na mecânica primeiro,
  usando a leitura de AGC/magnitude do AS5047P.

---

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
