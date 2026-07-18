# DriveLab Firmware — Base / Wheelbase (Trilho B)

Firmware for the DriveLab **base** (wheelbase) — **ODESC v4.2 (STM32F405)** + direct-drive motor.
*(The firmware for the removable rim/wheel — buttons, LEDs, paddles — lives in `firmware-wheel/` (RP2040).)*
Design/decisions: kept in internal project notes (not versioned in the public repo).

> **License:** this firmware will be **LGPL** starting at M0.5 (when we add the LGPL FFB libs — `USBLibrarySTM32` + `ArduinoJoystickWithFFBLibrary`). The DriveLab Studio app and the .NET libs stay MIT. The current code (M0) is ours; the license change happens when we integrate the libs.

<p align="center"><a href="#-english">🇬🇧 English</a> &nbsp;·&nbsp; <a href="#-português">🇧🇷 Português</a></p>

---

## 🇬🇧 English

### Current milestone: M0 — bring-up (serial only, NO motor)

**Goal:** prove toolchain + flashing + USB serial on your ODESC. Harmless (does not touch the motor/power).

#### Prerequisites
- **VS Code + PlatformIO extension** (installs the STM32duino toolchain by itself on the first build).
- **ODESC v4.2** + **USB** cable (micro-USB).
- To flash, **one** of the two:
  - **ST-Link V2** (recommended) connected to the ODESC **SWD** header: `SWDIO`, `SWCLK`, `GND`, `3V3`.
  - **DFU** (no ST-Link): put the board in DFU mode (BOOT0 high at reset — BOOT pad/button) and use `upload_protocol = dfu` in `platformio.ini`.
- Power: **no motor needed**. USB/ST-Link already powers the logic. (Do not connect the motor on M0.)

#### Steps
1. Open the `firmware-base/` folder in VS Code (PlatformIO detects `platformio.ini`).
2. Connect the ST-Link (or put it in DFU) and the ODESC USB cable.
3. **Build**: the PlatformIO ✓ icon (or `pio run`). The first time it downloads the framework — takes a few minutes.
4. **Upload**: the PlatformIO → icon (or `pio run -t upload`).
5. **Serial Monitor**: the 🔌 icon (or `pio device monitor`), 115200 baud.

#### Expected result (M0 ✅)
The serial monitor shows:
```
=== DriveLab Firmware — M0 bring-up ===
...
DriveLab M0 vivo, tick = 0
DriveLab M0 vivo, tick = 1
...
```

#### If something goes wrong
- **Build fails** → send the error; we'll adjust.
- **Upload fails (ST-Link can't find the board)** → check the 4 SWD wires (SWDIO/SWCLK/GND/3V3) and the power. Alternative: DFU.
- **Flashed but the USB serial doesn't show up** → suspect #1 is the **HSE crystal**. `genericSTM32F405RG` assumes 8 MHz; if the ODESC uses another, the USB 48 MHz clock is wrong and the port won't enumerate. Fix: find out the ODESC crystal and set `-D HSE_VALUE=<hz>` (and adjust the SystemClock if needed). Call me and we'll solve it. *(The LED/serial via ST-Link still works even if USB doesn't come up — you can isolate the problems.)*

---

### M0.5 — USB/FFB (the main de-risk) ⚠️ — COMPILES for F405, awaiting bench

Prove decision **B2** on the F405: **enumerate as a Force Feedback device** (DirectInput wheel) on Windows, reusing the ready-made FFB stack. **No motor** yet.

> **Update (2026-07):** the draft (`src/m05/main.cpp`) now **compiles cleanly for the F405** (3.5% flash). Two fixes were needed vs. the first draft: the shim's real header is `USBAPI.h` (there is no `USBLibrarySTM32.h`), and the FFB lib uses the AVR-only macro `_delay_us()` — mapped to `delayMicroseconds()` via a force-included shim (`include/avr_compat.h`), so the re-fetched third-party lib isn't edited. Still **awaiting bench validation**: that it actually enumerates as an FFB wheel on real hardware (watch the HSE-crystal / VBUS-sensing gotchas below).

