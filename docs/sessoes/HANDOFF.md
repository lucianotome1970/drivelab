# DriveLab — Handoff

> Nota pra retomar o trabalho em **qualquer máquina**. O contexto detalhado está na memória do
> Claude Code (`~/.claude/projects/<projeto>/memory/`) — mas o essencial está aqui, no git.

## 📍 ESTADO ATUAL (2026-08-11, fim da sessão de bancada no Windows)

| | |
|---|---|
| Último firmware **VALIDADO** | `ba6643b` — a `main` de hoje ← ponto de retorno |
| Ponto de retorno anterior | `f24afab` (10/08), se algo der muito errado |
| Release publicada | **`v0.2.2-alpha`** — cortada hoje, a partir do validado |
| Repositório | descrição bilíngue + 18 topics prontos. **Visibilidade: ver abaixo — não assuma** |
| Testes | **507 no app** (207 Core + 58 Hid + 242 Studio) + **7 de host** no firmware |

**O dia em uma linha:** 11 voltas em Monza sem perder FFB, e depois disso a descoberta de que a
base entregava ~28% menos força do que a conta dizia (Kt de catálogo) e que a curva de resposta
achatava as forças médias pela metade (`linearity` 1,59). Os dois consertados e sentidos na pista.

## 🔒 Visibilidade do repositório — não mexer

Público ou privado é decisão do dono do projeto, trocada por ele quando quiser. **Não alterar, e não
registrar o estado em documento** — ele muda. Quem precisar saber confere na hora:

```bash
gh api repos/lucianotome1970/drivelab --jq .visibility
```

Se alguma tarefa depender de ser público (indexação, alguém de fora baixar a release), avisar e
deixar a ação com o usuário.

## 🔧 O QUE FAZER PRIMEIRO NA BANCADA (amanhã, 12/08)

**1. Decidir o que fazer com os 28% de clipping.** O medidor novo já foi exercitado em pista e o
número é honesto: o teto passou a ser o **menor** entre o de configuração (15 Nm) e o que a corrente
permite (`current_lim × Kt` = 25 × 0,39 = **9,75 Nm**). Ou seja, pede-se 15 e entrega-se 9,75.

Duas saídas, e a escolha é do usuário:

| | efeito | custo |
|---|---|---|
| subir `current_lim` | mais torque real | calor cresce com o QUADRADO da corrente |
| baixar força total / limite máximo | resposta mais linear e previsível | menos pico |

**2. Olhar o FET junto.** Primeira vez que existe termômetro: **43 °C** em repouso é a referência.
O motor continua sem sensor — a mão nele segue sendo a única proteção.

**3. Teste 2 do encoder (pendente desde 11/08).** Escolher **MT6701 em SSI**, salvar, reiniciar: a
base tem de continuar normal em **A/B/Z**. Antes de `a39ddb6` ela passaria a ler o ângulo errado
por um fator de quatro, sem nada acusar. O teste 1 (A/B/Z idêntico) já passou.

Volta rápida se der ruim:

```bash
git checkout f24afab -- firmware-base/
cd firmware-base && make -j8      # e regravar
```

## 🔬 Kt: como medir de novo (vale para qualquer motor)

O `0,55` era catálogo (`8,27/KV`). O medido nesta bancada é **0,397**. Método sem risco, sem gravar
firmware: motor **armado**, jogo **fechado** (torque comandado zero), e o volante girado à mão —
o controlador aplica a tensão que cancela a back-EMF, e ela é proporcional à velocidade.

```
λ = (Vq − R·Iq) / ω_elétrico        Kt = 1,5 × pole_pairs × λ
```

Script pronto em `scratchpad/kt.py`; a coleta é um laço TCL dentro de UMA sessão do openocd (uma
invocação por amostra é lenta demais). Endereços saem do ELF a cada build — **`vel_estimate_` é um
`OutputPort<float>`, o valor fica 4 bytes depois do início** (o começo é o `age_`, que dá zero
sempre e me custou uma coleta inteira).

Usar velocidades variadas e regressão por origem, nunca ponto único. A de hoje: 234 amostras até
585 °/s, erro mediano de 11%.

## 📦 Release

A `v0.2.2-alpha` foi cortada hoje a partir de `ba6643b`, que é o validado. A `v0.2.1-alpha`
apontava para `843d6f6` e tinha **o batente desligado** por uma flag de teste esquecida — quem
baixasse pegaria um firmware pior que o da `main`.

**Convenção dos arquivos** (errei nela e o usuário corrigiu): nomes **sem versão** — a versão está na
tag — e uma tag só, `vX.Y.Z-alpha`. **Não existe tag `studio-v*` nem release do Studio separada.**
Os oito arquivos de sempre:

```
DriveLab.Studio.exe          firmware-base-wheelbase.bin / .hex
firmware-pedal-rp2040.uf2    firmware-handbrake-rp2040.uf2
firmware-wheel-rp2040.uf2    firmware-wheel-pico.uf2       SHA256SUMS.txt
```

Os módulos que não mudaram no ciclo são reaproveitados da release anterior (baixar os assets pela
API e re-subir). **As notas são bilíngues**, PT primeiro e depois `# English`, como os READMEs.

Não há `gh` CLI nesta máquina, mas dá para publicar pela API do GitHub usando a credencial que o
git já tem: `git credential fill` devolve o token, e daí é `POST /releases` + upload dos assets.

O workflow `windows-installer.yml` (instalador com driver DFU) dispara na tag `studio-v*` — que não
usamos mais. Para gerar o instalador, rodar o workflow **manualmente** pelo Actions
(`workflow_dispatch`, pede a versão).

