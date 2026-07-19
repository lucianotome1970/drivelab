# DriveLab Firmware — Base / Wheelbase (Trilho B)

Firmware for the DriveLab **base** (wheelbase) — **ODESC v4.2 (STM32F405)** + direct-drive motor.
*(The firmware for the removable rim/wheel — buttons, LEDs, paddles — lives in `firmware-wheel/` (RP2040).)*
Design/decisions: kept in internal project notes (not versioned in the public repo).

> **License:** M0.5 v2 dropped the LGPL shim libs (`USBLibrarySTM32` + `ArduinoJoystickWithFFBLibrary`) in favour of the MIT **Adafruit TinyUSB Library** plus our own HID PID descriptor (derived from **OpenFFBoard**, MIT, `github.com/Ultrawipf/OpenFFBoard`). So the firmware stays **MIT**, same as the DriveLab Studio app and the .NET libs — no LGPL bump needed after all.

<p align="center"><a href="#-english">🇬🇧 English</a> &nbsp;·&nbsp; <a href="#-português">🇧🇷 Português</a></p>

---

## 🇬🇧 English

### Current milestone: M0 — bring-up (serial only, NO motor)

**Goal:** prove toolchain + flashing + USB serial on your ODESC. Harmless (does not touch the motor/power).

> **Status (2026-07-18): M0 FULLY VALIDATED on real hardware ✅** — an **MKS ODRIVE-S V3.6-S6V** (STM32F405), flashed over **ST-Link + OpenOCD** (recipe below). Execution confirmed over SWD *and* the **USB CDC serial streams** `DriveLab M0 vivo, tick=…` (enumerates as `GENERIC_F405RGTX CDC in FS Mode`). The 8 MHz HSE / 48 MHz USB clock is correct — **this de-risks the whole USB/FFB path (M0.5+)**.
>
> *Board caveat:* on this unit the **USB power input (VBUS/5V) is dead** (plugging only the board's USB doesn't light the PWR LED), but the **D+/D‑ data lines are fine** — it enumerates normally when the logic is powered another way (ST-Link 3.3V, which *does* light the LED). Tip: put the ST-Link on a **USB charger** just for power (it still outputs 3.3V without a data host), freeing the laptop's only USB port for the board's data cable.

#### Prerequisites
- **VS Code + PlatformIO extension** (installs the STM32duino toolchain by itself on the first build).
- **ODESC v4.2** + **USB** cable (micro-USB).
- To flash, **one** of the two:
  - **ST-Link V2** (recommended) connected to the ODESC **SWD** header: `SWDIO`, `SWCLK`, `GND`, `3V3`.
  - **DFU** (no ST-Link): put the board in DFU mode (BOOT0 high at reset — BOOT pad/button) and use `upload_protocol = dfu` in `platformio.ini`.
- Power: **no motor needed**. USB/ST-Link already powers the logic. (Do not connect the motor on M0.)

#### Wiring the ST-Link V2 (SWD) — only 4 signals

Connect **by the printed label**, not by pin position (clones vary a lot). On the aluminium ST-Link V2 dongle the names are printed on the case (two rows of 5 pins); on the board look for a 4-pad **SWD** header near the STM32 (or a JTAG connector).

| ST-Link V2 (labelled pin) | Board (labelled pad) | Required? |
|---|---|---|
| **SWDIO** | SWDIO / **DIO** | ✅ yes |
| **SWCLK** | SWCLK / **CLK** | ✅ yes |
| **GND**   | **GND**         | ✅ yes |
| **3.3V** (3V3) | 3V3 | ⚠️ conditional (see below) |

- **Board powered by its own USB (recommended):** wire only **SWDIO + SWCLK + GND** — do **not** connect 3.3V (it can fight the on-board regulator).
- **Board with no USB (ST-Link only):** also connect **3.3V** to power the logic.
- **GND is always required** — it is the common reference for the signals.
- Ignore the `SWIM` / `5.0V` pins — we don't use them.
- **Optional NRST:** if `st-info --probe` can't find a "busy" board (still running the factory firmware), also wire **RST/NRST ↔ RST/NRST** for connect-under-reset.

Minimum to start (board on its own USB):
```
ST-Link          Board
 SWDIO  ───────►  DIO
 SWCLK  ───────►  CLK
 GND    ───────►  GND
 (3.3V not connected — board already powered by USB)
```

> ⚠️ **Never energise the DC bus (24 V / 56 V) during bring-up.** M0/M0.5 don't touch the motor — the logic is powered by USB or the ST-Link only.

#### Flashing an ODrive-class clone (VALIDATED on an MKS ODRIVE-S V3.6-S6V / STM32F405)

If your board is an ODrive clone (ODESC, **MKS ODRIVE-S**, etc.), two things fight the flasher: the **factory ODrive firmware re-arms a watchdog** that resets the core mid-write, and cheap **ST-Link clones trip `st-flash`'s SRAM loader** (`Flash loader run error`, garbage PC). This is the sequence that worked end-to-end:

