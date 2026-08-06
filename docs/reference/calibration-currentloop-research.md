# Research: encoder electrical-zero calibration & current-loop tuning (why the drag-current sweep overheats or oscillates)

> ⚠️ **Snapshot de pesquisa.** Escrito quando a FOC do DriveLab era caseira, construída sobre
> SimpleFOC. O projeto migrou depois para a **FOC do ODrive** (vendorizada, MIT). As menções a
> SimpleFOC aqui descrevem o que existia na época: o raciocínio e as medições continuam válidos,
> a implementação não é mais essa.


Companion to `current-sense-research.md` (that one is about the ADC sampling *architecture*;
this one is about *how hard/how* to drive the rotor during offset calibration, and how to
size the current-loop PI gains). Same board class (MKS ODRIVE-S v3.6 = genuine ODrive
v3.6 hardware fork), same local source of truth: `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/`.

**Bottom line up front:** ODrive's own `run_offset_calibration()` is **not** a closed-loop
current-controlled drag at all — it's a pure **open-loop voltage sweep**, but the voltage is
never a guessed constant like our `kCalV=6.0f`. It's computed from Ohm's law,
`voltage_magnitude = calibration_current * phase_resistance`, using the motor's *actual
measured* phase resistance. That single design choice sidesteps both of our problems at
once: there is no PI loop running during the sweep (nothing to oscillate), and the voltage
is auto-scaled down for a high-resistance motor like ours (so it never over-drives current
into overheat territory the way a fixed 6 V does on a ~1 Ω motor). Separately, ODrive derives
its **current-controller PI gains** mechanically from measured R/L via a textbook pole-placement
formula — never hand-tuned — and SimpleFOC's own docs prescribe the identical formula. Our
`P=1.5 / I=60` are hand-picked and, when checked against R≈1 Ω, imply two *wildly different*
bandwidths from the P term vs the I term (a ~60x mismatch) — which is the likely mechanistic
cause of the oscillation, independent of the forced-angle scheme itself.

---

## Q1 — ODrive's `run_offset_calibration`: current or voltage? How does it dodge heat AND instability?

**Answer: open-loop voltage, magnitude computed via Ohm's law from a small target current —
no closed-loop current controller runs during this step at all.**

`~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/MotorControl/encoder.cpp:183-280`,
`Encoder::run_offset_calibration()`:

```cpp
// encoder.cpp:197-203
float voltage_magnitude;
if (axis_->motor_.config_.motor_type == Motor::MOTOR_TYPE_HIGH_CURRENT)
    voltage_magnitude = axis_->motor_.config_.calibration_current * axis_->motor_.config_.phase_resistance;
else if (axis_->motor_.config_.motor_type == Motor::MOTOR_TYPE_GIMBAL)
    voltage_magnitude = axis_->motor_.config_.calibration_current;
else
    return false;
```

Then it just spins a slowly-advancing field reference and applies that fixed voltage
magnitude at that angle, straight through `enqueue_voltage_timings()` — **no PID, no
current feedback loop, no Iq/Id measurement in the loop at all**:

```cpp
// encoder.cpp:220-232 (forward sweep; backward sweep at :259+ is the mirror image)
axis_->run_control_loop([&]() {
    float phase = wrap_pm_pi(config_.calib_scan_distance * (float)i / (float)num_steps - ...);
    float v_alpha = voltage_magnitude * our_arm_cos_f32(phase);
    float v_beta  = voltage_magnitude * our_arm_sin_f32(phase);
    if (!axis_->motor_.enqueue_voltage_timings(v_alpha, v_beta)) return false;
    encvaluesum += shadow_count_;
    return ++i < num_steps;
});
```

Sequencing: `start_lock_duration=1.0s` initial lock at `voltage_magnitude` and phase 0
(encoder.cpp:184, 206-212), then forward sweep, then backward sweep, encoder position
averaged forward+backward (ODrive-style, matches what we already do). Sweep geometry comes
from `Encoder::Config_t` defaults (`encoder.hpp:26-28`):

