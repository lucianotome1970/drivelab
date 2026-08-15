# DriveLab Studio — App (.NET 8 / Avalonia)

Cross-platform configurator for the DriveLab ecosystem: wheelbase, pedals, handbrake and wheel rim.

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

---

## 🇬🇧 English

**DriveLab Studio** is the desktop app that connects to the DriveLab devices over USB HID, shows live telemetry, and reads/writes each device's settings (FFB tuning, pedal curves, handbrake, wheel LEDs & paddles). It runs on Windows, macOS and Linux, and has a **simulator mode** so you can explore the whole UI without any hardware.

## Tech stack

- **.NET 8** · **Avalonia 11.2.1** (Fluent theme) — cross-platform UI
- **MVVM** with **CommunityToolkit.Mvvm 8.3.2**
- **HidSharp 2.1.0** — real USB HID I/O
- **LiveChartsCore 2.0.5** — telemetry charts
- **xUnit** — tests

## Project layout

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

## How it talks to the devices

Each device enumerates as its own USB HID under vendor id **`0x1209`**; the app **auto-detects** it by VID/PID (plug the USB cable → the matching dashboard card lights up, no Connect button):

| Device | Product | PID |
|---|---|---|
| Wheelbase | DriveLab Base | `0x0001` |
| Pedals | DriveLab Pedal | `0x0002` |
| Handbrake | DriveLab Handbrake | `0x0003` |
| Wheel rim | DriveLab Wheel | `0x0004` |

Settings and telemetry travel over the **vendor P0/A0 channel** (report ids `0x14` write, `0x15` read-request, `0x16` value, `0x20`/`0x21` telemetry, `0x02` command, `0x18`/`0x19` rim LEDs). The **full wire contract** is documented in **[../docs/PROTOCOL.md](../docs/PROTOCOL.md)** — implement it on any board and the app drives it. See also `DriveLab.Core/Protocol`.

**The device is the source of truth.** Settings live in the controller's own flash, not in an app-side file. The app reads them on connect and writes them back on save.

## Screens

- **Home** — dashboard cards (wheel, base, pedals, handbrake) with live values; clicking a **detected** device's card opens its module page.
- **Wheel Base** — FFB tuning (total force, soft-stop, spring/damper…) + a read-only telemetry monitor + hardware setup.
- **Pedals** — per-pedal output curves, invert, smoothing, sensor type, load-cell target.
- **Handbrake** — single-axis curve + digital button (threshold/hysteresis).
- **Wheel** — rim button LED colors + global brightness + paddle configuration; the rim **stores its colors** and the app reads them back on connect.
- **Named profiles** — every module has a profile selector (save / apply / rename / delete); selecting one writes it to the controller.

## For DD makers — ship a pre-configured installer

