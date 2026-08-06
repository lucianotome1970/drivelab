# DriveLab Firmware — Wheel rim (RP2040)

Firmware for the **DriveLab rim** — the wheel face with buttons, paddles and LEDs. **Waveshare RP2040-Zero** board, its own USB HID device (`0x1209:0x0004`), enumerating as **"DriveLab Wheel"**.

<p align="center"><a href="README.pt.md">🇧🇷 Leia em português</a></p>

---

> **Status (July 2026): partially validated.** Enumeration and the vendor P0 channel are **confirmed on real hardware** — it appears as "DriveLab Wheel", the joystick streams, and settings read/write works on the wire. **Not yet validated:** the MCP23017 button reads, the encoders, the clutch ADC and the WS2812 LEDs — nothing was wired to the board on the bench. On the app side, a `HidWheelTransport` does not exist yet, so DriveLab Studio cannot drive a real rim until that is added.

Stack: RP2040 + **arduino-pico** (Philhower) + **Adafruit_TinyUSB** + **Adafruit_NeoPixel**. MIT.

## Two HID channels

1. **Gamepad** (report `0x01`) — 32 buttons + 2 axes (the clutch paddles). This is what games read.
2. **Vendor P0** (64 bytes) — `WheelState 0x21` telemetry, `WheelLed 0x18` colors, settings `0x14`/`0x15`/`0x16`, `Command 0x02`. This is what DriveLab Studio uses.

The rim **owns its own colors**: they are saved in flash (magic `"DLW2"`) alongside the paddle calibration, so it lights up on its own at power-on with no app running. The app reads them back on connect via `RequestLeds` → `LedValue 0x19`.

## What the rim has

10 push buttons (each RGB-lit), 5 rotary encoders with push, 4 paddles (2 clutch + 2 shift), a D-pad, and an LED bar for rev lights.

The 5 encoders already eat 10 GPIOs on an RP2040-Zero, so the ~21 slow buttons go onto **two MCP23017 I²C expanders** — 32 inputs for the price of 2 pins.

## Pin map

Tunable at the top of `main.cpp`.

| What | Pins | Notes |
|---|---|---|
| **I²C** (both MCP23017) | SDA `GP0`, SCL `GP1` | addresses `0x20` and `0x21` |
| **MCP #0** (16 inputs) | — | 10 push buttons → bits 0–9 · gear down/up → bits 10–11 · D-pad → bits 12–15 |
| **MCP #1** (5 used) | — | the 5 encoder pushes → bits 16–20 |
| **Encoders A/B** | `GP2/3`, `GP4/5`, `GP6/7`, `GP8/9`, `GP10/11` | CW → bits 21–25, CCW → bits 26–30 (momentary) |
| **Clutch paddles** | `GP26`, `GP27` | analog — progressive clutch + bite point |
| **WS2812 data** | `GP28` | one chain: pixels 0–9 are the button LEDs, then the LED bar |

> ⚠️ **Power the MCP23017 expanders at 3.3 V**, from the RP2040's `3V3` pin — **not 5 V**. The RP2040's GPIOs are not 5 V-tolerant, and 5 V on SDA/SCL will damage them. Buttons wire pin → `GND` and use the internal pull-ups.

The gamepad report carries 32 buttons (31 used, one spare) plus the 2 clutch axes. Games read that directly; the app drives the RGB over the P0 `WheelLed` channel.

**Wiring diagram** — every part drawn individually, with each encoder's A/B wires:

![DriveLab wheel pictorial wiring diagram](docs/wiring-pictorial.svg)

*Interactive, theme-aware version: [`docs/wiring-pictorial.html`](docs/wiring-pictorial.html) (open locally).*

<details><summary>Block/bus view (compact overview)</summary>

![DriveLab wheel wiring diagram](docs/wiring.svg)

*Interactive: [`docs/wiring.html`](docs/wiring.html). The exact per-pin detail is the table above.*

</details>

## Bill of materials

