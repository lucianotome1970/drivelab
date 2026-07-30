# DriveLab — Encoder Guide / Guia de Encoders

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> **Why three options?** A position sensor is what tells the base where the wheel is.
> DriveLab is meant to be built on a budget **or** with premium parts, so the plan is to
> support **three** sensors that all feed the same FOC/FFB code (via SimpleFOC's `Sensor`
> interface). This guide explains the trade-offs so you can pick what to buy.

> ⚠️ **Status (Stage 1, motor):** today the firmware drives the **incremental ABI** path
> (the **Omron E6B2-CWZ6C** used on the bench — including the ACC/EVO/AMS2 validation).
> Selecting the **MT6701** (planned default) or **AS5047P** via `encoder_type` is **planned
> for Stage 1** and not selectable yet. Buy accordingly.

---

## 🇬🇧 English

### The three sensors at a glance

| Quality | **E6B2-CWZ6C** (optical ABI) | **MT6701** (magnetic SSI) | **AS5047P** (magnetic SPI) |
|---|---|---|---|
| **Technology** | Optical incremental | Magnetic on-axis | Magnetic on-axis |
| **Measurement** | ❌ Incremental | ✅ Absolute (single-turn) | ✅ Absolute (single-turn) |
| **Keeps center after power-off** | ❌ No (home via Z) | ✅ Yes (within ±½ turn) | ✅ Yes (within ±½ turn) |
| **FOC startup** | ⚠️ Alignment "dance" every boot | ✅ Immediate | ✅ Immediate |
| **Resolution** | 10000 CPR (0.036°) | 14-bit / 16384 (0.022°) | 14-bit / 16384 (0.022°) |
| **Accuracy (raw INL)** | ✅ Excellent (optical) | ✅ ~±0.09° | ⚠️ ~±0.35° (but DAEC + calibrates out) |
| **Interface** | A/B/Z, NPN open-collector | SSI / I²C / ABZ | SPI / ABI / PWM |
| **Logic voltage** | ⚠️ Needs pull-ups to 3.3V | ✅ 3.3V native | ✅ 3.3V native |
| **Mounting diagnostics** | ❌ None | ⚠️ Coarse field flag | ✅ AGC + magnitude + MAGL/MAGH |
| **Immunity to magnetic field** | ✅ Immune (optical) | ⚠️ Sensitive (DD motor field) | ⚠️ Sensitive (idem) |
| **Max speed** | ✅ High (100 kHz resp.) | ✅ Very high | ✅ Very high (28k+ rpm) |
| **Size / mounting** | ❌ Large (Ø40 mm, 6 mm shaft, coupling) | ✅ Chip + magnet, compact on-axis | ✅ Chip + magnet, compact on-axis |
| **Wiring** | ⚠️ 4 wires + pull-ups + coupling | ✅ Simple (SPI + magnet) | ✅ Simple (SPI + magnet) |
| **SimpleFOC driver** | `Encoder(A,B,Z)` | `MagneticSensorMT6701SSI` | `MagneticSensorSPI(AS5047_SPI)` |
| **Reference price (BR)** | (bench, industrial) | **R$13** | R$58 |
| **FFB feel** | ✅ Great | ✅ Great | ✅ Great (tie — none is the bottleneck) |

### Which one for what

| Role | Best pick | Why |
|---|---|---|
| **Bench development (now)** | **E6B2** | Plugs into the ODrive ABI port, immune to the motor field, spins FOC today |
| **Product — lowest cost** | **MT6701** (planned default) | R$13, absolute, compact, 3.3V native, keeps center |
| **Product — easy build / premium** | **AS5047P** | AGC diagnostics help set the airgap; DAEC; ODrive ecosystem |

### Bottom line
- **Feel:** technical tie — all three exceed what you can feel; FFB feel is set by the FFB
  rate + torque loop, not the encoder's LSB.
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
  straight into a pin). Effective resolution is 2500 PPR × 4 = **10000 CPR**. It is incremental:
  it loses position on power-off; the **Z index** (1 pulse/turn) lets you home to a reference,
  and FOC needs a startup alignment each boot.
- **MT6701 / AS5047P:** a **diametric magnet** (~6×2.5 mm) on the shaft end, **on-axis**, at
  the datasheet airgap (~0.5–2 mm). Both read over the **same SPI header** (MT6701 in SSI mode);
  same wiring, same magnet — only the frame/driver differs. Both are **single-turn absolute**:
  store the center offset in flash and they recover center on boot, as long as the wheel was
  not turned more than **±½ turn** while off. For the full range regardless of movement you'd
  need homing to a hard stop or a multi-turn absolute encoder (overkill for a wheel).
