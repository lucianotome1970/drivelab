# DriveLab Firmware — Handbrake (RP2040)

Firmware for the **DriveLab handbrake** — **Waveshare RP2040-Zero** board (RP2040, USB-C).

<p align="center"><a href="README.pt.md">🇧🇷 Leia em português</a></p>

---

> **Status: ✅ validated on hardware** (Waveshare RP2040-Zero, July 2026) — **with one caveat: never tested with a physical sensor wired.** The board was validated bare. Enumeration, the app connection, settings read/write and flash persistence are all confirmed; anything that needs a real sensor is still open. See [What is still untested](#what-is-still-untested).

Works the same on any RP2040 — for a stock Pico, change `board = pico` in `platformio.ini`. The Zero's onboard LED is WS2812 on GP16; this firmware does not use it.

Stack: RP2040 + **arduino-pico** (Earle Philhower's core) + **Adafruit_TinyUSB**. MIT.

## What a handbrake is here

A single axis (potentiometer, Hall sensor or load cell — through an HX711 or an instrumentation amplifier) plus one digital button. **The button is not a physical button** — the firmware derives it from the axis: cross a threshold and it presses, drop below it and it releases. That means your lever needs no extra switch and no extra wiring.

It is `firmware-pedal` reduced from three axes to one, speaking the same P0 protocol the app already knows.

## What it does

- **HID joystick** (report `0x01`): 1 axis `Rx` (16-bit field, values 0..4095) + 1 button + 7 bits of padding.
- **Vendor P0** (usage page `0xFF00`, identical to the pedal):
  - `0x20` telemetry (~100 Hz) — the axis rides in the **Clutch** slot (raw u16 LE + output u16 LE); the Brake and Throttle slots are zeroed; `Flags` bit 0 is the button state.
  - `0x14` write / `0x15` read request / `0x16` value — fields 0–13 are the same as the pedal (sensor, min, max, invert, smoothing, 6-point curve, load-cell scale, deadzone), plus **14 = ButtonThreshold** and **15 = ButtonEnabled**. The index byte on the wire is accepted and ignored, since there is only one axis.
  - `0x02` command — calibrate start/stop, save to flash, load defaults.
- **Sensor:** ADC on `A0`/`GP26` for potentiometer, Hall and analog load cell (`sensorType` 0, 1 and 3), or HX711 (DT `GP2`, SCK `GP3`) when `sensorType == 2`. The read never blocks the loop. The ADC path is oversampled, and the analog load cell is also tared on boot — measured force has a zero that drifts, position does not.
- **Pipeline:** normalize (min/max) → invert → deadzone → 6-point curve → smoothing → clamp. Same maths as the pedal.
- **Button with hysteresis:** presses at `buttonThreshold`, releases 3 points below it. The gap is what stops it flickering when you hold the lever right at the threshold.
- **Flash persistence** (emulated EEPROM, magic `"DLH1"`): the whole config including the button settings.

## Bill of materials

| Qty | Part | Notes |
|----:|------|-------|
| 1 | **Waveshare RP2040-Zero** | RP2040 + USB-C. Any RP2040 works (`board = pico`). |
| 1 | **USB-C cable** | to the PC. |
| 1 | **Sensor** — your choice: **10 kΩ pot**, **analog Hall**, or **load cell** | pot and Hall on **`A0` = `GP26`**; a load cell needs an amplifier — HX711 (**DT `GP2`, SCK `GP3`**) or an instrumentation amp, whose output goes on **`A0`** itself. |
| 0–1 | **Load-cell amplifier** — **HX711** or **instrumentation amplifier** (INA333 / CJMCU-333) | only if you use a load cell. The HX711 delivers 10 or 80 readings per second; the instrumentation amp uses the board's ADC and has no such limit. Power either one at **3.3 V, never 5 V**. |
| — | Lever mechanics and spring | no extra button needed — it is derived in firmware. |

## Build & flash

Needs [PlatformIO](https://platformio.org).

```bash
cd firmware-handbrake
pio run -e rp2040_zero              # build
pio run -e rp2040_zero -t upload    # flash
```

To flash, hold **BOOT** and tap **RESET** (or hold BOOT while plugging the USB-C in) — the **RPI-RP2** drive appears and PlatformIO copies the `.uf2`.

The build produces a ~207 KB `.uf2` using about 4% of the RP2040's flash and 7% of its RAM. There is a lot of room left.

## What is still untested

Everything below needs a **physical sensor** connected, which never happened on the bench:

- Moving the sensor and seeing the axis travel 0..100%.
- The button pressing at `buttonThreshold` (70% by default) and releasing 3% below it, without flickering.
- Calibration capturing the sensor's real minimum and maximum.

Also worth knowing: with no HX711 connected, the driver may never report ready. The firmware does not hang — the raw value simply stays at zero — but that path was not observed on real hardware either.

## Implementation note — the single HID endpoint

Same constraint as the pedal firmware, and the same fix: TinyUSB has one HID endpoint here and drops the second of two back-to-back reports. So the vendor payload is **63 bytes**, and the `0x16` reply is **sent from `loop()` with priority over the joystick**, never straight from the callback. Validated on the wire: read `Smooth=0` → write 42 → read 42.

---

The wheelbase firmware is a different beast — STM32F405, in [`../firmware-base/`](../firmware-base/README.md). Pedals are in [`../firmware-pedal/`](../firmware-pedal/README.md). The full USB-HID contract is in [`../docs/PROTOCOL.md`](../docs/PROTOCOL.md).