1. **Wire the ST-Link `3.3V` pin** — the clone needs it as **VTref**, otherwise `st-info` says `Failed to enter SWD mode`. It also powers the logic (no board USB / DC bus needed for bring-up). Confirm the board's **PWR LED** lights.
2. **Confirm the MCU:** `brew install stlink && st-info --probe --connect-under-reset` → expect `chipid 0x413`, `STM32F4x5_F4x7`, `192 KiB SRAM`.
3. **Erase the factory firmware** (kills the watchdog; `erase` drives the flash controller directly, so it survives the resets — unlike a write): `st-flash --connect-under-reset --flash=1024k erase` → `Mass erase completed`.
4. **Flash with OpenOCD** (its loader works where `st-flash`'s fails; after the erase the core halts cleanly — the `BUG: can't assert SRST` on the HLA transport is harmless): `brew install open-ocd` then
> ```bash
> openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
>   -c "program .pio/build/m0/firmware.elf verify reset exit"
> ```
> → `Verified OK`. **From here just re-run step 4** — our firmware never arms a watchdog or holds SWD, so the fight is one-time.

> **No USB serial to confirm M0?** Prove it runs over SWD, no board USB needed: `arm-none-eabi-nm .pio/build/m0/firmware.elf | grep 'loop::n'` gives the tick address, then read it twice with the core running in between —
> ```bash
> openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
>   -c "init;halt;mdw 0x200010d8;resume;sleep 3000;halt;mdw 0x200010d8;shutdown"
> ```
> the value should increment (~1/s). Reading `uwTick` (HAL ms counter) the same way and seeing ~1 ms/tick also confirms the **HSE crystal is 8 MHz** (so USB will enumerate). *Alternative that sidesteps all of this if the board exposes USB: the **`SW1 → DFU`** switch boots the ST system bootloader (factory firmware never runs) — flash with `dfu-util -a 0 -s 0x08000000:leave -D firmware.bin`.*

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

### M0.5 — USB/FFB (the main de-risk) ✅ v2 VALIDATED on real hardware (Passos A/B/C)

Prove that the base can **enumerate as a Force Feedback device** (DirectInput wheel), so the game-effect pipe exists before any motor is touched. **No motor** in M0.5.

> **v1 (shim, decision B2) FAILED on hardware — replaced by v2 (TinyUSB).** The first attempt reused the AVR-origin `ArduinoJoystickWithFFBLibrary` + `USBLibrarySTM32` shim. It compiled for the F405, but on the bench **no OS enumerated it** — not macOS, not Windows, not even a UTM Windows VM. Root cause, measured with `tools/dump_report_descriptor.py` (pyusb): the shim's EP0 control-transfer plumbing **declared** `wReportDescriptorLength = 1259` bytes but only **transmitted 32** — a truncated HID report descriptor every OS rejects outright. The shim's control-transfer code is only exercised on F401/F411; it doesn't work on the F405.

#### The fix: TinyUSB with our own descriptor
- We moved to the **Adafruit TinyUSB Library** (`lib_deps = adafruit/Adafruit TinyUSB Library` in `env:m05`, MIT-licensed). Why: STM32duino's native HID class is **input-only** (no OUT endpoint, no `SET_REPORT`), so it physically cannot receive FFB effects from the host; TinyUSB's `dwc2`/OTG_FS port is mature, serves large descriptors without truncating, and supports OUT reports.
- **Descriptor:** the full HID PID Force-Feedback report descriptor, **flattened from OpenFFBoard** (`github.com/Ultrawipf/OpenFFBoard`, MIT, same VID family `0x1209`) — 1196 bytes, generated/validated by `tools/flatten_openffb_descriptor.py` + `tools/validate_ffb_descriptor.py`. Credit: OpenFFBoard.
- **Device identity:** composite **HID PID + CDC**, `VID 0x1209` / `PID 0x0001`, product string **"DriveLab Base"**. Set via `build_flags` in `platformio.ini` (`USB_VID`/`USB_PID`/`USB_MANUFACTURER`/`USB_PRODUCT`) — **not** at runtime: the Adafruit `begin()` rebuilds the whole device descriptor from these build-time macros unconditionally, so a runtime `TinyUSBDevice.setID()` call made before `begin()` gets silently discarded (bit us in Passo A v1: it enumerated with the library's default `0x239A/0xCAFE`).

#### Validated status (2026-07-18, MKS ODRIVE-S V3.6 / STM32F405)
- **Passo A — minimal joystick ✅** — enumerates as a HID gamepad: macOS mounts it as a game controller, and Windows `joy.cpl` shows **"DriveLab Base"** with a live moving axis. *(v1/shim never got this far.)*
- **Passo B — full descriptor ✅** — the complete 1196-byte HID PID descriptor is served **intact, no truncation** (confirmed via `dump_report_descriptor.py`, declared == received); the device is still recognized in Windows `joy.cpl`. Note: `joy.cpl` has no dedicated Force-Feedback tab by itself — that's normal, FFB lives in DirectInput, not the applet.
- **Passo C — constant force received ✅** — sending a raw HID output report (`Set Constant Force`, `mag=1000`) from the Mac via `tools/ffb_send_constant_force.py` (hidapi) makes the firmware parse it and log `FFB const block=1 mag=1000` over CDC. Proves the FFB pipe end-to-end (host effect → USB → parser → log) on real hardware, without needing Windows/DirectInput.

#### Still open (future milestone)
The full DirectInput PID init handshake (Pool/Block Load requests, as a real game would drive it) and turning a received effect into motor torque (**M5**, gated behind the brain + safety) are not part of M0.5. No motor is touched here.

#### How to flash M0.5
```bash
pio run -e m05 -t upload      # or select the "m05" env in the PlatformIO bar
```
`env:m05` does **not** use `USBD_USE_CDC` for the joystick path — CDC is composited alongside HID by TinyUSB itself (see `platformio.ini`).

#### Bench tools (`firmware-base/tools/`)
- `validate_ffb_descriptor.py` — parses/validates the HID PID descriptor bytes (self-test, no board needed).
- `flatten_openffb_descriptor.py` — generates the flattened descriptor from the OpenFFBoard source.
- `dump_report_descriptor.py` — reads the config + report descriptor off the real device via pyusb (declared vs. received length — this is what caught the v1 truncation).
- `ffb_send_constant_force.py` — sends a raw HID Set Constant Force output report via hidapi, to prove the FFB pipe without DirectInput/Windows.

*(Flashing the F405/ODrive-class board is documented above under M0 — ST-Link + OpenOCD; same recipe applies here, no need to repeat it.)*

*(The USB IRQ priority below the FOC timer only matters starting at M1, when SimpleFOC comes in.)*

---

### M3 — A0 config channel ✅ VALIDATED on real hardware (bench + app)

**What it is:** the base now speaks a vendor **config channel ("A0")**, the base's equivalent of the pedal's **P0** channel — **DriveLab Studio reads, writes and saves the base's settings**, and sees the base as connected. No motor.

> **Status (2026-07-19): M3 VALIDATED on real hardware ✅** — an **MKS ODRIVE-S / STM32F405**. On-bench via `hidapi`: read `TotalStrength` (default) → write `55` → re-read `55`; `SaveSettings` + power-cycle → `55` still there; `DeviceState` (0x21) telemetry streaming. **And in DriveLab Studio on macOS**: the base connects, the "Base do Volante" ("Wheelbase") screen loads all fields, an edit + **"Save to controller"** persists (confirmed after re-opening/power-cycle).

#### USB shape: one HID interface, two vendor collections
The STM32F405 OTG_FS core has only **~3 usable IN endpoints**, so the device can't do 2× HID + CDC (one HID for FFB, one for A0, plus CDC). Instead, **A0 shares the single HID interface with the FFB** — one combined report descriptor — plus **CDC**, kept for debug logging. The A0 reports live in a vendor collection (usage page `0xFF00`) with **report IDs remapped** so they don't collide with the FFB's: `DeviceState 0x21`, `Command 0x22`, `DirectControl 0x10`, `SettingWrite 0x14`, `SettingReadRequest 0x15`, `SettingValue 0x16`.

#### Settings model
A `BaseCfg` struct mirrors the app's frozen `BaseSettingId` enum — **19 fields** (TotalStrength, SoftStop\*, Spring/Damper, encoder, current-loop gains, etc.). Settings persist to **flash** (STM32duino EEPROM emulation, magic `"DLB1"`): `SaveSettings` writes them, and they survive a power-cycle (validated above).

#### Telemetry
The base pushes a periodic `DeviceState` (`0x21`) report carrying the firmware version. **Sensor fields (bus voltage, current, temperatures) are placeholder `0` until M1** — they need the power stage, which isn't wired up yet.

#### Two macOS/HidSharp gotchas
- The combined-interface base enumerates with **device-level usage `0x00`** (not the FFB's joystick usage), so the app detects it by **VID/PID**, not by usage-page.
- The firmware keeps a **single pending-read slot**, so the app must **serialize setting reads** — one `0x15` (SettingReadRequest) → `0x16` (SettingValue) round-trip at a time, not fired concurrently.

#### Still open (future milestones)
Real sensor telemetry and turning settings into torque land with **M1/M5**. Firmware update over USB (DFU) is now implemented and validated — see the dedicated section below.

#### Bench tool (`firmware-base/tools/`)
- `a0_config.py` — reads/writes a single setting over the A0 channel (hidapi), used to validate the read/write/persist flow above.

---

### Firmware update over USB (DFU) — working end-to-end via app + manual SW1 fallback

**What it is:** the base can be re-flashed **over the data USB cable, without the ST-Link**, using the STM32 system bootloader (DFU). Two ways to get there:
- **Software (from the app):** the app sends the A0 command `EnterDfu` (`Command 0x22`, `cmd=4`). The firmware writes a magic value to a `.noinit` RAM variable and calls `NVIC_SystemReset()`; an early check at the very top of `setup()` (before USB init) sees the magic, calls `HAL_RCC_DeInit()` (clock back to HSI) and jumps to the ST system bootloader at `0x1FFF0000` — the board is meant to re-enumerate as `0x0483:0xdf11` ("STM32 BOOTLOADER").
- **Hardware (reliable fallback):** the board's **`SW1 → DFU`** switch + a power-cycle boots the ROM bootloader directly, no firmware cooperation needed.

Either way, `dfu-util -a 0 -s 0x08000000:leave -D firmware.bin` flashes the new image over USB; `:leave` reboots straight into it.

**Root cause of the "software jump" failure on this bench board:** systematic on-device debugging (see `firmware-base/tools/enter_dfu.py` below) confirmed the `EnterDfu` command *does* reset the board — it disappears from USB as expected — but the ROM bootloader's USB peripheral then fails to enumerate after a **warm** jump on this specific board, because **its VBUS/5V USB power path is physically burnt**. The magic value in `.noinit` RAM does survive the reset fine (an RTC-backup-register trigger, considered earlier as a fix, would not help — that was never the problem). `SW1 → DFU` works because it's a **cold boot**, which doesn't hit the same USB-power state. This looks like a defect specific to this bench board's hardware; whether the automatic jump works on a healthy board is still to be re-validated.

#### App module (DriveLab Studio)
The "Atualizar firmware" ("Update firmware") screen: shows the detected device and its **currently running firmware version** (read live from the 0x21 telemetry) → pick the `.bin` file → the app validates the file matches the device via an embedded **`DRVLABFW` signature** (8 ASCII bytes + `DeviceKind` + 3 version bytes, checked by `firmware-base/tools/check_fw_signature.py` on the bench and by `DeviceKind`/`FirmwareFile` on the app side) → **Enviar/Send**.

The send flow (`BaseUpdater`) first tries the automatic path: it sends `EnterDfu` over the current HID transport and waits ~8 s for the DFU device to appear. If it doesn't (the expected outcome on this bench board), the app falls into a **manual fallback mode**: it keeps exclusive USB access (pauses auto-connect and releases the HID handle) and prompts the user to set **`SW1 → DFU` and power-cycle** the board. A **Continuar/Continue** button re-scans for the DFU device and, once found, flashes it via `dfu-util`; **Cancelar/Cancel** aborts and resumes normal auto-connect. Either path — automatic or manual — flashes over the same data USB cable, no ST-Link.

The firmware version itself is now a **single source of truth**: `DRVLAB_FW_VER_{MAJOR,MINOR,PATCH}` in `firmware-base/src/m05/fw_signature.h` feeds both the embedded `.bin` signature and the 0x21 telemetry payload (they used to disagree — telemetry reported 0.1.0 while the signature said 0.2.0). Bump the version only in `fw_signature.h`.

#### Bootstrap
The `EnterDfu` handler itself has to reach the board the old way once (ST-Link, or `SW1 → DFU` + `dfu-util`) — after that first flash, all further updates can go over USB with no ST-Link.

#### Bench tool (`firmware-base/tools/`)
- `enter_dfu.py` — isolated test of the firmware's DFU jump: sends `EnterDfu` via `hidapi` and watches `dfu-util -l` for the bootloader device to appear. Used to separate "does the firmware jump correctly?" from "does the app's send flow trigger and detect it correctly?" — this is what pinned the failure down to the ROM bootloader's USB not enumerating after a warm jump, rather than to the app or to the reset/magic logic.

> **Status (2026-07-19): validated end-to-end on hardware ✅** — on the bench MKS ODRIVE-S / STM32F405, flashing the base **through DriveLab Studio** (Send → automatic attempt times out as expected → manual `SW1 → DFU` fallback → `dfu-util`) changed the board's **reported firmware version from 0.1.0 to 0.2.0**, confirmed by re-reading the 0x21 telemetry after the flash. This is the reliable update path on this board today: **app Send → automatic attempt (currently always falls through on this board) → `SW1 → DFU` manual fallback prompted by the app → flash**.
>
> **Known limitations:**
> 1. The **fully hands-off automatic jump does not work on this bench board** — `EnterDfu` resets the board correctly, but the STM32 ROM bootloader's USB does not enumerate after a warm jump here, because this board's VBUS/5V USB path is physically burnt. This is believed to be a defect of this specific board, not of the jump mechanism itself; re-validating on a healthy board is a follow-up. The previously-planned RTC-backup-register trigger would not fix this — the magic already survives the reset today.
> 2. **Windows:** bundling `dfu-util` and the WinUSB driver for the DFU device (`0483:df11`) is a later phase — everything above is validated on **macOS** only so far.
> 3. The RP2040-based devices (pedal/handbrake/wheel) use **UF2/BOOTSEL**, not DFU — their updaters are a separate future phase.

#### Bench tool (`firmware-base/tools/`)
- `check_fw_signature.py` — verifies the `DRVLABFW` signature (magic + `DeviceKind` + version) is present and well-formed in a built `.bin`.

---

### Next milestones (summary)
M1 (open-loop motor) → M2 (encoder + closed loop + brake resistor) → M3 (A0 channel, **DriveLab Studio connects via HidTransport**) → M4 (settings) → M5 (FFB force → SimpleFOC) → M6 (game effects) → M7 (validation on a sim). Details in the design.

**M1 skeleton (compiles, ready to flash):** `src/m1/main.cpp` (env `m1`) wires the brain (`FfbEngine` + safe startup + protection) to **SimpleFOC** (BLDCMotor/driver/encoder) and the ADC — the real target of the interfaces we mock on host. It **compiles** (5.9% flash) but is **not hardware-validated**: the ODESC pins/ADC scales are placeholders to set on the bench, and it boots **disabled** (arm via serial `'1'`/`'0'` — safety first). Flash: `pio run -e m1 -t upload`.

**Testing the logic without the board:** the FFB "brain" (force→torque, soft-stop, safety) lives in a **portable module** (`lib/brain/`) behind a hardware seam (`hal.h`: `IEncoder`/`ICurrentSense`/`IMotor`). It compiles into the firmware (HAL = SimpleFOC/ADC) **and** into a PC host test with mocks — run `test/run.sh` (no board or emulator needed). Only USB enumeration and real-time timing still need silicon (a cheap Black Pill F411 de-risks USB before the ODESC). See **[docs/base-ffb-brain.md](../docs/base-ffb-brain.md)** (how it's built) and **[docs/ffb-quality-log.md](../docs/ffb-quality-log.md)** (levers + discoveries with measured numbers — reconstruction, cogging, the FFB "shake" and its fix).

### Wheel connection (USB hub + 5 V rail)

For a quick-release rim, the base is meant to host a small **USB hub** (the ODESC and the rim share one cable to the PC) and a **5 V buck** off the main PSU to power the rim's RGB LEDs — so no extra USB cable dangles from the wheel. Full wiring, the signals that cross the slip ring, and the required protections are documented in the rim README: **[firmware-wheel → Wheel ↔ base wiring & power](../firmware-wheel/README.md#wheel--base-wiring--power-simple-vs-full-rim)**.

---

## 🇧🇷 Português

### Marco atual: M0 — bring-up (só serial, SEM motor)

**Objetivo:** provar toolchain + gravação + USB serial na sua ODESC. Inofensivo (não mexe no motor/potência).

> **Status (2026-07-18): M0 TOTALMENTE VALIDADO no hardware real ✅** — uma **MKS ODRIVE-S V3.6-S6V** (STM32F405), gravada via **ST-Link + OpenOCD** (receita abaixo). Execução confirmada pelo SWD *e* a **serial USB CDC transmite** `DriveLab M0 vivo, tick=…` (enumera como `GENERIC_F405RGTX CDC in FS Mode`). O clock HSE 8 MHz / USB 48 MHz está certo — **isso de-risca todo o caminho USB/FFB (M0.5+)**.
>
> *Ressalva desta placa:* nesta unidade a **alimentação do USB (VBUS/5V) está queimada** (plugar só a USB da placa não acende o LED PWR), mas os **dados D+/D‑ estão ok** — enumera normal quando a lógica é alimentada por outro caminho (3.3V do ST-Link, que *acende* o LED). Dica: deixe o ST-Link num **carregador USB** só pra alimentar (ele solta 3.3V mesmo sem host de dados), liberando a única USB do note pro cabo de dados da placa.

#### Pré-requisitos
- **VS Code + extensão PlatformIO** (instala o toolchain STM32duino sozinho na primeira build).
- **ODESC v4.2** + cabo **USB** (micro-USB).
- Para gravar, **um** dos dois:
  - **ST-Link V2** (recomendado) ligado ao header **SWD** da ODESC: `SWDIO`, `SWCLK`, `GND`, `3V3`.
  - **DFU** (sem ST-Link): colocar a placa em modo DFU (BOOT0 em alto no reset — pad/botão de BOOT) e usar `upload_protocol = dfu` no `platformio.ini`.
- Alimentação: **não precisa de motor**. O USB/ST-Link já alimenta a lógica. (Não conecte o motor no M0.)

#### Ligando o ST-Link V2 (SWD) — só 4 sinais

Conecte **pelo rótulo impresso**, não pela posição do pino (os clones variam muito). No pendrive de alumínio do ST-Link V2 os nomes vêm impressos na carcaça (duas fileiras de 5 pinos); na placa, procure um header **SWD** de 4 pads perto do STM32 (ou um conector JTAG).

| ST-Link V2 (pino rotulado) | Placa (pad rotulado) | Obrigatório? |
|---|---|---|
| **SWDIO** | SWDIO / **DIO** | ✅ sim |
| **SWCLK** | SWCLK / **CLK** | ✅ sim |
| **GND**   | **GND**         | ✅ sim |
| **3.3V** (3V3) | 3V3 | ⚠️ depende (veja abaixo) |

- **Placa alimentada pelo USB dela (recomendado):** ligue só **SWDIO + SWCLK + GND** — **não** conecte o 3.3V (pode conflitar com o regulador da placa).
- **Placa sem USB (só o ST-Link):** aí **conecte o 3.3V** para alimentar a lógica.
- **GND é sempre obrigatório** — é a referência comum dos sinais.
- Ignore os pinos `SWIM` / `5.0V` — não usamos.
- **NRST opcional:** se o `st-info --probe` não achar a placa "ocupada" (ainda rodando o firmware de fábrica), ligue também **RST/NRST ↔ RST/NRST** para connect-under-reset.

Mínimo para começar (placa no USB dela):
```
ST-Link          Placa
 SWDIO  ───────►  DIO
 SWCLK  ───────►  CLK
 GND    ───────►  GND
 (3.3V não liga — placa já alimentada pelo USB)
```

> ⚠️ **Nunca energize o barramento DC (24 V / 56 V) durante o bring-up.** M0/M0.5 não tocam no motor — a lógica é alimentada só pelo USB ou pelo ST-Link.

#### Gravando um clone classe ODrive (VALIDADO numa MKS ODRIVE-S V3.6-S6V / STM32F405)

Se a placa for um clone ODrive (ODESC, **MKS ODRIVE-S**, etc.), duas coisas brigam com o gravador: o **firmware ODrive de fábrica re-arma um watchdog** que reseta o core no meio da escrita, e os **ST-Link clones travam o loader de SRAM do `st-flash`** (`Flash loader run error`, PC vira lixo). Esta é a sequência que funcionou de ponta a ponta:

1. **Ligue o pino `3.3V` do ST-Link** — o clone precisa dele como **VTref**, senão o `st-info` diz `Failed to enter SWD mode`. Ele também alimenta a lógica (não precisa de USB da placa nem do barramento DC no bring-up). Confirme que o **LED PWR** acende.
2. **Confirme o MCU:** `brew install stlink && st-info --probe --connect-under-reset` → esperado `chipid 0x413`, `STM32F4x5_F4x7`, `192 KiB SRAM`.
3. **Apague o firmware de fábrica** (mata o watchdog; o `erase` dirige o controlador de flash direto, então sobrevive aos resets — ao contrário de uma escrita): `st-flash --connect-under-reset --flash=1024k erase` → `Mass erase completed`.
4. **Grave com OpenOCD** (o loader dele funciona onde o do `st-flash` falha; depois do erase o core halta limpo — o `BUG: can't assert SRST` do transporte HLA é inofensivo): `brew install open-ocd` e então
> ```bash
> openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
>   -c "program .pio/build/m0/firmware.elf verify reset exit"
> ```
> → `Verified OK`. **Daqui pra frente basta repetir o passo 4** — nosso firmware nunca arma watchdog nem segura o SWD, então a briga é uma vez só.

> **Sem serial USB pra confirmar o M0?** Prove que roda pelo SWD, sem a USB da placa: `arm-none-eabi-nm .pio/build/m0/firmware.elf | grep 'loop::n'` dá o endereço do tick, aí leia duas vezes com o core rodando no meio —
> ```bash
> openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
>   -c "init;halt;mdw 0x200010d8;resume;sleep 3000;halt;mdw 0x200010d8;shutdown"
> ```
> o valor deve incrementar (~1/s). Ler o `uwTick` (contador de ms do HAL) do mesmo jeito e ver ~1 ms/tick também confirma que o **cristal HSE é 8 MHz** (então a USB vai enumerar). *Alternativa que evita tudo isso se a placa expõe USB: o switch **`SW1 → DFU`** sobe o bootloader da ST (o firmware de fábrica nem roda) — grave com `dfu-util -a 0 -s 0x08000000:leave -D firmware.bin`.*

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

### M0.5 — USB/FFB (o de-risco principal) ✅ v2 VALIDADO no hardware real (Passos A/B/C)

Provar que a base consegue **enumerar como dispositivo Force Feedback** (volante DirectInput), pra provar que o cano de efeitos de jogo existe antes de encostar em motor. **Sem motor** no M0.5.

> **v1 (shim, decisão B2) FALHOU no hardware — substituído pelo v2 (TinyUSB).** A primeira tentativa reusava o shim AVR-origin `ArduinoJoystickWithFFBLibrary` + `USBLibrarySTM32`. Compilava no F405, mas na bancada **nenhum SO enumerou** — nem macOS, nem Windows, nem uma VM UTM Windows. Causa raiz, medida com `tools/dump_report_descriptor.py` (pyusb): a canalização de control-transfer EP0 do shim **declarava** `wReportDescriptorLength = 1259` bytes mas **transmitia só 32** — um report descriptor HID truncado que todo SO rejeita de cara. O código de control-transfer do shim só é exercitado no F401/F411; não funciona no F405.

#### A correção: TinyUSB com descritor próprio
- Migramos para a **Adafruit TinyUSB Library** (`lib_deps = adafruit/Adafruit TinyUSB Library` no `env:m05`, MIT). Por quê: a classe HID nativa do STM32duino é **só de entrada** (sem endpoint OUT, sem `SET_REPORT`), então fisicamente não consegue receber efeitos FFB do host; a porta `dwc2`/OTG_FS da TinyUSB é madura, serve descritores grandes sem truncar, e suporta reports OUT.
- **Descritor:** o report descriptor HID PID Force-Feedback completo, **achatado a partir do OpenFFBoard** (`github.com/Ultrawipf/OpenFFBoard`, MIT, mesma família de VID `0x1209`) — 1196 bytes, gerado/validado por `tools/flatten_openffb_descriptor.py` + `tools/validate_ffb_descriptor.py`. Crédito: OpenFFBoard.
- **Identidade do dispositivo:** composto **HID PID + CDC**, `VID 0x1209` / `PID 0x0001`, string de produto **"DriveLab Base"**. Definida via `build_flags` no `platformio.ini` (`USB_VID`/`USB_PID`/`USB_MANUFACTURER`/`USB_PRODUCT`) — **não** em runtime: o `begin()` da Adafruit reconstrói o device descriptor inteiro a partir dessas macros de build incondicionalmente, então uma chamada `TinyUSBDevice.setID()` em runtime antes do `begin()` é descartada silenciosamente (nos mordeu no Passo A v1: enumerou com o default da lib `0x239A/0xCAFE`).

#### Status validado (2026-07-18, MKS ODRIVE-S V3.6 / STM32F405)
- **Passo A — joystick mínimo ✅** — enumera como HID gamepad: o macOS monta como game controller, e o `joy.cpl` do Windows mostra **"DriveLab Base"** com um eixo se movendo ao vivo. *(o v1/shim nunca chegou até aqui.)*
- **Passo B — descritor completo ✅** — o descritor HID PID completo de 1196 bytes sobe **intacto, sem truncamento** (confirmado via `dump_report_descriptor.py`, declared == received); o dispositivo continua reconhecido no `joy.cpl` do Windows. Nota: o `joy.cpl` não tem uma aba dedicada de Force Feedback por si só — isso é normal, o FFB mora no DirectInput, não no applet.
- **Passo C — constant force recebido ✅** — enviar um output report HID cru (`Set Constant Force`, `mag=1000`) do Mac via `tools/ffb_send_constant_force.py` (hidapi) faz o firmware parsear e logar `FFB const block=1 mag=1000` pela CDC. Prova o cano FFB de ponta a ponta (efeito do host → USB → parser → log) no hardware real, sem precisar de Windows/DirectInput.

#### Ainda em aberto (marco futuro)
O handshake completo de inicialização PID do DirectInput (requests de Pool/Block Load, como um jogo real dirigiria) e transformar um efeito recebido em torque de motor (**M5**, atrás do cérebro + segurança) não fazem parte do M0.5. Nenhum motor é tocado aqui.

#### Como gravar o M0.5
```bash
pio run -e m05 -t upload      # ou selecione o env "m05" na barra do PlatformIO
```
O `env:m05` **não** usa `USBD_USE_CDC` pro caminho do joystick — a CDC é composta junto com o HID pela própria TinyUSB (ver `platformio.ini`).

#### Ferramentas de bancada (`firmware-base/tools/`)
- `validate_ffb_descriptor.py` — parseia/valida os bytes do descriptor HID PID (self-test, sem placa).
- `flatten_openffb_descriptor.py` — gera o descriptor achatado a partir da fonte do OpenFFBoard.
- `dump_report_descriptor.py` — lê o config + report descriptor do dispositivo real via pyusb (tamanho declarado vs. recebido — foi isso que pegou o truncamento do v1).
- `ffb_send_constant_force.py` — envia um output report HID cru de Set Constant Force via hidapi, pra provar o cano FFB sem DirectInput/Windows.

*(A gravação da placa F405/classe ODrive já está documentada acima, no M0 — ST-Link + OpenOCD; a mesma receita vale aqui, sem repetir.)*

*(A prioridade da IRQ USB abaixo do timer do FOC só importa a partir do M1, quando o SimpleFOC entrar.)*

---

### M3 — canal de configuração A0 ✅ VALIDADO no hardware real (bancada + app)

**O que é:** a base agora fala um **canal de configuração vendor ("A0")**, o equivalente da base ao canal **P0** do pedal — o **DriveLab Studio lê, grava e salva as settings da base**, e vê a base como conectada. Sem motor.

> **Status (2026-07-19): M3 VALIDADO no hardware real ✅** — uma **MKS ODRIVE-S / STM32F405**. Na bancada via `hidapi`: ler `TotalStrength` (default) → gravar `55` → reler `55`; `SaveSettings` + power-cycle → `55` continua lá; telemetria `DeviceState` (0x21) fluindo. **E no DriveLab Studio no macOS**: a base conecta, a tela "Base do Volante" carrega todos os campos, uma alteração + **"Salvar no controlador"** persiste (confirmado reabrindo/power-cycle).

#### Formato da USB: uma interface HID, duas coleções vendor
O core OTG_FS do STM32F405 tem só **~3 endpoints IN utilizáveis**, então o dispositivo não consegue fazer 2× HID + CDC (um HID pro FFB, outro pro A0, mais a CDC). Em vez disso, o **A0 divide a única interface HID com o FFB** — um único report descriptor combinado — mais a **CDC**, mantida pro log de debug. Os reports do A0 vivem numa coleção vendor (usage page `0xFF00`) com os **report IDs remapeados** pra não colidir com os do FFB: `DeviceState 0x21`, `Command 0x22`, `DirectControl 0x10`, `SettingWrite 0x14`, `SettingReadRequest 0x15`, `SettingValue 0x16`.

#### Modelo de settings
Uma struct `BaseCfg` espelha o enum `BaseSettingId` congelado do app — **19 campos** (TotalStrength, SoftStop\*, Spring/Damper, encoder, ganhos da malha de corrente, etc.). As settings persistem em **flash** (emulação de EEPROM do STM32duino, magic `"DLB1"`): `SaveSettings` grava, e elas sobrevivem a um power-cycle (validado acima).

#### Telemetria
A base envia periodicamente um report `DeviceState` (`0x21`) com a versão do firmware. **Os campos de sensor (tensão do barramento, corrente, temperaturas) são placeholder `0` até o M1** — precisam do estágio de potência, que ainda não está ligado.

#### Duas pegadinhas do macOS/HidSharp
- A base com interface combinada enumera com **usage de nível de dispositivo `0x00`** (não o usage de joystick do FFB), então o app a detecta por **VID/PID**, não por usage-page.
- O firmware mantém **um único slot de leitura pendente**, então o app precisa **serializar as leituras de settings** — um round-trip `0x15` (SettingReadRequest) → `0x16` (SettingValue) por vez, não disparados em concorrência.

#### Ainda em aberto (marcos futuros)
Telemetria real de sensores e transformar settings em torque ficam pro **M1/M5**. A atualização de firmware via USB (DFU) já está implementada e validada — ver a seção dedicada abaixo.

#### Ferramenta de bancada (`firmware-base/tools/`)
- `a0_config.py` — lê/grava uma setting pelo canal A0 (hidapi), usada pra validar o fluxo de leitura/gravação/persistência acima.

---

### Atualização de firmware via USB (DFU) — funcionando ponta a ponta via app + fallback manual SW1

**O que é:** a base pode ser regravada **pelo próprio cabo USB de dados, sem o ST-Link**, usando o bootloader de sistema da ST (DFU). Dois jeitos de chegar lá:
- **Por software (a partir do app):** o app manda o comando A0 `EnterDfu` (`Command 0x22`, `cmd=4`). O firmware grava um valor mágico numa variável de RAM `.noinit` e chama `NVIC_SystemReset()`; uma checagem bem no início do `setup()` (antes de iniciar a USB) vê o mágico, chama `HAL_RCC_DeInit()` (clock de volta pro HSI) e salta pro bootloader de sistema da ST em `0x1FFF0000` — a ideia é a placa reenumerar como `0x0483:0xdf11` ("STM32 BOOTLOADER").
- **Por hardware (fallback confiável):** o switch **`SW1 → DFU`** da placa + um power-cycle sobe o bootloader da ROM direto, sem precisar de cooperação do firmware.

De qualquer um dos dois jeitos, `dfu-util -a 0 -s 0x08000000:leave -D firmware.bin` grava a nova imagem pela USB; o `:leave` reinicia direto na imagem nova.

**Causa raiz da falha do "salto por software" nesta placa de bancada:** uma depuração sistemática no próprio dispositivo (ver `firmware-base/tools/enter_dfu.py` abaixo) confirmou que o comando `EnterDfu` *de fato* reseta a placa — ela some da USB como esperado — mas depois a USB do bootloader da ROM não enumera após um salto **a quente** nesta placa específica, porque **o caminho de alimentação VBUS/5V da USB dela está fisicamente queimado**. O valor mágico na RAM `.noinit` sobrevive ao reset normalmente (um gatilho via registrador de backup do RTC, cogitado antes como correção, não resolveria isso — isso nunca foi o problema). O `SW1 → DFU` funciona porque é um **boot a frio**, que não passa pelo mesmo estado de alimentação da USB. Isso parece ser um defeito específico desta placa de bancada; se o salto automático funciona numa placa saudável ainda precisa ser revalidado.

#### Módulo no app (DriveLab Studio)
A tela "Atualizar firmware": mostra o dispositivo detectado e a **versão do firmware rodando nele agora** (lida ao vivo da telemetria 0x21) → escolher o arquivo `.bin` → o app valida que o arquivo bate com o dispositivo via uma **assinatura `DRVLABFW`** embutida (8 bytes ASCII + `DeviceKind` + 3 bytes de versão, checada na bancada por `firmware-base/tools/check_fw_signature.py` e no app por `DeviceKind`/`FirmwareFile`) → **Enviar/Send**.

O fluxo de envio (`BaseUpdater`) primeiro tenta o caminho automático: manda `EnterDfu` pelo transporte HID atual e espera ~8 s o dispositivo DFU aparecer. Se não aparecer (o resultado esperado nesta placa de bancada), o app entra num **modo de fallback manual**: mantém acesso exclusivo à USB (pausa o auto-connect e libera o handle HID) e pede ao usuário para colocar **`SW1 → DFU` e dar power-cycle** na placa. Um botão **Continuar/Continue** reescaneia em busca do dispositivo DFU e, ao encontrar, grava via `dfu-util`; **Cancelar/Cancel** aborta e retoma o auto-connect normal. Nos dois caminhos — automático ou manual — a gravação é pela mesma USB de dados, sem ST-Link.

A versão do firmware em si agora é uma **fonte única de verdade**: `DRVLAB_FW_VER_{MAJOR,MINOR,PATCH}` em `firmware-base/src/m05/fw_signature.h` alimenta tanto a assinatura embutida no `.bin` quanto o payload de telemetria 0x21 (antes divergiam — a telemetria reportava 0.1.0 enquanto a assinatura dizia 0.2.0). Suba a versão só em `fw_signature.h`.

#### Bootstrap
O próprio handler do `EnterDfu` precisa chegar na placa do jeito antigo uma vez (ST-Link, ou `SW1 → DFU` + `dfu-util`) — depois dessa primeira gravação, as atualizações seguintes podem ir todas pela USB, sem ST-Link.

#### Ferramenta de bancada (`firmware-base/tools/`)
- `enter_dfu.py` — teste isolado do salto DFU do firmware: manda `EnterDfu` via `hidapi` e observa `dfu-util -l` até o bootloader aparecer. Usada pra separar "o firmware salta certo?" de "o fluxo de envio do app dispara e detecta certo?" — foi isso que localizou a falha na USB do bootloader da ROM não enumerando após um salto a quente, e não no app nem na lógica de reset/mágico.

> **Status (2026-07-19): validado ponta a ponta no hardware ✅** — na MKS ODRIVE-S / STM32F405 de bancada, gravar a base **pelo DriveLab Studio** (Enviar → tentativa automática expira como esperado → fallback manual `SW1 → DFU` → `dfu-util`) mudou a **versão de firmware reportada pela placa de 0.1.0 para 0.2.0**, confirmado relendo a telemetria 0x21 depois da gravação. Esse é o caminho de atualização confiável nesta placa hoje: **Enviar no app → tentativa automática (hoje sempre cai no fallback nesta placa) → fallback manual `SW1 → DFU` pedido pelo app → gravação**.
>
> **Limitações conhecidas:**
> 1. O **salto automático totalmente sem intervenção não funciona nesta placa de bancada** — o `EnterDfu` reseta a placa corretamente, mas a USB do bootloader da ROM da STM32 não enumera depois de um salto a quente aqui, porque o caminho VBUS/5V da USB desta placa está fisicamente queimado. Acredita-se que seja um defeito específico desta placa, não do mecanismo de salto em si; revalidar numa placa saudável é um follow-up. O gatilho via registrador de backup do RTC, cogitado antes, não resolveria isso — o mágico já sobrevive ao reset hoje.
> 2. **Windows:** empacotar o `dfu-util` e o driver WinUSB pro dispositivo DFU (`0483:df11`) é uma fase futura — tudo acima está validado só no **macOS** por enquanto.
> 3. Os dispositivos baseados em RP2040 (pedal/handbrake/aro) usam **UF2/BOOTSEL**, não DFU — os atualizadores deles são uma fase futura separada.

#### Ferramenta de bancada (`firmware-base/tools/`)
- `check_fw_signature.py` — verifica se a assinatura `DRVLABFW` (mágico + `DeviceKind` + versão) está presente e bem formada num `.bin` gerado.

---

### Marcos seguintes (resumo)
M1 (motor malha aberta) → M2 (encoder + malha fechada + brake resistor) → M3 (canal A0, **DriveLab Studio conecta via HidTransport**) → M4 (settings) → M5 (força FFB → SimpleFOC) → M6 (efeitos de jogo) → M7 (validação num sim). Detalhes no design.

**Esqueleto do M1 (compila, pronto pra gravar):** `src/m1/main.cpp` (env `m1`) liga o cérebro (`FfbEngine` + partida segura + proteção) ao **SimpleFOC** (BLDCMotor/driver/encoder) e ao ADC — o alvo real das interfaces que mockamos no host. **Compila** (5,9% de flash), mas **não é validado em hardware**: os pinos/escalas de ADC da ODESC são placeholders p/ ajustar na bancada, e ele sobe **desarmado** (arma via serial `'1'`/`'0'` — segurança primeiro). Gravar: `pio run -e m1 -t upload`.

**Testar a lógica sem a placa:** o "cérebro" FFB (força→torque, soft-stop, segurança) mora num **módulo portável** (`lib/brain/`) atrás de uma costura de hardware (`hal.h`: `IEncoder`/`ICurrentSense`/`IMotor`). Compila no firmware (HAL = SimpleFOC/ADC) **e** num teste de host no PC com mocks — rode `test/run.sh` (sem placa nem emulador). Só a enumeração USB e o real-time ainda precisam de silício (uma Black Pill F411 barata de-risca o USB antes da ODESC). Ver **[docs/base-ffb-brain.md](../docs/base-ffb-brain.md)** (como é feito) e **[docs/ffb-quality-log.md](../docs/ffb-quality-log.md)** (alavancas + descobertas com números medidos — reconstrução, cogging, o "tremor" do FFB e seu fix).

### Conexão do volante (hub USB + trilho de 5 V)

Para um aro de engate rápido, a base deve hospedar um pequeno **hub USB** (a ODESC e o aro dividem um único cabo pro PC) e um **buck de 5 V** a partir do PSU principal para alimentar os LEDs RGB do aro — assim nenhum cabo USB extra fica enrolando no volante. A fiação completa, os sinais que cruzam o slip ring e as proteções necessárias estão no README do aro: **[firmware-wheel → Interligação com a base & alimentação](../firmware-wheel/README.md#interligação-com-a-base--alimentação-aro-simples-vs-completo)**.