#### How to flash M0.5
```bash
pio run -e m05 -t upload      # or select the "m05" env in the PlatformIO bar
```
The libs (`USBLibrarySTM32` + `ArduinoJoystickWithFFBLibrary`) are downloaded via `lib_deps` (git) on the first build. The `env:m05` does **not** use `USBD_USE_CDC` (the USB becomes the shim's).

#### Verification (M0.5 ✅)
Windows → *Control Panel → Devices and Printers → (the device) → Game controller settings → Properties*:
- A **steering axis moving by itself** appears (the sketch sweeps the steering) → **enumerated + axis ok**.
- A **Force Feedback / Test** tab/button present → the PID descriptor came up.

#### Uncertainty points to resolve on the bench (by order of risk)
1. **Compiling the libs together** — the shim and the FFB lib are AVR-origin; some symbol may be missing/conflicting. If it doesn't compile, send the error — we'll adjust (maybe copy files, not just `lib_deps`).
2. **Shim header name** (`#include <USBLibrarySTM32.h>`) — check the actual header in the repo.
3. **`getUSBPID()` in an ISR** — in the AVR version it runs inside the USB ISR; in the draft we call it in `loop()`. If the effects don't register in the FFB test, it needs to be hooked into the shim's USB callback/ISR.
4. **Clock/USB** — 48 MHz (PLL48CLK); **disable VBUS sensing** if bus-powered; the HSE crystal gotcha (see M0).
5. **F405 is not officially tested** by the shim (only F401/F411, same OTG_FS family).

If it gets fully stuck, the fallbacks are already mapped (**B1**: TinyUSB + our own MIT PID; or **2-MCU**: AVR 32u4 + STM32). See design §2.

*(The USB IRQ priority below the FOC timer only matters starting at M1, when SimpleFOC comes in.)*

---

### Next milestones (summary)
M1 (open-loop motor) → M2 (encoder + closed loop + brake resistor) → M3 (A0 channel, **DriveLab Studio connects via HidTransport**) → M4 (settings) → M5 (FFB force → SimpleFOC) → M6 (game effects) → M7 (validation on a sim). Details in the design.

### Wheel connection (USB hub + 5 V rail)

For a quick-release rim, the base is meant to host a small **USB hub** (the ODESC and the rim share one cable to the PC) and a **5 V buck** off the main PSU to power the rim's RGB LEDs — so no extra USB cable dangles from the wheel. Full wiring, the signals that cross the slip ring, and the required protections are documented in the rim README: **[firmware-wheel → Wheel ↔ base wiring & power](../firmware-wheel/README.md#wheel--base-wiring--power-simple-vs-full-rim)**.

---

## 🇧🇷 Português

### Marco atual: M0 — bring-up (só serial, SEM motor)

**Objetivo:** provar toolchain + gravação + USB serial na sua ODESC. Inofensivo (não mexe no motor/potência).

#### Pré-requisitos
- **VS Code + extensão PlatformIO** (instala o toolchain STM32duino sozinho na primeira build).
- **ODESC v4.2** + cabo **USB** (micro-USB).
- Para gravar, **um** dos dois:
  - **ST-Link V2** (recomendado) ligado ao header **SWD** da ODESC: `SWDIO`, `SWCLK`, `GND`, `3V3`.
  - **DFU** (sem ST-Link): colocar a placa em modo DFU (BOOT0 em alto no reset — pad/botão de BOOT) e usar `upload_protocol = dfu` no `platformio.ini`.
- Alimentação: **não precisa de motor**. O USB/ST-Link já alimenta a lógica. (Não conecte o motor no M0.)

#### Passos
1. Abra a pasta `firmware-base/` no VS Code (PlatformIO detecta o `platformio.ini`).
2. Conecte o ST-Link (ou ponha em DFU) e o cabo USB da ODESC.
3. **Build**: ícone ✓ do PlatformIO (ou `pio run`). A primeira vez baixa o framework — leva alguns minutos.
4. **Upload**: ícone → do PlatformIO (ou `pio run -t upload`).
5. **Serial Monitor**: ícone 🔌 (ou `pio device monitor`), 115200 baud.

#### Resultado esperado (M0 ✅)
No monitor serial aparece:
```
=== DriveLab Firmware — M0 bring-up ===
...
DriveLab M0 vivo, tick = 0
DriveLab M0 vivo, tick = 1
...
```

#### Se der problema
- **Build falha** → mande o erro; ajustamos.
- **Upload falha (ST-Link não acha a placa)** → confira os 4 fios SWD (SWDIO/SWCLK/GND/3V3) e a alimentação. Alternativa: DFU.
- **Gravou mas o USB serial não aparece** → o suspeito nº1 é o **cristal HSE**. O `genericSTM32F405RG` assume 8 MHz; se a ODESC usar outro, o clock de 48 MHz da USB fica errado e a porta não enumera. Solução: descobrir o cristal da ODESC e definir `-D HSE_VALUE=<hz>` (e ajustar o SystemClock se preciso). Me chame que resolvemos. *(O LED/serial via ST-Link ainda funciona mesmo se a USB não subir — dá pra separar os problemas.)*

---

### M0.5 — USB/FFB (o de-risco principal) ⚠️ — COMPILA no F405, aguardando bancada

Provar a decisão **B2** no F405: **enumerar como dispositivo Force Feedback** (volante DirectInput) no Windows, reusando a pilha FFB pronta. **Sem motor** ainda.

> **Atualização (2026-07):** o rascunho (`src/m05/main.cpp`) agora **compila limpo no F405** (3,5% de flash). Dois ajustes vs. o 1º rascunho: o header real do shim é `USBAPI.h` (não existe `USBLibrarySTM32.h`), e a lib de FFB usa a macro AVR `_delay_us()` — mapeada para `delayMicroseconds()` via header force-included (`include/avr_compat.h`), sem editar a lib de terceiros (re-baixada pelo lib_deps). **Falta validar na bancada**: que enumere de fato como volante FFB no hardware real (atenção ao cristal HSE / VBUS sensing abaixo).

#### Como gravar o M0.5
```bash
pio run -e m05 -t upload      # ou selecione o env "m05" na barra do PlatformIO
```
As libs (`USBLibrarySTM32` + `ArduinoJoystickWithFFBLibrary`) são baixadas via `lib_deps` (git) na primeira build. O `env:m05` **não** usa `USBD_USE_CDC` (o USB passa a ser o do shim).

#### Verificação (M0.5 ✅)
Windows → *Painel de Controle → Dispositivos e Impressoras → (o dispositivo) → Configurações do controle de jogo → Propriedades*:
- Aparece um **eixo de direção se movendo sozinho** (o sketch varre o steering) → **enumerou + eixo ok**.
- Aba/botão de **Force Feedback / Testar** presente → o descriptor PID subiu.

#### Pontos de incerteza a resolver na bancada (por ordem de risco)
1. **Compilação das libs juntas** — o shim e a lib de FFB são AVR-origin; pode faltar/conflitar algum símbolo. Se não compilar, mande o erro — ajustamos (talvez copiar arquivos, não só `lib_deps`).
2. **Nome do header do shim** (`#include <USBLibrarySTM32.h>`) — verificar o header real do repo.
3. **`getUSBPID()` numa ISR** — na versão AVR roda dentro da ISR de USB; no rascunho chamamos em `loop()`. Se os efeitos não registrarem no teste de FFB, precisa hookar no callback/ISR de USB do shim.
4. **Clock/USB** — 48 MHz (PLL48CLK); **desabilitar VBUS sensing** se bus-powered; o gotcha do cristal HSE (ver M0).
5. **F405 não é oficialmente testado** pelo shim (só F401/F411, mesma família OTG_FS).

Se travar de vez, os fallbacks já estão mapeados (**B1**: TinyUSB + PID próprio MIT; ou **2-MCU**: AVR 32u4 + STM32). Ver design §2.

*(A prioridade da IRQ USB abaixo do timer do FOC só importa a partir do M1, quando o SimpleFOC entrar.)*

---

### Marcos seguintes (resumo)
M1 (motor malha aberta) → M2 (encoder + malha fechada + brake resistor) → M3 (canal A0, **DriveLab Studio conecta via HidTransport**) → M4 (settings) → M5 (força FFB → SimpleFOC) → M6 (efeitos de jogo) → M7 (validação num sim). Detalhes no design.

### Conexão do volante (hub USB + trilho de 5 V)

Para um aro de engate rápido, a base deve hospedar um pequeno **hub USB** (a ODESC e o aro dividem um único cabo pro PC) e um **buck de 5 V** a partir do PSU principal para alimentar os LEDs RGB do aro — assim nenhum cabo USB extra fica enrolando no volante. A fiação completa, os sinais que cruzam o slip ring e as proteções necessárias estão no README do aro: **[firmware-wheel → Interligação com a base & alimentação](../firmware-wheel/README.md#interligação-com-a-base--alimentação-aro-simples-vs-completo)**.
