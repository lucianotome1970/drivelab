# Base FFB — cérebro portável + HAL testável / portable brain + testable HAL

> Design para desenvolver e testar a lógica FFB da base **sem a placa** (ODESC ainda não em mãos). O que precisa de silício (USB, real-time, motor) fica isolado atrás de interfaces; o resto roda e testa no PC.
>
> 🇬🇧 *Design to develop and test the base FFB logic **without the board**. Whatever needs silicon (USB, real-time, motor) sits behind interfaces; the rest runs and is tested on the PC.*

## 🇧🇷 Português

### Motivação

Emulador de STM32 (Renode/QEMU) resolve CPU/serial/periféricos, mas **não** o objetivo do M0.5 (enumeração USB↔Windows) nem o real-time. Já a **lógica** — a matemática força→torque, o soft-stop, as proteções, a máquina de settings — não precisa nem de placa nem de emulador: basta separá-la do hardware e testá-la no PC. É o "HIL simplificado" com mocks.

### Arquitetura

Três camadas, espelhando o que já fazemos no app (`DriveLab.Core` ↔ `Simulator`/`Hid`):

```
lib/brain/
  ffb_math.h        # funções PURAS (força→torque, soft-stop, corte de corrente). Zero deps.
  hal.h             # interfaces de hardware: IEncoder, ICurrentSense, IMotor
  ffb_controller.h  # orquestra 1 passo: lê HAL → torque seguro → comanda motor (+ desarme latched)
```

O `FfbController` só conhece as **interfaces** de `hal.h` — nunca o hardware. Isso dá **dois alvos de build** a partir do mesmo código:

| Alvo | HAL = | Onde |
|------|-------|------|
| **Firmware** (ODESC F405) | SimpleFOC (BLDCMotor) + ADC dos shunts + encoder ABZ/SPI | `pio run` |
| **Teste de host** (PC) | mocks (valores em variáveis) | `test/run.sh` |

### As interfaces (o "seam")

- `IEncoder` — `positionRad()`, `velocityRadPerSec()` (firmware: encoder via SimpleFOC).
- `ICurrentSense` — `readPhaseCurrents(ia, ib, ic)` (firmware: shunts + ADC) — usado na proteção.
- `IMotor` — `setTorque(nm)`, `disable()` (firmware: BLDCMotor em modo torque).
- `IPowerSense` — `busVoltage()`, `mosfetTempC()`, `motorTempC()` (firmware: ADC + NTC) — M2.
- `IBrakeResistor` — `setDuty(0..1)` (firmware: PWM num MOSFET) — M2.

### A matemática (pura, testável)

Básico:
- `forceToTorque(hostForce, ForceConfig)` — força FFB do host `[-255,255]` → torque (Nm), aplicando **força total** (0..100%) e o **teto duro de segurança** (`torqueLimitNm`).
- `endstopTorque(positionRad, EndstopConfig)` — **soft-stop**: 0 dentro da faixa, mola empurrando de volta além dela.
- `overCurrent(ia, ib, ic, limitA)` — **corte por sobrecorrente**.

**Modelagem de força (M5)** — casa 1:1 com os `BaseSettingId`:
- `responseCurve(norm, linearity)` — **curva de resposta** (`|x|^linearity·sinal`): >1 suaviza o leve, <1 realça o leve.
- `springTorque(pos, gain)` / `damperTorque(vel, gain)` / `frictionTorque(vel, nm)` — **efeitos de condição do device** (centragem/damper/atrito) computados do **encoder**, somados à força do jogo (`SpringStrength`/`DamperStrength`/`StaticDamping`).
- `slewLimit(target, prev, maxDelta)` — **slew-rate** (variação máx. de torque por passo; feel + protege a mecânica).
- `computeTorque(...)` — o **pipeline completo**: direção → curva → ganho→Nm → efeitos do device → soft-stop → **teto duro por último**. `ForceConfig` inclui `direction` (`ForceDirection`).