Building and **selling** DDs on DriveLab? The **[Maker's guide](../docs/guia-criador.md)** covers it end to end: the difference between using the app as a **maker** (advanced mode → the Hardware tab is visible, you configure the hardware profile) vs an **end user** (normal → tab hidden, feel only), plus how to generate a **Windows installer** that ships your hardware config bundled.

Want to know **how many Nm** your motor delivers without a lever and a scale? See the **[torque calculation guide](../docs/calculo-torque.md)** — measure the torque constant **Kt** and read the estimated torque live in the app.

## Running the app

**You do not need the .NET SDK to run DriveLab Studio.** Download `DriveLab.Studio.exe` from the [releases page](https://github.com/lucianotome1970/drivelab/releases/latest) and run it — it is self-contained.

Plug your device in and the app finds it: there is no Connect button on real hardware.

### Normal mode (default) — for whoever is driving

Just run the executable:

```
DriveLab.Studio.exe
```

The **Hardware tab stays hidden**. That is deliberate, and it is a safety feature, not a limitation: the Hardware tab holds the parameters that can destroy a motor or a board — current limits, encoder counts, current-loop gains, calibration current. Someone who only wants to adjust how the wheel feels should never be one misclick away from them.

### Advanced mode — for makers and expert users

You want the Hardware tab if you are **building** the base, bringing up new hardware, or **selling** DDs and configuring them before shipping. Two ways to enable it:

| How | What it's for |
|---|---|
| `DriveLab.Studio.exe --advanced` | One-off. You decide each time you launch. |
| Drop an empty file named **`advanced.flag`** next to the `.exe` | Permanent on this machine. Your bench always opens in advanced mode; the copy you ship to a customer does not carry the file, so it opens normal. |

The flag also accepts `-advanced` and `/advanced`.

Building DDs to sell? Read the **[maker's guide](../docs/guia-criador.md)** — it covers configuring the hardware profile in advanced mode and generating a Windows installer with that config already bundled, so your customer opens the app and everything is set.

### Simulator mode — no hardware at all

```
DriveLab.Studio.exe --simulator
```

A virtual wheel with real physics. The whole UI works, so you can explore the app, learn the settings, or develop against it before any board exists. In this mode the devices *do* show a **Connect** button. Accepts `--simulator`, `-simulator` or `/simulator`, and combines with `--advanced`.

## Build from source

Needs the **.NET 8 SDK**.

```bash
# build everything
dotnet build app/DriveLab.sln            # or: scripts/build.sh

# run from source
dotnet run --project app/DriveLab.Studio -- --simulator
dotnet run --project app/DriveLab.Studio -- --advanced

# tests
dotnet test app/DriveLab.sln             # or: scripts/test.sh
```

> **Running the whole solution at once can report a false failure** in `DriveLab.Studio.Tests` — the localization manager is static and leaks state between test projects. Re-run that project on its own to confirm.

## Publish a Windows .exe

```bash
scripts/publish-win.sh    # self-contained, single-file → dist/win-x64/DriveLab.Studio.exe
```

Releases live on the repo's [releases page](https://github.com/lucianotome1970/drivelab/releases).

## License

MIT. New source files start with the standard DriveLab header.

> Firmware for each device lives in the sibling `firmware-*/` folders — see the [main README](../README.md).

---

## 🇧🇷 Português

O **DriveLab Studio** é o app desktop que conecta aos dispositivos DriveLab por USB HID, mostra telemetria ao vivo e lê e grava os ajustes de cada dispositivo (afinação do FFB, curvas dos pedais, freio de mão, LEDs e pás do aro). Roda em Windows, macOS e Linux, e tem um **modo simulador** para explorar toda a interface sem hardware nenhum.

## Stack

- **.NET 8** · **Avalonia 11.2.1** (tema Fluent) — interface multiplataforma
- **MVVM** com **CommunityToolkit.Mvvm 8.3.2**
- **HidSharp 2.1.0** — I/O USB HID real
- **LiveChartsCore 2.0.5** — gráficos de telemetria
- **xUnit** — testes

## Estrutura dos projetos

```
app/
  DriveLab.Core/        Protocolo (reports P0/A0, ReportConstants), schema de ajustes e SettingValue,
                        interfaces de transporte, identidades dos dispositivos (VID/PID). .NET puro, sem UI.
  DriveLab.Hid/         I/O USB HID real via HidSharp: HidSharpChannel + transportes por dispositivo
                        (HidBaseTransport, HidPedalTransport, HidHandbrakeTransport) + autodetecção.
  DriveLab.Simulator/   Transportes em memória para o modo --simulator (sem hardware).
  DriveLab.Studio/      App Avalonia: Views/ViewModels, Themes, wiring do CompositionRoot, auto-connect.
  DriveLab.Tests/       Testes de Core + protocolo + ajustes (xUnit).
  DriveLab.Hid.Tests/   Testes de transporte e framing HID.
  DriveLab.Studio.Tests/ Testes de ViewModel.
  DriveLab.sln          Solution.
```

## Como conversa com os dispositivos

Cada dispositivo enumera como o seu próprio USB HID sob o vendor id **`0x1209`**; o app **detecta sozinho** pelo VID/PID (plugou o cabo USB → o card correspondente no painel acende, sem botão Conectar):

| Dispositivo | Produto | PID |
|---|---|---|
| Base do volante | DriveLab Base | `0x0001` |
| Pedais | DriveLab Pedal | `0x0002` |
| Freio de mão | DriveLab Handbrake | `0x0003` |
| Aro | DriveLab Wheel | `0x0004` |

Ajustes e telemetria trafegam pelo **canal vendor P0/A0** (report ids `0x14` write, `0x15` read-request, `0x16` value, `0x20`/`0x21` telemetria, `0x02` command, `0x18`/`0x19` LEDs do aro). O **contrato de fio completo** está documentado em **[../docs/PROTOCOL.md](../docs/PROTOCOL.md)** — implemente ele em qualquer placa e o app controla. Veja também `DriveLab.Core/Protocol`.

**O dispositivo é a fonte da verdade.** Os ajustes moram na flash do próprio controlador, não num arquivo do app. O app lê ao conectar e grava de volta ao salvar.

## Telas

- **Início** — cards do painel (volante, base, pedais, freio de mão) com valores ao vivo; clicar no card de um dispositivo **detectado** abre a página do módulo.
- **Base do Volante** — afinação do FFB (força total, batente, mola/damper…) + monitor de telemetria (só leitura) + configuração de hardware.
- **Pedais** — curvas de saída por pedal, inverter, suavização, tipo de sensor, alvo da célula de carga.
- **Freio de mão** — curva de eixo único + botão digital (limiar e histerese).
- **Volante** — cores dos LEDs dos botões do aro + brilho global + configuração das pás; o aro **guarda as cores** e o app lê elas de volta ao conectar.
- **Perfis nomeados** — todo módulo tem um seletor de perfis (salvar / aplicar / renomear / excluir); selecionar um grava no controlador.

## Para quem monta e vende DD — instalador já configurado

Vai **montar e vender** DDs com o DriveLab? O **[guia do criador](../docs/guia-criador.md)** explica de ponta a ponta: a diferença entre usar o app como **criador** (modo avançado → a aba Hardware aparece e você configura o perfil de hardware) e como **usuário final** (normal → aba escondida, só o feel), além de como gerar um **instalador Windows** que já entrega a sua configuração embutida.

Quer saber **quantos Nm** o seu motor entrega sem haste e balança? Veja o **[guia de cálculo de torque](../docs/calculo-torque.md)** — meça a constante de torque **Kt** e leia o torque estimado ao vivo no app.

## Como executar o app

**Você não precisa do SDK do .NET para rodar o DriveLab Studio.** Baixe o `DriveLab.Studio.exe` na [página de releases](https://github.com/lucianotome1970/drivelab/releases/latest) e execute — ele é self-contained.

Pluga o dispositivo e o app acha sozinho: no hardware real não existe botão Conectar.

### Modo normal (padrão) — para quem vai dirigir

É só executar:

```
DriveLab.Studio.exe
```

A **aba Hardware fica escondida**. Isso é proposital, e é proteção, não limitação: a aba Hardware guarda os parâmetros capazes de destruir um motor ou uma placa — limites de corrente, contagem do encoder, ganhos da malha de corrente, corrente de calibração. Quem só quer acertar como o volante se comporta não pode estar a um clique errado de distância deles.

### Modo avançado — para criadores e usuários experientes

Você quer a aba Hardware se está **montando** a base, subindo hardware novo, ou **vendendo** DDs e configurando antes de entregar. Duas formas de ligar:

| Como | Para que serve |
|---|---|
| `DriveLab.Studio.exe --advanced` | Pontual. Você decide a cada vez que abre. |
| Deixar um arquivo vazio chamado **`advanced.flag`** na mesma pasta do `.exe` | Permanente naquela máquina. A sua bancada sempre abre em modo avançado; a cópia que você entrega ao cliente não leva o arquivo, então abre no modo normal. |

A flag também aceita `-advanced` e `/advanced`.

Vai montar DDs para vender? Leia o **[guia do criador](../docs/guia-criador.md)** — ele cobre configurar o perfil de hardware em modo avançado e gerar um instalador Windows com essa configuração já embutida, para o seu cliente abrir o app com tudo pronto.

### Modo simulador — sem hardware nenhum

```
DriveLab.Studio.exe --simulator
```

Um volante virtual com física real. Toda a interface funciona, então dá para explorar o app, aprender os ajustes ou desenvolver antes de existir qualquer placa. Nesse modo os dispositivos *mostram* um botão **Conectar**. Aceita `--simulator`, `-simulator` ou `/simulator`, e combina com `--advanced`.

## Compilar do código-fonte

Precisa do **SDK do .NET 8**.

```bash
# compilar tudo
dotnet build app/DriveLab.sln            # ou: scripts/build.sh

# rodar a partir do código
dotnet run --project app/DriveLab.Studio -- --simulator
dotnet run --project app/DriveLab.Studio -- --advanced

# testes
dotnet test app/DriveLab.sln             # ou: scripts/test.sh
```

> **Rodar a solução inteira de uma vez pode acusar uma falha falsa** em `DriveLab.Studio.Tests` — o gerenciador de idiomas é estático e vaza estado entre os projetos de teste. Rode esse projeto isolado para confirmar.

## Gerar o .exe do Windows

```bash
scripts/publish-win.sh    # self-contained, arquivo único → dist/win-x64/DriveLab.Studio.exe
```

As versões ficam na [página de releases](https://github.com/lucianotome1970/drivelab/releases) do repositório.

## Licença

MIT. Arquivos novos começam com o cabeçalho padrão do DriveLab.

> O firmware de cada dispositivo fica nas pastas irmãs `firmware-*/` — veja o [README principal](../README.md#-português).