```cpp
float calib_range        = 0.02f;        // accuracy required to pass CPR check
float calib_scan_distance = 16.0f * M_PI; // rad electrical
float calib_scan_omega    = 4.0f * M_PI;  // rad/s electrical
```

→ `num_steps = calib_scan_distance/calib_scan_omega*current_meas_hz` (encoder.cpp:185),
i.e. each direction takes `calib_scan_distance/calib_scan_omega = 4 s`, so **~1 s lock + 4 s
fwd + 4 s bwd ≈ 9 s total** — same order of magnitude as our 12 s, so duration isn't the
lever here.

**`calibration_current` default is 10 A** (`motor.hpp:44`, sized for ODrive's typical
low-resistance hobby-king-class motors) with `resistance_calib_max_voltage=2.0f` (`motor.hpp:45`)
capping the *separate* resistance-measurement step (Q2) — but `calibration_current` is a
user-configurable field, explicitly meant to be tuned per motor. **For a ~1 Ω motor, dialing
`calibration_current` down to e.g. 0.3–0.5 A automatically yields `voltage_magnitude ≈
0.3–0.5 V`** — nothing like our fixed 6 V — while still guaranteeing (via Ohm's law, since
this is DC-resistance-dominated at near-zero speed) that the actual steady current tracks
close to that target, self-limited by construction (no integrator to wind up, no overshoot
possible).

**Why this avoids both of our problems:**
- **Heat (problem 1):** the drive voltage is *derived from the target current and the
  motor's own measured resistance*, not guessed. A motor with higher R gets automatically
  less voltage for the same target current — the opposite of what we did (same 6 V regardless
  of R, tuned empirically against cogging on what was presumably a different/lower-R test).
- **Instability (problem 2):** there is no feedback loop running during this step at all,
  so there is nothing to oscillate. ODrive doesn't try to make a current *controller* stable
  at low bandwidth for this step — it skips the controller entirely and uses feedforward.

**Separate mechanism — ODrive's closed-loop drag (`run_lockin_spin`, NOT used for offset
calibration, but relevant to our problem-2 approach):** used for direction-find,
"sensorless ramp", and general jogging (`axis.cpp:552,554,548`), this *does* use the real
current controller via `motor_.update(torque, phase, vel)`, but it never starts at full
target current + arbitrary angle. It **spirals up magnitude and phase together**:

```cpp
// axis.cpp:231-242
lockin_state_ = LOCKIN_STATE_RAMP;
float x = 0.0f;
run_control_loop([&]() {
    float phase = wrap_pm_pi(lockin_config.ramp_distance * x);
    float torque = lockin_config.current * motor_.config_.torque_constant * x;   // <-- x ramps 0→1
    x += current_meas_period / lockin_config.ramp_time;                          // ramp_time default 0.4s
    if (!motor_.update(torque, phase, 0.0f)) return false;
    return x < 1.0f;
});
```

(`Axis::default_calibration()`/`default_sensorless()`, `axis.cpp:55-81`: `ramp_time=0.4s`,
`ramp_distance=π rad`.) The comment is explicit: *"Spiral up current for softer rotor
lock-in."* This is a plausible second contributor to our oscillation (see Q6): our
`FOC_FWD`/`FOC_BWD` code (`firmware-base/src/m5/main.cpp:574-598`) hands the current PI a
**step** target (`kCalCurrentA=0.4f` constant, `main.cpp:400,587,610`) from the very first
tick of each direction, with no analogous magnitude/phase ramp-in.

---

## Q2 — ODrive's current-controller gain formula (derived from R/L, not hand-tuned)

**Answer:** `Motor::update_current_controller_gains()`, `motor.cpp:60-65`:

```cpp
void Motor::update_current_controller_gains() {
    current_control_.p_gain = config_.current_control_bandwidth * config_.phase_inductance;
    float plant_pole = config_.phase_resistance / config_.phase_inductance;
    current_control_.i_gain = plant_pole * current_control_.p_gain;   // == bandwidth * phase_resistance
}
```

