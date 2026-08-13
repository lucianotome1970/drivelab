# DriveLab — Motor Torque Calculation / Cálculo de Torque do Motor

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

This guide is for the **maker** (advanced user). It shows how to find out **how many Nm** your motor really delivers — **without a lever and a scale** — by measuring the torque constant **Kt** and multiplying by current. It's the same math commercial DD bases (Moza, Fanatec) use internally: `torque = Kt × Iq`.

---

## 🇬🇧 English

### 1. The physics (why no scale is needed)

Torque of a BLDC/PMSM motor is **linear with current**:

```
T [Nm] = Kt × I
```

- **Kt** = torque constant, in **Nm/A** (Newton-metre per ampere).
- **I** = phase (q-axis) current, in amperes — the firmware **measures** this in real time.

So if you know **Kt** and the **current** you can drive, the torque is settled. No weighing. `pole_pairs` does **not** enter here — it governs commutation, not torque magnitude.

Kt relates to the motor's **KV** rating (rpm per volt):

```
Kt [Nm/A] ≈ 9.55 / KV        (theoretical, SI)
```

### 2. Get Kt — two ways, neither needs a scale

#### Method A — ODrive calibration (recommended, most accurate)

During motor calibration (`initFOC()` / ODrive `motor.config`), the firmware **estimates the motor's `torque_constant` electrically** and reports it. **That number is your Kt** — use it directly. It's measured on your exact motor, so it already accounts for its real magnets and winding.

> This happens on the bench (Stage 1), because calibration spins the motor.

#### Method B — Back-EMF spin test (multimeter, ±10–15%)

Works with a drill and a multimeter, **motor disconnected from the driver**:

1. **Disconnect** the three motor phases from the board.
2. **Spin** the motor shaft at a **known, steady RPM** (a drill with a known speed, or count turns with a tachometer). Higher RPM = easier to read.
3. With the multimeter on **AC volts**, measure the voltage **between two phases** (line-to-line): `V_LL(rms)`.
4. Compute:

```
Kt [Nm/A] ≈ 7.8 × V_LL(rms) / RPM
```

<sub>(Derivation: in SI, Ke = Kt; line-to-line peak = √3 × phase peak; RMS→peak = ×√2; ω = RPM·2π/60. Combining gives the ≈7.8 factor.)</sub>

Optionally cross-check via KV: `KV ≈ RPM / V_LL(rms)`, then `Kt ≈ 9.55 / KV`. If Method A and Method B disagree by more than ~15%, trust **Method A**.

### 3. Get the current you can actually drive (the other half)

Torque = Kt × **I**, so you need the current ceiling. Two different limits:

| Limit | Governs | Set by |
|---|---|---|
| **Driver current** | absolute max | ODrive/DRV8301 shunt + gain config (`OC_ADJ`, `GAIN`) — hardware capable of ~40–50 A |
| **Motor thermal** | **continuous** | the winding heats up (I²R). Continuous ≈ the motor's rated phase current |
| **Magnetic saturation** | **peak** | above a point, more current makes less extra torque (Kt droops ~15%) |

Practical for a hoverboard hub:
- **Continuous:** ~10 A (sustained without cooking).
- **Peak (seconds):** ~20 A before saturation bites.

### 4. Compute your Nm

```
T_continuous = Kt × I_continuous
T_peak       = Kt × I_peak × 0.85   (the 0.85 accounts for saturation droop at high current)
```

**Worked example — hoverboard:**

Spin test reads `V_LL(rms) = 24.6 V` at `300 RPM`:

```
Kt         = 7.8 × 24.6 / 300  = 0.64 Nm/A
T_cont     = 0.64 × 10 A       = 6.4 Nm
T_peak     = 0.64 × 20 A × 0.85 ≈ 10.9 Nm  → ~9–11 Nm peak
```

Result: this motor delivers **~6.4 Nm continuous / ~9–11 Nm peak** — enough for the 9 Nm-peak target (remember: FFB is bursty, so peak is what matters most; you never hold max force for minutes).

### 5. See it live in the app (no math by hand)

