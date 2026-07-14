# DriveLab Firmware — Freio de mão (RP2040)

Firmware do **freio de mão DriveLab** — placa **Waveshare RP2040-Zero** (RP2040, USB-C).
(Funciona igual em qualquer RP2040; para o Pico padrão, troque `board = pico` no `platformio.ini`. LED onboard do Zero é WS2812/GP16 — o firmware não usa LED.)
Design/decisões: [`../docs/superpowers/specs/2026-07-14-handbrake-module-design.md`](../docs/superpowers/specs/2026-07-14-handbrake-module-design.md).

> Firmware **separado** do volante (ODESC/STM32 em `../firmware/`) e da pedaleira (`../firmware-pedal/`). Aqui é RP2040 + **arduino-pico** (core do Earle Philhower) + Adafruit_TinyUSB. Licença: MIT (é código nosso; TinyUSB é MIT).

> **Status:** M0 (scaffold) **escrito, aguardando validação na placa** (o RP2040-Zero ainda não está em mãos).

**Escopo do freio de mão:** um único eixo (célula de carga ou potenciômetro, a definir no design) + um botão. Bem mais simples que a pedaleira (3 eixos), mas segue o mesmo esqueleto de projeto e o mesmo protocolo vendor P0 nos próximos marcos.

---

## Marco atual: M0 — Scaffold (blink + serial, sem HID)

**Objetivo:** só provar o toolchain — o Pico compila, grava e roda um blink + banner serial. Ainda **sem HID** (nem joystick, nem vendor P0) — isso vem no M1+ (ver Task 14 em diante).

### Pré-requisitos
- **VS Code + extensão PlatformIO** (baixa o toolchain do arduino-pico sozinho na 1ª build).
- **Waveshare RP2040-Zero** (RP2040) + cabo **USB-C**.

### Passos
1. Abra a pasta `firmware-handbrake/` no VS Code (PlatformIO detecta o `platformio.ini`).
2. **Entre no bootloader UF2:** no RP2040-Zero, segure o botão **BOOT** e dê um toque no **RESET** (ou segure **BOOT** enquanto pluga o USB-C). Aparece a unidade **RPI-RP2**.
3. **Build**: ícone ✓ do PlatformIO (ou `pio run`). A 1ª vez baixa o core + TinyUSB (alguns minutos).
4. **Upload**: ícone → do PlatformIO. O PlatformIO copia o `.uf2` para o Pico (que reinicia sozinho).
5. **Serial Monitor** (115200) deve mostrar a linha de status do M0, repetindo a cada ciclo do blink.

### Resultado esperado (M0 ✅)
- Build gera um `.uf2` sem erros.
- LED onboard pisca (250ms ligado / 250ms desligado).
- Serial: `=== DriveLab Freio de mão — M0 (scaffold) ===`.

### Se der problema
- **Build falha em `Adafruit_TinyUSB.h`** → confira que `build_flags = -DUSE_TINYUSB` está no `platformio.ini` (é o que ativa o stack TinyUSB no core do Philhower). Ainda não é usado neste marco, mas fica pronto para o próximo.
- **Não aparece a unidade RPI-RP2** → segure **BOOT** e toque **RESET** (ou segure BOOT ao plugar).
- **Nada no Serial Monitor** → confira a porta/baud (115200) e que o cabo USB-C é de dados (não só energia).

---

## Próximo marco
- **M1+** — canal HID (joystick de 1 eixo + botão) e depois o protocolo vendor P0, espelhando o que já existe em `../firmware-pedal/`.

Detalhes no design.
