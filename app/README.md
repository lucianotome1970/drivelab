# DriveLab Studio — App (.NET 8 / Avalonia)

Cross-platform configurator for the DriveLab ecosystem (wheelbase, pedals, handbrake, wheel rim). MOZA Pit-House-style UI.

<p align="center"><a href="#-english">🇬🇧 English</a> &nbsp;·&nbsp; <a href="#-português">🇧🇷 Português</a></p>

---

## 🇬🇧 English

**DriveLab Studio** is the desktop app that connects to the DriveLab devices over USB HID, shows live telemetry, and reads/writes each device's settings (FFB tuning, pedal curves, handbrake, wheel LEDs & paddles). It runs on Windows, macOS and Linux, and has a **simulator mode** so you can explore the whole UI without any hardware.

### Tech stack
- **.NET 8** · **Avalonia 11.2.1** (Fluent theme) — cross-platform UI
- **MVVM** with **CommunityToolkit.Mvvm 8.3.2**
- **HidSharp 2.1.0** — real USB HID I/O
- **LiveChartsCore 2.0.5** — telemetry charts
- **xUnit** — tests

### Project layout
```
app/
  DriveLab.Core/        Protocol (P0/A0 reports, ReportConstants), settings schema & SettingValue,
                        transport interfaces, device identities (VID/PID). Pure .NET, no UI.
  DriveLab.Hid/         Real USB HID I/O via HidSharp: HidSharpChannel + per-device transports
                        (HidBaseTransport, HidPedalTransport, HidHandbrakeTransport) + autodetect.
  DriveLab.Simulator/   In-memory transports for --simulator mode (no hardware needed).
  DriveLab.Studio/      Avalonia app: Views/ViewModels, Themes, CompositionRoot wiring, auto-connect.
  DriveLab.Tests/       Core + protocol + settings tests (xUnit).
  DriveLab.Hid.Tests/   HID transport/framing tests.
  DriveLab.Studio.Tests/ ViewModel tests.
  DriveLab.sln          Solution.
```

### How it talks to the devices
Each device enumerates as its own USB HID under vendor id **`0x1209`**; the app **auto-detects** it by VID/PID (plug the USB cable → the matching dashboard card lights up, no Connect button):

| Device | Product | PID |
|---|---|---|
| Wheelbase / Base | DriveLab Base | `0x0001` |
| Pedals | DriveLab Pedal | `0x0002` |
| Handbrake | DriveLab Handbrake | `0x0003` |
| Wheel rim | DriveLab Wheel | `0x0004` |

Settings and telemetry travel over the **vendor P0/A0 channel** (report ids `0x14` write, `0x15` read-request, `0x16` value, `0x20`/`0x21` telemetry, `0x02` command, `0x18`/`0x19` rim LEDs). The **full wire contract** is documented in **[../docs/PROTOCOL.md](../docs/PROTOCOL.md)** — implement it on any board and the app drives it. See also `DriveLab.Core/Protocol`.

### Screens
- **Home** — dashboard cards (wheel, base, pedals, handbrake) with live values; clicking a **detected** device's card opens its module page.
- **Wheel Base** — FFB tuning (total force, soft-stop, spring/damper…) + a read-only telemetry monitor + hardware setup.
- **Pedals** — per-pedal output curves, invert, smoothing, sensor type, load-cell target.
- **Handbrake** — single-axis curve + digital button (threshold/hysteresis).
- **Wheel** — rim button LED colors + global brightness + paddle configuration; the rim **stores its colors** and the app reads them back on connect.
- **Named profiles** — every module has a profile selector (save / apply / rename / delete); selecting one writes it to the controller.

### Build & run
Needs the **.NET 8 SDK**.
```bash
# build everything
dotnet build app/DriveLab.sln            # or: scripts/build.sh

# run the app (with the simulator — no hardware needed)
dotnet run --project app/DriveLab.Studio -- --simulator

# run against real hardware (just plug the USB cable, it auto-detects)
dotnet run --project app/DriveLab.Studio

# tests
dotnet test app/DriveLab.sln             # or: scripts/test.sh
```
The simulator flag accepts `--simulator`, `-simulator` or `/simulator`. In simulator mode the devices expose a **Connect** button; on real hardware the connection is automatic.

### Publish a Windows .exe
```bash
scripts/publish-win.sh                   # self-contained, single-file → dist/win-x64/DriveLab.Studio.exe
```
Releases live on the repo's [releases page](https://github.com/lucianotome1970/drivelab/releases).

### License
MIT. New source files start with the standard DriveLab header.

> Firmware for each device lives in the sibling `firmware-*/` folders — see the [main README](../README.md).

---

## 🇧🇷 Português

