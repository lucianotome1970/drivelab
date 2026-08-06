# DriveLab Firmware — Pedais (RP2040)

Firmware da **pedaleira DriveLab** — placa **Waveshare RP2040-Zero** (RP2040, USB-C), o lado do dispositivo do contrato **P0**.

<p align="center"><a href="README.md">🇬🇧 Read in English</a></p>

---

> **Status: ✅ validado em hardware** (Waveshare RP2040-Zero, julho/2026). Enumera como **"DriveLab Pedal"** (VID `0x1209` / PID `0x0002`), confirmado no macOS e no Windows. Gravação e leitura de ajustes funcionam, e a configuração sobrevive a desligar e ligar.

Funciona igual em qualquer RP2040 — para um Pico comum, troque `board = pico` no `platformio.ini`. O LED da Zero é WS2812 no GP16; este firmware não usa ele.

Stack: RP2040 + **arduino-pico** (core do Earle Philhower) + **Adafruit_TinyUSB**. MIT — a dependência TinyUSB também é MIT.

## O que ele faz

- **Joystick HID**, 3 eixos de 12 bits, alimentados pelo pipeline de sinal: normaliza → zona morta → curva → suavização.
- **Canal vendor P0** para o app: telemetria `0x20`, `SettingWrite 0x14`, `ReadRequest 0x15`, `SettingValue 0x16`, `Command 0x02`. Calibração de mínimo e máximo incluída.
- **Três tipos de sensor por pedal**, escolhidos no app: potenciômetro, Hall analógico ou célula de carga.
- **Célula de carga** por HX711 (`sensor_type == 2`), lida sem travar o loop, tarada no boot.
- **Configuração permanente na flash** (EEPROM emulada, magic `"DLP1"`): os ajustes ficam **no dispositivo**, sobrevivem a desconectar, e o app carrega eles ao conectar.

## Ligação

**Potenciômetro ou Hall** — pontas em `3V3` e `GND`, cursor no ADC:

| Pedal | Pino do ADC |
|---|---|
| Embreagem | `A0` = `GP26` |
| Freio | `A1` = `GP27` |
| Acelerador | `A2` = `GP28` |

**Célula de carga (HX711)** — um amplificador por pedal:

| Pedal | DT | SCK |
|---|---|---|
| Embreagem | `GP2` | `GP3` |
| Freio | `GP4` | `GP5` |
| Acelerador | `GP6` | `GP7` |

Sem nada ligado, as entradas do ADC ficam flutuando e os eixos leem ruído. Isso é normal, não é defeito.

## Lista de materiais

| Qtd | Peça | Observações |
|----:|------|-------------|
| 1 | **Waveshare RP2040-Zero** | RP2040 + USB-C. Qualquer RP2040 serve (`board = pico` no `platformio.ini`). |
| 1 | **Cabo USB-C** | até o PC. |
| 3 | **Um sensor por pedal** — sua escolha: **potenciômetro linear 10 kΩ**, **Hall analógico** (SS49E / A1302), ou **célula de carga + HX711** | potenciômetro e Hall vão no ADC; célula de carga precisa do amplificador HX711. O tipo é definido por pedal, no app. |
| 0–3 | **Amplificador HX711** | um por pedal de célula de carga. |
| — | Fios, estrutura e molas dos pedais | a parte mecânica é sua. |

## Compilar e gravar

Precisa do [PlatformIO](https://platformio.org) — ele baixa o toolchain do arduino-pico sozinho na primeira compilação.

```bash
cd firmware-pedal
pio run                      # compilar
pio run -t upload            # gravar
```

Para gravar, coloque a placa no bootloader UF2 antes: segure **BOOT** e dê um toque no **RESET**, ou segure **BOOT** enquanto pluga o USB-C. Aparece a unidade **RPI-RP2** e o PlatformIO copia o `.uf2`.

Conferir se deu certo: no Windows, `Win+R` → `joy.cpl` → **"DriveLab Pedal"** com 3 eixos (Rx/Ry/Rz). Mexa num sensor e o eixo correspondente varre.

## Se der problema

- **A compilação falha em `Adafruit_TinyUSB.h`** — confira se `build_flags = -DUSE_TINYUSB` está no `platformio.ini`. É essa flag que ativa a pilha TinyUSB no core do Philhower, e é a causa número um na primeira compilação.
- **O dispositivo não aparece, ou aparece com o nome errado** — confira as chaves `board_build.arduino.earlephilhower.usb_*` (manufacturer / product / vid / pid). O Windows guarda nomes em cache por VID/PID; se você trocar, replugue em outra porta.
- **A unidade RPI-RP2 não aparece** — segure **BOOT** e toque **RESET**, ou segure BOOT ao plugar.
- **Um eixo fica travado no máximo** — o ADC está flutuando sem sensor ligado, ou o sensor está no pino errado.

## Nota de implementação — o endpoint HID único

O TinyUSB dá a esta placa **um só** endpoint HID, e ele descarta o segundo report quando dois são enviados em sequência. Duas consequências ficaram cravadas no firmware, e quem for portar isso precisa manter:

- O payload vendor tem **63 bytes, não 64** — 63 de payload + 1 do report id precisam caber no `CFG_TUD_HID_EP_BUFSIZE`.
- A resposta `0x16` é **enfileirada e enviada do `loop()` com prioridade sobre o joystick**, nunca direto do callback. Enviada do callback, ela cai logo atrás de um report do joystick e desaparece.

São esses dois pontos que fazem a leitura de ajustes funcionar. O mesmo tratamento está no `firmware-handbrake` e no `firmware-wheel`.

---

O firmware da base é outro bicho — STM32F405, em [`../firmware-base/`](../firmware-base/README.pt.md). O contrato USB-HID completo está em [`../docs/PROTOCOL.md`](../docs/PROTOCOL.md).
