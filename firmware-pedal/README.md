# DriveLab Firmware — Pedaleira (RP2040)

Firmware da **pedaleira DriveLab** — placa **Raspberry Pi Pico (RP2040)**, o lado do dispositivo do contrato **P0**.
Design/decisões: [`../docs/superpowers/specs/2026-07-14-pedal-firmware-rp2040-design.md`](../docs/superpowers/specs/2026-07-14-pedal-firmware-rp2040-design.md).

> Firmware **separado** do volante (que é ODESC/STM32 em `../firmware/`). Aqui é RP2040 + **arduino-pico** (core do Earle Philhower) + Adafruit_TinyUSB. Licença: MIT (é código nosso; TinyUSB é MIT).

> **Status:** M0 (bring-up) feito → **M1 (HID Joystick) e M2 (canal vendor P0) escritos, aguardando validação na placa** (o Pico ainda não está em mãos).

---

## Marco atual: M2 — HID Joystick + canal vendor P0

**Objetivo:** além do joystick (M1), o Pico responde o **protocolo P0** na mesma interface HID — o **DriveLab Studio conecta, lê/grava settings e recebe telemetria** (`PedalState`). O app já tem o lado dele pronto (`HidPedalTransport` + autodetecção por VID/PID `0x1209:0x0002`).

### O que o M2 faz
- Joystick 3 eixos 12-bit (do M1) — agora alimentado pelo **pipeline** (normaliza→deadzone→curva→suaviza).
- Reports vendor P0: telemetria `0x20`, `SettingWrite 0x14`/`ReadRequest 0x15`/`Command 0x02` (out), `SettingValue 0x16` (in).
- Settings **em RAM** (persistência em flash = **M4**), calibração min/max (`CalibrateStart/Stop`).
- Só **ADC analógico** (pot/hall); **load cell/HX711 = M3**.

### ⚠️ Escrito SEM placa — o que conferir primeiro na bancada
1. **OUTPUT reports do TinyUSB** (o `onSetReport`): é o **suspeito nº1**. Confirmar a assinatura/entrega do `setReportCallback` do Adafruit_TinyUSB — se o `report_id` vem separado e o `buffer` é o payload (assumido), ou se o ID vem em `buffer[0]`. Se o app grava setting e nada muda, é aqui.
2. **Report descriptor vendor** — se o Windows/HidSharp não enxergar os reports 0x14/0x15/0x16/0x20, revisar o bloco vendor.
3. **Byte-layout** dos reports (deve casar 1:1 com `DriveLab.Core`): `SettingReport` = [field][index][type][valor LE]; `PedalState` = [fw×4][flags][clutch raw+out][brake][throttle], u16 LE.

### Como validar
- Grave (BOOTSEL→UF2) e abra o **DriveLab Studio** com o Pico plugado → deve mostrar **"Pedaleira detectada"** (fonte configurável), barras ao vivo, e ao editar curva/deadzone/sensor o app grava via P0.
- No `joy.cpl` o **"DriveLab Pedal"** continua com os 3 eixos.

---

## Marco anterior: M1 — HID Joystick (3 eixos 12-bit)

**Objetivo:** o Pico enumera no Windows como **"DriveLab Pedal"** (Dispositivos de Jogo) com **3 eixos** que reagem ao ADC. Ainda SEM canal vendor P0 (M2) e SEM load cell/curva (M3) — só lê os 3 ADCs e manda como eixos.

### Ligação para testar (opcional, mas recomendado)
- Um **potenciômetro** (10k): extremos em **3V3** e **GND**, cursor no ADC. Canais:
  - **A0 = GP26** → embreagem (Rx)
  - **A1 = GP27** → freio (Ry)
  - **A2 = GP28** → acelerador (Rz)
- Sem nada ligado, os eixos leem ruído (flutuam) — normal; ligar um pot mostra um eixo respondendo de verdade.

### Pré-requisitos
- **VS Code + extensão PlatformIO** (baixa o toolchain do arduino-pico sozinho na 1ª build).
- **Raspberry Pi Pico** (RP2040) + cabo USB.

### Passos
1. Abra a pasta `firmware-pedal/` no VS Code (PlatformIO detecta o `platformio.ini`).
2. **Entre no bootloader UF2:** segure o botão **BOOTSEL** do Pico **enquanto** o pluga na USB. Aparece a unidade **RPI-RP2**.
3. **Build**: ícone ✓ do PlatformIO (ou `pio run`). A 1ª vez baixa o core + TinyUSB (alguns minutos).
4. **Upload**: ícone → do PlatformIO. O PlatformIO copia o `.uf2` para o Pico (que reinicia sozinho).
5. **Ver o joystick:** no Windows, `Win+R` → `joy.cpl` → deve aparecer **"DriveLab Pedal"** → Propriedades → gire o pot e veja o eixo mexer.
6. (Opcional) **Serial Monitor** (115200) mostra a linha de status do M1.

### Resultado esperado (M1 ✅)
- Em **Dispositivos de Jogo** (`joy.cpl`): controlador **"DriveLab Pedal"** com **3 eixos (Rx/Ry/Rz)**.
- Girando um pot em GP26/27/28, o eixo correspondente varre 0→100%.
- Serial: `=== DriveLab Pedaleira — M1 (HID Joystick 3 eixos 12-bit) ===`.

### Se der problema (M1 não validado em hardware ainda)
- **Build falha em `Adafruit_TinyUSB.h`** → confira que `build_flags = -DUSE_TINYUSB` está no `platformio.ini` (é o que ativa o stack TinyUSB no core do Philhower). Suspeito nº1 na 1ª build.
- **Não aparece "DriveLab Pedal" / nome errado** → confira as chaves `board_build.arduino.earlephilhower.usb_*` (manufacturer/product/vid/pid). O Windows cacheia nomes por VID/PID; se trocar, pode precisar re-plugar em outra porta.
- **Não aparece a unidade RPI-RP2** → segure o BOOTSEL ANTES de plugar.
- **Eixos "colados" no máximo** → ADC flutuando sem pot; ligue um potenciômetro (ou é o pino errado).

> Quando o M1 passar, seguimos pro **M2** (canal vendor P0 → o app conecta e configura).

---

## Próximos marcos (resumo)
- **M3** — **load cell/HX711** por `sensor_type` (o pipeline analógico já está no M2).
- **M4** — **persistência em flash** (`SaveToFlash`/boot carrega a config; EEPROM emulada) → config sobrevive ao desligar e o app a carrega ao conectar.
- **M5** — polimento + validação num sim.

Detalhes no design.
