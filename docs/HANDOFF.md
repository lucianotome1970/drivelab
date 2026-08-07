# DriveLab — Handoff

> Nota pra retomar o trabalho em **qualquer máquina**. O contexto detalhado está na memória do
> Claude Code (`~/.claude/projects/<projeto>/memory/`) — mas o essencial está aqui, no git.

## 📍 ESTADO ATUAL (2026-08-06, fim da sessão do Windows)

**A base está funcionando e validada na bancada.** Firmware `d36aee1` gravado, 4 voltas na pista
sem nada esquentar, zero desarmes, corrente de repouso em 0,25 A (ruído puro).

| | |
|---|---|
| Firmware na base | `d36aee1` (= o do `main`) |
| App | compila em Release, **213 testes passando** |
| Motor | frio após 4 voltas |
| `Iq` com volante parado | 0,25 A oscilando (0,018 W) |
| Falhas no latch | **nenhuma** |

O ambiente do Windows está pronto: PlatformIO + GCC ARM 10.3.1 + MSYS2/make + openocd + driver
ST-Link (WinUSB). `git clone` + `make` funciona — só lembrar que o `autogen/` do ODrive é
gitignored e precisa ser gerado.

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

### 1. Corrigir a decodificação da caixa-preta (pequeno, sem bancada)

Ver acima. Provavelmente a ordem de prioridade das flags, ou o `SFTRSTF` sendo retido. Lembrar que
`BORRSTF` acende **também** num power-on normal — o que distingue brown-out real é `BOR` **sem**
`POR`.

### 2. Monitor de corrente: pico em vez de média (só app)

O monitor mostra a **média** de 500 ms, o que estabilizou a tensão mas destruiu a leitura de
corrente: FFB é bidirecional e a média se cancela em ~zero. Corrente de FFB interessa pelo **pico**,
como já é feito com o clipping.

### 3. Medir o Kt de verdade (bancada, sem gravar firmware)

O `0,55 Nm/A` **nunca foi medido** — é catálogo genérico de hoverboard (`Kt = 8,27/KV` com KV≈15).
O Odrive-Wheel usa o mesmo valor pela mesma razão, então ninguém mediu. Enquanto for digitado, o
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

**O Odrive-Wheel evita isso por construção:** calibra **uma vez**, salva com `pre_calibrated=true` e
desliga `startup_*_calibration`. Da tabela deles (`docs/GETTING_STARTED.md`):

| campo | Odrive-Wheel | nosso |
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
- **Firmware SÓ na bancada**, uma mudança por vez, validando que não regrediu. App/docs off-bench.
- **Sem `Co-Authored-By: Claude`** nos commits (comunidade DIY).
- Todo arquivo de código novo começa com cabeçalho (descrição + `Autor: Luciano Tomé
  <lucianotome1970@gmail.com>` + `Licença: MIT`).
- Docs bilíngues EN+PT. **Não lançar o app** (o usuário testa). **Repo PRIVADO** até release estável.
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