## 🧭 Setting salvo ≠ setting APLICADO (levantado em 10/08)

**De 48 settings, 32 são lidos pelo firmware e 16 são IGNORADOS** (contagem de 11/08 — a atual sai
do `check-orphan-settings.py`). O campo aparece na tela, é
gravado na placa, volta correto ao reiniciar — e não faz nada. Foi assim com o CPR do encoder até
`0793adc`.

`scripts/check-orphan-settings.py` (roda no `scripts/test.sh`) **impede a lista crescer**: setting
novo que o firmware ignore quebra a suíte com o nome dele. Os 16 atuais são dívida registrada, com
o motivo de cada um — implementou, remove a linha.

**Já resolvidos:** `EncoderCpr`, `PolePairs`, `CalibrationCurrent` — a trinca que fazia o firmware
ser "o firmware do NOSSO motor".

**Os que mais doem agora:** `CurrentP` e `CurrentI`. São os ganhos da malha de corrente, o parâmetro
mais delicado da placa, e a aba Hardware deixa ajustar os dois **sem que cheguem no firmware** (a
placa usa os da NVM do ODrive). Estavam escondidos porque o próprio verificador contava a atribuição
do default como uso — corrigido em `b54ffaa`.

## ⚙️ Regra da aba Hardware: só vale no boot

A aba configura a **máquina** (encoder, geometria do motor, malha de corrente), e o firmware lê
esses campos **só na inicialização**. Não é comodidade: trocar número de polos ou ganhos da malha
com o motor girando é corrente no lugar errado, com a mão da pessoa no volante.

A aba já avisa isso na tela (`af11ffe`). **Ao implementar qualquer órfão da aba Hardware, ler no
boot — nunca ao vivo.**

## 📥 Padrões vêm do firmware (firmware PRONTO, app PENDENTE)

Os defaults existiam em **dois lugares** — o array `s_idef[]` no firmware e o campo `Default` de cada
descritor no app. Hoje eles coincidem, mas nada garantia isso: bastava editar um lado. O sintoma
seria cruel — "padrão" no app escreveria valores diferentes dos de uma placa recém-gravada, e os dois
pareceriam "o padrão".

**Feito no firmware:** report `0x17` (DEFREAD) — "qual é o padrão do campo X?", responde pelo mesmo
`0x16` da leitura normal. É **consulta pura**: não altera valor nenhum na placa.

**Falta no app:** mandar o `0x17` no botão "Padrão", exibir o que voltar, e deixar o **Salvar** ser
o único gesto que muda a placa. Precisa da constante, do método no transporte (real e simulador) e
do botão.

Desenho combinado com o usuário: clicar em Padrão **não altera nada na base** — só preenche a tela.

## 🚨 O QUE PRECISA DE BANCADA (nesta ordem)

1. ~~**Gravar com os settings NO PADRÃO e confirmar que NADA mudou.**~~ ✅ **VALIDADO em
   2026-08-10.** E validado do jeito mais forte possível: o usuário **alterou os pares de polos de
   propósito**, salvou (nada mudou na hora — a aba Hardware só vale no boot, como projetado),
   reiniciou, e a base **calibrou só para um lado** — o erro esperado para pares errados. Corrigiu
   o valor, reiniciou, calibrou normal, foi para a pista e rodou bem.
   Ou seja: o setting **chega mesmo no firmware** (não é placebo), **erra visivelmente quando o
   valor está errado**, e **com o valor certo o comportamento é o de sempre**. Os três lados da
   garantia de uma vez.
2. **Medidor do chopper** — zig-zag forte com o resistor ligado: acionamentos sobem, energia cresce.
   Depois, base ligada e **parada** por um minuto: os números **não podem se mover**. Se subirem
   parados, o chopper está conduzindo em repouso — foi esse erro que torrou um resistor de 50 W.
3. **Picos de corrente ±** — dirigir e olhar a assimetria. Um lado muito maior que o outro aponta
   referência de posição deslocada.
4. **Trocar a resolução no app e salvar** — ao reiniciar, a base deve **recusar armar** e pedir
   calibração. Calibrar, validar, e devolver o valor certo.

O passo 1 foi validado: a `v0.2.1-alpha` pode perder a ressalva de não-validada.

**Novo desde o handoff:** `current_lim` também deixou de ser cravado (setting 48, default 25 A =
o valor que estava no código). Com ele, o teto da corrente de calibração passa a significar algo em
qualquer bancada — antes se apoiava num 25 A fixo, então só estava correto para este motor.
Achado que motivou: a placa tinha **30 A** de corrente de calibração dormindo na flash, inerte
enquanto o setting era ignorado, e que acordaria ao passar a ser aplicado.

## 🔴 O BUG QUE MOTIVOU ESTA SESSÃO — o CPR era CRAVADO

O CPR estava escrito no firmware: **4000**, de um encoder de 1000 PPR. O app deixava escolher o
sensor, salvava na flash, mostrava de volta corretamente ao reiniciar — **e nada disso chegava na
placa**.

O tamanho do erro dependia do encoder de cada um: 1024 PPR dava 4096 contra 4000 (2,4%, quase
imperceptível); 2500 PPR dá 10000 contra 4000 — **a base leria uma volta como duas e meia**; um
magnético por SSI, 16384, erraria por quatro vezes.

Isso tornava o firmware **inutilizável por outra pessoa** e impedia a Fase 2 do roadmap: não se
distribui um binário com o encoder de uma bancada específica escrito dentro.

**Como a garantia de não regredir foi construída:** o default do setting é 4000, o mesmo valor que o
bring-up cravava, e `cpr == 0` ("não informado") não aplica nada. Nos dois caminhos o resultado é
idêntico ao anterior, e há teste de host fixando isso (`test_encoder_config.c`, primeiro caso).

