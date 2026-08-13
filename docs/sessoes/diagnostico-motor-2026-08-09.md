# Diagnóstico: o motor inverte o torque — o que já foi eliminado

**Sintoma (palavras do usuário):** girando devagar, o motor "auxilia o giro como se pulasse uma
barreira"; pulando 2 ou 3 barreiras ele ganha velocidade e **dispara**. No batente, "puxa a força
para dentro do batente". Para não disparar, é preciso segurar o volante.

**Fato central, medido na caixa-preta (2026-08-09):**

```
   n     pos       vel       acel       torque comandado   Iq
 418    +451      +76      +450          −0,16 Nm        +0,1 A   ← entra devagar
 419    +454     +112     +1800          −0,46           −0,4
 421    +462     +238     +3600          −1,58           −2,0
 423    +480     +536     +8775          −4,03           −4,7
 424    +495     +765    +11475          −6,14          −10,5
```

O volante entra no batente a **76 °/s** e é acelerado **para dentro** até 770 °/s, enquanto o
firmware comanda torque **negativo** (para fora), crescendo até −6 Nm. A aceleração é proporcional ao
torque comandado, com **sinal invertido** e ganho aproximadamente constante (≈ −1870 °/s² por Nm).
Não é oscilação nem atraso: é o torque saindo com o sinal trocado, de forma limpa.

---

## Hipóteses ELIMINADAS por medição — não repetir

| # | Hipótese | Como foi medida | Resultado |
|---|---|---|---|
| 1 | CPR do encoder errado | 10 voltas contra marca física | **39961 contagens = 3996,1/volta**; configurado 4000 (0,10% de erro) |
| 2 | Pares de polos errados | ciclos elétricos por volta mecânica, 4,7 voltas | **15,00 ciclos/volta** exatos |
| 3 | Cálculo da fase elétrica | fase lida × fórmula `(count_in_cpr − offset) × 15 × 2π/4000` | erro mediano **1,1° elétrico** — fórmula confere |
| 4 | Wrap do `count_in_cpr` quebrando a fase | salto de fase vs deslocamento real em 6 wraps | erro máximo **1,3° elétrico** — tratado corretamente |
| 5 | Fase não-uniforme ("barreira" no cálculo) | razão medido/esperado, 406 passos | **0 desvios acima de 10%** |
| 6 | `direction` (sinal encoder↔motor) invertido | varredura de torque com `direction = −1` | pico **+1700** contra **+147416** com `+1` — o `+1` está certo |
| 7 | Contadores dessincronizados (`count_in_cpr` vs `shadow_count`) | diferença ao longo de 1,24 volta | variação de **3 contagens** (ruído) — sincronizados |
| 8 | Índice Z re-zerando a contagem em uso | leitura do código | `unsubscribe()` no callback — dispara só uma vez |
| 9 | Sinal da velocidade invertido (anti-damper) | `vel_estimate` vs movimento real | mesmo sinal em **1963 de 1966** amostras |
| 10 | Damper do volante causando o disparo | zerado no firmware | disparo sumiu no meio do curso, **mas continuou no batente** |
| 11 | Damper do batente causando o disparo | zerado no firmware | **continua invertendo** — não é damper |
| 12 | Regen / alimentação / brake resistor | erros latched + vbus na caixa-preta | vbus **27,1 V estável**, zero erro durante a inversão |
| 13 | Travamento do laço de controle | batimentos de tarefa + relógio do sistema | laço rodando normalmente durante o evento |
| 14 | Força vindo do jogo | `host` na caixa-preta | **0 em 4800 amostras** — é o nosso próprio torque |
| 15 | Ajustes do batente (mola, amortecimento, teto, faixa) | 6 gravações em 2026-08-08 | nenhuma resolveu; três pioraram |

## Descoberto e corrigido no caminho

- **`phase_offset` sistematicamente errado e diferente a cada boot.** A calibração produz 83, 109,
  113, 127, 131, 137, 155. A varredura de torque real (um `offset_sweep.cpp` de bancada, descartado depois) mostrou o pico em **184**,
  curva senoidal limpa, vale em 56. Traduzindo: offset 83 → torque **−72%**; 109 → **−19%**;
  137 → 45%; 155 → 78%; 184 → 100% (a corrente parada caiu de **18 A para 0,11 A**).
  → **Congelado em 184.**
- **Resistor de freio ficava desarmado** após a primeira proteção e o motor rearmava sem ele.
  → Re-armado a cada tick.
- **Sliders "Força do batente" e "Range do batente" não faziam nada** (nunca chegavam ao modelo).
- **O app reenvia settings e sobrescreve testes feitos por SWD** — flagrado com o damper voltando de
  0 para 0,267 no meio de um teste. Por isso o estado de investigação está travado no firmware.

