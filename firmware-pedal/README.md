# DriveLab Firmware — Pedaleira (RP2040)

Firmware da **pedaleira DriveLab** — placa **Raspberry Pi Pico (RP2040)**, o lado do dispositivo do contrato **P0**.
Design/decisões: [`../docs/superpowers/specs/2026-07-14-pedal-firmware-rp2040-design.md`](../docs/superpowers/specs/2026-07-14-pedal-firmware-rp2040-design.md).

> Firmware **separado** do volante (que é ODESC/STM32 em `../firmware/`). Aqui é RP2040 + **arduino-pico** (core do Earle Philhower) + Adafruit_TinyUSB. Licença: MIT (é código nosso; TinyUSB é MIT).

---

## Marco atual: M0 — bring-up (blink + serial, SEM HID)

**Objetivo:** provar toolchain + gravação + serial USB no Pi Pico. Inofensivo (não mexe em nada além do LED).

### Pré-requisitos
- **VS Code + extensão PlatformIO** (baixa o toolchain do arduino-pico sozinho na 1ª build).
- **Raspberry Pi Pico** (RP2040) + cabo USB.
- Nada além do Pico — não precisa de sensores neste marco.

### Passos
1. Abra a pasta `firmware-pedal/` no VS Code (PlatformIO detecta o `platformio.ini`).
2. **Entre no bootloader UF2:** segure o botão **BOOTSEL** do Pico **enquanto** o pluga na USB. Aparece a unidade **RPI-RP2**.
3. **Build**: ícone ✓ do PlatformIO (ou `pio run`). A 1ª vez baixa o core (alguns minutos).
4. **Upload**: ícone → do PlatformIO. O PlatformIO copia o `.uf2` para o Pico (que reinicia sozinho).
5. **Serial Monitor**: ícone 🔌 (ou `pio device monitor`), **115200** baud.

### Resultado esperado (M0 ✅)
- LED onboard (GP25) piscando ~2 Hz.
- No monitor serial:
```
=== DriveLab Pedaleira — M0 bring-up ===
RP2040 vivo. Proximo marco: M1 (HID Joystick).
DriveLab pedal M0 vivo, tick = 0
DriveLab pedal M0 vivo, tick = 1
...
```

### Se der problema
- **Build falha** → mande o erro (às vezes é a 1ª sincronização do core; tentar de novo).
- **Não aparece a unidade RPI-RP2** → segure o BOOTSEL ANTES de plugar e só solte depois de conectado.
- **Gravou mas sem serial** → confira 115200 baud e a porta certa; no Pico o `Serial` é USB CDC (aparece após o boot).

---

## Próximos marcos (resumo)
- **M1** — HID **Joystick**: 3 eixos do ADC (GP26/27/28), Windows vê 3 eixos. *(de-risca o USB HID)*
- **M2** — canal **vendor P0**: DriveLab Studio conecta (via `HidPedalTransport`), lê telemetria, grava settings.
- **M3** — sensores (pot/hall/HX711 por `sensor_type`) + pipeline (normaliza→deadzone→curva→suaviza, porta do `PedalCurve`).
- **M4** — persistência em flash (`SaveToFlash`) + calibração (min/max).
- **M5** — polimento + validação num sim.

Detalhes no design.