## ⚙️ Como se configura um encoder agora

Duas perguntas separadas, porque são separadas:

- **Modelo** — o sensor que a pessoa comprou (lista que cresce com o catálogo)
- **Tecnologia** — como ela ligou; só aparecem as opções que **aquele** sensor oferece (o AS5047P
  não tem SSI, então não dá pra escolher)

E a resolução:

- **ABZ** → digita-se os **pulsos por volta impressos no encoder**; o app faz o ×4 da quadratura
- **SSI/SPI em sensor conhecido** → vem do silício, campo travado (MT6701 tem exatamente 16384)

⚠️ **O E6B2-CWZ6C é uma FAMÍLIA** (100 a 2500 PPR) e o número está no código do modelo
(`E6B2-CWZ6C 1000P/R` = 1000). Por isso o catálogo **não** crava resolução pra ele — a pessoa lê a
etiqueta. O da nossa bancada é o de **1000** (portanto 4000 contagens).

### O que a base sabe acionar (11/08, commit `a39ddb6`)

Até 11/08 o firmware aplicava só a **resolução** e descartava o modo. Escolher MT6701 em SSI
gravava 16384 contagens por cima de uma leitura A/B/Z: a base seguia girando reportando ângulo
errado por um fator de quatro, sem nada acusar. **Aplicar metade é pior que ignorar.**

Agora `encoder_config_from_settings` devolve `drivable`, e combinação sem driver **não aplica
NADA** — fica o bring-up, que funciona.

| Combinação | Estado |
|---|---|
| Qualquer modelo em **A/B/Z** | ✅ aplica CPR + modo incremental — caminho validado |
| **AS5047P** em SPI | ✅ modo absoluto AMS |
| **MT6701** em SSI | ⛔ sem driver → não aplica nada |
| **MT6835** em SPI | ⛔ sem driver → não aplica nada |

O AS5047P funciona porque o ODrive vendorizado já tem o ajuste de baud para o MISO desta placa e o
tratamento do **bit EF** do AS5047 (fica preso e rejeita toda leitura). Isso já existia — só nunca
era alcançado, porque o bring-up cravava `MODE_INCREMENTAL` e o apply nunca tocava no modo.

**Para escrever os drivers que faltam:**

- **MT6701** — a parte difícil já é nossa e testada: quadro de 24 bits + CRC-6 em
  `firmware-base/inc/magnetic_decode.h`. Falta ligar no encanamento SPI do ODrive.
- **MT6835** — o **OpenFFBoard** (que é **MIT**, mesma licença nossa) tem `MtEncoderSPI`, cobrindo
  `mt6825` e `mt6835`. É adaptação legal, não escrita do zero. Cópia local em
  `~/Downloads/OpenFFBoard-master`.

### Autocentralização: dá, com um limite geométrico

Os três magnéticos são absolutos de **uma volta**. O volante gira 900° (duas voltas e meia), então
o sensor entrega a posição **módulo uma volta** — falta saber qual volta.

`firmware-base/inc/center_recovery.h` (commit `ee48f7c`, **lógica pronta, NÃO ligada**) resolve
gravando o ponto do círculo onde fica o centro e a posição ao desligar; no boot escolhe, entre as
voltas possíveis, a mais próxima daquela. Volante parado no rig entre sessões recupera exato.

Perto de meia volta de discrepância, *"andou +0,45"* e *"andou −0,55"* são a **mesma leitura** e não
há como desempatar — ali devolve `trusted=0`. Isso não é conveniência: **errar a volta joga o
batente uma volta inteira para o lado errado**, e o volante giraria muito além de onde o piloto
espera antes de encontrar o limite.

Ligar só quando existir um absoluto funcionando. Ninguém inclui o header hoje, e o `make` nem
recompila — o binário é byte a byte o mesmo.

## 🌡️ Térmica — os FETs já estão cobertos; falta o sensor DO MOTOR

**Atualizado em 12/08.** O trecho abaixo dizia que não havia medição nenhuma e que os quatro
settings térmicos eram ignorados. Isso mudou nos dois lados:

- **FETs**: a temperatura é lida e exibida (~43 °C em repouso), e o `FetTempLimitC` deixou de ser
  órfão — o corte térmico do estágio de potência **está ligado**.
- **Motor**: continua **sem sensor**, e é a que mais importa (o limite prático do conjunto é o calor
  do motor). Seguem ignorados `MotorTempLimitC`, `ThermalContinuousPct` e `ThermalPeakSeconds`,
  porque dependem dele. **Aqui o limite térmico ainda é a mão no motor.**

A boa notícia para fechar o que falta: o ODrive vendorizado **já tem** `ThermistorCurrentLimiter`,
que reduz a corrente progressivamente entre dois limites — exatamente o controle de torque por
temperatura. Não é escrever, é **ligar**, como foi feito com o dos FETs.

Falta: instalar um **NTC 10k B=3950** colado **no enrolamento** (não na carcaça, que é o lado frio),
apontar o canal de ADC, dar os coeficientes e ligar os nossos settings nos limites.

**Vantagem da nossa montagem:** o estator é a parte parada, então o fio do NTC **não gira** — sai
pelo mesmo furo do eixo por onde passa o cabo do encoder.

⚠️ Ler o NTC é **leitura de ADC**, e foi mexer em ADC que quebrou a FOC em 06/08. Usar o
encanamento que o ODrive já tem para termistor; não inventar leitura nova perto do que amostra
corrente.

