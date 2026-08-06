# firmware-base — Direct-Drive wheelbase

Firmware for the **direct-drive base** — the FFB motor stage. Runs on **STM32F405 ODrive v3.6-class** boards, validated on an **MKS ODRIVE-S V3.6-S6V**.

<p align="center"><a href="README.pt.md">🇧🇷 Leia em português</a></p>

---

The game sees the base as a DirectInput wheel with force feedback: no plugin, no extra driver.

## Architecture

Rather than writing field-oriented control from scratch, this firmware builds on two proven foundations and keeps the force-feedback layer as ours:

| Layer | Source | License |
|---|---|---|
| FOC, motor control, calibration | **ODrive v0.5.6**, vendored in `vendor/odrive-fw` | MIT |
| USB stack | **TinyUSB**, vendored in `vendor/tinyusb` | MIT / Apache |
| ODrive bridge, FFB task, USB descriptors, persistence | **ours** (`src/`, `inc/`) | MIT |
| FFB effects (spring, damper, friction, inertia) | **ours** (`engine.step`) | MIT |
| Configuration and telemetry | **our** DriveLab Studio app | MIT |

No GPL code enters the binary — the whole project is MIT, and every source file carries a header saying so.

## Status (2026-08-06)

**Working and validated on the bench:**

- Motor running under FOC, arming repeatably.
- Force feedback validated in-game: **two full laps in Assetto Corsa Competizione**, plus AMS2 and EVO.
- Settings persisted to the board's own flash (`CMD_SAVE`) — the base is the source of truth, not the app.
- Live telemetry: bus voltage, motor current, MCU temperature.
- Protections: brake-resistor chopper, over-current cutoff in the ISR, over-temperature check before arming, soft stop, under- and over-voltage trips.

**Pending:** fine FFB tuning on track is waiting on the magnetic encoder. The current incremental encoder does not keep the centre between power cycles, which limits how far the calibration can go. See [`../docs/encoders.md`](../docs/encoders.md).

## Build

Needs **ARM GCC** (`arm-none-eabi-`) and Python with `pyyaml`, `jinja2` and `jsonschema` — ODrive generates code at build time.

The PlatformIO toolchain works and is what we use, so the build comes out identical on macOS and Windows:

```bash
export PATH="$HOME/.platformio/packages/toolchain-gccarmnoneeabi/bin:$PATH"
cd firmware-base
make -j8
```

Output lands in `build/`: `drivelab-base.elf`, `.bin` and `.hex`.

## Flashing

**Over DFU (USB, no programmer):** this is the normal path, and DriveLab Studio does it for you. If the board will not enter DFU, see the [FAQ](../docs/faq-hoverboard.md#cant-flash-the-firmware) — on many boards the manual method is mandatory, and on some the jumper has to be **removed**, not fitted.

**Over ST-Link (SWD):**

```bash
openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
        -c "program build/drivelab-base.elf verify reset exit"
```

> ⚠️ Do not halt the core over SWD while the motor is **armed** — that drops the motor mid-control. To watch the firmware run, use the USB serial (CDC) instead.

## Safety

Read [`../docs/faq-hoverboard.md#safety`](../docs/faq-hoverboard.md#safety) before powering a motor. The two that cost the most:

- **Match the supply voltage to your board variant** (24 V or 56 V). Going over destroys the board without warning.
- **A brake resistor is mandatory** before any closed-loop torque.
