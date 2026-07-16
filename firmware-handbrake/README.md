# DriveLab Firmware — Freio de mão (RP2040)

<p align="center"><b>Handbrake firmware — Waveshare RP2040-Zero (RP2040, USB-C)</b><br/>
Firmware do freio de mão DriveLab — placa Waveshare RP2040-Zero (RP2040, USB-C).</p>

<p align="center"><a href="#-english">🇬🇧 English</a> &nbsp;·&nbsp; <a href="#-português">🇧🇷 Português</a></p>

---

## 🇬🇧 English

Firmware for the **DriveLab handbrake** — **Waveshare RP2040-Zero** board (RP2040, USB-C).
(Works the same on any RP2040; for a stock Pico, switch `board = pico` in `platformio.ini`. The Zero's onboard LED is WS2812/GP16 — the firmware does not use the LED.)
Design/decisions: kept in the internal project notes (not versioned in the public repo).

> Firmware **separate** from the wheel (ODESC/STM32 in `../firmware/`) and from the pedals (`../firmware-pedal/`). This is RP2040 + **arduino-pico** (Earle Philhower's core) + Adafruit_TinyUSB. License: MIT (it's our code; TinyUSB is MIT).

> **Status:** M5 — HID (1-axis + 1-button Joystick) + vendor P0 protocol + sensors (ADC/HX711) + pipeline + button with hysteresis + flash persistence. **Validated in hardware** (same Waveshare RP2040-Zero board as the pedals, July/2026), **with one caveat: not yet tested with a physical sensor connected.**
> - Enumerates as **"DriveLab Handbrake"** (VID `0x1209` / PID `0x0003`) — confirmed via `ioreg` on macOS.
> - The **0x16** fix is applied (send from `loop()` with priority over the joystick, and payload ≤63 bytes — TinyUSB's single HID EP drops the 2nd back-to-back report), identical to the pedals. **Validated on the wire**: read Smooth=0 → write 42 → read 42.
> - In **DriveLab Studio**: autodetect OK, it **loads the board's parameters**, the **"Save to controller"** button with dirty-tracking works, and the label shows **"Handbrake detected"**.
> - Still to test: everything that needs a **physical sensor** connected (axis movement, button lighting via `buttonThreshold`, real min/max calibration). See the bench checklist below.

**Handbrake scope:** a single axis (potentiometer/Hall or HX711 load cell) + one digital button (threshold over the axis output, with hysteresis). A direct port of `../firmware-pedal/src/main.cpp` (3 axes) reduced to 1 axis + button, speaking the same P0 protocol the app (`DriveLab.Hid.HidHandbrakeTransport`) already expects.

---

### Current milestone: M5 — HID + P0 + sensors/pipeline/button/flash

#### What the firmware does
- **HID Joystick** (report `0x01`): 1 axis `Rx` (16-bit field, values 0..4095) + 1 button (1 bit) + 7 bits of padding.
- **Vendor P0** (usage page `0xFF00`, identical to the pedal):
  - `0x20` PedalState (telemetry, ~100 Hz) — axis in the **Clutch** slot (offset 5..8: raw u16 LE, output u16 LE); Brake/Throttle slots zeroed; `Flags` (offset 4) bit0 = button pressed (`HandbrakeFlags.ButtonPressed`).
  - `0x14` SettingWrite / `0x15` SettingReadRequest / `0x16` SettingValue — fields 0–13 same as the pedal (sensor/min/max/invert/smooth/curve×6/loadCellScale/deadzone), **14 = ButtonThreshold, 15 = ButtonEnabled**. The wire's index byte is accepted but ignored (single axis).
  - `0x02` Command — `CalibrateStart/Stop` (single axis), `SaveToFlash`, `LoadDefaults`.
- **Sensor:** ADC (`A0`/GP26) when `sensorType != 2`, or HX711 (DT=GP2, SCK=GP3) when `sensorType == 2`; the read does not block the loop (checks `is_ready()`).
- **Pipeline:** normalize (min/max) → invert → deadzone (low/high) → 6-point curve → smoothing (EMA) → clamp 0..4095/0..65535 — same math as the pedal.
- **Button:** `outputPct = output/655.35`; turns on when `outputPct >= buttonThreshold`; turns off when `outputPct < buttonThreshold - 3` (3-point hysteresis), mirroring `HandbrakeDeviceModel.UpdateButton`. Reflected both in the Joystick bit and in the `Flags` bit0 of PedalState.
- **Flash (emulated EEPROM):** magic `"DLH1"` + the full `HandbrakeCfg` (includes `buttonThreshold`/`buttonEnabled`); `SaveToFlash` writes, boot loads it if the magic matches, otherwise uses defaults.

#### Build
```bash
cd firmware-handbrake
/Users/macos/Library/Python/3.9/bin/pio run -e rp2040_zero
```
Local build confirmed: **SUCCESS**, generates `.pio/build/rp2040_zero/firmware.uf2` (~207 KB), RAM 6.7%, Flash 4.4% of the RP2040.

#### Flashing
1. Hold **BOOT** and tap **RESET** (or hold BOOT while plugging in the USB-C) — the **RPI-RP2** drive appears.
2. `pio run -e rp2040_zero -t upload` (or PlatformIO's → icon) — copies the `.uf2`.

---

### Bench validation checklist

> Items checked below were **validated in hardware** (July/2026). Unchecked items still depend on a **physical sensor connected**, which was **not** tested — the board was validated bare, without a pot/Hall/HX711 wired to the axis.

- [x] Enumerates as **DriveLab Handbrake** (VID `0x1209` / PID `0x0003`) — confirmed via `ioreg` on macOS. (The `joy.cpl` test on Windows — 1 axis + 1 button, not 3 axes like the pedal — is still open if you want it, but enumeration is confirmed.)
- [x] Autodetect in DriveLab Studio (`HidHandbrakeTransport.IsDevicePresent()` → handbrake page marked "detected").
- [x] Settings write/read both ways (sensor type, min/max, invert, smooth, curve, deadzone, loadCellScale, buttonThreshold, buttonEnabled) — validated on the wire (read Smooth=0 → write 42 → read 42) and in the app (loads the board's params; "Save to controller" with dirty-tracking).
- [x] `SaveToFlash` persists the config (incl. buttonThreshold/buttonEnabled).
- [ ] Moving the sensor (pot/Hall or load cell), the axis moves on the controller test screen (0..100%). *(needs a physical sensor)*
- [ ] Crossing the `buttonThreshold` (default 70%) lights the button; dropping below `threshold - 3%` turns it off (hysteresis, no flicker near the threshold). *(needs a physical sensor)*
- [ ] `CalibrateStart`/`CalibrateStop` capture the sensor's real min/max. *(needs a physical sensor)*
- [ ] `LoadDefaults` restores factory defaults without needing to disconnect.

### Known risks (inherited from firmware-pedal)
- **OUTPUT reports / 0x16** plumbing of Adafruit_TinyUSB (`setReportCallback`/`onSetReport`) and the `0x16` reply from `loop()` — **RESOLVED/validated**: sending from `loop()` with priority over the joystick and a payload ≤63 bytes fixes the single-EP drop; validated on the wire (read → write → read).
- Vendor report descriptor and the new **Joystick** layout (1 axis 16-bit + 1-bit button + 7 bits of padding) — validated via enumeration (`ioreg`); a full HID-parser / Windows `joy.cpl` pass is still open.
- HX711 with no board connected may never become `is_ready()`; the firmware does not hang (raw stays 0), but this was **not** observed with real hardware — the axis was validated bare, no sensor wired.

---

### Milestone history
- M0 — scaffold (blink + serial, no HID).
- M1–M4 (inherited from the pedal design, applied all at once in M5 below).
- **M5 (current)** — HID Joystick (1 axis + button) + vendor P0 + ADC/HX711 + pipeline + button with hysteresis + flash. Validated in hardware (bare board). This README.

### Next step
Test with a **physical sensor** connected (pot/Hall or HX711): axis movement, button via `buttonThreshold`, and real min/max calibration — the only checklist items still pending.

---

## 🇧🇷 Português

Firmware do **freio de mão DriveLab** — placa **Waveshare RP2040-Zero** (RP2040, USB-C).
(Funciona igual em qualquer RP2040; para o Pico padrão, troque `board = pico` no `platformio.ini`. LED onboard do Zero é WS2812/GP16 — o firmware não usa LED.)
Design/decisões: mantidas nas notas internas de projeto (não versionadas no repo público).

> Firmware **separado** do volante (ODESC/STM32 em `../firmware/`) e da pedaleira (`../firmware-pedal/`). Aqui é RP2040 + **arduino-pico** (core do Earle Philhower) + Adafruit_TinyUSB. Licença: MIT (é código nosso; TinyUSB é MIT).

> **Status:** M5 — HID (Joystick 1 eixo + 1 botão) + protocolo vendor P0 + sensores (ADC/HX711) + pipeline + botão com histerese + persistência em flash. **Validado em hardware** (mesma placa Waveshare RP2040-Zero da pedaleira, julho/2026), **com uma ressalva: ainda não testado com um sensor físico conectado.**
> - Enumera como **"DriveLab Handbrake"** (VID `0x1209` / PID `0x0003`) — confirmado via `ioreg` no macOS.
> - Fix do **0x16** aplicado (enviar do `loop()` com prioridade sobre o joystick, e payload ≤63 bytes — o EP HID único do TinyUSB dropa o 2º report back-to-back), idêntico ao da pedaleira. **Validado no fio**: read Smooth=0 → write 42 → read 42.
> - No **DriveLab Studio**: autodetecção OK, **carrega os parâmetros da placa**, o botão **"Salvar no controlador"** com dirty-tracking funciona, e o rótulo mostra **"Freio de mão detectado"**.
> - Ainda falta testar: tudo que depende de um **sensor físico** ligado (movimento do eixo, acender o botão pelo `buttonThreshold`, calibração de min/max real). Ver checklist de bancada abaixo.

**Escopo do freio de mão:** um único eixo (potenciômetro/Hall ou célula de carga HX711) + um botão digital (limiar sobre o output do eixo, com histerese). Porta direta de `../firmware-pedal/src/main.cpp` (3 eixos) reduzida a 1 eixo + botão, falando o mesmo protocolo P0 que o app (`DriveLab.Hid.HidHandbrakeTransport`) já espera.

---

### Marco atual: M5 — HID + P0 + sensores/pipeline/botão/flash

#### O que o firmware faz
- **HID Joystick** (report `0x01`): 1 eixo `Rx` (campo de 16 bits, valores 0..4095) + 1 botão (1 bit) + 7 bits de padding.
- **Vendor P0** (usage page `0xFF00`, idêntico ao pedal):
  - `0x20` PedalState (telemetria, ~100 Hz) — eixo no slot **Clutch** (offset 5..8: raw u16 LE, output u16 LE); slots Brake/Throttle zerados; `Flags` (offset 4) bit0 = botão pressionado (`HandbrakeFlags.ButtonPressed`).
  - `0x14` SettingWrite / `0x15` SettingReadRequest / `0x16` SettingValue — campos 0–13 iguais ao pedal (sensor/min/max/invert/smooth/curva×6/loadCellScale/deadzone), **14 = ButtonThreshold, 15 = ButtonEnabled**. O byte de índice do wire é aceito mas ignorado (eixo único).
  - `0x02` Command — `CalibrateStart/Stop` (eixo único), `SaveToFlash`, `LoadDefaults`.
- **Sensor:** ADC (`A0`/GP26) quando `sensorType != 2`, ou HX711 (DT=GP2, SCK=GP3) quando `sensorType == 2`; leitura não bloqueia o loop (checa `is_ready()`).
- **Pipeline:** normaliza (min/max) → invert → deadzone (low/high) → curva de 6 pontos → suavização (EMA) → clamp 0..4095/0..65535 — mesma matemática do pedal.
- **Botão:** `outputPct = output/655.35`; liga quando `outputPct >= buttonThreshold`; desliga quando `outputPct < buttonThreshold - 3` (histerese de 3 pontos), espelhando `HandbrakeDeviceModel.UpdateButton`. Refletido tanto no bit do Joystick quanto no bit0 de `Flags` do PedalState.
- **Flash (EEPROM emulada):** magic `"DLH1"` + `HandbrakeCfg` completo (inclui `buttonThreshold`/`buttonEnabled`); `SaveToFlash` grava, boot carrega se o magic bater, senão usa defaults.

#### Build
```bash
cd firmware-handbrake
/Users/macos/Library/Python/3.9/bin/pio run -e rp2040_zero
```
Build local confirmado: **SUCCESS**, gera `.pio/build/rp2040_zero/firmware.uf2` (~207 KB), RAM 6.7%, Flash 4.4% do RP2040.

#### Gravação
1. Segure **BOOT** e toque **RESET** (ou segure BOOT ao plugar o USB-C) — aparece a unidade **RPI-RP2**.
2. `pio run -e rp2040_zero -t upload` (ou ícone → do PlatformIO) — copia o `.uf2`.

---

### Checklist de validação em bancada

> Os itens marcados abaixo foram **validados em hardware** (julho/2026). Os desmarcados ainda dependem de um **sensor físico conectado**, o que **não** foi testado — a placa foi validada nua, sem um pot/Hall/HX711 ligado ao eixo.

- [x] Enumera como **DriveLab Handbrake** (VID `0x1209` / PID `0x0003`) — confirmado via `ioreg` no macOS. (O teste com `joy.cpl` no Windows — 1 eixo + 1 botão, não 3 eixos como o pedal — fica pendente se quiser, mas a enumeração está confirmada.)
- [x] Autodetecção no DriveLab Studio (`HidHandbrakeTransport.IsDevicePresent()` → página do freio de mão marcada como "detectado").
- [x] Gravação/leitura de settings nos dois sentidos (sensor type, min/max, invert, smooth, curva, deadzone, loadCellScale, buttonThreshold, buttonEnabled) — validado no fio (read Smooth=0 → write 42 → read 42) e no app (carrega os parâmetros da placa; "Salvar no controlador" com dirty-tracking).
- [x] `SaveToFlash` persiste a config (incl. buttonThreshold/buttonEnabled).
- [ ] Movendo o sensor (pot/Hall ou célula de carga), o eixo se move na tela de teste do controlador (0..100%). *(precisa de sensor físico)*
- [ ] Ao cruzar o `buttonThreshold` (default 70%) o botão acende; ao cair abaixo de `threshold - 3%` o botão apaga (histerese, sem "tremedeira" perto do limiar). *(precisa de sensor físico)*
- [ ] `CalibrateStart`/`CalibrateStop` capturam min/max reais do sensor. *(precisa de sensor físico)*
- [ ] `LoadDefaults` restaura os defaults de fábrica sem precisar desconectar.

### Riscos conhecidos (herdados do firmware-pedal)
- Plumbing dos **OUTPUT reports / 0x16** do Adafruit_TinyUSB (`setReportCallback`/`onSetReport`) e a resposta `0x16` a partir do `loop()` — **RESOLVIDO/validado**: enviar do `loop()` com prioridade sobre o joystick e payload ≤63 bytes corrige o drop do EP único; validado no fio (read → write → read).
- Report descriptor **vendor** e o novo layout do **Joystick** (1 eixo 16-bit + botão de 1 bit + 7 bits de padding) — validado via enumeração (`ioreg`); falta uma passada por HID parser / `joy.cpl` no Windows.
- HX711 sem placa conectada pode nunca ficar `is_ready()`; o firmware não trava (raw fica 0), mas isso **não** foi observado com hardware real — o eixo foi validado nu, sem sensor ligado.

---

### Histórico de marcos
- M0 — scaffold (blink + serial, sem HID).
- M1–M4 (herdados do design do pedal, aplicados de uma vez no M5 abaixo).
- **M5 (atual)** — HID Joystick (1 eixo + botão) + vendor P0 + ADC/HX711 + pipeline + botão com histerese + flash. Validado em hardware (placa nua). Este README.

### Próximo passo
Testar com um **sensor físico** conectado (pot/Hall ou HX711): movimento do eixo, botão pelo `buttonThreshold`, e calibração de min/max reais — os únicos itens do checklist ainda pendentes.