## 🔴 A LIÇÃO DA SESSÃO — timing de periférico compartilhado

**O que quebrou:** o commit `a6f5e35` trocou o canal 7 da sequência regular do ADC1 pelo sensor de
temperatura interno do STM32, com `ADC_SAMPLETIME_480CYCLES` contra 15 dos demais canais. A
justificativa foi *"ninguém lê o índice 7 do buffer"* — verdade, e irrelevante. O que mudou foi o
**tempo de conversão**, que derruba a varredura do ADC1 de ~48 kHz para ~23 kHz. E é no mesmo ADC1
que o `vbus` é amostrado pelo canal **injetado**, dentro da ISR de controle de 8 kHz.

**Num periférico compartilhado, "o dado não é lido" não é o mesmo que "não tem efeito".**

**O que isso causou (medido por SWD):**

```
Iq = 18,05 A com o volante PARADO e torque comandado ZERO   (valor TRAVADO, sem oscilar)
  → ~98 W de puro calor no motor, zero torque  →  motor esquentando parado
  → mech_power −111 W (freando) COM elec_power +51 W (consumindo) ao mesmo tempo
  → controller_err 0x80 SPINOUT_DETECTED → CONTROLLER_FAILED → motor desarma → "perdeu o FFB"
```

**Critério de diagnóstico** — o valor não denuncia, o contexto sim. 18 A aparece nos dois casos:

| | patológico | normal |
|---|---|---|
| quando | volante **parado** | só **sob força** |
| comportamento | valor **travado** | **oscila** e volta a ~0 ao soltar |

**Teste de campo, sem instrumento nenhum:** encostar a mão no motor depois que ele armar. Motor
esquentando parado = corrente sem torque. Cinco segundos e você sabe se aquele boot está bom.

### ⚠️ Erro de método que custou a sessão (não repetir)

Bissetei `bd7fffe` contra `a6f5e35` e chamei o segundo de "baseline validado" — **mas a mudança do
ADC estava nele**. Comparei a mudança contra ela mesma e apresentei o resultado como conclusão.
Todo firmware testado no dia estava contaminado; o único limpo era o `e0dd3ec` do dia anterior.

**Ao bissetar, verificar que o baseline REALMENTE precede a suspeita:**

```bash
git show <baseline> --stat -- <arquivo-suspeito>
```

O usuário apontou três vezes que "era a alteração" e que "o firmware de ontem estava bom", com o
histórico de que **o motor nunca esquentara no projeto inteiro**. Histórico de comportamento é
evidência, não impressão a ser explicada. "O diff não explica" é um limite do entendimento, não uma
afirmação sobre a realidade.

## 🧰 Ferramentas de diagnóstico novas (usar ANTES de teorizar)

Foram elas que capturaram os 18 A e o `SPINOUT_DETECTED`. Sem elas, teria sido mais um dia de
adivinhação.

### Latch da primeira falha — `g_fail_dbg` (`src/ffb_task.cpp`)

Fotografa o estado **antes** do `clear_errors()` apagá-lo. Guarda só a primeira ocorrência (as
seguintes costumam ser eco). Sobrevive enquanto a placa estiver ligada, então **dá pra dirigir sem
ST-Link e plugar só depois**.

```
[0] ocorrências   [1] controller_err   [2] axis   [3] motor   [4] enc
[5] mech_power mW [6] elec_power mW    [7] vbus mV
```

`controller_err = 0x80` é `SPINOUT_DETECTED`. Os campos 5 e 6 confirmam: mecânica < −50 W **e**
elétrica > +50 W = spinout. **Nunca afrouxar o spinout sem antes checar `Iq` com o volante parado** —
ele é o alarme de incêndio, não o incêndio.

### Caixa-preta de reset — `blackbox.h/.cpp`

Lê `RCC->CSR` no boot e limpa as flags: diz se o último reset foi power-on, brown-out, pino,
software ou watchdog. Mais o último hard fault (PC/LR/CFSR) em `.noinit` (seção nova no linker,
fora de `_sbss.._ebss`).

⚠️ **Descoberta importante:** hard fault **NÃO reinicia** esta placa — o handler do ODrive trava num
`while(1)` e não há IWDG/WWDG armado. Fault = base **CONGELA**. Logo, se a placa **reiniciou**, a
causa está na alimentação.

🔧 **PENDENTE:** o `reset_causa` reporta `4` (software) mesmo após power-cycle real, quando deveria
reportar `1` (power-on). Acertou o reset por SWD da gravação, mas não foi provado num ciclo de
alimentação. **Não confiar nele para diagnosticar reinício até revisar a decodificação.**

## ⏭️ ONDE CONTINUAR

### 0. AGORA: validar `a39ddb6` e cortar a release (11/08)

Os dois primeiros blocos deste documento. Nesta ordem: os dois testes de bancada da trava do
encoder, depois `v0.2.2-alpha` a partir do que passar. **Nada mais deve entrar no firmware antes
disso** — a regra é uma mudança por vez, e já há uma na fila.

### 1. Corrigir a decodificação da caixa-preta (pequeno, sem bancada)

Ver acima. Provavelmente a ordem de prioridade das flags, ou o `SFTRSTF` sendo retido. Lembrar que
`BORRSTF` acende **também** num power-on normal — o que distingue brown-out real é `BOR` **sem**
`POR`.

### 1a. Atualizar pela USB — FIRMWARE PRONTO, falta o driver no PC (2026-08-10)

O `EnterDfu` funciona ponta a ponta. Medido por SWD com o app disparando o comando:

