# Research: phase-current sense reading garbage/zero (ADC2 injected, TIM1 TRGO)

Bench symptom recap: DRV8301 G40, 500µΩ shunts (~0.0403 A/count), ADC2 injected on
TIM1_TRGO=Update. JDR1/JDR2 intermittently read near-zero (or mid-scale garbage like
~1180) for several consecutive PWM periods → decoded as ~-80A/phase → false overcurrent.
Waiting on JEOC doesn't help (conversion *completes*, value is genuinely wrong/near-zero
at the sample instant).

**Bottom line up front:** this is not a DRV8301 hardware fault and not (primarily) a
sample-*instant* problem. It is an architecture bug in our own current-read path
(`firmware-base/src/m5/main.cpp`): we sample **every** TIM1 Update event (there are two
per PWM period in center-aligned mode — one at the valley/zero-vector where the low-side
shunts see real phase current, one at the peak/all-high-side vector where the amp output
is legitimately near zero) with **no discrimination between the two**, and on top of that
we fully re-program the ADC2 injected registers (`CR1/CR2/SMPR1/JSQR`) on *every single
read call*, asynchronously to the conversion state machine. Both genuine ODrive and
SimpleFOC's own STM32 low-side driver solve exactly this by gating on the timer's
direction bit / a repetition-counter trick — which our hand-rolled path does not do.

---

## Q1 — Sample timing: when should the ADC fire, and what do ODrive/SimpleFOC do?

**Answer:** for low-side shunt sensing with center-aligned PWM, the ADC injected
conversion must be triggered so that it *lands* in the window where **all low-side FETs
are ON simultaneously** — the "valley" of the center-aligned counter (CNT≈0, the SVM
zero vector). That's the only instant where the shunt-amp output represents true phase
current with no switching transient. SimpleFOC's own docs state this plainly:

> "the current passing through the shunt resistor is phase current only if the
> corresponding low side mosfet is ON" — https://docs.simplefoc.com/low_side_current_sense

**How genuine ODrive (== the MKS factory fw fork, confirms our board) does it:**
`~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/Board/v3/Src/tim.c`

- TIM1/TIM8: `CounterMode = TIM_COUNTERMODE_CENTERALIGNED3`, `Period = TIM_1_8_PERIOD_CLOCKS`
  (3500 @ 168 MHz → 24 kHz switching), `RepetitionCounter = TIM_1_8_RCR` (=2),
  `MasterOutputTrigger = TIM_TRGO_UPDATE` (tim.c:95-96,120,325-326,334).
- `Board/v3/Src/adc.c`: ADC1/2/3 injected group `ExternalTrigInjecConv =
  ADC_EXTERNALTRIGINJECCONV_T1_TRGO`, rising edge (adc.c:113-118,165-170,217-222). So yes
  — like us, they trigger the injected group off **TIM1 Update**, not a CC4 compare.
- The key piece we're missing: **`MotorControl/low_level.cpp`**, `pwm_trig_adc_cb()`
  (line 494) and `tim_update_cb()` (line 592) both read
  `bool counting_down = htim->Instance->CR1 & TIM_CR1_DIR;` and explicitly branch:
  > "If the corresponding timer is counting up, we just sampled in SVM vector 0, i.e.
  > real current. If we are counting down, we just sampled in SVM vector 7, with zero
  > current." (low_level.cpp:506-507, 594-595)
  - `counting_down == false` (DIR just flipped to up-counting, i.e. we're right after the
    valley) → **real current sample**, goes through `phase_current_from_adcval()` into
    `current_meas_.phB/phC` and drives FOC (low_level.cpp:563-581).
  - `counting_down == true` (right after the peak) → **DC_CAL sample**, only used to
    continuously low-pass-filter the ADC/amp zero-offset (`DC_calib_`, calib_tau=0.2s,
    low_level.cpp:495-496,582-588). It is *never* fed into the Park transform.
  - `TIM_1_8_RCR = 2` means the hardware repetition counter only lets the *real* Update
    event (and hence the interrupt / one of the two TRGO pulses per switching period)
    through once every 3 direction flips, which is how ODrive decouples the 24 kHz
    switching rate from the 8 kHz current-control rate
    (`CURRENT_MEAS_HZ = TIM_1_8_CLOCK_HZ / (2*TIM_1_8_PERIOD_CLOCKS*(RCR+1))`,
    `Board/v3/Inc/main.h:171-172`).

So: **TIM1 Update *is* the correct trigger** (not a CC4 offset) — but only *half* of the
Update events are usable current samples, and telling them apart requires reading
`TIM1->CR1 & TIM_CR1_DIR` (or equivalent) at read time. ODrive never skips this check.

