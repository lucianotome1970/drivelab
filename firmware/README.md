# DriveLab Firmware (Trilho B)

Firmware para a **ODESC v4.2 (STM32F405)** — o lado do dispositivo do volante DriveLab.
Design/decisões: [`../docs/superpowers/specs/2026-07-13-drivelab-firmware-trilho-b-design.md`](../docs/superpowers/specs/2026-07-13-drivelab-firmware-trilho-b-design.md).

> **Licença:** este firmware será **LGPL** a partir do M0.5 (quando adicionarmos as libs de FFB LGPL — `USBLibrarySTM32` + `ArduinoJoystickWithFFBLibrary`). O app DriveLab Studio e as libs .NET seguem MIT. O código atual (M0) é nosso; a mudança de licença acontece ao integrar as libs.

---

## Marco atual: M0 — bring-up (só serial, SEM motor)

**Objetivo:** provar toolchain + gravação + USB serial na sua ODESC. Inofensivo (não mexe no motor/potência).

### Pré-requisitos
- **VS Code + extensão PlatformIO** (instala o toolchain STM32duino sozinho na primeira build).
- **ODESC v4.2** + cabo **USB** (micro-USB).
- Para gravar, **um** dos dois:
  - **ST-Link V2** (recomendado) ligado ao header **SWD** da ODESC: `SWDIO`, `SWCLK`, `GND`, `3V3`.
  - **DFU** (sem ST-Link): colocar a placa em modo DFU (BOOT0 em alto no reset — pad/botão de BOOT) e usar `upload_protocol = dfu` no `platformio.ini`.
- Alimentação: **não precisa de motor**. O USB/ST-Link já alimenta a lógica. (Não conecte o motor no M0.)

### Passos
1. Abra a pasta `firmware/` no VS Code (PlatformIO detecta o `platformio.ini`).
2. Conecte o ST-Link (ou ponha em DFU) e o cabo USB da ODESC.
3. **Build**: ícone ✓ do PlatformIO (ou `pio run`). A primeira vez baixa o framework — leva alguns minutos.
4. **Upload**: ícone → do PlatformIO (ou `pio run -t upload`).
5. **Serial Monitor**: ícone 🔌 (ou `pio device monitor`), 115200 baud.

### Resultado esperado (M0 ✅)
No monitor serial aparece:
```
=== DriveLab Firmware — M0 bring-up ===
...
DriveLab M0 vivo, tick = 0
DriveLab M0 vivo, tick = 1
...
```

### Se der problema
- **Build falha** → mande o erro; ajustamos.
- **Upload falha (ST-Link não acha a placa)** → confira os 4 fios SWD (SWDIO/SWCLK/GND/3V3) e a alimentação. Alternativa: DFU.
- **Gravou mas o USB serial não aparece** → o suspeito nº1 é o **cristal HSE**. O `genericSTM32F405RG` assume 8 MHz; se a ODESC usar outro, o clock de 48 MHz da USB fica errado e a porta não enumera. Solução: descobrir o cristal da ODESC e definir `-D HSE_VALUE=<hz>` (e ajustar o SystemClock se preciso). Me chame que resolvemos. *(O LED/serial via ST-Link ainda funciona mesmo se a USB não subir — dá pra separar os problemas.)*

---

## Próximo: M0.5 — USB/FFB (o de-risco principal) ⚠️

Provar a decisão **B2** no F405: fazer o volante **enumerar como dispositivo Force Feedback** no Windows, reusando a pilha FFB pronta.

**Ainda não scaffoldado** — a integração é manual/pioneira (melhor fazer juntos, ao vivo). O que envolve:
1. Adicionar as libs (LGPL): **`Levi--G/USBLibrarySTM32`** (shim USB) + **`YukMingLaw/ArduinoJoystickWithFFBLibrary`** (cérebro FFB PID). Provavelmente via `lib_deps` (git) + possíveis ajustes manuais (o shim/lib são AVR-origin; pode exigir cópia de arquivos).
2. **Desligar** o USB CDC do core (tirar `USBD_USE_CDC`) — o USB passa a ser o da lib de FFB.
3. Configurar: clock 48 MHz (PLL48CLK), **desabilitar VBUS sensing**, e **prioridade da IRQ USB abaixo** do timer (importa quando o SimpleFOC entrar).
4. Gravar e verificar no Windows: *Painel de Controle → Dispositivos de Jogo → Propriedades → aba Force Feedback / Testar*.

Se o shim não rodar no F405, os fallbacks já estão mapeados (B1: TinyUSB próprio; ou 2-MCU). Ver o design §2.

---

## Marcos seguintes (resumo)
M1 (motor malha aberta) → M2 (encoder + malha fechada + brake resistor) → M3 (canal A0, **DriveLab Studio conecta via HidTransport**) → M4 (settings) → M5 (força FFB → SimpleFOC) → M6 (efeitos de jogo) → M7 (validação num sim). Detalhes no design.
