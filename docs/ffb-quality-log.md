# FFB — Registro de melhorias e descobertas / FFB quality log

> Log curado das alavancas de qualidade e descobertas do Force Feedback do DriveLab — cada uma com **o problema, a abordagem, o resultado MEDIDO e o status**. O "como está implementado" fica em **[base-ffb-brain.md](base-ffb-brain.md)**; aqui é o "o que aprendemos e provamos".
>
> 🇬🇧 *Curated log of DriveLab's FFB quality levers and discoveries — each with the problem, approach, measured result and status. Implementation details live in [base-ffb-brain.md](base-ffb-brain.md); this is the "what we learned and proved".*

**Método (o pulo do gato):** a lógica crítica de FFB e segurança mora num **cérebro portável** (`firmware-base/lib/brain/`) atrás de interfaces de hardware. Ele compila no firmware **e** num alvo de teste no PC (`firmware-base/test/run.sh`, mocks). Assim, cada melhoria é **desenvolvida, medida e provada ANTES da placa** — as métricas abaixo saem de testes que rodam sem hardware (**107 checks, 0 falhas** hoje). Emular STM32 (Renode/QEMU) resolveria serial/lógica, mas não o USB nem o real-time; a lógica a gente resolve melhor com mocks, e o USB com uma placa barata (Black Pill F411).

---

## Alavancas de qualidade (rumo ao topo)

| # | Alavanca | Por que importa | Resultado MEDIDO (host) | Status |
|---|----------|-----------------|--------------------------|--------|
| 1 | **Força → torque** (`computeTorque`) | base de tudo: força do jogo → Nm com força total, direção e **teto duro** | escala linear + clamp corretos | ✅ lógica pronta |
| 2 | **Curva de resposta** (linearidade) | molda o feel (realça/suaviza forças leves) | `|x|^γ` correto (γ>1 suaviza, γ<1 realça) | ✅ |
| 3 | **Efeitos de condição do device** (mola/damper/atrito) | "feel" base sobre o FFB do jogo; **e a cura do tremor** (ver descobertas) | mola centra, damper opõe velocidade, atrito opõe movimento | ✅ |
| 4 | **Soft-stop** (fim de curso) | protege a mecânica e dá o "batente" | 0 na faixa, mola crescente fora | ✅ |
| 5 | **Reconstrução de força** | jogo manda força discreta (60–360 Hz); laço roda a 10–40 kHz → segurar o valor gera "degraus" (granulado) | **maior salto por tick: ~100 → 12,5** (janela de 8) | ✅ |
| 6 | **Cancelamento de cogging** | ripple do motor dependente da posição = a maior causa de "granulado" em força leve | **ripple ±0,2 Nm → <0,02 (~10× mais liso)** | ✅ math; calibração precisa de bancada |
| 7 | **Filtros DSP** (Biquad low-pass / **notch**) | suavizar + **matar ressonância mecânica** (anti-oscilação de correia/eixo) | DC=1, Nyquist <0,1; notch **>5×** de atenuação em f0 | ✅ |
| 8 | **Estimador de velocidade** | damper/inertia de qualidade dependem de velocidade limpa | converge para a velocidade real, suave | ✅ |
| 9 | **Slew-rate** | limita a variação de torque (feel + protege a mecânica) | degraus limitados por passo | ✅ |
| 10 | **Pipeline completo** (`FfbEngine`) | um `step()` encadeia tudo + HAL — é o que o firmware chama | end-to-end: partida→rodando→2,5 Nm→falha→0 | ✅ |

---

## Segurança (M1/M2) — testada antes de qualquer motor girar

| Item | O que faz | Resultado MEDIDO | Status |
|------|-----------|------------------|--------|
| **Sequência de partida** (`StartupSequencer`) | Idle→Alinhamento→Rodando→Falha; só arma com inter-travamentos; alinha o rotor; rampa de subida | transições + rampa 0→1 corretas; falha com prioridade | ✅ |
| **Inter-travamentos** | não arma fora da faixa de tensão / quente / em falha | bloqueia arme em cada condição | ✅ |
| **Brake resistor** (`BrakeController`) | dissipa a regeneração (histerese + duty proporcional) antes de estourar a tensão | liga/desliga com histerese, duty proporcional, clamp | ✅ |
| **Cortes de proteção** (`PowerGuard`) | falha latched por sobretensão/sobretemperatura → desliga a força | desarma e mantém latched | ✅ |
| **Sobrecorrente** | desarme latched, motor desligado, só volta com rearme | testado no controller + engine | ✅ |
| **PI anti-windup** (`PiController`) | malha fechada genérica (CurrentP/I) sem estouro do integrador | integrador clampado ao range de saída | ✅ |

---

## Descobertas

### 🔎 O "tremor" do FFB (oscilação de mãos-fora)

