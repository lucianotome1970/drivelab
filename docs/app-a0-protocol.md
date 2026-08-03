# Contrato A0 — o que o DriveLab Studio espera da base (spec p/ o firmware)

> Canal HID vendor **usage page 0xFF00**, VID 0x1209/PID 0x0001. Cada report = 1 byte Report ID
> + **63 bytes de payload** (zero-padded) = 64 na wire. Little-endian. Mesma interface HID do
> volante FFB (2 top-level collections: 0x01 joystick/PID + 0xFF00 A0).

## Report IDs
| Report | ID | Dir | |
|---|---|---|---|
| DeviceState | 0x21 | IN (device→host) | telemetria/ângulo, ~100Hz |
| SettingValue | 0x16 | IN | resposta de leitura |
| Command | 0x22 | OUT (host→device) | [CommandId, Arg] |
| DirectControl | 0x10 | OUT | forças diretas (baixa prio) |
| SettingWrite | 0x14 | OUT | grava setting |
| SettingReadRequest | 0x15 | OUT | pede leitura |

## DeviceState (0x21) — offsets no payload (o que o app usa)
| off | tam | campo | escala |
|---|---|---|---|
| 0-3 | 4 | Firmware [ReleaseType,Major,Minor,Patch] | |
| 4 | 1 | **Flags** | bit0 ForceEnabled, bit1 Calibrated, bit2 Error, bit3 UsingSimulator(=0!), bit4 VoltageImplausible, bit5 CoggingLoaded |
| 5-6 | i16 | Position | **±10000** (=±100%) |
| 7-8 | i16 | **AngleDeciDeg** | **décimos de grau — GIRA O VOLANTE NA TELA** (turns×3600) |
| 9-10 | i16 | Torque | ±10000 |
| 11-12 | i16 | MotorCurrentMa | mA |
| 13 | i8 | FetTempC | °C (−128=sem sensor) |
| 14 | u8 | ErrorCode | |
| 15-16 | u16 | BusVoltageMv | mV |
| 17 | i8 | MotorTempC | |
| 18 | i8 | McuTempC | |
| 19 | u8 | Clipping | 0-255 |

Mínimo p/ o app mostrar o volante: **0x21 com AngleDeciDeg** (+ Firmware/Flags). (ATENÇÃO: meu a0_channel.cpp atual usa Position ±32767 — CORRIGIR p/ ±10000.)

## Settings (0x14 grava / 0x15 pede / 0x16 responde)
- SettingWrite/SettingValue = struct `SettingReport`: **[FieldId(1), Index=0(1), Type(1), valor(1-4 LE)]**.
- SettingReadRequest = **[FieldId(1), Index=0(1)]**.
- Fluxo: app manda 0x15(FieldId) → firmware responde **0x16 com o MESMO FieldId + Type correto + valor**. **1 slot de read pendente**, responder em <500ms senão a aba "não carrega".
- Type: UInt8=0, Int8=1, UInt16=2, Int16=3, **Float=4** (4 bytes IEEE-754 LE). Inteiros ARREDONDADOS. Valor de engenharia direto (sem fixed-point).
- App lê motion_range(0) + total_strength(3) **na conexão** (mínimo pra não dar timeout).

### BaseSettingId (FieldId → tipo, range, default) — os que importam pro FFB
0 motion_range u16 90-2000° d900 (DOR/curso→endstop) · 1 soft_stop_range u8 0-30° d5 · 2 soft_stop_strength u8 0-100 d80 · **3 total_strength u8 0-100 d100 (força→maxTorque)** · 4 spring_strength u8 0-100 d0 · 5 damper_strength u8 0-100 d10 · 6 static_damping u8 · 7 max_torque_limit u8 0-100 d80 · **8 force_direction i8 -1..1 d1** · 9 encoder_direction i8 · 10 encoder_cpr u16 d4000 · 11 pole_pairs u8 d15 · 12 current_p **float** d0.05 · 13 current_i **float** d10 · 14 calibration_current u8 d30 · 15 position_smoothing u8 · 16 power_limit u8 · 17 braking_limit u8 · 18 encoder_type u8 0-2 · 19 reconstruction_steps u8 0-32 · 20 reconstruction_lpf u8 · 21 output_filter_hz u16 · 22 osc_guard u8 · 23 endstop_damping u8 · 24 linearity u8 50-200 · 25 cogging_enable u8 · 26 slew_rate u8 · 27 bus_nominal_v u8 12-56 · 28-32 ffb_curve_0..4 u8 · 33 board_variant u8 · 34 torque_constant **float** d0 · 35 thermal_continuous u8 · 36 thermal_peak_s u8 · 37 fet_temp_limit u8 · 38 motor_temp_limit u8 · **39-42 spring/damper/friction/inertia_gain u8 0-200 d100 (ganhos do jogo)** · 43 soft_power u8 · 44 power_button u8.

## Command (0x22) = [CommandId, Arg]
1 Reboot · **2 SaveSettings (grava flash)** · **3 ResetCenter (= CENTRALIZAR: zera o ângulo/centro atual)** · 4 EnterDfu · 5 Calibrate · **6 SetForceEnabled (Arg 0=off/≠0=on)** · 7 CalibrateCogging · 8 BrakeBench · 9 BrakeAuto. Fire-and-forget (sem resposta).
- **ResetCenter**: a posição atual vira o novo 0 → DeviceState passa a reportar AngleDeciDeg≈0. (Botão Centralizar do dashboard.)

## DirectControl (0x10) — baixa prio
[SpringForce i16 ±10000, ConstantForce i16, PeriodicForce i16, DamperForce i16, ForceDrop u8 0-100, TelemetryForce i16 ±255]. Forças do app somadas à demanda FFB.

## Prioridade de implementação
(a) DeviceState 0x21 c/ AngleDeciDeg → app mostra o volante.
(b) Settings 0x14/0x15/0x16 (ao menos motion_range+total_strength) → abas carregam.
(c) Command ResetCenter(3)+SaveSettings(2)+SetForceEnabled(6) → botão centralizar + salvar + liga/desliga.
(d) DirectControl + telemetria completa.

Comportamento de referência = simulador: app/DriveLab.Simulator/{SimulatorBaseTransport,VirtualBase}.cs.
Enumeração: replicar a ABORDAGEM do firmware-base (lib/base_usb/) — descriptor combinado + envio de reports (ver docs de análise / o outro agente).
