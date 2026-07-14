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

## M0.5 — USB/FFB (o de-risco principal) ⚠️ — RASCUNHO pronto p/ iterar

Provar a decisão **B2** no F405: **enumerar como dispositivo Force Feedback** (volante DirectInput) no Windows, reusando a pilha FFB pronta. **Sem motor** ainda.

> **É um primeiro rascunho, escrito sem placa** (`src/m05/main.cpp`). A combinação shim + lib de FFB no F405 nunca foi publicada — **espere iterar**. Não é garantido compilar/rodar de primeira.

### Como gravar o M0.5
```bash
pio run -e m05 -t upload      # ou selecione o env "m05" na barra do PlatformIO
```
As libs (`USBLibrarySTM32` + `ArduinoJoystickWithFFBLibrary`) são baixadas via `lib_deps` (git) na primeira build. O `env:m05` **não** usa `USBD_USE_CDC` (o USB passa a ser o do shim).

### Verificação (M0.5 ✅)
Windows → *Painel de Controle → Dispositivos e Impressoras → (o dispositivo) → Configurações do controle de jogo → Propriedades*:
- Aparece um **eixo de direção se movendo sozinho** (o sketch varre o steering) → **enumerou + eixo ok**.
- Aba/botão de **Force Feedback / Testar** presente → o descriptor PID subiu.

### Pontos de incerteza a resolver na bancada (por ordem de risco)
1. **Compilação das libs juntas** — o shim e a lib de FFB são AVR-origin; pode faltar/conflitar algum símbolo. Se não compilar, mande o erro — ajustamos (talvez copiar arquivos, não só `lib_deps`).
2. **Nome do header do shim** (`#include <USBLibrarySTM32.h>`) — verificar o header real do repo.
3. **`getUSBPID()` numa ISR** — na versão AVR roda dentro da ISR de USB; no rascunho chamamos em `loop()`. Se os efeitos não registrarem no teste de FFB, precisa hookar no callback/ISR de USB do shim.
4. **Clock/USB** — 48 MHz (PLL48CLK); **desabilitar VBUS sensing** se bus-powered; o gotcha do cristal HSE (ver M0).
5. **F405 não é oficialmente testado** pelo shim (só F401/F411, mesma família OTG_FS).

Se travar de vez, os fallbacks já estão mapeados (**B1**: TinyUSB + PID próprio MIT; ou **2-MCU**: AVR 32u4 + STM32). Ver design §2.

*(A prioridade da IRQ USB abaixo do timer do FOC só importa a partir do M1, quando o SimpleFOC entrar.)*

---

## Marcos seguintes (resumo)
M1 (motor malha aberta) → M2 (encoder + malha fechada + brake resistor) → M3 (canal A0, **DriveLab Studio conecta via HidTransport**) → M4 (settings) → M5 (força FFB → SimpleFOC) → M6 (efeitos de jogo) → M7 (validação num sim). Detalhes no design.
