# DriveLab Firmware — Pedaleira (RP2040)

Firmware da **pedaleira DriveLab** — placa **Waveshare RP2040-Zero** (RP2040, USB-C), o lado do dispositivo do contrato **P0**.
(Funciona igual em qualquer RP2040; para o Pico padrão, troque `board = pico` no `platformio.ini`. LED onboard do Zero é WS2812/GP16 — o firmware não usa LED.)
Design/decisões: mantidas nas notas internas de projeto (não versionadas no repo público).

> Firmware **separado** do volante (que é ODESC/STM32 em `../firmware/`). Aqui é RP2040 + **arduino-pico** (core do Earle Philhower) + Adafruit_TinyUSB. Licença: MIT (é código nosso; TinyUSB é MIT).

> **Status:** M0→M4 **escritos, aguardando validação na placa** (o RP2040-Zero ainda não está em mãos). Falta só o M5 (validação/polimento).

---

## Marco atual: M4 — Joystick + P0 + load cell (HX711) + flash

**Objetivo:** firmware funcional completo (menos validação): joystick + **protocolo P0** + **load cell** + **config permanente em flash**. O **DriveLab Studio conecta, lê/grava settings e recebe telemetria**; a config sobrevive ao desligar e é carregada no boot. O app já tem o lado dele pronto (`HidPedalTransport` + autodetecção por VID/PID `0x1209:0x0002`).

### O que já está escrito (M1–M4)
- **M1** Joystick 3 eixos 12-bit — alimentado pelo **pipeline** (normaliza→deadzone→curva→suaviza).
- **M2** Reports vendor P0: telemetria `0x20`, `SettingWrite 0x14`/`ReadRequest 0x15`/`Command 0x02` (out), `SettingValue 0x16` (in); calibração min/max.
- **M3** **Load cell (HX711)** por `sensor_type==2`: pinos DT/SCK por pedal = **GP2/3, GP4/5, GP6/7** (embreagem/freio/acelerador); leitura não-bloqueante (`is_ready`) + tara no boot. Pot/Hall continuam no ADC (GP26/27/28).
- **M4** **Flash** (EEPROM emulada): `SaveToFlash` grava; no boot carrega a config salva (magic "DLP1"); senão usa defaults. → a config fica **por dispositivo** e o app a carrega ao conectar.

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
- **Waveshare RP2040-Zero** (RP2040) + cabo **USB-C**.

### Passos
1. Abra a pasta `firmware-pedal/` no VS Code (PlatformIO detecta o `platformio.ini`).
2. **Entre no bootloader UF2:** no RP2040-Zero, segure o botão **BOOT** e dê um toque no **RESET** (ou segure **BOOT** enquanto pluga o USB-C). Aparece a unidade **RPI-RP2**.
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
- **Não aparece a unidade RPI-RP2** → segure **BOOT** e toque **RESET** (ou segure BOOT ao plugar).
- **Eixos "colados" no máximo** → ADC flutuando sem pot; ligue um potenciômetro (ou é o pino errado).

> Quando o M1 passar, seguimos pro **M2** (canal vendor P0 → o app conecta e configura).

---

## Próximo marco
- **M5** — validação na bancada (M0→M4) + polimento (ajustar o plumbing do TinyUSB, pinos, e testar num sim). É onde os "escrito sem placa" viram "funciona".

Detalhes no design.
