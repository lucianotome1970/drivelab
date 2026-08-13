# SIMAGIC P2000 — protocolo HID (engenharia reversa)

Capturado em 2026-07-13, macOS 26.4, via IOKit — `ioreg` (report descriptor) + `IOHIDManager` (leitura ao vivo).
Hardware do próprio usuário; documentado para **interoperabilidade**: modelar o firmware DriveLab no mesmo fluxo e dar suporte a um **perfil P1000/P2000** no app. Ferramentas em [`tools/hidread.swift`](../../tools/hidread.swift) e [`tools/HidDump`](../../tools/HidDump).

## Identidade USB / HID
- **VID** `0x0483` (1155, STMicroelectronics) · **PID** `0x0524` (1316)
- Manufacturer **"SIMAGIC"** · Product **"P2000"**
- HID: Usage Page `0x01` (Generic Desktop), Usage `0x04` (**Joystick**)
- **Taxa de report: 1000 Hz** (`ReportInterval` = 1000 µs)
- `MaxInputReportSize` = 17 · `MaxOutputReportSize` = 65

## Report descriptor cru
```
05 01 09 04 A1 01 A1 00 85 01 75 10 15 00 26 FF 0F 35 00 46 FF 0F
09 33 09 34 09 35 95 03 81 02 06 00 FF 09 01 75 08 95 0A 09 01 81 02 C0
A1 02 06 00 FF 85 F2 75 08 95 40 09 F2 91 02
85 80 75 08 95 0A 09 80 B1 02
85 F1 75 08 95 30 09 F1 B1 02
85 F5 75 08 95 40 09 F5 B1 02
85 F6 75 08 95 40 09 F6 B1 02
85 F7 75 08 95 40 09 F7 B1 02 C0 C0
```

## Input report (dados dos pedais) — Report ID `0x01`, 17 bytes

| offset | campo | tipo | range | pedal |
|-------:|-------|------|-------|-------|
| 0 | Report ID | `0x01` | — | — |
| 1–2 | **Rx** | uint16 LE | 0–4095 | **Embreagem** *(porta da embreagem — a unidade testada tinha só 2 pedais; Rx não capturado, mapeamento inferido pela ordem do descriptor)* |
| 3–4 | **Ry** | uint16 LE | 0–4095 | **Freio** *(confirmado; repouso ≈ 24)* |
| 5–6 | **Rz** | uint16 LE | 0–4095 | **Acelerador** *(confirmado; repouso ≈ 0)* |
| 7–16 | vendor `0xFF00` | 10 bytes | — | reservado/padding |

- **Resolução efetiva: 12-bit** (`LogicalMaximum` = `0x0FFF` = 4095), empacotada em 16 bits.
- Little-endian. Curso completo ≈ 0 → 4095.
- Leitura ao vivo confirmou: acelerador→Rz (0→4038), freio→Ry (24→4095).

## Canal de configuração (vendor `0xFF00`) — NÃO decodificado
Reports de output/feature usados pelo software Simagic (calibração, curvas):
- `0xF2` — Output, 64 bytes
- `0x80` — Feature, 10 bytes
- `0xF1` — Feature, 48 bytes
- `0xF5` / `0xF6` / `0xF7` — Feature, 64 bytes cada

Protocolo opaco; exigiria mais RE se quisermos **escrever** config no P2000. Para só **ler/monitorar**, o input report `0x01` basta.

## Implicações para o DriveLab
- **Firmware (RP2040):** apresentar como **Joystick com 3 eixos 12-bit** a taxa alta. O ADC do RP2040 é 12-bit (0–4095) → **casa nativamente** com o formato Simagic. Nosso canal vendor **P0** substitui os reports `0xFx` da Simagic para config.
- **App — perfil "P2000/P1000":** ler o input report `0x01`, parsear 3× `uint16 LE` (Rx/Ry/Rz) e normalizar por 4095. É um **transporte diferente** do nosso P0 (que é vendor HID próprio): o app leria os input reports de um joystick Simagic direto e aplicaria nossas curvas/telemetria por cima. No macOS a leitura funciona via `IOHIDManager` sem permissão especial; no Windows seria via HidSharp/raw HID.

## Método
- Descriptor: `ioreg -c IOHIDDevice -l -r` → chave `ReportDescriptor` do nó Product="P2000".
- Ao vivo: `tools/hidread.swift` (compila com `swiftc hidread.swift -o hidread`; uso `./hidread 1155 1316 <segundos>`), registra `IOHIDManagerRegisterInputValueCallback` e imprime eixos + min/max.
- HidSharp (`tools/HidDump`) **não enumerou** neste macOS 26 (a via IOKit/Swift funcionou).