## O que NÃO foi verificado (único suspeito restante)

**Os ganhos do laço de corrente**, que o ODrive deriva de R e L:

```
ganho proporcional = banda × L
ganho integral     = banda × R
```

Estes valores **nunca foram medidos neste conjunto**. Estão fixos no código, copiados de outra
placa, com a medição desligada de propósito (evitava um `DRV_FAULT` no bring-up):

```cpp
phase_resistance = 0.20f;    // "medido na placa antiga"
phase_inductance = 0.00035f;
pre_calibrated   = true;     // pula a medição
```

**Por que é o suspeito:** o motor obedece com **2 Nm / 3,6 A** (varredura) e inverte com
**6 Nm / 10-19 A** (batente). A diferença entre os dois casos é a **corrente**, não a posição — e um
laço de corrente mal sintonizado é estável com corrente baixa e perde o controle quando ela cresce.

**Como verificar:** `AXIS_STATE_MOTOR_CALIBRATION` mede R e L de verdade (~5 s, motor parado, apita).
Comparar com 0,20 Ω / 0,35 mH.

---

# Sessão 2 (tarde/noite) — o problema NÃO é o nosso software

## O teste que mudou o rumo (ideia do usuário)

Gravamos um **firmware de referência de terceiro** (binário pronto, obtido pelo usuário) na nossa
placa, com o nosso motor. Resultado relatado:

- **tem FFB** e **não treme em repouso** (o nosso treme — ver abaixo)
- **na T1 (chicane esquerda-direita) PERDE O FFB E INVERTE**
- repetiu numa curva forte passando por zebra
- depois de perder, **girar devagar dispara** — exatamente o nosso sintoma

**Firmware independente, validado por outros usuários, reproduz o defeito.** O problema não está no
nosso código. Isso invalida como causa raiz tudo o que perseguimos em software.

## Encoder A/B: DESCARTADO por medição

Monitor de perda de contagem usando o índice Z (que passa uma vez por volta num ponto físico fixo):
entre dois pulsos Z tem de haver exatamente `cpr` = 4000 contagens.

Resultado com o volante girando várias voltas, rápido e sacudindo, motor armado:

```
11 passagens: 4000 contagens EXATAS  (erro zero)
14 passagens: 3 a 4 contagens        (pulso Z DUPLICADO, 0,3° depois do real)
```

Os intervalos de 3-4 contagens **não são voltas perdidas** — são o índice Z disparando duas vezes na
mesma passagem. Ou seja:

- **os canais A/B não perdem contagem**, nem sob esforço → encoder descartado
- **o sinal Z tem repique**: nosso filtro de glitch só checa o nível do pino, não faz debounce por
  tempo, e aceita o segundo pulso. Defeito real, mas de outra natureza (afeta a busca de índice, não
  a posição).

## Limite de corrente do motor — estamos ACIMA

A documentação do FFBeast afirma: *"the motor can only pull a max of 15A at 24V. So a higher voltage
will only increase your RPM, not your torque."*

Nós usamos `current_lim = 25 A` e medimos picos de **18 a 19 A** — inclusive 18 A com o volante
PARADO por 20 s. Cerca de 25% acima. Consequências:

- acima da saturação, corrente extra vira **calor** sem virar torque (foi o que medimos)
- agrava o erro de comutação: o ângulo ótimo desloca ~4,9 contagens por ampère
- **pendente**: baixar `current_lim` para 15 A e reescalar o teto de torque do app (10 Nm assume
  ~18 A; com 15 A o máximo real fica em ~8 Nm)

## O tremor em repouso é NOSSO e foi introduzido depois de quarta

O firmware de referência **não treme**; o nosso treme. Medido no osciloscópio interno a 8 kHz
(o laço de controle): Iq com média +0,005 A, variação 0,067 A, frequência dominante ~1850 Hz,
**0,001 W** — perceptível ao toque, incapaz de aquecer. O "morno" do motor é calor residual das horas
com 10-18 A.

**Bissecção iniciada:** o firmware de quarta (`0d63667`) foi gravado e **confirmado sem tremor**.
A causa está em algum dos 13 commits entre `0d63667` e `2907cec`. Próximo passo: gravar `c782f22`
(calibração estática, o meio) e cortar pela metade.

## O que sobra para o problema principal

Com software, encoder A/B, CPR, pares de polos, cálculo de fase, R/L, damper, regen e travamento
todos descartados por medição, restam causas **físicas**:

1. fixação do rotor / ímãs no eixo
2. o próprio motor
3. o estágio de potência da placa — incluindo a suspeita do usuário de que o ST-Link alimentou a
   placa com **5 V no lugar de 3,3 V** em algum momento e danificou algo
