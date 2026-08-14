# Placa zerada: o teste que achou seis defeitos numa tarde

> **PT abaixo · English below**

Em 14/08/2026 apagamos a configuração de uma base que funcionava havia meses e a ligamos como se
fosse nova. Ela **não armou**. Quando armou, entregava **40% da força**. O resistor de freio acionou
**674 mil vezes** com o volante parado, e a base **desarmava ao girar o aro**.

Nenhum desses defeitos era novo. Todos estavam no firmware havia semanas, invisíveis, porque nunca
tínhamos ligado o produto — só a nossa bancada.

---

## 🇧🇷 Português

### O que aconteceu

Seis defeitos numa sessão. Cinco deles são **o mesmo defeito**, e vale entender por quê antes de
olhar a lista.

O firmware define **parte** da configuração da placa e deixa o resto no que estiver na memória não
volátil. Numa base nossa, esse "resto" foi moldado por meses de bancada: sempre havia ali um valor
que funcionava. Numa placa nova ele cai no padrão do ODrive, e o defeito nasce pronto.

| o que a pessoa vê | a causa |
|---|---|
| a base nunca arma | `is_calibrated_` é copiado no boot, **antes** do nosso ajuste — o valor certo chegava tarde |
| a base parece fraca | a migração da curva rodava sem haver curva a migrar, e achatava a de fábrica |
| o resistor de freio esquenta parado | sem banda morta, o ruído da corrente já aciona o chopper |
| a base "granula" ao girar | o limite de regeneração padrão é −0,1 A: girar o aro passa disso com folga |
| a placa congela | a ferramenta de diagnóstico é que parava o processador — veja abaixo |
| o resistor de freio estava cravado | quem monta 12 Ω e o firmware acredita em 2 Ω erra por seis vezes |

### Por que os padrões do ODrive não servem para um volante

Não são valores descuidados. O ODrive é um controlador de robótica, e os padrões dele assumem um
motor que é **acionado**: a regeneração acontece quando algo desacelera, de vez em quando.

Num volante o motor é **retroacionado por uma pessoa o tempo todo**. Cada curva devolve energia ao
barramento. Provavelmente somos uma das aplicações mais atípicas do ODrive — e a conclusão prática é
que **todo padrão dele merece uma decisão explícita nossa**, não apenas os que já nos morderam.

### A ferramenta de diagnóstico causava o defeito

Havia meses um sintoma sem explicação: a base congelava, e só tirar da tomada resolvia. Nunca
reproduzíamos sob observação.

A causa: a pilha USB tem uma instrução que **para o processador de propósito quando há um gravador
conectado** — e o gravador estava conectado justamente quando observávamos. Sem ele, a mesma
condição passava batido.

É a mesma doença dos cinco defeitos acima, com outra roupa: **a nossa bancada não era o produto.**
Configuração temperada por nós, gravador plugado, e um sintoma que só existia do nosso lado.

### As provas de vida não provavam nada

Dois diagnósticos atrasaram pelo mesmo motivo. Perguntávamos "o firmware está vivo?" olhando o
relógio do sistema — que é alimentado por interrupção e **continua andando com as tarefas todas
paradas**. Todo teste dava positivo com o USB morto.

Vale como regra: **só serve como prova de vida aquilo que para quando o processador para.**

### O que fizemos para isso não voltar

Corrigir os seis era o mínimo. O que fecha o padrão é `scripts/check-config-declarada.py`, que roda
junto dos testes e cobra **uma decisão por campo**: ou o firmware o define, ou ele está numa lista de
herdados **com o motivo escrito**. Não exige que tudo seja definido — exige que nada fique sem
decisão.

Hoje são 46 campos: 19 definidos e 27 herdados por escrito.

É o espelho do `check-orphan-settings.py`, que já cobria o outro lado da mesma lacuna: lá, o app
transporta um ajuste e o firmware o ignora; aqui, o firmware depende de um valor e ninguém o declara.

#### A razão também tem força, e o script mostra qual

Cobrar que exista uma razão não basta: uma razão fraca satisfaz o teste igual a uma medição. Foi
assim que o limite de regeneração passou — **−0,1 A também parecia razoável para quem lia sem girar o
volante**.

Por isso cada campo herdado declara de onde veio a certeza, e o número aparece a cada rodada:

| procedência | quantos | o que significa |
|---|---|---|
| `NAO_SE_APLICA` | 12 | hardware que não temos (Hall, sin/cos, indução) ou campo morto no v0.5.6 |
| `SAIDA_CALIB` | 3 | é resultado, não entrada — escrever descartaria a medição |
| `MEDIDO` | 3 | conferido **na bancada**, com o efeito observado |
| `PENDENTE` | 2 | sai da lista quando o Z e o SPI absoluto entrarem |
| `A_VERIFICAR` | **7** | julgamento por leitura de código, **sem teste** |

Os sete de `A_VERIFICAR` são a dívida real, e o script os imprime pelo nome toda vez:
`torque_lim`, `I_bus_hard_min`, `I_bus_hard_max`, `I_leak_max`, `dc_calib_tau`,
`calib_scan_distance`, `enable_phase_interpolation`.

Ele **não reprova o build** por causa deles: são trabalho de bancada, e reprovar aqui só ensinaria a
esvaziar a lista sem testar nada — o mesmo vício do teste que afirmava o comportamento errado da
curva de força. Ao exercitar um deles na bancada, ele é promovido a `MEDIDO` com o que foi observado.