i.e. **`p_gain = bw[rad/s] · L[H]`**, **`i_gain = bw[rad/s] · R[Ω]`** — a standard
pole-placement/pole-cancellation design (place the closed-loop pole such that the plant's
own `R/L` pole is cancelled by the PI zero, then set the loop bandwidth directly via the
gain magnitude). Default `current_control_bandwidth = 1000.0f` rad/s (`motor.hpp:58`, ≈159 Hz).
It is invoked automatically — the setters for R, L, and bandwidth all call it as a side
effect (`motor.hpp:74-76`: `set_phase_inductance`, `set_phase_resistance`,
`set_current_control_bandwidth` each call `parent->update_current_controller_gains()`),
and once more explicitly at the end of `run_calibration()` (`motor.cpp:297`). **There is no
code path in ODrive where a user hand-enters `p_gain`/`i_gain` directly for normal use** —
the fields exist only as the *output* of this function.

**Where R and L come from — both measured by open-loop routines, run before the current
controller is ever engaged (`Motor::run_calibration()`, `motor.cpp:283-301`):**

- `measure_phase_resistance(test_current, max_voltage)` (`motor.cpp:214-243`): also
  open-loop feedforward (not the real current PI) — a simple fixed-rate integrator,
  `test_voltage += kI*dt*(test_current - Ialpha)` with `kI=10 (V/s)/A` (a throwaway
  measurement-only integrator, not `p_gain`/`i_gain`), run for 3 s, capped at
  `resistance_calib_max_voltage` (default 2 V, `motor.hpp:45`), then
  `R = test_voltage / test_current` (`motor.cpp:240`).
- `measure_phase_inductance(voltage_low, voltage_high)` (`motor.cpp:245-280`): applies an
  alternating square wave (`±resistance_calib_max_voltage`) for 5000 cycle-pairs and derives
  `L = 0.5·(V_high−V_low) / (dI/dt)` from the current response (`motor.cpp:269-275`).
  Sanity-checked against `2 µH < L < 4 mH` (`motor.cpp:277`).
- Both run **before** `update_current_controller_gains()` is called (`motor.cpp:287-297`),
  i.e. gains are only ever computed *after* R and L are known, never guessed first.

---

## Q3 — SimpleFOC's own current-PI guidance

**Answer: the identical pole-placement formula**, from `docs.simplefoc.com/tuning_current_loop`:

> `P = L × (2π × bandwidth)`, `I = R × (2π × bandwidth)` (bandwidth in Hz — this is exactly
> ODrive's `p_gain = bw[rad/s]·L`, `i_gain = bw[rad/s]·R` with the rad/s↔Hz conversion baked
> into the `2π`). Library helper `motor.tuneCurrentController(bandwidth)` automates it.
> Recommended bandwidth ≈ **5–10 % of the `loopFOC()` call frequency**. Current LPF time
> constant `Tf = 1/(2π·5·bandwidth)`. Troubleshooting table: oscillation → bandwidth too
> high → lower it; weak torque → bandwidth too low → raise it; if `loopFOC()` itself is too
> slow, `tuneCurrentController` returns an error code rather than silently producing bad gains.

`docs.simplefoc.com/foc_current_torque_mode` reiterates: *"If you know your motor's Phase
Resistance (R) and Inductance (L), the library can calculate these PID gains for you"* — the
docs explicitly frame R/L-derived gains as the intended path, with hand-tuned P/I ranges
("2.0–20.0" / "300–5000") shown only as illustrative examples for *their* demo motors, not
as universal defaults.

