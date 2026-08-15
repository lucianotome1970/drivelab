<p align="center">
  <img src="app/DriveLab.Studio/Assets/splash.png" width="760" alt="DriveLab" />
</p>

<h1 align="center">DriveLab</h1>

<p align="center"><b>Open-source Direct-Drive sim-racing wheel</b><br/>
Custom firmware for ODrive v3.6-class boards (validated on the MKS ODRIVE-S V3.6-S6V) + a cross-platform configurator app.</p>

<p align="center">
  <a href="https://discord.gg/Xp2pGm5wj"><img src="https://img.shields.io/badge/Discord-join%20the%20server-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
  <img src="https://img.shields.io/badge/app-.NET%208%20%C2%B7%20Avalonia-512BD4" alt="App stack">
  <img src="https://img.shields.io/badge/firmware-STM32F405%20%C2%B7%20ODrive%20FOC-00979D" alt="Firmware stack">
  <img src="https://img.shields.io/badge/license-MIT-blue" alt="License">
  <img src="https://img.shields.io/badge/status-in%20development-orange" alt="Status">
</p>

<p align="center">
  <a href="#-english">🇬🇧 English</a> &nbsp;·&nbsp; <a href="#-português">🇧🇷 Português</a> &nbsp;·&nbsp; <a href="#-download">⬇️ Download</a> &nbsp;·&nbsp; <a href="https://discord.gg/Xp2pGm5wj">💬 Discord</a>
</p>

---

## 🇬🇧 English


## ⬇️ Download