### Como repetir o teste na bancada

**Rotina, antes de fechar qualquer trabalho de firmware:** apagar a configuração, gravar, ligar e
testar. Isso reproduz o estado de configuração de uma placa nova, que é o que causou cinco dos seis
defeitos — e não exige gravar o firmware de fábrica.

**Ocasional, antes de uma entrega:** o caminho completo de quem está começando — firmware de fábrica,
instalação por USB, importar os ajustes. Só ele cobre a **experiência de instalação**, que a rotina
acima não toca.

Os dois testam coisas diferentes, e é por isso que os dois existem.

---

## 🇬🇧 English

### What happened

On 2026-08-14 we wiped the configuration of a wheelbase that had worked for months and powered it up
as if it were new. It **would not arm**. Once it armed, it delivered **40% of the force**. The brake
resistor fired **674,000 times** with the wheel standing still, and the base **disarmed whenever the
rim was turned**.

None of these were new. All had been in the firmware for weeks, invisible, because we had never once
powered up the product — only our own bench.

Six defects in one session. Five of them are **the same defect**, and it's worth understanding why
before reading the list.

The firmware sets **part** of the board's configuration and leaves the rest at whatever is in
non-volatile memory. On one of our boards that "rest" had been shaped by months of bench work: there
was always something there that worked. On a fresh board it falls back to the ODrive defaults, and
the defect is born ready.

| what the user sees | the cause |
|---|---|
| the base never arms | `is_calibrated_` is copied at boot, **before** our setup — the right value arrived too late |
| the base feels weak | the force-curve migration ran with no curve to migrate, flattening the factory one |
| brake resistor heats up at rest | with no dead band, current noise alone fires the chopper |
| the base feels "gritty" when turned | the default regen limit is −0.1 A; turning the rim clears it easily |
| the board freezes | the diagnostic tool was halting the processor — see below |
| brake resistance was hard-coded | someone fitting 12 Ω while the firmware believes 2 Ω is off by six times |

### Why the ODrive defaults don't suit a steering wheel

They are not careless values. ODrive is a robotics controller, and its defaults assume a motor that
is **driven**: regeneration happens when something decelerates, occasionally.

In a wheel the motor is **back-driven by a human continuously**. Every corner pushes energy back into
the bus. We are probably one of ODrive's most unusual applications — and the practical conclusion is
that **every default deserves an explicit decision from us**, not just the ones that have already
bitten.

### The diagnostic tool caused the defect

For months there was a symptom with no explanation: the base froze, and only unplugging it helped. We
could never reproduce it under observation.

The cause: the USB stack contains an instruction that **halts the processor on purpose when a
programmer is attached** — and the programmer was attached precisely when we were watching. Without
it, the same condition passed unnoticed.

It's the same disease as the five above, wearing different clothes: **our bench was not the product.**

### Liveness checks that proved nothing

Two diagnoses were delayed for the same reason. We asked "is the firmware alive?" by watching the
system clock — which is driven by an interrupt and **keeps counting while every task is stuck**.
Every check came back positive with USB dead.

Worth keeping as a rule: **only something that stops when the processor stops counts as proof of
life.**

### What we did so it doesn't come back

Fixing the six was the minimum. What closes the pattern is `scripts/check-config-declarada.py`, which
runs with the tests and demands **one decision per field**: either the firmware sets it, or it is on
an inherited list **with the reason written down**. It doesn't require everything to be set — it
requires nothing to be left undecided.

Today that's 46 fields: 19 set, 27 inherited on the record.

It mirrors `check-orphan-settings.py`, which already covered the other side of the same gap: there,
the app carries a setting the firmware ignores; here, the firmware depends on a value nobody
declares.

#### Reasons have strength, and the script reports it

Demanding that a reason exist isn't enough: a weak reason satisfies the check just like a
measurement does. That is how the regen limit got through — **−0.1 A also looked reasonable to
anyone reading without turning the wheel**.

So every inherited field declares where the certainty came from, and the tally prints every run:

| provenance | count | meaning |
|---|---|---|
| `NAO_SE_APLICA` | 12 | hardware we don't have (Hall, sin/cos, induction) or a field that is dead in v0.5.6 |
| `SAIDA_CALIB` | 3 | it's an output, not an input — writing it would discard the measurement |
| `MEDIDO` | 3 | verified **at the bench**, with the effect observed |
| `PENDENTE` | 2 | leaves the list when the Z index and absolute SPI land |
| `A_VERIFICAR` | **7** | judgement from reading the code, **untested** |

The seven under `A_VERIFICAR` are the real debt, and the script names them every run:
`torque_lim`, `I_bus_hard_min`, `I_bus_hard_max`, `I_leak_max`, `dc_calib_tau`,
`calib_scan_distance`, `enable_phase_interpolation`.

It deliberately **does not fail the build** over them: they are bench work, and failing here would
only teach people to empty the list without testing anything — the same vice as the test that
asserted the wrong force-curve behaviour. Exercising one at the bench promotes it to `MEDIDO` with
whatever was observed.

### Repeating the test at the bench

**Routine, before closing any firmware work:** wipe the configuration, flash, power up, test. That
reproduces the configuration state of a new board — the cause of five of the six defects — and does
not require flashing the factory firmware.

**Occasional, before a release:** the full path a beginner takes — factory firmware, install over
USB, import settings. Only that covers the **installation experience**, which the routine above never
touches.

They test different things, which is why both exist.

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