**Are our `P=1.5 / I=60` sane for a ~1 Ω / low-L motor at ~14 kHz?** Checked against the
formula, they're **internally inconsistent with each other**, which is the real problem,
not just "too high" or "too low" in isolation. In a correctly-derived pair the ratio
`P/I = L/R` (the motor's electrical time constant) is fixed by the motor, so P and I must
imply the *same* bandwidth:

- From `I = R·2π·bw` with `R ≈ 1 Ω`: `bw ≈ 60/(2π·1) ≈ 9.5 Hz` — a very gentle, sluggish
  target.
- From `P = L·2π·bw` with a plausible hoverboard-class phase inductance of order
  `L ≈ 100–400 µH`: `bw ≈ 1.5/(2π·L) ≈ 600–2400 Hz` — a much more aggressive target, in
  fact close to (or above) the "5–10 % of loop frequency" ceiling for a ~14 kHz current loop
  (≈700–1400 Hz).

That's roughly a **60–250× mismatch** between what the P term and the I term each imply
about the intended loop bandwidth. Concretely: the proportional term is fast enough to react
strongly to any transient (angle-forcing discontinuity, cross-axis Id/Iq coupling, sensor
noise), but the integral term is far too weak to have settled/cancelled the plant's own R/L
pole at that speed — a textbook recipe for ringing. This matches the task's own framing that
these gains were "hand-picked... blind guess," not derived from a measured R/L pair at all.

---

## Q4 — OpenFFBoard (Ultrawipf/OpenFFBoard): alignment approach

**Limited public detail; architecturally different from us, so only partially comparable.**
OpenFFBoard's FOC path for its most-used motor drivers is built around the **TMC4671**
motion-control IC (Trinamic), which has its own on-chip FOC current controller with its own
built-in R/L identification and current-PI auto-tuning ("ACIN"-style calibration) — this is
fundamentally different from a from-scratch SimpleFOC/ODrive-style software current loop:
the analogous "derive gains from R/L" work is done inside the TMC4671 silicon/firmware
library, not in OpenFFBoard's own application code. Public wiki/FAQ pages
(`github.com/Ultrawipf/OpenFFBoard/wiki/FAQ`, `.../wiki/Commands`) confirm the general shape
of encoder alignment (align to electrical-0, resolve unknown natural direction by moving a
few electrical revolutions forward/backward — same idea as ODrive's `run_direction_find()`,
`encoder.cpp:152-177`) but **do not publish current/voltage magnitudes, calibration-current
values, or heat-mitigation specifics** for the TMC4671 path. Given the architecture
difference (dedicated FOC silicon vs. our software current loop on the same MCU core doing
everything else), this line of research had low yield beyond confirming the alignment
*concept* is the same as ODrive's — no additional numeric guidance to import.

---

## Q5 — ODESC / MKS-clone specifics, FFBeast

**No divergent calibration algorithm found; these boards ride on stock ODrive firmware.**
Community sources describe the ODESC v4.2 (a closely related clone to our MKS ODRIVE-S v3.6)
as running firmware "based on the open-source ODrive project... matching ODrive original
firmware," i.e. the same `run_offset_calibration`/`run_calibration` code paths documented in
Q1/Q2 above, not a bespoke calibration scheme — consistent with what the earlier
current-sense report already established for the MKS factory firmware (byte-for-byte
ODrive v0.5.x architecture on this board family). FFBeast's own docs
(`ffbeast.github.io/docs/en/connection_odrive.html`,
`ffbeast.github.io/docs/en/hardware_controller.html`) confirm ODESC/XDrive/MKS-class boards
as supported "ODrive 3.6"-schematic targets, but only cover **wiring** (there's a specific
ODESC-only note about input-filtering hardware modification needed, unrelated to
calibration) — no published detail on FFBeast's own calibration-current or gain-tuning
choices. Net: **there is no separate "ODESC calibration algorithm" to reconcile against** —
genuine ODrive's source (which we already have locally) is the correct and sufficient
reference for this whole board family, including ours.

---

## Q6 — The heat-vs-stability tradeoff: what's the standard approach we're missing?

Yes — **ODrive doesn't solve "heat vs. stability" as one problem, it avoids the tradeoff by
never putting those two costs in the same step**:

1. **Encoder electrical-zero offset cal = open-loop voltage feedforward, sized by Ohm's law
   from a small target current and the motor's *measured* resistance** (Q1). No PI loop →
   no instability possible by construction. Small `calibration_current` (tunable, not fixed
   at whatever fixed voltage "feels right") → automatically low power (`P ≈ I²·R`) on a
   high-R motor like ours, so no thermal runaway even over several seconds.
2. **R and L are measured first, with their own separate open-loop routines**
   (`measure_phase_resistance`/`measure_phase_inductance`, Q2) — also feedforward, also
   never engage the closed current PI, also bounded by an explicit max-voltage cap
   (`resistance_calib_max_voltage`).
3. **Only after 1 and 2 are done does ODrive ever run the real closed-loop current
   controller** — and even then (`run_lockin_spin`, Q1) it ramps both current magnitude and
   phase together over ~0.4 s before demanding full target current at an arbitrary angle,
   specifically to avoid exciting a fresh loop with a step input.
4. **Gains for that closed loop are never hand-picked** — they're `bw·L` / `bw·R`, computed
   the instant R/L are known (Q2/Q3), so instability from mismatched/guessed gains (our Q3
   finding) is structurally impossible in ODrive's own flow.

---

## Prioritized recommendation for our calibration

**1. (Highest leverage — do this first) Replace the closed-loop-current forced-angle sweep
(`stage1aTick` `FOC_FWD`/`FOC_BWD`, `firmware-base/src/m5/main.cpp:574-598`) with ODrive's
actual method: an open-loop voltage sweep, voltage sized by Ohm's law, not guessed.**
Concretely:
   - Measure our motor's real `phase_resistance` once, using an open-loop routine modeled on
     `measure_phase_resistance()` (`motor.cpp:214-243`) — feedforward integrator toward a
     small `test_current` (e.g. 0.3–0.5 A), capped at a low `max_voltage` (e.g. 1–2 V), no
     PID involved, ~3 s, cool by construction.
   - Compute `voltage_magnitude = calibration_current * measured_R` (mirror
     `encoder.cpp:199`) with `calibration_current` kept small — **0.3–0.5 A**, matching the
     current target we already validated as gentle/safe (`kCalCurrentA=0.4f`,
     `main.cpp:400`). On a ~1 Ω motor this computes to **~0.3–0.5 V**, nothing like the fixed
     `kCalV=6.0f` (`main.cpp:392`) we're using today, and self-scales correctly if R turns
     out different than assumed.
   - Drive the FWD/BWD sweep with plain `motor.setPhaseVoltage(voltage_magnitude, 0, ang)`
     (our existing `setPhaseVoltage`-based path, i.e. "problem 1" method) at this
     *right-sized* voltage instead of the current-PI path ("problem 2" method). This
     eliminates the oscillation risk entirely (no loop to be unstable) and should keep power
     dissipation on the order of `I²R ≈ 0.5²·1 ≈ 0.25 W` continuous — far below whatever
     tripped the DRV at 6 V/4–6 A (~30 W class).
   - Sweep geometry can stay close to what we have (`kScanMs=6000`ms/direction,
     `kCalOmega=4`rad/s elec, `main.cpp:392-396`), though ODrive's own default
     `calib_scan_omega=4π rad/s` (`encoder.hpp:28`) is ~4× faster than ours — shortening our
     sweep proportionally would cut total heat-soak time further once the drive method is
     fixed, but is a secondary/optional tweak.

**2. If/when a closed-loop current drag is wanted again (e.g. to match ODrive's
`run_lockin_spin` more closely, or for actual torque-mode operation later), derive
`PID_current_q/d.P` and `.I` from measured R/L instead of hand-picking them:**
   - After measuring `phase_resistance` (step 1) and `phase_inductance` (mirror
     `measure_phase_inductance()`, `motor.cpp:245-280`: alternating ± low-voltage square wave,
     derive `L` from `dI/dt`), compute:
     `P = L · 2π · bw`, `I = R · 2π · bw` (SimpleFOC formula, Q3 — identical to ODrive's
     `p_gain = bw·L`, `i_gain = bw·R`, Q2, just Hz vs rad/s).
   - Pick `bw` as 5–10 % of our actual current-loop tick rate (~14 kHz per the SWD log) →
     start around **bw ≈ 700–1000 Hz** (toward the low end, since we're recovering from
     instability, not chasing max bandwidth).
   - This directly fixes the diagnosed root cause in Q3: our current `P=1.5/I=60` imply
     bandwidths ~60–250× apart from each other (9.5 Hz from I vs. 600–2400 Hz from P assuming
     plausible L) — internally inconsistent regardless of which one is "right." Any gains
     computed from the *same* measured R, L, bw pair will not have this mismatch.
   - Re-run this computation any time R or L changes (temperature drift, different motor) —
     never leave `P`/`I` as static hand-tuned constants for the current loop, matching both
     ODrive's and SimpleFOC's stance that these should always be *derived*, not tuned by
     feel.

**3. If a closed-loop forced-angle sweep is still desired as the calibration method
(rather than switching to open-loop per #1), at minimum adopt ODrive's soft-start
discipline from `run_lockin_spin`** (`axis.cpp:231-242`): ramp both the current target
*and* the starting phase together from zero over ~0.3–0.5 s before entering the
constant-sweep-rate phase, instead of the current step-target-from-tick-zero
(`kCalCurrentA` used as a fixed setpoint from the first control-loop iteration,
`main.cpp:587,610`). This is a smaller, independent fix on top of #2 — it removes the
step-discontinuity trigger even if gains are otherwise correct.

**Priority order:** #1 (switch to right-sized open-loop voltage for the *offset
calibration specifically* — removes the oscillation risk entirely and is a small, mechanical
change) → #2 (derive current-PI gains from measured R/L, needed for any future closed-loop
current-mode operation, including real FFB torque control, not just calibration) → #3
(soft-start ramp, useful hardening if/when the closed-loop path is used again). Do **not**
re-arm the bench for calibration testing until at least #1 is implemented, per the existing
project safety note on the M6 FFB bench blocker.

