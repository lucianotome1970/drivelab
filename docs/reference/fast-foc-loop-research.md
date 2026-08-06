# Research: moving the FOC current loop out of `loop()` into a fast ISR

Companion to `current-sense-research.md` (ADC sampling architecture — DIR-gated JDR read,
fixed) and `calibration-currentloop-research.md` (PI gain math). This report is about the
**timing architecture**: how to run our current loop at a real fixed rate instead of
polled-in-`loop()` at ~1.8 kHz (dt≈544µs, measured on bench), without breaking TinyUSB.

**Bottom line up front:** ODrive's *actual* v0.5.1 firmware (our board's factory fork) does
**not** run the full FOC math inside the ADC/TIM ISR — it runs a **DIR-gated ADC capture in
the ISR**, then wakes a **FreeRTOS thread** (`osSignalSet`) that does the Clarke/Park/PI/SVM
math at 8 kHz, at the highest thread priority in the system. We have no RTOS (bare
Arduino/TinyUSB `loop()`), so the correct adaptation for us is **not** to add an RTOS —
it's to do the *entire* fast path (read → Park → PI → inverse-Park → PWM write) directly
inside a single dedicated TIM ISR, which is exactly the model newer ODrive firmware and the
SimpleFOC "hard real-time loop" doc both use. TinyUSB's own ISR is deliberately tiny (it
just defers to `.task()`), and ODrive/STM32 NVIC practice puts the control-loop interrupt at
the *highest* priority and USB at one of the *lowest* — the fast ISR preempts USB freely as
long as it stays short (a few µs), and `TinyUSBDevice.task()` keeps draining in `loop()`
exactly as it does today.

---

## Q1 — ODrive's control-loop ISR: which interrupt, what rate, what's inside