| etapa | resultado |
|---|---|
| app envia EnterDfu | ✅ chegou |
| firmware desarma e grava o cookie | ✅ `0xCAFEFEED` (cookie de trânsito do ODrive) |
| reset + salto para a ROM | ✅ `PC = 0x1FFF14A6`, `VTOR = 0x1FFF0000` |
| interrupções liberadas | ✅ `PRIMASK = 0` — **não** era o problema de 2026-08-01 |
| bootloader enumera | ✅ `STM32 BOOTLOADER`, `0483:DF11` |
| Windows carrega driver | ❌ **erro código 28** — nenhum driver |
| dfu-util grava | ❌ não existia na máquina |

**A mensagem do app engana**: ela diz "a placa não entrou em DFU sozinha" quando na verdade a placa
entrou e o app é que não conseguiu falar com ela. Vale melhorar o texto para mandar conferir o
Gerenciador de Dispositivos.

**Feito:** `docs/firmware-update-windows.md` (bilíngue, com o passo a passo do Zadig), o instalador
passa a empacotar o `dfu-util`, e o app passa a procurá-lo **ao lado do próprio executável** — sem
isso, empacotar não adiantaria.

**Falta na bancada:** instalar o driver WinUSB (Zadig já está em `~/Downloads/zadig-2.9.exe`) e
fazer uma atualização real pelo app, ponta a ponta.

### 1b. Desenho do volante ainda salta de vez em quando (2026-08-10, PARCIAL)

Melhorou muito, **não ficou 100%**. O que já foi feito e medido:

- interpolação do ângulo restaurada no app (tinha se perdido ao voltar para o commit de quarta)
- relógio de quadros passa a iniciar no construtor + rede que segue a base direto se ninguém animar
- telemetria não morre mais de fome com o app lendo settings (era 1,2 s sem sair)
- tolerância do anti-starvation de 100 → **40 ms**: com 100 ms, a 1000 °/s o ângulo pulava 100°, e o
  app assume direto acima de 90° — os dois números se atropelavam. Paradas: 8,7% → **1,7%**

**O que sobrou:** um evento isolado de **300 ms** sem telemetria. Não vem da prioridade — vem do
endpoint USB indisponível (`tud_hid_ready()` falso), quando não há janela para enviar. Investigar
por esse lado (é o mesmo mecanismo do watchdog de EP travado), não pela prioridade.

Ferramenta: capturar `s_last_state_ms` + `encoders+144` por SWD enquanto o usuário gira, e cruzar
saltos de posição com pausas do carimbo.

### 2. Monitor de corrente: pico em vez de média (só app)

O monitor mostra a **média** de 500 ms, o que estabilizou a tensão mas destruiu a leitura de
corrente: FFB é bidirecional e a média se cancela em ~zero. Corrente de FFB interessa pelo **pico**,
como já é feito com o clipping.

### 3. Medir o Kt de verdade (bancada, sem gravar firmware)

O `0,55 Nm/A` **nunca foi medido** — é catálogo genérico de hoverboard (`Kt = 8,27/KV` com KV≈15).
Outros ports usam o mesmo valor pela mesma razão, então ninguém mediu. Enquanto for digitado, o
torque estimado herda o chute.

**Método já preparado, sem risco e sem gravar firmware:** com o motor armado e torque zero, o
controlador aplica a tensão que cancela a back-EMF para manter `Iq = 0`. Basta **girar o volante à
mão** e ler:

```
λ = (Vq − R·Iq) / ω_elétrico        Kt = 1,5 · pole_pairs · λ
```

Endereços (reextrair do ELF a cada build — eles andam!): `motors+364` = `v_current_control_integral_q_`
(Vq), `motors+356` = `Iq`, `motors+48` = `R`, `motors+32` = `pole_pairs`, `encoders+184` = vel (counts/s),
`encoders+80` = cpr. Script de coleta em `scratchpad/kt.cfg`. Fazer **regressão** com velocidades
variadas, não ponto único.

### 4. `apply_hw_profile` desfaz o autoscale — BUG REAL de segurança

`odrive_bridge_apply_hw_profile()` **sobrescreve** `dc_bus_overvoltage_trip_level` para 55 V toda vez
que o app manda settings — inclusive depois do `autoscale_bus_limits()` ter dimensionado 33 V pela
fonte medida. Ou seja: **mexer em qualquer setting da aba Hardware com a base ligada desfaz a
proteção**. Explica por que aquela aba já derrubou a placa antes.

### 5. Calibração de offset: parar de recalibrar a cada boot

O `newboard_bringup()` refaz a calibração toda vez que a base liga → **todo boot é um sorteio**
(encoder incremental sem index Z não persiste offset, e o cogging do hoverboard trava o lock-in num
detente aleatório).

**A prática consolidada evita isso por construção:** calibrar **uma vez**, salvar com
`pre_calibrated=true` e desligar `startup_*_calibration`. Comparação:

| campo | prática consolidada | nosso |
|---|---|---|
| `startup_motor_calibration` | **false** | recalibra |
| `startup_encoder_offset_calibration` | **false** | **recalibra a cada boot** |
| `pre_calibrated` | **true** (após bring-up manual) | false |
| `use_index` | **true se o Z estiver fiado** | não usa |
| `calibration_current` | **5 A** | 30 A no app |
| anticogging | mapa de 3600 pontos | não temos |

⚠️ Sem index Z o ODrive **força** `pre_calibrated=false` (`encoder.cpp check_pre_calibrated`), então
persistir o offset exige o Z. A decisão de 2026-08-03 descartou o Z para manter drop-in FFBeast —
**vale reabrir**, agora que sabemos o custo real da loteria por boot.

### 6. Outros

