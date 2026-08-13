# DriveLab Firmware — Pedals (RP2040)

Firmware for the **DriveLab pedal set** — **Waveshare RP2040-Zero** board (RP2040, USB-C), the device side of the **P0** contract.

<p align="center"><a href="README.pt.md">🇧🇷 Leia em português</a></p>

---

> **Status: ✅ validated on hardware** (Waveshare RP2040-Zero, July 2026). Enumerates as **"DriveLab Pedal"** (VID `0x1209` / PID `0x0002`), confirmed on macOS and Windows. Settings write and read-back both work, and the configuration survives a power cycle.

Works the same on any RP2040 — for a stock Pico, change `board = pico` in `platformio.ini`. The Zero's onboard LED is WS2812 on GP16; this firmware does not use it.

Stack: RP2040 + **arduino-pico** (Earle Philhower's core) + **Adafruit_TinyUSB**. MIT — the TinyUSB dependency is MIT too.

## What it does

- **HID joystick**, 3 axes at 12 bits, fed by the signal pipeline: normalize → deadzone → curve → smoothing.
- **Vendor P0 channel** for the app: telemetry `0x20`, `SettingWrite 0x14`, `ReadRequest 0x15`, `SettingValue 0x16`, `Command 0x02`. Min/max calibration included.
- **Three sensor types per pedal**, chosen in the app: potentiometer, analog Hall, or load cell.
- **Load cell** through an HX711 (`sensor_type == 2`), read without blocking the loop, tared on boot.
- **Persistent config in flash** (emulated EEPROM, magic `"DLP1"`): the settings are stored **on the device**, so they survive unplugging and the app loads them on connect.

## Wiring

**Potentiometer or Hall** — ends to `3V3` and `GND`, wiper to the ADC:

| Pedal | ADC pin |
|---|---|
| Clutch | `A0` = `GP26` |
| Brake | `A1` = `GP27` |
| Throttle | `A2` = `GP28` |

**Load cell (HX711)** — one amplifier per pedal:

| Pedal | DT | SCK |
|---|---|---|
| Clutch | `GP2` | `GP3` |
| Brake | `GP4` | `GP5` |
| Throttle | `GP6` | `GP7` |

With nothing wired, the ADC inputs float and the axes read noise. That is normal, not a fault.

## Bill of materials

| Qty | Part | Notes |
|----:|------|-------|
| 1 | **Waveshare RP2040-Zero** | RP2040 + USB-C. Any RP2040 works (`board = pico` in `platformio.ini`). |
| 1 | **USB-C cable** | to the PC. |
| 3 | **One sensor per pedal** — your choice: **10 kΩ linear pot**, **analog Hall** (SS49E / A1302), or **load cell + HX711** | pot and Hall go on the ADC; a load cell needs the HX711 amplifier. The type is set per pedal in the app. |
| 0–3 | **HX711 amplifier** | one per load-cell pedal. |
| — | Wires, pedal frame and springs | the mechanical rig is yours. |

## Build & flash

Needs [PlatformIO](https://platformio.org) — it downloads the arduino-pico toolchain by itself on the first build.

```bash
cd firmware-pedal
pio run                      # build
pio run -t upload            # flash
```

To flash, put the board in its UF2 bootloader first: hold **BOOT** and tap **RESET**, or hold **BOOT** while plugging the USB-C in. The **RPI-RP2** drive appears and PlatformIO copies the `.uf2` across.

Check it worked: on Windows, `Win+R` → `joy.cpl` → **"DriveLab Pedal"** with 3 axes (Rx/Ry/Rz). Move a sensor and the matching axis sweeps.

## Troubleshooting

- **Build fails on `Adafruit_TinyUSB.h`** — check that `build_flags = -DUSE_TINYUSB` is in `platformio.ini`. That flag is what activates the TinyUSB stack in Philhower's core, and it is the number one cause on a first build.
- **The device doesn't appear, or has the wrong name** — check the `board_build.arduino.earlephilhower.usb_*` keys (manufacturer / product / vid / pid). Windows caches names by VID/PID; if you change them, re-plug on a different port.
- **The RPI-RP2 drive doesn't appear** — hold **BOOT** and tap **RESET**, or hold BOOT while plugging in.
- **An axis is stuck at maximum** — the ADC is floating with no sensor wired, or the sensor is on the wrong pin.

## Implementation note — the single HID endpoint

TinyUSB gives this board **one** HID endpoint, and it drops the second report if two are sent back to back. Two consequences are baked into the firmware, and anyone porting this should keep them:

- The vendor payload is **63 bytes, not 64** — 63 payload + 1 report id has to fit in `CFG_TUD_HID_EP_BUFSIZE`.
- The `0x16` reply is **queued and sent from `loop()` with priority over the joystick**, never straight from the callback. Sent from the callback, it lands right behind a joystick report and disappears.

Both are why settings read-back works. The same fix is applied in `firmware-handbrake` and `firmware-wheel`.

---

The wheelbase firmware is a different beast — STM32F405, in [`../firmware-base/`](../firmware-base/README.md). The full USB-HID contract is in [`../docs/PROTOCOL.md`](../docs/PROTOCOL.md).