O **DriveLab Studio** é o app desktop que conecta aos dispositivos DriveLab via USB HID, mostra telemetria ao vivo e lê/grava os settings de cada dispositivo (ajuste de FFB, curvas dos pedais, freio de mão, LEDs e pás do volante). Roda em Windows, macOS e Linux, e tem um **modo simulador** para explorar toda a UI sem nenhum hardware.

### Stack
- **.NET 8** · **Avalonia 11.2.1** (tema Fluent) — UI multiplataforma
- **MVVM** com **CommunityToolkit.Mvvm 8.3.2**
- **HidSharp 2.1.0** — I/O USB HID real
- **LiveChartsCore 2.0.5** — gráficos de telemetria
- **xUnit** — testes

### Estrutura dos projetos
```
app/
  DriveLab.Core/        Protocolo (reports P0/A0, ReportConstants), schema de settings & SettingValue,
                        interfaces de transporte, identidades dos dispositivos (VID/PID). .NET puro, sem UI.
  DriveLab.Hid/         I/O USB HID real via HidSharp: HidSharpChannel + transports por dispositivo
                        (HidBaseTransport, HidPedalTransport, HidHandbrakeTransport) + autodetecção.
  DriveLab.Simulator/   Transports em memória para o modo --simulator (sem hardware).
  DriveLab.Studio/      App Avalonia: Views/ViewModels, Themes, wiring do CompositionRoot, auto-connect.
  DriveLab.Tests/       Testes de Core + protocolo + settings (xUnit).
  DriveLab.Hid.Tests/   Testes de transporte/framing HID.
  DriveLab.Studio.Tests/ Testes de ViewModel.
  DriveLab.sln          Solution.
```

### Como conversa com os dispositivos
Cada dispositivo enumera como seu próprio USB HID sob o vendor id **`0x1209`**; o app o **autodetecta** por VID/PID (plugou o cabo USB → o card correspondente no dashboard acende, sem botão Conectar):

| Dispositivo | Produto | PID |
|---|---|---|
| Base / Wheelbase | DriveLab Base | `0x0001` |
| Pedaleira | DriveLab Pedal | `0x0002` |
| Freio de mão | DriveLab Handbrake | `0x0003` |
| Aro / Volante | DriveLab Wheel | `0x0004` |

Settings e telemetria trafegam pelo **canal vendor P0/A0** (report ids `0x14` write, `0x15` read-request, `0x16` value, `0x20`/`0x21` telemetria, `0x02` command, `0x18`/`0x19` LEDs do aro). O **contrato de fio completo** está documentado em **[../docs/PROTOCOL.md](../docs/PROTOCOL.md)** — implemente-o em qualquer placa e o app a controla. Ver também `DriveLab.Core/Protocol`.

### Telas
- **Home** — cards do dashboard (volante, base, pedais, freio de mão) com valores ao vivo; clicar no card de um dispositivo **detectado** abre a página do módulo.
- **Base do Volante** — ajuste de FFB (força total, batente, mola/damper…) + monitor de telemetria (só leitura) + configuração de hardware.
- **Pedais** — curvas de saída por pedal, inverter, suavização, tipo de sensor, alvo de load cell.
- **Freio de mão** — curva de eixo único + botão digital (limiar/histerese).
- **Volante** — cores dos LEDs dos botões do aro + brilho global + configuração das pás; o aro **guarda as cores** e o app as lê de volta ao conectar.
- **Perfis nomeados** — todo módulo tem um seletor de perfis (salvar / aplicar / renomear / excluir); selecionar um grava no controlador.

### Build & execução
Precisa do **SDK do .NET 8**.
```bash
# compilar tudo
dotnet build app/DriveLab.sln            # ou: scripts/build.sh

# rodar o app (com o simulador — sem hardware)
dotnet run --project app/DriveLab.Studio -- --simulator

# rodar com hardware real (é só plugar o cabo USB, ele autodetecta)
dotnet run --project app/DriveLab.Studio

# testes
dotnet test app/DriveLab.sln             # ou: scripts/test.sh
```
A flag do simulador aceita `--simulator`, `-simulator` ou `/simulator`. No modo simulador os dispositivos exibem um botão **Conectar**; no hardware real a conexão é automática.

### Gerar o .exe do Windows
```bash
scripts/publish-win.sh                   # self-contained, single-file → dist/win-x64/DriveLab.Studio.exe
```
As versões ficam na [página de releases](https://github.com/lucianotome1970/drivelab/releases) do repo.

### Licença
MIT. Arquivos novos começam com o cabeçalho padrão do DriveLab.

> O firmware de cada dispositivo fica nas pastas irmãs `firmware-*/` — ver o [README principal](../README.md).