- **Perfil por jogo** — F1 2016 é leve em baixa velocidade por design (força vem do
  auto-alinhamento ∝ velocidade); paliativo é static damping / mola só nele
- **Roadmap de feel (P0)** — muita coisa já codada no `ffb_math.h`, só ligar
- **Serial USB fixo** — identidade DirectInput estável
- **Sobretemp do motor** — infra 90% pronta, falta o NTC no `AUX_TEMP` (PA5) e a lógica de desarme
- **App**: mostrar "não suportado nesta versão" em vez de `0 °C` quando o firmware não preenche o
  campo (o `BaseState` já traz a versão do firmware)

## 🔧 Protocolo de bancada (aprendido na dor, 2026-08-06)

1. **UMA gravação por vez, com power-cycle entre elas.** Encadear reflashes por SWD trava o
   DRV8301 e custa muito mais tempo do que economiza — vale mesmo quando a mudança é inofensiva
2. **Power-cycle completo = fonte off + ST-Link FORA DA USB**, ~10 s. Só desligar a fonte pode não
   bastar
3. **Depois de gravar, medir `Iq` com o volante parado ANTES de dirigir.** ~0,2 A oscilando = bom;
   valor alto travado = desligar
4. **Ler o latch ANTES de teorizar** — `controller_err` + as duas potências fecham o diagnóstico em
   segundos
5. **`mrw` (sem halt) para ler com o motor armado.** Halt derruba motor armado
6. **Reextrair endereços do ELF a cada build** — eles andam (`g_brake_meter` andou 4 bytes e me
   fez ler tudo deslocado)

---

# 📚 Histórico anterior (sessões do Mac, ainda válido)

## Estado atual (2026-08-05) — GRANDE VITÓRIA
- ✅ **O volante DD roda no ACC com FFB** — validado: **2 voltas completas, 100% de força, sem travar.**
- **Hardware:** MKS **XDrive-S** (ODRIVE-S v3.6-56V, STM32F405 + DRV8301) + motor **hoverboard** (15 pole
  pairs, R≈0,20 Ω / L≈0,35 mH) + encoder **E6B2 externo** incremental (4000 CPR).
- **Fonte:** bus ~19,6 V.

## A receita que funcionou (em `firmware-base/src/odrive_bridge.cpp`)
- Motor **`pre_calibrated`** + R/L fixos (**pula a medição de R/L** — é a medição de indutância que dava
  DRV_FAULT em L baixa) + `startup_motor_calibration=false`.
- `pole_pairs=15`, `cpr=4000`, `current_control_bandwidth=200`, `current_lim=25`, **`calibration_current=3.0`**
  (validado; 8A tripava o DRV).
- Encoder incremental **sem Z** → offset cal a cada boot (`startup_encoder_offset_calibration=true`) + auto-arma.
- `get_report` **nunca retorna 0** (senão STALL no EP0 → ACC trava) + **brake off** no boot.
- **LIÇÃO:** **SWD-halt DERRUBA o motor armado** (era o "churn"). Monitorar por **CDC serial** (ASCII do
  ODrive: `r axis0.current_state`, `r axis0.error`, `r axis0.motor.error`, `r vbus_voltage`), **nunca** SWD
  com o motor armado.

## Onde está cada coisa (git)
- **Tudo relevante está no `main`** — firmware ativo, app e docs. É só `git pull origin main`.
- **Firmware ATIVO = `firmware-base/`** (ODrive-base, MIT; 522 arquivos rastreados, incl. `vendor/odrive-fw`;
  só `autogen/` e `build/` são gitignored — regenerar autogen no clone, ver seção Windows). **É AQUI que
  trabalhamos daqui pra frente.**
- **`firmware-old/`** (SimpleFOC antigo, ODESC/STM32duino) foi **REMOVIDO** (2026-08-06) — o `firmware-base`
  novo já está validado na bancada. Recuperável do histórico git (existe em qualquer commit até `d38f210`).
- **RENAME concluído no main (2026-08-05):** `firmware-wheel-dd`→`firmware-base` (ODrive vira o ativo) e o
  SimpleFOC antigo `firmware-base`→`firmware-old`. Antes o rename só existia nas branches de feature; agora o
  `main` bate com elas. Memória: `drivelab-firmware-rename`.
- **Untracked que NÃO vão pro git** (podem apagar): `app.zip` (~370 MB), `hardware-profile.json.bak` (sobra do
  JSON removido), `firmware-*/build/`, `firmware-*/vendor/build` (regeneráveis por `make`).

### ⚙️ EQUALIZAR após sincronizar (Windows) — pastas-fantasma
> Vale igual depois do `git reset --hard origin/main` do topo deste arquivo.

O sincronismo move os arquivos **rastreados** de `firmware-wheel-dd`→`firmware-base`, mas **não apaga**
os **untracked** que sobram (build/, `autogen/` que é gitignored, vendor build) → ficam **pastas-fantasma**
`firmware-wheel-dd/` e `firmware-old/` com lixo. Elas têm **zero arquivo rastreado** — deletar é seguro.
No shell (MSYS2/bash):
```
git ls-files firmware-wheel-dd firmware-old   # deve imprimir NADA (confirma que é só casca)
rm -rf firmware-wheel-dd firmware-old         # remove as pastas-fantasma
```
(PowerShell: `Remove-Item -Recurse -Force firmware-wheel-dd, firmware-old`.) O firmware ATIVO é `firmware-base/`.

## Diagnóstico da força — RESPONDIDO (2026-08-04) e RESOLVIDO (2026-08-05)
A pergunta era: perder força numa curva é **teto de 5 Nm** ou **bug**? **Nenhum dos dois.**
**Não era o teto:** no desarme o `Iq` era ~5–7 A de 25 (≈2,7–4 Nm de 13,75). Cap
`kFullScaleTorqueNm=5.0` segue intocado. Ver a causa raiz na seção do "tec", abaixo.

