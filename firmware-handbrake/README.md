# DriveLab Firmware — Handbrake (RP2040)

Firmware for the **DriveLab handbrake** — **Waveshare RP2040-Zero** board (RP2040, USB-C).

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

---

## 🇬🇧 English

> **Status: ✅ validated on hardware** (Waveshare RP2040-Zero, July 2026) — **with one caveat: never tested with a physical sensor wired.** The board was validated bare. Enumeration, the app connection, settings read/write and flash persistence are all confirmed; anything that needs a real sensor is still open. See [What is still untested](#what-is-still-untested).

Works the same on any RP2040 — for a stock Pico, change `board = pico` in `platformio.ini`. The Zero's onboard LED is WS2812 on GP16; this firmware does not use it.

Stack: RP2040 + **arduino-pico** (Earle Philhower's core) + **Adafruit_TinyUSB**. MIT.

## What a handbrake is here

A single axis (potentiometer, Hall sensor or load cell — through an HX711 or an instrumentation amplifier) plus one digital button. **The button is not a physical button** — the firmware derives it from the axis: cross a threshold and it presses, drop below it and it releases. That means your lever needs no extra switch and no extra wiring.

It is `firmware-pedal` reduced from three axes to one, speaking the same P0 protocol the app already knows.

## What it does

- **HID joystick** (report `0x01`): 1 axis `Rx` (16-bit field, values 0..4095) + 1 button + 7 bits of padding.
- **Vendor P0** (usage page `0xFF00`, identical to the pedal):
  - `0x20` telemetry (~100 Hz) — the axis rides in the **Clutch** slot (raw u16 LE + output u16 LE); the Brake and Throttle slots are zeroed; `Flags` bit 0 is the button state.
  - `0x14` write / `0x15` read request / `0x16` value — fields 0–13 are the same as the pedal (sensor, min, max, invert, smoothing, 6-point curve, load-cell scale, deadzone), plus **14 = ButtonThreshold** and **15 = ButtonEnabled**. The index byte on the wire is accepted and ignored, since there is only one axis.
  - `0x02` command — calibrate start/stop, save to flash, load defaults.
- **Sensor:** ADC on `A0`/`GP26` for potentiometer, Hall and analog load cell (`sensorType` 0, 1 and 3), or HX711 (DT `GP2`, SCK `GP3`) when `sensorType == 2`. The read never blocks the loop. The ADC path is oversampled, and the analog load cell is also tared on boot — measured force has a zero that drifts, position does not. Wiring for the load-cell paths is in **[docs/pedal-wiring.md](../docs/pedal-wiring.md)**; it applies here with one axis instead of three.
- **Pipeline:** normalize (min/max) → invert → deadzone → 6-point curve → smoothing → clamp. Same maths as the pedal.
- **Button with hysteresis:** presses at `buttonThreshold`, releases 3 points below it. The gap is what stops it flickering when you hold the lever right at the threshold.
- **Flash persistence** (emulated EEPROM, magic `"DLH1"`): the whole config including the button settings.

## Bill of materials

| Qty | Part | Notes |
|----:|------|-------|
| 1 | **Waveshare RP2040-Zero** | RP2040 + USB-C. Any RP2040 works (`board = pico`). |
| 1 | **USB-C cable** | to the PC. |
| 1 | **Sensor** — your choice: **10 kΩ pot**, **analog Hall**, or **load cell** | pot and Hall on **`A0` = `GP26`**; a load cell needs an amplifier — HX711 (**DT `GP2`, SCK `GP3`**) or an instrumentation amp, whose output goes on **`A0`** itself. |
| 0–1 | **Load-cell amplifier** — **HX711** or **instrumentation amplifier** (INA333 / CJMCU-333) | only if you use a load cell. The HX711 delivers 10 or 80 readings per second; the instrumentation amp uses the board's ADC and has no such limit. Power either one at **3.3 V, never 5 V**. |
| — | Lever mechanics and spring | no extra button needed — it is derived in firmware. |

## Build & flash

Needs [PlatformIO](https://platformio.org).

```bash
cd firmware-handbrake
pio run -e rp2040_zero              # build
pio run -e rp2040_zero -t upload    # flash
```

To flash, hold **BOOT** and tap **RESET** (or hold BOOT while plugging the USB-C in) — the **RPI-RP2** drive appears and PlatformIO copies the `.uf2`.

The build produces a ~207 KB `.uf2` using about 4% of the RP2040's flash and 7% of its RAM. There is a lot of room left.

## What is still untested

Everything below needs a **physical sensor** connected, which never happened on the bench:

- Moving the sensor and seeing the axis travel 0..100%.
- The button pressing at `buttonThreshold` (70% by default) and releasing 3% below it, without flickering.
- Calibration capturing the sensor's real minimum and maximum.

Also worth knowing: with no HX711 connected, the driver may never report ready. The firmware does not hang — the raw value simply stays at zero — but that path was not observed on real hardware either.

## Implementation note — the single HID endpoint

Same constraint as the pedal firmware, and the same fix: TinyUSB has one HID endpoint here and drops the second of two back-to-back reports. So the vendor payload is **63 bytes**, and the `0x16` reply is **sent from `loop()` with priority over the joystick**, never straight from the callback. Validated on the wire: read `Smooth=0` → write 42 → read 42.

---

The wheelbase firmware is a different beast — STM32F405, in [`../firmware-base/`](../firmware-base/README.md). Pedals are in [`../firmware-pedal/`](../firmware-pedal/README.md). The full USB-HID contract is in [`../docs/PROTOCOL.md`](../docs/PROTOCOL.md).

---

## 🇧🇷 Português

> **Status: ✅ validado em hardware** (Waveshare RP2040-Zero, julho/2026) — **com uma ressalva: nunca foi testado com um sensor físico ligado.** A placa foi validada nua. Enumeração, conexão com o app, leitura e gravação de ajustes e persistência em flash estão confirmados; tudo que depende de sensor real continua em aberto. Veja [O que ainda não foi testado](#o-que-ainda-não-foi-testado).

Funciona igual em qualquer RP2040 — para um Pico comum, troque `board = pico` no `platformio.ini`. O LED da Zero é WS2812 no GP16; este firmware não usa ele.

Stack: RP2040 + **arduino-pico** (core do Earle Philhower) + **Adafruit_TinyUSB**. MIT.

## O que é o freio de mão aqui

Um eixo único (potenciômetro, sensor Hall ou célula de carga — por HX711 ou por amplificador de instrumentação) mais um botão digital. **O botão não é um botão físico** — o firmware deriva ele do eixo: passou do limiar, o botão aperta; caiu abaixo, solta. Ou seja, a sua alavanca não precisa de chave nenhuma nem de fiação extra.

É o `firmware-pedal` reduzido de três eixos para um, falando o mesmo protocolo P0 que o app já conhece.

## O que ele faz

- **Joystick HID** (report `0x01`): 1 eixo `Rx` (campo de 16 bits, valores 0..4095) + 1 botão + 7 bits de preenchimento.
- **Vendor P0** (usage page `0xFF00`, igual ao do pedal):
  - `0x20` telemetria (~100 Hz) — o eixo viaja no lugar da **embreagem** (raw u16 LE + saída u16 LE); os lugares de freio e acelerador vão zerados; o bit 0 de `Flags` é o estado do botão.
  - `0x14` write / `0x15` read request / `0x16` value — os campos 0–13 são os mesmos do pedal (sensor, mínimo, máximo, inverter, suavização, curva de 6 pontos, escala da célula, zona morta), mais **14 = ButtonThreshold** e **15 = ButtonEnabled**. O byte de índice do fio é aceito e ignorado, já que só existe um eixo.
  - `0x02` comando — iniciar/parar calibração, salvar na flash, restaurar padrões.
- **Sensor:** ADC em `A0`/`GP26` para potenciômetro, Hall e célula analógica (`sensorType` 0, 1 e 3), ou HX711 (DT `GP2`, SCK `GP3`) quando `sensorType == 2`. A leitura nunca trava o loop. O caminho do ADC é sobreamostrado, e a célula analógica ainda é tarada no boot — força medida tem zero que anda, posição não. A ligação dos caminhos de célula está em **[docs/pedal-wiring.md](../docs/pedal-wiring.md)**; vale igual aqui, com um eixo em vez de três.
- **Pipeline:** normaliza (mínimo/máximo) → inverte → zona morta → curva de 6 pontos → suavização → limita. Mesma matemática do pedal.
- **Botão com histerese:** aperta no `buttonThreshold`, solta 3 pontos abaixo. É essa folga que impede o botão de piscar quando você segura a alavanca bem em cima do limiar.
- **Persistência em flash** (EEPROM emulada, magic `"DLH1"`): a configuração inteira, incluindo os ajustes do botão.

## Lista de materiais

| Qtd | Peça | Observações |
|----:|------|-------------|
| 1 | **Waveshare RP2040-Zero** | RP2040 + USB-C. Qualquer RP2040 serve (`board = pico`). |
| 1 | **Cabo USB-C** | até o PC. |
| 1 | **Sensor** — sua escolha: **potenciômetro 10 kΩ**, **Hall analógico**, ou **célula de carga** | potenciômetro e Hall em **`A0` = `GP26`**; a célula precisa de amplificador — HX711 (**DT `GP2`, SCK `GP3`**) ou de instrumentação, cuja saída vai no próprio **`A0`**. |
| 0–1 | **Amplificador da célula** — **HX711** ou **amplificador de instrumentação** (INA333 / CJMCU-333) | só se você usar célula de carga. O HX711 entrega 10 ou 80 leituras por segundo; o de instrumentação usa o ADC da placa e não tem esse limite. Alimente qualquer um deles em **3,3 V, nunca 5 V**. |
| — | Mecânica e mola da alavanca | não precisa de botão extra — ele é derivado no firmware. |

## Compilar e gravar

Precisa do [PlatformIO](https://platformio.org).

```bash
cd firmware-handbrake
pio run -e rp2040_zero              # compilar
pio run -e rp2040_zero -t upload    # gravar
```

Para gravar, segure **BOOT** e dê um toque no **RESET** (ou segure BOOT enquanto pluga o USB-C) — aparece a unidade **RPI-RP2** e o PlatformIO copia o `.uf2`.

A compilação gera um `.uf2` de uns 207 KB, usando cerca de 4% da flash do RP2040 e 7% da RAM. Sobra bastante espaço.

## O que ainda não foi testado

Tudo abaixo depende de um **sensor físico** ligado, o que nunca aconteceu na bancada:

- Mexer o sensor e ver o eixo percorrer 0..100%.
- O botão apertar no `buttonThreshold` (70% por padrão) e soltar 3% abaixo, sem ficar piscando.
- A calibração capturar o mínimo e o máximo reais do sensor.

Outra coisa que vale saber: sem o HX711 ligado, o driver pode nunca ficar pronto. O firmware não trava — o valor bruto simplesmente fica em zero — mas esse caminho também não foi observado em hardware real.

## Nota de implementação — o endpoint HID único

Mesma limitação do firmware dos pedais, e o mesmo tratamento: o TinyUSB tem um só endpoint HID aqui e descarta o segundo de dois reports enviados em sequência. Por isso o payload vendor tem **63 bytes**, e a resposta `0x16` é **enviada do `loop()` com prioridade sobre o joystick**, nunca direto do callback. Validado no fio: ler `Smooth=0` → gravar 42 → ler 42.

---

O firmware da base é outro bicho — STM32F405, em [`../firmware-base/`](../firmware-base/README.md#-português). Os pedais estão em [`../firmware-pedal/`](../firmware-pedal/README.md#-português). O contrato USB-HID completo está em [`../docs/PROTOCOL.md`](../docs/PROTOCOL.md).