O `FfbController.step()` junta tudo (lê encoder pos+vel + corrente → `computeTorque` → `slewLimit` → comanda o motor): se `!enabled` ou desarmado → motor desligado; sobrecorrente → **desarme latched** (`tripped`, só volta com `rearm()`); slew-rate opcional entre passos.

**Proteção de potência (M2)** — camada separada, em `pi_controller.h` + `ffb_power.h`:
- `PiController` — PI genérico com **anti-windup** (integrador clampado ao range de saída); ganhos = `CurrentP`/`CurrentI`. *(A malha FOC de corrente em si roda no SimpleFOC; este é o componente/tuning testável.)*
- `BrakeController` — **brake resistor** com **histerese** (liga acima de `onVoltage`, só desliga abaixo de `offVoltage`) e **duty proporcional** até `fullVoltage`. Dissipa a regeneração antes de estourar a tensão.
- `overVoltage()` / `overTemp()` + `PowerGuard.step()` — comanda o brake e sinaliza **falha latched** por sobretensão/sobretemperatura (o laço então desliga a força). Sistema 24V → nunca deixar a tensão disparar.

**Sequência de partida (M1)** — em `startup.h`, `StartupSequencer` (máquina de estados **Idle→Aligning→Running→Fault**):
- **Inter-travamentos** — só arma com tensão na faixa `[busMinV, busMaxV]`, temperatura ≤ `tempMaxC` e sem falha da proteção.
- **Alinhamento** — energiza open-loop com torque baixo (`alignTorqueNm`) por `alignSeconds` para alinhar o rotor antes de liberar a força.
- **Rampa** — ao entrar em Running, a força sobe de 0→1 em `rampSeconds` (`rampGain()`), sem solavanco.
- **Falha com prioridade** — `guardFaulted` derruba para Fault em qualquer estado; sai só com `clearFault()` e **re-arma** se a causa persistir. O ângulo/FOC em si é do SimpleFOC; o sequenciador só decide *se/quanto* liberar.

**Reconstrução de força (rumo ao topo)** — em `force_reconstruct.h`, `ForceReconstructor`: o jogo manda força discreta a 60–360 Hz; o laço roda a 10–40 kHz. Segurar o último valor (ZOH) gera "degraus" (a sensação granulada). O reconstrutor **espalha cada atualização** ao longo dos ticks (rampa linear, janela `steps`) + **LPF opcional** → saída contínua ("silky"). É um dos algoritmos que separam DD bom de DD top — e é **mensurável no host**: o teste mostra o maior salto por tick caindo de ~100 (ZOH) para 12,5 (janela de 8). No firmware: `setTarget()` quando chega um FFB report; `tick()` a cada passo do laço, alimentando `computeTorque`.

**Cancelamento de cogging (rumo ao topo)** — em `cogging.h`: o cogging é ripple de torque **dependente da posição** (ímãs × ranhuras) — a maior causa de "granulado" em força pequena. `CoggingMap<N>` guarda a compensação por posição ([0,2π), N bins, interpolação + wrap) e `compensation(pos)` dá o feed-forward a **somar** ao torque. `CoggingCalibrator<N>` constrói o mapa girando o motor devagar: média por bin → remove o offset DC (carga/atrito, não é cogging) → inverte o sinal. Mensurável no host: o teste reconstrói um ripple sintético de ±0,2 Nm e o **cancela para <0,02 (~10× mais liso)**. Só a coleta das amostras precisa de bancada; a matemática (tabela, interpolação, calibração) está pronta e testada. No firmware: `torque += cog.compensation(encoderPos)` a cada tick; a tabela mora na flash, calibrada por-motor.

