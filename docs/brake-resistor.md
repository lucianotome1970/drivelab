# DriveLab — Brake Resistor Guide / Guia do Resistor de Frenagem

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> **What is it?** The base is an ODrive v3.6 (MKS ODESC clone). When the wheel is
> decelerated or back-driven, the motor becomes a generator and pushes energy back into
> the DC bus, raising its voltage. The **brake (dump) resistor** burns that excess energy
> as heat so the bus voltage stays safe. This guide explains the two ratings (Ω and W) and
> **which resistance to pick for a sim wheel** — the short answer is more nuanced than the
> ODrive spec suggests.

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
| **Ω (ohms)** | How much **current/power the system can dump** when the chopper is on. Current = Voltage ÷ Resistance. | Lower Ω = more dump capacity, **but also higher peak current** (more EMI, more FET stress). |
| **W (watts)** | The **continuous thermal endurance** — how much sustained heat the resistor survives. | **Higher W = runs cooler / safer.** Braking is pulsed, so average power is low. |

### Which resistance — 2Ω or 12Ω?

Two values are common, and they suit **different goals**:

| | **2Ω** (ODrive spec — ships with the ODESC) | **12Ω** (DD-wheel / FFBeast community) |
|---|---|---|
| Dump capacity | **High** — matches the brake-FET current rating | Lower, **but plenty for a steering wheel** |
| Peak current `V/R` (→ EMI / di/dt) | **High** → more switching noise | **~6× lower** → much less noise |
| Best for | High-power / **robotics** (ODrive's origin) | A **DD sim wheel** |

**For a DD sim wheel, 12Ω is the community standard** — people running *working* FFBeast
setups use it — and there are two solid reasons:

1. **A wheel's regen is small.** A steering wheel has **low inertia** (it's not a vehicle);
   even a hard counter-steer is a few tens of watts, well within 12Ω's capacity. You don't
   need 2Ω's huge dump.
2. **~6× lower peak current = far less EMI.** The chopper *hard-switches* the resistor, so
   the peak current (`V/R`) sets the di/dt. A **2Ω** resistor pulls **6–28 A** pulses whose
   switching noise can **reset the board / drop the USB link** — *we hit exactly this on the
   DriveLab bench with a ~2Ω resistor, and only fixed it by twisting the resistor leads*. A
   **12Ω** resistor pulls **~1–5 A** → the noise is far smaller → the problem largely
   **disappears at the source**.

