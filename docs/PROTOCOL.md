# DriveLab HID Protocol / Protocolo HID do DriveLab

> The contract DriveLab Studio uses to talk to the hardware over USB HID. Implement it on your own board and the app will drive it — no app changes needed.
>
> 🇧🇷 *O contrato que o DriveLab Studio usa para falar com o hardware via USB HID. Implemente-o na sua própria placa e o app a controla — sem mudar o app.*

Source of truth: `app/DriveLab.Core/Protocol/` and `app/DriveLab.Core/Settings/`. This document mirrors that code.
🇧🇷 *Fonte da verdade: `app/DriveLab.Core/Protocol/` e `app/DriveLab.Core/Settings/`. Este documento espelha esse código.*

---

## 🇬🇧 English

### 1. Transport

- **USB HID**, custom vendor collection (usage page `0xFF00`). No drivers needed.
- Every report is **64 bytes on the wire**: **1 byte report ID** + **63 bytes payload** (`ReportConstants.ReportSize = 63`).
- Multi-byte integers are **little-endian**. Unused payload bytes are `0`.
- Reports are addressed by **report ID** (first byte). Output = host→device, Input = device→host.

### 2. USB identity (VID / PID)

VID is the shared [pid.codes](https://pid.codes) test vendor `0x1209`. Pick the PID for the module you emulate.

| Device | VID | PID | USB product string |
|--------|-----|-----|--------------------|
| Wheel base | `0x1209` | `0x0001` | `DriveLab Base` |
| Pedals | `0x1209` | `0x0002` | `DriveLab Pedal` |
| Handbrake | `0x1209` | `0x0003` | `DriveLab Handbrake` |
| Wheel (rim) | `0x1209` | `0x0004` | `DriveLab Wheel` |

The app auto-detects each module by VID/PID.

### 3. Report ID map

Shared across modules unless noted:

| ID | Dir | Name | Meaning |
|----|-----|------|---------|
| `0x01` | In | Joystick / DeviceState | Standard gamepad report (rim) / base state |
| `0x20` | In | PedalState | Pedal telemetry |
| `0x21` | In | WheelState | Rim telemetry |
| `0x02` | Out | Command | `[0]=commandId [1]=arg` |
| `0x14` | Out | SettingWrite | Write one setting |
| `0x15` | Out | SettingReadRequest | Ask for one setting |
| `0x16` | In | SettingValue | Reply to a read request |
| `0x18` | Out | WheelLed | Rim LED colors (rim only) |
| `0x19` | In | LedValue | Rim stored LED colors reply (rim only) |
| `0x10` | Out | DirectControl | Base: direct force (test screen) |

### 4. ⚠️ The single-endpoint rule (critical for firmware)

Many low-cost stacks (TinyUSB on RP2040) expose a **single HID endpoint**. If you send two Input reports back-to-back, **the second is dropped**. So **never** answer a read request (`0x16`) or LED request (`0x19`) from inside the report-received callback. Instead:

1. In the OUTPUT callback, just **queue** the request (set a flag + remember field/index).
2. In your main `loop()`, when the endpoint is free again, send the queued reply **with priority** over routine telemetry.

Getting this wrong looks like "settings read times out" — the DriveLab firmwares all had to fix exactly this.

### 5. Settings (read / write)

A setting is addressed by `(FieldId, Index)` and carries a typed value.

**SettingType** (`app/DriveLab.Core/Settings/SettingType.cs`):

| Value | Type | Bytes |
|-------|------|-------|
| `0` | UInt8 | 1 |
| `1` | Int8 | 1 |
| `2` | UInt16 | 2 (LE) |
| `3` | Int16 | 2 (LE) |
| `4` | Float | 4 (IEEE-754 LE) |

**SettingWrite `0x14`** (out) and **SettingValue `0x16`** (in) share the same payload:

| Offset | Field |
|--------|-------|
| 0 | FieldId (setting id) |
| 1 | Index (sub-channel, e.g. pedal 0/1/2; `0` if unused) |
| 2 | ValueType (SettingType) |
| 3.. | value bytes (little-endian, length per type) |

**SettingReadRequest `0x15`** (out):

| Offset | Field |
|--------|-------|
| 0 | FieldId |
| 1 | Index |

Flow: host sends `0x15 (field,index)` → device replies `0x16 (field,index,type,value)`. Integers are **rounded, not truncated**, on write.

The setting IDs per module are enums in `app/DriveLab.Core/Settings/` — e.g. the rim (`WheelSettingId`): `ClutchLeftMin=0, ClutchLeftMax=1, ClutchRightMin=2, ClutchRightMax=3, ClutchInvertLeft=4, ClutchInvertRight=5, ClutchMode=6, ClutchBitePoint=7, LedBrightness=8, LedCount=9`. See also `PedalSettingId`, `HandbrakeSettingId`, `BaseSettingId`.

### 6. Commands `0x02`

Payload: `[0]=commandId  [1]=arg`. Command tables:

- **Pedals / Handbrake** (`PedalCommandId`): `CalibrateStart=1, CalibrateStop=2, SaveToFlash=3, LoadDefaults=4`. Arg = pedal index (pedals).
- **Wheel** (`WheelCommandId`): `CalibrateClutchStart=1, CalibrateClutchStop=2, SaveToFlash=3, LoadDefaults=4, RequestLeds=5`.
- **Base** (`BaseCommand`): `Reboot=1, SaveSettings=2, ResetCenter=3, EnterDfu=4, Calibrate=5, SetForceEnabled=6`.

`SaveToFlash`/`SaveSettings` must persist current RAM settings to non-volatile memory.

### 7. Telemetry (Input)

**PedalState `0x20`** (payload, LE):

| Offset | Field |
|--------|-------|
| 0..3 | Firmware version (major, minor, patch, build) |
| 4 | Flags |
| 5..8 | Clutch: rawInput (u16) + output (u16) |
| 9..12 | Brake: rawInput (u16) + output (u16) |
| 13..16 | Throttle: rawInput (u16) + output (u16) |

`rawInput` is 0..4095 (12-bit ADC); `output` is 0..65535 (after curve/deadzone).

**WheelState `0x21`** (payload, LE):

| Offset | Field |
|--------|-------|
| 0..3 | Firmware version |
| 4 | Flags |
| 5..8 | Buttons bitmap (u32) — bit N = button N |
| 9..12 | Clutch left: raw (u16) + output (u16) |
| 13..16 | Clutch right: raw (u16) + output (u16) |
| 17..21 | Encoder deltas, 5× int8 (signed, since last frame) |

Send it at ~100 Hz. Button bit map is up to you, but the reference rim uses bits 0–7 for face buttons, 10/11 for shift down/up.

### 8. Wheel LEDs (rim only)

**WheelLed `0x18`** (out) and **LedValue `0x19`** (in) share the layout:

| Offset | Field |
|--------|-------|
| 0 | count (number of LEDs, ≤ 20) |
| 1 | brightness (0..255) |
| 2 + 3·i | LED i: R, G, B |

The host pushes colors live with `0x18`. To read the rim's **stored** colors, host sends command `RequestLeds` (`0x02`, id 5); the device replies with `0x19` (queued from `loop()`, per §4). Firmware should persist the last colors on `SaveToFlash` and restore them on boot.

### 9. Bring your own board

Minimum to be picked up and driven by DriveLab Studio:

1. Enumerate with the right **VID/PID** and a vendor HID collection with 64-byte reports.
2. Implement the **Input telemetry** report for your module (`0x20`/`0x21`) so the app shows live state.
3. Implement **SettingWrite/ReadRequest/Value** (`0x14`/`0x15`/`0x16`) with the single-endpoint rule.
4. Implement **Command** (`0x02`) — at least `SaveToFlash` and `LoadDefaults`.
5. Rim only: **WheelLed** (`0x18`) and, for read-back, **LedValue** (`0x19`).

That's the whole contract. The app doesn't care what MCU or sensors you use.

---

## 🇧🇷 Português

### 1. Transporte

- **USB HID**, coleção vendor customizada (usage page `0xFF00`). Sem drivers.
- Todo report tem **64 bytes no fio**: **1 byte de report ID** + **63 bytes de payload** (`ReportConstants.ReportSize = 63`).
- Inteiros multi-byte são **little-endian**. Bytes não usados do payload são `0`.
- Os reports são endereçados pelo **report ID** (1º byte). Output = host→device, Input = device→host.

### 2. Identidade USB (VID / PID)

O VID é o vendor de teste compartilhado [pid.codes](https://pid.codes) `0x1209`. Escolha o PID do módulo que você emula.

| Dispositivo | VID | PID | String de produto USB |
|-------------|-----|-----|-----------------------|
| Base do volante | `0x1209` | `0x0001` | `DriveLab Base` |
| Pedais | `0x1209` | `0x0002` | `DriveLab Pedal` |
| Freio de mão | `0x1209` | `0x0003` | `DriveLab Handbrake` |
| Volante (aro) | `0x1209` | `0x0004` | `DriveLab Wheel` |

O app autodetecta cada módulo por VID/PID.

### 3. Mapa de report IDs

Compartilhados entre os módulos, salvo indicação:

| ID | Dir | Nome | Significado |
|----|-----|------|-------------|
| `0x01` | In | Joystick / DeviceState | Gamepad padrão (aro) / estado da base |
| `0x20` | In | PedalState | Telemetria dos pedais |
| `0x21` | In | WheelState | Telemetria do aro |
| `0x02` | Out | Command | `[0]=commandId [1]=arg` |
| `0x14` | Out | SettingWrite | Grava um setting |
| `0x15` | Out | SettingReadRequest | Pede um setting |
| `0x16` | In | SettingValue | Resposta do pedido de leitura |
| `0x18` | Out | WheelLed | Cores dos LEDs do aro (só aro) |
| `0x19` | In | LedValue | Resposta com as cores guardadas no aro (só aro) |
| `0x10` | Out | DirectControl | Base: força direta (tela de teste) |

### 4. ⚠️ A regra do endpoint único (crítica no firmware)

Muitas pilhas baratas (TinyUSB no RP2040) expõem **um único endpoint HID**. Se você mandar dois Input reports em sequência, **o segundo é dropado**. Então **nunca** responda um pedido de leitura (`0x16`) ou de LEDs (`0x19`) de dentro do callback de report recebido. Em vez disso:

1. No callback de OUTPUT, apenas **enfileire** o pedido (seta um flag + guarda field/index).
2. No seu `loop()`, quando o endpoint estiver livre de novo, envie a resposta enfileirada **com prioridade** sobre a telemetria de rotina.

Errar isso aparece como "a leitura de settings dá timeout" — todos os firmwares DriveLab tiveram que corrigir exatamente isto.

### 5. Settings (leitura / escrita)

Um setting é endereçado por `(FieldId, Index)` e carrega um valor tipado.

**SettingType** (`app/DriveLab.Core/Settings/SettingType.cs`):

| Valor | Tipo | Bytes |
|-------|------|-------|
| `0` | UInt8 | 1 |
| `1` | Int8 | 1 |
| `2` | UInt16 | 2 (LE) |
| `3` | Int16 | 2 (LE) |
| `4` | Float | 4 (IEEE-754 LE) |

**SettingWrite `0x14`** (out) e **SettingValue `0x16`** (in) têm o mesmo payload:

| Offset | Campo |
|--------|-------|
| 0 | FieldId (id do setting) |
| 1 | Index (sub-canal, ex.: pedal 0/1/2; `0` se não usar) |
| 2 | ValueType (SettingType) |
| 3.. | bytes do valor (little-endian, tamanho conforme o tipo) |

**SettingReadRequest `0x15`** (out):

| Offset | Campo |
|--------|-------|
| 0 | FieldId |
| 1 | Index |

Fluxo: host manda `0x15 (field,index)` → device responde `0x16 (field,index,type,valor)`. Na escrita, inteiros são **arredondados, não truncados**.

Os IDs de setting de cada módulo são enums em `app/DriveLab.Core/Settings/` — ex.: o aro (`WheelSettingId`): `ClutchLeftMin=0, ClutchLeftMax=1, ClutchRightMin=2, ClutchRightMax=3, ClutchInvertLeft=4, ClutchInvertRight=5, ClutchMode=6, ClutchBitePoint=7, LedBrightness=8, LedCount=9`. Veja também `PedalSettingId`, `HandbrakeSettingId`, `BaseSettingId`.

### 6. Comandos `0x02`

Payload: `[0]=commandId  [1]=arg`. Tabelas:

- **Pedais / Freio** (`PedalCommandId`): `CalibrateStart=1, CalibrateStop=2, SaveToFlash=3, LoadDefaults=4`. Arg = índice do pedal (pedais).
- **Volante** (`WheelCommandId`): `CalibrateClutchStart=1, CalibrateClutchStop=2, SaveToFlash=3, LoadDefaults=4, RequestLeds=5`.
- **Base** (`BaseCommand`): `Reboot=1, SaveSettings=2, ResetCenter=3, EnterDfu=4, Calibrate=5, SetForceEnabled=6`.

`SaveToFlash`/`SaveSettings` devem persistir os settings da RAM na memória não-volátil.

### 7. Telemetria (Input)

**PedalState `0x20`** (payload, LE):

| Offset | Campo |
|--------|-------|
| 0..3 | Versão do firmware (major, minor, patch, build) |
| 4 | Flags |
| 5..8 | Embreagem: rawInput (u16) + output (u16) |
| 9..12 | Freio: rawInput (u16) + output (u16) |
| 13..16 | Acelerador: rawInput (u16) + output (u16) |

`rawInput` é 0..4095 (ADC 12-bit); `output` é 0..65535 (após curva/deadzone).

**WheelState `0x21`** (payload, LE):

| Offset | Campo |
|--------|-------|
| 0..3 | Versão do firmware |
| 4 | Flags |
| 5..8 | Bitmap de botões (u32) — bit N = botão N |
| 9..12 | Embreagem esq.: raw (u16) + output (u16) |
| 13..16 | Embreagem dir.: raw (u16) + output (u16) |
| 17..21 | Deltas dos encoders, 5× int8 (com sinal, desde o último frame) |

Envie a ~100 Hz. O mapa de bits dos botões é livre, mas o aro de referência usa bits 0–7 para os botões da face e 10/11 para marcha ↓/↑.

### 8. LEDs do volante (só aro)

**WheelLed `0x18`** (out) e **LedValue `0x19`** (in) têm o mesmo layout:

| Offset | Campo |
|--------|-------|
| 0 | count (nº de LEDs, ≤ 20) |
| 1 | brilho (0..255) |
| 2 + 3·i | LED i: R, G, B |

O host empurra as cores ao vivo com `0x18`. Para ler as cores **guardadas** no aro, o host manda o comando `RequestLeds` (`0x02`, id 5); o device responde com `0x19` (enfileirado no `loop()`, ver §4). O firmware deve persistir as últimas cores no `SaveToFlash` e restaurá-las no boot.

### 9. Traga sua própria placa

Mínimo para ser reconhecida e controlada pelo DriveLab Studio:

1. Enumerar com o **VID/PID** certo e uma coleção HID vendor com reports de 64 bytes.
2. Implementar o report de **telemetria Input** do seu módulo (`0x20`/`0x21`) para o app mostrar o estado ao vivo.
3. Implementar **SettingWrite/ReadRequest/Value** (`0x14`/`0x15`/`0x16`) com a regra do endpoint único.
4. Implementar **Command** (`0x02`) — pelo menos `SaveToFlash` e `LoadDefaults`.
5. Só no aro: **WheelLed** (`0x18`) e, para leitura de volta, **LedValue** (`0x19`).

Esse é o contrato inteiro. O app não se importa com qual MCU ou sensores você usa.
