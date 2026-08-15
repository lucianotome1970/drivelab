# DriveLab Firmware — Freio de mão (RP2040)

Firmware do **freio de mão DriveLab** — placa **Waveshare RP2040-Zero** (RP2040, USB-C).

<p align="center"><a href="README.md">🇬🇧 Read in English</a></p>

---

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
- **Sensor:** ADC em `A0`/`GP26` para potenciômetro, Hall e célula analógica (`sensorType` 0, 1 e 3), ou HX711 (DT `GP2`, SCK `GP3`) quando `sensorType == 2`. A leitura nunca trava o loop. O caminho do ADC é sobreamostrado, e a célula analógica ainda é tarada no boot — força medida tem zero que anda, posição não.
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

O firmware da base é outro bicho — STM32F405, em [`../firmware-base/`](../firmware-base/README.pt.md). Os pedais estão em [`../firmware-pedal/`](../firmware-pedal/README.pt.md). O contrato USB-HID completo está em [`../docs/PROTOCOL.md`](../docs/PROTOCOL.md).
