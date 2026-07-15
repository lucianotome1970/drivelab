# DriveLab Firmware — Freio de mão (RP2040)

Firmware do **freio de mão DriveLab** — placa **Waveshare RP2040-Zero** (RP2040, USB-C).
(Funciona igual em qualquer RP2040; para o Pico padrão, troque `board = pico` no `platformio.ini`. LED onboard do Zero é WS2812/GP16 — o firmware não usa LED.)
Design/decisões: mantidas nas notas internas de projeto (não versionadas no repo público).

> Firmware **separado** do volante (ODESC/STM32 em `../firmware/`) e da pedaleira (`../firmware-pedal/`). Aqui é RP2040 + **arduino-pico** (core do Earle Philhower) + Adafruit_TinyUSB. Licença: MIT (é código nosso; TinyUSB é MIT).

> **Status:** M5 — HID (Joystick 1 eixo + 1 botão) + protocolo vendor P0 + sensores (ADC/HX711) + pipeline + botão com histerese + persistência em flash. **Escrito e compilado (.uf2 gerado), mas NÃO validado em hardware** — o RP2040-Zero ainda não está em mãos. Ver checklist de bancada abaixo.

**Escopo do freio de mão:** um único eixo (potenciômetro/Hall ou célula de carga HX711) + um botão digital (limiar sobre o output do eixo, com histerese). Porta direta de `../firmware-pedal/src/main.cpp` (3 eixos) reduzida a 1 eixo + botão, falando o mesmo protocolo P0 que o app (`DriveLab.Hid.HidHandbrakeTransport`) já espera.

---

## Marco atual: M5 — HID + P0 + sensores/pipeline/botão/flash

### O que o firmware faz
- **HID Joystick** (report `0x01`): 1 eixo `Rx` (campo de 16 bits, valores 0..4095) + 1 botão (1 bit) + 7 bits de padding.
- **Vendor P0** (usage page `0xFF00`, idêntico ao pedal):
  - `0x20` PedalState (telemetria, ~100 Hz) — eixo no slot **Clutch** (offset 5..8: raw u16 LE, output u16 LE); slots Brake/Throttle zerados; `Flags` (offset 4) bit0 = botão pressionado (`HandbrakeFlags.ButtonPressed`).
  - `0x14` SettingWrite / `0x15` SettingReadRequest / `0x16` SettingValue — campos 0–13 iguais ao pedal (sensor/min/max/invert/smooth/curva×6/loadCellScale/deadzone), **14 = ButtonThreshold, 15 = ButtonEnabled**. O byte de índice do wire é aceito mas ignorado (eixo único).
  - `0x02` Command — `CalibrateStart/Stop` (eixo único), `SaveToFlash`, `LoadDefaults`.
- **Sensor:** ADC (`A0`/GP26) quando `sensorType != 2`, ou HX711 (DT=GP2, SCK=GP3) quando `sensorType == 2`; leitura não bloqueia o loop (checa `is_ready()`).
- **Pipeline:** normaliza (min/max) → invert → deadzone (low/high) → curva de 6 pontos → suavização (EMA) → clamp 0..4095/0..65535 — mesma matemática do pedal.
- **Botão:** `outputPct = output/655.35`; liga quando `outputPct >= buttonThreshold`; desliga quando `outputPct < buttonThreshold - 3` (histerese de 3 pontos), espelhando `HandbrakeDeviceModel.UpdateButton`. Refletido tanto no bit do Joystick quanto no bit0 de `Flags` do PedalState.
- **Flash (EEPROM emulada):** magic `"DLH1"` + `HandbrakeCfg` completo (inclui `buttonThreshold`/`buttonEnabled`); `SaveToFlash` grava, boot carrega se o magic bater, senão usa defaults.

### Build
```bash
cd firmware-handbrake
/Users/macos/Library/Python/3.9/bin/pio run -e rp2040_zero
```
Build local confirmado: **SUCCESS**, gera `.pio/build/rp2040_zero/firmware.uf2` (~207 KB), RAM 6.7%, Flash 4.4% do RP2040.

### Gravação
1. Segure **BOOT** e toque **RESET** (ou segure BOOT ao plugar o USB-C) — aparece a unidade **RPI-RP2**.
2. `pio run -e rp2040_zero -t upload` (ou ícone → do PlatformIO) — copia o `.uf2`.

---

## Checklist de validação em bancada (hardware pendente — preencher ao testar)

> Nada abaixo foi executado em hardware real; é o roteiro a seguir quando a placa chegar.

- [ ] Windows → "Dispositivos e impressoras" / "Configurar dispositivos de jogo USB" mostra **DriveLab Handbrake** com **1 eixo** e **1 botão** (não 3 eixos como o pedal).
- [ ] Movendo o sensor (pot/Hall ou célula de carga), o eixo se move na tela de teste do controlador (0..100%).
- [ ] Ao cruzar o `buttonThreshold` (default 70%) o botão acende; ao cair abaixo de `threshold - 3%` o botão apaga (histerese, sem "tremedeira" perto do limiar).
- [ ] DriveLab Studio: autodetecta o freio de mão (`HidHandbrakeTransport.IsDevicePresent()` → página do freio de mão marcada como "detectado").
- [ ] Studio: telemetria anima em tempo real (raw/output do eixo, estado do botão).
- [ ] Studio: gravação/leitura de settings (sensor type, min/max, invert, smooth, curva, deadzone, loadCellScale, buttonThreshold, buttonEnabled) funciona nos dois sentidos.
- [ ] Studio: `CalibrateStart`/`CalibrateStop` capturam min/max reais do sensor.
- [ ] `SaveToFlash` persiste a config (incl. buttonThreshold/buttonEnabled) através de um replug/reset da placa.
- [ ] `LoadDefaults` restaura os defaults de fábrica sem precisar desconectar.

### Riscos conhecidos (herdados do firmware-pedal, ainda não testados em placa)
- Plumbing dos **OUTPUT reports** do Adafruit_TinyUSB (`setReportCallback`/`onSetReport`): assume `report_id` + payload sem o ID; se o core entregar diferente, ajustar `onSetReport`.
- Report descriptor **vendor** e o novo layout do **Joystick** (1 eixo 16-bit + botão de 1 bit + 7 bits de padding) — não validado com um HID parser real (ex.: usbhid-dump/USB Descriptor Tool) nem com o Windows.
- HX711 sem placa conectada pode nunca ficar `is_ready()`; o firmware não trava (raw fica 0), mas isso não foi observado com hardware real.

---

## Histórico de marcos
- M0 — scaffold (blink + serial, sem HID).
- M1–M4 (herdados do design do pedal, aplicados de uma vez no M5 abaixo).
- **M5 (atual)** — HID Joystick (1 eixo + botão) + vendor P0 + ADC/HX711 + pipeline + botão com histerese + flash. Este README.

## Próximo passo
Validação em bancada (checklist acima) assim que o RP2040-Zero estiver disponível fisicamente.
