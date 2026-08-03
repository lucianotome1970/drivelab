# firmware-wheel-dd — Wheelbase Direct-Drive (nosso firmware sobre a FOC do ODrive)

Firmware do volante **direct-drive** (base motor). Substitui a FOC caseira do `firmware-base`
(SimpleFOC, aposentada — virou whack-a-mole, ver `docs/../memory`) por uma base **provada**.

## Arquitetura (Opção A — enxuta, MIT)

Usamos **duas bases**, como o Odrive-Wheel (mas escrevendo a NOSSA cola, não copiando):

| Camada | Fonte | Licença |
|---|---|---|
| FOC / motor / calibração | **ODrive v0.5.6** (vendorizado em `vendor/odrive-fw`) | MIT |
| USB stack | **TinyUSB** (vendorizado em `vendor/tinyusb`) | MIT/Apache |
| Ponte + task FFB + USB descriptors + persistência | **NOSSO** (`src/`, a escrever) | MIT |
| Efeitos FFB (mola/damper/...) | **NOSSO** `engine.step` (do firmware-base) | MIT |
| Config/UI | **NOSSO app** DriveLab Studio (não clonamos as 11 abas do Odrive-Wheel) | MIT |

**NÃO usamos** o HidFFB nem o EffectsCalculator do OpenFFBoard (são GPL). A receita de como
o Odrive-Wheel colou tudo está em [`../docs/odrive-integration-recipe.md`](../docs/odrive-integration-recipe.md).

## Os 4 patches de config no ODrive (defaults, já presentes na base vendorizada)

`control_mode=TORQUE`, `input_mode=PASSTHROUGH`, `enable_torque_mode_vel_limit=false`,
`current_control_deadband≈0.1A`. + vbus divider 19:1 (MKS) carregado antes do ADC.

## Plano de construção (hello-world antes de efeitos)

- [ ] **Stage 1** — build completo da base no nosso projeto (Makefile + override board_v3 + TinyUSB).
      Milestone: `firmware-wheel-dd` compila um firmware ODrive-torque pro nosso board (v3.6-56V).
      *(O núcleo ODrive é entrelaçado com USB/board — precisa do build completo, não compila peça isolada.)*
- [ ] **Stage 2** — NOSSA `odrive_bridge` (escreve `axes[0].controller_.input_torque_`, lê `shadow_count_/cpr`)
      + 1 thread 1kHz escrevendo **torque CONSTANTE** → motor segura força firme. Prova a ponte.
- [ ] **Stage 3** — TinyUSB CDC+HID composite + NOSSO parser HID PID → força constante do jogo → torque.
- [ ] **Stage 4** — NOSSOS efeitos (`engine.step`) + telemetria + protocolo do app.

## Build (toolchain confirmado no Mac)

ARM GCC via platformio + python (pyyaml/jinja2/jsonschema). Autogen do ODrive precisa do
`vendor/odrive-tools/` (interface_generator + version.py). Ver a receita do build no
`Makefile.ref` (o Makefile do Odrive-Wheel, referência a adaptar — trata todos os includes,
o override de board, o TinyUSB). Flash: ST-Link/openocd `program <bin> 0x08000000`.

## Estado (2026-08-02)

Fundação montada: base ODrive + tools + TinyUSB vendorizados; toolchain confirmado; receita
documentada. **Odrive-Wheel já roda no nosso board** (validação do pivô disponível via Windows).
Próximo: montar o Makefile completo adaptado (Stage 1) — trabalho de build-engineering iterativo.