**How SimpleFOC's own generic STM32 low-side driver does the same thing** (this is the
library our firmware links against, `firmware-base/.pio/libdeps/m5/Arduino-FOC/...`):

- `drivers/hardware_specific/stm32/stm32_mcu.cpp:120,123-124`: PWM timers are configured
  `TIM_COUNTERMODE_CENTERALIGNED2` with `RepetitionCounter = 1` when the timer supports it.
- `current_sense/hardware_specific/stm32/stm32_adc_utils.cpp`,
  `_initTimerInterruptDownsampling()` (line 447): reads `CR1 & TIM_CR1_DIR` to determine
  `next_event_high_side`, then **presets `CNT`/`DIR` on every PWM timer** so that, combined
  with `RepetitionCounter=1`, the *only* Update/TRGO event that survives the repetition
  counter is the one at the low-side (valley) window (lines 449-475). If the timer has no
  repetition counter it falls back to a software downsample that skips every other
  ADC-injected-complete interrupt (lines 476-491, `_handleInjectedConvCpltCallback`,
  line 495).

So SimpleFOC's answer is the same as ODrive's in spirit: use the repetition counter (or a
software skip-one-in-two) to *structurally* guarantee that only the valley sample is ever
delivered to the current-sense read function — not a statistical/threshold filter after
the fact.

**Where our own firmware diverges:** `firmware-base/src/m5/main.cpp:158-213`. The code's
own comments record that SimpleFOC's `LowsideCurrentSense` (the driver described above)
**hangs on `init()` on this board** ("O LowsideCurrentSense do SimpleFOC PENDURA o init
nesta placa" — main.cpp:158-159), so a hand-rolled replacement (`adc2Config()` /
`genCurrentRead()` / `genCurrentInit()`, main.cpp:180-213) was written instead, wired in
via `GenericCurrentSense` (main.cpp:215). This hand-rolled path:

```cpp
// main.cpp:187-191
ADC2->CR1 = ADC_CR1_SCAN;                                     // scan (2 inj), SEM interrupt
ADC2->CR2 = (1u << 16) | (1u << 20) | ADC_CR2_ADON;           // JEXTSEL=TIM1_TRGO, JEXTEN=rising, ADON (junto)
ADC2->SMPR1 = (3u << 0) | (3u << 3);                          // 56 ciclos ch10/11
ADC2->JSQR = (1u << 20) | (11u << 15) | (10u << 10);          // JL=1: JSQ3=10→JDR1, JSQ4=11→JDR2
TIM1->CR2 = (TIM1->CR2 & ~TIM_CR2_MMS) | (2u << 4);           // TRGO = update
```
```cpp
// main.cpp:194-201
static PhaseCurrent_s genCurrentRead()
{
    adc2Config();   // reconfig completo por leitura → sobrevive a qualquer analogRead que de-inicializou o ADC
    const float ib = ((float)ADC2->JDR1 - g_off10) * kAmpsPerCount;
    const float ic = ((float)ADC2->JDR2 - g_off11) * kAmpsPerCount;
    ...
}
```

