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
  <a href="README.pt.md">🇧🇷 Leia em português</a> &nbsp;·&nbsp; <a href="#-download">⬇️ Download</a> &nbsp;·&nbsp; <a href="https://discord.gg/Xp2pGm5wj">💬 Discord</a>
</p>

---

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