Genuine ODrive v0.5.1 (our board's factory fork, confirms board family):
`~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/`

- **Rate:** `CURRENT_MEAS_HZ = TIM_1_8_CLOCK_HZ / (2·TIM_1_8_PERIOD_CLOCKS·(RCR+1))
  = 168 MHz / (2·3500·3) = 8000 Hz` (`Board/v3/Inc/main.h:69-75,171-172`). PWM switches at
  24 kHz (center-aligned, so 2 Update events/period); `RCR=2` means only 1-in-3 Update
  events actually fires the interrupt chain → 8 kHz current loop from a 24 kHz switching
  rate.
- **Which ISR:** `TIM1_UP_TIM10_IRQn` (motor 0) — configured
  `HAL_NVIC_SetPriority(TIM1_UP_TIM10_IRQn, 0, 0)` (`Board/v3/Src/tim.c:406`), i.e.
  **NVIC priority 0 — the highest priority in the whole firmware** (nothing else is set to
  0 except the fixed-priority fault handlers in `stm32f4xx_hal_msp.c:71-79`).
- **What runs where, concretely:**
  - `pwm_trig_adc_cb()` (`MotorControl/low_level.cpp:494`) — fires from the ADC
    injected-conversion-complete callback. Reads `axis.motor_.hw_config_.timer->Instance->CR1
    & TIM_CR1_DIR` to classify the sample as **valley (real current)** or **peak (DC_CAL)**
    (low_level.cpp:511-518) — this is the exact mechanism our own DIR-gate fix in
    `current-sense-research.md` mirrors. Valley samples go to `current_meas_.phB/phC`
    (offset-corrected by a continuously-updated `DC_calib_`, 0.2s LPF); on the second of the
    two ADC channels it calls `axis.signal_current_meas()` (low_level.cpp:583).
  - `tim_update_cb()` (`low_level.cpp:592`) — same DIR gate, samples the encoder GPIO port
    and hall lines synchronously with the current sample (`low_level.cpp:594-618`).
  - `signal_current_meas()` (`MotorControl/axis.cpp:107-109`) — `osSignalSet(thread_id_,
    M_SIGNAL_PH_CURRENT_MEAS)`. This is the hand-off: **the actual Clarke/Park transform,
    current PI, inverse Park, and SVM/PWM-timing update do NOT run in this ISR.** They run
    in `Axis::run_control_loop()` (`axis.cpp:198+`, called from each control-mode method,
    e.g. `axis.cpp:302-307` closed-loop control), which is the body of the **axis FreeRTOS
    thread**, unblocked by `osSignalWait(M_SIGNAL_PH_CURRENT_MEAS, ...)` immediately after
    the ISR sets the signal.
  - Task priorities (per DeepWiki's ODrive analysis, corroborated by RTOS convention):
    Axis 0/1 threads run at the **highest application priority** in the system (above
    CAN/USB/UART), so in practice the thread resumes essentially immediately after the ISR
    — but it is still task-context, not ISR-context, execution.
- **USB coexistence:** USB (CDC over the same OTG_FS controller we'd use) runs in its own
  low-priority thread/task, `HAL_NVIC_SetPriority(OTG_FS_IRQn, 5, 0)`
  (`Board/v3/Src/usbd_conf.c:117`) — NVIC priority 5, five steps below the TIM1 current ISR
  (priority 0). Every other communications peripheral (ADC_IRQn, SPI3, I2C1, UART4, DMA) is
  also parked at priority 5 (`adc.c:282/318/344`, `spi.c:161`, `i2c.c:146/148`,
  `usart.c:151`, `dma.c:75-84`). **The pattern is unambiguous: one interrupt (the current
  loop trigger) gets priority 0, everything communications-related is priority 5, nothing
  is in between.** ODrive does not need to "protect" USB from the control loop by boosting
  USB's priority — it protects USB by keeping the *ISR* portion of the control loop tiny
  (register reads + a signal-set) and doing the heavy math in a thread that a
  cooperative/preemptive RTOS scheduler still has to yield to USB (lower prio, but not
  starved — FreeRTOS's scheduler still runs the USB thread between axis-thread
  iterations/waits).

Newer ODrive firmware generations (post-RTOS-simplification, per DeepWiki's page on the
current `odriverobotics/ODrive` main branch, *not* our local v0.5.1 fork) instead run
`control_loop_cb()` — the **full** Clarke/Park/PI/SVM math — directly inside
`TIM8_UP_TIM13_IRQHandler`/`ControlLoop_IRQHandler`, with USB pushed into a fully separate
low-priority `usb_server_thread`. This is architecturally simpler (no ISR→thread hop) and is
the model most relevant to us, since **we have no RTOS at all** — see the recommendation.

## Q2 — What must run fast vs what can stay slow

**Fast path (must run at the control-loop rate, ISR or ISR-equivalent):**
1. Read phase current samples (our DIR-gated `ADC2->JDR1/JDR2`, already correct per
   `current-sense-research.md`).
2. Clarke transform → Park transform at the *current* rotor electrical angle.
3. Current-loop PI (Iq/Id).
4. Inverse Park → SVM duty cycles.
5. Write `TIM1->CCRx` (or `motor.setPhaseVoltage()`/equivalent low-level SimpleFOC call).

**Slow path (main loop, unchanged rate is fine):**
- Velocity/position estimation and outer loop, torque setpoint from the FFB engine
  (`engine.step()`), USB/A0 servicing, telemetry, PID-state heartbeat, cogging table,
  live-config reapply.

**ODrive's actual split** confirms this: `CURRENT_MEAS_HZ` (8 kHz, Q1) drives only the inner
current loop; the *velocity/position* control body (`run_control_loop`'s lambda in
`axis.cpp:302-307` etc.) is invoked once per current-loop tick too in ODrive's design (no
separate downsampled controller rate in this fork — `main.h:171-172` defines only the one
frequency), but ODrive's own docs elsewhere note the controller (outer loop) does not need
anywhere near 8 kHz to be stable; it's clocked at the current-loop rate mostly for
implementation simplicity, not because it needs to be.

**What rate do we need?** The task's own numbers: our current dt (≈544µs / 1.8 kHz) is
already close to our estimated phase inductance time constant L/R≈154µs — i.e. we're
sampling barely 3.5× per L/R time constant, nowhere near enough to even *measure*
inductance cleanly, let alone close a stable current loop. ODrive targets 8 kHz on a similar
low-inductance BLDC. SimpleFOC's own real-time-loop doc (Q4) uses a 10 kHz worked example
and states the FOC execution time should stay "ideally above 5 kHz." Given our 24 kHz PWM
(TIM1, confirmed `current-sense-research.md`), an 8 kHz current loop (1-in-3 valley samples,
mirroring ODrive's `RCR=2` scheme) is the natural, low-risk target — it reuses a ratio ODrive
has already validated on this exact silicon/board family, and leaves headroom under the
24 kHz switching rate for FET/deadtime settling.

## Q3 — Coexistence with TinyUSB on STM32F405: NVIC scheme

- **TinyUSB's own ISR is intentionally tiny.** TinyUSB "is designed for memory safety and
  thread safety with all interrupts deferred to non-ISR task functions" — the USB peripheral
  IRQ just records the event; the actual protocol/report handling happens in
  `TinyUSBDevice.task()`, called from `loop()` (exactly how our firmware already uses it,
  `main.cpp:1251,1597,1663`). This means a higher-priority FOC ISR preempting the USB ISR
  briefly does not corrupt TinyUSB state — TinyUSB already assumes its ISR can be interrupted
  and deferred; it does not do heavy work inside the ISR itself.
- **General STM32/ST guidance:** USB is not time-critical at the microsecond level ("USB IRQ
  priority can be one of the lowest levels" — ST community consensus) — consistent with
  ODrive hardcoding `OTG_FS_IRQn` to priority 5 while the current-loop timer sits at
  priority 0 (Q1).
- **Recommended scheme for us (STM32F405, single NVIC priority group,
  `NVIC_PRIORITYGROUP_4` like ODrive, `stm32f4xx_hal_msp.c:67`):**
  - FOC timer ISR: priority **0** (or lowest numeric value the Arduino core allows us to set
    — STM32duino's `HardwareTimer::attachInterrupt` lets you call
    `NVIC_SetPriority(TIMx_IRQn, 0)` after attaching).
  - `OTG_FS_IRQn` (TinyUSB): leave at its Arduino-core default (typically mid-range, e.g.
    5-ish) — **do not raise it**; it must stay numerically higher (= lower priority) than
    the FOC ISR.
  - Any other ISR we already use (encoder A/B quadrature EXTI, SysTick) should stay at or
    below USB's priority — they're not time-critical to the microsecond.
- **Known gotcha specific to us:** our firmware's own comment
  (`main.cpp:158-159`, `current-sense-research.md` Q1) already discovered that
  **SimpleFOC's `LowsideCurrentSense::init()` hangs on this board** — its ADC-injected +
  DMA + timer-sync init machinery conflicts with something in the STM32duino core
  (plausibly exactly this kind of NVIC/ISR ownership fight — SimpleFOC's STM32 driver wants
  to own the ADC-injected-complete interrupt, and the core's `analogRead()` elsewhere
  fights over the same ADC). **This means we should not attempt to use SimpleFOC's own
  ADC-ISR current-sense driver for the fast loop either** — the fix is the same one already
  proven for polling (hand-rolled register access, no `LowsideCurrentSense`), just moved
  from `loop()` into our *own* TIM ISR instead of SimpleFOC's ADC ISR. This sidesteps the
  known conflict entirely: we are not asking SimpleFOC to own an interrupt, we are using our
  own `HardwareTimer`-attached ISR that only *calls* SimpleFOC math functions
  (`motor.setPhaseVoltage()`/manual Clarke-Park) — SimpleFOC's ADC driver and its ISR
  ownership are never invoked.

## Q4 — SimpleFOC fast-loop guidance

- SimpleFOC's own doc, **"Hard real-time FOC loop using timers"**
  (https://docs.simplefoc.com/real_time_loop), explicitly supports and documents calling
  `motor.loopFOC()` and `motor.move()` from inside a hardware timer ISR (worked STM32
  example at **10 kHz** via `HardwareTimer`), specifically to get synchronous,
  jitter-free current-loop timing instead of `micros()`-based dt in `loop()`. It moves
  `motor.monitor()`/`command.run()` (non-time-critical serial/telemetry) back out to
  `loop()` — directly analogous to our own outer FFB-engine/USB/telemetry code staying in
  `loop()`.
- Its own warning: if `loopFOC()`+`move()` take longer than the timer interval, the timer
  "may miss calls or overload the microcontroller" — i.e. **profile execution time before
  picking the ISR rate**; don't just copy ODrive's 8 kHz blindly without checking our own
  loop body fits comfortably (say <50% of the period) at that rate on an F405 at (whatever
  our clock is, but genuine ODrive's 168 MHz Cortex-M4F number is a reasonable proxy).
- Community discussion
  (https://community.simplefoc.com/t/running-foc-control-loop-inside-to-pwm-timer-overflow-interrupt-handler-or-adc-sample-finish-interrupt-handler/723):
  SimpleFOC maintainer Antun Skuric explicitly recommends **a dedicated timer over the raw
  PWM-overflow interrupt** — PWM overflow on a typical setup fires "above 15 kHz, which is
  far too fast for motion control," and reiterates the two-tier split: fast interrupt-driven
  current sampling/PWM update vs. slower main-loop motion control — the same split as Q2.
- **Given our specific constraints** (LowsideCurrentSense hangs, SimpleFOC ADC ISR conflicts
  with the core, hand-rolled DIR-gated JDR read already working in `genCurrentRead()`), the
  minimal, lowest-risk fast loop is: **our own `HardwareTimer`-based ISR** (not SimpleFOC's
  ADC ISR, not PWM-overflow) that on every tick does — read `ADC2->JDR1/JDR2` with the
  existing DIR-gate logic (`main.cpp:193-214`, already correct and ISR-safe: it's a few
  register reads, no blocking) → `motor._electricalAngle()`/our own angle read → Clarke/Park
  → PI → inverse Park → either `motor.setPhaseVoltage(Uq, Ud, angle)` (SimpleFOC's own
  higher-level call, safe to use since it just writes `TIM1->CCRx` under the hood on STM32)
  or a hand-rolled equivalent if `setPhaseVoltage()` proves to have hidden non-reentrant
  state. This reuses proven, already-debugged pieces (the DIR-gate fix) rather than
  reintroducing SimpleFOC's ADC-ISR machinery that's known broken on this board.

## Q5 — OpenFFBoard / FFBeast / ODESC

- **OpenFFBoard** (STM32F407, FreeRTOS, TMC4671-based — note: TMC4671 is a dedicated FOC
  ASIC/co-processor, so OpenFFBoard's own MCU does *not* run the current loop math itself;
  it streams torque/flux targets to the TMC4671 over SPI and lets the chip's internal
  hardware close the current loop). Its architecture is still informative for the *task
  split*, not the ISR mechanics: DeepWiki's analysis confirms peripheral ISRs (ADC/TIM/SPI/
  CAN/I2C) are routed to C++ handler objects, and command/config processing runs in "an
  asynchronous processing thread ... to handle commands without blocking the high-priority
  FFB loops" — i.e. **the same fast-loop/slow-thread split**, just with FreeRTOS priority
  levels instead of NVIC alone, and with "fast loop" meaning "talk to the TMC4671 fast
  enough," not "run Clarke/Park ourselves." Not directly reusable for our bare-metal,
  software-FOC case beyond confirming the general pattern.
- **ODESC-class ODrive-clone force-feedback ports** (community ports combining OpenFFBoard
  firmware + ODrive firmware ported to the MKS XDrive Mini, a board in the same family as
  ours): these projects graft OpenFFBoard's USB-HID-FFB front end onto ODrive's own current
  loop rather than reimplementing FOC — i.e. **the pragmatic community answer to "fast motor
  loop + USB HID FFB simultaneously" on this board family is ODrive's own existing 8 kHz
  ISR/thread split (Q1) plus a USB HID descriptor swap**, not a novel scheduling scheme. This
  reinforces that ODrive's structure (Q1) is the right reference architecture to mirror, and
  that nobody in this ecosystem has needed to invent something different for the
  USB-vs-control-loop coexistence problem specifically.
- No ODESC-specific firmware source (as opposed to hardware listings) was found beyond what
  the MKS factory fork (`~/Downloads/MKS_ODrive_S-fw-v0.5.1/`) already gives us — consistent
  with `current-sense-research.md` Q5's finding that ODESC boards don't diverge from genuine
  ODrive on the low-level control path.

## Q6 — Least-risky migration path for OUR firmware specifically

Current state: `motor.loopFOC()` / `motor.move()` called from `loop()`
(`firmware-base/src/m5/main.cpp:1633`, gated by dead `g_calibrated`, and the actually-armed
game-FFB path further down at ~1637 via `engine.step()` → `focMotor` → `motor.move()`), at
whatever rate `loop()` happens to spin (~1.8 kHz, starved by `TinyUSBDevice.task()` +
A0/HID + housekeeping all sharing the same iteration).

**Least-risky moves:**
1. Keep `genCurrentRead()`'s DIR-gated JDR logic **exactly as-is** (`main.cpp:193-214`) — it
   is already ISR-safe (pure register reads, no blocking, no `analogRead()`), already fixed
   per `current-sense-research.md`, and is the single piece of this system already proven
   correct on the bench. Do not rewrite it as part of this migration; only change *who calls
   it and how often*.
2. Add a **dedicated `HardwareTimer`** (STM32duino API, e.g. on a timer not already claimed
   by TIM1/PWM, encoder, or the brake chopper's TIM2 — check `firmware-base/lib/base_motor`
   for claimed timers before picking one) running at **8 kHz** (matching ODrive's proven
   ratio to our 24 kHz PWM), with `attachInterrupt()` calling a new, small ISR function.
3. Inside that ISR: `genCurrentRead()` → Clarke/Park at current angle → current PI → inverse
   Park → `motor.setPhaseVoltage(...)`. Do **not** call `motor.loopFOC()` wholesale if it
   internally does anything blocking/non-reentrant (velocity/position sensor filtering,
   etc.) — check `Arduino-FOC/src/BLDCMotor.cpp`'s `loopFOC()` body first; if it's just
   Clarke/Park/PI/inverse-Park + a `setPhaseVoltage()` call it's fine to call directly from
   the ISR as SimpleFOC's own doc does (Q4); if it touches the encoder object non-atomically
   with the quadrature ISRs (`doEncoderA`/`doEncoderB`, `main.cpp:145-146`), that's a second,
   separate risk to check (shared `encoder` state read from two ISR contexts — likely fine
   since both are STM32 atomic word reads/writes, but worth a explicit look before wiring
   this up).
4. Set the new timer's NVIC priority to **0** (`NVIC_SetPriority(TIMx_IRQn, 0)` after
   `attachInterrupt`), matching ODrive's `TIM1_UP_TIM10_IRQn` priority-0 precedent (Q1).
   Explicitly do **not** touch `OTG_FS_IRQn`'s priority — leave TinyUSB at whatever the
   Adafruit_TinyUSB STM32 port sets by default; it only needs to stay numerically below (less
   urgent than) priority 0, which it already will.
5. **Leave `loop()`'s structure unchanged** for everything else: `TinyUSBDevice.task()` calls
   stay exactly where they are (`main.cpp:1251,1597,1663`), `engine.step()` (velocity/torque
   setpoint, outer loop, FFB force computation) stays in `loop()` and now just writes a
   target Iq/Id or torque setpoint that the ISR's PI loop chases, instead of `loop()` calling
   `motor.move()` directly. This is the same fast-ISR/slow-loop split ODrive, SimpleFOC's own
   doc, and OpenFFBoard all converge on (Q1/Q2/Q4/Q5) — nothing about USB servicing changes.
6. **Bench validation order** (don't re-arm force-producing FFB until this is proven,
   per project safety notes): (a) first confirm the ISR fires at the expected 8 kHz with
   FOC math *disabled* (just toggle a spare GPIO pin in the ISR, scope it) and that
   `TinyUSBDevice.task()`/game FFB reports keep flowing normally (repeat the exact ACC/AMS2
   compat check from `drivelab-game-compat-a0` — this is precisely the failure mode that
   memory warns about: heavy work starving USB froze the game before); (b) only then move
   the actual Clarke/Park/PI math in and re-run the inductance-measurement step that
   originally motivated this work (dt≈544µs was too coarse to resolve L/R≈154µs) to confirm
   8 kHz (125µs) actually resolves it; (c) only then re-attempt closed-loop current control
   / re-arm FFB.

---

## Prioritized architecture recommendation

1. **Timer/ISR:** one new dedicated `HardwareTimer` ISR (STM32duino API), **not** SimpleFOC's
   `LowsideCurrentSense`/ADC-ISR path (known to hang on this board — `main.cpp:158-159`) and
   **not** the raw PWM-overflow interrupt (SimpleFOC maintainer guidance: too fast/jittery
   for motion timing, Q4). Pick a timer not already owned by TIM1 (PWM/current-sense
   trigger), the encoder quadrature EXTIs, or TIM2 (brake chopper).
2. **Target rate: 8 kHz**, mirroring ODrive's `CURRENT_MEAS_HZ` derivation from our identical
   24 kHz TIM1 PWM (`RCR`-equivalent 1-in-3 downsample) — proven on this exact board family
   (Q1), comfortably clears our L/R≈154µs time-constant floor (Q2), and matches SimpleFOC's
   own "ideally above 5 kHz" guidance (Q4). Profile actual ISR execution time on real
   hardware before committing; drop to 4-5 kHz only if 8 kHz proves to overrun.
3. **NVIC priority:** FOC timer ISR at priority **0** (highest, matching
   `TIM1_UP_TIM10_IRQn` in genuine ODrive, `tim.c:406`); leave `OTG_FS_IRQn` (TinyUSB) at its
   existing Arduino-core default — do not raise it. This mirrors ODrive's real scheme
   exactly (control-loop timer = 0, every communications peripheral = 5, `Board/v3/Src/*.c`)
   rather than generic RTOS priority-inversion advice.
4. **What moves into the ISR:** DIR-gated `genCurrentRead()` (unchanged) → Clarke/Park at
   rotor angle → current-loop PI (Iq/Id) → inverse Park → `motor.setPhaseVoltage()` (or the
   minimal direct `TIM1->CCRx` write if `setPhaseVoltage()` turns out non-ISR-safe on
   inspection).
5. **What stays in `loop()`:** `TinyUSBDevice.task()` (all three existing call sites,
   unchanged), A0/HID servicing, `engine.step()` (FFB force computation → produces the
   target Iq/Id/torque the ISR's PI chases), telemetry, PID-state heartbeat, live-config
   reapply, cogging table. None of this needs to move or change rate.
6. **Migration order, least risk first:** (a) wire the ISR with FOC math stubbed out, verify
   8 kHz timing on a scope pin and that USB/game-FFB compat is unaffected (repeat the
   ACC/AMS2/EVO bench check); (b) enable current sampling + inductance measurement inside
   the ISR to validate the original motivating problem (dt too coarse to measure L) is
   fixed; (c) only then enable the actual current PI + `setPhaseVoltage()` in the ISR and
   re-validate FFB/game-force bench behavior before considering re-arming force-producing
   modes, per the project's existing "not until M6 bench is proven" gate.

**Sources:**
- `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/MotorControl/low_level.cpp:494-618` (DIR-gated
  ADC callback, `pwm_trig_adc_cb`/`tim_update_cb`)
- `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/MotorControl/axis.cpp:107-115,198+,302-307`
  (ISR→thread signal hand-off, `run_control_loop`)
- `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/Board/v3/Inc/main.h:69-75,171-172`
  (`CURRENT_MEAS_HZ`/`TIM_1_8_RCR` derivation)
- `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/Board/v3/Src/tim.c:406` (`TIM1_UP_TIM10_IRQn`
  priority 0)
- `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/Board/v3/Src/usbd_conf.c:117` (`OTG_FS_IRQn`
  priority 5), plus `adc.c:282/318/344`, `spi.c:161`, `i2c.c:146/148`, `usart.c:151`,
  `dma.c:75-84` (all comms peripherals at priority 5)
- `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/Board/v3/Src/stm32f4xx_hal_msp.c:67-83`
  (`NVIC_PRIORITYGROUP_4`, fixed-priority fault handlers)
- SimpleFOC, "Hard real-time FOC loop using timers" — https://docs.simplefoc.com/real_time_loop
- SimpleFOC community, "Running FOC control loop inside PWM timer overflow interrupt handler
  or ADC sample finish interrupt handler" — https://community.simplefoc.com/t/running-foc-control-loop-inside-to-pwm-timer-overflow-interrupt-handler-or-adc-sample-finish-interrupt-handler/723
- DeepWiki, "Main Loop and FreeRTOS Tasks" (odriverobotics/ODrive, current main branch) —
  https://deepwiki.com/odriverobotics/ODrive/4.1-main-loop-and-freertos-tasks
- DeepWiki, Ultrawipf/OpenFFBoard overview — https://deepwiki.com/Ultrawipf/OpenFFBoard
- Community OpenFFBoard + ODrive ports to MKS XDrive-class boards
- TinyUSB architecture (ISR deferred to `.task()`) — https://docs.tinyusb.org/en/latest/
- Our own firmware: `firmware-base/src/m5/main.cpp:145-146` (encoder quadrature ISRs),
  `:153-215` (DIR-gated current read, `genCurrentRead`/`adc2Config`), `:1243-1663` (`loop()`
  structure, `TinyUSBDevice.task()` call sites, `motor.loopFOC()`/`engine.step()` call sites)
- `docs/reference/current-sense-research.md` (DIR-gate fix this ISR migration must preserve)
- `docs/reference/calibration-currentloop-research.md` (PI gain math the ISR-hosted current
  loop will run)
