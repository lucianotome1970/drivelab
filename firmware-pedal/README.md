# DriveLab Firmware — Pedaleira (RP2040)

Firmware for the **DriveLab pedal set** — **Waveshare RP2040-Zero** board (RP2040, USB-C), the device side of the **P0** contract.

<p align="center"><a href="#-english">🇬🇧 English</a> &nbsp;·&nbsp; <a href="#-português">🇧🇷 Português</a></p>

---

## 🇬🇧 English

Firmware for the **DriveLab pedal set** — **Waveshare RP2040-Zero** board (RP2040, USB-C), the device side of the **P0** contract.
(Works the same on any RP2040; for a stock Pico, change `board = pico` in `platformio.ini`. The Zero's onboard LED is WS2812/GP16 — the firmware doesn't use the LED.)
Design/decisions: kept in the internal project notes (not versioned in the public repo).

> Firmware **separate** from the wheel (which is ODESC/STM32 in `../firmware/`). Here it's RP2040 + **arduino-pico** (Earle Philhower's core) + Adafruit_TinyUSB. License: MIT (it's our code; TinyUSB is MIT).

> **Status:** ✅ **Validated on hardware** (Waveshare RP2040-Zero, July 2026). M1→M4 written **and validated on the board**. Enumerates as **"DriveLab Pedal"** (VID `0x1209` / PID `0x0002`) — confirmed on macOS/Windows. Settings write (app→firmware) validated; **read-back (`0x16`) validated** after two fixes (see below); **flash persistence confirmed** (config survives reboot/replug).

---

### Current milestone: M4 — Joystick + P0 + load cell (HX711) + flash

**Goal:** a complete, functional firmware: joystick + **P0 protocol** + **load cell** + **persistent config in flash**. **DriveLab Studio connects, reads/writes settings and receives telemetry**; the config survives power-off and is loaded on boot. The app side is already done (`HidPedalTransport` + autodetect by VID/PID `0x1209:0x0002`).

#### What's written and validated (M1–M4)
- **M1** 3-axis 12-bit joystick — fed by the **pipeline** (normalize→deadzone→curve→smooth).
- **M2** P0 vendor reports: telemetry `0x20`, `SettingWrite 0x14`/`ReadRequest 0x15`/`Command 0x02` (out), `SettingValue 0x16` (in); min/max calibration.
- **M3** **Load cell (HX711)** via `sensor_type==2`: DT/SCK pins per pedal = **GP2/3, GP4/5, GP6/7** (clutch/brake/throttle); non-blocking read (`is_ready`) + tare on boot. Pot/Hall stay on the ADC (GP26/27/28).
- **M4** **Flash** (emulated EEPROM): `SaveToFlash` writes; on boot it loads the saved config (magic "DLP1"); otherwise uses defaults. → the config is **per device** and the app loads it on connect.

#### ✅ Written without a board — now resolved/validated on the bench
These points were flagged as risky while the firmware was written blind. All have now been **resolved and validated** on the RP2040-Zero (kept here as history):
1. **TinyUSB OUTPUT reports** (the `onSetReport`) — **confirmed**: the `report_id` comes separately and the `buffer` is the payload **without** the ID (on macOS the input report includes the report id in byte 0). Settings write app→firmware is validated.
2. **Report payload reduced from 64→63 bytes** — TinyUSB's single HID endpoint (`CFG_TUD_HID_EP_BUFSIZE=64`, no guard) can't hold 65 bytes (63 payload + 1 report id). This was the fix that made read-back work.
3. **The `0x16` response is now sent from `loop()` with priority over the joystick** — the single EP drops the 2nd report sent back-to-back, so `0x16` is never sent straight from the callback. With this, **read-back (`0x16`) is validated**.
4. **Off-by-one on integer settings fixed on the app side** (rounding instead of truncation).
- Vendor report descriptor and byte-layout (must match `DriveLab.Core` 1:1): `SettingReport` = [field][index][type][value LE]; `PedalState` = [fw×4][flags][clutch raw+out][brake][throttle], u16 LE — all confirmed working end-to-end.

#### How it was validated
- Flash (BOOTSEL→UF2) and open **DriveLab Studio** with the Pico plugged in → it shows **"Pedals detected"** (source configurable), live bars, and editing curve/deadzone/sensor writes over P0.
- In `joy.cpl` the **"DriveLab Pedal"** still shows the 3 axes.

---

### Previous milestone: M1 — HID Joystick (3-axis 12-bit)

**Goal:** the Pico enumerates on Windows as **"DriveLab Pedal"** (Game Devices) with **3 axes** that react to the ADC. Still WITHOUT the P0 vendor channel (M2) and WITHOUT load cell/curve (M3) — it just reads the 3 ADCs and sends them as axes.

#### Wiring to test (optional, but recommended)
- A **potentiometer** (10k): ends to **3V3** and **GND**, wiper to the ADC. Channels:
  - **A0 = GP26** → clutch (Rx)
  - **A1 = GP27** → brake (Ry)
  - **A2 = GP28** → throttle (Rz)
- With nothing connected, the axes read noise (they float) — normal; wiring a pot shows one axis actually responding.

#### Prerequisites
- **VS Code + PlatformIO extension** (downloads the arduino-pico toolchain by itself on the 1st build).
- **Waveshare RP2040-Zero** (RP2040) + **USB-C** cable.

#### Steps
1. Open the `firmware-pedal/` folder in VS Code (PlatformIO detects `platformio.ini`).
2. **Enter the UF2 bootloader:** on the RP2040-Zero, hold **BOOT** and tap **RESET** (or hold **BOOT** while plugging in USB-C). The **RPI-RP2** drive appears.
3. **Build**: PlatformIO's ✓ icon (or `pio run`). The 1st time downloads the core + TinyUSB (a few minutes).
4. **Upload**: PlatformIO's → icon. PlatformIO copies the `.uf2` to the Pico (which reboots on its own).
5. **See the joystick:** on Windows, `Win+R` → `joy.cpl` → **"DriveLab Pedal"** should appear → Properties → turn the pot and watch the axis move.
6. (Optional) **Serial Monitor** (115200) shows the M1 status line.

#### Expected result (M1 ✅)
- In **Game Devices** (`joy.cpl`): a **"DriveLab Pedal"** controller with **3 axes (Rx/Ry/Rz)**.
- Turning a pot on GP26/27/28, the matching axis sweeps 0→100%.
- Serial: `=== DriveLab Pedaleira — M1 (HID Joystick 3 eixos 12-bit) ===`.

#### Troubleshooting
- **Build fails on `Adafruit_TinyUSB.h`** → check that `build_flags = -DUSE_TINYUSB` is in `platformio.ini` (that's what activates the TinyUSB stack in Philhower's core). Suspect #1 on the 1st build.
- **"DriveLab Pedal" doesn't appear / wrong name** → check the `board_build.arduino.earlephilhower.usb_*` keys (manufacturer/product/vid/pid). Windows caches names by VID/PID; if you change them, you may need to re-plug on another port.
- **RPI-RP2 drive doesn't appear** → hold **BOOT** and tap **RESET** (or hold BOOT while plugging in).
- **Axes "stuck" at max** → ADC floating without a pot; wire a potentiometer (or it's the wrong pin).

---

### Next steps
- **M5** — polish and sim testing. The bring-up validation (M1→M4 on the board) is done; what remains is refining details and testing in an actual sim.

Details in the design.

---

## 🇧🇷 Português

Firmware da **pedaleira DriveLab** — placa **Waveshare RP2040-Zero** (RP2040, USB-C), o lado do dispositivo do contrato **P0**.
(Funciona igual em qualquer RP2040; para o Pico padrão, troque `board = pico` no `platformio.ini`. LED onboard do Zero é WS2812/GP16 — o firmware não usa LED.)
Design/decisões: mantidas nas notas internas de projeto (não versionadas no repo público).

> Firmware **separado** do volante (que é ODESC/STM32 em `../firmware/`). Aqui é RP2040 + **arduino-pico** (core do Earle Philhower) + Adafruit_TinyUSB. Licença: MIT (é código nosso; TinyUSB é MIT).

> **Status:** ✅ **Validado em hardware** (Waveshare RP2040-Zero, julho/2026). M1→M4 escritos **e validados na placa**. Enumera como **"DriveLab Pedal"** (VID `0x1209` / PID `0x0002`) — confirmado no macOS/Windows. Gravação de settings (app→firmware) validada; **leitura de volta (`0x16`) validada** após dois fixes (veja abaixo); **persistência em flash confirmada** (a config sobrevive a reboot/replug).

---

### Marco atual: M4 — Joystick + P0 + load cell (HX711) + flash

**Objetivo:** firmware funcional completo: joystick + **protocolo P0** + **load cell** + **config permanente em flash**. O **DriveLab Studio conecta, lê/grava settings e recebe telemetria**; a config sobrevive ao desligar e é carregada no boot. O app já tem o lado dele pronto (`HidPedalTransport` + autodetecção por VID/PID `0x1209:0x0002`).

#### O que está escrito e validado (M1–M4)
- **M1** Joystick 3 eixos 12-bit — alimentado pelo **pipeline** (normaliza→deadzone→curva→suaviza).
- **M2** Reports vendor P0: telemetria `0x20`, `SettingWrite 0x14`/`ReadRequest 0x15`/`Command 0x02` (out), `SettingValue 0x16` (in); calibração min/max.
- **M3** **Load cell (HX711)** por `sensor_type==2`: pinos DT/SCK por pedal = **GP2/3, GP4/5, GP6/7** (embreagem/freio/acelerador); leitura não-bloqueante (`is_ready`) + tara no boot. Pot/Hall continuam no ADC (GP26/27/28).
- **M4** **Flash** (EEPROM emulada): `SaveToFlash` grava; no boot carrega a config salva (magic "DLP1"); senão usa defaults. → a config fica **por dispositivo** e o app a carrega ao conectar.

#### ✅ Escrito SEM placa — agora resolvido/validado na bancada
Estes pontos foram marcados como suspeitos enquanto o firmware era escrito às cegas. Todos já foram **resolvidos e validados** no RP2040-Zero (mantidos aqui como histórico):
1. **OUTPUT reports do TinyUSB** (o `onSetReport`) — **confirmado**: o `report_id` vem separado e o `buffer` é o payload **sem** o ID (no macOS o input report inclui o report id no byte 0). A gravação de setting app→firmware está validada.
2. **Payload dos reports reduzido de 64→63 bytes** — o endpoint HID único do TinyUSB (`CFG_TUD_HID_EP_BUFSIZE=64`, sem guarda) não comporta 65 bytes (63 payload + 1 report id). Foi o fix que fez a leitura de volta funcionar.
3. **A resposta `0x16` passou a ser enviada do `loop()` com prioridade sobre o joystick** — o EP único dropa o 2º report enviado back-to-back, então nunca se envia `0x16` direto do callback. Com isso, a **leitura de volta (`0x16`) está validada**.
4. **Off-by-one em settings inteiros corrigido no lado do app** (arredondamento em vez de truncamento).
- Report descriptor vendor e byte-layout (devem casar 1:1 com `DriveLab.Core`): `SettingReport` = [field][index][type][valor LE]; `PedalState` = [fw×4][flags][clutch raw+out][brake][throttle], u16 LE — tudo confirmado funcionando de ponta a ponta.

#### Como foi validado
- Grave (BOOTSEL→UF2) e abra o **DriveLab Studio** com o Pico plugado → deve mostrar **"Pedaleira detectada"** (fonte configurável), barras ao vivo, e ao editar curva/deadzone/sensor o app grava via P0.
- No `joy.cpl` o **"DriveLab Pedal"** continua com os 3 eixos.

---

### Marco anterior: M1 — HID Joystick (3 eixos 12-bit)

**Objetivo:** o Pico enumera no Windows como **"DriveLab Pedal"** (Dispositivos de Jogo) com **3 eixos** que reagem ao ADC. Ainda SEM canal vendor P0 (M2) e SEM load cell/curva (M3) — só lê os 3 ADCs e manda como eixos.

#### Ligação para testar (opcional, mas recomendado)
- Um **potenciômetro** (10k): extremos em **3V3** e **GND**, cursor no ADC. Canais:
  - **A0 = GP26** → embreagem (Rx)
  - **A1 = GP27** → freio (Ry)
  - **A2 = GP28** → acelerador (Rz)
- Sem nada ligado, os eixos leem ruído (flutuam) — normal; ligar um pot mostra um eixo respondendo de verdade.

#### Pré-requisitos
- **VS Code + extensão PlatformIO** (baixa o toolchain do arduino-pico sozinho na 1ª build).
- **Waveshare RP2040-Zero** (RP2040) + cabo **USB-C**.

#### Passos
1. Abra a pasta `firmware-pedal/` no VS Code (PlatformIO detecta o `platformio.ini`).
2. **Entre no bootloader UF2:** no RP2040-Zero, segure o botão **BOOT** e dê um toque no **RESET** (ou segure **BOOT** enquanto pluga o USB-C). Aparece a unidade **RPI-RP2**.
3. **Build**: ícone ✓ do PlatformIO (ou `pio run`). A 1ª vez baixa o core + TinyUSB (alguns minutos).
4. **Upload**: ícone → do PlatformIO. O PlatformIO copia o `.uf2` para o Pico (que reinicia sozinho).
5. **Ver o joystick:** no Windows, `Win+R` → `joy.cpl` → deve aparecer **"DriveLab Pedal"** → Propriedades → gire o pot e veja o eixo mexer.
6. (Opcional) **Serial Monitor** (115200) mostra a linha de status do M1.

#### Resultado esperado (M1 ✅)
- Em **Dispositivos de Jogo** (`joy.cpl`): controlador **"DriveLab Pedal"** com **3 eixos (Rx/Ry/Rz)**.
- Girando um pot em GP26/27/28, o eixo correspondente varre 0→100%.
- Serial: `=== DriveLab Pedaleira — M1 (HID Joystick 3 eixos 12-bit) ===`.

#### Se der problema
- **Build falha em `Adafruit_TinyUSB.h`** → confira que `build_flags = -DUSE_TINYUSB` está no `platformio.ini` (é o que ativa o stack TinyUSB no core do Philhower). Suspeito nº1 na 1ª build.
- **Não aparece "DriveLab Pedal" / nome errado** → confira as chaves `board_build.arduino.earlephilhower.usb_*` (manufacturer/product/vid/pid). O Windows cacheia nomes por VID/PID; se trocar, pode precisar re-plugar em outra porta.
- **Não aparece a unidade RPI-RP2** → segure **BOOT** e toque **RESET** (ou segure BOOT ao plugar).
- **Eixos "colados" no máximo** → ADC flutuando sem pot; ligue um potenciômetro (ou é o pino errado).

---

### Próximos passos
- **M5** — polimento e teste no sim. A validação de bring-up (M1→M4 na placa) está feita; falta refinar detalhes e testar num sim de verdade.

Detalhes no design.