1. Open DriveLab Studio in **advanced mode** (see the Maker's Guide).
2. In the **Hardware** tab, set **Constante de torque (Kt)** to your measured value (Nm/A).
3. The **Hardware monitor** now shows **Estimated torque** = `Kt × measured current`, live.
   - With the motor off (no current) it reads ~0 / `—`; on the bench (Stage 1), once current flows, it shows the **real Nm** on screen — more accurate than a lever and a scale (which add their own friction and arm-length error).

### 6. Reality checks
- **Peak ≠ continuous.** A wheelbase advertised as "9 Nm" almost always means **peak**. Size for ~5–6 Nm continuous + peak on top.
- **Diameter is torque.** Two motors with the same Kt but different sizes differ in the current they can push before saturating — the bigger one (hoverboard) makes more real torque. This is why a big gearless hub beats a small frameless motor.
- **Avoid gearboxes.** A geared motor multiplies torque on paper but adds backlash, friction and reflected inertia — it ruins DD feel. Keep it gearless.

---

## 🇧🇷 Português

Este guia é para o **criador** (usuário avançado). Ensina a descobrir **quantos Nm** o seu motor realmente entrega — **sem haste e balança** — medindo a constante de torque **Kt** e multiplicando pela corrente. É a mesma conta que as bases DD comerciais (Moza, Fanatec) usam por dentro: `torque = Kt × Iq`.

### 1. A física (por que não precisa de balança)

O torque de um motor BLDC/PMSM é **linear com a corrente**:

```
T [Nm] = Kt × I
```

- **Kt** = constante de torque, em **Nm/A** (Newton-metro por ampère).
- **I** = corrente de fase (eixo q), em ampères — o firmware **mede** isso em tempo real.

Ou seja: sabendo o **Kt** e a **corrente** que você consegue aplicar, o torque está resolvido. Sem pesar. O `pole_pairs` **não** entra aqui — ele rege a comutação, não a magnitude do torque.

O Kt se relaciona com o **KV** do motor (rpm por volt):

```
Kt [Nm/A] ≈ 9,55 / KV        (teórico, SI)
```

### 2. Descobrir o Kt — dois jeitos, nenhum precisa de balança

#### Método A — Calibração do ODrive (recomendado, mais preciso)

Durante a calibração do motor (`initFOC()` / `motor.config` da ODrive), o firmware **estima o `torque_constant` eletricamente** e reporta. **Esse número é o seu Kt** — use direto. É medido no seu motor exato, então já considera os ímãs e o enrolamento reais dele.

> Isso acontece na bancada (Stage 1), porque a calibração gira o motor.

#### Método B — Teste de back-EMF (multímetro, ±10–15%)

Funciona com furadeira e multímetro, **motor desconectado do driver**:

1. **Desconecte** as três fases do motor da placa.
2. **Gire** o eixo do motor a uma **RPM conhecida e estável** (furadeira com rotação conhecida, ou conte as voltas com um tacômetro). Quanto maior a RPM, mais fácil de ler.
3. Com o multímetro em **tensão AC**, meça a tensão **entre duas fases** (linha-a-linha): `V_LL(rms)`.
4. Calcule:

```
Kt [Nm/A] ≈ 7,8 × V_LL(rms) / RPM
```

<sub>(Dedução: em SI, Ke = Kt; pico linha-a-linha = √3 × pico de fase; RMS→pico = ×√2; ω = RPM·2π/60. Juntando dá o fator ≈7,8.)</sub>

Opcional: confira via KV: `KV ≈ RPM / V_LL(rms)`, depois `Kt ≈ 9,55 / KV`. Se os Métodos A e B divergirem mais de ~15%, confie no **Método A**.

### 3. Descobrir a corrente que você consegue aplicar (a outra metade)

Torque = Kt × **I**, então você precisa do teto de corrente. São dois limites diferentes:

| Limite | Rege | Definido por |
|---|---|---|
| **Corrente do driver** | máximo absoluto | shunt + ganho da ODrive/DRV8301 (`OC_ADJ`, `GAIN`) — hardware aguenta ~40–50 A |
| **Térmico do motor** | **contínuo** | o enrolamento esquenta (I²R). Contínuo ≈ a corrente de fase nominal do motor |
| **Saturação magnética** | **pico** | acima de um ponto, mais corrente faz menos torque extra (Kt cai ~15%) |

Prático para um hub de hoverboard:
- **Contínuo:** ~10 A (sustentado sem fritar).
- **Pico (segundos):** ~20 A antes da saturação pesar.

### 4. Calcular os seus Nm

```
T_contínuo = Kt × I_contínuo
T_pico     = Kt × I_pico × 0,85   (o 0,85 desconta a queda do Kt por saturação em corrente alta)
```

**Exemplo resolvido — hoverboard:**

O teste de giro lê `V_LL(rms) = 24,6 V` a `300 RPM`:

```
Kt         = 7,8 × 24,6 / 300  = 0,64 Nm/A
T_cont     = 0,64 × 10 A       = 6,4 Nm
T_pico     = 0,64 × 20 A × 0,85 ≈ 10,9 Nm  → ~9–11 Nm de pico
```

Resultado: esse motor entrega **~6,4 Nm contínuo / ~9–11 Nm de pico** — suficiente para o alvo de 9 Nm de pico (lembre: FFB é rajada, então o pico é o que mais importa; você nunca segura força máxima por minutos).

### 5. Ver ao vivo no app (sem conta na mão)

1. Abra o DriveLab Studio em **modo avançado** (veja o Guia do Criador).
2. Na aba **Hardware**, coloque em **Constante de torque (Kt)** o valor medido (Nm/A).
3. O **monitor de hardware** passa a mostrar **Torque estimado** = `Kt × corrente medida`, ao vivo.
   - Com o motor desligado (sem corrente) lê ~0 / `—`; na bancada (Stage 1), quando houver corrente, mostra os **Nm reais** na tela — mais preciso que haste e balança (que adicionam atrito e erro de braço de alavanca).

### 6. Verdades a lembrar
- **Pico ≠ contínuo.** Base anunciada como "9 Nm" quase sempre é **pico**. Dimensione para ~5–6 Nm contínuo + o pico por cima.
- **Diâmetro é torque.** Dois motores com o mesmo Kt mas tamanhos diferentes divergem na corrente que aguentam antes de saturar — o maior (hoverboard) faz mais torque real. É por isso que um hub grande gearless ganha de um frameless pequeno.
- **Fuja de caixa de redução.** Motor com engrenagem multiplica torque no papel mas adiciona backlash, atrito e inércia refletida — arruína o feel de DD. Mantenha gearless.
