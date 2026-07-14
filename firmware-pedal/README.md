# DriveLab Firmware — Pedaleira (RP2040)

Firmware da **pedaleira DriveLab** — placa **Raspberry Pi Pico (RP2040)**, o lado do dispositivo do contrato **P0**.
Design/decisões: [`../docs/superpowers/specs/2026-07-14-pedal-firmware-rp2040-design.md`](../docs/superpowers/specs/2026-07-14-pedal-firmware-rp2040-design.md).

> Firmware **separado** do volante (que é ODESC/STM32 em `../firmware/`). Aqui é RP2040 + **arduino-pico** (core do Earle Philhower) + Adafruit_TinyUSB. Licença: MIT (é código nosso; TinyUSB é MIT).

> **Status:** M0 (bring-up) feito → **M1 (HID Joystick) escrito, aguardando validação na placa** (o Pico ainda não está em mãos).

---

## Marco atual: M1 — HID Joystick (3 eixos 12-bit)

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
- **M2** — canal **vendor P0**: DriveLab Studio conecta (via `HidPedalTransport`), lê telemetria, grava settings.
- **M3** — sensores (pot/hall/HX711 por `sensor_type`) + pipeline (normaliza→deadzone→curva→suaviza, porta do `PedalCurve`).
- **M4** — persistência em flash (`SaveToFlash`) + calibração (min/max).
- **M5** — polimento + validação num sim.

Detalhes no design.
