# DriveLab Firmware — Volante removível / rim (RP2040)

Firmware do **rim DriveLab** (o aro com botões, pás e LEDs) — placa **Waveshare RP2040-Zero**,
dispositivo USB HID **próprio** (PID `0x1209:0x0004`), enumera como **"DriveLab Wheel"**.

<p align="center"><a href="#-english">🇬🇧 English</a> &nbsp;·&nbsp; <a href="#-português">🇧🇷 Português</a></p>

---

## 🇬🇧 English

Firmware for the **DriveLab rim** (the wheel face with buttons, paddles and LEDs) — **Waveshare RP2040-Zero** board,
a **custom** USB HID device (PID `0x1209:0x0004`), enumerates as **"DriveLab Wheel"**.
Design/decisions: internal project notes (not versioned in the public repo).

> RP2040 + **arduino-pico** (Philhower) + Adafruit_TinyUSB + Adafruit_NeoPixel. MIT license.
> **Status:** M1→M4 **written, awaiting on-board validation**. M5 (validation/polish) is still pending.

### Two HID channels
1. **Gamepad** (report `0x01`): 32 buttons + 2 axes (clutch paddles). What games read.
2. **Vendor P0** (64 bytes): `WheelState 0x21` (telemetry), `WheelLed 0x18` (colors),
   `SettingWrite/ReadReq/Value 0x14/0x15/0x16`, `Command 0x02`. What DriveLab Studio uses.

### Milestones
- **M1** Gamepad HID: buttons (matrix + shift paddles) + 2 clutch axes (ADC) + encoders-as-buttons. Visible in `joy.cpl`.
- **M2** Vendor P0: `WheelState` (button + paddle bitmap), `Command`, `Settings`, paddle calibration.
- **M3** WS2812 LEDs: `WheelLed` applies colors; brightness/count via setting.
- **M4** Flash: `SaveToFlash` persists paddle calibration + LED config (magic "DLW1"); loads on boot.

### Rim I/O & pin map (tunable at the top of main.cpp)
Target rim: **10 push buttons (each RGB-lit)**, **5 rotary encoders** (with push), **4 paddles** (2 clutch + 2 shift/gears), a **D-pad** (directional buttons), and a **LED bar** (rev lights). On an RP2040-Zero the 5 encoders already eat 10 GPIOs, so the ~21 slow buttons go on **two MCP23017 I²C expanders** (32 inputs on 2 pins). BOM adds **2× MCP23017** (~US$1.5 each).