**[Download the latest DriveLab Studio for Windows](https://github.com/lucianotome1970/drivelab/releases/latest)** — a self-contained `.exe`, no .NET install needed.

Early **pre-release** for testing. It isn't code-signed, so Windows SmartScreen will warn: **More info → Run anyway**. To explore without hardware, run `DriveLab.Studio.exe --simulator`.

All builds live on the [releases page](https://github.com/lucianotome1970/drivelab/releases).

> 🛠️ **Building & selling DDs?** The **[Maker's guide](docs/guia-criador.md)** shows how to configure your hardware and ship a Windows installer with the config bundled.

---

## 📸 Screenshots

<p align="center"><img src="docs/screenshots/home.png" width="860" alt="Home"></p>

**Home** — overview dashboard: the Wheel, Base, Pedals and Handbrake cards with live values (wheel angle, base force, live pedal bars) plus steering-rotation presets and the **Center** button.

<p align="center"><img src="docs/screenshots/wheelbase-basic.png" width="860" alt="Wheel Base — Basic"></p>

**Wheel Base → Basic** — everyday force-feedback tuning: total force, soft-stop force/range, wheel spring and damper, each with a slider and quick presets.

<p align="center"><img src="docs/screenshots/wheelbase-hardware.png" width="860" alt="Wheel Base — Hardware & telemetry"></p>

**Wheel Base → Hardware** — the read-only **telemetry monitor** (bus voltage, motor current, FET/motor/MCU temperatures) sits above the hardware setup: encoder direction/CPR, **encoder type** (quadrature E6B2 or magnetic SPI AS5047), current-loop P/I gains and calibration current.

<p align="center"><img src="docs/screenshots/pedals.png" width="860" alt="Pedals"></p>

**Pedals** — per-pedal output curves (Linear / S-Curve / Fast / Slow) with a draggable curve editor, invert, smoothing and sensor type (pot / hall / load cell through an HX711 or an instrumentation amp); the brake adds a load-cell target in % or kg. Live input bars on the right.

<p align="center"><img src="docs/screenshots/wheel.png" width="860" alt="Wheel"></p>

**Wheel** — customize the rim button **LED colors** and configure the paddles: number of paddles, per-paddle function (shift / clutch / free / button), combined vs independent clutch, digital vs progressive engagement, and bite point.

---

## ⚙️ The motor

The direct-drive actuator is a **cheap hoverboard hub motor**. Buy a spare 6.5" hoverboard wheel, strip the tire, and mount the bare hub to your rig — the machined face is where the wheel adapter bolts on.

<table>
<tr>
<td width="50%" valign="top">
<img src="docs/screenshots/motor-hoverboard-wheel.jpg" width="100%" alt="Hoverboard wheel" /><br/>
<b>As bought</b> — a 6.5" hoverboard wheel (3 phase wires + hall sensor).
</td>
<td width="50%" valign="top">
<img src="docs/screenshots/motor-hub-bare.webp" width="100%" alt="Bare hub motor" /><br/>
<b>Tire removed</b> — the bare hub motor: mounting face + axle, ready for the build.
</td>
</tr>
</table>

New to hoverboard motors? The **[Hoverboard base FAQ](docs/faq-hoverboard.md)** covers which motor to buy, how to open and prepare it, and the 25 problems people hit most often.

---

## 🔌 Base board

The wheelbase (the FFB motor stage) runs on any **STM32F405 ODrive-class controller** — the firmware is the same for all of them. Four proven options:

<table>
<tr>
<td width="50%"><img src="docs/screenshots/odesc-v42-24v.jpg" width="100%" alt="ODESC v4.2, 24 V variant"></td>
<td width="50%"><img src="docs/screenshots/odesc-v42-56v.jpg" width="100%" alt="ODESC v4.2, 56 V variant"></td>
</tr>
<tr>
<td><b>ODESC v4.2 — 8–24 V</b><br/>~70 A / 120 A peak.<br/><code>QC PASS 24V</code> sticker · <b>purple</b> status LED.</td>
<td><b>ODESC v4.2 — 8–56 V</b><br/>~70 A / 120 A peak.<br/><code>QC PASS 56V</code> sticker · <b>green</b> status LED.</td>
</tr>
<tr>
<td><img src="docs/screenshots/board-mks-xdrive-s.jpg" width="100%" alt="MKS XDrive-S"></td>
<td><img src="docs/screenshots/board-mks-xdrive-mini.jpg" width="100%" alt="MKS XDrive Mini V1.0"></td>
</tr>
<tr>
<td><b>MKS XDrive-S</b> — 12–56 V<br/>60 A / 120 A peak · ships with heatsinks.</td>
<td><b>MKS XDrive Mini V1.0</b> — 12–56 V<br/>Same F405 MCU in a smaller board.</td>
</tr>
</table>

**Wiring is the same on all four** — power on the screw terminals, signal on the JST headers along the bottom edge:

| Connection | Where |
|---|---|
| Motor phases | <code>A</code> / <code>B</code> / <code>C</code> screw terminals |
| Power supply | <code>DC +/−</code> screw terminals |
| Brake resistor | <code>AUX +/−</code> |
| **Incremental encoder** (A/B/Z, e.g. Omron E6B2) | <code>ABZ</code> header — `5V · A · B · Z · GND` |
| **Magnetic encoder** (SPI, e.g. MT6835 / AS5047P) | <code>SPI</code> header — `3.3V · GND · SCK · MISO · MOSI · CS` |
| Debug / flashing | <code>SWD</code> header — `3.3V · SWDIO · SWCLK · GND · RST` |

Both encoder headers are present on every one of these boards, so the sensor choice is not a board choice. The firmware currently drives the **incremental A/B/Z** path; **SPI magnetic** support is in progress — see [encoders.md](docs/encoders.md) for which sensor to buy and why.

Adding a sensor or a button? **[Pinout & wiring reference](docs/pinout.md)** maps every MCU pin, marks what is free, and has the wiring for the motor NTC, the centering button and the phase contactor — including why the NTC pull-up is 1 kΩ and not the obvious 10 kΩ.

The two ODESC variants look identical apart from the sticker and the LED colour — the capacitors are 63 V on both, so they tell you nothing. Feeding the 24 V one more than it takes destroys it.

> ℹ️ The firmware is currently **pinned to the ODrive v3.6 layout** and **validated on an MKS ODRIVE-S V3.6-S6V**. Other F405 ODrive-class boards share the MCU and USB, but a board with a **different pinout (e.g. ODESC v4.2)** may need a pin remap in `firmware-base/vendor/odrive-fw/Board/v3/`.

---

## 🎛️ Firmware modules

DriveLab is split into independent firmwares — one per device, each with its own README. The Studio app connects to each over USB HID and auto-detects it by VID/PID.

- **[Wheelbase »](firmware-base/README.md)** — ODrive v3.6-class (MKS ODRIVE-S V3.6-S6V) · STM32F405 · the FFB motor stage. *Runs the motor under FOC; FFB pipe validated with ACC/AMS2/EVO. Game-effects (M6) on the bench.*
- **[Pedals »](firmware-pedal/README.md)** — RP2040 · 3 axes · load cell (HX711 or instrumentation amp) · **P0** protocol. **✅ Validated on hardware.**
- **[Handbrake »](firmware-handbrake/README.md)** — RP2040 · 1 axis + button · **P0** protocol. **✅ Validated on hardware** (physical sensor still to test).
- **[Rim »](firmware-wheel/README.md)** — RP2040 · gamepad (buttons + paddles) · WS2812 LEDs · **P0**. *Written, awaiting bench validation.*

The desktop app that connects to all of them: **[DriveLab Studio (app) »](app/README.md)** — .NET 8 / Avalonia.

Want to run the app against **your own board**? The full USB-HID contract is documented in **[docs/PROTOCOL.md »](docs/PROTOCOL.md)** — implement it and the Studio drives your hardware, no app changes.

### 📚 Guides

Start here:

- **[How it works](docs/how-it-works.md)** — study guide: how a DD wheel works, grounded in DriveLab (motor · encoder · FOC · FFB · safety).

Hardware & build guides:

- **[Hoverboard base FAQ](docs/faq-hoverboard.md)** — the most common build problems and what to do, sorted by symptom. Firmware-agnostic.
- **[Encoder guide](docs/encoders.md)** — which position sensor to buy (E6B2 · MT6701 · AS5047P).
- **[Brake resistor](docs/brake-resistor.md)** — why 2Ω, and the Ω vs W ratings.
- **[Soft-power & contactor](docs/soft-power-contator.md)** — safe power-off, contactor wiring, soft-power button.
- **[Torque](docs/calculo-torque.md)** — sizing the motor's Nm.
- **[Maker's guide](docs/guia-criador.md)** — configure hardware & ship a Windows installer.
- **[USB-HID protocol](docs/PROTOCOL.md)** — the full contract to drive your own board.

---

## 🧠 Why the RP2040?

The pedals, handbrake and rim all run on a **Waveshare RP2040-Zero**. The device side needs to be a **custom USB HID device** — a gamepad **plus** a vendor channel (report ids `0x14/0x15/0x16/0x20`) that the app uses to read/write settings and stream telemetry. That drove the choice:

- **Native USB** on the MCU (not a USB-to-serial bridge) — required to enumerate as a real HID device.
- **Full control of the HID report descriptor** — provided by **Adafruit_TinyUSB**, which runs on the RP2040. That's what lets us define the custom vendor reports, not just a stock gamepad.
- Plenty of **GPIO** (axes, buttons, encoders, WS2812 LEDs), **USB-C**, dual core, and it's **cheap** (~US$2–5).

**Can I use an Arduino instead?** Only boards with **native USB**: the **Arduino Nano RP2040 Connect** runs practically as-is (it's an RP2040); **SAMD21** boards (Zero, MKR, Nano 33 IoT) need a light port (TinyUSB supports SAMD); an **ATmega32U4** (Leonardo/Micro/Pro Micro) can do HID but through a different USB stack with tight flash/RAM (the vendor settings channel would need porting). Classic **Uno / Nano / Mega** (ATmega328/2560) **won't work** — no native USB, their CH340/FTDI chip is serial-only.

> The wheelbase firmware is the exception — it targets the **STM32F405** for the FFB motor, not an RP2040.

---

## What is DriveLab?

DriveLab turns cheap, widely-available parts — an **ODrive v3.6-class** motor controller (validated on an **MKS ODRIVE-S V3.6-S6V**) and a **hoverboard hub motor** — into a real **Direct-Drive force-feedback steering wheel** for sim racing (Assetto Corsa Competizione, iRacing, rFactor 2, Automobilista 2, and any DirectInput title).

It is a fully open alternative to closed solutions like FFBeast, with two halves:

- **DriveLab Studio** — a desktop app (.NET 8 / Avalonia) to configure and monitor the wheel. Runs on Windows, and on macOS/Linux for development.
- **DriveLab Firmware** — firmware for the ODrive v3.6-class board that enumerates as a standard DirectInput force-feedback wheel and drives the motor with field-oriented control built on the [ODrive](https://odriverobotics.com) firmware (vendored, MIT).

> ⚠️ **Status: in active development.** The app is functional (with a hardware simulator, no board required). The firmware **runs the motor under FOC** and the **FFB pipe is validated with real games (ACC 400 Hz, AMS2, EVO)**; full on-track FFB tuning is waiting on the magnetic encoder. See the [Roadmap](#roadmap).

## Features

**App (DriveLab Studio)**
- Clean, modern UI with **Wheel Base**, **Pedals**, **Handbrake**, and **Wheel** (rim/LEDs) modules.
- **Named profiles per module** — save, apply, rename and delete named configs (e.g. "GT3", "Rain") for the base, pedals, handbrake and wheel; selecting a profile writes it to the controller, and *Save* lights up only when the current config differs from the loaded profile.
- **Wheel LEDs** — per-button colors + global brightness; the rim **stores its colors in flash** (lights up on its own after a power-cycle) and the app **reads them back** from the board on connect.
- Live **settings** grouped in tabs (Basic / Advanced / **Feel** / Hardware) — total force, damper, spring, soft-stop, **per-effect FFB gains** (spring/damper/friction/inertia), torque & power limits, encoder config, current loop, etc. Auto-load on connect, auto-save on change.
- **Telemetry monitor** in the Hardware tab: bus voltage + FET/motor/MCU temperatures + motor current, with ok/warning/critical thresholds.
- **Three encoder types** — incremental **quadrature** (Omron E6B2, the current bench sensor) or absolute **magnetic** (MT6701 planned default · AS5047P planned). Absolute keeps its zero across power cycles. *(Magnetic drivers land in Stage 1.)*
- **Simulator mode** — a virtual wheel with real physics, so you can develop and test the whole UI without any hardware.
- Bilingual (English / Portuguese), auto-detected from the OS.

**Firmware**
- Enumerates as a **DirectInput FFB wheel** — games send force feedback to it exactly like they would to any commercial wheel, no plugin needed.
- **Field-oriented control** of the hub motor, built on the ODrive firmware.
- Multi-stage safety: brake-resistor chopper, current/torque limits, soft-stop, over-voltage cutoff, plus **opt-in off-state contactor and soft-power button** (host-tested groundwork).
- Companion firmware for **pedals** and **handbrake** modules (RP2040 + load cell).

## Hardware — wheelbase (bill of materials)

This is the **base** (the direct-drive wheelbase). Each other module is an independent USB device with its **own** bill of materials — see the per-module table below.

| Part | Notes |
|------|-------|
| **ODrive v3.6-class board** (STM32F405) — validated: **MKS ODRIVE-S V3.6-S6V** | Any F405 ODrive-class board — see [Base board](#-base-board). **MKS boards take 12–56 V**, so the supply is your choice within that range. **ODESC boards ship in two variants, 8–24 V and 8–56 V** — check the `QC PASS` sticker (`24V` / `56V`) or the LED colour (purple / green) and stay inside it. Either way, a lower supply leaves extra headroom against regen voltage spikes. Boards other than the ODrive v3.6 layout may need a pin remap. |
| **Hoverboard hub motor** | The direct-drive actuator. |
| **Encoder** | Incremental Omron E6B2-CWZ6C **or** absolute magnetic AS5047P/MT6701 — your choice. |
| **Brake resistor 2 Ω / 100 W** | **Mandatory** before closed loop — dissipates regen energy so it doesn't destroy the caps. |
| **PSU** | Stay inside what your board accepts: **12–56 V** on an MKS board; on an ODESC, **8–24 V** or **8–56 V** depending on the variant. Example: 24 V / 30 A (720 W). |
| ST-Link V2 — **optional** | **You do not need one to build a wheel.** The STM32F405 has a bootloader burned into ROM at the factory, and firmware goes in over the same USB data cable — including the very first flash on a factory board (put it in DFU by hand, then DriveLab Studio writes it). An ST-Link is a bench tool: it reads the board live while it runs, which is how the numbers in this project were measured. Get one if you plan to debug firmware, not to assemble. |

## Hardware — per module

Each device is independent (its own board + USB). Full parts list + wiring/pinout in each module's README:

| Module | Core hardware | Full BOM |
|--------|---------------|----------|
| **Wheelbase** | F405 board + hub motor + encoder + brake resistor + PSU (table above) | this page |
| **Pedals** | RP2040-Zero + 3 sensors (pot / Hall / load cell + HX711 or instrumentation amp) | **[firmware-pedal »](firmware-pedal/README.md#bill-of-materials-pedals)** |
| **Handbrake** | RP2040-Zero + 1 sensor (pot / Hall / load cell + HX711 or instrumentation amp) | **[firmware-handbrake »](firmware-handbrake/README.md#bill-of-materials-handbrake)** |
| **Wheel (rim)** | RP2040-Zero + 2× MCP23017 + 5 encoders + SK6812 LEDs | **[firmware-wheel »](firmware-wheel/README.md)** |

## How force feedback works

The game does **not** send telemetry — it sends the **already-computed force**:

```
Game physics (ACC/iRacing)  →  one torque value for the wheel  (~360–400 Hz)
        ↓  DirectInput / HID PID  (Windows)
        ↓  USB
Firmware (TinyUSB HID PID parser → FfbEngine.step)  →  torque
        ↓  FOC (ODrive)
Motor torque  →  you feel it
```

Condition effects (spring/damper) are computed on the device from the **encoder** position/velocity; your Studio settings (gain, damper, filters) shape the result before it reaches the motor.

## Repository layout

```
app/                 DriveLab Studio (.NET 8 / Avalonia) + Core, Hid, Simulator, tests
firmware-base/       Wheelbase firmware — ODrive v3.6-class / STM32F405, the FFB motor  [MIT]
firmware-pedal/      Pedals firmware — RP2040 + load cell                               [MIT]
firmware-handbrake/  Handbrake firmware — RP2040 + load cell                            [MIT]
firmware-wheel/      Rim firmware — RP2040 (Waveshare Zero): gamepad + WS2812 LEDs      [MIT]
tools/HidDump/       HID protocol debug tool
docs/                Guides, design specs & implementation plans
```

## Getting started

**Run the app (with the simulator — no hardware needed):**

```bash
# needs the .NET 8 SDK
cd app
dotnet run --project DriveLab.Studio -- --simulator
```

**Build & test:**

```bash
./scripts/build.sh    # or scripts/build.ps1 on Windows
./scripts/test.sh     # app tests + firmware host tests + the orphan-setting check
```

**Ship a Windows build** (self-contained single-file `.exe`, no .NET needed on the target):

```bash
./scripts/publish-win.sh   # or scripts/publish-win.ps1 on Windows
# output: dist/win-x64/DriveLab.Studio.exe
```

**Flash the firmware** (needs [PlatformIO](https://platformio.org)): open `firmware-base/` and start at milestone **M0** (serial only, no motor) — see `firmware-base/README.md`.

## Roadmap

`M0` ✅ → `M0.5` ✅ USB FFB → `M1`–`M2` motor + encoder + closed loop ✅ → `M2.5` telemetry ✅ → `M3` app↔firmware ✅ → `M4` settings ✅ → `M5` FFB force → motor ✅ *(firmware; bench-tested)* → `M6` game effects 🔧 *(firmware done; on-track validation waiting on the magnetic encoder)* → `M7` sim validation ⏳.

The brake-resistor chopper, off-state contactor and soft-power are implemented as **opt-in groundwork**. Details in `docs/` — start with **[how-it-works.md](docs/how-it-works.md)**.

## ⚠️ Safety

- **Know what your board accepts before you plug in a supply.** **MKS boards take 12–56 V.** **ODESC boards come in two variants, 8–24 V and 8–56 V.** You can tell them apart without a datasheet: the **QC PASS sticker** on the board reads `24V` or `56V`, and the status **LED is purple on the 24 V board and green on the 56 V board**. Do not go by the capacitors — both variants use 63 V ones. **Never exceed your board's rating**, and remember regen spikes push the bus above the supply voltage, so a lower supply is the safer choice.
- The **2 Ω brake resistor is mandatory** before any closed-loop torque; regen braking pushes energy back onto the bus and will destroy the capacitors without it.
- `M0`/`M0.5` run **with no motor connected**. Bring current up gradually. A direct-drive wheel has enough torque to hurt your wrist — keep an e-stop (the plug) within reach.

## License

**Everything — app, libraries, tools, and all firmware** (base + pedal/handbrake/wheel): [MIT](https://opensource.org/licenses/MIT).

Every source file carries a header stating its license.

## Community & contributing

Questions, build logs, help getting your board running — **join the Discord**: **https://discord.gg/Xp2pGm5wj**

Issues and pull requests are welcome. New source files should include the standard DriveLab header.

---

## 🇧🇷 Português


## ⬇️ Download

**[Baixar o DriveLab Studio mais recente para Windows](https://github.com/lucianotome1970/drivelab/releases/latest)** — um `.exe` self-contained, sem precisar instalar o .NET.

**Pre-release** inicial, para testes. Não é assinado digitalmente, então o SmartScreen do Windows vai avisar: **Mais informações → Executar assim mesmo**. Para explorar sem hardware, rode `DriveLab.Studio.exe --simulator`.

Todas as versões ficam na [página de releases](https://github.com/lucianotome1970/drivelab/releases).

> 🛠️ **Monta e vende DDs?** O **[Guia do criador](docs/guia-criador.md)** mostra como configurar seu hardware e gerar um instalador Windows já com a configuração embutida.

---

## 📸 Telas

<p align="center"><img src="docs/screenshots/home.png" width="860" alt="Painel inicial"></p>

**Painel inicial** — visão geral: cartões do Volante, Base, Pedais e Freio de mão com valores ao vivo (ângulo do volante, força da base, barras dos pedais), mais os presets de rotação e o botão **Center**.

<p align="center"><img src="docs/screenshots/wheelbase-basic.png" width="860" alt="Base do Volante — Básico"></p>

**Base do Volante → Básico** — ajuste de force feedback do dia a dia: força total, força e alcance do batente, mola e damper do volante, cada um com slider e presets rápidos.

<p align="center"><img src="docs/screenshots/wheelbase-hardware.png" width="860" alt="Base do Volante — Hardware e telemetria"></p>

**Base do Volante → Hardware** — o **monitor de telemetria** (tensão do barramento, corrente do motor, temperaturas FET/motor/MCU), somente leitura, fica acima da configuração de hardware: direção e CPR do encoder, **tipo de encoder** (quadratura E6B2 ou magnético SPI AS5047), ganhos P/I da malha de corrente e corrente de calibração.

<p align="center"><img src="docs/screenshots/pedals.png" width="860" alt="Pedais"></p>

**Pedais** — curvas de saída por pedal (Linear / S-Curve / Fast / Slow) com editor de curva arrastável, inverter, suavização e tipo de sensor (potenciômetro / hall / célula de carga por HX711 ou por amplificador); o freio ganha um alvo de célula de carga em % ou kg. Barras de entrada ao vivo à direita.

<p align="center"><img src="docs/screenshots/wheel.png" width="860" alt="Volante"></p>

**Volante** — personalize as **cores dos LEDs** dos botões do aro e configure as pás: número de pás, função de cada uma (marcha / embreagem / livre / botão), embreagem combinada ou independente, acionamento digital ou progressivo, e bite point.

---

## ⚙️ O motor

O atuador direct-drive é um **motor de roda de hoverboard, barato**. Compre uma roda de hoverboard de 6,5", tire o pneu e monte o cubo nu na sua estrutura — a face usinada é onde o adaptador do volante parafusa.

<table>
<tr>
<td width="50%" valign="top">
<img src="docs/screenshots/motor-hoverboard-wheel.jpg" width="100%" alt="Roda de hoverboard" /><br/>
<b>Como vem</b> — roda de hoverboard de 6,5" (3 fios de fase + sensor hall).
</td>
<td width="50%" valign="top">
<img src="docs/screenshots/motor-hub-bare.webp" width="100%" alt="Cubo nu" /><br/>
<b>Sem o pneu</b> — o cubo nu: face de fixação + eixo, pronto pra montar.
</td>
</tr>
</table>

Primeira vez com motor de hoverboard? O **[FAQ da base hoverboard](docs/faq-hoverboard.md)** cobre qual motor comprar, como abrir e preparar ele, e os 25 problemas que mais aparecem.

---

## 🔌 Placa base

A base (o estágio do motor de FFB) roda em qualquer **controladora STM32F405 classe ODrive** — o firmware é o mesmo para todas. Quatro opções comprovadas:

<table>
<tr>
<td width="50%"><img src="docs/screenshots/odesc-v42-24v.jpg" width="100%" alt="ODESC v4.2, variante de 24 V"></td>
<td width="50%"><img src="docs/screenshots/odesc-v42-56v.jpg" width="100%" alt="ODESC v4.2, variante de 56 V"></td>
</tr>
<tr>
<td><b>ODESC v4.2 — 8–24 V</b><br/>~70 A / 120 A de pico.<br/>Adesivo <code>QC PASS 24V</code> · LED de status <b>roxo</b>.</td>
<td><b>ODESC v4.2 — 8–56 V</b><br/>~70 A / 120 A de pico.<br/>Adesivo <code>QC PASS 56V</code> · LED de status <b>verde</b>.</td>
</tr>
<tr>
<td><img src="docs/screenshots/board-mks-xdrive-s.jpg" width="100%" alt="MKS XDrive-S"></td>
<td><img src="docs/screenshots/board-mks-xdrive-mini.jpg" width="100%" alt="MKS XDrive Mini V1.0"></td>
</tr>
<tr>
<td><b>MKS XDrive-S</b> — 12–56 V<br/>60 A / 120 A de pico · já vem com dissipadores.</td>
<td><b>MKS XDrive Mini V1.0</b> — 12–56 V<br/>Mesmo MCU F405, numa placa menor.</td>
</tr>
</table>

**A ligação é a mesma nas quatro** — potência nos bornes de parafuso, sinal nos conectores JST da borda de baixo:

| Ligação | Onde |
|---|---|
| Fases do motor | bornes <code>A</code> / <code>B</code> / <code>C</code> |
| Fonte de alimentação | bornes <code>DC +/−</code> |
| Resistor de freio | <code>AUX +/−</code> |
| **Encoder incremental** (A/B/Z, ex.: Omron E6B2) | conector <code>ABZ</code> — `5V · A · B · Z · GND` |
| **Encoder magnético** (SPI, ex.: MT6835 / AS5047P) | conector <code>SPI</code> — `3,3V · GND · SCK · MISO · MOSI · CS` |
| Debug e gravação | conector <code>SWD</code> — `3,3V · SWDIO · SWCLK · GND · RST` |

Os dois conectores de encoder existem em todas essas placas, então a escolha do sensor não é escolha de placa. O firmware hoje aciona o caminho **incremental A/B/Z**; o suporte a **magnético por SPI** está em andamento — veja o [encoders.md](docs/encoders.md) para escolher qual sensor comprar e por quê.

Vai acrescentar um sensor ou um botão? A **[referência de pinagem e montagem](docs/pinout.md)** mapeia cada pino do microcontrolador, marca o que está livre, e traz a ligação do NTC do motor, do botão de centralização e do contator das fases — inclusive por que o resistor do NTC é de 1 kΩ e não os 10 kΩ óbvios.

As duas variantes da ODESC são idênticas fora o adesivo e a cor do LED — os capacitores são de 63 V nas duas, então não dizem nada. Dar na de 24 V mais tensão do que ela aceita destrói a placa.

> ℹ️ O firmware está hoje **fixado no layout da ODrive v3.6** e **validado numa MKS ODRIVE-S V3.6-S6V**. Outras placas F405 classe ODrive compartilham o MCU e o USB, mas uma placa com **pinagem diferente (ex.: ODESC v4.2)** pode exigir remapear os pinos em `firmware-base/vendor/odrive-fw/Board/v3/`.

---

## 🎛️ Módulos de firmware

O DriveLab é dividido em firmwares independentes — um por dispositivo, cada um com seu próprio README. O app Studio conecta em cada um por USB HID e detecta automaticamente pelo VID/PID.

- **[Base »](firmware-base/README.md#-português)** — classe ODrive v3.6 (MKS ODRIVE-S V3.6-S6V) · STM32F405 · o estágio do motor de FFB. *Roda o motor em FOC; caminho do FFB validado com ACC/AMS2/EVO. Efeitos de jogo (M6) na bancada.*
- **[Pedais »](firmware-pedal/README.md#-português)** — RP2040 · 3 eixos · célula de carga (HX711 ou amplificador de instrumentação) · protocolo **P0**. **✅ Validado em hardware.**
- **[Freio de mão »](firmware-handbrake/README.md#-português)** — RP2040 · 1 eixo + botão · protocolo **P0**. **✅ Validado em hardware** (falta testar com o sensor físico).
- **[Aro »](firmware-wheel/README.md#-português)** — RP2040 · gamepad (botões + pás) · LEDs WS2812 · **P0**. *Escrito, aguardando validação na bancada.*

O app desktop que conversa com todos eles: **[DriveLab Studio (app) »](app/README.md#-português)** — .NET 8 / Avalonia.

Quer rodar o app na **sua própria placa**? O contrato USB-HID completo está documentado em **[docs/PROTOCOL.md »](docs/PROTOCOL.md)** — implemente ele e o Studio controla o seu hardware, sem mexer no app.

### 📚 Guias

Comece por aqui:

- **[Como funciona](docs/how-it-works.md)** — guia de estudo: como um volante DD funciona, aterrado no DriveLab (motor · encoder · FOC · FFB · segurança).

Guias de hardware e montagem:

- **[FAQ da base hoverboard](docs/faq-hoverboard.md)** — os problemas de montagem mais comuns e o que fazer, organizados por sintoma. Serve para qualquer firmware.
- **[Guia de encoders](docs/encoders.md)** — qual sensor de posição comprar (E6B2 · MT6701 · AS5047P).
- **[Resistor de frenagem](docs/brake-resistor.md)** — por que 2Ω, e a diferença entre os valores de Ω e W.
- **[Soft-power e contator](docs/soft-power-contator.md)** — desligar com segurança, fiação do contator, botão soft-power.
- **[Cálculo de torque](docs/calculo-torque.md)** — dimensionar os Nm do motor.
- **[Guia do criador](docs/guia-criador.md)** — configurar o hardware e gerar um instalador Windows.
- **[Protocolo USB-HID](docs/PROTOCOL.md)** — o contrato completo para usar a sua própria placa.

---

## 🧠 Por que o RP2040?

Os pedais, o freio de mão e o aro rodam numa **Waveshare RP2040-Zero**. O lado do dispositivo precisa ser um **dispositivo USB HID customizado** — um gamepad **mais** um canal vendor (report ids `0x14/0x15/0x16/0x20`) que o app usa para ler e gravar ajustes e receber telemetria. Foi isso que definiu a escolha:

- **USB nativo** no MCU (não uma ponte USB-serial) — necessário para enumerar como um HID de verdade.
- **Controle total do descritor HID** — dado pelo **Adafruit_TinyUSB**, que roda no RP2040. É o que permite definir os reports vendor customizados, não só um gamepad padrão.
- Bastante **GPIO** (eixos, botões, encoders, LEDs WS2812), **USB-C**, dois núcleos, e é **barato** (~US$ 2–5).

**Dá pra usar um Arduino?** Só placas com **USB nativo**: o **Arduino Nano RP2040 Connect** roda praticamente sem mudança (é um RP2040); placas **SAMD21** (Zero, MKR, Nano 33 IoT) precisam de um porte leve (o TinyUSB suporta SAMD); um **ATmega32U4** (Leonardo/Micro/Pro Micro) faz HID, mas por outra pilha USB e com flash/RAM apertados (o canal vendor de ajustes precisaria ser portado). Os clássicos **Uno / Nano / Mega** (ATmega328/2560) **não funcionam** — não têm USB nativo, o chip CH340/FTDI deles é só serial.

> O firmware da base é a exceção — ele mira o **STM32F405** para o motor de FFB, não um RP2040.

---

## O que é o DriveLab?

O DriveLab transforma peças baratas e fáceis de achar — uma controladora **classe ODrive v3.6** (validada numa **MKS ODRIVE-S V3.6-S6V**) e um **motor de roda de hoverboard** — num verdadeiro **volante Direct-Drive com force feedback** para simuladores (Assetto Corsa Competizione, iRacing, rFactor 2, Automobilista 2 e qualquer título DirectInput).

É uma alternativa totalmente aberta a soluções fechadas como o FFBeast, com duas metades:

- **DriveLab Studio** — um app desktop (.NET 8 / Avalonia) para configurar e monitorar o volante. Roda no Windows, e no macOS/Linux para desenvolvimento.
- **DriveLab Firmware** — firmware para a placa classe ODrive v3.6 que se apresenta como um volante DirectInput de force feedback padrão e aciona o motor com controle orientado a campo construído sobre o firmware do [ODrive](https://odriverobotics.com) (vendorizado, MIT).

> ⚠️ **Status: em desenvolvimento ativo.** O app já funciona (com um simulador de hardware, sem precisar de placa). O firmware **roda o motor em FOC** e o **caminho do FFB está validado com jogos reais (ACC 400 Hz, AMS2, EVO)**; o ajuste fino de FFB em pista depende do encoder magnético. Veja o [Roadmap](#roadmap).

## Recursos

**App (DriveLab Studio)**
- Interface limpa e moderna com os módulos **Base do Volante**, **Pedais**, **Freio de mão** e **Volante** (aro/LEDs).
- **Perfis nomeados por módulo** — salvar, aplicar, renomear e excluir perfis (ex.: "GT3", "Chuva") na base, pedais, freio de mão e volante; selecionar um perfil grava no controlador, e o *Salvar* só habilita quando a configuração atual difere do perfil carregado.
- **LEDs do volante** — cores por botão + brilho global; o aro **guarda as cores na flash** (acende sozinho depois de religar) e o app **lê as cores de volta** da placa ao conectar.
- **Ajustes** ao vivo agrupados em abas (Básico / Avançado / **Feel** / Hardware) — força total, damper, mola, batente, **ganhos de FFB por efeito** (mola/damper/atrito/inércia), limites de torque e potência, configuração do encoder, malha de corrente, etc. Carrega ao conectar, salva ao alterar.
- **Monitor de telemetria** na aba Hardware: tensão do barramento + temperaturas FET/motor/MCU + corrente do motor, com limiares ok/alerta/crítico.
- **Três tipos de encoder** — **quadratura** incremental (Omron E6B2, o sensor atual da bancada) ou **magnético** absoluto (MT6701 como padrão planejado · AS5047P planejado). O absoluto mantém o zero mesmo desligando. *(Os drivers magnéticos chegam no Stage 1.)*
- **Modo simulador** — um volante virtual com física real, para desenvolver e testar toda a interface sem hardware nenhum.
- Bilíngue (Português / Inglês), detectado automaticamente pelo sistema.

**Firmware**
- Se apresenta como **volante FFB DirectInput** — os jogos mandam force feedback pra ele igualzinho a qualquer volante comercial, sem plugin.
- **Controle orientado a campo** do motor, construído sobre o firmware do ODrive.
- Segurança em múltiplos estágios: chopper do resistor de freio, limites de corrente e torque, batente, corte por sobretensão, mais **contator off-state e botão soft-power (opt-in)** — base testada no host.
- Firmwares companheiros para os módulos de **pedais** e **freio de mão** (RP2040 + célula de carga).

## Hardware — base do volante (lista de materiais)

Esta é a **base** (o wheelbase direct-drive). Cada outro módulo é um dispositivo USB independente, com a **sua própria** lista de materiais — veja a tabela por módulo abaixo.

| Peça | Observações |
|------|-------------|
| **Placa classe ODrive v3.6** (STM32F405) — validada: **MKS ODRIVE-S V3.6-S6V** | Qualquer placa F405 classe ODrive — veja [Placa base](#-placa-base). **As placas MKS aceitam de 12 a 56 V**, então a fonte é escolha sua dentro dessa faixa. **As ODESC vêm em duas variantes, 8–24 V e 8–56 V** — confira no adesivo `QC PASS` (`24V` / `56V`) ou na cor do LED (roxo / verde) e fique dentro dela. Em qualquer caso, uma fonte mais baixa dá folga extra contra os picos de tensão da frenagem regenerativa. Placas fora do layout ODrive v3.6 podem exigir remapear pinos. |
| **Motor de roda de hoverboard** | O atuador direct-drive. |
| **Encoder** | Omron E6B2-CWZ6C incremental **ou** magnético absoluto AS5047P/MT6701 — sua escolha. |
| **Resistor de freio 2 Ω / 100 W** | **Obrigatório** antes da malha fechada — dissipa a energia da frenagem regenerativa para ela não destruir os capacitores. |
| **Fonte** | Fique dentro do que a sua placa aceita: **12 a 56 V** numa placa MKS; numa ODESC, **8–24 V** ou **8–56 V**, conforme a variante. Exemplo: 24 V / 30 A (720 W). |
| ST-Link V2 — **opcional** | **Não é preciso ter um para montar um volante.** O STM32F405 traz um bootloader gravado em ROM de fábrica, e o firmware entra pelo mesmo cabo USB de dados — inclusive na primeira gravação, numa placa de fábrica (põe em DFU à mão e o DriveLab Studio grava). O ST-Link é ferramenta de bancada: ele lê a placa ao vivo enquanto ela roda, que foi como os números deste projeto foram medidos. Compre se pretende depurar firmware, não para montar. |

## Hardware — por módulo

Cada dispositivo é independente (placa e USB próprios). Lista completa de peças + fiação e pinagem no README de cada módulo:

| Módulo | Hardware principal | Lista completa |
|--------|--------------------|----------------|
| **Base do volante** | Placa F405 + motor + encoder + resistor de freio + fonte (tabela acima) | esta página |
| **Pedais** | RP2040-Zero + 3 sensores (pot / Hall / célula + HX711 ou amplificador) | **[firmware-pedal »](firmware-pedal/README.md#lista-de-materiais-pedais)** |
| **Freio de mão** | RP2040-Zero + 1 sensor (pot / Hall / célula + HX711 ou amplificador) | **[firmware-handbrake »](firmware-handbrake/README.md#lista-de-materiais-freio-de-mão)** |
| **Volante (aro)** | RP2040-Zero + 2× MCP23017 + 5 encoders + LEDs SK6812 | **[firmware-wheel »](firmware-wheel/README.md#-português)** |

## Como o force feedback funciona

O jogo **não** manda telemetria — ele manda a **força já calculada**:

```
Física do jogo (ACC/iRacing)  →  um valor de torque pro volante  (~360–400 Hz)
        ↓  DirectInput / HID PID  (Windows)
        ↓  USB
Firmware (parser HID PID do TinyUSB → FfbEngine.step)  →  torque
        ↓  FOC (ODrive)
Torque no motor  →  você sente
```

Os efeitos de condição (mola/damper) são calculados dentro do dispositivo a partir da posição e da velocidade do **encoder**; os seus ajustes no Studio (ganho, damper, filtros) moldam o resultado antes de ele chegar ao motor.

## Estrutura do repositório

```
app/                 DriveLab Studio (.NET 8 / Avalonia) + Core, Hid, Simulator, testes
firmware-base/       Firmware da base — classe ODrive v3.6 / STM32F405, o motor de FFB  [MIT]
firmware-pedal/      Firmware dos pedais — RP2040 + célula de carga                     [MIT]
firmware-handbrake/  Firmware do freio de mão — RP2040 + célula de carga                [MIT]
firmware-wheel/      Firmware do aro — RP2040 (Waveshare Zero): gamepad + LEDs WS2812   [MIT]
tools/HidDump/       Ferramenta de debug do protocolo HID
docs/                Guias, specs de design e planos de implementação
```

## Primeiros passos

**Rodar o app (com o simulador — sem hardware):**

```bash
# precisa do SDK do .NET 8
cd app
dotnet run --project DriveLab.Studio -- --simulator
```

**Build e testes:**

```bash
./scripts/build.sh    # ou scripts/build.ps1 no Windows
./scripts/test.sh     # testes do app + testes de host do firmware + o check de settings órfãos
```

**Gerar o executável Windows** (self-contained, arquivo único, sem precisar de .NET na máquina de destino):

```bash
./scripts/publish-win.sh   # ou scripts/publish-win.ps1 no Windows
# saída: dist/win-x64/DriveLab.Studio.exe
```

**Gravar o firmware** (precisa do [PlatformIO](https://platformio.org)): abra `firmware-base/` e comece pelo marco **M0** (só serial, sem motor) — veja `firmware-base/README.md`.

## Roadmap

`M0` ✅ → `M0.5` ✅ USB FFB → `M1`–`M2` motor + encoder + malha fechada ✅ → `M2.5` telemetria ✅ → `M3` app↔firmware ✅ → `M4` ajustes ✅ → `M5` força de FFB → motor ✅ *(firmware; testado na bancada)* → `M6` efeitos de jogo 🔧 *(firmware pronto; validação em pista depende do encoder magnético)* → `M7` validação no simulador ⏳.

O chopper do resistor de freio, o contator off-state e o soft-power estão implementados como **base opt-in**. Detalhes em `docs/` — comece por **[how-it-works.md](docs/how-it-works.md)**.

## ⚠️ Segurança

- **Saiba o que a sua placa aceita antes de ligar uma fonte.** **As placas MKS aceitam de 12 a 56 V.** **As ODESC vêm em duas variantes, 8–24 V e 8–56 V.** Dá pra diferenciar olhando a placa, sem datasheet: o **adesivo `QC PASS`** diz `24V` ou `56V`, e o **LED de status é roxo na placa de 24 V e verde na de 56 V**. Não se guie pelos capacitores — as duas variantes usam os de 63 V. **Nunca ultrapasse o limite da SUA placa**, e lembre que os picos da frenagem regenerativa jogam o barramento acima da tensão da fonte, então uma fonte mais baixa é a escolha mais segura.
- O **resistor de freio de 2 Ω é obrigatório** antes de qualquer torque em malha fechada; a frenagem regenerativa devolve energia ao barramento e, sem ele, destrói os capacitores.
- `M0` e `M0.5` rodam **sem motor conectado**. Suba a corrente aos poucos. Um volante direct-drive tem torque de sobra pra machucar o seu pulso — mantenha uma parada de emergência (a tomada) ao alcance.

## Licença

**Tudo — app, bibliotecas, ferramentas e todo o firmware** (base + pedais/freio/aro): [MIT](https://opensource.org/licenses/MIT).

Todo arquivo-fonte traz um cabeçalho declarando a sua licença.

## Comunidade e contribuição

Dúvidas, relatos de montagem, ajuda pra pôr a sua placa pra rodar — **entre no Discord**: **https://discord.gg/Xp2pGm5wj**

Issues e pull requests são bem-vindos. Arquivos novos devem incluir o cabeçalho padrão do DriveLab.