This never reads `TIM1->CR1 & TIM_CR1_DIR` (or any equivalent). It triggers on **every**
TIM1 Update — both the valley (real-current) and the peak (near-zero, all-high-side)
vector — and hands whatever is in JDR1/JDR2 straight to the current controller with no
discrimination. That alone reproduces the reported symptom almost exactly: on a
zero/light-load bench current (0.4–5 A), the "real" valley sample and the "DC_CAL" peak
sample both sit close to the 2048-count midscale, and roughly every other read is the
*wrong one of the two* — decoded as `(counts-2048)*0.0403`, a peak-window read of a
near-zero raw count (which is *not* offset-corrected the way ODrive's `DC_calib_` is)
looks exactly like the "~3-25 raw counts → -80A" failure mode described in the task.

Sources: [SimpleFOC low-side current sense docs](https://docs.simplefoc.com/low_side_current_sense),
[ODrive low_level.cpp (GitHub mirror of this fw generation)](https://github.com/madcowswe/ODrive/blob/master/Firmware/MotorControl/low_level.cpp).

---

## Q2 — "Amp reads 0/garbage for several cycles": known issue? Root cause here?

Not a known DRV8301 erratum, and not really an ODrive issue either — because ODrive's
design (Q1) makes it structurally impossible: it never asks "was this ADC reading
correct?", it asks "was this even a real-current sample?" *before* trusting it.

Two compounding problems in our path, both firmware-only:

1. **No valley/peak discrimination (the big one).** As shown above, every TIM1 Update
   triggers an injected conversion, but only the valley one is real current — the peak
   one is a legitimate, correctly-converted, near-zero reading (SVM vector 7, all
   high-side FETs on, no return path through the shunts). Our code can't tell them apart,
   so ~half of the "current" samples it feeds to FOC are actually zero-current samples
   misinterpreted as real, and vice versa. This alone explains "value is ~0, not a stale
   read" (task's own diagnosis) — it genuinely *is* ~0 at that instant, just not the
   instant we should have used.

2. **`adc2Config()` re-programs `CR1/CR2/SMPR1/JSQR` on every single read**
   (main.cpp:196, called from `genCurrentRead()` every control-loop tick), because
   `analogRead()` elsewhere (telemetry: bus voltage, FET/motor NTC — all via the Arduino
   core, which touches the shared `ADC->CCR` common-control register and/or re-inits
   ADC1) leaves ADC2 in an unknown state. Rewriting the injected sequence/config
   registers is **not synchronized to TIM1's counter or to whether an injected conversion
   is currently in flight** — STM32 RM0090 flags modifying `JSQR`/trigger config while a
   conversion is armed/in-progress as producing undefined results. This is a plausible
   source of the "intermediate garbage, e.g. ~1180" counts the task description mentions
   — those aren't valley/peak-vector values, they're the ADC caught mid-reconfiguration.
   Genuine ODrive/SimpleFOC never touch the injected config after `init()` — they
   configure once and let hardware retrigger every period.

**How ODrive handles "bad" samples once armed:** it doesn't statistically filter. It has
one blunt safety net — a per-phase magnitude trip, `FOC_current()`:
```cpp
// motor.cpp:340-344
if (std::abs(current_meas_.phB) > ictrl.overcurrent_trip_level || std::abs(current_meas_.phC) > ictrl.overcurrent_trip_level) {
    set_error(ERROR_CURRENT_SENSE_SATURATION);
    return false;
}
```
That's it — no range clamp, no "hold last good value." It can afford this precisely
*because* the sampling pipeline structurally guarantees clean samples (Q1), so a trip
means a real fault, not a sampling artifact. This directly informs the answer to Q4 below.

---

## Q3 — Low-current SNR: gain selection

ODrive does **not** hardcode G40. `DRV8301_setup()` (`MotorControl/motor.cpp:68-121`)
auto-picks the DRV8301 SPI gain from a config value, snapping down to the largest gain
that still covers the requested range:

```cpp
// motor.cpp:83-102
std::array<std::pair<float, DRV8301_ShuntAmpGain_e>, 4> gain_choices = {
    std::make_pair(10.0f, DRV8301_ShuntAmpGain_10VpV),
    std::make_pair(20.0f, DRV8301_ShuntAmpGain_20VpV),
    std::make_pair(40.0f, DRV8301_ShuntAmpGain_40VpV),
    std::make_pair(80.0f, DRV8301_ShuntAmpGain_80VpV)
};
...
phase_current_rev_gain_ = 1.0f / gain_snap_down->first;
```
driven by `config_.requested_current_range` (default 60 A, `motor.hpp:57`) — i.e. a user
who declares they only need a handful of amps gets automatically bumped to G80 (or would,
if the array went higher — 80 V/V is ODrive's max on this DRV8301 wiring). The gain is
then written into the DRV8301 SPI register directly: `local_regs->Ctrl_Reg_2.GAIN =
gain_snap_down->second;` (motor.cpp:115). No oversampling/dithering trick is used —
ODrive's whole answer to low-current SNR is "pick the highest gain the DRV8301 supports
for your expected current range."

At **G80** on our 500µΩ shunts, scale becomes ≈0.0201 A/count (half of G40's 0.0403),
doubling resolution for the same ±3.3 V/12-bit ADC — directly useful for our 0.4–5 A
range (full-scale would drop from ±82 A to ±41 A, still far more headroom than needed).

**Our firmware already has the gain enum and the SPI write path**, but hardcodes G40 in
two independent places that would both need to change together:
- `firmware-base/src/m5/main.cpp:155` — `FocCurrent focCurrent(Drv8301Gain::G40)` (this
  object is actually dead code now — `genCurrentRead()` bypasses it, see Q1/Q2).
- `firmware-base/src/m5/main.cpp:1077` — `drv.begin(spi3, ..., Drv8301Gain::G40)` (the
  **real** SPI register write that sets the physical amp gain).
- `firmware-base/src/m5/main.cpp:169` — `kAmpsPerCount = 3.3f/4096.0f/(40.0f*0.0005f)`
  hardcodes the 40.0f gain factor used by the *actual* current-read path
  (`genCurrentRead()`), independently of the `Drv8301Gain` enum above.

`firmware-base/lib/base_motor/drv8301.h:72` already declares
`enum class Drv8301Gain : uint8_t { G10=0, G20=1, G40=2, G80=3 }` and
`drv8301ControlReg2()` (line 115) encodes it into the SPI register — the plumbing for G80
exists, it's just not selected.

---

## Q4 — Sample validation: ODrive vs. our "threshold + hold" approach

ODrive validates structurally, not statistically:
1. Only the DIR-gated valley sample is ever handed to the current controller (Q1) — bad
   samples are excluded *by construction*, not detected after the fact.
2. A continuously-updated DC offset (`DC_calib_`, 0.2 s time constant) compensates
   thermal/amp drift automatically instead of a one-shot calibration
   (`low_level.cpp:495-496,582-588`).
3. The only runtime check is the blunt overcurrent-trip magnitude check
   (`motor.cpp:340-344`) — it **disarms** (`set_error` + `return false`), it does not
   attempt to filter/hold/interpolate through a bad sample. Holding "last good value"
   through what might be a real fault is exactly what ODrive avoids — a trip is meant to
   be trusted, which it can be, because the pipeline feeding it is clean.

This is why our current "reject out-of-range + hold last value" approach is fighting a
losing battle: it's trying to statistically clean up a signal that's dirty *by
architecture* (mixing two legitimately different physical samples into one stream). Fix
the architecture (Q1/Q2) first; the threshold-filter approach should become unnecessary,
just like it is for ODrive.

---

## Q5 — ODESC/MKS clone divergence on the current-sense path

None found. The MKS factory firmware (`~/Downloads/MKS_ODrive_S-fw-v0.5.1/`) is a
byte-for-byte-architecture fork of ODrive v0.5.x for the v3.6 board: same ADC pin
mapping (ADC1 ch6 = Ibus/aux, ADC2 ch13/10 = phase B/C on M0, ADC3 ch12/11 = phase B/C on
M1 — `Board/v3/Src/adc.c:99,151,161,203,213`), same 500 µΩ shunts, same DRV8301 SPI-gain
scheme, same TIM1/TIM8 center-aligned + RCR trigger scheme. Nothing in the factory
firmware suggests the MKS clone deviates from genuine ODrive on this path — the
divergence we're hitting is entirely in **our own replacement current-sense code**, not
in the board.

---

## Prioritized fix list

**All of these are firmware-only — no hardware/scope work needed to fix the root cause.**
Scope work is listed last, as a *verification* step, not a prerequisite.

1. **(Firmware, top priority) Gate current-sample acceptance on `TIM1->CR1 & TIM_CR1_DIR`,**
   exactly like ODrive's `pwm_trig_adc_cb`/`tim_update_cb` (low_level.cpp:511-518,
   592-598) or SimpleFOC's RCR-based valley-only trigger
   (`_initTimerInterruptDownsampling`, stm32_adc_utils.cpp:447-491). Concretely in
   `genCurrentRead()`/`adc2Config()` (main.cpp:180-213): either (a) read `TIM1->CR1 &
   TIM_CR1_DIR` at read time and only trust the sample when it indicates "just left the
   valley," feeding the other one into a DC-offset filter instead (see #2); or (b) set
   `TIM1`'s repetition counter and pre-align `DIR`/`CNT` once at init (SimpleFOC's
   approach) so only the valley-triggered conversion ever reaches the ADC group, and stop
   re-touching `TIM1->CR2`/`ADC2->JSQR` on every read. Given that `LowsideCurrentSense`'s
   `init()` hangs on this board, option (a) — DIR-bit check inside `genCurrentRead()`,
   keeping the rest of the hand-rolled path — is the lower-risk fix.

2. **(Firmware) Stop discarding the peak-vector sample — use it for continuous DC offset
   calibration** the way ODrive does (`DC_calib_`, calib_tau≈0.2 s low-pass,
   low_level.cpp:495-496,582-588), instead of the current static `g_off10`/`g_off11`
   measured once in `genCurrentInit()` (main.cpp:207-209). This also removes the need to
   re-run a blocking 400-sample offset loop at init and compensates thermal drift live.

3. **(Firmware) Stop fully re-programming `ADC2->CR1/CR2/SMPR1/JSQR` and `TIM1->CR2` on
   every single `genCurrentRead()` call** (main.cpp:196, `adc2Config()`). This write is
   asynchronous to the injected-conversion state machine and risks corrupting an
   in-flight conversion (plausible source of the reported mid-scale "~1180" garbage,
   distinct from the clean-zero peak-vector samples explained by #1). If `analogRead()`
   elsewhere really does clobber ADC2's shared `ADC->CCR` prescaler, reconfigure only the
   registers that `analogRead()` actually touches (likely just `ADC->CCR` and ADC1's own
   registers) — not blindly rewrite ADC2's injected sequence/trigger config every tick.
   Alternatively, move telemetry `analogRead()` calls (bus voltage, NTCs) fully onto
   ADC1/ADC3 and never call Arduino `analogRead()` on any ADC2-adjacent state, so ADC2 can
   be configured once at boot and left alone.

4. **(Firmware) Switch DRV8301 gain from G40 to G80** to roughly double raw-count
   resolution for the 0.4–5 A bench range (0.0403 → ~0.0201 A/count), following ODrive's
   own auto-gain-selection logic (`motor.cpp:68-121`) as a model. Requires updating in
   lockstep: `drv.begin(..., Drv8301Gain::G80)` (main.cpp:1077, the real SPI write) *and*
   `kAmpsPerCount` in main.cpp:169 (currently hardcoded for the 40.0f factor, completely
   decoupled from the `Drv8301Gain` enum — this decoupling is itself a latent bug worth
   fixing regardless of which gain is chosen). Do this **after** #1, since better
   resolution on a still-architecturally-broken sample stream won't fix the phantom
   -80A trips.

5. **(Firmware) Once #1–#3 land, remove/relax the ad-hoc "reject out-of-range + hold
   last value" filtering.** ODrive's only runtime safety net beyond clean sampling is the
   blunt `overcurrent_trip_level` magnitude check that disarms (motor.cpp:340-344,
   `FOC_current`) — it doesn't paper over bad samples, because with correct DIR gating
   there shouldn't be any. Keep an equivalent hard trip-and-disarm for genuine
   overcurrent, but stop trying to statistically distinguish real overcurrent from
   sampling artifacts once the artifacts are gone by construction.

6. **(Hardware/scope, verification only, do last)** If, after #1–#3, occasional bad
   *valley* samples still appear (as opposed to the now-correctly-filtered peak samples),
   scope the DRV8301 SO1/SO2 amp outputs right at the valley instant to check settling
   time against `TIM_1_8_DEADTIME_CLOCKS` (20 counts ≈ 119 ns, matches project memory) and
   the ADC sampling-time setting (`SMPR1` currently 56 cycles, main.cpp:189 — reasonable,
   matches ODrive's ballpark at similarly-fast conversions). Expected to be unnecessary:
   the described symptoms (clean-ish 0 counts, runs of several PWM periods, matches the
   40x-gain 2048-offset-to-80A math exactly) are fully explained by the missing
   valley/peak discrimination in #1, not by amplifier settling.

---

## File/line index (for quick navigation)

- Genuine ODrive valley/peak gating: `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/MotorControl/low_level.cpp:494-618`
- Genuine ODrive TIM1/TIM8/ADC injected init: `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/Board/v3/Src/tim.c:85-140,300-378`, `adc.c:72-228`
- Genuine ODrive gain auto-select + DRV8301 SPI write: `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/MotorControl/motor.cpp:60-121`
- Genuine ODrive overcurrent trip (only runtime sample check): `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/MotorControl/motor.cpp:333-344`
- Genuine ODrive PWM/current-meas frequency constants: `~/Downloads/MKS_ODrive_S-fw-v0.5.1/Firmware/Board/v3/Inc/main.h:69-75,171-172`
- SimpleFOC generic STM32 RCR-based valley gating: `firmware-base/.pio/libdeps/m5/Arduino-FOC/src/current_sense/hardware_specific/stm32/stm32_adc_utils.cpp:447-513`
- SimpleFOC PWM timer center-aligned + RCR setup: `firmware-base/.pio/libdeps/m5/Arduino-FOC/src/drivers/hardware_specific/stm32/stm32_mcu.cpp:120-124`
- Our current-sense implementation (the actual bug site): `firmware-base/src/m5/main.cpp:153-215`
- Our DRV8301 gain enum/SPI encode (plumbing exists, unused for G80): `firmware-base/lib/base_motor/drv8301.h:70-145`
- Our DRV8301 gain init call (G40 hardcoded): `firmware-base/src/m5/main.cpp:1077`