---

## File/line index

- ODrive open-loop offset calibration: `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/MotorControl/encoder.cpp:183-280` (voltage formula: `:197-203`)
- ODrive encoder config defaults (scan distance/omega/range): `encoder.hpp:26-28`
- ODrive closed-loop soft-start lock-in spin: `axis.cpp:231-271`, defaults `axis.cpp:55-81`
- ODrive current-controller gain formula: `motor.cpp:60-65`; default bandwidth `motor.hpp:58`; auto-invoke on R/L/bw setters `motor.hpp:74-76`
- ODrive R measurement: `motor.cpp:214-243`; L measurement: `motor.cpp:245-280`; orchestration `run_calibration()`: `motor.cpp:283-301`
- ODrive `calibration_current`/`resistance_calib_max_voltage` defaults: `motor.hpp:44-45`
- SimpleFOC current-loop tuning formula/bandwidth guidance: https://docs.simplefoc.com/tuning_current_loop
- SimpleFOC FOC current torque mode docs: https://docs.simplefoc.com/foc_current_torque_mode
- Our current offset-calibration implementation (to be reworked per recommendation #1): `firmware-base/src/m5/main.cpp:392-400` (constants), `:483-494` (`restartScan`), `:559-620` (`stage1aTick` FOC_LOCK/FWD/BWD), `:542-548` (hand-picked current PI gains)
- ODESC/FFBeast board-family context: https://ffbeast.github.io/docs/en/connection_odrive.html , https://ffbeast.github.io/docs/en/hardware_controller.html
- OpenFFBoard alignment/FAQ (TMC4671-based, limited public detail): https://github.com/Ultrawipf/OpenFFBoard/wiki/FAQ , https://github.com/Ultrawipf/OpenFFBoard/wiki/Commands