> ⚠️ **Hipótese SUPERADA (mantida só como histórico):** por horas trabalhamos com "é **sobretensão** de
> regeneração". Estava errado. Ela nasceu de duas medições ruins: (a) polling por CDC a **5 Hz** é cego a
> transientes — mostrou pico de 22,4 V numa volta em que o trip disparou; (b) um **mapeamento de bits de
> `ODrive::Error` escrito de memória**, que trocava UNDER por OVER. Instrumentando o trip no firmware,
> provou-se que a sobretensão **nunca disparou** (pico real 23,9 V < trip 24,79 V).
> **Lições:** medir na escala do fenômeno, e ler o enum em `autogen/interfaces.hpp`, nunca de memória.
> Subir o trip de sobretensão (24,79 → 30 V) foi testado e **não resolveu** — era o limite errado.
### 🏁 O "tec" — CAUSA RAIZ ACHADA E CORRIGIDA (2026-08-04, noite)
O que o usuário sente como **"tec"** (solavanco, "parece que pulou um ímã" — **não** é perda de FFB) era
o motor em **churn arma/desarma a cada ~7 ms (~140 Hz)**, medido por **SWD a 200 Hz**.
**Raiz: `config.dc_bus_undervoltage_trip_level` estava em 14,79 V.** Sob corrente alta (curva lenta de
1ª/2ª = torque alto) o bus **afunda** até lá → `disarm_with_error(DC_BUS_UNDER_VOLTAGE)` → sem torque a
corrente cai → o bus volta → re-arma → afunda de novo. **FIX: 8,0 V** (valor de referência,
"prevents brown-outs") — **já SALVO na NVM**. Resultado: 110 s de pista com **ZERO desarmes**
(antes: dezenas por segundo). O `vbus` mínimo de 14,79 V no log era a pista, e estava lá o dia todo.
- ⚠️ **`save_configuration` falha em silêncio** se o auto-arme re-armar antes: bloquear com
  `mww <&g_arm_gate> 0` por SWD, mandar `w axis0.requested_state 1`, então `ss`. Sem isso o valor fica
  só em RAM e volta no próximo boot (aconteceu, e só apareceu na gravação seguinte).
- **Chopper (atualizado 2026-08-05):** está **LIGADO** no `firmware-base` (`f104503`) e **estável** —
  arma no boot (`brake_resistor_armed=1`) e **não trava mais** o motor. Derruba o pico de `vbus` de
  23,9 → **20,9 V**. Na noite de 04/08 ele ainda deixava desarmes residuais; o que faltava eram as duas
  peças do commit `046c421` (`clear_errors` re-armando o brake + auto-arme com retry espaçado).
  *(A frase anterior desta linha — "segue desligado por padrão" — ficou obsoleta.)*

### ✅ FECHAMENTO (2026-08-05): zig-zag limpo
Com o `main` atual gravado: **zig-zag = ZERO desarmes** (em 04/08 dava 31 em segundos), voltas normais
idem, `error=0`, motor armado o tempo todo. **Nenhuma peça sozinha resolvia** — foi a combinação de
(1) trip de subtensão **8 V**, (2) **chopper ligado** + trip de sobretensão 55 V, (3) `clear_errors`
delegando a `odrv.clear_errors()`, (4) auto-arme com **backoff 50→250 ms** sem desistir em 15 tentativas.
Sem (3)+(4) o chopper travava o motor; sem (1) sobrava o churn.
⚠️ Ao gravar firmware novo, **conferir a config**: o `firmware-base` escreve `enable_brake_resistor=1` e
`dc_bus_overvoltage_trip_level=55 V` no boot, o que **difere** do que estava na NVM antes.

### Ferramenta nova: SWD SEM HALT (ideia do usuário) — 200 Hz
A regra "não usar SWD com o motor armado" valia só para **halt**. Ler RAM pelo DAP com o core rodando
**não derruba o motor** e é 40× o polling da CDC. `mrw <addr>` (o `mdw` não imprime com o alvo
rodando). Endereços **mudam a cada build** — extrair sempre do ELF (`arm-none-eabi-nm` /
`gdb -ex "p &odrv.error_"`), nunca fixar. Úteis: `g_axis_dbg` (armed/state/axis_err/motor_err),
`odrv.error_` (a causa real; `motor_err=0x1000000 SYSTEM_LEVEL` só diz "foi uma proteção").
**Bits de `ODrive::Error` — conferir em `autogen/interfaces.hpp`, NÃO de memória:**
`0x01 CONTROL_ITERATION_MISSED · 0x02 DC_BUS_UNDER_VOLTAGE · 0x04 DC_BUS_OVER_VOLTAGE ·
0x08 OVER_REGEN_CURRENT · 0x10 OVER_CURRENT · 0x20 BRAKE_DEADTIME · 0x40 BRAKE_DUTY_NAN ·
0x80 INVALID_BRAKE_RESISTANCE`.

- **PENDENTE: validar o brake resistor (chopper) de 2 Ω** — instalado, com suporte no firmware,
  mas **nunca visto conduzindo** (`brake_resistor_armed=0`, `brake_resistor_current=0`). Hoje o firmware o
  desliga de propósito todo boot (`odrive_bridge_disable_brake_resistor`, src/odrive_bridge.cpp:80) porque
  `enable&&!armed` impedia o motor de armar. O comentário lá já dizia `⚠️ Reabilitar quando validado` — e
  a premissa escrita nele ("a ~19,6 V a regen é pequena") foi **refutada pelos dados**.
  Ordem sugerida: (1) multímetro no resistor com a placa DESLIGADA (confirmar ~2 Ω e a fiação);
  (2) só então recompilar tornando o disable condicional; (3) testar na bancada.