**Filtros DSP (rumo ao topo)** — em `filters.h`: um `Biquad` genérico (2ª ordem, Direct Form II transposto) com coeficientes RBJ para **low-pass** (suavizar força/velocidade) e **notch** (matar uma **ressonância mecânica** específica — o anti-oscilação da correia/eixo). Sobre ele, `VelocityEstimator` (diferença finita + low-pass) entrega a **velocidade limpa** de que dependem a qualidade do damper/inertia/friction. Mensurável no host: o teste confirma ganho DC = 1, esmagamento em Nyquist, rejeição >5× no notch e a estimativa de velocidade convergindo para a real. É a caixa de ferramentas do "FFB tuning" (os filtros ajustáveis dos menus top-de-linha).

### Log de diagnóstico + loop de feedback (design, para quando houver hardware)

A ideia (do usuário): **firmware+app geram um log; com o log + o que o usuário sentiu, refinamos as sensações.** O caminho:

1. **Firmware emite um quadro de diagnóstico** (telemetria estendida, sob demanda p/ não custar no uso normal): por tick (ou subamostrado) → `t`, força do jogo, força **reconstruída**, posição/velocidade do encoder, torque comandado, corrente medida, tensão de barramento, temperaturas, estado da máquina.
2. **App grava em CSV** e oferece um botão **"marcar"** — o usuário anota o instante em que sentiu algo ("aqui ficou notchy", "aqui clipou", "damper fraco"). O marcador entra na mesma trilha de tempo.
3. **Análise offline** reusa o **mesmo harness de host**: com o CSV, replicamos a entrada e comparamos algoritmos/parâmetros (janela de reconstrução, LPF, mapa de cogging, ganhos) **objetivamente** — casando a métrica com a sensação relatada.
4. **Ajusta → regrava → repete.** Cada volta liga um número a uma sensação; é assim que se persegue o topo com método, não por tentativa e erro.

Precisa de hardware só como **fonte de dados**; a infra de análise já existe (o host test). Concretamente falta: um report de diagnóstico no protocolo (base) + um gravador CSV no app (o app já tem telemetria 0x21/0x20 — estende-se para o modo diagnóstico).

### Rodar os testes (sem placa)

```bash
firmware-base/test/run.sh      # compila com -Wall -Wextra -Werror e roda no PC
# → "OK — N checks, 0 fail(s)"
```

Nenhuma dependência de PlatformIO/toolchain ARM — é só um `c++ -std=c++17`. Dá pra plugar em CI e, mais tarde, no `platformio test -e native`.

### Encaixe no roadmap / segurança

Ao escrever **M1 (motor open-loop)** e **M5 (força FFB → SimpleFOC)**, a lógica entra no `ffb_math.h`/`ffb_controller.h` e ganha teste de host **na hora** — não retrofit. A proteção (teto de torque, soft-stop, corte de sobrecorrente latched) é testada aqui antes de qualquer motor girar. Casado com uma **Black Pill F411** (~US$5) para o de-risk de USB, cobre-se quase tudo antes de encostar na ODESC.

### O que isto NÃO cobre

- **Enumeração USB** (M0.5) — precisa do periférico USB real (placa).
- **Timing / ISR / frequência da malha FOC** — precisa de hardware ou sim cycle-accurate.
- **Comportamento do SimpleFOC/driver** em si — mockamos as entradas, não o silício.

## 🇬🇧 English (summary)

Same idea: a **portable brain** (`lib/brain/`) with pure force→torque math (`ffb_math.h`), a hardware **seam** (`hal.h`: `IEncoder`/`ICurrentSense`/`IMotor`) and an orchestrator (`ffb_controller.h`) that only knows the interfaces. It compiles into the **firmware** (HAL = SimpleFOC/ADC/encoder) and into a **host test** (HAL = mocks) — run with `test/run.sh`, no board or emulator needed. Safety (hard torque cap, soft-stop, latched over-current trip) is unit-tested on the PC. It does **not** cover USB enumeration, real-time timing, or the SimpleFOC silicon — those still need a real STM32F4 (a cheap Black Pill F411 de-risks USB before the ODESC).
