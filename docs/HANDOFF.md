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

## Próximo passo imediato (diagnóstico da força)
Pergunta em aberto: quando o usuário **vence o motor** numa curva, é **teto normal (5 Nm é vencível)** ou
**bug (desarme/spinout)**? Cap de FFB = **NOSSO** (`kFullScaleTorqueNm=5.0` em `ffb_model.cpp`), motor
aguenta ~13,7 Nm pico. **No Windows** isto vira trivial: ler a **CDC serial ao vivo** enquanto dirige no ACC —
`armado` pisca = desarme (bug); corrente bate no teto + armado aceso = só o teto (não é bug).

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

**Setup no Windows (as ferramentas o Claude instala por comando quando estivermos lá):**
- **Toolchain = O MESMO DO MAC.** Instalar **PlatformIO** → ele traz o pacote **`toolchain-gccarmnoneeabi`**
  (mesmo `arm-none-eabi-gcc`, versão pinada → builds **idênticos** aos do Mac) + **`tool-openocd`**.
  Build igual ao Mac: `make` (via MSYS2) com o toolchain do PlatformIO no PATH →
  `%USERPROFILE%\.platformio\packages\toolchain-gccarmnoneeabi\bin`.
  (No Mac era `~/.platformio/packages/toolchain-gccarmnoneeabi/bin`.)
- Ainda: **driver do ST-Link** (WinUSB via Zadig p/ o openocd), `python` (autogen; o PlatformIO já traz um),
  `dotnet` (app).
- Repo: `git clone` + `git checkout` da branch conforme o trabalho.
- Memória: copiar `~/.claude/.../memory/` pra pasta que o Claude Code criar no Windows (nome do caminho muda).