## App — device = fonte de verdade (CONCLUÍDO 2026-08-05, commits `c9ee58a..be6ce38`)
Refactor do DriveLab Studio: **a BASE (firmware) é a fonte de verdade**. **Sem JSON de config, sem salvamento
só-no-app.** O app **lê da base** ao conectar e **salva na base** (`CMD_SAVE`). Detalhes na memória
`drivelab-app-device-source-of-truth`. Resumo: (1-2) removido load+export do `hardware-profile.json`;
(3) removida a biblioteca de perfis da base (base saiu do auto-perfil-por-jogo; aro/pedais/freio seguem);
(4) **modo criador/cliente** — `--advanced` (ou arquivo `advanced.flag` na pasta do exe) mostra a aba
Hardware; cliente sem a flag **não vê Hardware**; (5) **campo vazio (`"—"`) sem conexão** (não inventa o
default do schema); (6) classes mortas deletadas. **App build 0 erros; suite 464/464.**
- ⚠️ **GOTCHA de teste:** `dotnet test app/DriveLab.sln` (solução inteira) dá **falsas falhas** por contenção
  no `LocalizationManager` estático entre assemblies em paralelo. Veredito real: rodar o **projeto isolado**
  ou `dotnet test app/DriveLab.sln -- -parallel none`.

## Roadmap (ver `docs/ROADMAP.md` + `docs/ROADMAP-features.md`)
- **Fase 1 (core estável):** P0 (linearity/expo, slew, friction) **✅ ligado** (`e8757d5..10fe395`, validar feel);
  P1 **SAVE ✅ implementado** (`3ad7599`, **validar persistência na bancada**), falta DOR real + force-disable
  real; P2 (sobretemp do motor + failsafe USB).
- **Fase 2 (modelo FFBeast):** 1 binário pra família ODrive (XDrive-S/MINI) lendo motor+encoder da flash.
- **Fase 3 (exploratória):** multi-arquitetura (só ESP32-**S3**; clássico não tem USB).

## Regras permanentes (NÃO esquecer)

- **NUNCA citar o nome de outros ports/projetos de terceiros** (o do `eagabriel`, o do FFBeast) em
  documento, comentário de código, mensagem de commit ou nome de build. Pedido do autor, já a
  terceira vez que reaparece. Quando precisar registrar de onde veio um valor ou uma decisão,
  escrever o **motivo técnico** sem nomear: "prática consolidada", "config funcional para placas
  classe ODESC", "valor de referência". O que importa é *qual* valor e *por quê*, não de quem veio.
  Citar o **ODrive** (o projeto de origem, MIT, vendorizado) continua correto e necessário.

- **Firmware SÓ na bancada**, uma mudança por vez, validando que não regrediu. App/docs off-bench.
- **Sem `Co-Authored-By: Claude`** nos commits (comunidade DIY).
- Todo arquivo de código novo começa com cabeçalho (descrição + `Autor: Luciano Tomé
  <lucianotome1970@gmail.com>` + `Licença: MIT`).
- Docs bilíngues EN+PT. **Não lançar o app** (o usuário testa). **Visibilidade do repo: não mexer**
  — ver a seção no topo.
- Eu (Claude) gravo/valido o firmware (make/PlatformIO → openocd/ST-Link → ler por SWD/CDC).

## Migração pro Windows — por quê e setup
**Por quê:** acabar com o revezamento Mac↔Windows. No Windows, com a base plugada ali, eu **gravo (ST-Link)**,
**leio telemetria (porta COM/CDC) AO VIVO** e **correlaciono com o ACC** — tudo local, sem halt de SWD.

**Setup no Windows — FEITO em 2026-08-04 (build validado: `.elf/.hex/.bin`, text=327176):**
- **Toolchain = O MESMO DO MAC.** Instalar **PlatformIO** → ele traz o pacote **`toolchain-gccarmnoneeabi`**
  + **`tool-openocd`**. ⚠️ **Atenção:** a platform `ststm32` instala por padrão o **GCC 7.2.1**, que
  **NÃO compila** o ODrive 0.5.6 (`can_helpers.hpp: uninitialized variable in constexpr function`).
  Force o **GCC 10.3.1** (o do ODrive 0.5.6):
  `pio pkg install -g -t "platformio/toolchain-gccarmnoneeabi@~1.100301.0"` — ele vira o
  `%USERPROFILE%\.platformio\packages\toolchain-gccarmnoneeabi\bin` (mesmo caminho do Mac).
  Build: `make` via **MSYS2** (`C:\msys64`, `pacman -S make`) com esse `bin` no PATH.
- ⚠️ **`vendor/odrive-fw/autogen/` é gitignored** → gerar após todo clone (python + pyyaml/jinja2/jsonschema),
  chamando `vendor/odrive-tools/fibre-tools/interface_generator.py` direto (o `interface_generator_stub.py`
  aponta pra `vendor/tools/`, que aqui não existe): 4 headers (`--generate-endpoints ODrive3` no
  `endpoints`) + `odrive/version.py --output autogen/version.c`.
- Ainda: **driver do ST-Link** (WinUSB via Zadig p/ o openocd), `dotnet` (app).
- Repo: `git clone` + `git checkout` da branch conforme o trabalho.
- Memória: copiar `~/.claude/.../memory/` pra pasta que o Claude Code criar no Windows (nome do caminho muda).