| Qty | Part | Notes |
|----:|------|-------|
| 1 | **Waveshare RP2040-Zero** | the rim's MCU (USB-C, tiny). |
| 2 | **MCP23017 I²C expander board** | addresses `0x20` + `0x21` (set A0/A1/A2). **Power at 3.3 V.** |
| 10 | **SK6812** (e.g. SK6812-E, reverse-mount) | one RGB LED per button, behind a translucent momentary cap (~15–16 mm). |
| ~8–16 | **WS2812/SK6812** | the LED bar (rev lights), chained after the buttons. |
| 5 | **rotary encoder** (with push) | A/B on `GP2`–`GP11`; push on MCP #1. |
| 2 | **pot or Hall sensor** | clutch paddles → `GP26` / `GP27`. |
| 10+ | **momentary buttons** | 10 push + 2 gear + D-pad, into the MCP23017s. |
| 1 | **330–470 Ω resistor** | in series on the WS2812 data line. |
| 1 | **PTC ~2–2.5 A** + **1000 µF cap** | on the `5V_LED` rail — see below. |

> Only for a **full (RGB) rim**. A simple rim with no LEDs skips the SK6812s, the LED bar, the PTC and the cap.

## Powering the rim — read this before wiring the quick release

You can build the rim at two levels, and the choice changes what has to cross the rotating joint.

**Simple rim (no LEDs)** — buttons, encoders and clutch paddles only. The RP2040 and its inputs draw a few tens of milliamps, so the whole rim runs straight off USB `VBUS`. Across the joint you need only the **4 USB wires**: `VBUS`, `D+`, `D−`, `GND`.

**Full rim (RGB buttons + LED bar)** — the WS2812s can pull **~1.5 A** (26 LEDs at full white), far beyond any USB port's budget (0.5 A on USB 2.0, 0.9 A on USB 3). **Do not power the LEDs from USB `VBUS`.** That limit is the same whether the rim plugs into the PC directly or through the base. Feed them from a **dedicated 5 V rail off the base's own power supply** — a buck converter from the 24/56 V bus. The base has the power budget; USB does not.

**Routing through the base (recommended).** The base holds a small USB hub, so the wheelbase and the rim share one cable to the PC, plus a 5 V buck off the main PSU. Because the RP2040 lives in the rim, only these cross the quick release and the slip ring:

| Signal | Source | Notes |
|---|---|---|
| `D+`, `D−`, `GND` | hub | USB data — 12 Mb/s full speed, tolerant of a decent slip ring |
| `VBUS (5V)` | hub | powers **only the RP2040 logic** (tens of mA) |
| `5V_LED`, `GND` | base 5 V buck | powers **only the WS2812s** — size the conductor for ~2 A |

Keep logic on USB `VBUS` and LEDs on the base's 5 V, and **never tie the two 5 V rails together** — that is two sources fighting each other. They share `GND` only.

**Protections for a full rim:**

- **Common ground** between USB `GND` and the base's 5 V `GND` — mandatory. It is both the data reference and the LEDs' return path.
- **Resettable fuse (PTC ~2–2.5 A)** on the `5V_LED` rail — covers a shorted LED or a slip-ring fault.
- **Bulk capacitor ~1000 µF** across 5 V/`GND` right next to the WS2812s, on the rim — absorbs inrush and spikes.
- **330–470 Ω series resistor** on the WS2812 **data** line, at the first pixel — damps ringing.
- **Level note:** the RP2040 drives the data line at 3.3 V. That usually works, but a 3.3→5 V level shifter is more reliable on long chains.
- **Firmware safety net:** the `ledBrightness` setting caps worst-case current even if something requests full white.
- **Slip ring:** gold contacts, power pair kept away from the data pair, short USB run between slip ring and hub, and never hot-swap the quick release with the LEDs under load.

## Build & flash

Needs [PlatformIO](https://platformio.org). Two environments are defined — `rp2040_zero` (the target board) and `pico`.

```bash
cd firmware-wheel
pio run                             # builds both environments
pio run -e rp2040_zero -t upload    # flash
```

Hold **BOOT** and tap **RESET** to get the UF2 bootloader, then upload.

Check it worked: `joy.cpl` on Windows shows **"DriveLab Wheel"** with 32 buttons and 2 axes.

## Implementation note — the single HID endpoint

Same constraint as the pedal and handbrake firmwares: TinyUSB has one HID endpoint and drops the second of two back-to-back reports. So the vendor payload is **63 bytes**, and the `0x16` reply is **queued in `onSetReport` and sent from `loop()` with priority over the gamepad**, never straight from the callback. Confirmed on the wire here (read brightness 128 → write 42 → read 42).

---

The wheelbase firmware is a different beast — STM32F405, in [`../firmware-base/`](../firmware-base/README.md). The full USB-HID contract is in [`../docs/PROTOCOL.md`](../docs/PROTOCOL.md).
