# DriveLab — Brake Resistor Guide / Guia do Resistor de Frenagem

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> **What is it?** The base is an ODrive v3.6 (MKS ODESC clone). When the wheel is
> decelerated or back-driven, the motor becomes a generator and pushes energy back into
> the DC bus, raising its voltage. The **brake (dump) resistor** burns that excess energy
> as heat so the bus voltage stays safe. This guide explains the two ratings (Ω and W) and
> why **2Ω** is the recommended value.

> ⚠️ **The brake resistor only protects while the base is POWERED ON** (the chopper has to
> be actively switching). Spinning the motor with the base **off** rectifies back-EMF
> straight into the bus — that is covered by the **contactor**, not the resistor. They are
> complementary. See **[soft-power-contator.md](soft-power-contator.md)**.

---

## 🇬🇧 English

### What the brake resistor does

Under braking / counter-steer / hitting the end-stop, the motor regenerates current into
the DC bus and the bus voltage climbs. If it climbs too far it destroys capacitors and
FETs. The brake resistor, driven by the board's **AUX half-bridge chopper**, dissipates
that energy as heat and clamps the bus voltage. It is an *electrical brake*.

### The two numbers

| Rating | Meaning | Rule of thumb |
|---|---|---|
| **Ω (ohms)** | How much **current/power the system can dump** when the chopper is on. Current = Voltage ÷ Resistance. | **Lower Ω = stronger brake** (more dump capacity), but must respect the brake-FET current limit. |
| **W (watts)** | The **continuous thermal endurance** — how much sustained heat the resistor survives. | **Higher W = runs cooler / safer.** Braking is pulsed, so average power is low. |

### Why 2Ω (and not 10Ω / 12Ω)

**2Ω is the official ODrive specification.** A low resistance lets the chopper sink enough
current to hold the bus voltage down during a strong regen event. Peak dump capability at
a **56V** bus (chopper fully on):

| Resistor | Dump current (56V) | Peak dump power | Notes |
|---|---|---|---|
| **2Ω** ✅ (recommended) | 56 / 2 = **28 A** | ~3.1 kW | ODrive spec; handles abrupt braking |
| 10Ω | 5.6 A | ~0.31 kW | Sinks **~10× less** → bus voltage can spike on a hard counter-steer |
| 12Ω | 4.7 A | ~0.26 kW | Even weaker |

- On a **24V** variant the currents halve (2Ω → 12 A), but **2Ω is still the right choice** —
  the value is set by the board's brake FET, not by the PSU.
- **Do not go below 2Ω** (e.g. 1Ω): you would exceed the brake-FET current rating. 2Ω is the
  **safe floor**.
- Someone using **10Ω/12Ω** simply has a **weaker brake** (probably parts on hand, or a
  low-power setup). It works until a hard regen event pushes the bus into over-voltage fault.

### Wattage: 100W vs 50W

The **W rating is thermal headroom, not the peak dump power**. Braking happens in **short
pulses**, so the average power is low and even 50W works. A **100W** resistor has **double the
headroom** of the ODrive 50W minimum → it runs cooler and is safer. **A 2Ω/100W is better
than a 50W/12Ω.**

### Two things that matter

1. **The config must match the resistor.** Set `brake_resistance = 2.0` in the
   firmware/app to match the real part. If the config says 2Ω but you fit 10Ω, the brake
   regulation is miscalibrated.
2. **Powered-on only.** The resistor + chopper only work while the base is on. Off-state
   back-EMF is the **contactor's** job (phase disconnect). Different problem, different part.

### Marking codes

- `2R` = 2.0 Ω · `2R2` = 2.2 Ω (the `R` marks the ohm / decimal point).
- `10R` = 10 Ω · `12R` = 12 Ω.
- A trailing letter is the **tolerance**: `12RJ` = 12 Ω **±5%** (`J` = ±5%, `K` = ±10%, `F` = ±1%).

### Shopping summary

- **Buy: 2Ω, 50–100W** wire-wound / aluminium-clad power resistor. **100W preferred** for
  headroom. Rated ≥ your bus voltage. Mount on metal / with airflow — it gets hot.
