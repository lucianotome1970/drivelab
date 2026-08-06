# DriveLab — Handoff (Mac → Windows)

> Nota pra retomar o trabalho em **qualquer máquina** (esp. Windows). O contexto detalhado está na
> memória do Claude Code (`~/.claude/projects/<projeto>/memory/`) — mas o essencial está aqui, no git.

## ⏭️ ONDE CONTINUAR (Windows, na bancada) — atualizado 2026-08-05
Tudo abaixo já está **commitado e no `main`** (é só `git pull`). O que falta é **validar na bancada** (Windows,
base plugada). Ordem sugerida — firmware é do Claude (grava por ST-Link, lê por CDC), uma mudança por vez.

> **Nota pra sessão do Claude no Windows:** esta sessão não tem a memória da máquina do dev (a memória é por
> caminho de projeto). Este arquivo (no git) é a fonte. Ao abrir, ler este bloco + a seção "App" abaixo.

### ✅ FECHADO na bancada em 2026-08-05 (dia longo — tudo commitado e no GitHub)
Hardware novo em uso: **fonte 27 V / 30 A** (a de 19,5 V afundava a 7,47 V sob torque = a raiz do "tec")
e **resistor de brake de 100 W** (o de 50 W torrou — ver abaixo).

1. ✅ **"Tec" RESOLVIDO** — era `dc_bus_undervoltage_trip_level` em 14,79 V. Fix: **8 V** (salvo na NVM).
2. ✅ **SAVE persistente** — settings sobrevivem ao power-cycle **e** a cal do motor fica intacta
   (FFB_NVM setor 1 × setores 10/11). Fecha o antigo item 1.
3. ✅ **Chopper funcionando** (`68f9ab3`) — com resistor o pico de `vbus` cai de **33,5 → 27,5 V**;
   média de só **3,3 W** no resistor. Fecha o antigo item 3.
4. ✅ **Escala de torque 5 → 10 Nm** — o sistema usava ~18% do que o conjunto entrega.
5. ✅ **4 SIMULADORES**: ACC, AC EVO, AMS2 e F1 2016 — **zero desarme**, `error=0` (cumulativo).
   *F1 2016 fica leve em baixa velocidade: é DESIGN do jogo (força ∝ auto-alinhamento), não defeito.*
6. ✅ **App** (`fd1a641`) — atalho de centralizar recuperado (tinha sumido numa regressão de git) +
   botão "Salvar no controlador" no **dashboard** (o das abas não alcança o DOR/centro).

> ⚠️ **Um resistor de 50 W QUEIMOU** ao trocar a fonte: a rampa do chopper vinha da NVM calibrada para
> 19,5 V e o vbus da fonte nova já nascia acima do `ramp_end` → duty 95% contínuo ≈ 273 W. Corrigido
> por três proteções que **nem o ODrive nem o Odrive-Wheel têm** (`68f9ab3`): limites derivados da
> **fonte medida**, chopper **mudo** enquanto não medir, e **watchdog térmico** (60 W médios).

### Pendente
1. **Feel das ligações P0** — validar que o FFB não regrediu. Para MAIS DETALHE (zebra/pista):
   linearidade **100→75-80%** (amplifica forças pequenas), damper e damping estático **para ~0**,
   e ligar **anti-oscilação** se o DD tremer. Os filtros que matam detalhe já estão em 0.
2. **Perfil por jogo** — F1 2016 quer o oposto (mola/damping estático altos p/ compensar a baixa
   velocidade). Candidato a feature: piso de força ("minimum force").
3. **Serial USB único** — hoje é a string fixa `"0001"`; em produção duas placas colidem no mesmo PC.
   Tentamos derivar do UID do STM32 (`0x1FFF7A10`, como a Moza) e **quebrou a enumeração**
   (`VID_0000&PID_0002`, falha de descritor) → revertido. Refazer com calma e testar isolado.
4. **Sobretemp do motor (P2)** — sem sensor não há corte térmico, e o limite prático dos 10 Nm é calor.
5. **Cap de torque derivado do motor** (`torque_constant × current_lim`) em vez de constante compilada —
   pré-requisito pra distribuir o firmware. Conversa com a Fase 2 (1 binário pra família ODrive).

> **Hot-reconnect: ARQUIVADO.** A premissa era falsa — o usuário ligou a **Moza** no mesmo PC e o ACC
> **também não a re-detecta** depois de reiniciada. É limitação do jogo, não defeito nosso.

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
corrente cai → o bus volta → re-arma → afunda de novo. **FIX: 8,0 V** (valor do Odrive-Wheel,
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