- **I²C (MCP23017 ×2):** SDA `GP0`, SCL `GP1` — addresses `0x20` (#0) and `0x21` (#1).
- **MCP #0** (16 in): 10 push buttons → bits 0–9 · gears down/up → bits 10–11 · D-pad U/D/L/R → bits 12–15.
- **MCP #1** (5 in used): the 5 rotary pushes → bits 16–20.
- **Encoders A/B (direct GPIO):** `GP2/3, GP4/5, GP6/7, GP8/9, GP10/11` — CW → bits 21–25, CCW → bits 26–30 (momentary).
- **Clutches (ADC):** `GP26` (left), `GP27` (right) — analog, for progressive clutch + bite point.
- **WS2812 data:** `GP28` — one chained strand: **pixels 0–9 = the 10 button LEDs, then the LED bar** (`ledCount` = 10 + bar, a P0 setting).

> 32-button gamepad report: 31 buttons used (bit 31 spare) + 2 clutch axes. Games read it directly; the app drives the RGB over the P0 `WheelLed` (0x18) channel.

### ⚠️ Written without a board — check on the bench first
1. **Vendor P0 response — ✅ already fixed (2026-07).** The `0x16` (SettingValue) response is now **queued in `onSetReport` and sent from `loop()` with priority over the gamepad**, and the payload is ≤ 63 bytes — the same fix applied to `firmware-pedal`/`firmware-handbrake` (TinyUSB's single HID endpoint drops the 2nd report sent back-to-back, so settings reads would fail if `0x16` went straight from the callback). Still to confirm on real hardware once the rim is wired.
2. **TinyUSB OUTPUT reports** (`onSetReport`): reception of `WheelLed`/`SettingWrite` — suspect #1 (same as pedal/handbrake).
3. **Report descriptor** (gamepad + vendor) visible to Windows/HidSharp.
4. **Byte-layout** matching `DriveLab.Core` 1:1 (`WheelState`, `WheelLedReport`).
5. WS2812 timing vs. USB.

### How to flash/validate (future M5)
- BOOTSEL → build/upload in PlatformIO. `joy.cpl` shows "DriveLab Wheel" (32 buttons + 2 axes).
- DriveLab Studio (once the rim transport exists) reads telemetry and sends colors.

---

## 🇧🇷 Português

Firmware do **rim DriveLab** (o aro com botões, pás e LEDs) — placa **Waveshare RP2040-Zero**,
dispositivo USB HID **próprio** (PID `0x1209:0x0004`), enumera como **"DriveLab Wheel"**.
Design/decisões: notas internas de projeto (não versionadas no repo público).

> RP2040 + **arduino-pico** (Philhower) + Adafruit_TinyUSB + Adafruit_NeoPixel. Licença MIT.
> **Status:** M1→M4 **escritos, aguardando validação na placa**. Falta o M5 (validação/polimento).

### Dois canais HID
1. **Gamepad** (report `0x01`): 32 botões + 2 eixos (pás de embreagem). O que os jogos leem.
2. **Vendor P0** (64 bytes): `WheelState 0x21` (telemetria), `WheelLed 0x18` (cores),
   `SettingWrite/ReadReq/Value 0x14/0x15/0x16`, `Command 0x02`. O que o DriveLab Studio usa.

### Marcos
- **M1** Gamepad HID: botões (matriz + pás de shift) + 2 eixos de embreagem (ADC) + encoders-como-botões. Visível no `joy.cpl`.
- **M2** Vendor P0: `WheelState` (bitmap de botões + pás), `Command`, `Settings`, calibração das pás.
- **M3** LEDs WS2812: `WheelLed` aplica cores; brilho/contagem por setting.
- **M4** Flash: `SaveToFlash` persiste calibração das pás + config de LED (magic "DLW1"); carrega no boot.

### Entradas do aro & mapa de pinos (ajustável no topo do main.cpp)
Aro alvo: **10 botões de pressão (cada um com LED RGB)**, **5 encoders rotativos** (com push), **4 pás** (2 embreagem + 2 marcha), um **D-pad** (botões direcionais) e uma **barra de LEDs** (rev lights). Na RP2040-Zero os 5 encoders já consomem 10 GPIOs, então os ~21 botões lentos vão em **dois expanders I²C MCP23017** (32 entradas em 2 pinos). BOM acrescenta **2× MCP23017** (~US$1,5 cada).

- **I²C (MCP23017 ×2):** SDA `GP0`, SCL `GP1` — endereços `0x20` (#0) e `0x21` (#1).
- **MCP #0** (16 in): 10 botões de pressão → bits 0–9 · marcha down/up → bits 10–11 · D-pad cima/baixo/esq/dir → bits 12–15.
- **MCP #1** (5 in usados): os 5 push dos rotativos → bits 16–20.
- **Encoders A/B (GPIO direto):** `GP2/3, GP4/5, GP6/7, GP8/9, GP10/11` — CW → bits 21–25, CCW → bits 26–30 (momentâneos).
- **Embreagens (ADC):** `GP26` (esq.), `GP27` (dir.) — analógico, para embreagem progressiva + bite point.
- **WS2812 (dados):** `GP28` — um cordão em série: **pixels 0–9 = os 10 LEDs dos botões, depois a barra de LEDs** (`ledCount` = 10 + barra, um setting P0).

> Report de gamepad com 32 botões: 31 usados (bit 31 sobra) + 2 eixos de embreagem. Os jogos leem direto; o app manda as cores RGB pelo canal P0 `WheelLed` (0x18).

### ⚠️ Escrito sem placa — conferir primeiro na bancada
1. **Resposta do vendor P0 — ✅ já corrigido (jul/2026).** A resposta `0x16` (SettingValue) agora é **enfileirada no `onSetReport` e enviada do `loop()` com prioridade sobre o gamepad**, com payload ≤ 63 bytes — o mesmo fix aplicado em `firmware-pedal`/`firmware-handbrake` (o endpoint HID único do TinyUSB dropa o 2º report back-to-back, então a leitura de settings falharia se o `0x16` saísse direto do callback). Falta confirmar em hardware real quando o aro estiver montado.
2. **OUTPUT reports do TinyUSB** (`onSetReport`): recepção de `WheelLed`/`SettingWrite` — suspeito nº1 (igual pedal/handbrake).
3. **Report descriptor** (gamepad + vendor) visível ao Windows/HidSharp.
4. **Byte-layout** casando 1:1 com `DriveLab.Core` (`WheelState`, `WheelLedReport`).
5. Timing WS2812 vs. USB.

### Como gravar/validar (futuro M5)
- BOOTSEL → build/upload no PlatformIO. `joy.cpl` mostra "DriveLab Wheel" (32 botões + 2 eixos).
- DriveLab Studio (quando o transport do rim existir) lê telemetria e manda cores.
