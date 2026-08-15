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

| | **2Ω** (the ODrive spec) | **12Ω** |
|---|---|---|
| Dump capacity | **High** — matches the brake-FET current rating | Lower, **but plenty for a steering wheel** |
| Peak current `V/R` (→ EMI / di/dt) | **High** → more switching noise | **~6× lower** → much less noise |
| Best for | High-power / **robotics** (ODrive's origin) | A **DD sim wheel** |

**For a DD sim wheel, 12Ω is the better fit**, for two reasons that come straight from the
physics:

1. **A wheel's regen is small.** A steering wheel has **low inertia** (it's not a vehicle);
   even a hard counter-steer is a few tens of watts, well within 12Ω's capacity. You don't
   need 2Ω's huge dump.
2. **~6× lower peak current = far less EMI.** The chopper *hard-switches* the resistor, so
   the peak current (`V/R`) sets the di/dt. A **2Ω** resistor pulls **6–28 A** pulses whose
   switching noise can **reset the board / drop the USB link** — *we hit exactly this on the
   DriveLab bench with a ~2Ω resistor, and only fixed it by twisting the resistor leads*. A
   **12Ω** resistor pulls **~1–5 A** → the noise is far smaller → the problem largely
   **disappears at the source**.

**2Ω also works** — it is the ODrive spec, and it is what our own bench runs — especially
with clean wiring (short, twisted resistor leads). But for a DD wheel **12Ω is the safer
choice**: enough dump capacity *and* far gentler on EMI.

Peak dump (chopper fully on):

| Resistor | @24V (peak A / peak W) | @56V (peak A / peak W) |
|---|---|---|
| **2Ω** | 12 A / 288 W | 28 A / 3.1 kW |
| **12Ω** | 2 A / 48 W | 4.7 A / 261 W |

- **Don't go *below* 2Ω** (e.g. 1Ω): you'd exceed the brake-FET current rating. 2Ω is the low floor.
- The trade-off is real but mild: 12Ω dumps less, so in a *pathological* high-energy regen it
  could let the bus climb higher than 2Ω would. For a sim wheel that case basically doesn't occur.

> **Most DIY DD-wheel builds run a 24V supply.** At 24V the peak currents are moderate (table
> above), and the **~8–12Ω range is the sweet spot** — low EMI *and* plenty of dump for a wheel.
> **12Ω** is the lowest-EMI / community pick; **~8Ω** keeps a bit more capacity margin (a hedge if
> your FFB is aggressive). Both are cheap, so the honest move is to **buy an 8Ω and a 12Ω and
> compare them under real FFB** — pick the highest R (least EMI) that still holds the bus on your
> worst counter-steer. (At **56V** the peaks are ~2× higher, so lean toward 12Ω+.)

### The 56 V variant has almost no margin

Both ODESC variants use **60 V MOSFETs** and **63 V bus capacitors** — the sticker changes, the
silicon does not. What differs is the headroom left above your supply, and that is what decides
whether a regen spike is survivable:

| Supply | Headroom to the 60 V MOSFETs |
|---|---|
| 24 V | 36 V — 150 % of the supply |
| 56 V | **4 V — 7 % of the supply** |

Our bench measured, on a **24 V** supply during a zig-zag under real FFB, a bus peak of **33.5 V
without the brake resistor** and **27.5 V with it**. What carries over to another bus voltage is the
*energy*, not the voltage step, so `E = ½C(V₂² − V₁²)` transports it:

| Setup | Bus peak | Margin to 60 V |
|---|---|---|
| 24 V, no resistor | 33.5 V (measured) | 26.5 V |
| 24 V, with resistor | 27.5 V (measured) | 32.5 V |
| **56 V, no resistor** | **60.7 V** | **over the limit** |
| **56 V, with resistor** | **57.6 V** | **2.4 V** |

On a 56 V board running at 56 V, the same manoeuvre we already ran on our own bench **crosses the
MOSFETs' rating without the resistor** and leaves 2.4 V with it. The 63 V capacitors are just as
tight.

So if you build on the 56 V variant, the resistor stops being a safety net and becomes part of the
circuit — and it is worth asking whether you need the full 56 V at all. Running the supply lower
costs you torque; running it at 56 V can cost you the board.

**What this estimate does not know:** it assumes the same regen energy and the same bus capacitance
as our 24 V bench. A 56 V build reaches higher speeds and can regenerate more, which moves the
number the wrong way. Treat 60.7 V as a floor, not a worst case.

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

- **DD sim wheel (most run 24V) → 8–12Ω, 50W** wire-wound / aluminium-clad. **12Ω** = community /
  lowest-EMI; **~8Ω** = a bit more capacity margin — both are cheap, so buy both and compare under
  FFB. The **2Ω 50W** that ships with the ODESC also works, especially with short/twisted leads.
  Rated ≥ your bus voltage; mount on metal / with airflow.
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