**2Ω also works** (it's the ODrive spec and what the ODESC ships), especially with clean
wiring (short, twisted resistor leads). But for a DD wheel, **12Ω is likely the better
choice** — adequate dump *and* much gentler on EMI.

Peak dump (chopper fully on):

| Resistor | @24V (peak A / peak W) | @56V (peak A / peak W) |
|---|---|---|
| **2Ω** | 12 A / 288 W | 28 A / 3.1 kW |
| **12Ω** | 2 A / 48 W | 4.7 A / 261 W |

- **Don't go *below* 2Ω** (e.g. 1Ω): you'd exceed the brake-FET current rating. 2Ω is the low floor.
- The trade-off is real but mild: 12Ω dumps less, so in a *pathological* high-energy regen it
  could let the bus climb higher than 2Ω would. For a sim wheel that case basically doesn't occur.

### Wattage: 50W or 100W?

The **W rating is thermal headroom, not the peak dump power.** Braking happens in **short
pulses**, so the average power is low and **50W is the community/ODESC standard** and works.
A **100W** part just runs cooler / has more margin — nice to have, not required.

### Two things that matter

1. **The config must match the resistor.** Set `brake_resistance` in the firmware/app to the
   **measured** value (`2.0` for a 2Ω part, `~12` for a 12Ω part). If the config disagrees
   with the real part, the brake regulation is miscalibrated.
2. **Powered-on only.** The resistor + chopper only work while the base is on. Off-state
   back-EMF is the **contactor's** job (phase disconnect). Different problem, different part.

### Marking codes

- `2R` = 2.0 Ω · `2R2` = 2.2 Ω (the `R` marks the ohm / decimal point).
- `10R` = 10 Ω · `12R` = 12 Ω.
- A trailing letter is the **tolerance**: `12RJ` = 12 Ω **±5%** (`J` = ±5%, `K` = ±10%, `F` = ±1%).

### Shopping summary

- **DD sim wheel → 12Ω, 50W** wire-wound / aluminium-clad (the FFBeast-community pick — gentler
  on EMI, adequate for a wheel). The **2Ω 50W** that ships with the ODESC also works, especially
  with short/twisted leads. Rated ≥ your bus voltage; mount on metal / with airflow.
- Wire it to the base's **AUX brake output** (the dump terminals), and set `brake_resistance` to
  the value you actually fitted.

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
| **Ω (ohms)** | Quanta **corrente/potência o sistema consegue dissipar** com o chopper ligado. Corrente = Tensão ÷ Resistência. | Menor Ω = mais capacidade, **mas também maior corrente de pico** (mais EMI, mais estresse no FET). |
| **W (watts)** | O **aguento térmico contínuo** — quanto calor sustentado o resistor suporta. | **Maior W = roda mais frio / mais seguro.** Frenagem é em pulsos, então a média é baixa. |

### Qual resistência — 2Ω ou 12Ω?

Dois valores são comuns, e servem a **objetivos diferentes**:

| | **2Ω** (spec ODrive — vem com a ODESC) | **12Ω** (comunidade DD / FFBeast) |
|---|---|---|
| Capacidade de dump | **Alta** — casa com o limite de corrente do FET de freio | Menor, **mas de sobra pra um volante** |
| Corrente de pico `V/R` (→ EMI / di/dt) | **Alta** → mais ruído de chaveamento | **~6× menor** → muito menos ruído |
| Melhor pra | Alta potência / **robótica** (origem do ODrive) | Um **volante DD** |

**Pra um volante DD, o 12Ω é o padrão da comunidade** — quem tem setup FFBeast *funcionando*
usa ele — e por dois motivos sólidos:

1. **O regen de um volante é pequeno.** Um volante tem **inércia baixa** (não é um veículo);
   mesmo um contra-esterço forte são umas dezenas de watts, bem dentro da capacidade do 12Ω.
   Não precisa do dump gigante do 2Ω.
2. **Corrente de pico ~6× menor = MUITO menos EMI.** O chopper *chaveia duro* o resistor, então
   o pico (`V/R`) define o di/dt. Um **2Ω** puxa pulsos de **6–28 A** cujo ruído pode **resetar a
   placa / derrubar o USB** — *foi exatamente o que aconteceu na bancada do DriveLab com um
   resistor de ~2Ω, e só resolveu trançando os cabos do resistor*. Um **12Ω** puxa **~1–5 A** → o
   ruído é muito menor → o problema **some na origem**.

**2Ω também funciona** (é a spec do ODrive e o que a ODESC manda), principalmente com fiação
limpa (cabos curtos e trançados). Mas pra um volante DD, **o 12Ω provavelmente é a melhor
escolha** — dump suficiente *e* muito mais gentil com a EMI.

Pico de dissipação (chopper 100% ligado):

| Resistor | @24V (pico A / pico W) | @56V (pico A / pico W) |
|---|---|---|
| **2Ω** | 12 A / 288 W | 28 A / 3,1 kW |
| **12Ω** | 2 A / 48 W | 4,7 A / 261 W |

- **Não desça *abaixo* de 2Ω** (ex.: 1Ω): estoura o limite de corrente do FET de freio. 2Ω é o piso.
- O trade-off é real mas leve: o 12Ω dissipa menos, então num regen *patológico* de muita energia
  ele deixaria o bus subir mais que o 2Ω. Pra um volante, esse caso basicamente não acontece.

### Watts: 50W ou 100W?

O **W é folga térmica, não a potência de pico.** Frenagem é em **pulsos curtos**, então a média
é baixa e **50W é o padrão da comunidade/ODESC** e funciona. Um de **100W** só roda mais frio /
tem mais margem — bom ter, não obrigatório.

### Duas coisas que importam

1. **A config tem que casar com o resistor.** Ajuste o `brake_resistance` no firmware/app pro
   valor **medido** (`2.0` pra um 2Ω, `~12` pra um 12Ω). Se a config discordar da peça real, a
   regulação do freio fica errada.
2. **Só com a placa LIGADA.** O resistor + chopper só funcionam com a base ligada. O back-EMF de
   estado desligado é tarefa do **contator** (desconecta as fases). Problema diferente, peça diferente.

### Códigos de marcação

- `2R` = 2,0 Ω · `2R2` = 2,2 Ω (o `R` marca o ohm / a vírgula decimal).
- `10R` = 10 Ω · `12R` = 12 Ω.
- Uma letra no fim é a **tolerância**: `12RJ` = 12 Ω **±5%** (`J` = ±5%, `K` = ±10%, `F` = ±1%).

### Resumo de compra

- **Volante DD → 12Ω, 50W** wire-wound / aluminium-clad (a escolha da comunidade FFBeast — mais
  gentil com a EMI, suficiente pra um volante). O **2Ω 50W** que vem com a ODESC também funciona,
  principalmente com cabos curtos/trançados. Tensão nominal ≥ a do seu bus; monte em metal / com
  ventilação.
- Ligue na **saída de freio AUX** da base (os terminais de dump) e ajuste o `brake_resistance` pro
  valor que você de fato instalou.

---

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT. Revisão: 2Ω (spec ODrive) vs 12Ω (comunidade DD) — o 12Ω é provavelmente melhor pra volante por causa da EMI/pico de corrente, a confirmar com o motor sob FFB.</sub>