- **Eccentricity:** a slightly off-center magnet is a repeatable once-per-turn error — it
  **calibrates out** with a lookup table (SimpleFOC `CalibratedSensor`, motor spinning). What
  a table can't fix is a bad airgap (weak field / low SNR) — fix that mechanically first, using
  the AS5047P's AGC/magnitude readout.

---

## 🇧🇷 Português

### Os três sensores num relance

| Qualidade | **E6B2-CWZ6C** (óptico ABI) | **MT6701** (magnético SSI) | **AS5047P** (magnético SPI) |
|---|---|---|---|
| **Tecnologia** | Óptico incremental | Magnético on-axis | Magnético on-axis |
| **Medição** | ❌ Incremental | ✅ Absoluto (volta única) | ✅ Absoluto (volta única) |
| **Guarda o centro ao desligar** | ❌ Não (homing por Z) | ✅ Sim (±½ volta) | ✅ Sim (±½ volta) |
| **Arranque FOC** | ⚠️ "Dancinha" a cada boot | ✅ Imediato | ✅ Imediato |
| **Resolução** | 10000 CPR (0,036°) | 14-bit / 16384 (0,022°) | 14-bit / 16384 (0,022°) |
| **Precisão (INL bruto)** | ✅ Ótima (óptico) | ✅ ~±0,09° | ⚠️ ~±0,35° (mas DAEC + calibra fora) |
| **Interface** | A/B/Z, NPN coletor aberto | SSI / I²C / ABZ | SPI / ABI / PWM |
| **Tensão lógica** | ⚠️ Precisa pull-up p/ 3,3V | ✅ 3,3V nativo | ✅ 3,3V nativo |
| **Diagnóstico de montagem** | ❌ Nenhum | ⚠️ Flag grosso de campo | ✅ AGC + magnitude + MAGL/MAGH |
| **Imunidade a campo magnético** | ✅ Imune (óptico) | ⚠️ Sensível (campo do motor DD) | ⚠️ Sensível (idem) |
| **Velocidade máx** | ✅ Alta (resp. 100 kHz) | ✅ Muito alta | ✅ Altíssima (28k+ rpm) |
| **Tamanho / montagem** | ❌ Grande (Ø40 mm, eixo 6 mm, coupling) | ✅ Chip + ímã, compacto on-axis | ✅ Chip + ímã, compacto on-axis |
| **Fiação** | ⚠️ 4 fios + pull-ups + acoplamento | ✅ Simples (SPI + ímã) | ✅ Simples (SPI + ímã) |
| **Driver SimpleFOC** | `Encoder(A,B,Z)` | `MagneticSensorMT6701SSI` | `MagneticSensorSPI(AS5047_SPI)` |
| **Preço de referência (BR)** | (bancada, industrial) | **R$13** | R$58 |
| **Feel/fidelidade FFB** | ✅ Ótimo | ✅ Ótimo | ✅ Ótimo (empate — nenhum é gargalo) |

### Qual usar pra quê

| Papel | Melhor escolha | Porquê |
|---|---|---|
| **Dev de bancada (agora)** | **E6B2** | Pluga na porta ABI da ODrive, imune ao campo do motor, gira o FOC hoje |
| **Produto — menor custo** | **MT6701** (default planejado) | R$13, absoluto, compacto, 3,3V nativo, guarda o centro |
| **Produto — montagem fácil / premium** | **AS5047P** | Diagnóstico AGC ajuda no airgap; DAEC; ecossistema ODrive |

### Resumo
- **Feel:** empate técnico — os três excedem o que você sente; o feel vem da taxa de FFB +
  loop de torque, não do LSB do encoder.
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
  de 5V direto no pino). Resolução efetiva 2500 PPR × 4 = **10000 CPR**. É incremental: perde a
  posição ao desligar; o **índice Z** (1 pulso/volta) permite homing a uma referência, e o FOC
  precisa de alinhamento a cada boot.
- **MT6701 / AS5047P:** um **ímã diametral** (~6×2,5 mm) na ponta do eixo, **on-axis**, no airgap
  do datasheet (~0,5–2 mm). Os dois leem pelo **mesmo header SPI** (o MT6701 em modo SSI); mesma
  fiação, mesmo ímã — só muda o frame/driver. Ambos são **absolutos de volta única**: grave o
  offset de centro na flash e eles recuperam o centro no boot, desde que o volante não tenha
  girado mais de **±½ volta** desligado. Pra cobrir o range inteiro independente do movimento,
  seria homing na batente ou encoder multi-volta (overkill num volante).
- **Excentricidade:** um ímã levemente descentrado é um erro repetível 1×/volta — ele **calibra
  fora** com uma tabela (LUT do `CalibratedSensor` do SimpleFOC, motor girando). O que a tabela
  não conserta é airgap ruim (campo fraco / baixo SNR) — isso resolve na mecânica primeiro,
  usando a leitura de AGC/magnitude do AS5047P.

---

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
