# DriveLab — Roadmap

> Documento de planejamento. Registra a visão de longo prazo **sem** desviar do foco atual
> (estabilizar o core primeiro). Nada aqui é para desenvolver agora, salvo a Fase 1.

## Status atual (2026-08-04)

- ✅ Firmware **ODrive-base roda no ACC com FFB** — validado: 2 voltas completas, 100% de força, sem travar.
- **Hardware validado:** MKS **XDrive-S** (ODRIVE-S v3.6-56V, STM32F405 + DRV8301) + motor hoverboard
  (15 pole pairs, 0,20 Ω / 0,35 mH) + encoder **E6B2 externo** incremental (4000 CPR).
- Receita que funcionou: motor `pre_calibrated` + R/L fixos (pula a medição que dá DRV_FAULT) + cal 3A +
  `get_report` nunca-zero (ACC não trava) + brake off + offset cal por boot.
- **Repo privado** até uma release estável (segurança: DD tem torque perigoso).

---

## Fase 1 — Estabilizar o core (foco ATUAL; só na bancada, 1 mudança por vez)

- [ ] **Portar os settings de firmware do contrato A0** para casar com o app reincorporado
      (branch `feat/incorporar-app-2026-08-04`): inverter direção, encoder_type, corrente de cal, etc.
      Guiado pelo `FirmwareA0ContractTests`. Testar arme + FFB após cada setting.
- [ ] **Liberar força com segurança:** subir o cap de torque do FFB (hoje 5 Nm em `ffb_model.cpp`,
      `kFullScaleTorqueNm`) **junto com** o **corte por sobretemperatura do motor** (infra existe, falta a
      lógica de desarme).
- [ ] **Ligar o fio Z do encoder** (index) → offset elétrico consistente por boot → arme confiável sempre
      (hoje sem Z é um "sorteio" por boot).
- [ ] **Merge** da branch `feat/incorporar-app-2026-08-04` no `main` após validar firmware+app juntos.
- [ ] **Volante (aro) físico** como alavanca para sentir/tunar a força de verdade.
- [ ] **Release estável + reabrir o repo** com `SAFETY.md` + disclaimer de segurança (EN+PT) no README.

## Fase 2 — Um binário para a família ODrive (modelo FFBeast)

Objetivo: o mesmo firmware rodar em **XDrive-S** e **XDrive MINI** (e clones ODrive v3.6) **sem recompilar** —
só configurando motor+encoder, como o FFBeast faz.

- [ ] Refatorar: **ler motor + encoder das settings da flash**, em vez de cravar em `odrive_bridge_newboard_bringup`.
- [ ] Ligar `encoder_type`: incremental (E6B2) **+** magnético externo por SPI (MT6701 / AS5047P **no motor**) —
      groundwork já existe na branch `trabalho-2026-08-03` (commit 9751707).
- [ ] Confirmar na bancada que o **shunt da MINI == da S** (o FFBeast não expõe shunt → forte indício de que
      é o padrão ODrive v3.6). Se bater, o binário roda nas duas.
- **Nota:** encoder é **sempre EXTERNO** em DD (o motor fica longe da placa) → o encoder onboard da MINI é
  irrelevante. Core idêntico (F405 + DRV8301 + sense v3.6) → S e MINI são praticamente a mesma placa pra nós.

## Fase 3 — Multi-arquitetura (EXPLORATÓRIO, longe; só após o core estável)

Separar um **core portável** (modelo de efeitos FFB + protocolo HID PID + canal A0 — lógica pura) de uma
**camada de plataforma** (MCU / FOC / gate-driver / USB). Cada arquitetura vira um adaptador.

- **STM32 / ODrive** — já temos e funciona (base da Fase 1/2).
- **ESP32-S3 + SimpleFOC** — alvo *possível* no futuro:
  - ⚠️ **USB nativo só existe no ESP32-S3** (e -S2). O **ESP32 clássico (WROOM-32E) NÃO tem USB** →
    **não vira controle USB HID FFB** (Bluetooth HID não serve pra sim racing). Placas ESP32-clássico e
    de impressora 3D estão **descartadas** pra volante USB.
  - Seria **firmware separado** (SimpleFOC ≠ ODrive; nós já saímos do SimpleFOC por robustez — ver a
    memória do pivô arquitetural), força menor (~20A/35V), e USB só no chip -S3.
- Regra de ouro pra qualquer placa nova de volante USB: **precisa de MCU com USB nativo.**

---

## Backlog / ideias (sem fase definida)

- **Perfis de hardware** (`board_variant` já existe no A0) — vendor/modelo embutido no instalador.
- **hot-reconnect**: serial USB fixo p/ identidade DirectInput estável (ACC re-detectar sem sair/reentrar).
- **Compensação de cogging** (tabela na flash) — feel liso de DD caro.
- **Brake resistor / chopper** validado conduzindo (hoje desligado na bancada; reabilitar com re-arm após
  escrever, em bus alto).
- **Soft-power por botão** (groundwork pronto, falta fiação do bus DC).
- **Firmware-wheel (aro):** 10 botões RGB + rotaries + pás + D-pad + barra de LEDs.
- **Pedais / handbrake** (branches existentes: `feat/pedals-*`, firmware-pedal/handbrake).

> **Regra permanente:** mexer em **firmware só na bancada**, uma mudança por vez, validando que não regrediu.
> App / docs / testes host-only podem ser off-bench.
