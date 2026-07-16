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

### Pins (RP2040-Zero — initial proposal, tunable at the top of main.cpp)
- Buttons (3×4 matrix): rows GP2/3/4, columns GP5/6/7/8.
- Shift paddles: GP9 (up), GP10 (down).
- Clutches (ADC): GP26 (left), GP27 (right).
- Encoders: enc0 GP11/GP12, enc1 GP13/GP14; push GP15.
- WS2812 (data): GP16.

### ⚠️ Written without a board — check on the bench first
1. **Known latent bug — vendor P0 response.** This firmware's **vendor P0** channel **still sends the `0x16` (SettingValue) response straight from the `onSetReport` callback** — this is a **known latent bug**. The pedals and the handbrake had exactly this problem: TinyUSB's single HID endpoint drops the 2nd report sent back-to-back, so settings reads fail. **Before validating settings on this firmware, you MUST apply the same fix already made in `firmware-pedal`/`firmware-handbrake`**: queue the response in the callback and send it from `loop()` with priority over the gamepad, and make sure the payload is ≤ 63 bytes (the 64-byte EP = 63 payload + 1 report id).
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

### Pinos (RP2040-Zero — proposta inicial, ajustável no topo do main.cpp)
- Botões (matriz 3×4): linhas GP2/3/4, colunas GP5/6/7/8.
- Pás de shift: GP9 (up), GP10 (down).
- Embreagens (ADC): GP26 (esq.), GP27 (dir.).
- Encoders: enc0 GP11/GP12, enc1 GP13/GP14; push GP15.
- WS2812 (dados): GP16.

### ⚠️ Escrito sem placa — conferir primeiro na bancada
1. **Bug latente conhecido — resposta do vendor P0.** O canal **vendor P0** deste firmware **ainda envia a resposta `0x16` (SettingValue) direto do callback `onSetReport`** — isso é um **bug latente já conhecido**. A pedaleira e o freio de mão tiveram exatamente este problema: o endpoint HID único do TinyUSB dropa o 2º report enviado back-to-back, então a leitura de settings falha. **Antes de validar settings neste firmware, é obrigatório aplicar o mesmo fix já feito em `firmware-pedal`/`firmware-handbrake`**: enfileirar a resposta no callback e enviá-la a partir do `loop()` com prioridade sobre o gamepad, além de garantir payload ≤ 63 bytes (o EP de 64 = 63 payload + 1 report id).
2. **OUTPUT reports do TinyUSB** (`onSetReport`): recepção de `WheelLed`/`SettingWrite` — suspeito nº1 (igual pedal/handbrake).
3. **Report descriptor** (gamepad + vendor) visível ao Windows/HidSharp.
4. **Byte-layout** casando 1:1 com `DriveLab.Core` (`WheelState`, `WheelLedReport`).
5. Timing WS2812 vs. USB.

### Como gravar/validar (futuro M5)
- BOOTSEL → build/upload no PlatformIO. `joy.cpl` mostra "DriveLab Wheel" (32 botões + 2 eixos).
- DriveLab Studio (quando o transport do rim existir) lê telemetria e manda cores.