| | **2Ω** (a spec do ODrive) | **12Ω** |
|---|---|---|
| Capacidade de dump | **Alta** — casa com o limite de corrente do FET de freio | Menor, **mas de sobra pra um volante** |
| Corrente de pico `V/R` (→ EMI / di/dt) | **Alta** → mais ruído de chaveamento | **~6× menor** → muito menos ruído |
| Melhor pra | Alta potência / **robótica** (origem do ODrive) | Um **volante DD** |

**Pra um volante DD, o 12Ω é a escolha que faz mais sentido**, por dois motivos que saem
direto da física:

1. **O regen de um volante é pequeno.** Um volante tem **inércia baixa** (não é um veículo);
   mesmo um contra-esterço forte são umas dezenas de watts, bem dentro da capacidade do 12Ω.
   Não precisa do dump gigante do 2Ω.
2. **Corrente de pico ~6× menor = MUITO menos EMI.** O chopper *chaveia duro* o resistor, então
   o pico (`V/R`) define o di/dt. Um **2Ω** puxa pulsos de **6–28 A** cujo ruído pode **resetar a
   placa / derrubar o USB** — *foi exatamente o que aconteceu na bancada do DriveLab com um
   resistor de ~2Ω, e só resolveu trançando os cabos do resistor*. Um **12Ω** puxa **~1–5 A** → o
   ruído é muito menor → o problema **some na origem**.

**2Ω também funciona** — é a spec do ODrive e é o que roda na nossa bancada —, principalmente com fiação
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

> **A maioria dos setups DIY de volante DD usa fonte de 24V.** A 24V os picos de corrente são
> moderados (tabela acima), e a **faixa ~8–12Ω é o ponto ótimo** — pouca EMI *e* dump de sobra pra
> um volante. **12Ω** = menor EMI / escolha da comunidade; **~8Ω** = um pouco mais de margem de
> capacidade (hedge se o FFB for agressivo). Os dois são baratos, então o jeito honesto é **comprar
> um 8Ω e um 12Ω e comparar sob FFB de verdade** — escolher o maior R (menos EMI) que ainda segura o
> bus no pior contra-esterço. (A **56V** os picos são ~2× maiores → puxe pra 12Ω+.)

### A variante de 56 V quase não tem margem

As duas variantes da ODESC usam **MOSFETs de 60 V** e **capacitores de barramento de 63 V** — muda o
adesivo, não muda o silício. O que muda é a folga que sobra acima da sua fonte, e é ela que decide se
um pico de regeneração é sobrevivível:

| Fonte | Folga até os 60 V do MOSFET |
|---|---|
| 24 V | 36 V — 150 % da fonte |
| 56 V | **4 V — 7 % da fonte** |

Medimos na nossa bancada, com fonte de **24 V** num zigue-zague sob FFB real, um pico de barramento
de **33,5 V sem o resistor de freio** e **27,5 V com ele**. O que se conserva ao levar isso para
outra tensão de barramento é a *energia*, não o degrau de tensão, então `E = ½C(V₂² − V₁²)` faz o
transporte:

| Configuração | Pico no barramento | Margem até 60 V |
|---|---|---|
| 24 V, sem resistor | 33,5 V (medido) | 26,5 V |
| 24 V, com resistor | 27,5 V (medido) | 32,5 V |
| **56 V, sem resistor** | **60,7 V** | **estourou** |
| **56 V, com resistor** | **57,6 V** | **2,4 V** |

Numa placa de 56 V rodando a 56 V, a mesma manobra que já fizemos na nossa bancada **passa do limite
dos MOSFETs sem o resistor** e deixa 2,4 V com ele. Os capacitores de 63 V ficam igualmente no
limite.

Ou seja: se você montar na variante de 56 V, o resistor deixa de ser rede de segurança e vira parte
do circuito — e vale perguntar se você precisa mesmo dos 56 V inteiros. Baixar a fonte custa torque;
manter 56 V pode custar a placa.

**O que esta estimativa NÃO sabe:** ela assume a mesma energia de regeneração e a mesma capacitância
de barramento da nossa bancada de 24 V. Uma montagem de 56 V alcança velocidades maiores e pode
regenerar mais, o que empurra o número para o lado errado. Trate os 60,7 V como piso, não como pior
caso.

### Watts: 50W ou 100W?

O **W é folga térmica, não a potência de pico.** Frenagem é em **pulsos curtos**, então a média
é baixa e **50W dá conta**. Um de **100W** só roda mais frio /
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

- **Volante DD (a maioria usa 24V) → 8–12Ω, 50W** wire-wound / aluminium-clad. **12Ω** = comunidade
  / menor EMI; **~8Ω** = um pouco mais de margem de capacidade — baratos, então compre os dois e
  compare sob FFB. O **2Ω 50W** que vem com a ODESC também funciona, principalmente com cabos
  curtos/trançados. Tensão nominal ≥ a do seu bus; monte em metal / com ventilação.
- Ligue na **saída de freio AUX** da base (os terminais de dump) e ajuste o `brake_resistance` pro
  valor que você de fato instalou.

---

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT. Revisão: 2Ω (spec ODrive) vs 12Ω (comunidade DD) — o 12Ω é provavelmente melhor pra volante por causa da EMI/pico de corrente, a confirmar com o motor sob FFB.</sub>