- Wire it to the base's **AUX brake output** (the dump terminals), and set
  `brake_resistance = 2.0`.

---

## 🇧🇷 Português

### O que o resistor de frenagem faz

A base é uma ODrive v3.6 (clone MKS ODESC). Quando o volante **desacelera / é forçado ao
contrário / bate no batente**, o motor vira **gerador** e joga corrente de volta no
barramento DC → a **tensão do bus sobe**. Se subir demais, estoura capacitor e FET. O
**resistor de frenagem (dump)**, acionado pelo **chopper da ponte AUX** da placa, queima
essa energia em calor e segura a tensão. É um *freio elétrico*.

### Os dois números

| Rating | O que significa | Regra prática |
|---|---|---|
| **Ω (ohms)** | Quanta **corrente/potência o sistema consegue dissipar** com o chopper ligado. Corrente = Tensão ÷ Resistência. | **Menor Ω = freio mais forte** (mais capacidade), mas tem que respeitar o limite de corrente do FET de freio. |
| **W (watts)** | O **aguento térmico contínuo** — quanto calor sustentado o resistor suporta. | **Maior W = roda mais frio / mais seguro.** Frenagem é em pulsos, então a média é baixa. |

### Por que 2Ω (e não 10Ω / 12Ω)

**2Ω é a especificação oficial do ODrive.** A resistência baixa deixa o chopper sorver
corrente suficiente pra segurar a tensão do bus num regen forte. Pico de dissipação num
bus de **56V** (chopper 100% ligado):

| Resistor | Corrente de dump (56V) | Potência de pico | Comentário |
|---|---|---|---|
| **2Ω** ✅ (recomendado) | 56 / 2 = **28 A** | ~3,1 kW | Spec ODrive; dá conta de frenagem brusca |
| 10Ω | 5,6 A | ~0,31 kW | Sorve **~10× menos** → tensão pode disparar num contra-esterço forte |
| 12Ω | 4,7 A | ~0,26 kW | Ainda mais fraco |

- Na variante **24V** as correntes caem pela metade (2Ω → 12 A), mas **2Ω continua sendo a
  escolha certa** — o valor é ditado pelo FET de freio da placa, não pela fonte.
- **Não desça abaixo de 2Ω** (ex.: 1Ω): você estoura o limite de corrente do FET de freio.
  2Ω é o **piso seguro**.
- Quem usa **10Ω/12Ω** está com um freio **mais fraco** (provavelmente peça à mão, ou setup
  de menor potência). Funciona até um regen forte empurrar o bus pra falha de sobretensão.

### Watts: 100W vs 50W

O **W é folga térmica, não a potência de pico.** Frenagem acontece em **pulsos curtos**,
então a média é baixa e mesmo 50W funciona. Um resistor de **100W** tem o **dobro da folga**
do mínimo do ODrive (50W) → roda mais frio e mais seguro. **Um 2Ω/100W é melhor que um
50W/12Ω.**

### Duas coisas que importam

1. **A config tem que casar com o resistor.** Ajuste `brake_resistance = 2.0` no
   firmware/app pra bater com a peça real. Se a config disser 2Ω mas você instalar 10Ω, a
   regulação do freio fica errada.
2. **Só com a placa LIGADA.** O resistor + chopper só funcionam com a base ligada. O
   back-EMF de estado desligado é tarefa do **contator** (desconecta as fases). Problema
   diferente, peça diferente.

### Códigos de marcação

- `2R` = 2,0 Ω · `2R2` = 2,2 Ω (o `R` marca o ohm / a vírgula decimal).
- `10R` = 10 Ω · `12R` = 12 Ω.
- Uma letra no fim é a **tolerância**: `12RJ` = 12 Ω **±5%** (`J` = ±5%, `K` = ±10%, `F` = ±1%).

### Resumo de compra

- **Compre: 2Ω, 50–100W**, resistor de potência wire-wound / aluminium-clad (aletado).
  **100W preferível** pela folga. Tensão nominal ≥ a do seu bus. Monte em metal / com
  ventilação — ele esquenta.
- Ligue na **saída de freio AUX** da base (os terminais de dump) e ajuste
  `brake_resistance = 2.0`.

---

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
