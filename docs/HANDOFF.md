# DriveLab — Handoff (Mac → Windows)

> Nota pra retomar o trabalho em **qualquer máquina** (esp. Windows). O contexto detalhado está na
> memória do Claude Code (`~/.claude/projects/<projeto>/memory/`) — mas o essencial está aqui, no git.

## Estado atual (2026-08-04) — GRANDE VITÓRIA
- ✅ **O volante DD roda no ACC com FFB** — validado: **2 voltas completas, 100% de força, sem travar.**
- **Hardware:** MKS **XDrive-S** (ODRIVE-S v3.6-56V, STM32F405 + DRV8301) + motor **hoverboard** (15 pole
  pairs, R≈0,20 Ω / L≈0,35 mH) + encoder **E6B2 externo** incremental (4000 CPR).
- **Fonte:** bus ~19,6 V.

## A receita que funcionou (em `firmware-wheel-dd/src/odrive_bridge.cpp`)
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
| Branch | Conteúdo |
|---|---|
| **`main`** | Firmware **funcional** + `docs/ROADMAP.md` + `docs/ROADMAP-features.md` + este handoff |
| **`feat/incorporar-app-2026-08-04`** | App de ontem reincorporado (compila 0 erros); firmware = o funcional |
| **`trabalho-2026-08-03`** | Trabalho COMPLETO de ontem (app + experimentos de firmware). Nada foi perdido |

## Diagnóstico da força — RESPONDIDO em 2026-08-04 (era outra coisa)
A pergunta era: perder força numa curva é **teto de 5 Nm** ou **bug**? Resposta pela CDC ao vivo no ACC:
**nenhum dos dois** — é **sobretensão de REGENERAÇÃO**. Na reversão rápida (chicane T1 de Monza, ponto
único, repetível) o motor freia o volante e regenera; sem destino pra essa energia
(`enable_brake_resistor=0`) o bus sobe e estoura `dc_bus_overvoltage_trip_level` → `do_fast_checks()`
(main.cpp:311, ~8 kHz) desarma tudo (`motor.error=0x1000000 SYSTEM_LEVEL`) → o auto-arme reergue em
~200 ms → o usuário sente o FFB "sumir". **Não era o teto:** no desarme o `Iq` era ~5 A de 25 (≈2,7 Nm
de 13,75). Cap `kFullScaleTorqueNm=5.0` segue intocado.
- **Subir o trip NÃO resolve** (testado): 24,79 → **30 V** e o bus estourou os 30 assim mesmo. Revertido.
  Perseguir com trip maior só castiga a fonte de 19,5 V, que passa a ver >30 V.
- **Medir com log de 5 Hz ENGANA** (o transiente é rápido demais: CSV mostrou pico de 22,4 V numa volta
  em que o bus passou de 30 V). A testemunha confiável é o **`error` global** (`r error`) depois da volta.
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
- **Chopper:** ligá-lo derruba o pico de `vbus` de 23,9 → **20,6 V** (dissipa mesmo), mas ainda deixa
  desarmes residuais e **não persistiu na NVM** — segue **desligado** por padrão, como validado.

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

## Roadmap (ver `docs/ROADMAP.md` + `docs/ROADMAP-features.md`)
- **Fase 1 (core estável):** ligar P0 (linearity/expo, slew, friction — já codados, só conectar), P1 (SAVE
  persistência — hoje no-op!, DOR real, force-disable real), P2 (sobretemp do motor + failsafe USB).
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
