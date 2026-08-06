# firmware-base — Wheelbase Direct-Drive

Firmware da **base direct-drive** (o estágio do motor de FFB). Roda em placas
**STM32F405 classe ODrive v3.6** — validado numa **MKS ODRIVE-S V3.6-S6V**.

O jogo enxerga a base como um volante DirectInput com force feedback: nenhum plugin,
nenhum driver extra.

## Arquitetura

Em vez de escrever uma FOC do zero, o firmware monta duas fundações prontas e maduras, e
a camada de FFB é nossa:

| Camada | Origem | Licença |
|---|---|---|
| FOC, controle do motor, calibração | **ODrive v0.5.6**, vendorizado em `vendor/odrive-fw` | MIT |
| Pilha USB | **TinyUSB**, vendorizado em `vendor/tinyusb` | MIT / Apache |
| Ponte com o ODrive, task de FFB, descritores USB, persistência | **nosso** (`src/`, `inc/`) | MIT |
| Efeitos de FFB (mola, damper, atrito, inércia) | **nosso** (`engine.step`) | MIT |
| Configuração e telemetria | **nosso** app DriveLab Studio | MIT |

Nada de GPL entra no binário — o projeto inteiro é MIT, e cada arquivo-fonte traz o
cabeçalho declarando isso.

## Estado (2026-08-06)

**Funcionando e validado em bancada:**

- Motor rodando em FOC, armando de forma repetível.
- Force feedback validado em jogo: **duas voltas completas no Assetto Corsa Competizione**,
  mais AMS2 e EVO.
- Persistência dos ajustes na flash da própria placa (`CMD_SAVE`) — a base é a fonte da
  verdade, não o app.
- Telemetria ao vivo: tensão do barramento, corrente do motor, temperatura do MCU.
- Proteções: chopper do resistor de freio, corte por sobrecorrente na ISR, verificação de
  sobretemperatura antes de armar, batente por software, trips de sub e sobretensão.

**Pendente:** o ajuste fino do FFB em pista depende do encoder magnético — o encoder
incremental atual não guarda o centro entre um boot e outro, o que limita o quanto dá pra
avançar na calibração. Veja [`../docs/encoders.md`](../docs/encoders.md).

## Build

Precisa do **ARM GCC** (`arm-none-eabi-`) e do Python com `pyyaml`, `jinja2` e
`jsonschema` — o ODrive gera código na hora do build.

O toolchain do PlatformIO serve e é o que usamos, pra o build sair idêntico no Mac e no
Windows:

```bash
export PATH="$HOME/.platformio/packages/toolchain-gccarmnoneeabi/bin:$PATH"
cd firmware-base
make -j8
```

Saída em `build/`: `drivelab-base.elf`, `.bin` e `.hex`.

## Gravar

**Por DFU (pelo USB, sem gravador):** é o caminho normal, e o DriveLab Studio faz isso
sozinho. Se a placa não entrar em DFU, veja o
[FAQ](../docs/faq-hoverboard.md#não-consigo-gravar-o-firmware) — em muitas placas o modo
manual é obrigatório, e em algumas o jumper precisa ser **retirado**, não colocado.

**Por ST-Link (SWD):**

```bash
openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
        -c "program build/drivelab-base.elf verify reset exit"
```

> ⚠️ Com o motor **armado**, não pare o núcleo pelo SWD — isso derruba o motor no meio do
> controle. Para acompanhar o firmware rodando, use a serial USB (CDC).

## Segurança

Leia [`../docs/faq-hoverboard.md#segurança`](../docs/faq-hoverboard.md#segurança) antes de
energizar um motor. Os dois pontos que mais custam caro:

- **Case a tensão da fonte com a variante da placa** (24 V ou 56 V). Passar disso destrói
  a placa sem aviso.
- **Resistor de freio é obrigatório** antes de qualquer torque em malha fechada.
