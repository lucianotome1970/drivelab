# DriveLab — Hoverboard Base FAQ / FAQ da Base Hoverboard

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> **Who this is for / Para quem é:** you are building your first direct drive wheel
> base around a hoverboard motor, and something is not working. Find your symptom
> below. — você está montando sua primeira base direct drive com motor de
> hoverboard e alguma coisa não está funcionando. Ache o seu sintoma abaixo.

> **Hardware only / Só hardware.** This page applies to any firmware — ours,
> FFBeast, ODrive, whatever you run. — Esta página vale para qualquer firmware:
> o nosso, o FFBeast, o ODrive, qualquer um.

> ✅ **verified on our bench / validado na bancada** marks answers we hit and
> solved ourselves. Answers without the mark are general community knowledge. —
> marca as respostas que nós mesmos vivemos e resolvemos. Sem a marca, é
> conhecimento geral da comunidade.

---

## 🇬🇧 English

**Find your symptom:**

| What is happening | Go to |
|---|---|
| I have not bought the parts yet | [Before you buy](#before-you-buy) |
| The board won't take the firmware, or the PC can't see it | [Can't flash the firmware](#cant-flash-the-firmware) |
| It's powered, but the wheel still spins freely | [It powers up but the motor won't lock](#it-powers-up-but-the-motor-wont-lock) |
| Calibration shakes, jams, or never finishes | [Calibration](#calibration) |
| The wheel turns in steps, buzzes, or is noisy | [Shakes, notchy or noisy](#shakes-notchy-or-noisy) |
| Too little force, or strong one way and weak the other | [Weak or uneven force](#weak-or-uneven-force) |
| The wheel runs away from my hand | [It spins on its own](#it-spins-on-its-own) |
| The motor gets hot, or force fades after a while | [It runs too hot](#it-runs-too-hot) |
| Straight-ahead moves between sessions, or while driving | [It loses the center](#it-loses-the-center) |
| The game doesn't list it, or it drops out mid-session | [It disappears in the game](#it-disappears-in-the-game) |
| What can hurt me or kill the board | [Safety](#safety) |

### Before you buy

Most build problems are decided here, before anything is even plugged in.

#### Will any hoverboard motor work?

**What you see**

You found cheap hoverboard motors online and they all look the same.

**Why it happens**

They are not the same. Two things decide whether your wheel feels strong and lasts:
the material of the outer shell, and the size of the magnets inside.

**What to do**

1. **Only buy a motor with a metal shell.** A plastic shell cracks under load. A wheel
   base pulls much harder on the motor than a person standing on a hoverboard ever did.
2. **Look for 30 mm magnets inside.** This is the single number that decides how much
   force you get. With smaller magnets you will not get close to the torque you expect.
3. Ignore the seller's power rating. It describes a scooter driving forward, not a
   motor being held still while fighting your arms.
4. If you can, buy a motor that someone in the hobby has already used. A known good
   part removes a whole class of problems from your build.

#### How do I open and prepare the motor?

**What you see**

The motor is sealed, you cannot see the coils, and there are five thin wires coming
out next to the three thick ones.

**Why it happens**

Hoverboard motors ship as a finished wheel. You need to open it to fit your own
position sensor, and the thin wires belong to sensors you will not use.

**Why you open it**

You open the motor for two reasons: **to take out the factory sensor** (a small board
with those five thin wires, which your controller does not use) and **to free the shaft**
so you can fit your own position sensor.

**What to do**

Do it in this order:

1. **Unscrew and remove the rear cover.** Ordinary screws around the rim. Keep them —
   you need them to close the motor again.
2. **Separate the stator from the housing.** The stator is the inner part: it is the one
   that carries the shaft and the copper windings. It is **not bolted down** — what holds
   it is the pull of the magnets sitting in the housing. That is why it feels stuck.
3. **Pull hard to free the stator.** A firm, slightly sharp pull does it. The first time
   it feels like you are about to break something. That is normal, it really is how it
   comes apart. Mind your fingers: when it lets go, the magnets snap it back.
4. **Take out the factory sensor board** attached to the stator. It is usually glued, so
   it normally comes away together with the pins at its base.
5. **Cut the five thin wires and forget them.** They only belonged to that factory sensor.
6. **Do not cut the three thick wires.** Those are the motor phases — the force travels
   through them, and they are what your controller connects to.
7. **Clean out the old grease and any rust on the shaft** before closing it up.
8. **Mount your own position sensor on the end of the shaft**, outside the motor, unless
   you have a good reason not to. Outside is far easier to reach when something goes
   wrong.

#### What power supply do I need?

**What you see**

Power supplies are listed by volts and watts and you do not know which combination
you need.

**Why it happens**

The voltage is not a free choice — it has to match the board you bought. The watts
decide how much force you can hold before the supply gives up.

**What to do**

1. **Match the voltage to what your board accepts — check the board, not the motor.**
   Some boards take a wide range (many accept 12–56 V). Others ship as two variants, a
   lower-voltage one and a higher-voltage one, and feeding the low one too much destroys
   it. On the common ODESC boards you can tell which you have by looking: the `QC PASS`
   sticker reads `24V` or `56V`, and the status LED is purple on the 24 V board and green
   on the 56 V one. Do not judge by the capacitors — both variants use the same ones.

   <table>
   <tr>
   <td width="50%"><img src="screenshots/odesc-v42-24v.jpg" width="100%" alt="ODESC v4.2 24 V — QC PASS 24V sticker, purple LED"></td>
   <td width="50%"><img src="screenshots/odesc-v42-56v.jpg" width="100%" alt="ODESC v4.2 56 V — QC PASS 56V sticker, green LED"></td>
   </tr>
   <tr>
   <td><b>8–24 V</b> — sticker <code>24V</code>, purple LED</td>
   <td><b>8–56 V</b> — sticker <code>56V</code>, green LED</td>
   </tr>
   </table>
2. **Aim for 500-600 W** for a single wheel base. This is comfortable, not tight.
3. Use a supply with a real, steady output. A laptop-style brick is not suitable —
   it cannot deliver the short, hard current spikes a wheel base demands.
4. **Plan for the brake resistor now, not later.** When you turn the wheel, the motor
   pushes energy back into the supply. That energy has to go somewhere or the board
   shuts down mid-corner. See [brake-resistor.md](brake-resistor.md).

#### Which sensor, and where do I mount it?

✅ **verified on our bench**

**What you see**

There are several position sensor options and no obvious reason to pick one.

**Why it happens**

The sensor tells the board where the wheel is. Everything else — force, centering,
smoothness — is built on top of that one measurement. A weak choice here shows up
later as problems that look like something else entirely.

**What to do**

1. **Mount the sensor directly on the motor shaft.** Not through a belt, not through
   gears. This is the most common avoidable mistake in the whole build.
2. If you already mounted it through a belt or gears: every bit of slack in that
   coupling becomes a step you can feel in the wheel, and the pulse count almost never
   works out to a clean number. Move it to the shaft.
3. For which sensor to buy and the trade-offs between them, see
   [encoders.md](encoders.md).
4. Write down the sensor's pulses-per-revolution from its datasheet before you install
   it. You will need that number to set the board up, and it is much harder to read off
   a sticker once the sensor is buried in the assembly.

### Can't flash the firmware

Flashing means putting the software onto the board. This is where most people meet
their board for the first time, and where a lot of them get stuck.

#### The board won't enter DFU mode

✅ **verified on our bench**

**What you see**

You follow the instructions, but the board never shows up as something you can write
firmware to.

**Why it happens**

DFU is a special mode where the board accepts new firmware instead of running the
firmware it already has. Normally a program can ask the board to switch into it. On
many of these boards that automatic request does not work, and nothing tells you why.

**What to do**

1. **Do it by hand instead.** Your board has one of two ways to force DFU. Look at the
   board and find out which one you have before doing anything else:
   - **A button**, usually labelled `SW1` or `BOOT0`.
   - **A jumper**, usually near the BOOT pins — a small removable plastic cap bridging
     two pins.
2. **Always start from fully powered off.** Not a reset — actually remove power, count
   to five. Both methods below only work at the moment the board wakes up.
3. **If your board has a button:** hold the button down, power the board up while still
   holding it, then release.
4. **If your board has a jumper:** the position that forces DFU depends on the board. On
   many of these boards you have to **remove** the jumper, not add one. Note down how it
   was set before you touch it, change it, then power up.
5. **Flash now, while the board is in DFU.** It stays in this mode until the next power
   cycle.
6. **Put the jumper back, or stop holding the button, before the next power-up.** A board
   left forced into DFU will never run your firmware — and that looks exactly like a
   failed flash.
7. If it still refuses, try the other USB cable and the other USB port before assuming
   the board is faulty. See the next entry.

#### The PC doesn't recognize the board

✅ **verified on our bench**

**What you see**

You plug the board in and nothing appears on the computer at all.

**Why it happens**

Usually the cable. Many USB cables are built only to charge phones — they carry power
but no data. The board lights up, so everything looks fine, and the computer never
sees it.

**What to do**

1. **Change the USB cable first.** Use one you know transfers data, for example the one
   that came with a phone you have actually copied files with. This is the cheapest fix
   and it is the answer more often than anything else.
2. Try a different USB port, directly on the computer. Skip hubs, docks and monitors.
3. On Windows, check whether the board shows up as an unknown device. If it does, the
   cable is fine and you need the driver.
4. If the board only appears when it has its own power supply connected, that is a
   clue — see the next entry.

#### It flashes fine, then disappears after a reboot

✅ **verified on our bench**

**What you see**

The board shows up while you are flashing, the flash finishes without error, and then
after restarting it is gone.

**Why it happens**

Two different causes look identical from outside. Either the USB power circuit on the
board is damaged — common on boards that have been miswired once — or the firmware is
running but the computer has nothing to talk to yet.

**What to do**

1. **Check the BOOT jumper is back where it was.** If you had to move or remove a jumper
   to get into DFU and did not put it back, the board wakes up straight into DFU again
   and never runs your firmware. This is the cheapest cause and the easiest to overlook.
2. **Power the board from its own supply, then plug in USB.** If it appears now but not
   on USB power alone, the board's USB power path is damaged. The board still works —
   it just needs external power to be seen.
3. Check that the flash actually covered the right region. A flash that reports success
   but writes to the wrong place leaves the board silent.
4. If you only have one USB port available, remember that a hardware programmer and the
   data cable compete for it. Flash first, unplug the programmer, then connect the data
   cable — do not expect both at once.
5. Re-flash once with the manual DFU method from the first entry before concluding the
   board is dead.

### It powers up but the motor won't lock

"Locking" is the moment the motor starts holding position. Before that, the wheel spins
freely in your hand. After it, the wheel feels solid. If it never locks, the motor is
not armed.

#### The board is powered and connected, but the wheel still spins freely

**What you see**

Lights are on, the computer sees the board, but the wheel turns as loosely as it did
with everything switched off.

**Why it happens**

The board refuses to arm the motor when something in its startup checks failed. Almost
always it already knows why — it is just not showing you.

**What to do**

1. **Read the error the board reports** before changing anything. Every board keeps a
   list of what went wrong at startup. Guessing without reading it costs hours.
2. **Check the three thick motor wires** at both ends. One loose phase gives you a motor
   that cannot arm, and it looks identical to a software problem.
3. **Check that the position sensor is being read.** Turn the wheel by hand and see
   whether the reported angle moves. If the angle never changes, the board has no idea
   where the wheel is and will not arm.
4. **Confirm the power supply is the right voltage** for your board and is actually
   holding that voltage under load.
5. If the board reports a brake-resistor error, go to the next entry — that one is a
   trap.

#### It refuses to arm because of the brake resistor

✅ **verified on our bench**

**What you see**

Everything looks correct, the wiring is fine, and the motor still refuses to arm. The
error mentions the brake resistor.

**Why it happens**

The brake resistor is the part that burns off the energy the motor sends back when you
turn the wheel. If the board is configured to expect one and cannot verify it, it
refuses to arm — on purpose, to protect itself. This is the single most confusing
"it just won't work" cause, because nothing about the motor is wrong.

**What to do**

1. **Decide first whether you actually have a brake resistor connected.** Not planned,
   not on the shelf — physically wired to the board right now.
2. If you do not have one connected, **turn the brake resistor option off** in the
   board's configuration. The motor will arm.
3. If you do have one connected, **check its wiring and its value.** A resistor that is
   wired but not conducting fails the same check as no resistor at all.
4. Do not treat this as a permanent solution. Running without a brake resistor is fine
   for gentle bench testing, and a real risk once you drive with force. See
   [brake-resistor.md](brake-resistor.md).
5. Remember that this setting can survive a firmware update, because it lives in the
   board's own saved settings. If the fault came back "for no reason", check it again.

### Calibration

Calibration is the short routine where the board moves the motor by itself to learn two
things: how the motor is wired, and where the position sensor sits relative to the
magnets. Everything else depends on it being right.

#### Calibration never finishes — it shakes and locks up

✅ **verified on our bench**

**What you see**

You start calibration and the motor stutters violently, jams, or shakes hard instead of
turning smoothly. It either stops with an error or never completes.

**Why it happens**

During calibration the board pushes current into the motor while guessing where the
magnets are. If its guess and reality disagree, the motor fights itself. Hoverboard
motors make this worse: they have many magnet pairs, and they naturally "click" into
positions even unpowered.

**What to do**

1. **Lower the calibration current.** Too much current makes the motor slam between
   positions instead of easing into them. Come down in steps and retry.
2. **Do the first calibration with nothing attached to the shaft.** No wheel rim, no
   hub. Extra weight is extra momentum for the motor to fight.
3. **Confirm the number of magnet pairs** you told the board. A wrong number here makes
   calibration physically impossible to complete. See the entry below.
4. **Confirm the sensor's pulse count.** Same problem, different number — see the entry
   below.
5. **Fully power off between attempts.** Not a reset — remove power, wait, power up.
   Repeated resets can leave the motor driver chip in a confused state that only a real
   power cycle clears, and that produces exactly this symptom.
6. If the motor calibrates fine one time and badly the next with nothing changed, the
   problem is in what it measures, not in your settings. Read the "loses the center"
   section.

#### The sensor pulse count (CPR) is wrong

**What you see**

Calibration completes, but the wheel behaves strangely afterwards: the range of motion
is wrong, the end stops are in the wrong place, or the force feels stepped.

**Why it happens**

CPR is how many counts the sensor produces in one full turn. It is how the board
converts sensor pulses into an actual angle. If that number is wrong, every angle the
board computes is wrong by the same factor, and everything built on top drifts with it.

**What to do**

1. **Find the sensor's pulses-per-revolution (PPR) in its datasheet.** It is often
   printed on the sensor body too.
2. **Multiply PPR by 4 to get CPR** for a common two-wire sensor — the kind with an A and a B channel, which is most of them. A 600 PPR sensor is
   2400 CPR. This ×4 is where most people go wrong — they enter 600.
3. **Enter the number for one turn of the motor**, not one turn of the sensor. These are
   the same thing only when the sensor is mounted directly on the motor shaft.
4. If your sensor is driven through gears or a belt, the ratio multiplies into this
   number, and it rarely lands on a clean value. This is one more reason to mount the
   sensor on the shaft.
5. **Test it:** turn the wheel exactly one full turn by hand and check that the board
   reports 360°. If it reports half or double, your CPR is off by that factor.

#### The number of magnet pairs is wrong

**What you see**

The motor hums, jerks, gets hot, or refuses to calibrate, and nothing you change in the
force settings helps.

**Why it happens**

The board needs to know how many magnet pairs are inside the motor to send current to
the right coil at the right moment. With the wrong count it energises the wrong coil —
so the motor heats up and pushes in the wrong direction.

**What to do**

1. **Most hoverboard motors have 15 magnet pairs.** Start there.
2. **Count them if you can.** With the motor open, count the magnets glued inside the
   housing and divide by two.
3. **Try 15, then 14.** If neither works, the problem is somewhere else — do not keep
   guessing numbers.
4. A wrong count makes the motor heat up quickly with little force to show for it. If
   you feel that combination, come back to this entry.

### Shakes, notchy or noisy

#### The wheel turns in steps instead of smoothly

✅ **verified on our bench**

**What you see**

Turning the wheel feels like dragging it over teeth. You feel hard little jerks, one
after another, instead of one smooth motion.

**Why it happens**

Two different things produce this feeling, and telling them apart takes ten seconds.

The first is **cogging**: the magnets inside the motor naturally pull towards certain
positions, so the motor "clicks" into them. This exists even with everything switched
off, and it is not a fault.

The second is a **position sensor problem**: if the board's idea of the angle jumps
around, the force it produces jumps with it.

**What to do**

1. **Switch everything off and turn the wheel by hand.** If you still feel the steps
   with no power at all, that is cogging — the motor itself, not your setup.
2. **If the steps only appear with power on**, the fault is in what the board measures
   or how it is driving the motor. Continue with the steps below.
3. **Check the sensor pulse count (CPR) first.** It is the most common cause of powered
   stepping. See the Calibration section.
4. **Check that the sensor is mounted on the motor shaft**, not through a belt or gears.
   Slack in a belt or backlash between gears becomes exactly this stepping feeling.
5. **Redo the calibration after a full power cycle.** A calibration that landed on a bad
   reference produces stepping that no force setting can smooth out.
6. Accept some cogging as normal. Hoverboard motors have strong magnets, and a small
   amount of notchiness at very low speed is part of the deal. It fades once real force
   is being produced.

#### It vibrates, buzzes, or the vibration reaches my pedals

**What you see**

The wheel buzzes or hums while holding still, or you feel a vibration in your pedals
that appears only when the base is powered.

**Why it happens**

A buzz while holding still means the board is correcting the position too aggressively:
it overshoots, corrects back, and repeats many times per second. Vibration that shows up
in another device is usually not vibration at all — it is electrical noise travelling
through the shared ground between the two devices.

**What to do**

1. **If the buzz appears only when the motor is holding a position**, reduce how hard the
   board corrects. Lower the gains a step at a time until the buzz stops.
2. **Check the sensor mounting is rigid.** A sensor that can move slightly relative to
   the shaft feeds the board a wobbling angle, and the board chases it.
3. **For vibration felt in the pedals**, unplug the pedals from the computer and see if
   it stops. If it does, it is electrical, not mechanical.
4. **Do not daisy-chain grounds.** Give the base and the pedals their own paths to the
   computer, and avoid powering the pedals from the base's supply.
5. **Check for a loose motor mount.** A base that is not bolted down solidly turns motor
   torque into rig vibration, and that one is real mechanical vibration.

### Weak or uneven force

#### The force is weak in both directions

✅ **verified on our bench**

**What you see**

Everything runs, nothing errors, but the wheel is far weaker than you expected — you can
overpower it easily with one hand.

**Why it happens**

Weak force with no error almost never means the motor is too small. It usually means the
board is being told to hold back, or its idea of where the magnets are is off, so the
current it sends does not turn into torque.

**What to do**

1. **Check the current and power limits** in the board's configuration first. These
   usually ship set low for safety, and a low limit produces exactly this.
2. **Check the game's own force settings.** A game set to 30% will feel weak no matter
   how good your base is. Test with one game you know, at a known setting.
3. **Confirm the power supply holds its voltage.** A supply that sags under load quietly
   limits your peak force.
4. **Redo the calibration.** If the reference the board found is off, current goes into
   the motor at the wrong moment — you get heat instead of force. If the motor gets warm
   while feeling weak, this is almost certainly your cause.
5. **Check the magnet size of the motor you bought.** If it is well under 30 mm, the
   motor itself is the limit and no setting will fix it.

#### It is strong one way and weak the other, or it slips past center

**What you see**

The wheel pushes hard in one direction and barely at all in the other. Or it turns past
the center point with almost no resistance and then catches again.

**Why it happens**

This is a symmetry problem. The board's position reference is offset from where it
really should be, so the force it produces is correct on one side of that offset and
wrong on the other.

**What to do**

1. **Recentre the wheel mechanically first.** Set the wheel physically straight, then
   tell the board this is the center. Do not skip this — a mechanically crooked start
   makes everything below look broken.
2. **Redo the calibration with the wheel unloaded** and check whether the asymmetry
   moves. If it changes place each time, your position reference is not repeatable —
   read the "loses the center" section.
3. **Check the sensor pulse count.** A wrong count makes one side of the travel arrive
   at the end stop early and the other side late.
4. **Check whether force inversion is enabled** somewhere it should not be — in the
   board, and again in the game. Two inversions cancel and are hard to spot.
5. **Check the mechanical coupling to the wheel.** A hub or spoke that slips under load
   produces exactly this and is often blamed on software.

#### It gets very hot and still has little force

✅ **verified on our bench**

**What you see**

The motor becomes hot quickly, but the force you feel is disappointing for that much
heat.

**Why it happens**

This pair — much heat, little force — is a signature. It means current is going into the
motor at the wrong moment relative to the magnets. The energy still turns up, but as heat
instead of torque.

**What to do**

1. **Redo the calibration from a full power cycle.** This is the fix in most cases.
2. **Check the number of magnet pairs.** A wrong count produces this exact signature.
   See the Calibration section.
3. **Check the sensor is rigidly fixed to the shaft** and not slipping. A sensor that
   creeps makes the reference drift, and the symptom comes back after a while even if it
   started fine.
4. **Stop and let it cool** before repeating tests. Repeating a bad calibration on a hot
   motor is how motors get damaged.

### It spins on its own

#### The wheel runs away from my hand

✅ **verified on our bench**

**What you see**

You touch the wheel and it takes off in the direction you nudged it, or it starts
oscillating and grows worse the longer you hold it.

**Why it happens**

The board is reading how fast the wheel is moving and adding force in the same direction
instead of opposing it. Every small movement gets amplified. It feels alive, and it is
genuinely dangerous — this is the failure most likely to hurt someone.

**What to do**

1. **Cut the power immediately** when this happens. Do not try to hold it.
2. **Reduce the force limit to a low value before testing again.** Never diagnose this
   at full force.
3. **Check the direction settings.** Somewhere between the sensor direction, the motor
   phase order, and the force inversion option, one sign is flipped. Change one at a
   time and retest.
4. **Swap two of the three thick motor wires** if you have already ruled out settings.
   Two phases in the wrong order invert the direction of the force the motor makes.
5. **Check the damping setting.** Damping should resist motion. If it is inverted, it
   adds to motion instead, which produces exactly this growing oscillation.
6. Test with the wheel rim off. It is safer, and the lower inertia makes the fault easier
   to see.

### It runs too hot

#### How hot is normal, and do I need a fan?

**What you see**

The motor is warm — or hot — after a session, and you have no reference for whether that
is fine or a warning.

**Why it happens**

A direct drive base makes force by holding current in the motor while it barely moves.
A motor that is not spinning has no airflow of its own, so nearly all of that energy
becomes heat with nowhere to go. Warm is normal. The question is how warm, and for how
long.

**What to do**

1. **Use your hand as the first test.** If you can rest your hand on the motor and keep
   it there comfortably, you are fine. If you have to pull away quickly, you are running
   too hard for your setup.
2. **Turn the force down to 55-65% of maximum.** This one change solves most heat
   complaints, and you will barely notice the difference while driving.
3. **Know what you actually use.** Most people with strong bases end up driving at around
   7-8 Nm. Running at maximum all the time buys heat, not lap time.
4. **For short sessions, a fan is usually not needed.** For long sessions with high
   force, it is.
5. **If you add cooling, blow air on the stator, not on the closed casing.** That means
   drilling ventilation holes in the housing. Blowing air at a sealed motor from outside
   does very little — the heat is generated inside, and the shell is a poor path out.
6. **Measure the motor temperature if you can.** A number beats a guess, and it lets you
   set a limit that shuts things down before damage.

#### It loses force after a few minutes

✅ **verified on our bench**

**What you see**

The base feels right at the start of a session and then quietly gets weaker, or drops
out entirely, after ten or twenty minutes.

**Why it happens**

Two causes look the same from the driver's seat. Either something is protecting itself
from heat and reducing output, or the power supply cannot sustain what you are asking
and its voltage dips until the board cuts out.

**What to do**

1. **Touch the motor when it happens.** Hot motor means the heat path — see the entry
   above. Cool motor points at the power supply instead.
2. **Watch the supply voltage while driving** if your setup can show it. A voltage that
   sags during hard corners and recovers afterwards is your answer.
3. **Check whether the board is reporting an under-voltage or over-voltage event.** These
   are recorded even when nothing visibly errors, and they explain drop-outs that look
   random.
4. **Reduce the force limit** and see whether the session length improves. If it does,
   you were simply asking for more than the system can hold continuously.
5. **Fit or check the brake resistor.** Force that disappears specifically during fast
   direction changes is energy coming back from the motor with nowhere to go. See
   [brake-resistor.md](brake-resistor.md).
6. Understand the difference between peak and continuous torque. A base rated for 9 Nm
   peak may only sustain around 5 Nm. See [calculo-torque.md](calculo-torque.md).

### It loses the center

#### The center is different every time I switch on

✅ **verified on our bench**

**What you see**

You set the wheel straight, use it, switch off, switch on the next day — and straight is
now somewhere else.

**Why it happens**

This is the difference between two kinds of position sensor. An **incremental** sensor
only counts movement; it has no idea where it is when it wakes up, so it needs a fixed
reference to find zero again. Without one, "zero" ends up wherever the wheel happened to
be, plus whatever the calibration guessed that day. Hoverboard motors make it worse,
because the magnets pull the wheel to a slightly different resting spot each time.

**What to do**

1. **Use the sensor's index signal if it has one.** That is the third channel, often
   called Z. It marks one exact spot per revolution, and it turns a guess into a
   repeatable reference. This is the real fix.
2. **If your sensor has no index signal**, expect to set the center each time you power
   up, and make that a one-button routine rather than a chore.
3. **An absolute magnetic sensor removes the problem entirely** — it knows its angle the
   moment it powers on. See [encoders.md](encoders.md).
4. **Do not fight this with calibration settings.** If the reference itself is not
   repeatable, no amount of tuning makes it repeatable.
5. **Save the center offset** so it survives a power cycle, rather than re-teaching it
   from scratch every session.

#### The center drifts while I am driving

**What you see**

You start straight, and after a few laps the wheel's straight-ahead has moved to one
side.

**Why it happens**

Something is slipping. Either the sensor is moving relative to the shaft, the wheel is
moving relative to the motor, or the board is losing counts — reading movement that
happened as movement that did not.

**What to do**

1. **Mark the shaft and the sensor with a pen.** Drive until it drifts, then look. If the
   marks no longer line up, you found it: the sensor is slipping.
2. **Do the same on the wheel hub.** A hub or spoke slipping under load is extremely
   common and gets blamed on software constantly.
3. **Check the grub screws** on both the sensor coupling and the wheel hub. Tighten onto
   a flat on the shaft, not onto a round surface.
4. **If nothing is physically slipping, suspect lost counts.** Wiring that runs alongside
   the motor phase wires picks up interference and drops pulses. Route the sensor cable
   away from the thick motor wires, and use a shielded cable.
5. **Drift that only appears at high force** points at electrical interference or a
   mechanical part that only slips when loaded — both get worse with force, so test at
   low force to confirm.

### It disappears in the game

#### The game does not list my wheel

✅ **verified on our bench**

**What you see**

The base works on the desktop — you can see it responding in a controller test tool —
but the game does not offer it as a steering device.

**Why it happens**

Games look for a device that presents itself as a wheel with force feedback. If the
board is seen by the computer but does not announce itself correctly, or if the game
scanned for devices before your base was ready, it simply is not on the list.

**What to do**

1. **Confirm the computer sees it first**, outside the game, in a controller test tool.
   If it is not there, this is a connection problem — go back to the flashing section.
2. **Start the base before starting the game.** Many games build their device list once,
   at launch, and never look again.
3. **Check inside the game's controller settings**, not just the main list. Some titles
   hide unrecognised devices behind an extra menu.
4. **Delete the game's saved controller configuration** and let it detect from scratch.
   A stale saved profile can point at a device that no longer exists.
5. **Try one other game.** If it appears in one and not another, the problem is that
   title's device handling, not your base.

#### It disappears during a session, and only comes back if I restart the game

✅ **verified on our bench**

**What you see**

The wheel works, then the force stops mid-session. Reconnecting the cable is not enough
— you have to quit the game and go back in.

**Why it happens**

When the base disconnects and reconnects, the computer may treat it as a different
device. The game is still holding on to the old one, which no longer exists, so nothing
you do at the cable end brings the force back.

**What to do**

1. **Find out why it disconnected in the first place** — that is the real fault. A
   momentary power dip, a marginal USB cable, or a board reset all cause it.
2. **Check the USB cable and port again.** A cable that works most of the time is still
   a bad cable.
3. **Power the base from its own supply**, not from USB, so a power dip on one side does
   not take out the other.
4. **If the board reports voltage events**, read them. A board that resets from a
   voltage dip looks exactly like a USB problem from the outside.
5. Expect to restart the game after a reconnect. This is normal behaviour for most
   titles and is not a fault in your build.

### Safety

✅ **verified on our bench**

This section is not a symptom list. It is the short list of things that can hurt you
or destroy your board. Read it once before the first power-up.

#### What can hurt you

A direct drive base is not a toy motor. At full force it is stronger than your wrist.

1. **Never put your thumbs through the spokes** of the wheel, the way you might on a
   toy wheel. If the base snaps to one side, that is how thumbs get broken.
2. **Start with the force turned down.** Set it low for the first session and raise it
   over several sessions. You cannot judge what is safe from a number on a screen.
3. **Keep the wheel clear of your face** during the first power-up and during
   calibration. Calibration moves the motor on its own, sometimes sharply.
4. **Have the power switch where you can reach it** without leaning into the wheel.

#### What can destroy the board

1. **Spinning the motor while the power is off pushes voltage back into the board.**
   A motor turned by hand is a generator. With everything switched off that energy has
   nowhere to go, and it can damage the electronics. Do not spin the wheel fast when
   the base is off — and be aware that shipping or moving the rig can do it for you.
2. **Run a brake resistor.** When you turn the wheel against the motor, energy comes
   back down the wires. Without somewhere to dump it, the board's voltage rises until
   it protects itself and cuts out — usually in the middle of a corner. See
   [brake-resistor.md](brake-resistor.md).
3. **Match the power supply voltage to the board's variant.** A 24 V board fed higher
   voltage does not complain. It dies.
4. **Never change the motor wiring with power connected.** Switch off, unplug, and give
   the board time to discharge before touching the phase wires.
5. **Do not leave the motor armed and unattended.** If something is wrong with the
   sensor, an armed motor can push current into a stuck position and heat up fast with
   nobody watching.

---

## 🇧🇷 Português

**Ache o seu sintoma:**

| O que está acontecendo | Vá para |
|---|---|
| Ainda não comprei as peças | [Antes de comprar](#antes-de-comprar) |
| A placa não aceita o firmware, ou o PC não enxerga ela | [Não consigo gravar o firmware](#não-consigo-gravar-o-firmware) |
| Está ligado, mas o volante continua girando solto | [Liga, mas o motor não trava](#liga-mas-o-motor-não-trava) |
| A calibração treme, trava ou nunca termina | [Calibração](#calibração) |
| O volante gira em degraus, zumbe ou faz barulho | [Treme, trepida ou faz barulho](#treme-trepida-ou-faz-barulho) |
| Força de menos, ou forte pra um lado e fraca pro outro | [Força fraca ou torta](#força-fraca-ou-torta) |
| O volante foge da minha mão | [Gira sozinho](#gira-sozinho) |
| O motor esquenta, ou a força some depois de um tempo | [Esquenta demais](#esquenta-demais) |
| O centro muda entre sessões, ou anda enquanto dirijo | [Perde o centro](#perde-o-centro) |
| O jogo não lista, ou cai no meio da sessão | [Some no jogo](#some-no-jogo) |
| O que pode me machucar ou queimar a placa | [Segurança](#segurança) |

### Antes de comprar

A maior parte dos problemas de montagem é decidida aqui, antes de ligar qualquer coisa.

#### Qualquer motor de hoverboard serve?

**O que você vê**

Você achou motores de hoverboard baratos na internet e todos parecem iguais.

**Por que acontece**

Eles não são iguais. Duas coisas decidem se o seu volante vai ter força e durar: o
material da carcaça de fora e o tamanho dos ímãs de dentro.

**O que fazer**

1. **Compre só motor de carcaça metálica.** Carcaça de plástico racha sob carga. Uma
   base de volante puxa o motor muito mais forte do que uma pessoa em pé no hoverboard
   jamais puxou.
2. **Procure ímãs de 30 mm dentro.** Esse é o número que decide quanta força você vai
   ter. Com ímã menor você não chega perto do torque que espera.
3. Ignore a potência que o vendedor anuncia. Ela descreve uma patinete andando pra
   frente, não um motor sendo segurado parado enquanto briga com os seus braços.
4. Se puder, compre um motor que alguém do hobby já usou. Uma peça sabidamente boa
   elimina uma categoria inteira de problemas da sua montagem.

#### Como eu abro e preparo o motor?

**O que você vê**

O motor é fechado, você não enxerga as bobinas, e saem cinco fios finos ao lado dos
três fios grossos.

**Por que acontece**

Motor de hoverboard vem pronto, como uma roda. Você precisa abrir pra instalar o seu
próprio sensor de posição, e os fios finos são de sensores que você não vai usar.

**Por que abrir**

Você abre o motor por dois motivos: **tirar de dentro dele o sensor de fábrica** (uma
plaquinha com os cinco fios finos, que a sua placa não usa) e **liberar o eixo** para
montar o seu próprio sensor de posição.

**O que fazer**

Faça nesta ordem:

1. **Desparafuse e retire a tampa traseira do motor.** São parafusos comuns em volta da
   borda. Guarde-os, você vai precisar deles na hora de fechar.
2. **Separe o estator da carcaça.** O estator é a parte de dentro: é ela que tem o eixo
   e os fios de cobre enrolados. Ele **não é parafusado** — o que segura ele é a força
   dos ímãs que ficam na carcaça. Por isso parece travado.
3. **Puxe com força para soltar o estator.** Um puxão firme e um pouco seco resolve.
   Na primeira vez parece que você vai quebrar alguma coisa. É normal, é assim mesmo.
   Cuidado com os dedos: quando ele solta, os ímãs puxam de volta com tudo.
4. **Retire a plaquinha do sensor de fábrica** que fica presa ao estator. Ela costuma
   estar colada, então normalmente sai junto com os pinos da base dela.
5. **Corte os cinco fios finos e ignore.** Eram só desse sensor de fábrica.
6. **Não corte os três fios grossos.** Esses são as fases do motor — a força passa por
   eles, e é neles que a sua placa liga.
7. **Limpe a graxa velha e a ferrugem do eixo** antes de fechar.
8. **Monte o seu sensor de posição na ponta do eixo**, por fora do motor, a não ser que
   você tenha um bom motivo pra não fazer isso. Por fora é muito mais fácil de alcançar
   quando alguma coisa der errado.

#### Qual fonte de alimentação eu preciso?

**O que você vê**

As fontes são anunciadas por volts e watts e você não sabe qual combinação precisa.

**Por que acontece**

A tensão não é escolha livre — ela tem que casar com a placa que você comprou. Os watts
decidem quanta força você consegue segurar antes da fonte desistir.

**O que fazer**

1. **Case a tensão com o que a sua placa aceita — confira a placa, não o motor.**
   Algumas placas aceitam uma faixa larga (muitas vão de 12 a 56 V). Outras vêm em duas
   variantes, uma de tensão menor e outra de tensão maior, e dar tensão demais na menor
   destrói ela. Nas ODESC, que são as mais comuns, dá pra saber qual você tem olhando: o
   adesivo `QC PASS` diz `24V` ou `56V`, e o LED de status é roxo na placa de 24 V e
   verde na de 56 V. Não julgue pelos capacitores — as duas variantes usam os mesmos.

   <table>
   <tr>
   <td width="50%"><img src="screenshots/odesc-v42-24v.jpg" width="100%" alt="ODESC v4.2 24 V — adesivo QC PASS 24V, LED roxo"></td>
   <td width="50%"><img src="screenshots/odesc-v42-56v.jpg" width="100%" alt="ODESC v4.2 56 V — adesivo QC PASS 56V, LED verde"></td>
   </tr>
   <tr>
   <td><b>8–24 V</b> — adesivo <code>24V</code>, LED roxo</td>
   <td><b>8–56 V</b> — adesivo <code>56V</code>, LED verde</td>
   </tr>
   </table>
2. **Mire em 500-600 W** para uma base de volante. Isso é confortável, não apertado.
3. Use uma fonte de saída firme e estável. Fonte tipo tijolo de notebook não serve —
   ela não entrega os picos curtos e fortes de corrente que uma base exige.
4. **Já planeje o resistor de freio agora, não depois.** Quando você gira o volante, o
   motor empurra energia de volta pra fonte. Essa energia precisa ir pra algum lugar,
   senão a placa se desliga no meio da curva. Veja
   [brake-resistor.md](brake-resistor.md).

#### Qual sensor, e onde montar ele?

✅ **validado na bancada**

**O que você vê**

Existem várias opções de sensor de posição e nenhum motivo óbvio pra escolher uma.

**Por que acontece**

O sensor diz pra placa onde o volante está. Todo o resto — força, centralização,
suavidade — é construído em cima dessa única medida. Uma escolha ruim aqui aparece
depois como problemas que parecem ser outra coisa completamente diferente.

**O que fazer**

1. **Monte o sensor direto no eixo do motor.** Não por correia, não por engrenagem.
   Esse é o erro evitável mais comum da montagem inteira.
2. Se você já montou por correia ou engrenagem: toda folga desse acoplamento vira um
   degrau que você sente no volante, e a contagem de pulsos quase nunca fecha num
   número redondo. Mude pro eixo.
3. Pra escolher qual sensor comprar e ver as diferenças entre eles, veja
   [encoders.md](encoders.md).
4. Anote os pulsos por volta do sensor pelo manual dele antes de instalar. Você vai
   precisar desse número pra configurar a placa, e é bem mais difícil ler de um adesivo
   depois que o sensor está enterrado na montagem.

### Não consigo gravar o firmware

Gravar é colocar o programa dentro da placa. É aqui que a maioria conhece a placa pela
primeira vez, e onde muita gente empaca.

#### A placa não entra em modo DFU

✅ **validado na bancada**

**O que você vê**

Você segue as instruções, mas a placa nunca aparece como algo em que dá pra escrever
o firmware.

**Por que acontece**

DFU é um modo especial em que a placa aceita receber um firmware novo em vez de rodar
o que ela já tem. Normalmente um programa consegue pedir pra placa entrar nesse modo.
Em muitas dessas placas esse pedido automático não funciona, e nada te avisa por quê.

**O que fazer**

1. **Faça na mão.** A sua placa tem uma de duas formas de forçar o DFU. Olhe a placa e
   descubra qual é a sua antes de mexer em qualquer coisa:
   - **Um botão**, normalmente escrito `SW1` ou `BOOT0`.
   - **Um jumper**, normalmente perto dos pinos de BOOT — aquela capinha de plástico
     removível que liga dois pinos.
2. **Sempre comece com a placa totalmente desligada.** Não é reset — é tirar a energia
   de verdade e contar até cinco. Os dois métodos abaixo só funcionam no instante em que
   a placa acorda.
3. **Se a sua placa tem botão:** segure o botão apertado, energize a placa ainda
   segurando, e só então solte.
4. **Se a sua placa tem jumper:** qual posição força o DFU depende da placa. Em muitas
   dessas placas você precisa **retirar** o jumper, não colocar. Anote como ele estava
   antes de mexer, mude, e só então energize.
5. **Grave agora, com a placa em DFU.** Ela fica nesse modo até você desligar e ligar
   de novo.
6. **Recoloque o jumper, ou pare de segurar o botão, antes da próxima vez que ligar.**
   Uma placa deixada travada em DFU nunca vai rodar o seu firmware — e isso parece
   exatamente uma gravação que falhou.
7. Se mesmo assim não for, troque o cabo USB e a porta antes de concluir que a placa
   está com defeito. Veja o item seguinte.

#### O PC não reconhece a placa

✅ **validado na bancada**

**O que você vê**

Você espeta a placa e não aparece absolutamente nada no computador.

**Por que acontece**

Quase sempre é o cabo. Muito cabo USB é feito só pra carregar celular: leva energia,
mas não leva dados. A placa acende, então parece que está tudo certo, e o computador
nunca enxerga ela.

**O que fazer**

1. **Troque o cabo USB primeiro.** Use um que você sabe que transfere dados, por
   exemplo aquele com que você já copiou arquivos de um celular. É a solução mais
   barata e é a resposta mais vezes do que qualquer outra coisa.
2. Teste outra porta USB, direto no computador. Evite hub, dock e monitor.
3. No Windows, veja se a placa aparece como dispositivo desconhecido. Se aparecer, o
   cabo está bom e o que falta é o driver.
4. Se a placa só aparece quando está com a fonte dela ligada, isso é uma pista — veja
   o item seguinte.

#### Gravou certo, mas some depois de reiniciar

✅ **validado na bancada**

**O que você vê**

A placa aparece enquanto você grava, a gravação termina sem erro, e depois de
reiniciar ela sumiu.

**Por que acontece**

Duas causas bem diferentes parecem idênticas por fora. Ou o circuito de alimentação
USB da placa está danificado — comum em placa que já foi ligada errado uma vez — ou o
firmware está rodando mas o computador ainda não tem com o que conversar.

**O que fazer**

1. **Confira se o jumper de BOOT voltou pro lugar.** Se você teve que mexer ou retirar
   um jumper pra entrar em DFU e não recolocou, a placa acorda direto em DFU de novo e
   nunca roda o seu firmware. É a causa mais barata e a mais fácil de passar batido.
2. **Alimente a placa pela fonte dela e só então espete o USB.** Se ela aparecer agora,
   mas não aparecia só no USB, a alimentação USB da placa está danificada. A placa
   continua funcionando — ela só precisa de energia externa pra ser vista.
3. Confira se a gravação realmente cobriu a região certa. Uma gravação que diz "sucesso"
   mas escreve no lugar errado deixa a placa muda.
4. Se você só tem uma porta USB disponível, lembre que o gravador de hardware e o cabo
   de dados disputam ela. Grave primeiro, desconecte o gravador, e só então ligue o
   cabo de dados — não espere os dois ao mesmo tempo.
5. Grave mais uma vez pelo método manual de DFU do primeiro item antes de concluir que
   a placa morreu.

### Liga, mas o motor não trava

"Travar" é o momento em que o motor começa a segurar a posição. Antes disso, o volante
gira solto na sua mão. Depois disso, o volante fica firme. Se ele nunca trava, o motor
não está armado.

#### A placa está ligada e conectada, mas o volante continua girando solto

**O que você vê**

As luzes estão acesas, o computador enxerga a placa, mas o volante gira tão solto
quanto girava com tudo desligado.

**Por que acontece**

A placa se recusa a armar o motor quando alguma verificação de partida falhou. Quase
sempre ela já sabe o motivo — só não está te mostrando.

**O que fazer**

1. **Leia o erro que a placa está reportando** antes de mexer em qualquer coisa. Toda
   placa guarda uma lista do que deu errado na partida. Chutar sem ler custa horas.
2. **Confira os três fios grossos do motor** nas duas pontas. Uma fase solta dá um motor
   que não arma, e parece idêntico a um problema de software.
3. **Veja se o sensor de posição está sendo lido.** Gire o volante com a mão e olhe se o
   ângulo mostrado muda. Se o ângulo nunca muda, a placa não faz ideia de onde o volante
   está e não vai armar.
4. **Confirme que a fonte é da tensão certa** pra sua placa e que ela está realmente
   segurando essa tensão com carga.
5. Se a placa reclamar de resistor de freio, vá pro item seguinte — esse é uma armadilha.

#### Ele se recusa a armar por causa do resistor de freio

✅ **validado na bancada**

**O que você vê**

Está tudo certo, a fiação está boa, e o motor continua se recusando a armar. O erro fala
em resistor de freio.

**Por que acontece**

O resistor de freio é a peça que queima a energia que o motor manda de volta quando você
gira o volante. Se a placa está configurada esperando um resistor e não consegue
confirmar que ele existe, ela se recusa a armar — de propósito, pra se proteger. Essa é
a causa mais confusa de "simplesmente não funciona", porque não há nada de errado com
o motor.

**O que fazer**

1. **Primeiro decida se você realmente tem um resistor de freio ligado.** Não planejado,
   não guardado na gaveta — fisicamente ligado na placa agora.
2. Se você não tem um ligado, **desligue a opção de resistor de freio** na configuração
   da placa. O motor vai armar.
3. Se você tem um ligado, **confira a fiação e o valor dele.** Um resistor que está
   ligado mas não conduz reprova no mesmo teste que resistor nenhum.
4. Não trate isso como solução definitiva. Rodar sem resistor de freio é aceitável em
   teste leve de bancada, e vira risco de verdade assim que você dirige com força. Veja
   [brake-resistor.md](brake-resistor.md).
5. Lembre que essa opção sobrevive a uma atualização de firmware, porque ela mora na
   memória de configuração da própria placa. Se o erro voltou "do nada", confira de novo.

### Calibração

Calibração é a rotina curta em que a placa mexe o motor sozinha pra descobrir duas
coisas: como o motor está ligado, e onde o sensor de posição está em relação aos ímãs.
Todo o resto depende de ela estar certa.

#### A calibração nunca termina — treme e trava

✅ **validado na bancada**

**O que você vê**

Você inicia a calibração e o motor engasga violentamente, trava, ou treme forte em vez
de girar liso. Ou para com erro, ou nunca completa.

**Por que acontece**

Durante a calibração a placa empurra corrente no motor enquanto tenta adivinhar onde os
ímãs estão. Se o palpite dela e a realidade discordam, o motor briga consigo mesmo.
Motor de hoverboard piora isso: ele tem muitos pares de ímã e naturalmente "engata" em
posições mesmo desligado.

**O que fazer**

1. **Diminua a corrente de calibração.** Corrente demais faz o motor se jogar de uma
   posição pra outra em vez de acomodar. Baixe aos poucos e tente de novo.
2. **Faça a primeira calibração sem nada preso no eixo.** Sem aro, sem cubo. Peso a mais
   é inércia a mais pro motor brigar.
3. **Confirme o número de pares de ímã** que você informou pra placa. Um número errado
   aqui torna a calibração fisicamente impossível de terminar. Veja o item mais abaixo.
4. **Confirme a contagem de pulsos do sensor.** Mesmo problema, outro número — veja o
   item mais abaixo.
5. **Desligue completamente entre as tentativas.** Não é reset — é tirar a energia,
   esperar, e ligar de novo. Resets repetidos podem deixar o chip que controla o motor
   num estado confuso que só uma desligada de verdade limpa, e isso produz exatamente
   esse sintoma.
6. Se o motor calibra bem uma vez e mal na outra sem você mudar nada, o problema está no
   que ele mede, não nos seus ajustes. Leia a seção "perde o centro".

#### A contagem de pulsos do sensor (CPR) está errada

**O que você vê**

A calibração termina, mas o volante se comporta de forma estranha depois: a amplitude de
giro está errada, os batentes ficam no lugar errado, ou a força parece dar degraus.

**Por que acontece**

CPR é quantas contagens o sensor produz numa volta completa. É por esse número que a
placa converte pulso do sensor em ângulo de verdade. Se ele está errado, todo ângulo que
a placa calcula está errado pelo mesmo fator, e tudo que é construído em cima erra junto.

**O que fazer**

1. **Ache os pulsos por volta (PPR) do sensor no manual dele.** Muitas vezes também está
   impresso no corpo do sensor.
2. **Multiplique o PPR por 4 pra chegar no CPR** num sensor comum de dois canais — daqueles que têm um canal A e um canal B, que é a maioria. Um
   sensor de 600 PPR é 2400 CPR. Esse ×4 é onde quase todo mundo erra — a pessoa digita
   600.
3. **Informe o número referente a uma volta do motor**, não a uma volta do sensor. Só são
   a mesma coisa quando o sensor está montado direto no eixo do motor.
4. Se o seu sensor é movido por engrenagem ou correia, a relação entra multiplicando
   nesse número, e ele raramente cai num valor redondo. É mais um motivo pra montar o
   sensor no eixo.
5. **Teste assim:** gire o volante exatamente uma volta completa com a mão e veja se a
   placa reporta 360°. Se reportar metade ou o dobro, seu CPR está errado nesse fator.

#### O número de pares de ímã está errado

**O que você vê**

O motor zumbe, dá trancos, esquenta, ou se recusa a calibrar, e nada que você mexe nos
ajustes de força melhora.

**Por que acontece**

A placa precisa saber quantos pares de ímã existem dentro do motor pra mandar corrente
pra bobina certa no momento certo. Com o número errado ela energiza a bobina errada —
então o motor esquenta e empurra pro lado errado.

**O que fazer**

1. **A maioria dos motores de hoverboard tem 15 pares de ímã.** Comece por aí.
2. **Conte, se der.** Com o motor aberto, conte os ímãs colados dentro da carcaça e
   divida por dois.
3. **Teste 15, depois 14.** Se nenhum dos dois funcionar, o problema é outro — não fique
   chutando números.
4. Número errado faz o motor esquentar rápido entregando pouca força. Se você sentir essa
   combinação, volte pra este item.

### Treme, trepida ou faz barulho

#### O volante gira em degraus em vez de liso

✅ **validado na bancada**

**O que você vê**

Girar o volante parece arrastar ele por cima de dentes. Você sente pequenos trancos
duros, um atrás do outro, em vez de um movimento liso.

**Por que acontece**

Duas coisas diferentes produzem essa sensação, e separar as duas leva dez segundos.

A primeira é o **cogging**: os ímãs dentro do motor puxam naturalmente pra certas
posições, então o motor "engata" nelas. Isso existe mesmo com tudo desligado, e não é
defeito.

A segunda é **problema no sensor de posição**: se o ângulo que a placa enxerga pula, a
força que ela produz pula junto.

**O que fazer**

1. **Desligue tudo e gire o volante com a mão.** Se você continua sentindo os degraus com
   a energia toda cortada, isso é cogging — é o motor, não a sua montagem.
2. **Se os degraus só aparecem com a energia ligada**, o defeito está no que a placa mede
   ou em como ela aciona o motor. Siga os passos abaixo.
3. **Confira a contagem de pulsos do sensor (CPR) primeiro.** É a causa mais comum de
   degrau com energia ligada. Veja a seção de Calibração.
4. **Confira se o sensor está montado no eixo do motor**, não por correia ou engrenagem.
   Folga de correia ou entre dentes de engrenagem vira exatamente essa sensação de
   degrau.
5. **Refaça a calibração depois de desligar tudo por completo.** Uma calibração que caiu
   numa referência ruim produz degrau que nenhum ajuste de força consegue alisar.
6. Aceite um pouco de cogging como normal. Motor de hoverboard tem ímã forte, e um
   tantinho de aspereza em velocidade bem baixa faz parte. Isso some quando há força de
   verdade sendo produzida.

#### Vibra, zumbe, ou a vibração chega nos meus pedais

**O que você vê**

O volante zumbe ou vibra enquanto está parado, ou você sente uma vibração nos pedais que
só aparece com a base ligada.

**Por que acontece**

Zumbido com o volante parado quer dizer que a placa está corrigindo a posição de forma
agressiva demais: ela passa do ponto, corrige de volta, e repete isso muitas vezes por
segundo. Já vibração que aparece em outro aparelho normalmente não é vibração — é ruído
elétrico viajando pelo terra que os dois compartilham.

**O que fazer**

1. **Se o zumbido só aparece quando o motor está segurando a posição**, diminua o quanto
   a placa corrige. Baixe os ganhos um passo por vez até o zumbido sumir.
2. **Confira se o sensor está fixado com firmeza.** Um sensor que se mexe um pouco em
   relação ao eixo entrega um ângulo trêmulo pra placa, e a placa corre atrás dele.
3. **Se a vibração é sentida nos pedais**, desconecte os pedais do computador e veja se
   para. Se parar, é elétrico, não mecânico.
4. **Não ligue terra em cascata.** Dê à base e aos pedais caminhos próprios até o
   computador, e evite alimentar os pedais pela fonte da base.
5. **Procure fixação frouxa do motor.** Uma base que não está bem parafusada transforma
   torque do motor em vibração do rig — e essa é vibração mecânica de verdade.

### Força fraca ou torta

#### A força é fraca dos dois lados

✅ **validado na bancada**

**O que você vê**

Tudo funciona, nada dá erro, mas o volante está muito mais fraco do que você esperava —
você vence ele fácil com uma mão só.

**Por que acontece**

Força fraca sem erro quase nunca quer dizer que o motor é pequeno demais. Normalmente
quer dizer que estão mandando a placa se segurar, ou que a ideia dela de onde os ímãs
estão está errada, então a corrente que ela manda não vira torque.

**O que fazer**

1. **Confira os limites de corrente e de potência** na configuração da placa primeiro.
   Eles costumam vir baixos de fábrica por segurança, e limite baixo produz exatamente
   isso.
2. **Confira os ajustes de força do próprio jogo.** Um jogo em 30% vai parecer fraco por
   melhor que seja a sua base. Teste com um jogo que você conhece, num ajuste conhecido.
3. **Confirme que a fonte segura a tensão.** Uma fonte que cai sob carga limita o seu
   pico de força silenciosamente.
4. **Refaça a calibração.** Se a referência que a placa achou está errada, a corrente
   entra no motor no momento errado — você ganha calor em vez de força. Se o motor
   esquenta enquanto parece fraco, essa é quase certamente a sua causa.
5. **Confira o tamanho do ímã do motor que você comprou.** Se for bem menor que 30 mm, o
   limite é o próprio motor e nenhum ajuste resolve.

#### É forte pra um lado e fraco pro outro, ou escapa passando do centro

**O que você vê**

O volante empurra forte numa direção e quase nada na outra. Ou ele passa do ponto
central quase sem resistência e depois volta a agarrar.

**Por que acontece**

Isso é problema de simetria. A referência de posição da placa está deslocada de onde
deveria estar, então a força que ela produz fica certa de um lado desse deslocamento e
errada do outro.

**O que fazer**

1. **Centralize o volante mecanicamente primeiro.** Deixe o volante fisicamente reto e só
   então diga pra placa que ali é o centro. Não pule isso — começar torto faz tudo abaixo
   parecer quebrado.
2. **Refaça a calibração com o volante sem carga** e veja se a assimetria muda de lugar.
   Se ela muda a cada vez, a sua referência de posição não é repetível — leia a seção
   "perde o centro".
3. **Confira a contagem de pulsos do sensor.** Contagem errada faz um lado do curso
   chegar cedo no batente e o outro chegar tarde.
4. **Veja se a inversão de força está ligada** em algum lugar onde não devia — na placa,
   e de novo no jogo. Duas inversões se cancelam e são difíceis de perceber.
5. **Confira o acoplamento mecânico até o volante.** Um cubo ou raio que escorrega sob
   carga produz exatamente isso, e muita gente culpa o software.

#### Esquenta muito e mesmo assim tem pouca força

✅ **validado na bancada**

**O que você vê**

O motor fica quente rápido, mas a força que você sente é decepcionante pra tanto calor.

**Por que acontece**

Essa dupla — muito calor, pouca força — é uma assinatura. Ela quer dizer que a corrente
está entrando no motor no momento errado em relação aos ímãs. A energia aparece do mesmo
jeito, só que como calor em vez de torque.

**O que fazer**

1. **Refaça a calibração começando por desligar tudo.** Essa é a solução na maioria dos
   casos.
2. **Confira o número de pares de ímã.** Número errado produz exatamente essa assinatura.
   Veja a seção de Calibração.
3. **Confira se o sensor está preso firme no eixo** e não escorregando. Um sensor que vai
   escorregando faz a referência derivar, e o sintoma volta depois de um tempo mesmo
   tendo começado bem.
4. **Pare e deixe esfriar** antes de repetir testes. Repetir calibração ruim com o motor
   quente é jeito de danificar motor.

### Gira sozinho

#### O volante foge da minha mão

✅ **validado na bancada**

**O que você vê**

Você encosta no volante e ele dispara na direção pra onde você empurrou, ou ele começa a
oscilar e piora quanto mais tempo você segura.

**Por que acontece**

A placa está lendo a velocidade com que o volante se move e somando força na mesma
direção em vez de se opor a ela. Todo movimento pequeno é amplificado. Parece que a coisa
está viva, e é genuinamente perigoso — essa é a falha com mais chance de machucar alguém.

**O que fazer**

1. **Corte a energia na hora** quando isso acontecer. Não tente segurar.
2. **Baixe o limite de força pra um valor pequeno antes de testar de novo.** Nunca
   diagnostique isso na força máxima.
3. **Confira os ajustes de sentido.** Entre o sentido do sensor, a ordem das fases do
   motor e a opção de inverter a força, algum sinal está trocado. Mude um por vez e
   teste.
4. **Troque dois dos três fios grossos do motor** se você já descartou os ajustes. Duas
   fases na ordem errada invertem o sentido da força que o motor faz.
5. **Confira o ajuste de amortecimento.** Amortecimento deve resistir ao movimento. Se
   ele estiver invertido, soma ao movimento — e é exatamente essa oscilação que cresce.
6. Teste com o aro desmontado. É mais seguro, e com menos inércia o defeito fica mais
   fácil de enxergar.

### Esquenta demais

#### Quanto de calor é normal, e eu preciso de ventoinha?

**O que você vê**

O motor fica morno — ou quente — depois de uma sessão, e você não tem referência pra
saber se isso é normal ou é aviso.

**Por que acontece**

Uma base direct drive faz força segurando corrente no motor enquanto ele quase não gira.
Motor que não gira não tem ventilação própria, então quase toda essa energia vira calor
sem ter pra onde ir. Morno é normal. A pergunta é quão morno, e por quanto tempo.

**O que fazer**

1. **Use a mão como primeiro teste.** Se você consegue apoiar a mão no motor e deixar
   ali sem incômodo, está tudo bem. Se você precisa tirar a mão rápido, está puxando
   demais pro que a sua montagem aguenta.
2. **Baixe a força pra 55-65% do máximo.** Só essa mudança resolve a maior parte das
   queixas de calor, e você quase não sente diferença dirigindo.
3. **Saiba o que você usa de verdade.** A maioria das pessoas com base forte acaba
   dirigindo em torno de 7-8 Nm. Rodar no máximo o tempo todo compra calor, não tempo de
   volta.
4. **Pra sessão curta, normalmente não precisa de ventoinha.** Pra sessão longa com
   força alta, precisa.
5. **Se for refrigerar, jogue ar no estator, não na carcaça fechada.** Isso significa
   furar a carcaça pra ventilação. Soprar ar num motor fechado por fora faz muito pouco
   — o calor nasce lá dentro, e a carcaça é um caminho ruim pra ele sair.
6. **Meça a temperatura do motor se puder.** Um número vale mais que um palpite, e
   permite pôr um limite que desliga antes de estragar.

#### Perde força depois de alguns minutos

✅ **validado na bancada**

**O que você vê**

A base parece certa no começo da sessão e vai ficando fraca calada, ou some de vez,
depois de dez ou vinte minutos.

**Por que acontece**

Duas causas parecem iguais do banco do piloto. Ou alguma coisa está se protegendo do
calor e reduzindo a saída, ou a fonte não sustenta o que você está pedindo e a tensão
dela cai até a placa cortar.

**O que fazer**

1. **Encoste no motor na hora que acontecer.** Motor quente aponta pro caminho do calor —
   veja o item acima. Motor frio aponta pra fonte.
2. **Acompanhe a tensão da fonte enquanto dirige**, se a sua montagem mostrar isso. Uma
   tensão que afunda nas curvas fortes e se recupera depois é a sua resposta.
3. **Veja se a placa está registrando evento de subtensão ou sobretensão.** Isso fica
   gravado mesmo quando nada dá erro visível, e explica quedas que parecem aleatórias.
4. **Reduza o limite de força** e veja se a sessão dura mais. Se durar, você estava
   simplesmente pedindo mais do que o sistema segura continuamente.
5. **Instale ou confira o resistor de freio.** Força que some justo nas mudanças rápidas
   de direção é energia voltando do motor sem ter pra onde ir. Veja
   [brake-resistor.md](brake-resistor.md).
6. Entenda a diferença entre torque de pico e contínuo. Uma base de 9 Nm de pico pode
   sustentar só uns 5 Nm. Veja [calculo-torque.md](calculo-torque.md).

### Perde o centro

#### O centro muda toda vez que eu ligo

✅ **validado na bancada**

**O que você vê**

Você deixa o volante reto, usa, desliga, liga no dia seguinte — e o "reto" agora é em
outro lugar.

**Por que acontece**

Essa é a diferença entre dois tipos de sensor de posição. Um sensor **incremental** só
conta movimento; ele não faz ideia de onde está quando acorda, então precisa de uma
referência fixa pra achar o zero de novo. Sem ela, o "zero" acaba sendo onde quer que o
volante estivesse, mais o que a calibração chutou naquele dia. Motor de hoverboard piora
isso, porque os ímãs param o volante num ponto de descanso um pouco diferente a cada vez.

**O que fazer**

1. **Use o sinal de índice do sensor, se ele tiver um.** É o terceiro canal, geralmente
   chamado de Z. Ele marca um ponto exato por volta, e transforma um chute numa
   referência repetível. Essa é a solução de verdade.
2. **Se o seu sensor não tem sinal de índice**, conte com definir o centro toda vez que
   ligar, e transforme isso num botão só em vez de uma tarefa chata.
3. **Um sensor magnético absoluto elimina o problema de vez** — ele já sabe o ângulo no
   instante em que liga. Veja [encoders.md](encoders.md).
4. **Não tente resolver isso mexendo na calibração.** Se a própria referência não é
   repetível, nenhum ajuste vai fazer ela ficar repetível.
5. **Salve o deslocamento do centro** pra ele sobreviver a desligar e ligar, em vez de
   reensinar do zero toda sessão.

#### O centro vai andando enquanto eu dirijo

**O que você vê**

Você começa reto e, depois de algumas voltas, o "reto pra frente" do volante mudou pra
um lado.

**Por que acontece**

Alguma coisa está escorregando. Ou o sensor se move em relação ao eixo, ou o volante se
move em relação ao motor, ou a placa está perdendo contagens — lendo como parado um
movimento que aconteceu.

**O que fazer**

1. **Marque o eixo e o sensor com caneta.** Dirija até derivar e olhe. Se as marcas não
   estão mais alinhadas, achou: o sensor está escorregando.
2. **Faça a mesma marca no cubo do volante.** Cubo ou raio escorregando sob carga é
   comuníssimo, e o software leva a culpa o tempo todo.
3. **Confira os parafusos de aperto** tanto do acoplamento do sensor quanto do cubo do
   volante. Aperte contra um rebaixo plano do eixo, não contra superfície redonda.
4. **Se nada está escorregando fisicamente, suspeite de contagem perdida.** Fiação que
   corre junto dos fios de fase do motor pega interferência e derruba pulsos. Afaste o
   cabo do sensor dos fios grossos do motor, e use cabo blindado.
5. **Deriva que só aparece com força alta** aponta pra interferência elétrica ou pra uma
   peça mecânica que só escorrega sob carga — as duas pioram com força, então teste com
   força baixa pra confirmar.

### Some no jogo

#### O jogo não lista o meu volante

✅ **validado na bancada**

**O que você vê**

A base funciona no computador — você vê ela respondendo num programa de teste de
controle — mas o jogo não oferece ela como volante.

**Por que acontece**

Os jogos procuram um aparelho que se apresente como volante com force feedback. Se a
placa é vista pelo computador mas não se anuncia direito, ou se o jogo varreu os
aparelhos antes da sua base estar pronta, ela simplesmente não está na lista.

**O que fazer**

1. **Confirme primeiro que o computador enxerga ela**, fora do jogo, num programa de
   teste de controle. Se não estiver lá, é problema de conexão — volte pra seção de
   gravação.
2. **Ligue a base antes de abrir o jogo.** Muitos jogos montam a lista de aparelhos uma
   vez só, ao abrir, e nunca mais olham de novo.
3. **Olhe dentro das configurações de controle do jogo**, não só na lista principal.
   Alguns títulos escondem aparelhos não reconhecidos atrás de um menu a mais.
4. **Apague a configuração de controle salva do jogo** e deixe ele detectar do zero. Um
   perfil salvo velho pode estar apontando pra um aparelho que não existe mais.
5. **Teste em outro jogo.** Se aparece num e não noutro, o problema é de como aquele
   título lida com aparelhos, não da sua base.

#### Some no meio da sessão, e só volta se eu reiniciar o jogo

✅ **validado na bancada**

**O que você vê**

O volante funciona, aí a força para no meio da sessão. Reconectar o cabo não resolve —
você precisa sair do jogo e entrar de novo.

**Por que acontece**

Quando a base desconecta e reconecta, o computador pode tratar ela como um aparelho
diferente. O jogo continua segurando o antigo, que não existe mais, então nada que você
faça no cabo traz a força de volta.

**O que fazer**

1. **Descubra por que ela desconectou** — esse é o defeito de verdade. Uma queda rápida
   de energia, um cabo USB no limite, ou um reset da placa causam isso.
2. **Confira de novo o cabo e a porta USB.** Um cabo que funciona quase sempre continua
   sendo um cabo ruim.
3. **Alimente a base pela fonte dela**, não pelo USB, pra que uma queda de energia de um
   lado não derrube o outro.
4. **Se a placa registra eventos de tensão, leia.** Uma placa que reinicia por queda de
   tensão parece exatamente um problema de USB visto de fora.
5. Conte com ter que reiniciar o jogo depois de uma reconexão. Esse é o comportamento
   normal da maioria dos títulos e não é defeito da sua montagem.

### Segurança

✅ **validado na bancada**

Esta seção não é uma lista de sintomas. É a lista curta do que pode te machucar ou
destruir a sua placa. Leia uma vez antes de ligar pela primeira vez.

#### O que pode te machucar

Uma base direct drive não é um motorzinho de brinquedo. Na força máxima ela é mais
forte que o seu pulso.

1. **Nunca enfie os polegares por dentro dos raios** do volante, como você faria num
   volante de brinquedo. Se a base der um tranco pra um lado, é assim que polegar
   quebra.
2. **Comece com a força baixa.** Deixe fraca na primeira sessão e vá subindo ao longo
   de várias sessões. Você não consegue julgar o que é seguro por um número na tela.
3. **Mantenha o volante longe do seu rosto** na primeira ligada e durante a calibração.
   A calibração mexe o motor sozinha, às vezes de forma brusca.
4. **Deixe o interruptor de energia onde você alcança** sem precisar se debruçar por
   cima do volante.

#### O que pode destruir a placa

1. **Girar o motor com tudo desligado empurra tensão de volta pra placa.** Um motor
   girado com a mão é um gerador. Com tudo desligado essa energia não tem pra onde ir,
   e pode danificar a eletrônica. Não gire o volante rápido com a base desligada — e
   saiba que transportar ou arrastar o rig faz isso por você.
2. **Use um resistor de freio.** Quando você gira o volante contra o motor, energia
   volta pelos fios. Sem um lugar pra jogar essa energia fora, a tensão da placa sobe
   até ela se proteger e cortar — normalmente no meio de uma curva. Veja
   [brake-resistor.md](brake-resistor.md).
3. **Case a tensão da fonte com a versão da placa.** Uma placa de 24 V alimentada com
   tensão maior não reclama. Ela morre.
4. **Nunca mexa na fiação do motor com a energia ligada.** Desligue, tire da tomada, e
   dê um tempo pra placa descarregar antes de encostar nos fios de fase.
5. **Não deixe o motor armado e sem ninguém olhando.** Se houver algo errado com o
   sensor, um motor armado consegue empurrar corrente numa posição travada e esquentar
   rápido sem ninguém ver.
