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

The whole project is MIT, and every source file carries a header saying so.

## Status (2026-08-12)

**Working and validated on the bench:**

- Motor running under FOC, arming repeatably.
- Force feedback validated in-game: **11 laps at Monza in Assetto Corsa Competizione** without
  losing FFB or disarming, plus AMS2 and EVO. Monza matters here — its chicanes are what exposed
  the regen voltage loss back in August.
- **Torque constant measured** (0.397 Nm/A) instead of the 0.55 from a generic catalogue formula,
  which was overstating delivered force by 39%.
- **Response curve back to linear.** It had been sitting at 1.59, which flattened mid-range forces
  by half — the game asked for 50% and 33% arrived.
- Settings persisted to the board's own flash (`CMD_SAVE`) — the base is the source of truth, not
  the app.
- Live telemetry: bus voltage, motor current with positive and negative peaks, **FET temperature**
  (43 °C at rest), clipping split into the game's share and the base's share.
- Protections: brake-resistor chopper, over-current cutoff in the ISR, soft stop, under- and
  over-voltage trips sized from the measured supply, electrical-angle coherence guard, overspeed
  guard.

**Built and awaiting bench validation:**

- Configurable FET thermal derating (the board starts backing off at 85 °C instead of the ODrive
  default of 100 °C).
- Force output filter.
- Encoder guard: a sensor/interface combination the firmware cannot drive now applies **nothing**,
  instead of applying the resolution over an A/B/Z reading.
- Force response curve with 11 points and smooth interpolation.

**Pending:**

- **The motor has no temperature sensor.** `MotorTempC` reports -128 and there is no thermal cutoff
  for the motor itself — the only protection is your hand on it. Fitting an NTC is what unlocks
  raising the current limit safely.
- MCU temperature is **deliberately disabled**: reading it shared ADC1 with the current sense and
  cost the FFB. See the note in `src/motor_link.cpp`.
- The encoder is still incremental, so the centre is not kept across power cycles. See
  [`../docs/encoders.md`](../docs/encoders.md).

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

## Power supply

Every number below was **measured on our own bench** or read from the firmware source, not taken
from a datasheet. The motor is a hoverboard motor; the board is a v3.6-class 56 V variant.

| Measured | Value | Where it comes from |
|---|---|---|
| Phase resistance | **0.2016 Ω** | measured on this motor (`motor_link.cpp`) |
| Torque constant Kt | **0.397 Nm/A** | measured 2026-08-11 (234 samples, regression through origin) |
| Bench supply | 27 V / 30 A | |
| Regen peak, no brake resistor | **33.5 V** from a 27.1 V supply | on track |
| Same peak, with a 2 Ω brake resistor | **27.5 V** (3.3 W average) | on track |

### What the supply actually has to deliver

The current your **supply** provides is not the current your **motor** draws. The motor pulls its
current through the winding resistance, and that is all the supply has to cover:

```
P = 1.5 × R × I²        (the 1.5 is the three-phase conversion: Iq is the phase-current amplitude)
P = 1.5 × 0.2016 × 25²  = 189 W at the 25 A default current limit
```

At 27 V that is **7 A from the supply**, while the motor sees 25 A in the windings.

The formula is confirmed by our own bench: a fault once locked the motor at 18.05 A, and
`1.5 × 0.2016 × 18.05² = 98.5 W`, matching the ~98 W of heat recorded at the time.

### Voltage does not buy torque

Torque is `current × Kt`. Voltage only has to be enough to push that current through the winding —
`R × I = 0.2016 × 25 ≈ 5 V` with the wheel held still.

Voltage starts to matter only at speed, where the motor's own back-EMF eats the headroom. Working
out where a 27 V bus would run out:

```
available phase voltage ≈ 27 / √3 ≈ 15.6 V
back-EMF = λ × ω_electrical,  λ = Kt / (1.5 × pole_pairs) = 0.0176 V·s/rad
15.6 = 0.0176 × 15 × ω_mechanical   →   ω ≈ 59 rad/s ≈ 9.4 rev/s
```

**9.4 revolutions per second.** On a wheel with 2.5 turns lock to lock, that never happens. A 48 V
bus pushes it to 16.7 rev/s — headroom you will never use.

### What a higher voltage does cost

Regeneration adds on top of the supply. When you reverse the wheel quickly the motor returns
energy, and the bus rises. On our bench that was **+6.5 V** (27.1 V supply, 33.5 V peak, no brake
resistor).

The firmware sizes its limits from the supply it measures when arming
(`motor_link_autoscale_bus_limits`):

| | 27 V supply | 48 V supply |
|---|---|---|
| brake-chopper ramp starts | 29 V | 50 V |
| ramp ends | 31 V | 52 V |
| over-voltage trip | 33 V | 54 V |
| measured regen peak (no resistor) | 33.5 V | ~54.5 V |

At 27 V the peak lands inside the ramp and the resistor absorbs it. **At 48 V the same event lands
on the trip.** The board's own ceiling is 55 V (bus capacitors ~63 V), so the margin goes from
comfortable to none — and if that +6.5 V grows with voltage instead of staying fixed (we have one
data point, so we do not know), it goes over.

### Recommendation for this class of motor

- **24–30 V is the sweet spot.** It delivers full torque, keeps a wide regen margin, and is easy on
  the bus capacitors.
- **Size the supply by power, not by motor current.** ~200 W covers the 25 A default; a 30 A supply
  is roughly four times what is needed.
- **A brake resistor is not optional**, and above ~36 V it stops being a safety net and becomes part
  of normal operation.
- **More force comes from more current, not more volts** — and current costs heat with the *square*:
  25 A → 9.75 Nm and 189 W; 30 A → 11.7 Nm and 272 W. Fit a motor thermistor before raising it.

### Under-voltage is a real failure mode too

A supply that sags under load trips the base just as surely as one that spikes. The firmware keeps
the under-voltage trip fixed at **8 V** for exactly this reason: a trip level of 14.79 V once caused
repeated cut-outs during slow, high-current corners, because the bus dipped to 14.79 V under load
and the base disarmed. Long or thin supply leads make this worse.

## Safety

Read [`../docs/faq-hoverboard.md#safety`](../docs/faq-hoverboard.md#safety) before powering a motor. The two that cost the most:

- **Match the supply voltage to your board variant** (24 V or 56 V). Going over destroys the board without warning.
- **A brake resistor is mandatory** before any closed-loop torque.