**Sintoma (relato real, MOZA R9):** com o jogo aberto e sem segurar (só a base, ou soltando o volante numa reta), a base **tremia violentamente** e não dava pra segurar. Atualizações de firmware corrigiram depois.

**Causa (física, não bug de marca):** volante + jogo formam uma **malha fechada** (o jogo centra o carro lendo a posição). Com **latência** (USB + taxa de update) e **sem amortecimento** (mãos fora), a força chega **atrasada** → vira **amortecimento negativo** → a oscilação cresce. As mãos normalmente amortecem e estabilizam.

**O fix:** **amortecimento ativo** — torque proporcional à **velocidade local** (`−D·ω`), sem atraso (medido na base). É o nosso `damperTorque` (`DamperStrength`/`StaticDamping`), reforçado por `notch` (mata a ressonância) e `frictionTorque`.

**Provado no host (teste 17):** malha volante+jogo com 12 ms de atraso.

| | pico da oscilação (últimos 25%) |
|---|---|
| **sem damper** | **4,7 rad** (cresce de 0,1 → tremor) |
| **com damper** (D=0,3) | **~0,0 rad** (decai → estável) |

**Limiar dimensionável:** o atraso injeta ~`Ks·τ` de amortecimento negativo (aqui ~0,18), então o damper precisa passar disso — por isso D=0,05 não bastou e D=0,3 estabilizou. **Conclusão: sim, nosso DD teria o tremor sem contramedidas — mas já temos o fix, e conseguimos dimensioná-lo e prová-lo sem placa.**

### 🔎 M0.5 (USB/FFB) agora compila no F405

O rascunho de enumeração como volante FFB (o "principal de-risk") **nunca tinha compilado**. Dois consertos o fizeram buildar (3,5% de flash): (1) o header real do shim é **`USBAPI.h`** (não existe `USBLibrarySTM32.h`); (2) a lib de FFB usa a macro AVR **`_delay_us()`** → mapeada p/ `delayMicroseconds()` via header force-included (`include/avr_compat.h`), sem editar a lib de terceiros. Ver `firmware-base/README.md`.

### 🔎 O padrão que emergiu

**Separe o que precisa de silício do que é lógica.** Lógica/matemática/segurança → mocks no PC (feito). USB e real-time → placa (Black Pill de-risca antes da ODESC). Isso deixou resolver problemas reais de topo (tremor, cogging, reconstrução) **antes do hardware**, com método (simular → medir → afinar).

---

## Como reproduzir as métricas

```bash
firmware-base/test/run.sh      # compila o cérebro com mocks e roda os 107 checks (sem placa)
```

Cada número desta página sai de um bloco de teste em `firmware-base/test/test_ffb_brain.cpp` (blocos 1–17). O `printf` do bloco 17 imprime os picos da simulação de estabilidade.

## O que falta (precisa de bancada)

- Validar o M0.5 na ODESC (enumerar FFB no Windows) — ou numa **Black Pill F411** (~US$5) antes.
- Implementar o HAL sobre **SimpleFOC / ADC / NTC / PWM** (`IEncoder`/`IMotor`/`ICurrentSense`/`IPowerSense`/`IBrakeResistor`).
- **Calibrar o cogging** por-motor (a única parte do cogging que precisa girar o motor).
- **Afinar os números contra a sensação** via o loop de **log de diagnóstico + feedback** (desenhado em [base-ffb-brain.md](base-ffb-brain.md)).

## Próximas alavancas (modeláveis/mensuráveis sem placa)

Interpolação de encoder (resolução efetiva maior), filtro de reconstrução preditivo (menos latência), detector de oscilação (rampa de força se detectar limit-cycle), auto-caracterização do motor (R/L, pares de polos, mapa de cogging).

---

## 🇬🇧 English (summary)

DriveLab's FFB logic and safety live in a **portable brain** tested on the PC (no board, 107 checks). This log records each quality lever with its **measured** result: force→torque + response curve, condition effects (spring/damper/friction), soft-stop, **force reconstruction** (per-tick jump 100→12.5), **cogging cancellation** (±0.2 Nm ripple → <0.02), **DSP filters** (low-pass/notch, velocity estimator), slew-rate, and the full `FfbEngine` pipeline — plus the safety layer (startup sequence, interlocks, brake resistor, over-voltage/temperature/current trips, anti-windup PI).

**Key discovery — the FFB "shake" (hands-off oscillation):** any DD oscillates without countermeasures — it's physics (game↔wheel closed loop + latency + no damping → the delayed force acts as *negative* damping). The fix is **active damping** from local velocity (`−D·ω`), i.e. our device damper (+ notch + friction). Proved on host: peak grows to **4.7 rad without damper**, decays to **~0 with damper (D=0.3)**; the needed damping is derivable (~`Ks·τ`). So yes, ours would shake without countermeasures — but we already have the fix and can size/prove it before any hardware. Reproduce with `firmware-base/test/run.sh`.
